using Amazon.S3;
using System.Text.Json;
using Dapper;
using Northwoods.Contracts;
using Northwoods.Tenancy;

namespace Northwoods.Api;

internal static class TemplateEndpoints
{
    public static WebApplication MapTemplateEndpoints(this WebApplication app)
    {
        app.MapGet("/templates", async (HttpContext httpContext, DbConnectionFactory dbFactory) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var includeArchived = httpContext.Request.Query.ContainsKey("includeArchived");
            var rows = await session.Connection.QueryAsync<(string id, string name, string field_schema, bool is_archived, string? blank_pdf_key)>(
                includeArchived
                    ? "SELECT id, name, field_schema, is_archived, blank_pdf_key FROM templates WHERE tenant_id = @TenantId ORDER BY name"
                    : "SELECT id, name, field_schema, is_archived, blank_pdf_key FROM templates WHERE tenant_id = @TenantId AND is_archived = false ORDER BY name",
                new { TenantId = authContext.TenantId },
                session.Transaction);

            var templates = rows
                .Select(row => new TemplateDescriptor(row.id, row.name, ParseTemplateFields(row.field_schema), row.is_archived, row.blank_pdf_key))
                .ToList();

            return Results.Ok(templates);
        })
            .WithName("GetTemplates").WithTags("Templates")
            .WithSummary("Returns tenant-scoped intake templates and their field schemas.")
            .RequireAuthorization();

        app.MapGet("/templates/{templateId}/blank", async (
            HttpContext httpContext,
            string templateId,
            DbConnectionFactory dbFactory,
            ObjectStore objectStore) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var template = await session.Connection.QueryFirstOrDefaultAsync<(string id, string? blank_pdf_key)>(
                "SELECT id, blank_pdf_key FROM templates WHERE tenant_id = @TenantId AND id = @TemplateId LIMIT 1",
                new { TenantId = authContext.TenantId, TemplateId = templateId },
                session.Transaction);

            if (template == default)
                return Results.NotFound();

            if (string.IsNullOrWhiteSpace(template.blank_pdf_key))
                return Results.NotFound(new { error = "No printable PDF is available for this template. Contact your administrator to upload one." });

            byte[] pdfBytes;
            try
            {
                pdfBytes = await objectStore.DownloadAsync(template.blank_pdf_key);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
            {
                return Results.NotFound(new { error = "No printable PDF is available for this template. Contact your administrator to upload one." });
            }

            var pdfFilename = $"{template.id}-template.pdf";
            return Results.File(pdfBytes, "application/pdf", pdfFilename);
        })
            .WithName("GetTemplateBlankForm").WithTags("Templates")
            .WithSummary("Downloads the blank PDF template from storage. Returns 404 if no PDF has been uploaded for this template.")
            .RequireAuthorization();

        app.MapPost("/templates", async (CreateTemplateRequest request, HttpContext httpContext, DbConnectionFactory dbFactory) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();
            if (authContext.Role != UserRole.Admin)
                return Results.Forbid();

            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(request.Id)) errors.Add("Id is required.");
            if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Name is required.");
            if (request.Fields is null || request.Fields.Count == 0) errors.Add("At least one field is required.");
            if (errors.Count > 0)
                return Results.BadRequest(new { errors });

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var existing = await session.Connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM templates WHERE id = @Id AND tenant_id = @TenantId LIMIT 1",
                new { request.Id, TenantId = authContext.TenantId },
                session.Transaction);
            if (existing is not null)
                return Results.Conflict(new { error = $"Template '{request.Id}' already exists." });

            var fieldSchema = SerializeFieldSchema(request.Fields!);
            await session.Connection.ExecuteAsync(
                """
                INSERT INTO templates (id, tenant_id, name, field_schema)
                VALUES (@Id, @TenantId, @Name, @FieldSchema::jsonb)
                """,
                new { request.Id, TenantId = authContext.TenantId, request.Name, FieldSchema = fieldSchema },
                session.Transaction);

            await session.Connection.ExecuteAsync(
                """
                INSERT INTO audit_events (tenant_id, event_type, actor_id, details)
                VALUES (@TenantId, 'template_created', @ActorId, @Details::jsonb)
                """,
                new
                {
                    TenantId = authContext.TenantId,
                    ActorId = authContext.UserId,
                    Details = JsonSerializer.Serialize(new { template_id = request.Id, name = request.Name })
                },
                session.Transaction);

            await session.CommitAsync();

            var created = new TemplateDescriptor(request.Id, request.Name, request.Fields!);
            return Results.Created($"/templates/{request.Id}", created);
        })
            .WithName("CreateTemplate").WithTags("Templates")
            .WithSummary("Creates a new template for the current tenant. Admin only.")
            .RequireAuthorization();

        app.MapPut("/templates/{templateId}", async (string templateId, UpdateTemplateRequest request, HttpContext httpContext, DbConnectionFactory dbFactory) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();
            if (authContext.Role != UserRole.Admin)
                return Results.Forbid();

            if (string.IsNullOrWhiteSpace(request.Name) && request.Fields is null)
                return Results.BadRequest(new { error = "At least one of Name or Fields must be provided." });

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var existing = await session.Connection.QueryFirstOrDefaultAsync<(string id, string name, string field_schema, bool is_archived)>(
                "SELECT id, name, field_schema, is_archived FROM templates WHERE id = @Id AND tenant_id = @TenantId LIMIT 1",
                new { Id = templateId, TenantId = authContext.TenantId },
                session.Transaction);
            if (existing == default)
                return Results.NotFound();
            if (existing.is_archived)
                return Results.BadRequest(new { error = "Cannot edit an archived template." });

            var newName = request.Name ?? existing.name;
            var newFieldSchema = request.Fields is not null ? SerializeFieldSchema(request.Fields) : existing.field_schema;

            await session.Connection.ExecuteAsync(
                """
                UPDATE templates SET name = @Name, field_schema = @FieldSchema::jsonb, updated_at = now()
                WHERE id = @Id AND tenant_id = @TenantId
                """,
                new { Id = templateId, TenantId = authContext.TenantId, Name = newName, FieldSchema = newFieldSchema },
                session.Transaction);

            await session.Connection.ExecuteAsync(
                """
                INSERT INTO audit_events (tenant_id, event_type, actor_id, details)
                VALUES (@TenantId, 'template_updated', @ActorId, @Details::jsonb)
                """,
                new
                {
                    TenantId = authContext.TenantId,
                    ActorId = authContext.UserId,
                    Details = JsonSerializer.Serialize(new { template_id = templateId, name = newName })
                },
                session.Transaction);

            await session.CommitAsync();

            var updatedFields = request.Fields ?? ParseTemplateFields(existing.field_schema);
            return Results.Ok(new TemplateDescriptor(templateId, newName, updatedFields));
        })
            .WithName("UpdateTemplate").WithTags("Templates")
            .WithSummary("Updates a template's name and/or field schema. Admin only.")
            .RequireAuthorization();

        app.MapDelete("/templates/{templateId}", async (string templateId, HttpContext httpContext, DbConnectionFactory dbFactory) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();
            if (authContext.Role != UserRole.Admin)
                return Results.Forbid();

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var existing = await session.Connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM templates WHERE id = @Id AND tenant_id = @TenantId LIMIT 1",
                new { Id = templateId, TenantId = authContext.TenantId },
                session.Transaction);
            if (existing is null)
                return Results.NotFound();

            await session.Connection.ExecuteAsync(
                "UPDATE templates SET is_archived = true, updated_at = now() WHERE id = @Id AND tenant_id = @TenantId",
                new { Id = templateId, TenantId = authContext.TenantId },
                session.Transaction);

            await session.Connection.ExecuteAsync(
                """
                INSERT INTO audit_events (tenant_id, event_type, actor_id, details)
                VALUES (@TenantId, 'template_archived', @ActorId, @Details::jsonb)
                """,
                new
                {
                    TenantId = authContext.TenantId,
                    ActorId = authContext.UserId,
                    Details = JsonSerializer.Serialize(new { template_id = templateId })
                },
                session.Transaction);

            await session.CommitAsync();
            return Results.NoContent();
        })
            .WithName("ArchiveTemplate").WithTags("Templates")
            .WithSummary("Archives (soft-deletes) a template. Admin only.")
            .RequireAuthorization();

        app.MapPost("/templates/{templateId}/blank-pdf", async (string templateId, HttpContext httpContext, DbConnectionFactory dbFactory, ObjectStore objectStore) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();
            if (authContext.Role != UserRole.Admin)
                return Results.Forbid();

            var form = await httpContext.Request.ReadFormAsync();
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "A PDF file is required." });

            if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "File must be a PDF." });

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var existing = await session.Connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM templates WHERE id = @Id AND tenant_id = @TenantId AND is_archived = false LIMIT 1",
                new { Id = templateId, TenantId = authContext.TenantId },
                session.Transaction);
            if (existing is null)
                return Results.NotFound();

            var pdfKey = $"{authContext.TenantId}/templates/{templateId}/blank.pdf";
            await using var stream = file.OpenReadStream();
            await objectStore.UploadAsync(pdfKey, stream, "application/pdf");

            await session.Connection.ExecuteAsync(
                "UPDATE templates SET blank_pdf_key = @PdfKey, updated_at = now() WHERE id = @Id AND tenant_id = @TenantId",
                new { Id = templateId, TenantId = authContext.TenantId, PdfKey = pdfKey },
                session.Transaction);

            await session.CommitAsync();
            return Results.Ok(new { templateId, blankPdfKey = pdfKey });
        })
            .WithName("UploadTemplateBlankPdf").WithTags("Templates")
            .WithSummary("Uploads a custom blank PDF for a template. Admin only.")
            .RequireAuthorization();

        return app;
    }

    internal static string SerializeFieldSchema(IReadOnlyList<TemplateField> fields)
    {
        var fieldArray = fields.Select(f => new { key = f.Key, type = f.Type, required = f.Required }).ToArray();
        return JsonSerializer.Serialize(new { fields = fieldArray });
    }

    internal static IReadOnlyList<TemplateField> ParseTemplateFields(string schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            return [];

        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            if (!document.RootElement.TryGetProperty("fields", out var fieldsElement) ||
                fieldsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var fields = new List<TemplateField>();
            foreach (var field in fieldsElement.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object)
                    continue;

                var key = ReadTemplateFieldString(field, "key");
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                fields.Add(new TemplateField(
                    key,
                    ReadTemplateFieldString(field, "type", "string"),
                    ReadTemplateFieldBool(field, "required")));
            }

            return fields;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ReadTemplateFieldString(JsonElement field, string propertyName, string defaultValue = "")
    {
        if (!field.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return defaultValue;

        return property.GetString() ?? defaultValue;
    }

    private static bool ReadTemplateFieldBool(JsonElement field, string propertyName)
    {
        if (!field.TryGetProperty(propertyName, out var property) ||
            (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False))
            return false;

        return property.GetBoolean();
    }

}
