using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using System.Net.Http.Headers;
using Dapper;
using Northwoods.Tenancy;
using Npgsql;

namespace Extraction.Worker;

public sealed partial class Worker(ILogger<Worker> logger, IConfiguration config) : BackgroundService
{
    private const decimal HighConfidenceThreshold = 0.90m;
    private const decimal ReviewRequiredThreshold = 0.75m;
    private const decimal EscalateThreshold = 0.82m;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        var objectStore = BuildObjectStore(config);
        var providers = BuildProviders(config);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingDocuments(connectionString, objectStore, providers, stoppingToken);
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
        else
        {
            providers.Add(new MockTesseractProvider());
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

        return [.. providers.OrderBy(p => p.Order)];
    }

    private async Task ProcessPendingDocuments(
        string connectionString,
        ObjectStore objectStore,
        IReadOnlyList<IExtractionProvider> providers,
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
            await ExtractDocument(conn, objectStore, doc.id, doc.tenant_id, doc.template_id, doc.original_file_key, providers, ct);
        }
    }

    private async Task ExtractDocument(
        NpgsqlConnection conn,
        ObjectStore objectStore,
        Guid docId,
        string tenantId,
        string templateId,
        string originalFileKey,
        IReadOnlyList<IExtractionProvider> providers,
        CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                "UPDATE documents SET status = 'extracting', updated_at = now() WHERE id = @Id",
                new { Id = docId },
                tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO audit_events (document_id, tenant_id, event_type)
                VALUES (@DocId, @TenantId, 'extraction_started')
                """,
                new { DocId = docId, TenantId = tenantId },
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
                var extractionContext = new ExtractionContext(docId, tenantId, templateId, originalFileKey, tempFile, bytes.Length);
                var results = await RunExtractionPipeline(extractionContext, fieldKeys, providers, ct);
                var canPersistAttempts = await SupportsExtractionAttempts(conn, tx);
                var extractionRunId = Guid.NewGuid();

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

                await conn.ExecuteAsync(
                    "UPDATE documents SET status = 'review_ready', updated_at = now() WHERE id = @Id",
                    new { Id = docId },
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
                            provider_count = providers.Count,
                            fields_extracted = results.Count,
                            high_confidence_fields = results.Count(r => r.SystemConfidence >= HighConfidenceThreshold),
                            warning_fields = results.Count(r => r.SystemConfidence >= ReviewRequiredThreshold && r.SystemConfidence < HighConfidenceThreshold),
                            review_required_threshold = ReviewRequiredThreshold,
                            auto_accept_threshold = HighConfidenceThreshold,
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

            await using var failTx = await conn.BeginTransactionAsync(ct);
            await conn.ExecuteAsync(
                "UPDATE documents SET status = 'failed', updated_at = now() WHERE id = @Id",
                new { Id = docId },
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
                    Details = JsonSerializer.Serialize(new { error = ex.Message })
                },
                failTx);

            await failTx.CommitAsync(ct);
        }
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

        var baseline = await providers[0].ExtractAsync(context, fieldKeys, null, ct);
        var attemptsByField = baseline.ToDictionary(
            r => r.FieldKey,
            r => new List<ExtractionCandidate> { r },
            StringComparer.OrdinalIgnoreCase);

        var lowConfidenceKeys = baseline
            .Where(r => r.Confidence < EscalateThreshold)
            .Select(r => r.FieldKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (providers.Count > 1 && lowConfidenceKeys.Length > 0)
        {
            foreach (var provider in providers.Skip(1))
            {
                var escalated = await provider.ExtractAsync(context, lowConfidenceKeys, attemptsByField, ct);
                foreach (var attempt in escalated)
                {
                    if (!attemptsByField.TryGetValue(attempt.FieldKey, out var list))
                    {
                        list = [];
                        attemptsByField[attempt.FieldKey] = list;
                    }

                    list.Add(attempt);
                }
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
                    Technique = attempt.Metadata is not null && attempt.Metadata.TryGetValue("technique", out var technique) && !string.IsNullOrWhiteSpace(technique)
                        ? technique
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

    private readonly record struct ExtractionContext(
        Guid DocumentId,
        string TenantId,
        string TemplateId,
        string OriginalFileKey,
        string LocalFilePath,
        int ByteLength);

    private sealed record ExtractionCandidate(
        string FieldKey,
        string Value,
        decimal Confidence,
        string Stage,
        string Provider,
        Dictionary<string, string>? Metadata = null);

    private sealed record FieldExtractionResult(
        string FieldKey,
        string FinalValue,
        decimal SystemConfidence,
        string NormalizedValue,
        IReadOnlyList<ExtractionCandidate> AllAttempts,
        IReadOnlyList<string> ProviderSequence);

    private interface IExtractionProvider
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
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["script"] = Path.GetFileName(script),
                        ["file_size"] = context.ByteLength.ToString(CultureInfo.InvariantCulture),
                        ["tenant"] = TenantHash(context.TenantId),
                        ["technique"] = "paddleocr+label-regex"
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
                throw new InvalidOperationException($"OpenAI normalization failed ({(int)res.StatusCode}).");

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
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
                return outputText.GetString() ?? string.Empty;
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
                    new Dictionary<string, string>
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
