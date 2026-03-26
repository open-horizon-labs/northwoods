namespace Northwoods.Tenancy;

public static class TenantHeaders
{
    public const string TenantId = "X-Tenant-Id";
    public const string Role = "X-User-Role";
}

public sealed record TenantContext(string TenantId, string Role);
