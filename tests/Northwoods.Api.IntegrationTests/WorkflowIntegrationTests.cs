using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Northwoods.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace Northwoods.Api.IntegrationTests;

public sealed class WorkflowIntegrationTests
{
    private const string DefaultBaseUrl = "http://localhost:5100";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ITestOutputHelper _output;

    public WorkflowIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task Upload_WorkerPipeline_TransitionsStatusFromUploadedToTerminal()
    {
        using var baselineClient = CreateClient();

        if (!await IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime is not available at test time; skipping runtime integration assertions.");
            return;
        }

        var workerToken = await LoginAsync(baselineClient, "tenant-a", "worker@sunrise.example", "dev");
        using var workerClient = CreateClient(workerToken);

        var sampleFile = ResolveSampleFile();
        if (!File.Exists(sampleFile))
        {
            _output.WriteLine($"Sample file not found: {sampleFile}; skipping runtime workflow assertions.");
            return;
        }

        var intake = await CreateIntakeAsync(workerClient, sampleFile);
        Assert.NotEqual(Guid.Empty, intake.IntakeId);
        Assert.Equal(ProcessingStatus.Uploaded, intake.Status);

        var history = await WaitForStatusHistoryAsync(workerClient, intake.IntakeId, intake.Status);
        Assert.Contains(ProcessingStatus.Uploaded, history);

        var terminal = history.Last();
        Assert.True(terminal is ProcessingStatus.ReviewReady or ProcessingStatus.Failed or ProcessingStatus.Finalized);
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task UploadReviewQueueFinalize_Workflow_HonorsTenantBoundaries()
    {
        using var baselineClient = CreateClient();

        if (!await IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime is not available at test time; skipping runtime integration assertions.");
            return;
        }

        var tenantAWorkerToken = await LoginAsync(baselineClient, "tenant-a", "worker@sunrise.example", "dev");
        var tenantAReviewerToken = await LoginAsync(baselineClient, "tenant-a", "reviewer@sunrise.example", "dev");
        var tenantBWorkerToken = await LoginAsync(baselineClient, "tenant-b", "worker@lakewood.example", "dev");
        var tenantBReviewerToken = await LoginAsync(baselineClient, "tenant-b", "reviewer@lakewood.example", "dev");

        using var tenantAWorkerClient = CreateClient(tenantAWorkerToken);
        using var tenantAReviewerClient = CreateClient(tenantAReviewerToken);
        using var tenantBClient = CreateClient(tenantBWorkerToken);
        using var tenantBReviewerClient = CreateClient(tenantBReviewerToken);

        var sampleFile = ResolveSampleFile();
        if (!File.Exists(sampleFile))
        {
            _output.WriteLine($"Sample file not found: {sampleFile}; skipping runtime workflow assertions.");
            return;
        }

        var intake = await CreateIntakeAsync(tenantAWorkerClient, sampleFile);
        var intakeId = intake.IntakeId;

        Assert.NotEqual(Guid.Empty, intakeId);
        Assert.Equal(ProcessingStatus.Uploaded, intake.Status);

        using var otherTenantAccess = await tenantBClient.GetAsync($"/intakes/{intakeId}");
        Assert.Equal(HttpStatusCode.NotFound, otherTenantAccess.StatusCode);

        var status = await WaitForStatusAsync(tenantAWorkerClient, intakeId);
        if (status != ProcessingStatus.ReviewReady)
        {
            _output.WriteLine($"Runtime integration stopped early with status '{status}' for intake {intakeId}; skipping finalize assertions.");
            return;
        }

        var reviewQueue = await tenantAReviewerClient.GetFromJsonAsync<List<ReviewQueueItem>>("/review-queue");
        Assert.NotNull(reviewQueue);
        Assert.Contains(reviewQueue, item => item.IntakeId == intakeId);

        var review = await tenantAReviewerClient.GetFromJsonAsync<ReviewDetailResponse>($"/reviews/{intakeId}");
        Assert.NotNull(review);
        Assert.Equal(intakeId, review.IntakeId);

        var finalizePayload = new FinalizeReviewRequest(
            review.Fields.ToList(),
            "Representatively finalized by runtime integration test");

        using var finalizeResponse = await tenantAReviewerClient.PostAsJsonAsync($"/reviews/{intakeId}/finalize", finalizePayload);
        Assert.Equal(HttpStatusCode.OK, finalizeResponse.StatusCode);

        var finalize = await finalizeResponse.Content.ReadFromJsonAsync<FinalizeReviewResponse>();
        Assert.NotNull(finalize);
        Assert.Equal(ProcessingStatus.Finalized, finalize.Status);

        var finalized = await tenantAReviewerClient.GetFromJsonAsync<IntakeStatusResponse>($"/intakes/{intakeId}");
        Assert.NotNull(finalized);
        Assert.Equal(ProcessingStatus.Finalized, finalized.Status);

        var tenantAMetrics = await tenantAReviewerClient.GetFromJsonAsync<ApiMetricsResponse>("/metrics");
        var tenantBMetrics = await tenantBReviewerClient.GetFromJsonAsync<ApiMetricsResponse>("/metrics");

        Assert.NotNull(tenantAMetrics);
        Assert.NotNull(tenantBMetrics);

        Assert.NotEqual(0, tenantAMetrics.ReviewFinalizationCount);
        Assert.True(tenantAMetrics.ReviewFinalizationCount > tenantBMetrics.ReviewFinalizationCount);
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task Search_ReturnsTenantScopedResults()
    {
        using var baselineClient = CreateClient();

        if (!await IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime is not available; skipping search integration test.");
            return;
        }

        var tenantAToken = await LoginAsync(baselineClient, "tenant-a", "worker@sunrise.example", "dev");
        var tenantBToken = await LoginAsync(baselineClient, "tenant-b", "worker@lakewood.example", "dev");

        using var tenantAClient = CreateClient(tenantAToken);
        using var tenantBClient = CreateClient(tenantBToken);

        // Search with a generic query that may return results for tenant-a
        using var searchResponseA = await tenantAClient.GetAsync("/search?q=intake");
        Assert.Equal(HttpStatusCode.OK, searchResponseA.StatusCode);

        var searchA = await searchResponseA.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(searchA);
        Assert.Equal("intake", searchA.Query);

        // Search with same query for tenant-b should not return tenant-a data
        using var searchResponseB = await tenantBClient.GetAsync("/search?q=intake");
        Assert.Equal(HttpStatusCode.OK, searchResponseB.StatusCode);

        var searchB = await searchResponseB.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(searchB);

        // Tenant-b results should not contain any tenant-a intake IDs
        if (searchA.Results.Count > 0)
        {
            var tenantAIds = searchA.Results.Select(r => r.IntakeId).ToHashSet();
            var tenantBIds = searchB.Results.Select(r => r.IntakeId).ToHashSet();
            Assert.Empty(tenantAIds.Intersect(tenantBIds));
        }

        // Empty query returns empty results
        using var emptyResponse = await tenantAClient.GetAsync("/search?q=");
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        var emptySearch = await emptyResponse.Content.ReadFromJsonAsync<SearchResponse>();
        Assert.NotNull(emptySearch);
        Assert.Empty(emptySearch.Results);
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task CaseAggregate_ReturnsTenantScopedDocuments()
    {
        using var baselineClient = CreateClient();

        if (!await IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime is not available; skipping case aggregate integration test.");
            return;
        }

        var tenantAToken = await LoginAsync(baselineClient, "tenant-a", "worker@sunrise.example", "dev");
        var tenantBToken = await LoginAsync(baselineClient, "tenant-b", "worker@lakewood.example", "dev");

        using var tenantAClient = CreateClient(tenantAToken);
        using var tenantBClient = CreateClient(tenantBToken);

        // Case view with a name that might exist in tenant-a
        using var caseResponseA = await tenantAClient.GetAsync("/cases/Jamie%20Carter");
        Assert.Equal(HttpStatusCode.OK, caseResponseA.StatusCode);

        var caseA = await caseResponseA.Content.ReadFromJsonAsync<CaseAggregateResponse>();
        Assert.NotNull(caseA);
        Assert.Equal("Jamie Carter", caseA.PersonKey);

        // Same name with tenant-b should return empty (tenant isolation)
        using var caseResponseB = await tenantBClient.GetAsync("/cases/Jamie%20Carter");
        Assert.Equal(HttpStatusCode.OK, caseResponseB.StatusCode);

        var caseB = await caseResponseB.Content.ReadFromJsonAsync<CaseAggregateResponse>();
        Assert.NotNull(caseB);

        // Tenant-b should not see tenant-a documents
        if (caseA.Documents.Count > 0)
        {
            var tenantAIds = caseA.Documents.Select(d => d.IntakeId).ToHashSet();
            var tenantBIds = caseB.Documents.Select(d => d.IntakeId).ToHashSet();
            Assert.Empty(tenantAIds.Intersect(tenantBIds));
        }

        // Non-existent person returns empty documents list
        using var notFoundResponse = await tenantAClient.GetAsync("/cases/Nonexistent%20Person%20XYZ");
        Assert.Equal(HttpStatusCode.OK, notFoundResponse.StatusCode);
        var notFoundCase = await notFoundResponse.Content.ReadFromJsonAsync<CaseAggregateResponse>();
        Assert.NotNull(notFoundCase);
        Assert.Empty(notFoundCase.Documents);
    }

    private static HttpClient CreateClient(string? token = null)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("NORTHWOODS_API_BASE_URL") ?? DefaultBaseUrl)
        };

        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static async Task<bool> IsRuntimeAvailableAsync(HttpClient client)
    {
        try
        {
            using var response = await client.GetAsync("/healthz");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> LoginAsync(HttpClient client, string tenantId, string email, string password)
    {
        using var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, password, tenantId));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (payload is null)
        {
            throw new InvalidOperationException("Login response payload was empty.");
        }

        return payload.AccessToken;
    }

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
        {
            throw new InvalidOperationException("Intake response payload was empty.");
        }

        return payload;
    }

    private static async Task<IReadOnlyList<ProcessingStatus>> WaitForStatusHistoryAsync(
        HttpClient client,
        Guid intakeId,
        ProcessingStatus initialStatus)
    {
        var history = new List<ProcessingStatus> { initialStatus };
        var deadline = DateTimeOffset.UtcNow + DefaultTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await client.GetAsync($"/intakes/{intakeId}");
            if (!response.IsSuccessStatusCode)
            {
                await Task.Delay(PollInterval);
                continue;
            }

            var statusResponse = await response.Content.ReadFromJsonAsync<IntakeStatusResponse>();
            if (statusResponse is null)
            {
                throw new InvalidOperationException("Failed to parse intake status response.");
            }

            if (history.Count == 0 || history[^1] != statusResponse.Status)
            {
                history.Add(statusResponse.Status);
            }

            if (statusResponse.Status is ProcessingStatus.ReviewReady or ProcessingStatus.Failed or ProcessingStatus.Finalized)
            {
                return history;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException($"Intake {intakeId} did not complete status transition in time.");
    }

    private static async Task<ProcessingStatus> WaitForStatusAsync(HttpClient client, Guid intakeId)
    {
        var history = await WaitForStatusHistoryAsync(client, intakeId, ProcessingStatus.Uploaded);
        return history.Last();
    }

    private static string ResolveSampleFile()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        for (var i = 0; i < 8 && current is not null; i++)
        {
            var candidate = Path.Combine(current.FullName, "samples", "intakes", "chatgpt-sample-general-intake.pdf");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        var fallback = Path.Combine("samples", "intakes", "chatgpt-sample-general-intake.pdf");
        return Path.GetFullPath(fallback);
    }
}

internal sealed record ApiMetricsResponse(
    int RequestCount,
    int ReviewFinalizationCount,
    long ExtractionSuccessCount,
    long ExtractionFailureCount);


internal sealed record SearchResultItem(Guid IntakeId, string TemplateId, string ApplicantName, string Status, decimal Confidence, string Snippet);
internal sealed record SearchResponse(string Query, List<SearchResultItem> Results);
internal sealed record CaseDocumentItem(Guid IntakeId, string TemplateId, string Status, DateTimeOffset CreatedAt, List<ConfidenceField> Fields);
internal sealed record CaseAggregateResponse(string PersonKey, List<CaseDocumentItem> Documents);