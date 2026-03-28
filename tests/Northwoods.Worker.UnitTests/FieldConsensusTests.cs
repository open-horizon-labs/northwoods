using System.Collections.Generic;
using Extraction.Worker;
using WorkerService = Extraction.Worker.Worker;
using Xunit;

namespace Northwoods.Worker.UnitTests;

public class FieldConsensusTests
{
    [Fact]
    public void ChoosesTopScoringAgreedValueAndBoostsConfidence()
    {
        var attempts = new List<ExtractionCandidate>
        {
            new("applicantName", "Jamie Carter", 0.60m, "ocr", "provider-a", []),
            new("applicantName", "Jamie Carter", 0.68m, "ocr", "provider-b", []),
            new("applicantName", "James Carter", 0.95m, "normalize", "provider-c", [])
        };

        var resolved = WorkerService.ResolveConsensusForTests("applicantName", attempts);

        Assert.Equal("applicantName", resolved.FieldKey);
        Assert.Equal("Jamie Carter", resolved.FinalValue);
        Assert.Equal("jamie carter", resolved.NormalizedValue);
        Assert.Equal(3, resolved.AllAttempts.Count);
        Assert.InRange(resolved.SystemConfidence, 0.69m, 0.71m);
    }

    [Fact]
    public void UsesBestAvailableCandidateWhenNoAgreementCanBeMade()
    {
        var attempts = new List<ExtractionCandidate>
        {
            new("dateOfBirth", "03/15/1988", 0.63m, "ocr", "provider-a", []),
            new("dateOfBirth", string.Empty, 0.20m, "ocr", "provider-b", [])
        };

        var resolved = WorkerService.ResolveConsensusForTests("dateOfBirth", attempts);

        Assert.Equal("03/15/1988", resolved.FinalValue);
        Assert.Equal("03/15/1988", resolved.NormalizedValue);
        Assert.Equal(0.63m, resolved.SystemConfidence);
    }

    [Fact]
    public void ReturnsEmptyLowConfidenceWhenNoCandidatesHaveValues()
    {
        var attempts = new List<ExtractionCandidate>
        {
            new("notes", string.Empty, 0.20m, "ocr", "provider-a", [])
        };

        var resolved = WorkerService.ResolveConsensusForTests("notes", attempts);

        Assert.Equal(string.Empty, resolved.FinalValue);
        Assert.Equal(string.Empty, resolved.NormalizedValue);
        Assert.Equal(0.01m, resolved.SystemConfidence);
    }
}
