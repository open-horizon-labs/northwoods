using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Northwoods.Contracts;
using Northwoods.Tenancy;

namespace Northwoods.Api;

internal sealed record AuthContext(Guid UserId, string TenantId, UserRole Role);

internal sealed record SimilarCaseCandidate(
    Guid IntakeId,
    string TemplateId,
    string? ApplicantName,
    string? DateOfBirth,
    string? Address,
    decimal MatchScore,
    string? MatchSourcesCsv)
{
    // MatchSources is derived from the CSV string returned by the SQL query.
    // array_agg() cannot be automatically mapped to string[] by Dapper+Npgsql,
    // so we convert it via a comma-separated string column instead.
    public IReadOnlyList<string> MatchSources =>
        string.IsNullOrEmpty(MatchSourcesCsv)
            ? []
            : MatchSourcesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
}

internal sealed record CaseFieldValue(
    Guid IntakeId,
    string FieldKey,
    string Value);

internal sealed class ApiObservability
{
    private long _requestCount;
    private long _reviewFinalizationCount;

    public void IncrementRequestCount() => Interlocked.Increment(ref _requestCount);

    public void IncrementReviewFinalizationCount() => Interlocked.Increment(ref _reviewFinalizationCount);

    public long RequestCount => Interlocked.Read(ref _requestCount);

    public long ReviewFinalizationCount => Interlocked.Read(ref _reviewFinalizationCount);
}

internal sealed record ApiMetricsResponse(
    long RequestCount,
    long ReviewFinalizationCount,
    long ExtractionSuccessCount,
    long ExtractionFailureCount);

internal static class ApiHelpers
{
    public static string GetCorrelationId(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue("CorrelationId", out var item) && item is string existing && !string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        if (httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }

        return Guid.NewGuid().ToString("N");
    }

    public static bool CanUpload(UserRole role, bool reviewersCanUpload) =>
        role == UserRole.IntakeWorker || (reviewersCanUpload && role == UserRole.Reviewer);

    public static AuthContext? GetAuthContext(ClaimsPrincipal principal)
    {
        if (principal.Identity is not { IsAuthenticated: true })
            return null;

        var tenantId = principal.FindFirstValue(AuthClaims.TenantId);
        var userIdValue = principal.FindFirstValue(AuthClaims.UserId) ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var roleValue = principal.FindFirstValue(AuthClaims.Role);

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userIdValue) || string.IsNullOrWhiteSpace(roleValue))
            return null;

        if (!Guid.TryParse(userIdValue, out var userId))
            return null;

        if (!Enum.TryParse<UserRole>(roleValue, true, out var role))
            return null;

        return new AuthContext(userId, tenantId, role);
    }

    public static ProcessingStatus ParseStatus(string dbStatus) => dbStatus switch
    {
        "uploaded" => ProcessingStatus.Uploaded,
        "extracting" => ProcessingStatus.Extracting,
        "review_ready" => ProcessingStatus.ReviewReady,
        "completed" => ProcessingStatus.Completed,
        "finalized" => ProcessingStatus.Finalized,
        "failed" => ProcessingStatus.Failed,
        _ => throw new ArgumentException($"Unknown status: {dbStatus}")
    };
}
