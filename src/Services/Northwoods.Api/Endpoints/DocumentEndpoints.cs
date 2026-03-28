using Dapper;
using Northwoods.Tenancy;

namespace Northwoods.Api;

internal static class DocumentEndpoints
{
    public static WebApplication MapDocumentEndpoints(this WebApplication app)
    {
        app.MapGet("/documents/{id:guid}/source", async (Guid id, HttpContext httpContext, DbConnectionFactory dbFactory, ObjectStore objectStore) =>
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

            var s3Response = await objectStore.GetObjectStreamAsync(fileKey);
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

        return app;
    }
}
