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

/// <summary>
/// Validates that GET /review-queue only returns documents with status
/// 'review_ready' or 'completed', filtering out uploaded, extracting,
/// failed, and finalized documents.
/// </summary>
public sealed class ReviewQueueStatusFilterTests
{
    private readonly ITestOutputHelper _output;

    public ReviewQueueStatusFilterTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task ReviewQueue_OnlyReturnsReviewReadyAndCompletedDocuments()
    {
        using var baselineClient = IntegrationTestHelpers.CreateClient();

        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(baselineClient))
        {
            _output.WriteLine("API runtime is not available; skipping review queue status filter test.");
            return;
        }

        // Log in as a reviewer (required for /review-queue) and a worker (required for /documents)
        var reviewerToken = await IntegrationTestHelpers.LoginAsync(
            baselineClient, "tenant-a", "reviewer@sunrise.example", "password");
        var workerToken = await IntegrationTestHelpers.LoginAsync(
            baselineClient, "tenant-a", "worker@sunrise.example", "password");

        using var reviewerClient = IntegrationTestHelpers.CreateClient(reviewerToken);
        using var workerClient = IntegrationTestHelpers.CreateClient(workerToken);

        // Get ALL documents for the tenant (no status filter) via /documents
        var allDocs = await workerClient.GetFromJsonAsync<List<DocumentListItem>>("/documents");
        Assert.NotNull(allDocs);
        _output.WriteLine($"Total documents for tenant-a: {allDocs.Count}");

        var statusCounts = allDocs
            .GroupBy(d => d.Status)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var (status, count) in statusCounts.OrderBy(kv => kv.Key))
        {
            _output.WriteLine($"  {status}: {count}");
        }

        // Get the review queue (should be filtered)
        var reviewQueue = await reviewerClient.GetFromJsonAsync<List<ReviewQueueItem>>("/review-queue");
        Assert.NotNull(reviewQueue);
        _output.WriteLine($"Review queue items: {reviewQueue.Count}");

        if (reviewQueue.Count == 0)
        {
            _output.WriteLine("Review queue is empty; cannot validate status filter. " +
                "Ensure seed data includes review_ready or completed documents.");
            return;
        }

        // For each item in the review queue, verify its status via /intakes/{id}
        var allowedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "review_ready", "completed"
        };

        var reviewQueueIds = reviewQueue.Select(r => r.IntakeId).ToHashSet();

        foreach (var item in reviewQueue)
        {
            var intake = await workerClient.GetFromJsonAsync<IntakeStatusResponse>($"/intakes/{item.IntakeId}");
            Assert.NotNull(intake);

            var statusStr = intake.Status.ToString();
            // ProcessingStatus enum uses PascalCase; the DB uses snake_case.
            // Convert enum to the comparable form.
            Assert.True(
                intake.Status is ProcessingStatus.ReviewReady or ProcessingStatus.Completed,
                $"Document {item.IntakeId} has status '{intake.Status}' but should only be " +
                $"'ReviewReady' or 'Completed' in the review queue.");
        }

        // Verify that documents with non-reviewable statuses are NOT in the queue
        var nonReviewableDocs = allDocs
            .Where(d => !allowedStatuses.Contains(d.Status))
            .ToList();

        _output.WriteLine($"Non-reviewable documents (should be excluded): {nonReviewableDocs.Count}");

        foreach (var doc in nonReviewableDocs)
        {
            Assert.DoesNotContain(reviewQueue, q => q.IntakeId == doc.DocumentId);
        }

        // Positive check: all review_ready/completed documents from /documents should appear in queue
        var reviewableDocs = allDocs
            .Where(d => allowedStatuses.Contains(d.Status))
            .ToList();

        _output.WriteLine($"Reviewable documents (should be included): {reviewableDocs.Count}");

        foreach (var doc in reviewableDocs)
        {
            Assert.Contains(reviewQueue, q => q.IntakeId == doc.DocumentId);
        }
    }
}
