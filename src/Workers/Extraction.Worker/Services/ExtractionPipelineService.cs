using System.Text.Json;
using Dapper;
using Npgsql;

namespace Extraction.Worker;

internal static class ExtractionPipelineService
{
    public static async Task<(IReadOnlyList<FieldExtractionResult> SchemaFields, IReadOnlyList<FieldExtractionResult> DiscoveredFields)> RunExtractionPipeline(
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

        var schemaKeySet = new HashSet<string>(fieldKeys, StringComparer.OrdinalIgnoreCase);

        var schemaResults = new List<FieldExtractionResult>(fieldKeys.Count);
        foreach (var key in fieldKeys)
        {
            if (!attemptsByField.TryGetValue(key, out var attempts) || attempts.Count == 0)
                continue;

            schemaResults.Add(FieldConsensus.Resolve(key, attempts));
        }

        // Discovered fields: keys returned by providers that are not in the schema
        var discoveredResults = new List<FieldExtractionResult>();
        foreach (var (key, attempts) in attemptsByField)
        {
            if (schemaKeySet.Contains(key))
                continue;
            if (attempts.Count == 0)
                continue;
            // Only include fields marked as discovered by at least one provider attempt
            if (!attempts.Any(a => a.Metadata is not null &&
                                   a.Metadata.TryGetValue("is_discovered", out var flag) &&
                                   flag is true or "true"))
                continue;

            discoveredResults.Add(FieldConsensus.Resolve(key, attempts));
        }

        return (schemaResults, discoveredResults);
    }

    public static async Task<bool> SupportsExtractionAttempts(NpgsqlConnection conn, NpgsqlTransaction tx)
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

    public static async Task PersistExtractionAttempts(
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
                    RequiresReview = result.SystemConfidence < Worker.ReviewRequiredThreshold,
                    Details = JsonSerializer.Serialize(attempt.Metadata)
                },
                tx);

            ct.ThrowIfCancellationRequested();
        }
    }
}

internal static class FieldConsensus
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
