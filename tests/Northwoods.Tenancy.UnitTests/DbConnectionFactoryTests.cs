using System.Data;
using Northwoods.Tenancy;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Northwoods.Tenancy.UnitTests;

public sealed class DbConnectionFactoryTests
{
    private const string SeedTenantADoc = "11111111-1111-1111-1111-111111111111";
    private const string SeedTenantBDoc = "44444444-4444-4444-4444-444444444444";

    private readonly ITestOutputHelper _output;

    public DbConnectionFactoryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task OpenSessionAsync_SetsTenantContextAndRestrictedRole()
    {
        var connectionString = ResolveConnectionString();
        if (!await IsDatabaseAvailableAsync(connectionString))
        {
            _output.WriteLine("Postgres is not available; deferring tenant-session runtime assertions.");
            return;
        }

        var factory = new DbConnectionFactory(connectionString);
        await using var session = await factory.OpenSessionAsync("tenant-a");

        var tenantId = await ScalarStringAsync(session, "SELECT current_setting('app.tenant_id', true)");
        var currentRole = await ScalarStringAsync(session, "SELECT current_user");

        Assert.Equal("tenant-a", tenantId);
        Assert.Equal("app_user", currentRole);

        await session.CommitAsync();
    }

    [Fact]
    public async Task OpenSessionAsync_EnforcesTenantRlsIsolationForDocuments()
    {
        var connectionString = ResolveConnectionString();
        if (!await IsDatabaseAvailableAsync(connectionString))
        {
            _output.WriteLine("Postgres is not available; deferring tenant RLS runtime assertions.");
            return;
        }

        var factory = new DbConnectionFactory(connectionString);

        await using var tenantASession = await factory.OpenSessionAsync("tenant-a");
        var tenantAOwnSeed = await ScalarLongAsync(tenantASession, "SELECT COUNT(*) FROM documents WHERE id = @id", SeedTenantADoc);
        var tenantAOtherSeed = await ScalarLongAsync(tenantASession, "SELECT COUNT(*) FROM documents WHERE id = @id", SeedTenantBDoc);
        await tenantASession.CommitAsync();

        await using var tenantBSession = await factory.OpenSessionAsync("tenant-b");
        var tenantBOwnSeed = await ScalarLongAsync(tenantBSession, "SELECT COUNT(*) FROM documents WHERE id = @id", SeedTenantBDoc);
        var tenantBOtherSeed = await ScalarLongAsync(tenantBSession, "SELECT COUNT(*) FROM documents WHERE id = @id", SeedTenantADoc);
        await tenantBSession.CommitAsync();

        Assert.Equal(1, tenantAOwnSeed);
        Assert.Equal(0, tenantAOtherSeed);
        Assert.Equal(1, tenantBOwnSeed);
        Assert.Equal(0, tenantBOtherSeed);
    }


    [Fact]
    public async Task OpenSessionAsync_EnforcesTenantRlsIsolationForTemplates()
    {
        var connectionString = ResolveConnectionString();
        if (!await IsDatabaseAvailableAsync(connectionString))
        {
            _output.WriteLine("Postgres is not available; deferring template tenant RLS runtime assertions.");
            return;
        }

        var factory = new DbConnectionFactory(connectionString);

        // Both tenants own general-assistance; RLS must scope to one per session.
        await using var tenantASession = await factory.OpenSessionAsync("tenant-a");
        var tenantACount = await ScalarLongAsync(tenantASession,
            "SELECT COUNT(*) FROM templates WHERE id = @id", "general-assistance", textId: true);
        await tenantASession.CommitAsync();

        await using var tenantBSession = await factory.OpenSessionAsync("tenant-b");
        var tenantBCount = await ScalarLongAsync(tenantBSession,
            "SELECT COUNT(*) FROM templates WHERE id = @id", "general-assistance", textId: true);
        await tenantBSession.CommitAsync();

        // Each tenant sees exactly their own template row, not both.
        Assert.Equal(1, tenantACount);
        Assert.Equal(1, tenantBCount);
    }

    /// <summary>
    /// Reproduces the exact query pattern from Extraction.Worker to verify
    /// that the tenant_id filter prevents cross-tenant template resolution.
    /// The worker uses a raw connection (no RLS), so the application-level
    /// WHERE tenant_id = @TenantId clause is the only guard.
    /// Without it (#54), a QueryFirstOrDefault on a shared template ID could
    /// return the wrong tenant's schema.
    /// </summary>
    [Fact]
    public async Task WorkerTemplateQuery_WithTenantFilter_RejectsCrossTenantAccess()
    {
        var connectionString = ResolveConnectionString();
        if (!await IsDatabaseAvailableAsync(connectionString))
        {
            _output.WriteLine("Postgres is not available; deferring worker template query assertions.");
            return;
        }

        // Use a raw connection (no RLS) — same as the extraction worker.
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Without tenant_id filter, the query is ambiguous: multiple rows match.
        await using var ambiguousQuery = new NpgsqlCommand(
            "SELECT COUNT(*) FROM templates WHERE id = @Id", conn);
        ambiguousQuery.Parameters.AddWithValue("@Id", "general-assistance");
        var ambiguousCount = Convert.ToInt64(await ambiguousQuery.ExecuteScalarAsync());
        Assert.True(ambiguousCount > 1,
            $"Expected multiple tenant rows for shared template id, got {ambiguousCount}");

        // With tenant_id filter (the fix), exactly one row is returned.
        await using var scopedQuery = new NpgsqlCommand(
            "SELECT field_schema FROM templates WHERE id = @Id AND tenant_id = @TenantId", conn);
        scopedQuery.Parameters.AddWithValue("@Id", "general-assistance");
        scopedQuery.Parameters.AddWithValue("@TenantId", "tenant-a");
        var scopedResult = await scopedQuery.ExecuteScalarAsync();
        Assert.NotNull(scopedResult);

        // With tenant_id filter and a non-existent tenant, nothing is returned.
        await using var noMatchQuery = new NpgsqlCommand(
            "SELECT field_schema FROM templates WHERE id = @Id AND tenant_id = @TenantId", conn);
        noMatchQuery.Parameters.AddWithValue("@Id", "general-assistance");
        noMatchQuery.Parameters.AddWithValue("@TenantId", "tenant-nonexistent");
        var noMatchResult = await noMatchQuery.ExecuteScalarAsync();
        Assert.Null(noMatchResult);
    }

    private static string ResolveConnectionString()
    {
        return Environment.GetEnvironmentVariable("NORTHWOODS_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5433;Database=northwoods;Username=northwoods;Password=northwoods";
    }

    private static async Task<bool> IsDatabaseAvailableAsync(string connectionString)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ScalarStringAsync(DbSession session, string sql)
    {
        await using var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
        var value = await command.ExecuteScalarAsync();
        return Convert.ToString(value) ?? string.Empty;
    }

    private static async Task<long> ScalarLongAsync(DbSession session, string sql, string id)
    {
        await using var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@id", Guid.Parse(id));

        var value = await command.ExecuteScalarAsync();
        return value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            _ => throw new InvalidOperationException("Unexpected scalar value for COUNT query")
        };
    }

    private static async Task<long> ScalarLongAsync(DbSession session, string sql, string id, bool textId)
    {
        await using var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@id", id);

        var value = await command.ExecuteScalarAsync();
        return value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            _ => throw new InvalidOperationException("Unexpected scalar value for COUNT query")
        };
    }
}
