using System.Text.Json;
using Dapper;
using Northwoods.Tenancy;
using Npgsql;

namespace Extraction.Worker;

public sealed partial class Worker(ILogger<Worker> logger, IConfiguration config) : BackgroundService
{
    internal const decimal HighConfidenceThreshold = 0.90m;
    internal const decimal ReviewRequiredThreshold = 0.75m;
    internal const decimal EscalateThreshold = 0.82m;
    private const int DefaultMaxRetryAttempts = 3;
    private const int DefaultRetryDelayMilliseconds = 1_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        var objectStore = BuildObjectStore(config);
        var providers = BuildProviders(config);
        logger.LogInformation("Registered {Count} extraction providers: {Providers}",
            providers.Count, string.Join(", ", providers.Select(p => $"{p.Name}(order={p.Order})")));
        var maxRetryAttempts = Math.Max(1, config.GetValue("Extraction:MaxRetryAttempts", DefaultMaxRetryAttempts));
        var retryDelayMs = Math.Max(0, config.GetValue("Extraction:RetryDelayMs", DefaultRetryDelayMilliseconds));
        var metrics = new WorkerMetrics();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingDocuments(connectionString, objectStore, providers, maxRetryAttempts, retryDelayMs, metrics, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Extraction cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private static ObjectStore BuildObjectStore(IConfiguration configuration)
    {
        var endpoint = configuration["Minio:Endpoint"] ?? "localhost:9000";
        var accessKey = configuration["Minio:AccessKey"] ?? "northwoods";
        var secretKey = configuration["Minio:SecretKey"] ?? "northwoods";
        var bucketName = configuration["Minio:BucketName"] ?? "intakes";
        var publicEndpoint = configuration["Minio:PublicEndpoint"];

        return new ObjectStore(endpoint, accessKey, secretKey, bucketName, publicEndpoint);
    }

    private static IReadOnlyList<IExtractionProvider> BuildProviders(IConfiguration configuration)
    {
        var providers = new List<IExtractionProvider>();

        var usePaddle = configuration.GetValue("Extraction:UsePaddleOcr", false);
        if (usePaddle)
        {
            var pythonPath = configuration["Extraction:PaddleOcr:PythonPath"] ?? "python3";
            var scriptPath = configuration["Extraction:PaddleOcr:ScriptPath"] ?? "scripts/paddle_extract.py";
            providers.Add(new PaddleOcrProvider(pythonPath, scriptPath));
        }

        var useOpenAiNormalizer = configuration.GetValue("Extraction:UseOpenAiNormalizer", false);
        if (useOpenAiNormalizer)
        {
            var apiKey = configuration["OPENAI_API_KEY"] ?? configuration["Extraction:OpenAi:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Extraction:UseOpenAiNormalizer=true requires OPENAI_API_KEY or Extraction:OpenAi:ApiKey.");

            var modelMini = configuration["Extraction:OpenAi:ModelMini"] ?? "gpt-5.4-mini";
            providers.Add(new OpenAiNormalizerProvider(apiKey, modelMini));
        }

        var useOpenAiVision = configuration.GetValue("Extraction:UseOpenAiVision", true);
        if (useOpenAiVision)
        {
            var apiKey = configuration["OPENAI_API_KEY"] ?? configuration["Extraction:OpenAi:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Extraction:UseOpenAiVision=true requires OPENAI_API_KEY or Extraction:OpenAi:ApiKey.");

            var modelNano = configuration["Extraction:OpenAi:VisionModel"] ?? "gpt-5.4-nano";
            var modelMini = configuration["Extraction:OpenAi:ModelMini"] ?? "gpt-5.4-mini";
            providers.Add(new OpenAiVisionProvider(apiKey, modelNano, modelMini));
        }

        if (providers.Count == 0)
        {
            throw new InvalidOperationException("No extraction providers are enabled. Set at least one of Extraction:UseOpenAiVision, Extraction:UseOpenAiNormalizer, or Extraction:UsePaddleOcr to true.");
        }

        return [.. providers.OrderBy(p => p.Order)];
    }

    private async Task ProcessPendingDocuments(
        string connectionString,
        ObjectStore objectStore,
        IReadOnlyList<IExtractionProvider> providers,
        int maxRetryAttempts,
        int retryDelayMs,
        WorkerMetrics metrics,
        CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var docs = (await conn.QueryAsync<(Guid id, string tenant_id, string template_id, string original_file_key)>(
            """
            SELECT id, tenant_id, template_id, original_file_key
            FROM documents
            WHERE status = 'uploaded'
            ORDER BY created_at
            LIMIT 10
            """ )).ToList();

        if (docs.Count == 0)
            return;

        logger.LogInformation("Found {Count} documents to extract", docs.Count);

        foreach (var doc in docs)
        {
            await ExtractDocumentWithRetry(
                conn,
                objectStore,
                doc.id,
                doc.tenant_id,
                doc.template_id,
                doc.original_file_key,
                providers,
                maxRetryAttempts,
                retryDelayMs,
                metrics,
                ct);
        }

        logger.LogInformation(
            "Extraction metrics snapshot Success={ExtractionSuccessCount} Failure={ExtractionFailureCount}",
            metrics.ExtractionSuccessCount,
            metrics.ExtractionFailureCount);
    }

    private async Task ExtractDocument(
        NpgsqlConnection conn,
        ObjectStore objectStore,
        Guid docId,
        string tenantId,
        string templateId,
        string originalFileKey,
        IReadOnlyList<IExtractionProvider> providers,
        Guid extractionRunId,
        string correlationId,
        CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                "UPDATE documents SET status = 'extracting', updated_at = now() WHERE id = @Id AND tenant_id = @TenantId",
                new { Id = docId, TenantId = tenantId },
                tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO audit_events (document_id, tenant_id, event_type, details)
                VALUES (@DocId, @TenantId, 'extraction_started', @Details::jsonb)
                """,
                new
                {
                    DocId = docId,
                    TenantId = tenantId,
                    Details = JsonSerializer.Serialize(new
                    {
                        correlation_id = correlationId,
                        extraction_run_id = extractionRunId
                    })
                },
                tx);

            var fieldSchemaJson = await conn.QueryFirstOrDefaultAsync<string>(
                "SELECT field_schema FROM templates WHERE id = @Id AND tenant_id = @TenantId",
                new { Id = templateId, TenantId = tenantId },
                tx);

            var fieldKeys = ExtractTemplateKeys(fieldSchemaJson).ToList();
            if (fieldKeys.Count == 0)
            {
                fieldKeys = [
                    "applicantName",
                    "dateOfBirth",
                    "address",
                    "householdSize",
                    "monthlyIncome",
                    "requestedServices",
                    "notes"
                ];
            }

            var bytes = await objectStore.DownloadAsync(originalFileKey);
            var tempFile = WriteTempFile(originalFileKey, bytes);

            try
            {
                var extractionContext = new ExtractionContext(docId, tenantId, templateId, originalFileKey, tempFile, bytes.Length, bytes);
                var (results, discoveredResults) = await ExtractionPipelineService.RunExtractionPipeline(extractionContext, fieldKeys, providers, ct);

                // Provider comparison summary
                var providerNames = results.SelectMany(r => r.AllAttempts.Select(a => a.Provider)).Distinct().ToArray();
                logger.LogInformation(
                    "Provider comparison for {DocId}: providers={Providers} fields={FieldCount} attempts={AttemptCount} discovered={DiscoveredCount}",
                    docId, string.Join(",", providerNames), results.Count,
                    results.Sum(r => r.AllAttempts.Count), discoveredResults.Count);
                var canPersistAttempts = await ExtractionPipelineService.SupportsExtractionAttempts(conn, tx);
                var canPersistCaseProfiles = await CaseProfileService.SupportsCaseProfiles(conn, tx);

                // Persist schema fields with normal confidence/review logic
                foreach (var result in results)
                {
                    await conn.ExecuteAsync(
                        """
                        INSERT INTO extracted_fields
                            (document_id, tenant_id, field_key, extracted_value, confidence, requires_review, is_discovered)
                        VALUES
                            (@DocId, @TenantId, @FieldKey, @Value, @Confidence, @RequiresReview, false)
                        ON CONFLICT (document_id, field_key) DO UPDATE
                            SET extracted_value = EXCLUDED.extracted_value,
                                confidence = EXCLUDED.confidence,
                                requires_review = EXCLUDED.requires_review,
                                is_discovered = false,
                                updated_at = now()
                        """,
                        new
                        {
                            DocId = docId,
                            TenantId = tenantId,
                            FieldKey = result.FieldKey,
                            Value = result.FinalValue,
                            Confidence = result.SystemConfidence,
                            RequiresReview = result.SystemConfidence < ReviewRequiredThreshold
                        },
                        tx);

                    if (canPersistAttempts)
                    {
                        await ExtractionPipelineService.PersistExtractionAttempts(conn, tx, docId, tenantId, extractionRunId, result, ct);
                    }
                }

                // Persist discovered fields — don't affect document status or require review
                foreach (var result in discoveredResults)
                {
                    await conn.ExecuteAsync(
                        """
                        INSERT INTO extracted_fields
                            (document_id, tenant_id, field_key, extracted_value, confidence, requires_review, is_discovered)
                        VALUES
                            (@DocId, @TenantId, @FieldKey, @Value, @Confidence, false, true)
                        ON CONFLICT (document_id, field_key) DO UPDATE
                            SET extracted_value = EXCLUDED.extracted_value,
                                confidence = EXCLUDED.confidence,
                                requires_review = false,
                                is_discovered = true,
                                updated_at = now()
                        """,
                        new
                        {
                            DocId = docId,
                            TenantId = tenantId,
                            FieldKey = result.FieldKey,
                            Value = result.FinalValue,
                            Confidence = result.SystemConfidence
                        },
                        tx);

                    if (canPersistAttempts)
                    {
                        await ExtractionPipelineService.PersistExtractionAttempts(conn, tx, docId, tenantId, extractionRunId, result, ct);
                    }
                }

                if (canPersistCaseProfiles)
                {
                    var profileText = CaseProfileService.BuildCaseProfileText(templateId, results, discoveredResults);
                    var apiKey = config["OPENAI_API_KEY"] ?? config["Extraction:OpenAi:ApiKey"];
                    await CaseProfileService.PersistCaseProfile(
                        conn,
                        tx,
                        docId,
                        tenantId,
                        templateId,
                        profileText,
                        results,
                        apiKey,
                        logger,
                        ct);
                }

                // ADR 005 confidence tiers: compute document-level confidence (min of field confidences)
                // Only schema fields contribute to document status
                var minFieldConfidence = results.Count > 0
                    ? results.Min(r => r.SystemConfidence)
                    : 0m;
                var (documentStatus, autoAccepted, requiresAttention) = DetermineDocumentStatus(results, fieldKeys.Count);

                await conn.ExecuteAsync(
                    "UPDATE documents SET status = @Status, updated_at = now() WHERE id = @Id AND tenant_id = @TenantId",
                    new { Status = documentStatus, Id = docId, TenantId = tenantId },
                    tx);
                await conn.ExecuteAsync(
                    """
                    INSERT INTO audit_events (document_id, tenant_id, event_type, details)
                    VALUES (@DocId, @TenantId, 'extraction_completed', @Details::jsonb)
                    """,
                    new
                    {
                        DocId = docId,
                        TenantId = tenantId,
                        Details = JsonSerializer.Serialize(new
                        {
                            correlation_id = correlationId,
                            extraction_run_id = extractionRunId,
                            provider_count = providers.Count,
                            fields_extracted = results.Count,
                            discovered_fields = discoveredResults.Count,
                            high_confidence_fields = results.Count(r => r.SystemConfidence >= HighConfidenceThreshold),
                            warning_fields = results.Count(r => r.SystemConfidence >= ReviewRequiredThreshold && r.SystemConfidence < HighConfidenceThreshold),
                            low_confidence_fields = results.Count(r => r.SystemConfidence < ReviewRequiredThreshold),
                            review_required_threshold = ReviewRequiredThreshold,
                            auto_accept_threshold = HighConfidenceThreshold,
                            min_field_confidence = minFieldConfidence,
                            document_status = documentStatus,
                            auto_accepted = autoAccepted,
                            requires_attention = requiresAttention,
                            escalated_fields = results.Count(r => r.AllAttempts.Count > 1)
                        })
                    },
                    tx);

                await tx.CommitAsync(ct);
            }
            finally
            {
                TryDeleteTempFile(tempFile);
            }

            logger.LogInformation("Extracted document {DocId} for tenant {TenantId}", docId, tenantId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            logger.LogError(ex, "Failed to extract document {DocId}", docId);
            throw;
        }
    }

    private async Task ExtractDocumentWithRetry(
        NpgsqlConnection conn,
        ObjectStore objectStore,
        Guid docId,
        string tenantId,
        string templateId,
        string originalFileKey,
        IReadOnlyList<IExtractionProvider> providers,
        int maxRetryAttempts,
        int retryDelayMs,
        WorkerMetrics metrics,
        CancellationToken ct)
    {
        var extractionRunId = Guid.NewGuid();
        var correlationId = await GetCorrelationIdFromUploadEvent(conn, docId) ?? docId.ToString("N");

        for (var attempt = 1; attempt <= maxRetryAttempts; attempt++)
        {
            try
            {
                using (logger.BeginScope(new Dictionary<string, object?>
                       {
                           ["CorrelationId"] = correlationId,
                           ["DocumentId"] = docId,
                           ["Attempt"] = attempt,
                           ["ExtractionRunId"] = extractionRunId
                       }))
                {
                    await ExtractDocument(
                        conn,
                        objectStore,
                        docId,
                        tenantId,
                        templateId,
                        originalFileKey,
                        providers,
                        extractionRunId,
                        correlationId,
                        ct);
                }

                metrics.IncrementExtractionSuccess();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetryAttempts && IsTransientFailure(ex))
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(30_000, retryDelayMs * (1 << (attempt - 1))));
                logger.LogWarning(
                    ex,
                    "Transient extraction failure for {DocId}; retrying in {Delay} (attempt {Attempt}/{MaxAttempts})",
                    docId,
                    delay,
                    attempt,
                    maxRetryAttempts);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                metrics.IncrementExtractionFailure();
                await RecordExtractionFailure(conn, docId, tenantId, extractionRunId, correlationId, ex, ct);
                return;
            }
        }
    }


}