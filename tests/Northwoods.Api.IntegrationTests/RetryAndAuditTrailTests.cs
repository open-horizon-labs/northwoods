using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Northwoods.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace Northwoods.Api.IntegrationTests;

/// <summary>
/// Integration tests for the three features added in #110:
///   1. POST /intakes/{id}/retry — reset a failed document back to 'uploaded'
///   2. GET /reviews/{id} failureReason — populated from extraction_failed audit event
///   3. GET /reviews/{id} auditEvents — structured AuditEventItem[] with createdAt and actorEmail
/// </summary>
public sealed class RetryAndAuditTrailTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ITestOutputHelper _output;

    public RetryAndAuditTrailTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ---------------------------------------------------------------------------
    // POST /intakes/{id}/retry
    // ---------------------------------------------------------------------------

    [Trait("Category", "runtime")]
    [Fact]
    public async Task Retry_UnknownDocument_Returns404()
    {
        using var client = IntegrationTestHelpers.CreateClient();
        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(client))
        {
            _output.WriteLine("API runtime not available; skipping.");
            return;
        }

        var reviewerToken = await IntegrationTestHelpers.LoginAsync(client, "tenant-a", "reviewer@sunrise.example", "password");
        using var reviewerClient = IntegrationTestHelpers.CreateClient(reviewerToken);

        using var response = await reviewerClient.PostAsJsonAsync($"/intakes/{Guid.NewGuid()}/retry", new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task Retry_RequiresAuthentication()
    {
        using var client = IntegrationTestHelpers.CreateClient();
        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(client))
        {
            _output.WriteLine("API runtime not available; skipping.");
            return;
        }

        // No token attached — should be 401
        using var response = await client.PostAsJsonAsync($"/intakes/{Guid.NewGuid()}/retry", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task Retry_WorkerRole_Returns403()
    {
        using var client = IntegrationTestHelpers.CreateClient();
        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(client))
        {
            _output.WriteLine("API runtime not available; skipping.");
            return;
        }

        var workerToken = await IntegrationTestHelpers.LoginAsync(client, "tenant-a", "worker@sunrise.example", "password");
        using var workerClient = IntegrationTestHelpers.CreateClient(workerToken);

        // Workers are not allowed to retry — only Reviewers can
        using var response = await workerClient.PostAsJsonAsync($"/intakes/{Guid.NewGuid()}/retry", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task Retry_NonFailedDocument_Returns400()
    {
        using var baselineClient = IntegrationTestHelpers.CreateClient();
        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime not available; skipping.");
            return;
        }

        var workerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "worker@sunrise.example", "password");
        var reviewerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "reviewer@sunrise.example", "password");

        using var workerClient = IntegrationTestHelpers.CreateClient(workerToken);
        using var reviewerClient = IntegrationTestHelpers.CreateClient(reviewerToken);

        var sampleFile = ResolveSampleFile();
        if (!File.Exists(sampleFile))
        {
            _output.WriteLine($"Sample file not found: {sampleFile}; skipping.");
            return;
        }

        // Upload a document — it will be in 'uploaded' state immediately
        var intake = await CreateIntakeAsync(workerClient, sampleFile);
        Assert.Equal(ProcessingStatus.Uploaded, intake.Status);

        // Retry while still in 'uploaded' state must return 400
        using var retryResponse = await reviewerClient.PostAsJsonAsync($"/intakes/{intake.IntakeId}/retry", new { });
        Assert.Equal(HttpStatusCode.BadRequest, retryResponse.StatusCode);

        var body = await retryResponse.Content.ReadAsStringAsync();
        Assert.Contains("failed", body);
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task Retry_FailedDocument_ResetsToUploadedAndWritesAuditEvent()
    {
        using var baselineClient = IntegrationTestHelpers.CreateClient();
        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime not available; skipping.");
            return;
        }

        var workerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "worker@sunrise.example", "password");
        var reviewerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "reviewer@sunrise.example", "password");

        using var workerClient = IntegrationTestHelpers.CreateClient(workerToken);
        using var reviewerClient = IntegrationTestHelpers.CreateClient(reviewerToken);

        var sampleFile = ResolveSampleFile();
        if (!File.Exists(sampleFile))
        {
            _output.WriteLine($"Sample file not found: {sampleFile}; skipping.");
            return;
        }

        var intake = await CreateIntakeAsync(workerClient, sampleFile);
        var intakeId = intake.IntakeId;

        // Wait for the worker to reach a terminal state
        var terminal = await WaitForStatusAsync(workerClient, intakeId);
        if (terminal != ProcessingStatus.Failed)
        {
            _output.WriteLine($"Document reached '{terminal}' (not 'failed'); skipping retry happy-path assertions.");
            return;
        }

        // --- Happy path ---
        using var retryResponse = await reviewerClient.PostAsJsonAsync($"/intakes/{intakeId}/retry", new { });
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        // Status must be back to Uploaded immediately after retry
        var statusAfterRetry = await workerClient.GetFromJsonAsync<IntakeStatusResponse>($"/intakes/{intakeId}");
        Assert.NotNull(statusAfterRetry);
        Assert.Equal(ProcessingStatus.Uploaded, statusAfterRetry.Status);

        // Audit trail returned by GET /reviews/{id} must include retry_requested
        // (reviewer has access to their own review endpoint)
        var review = await reviewerClient.GetFromJsonAsync<ReviewDetailResponse>($"/reviews/{intakeId}");
        Assert.NotNull(review);
        Assert.Contains(review.AuditEvents, e => e.EventType == "retry_requested");

        // The retry_requested event should carry the reviewer's email in actorEmail
        var retryEvent = review.AuditEvents.First(e => e.EventType == "retry_requested");
        Assert.Equal("reviewer@sunrise.example", retryEvent.ActorEmail);
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task Retry_TenantB_CannotRetryTenantADocument()
    {
        using var baselineClient = IntegrationTestHelpers.CreateClient();
        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime not available; skipping.");
            return;
        }

        var tenantAWorkerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "worker@sunrise.example", "password");
        var tenantBReviewerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-b", "reviewer@lakewood.example", "password");

        using var tenantAClient = IntegrationTestHelpers.CreateClient(tenantAWorkerToken);
        using var tenantBClient = IntegrationTestHelpers.CreateClient(tenantBReviewerToken);

        var sampleFile = ResolveSampleFile();
        if (!File.Exists(sampleFile))
        {
            _output.WriteLine($"Sample file not found: {sampleFile}; skipping.");
            return;
        }

        var intake = await CreateIntakeAsync(tenantAClient, sampleFile);

        // Tenant-B reviewer must get 404 — not 403 — so the document ID is not leaked
        using var retryResponse = await tenantBClient.PostAsJsonAsync($"/intakes/{intake.IntakeId}/retry", new { });
        Assert.Equal(HttpStatusCode.NotFound, retryResponse.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // GET /reviews/{id} — failureReason field
    // ---------------------------------------------------------------------------

    [Trait("Category", "runtime")]
    [Fact]
    public async Task ReviewDetail_FailedDocument_ProvidesFailureReason()
    {
        using var baselineClient = IntegrationTestHelpers.CreateClient();
        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime not available; skipping.");
            return;
        }

        var workerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "worker@sunrise.example", "password");
        var reviewerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "reviewer@sunrise.example", "password");

        using var workerClient = IntegrationTestHelpers.CreateClient(workerToken);
        using var reviewerClient = IntegrationTestHelpers.CreateClient(reviewerToken);

        var sampleFile = ResolveSampleFile();
        if (!File.Exists(sampleFile))
        {
            _output.WriteLine($"Sample file not found: {sampleFile}; skipping.");
            return;
        }

        var intake = await CreateIntakeAsync(workerClient, sampleFile);
        var intakeId = intake.IntakeId;

        var terminal = await WaitForStatusAsync(workerClient, intakeId);
        if (terminal != ProcessingStatus.Failed)
        {
            _output.WriteLine($"Document reached '{terminal}' (not 'failed'); skipping failureReason assertions.");
            return;
        }

        var review = await reviewerClient.GetFromJsonAsync<ReviewDetailResponse>($"/reviews/{intakeId}");
        Assert.NotNull(review);
        Assert.Equal(ProcessingStatus.Failed, review.Status);

        // When the document is 'failed' and an extraction_failed audit event exists,
        // failureReason must be a non-empty string (the error text from the event details).
        // If no extraction_failed event was written, the field is allowed to be null,
        // but the review itself must still be returned.
        if (review.FailureReason is not null)
        {
            Assert.NotEmpty(review.FailureReason);
        }
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task ReviewDetail_NonFailedDocument_FailureReasonIsNull()
    {
        using var baselineClient = IntegrationTestHelpers.CreateClient();
        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime not available; skipping.");
            return;
        }

        var workerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "worker@sunrise.example", "password");
        var reviewerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "reviewer@sunrise.example", "password");

        using var workerClient = IntegrationTestHelpers.CreateClient(workerToken);
        using var reviewerClient = IntegrationTestHelpers.CreateClient(reviewerToken);

        var sampleFile = ResolveSampleFile();
        if (!File.Exists(sampleFile))
        {
            _output.WriteLine($"Sample file not found: {sampleFile}; skipping.");
            return;
        }

        var intake = await CreateIntakeAsync(workerClient, sampleFile);
        var intakeId = intake.IntakeId;

        var terminal = await WaitForStatusAsync(workerClient, intakeId);
        if (terminal != ProcessingStatus.ReviewReady && terminal != ProcessingStatus.Finalized)
        {
            _output.WriteLine($"Document reached '{terminal}' (not review_ready/finalized); skipping null-failureReason assertions.");
            return;
        }

        var review = await reviewerClient.GetFromJsonAsync<ReviewDetailResponse>($"/reviews/{intakeId}");
        Assert.NotNull(review);

        // failureReason must be null for non-failed documents — the backend only populates
        // it when doc.status == "failed"
        Assert.Null(review.FailureReason);
    }

    // ---------------------------------------------------------------------------
    // GET /reviews/{id} — AuditEventItem shape
    // ---------------------------------------------------------------------------

    [Trait("Category", "runtime")]
    [Fact]
    public async Task ReviewDetail_AuditEvents_HaveStructuredShape()
    {
        using var baselineClient = IntegrationTestHelpers.CreateClient();
        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime not available; skipping.");
            return;
        }

        var workerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "worker@sunrise.example", "password");
        var reviewerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "reviewer@sunrise.example", "password");

        using var workerClient = IntegrationTestHelpers.CreateClient(workerToken);
        using var reviewerClient = IntegrationTestHelpers.CreateClient(reviewerToken);

        var sampleFile = ResolveSampleFile();
        if (!File.Exists(sampleFile))
        {
            _output.WriteLine($"Sample file not found: {sampleFile}; skipping.");
            return;
        }

        var intake = await CreateIntakeAsync(workerClient, sampleFile);
        var intakeId = intake.IntakeId;

        // Wait for the worker to produce at least one audit event beyond intake_uploaded
        await WaitForStatusAsync(workerClient, intakeId);

        var review = await reviewerClient.GetFromJsonAsync<ReviewDetailResponse>($"/reviews/{intakeId}");
        Assert.NotNull(review);

        // Must have at least the intake_uploaded event
        Assert.NotEmpty(review.AuditEvents);

        foreach (var evt in review.AuditEvents)
        {
            // EventType must be a non-empty string
            Assert.False(string.IsNullOrWhiteSpace(evt.EventType),
                "AuditEventItem.EventType must not be empty");

            // CreatedAt must be a real timestamp (not default/zero)
            Assert.NotEqual(default, evt.CreatedAt);
            Assert.True(evt.CreatedAt.Year >= 2024,
                $"AuditEventItem.CreatedAt looks implausible: {evt.CreatedAt}");

            // ActorEmail: system-generated events have null; user-triggered ones have an email
            if (evt.ActorEmail is not null)
            {
                Assert.Contains("@", evt.ActorEmail);
            }
        }

        // The intake_uploaded event is always written by the worker who uploaded the file,
        // so its actorEmail must be the worker's email
        var uploadEvent = review.AuditEvents.FirstOrDefault(e => e.EventType == "intake_uploaded");
        Assert.NotNull(uploadEvent);
        Assert.Equal("worker@sunrise.example", uploadEvent.ActorEmail);
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task ReviewDetail_AuditEvents_AreChronologicallyOrdered()
    {
        using var baselineClient = IntegrationTestHelpers.CreateClient();
        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime not available; skipping.");
            return;
        }

        var workerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "worker@sunrise.example", "password");
        var reviewerToken = await IntegrationTestHelpers.LoginAsync(baselineClient, "tenant-a", "reviewer@sunrise.example", "password");

        using var workerClient = IntegrationTestHelpers.CreateClient(workerToken);
        using var reviewerClient = IntegrationTestHelpers.CreateClient(reviewerToken);

        var sampleFile = ResolveSampleFile();
        if (!File.Exists(sampleFile))
        {
            _output.WriteLine($"Sample file not found: {sampleFile}; skipping.");
            return;
        }

        var intake = await CreateIntakeAsync(workerClient, sampleFile);
        var intakeId = intake.IntakeId;

        await WaitForStatusAsync(workerClient, intakeId);

        var review = await reviewerClient.GetFromJsonAsync<ReviewDetailResponse>($"/reviews/{intakeId}");
        Assert.NotNull(review);

        // Audit events must be in ascending chronological order
        var events = review.AuditEvents.ToList();
        for (var i = 1; i < events.Count; i++)
        {
            Assert.True(events[i].CreatedAt >= events[i - 1].CreatedAt,
                $"AuditEvent at index {i} ({events[i].EventType} @ {events[i].CreatedAt}) " +
                $"is earlier than event at index {i - 1} ({events[i - 1].EventType} @ {events[i - 1].CreatedAt})");
        }

        // intake_uploaded must be the first event
        Assert.Equal("intake_uploaded", events[0].EventType);
    }

    // ---------------------------------------------------------------------------
    // Helpers (duplicated from WorkflowIntegrationTests to keep the class self-contained)
    // ---------------------------------------------------------------------------

    private static async Task<CreateIntakeResponse> CreateIntakeAsync(HttpClient client, string sampleFile)
    {
        using var fileStream = File.OpenRead(sampleFile);
        using var form = new MultipartFormDataContent
        {
            { new StreamContent(fileStream), "file", Path.GetFileName(sampleFile) },
            { new StringContent("general-assistance"), "templateId" }
        };

        using var response = await client.PostAsync("/intakes", form);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CreateIntakeResponse>();
        if (payload is null)
            throw new InvalidOperationException("Intake response payload was empty.");

        return payload;
    }

    private static async Task<ProcessingStatus> WaitForStatusAsync(HttpClient client, Guid intakeId)
    {
        var deadline = DateTimeOffset.UtcNow + DefaultTimeout;
        ProcessingStatus last = ProcessingStatus.Uploaded;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await client.GetAsync($"/intakes/{intakeId}");
            if (response.IsSuccessStatusCode)
            {
                var status = await response.Content.ReadFromJsonAsync<IntakeStatusResponse>();
                if (status is not null)
                {
                    last = status.Status;
                    if (status.Status is ProcessingStatus.ReviewReady or ProcessingStatus.Failed or ProcessingStatus.Finalized)
                        return status.Status;
                }
            }

            await Task.Delay(PollInterval);
        }

        return last;
    }

    private static string ResolveSampleFile()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        for (var i = 0; i < 8 && current is not null; i++)
        {
            var candidate = Path.Combine(current.FullName, "samples", "intakes", "blank-general-assistance.pdf");
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine("samples", "intakes", "blank-general-assistance.pdf"));
    }
}
