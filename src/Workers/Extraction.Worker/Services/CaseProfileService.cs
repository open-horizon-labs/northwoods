using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace Extraction.Worker;

internal static class CaseProfileService
{
    private const int CaseEmbeddingDimensions = 1536;
    private static readonly HttpClient EmbeddingHttp = new();

    public static string BuildCaseProfileText(
        string templateId,
        IReadOnlyList<FieldExtractionResult> fields,
        IReadOnlyList<FieldExtractionResult>? discoveredFields = null)
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

        var result = $"template={templateId}; fields={extractedText}; ocr={ocrText}";

        if (discoveredFields is { Count: > 0 })
        {
            var discoveredPairs = discoveredFields
                .Select(f => $"{f.FieldKey}: {f.FinalValue}")
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();

            if (discoveredPairs.Length > 0)
                result += $"; discovered={string.Join(" | ", discoveredPairs)}";
        }

        return result;
    }

    public static async Task<bool> SupportsCaseProfiles(NpgsqlConnection conn, NpgsqlTransaction tx)
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

    public static async Task PersistCaseProfile(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid docId,
        string tenantId,
        string templateId,
        string caseProfileText,
        IReadOnlyList<FieldExtractionResult> fields,
        string? apiKey,
        ILogger logger,
        CancellationToken ct)
    {
        static string? GetFieldValue(IReadOnlyList<FieldExtractionResult> results, string key)
            => results.FirstOrDefault(r => string.Equals(r.FieldKey, key, StringComparison.OrdinalIgnoreCase))?.FinalValue;

        var applicantName = GetFieldValue(fields, "applicantName");
        var dateOfBirth = GetFieldValue(fields, "dateOfBirth");
        var address = GetFieldValue(fields, "address");

        string? embeddingLiteral = null;

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

    public static async Task<(double[] Embedding, long PromptTokens, long TotalTokens)> GenerateCaseEmbeddingAsync(
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

    public static string ToPgVectorLiteral(double[] values)
    {
        return $"[{string.Join(',', values.Select(v => v.ToString(CultureInfo.InvariantCulture)))}]";
    }
}
