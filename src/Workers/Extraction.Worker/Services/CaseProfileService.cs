using System.Text.Json;
using Dapper;
using Northwoods.Contracts;
using Npgsql;

namespace Extraction.Worker;

internal static class CaseProfileService
{
    private static readonly HttpClient EmbeddingHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

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
                var (embedding, promptTokens, totalTokens) = await EmbeddingService.GenerateCaseEmbeddingAsync(EmbeddingHttp, caseProfileText, apiKey, ct);
                embeddingLiteral = EmbeddingService.ToPgVectorLiteral(embedding);

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
                            dimensions = EmbeddingService.CaseEmbeddingDimensions,
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

}
