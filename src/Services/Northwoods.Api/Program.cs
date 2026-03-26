using Dapper;
using Northwoods.Contracts;
using Northwoods.Tenancy;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

var db = new DbConnectionFactory(connectionString);
var store = new ObjectStore(
    builder.Configuration["Minio:Endpoint"] ?? "localhost:9000",
    builder.Configuration["Minio:AccessKey"] ?? "northwoods",
    builder.Configuration["Minio:SecretKey"] ?? "northwoods",
    builder.Configuration["Minio:BucketName"] ?? "intakes");

builder.Services.AddSingleton(db);
builder.Services.AddSingleton(store);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck("postgres", new PostgresHealthCheck(connectionString));

var app = builder.Build();

// Ensure the MinIO bucket exists on startup
await store.EnsureBucketAsync();

app.MapOpenApi();
app.MapHealthChecks("/healthz");

// --- Helpers ---

static string? TenantId(HttpRequest r) =>
    r.Headers[TenantHeaders.TenantId].ToString() is { Length: > 0 } v ? v : null;

static ProcessingStatus ParseStatus(string dbStatus) => dbStatus switch
{
    "uploaded" => ProcessingStatus.Uploaded,
    "extracting" => ProcessingStatus.Extracting,
    "review_ready" => ProcessingStatus.ReviewReady,
    "finalized" => ProcessingStatus.Finalized,
    "failed" => ProcessingStatus.Failed,
    _ => throw new ArgumentException($"Unknown status: {dbStatus}")
};

// --- Auth ---

app.MapPost("/auth/login", async (LoginRequest request, DbConnectionFactory dbFactory) =>
{
    await using var session = await dbFactory.OpenSessionAsync(request.TenantId);
    var user = await session.Connection.QueryFirstOrDefaultAsync<(Guid id, string role)>(
        "SELECT id, role FROM users WHERE email = @Email AND tenant_id = @TenantId",
        new { request.Email, request.TenantId },
        session.Transaction);

    if (user == default)
        return Results.Unauthorized();

    var token = $"dev-token::{request.TenantId}::{user.role}::{user.id}";
    var role = Enum.Parse<UserRole>(user.role);
    return Results.Ok(new LoginResponse(token, request.TenantId, role));
})
.WithName("Login").WithTags("Auth")
.WithSummary("Authenticates a user and returns a dev token with tenant context.");

// --- Intakes ---

app.MapPost("/intakes", async (HttpRequest httpRequest, DbConnectionFactory dbFactory, ObjectStore objectStore) =>
{
    var tenantId = TenantId(httpRequest);
    if (tenantId is null) return Results.BadRequest(new { error = "Missing tenant header." });

    var form = await httpRequest.ReadFormAsync();
    var file = form.Files["file"];
    var templateId = form["templateId"].ToString();

    if (file is null || string.IsNullOrWhiteSpace(templateId))
        return Results.BadRequest(new { error = "file and templateId are required." });

    // Get the acting user (for now, use the first intake worker for this tenant)
    await using var session = await dbFactory.OpenSessionAsync(tenantId);
    var userId = await session.Connection.QueryFirstOrDefaultAsync<Guid>(
        "SELECT id FROM users WHERE tenant_id = @tenantId AND role = 'IntakeWorker' LIMIT 1",
        new { tenantId },
        session.Transaction);

    if (userId == default)
        return Results.Unauthorized();

    // Upload to MinIO
    var docId = Guid.NewGuid();
    var fileKey = $"{tenantId}/{docId}/{file.FileName}";
    await using var stream = file.OpenReadStream();
    await objectStore.UploadAsync(fileKey, stream, file.ContentType ?? "application/octet-stream");

    // Insert document record
    await session.Connection.ExecuteAsync(
        """
        INSERT INTO documents (id, tenant_id, template_id, uploaded_by, original_file_key, original_file_name, status)
        VALUES (@Id, @TenantId, @TemplateId, @UploadedBy, @FileKey, @FileName, 'uploaded')
        """,
        new { Id = docId, TenantId = tenantId, TemplateId = templateId, UploadedBy = userId, FileKey = fileKey, FileName = file.FileName },
        session.Transaction);

    // Audit
    await session.Connection.ExecuteAsync(
        """
        INSERT INTO audit_events (document_id, tenant_id, event_type, actor_id, details)
        VALUES (@DocId, @TenantId, 'intake_uploaded', @ActorId, @Details::jsonb)
        """,
        new { DocId = docId, TenantId = tenantId, ActorId = userId, Details = $"{{\"template\":\"{templateId}\",\"file\":\"{file.FileName}\"}}" },
        session.Transaction);

    await session.CommitAsync();
    return Results.Accepted($"/intakes/{docId}", new CreateIntakeResponse(docId, ProcessingStatus.Uploaded));
})
.WithName("CreateIntake").WithTags("Intakes")
.WithSummary("Uploads a document and creates an intake record.")
.DisableAntiforgery();

app.MapGet("/intakes/{id:guid}", async (Guid id, HttpRequest httpRequest, DbConnectionFactory dbFactory) =>
{
    var tenantId = TenantId(httpRequest);
    if (tenantId is null) return Results.BadRequest(new { error = "Missing tenant header." });

    await using var session = await dbFactory.OpenSessionAsync(tenantId);

    var doc = await session.Connection.QueryFirstOrDefaultAsync<(Guid id, string tenant_id, string template_id, string status)>(
        "SELECT id, tenant_id, template_id, status FROM documents WHERE id = @Id",
        new { Id = id },
        session.Transaction);

    if (doc == default) return Results.NotFound();

    var fields = (await session.Connection.QueryAsync<ConfidenceField>(
        """
        SELECT field_key AS FieldKey,
               COALESCE(corrected_value, extracted_value) AS Value,
               confidence AS Confidence,
               requires_review AS RequiresReview
        FROM extracted_fields WHERE document_id = @Id
        """,
        new { Id = id },
        session.Transaction)).ToList();

    var status = ParseStatus(doc.status);
    return Results.Ok(new IntakeStatusResponse(doc.id, doc.tenant_id, doc.template_id, status, fields));
})
.WithName("GetIntake").WithTags("Intakes")
.WithSummary("Returns intake processing state and extracted draft fields.");

// --- Reviews ---

app.MapGet("/review-queue", async (HttpRequest httpRequest, DbConnectionFactory dbFactory) =>
{
    var tenantId = TenantId(httpRequest);
    if (tenantId is null) return Results.BadRequest(new { error = "Missing tenant header." });

    await using var session = await dbFactory.OpenSessionAsync(tenantId);

    var items = (await session.Connection.QueryAsync<ReviewQueueItem>(
        """
        SELECT d.id AS ReviewId, d.id AS IntakeId,
               COALESCE((SELECT ef.extracted_value FROM extracted_fields ef WHERE ef.document_id = d.id AND ef.field_key = 'applicantName' LIMIT 1), '(unknown)') AS ApplicantName,
               d.template_id AS TemplateId,
               (SELECT COUNT(*)::int FROM extracted_fields ef WHERE ef.document_id = d.id AND ef.requires_review) AS UncertainFieldCount
        FROM documents d
        WHERE d.status = 'review_ready'
        ORDER BY d.created_at
        """,
        transaction: session.Transaction)).ToList();

    return Results.Ok(items);
})
.WithName("GetReviewQueue").WithTags("Reviews")
.WithSummary("Returns documents awaiting review for the active tenant.");

app.MapGet("/reviews/{id:guid}", async (Guid id, HttpRequest httpRequest, DbConnectionFactory dbFactory, ObjectStore objectStore) =>
{
    var tenantId = TenantId(httpRequest);
    if (tenantId is null) return Results.BadRequest(new { error = "Missing tenant header." });

    await using var session = await dbFactory.OpenSessionAsync(tenantId);

    var doc = await session.Connection.QueryFirstOrDefaultAsync<(Guid id, string tenant_id, string template_id, string status, string original_file_key)>(
        "SELECT id, tenant_id, template_id, status, original_file_key FROM documents WHERE id = @Id",
        new { Id = id },
        session.Transaction);

    if (doc == default) return Results.NotFound();

    var fields = (await session.Connection.QueryAsync<ConfidenceField>(
        """
        SELECT field_key AS FieldKey,
               COALESCE(corrected_value, extracted_value) AS Value,
               confidence AS Confidence,
               requires_review AS RequiresReview
        FROM extracted_fields WHERE document_id = @Id
        """,
        new { Id = id },
        session.Transaction)).ToList();

    var auditEvents = (await session.Connection.QueryAsync<string>(
        "SELECT event_type FROM audit_events WHERE document_id = @Id ORDER BY created_at",
        new { Id = id },
        session.Transaction)).ToList();

    var sourceUrl = objectStore.GetPresignedUrl(doc.original_file_key, TimeSpan.FromMinutes(15));
    var status = ParseStatus(doc.status);

    return Results.Ok(new ReviewDetailResponse(doc.id, doc.id, doc.tenant_id, doc.template_id, sourceUrl, status, fields, auditEvents));
})
.WithName("GetReview").WithTags("Reviews")
.WithSummary("Returns the document, extracted fields, and audit trail for a review task.");

app.MapPost("/reviews/{id:guid}/finalize", async (Guid id, FinalizeReviewRequest request, HttpRequest httpRequest, DbConnectionFactory dbFactory) =>
{
    var tenantId = TenantId(httpRequest);
    if (tenantId is null) return Results.BadRequest(new { error = "Missing tenant header." });

    await using var session = await dbFactory.OpenSessionAsync(tenantId);

    var doc = await session.Connection.QueryFirstOrDefaultAsync<(Guid id, string status)>(
        "SELECT id, status FROM documents WHERE id = @Id",
        new { Id = id },
        session.Transaction);

    if (doc == default) return Results.NotFound();
    if (doc.status != "review_ready")
        return Results.BadRequest(new { error = $"Document is in '{doc.status}' state, not review_ready." });

    // Apply corrections
    foreach (var field in request.Fields)
    {
        await session.Connection.ExecuteAsync(
            """
            UPDATE extracted_fields
            SET corrected_value = @Value, requires_review = false, updated_at = now()
            WHERE document_id = @DocId AND field_key = @FieldKey
            """,
            new { DocId = id, field.FieldKey, field.Value },
            session.Transaction);
    }

    // Finalize
    await session.Connection.ExecuteAsync(
        "UPDATE documents SET status = 'finalized', updated_at = now() WHERE id = @Id",
        new { Id = id },
        session.Transaction);

    // Audit
    await session.Connection.ExecuteAsync(
        """
        INSERT INTO audit_events (document_id, tenant_id, event_type, details)
        VALUES (@DocId, @TenantId, 'finalized', @Details::jsonb)
        """,
        new { DocId = id, TenantId = tenantId, Details = $"{{\"note\":\"{request.ReviewerNote}\"}}" },
        session.Transaction);

    await session.CommitAsync();
    return Results.Ok(new FinalizeReviewResponse(id, ProcessingStatus.Finalized));
})
.WithName("FinalizeReview").WithTags("Reviews")
.WithSummary("Persists reviewer corrections and finalizes the intake record.");

app.Run();
