using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Extraction.Worker;

internal sealed class OpenAiNormalizerProvider(string apiKey, string modelMini) : IExtractionProvider
{
    private static readonly HttpClient Http = new();

    public string Name => "openai";
    public string Stage => "normalize";
    public int Order => 1;

    public async Task<IReadOnlyList<ExtractionCandidate>> ExtractAsync(
        ExtractionContext context,
        IReadOnlyCollection<string> fieldKeys,
        IReadOnlyDictionary<string, List<ExtractionCandidate>>? priorAttempts,
        CancellationToken ct)
    {
        if (priorAttempts is null || priorAttempts.Count == 0)
            return [];

        var baseline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in fieldKeys)
        {
            if (!priorAttempts.TryGetValue(key, out var attempts) || attempts.Count == 0)
                continue;

            var best = attempts.OrderByDescending(a => a.Confidence).First();
            baseline[key] = best.Value;
        }

        if (baseline.Count == 0)
            return [];

        var requestPayload = new
        {
            model = modelMini,
            input = new object[]
            {
                new
                {
                    role = "developer",
                    content = "Normalize OCR-extracted intake fields. Return ONLY compact JSON object where keys are field names and values are objects: {\"value\": string, \"confidence\": number}. Confidence must be 0-1. Preserve missing fields as empty string with low confidence."
                },
                new
                {
                    role = "user",
                    content = $"Document key: {context.OriginalFileKey}. Baseline values: {JsonSerializer.Serialize(baseline)}"
                }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var res = await Http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            var isTransient = (int)res.StatusCode >= 500 || res.StatusCode == HttpStatusCode.TooManyRequests || res.StatusCode == HttpStatusCode.RequestTimeout;

            if (isTransient)
            {
                throw new TransientExtractionException($"OpenAI normalization failed ({(int)res.StatusCode}).");
            }

            throw new InvalidOperationException($"OpenAI normalization failed ({(int)res.StatusCode}).");
        }

        var outputText = TryGetOutputText(body);
        if (string.IsNullOrWhiteSpace(outputText))
            return [];

        Dictionary<string, (string Value, decimal Confidence)> normalized;
        try
        {
            normalized = ParseNormalizedFields(outputText);
        }
        catch (JsonException)
        {
            return [];
        }
        var results = new List<ExtractionCandidate>();
        foreach (var key in fieldKeys)
        {
            if (!normalized.TryGetValue(key, out var item))
                continue;

            results.Add(new ExtractionCandidate(
                key,
                item.Value,
                item.Confidence,
                Stage,
                Name,
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["model"] = modelMini,
                    ["tenant"] = ProviderHelpers.TenantHash(context.TenantId),
                    ["technique"] = "openai-mini-json-normalize"
                }));
        }

        return results;
    }

    private static string TryGetOutputText(string rawResponse)
    {
        using var doc = JsonDocument.Parse(rawResponse);

        if (doc.RootElement.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            var text = outputText.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        // Fallback: parse output[] -> content[] -> output_text items
        if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var t) && t.GetString() == "output_text"
                            && part.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                        {
                            var text = txt.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                                return text;
                        }
                    }
                }
            }
        }

        return string.Empty;
    }

    private static Dictionary<string, (string Value, decimal Confidence)> ParseNormalizedFields(string jsonText)
    {
        var map = new Dictionary<string, (string Value, decimal Confidence)>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(jsonText);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return map;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
                continue;

            var value = prop.Value.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty
                : string.Empty;

            var confidence = prop.Value.TryGetProperty("confidence", out var c) && c.ValueKind is JsonValueKind.Number
                ? Math.Clamp(c.GetDecimal(), 0m, 1m)
                : 0.4m;

            map[prop.Name] = (value, Math.Round(confidence, 4, MidpointRounding.ToZero));
        }

        return map;
    }
}
