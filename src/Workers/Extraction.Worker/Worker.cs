using Dapper;
using Npgsql;

namespace Extraction.Worker;

public sealed class Worker(ILogger<Worker> logger, IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingDocuments(connectionString, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Extraction cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessPendingDocuments(string connectionString, CancellationToken ct)
    {
        // Use the superuser connection (not RLS-scoped) because the worker
        // processes documents across all tenants. Tenant context is preserved
        // in the data itself.
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var docs = (await conn.QueryAsync<(Guid id, string tenant_id, string template_id)>(
            """
            SELECT id, tenant_id, template_id FROM documents
            WHERE status = 'uploaded'
            ORDER BY created_at
            LIMIT 10
            """)).ToList();

        if (docs.Count == 0) return;

        logger.LogInformation("Found {Count} documents to extract", docs.Count);

        foreach (var doc in docs)
        {
            await ExtractDocument(conn, doc.id, doc.tenant_id, doc.template_id, ct);
        }
    }

    private async Task ExtractDocument(NpgsqlConnection conn, Guid docId, string tenantId, string templateId, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // Mark as extracting
            await conn.ExecuteAsync(
                "UPDATE documents SET status = 'extracting', updated_at = now() WHERE id = @Id",
                new { Id = docId }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO audit_events (document_id, tenant_id, event_type)
                VALUES (@DocId, @TenantId, 'extraction_started')
                """,
                new { DocId = docId, TenantId = tenantId }, tx);

            // Load the template field schema to know what fields to extract
            var fieldSchemaJson = await conn.QueryFirstOrDefaultAsync<string>(
                "SELECT field_schema FROM templates WHERE id = @Id",
                new { Id = templateId }, tx);

            // Simulate extraction: generate mock extracted values with confidence scores
            var fields = GenerateMockExtraction(templateId);

            foreach (var (key, value, confidence) in fields)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO extracted_fields (document_id, tenant_id, field_key, extracted_value, confidence, requires_review)
                    VALUES (@DocId, @TenantId, @Key, @Value, @Confidence, @RequiresReview)
                    """,
                    new { DocId = docId, TenantId = tenantId, Key = key, Value = value, Confidence = confidence, RequiresReview = confidence < 0.80m },
                    tx);
            }

            // Mark as review-ready
            await conn.ExecuteAsync(
                "UPDATE documents SET status = 'review_ready', updated_at = now() WHERE id = @Id",
                new { Id = docId }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO audit_events (document_id, tenant_id, event_type, details)
                VALUES (@DocId, @TenantId, 'extraction_completed', @Details::jsonb)
                """,
                new { DocId = docId, TenantId = tenantId, Details = $"{{\"fields_extracted\":{fields.Count}}}" },
                tx);

            await tx.CommitAsync(ct);
            logger.LogInformation("Extracted document {DocId} for tenant {TenantId}: {FieldCount} fields",
                docId, tenantId, fields.Count);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            logger.LogError(ex, "Failed to extract document {DocId}", docId);

            // Mark as failed outside the rolled-back transaction
            await using var failTx = await conn.BeginTransactionAsync(ct);
            await conn.ExecuteAsync(
                "UPDATE documents SET status = 'failed', updated_at = now() WHERE id = @Id",
                new { Id = docId }, failTx);
            await conn.ExecuteAsync(
                """
                INSERT INTO audit_events (document_id, tenant_id, event_type, details)
                VALUES (@DocId, @TenantId, 'extraction_failed', @Details::jsonb)
                """,
                new { DocId = docId, TenantId = tenantId, Details = $"{{\"error\":\"{ex.Message.Replace("\"", "\\\"")}\"}}" },
                failTx);
            await failTx.CommitAsync(ct);
        }
    }

    /// <summary>
    /// Generates mock extracted field values with realistic confidence scores.
    /// This simulates OCR/AI extraction output and will be replaced by real
    /// extraction (via Temporal activities) in a later iteration.
    /// </summary>
    private static List<(string key, string value, decimal confidence)> GenerateMockExtraction(string templateId)
    {
        var rng = Random.Shared;
        return
        [
            ("applicantName", "Jamie Carter", RoundConfidence(0.85m + (decimal)rng.NextDouble() * 0.15m)),
            ("dateOfBirth", "03/15/1988", RoundConfidence(0.70m + (decimal)rng.NextDouble() * 0.25m)),
            ("address", "742 Evergreen Terrace, Springfield", RoundConfidence(0.60m + (decimal)rng.NextDouble() * 0.35m)),
            ("householdSize", "4", RoundConfidence(0.80m + (decimal)rng.NextDouble() * 0.20m)),
            ("monthlyIncome", "$1,850", RoundConfidence(0.50m + (decimal)rng.NextDouble() * 0.40m)),
            ("requestedServices", "Housing assistance, utility aid", RoundConfidence(0.55m + (decimal)rng.NextDouble() * 0.35m)),
            ("notes", "Applicant mentioned recent job loss", RoundConfidence(0.40m + (decimal)rng.NextDouble() * 0.30m)),
        ];
    }

    private static decimal RoundConfidence(decimal v) => Math.Round(Math.Min(v, 0.99m), 4);
}
