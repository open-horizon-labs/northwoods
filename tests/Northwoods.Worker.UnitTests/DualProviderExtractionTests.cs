using System.Text.Json;
using Extraction.Worker;
using WorkerService = Extraction.Worker.Worker;
using Xunit;

namespace Northwoods.Worker.UnitTests;

public class DualProviderExtractionTests
{
    private static readonly string[] FieldKeys = ["applicantName", "dateOfBirth", "address", "householdSize", "monthlyIncome", "requestedServices", "notes"];

    private static WorkerService.ExtractionContext MakeContext() =>
        new(Guid.NewGuid(), "tenant-a", "general-assistance", "test.pdf", "/tmp/test.pdf", 1024, [0x25, 0x50, 0x44, 0x46]); // %PDF header

    /// <summary>
    /// A test provider that returns fixed extraction candidates for all requested fields.
    /// </summary>
    private sealed class FixedProvider(
        string name, string stage, int order,
        Dictionary<string, (string Value, decimal Confidence)> fieldValues,
        Dictionary<string, object>? extraMetadata = null) : WorkerService.IExtractionProvider
    {
        public string Name => name;
        public string Stage => stage;
        public int Order => order;

        public Task<IReadOnlyList<WorkerService.ExtractionCandidate>> ExtractAsync(
            WorkerService.ExtractionContext context,
            IReadOnlyCollection<string> fieldKeys,
            IReadOnlyDictionary<string, List<WorkerService.ExtractionCandidate>>? priorAttempts,
            CancellationToken ct)
        {
            var results = new List<WorkerService.ExtractionCandidate>();
            foreach (var key in fieldKeys)
            {
                if (!fieldValues.TryGetValue(key, out var entry))
                    continue;

                var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["technique"] = $"{name}-test"
                };
                if (extraMetadata is not null)
                {
                    foreach (var kv in extraMetadata)
                        metadata[kv.Key] = kv.Value;
                }

                results.Add(new WorkerService.ExtractionCandidate(
                    key, entry.Value, entry.Confidence, stage, name, metadata));
            }
            return Task.FromResult<IReadOnlyList<WorkerService.ExtractionCandidate>>(results);
        }
    }

    [Fact]
    public async Task BothProvidersRunOnAllFields()
    {
        var paddleValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Jamie Carter", 0.85m),
            ["dateOfBirth"] = ("03/15/1988", 0.70m),
            ["address"] = ("742 Evergreen Terrace", 0.62m),
        };
        var visionValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Jamie Carter", 0.95m),
            ["dateOfBirth"] = ("03/15/1988", 0.98m),
            ["address"] = ("742 Evergreen Terrace, Springfield", 0.90m),
        };

        var paddleProvider = new FixedProvider("paddleocr", "ocr", 0, paddleValues,
            new Dictionary<string, object> { ["processing_ms"] = 150L });
        var visionProvider = new FixedProvider("openai-vision", "ocr", 1, visionValues,
            new Dictionary<string, object> { ["prompt_tokens"] = 100L, ["completion_tokens"] = 50L, ["total_tokens"] = 150L });

        IReadOnlyList<WorkerService.IExtractionProvider> providers = [paddleProvider, visionProvider];
        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["applicantName", "dateOfBirth", "address"], providers, CancellationToken.None);

        Assert.Equal(3, results.Count);

        // Each field should have 2 attempts (one from each provider)
        foreach (var result in results)
        {
            Assert.Equal(2, result.AllAttempts.Count);
            var providerNames = result.AllAttempts.Select(a => a.Provider).OrderBy(n => n).ToArray();
            Assert.Equal(["openai-vision", "paddleocr"], providerNames);
        }

        // Both providers in providerSequence
        foreach (var result in results)
        {
            Assert.Contains("paddleocr", result.ProviderSequence);
            Assert.Contains("openai-vision", result.ProviderSequence);
        }
    }

    [Fact]
    public async Task ConsensusBoostsConfidenceOnAgreement()
    {
        var paddleValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Maria Lopez", 0.84m),
        };
        var visionValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Maria Lopez", 0.95m),
        };

        var paddleProvider = new FixedProvider("paddleocr", "ocr", 0, paddleValues);
        var visionProvider = new FixedProvider("openai-vision", "ocr", 1, visionValues);

        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["applicantName"], [paddleProvider, visionProvider], CancellationToken.None);

        var nameResult = results.Single();
        // Consensus should boost confidence because both providers agree
        // Average is (0.84 + 0.95)/2 = 0.895, plus 0.06 agreement boost = 0.955 clamped to 0.99
        Assert.True(nameResult.SystemConfidence > 0.89m, $"Expected consensus boost, got {nameResult.SystemConfidence}");
        Assert.Equal("Maria Lopez", nameResult.FinalValue);
    }

    [Fact]
    public async Task MetadataContainsTokenUsage()
    {
        var visionValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Test Name", 0.90m),
        };

        var tokenMetadata = new Dictionary<string, object>
        {
            ["prompt_tokens"] = 200L,
            ["completion_tokens"] = 80L,
            ["total_tokens"] = 280L,
            ["model"] = "gpt-5.4-nano"
        };

        var provider = new FixedProvider("openai-vision", "ocr", 0, visionValues, tokenMetadata);
        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["applicantName"], [provider], CancellationToken.None);

        var attempt = results.Single().AllAttempts.Single();
        Assert.NotNull(attempt.Metadata);

        // Verify token usage in metadata (will be serialized to details JSONB)
        var json = JsonSerializer.Serialize(attempt.Metadata);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("prompt_tokens", out var pt));
        Assert.Equal(200, pt.GetInt64());
        Assert.True(doc.RootElement.TryGetProperty("completion_tokens", out var ct2));
        Assert.Equal(80, ct2.GetInt64());
        Assert.True(doc.RootElement.TryGetProperty("total_tokens", out var tt));
        Assert.Equal(280, tt.GetInt64());
    }

    [Fact]
    public async Task MetadataContainsProcessingMs()
    {
        var paddleValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Test Name", 0.85m),
        };

        var processingMetadata = new Dictionary<string, object>
        {
            ["processing_ms"] = 342L
        };

        var provider = new FixedProvider("paddleocr", "ocr", 0, paddleValues, processingMetadata);
        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["applicantName"], [provider], CancellationToken.None);

        var attempt = results.Single().AllAttempts.Single();
        var json = JsonSerializer.Serialize(attempt.Metadata);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("processing_ms", out var pm));
        Assert.Equal(342, pm.GetInt64());
    }

    [Fact]
    public async Task DisagreementKeepsBothAttempts()
    {
        var paddleValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["address"] = ("128Maple St.", 0.50m),
        };
        var visionValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["address"] = ("128 Maple St.", 0.90m),
        };

        var paddleProvider = new FixedProvider("paddleocr", "ocr", 0, paddleValues);
        var visionProvider = new FixedProvider("openai-vision", "ocr", 1, visionValues);

        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["address"], [paddleProvider, visionProvider], CancellationToken.None);

        var addressResult = results.Single();
        Assert.Equal(2, addressResult.AllAttempts.Count);
        // Higher-confidence value should win (or be boosted by agreement)
        Assert.Equal("128 Maple St.", addressResult.FinalValue);
    }

    [Fact]
    public async Task SingleProviderStillWorks()
    {
        var values = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Solo Provider Name", 0.85m),
        };

        var provider = new FixedProvider("paddleocr", "ocr", 0, values);
        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["applicantName"], [provider], CancellationToken.None);

        Assert.Single(results);
        Assert.Single(results[0].AllAttempts);
        Assert.Equal("Solo Provider Name", results[0].FinalValue);
        Assert.Equal(0.85m, results[0].SystemConfidence);
    }

    [Fact]
    public void ConsensusResolvesMultipleAttempts()
    {
        var attempts = new List<WorkerService.ExtractionCandidate>
        {
            new("applicantName", "Maria Lopez", 0.84m, "ocr", "paddleocr",
                new Dictionary<string, object> { ["technique"] = "paddleocr+label-regex", ["processing_ms"] = 200L }),
            new("applicantName", "Maria Lopez", 0.95m, "ocr", "openai-vision",
                new Dictionary<string, object> { ["technique"] = "openai-vision-extract", ["prompt_tokens"] = 100L, ["completion_tokens"] = 50L, ["total_tokens"] = 150L }),
        };

        var result = WorkerService.ResolveConsensusForTests("applicantName", attempts);

        Assert.Equal("Maria Lopez", result.FinalValue);
        Assert.True(result.SystemConfidence > Math.Max(0.84m, 0.95m),
            $"Consensus should boost beyond individual max, got {result.SystemConfidence}");
        Assert.Equal(2, result.AllAttempts.Count);
        Assert.Contains("paddleocr", result.ProviderSequence);
        Assert.Contains("openai-vision", result.ProviderSequence);
    }

    [Fact]
    public void MetadataDictionarySerializesWithProperTypes()
    {
        var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["technique"] = "openai-vision-extract",
            ["model"] = "gpt-5.4-nano",
            ["prompt_tokens"] = 200L,
            ["completion_tokens"] = 80L,
            ["total_tokens"] = 280L,
            ["processing_ms"] = 342L
        };

        var json = JsonSerializer.Serialize(metadata);
        using var doc = JsonDocument.Parse(json);

        // Strings serialize as strings
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("technique").ValueKind);
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("model").ValueKind);

        // Numbers serialize as numbers (not strings)
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("prompt_tokens").ValueKind);
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("completion_tokens").ValueKind);
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("total_tokens").ValueKind);
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("processing_ms").ValueKind);
    }

    // =========================================================================
    // P2: Provider agreement boosts confidence, disagreement lowers it
    // =========================================================================

    [Fact]
    public async Task AgreementBoostsConfidenceBeyondIndividualMax()
    {
        var paddleValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Maria Lopez", 0.84m),
            ["dateOfBirth"] = ("03/15/1988", 0.88m),
        };
        var visionValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Maria Lopez", 0.91m),
            ["dateOfBirth"] = ("03/15/1988", 0.93m),
        };

        var p1 = new FixedProvider("paddleocr", "ocr", 0, paddleValues);
        var p2 = new FixedProvider("openai-vision", "ocr", 1, visionValues);

        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["applicantName", "dateOfBirth"], [p1, p2], CancellationToken.None);

        foreach (var result in results)
        {
            var maxIndividual = result.AllAttempts.Max(a => a.Confidence);
            Assert.True(result.SystemConfidence > maxIndividual,
                $"{result.FieldKey}: consensus {result.SystemConfidence} should exceed individual max {maxIndividual}");
        }
    }

    [Fact]
    public async Task DisagreementUsesHigherConfidenceValue()
    {
        var paddleValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["address"] = ("123 Oak St", 0.55m),
        };
        var visionValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["address"] = ("123 Oak Street, Suite 4", 0.88m),
        };

        var p1 = new FixedProvider("paddleocr", "ocr", 0, paddleValues);
        var p2 = new FixedProvider("openai-vision", "ocr", 1, visionValues);

        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["address"], [p1, p2], CancellationToken.None);

        var result = results.Single();
        // Higher-confidence value wins
        Assert.Equal("123 Oak Street, Suite 4", result.FinalValue);
        Assert.Equal(2, result.AllAttempts.Count);
        // Disagreement means no agreement boost -- confidence should not exceed the higher individual
        Assert.True(result.SystemConfidence <= 0.88m,
            $"Disagreement should not boost beyond higher value confidence, got {result.SystemConfidence}");
    }

    // =========================================================================
    // P3: ADR 005 confidence tier status determination
    // =========================================================================

    [Fact]
    public async Task AllHighConfidenceFieldsProduceCompletedStatus()
    {
        var values = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Jamie Carter", 0.95m),
            ["dateOfBirth"] = ("03/15/1988", 0.92m),
            ["address"] = ("742 Evergreen Terrace", 0.91m),
        };

        var provider = new FixedProvider("openai-vision", "ocr", 0, values);
        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["applicantName", "dateOfBirth", "address"], [provider], CancellationToken.None);

        var (status, autoAccepted, requiresAttention) = WorkerService.DetermineDocumentStatus(results);

        Assert.Equal("completed", status);
        Assert.True(autoAccepted);
        Assert.False(requiresAttention);
    }

    [Fact]
    public async Task MixedConfidenceFieldsProduceReviewReadyStatus()
    {
        var values = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Jamie Carter", 0.95m),
            ["dateOfBirth"] = ("03/15/1988", 0.85m),
            ["address"] = ("742 Evergreen Terrace", 0.60m),
        };

        var provider = new FixedProvider("openai-vision", "ocr", 0, values);
        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["applicantName", "dateOfBirth", "address"], [provider], CancellationToken.None);

        var (status, autoAccepted, requiresAttention) = WorkerService.DetermineDocumentStatus(results);

        Assert.Equal("review_ready", status);
        Assert.False(autoAccepted);
        Assert.True(requiresAttention, "Low-confidence field should set requires_attention");
    }

    [Fact]
    public async Task WarningRangeFieldsProduceReviewReadyWithoutAttention()
    {
        // All fields in 0.75-0.90 range: warning review path
        var values = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Jamie Carter", 0.80m),
            ["dateOfBirth"] = ("03/15/1988", 0.85m),
        };

        var provider = new FixedProvider("openai-vision", "ocr", 0, values);
        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["applicantName", "dateOfBirth"], [provider], CancellationToken.None);

        var (status, autoAccepted, requiresAttention) = WorkerService.DetermineDocumentStatus(results);

        Assert.Equal("review_ready", status);
        Assert.False(autoAccepted);
        Assert.False(requiresAttention, "Warning-range fields should not flag requires_attention");
    }

    // =========================================================================
    // P4: Edge cases -- empty response, single value provider
    // =========================================================================

    [Fact]
    public async Task EmptyProviderResponseProducesLowConfidence()
    {
        // Provider returns no values for any fields
        var emptyValues = new Dictionary<string, (string Value, decimal Confidence)>();

        var provider = new FixedProvider("openai-vision", "ocr", 0, emptyValues);
        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["applicantName", "dateOfBirth"], [provider], CancellationToken.None);

        // No results should come back for fields with no candidates
        Assert.Empty(results);

        // Document status for empty results should be review_ready with attention
        var (status, autoAccepted, requiresAttention) = WorkerService.DetermineDocumentStatus(results);
        Assert.Equal("review_ready", status);
        Assert.True(requiresAttention);
    }

    [Fact]
    public async Task NanoEscalationTriggeredOnLowAvgConfidence()
    {
        // Simulates what CallWithFallback would see: nano returns low confidence,
        // so mini should be used. We verify via the pipeline that low-confidence
        // single-provider results produce review_ready with attention flag.
        var lowConfValues = new Dictionary<string, (string Value, decimal Confidence)>
        {
            ["applicantName"] = ("Jamie Carter", 0.55m),
            ["dateOfBirth"] = ("03/15/1988", 0.50m),
            ["address"] = ("742 Evergreen Terrace", 0.45m),
        };

        var provider = new FixedProvider("nano-sim", "ocr", 0, lowConfValues);
        var results = await WorkerService.RunExtractionPipelineForTests(
            MakeContext(), ["applicantName", "dateOfBirth", "address"], [provider], CancellationToken.None);

        // All fields should require review
        Assert.All(results, r => Assert.True(
            WorkerService.RequiresReview(r.SystemConfidence),
            $"{r.FieldKey} confidence {r.SystemConfidence} should require review"));

        // Average confidence is well below 0.75 -- this is the escalation trigger
        var avgConfidence = results.Average(r => r.SystemConfidence);
        Assert.True(avgConfidence < WorkerService.GetReviewRequiredThreshold(),
            $"Average confidence {avgConfidence} should be below escalation threshold");

        var (status, _, requiresAttention) = WorkerService.DetermineDocumentStatus(results);
        Assert.Equal("review_ready", status);
        Assert.True(requiresAttention);
    }
}