using Dapper;
using Northwoods.Contracts;
using Northwoods.Tenancy;

namespace Northwoods.Api;

internal static class AdminEndpoints
{
    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        // DELETE /admin/documents — wipes all documents and derived data for the caller's tenant.
        // Requires Admin role. Idempotent (safe to call on an already-empty tenant).
        app.MapDelete("/admin/documents", async (HttpContext httpContext, DbConnectionFactory dbFactory) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            if (authContext.Role != UserRole.Admin)
                return Results.Forbid();

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            // Delete in dependency order; ON DELETE CASCADE handles most child rows,
            // but explicit ordering avoids FK errors on databases without cascades.
            var deleted = await session.Connection.ExecuteScalarAsync<int>(
                """
                WITH del_audit AS (
                    DELETE FROM audit_events
                    WHERE tenant_id = @TenantId AND document_id IS NOT NULL
                    RETURNING 1
                ),
                del_profiles AS (
                    DELETE FROM case_profiles WHERE tenant_id = @TenantId RETURNING 1
                ),
                del_fields AS (
                    DELETE FROM extracted_fields WHERE tenant_id = @TenantId RETURNING 1
                ),
                del_attempts AS (
                    DELETE FROM extraction_attempts WHERE tenant_id = @TenantId RETURNING 1
                ),
                del_docs AS (
                    DELETE FROM documents WHERE tenant_id = @TenantId RETURNING 1
                )
                SELECT (SELECT COUNT(*) FROM del_docs)::int
                """,
                new { TenantId = authContext.TenantId },
                session.Transaction);

            await session.CommitAsync();

            return Results.Ok(new { deleted, tenantId = authContext.TenantId });
        })
            .WithName("WipeDocuments").WithTags("Admin")
            .WithSummary("Deletes all documents and derived data for the caller's tenant. Requires Admin role.")
            .RequireAuthorization();

        return app;
    }
}
