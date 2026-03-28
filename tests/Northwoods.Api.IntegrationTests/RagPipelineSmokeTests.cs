using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Northwoods.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace Northwoods.Api.IntegrationTests;

/// <summary>
/// RAG pipeline smoke test: uploads corpus PDFs for known story-arc people,
/// waits for extraction + embedding, then verifies similar-case retrieval
/// surfaces same-person documents and respects tenant boundaries.
///
/// Requires a running stack with OpenAI API key.
/// Run: dotnet test --filter Category=RAG
/// </summary>
public sealed class RagPipelineSmokeTests
{

    /// <summary>Extraction + embedding can take a while with real OpenAI calls.</summary>
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly ITestOutputHelper _output;

    public RagPipelineSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ── Corpus manifest ─────────────────────────────────────────────────────
    // Each entry: (personId, tenantId, tenantSlug, relativePath, templateId)
    // Paths are relative to the repo root samples/corpus/ directory.

    private static readonly (string PersonId, string TenantId, string TenantSlug, string RelativePath, string TemplateId)[] CorpusManifest =
    [
        // P019 — Raymond Castillo, sunrise frequent flyer (tenant-a)
        ("P019", "tenant-a", "sunrise", "sunrise/P019_01_general-assistance.pdf", "general-assistance"),
        ("P019", "tenant-a", "sunrise", "sunrise/P019_02_general-assistance.pdf", "general-assistance"),
        ("P019", "tenant-a", "sunrise", "sunrise/P019_01_housing-stability.pdf", "housing-stability"),
        ("P019", "tenant-a", "sunrise", "P019_01_clinical-soap-note.pdf", "soap-note"),
        ("P019", "tenant-a", "sunrise", "P019_02_clinical-soap-note.pdf", "soap-note"),

        // P039 — Gloria Navarro, lakewood frequent flyer (tenant-b)
        ("P039", "tenant-b", "lakewood", "lakewood/P039_01_general-assistance.pdf", "general-assistance"),
        ("P039", "tenant-b", "lakewood", "lakewood/P039_02_general-assistance.pdf", "general-assistance"),
        ("P039", "tenant-b", "lakewood", "lakewood/P039_01_housing-stability.pdf", "housing-stability"),
        ("P039", "tenant-b", "lakewood", "P039_01_clinical-soap-note.pdf", "soap-note"),

        // P017 — Carlton Hughes, sunrise->lakewood transfer (upload v2 era at sunrise/tenant-a)
        ("P017", "tenant-a", "sunrise", "sunrise/P017_01_general-assistance.pdf", "general-assistance"),
        ("P017", "tenant-a", "sunrise", "sunrise/P017_01_housing-stability.pdf", "housing-stability"),
    ];

    // ── Main test ───────────────────────────────────────────────────────────

    [Trait("Category", "RAG")]
    [Fact]
    public async Task UploadCorpus_SimilarCases_RetrievesSamePersonAndRespectsIsolation()
    {
        using var baseClient = IntegrationTestHelpers.CreateClient();

        if (!await IntegrationTestHelpers.IsRuntimeAvailableAsync(baseClient))
        {
            _output.WriteLine("SKIP: API not available at {0}", IntegrationTestHelpers.DefaultBaseUrl);
            return;
        }

        // ── 1. Resolve corpus directory ─────────────────────────────────
        var corpusDir = ResolveCorpusDir();
        if (corpusDir is null)
        {
            _output.WriteLine("SKIP: samples/corpus/ directory not found from test output directory.");
            return;
        }
        _output.WriteLine("Corpus directory: {0}", corpusDir);

        // Verify all manifest files exist before uploading anything.
        var missingFiles = CorpusManifest
            .Where(m => !File.Exists(Path.Combine(corpusDir, m.RelativePath)))
            .Select(m => m.RelativePath)
            .ToList();

        if (missingFiles.Count > 0)
        {
            _output.WriteLine("SKIP: Missing corpus PDFs:\n  {0}", string.Join("\n  ", missingFiles));
            return;
        }

        // ── 2. Authenticate ────────────────────────────────────────────
        var tenantAWorkerToken = await IntegrationTestHelpers.LoginAsync(baseClient, "tenant-a", "worker@sunrise.example", "password");
        var tenantBWorkerToken = await IntegrationTestHelpers.LoginAsync(baseClient, "tenant-b", "worker@lakewood.example", "password");
        var tenantAReviewerToken = await IntegrationTestHelpers.LoginAsync(baseClient, "tenant-a", "reviewer@sunrise.example", "password");
        var tenantBReviewerToken = await IntegrationTestHelpers.LoginAsync(baseClient, "tenant-b", "reviewer@lakewood.example", "password");

        using var tenantAWorkerClient = IntegrationTestHelpers.CreateClient(tenantAWorkerToken);
        using var tenantBWorkerClient = IntegrationTestHelpers.CreateClient(tenantBWorkerToken);
        using var tenantAReviewerClient = IntegrationTestHelpers.CreateClient(tenantAReviewerToken);
        using var tenantBReviewerClient = IntegrationTestHelpers.CreateClient(tenantBReviewerToken);

        // ── 3. Upload all corpus documents ──────────────────────────────
        var uploads = new List<(string PersonId, string TenantId, string TemplateId, string FileName, Guid IntakeId)>();

        _output.WriteLine("\n=== UPLOAD PHASE ===");
        foreach (var entry in CorpusManifest)
        {
            var client = entry.TenantId == "tenant-a" ? tenantAWorkerClient : tenantBWorkerClient;
            var filePath = Path.Combine(corpusDir, entry.RelativePath);
            var intakeId = await UploadIntakeAsync(client, filePath, entry.TemplateId);
            uploads.Add((entry.PersonId, entry.TenantId, entry.TemplateId, Path.GetFileName(entry.RelativePath), intakeId));
            _output.WriteLine("  Uploaded {0} [{1}] as {2} -> {3}", Path.GetFileName(entry.RelativePath), entry.TemplateId, entry.TenantId, intakeId);
        }

        // ── 4. Wait for all extractions to complete ─────────────────────
        _output.WriteLine("\n=== EXTRACTION PHASE ===");
        var results = new Dictionary<Guid, ProcessingStatus>();
        var deadline = DateTimeOffset.UtcNow + ExtractionTimeout;

        foreach (var upload in uploads)
        {
            var client = upload.TenantId == "tenant-a" ? tenantAWorkerClient : tenantBWorkerClient;
            var status = await WaitForTerminalStatusAsync(client, upload.IntakeId, deadline);
            results[upload.IntakeId] = status;
            _output.WriteLine("  {0} ({1}) -> {2}", upload.FileName, upload.IntakeId, status);
        }

        // At least some P019 docs must reach review_ready for the test to be meaningful.
        var p019Uploads = uploads.Where(u => u.PersonId == "P019").ToList();
        var p019ReviewReady = p019Uploads.Where(u => results[u.IntakeId] == ProcessingStatus.ReviewReady).ToList();

        if (p019ReviewReady.Count < 2)
        {
            _output.WriteLine("ABORT: Only {0}/{1} P019 documents reached review_ready. Need >= 2 for same-person retrieval test.",
                p019ReviewReady.Count, p019Uploads.Count);

            // Still dump status for debugging.
            foreach (var u in uploads)
                _output.WriteLine("  {0} {1} -> {2}", u.PersonId, u.FileName, results[u.IntakeId]);

            Assert.Fail($"Insufficient P019 documents reached review_ready ({p019ReviewReady.Count}/{p019Uploads.Count}). Check extraction worker logs.");
        }

        // ── 5. Query similar cases for P019's latest document ───────────
        _output.WriteLine("\n=== SIMILAR CASES RETRIEVAL ===");

        // Pick the last P019 doc that reached review_ready as the query document.
        var queryDoc = p019ReviewReady.Last();
        _output.WriteLine("Query document: {0} ({1}, {2})", queryDoc.FileName, queryDoc.TemplateId, queryDoc.IntakeId);

        var review = await tenantAReviewerClient.GetFromJsonAsync<ReviewDetailResponse>($"/reviews/{queryDoc.IntakeId}");
        Assert.NotNull(review);

        _output.WriteLine("  Fields extracted: {0}", review.Fields.Count);
        foreach (var field in review.Fields)
            _output.WriteLine("    {0} = {1} (confidence: {2:P0}, review: {3})", field.FieldKey, Truncate(field.Value, 60), field.Confidence, field.RequiresReview);

        _output.WriteLine("\n  Similar cases returned: {0}", review.SimilarCases.Count);

        // Build a lookup from intakeId -> (personId, fileName) for our uploaded docs.
        var uploadLookup = uploads.ToDictionary(u => u.IntakeId, u => (u.PersonId, u.FileName, u.TenantId));

        // ── 6. Human-readable report ────────────────────────────────────
        _output.WriteLine("\n=== RAG RETRIEVAL REPORT ===");
        _output.WriteLine("Query: {0} (P019, {1})", queryDoc.FileName, queryDoc.TemplateId);
        _output.WriteLine("{0,-40} {1,-8} {2,-12} {3,-10} {4}", "Matched Document", "Person", "Template", "Score", "Summary");
        _output.WriteLine(new string('-', 120));

        var p019MatchCount = 0;
        var crossTenantViolations = new List<string>();

        foreach (var similar in review.SimilarCases)
        {
            var isKnown = uploadLookup.TryGetValue(similar.IntakeId, out var info);
            var knownPerson = isKnown ? info.PersonId : "unknown";
            var knownFile = isKnown ? info.FileName : similar.IntakeId.ToString();
            var knownTenant = isKnown ? info.TenantId : "?";

            _output.WriteLine("{0,-40} {1,-8} {2,-12} {3,-10:F4} {4}",
                knownFile, knownPerson, similar.TemplateId, similar.MatchScore, Truncate(similar.Summary, 60));

            // Count P019 self-matches (other P019 docs retrieved).
            if (knownPerson == "P019")
                p019MatchCount++;

            // Flag cross-tenant violations: P039 (tenant-b) docs must never appear in tenant-a results.
            if (knownTenant == "tenant-b")
                crossTenantViolations.Add($"{knownFile} (tenant-b) appeared in tenant-a similar cases");
        }

        _output.WriteLine(new string('-', 120));
        _output.WriteLine("P019 self-matches in top-{0}: {1}", review.SimilarCases.Count, p019MatchCount);
        _output.WriteLine("Cross-tenant violations: {0}", crossTenantViolations.Count);

        // ── 7. Also query P039 from tenant-b to verify isolation in reverse ──
        var p039ReviewReady = uploads
            .Where(u => u.PersonId == "P039" && results[u.IntakeId] == ProcessingStatus.ReviewReady)
            .ToList();

        if (p039ReviewReady.Count > 0)
        {
            var p039Query = p039ReviewReady.Last();
            _output.WriteLine("\n--- Cross-check: P039 from tenant-b ---");
            _output.WriteLine("Query document: {0} ({1})", p039Query.FileName, p039Query.IntakeId);

            var p039Review = await tenantBReviewerClient.GetFromJsonAsync<ReviewDetailResponse>($"/reviews/{p039Query.IntakeId}");
            Assert.NotNull(p039Review);

            _output.WriteLine("  Similar cases: {0}", p039Review.SimilarCases.Count);
            foreach (var similar in p039Review.SimilarCases)
            {
                var isKnown = uploadLookup.TryGetValue(similar.IntakeId, out var info);
                var knownPerson = isKnown ? info.PersonId : "unknown";
                var knownFile = isKnown ? info.FileName : similar.IntakeId.ToString();
                var knownTenant = isKnown ? info.TenantId : "?";

                _output.WriteLine("    {0} ({1}, {2}) score={3:F4}", knownFile, knownPerson, similar.TemplateId, similar.MatchScore);

                // No tenant-a docs should appear in tenant-b results.
                if (knownTenant == "tenant-a")
                    crossTenantViolations.Add($"{knownFile} (tenant-a) appeared in tenant-b (P039) similar cases");
            }
        }

        // ── 8. LLM-as-judge (optional, best-effort) ────────────────────
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(openAiKey) && review.SimilarCases.Count > 0 && p019MatchCount > 0)
        {
            _output.WriteLine("\n=== LLM-AS-JUDGE ===");
            await RunLlmJudgeAsync(openAiKey, review, uploadLookup);
        }

        // ── 9. Assertions ───────────────────────────────────────────────
        _output.WriteLine("\n=== ASSERTIONS ===");

        // Core assertion: P019's latest doc returns at least 1 other P019 doc in similar cases.
        _output.WriteLine("P019 same-person retrieval: {0} matches (need >= 1)", p019MatchCount);
        Assert.True(p019MatchCount >= 1,
            $"P019's latest document should return at least 1 other P019 document in similar cases. Got {p019MatchCount}.");

        // Tenant isolation: no cross-tenant leakage.
        _output.WriteLine("Cross-tenant violations: {0}", crossTenantViolations.Count);
        Assert.Empty(crossTenantViolations);

        _output.WriteLine("\nAll RAG pipeline assertions passed.");
    }

    // ── LLM-as-judge ────────────────────────────────────────────────────────

    private async Task RunLlmJudgeAsync(
        string apiKey,
        ReviewDetailResponse review,
        Dictionary<Guid, (string PersonId, string FileName, string TenantId)> uploadLookup)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            // Build excerpt from query doc fields.
            var queryExcerpt = string.Join("; ", review.Fields.Select(f => $"{f.FieldKey}: {f.Value}"));

            // Build excerpts from top-3 similar cases.
            var topCases = review.SimilarCases.Take(3).ToList();
            var caseExcerpts = new StringBuilder();
            for (var i = 0; i < topCases.Count; i++)
            {
                var c = topCases[i];
                var personId = uploadLookup.TryGetValue(c.IntakeId, out var info) ? info.PersonId : "unknown";
                caseExcerpts.AppendLine($"Document {(char)('B' + i)} (retrieved, {personId}): {c.ApplicantName}, template={c.TemplateId}, summary={c.Summary}");
            }

            var prompt = $"""
                Given these document excerpts from a social services case management system, do they appear to describe the same person's situation over time?

                Document A (query): {Truncate(queryExcerpt, 500)}

                {caseExcerpts}

                For each retrieved document (B, C, etc.), answer: Yes or No, plus one sentence of reasoning.
                Format: "Document X: Yes/No - reasoning"
                """;

            var requestBody = new
            {
                model = "gpt-4.1-nano",
                messages = new[]
                {
                    new { role = "system", content = "You are evaluating whether retrieved social services documents describe the same person over time. Be concise." },
                    new { role = "user", content = prompt }
                },
                max_tokens = 300,
                temperature = 0.0
            };

            using var response = await httpClient.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", requestBody);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                var judgeResult = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                _output.WriteLine("LLM Judge result:\n{0}", judgeResult);

                // Count "Yes" responses.
                var yesCount = judgeResult?.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Count(line => line.Contains("Yes", StringComparison.OrdinalIgnoreCase)) ?? 0;
                _output.WriteLine("Judge: {0}/{1} documents rated as same person.", yesCount, topCases.Count);
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _output.WriteLine("LLM judge call failed ({0}): {1}", response.StatusCode, Truncate(errorBody, 200));
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine("LLM judge skipped due to error: {0}", ex.Message);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────


    private static async Task<Guid> UploadIntakeAsync(HttpClient client, string filePath, string templateId)
    {
        await using var fileStream = File.OpenRead(filePath);
        using var form = new MultipartFormDataContent
        {
            { new StreamContent(fileStream), "file", Path.GetFileName(filePath) },
            { new StringContent(templateId), "templateId" }
        };

        using var response = await client.PostAsync("/intakes", form);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CreateIntakeResponse>();
        return payload?.IntakeId ?? throw new InvalidOperationException("Intake response missing IntakeId.");
    }

    private async Task<ProcessingStatus> WaitForTerminalStatusAsync(HttpClient client, Guid intakeId, DateTimeOffset deadline)
    {
        var lastStatus = ProcessingStatus.Uploaded;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var statusResponse = await client.GetFromJsonAsync<IntakeStatusResponse>($"/intakes/{intakeId}");
                if (statusResponse is not null)
                {
                    lastStatus = statusResponse.Status;
                    if (lastStatus is ProcessingStatus.ReviewReady or ProcessingStatus.Failed or ProcessingStatus.Finalized)
                        return lastStatus;
                }
            }
            catch
            {
                // Transient error — keep polling.
            }

            await Task.Delay(PollInterval);
        }

        _output.WriteLine("  TIMEOUT: {0} stuck at {1}", intakeId, lastStatus);
        return lastStatus;
    }

    private static string? ResolveCorpusDir()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && current is not null; i++)
        {
            var candidate = Path.Combine(current.FullName, "samples", "corpus");
            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        // Fallback: relative to CWD.
        var fallback = Path.GetFullPath(Path.Combine("samples", "corpus"));
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }
}
