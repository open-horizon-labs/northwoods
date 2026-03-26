using Northwoods.Contracts;
using Northwoods.Tenancy;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck("postgres", new PostgresHealthCheck(connectionString));

var app = builder.Build();

app.MapOpenApi();
app.MapHealthChecks("/healthz");

// --- Auth ---

app.MapPost("/auth/login", (LoginRequest request) =>
{
    var token = $"dev-token::{request.TenantId}::{request.Role}";
    return Results.Ok(new LoginResponse(token, request.TenantId, request.Role));
})
.WithName("Login")
.WithTags("Auth")
.WithSummary("Issues a development login token for the selected tenant and role.");

// --- Intakes ---

app.MapPost("/intakes", (CreateIntakeRequest request, HttpRequest httpRequest) =>
{
    var tenantId = httpRequest.Headers[TenantHeaders.TenantId].ToString();
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.BadRequest(new { error = "Missing tenant header." });

    var intakeId = Guid.NewGuid();
    return Results.Accepted($"/intakes/{intakeId}", new CreateIntakeResponse(intakeId, ProcessingStatus.Extracting));
})
.WithName("CreateIntake")
.WithTags("Intakes")
.WithSummary("Creates an intake and starts the extraction workflow.");

app.MapGet("/intakes/{id:guid}", (Guid id, HttpRequest httpRequest) =>
{
    var tenantId = httpRequest.Headers[TenantHeaders.TenantId].ToString();
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.BadRequest(new { error = "Missing tenant header." });

    var response = new IntakeStatusResponse(
        id,
        tenantId,
        "general-assistance",
        ProcessingStatus.ReviewReady,
        [
            new ConfidenceField("applicantName", "Jamie Carter", 0.98m, false),
            new ConfidenceField("householdSize", "4", 0.92m, false),
            new ConfidenceField("monthlyIncome", "$1,850", 0.61m, true)
        ]);

    return Results.Ok(response);
})
.WithName("GetIntake")
.WithTags("Intakes")
.WithSummary("Returns intake processing state and extracted draft fields.");

// --- Reviews ---

app.MapGet("/review-queue", (HttpRequest httpRequest) =>
{
    var tenantId = httpRequest.Headers[TenantHeaders.TenantId].ToString();
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.BadRequest(new { error = "Missing tenant header." });

    ReviewQueueItem[] queue =
    [
        new(Guid.NewGuid(), Guid.NewGuid(), "Jamie Carter", "general-assistance", 1)
    ];

    return Results.Ok(queue);
})
.WithName("GetReviewQueue")
.WithTags("Reviews")
.WithSummary("Returns the reviewer work queue for the active tenant.");

app.MapGet("/reviews/{id:guid}", (Guid id, HttpRequest httpRequest) =>
{
    var tenantId = httpRequest.Headers[TenantHeaders.TenantId].ToString();
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.BadRequest(new { error = "Missing tenant header." });

    var review = new ReviewDetailResponse(
        id,
        Guid.NewGuid(),
        tenantId,
        "general-assistance",
        "/objects/dev/intakes/sample.pdf",
        ProcessingStatus.ReviewReady,
        [
            new ConfidenceField("applicantName", "Jamie Carter", 0.98m, false),
            new ConfidenceField("monthlyIncome", "$1,850", 0.61m, true)
        ],
        [
            "Intake uploaded",
            "Extraction completed",
            "Review task created"
        ]);

    return Results.Ok(review);
})
.WithName("GetReview")
.WithTags("Reviews")
.WithSummary("Returns the document, extracted fields, and audit trail for a review task.");

app.MapPost("/reviews/{id:guid}/finalize", (Guid id, FinalizeReviewRequest request, HttpRequest httpRequest) =>
{
    var tenantId = httpRequest.Headers[TenantHeaders.TenantId].ToString();
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.BadRequest(new { error = "Missing tenant header." });

    return Results.Ok(new FinalizeReviewResponse(id, ProcessingStatus.Finalized));
})
.WithName("FinalizeReview")
.WithTags("Reviews")
.WithSummary("Persists reviewer corrections and finalizes the intake record.");

app.Run();
