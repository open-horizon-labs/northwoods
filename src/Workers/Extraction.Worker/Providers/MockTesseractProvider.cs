namespace Extraction.Worker;

internal sealed class MockTesseractProvider : IExtractionProvider
{
    public string Name => "tesseract-mock";
    public string Stage => "ocr";
    public int Order => 0;

    public Task<IReadOnlyList<ExtractionCandidate>> ExtractAsync(
        ExtractionContext context,
        IReadOnlyCollection<string> fieldKeys,
        IReadOnlyDictionary<string, List<ExtractionCandidate>>? priorAttempts,
        CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["applicantName"] = "Jamie Carter",
            ["dateOfBirth"] = "03/15/1988",
            ["address"] = "742 Evergreen Terrace, Springfield",
            ["householdSize"] = "4",
            ["monthlyIncome"] = "$1,850",
            ["requestedServices"] = "Housing assistance, utility aid",
            ["notes"] = "Applicant mentioned recent job loss"
        };

        var baseConfidence = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["applicantName"] = 0.85m,
            ["dateOfBirth"] = 0.70m,
            ["address"] = 0.62m,
            ["householdSize"] = 0.82m,
            ["monthlyIncome"] = 0.53m,
            ["requestedServices"] = 0.56m,
            ["notes"] = 0.49m
        };

        var list = new List<ExtractionCandidate>();
        foreach (var key in fieldKeys)
        {
            if (!values.TryGetValue(key, out var value))
                continue;

            var confidence = baseConfidence.TryGetValue(key, out var c) ? c : 0.50m;
            list.Add(new ExtractionCandidate(
                key,
                value,
                confidence,
                Stage,
                Name,
                new Dictionary<string, object>
                {
                    ["tenant"] = ProviderHelpers.TenantHash(context.TenantId),
                    ["technique"] = "mock-template-values"
                }));
        }

        return Task.FromResult<IReadOnlyList<ExtractionCandidate>>(list);
    }
}
