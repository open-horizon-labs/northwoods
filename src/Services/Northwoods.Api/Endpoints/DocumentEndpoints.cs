using Amazon.S3;
using Amazon.S3.Model;
using Dapper;
using Northwoods.Contracts;
using Northwoods.Tenancy;

namespace Northwoods.Api;

internal static class DocumentEndpoints
{
    public static WebApplication MapDocumentEndpoints(this WebApplication app)
    {
        app.MapMethods("/documents/{id:guid}/source", ["GET", "HEAD"], async (Guid id, HttpContext httpContext, DbConnectionFactory dbFactory, ObjectStore objectStore) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var fileKey = await session.Connection.QueryFirstOrDefaultAsync<string>(
                "SELECT original_file_key FROM documents WHERE id = @Id AND tenant_id = @TenantId",
                new { Id = id, TenantId = authContext.TenantId },
                session.Transaction);

            if (fileKey is null)
                return Results.NotFound();

            GetObjectResponse s3Response;
            try
            {
                s3Response = await objectStore.GetObjectStreamAsync(fileKey);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
            {
                return Results.NotFound(new { error = "Source document not available." });
            }

            httpContext.Response.RegisterForDispose(s3Response);

            var contentType = s3Response.Headers.ContentType ?? "application/pdf";
            var extension = contentType == "application/pdf" ? ".pdf" : "";
            httpContext.Response.Headers["Content-Disposition"] = $"inline; filename=\"{id}{extension}\"";
            httpContext.Response.Headers["Cache-Control"] = "private, max-age=300";

            return Results.Stream(
                s3Response.ResponseStream,
                contentType: contentType,
                enableRangeProcessing: true);
        })
            .WithName("GetDocumentSource").WithTags("Documents")
            .WithSummary("Streams the original source document for a given document ID.")
            .RequireAuthorization();

        app.MapGet("/documents", async (HttpContext httpContext, DbConnectionFactory dbFactory) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var items = (await session.Connection.QueryAsync<DocumentListItem>(
                """
                SELECT d.id AS DocumentId,
                       d.template_id AS TemplateId,
                       COALESCE((SELECT ef.extracted_value FROM extracted_fields ef WHERE ef.document_id = d.id AND ef.tenant_id = d.tenant_id AND LOWER(ef.field_key) = 'applicantname' ORDER BY ef.id LIMIT 1), d.template_id) AS ApplicantName,
                       d.status AS Status,
                       d.created_at AS CreatedAt,
                       (SELECT ef.extracted_value FROM extracted_fields ef WHERE ef.document_id = d.id AND ef.tenant_id = d.tenant_id AND LOWER(ef.field_key) IN ('date', 'intakedate', 'servicedate', 'formdate') ORDER BY CASE LOWER(ef.field_key) WHEN 'date' THEN 1 WHEN 'intakedate' THEN 2 WHEN 'servicedate' THEN 3 WHEN 'formdate' THEN 4 ELSE 5 END, ef.id LIMIT 1) AS DocumentDate
                FROM documents d
                WHERE d.tenant_id = @TenantId
                ORDER BY d.created_at DESC
                """,
                new { TenantId = authContext.TenantId },
                transaction: session.Transaction)).ToList();
            return Results.Ok(items);
        })
            .WithName("ListDocuments").WithTags("Documents")
            .WithSummary("Lists all documents for the active tenant.")
            .RequireAuthorization();

        return app;
    }
}