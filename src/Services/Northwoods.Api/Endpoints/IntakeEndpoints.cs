using System.Text.Json;
using Dapper;
using Northwoods.Contracts;
using Northwoods.Tenancy;

namespace Northwoods.Api;

internal static class IntakeEndpoints
{
    public static WebApplication MapIntakeEndpoints(this WebApplication app, bool reviewersCanUpload)
    {
        app.MapPost("/intakes", async (HttpContext httpContext, DbConnectionFactory dbFactory, ObjectStore objectStore) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            if (!ApiHelpers.CanUpload(authContext.Role, reviewersCanUpload))
                return Results.Forbid();

            var correlationId = ApiHelpers.GetCorrelationId(httpContext);

            var form = await httpContext.Request.ReadFormAsync();
            var file = form.Files["file"];
            var templateId = form["templateId"].ToString();

            if (file is null || string.IsNullOrWhiteSpace(templateId))
                return Results.BadRequest(new { error = "file and templateId are required." });

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var templateExists = await session.Connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM templates WHERE tenant_id = @TenantId AND id = @TemplateId LIMIT 1",
                new { TenantId = authContext.TenantId, TemplateId = templateId },
                session.Transaction);
            if (templateExists != 1)
            {
                return Results.BadRequest(new { error = "Unknown templateId." });
            }

            var docId = Guid.NewGuid();
            var fileKey = $"{authContext.TenantId}/{docId}/{file.FileName}";
            await using var stream = file.OpenReadStream();
            await objectStore.UploadAsync(fileKey, stream, file.ContentType ?? "application/octet-stream");

            await session.Connection.ExecuteAsync(
                """
                INSERT INTO documents (id, tenant_id, template_id, uploaded_by, original_file_key, original_file_name, status)
                VALUES (@Id, @TenantId, @TemplateId, @UploadedBy, @FileKey, @FileName, 'uploaded')
                """,
                new
                {
                    Id = docId,
                    TenantId = authContext.TenantId,
                    TemplateId = templateId,
                    UploadedBy = authContext.UserId,
                    FileKey = fileKey,
                    FileName = file.FileName
                },
                session.Transaction);

            await session.Connection.ExecuteAsync(
                """
                INSERT INTO audit_events (document_id, tenant_id, event_type, actor_id, details)
                VALUES (@DocId, @TenantId, 'intake_uploaded', @ActorId, @Details::jsonb)
                """,
                new
                {
                    DocId = docId,
                    TenantId = authContext.TenantId,
                    ActorId = authContext.UserId,
                    Details = JsonSerializer.Serialize(new
                    {
                        correlation_id = correlationId,
                        template = templateId,
                        file = file.FileName
                    })
                },
                session.Transaction);

            await session.CommitAsync();

            return Results.Accepted($"/intakes/{docId}", new CreateIntakeResponse(docId, ProcessingStatus.Uploaded));
        })
            .WithName("CreateIntake").WithTags("Intakes")
            .WithSummary("Uploads a document and creates an intake record.")
            .RequireAuthorization();

        app.MapGet("/intakes/{id:guid}", async (Guid id, HttpContext httpContext, DbConnectionFactory dbFactory) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var doc = await session.Connection.QueryFirstOrDefaultAsync<(Guid id, string tenant_id, string template_id, string status)>(
                "SELECT id, tenant_id, template_id, status FROM documents WHERE id = @Id AND tenant_id = @TenantId",
                new { Id = id, TenantId = authContext.TenantId },
                session.Transaction);

            if (doc == default) return Results.NotFound();

            var fields = (await session.Connection.QueryAsync<ConfidenceField>(
                """
                SELECT field_key AS FieldKey,
                       COALESCE(corrected_value, extracted_value) AS Value,
                       confidence AS Confidence,
                       requires_review AS RequiresReview
                FROM extracted_fields
                WHERE document_id = @Id AND tenant_id = @TenantId
                """,
                new { Id = id, TenantId = authContext.TenantId },
                session.Transaction)).ToList();

            var status = ApiHelpers.ParseStatus(doc.status);
            return Results.Ok(new IntakeStatusResponse(doc.id, doc.tenant_id, doc.template_id, status, fields));
        })
            .WithName("GetIntake").WithTags("Intakes")
            .WithSummary("Returns intake processing state and extracted draft fields.")
            .RequireAuthorization();

        return app;
    }
}
