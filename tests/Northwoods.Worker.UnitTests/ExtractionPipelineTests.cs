using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkerService = Extraction.Worker.Worker;
using Xunit;

namespace Northwoods.Worker.UnitTests;

public class ExtractionPipelineTests
{
    [Fact]
    public async Task PipelineEscalatesOnlyLowConfidenceFields()
    {
        var context = new WorkerService.ExtractionContext(
            Guid.NewGuid(),
            "tenant-a",
            "general-assistance",
            "sample.pdf",
            "sample.pdf",
            1024);

        var baseline = new TrackingProvider("baseline", "ocr", 0)
        {
            FieldValues =
            {
                ["applicantName"] = ("Jamie Carter", 0.78m),
                ["dateOfBirth"] = ("03/15/1988", 0.94m),
                ["notes"] = ("No remarks", 0.58m)
            }
        };

        var escalated = new TrackingProvider("normalizer", "normalize", 1)
        {
            FieldValues =
            {
                ["applicantName"] = ("Jamie Carter", 0.93m),
                ["notes"] = ("No remarks", 0.72m)
            }
        };

        var result = await WorkerService.RunExtractionPipelineForTests(
            context,
            ["applicantName", "dateOfBirth", "notes"],
            [baseline, escalated],
            CancellationToken.None);

        var applicant = result.Single(r => r.FieldKey == "applicantName");
        var dob = result.Single(r => r.FieldKey == "dateOfBirth");
        var notes = result.Single(r => r.FieldKey == "notes");

        Assert.Equal(3, baseline.RequestedFieldKeys.Count);
        Assert.Equal(2, escalated.RequestedFieldKeys.Count);
        Assert.Contains("applicantName", baseline.RequestedFieldKeys);
        Assert.Contains("dateOfBirth", baseline.RequestedFieldKeys);
        Assert.Contains("notes", escalated.RequestedFieldKeys);
        Assert.DoesNotContain("dateOfBirth", escalated.RequestedFieldKeys);

        Assert.Equal("Jamie Carter", applicant.FinalValue);
        Assert.True(applicant.SystemConfidence > 0.78m);
        Assert.Equal("03/15/1988", dob.FinalValue);
        Assert.False(WorkerService.RequiresReview(dob.SystemConfidence));

        Assert.Equal("No remarks", notes.FinalValue);
        Assert.True(WorkerService.RequiresReview(notes.SystemConfidence));
    }

    private sealed class TrackingProvider(string name, string stage, int order) : WorkerService.IExtractionProvider
    {
        public HashSet<string> RequestedFieldKeys { get; } = [];

        public string Name { get; } = name;

        public string Stage { get; } = stage;

        public int Order { get; } = order;

        public Dictionary<string, (string Value, decimal Confidence)> FieldValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<WorkerService.ExtractionCandidate>> ExtractAsync(
            WorkerService.ExtractionContext context,
            IReadOnlyCollection<string> fieldKeys,
            IReadOnlyDictionary<string, List<WorkerService.ExtractionCandidate>>? priorAttempts,
            CancellationToken ct)
        {
            var candidates = new List<WorkerService.ExtractionCandidate>();
            foreach (var key in fieldKeys)
            {
                RequestedFieldKeys.Add(key);
                if (!FieldValues.TryGetValue(key, out var item))
                {
                    continue;
                }

                candidates.Add(
                    new WorkerService.ExtractionCandidate(
                        key,
                        item.Value,
                        item.Confidence,
                        Stage,
                        Name,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["tenant"] = context.TenantId
                        }));
            }

            return Task.FromResult<IReadOnlyList<WorkerService.ExtractionCandidate>>(candidates);
        }
    }
}
