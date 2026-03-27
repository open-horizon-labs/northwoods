using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.Net;
using System.Net.Sockets;

using System.Net.Http.Headers;
using Dapper;
using Northwoods.Tenancy;
using Npgsql;

namespace Extraction.Worker;

public sealed partial class Worker(ILogger<Worker> logger, IConfiguration config) : BackgroundService
{
    internal const decimal HighConfidenceThreshold = 0.90m;
    internal const decimal ReviewRequiredThreshold = 0.75m;
    internal const decimal EscalateThreshold = 0.82m;
    private const int CaseEmbeddingDimensions = 1536;
    private const int DefaultMaxRetryAttempts = 3;
    private const int DefaultRetryDelayMilliseconds = 1_000;
    private static readonly HttpClient EmbeddingHttp = new();

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

        var useMock = configuration.GetValue("Extraction:UseMockProvider", false);
        if (useMock)
        {
            providers.Add(new MockTesseractProvider());
        }

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
                "SELECT field_schema FROM templates WHERE id = @Id",
                new { Id = templateId },
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
                var results = await RunExtractionPipeline(extractionContext, fieldKeys, providers, ct);

                // Provider comparison summary
                var providerNames = results.SelectMany(r => r.AllAttempts.Select(a => a.Provider)).Distinct().ToArray();
                logger.LogInformation(
                    "Provider comparison for {DocId}: providers={Providers} fields={FieldCount} attempts={AttemptCount}",
                    docId, string.Join(",", providerNames), results.Count,
                    results.Sum(r => r.AllAttempts.Count));
                var canPersistAttempts = await SupportsExtractionAttempts(conn, tx);
                var canPersistCaseProfiles = await SupportsCaseProfiles(conn, tx);

                foreach (var result in results)
                {
                    await conn.ExecuteAsync(
                        """
                        INSERT INTO extracted_fields
                            (document_id, tenant_id, field_key, extracted_value, confidence, requires_review)
                        VALUES
                            (@DocId, @TenantId, @FieldKey, @Value, @Confidence, @RequiresReview)
                        ON CONFLICT (document_id, field_key) DO UPDATE
                            SET extracted_value = EXCLUDED.extracted_value,
                                confidence = EXCLUDED.confidence,
                                requires_review = EXCLUDED.requires_review,
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
                        await PersistExtractionAttempts(conn, tx, docId, tenantId, extractionRunId, result, ct);
                    }
                }

                if (canPersistCaseProfiles)
                {
                    var profileText = BuildCaseProfileText(templateId, results);
                    await PersistCaseProfile(
                        conn,
                        tx,
                        docId,
                        tenantId,
                        templateId,
                        profileText,
                        results,
                        ct);
                }

                // ADR 005 confidence tiers: compute document-level confidence (min of field confidences)
                var minFieldConfidence = results.Count > 0
                    ? results.Min(r => r.SystemConfidence)
                    : 0m;
                var noResolvedFields = results.Count == 0;
                var hasMissingFields = results.Count < fieldKeys.Count;
                var allFieldsHighConfidence = !noResolvedFields && !hasMissingFields && results.All(r => r.SystemConfidence >= HighConfidenceThreshold);
                var anyFieldLowConfidence = noResolvedFields || hasMissingFields || results.Any(r => r.SystemConfidence < ReviewRequiredThreshold);

                string documentStatus;
                bool autoAccepted;
                bool requiresAttention;

                if (allFieldsHighConfidence)
                {
                    // All fields >= 0.90: auto-accept (still auditable, still visible in review queue)
                    documentStatus = "completed";
                    autoAccepted = true;
                    requiresAttention = false;
                }
                else if (anyFieldLowConfidence)
                {
                    // Any field < 0.75: forced review with attention flag
                    documentStatus = "review_ready";
                    autoAccepted = false;
                    requiresAttention = true;
                }
                else
                {
                    // Fields between 0.75-0.90: warning review path
                    documentStatus = "review_ready";
                    autoAccepted = false;
                    requiresAttention = false;
                }

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

    private static async Task<string?> GetCorrelationIdFromUploadEvent(NpgsqlConnection conn, Guid docId)
    {
        var correlationId = await conn.QueryFirstOrDefaultAsync<string?>(
            """
            SELECT details ->> 'correlation_id'
            FROM audit_events
            WHERE document_id = @DocId
              AND event_type = 'intake_uploaded'
            ORDER BY created_at DESC
            LIMIT 1
            """,
            new { DocId = docId });

        return string.IsNullOrWhiteSpace(correlationId) ? null : correlationId;
    }

    private static bool IsTransientFailure(Exception ex)
    {
        if (ex is TaskCanceledException taskCanceled)
        {
            return !taskCanceled.CancellationToken.IsCancellationRequested;
        }

        return ex is TransientExtractionException
               || ex is NpgsqlException
               || ex is HttpRequestException
               || ex is IOException
               || ex is SocketException
               || ex is TimeoutException;
    }

    private static async Task RecordExtractionFailure(
        NpgsqlConnection conn,
        Guid docId,
        string tenantId,
        Guid extractionRunId,
        string correlationId,
        Exception error,
        CancellationToken ct)
    {
        await using var failTx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE documents SET status = 'failed', updated_at = now() WHERE id = @Id AND tenant_id = @TenantId",
            new { Id = docId, TenantId = tenantId },
            failTx);

        await conn.ExecuteAsync(
            """
            INSERT INTO audit_events (document_id, tenant_id, event_type, details)
            VALUES (@DocId, @TenantId, 'extraction_failed', @Details::jsonb)
            """,
            new
            {
                DocId = docId,
                TenantId = tenantId,
                Details = JsonSerializer.Serialize(new
                {
                    correlation_id = correlationId,
                    extraction_run_id = extractionRunId,
                    error = error.Message,
                    error_type = error.GetType().Name
                })
            },
            failTx);

        await failTx.CommitAsync(ct);
    }

    private static string WriteTempFile(string key, byte[] bytes)
    {
        var extension = Path.GetExtension(key);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"northwoods-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(tempPath, bytes);
        return tempPath;
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    private sealed class WorkerMetrics
    {
        private long _extractionSuccessCount;
        private long _extractionFailureCount;

        public void IncrementExtractionSuccess() => Interlocked.Increment(ref _extractionSuccessCount);

        public void IncrementExtractionFailure() => Interlocked.Increment(ref _extractionFailureCount);

        public long ExtractionSuccessCount => Interlocked.Read(ref _extractionSuccessCount);

        public long ExtractionFailureCount => Interlocked.Read(ref _extractionFailureCount);
    }

    private sealed class TransientExtractionException(string message) : Exception(message)
    {
    }

    private static string BuildCaseProfileText(string templateId, IReadOnlyList<FieldExtractionResult> fields)
    {
        var extractedPairs = fields
            .Select(f => $"{f.FieldKey}: {f.FinalValue}")
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        var ocrPairs = fields
            .SelectMany(f => f.AllAttempts
                .Where(a => string.Equals(a.Stage, "ocr", StringComparison.OrdinalIgnoreCase))
                .Select(a => $"{f.FieldKey}: {a.Value}"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        var extractedText = extractedPairs.Length > 0 ? string.Join(" | ", extractedPairs) : "(no fields)";
        var ocrText = ocrPairs.Length > 0 ? string.Join(" | ", ocrPairs) : "(no ocr segments)";

        return $"template={templateId}; fields={extractedText}; ocr={ocrText}";
    }

    private static async Task<bool> SupportsCaseProfiles(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT EXISTS(
                SELECT 1
                FROM pg_class
                WHERE relname = 'case_profiles'
                  AND relkind = 'r'
            )::int
            """,
            transaction: tx);

        return exists == 1;
    }

    private async Task PersistCaseProfile(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid docId,
        string tenantId,
        string templateId,
        string caseProfileText,
        IReadOnlyList<FieldExtractionResult> fields,
        CancellationToken ct)
    {
        static string? GetFieldValue(IReadOnlyList<FieldExtractionResult> results, string key)
            => results.FirstOrDefault(r => string.Equals(r.FieldKey, key, StringComparison.OrdinalIgnoreCase))?.FinalValue;

        var applicantName = GetFieldValue(fields, "applicantName");
        var dateOfBirth = GetFieldValue(fields, "dateOfBirth");
        var address = GetFieldValue(fields, "address");

        string? embeddingLiteral = null;
        var apiKey = config["OPENAI_API_KEY"] ?? config["Extraction:OpenAi:ApiKey"];

        if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(caseProfileText))
        {
            try
            {
                var (embedding, promptTokens, totalTokens) = await GenerateCaseEmbeddingAsync(caseProfileText, apiKey, ct);
                embeddingLiteral = ToPgVectorLiteral(embedding);

                await conn.ExecuteAsync(
                    """
                    INSERT INTO audit_events (document_id, tenant_id, event_type, details)
                    VALUES (@DocId, @TenantId, 'embedding_generated', @Details::jsonb)
                    """,
                    new
                    {
                        DocId = docId,
                        TenantId = tenantId,
                        Details = JsonSerializer.Serialize(new
                        {
                            model = "text-embedding-3-small",
                            dimensions = CaseEmbeddingDimensions,
                            prompt_tokens = promptTokens,
                            total_tokens = totalTokens
                        })
                    },
                    tx);

                logger.LogInformation(
                    "Generated embedding for {DocId}: model=text-embedding-3-small tokens={TotalTokens}",
                    docId, totalTokens);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to generate embedding for {DocId}; storing without embedding", docId);
            }
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO case_profiles
                (document_id, tenant_id, template_id, applicant_name, date_of_birth, address, search_text, embedding)
            VALUES
                (@DocId, @TenantId, @TemplateId, @ApplicantName, @DateOfBirth, @Address, @SearchText,
                 CASE WHEN @Embedding IS NULL THEN NULL ELSE CAST(@Embedding AS vector(1536)) END)
            ON CONFLICT (document_id)
                DO UPDATE SET
                    tenant_id = EXCLUDED.tenant_id,
                    template_id = EXCLUDED.template_id,
                    applicant_name = EXCLUDED.applicant_name,
                    date_of_birth = EXCLUDED.date_of_birth,
                    address = EXCLUDED.address,
                    search_text = EXCLUDED.search_text,
                    embedding = COALESCE(EXCLUDED.embedding, case_profiles.embedding),
                    updated_at = now()
            """,
            new
            {
                DocId = docId,
                TenantId = tenantId,
                TemplateId = templateId,
                ApplicantName = string.IsNullOrWhiteSpace(applicantName) ? null : applicantName,
                DateOfBirth = string.IsNullOrWhiteSpace(dateOfBirth) ? null : dateOfBirth,
                Address = string.IsNullOrWhiteSpace(address) ? null : address,
                SearchText = caseProfileText,
                Embedding = embeddingLiteral
            },
            tx);

        ct.ThrowIfCancellationRequested();
    }

    private static async Task<(double[] Embedding, long PromptTokens, long TotalTokens)> GenerateCaseEmbeddingAsync(
        string text, string apiKey, CancellationToken ct)
    {
        var requestPayload = new
        {
            model = "text-embedding-3-small",
            input = text
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var res = await EmbeddingHttp.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            var isTransient = (int)res.StatusCode >= 500
                              || res.StatusCode == HttpStatusCode.TooManyRequests
                              || res.StatusCode == HttpStatusCode.RequestTimeout;
            if (isTransient)
                throw new TransientExtractionException($"OpenAI embedding failed ({(int)res.StatusCode}).");
            throw new InvalidOperationException($"OpenAI embedding failed ({(int)res.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var dataArray = doc.RootElement.GetProperty("data");
        var embeddingElement = dataArray[0].GetProperty("embedding");
        var values = new double[CaseEmbeddingDimensions];
        var idx = 0;
        foreach (var el in embeddingElement.EnumerateArray())
        {
            if (idx < values.Length)
                values[idx++] = el.GetDouble();
        }

        long promptTokens = 0, totalTokens = 0;
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number)
                promptTokens = pt.GetInt64();
            if (usage.TryGetProperty("total_tokens", out var tt) && tt.ValueKind == JsonValueKind.Number)
                totalTokens = tt.GetInt64();
        }

        return (values, promptTokens, totalTokens);
    }

    private static string ToPgVectorLiteral(double[] values)
    {
        return $"[{string.Join(',', values.Select(v => v.ToString(CultureInfo.InvariantCulture)))}]";
    }

    private static IEnumerable<string> ExtractTemplateKeys(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            return Array.Empty<string>();

        using var doc = JsonDocument.Parse(schemaJson);
        if (!doc.RootElement.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var keys = new List<string>();
        foreach (var field in fields.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object)
                continue;

            if (!field.TryGetProperty("key", out var keyProp) || keyProp.ValueKind != JsonValueKind.String)
                continue;

            var key = keyProp.GetString();
            if (!string.IsNullOrWhiteSpace(key))
                keys.Add(key.Trim());
        }

        return keys;
    }

    private static async Task<IReadOnlyList<FieldExtractionResult>> RunExtractionPipeline(
        ExtractionContext context,
        IReadOnlyList<string> fieldKeys,
        IReadOnlyList<IExtractionProvider> providers,
        CancellationToken ct)
    {
        if (providers.Count == 0)
            throw new InvalidOperationException("At least one extraction provider is required.");

        var attemptsByField = new Dictionary<string, List<ExtractionCandidate>>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            var candidates = await provider.ExtractAsync(
                context,
                fieldKeys,
                attemptsByField.Count > 0 ? attemptsByField : null,
                ct);

            foreach (var attempt in candidates)
            {
                if (!attemptsByField.TryGetValue(attempt.FieldKey, out var list))
                {
                    list = [];
                    attemptsByField[attempt.FieldKey] = list;
                }

                list.Add(attempt);
            }
        }

        var results = new List<FieldExtractionResult>(fieldKeys.Count);
        foreach (var key in fieldKeys)
        {
            if (!attemptsByField.TryGetValue(key, out var attempts) || attempts.Count == 0)
                continue;

            results.Add(FieldConsensus.Resolve(key, attempts));
        }

        return results;
    }

    private static async Task<bool> SupportsExtractionAttempts(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT EXISTS(
                SELECT 1
                FROM pg_class
                WHERE relname = 'extraction_attempts'
                  AND relkind = 'r'
            )::int
            """,
            transaction: tx);

        return exists == 1;
    }

    private static async Task PersistExtractionAttempts(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid docId,
        string tenantId,
        Guid extractionRunId,
        FieldExtractionResult result,
        CancellationToken ct)
    {
        foreach (var attempt in result.AllAttempts)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO extraction_attempts
                    (document_id, tenant_id, extraction_run_id, field_key, provider, stage, technique, raw_value, raw_confidence, normalized_value, normalized_confidence, requires_review, details)
                VALUES
                    (@DocId, @TenantId, @ExtractionRunId, @FieldKey, @Provider, @Stage, @Technique, @RawValue, @RawConfidence, @NormalizedValue, @NormalizedConfidence, @RequiresReview, @Details::jsonb)
                """,
                new
                {
                    DocId = docId,
                    TenantId = tenantId,
                    ExtractionRunId = extractionRunId,
                    FieldKey = result.FieldKey,
                    Provider = attempt.Provider,
                    Stage = attempt.Stage,
                    Technique = attempt.Metadata is not null && attempt.Metadata.TryGetValue("technique", out var technique) && technique is string techStr && !string.IsNullOrWhiteSpace(techStr)
                        ? techStr
                        : $"{attempt.Provider}:{attempt.Stage}",
                    RawValue = attempt.Value,
                    RawConfidence = attempt.Confidence,
                    NormalizedValue = result.NormalizedValue,
                    NormalizedConfidence = attempt.Confidence,
                    RequiresReview = result.SystemConfidence < ReviewRequiredThreshold,
                    Details = JsonSerializer.Serialize(attempt.Metadata)
                },
                tx);

            ct.ThrowIfCancellationRequested();
        }
    }

    internal readonly record struct ExtractionContext(
        Guid DocumentId,
        string TenantId,
        string TemplateId,
        string OriginalFileKey,
        string LocalFilePath,
        int ByteLength,
        byte[] FileBytes);

    internal sealed record ExtractionCandidate(
        string FieldKey,
        string Value,
        decimal Confidence,
        string Stage,
        string Provider,
        Dictionary<string, object>? Metadata = null);

    internal sealed record FieldExtractionResult(
        string FieldKey,
        string FinalValue,
        decimal SystemConfidence,
        string NormalizedValue,
        IReadOnlyList<ExtractionCandidate> AllAttempts,
        IReadOnlyList<string> ProviderSequence);

    internal interface IExtractionProvider
    {
        string Name { get; }
        string Stage { get; }
        int Order { get; }

        Task<IReadOnlyList<ExtractionCandidate>> ExtractAsync(
            ExtractionContext context,
            IReadOnlyCollection<string> fieldKeys,
            IReadOnlyDictionary<string, List<ExtractionCandidate>>? priorAttempts,
            CancellationToken ct);
    }
    private sealed class PaddleOcrProvider(string pythonPath, string scriptPath) : IExtractionProvider
    {
        public string Name => "paddleocr";
        public string Stage => "ocr";
        public int Order => 0;

        public async Task<IReadOnlyList<ExtractionCandidate>> ExtractAsync(
            ExtractionContext context,
            IReadOnlyCollection<string> fieldKeys,
            IReadOnlyDictionary<string, List<ExtractionCandidate>>? priorAttempts,
            CancellationToken ct)
        {
            var script = ResolveScriptPath(scriptPath);
            if (!File.Exists(script))
            {
                throw new InvalidOperationException($"Paddle OCR script not found at '{script}'.");
            }

            var sw = Stopwatch.StartNew();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{script}\" --file \"{context.LocalFilePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            sw.Stop();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Paddle OCR failed with exit code {process.ExitCode}: {stderr}");
            }

            using var doc = JsonDocument.Parse(stdout);
            var text = doc.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? string.Empty
                : string.Empty;

            var candidates = new List<ExtractionCandidate>(fieldKeys.Count);
            foreach (var key in fieldKeys)
            {
                var value = InferFieldValue(key, text);
                var confidence = InferConfidence(key, text, value);
                candidates.Add(new ExtractionCandidate(
                    key,
                    value,
                    confidence,
                    Stage,
                    Name,
                    new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["script"] = Path.GetFileName(script),
                        ["file_size"] = context.ByteLength,
                        ["tenant"] = TenantHash(context.TenantId),
                        ["technique"] = "paddleocr+label-regex",
                        ["processing_ms"] = sw.ElapsedMilliseconds
                    }));
            }

            return candidates;
        }

        private static string ResolveScriptPath(string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            var baseDir = AppContext.BaseDirectory;
            return Path.GetFullPath(Path.Combine(baseDir, configuredPath));
        }

        private static string InferFieldValue(string fieldKey, string text)
        {
            var normalized = text.Replace("\r", "\n", StringComparison.Ordinal);

            return fieldKey switch
            {
                "applicantName" => CaptureLabel(normalized, ["applicant name", "name"]),
                "dateOfBirth" => CaptureLabel(normalized, ["date of birth", "dob"], DateRegex()),
                "address" => CaptureLabel(normalized, ["address"]),
                "householdSize" => CaptureLabel(normalized, ["household size"], NumericRegex()),
                "monthlyIncome" => CaptureLabel(normalized, ["monthly income", "income"], CurrencyRegex()),
                "requestedServices" => CaptureLabel(normalized, ["requested services", "services"], null, maxWords: 16),
                "notes" => CaptureLabel(normalized, ["notes", "comments"], null, maxWords: 24),
                _ => string.Empty
            };
        }

        private static decimal InferConfidence(string fieldKey, string text, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0.35m;
            }

            var lower = text.ToLowerInvariant();
            var hasExplicitLabel = fieldKey switch
            {
                "applicantName" => lower.Contains("applicant name", StringComparison.Ordinal),
                "dateOfBirth" => lower.Contains("date of birth", StringComparison.Ordinal) || lower.Contains("dob", StringComparison.Ordinal),
                "address" => lower.Contains("address", StringComparison.Ordinal),
                "householdSize" => lower.Contains("household size", StringComparison.Ordinal),
                "monthlyIncome" => lower.Contains("monthly income", StringComparison.Ordinal) || lower.Contains("income", StringComparison.Ordinal),
                "requestedServices" => lower.Contains("requested services", StringComparison.Ordinal) || lower.Contains("services", StringComparison.Ordinal),
                "notes" => lower.Contains("notes", StringComparison.Ordinal),
                _ => false
            };

            var baseConfidence = hasExplicitLabel ? 0.84m : 0.68m;
            if (value.Length > 60)
            {
                baseConfidence -= 0.08m;
            }

            return Math.Round(Math.Clamp(baseConfidence, 0.20m, 0.95m), 4, MidpointRounding.ToZero);
        }

        private static string CaptureLabel(string text, string[] labels, Regex? preferredValueRegex = null, int maxWords = 8)
        {
            foreach (var label in labels)
            {
                var lineRegex = new Regex($@"(?im)^\s*{Regex.Escape(label)}\s*[:\-]\s*(?<value>.+)$", RegexOptions.Compiled);
                var match = lineRegex.Match(text);
                if (match.Success)
                {
                    var candidate = match.Groups["value"].Value.Trim();
                    if (preferredValueRegex is null)
                    {
                        return TrimWords(candidate, maxWords);
                    }

                    var preferred = preferredValueRegex.Match(candidate);
                    return preferred.Success ? preferred.Value.Trim() : TrimWords(candidate, maxWords);
                }
            }

            if (preferredValueRegex is not null)
            {
                var fallback = preferredValueRegex.Match(text);
                if (fallback.Success)
                {
                    return fallback.Value.Trim();
                }
            }

            return string.Empty;
        }

        private static string TrimWords(string value, int maxWords)
        {
            var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length <= maxWords)
            {
                return value.Trim();
            }

            return string.Join(' ', words.Take(maxWords));
        }
    }

    private sealed class OpenAiNormalizerProvider(string apiKey, string modelMini) : IExtractionProvider
    {
        private static readonly HttpClient Http = new();

        public string Name => "openai";
        public string Stage => "normalize";
        public int Order => 1;

        public async Task<IReadOnlyList<ExtractionCandidate>> ExtractAsync(
            ExtractionContext context,
            IReadOnlyCollection<string> fieldKeys,
            IReadOnlyDictionary<string, List<ExtractionCandidate>>? priorAttempts,
            CancellationToken ct)
        {
            if (priorAttempts is null || priorAttempts.Count == 0)
                return [];

            var baseline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in fieldKeys)
            {
                if (!priorAttempts.TryGetValue(key, out var attempts) || attempts.Count == 0)
                    continue;

                var best = attempts.OrderByDescending(a => a.Confidence).First();
                baseline[key] = best.Value;
            }

            if (baseline.Count == 0)
                return [];

            var requestPayload = new
            {
                model = modelMini,
                input = new object[]
                {
                    new
                    {
                        role = "developer",
                        content = "Normalize OCR-extracted intake fields. Return ONLY compact JSON object where keys are field names and values are objects: {\"value\": string, \"confidence\": number}. Confidence must be 0-1. Preserve missing fields as empty string with low confidence."
                    },
                    new
                    {
                        role = "user",
                        content = $"Document key: {context.OriginalFileKey}. Baseline values: {JsonSerializer.Serialize(baseline)}"
                    }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var res = await Http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                var isTransient = (int)res.StatusCode >= 500 || res.StatusCode == HttpStatusCode.TooManyRequests || res.StatusCode == HttpStatusCode.RequestTimeout;

                if (isTransient)
                {
                    throw new TransientExtractionException($"OpenAI normalization failed ({(int)res.StatusCode}).");
                }

                throw new InvalidOperationException($"OpenAI normalization failed ({(int)res.StatusCode}).");
            }

            var outputText = TryGetOutputText(body);
            if (string.IsNullOrWhiteSpace(outputText))
                return [];

            Dictionary<string, (string Value, decimal Confidence)> normalized;
            try
            {
                normalized = ParseNormalizedFields(outputText);
            }
            catch (JsonException)
            {
                return [];
            }
            var results = new List<ExtractionCandidate>();
            foreach (var key in fieldKeys)
            {
                if (!normalized.TryGetValue(key, out var item))
                    continue;

                results.Add(new ExtractionCandidate(
                    key,
                    item.Value,
                    item.Confidence,
                    Stage,
                    Name,
                    new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["model"] = modelMini,
                        ["tenant"] = TenantHash(context.TenantId),
                        ["technique"] = "openai-mini-json-normalize"
                    }));
            }

            return results;
        }

        private static string TryGetOutputText(string rawResponse)
        {
            using var doc = JsonDocument.Parse(rawResponse);

            if (doc.RootElement.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
            {
                var text = outputText.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            // Fallback: parse output[] → content[] → output_text items
            if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var part in content.EnumerateArray())
                        {
                            if (part.TryGetProperty("type", out var t) && t.GetString() == "output_text"
                                && part.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                            {
                                var text = txt.GetString();
                                if (!string.IsNullOrWhiteSpace(text))
                                    return text;
                            }
                        }
                    }
                }
            }

            return string.Empty;
        }

        private static Dictionary<string, (string Value, decimal Confidence)> ParseNormalizedFields(string jsonText)
        {
            var map = new Dictionary<string, (string Value, decimal Confidence)>(StringComparer.OrdinalIgnoreCase);
            using var doc = JsonDocument.Parse(jsonText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return map;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var value = prop.Value.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() ?? string.Empty
                    : string.Empty;

                var confidence = prop.Value.TryGetProperty("confidence", out var c) && c.ValueKind is JsonValueKind.Number
                    ? Math.Clamp(c.GetDecimal(), 0m, 1m)
                    : 0.4m;

                map[prop.Name] = (value, Math.Round(confidence, 4, MidpointRounding.ToZero));
            }

            return map;
        }
    }

    private sealed class OpenAiVisionProvider(string apiKey, string modelNano, string modelMini) : IExtractionProvider
    {
        private static readonly HttpClient Http = new();

        public string Name => "openai-vision";
        public string Stage => "ocr";
        public int Order => 1;

        public async Task<IReadOnlyList<ExtractionCandidate>> ExtractAsync(
            ExtractionContext context,
            IReadOnlyCollection<string> fieldKeys,
            IReadOnlyDictionary<string, List<ExtractionCandidate>>? priorAttempts,
            CancellationToken ct)
        {
            var fieldsJson = JsonSerializer.Serialize(fieldKeys);
            var prompt = $"Extract the following fields from this scanned intake form. Fields: {fieldsJson}. " +
                "Return ONLY a JSON object where keys are field names and values are objects with \"value\" (string or null) and \"confidence\" (number 0-1). " +
                "If a field is not present in the form, use null value with 0 confidence. Do not include markdown fences.";

            var (model, body, escalated, escalationReason) = await CallWithFallback(context, prompt, fieldKeys, ct);
            var outputText = TryGetResponseText(body);
            if (string.IsNullOrWhiteSpace(outputText))
                throw new InvalidOperationException($"OpenAI vision returned empty output_text. Model={model}. Response (first 500 chars): {body[..Math.Min(body.Length, 500)]}");

            Dictionary<string, (string Value, decimal Confidence)> parsed;
            try
            {
                parsed = ParseFieldsStrict(outputText);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"OpenAI vision returned unparseable JSON. Model={model}. Output: {outputText[..Math.Min(outputText.Length, 300)]}", ex);
            }

            var usage = TryGetUsage(body);
            var results = new List<ExtractionCandidate>();
            foreach (var key in fieldKeys)
            {
                if (!parsed.TryGetValue(key, out var item))
                    continue;

                var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["model"] = model,
                    ["technique"] = "openai-vision-extract",
                    ["tenant"] = TenantHash(context.TenantId)
                };

                if (usage is not null)
                {
                    metadata["prompt_tokens"] = usage.Value.PromptTokens;
                    metadata["completion_tokens"] = usage.Value.CompletionTokens;
                    metadata["total_tokens"] = usage.Value.TotalTokens;
                }

                if (escalated)
                {
                    metadata["escalated_from"] = modelNano;
                    metadata["escalation_reason"] = escalationReason ?? "nano_failed";
                }

                results.Add(new ExtractionCandidate(
                    key, item.Value, item.Confidence, Stage, Name, metadata));
            }

            return results;
        }

        private async Task<(string Model, string Body, bool Escalated, string? Reason)> CallWithFallback(
            ExtractionContext context, string prompt, IReadOnlyCollection<string> expectedFields, CancellationToken ct)
        {
            // Phase 1: Try nano with retry for transient errors
            string? nanoBody = null;
            Exception? lastTransient = null;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    nanoBody = await CallVisionApi(modelNano, context, prompt, ct);
                    lastTransient = null;
                    break;
                }
                catch (TransientExtractionException tex)
                {
                    lastTransient = tex;
                    if (attempt < 2)
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(10_000, 500 * (1 << attempt))), ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (InvalidOperationException)
                {
                    // Hard error (e.g. 400 Bad Request) -- fail immediately, do not escalate
                    throw;
                }
            }

            // If all nano retries failed with transient errors, escalate to mini
            if (nanoBody is null)
            {
                var reason = $"nano_transient_exhausted:{lastTransient?.GetType().Name}:{lastTransient?.Message}";
                var miniBody = await CallVisionApi(modelMini, context, prompt, ct);
                return (modelMini, miniBody, true, reason);
            }

            // Phase 2: Check nano response quality -- escalate on low confidence or empty output
            var outputText = TryGetResponseText(nanoBody);
            if (string.IsNullOrWhiteSpace(outputText))
            {
                var miniBody = await CallVisionApi(modelMini, context, prompt, ct);
                return (modelMini, miniBody, true, "nano_empty_response");
            }

            Dictionary<string, (string Value, decimal Confidence)>? parsed = null;
            try
            {
                parsed = ParseFieldsStrict(outputText);
            }
            catch (JsonException)
            {
                // Unparseable -- escalate to mini
                var miniBody = await CallVisionApi(modelMini, context, prompt, ct);
                return (modelMini, miniBody, true, "nano_unparseable_json");
            }

            // Compute average confidence over expected fields only (ignore hallucinated keys)
            if (parsed.Count > 0)
            {
                // Sum confidence only for expected fields; missing fields count as 0
                var totalConfidence = expectedFields
                    .Sum(key => parsed.TryGetValue(key, out var v) ? v.Confidence : 0m);
                var avgConfidence = totalConfidence / expectedFields.Count;
                if (avgConfidence < ReviewRequiredThreshold)
                {
                    var matchedCount = expectedFields.Count(key => parsed.ContainsKey(key));
                    var reason = $"low_confidence:nano_avg={avgConfidence:F4},matched={matchedCount}/{expectedFields.Count}";
                    var miniBody = await CallVisionApi(modelMini, context, prompt, ct);
                    return (modelMini, miniBody, true, reason);
                }
            }
            else
            {
                // No fields parsed -- escalate
                var miniBody = await CallVisionApi(modelMini, context, prompt, ct);
                return (modelMini, miniBody, true, "nano_no_fields_parsed");
            }

            return (modelNano, nanoBody, false, null);
        }

        private async Task<string> CallVisionApi(string model, ExtractionContext context, string prompt, CancellationToken ct)
        {
            var imageBase64 = Convert.ToBase64String(context.FileBytes);
            var mediaType = GetMediaType(context.OriginalFileKey);
            var dataUrl = $"data:{mediaType};base64,{imageBase64}";

            object fileContent = mediaType.StartsWith("image/", StringComparison.Ordinal)
                ? new { type = "input_image", image_url = dataUrl }
                : new { type = "input_file", filename = Path.GetFileName(context.OriginalFileKey), file_data = dataUrl };

            var requestPayload = new
            {
                model,
                input = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "input_text", text = prompt },
                            fileContent
                        }
                    }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var res = await Http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                var isTransient = (int)res.StatusCode >= 500 || res.StatusCode == HttpStatusCode.TooManyRequests || res.StatusCode == HttpStatusCode.RequestTimeout;
                if (isTransient)
                    throw new TransientExtractionException($"OpenAI vision failed ({(int)res.StatusCode}).");
                throw new InvalidOperationException($"OpenAI vision failed ({(int)res.StatusCode}): {body}");
            }

            return body;
        }

        private static string GetMediaType(string fileKey)
        {
            var ext = Path.GetExtension(fileKey).ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }

        private static string TryGetResponseText(string rawResponse)
        {
            using var doc = JsonDocument.Parse(rawResponse);

            // Prefer top-level output_text (convenience field)
            if (doc.RootElement.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
            {
                var text = ot.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            // Fallback: parse output[] → content[] → output_text items
            if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var part in content.EnumerateArray())
                        {
                            if (part.TryGetProperty("type", out var t) && t.GetString() == "output_text"
                                && part.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                            {
                                var text = txt.GetString();
                                if (!string.IsNullOrWhiteSpace(text))
                                    return text;
                            }
                        }
                    }
                }
            }

            return string.Empty;
        }

        private readonly record struct TokenUsage(long PromptTokens, long CompletionTokens, long TotalTokens);

        private static TokenUsage? TryGetUsage(string rawResponse)
        {
            using var doc = JsonDocument.Parse(rawResponse);
            if (!doc.RootElement.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                return null;

            var input = usage.TryGetProperty("input_tokens", out var it) && it.ValueKind == JsonValueKind.Number ? it.GetInt64() : 0;
            var output = usage.TryGetProperty("output_tokens", out var ot) && ot.ValueKind == JsonValueKind.Number ? ot.GetInt64() : 0;
            var total = usage.TryGetProperty("total_tokens", out var tt) && tt.ValueKind == JsonValueKind.Number ? tt.GetInt64() : input + output;
            return new TokenUsage(input, output, total);
        }

        private static Dictionary<string, (string Value, decimal Confidence)> ParseFieldsStrict(string jsonText)
        {
            // Strip markdown fences if model wraps output
            var text = jsonText.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var lines = text.Split('\n');
                var startIdx = lines[0].TrimEnd().Length > 3 ? 1 : 1;
                var endIdx = lines.Length - 1;
                if (endIdx > 0 && lines[endIdx].Trim() == "```")
                    endIdx--;
                text = string.Join('\n', lines[startIdx..(endIdx + 1)]);
            }

            var map = new Dictionary<string, (string Value, decimal Confidence)>(StringComparer.OrdinalIgnoreCase);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Expected JSON object at root.");

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                string value;
                if (prop.Value.TryGetProperty("value", out var v))
                {
                    value = v.ValueKind switch
                    {
                        JsonValueKind.String => v.GetString() ?? string.Empty,
                        JsonValueKind.Null => string.Empty,
                        JsonValueKind.Array => JsonSerializer.Serialize(v),
                        _ => v.GetRawText()
                    };
                }
                else
                {
                    value = string.Empty;
                }

                var confidence = prop.Value.TryGetProperty("confidence", out var c) && c.ValueKind is JsonValueKind.Number
                    ? Math.Clamp(c.GetDecimal(), 0m, 1m)
                    : 0.4m;

                map[prop.Name] = (value, Math.Round(confidence, 4, MidpointRounding.ToZero));
            }

            return map;
        }
    }

    private sealed class MockTesseractProvider : IExtractionProvider
    {
        public string Name => "tesseract-mock";
        public string Stage => "ocr";
        public int Order => 0;

        public Task<IReadOnlyList<ExtractionCandidate>> ExtractAsync(
            ExtractionContext context,
            IReadOnlyCollection<string> fieldKeys,
            IReadOnlyDictionary<string, List<ExtractionCandidate>>? priorAttempts,
            CancellationToken ct)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["applicantName"] = "Jamie Carter",
                ["dateOfBirth"] = "03/15/1988",
                ["address"] = "742 Evergreen Terrace, Springfield",
                ["householdSize"] = "4",
                ["monthlyIncome"] = "$1,850",
                ["requestedServices"] = "Housing assistance, utility aid",
                ["notes"] = "Applicant mentioned recent job loss"
            };

            var baseConfidence = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["applicantName"] = 0.85m,
                ["dateOfBirth"] = 0.70m,
                ["address"] = 0.62m,
                ["householdSize"] = 0.82m,
                ["monthlyIncome"] = 0.53m,
                ["requestedServices"] = 0.56m,
                ["notes"] = 0.49m
            };

            var list = new List<ExtractionCandidate>();
            foreach (var key in fieldKeys)
            {
                if (!values.TryGetValue(key, out var value))
                    continue;

                var confidence = baseConfidence.TryGetValue(key, out var c) ? c : 0.50m;
                list.Add(new ExtractionCandidate(
                    key,
                    value,
                    confidence,
                    Stage,
                    Name,
                    new Dictionary<string, object>
                    {
                        ["tenant"] = TenantHash(context.TenantId),
                        ["technique"] = "mock-template-values"
                    }));
            }

            return Task.FromResult<IReadOnlyList<ExtractionCandidate>>(list);
        }
    }

    private static class FieldConsensus
    {
        public static FieldExtractionResult Resolve(string key, IReadOnlyList<ExtractionCandidate> attempts)
        {
            var groups = attempts
                .Where(a => !string.IsNullOrWhiteSpace(a.Value))
                .GroupBy(a => Normalize(a.Value))
                .Select(g => new
                {
                    Value = g.Key,
                    Attempts = g.ToList(),
                    AvgConfidence = g.Average(a => a.Confidence)
                })
                .OrderByDescending(g => g.Attempts.Count)
                .ThenByDescending(g => g.AvgConfidence)
                .ThenByDescending(g => g.Attempts.Max(a => a.Confidence))
                .ToList();

            var raw = attempts.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Value));
            if (raw is null)
            {
                return new FieldExtractionResult(
                    key,
                    string.Empty,
                    0.01m,
                    string.Empty,
                    attempts,
                    attempts.Select(a => a.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            }

            if (groups.Count == 0)
            {
                return new FieldExtractionResult(
                    key,
                    raw.Value,
                    raw.Confidence,
                    Normalize(raw.Value),
                    attempts,
                    attempts.Select(a => a.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            }

            var top = groups[0];
            var agreementBoost = Math.Min(0.12m, 0.06m * (top.Attempts.Count - 1));
            var systemConfidence = Math.Round(Math.Min(0.99m, top.AvgConfidence + agreementBoost), 4, MidpointRounding.ToZero);
            var representative = top.Attempts.MaxBy(a => a.Confidence)!.Value;

            return new FieldExtractionResult(
                key,
                representative,
                systemConfidence,
                top.Value,
                attempts,
                attempts.Select(a => a.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        private static string Normalize(string value)
        {
            return string.Join(
                ' ',
                value.Replace("\r", " ", StringComparison.Ordinal)
                    .Replace("\n", " ", StringComparison.Ordinal)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(v => v.ToLowerInvariant()));
        }
    }

    private static string TenantHash(string tenantId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(tenantId));
        return Convert.ToHexString(hash.AsSpan(0, 4));
    }

    [GeneratedRegex(@"\b\d{1,2}/\d{1,2}/\d{2,4}\b", RegexOptions.Compiled)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\$?[0-9][0-9,]*(?:\.[0-9]{2})?", RegexOptions.Compiled)]
    private static partial Regex CurrencyRegex();

    [GeneratedRegex(@"\b\d+\b", RegexOptions.Compiled)]
    private static partial Regex NumericRegex();
}
