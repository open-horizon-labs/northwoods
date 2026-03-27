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
}
