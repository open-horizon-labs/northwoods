using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Northwoods.Api;
using Northwoods.Contracts;
using Northwoods.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

var jwtSigningSecret = builder.Configuration["Auth:Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Auth:Jwt:SigningKey is required.");

var jwtSigningKeyBytes = Encoding.UTF8.GetBytes(jwtSigningSecret);
if (jwtSigningKeyBytes.Length < 32)
{
    throw new InvalidOperationException("Auth:Jwt:SigningKey must be at least 32 bytes when UTF-8 encoded.");
}

var jwtSigningCredentials = new SigningCredentials(new SymmetricSecurityKey(jwtSigningKeyBytes), SecurityAlgorithms.HmacSha256);
var jwtIssuer = builder.Configuration["Auth:Jwt:Issuer"] ?? "northwoods-api";
var jwtAudience = builder.Configuration["Auth:Jwt:Audience"] ?? "northwoods-web";
var jwtExpiration = TimeSpan.FromMinutes(builder.Configuration.GetValue("Auth:Jwt:ExpiresInMinutes", 120));
var reviewersCanUpload = builder.Configuration.GetValue("Auth:ReviewerCanUpload", false);

var useAppUserRole = builder.Configuration.GetValue("Database:UseAppUserRole", true);
var db = new DbConnectionFactory(connectionString, useAppUserRole);
var store = new ObjectStore(
    builder.Configuration["Minio:Endpoint"] ?? "localhost:9000",
    builder.Configuration["Minio:AccessKey"] ?? "northwoods",
    builder.Configuration["Minio:SecretKey"] ?? "northwoods",
    builder.Configuration["Minio:BucketName"] ?? "intakes");

builder.Services.AddSingleton(db);
builder.Services.AddSingleton(store);
builder.Services.AddOpenApi();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:4173"];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
builder.Services.AddHealthChecks()
    .AddCheck("postgres", new PostgresHealthCheck(connectionString));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = jwtSigningCredentials.Key,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = AuthClaims.Role
        };
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Allow query-string token for document streaming endpoints (iframes cannot send Authorization headers)
                if (context.Request.Path.StartsWithSegments("/documents")
                    && context.Request.Query.TryGetValue("access_token", out var token)
                    && !string.IsNullOrWhiteSpace(token))
                {
                    context.Token = token!;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();
var observability = new ApiObservability();

const string correlationIdHeader = "X-Correlation-Id";
var embeddingHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
var useAiSummaries = builder.Configuration.GetValue("Extraction:UseAiSummaries", true);
var openAiApiKey = builder.Configuration["OPENAI_API_KEY"] ?? builder.Configuration["Extraction:OpenAi:ApiKey"];

app.UseCors();

app.Use(async (httpContext, next) =>
{
    var correlationId = httpContext.Request.Headers.TryGetValue(correlationIdHeader, out var provided)
        && !string.IsNullOrWhiteSpace(provided)
            ? provided.ToString()
            : Guid.NewGuid().ToString("N");

    httpContext.Items["CorrelationId"] = correlationId;
    httpContext.Response.Headers[correlationIdHeader] = correlationId;
    observability.IncrementRequestCount();

    using (app.Logger.BeginScope(new Dictionary<string, object?>
           {
               ["CorrelationId"] = correlationId
           }))
    {
        try
        {
            await next();
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Unhandled exception on {Path}", httpContext.Request.Path);
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = 500;
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsJsonAsync(new { error = ex.Message, type = ex.GetType().Name });
            }
        }
    }
});

// Ensure database schema and seed data exist on startup
await DatabaseInitializer.InitializeAsync(connectionString, useAppUserRole, app.Logger);

// Verify tenant isolation is enforced before accepting any requests.
// If UseAppUserRole=false the app runs as the DB owner, which PostgreSQL exempts from RLS,
// meaning every query can see every tenant's data — a critical security failure.
if (useAppUserRole)
{
    await DatabaseInitializer.AssertRlsEnforcedAsync(connectionString, app.Logger);
}
else
{
    app.Logger.LogCritical(
        "SECURITY: Database:UseAppUserRole=false — RLS is disabled. " +
        "All tenant data is visible to all connections. DO NOT run in production.");
}

// Ensure the MinIO bucket exists on startup
await store.EnsureBucketAsync();

// Seed blank template PDFs into MinIO (idempotent — skips templates that already have one)
var samplesDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "samples", "intakes");
if (!Directory.Exists(samplesDir))
    samplesDir = Path.Combine(Directory.GetCurrentDirectory(), "samples", "intakes");
await DatabaseInitializer.SeedBlankPdfsAsync(connectionString, store, samplesDir, app.Logger);

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapHealthChecks("/healthz");

// --- Metrics ---
app.MapGet("/metrics", async (HttpContext httpContext, DbConnectionFactory dbFactory) =>
{
    var authContext = ApiHelpers.GetAuthContext(httpContext.User);
    if (authContext is null)
    {
        return Results.Unauthorized();
    }

    await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

    var counts = await session.Connection.QueryFirstOrDefaultAsync<(long ExtractionSuccessCount, long ExtractionFailureCount, long ReviewFinalizationCount)>(
        """
        SELECT
            COUNT(*) FILTER (WHERE status IN ('review_ready', 'completed', 'finalized'))::bigint AS ExtractionSuccessCount,
            COUNT(*) FILTER (WHERE status = 'failed')::bigint AS ExtractionFailureCount,
            COUNT(*) FILTER (WHERE status = 'finalized')::bigint AS ReviewFinalizationCount
        FROM documents
        WHERE tenant_id = @TenantId
        """,
        new { TenantId = authContext.TenantId },
        transaction: session.Transaction);

    return Results.Ok(new ApiMetricsResponse(
        observability.RequestCount,
        counts.ReviewFinalizationCount,
        counts.ExtractionSuccessCount,
        counts.ExtractionFailureCount));
})
    .WithName("GetMetrics")
    .WithSummary("Returns basic service metrics for tenant-scoped requests.")
    .RequireAuthorization();

// --- Endpoint groups ---
app.MapAuthEndpoints(jwtSigningCredentials, jwtIssuer, jwtAudience, jwtExpiration);
app.MapTemplateEndpoints();
app.MapIntakeEndpoints(reviewersCanUpload);
app.MapReviewEndpoints(embeddingHttpClient, openAiApiKey, useAiSummaries, observability);
app.MapDocumentEndpoints();
app.MapSearchEndpoints();
app.MapAdminEndpoints();

app.Run();
