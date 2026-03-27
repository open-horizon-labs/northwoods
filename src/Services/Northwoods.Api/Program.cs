using Dapper;
using Npgsql;
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
    builder.Configuration["Minio:BucketName"] ?? "intakes",
    builder.Configuration["Minio:PublicEndpoint"]);

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

    var similarCases = await FindSimilarCasesAsync(session.Connection, session.Transaction, id, tenantId, doc.template_id, fields, 5);

    var sourceUrl = objectStore.GetPresignedUrl(doc.original_file_key, TimeSpan.FromMinutes(15));
    var status = ParseStatus(doc.status);

    return Results.Ok(new ReviewDetailResponse(doc.id, doc.id, doc.tenant_id, doc.template_id, sourceUrl, status, fields, similarCases, auditEvents));
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
        """
        WITH target AS (
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
        """,
        new { Id = sourceDocumentId, TenantId = tenantId, Limit = limit },
        transaction)).ToList();

    if (ranked.Count == 0)
    {
        return [];
    }

    var candidateIds = ranked.Select(r => r.IntakeId).ToArray();
    var fieldsByCase = (await connection.QueryAsync<CaseFieldValue>(
        """
        SELECT document_id AS IntakeId,
               field_key AS FieldKey,
               COALESCE(corrected_value, extracted_value) AS Value
        FROM extracted_fields
        WHERE document_id = ANY(@CandidateIds)
        """,
        new { CandidateIds = candidateIds },
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
        """
        SELECT EXISTS(
            SELECT 1
            FROM pg_class
            WHERE relname = 'case_profiles'
              AND relkind = 'r'
        )::int
        """,
        transaction: transaction);

    return exists == 1;
}

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