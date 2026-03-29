using WorkerService = Extraction.Worker.Worker;
using Xunit;

namespace Northwoods.Worker.UnitTests;

public class ExtractionPolicyTests
{
    [Fact]
    public void RequiresReviewMatchesConfiguredThreshold()
    {
        var threshold = WorkerService.GetReviewRequiredThreshold();

        Assert.True(WorkerService.RequiresReview(threshold - 0.01m));
        Assert.False(WorkerService.RequiresReview(threshold));
    }

    [Fact]
    public void AutoAcceptMatchesConfiguredThreshold()
    {
        var threshold = WorkerService.GetHighConfidenceThreshold();

        Assert.True(WorkerService.IsAutoAccept(threshold + 0.01m));
        Assert.False(WorkerService.IsAutoAccept(threshold - 0.01m));
    }
}
