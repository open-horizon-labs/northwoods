using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
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
    builder.Configuration["Minio:BucketName"] ?? "intakes",
    builder.Configuration["Minio:PublicEndpoint"]);

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
const double SearchFuzzySimilarityThreshold = 0.3;
const double CaseAggregateSimilarityThreshold = 0.6;

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
        await next();
    }
});

// Ensure the MinIO bucket exists on startup
await store.EnsureBucketAsync();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapHealthChecks("/healthz");


app.MapGet("/metrics", async (HttpContext httpContext, DbConnectionFactory dbFactory) =>
{
    var authContext = GetAuthContext(httpContext.User);
    if (authContext is null)
    {
        return Results.Unauthorized();
    }

    await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

    var counts = await session.Connection.QueryFirstOrDefaultAsync<(long ExtractionSuccessCount, long ExtractionFailureCount, long ReviewFinalizationCount)>(
        """
        SELECT
            COUNT(*) FILTER (WHERE status IN ('review_ready', 'finalized'))::bigint AS ExtractionSuccessCount,
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

// --- Auth ---
app.MapPost("/auth/login", async (LoginRequest request, DbConnectionFactory dbFactory) =>
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.Email)) errors.Add("Email is required.");
    if (string.IsNullOrWhiteSpace(request.Password)) errors.Add("Password is required.");

    static bool ContainsNullByte(string? value) => value is not null && value.Contains('\0');
    if (ContainsNullByte(request.Email) || ContainsNullByte(request.Password) || ContainsNullByte(request.TenantId))
        errors.Add("Fields must not contain null bytes.");

    if (errors.Count > 0)
        return Results.BadRequest(new { errors });

    // Resolve tenant: if TenantId is absent, look it up from the email globally.
    // The unscoped session runs as the DB owner (bypasses RLS).
    var tenantId = request.TenantId;
    if (string.IsNullOrWhiteSpace(tenantId))
    {
        await using var global = await dbFactory.OpenUnscopedSessionAsync();
        tenantId = await global.Connection.QueryFirstOrDefaultAsync<string>(
            "SELECT tenant_id FROM users WHERE email = @Email LIMIT 1",
            new { request.Email },
            global.Transaction);
        if (string.IsNullOrWhiteSpace(tenantId))
            return Results.Unauthorized();
    }

    await using var session = await dbFactory.OpenSessionAsync(tenantId);
    var user = await session.Connection.QueryFirstOrDefaultAsync<(Guid id, string role, string password_hash)>(
        "SELECT id, role, password_hash FROM users WHERE email = @Email AND tenant_id = @TenantId",
        new { request.Email, TenantId = tenantId },
        session.Transaction);

    if (user == default || !VerifyPassword(request.Password, user.password_hash))
        return Results.Unauthorized();

    if (!Enum.TryParse<UserRole>(user.role, true, out var role))
        return Results.Unauthorized();

    var token = CreateJwt(
        user.id,
        tenantId,
        role,
        jwtSigningCredentials,
        jwtIssuer,
        jwtAudience,
        jwtExpiration);

    return Results.Ok(new LoginResponse(token, tenantId, role));
})
    .WithName("Login").WithTags("Auth")
    .WithSummary("Authenticates a user and returns a signed JWT access token.");

// --- Templates ---
app.MapGet("/templates", async (HttpContext httpContext, DbConnectionFactory dbFactory) =>
{
    var authContext = GetAuthContext(httpContext.User);
    if (authContext is null)
        return Results.Unauthorized();

    await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

    var rows = await session.Connection.QueryAsync<(string id, string name, string field_schema)>(
        "SELECT id, name, field_schema FROM templates WHERE tenant_id = @TenantId ORDER BY name",
        new { TenantId = authContext.TenantId },
        session.Transaction);

    var templates = rows
        .Select(row => new TemplateDescriptor(row.id, row.name, ParseTemplateFields(row.field_schema)))
        .ToList();

    return Results.Ok(templates);
})
    .WithName("GetTemplates").WithTags("Templates")
    .WithSummary("Returns tenant-scoped intake templates and their field schemas.")
    .RequireAuthorization();

app.MapGet("/templates/{templateId}/blank", async (
    HttpContext httpContext,
    string templateId,
    bool? download,
    DbConnectionFactory dbFactory) =>
{
    var authContext = GetAuthContext(httpContext.User);
    if (authContext is null)
        return Results.Unauthorized();

    await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

    var template = await session.Connection.QueryFirstOrDefaultAsync<(string id, string name, string field_schema)>(
        "SELECT id, name, field_schema FROM templates WHERE tenant_id = @TenantId AND id = @TemplateId LIMIT 1",
        new { TenantId = authContext.TenantId, TemplateId = templateId },
        session.Transaction);

    if (template == default)
        return Results.NotFound();

    var fields = ParseTemplateFields(template.field_schema);
    var html = BuildBlankTemplateHtml(template.name, fields);
    var filename = $"{template.id}-template.html";

    if (download is true)
        return Results.File(Encoding.UTF8.GetBytes(html), "text/html", filename);

    return Results.Text(html, "text/html");
})
    .WithName("GetTemplateBlankForm").WithTags("Templates")
    .WithSummary("Generates a printable blank template form for preview/download.")
    .RequireAuthorization();

// --- Intakes ---
app.MapPost("/intakes", async (HttpContext httpContext, DbConnectionFactory dbFactory, ObjectStore objectStore) =>
{
    var authContext = GetAuthContext(httpContext.User);
    if (authContext is null)
        return Results.Unauthorized();

    if (!CanUpload(authContext.Role, reviewersCanUpload))
        return Results.Forbid();

    var correlationId = GetCorrelationId(httpContext);

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

    // Upload to MinIO
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
    var authContext = GetAuthContext(httpContext.User);
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

    var status = ParseStatus(doc.status);
    return Results.Ok(new IntakeStatusResponse(doc.id, doc.tenant_id, doc.template_id, status, fields));
})
    .WithName("GetIntake").WithTags("Intakes")
    .WithSummary("Returns intake processing state and extracted draft fields.")
    .RequireAuthorization();

// --- Reviews ---
app.MapGet("/review-queue", async (HttpContext httpContext, DbConnectionFactory dbFactory) =>
{
    var authContext = GetAuthContext(httpContext.User);
    if (authContext is null)
        return Results.Unauthorized();

    await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

    var items = (await session.Connection.QueryAsync<ReviewQueueItem>(
        """
        SELECT d.id AS ReviewId, d.id AS IntakeId,
               COALESCE((SELECT ef.extracted_value FROM extracted_fields ef WHERE ef.document_id = d.id AND ef.tenant_id = d.tenant_id AND ef.field_key = 'applicantName' LIMIT 1), '(unknown)') AS ApplicantName,
               d.template_id AS TemplateId,
               (SELECT COUNT(*)::int FROM extracted_fields ef WHERE ef.document_id = d.id AND ef.tenant_id = d.tenant_id AND ef.requires_review) AS UncertainFieldCount
        FROM documents d
        WHERE d.tenant_id = @TenantId AND d.status = 'review_ready'
        ORDER BY d.created_at
        """,
        new { TenantId = authContext.TenantId },
        transaction: session.Transaction)).ToList();
    return Results.Ok(items);
})
    .WithName("GetReviewQueue").WithTags("Reviews")
    .WithSummary("Returns documents awaiting review for the active tenant.")
    .RequireAuthorization();

app.MapGet("/reviews/{id:guid}", async (Guid id, HttpContext httpContext, DbConnectionFactory dbFactory, ObjectStore objectStore) =>
{
    var authContext = GetAuthContext(httpContext.User);
    if (authContext is null)
        return Results.Unauthorized();

    await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

    var doc = await session.Connection.QueryFirstOrDefaultAsync<(Guid id, string tenant_id, string template_id, string status, string original_file_key)>(
        "SELECT id, tenant_id, template_id, status, original_file_key FROM documents WHERE id = @Id AND tenant_id = @TenantId",
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

    var auditEvents = (await session.Connection.QueryAsync<string>(
        "SELECT event_type FROM audit_events WHERE document_id = @Id AND tenant_id = @TenantId ORDER BY created_at",
        new { Id = id, TenantId = authContext.TenantId },
        session.Transaction)).ToList();

    var similarCases = await FindSimilarCasesAsync(session.Connection, session.Transaction, id, authContext.TenantId, doc.template_id, fields, 5);

    var sourceUrl = objectStore.GetPresignedUrl(doc.original_file_key, TimeSpan.FromMinutes(15));
    var status = ParseStatus(doc.status);

    return Results.Ok(new ReviewDetailResponse(doc.id, doc.id, doc.tenant_id, doc.template_id, sourceUrl, status, fields, similarCases, auditEvents));
})
    .WithName("GetReview").WithTags("Reviews")
    .WithSummary("Returns the document, extracted fields, and audit trail for a review task.")
    .RequireAuthorization();

app.MapPost("/reviews/{id:guid}/finalize", async (Guid id, FinalizeReviewRequest request, HttpContext httpContext, DbConnectionFactory dbFactory) =>
{
    var authContext = GetAuthContext(httpContext.User);
    if (authContext is null)
        return Results.Unauthorized();

    if (authContext.Role != UserRole.Reviewer)
        return Results.Forbid();

    var correlationId = GetCorrelationId(httpContext);

    await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

    var doc = await session.Connection.QueryFirstOrDefaultAsync<(Guid id, string status)>(
        "SELECT id, status FROM documents WHERE id = @Id AND tenant_id = @TenantId",
        new { Id = id, TenantId = authContext.TenantId },
        session.Transaction);

    if (doc == default) return Results.NotFound();
    if (doc.status != "review_ready")
        return Results.BadRequest(new { error = $"Document is in '{doc.status}' state, not review_ready." });

    foreach (var field in request.Fields)
    {
        await session.Connection.ExecuteAsync(
            """
            UPDATE extracted_fields
            SET corrected_value = @Value, requires_review = false, updated_at = now()
            WHERE document_id = @DocId AND tenant_id = @TenantId AND field_key = @FieldKey
            """,
            new { DocId = id, TenantId = authContext.TenantId, field.FieldKey, field.Value },
            session.Transaction);
    }

    // Finalize
    await session.Connection.ExecuteAsync(
        "UPDATE documents SET status = 'finalized', updated_at = now() WHERE id = @Id AND tenant_id = @TenantId",
        new { Id = id, TenantId = authContext.TenantId },
        session.Transaction);

    await session.Connection.ExecuteAsync(
        """
        INSERT INTO audit_events (document_id, tenant_id, event_type, actor_id, details)
        VALUES (@DocId, @TenantId, 'finalized', @ActorId, @Details::jsonb)
        """,
        new
        {
            DocId = id,
            TenantId = authContext.TenantId,
            ActorId = authContext.UserId,
            Details = JsonSerializer.Serialize(new
            {
                correlation_id = correlationId,
                note = request.ReviewerNote
            })
        },
        session.Transaction);

    await session.CommitAsync();
    observability.IncrementReviewFinalizationCount();
    return Results.Ok(new FinalizeReviewResponse(id, ProcessingStatus.Finalized));
})
    .WithName("FinalizeReview").WithTags("Reviews")
    .WithSummary("Persists reviewer corrections and finalizes the intake record.")
    .RequireAuthorization();

// --- Search ---
app.MapGet("/search", async (HttpContext httpContext, DbConnectionFactory dbFactory, string? q) =>
{
    var authContext = GetAuthContext(httpContext.User);
    if (authContext is null)
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(q))
        return Results.Ok(new SearchResponse("", []));

    await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

    var items = (await session.Connection.QueryAsync<SearchResultItem>(
        @"
        SELECT
            cp.document_id AS IntakeId,
            cp.template_id AS TemplateId,
            COALESCE(cp.applicant_name, '(unknown)') AS ApplicantName,
            d.status AS Status,
            COALESCE(
                (SELECT AVG(ef.confidence) FROM extracted_fields ef WHERE ef.document_id = cp.document_id AND ef.tenant_id = cp.tenant_id),
                0
            )::decimal AS Confidence,
            ts_headline('simple', cp.search_text, websearch_to_tsquery('simple', @Query),
                'StartSel=**, StopSel=**, MaxWords=30, MinWords=15') AS Snippet
        FROM case_profiles cp
        JOIN documents d ON d.id = cp.document_id AND d.tenant_id = cp.tenant_id
        WHERE cp.tenant_id = @TenantId
          AND (cp.search_tsv @@ websearch_to_tsquery('simple', @Query)
           OR similarity(lower(COALESCE(cp.applicant_name, '')), lower(@Query)) > @SearchThreshold)
        ORDER BY
            ts_rank_cd(cp.search_tsv, websearch_to_tsquery('simple', @Query)) DESC,
            similarity(lower(COALESCE(cp.applicant_name, '')), lower(@Query)) DESC
        LIMIT 50
        ",
        new { Query = q, TenantId = authContext.TenantId, SearchThreshold = SearchFuzzySimilarityThreshold },
        session.Transaction)).ToList();

    return Results.Ok(new SearchResponse(q, items));
})
    .WithName("Search").WithTags("Search")
    .WithSummary("Full-text and fuzzy search across processed intakes within the active tenant.")
    .RequireAuthorization();

// --- Cases ---
app.MapGet("/cases/{personKey}", async (string personKey, HttpContext httpContext, DbConnectionFactory dbFactory) =>
{
    var authContext = GetAuthContext(httpContext.User);
    if (authContext is null)
        return Results.Unauthorized();

    var decodedKey = Uri.UnescapeDataString(personKey);

    await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

    var docs = (await session.Connection.QueryAsync<(Guid id, string template_id, string status, DateTimeOffset created_at)>(
        @"
        SELECT d.id, d.template_id, d.status, d.created_at
        FROM documents d
        JOIN case_profiles cp ON cp.document_id = d.id AND cp.tenant_id = d.tenant_id
        WHERE cp.tenant_id = @TenantId
          AND (
            lower(cp.applicant_name) = lower(@PersonKey)
            OR similarity(lower(COALESCE(cp.applicant_name, '')), lower(@PersonKey)) > @CaseThreshold
          )
        ORDER BY d.created_at DESC
        ",
        new { TenantId = authContext.TenantId, PersonKey = decodedKey, CaseThreshold = CaseAggregateSimilarityThreshold },
        session.Transaction)).ToList();

    var docIds = docs.Select(d => d.id).ToArray();
    var allFields = (await session.Connection.QueryAsync<(Guid document_id, string FieldKey, string Value, decimal Confidence, bool RequiresReview)>(
        @"SELECT document_id,
               field_key AS FieldKey,
               COALESCE(corrected_value, extracted_value) AS Value,
               confidence AS Confidence,
               requires_review AS RequiresReview
        FROM extracted_fields
        WHERE document_id = ANY(@DocIds) AND tenant_id = @TenantId",
        new { DocIds = docIds, TenantId = authContext.TenantId },
        session.Transaction)).ToLookup(f => f.document_id);

    var caseDocuments = new List<CaseDocumentItem>(docs.Count);
    foreach (var doc in docs)
    {
        var fields = allFields[doc.id]
            .Select(f => new ConfidenceField(f.FieldKey, f.Value, f.Confidence, f.RequiresReview))
            .ToList();
        caseDocuments.Add(new CaseDocumentItem(doc.id, doc.template_id, doc.status, doc.created_at, fields));
    }

    return Results.Ok(new CaseAggregateResponse(decodedKey, caseDocuments));
})
    .WithName("GetCaseAggregate").WithTags("Cases")
    .WithSummary("Aggregates all documents for a person/case identified by applicant name within the active tenant.")
    .RequireAuthorization();

app.Run();

static string GetCorrelationId(HttpContext httpContext)
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


static bool CanUpload(UserRole role, bool reviewersCanUpload) =>
    role == UserRole.IntakeWorker || (reviewersCanUpload && role == UserRole.Reviewer);

static AuthContext? GetAuthContext(ClaimsPrincipal principal)
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

static string CreateJwt(
    Guid userId,
    string tenantId,
    UserRole role,
    SigningCredentials credentials,
    string issuer,
    string audience,
    TimeSpan lifetime)
{
    var now = DateTime.UtcNow;
    var descriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(AuthClaims.UserId, userId.ToString()),
            new Claim(AuthClaims.TenantId, tenantId),
            new Claim(AuthClaims.Role, role.ToString())
        }),
        NotBefore = now,
        IssuedAt = now,
        Expires = now.Add(lifetime),
        SigningCredentials = credentials,
        Issuer = issuer,
        Audience = audience
    };

    var handler = new JwtSecurityTokenHandler();
    var token = handler.CreateToken(descriptor);
    return handler.WriteToken(token);
}

static bool VerifyPassword(string requestPassword, string passwordHash)
{
    return BCrypt.Net.BCrypt.Verify(requestPassword, passwordHash);
}

static ProcessingStatus ParseStatus(string dbStatus) => dbStatus switch
{
    "uploaded" => ProcessingStatus.Uploaded,
    "extracting" => ProcessingStatus.Extracting,
    "review_ready" => ProcessingStatus.ReviewReady,
    "finalized" => ProcessingStatus.Finalized,
    "failed" => ProcessingStatus.Failed,
    _ => throw new ArgumentException($"Unknown status: {dbStatus}")
};

static IReadOnlyList<TemplateField> ParseTemplateFields(string schemaJson)
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

static string ReadTemplateFieldString(JsonElement field, string propertyName, string defaultValue = "")
{
    if (!field.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        return defaultValue;

    return property.GetString() ?? defaultValue;
}

static bool ReadTemplateFieldBool(JsonElement field, string propertyName)
{
    if (!field.TryGetProperty(propertyName, out var property) ||
        (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False))
        return false;

    return property.GetBoolean();
}

static string BuildTemplateInputName(string key)
{
    if (string.IsNullOrWhiteSpace(key))
        return "field";

    var builder = new StringBuilder();
    var wroteSeparator = false;

    foreach (var ch in key.Trim())
    {
        if (char.IsLetterOrDigit(ch))
        {
            builder.Append(char.ToLowerInvariant(ch));
            wroteSeparator = false;
        }
        else if (!wroteSeparator && builder.Length > 0)
        {
            builder.Append('-');
            wroteSeparator = true;
        }
    }

    var normalized = builder.ToString().Trim('-');
    return string.IsNullOrWhiteSpace(normalized) ? "field" : normalized;
}

static string BuildBlankTemplateHtml(string templateName, IReadOnlyList<TemplateField> fields)
{
    var displayName = WebUtility.HtmlEncode(templateName);
    var fieldRows = new StringBuilder();

    if (fields.Count == 0)
    {
        fieldRows.AppendLine("<p style=\"margin:0;color:#555;\">No fields are defined for this template.</p>");
    }
    else
    {
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            var label = WebUtility.HtmlEncode(field.Key);
            var type = WebUtility.HtmlEncode(field.Type);
            var requiredText = field.Required ? " *" : string.Empty;
            var requiredAttribute = field.Required ? " required" : string.Empty;
            var inputType = field.Type switch
            {
                "date" => "date",
                "integer" => "number",
                "decimal" => "number",
                _ => "text"
            };

            var fieldId = WebUtility.HtmlEncode($"{BuildTemplateInputName(field.Key)}-{index + 1}");
            var inputHint = field.Type.Equals("array", StringComparison.OrdinalIgnoreCase)
                ? "Separate multiple values with commas"
                : string.Empty;

            fieldRows.AppendLine($"    <div style=\"margin-bottom: 14px;\">\n" +
                             $"      <label for=\"{fieldId}\" style=\"font-size:12px;display:block;color:#444;letter-spacing:.02em;margin-bottom:4px;text-transform:uppercase;\">{label}{requiredText} <span style=\"color:#666;font-style:italic;\">({type})</span></label>\n" +
                             $"      <input type=\"{inputType}\" id=\"{fieldId}\" name=\"{fieldId}\"{requiredAttribute} style=\"width:100%;height:36px;border:1px solid #bdbdbd;border-radius:6px;padding:6px 10px;box-sizing:border-box;font-size:14px;\" />\n" +
                             (string.IsNullOrWhiteSpace(inputHint)
                                 ? ""
                                 : $"      <small style=\"color:#666;\">{inputHint}</small>\n") +
                             "    </div>");
        }
    }

    return $$"""
<!doctype html>
<html lang=\"en\">
  <head>
    <meta charset=\"utf-8\" />
    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />
    <title>{{displayName}}</title>
    <style>
      body { font-family: Arial, sans-serif; margin: 0; background: #f7f7f7; }
      .page { width: min(760px, calc(100vw - 24px)); margin: 24px auto; background: #fff; border: 1px solid #ccc; padding: 24px; }
      h1 { margin: 0 0 8px; }
      .field-note { color: #555; font-size: 12px; margin-bottom: 20px; }
      @media print { .print-note { display: none; } }
    </style>
  </head>
  <body>
    <div class=\"page\">
      <h1>{{displayName}}</h1>
      <p class=\"field-note\">Use this blank intake template when collecting values for this form. All fields can be printed as a paper form.</p>
      <form autocomplete=\"off\"> 
{{fieldRows}}\n      </form>
      <p class=\"print-note\" style=\"margin-top:18px;font-size:11px;color:#666;\">Generated for demo usage only.</p>
    </div>
  </body>
</html>
""";
}

static async Task<IReadOnlyList<SimilarCaseItem>> FindSimilarCasesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid sourceDocumentId,
    string tenantId,
    string sourceTemplateId,
    IReadOnlyList<ConfidenceField> sourceFields,
    int limit)
{
    if (!await SupportsCaseProfiles(connection, transaction))
    {
        return [];
    }

    var targetApplicant = sourceFields.FirstOrDefault(f => string.Equals(f.FieldKey, "applicantName", StringComparison.OrdinalIgnoreCase))?.Value;
    var targetDob = sourceFields.FirstOrDefault(f => string.Equals(f.FieldKey, "dateOfBirth", StringComparison.OrdinalIgnoreCase))?.Value;
    var targetAddress = sourceFields.FirstOrDefault(f => string.Equals(f.FieldKey, "address", StringComparison.OrdinalIgnoreCase))?.Value;

    var sourceExists = await connection.ExecuteScalarAsync<int>(
        "SELECT EXISTS(SELECT 1 FROM case_profiles WHERE document_id = @Id AND tenant_id = @TenantId)::int",
        new { Id = sourceDocumentId, TenantId = tenantId },
        transaction);
    if (sourceExists == 0)
    {
        return [];
    }

    var ranked = (await connection.QueryAsync<SimilarCaseCandidate>(
        @"WITH target AS (
            SELECT tenant_id, template_id, search_tsv, search_text, embedding, applicant_name, date_of_birth, address
            FROM case_profiles
            WHERE document_id = @Id AND tenant_id = @TenantId
            LIMIT 1
        ),
        fts AS (
            SELECT cp.document_id, row_number() OVER (
                ORDER BY ts_rank_cd(cp.search_tsv, websearch_to_tsquery('simple', COALESCE(t.search_text, '')))
                    DESC,
                    cp.document_id
            ) AS rank
            FROM case_profiles cp
            CROSS JOIN target t
            WHERE cp.tenant_id = t.tenant_id
              AND cp.document_id <> @Id
              AND cp.search_tsv @@ websearch_to_tsquery('simple', COALESCE(t.search_text, ''))
        ),
        vector AS (
            SELECT cp.document_id, row_number() OVER (
                ORDER BY (cp.embedding <=> t.embedding) ASC,
                         cp.document_id
            ) AS rank
            FROM case_profiles cp
            CROSS JOIN target t
            WHERE cp.tenant_id = t.tenant_id
              AND cp.document_id <> @Id
              AND cp.embedding IS NOT NULL
              AND t.embedding IS NOT NULL
        ),
        name_fuzzy AS (
            SELECT cp.document_id, row_number() OVER (
                ORDER BY similarity(lower(cp.applicant_name), lower(t.applicant_name)) DESC,
                         cp.document_id
            ) AS rank
            FROM case_profiles cp
            CROSS JOIN target t
            WHERE cp.tenant_id = t.tenant_id
              AND cp.document_id <> @Id
              AND cp.applicant_name IS NOT NULL
              AND t.applicant_name IS NOT NULL
              AND similarity(lower(cp.applicant_name), lower(t.applicant_name)) > 0.25
        ),
        address_fuzzy AS (
            SELECT cp.document_id, row_number() OVER (
                ORDER BY similarity(lower(cp.address), lower(t.address)) DESC,
                         cp.document_id
            ) AS rank
            FROM case_profiles cp
            CROSS JOIN target t
            WHERE cp.tenant_id = t.tenant_id
              AND cp.document_id <> @Id
              AND cp.address IS NOT NULL
              AND t.address IS NOT NULL
              AND similarity(lower(cp.address), lower(t.address)) > 0.2
        ),
        dob_exact AS (
            SELECT cp.document_id, 1 AS rank
            FROM case_profiles cp
            CROSS JOIN target t
            WHERE cp.tenant_id = t.tenant_id
              AND cp.document_id <> @Id
              AND cp.date_of_birth IS NOT NULL
              AND t.date_of_birth IS NOT NULL
              AND cp.date_of_birth = t.date_of_birth
        ),
        fused AS (
            SELECT document_id, SUM(1.0 / (60.0 + rank)) AS match_score
            FROM (
                SELECT document_id, rank FROM fts
                UNION ALL
                SELECT document_id, rank FROM vector
                UNION ALL
                SELECT document_id, rank FROM name_fuzzy
                UNION ALL
                SELECT document_id, rank FROM address_fuzzy
                UNION ALL
                SELECT document_id, rank FROM dob_exact
            ) x
            GROUP BY document_id
        )
        SELECT
            cp.document_id AS IntakeId,
            cp.template_id AS TemplateId,
            cp.applicant_name AS ApplicantName,
            cp.date_of_birth AS DateOfBirth,
            cp.address AS Address,
            ROUND(f.match_score::numeric, 4) AS MatchScore
        FROM fused f
        JOIN case_profiles cp ON cp.document_id = f.document_id
        ORDER BY f.match_score DESC, cp.applicant_name NULLS LAST
        LIMIT @Limit;
        ",
        new { Id = sourceDocumentId, TenantId = tenantId, Limit = limit },
        transaction)).ToList();

    if (ranked.Count == 0)
    {
        return [];
    }

    var candidateIds = ranked.Select(r => r.IntakeId).ToArray();
    var fieldsByCase = (await connection.QueryAsync<CaseFieldValue>(
        @"SELECT document_id AS IntakeId,
               field_key AS FieldKey,
               COALESCE(corrected_value, extracted_value) AS Value
        FROM extracted_fields
        WHERE document_id = ANY(@CandidateIds) AND tenant_id = @TenantId
        ",
        new { CandidateIds = candidateIds, TenantId = tenantId },
        transaction))
        .ToLookup(r => r.IntakeId, r => r)
        .ToDictionary(g => g.Key, g => g.ToDictionary(f => f.FieldKey, f => f.Value, StringComparer.OrdinalIgnoreCase));

    var result = new List<SimilarCaseItem>(ranked.Count);
    foreach (var caseItem in ranked)
    {
        var candidateFields = fieldsByCase.GetValueOrDefault(caseItem.IntakeId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var summary = BuildCaseSummary(caseItem, sourceTemplateId, targetApplicant, targetDob, targetAddress, candidateFields);
        result.Add(new SimilarCaseItem(caseItem.IntakeId, caseItem.IntakeId, caseItem.ApplicantName ?? "Unknown applicant", caseItem.TemplateId, caseItem.MatchScore, summary));
    }

    return result;
}

static string BuildCaseSummary(
    SimilarCaseCandidate candidate,
    string sourceTemplateId,
    string? sourceApplicant,
    string? sourceDob,
    string? sourceAddress,
    IReadOnlyDictionary<string, string> candidateFields)
{
    var signals = new List<string>();

    var candidateApplicant = candidate.ApplicantName;
    if (!string.IsNullOrWhiteSpace(candidateApplicant) && string.Equals(candidateApplicant, sourceApplicant, StringComparison.OrdinalIgnoreCase))
    {
        signals.Add("same applicant");
    }

    var candidateDob = candidate.DateOfBirth;
    if (!string.IsNullOrWhiteSpace(candidateDob) && !string.IsNullOrWhiteSpace(sourceDob) && candidateDob == sourceDob)
    {
        signals.Add("matching DOB");
    }

    var candidateAddress = candidate.Address;
    if (!string.IsNullOrWhiteSpace(candidateAddress) && !string.IsNullOrWhiteSpace(sourceAddress) &&
        string.Equals(candidateAddress, sourceAddress, StringComparison.OrdinalIgnoreCase))
    {
        signals.Add("matching address");
    }

    if (string.Equals(candidate.TemplateId, sourceTemplateId, StringComparison.OrdinalIgnoreCase))
    {
        signals.Add("same template");
    }

    if (signals.Count == 0)
    {
        signals.Add("cross-field lexical overlap");
    }

    var requestedServices = candidateFields.TryGetValue("requestedServices", out var services) && !string.IsNullOrWhiteSpace(services)
        ? services
        : candidateFields.TryGetValue("notes", out var notes) && !string.IsNullOrWhiteSpace(notes)
            ? notes
            : "review case context available";

    var evidence = string.Join(", ", signals);
    var maxSnippet = requestedServices.Length > 120 ? requestedServices[..117] + "…" : requestedServices;
    return $"{evidence}; top hints: {maxSnippet}";
}

static async Task<bool> SupportsCaseProfiles(NpgsqlConnection connection, NpgsqlTransaction transaction)
{
    var exists = await connection.ExecuteScalarAsync<int>(
        @"
        SELECT EXISTS(
            SELECT 1
            FROM pg_class
            WHERE relname = 'case_profiles'
              AND relkind = 'r'
        )::int
        ",
        transaction: transaction);

    return exists == 1;
}

file sealed class ApiObservability
{
    private long _requestCount;
    private long _reviewFinalizationCount;

    public void IncrementRequestCount() => Interlocked.Increment(ref _requestCount);

    public void IncrementReviewFinalizationCount() => Interlocked.Increment(ref _reviewFinalizationCount);

    public long RequestCount => Interlocked.Read(ref _requestCount);

    public long ReviewFinalizationCount => Interlocked.Read(ref _reviewFinalizationCount);
}

file sealed record ApiMetricsResponse(
    long RequestCount,
    long ReviewFinalizationCount,
    long ExtractionSuccessCount,
    long ExtractionFailureCount);


file sealed record AuthContext(Guid UserId, string TenantId, UserRole Role);
file sealed record SimilarCaseCandidate(
    Guid IntakeId,
    string TemplateId,
    string? ApplicantName,
    string? DateOfBirth,
    string? Address,
    decimal MatchScore);
file sealed record CaseFieldValue(
    Guid IntakeId,
    string FieldKey,
    string Value);
