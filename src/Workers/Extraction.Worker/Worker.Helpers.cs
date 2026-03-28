using System.Net.Sockets;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace Extraction.Worker;

public sealed partial class Worker
{
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
}
