using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Extraction.Worker;

internal sealed class OpenAiVisionProvider(string apiKey, string modelNano, string modelMini) : IExtractionProvider
{
    private static readonly HttpClient Http = new();

    public string Name => "openai-vision";
    public string Stage => "ocr";
    public int Order => 1;

    public async Task<IReadOnlyList<ExtractionCandidate>> ExtractAsync(
        ExtractionContext context,
        IReadOnlyCollection<string> fieldKeys,
        IReadOnlyDictionary<string, List<ExtractionCandidate>>? priorAttempts,
        CancellationToken ct)
    {
        var schemaFieldsJson = JsonSerializer.Serialize(fieldKeys);
        var prompt = $"Extract ALL fields you find on this scanned intake form. Also specifically return these fields: {schemaFieldsJson}. " +
            "Return ONLY a JSON object where keys are field names and values are objects with \"value\" (string or null) and \"confidence\" (number 0-1). " +
            "If a required field is not present in the form, use null value with 0 confidence. Do not include markdown fences.";

        var (model, body, escalated, escalationReason) = await CallWithFallback(context, prompt, fieldKeys, ct);
        var outputText = TryGetResponseText(body);
        if (string.IsNullOrWhiteSpace(outputText))
            throw new InvalidOperationException($"OpenAI vision returned empty output_text. Model={model}. Response (first 500 chars): {body[..Math.Min(body.Length, 500)]}");

        Dictionary<string, (string Value, decimal Confidence)> parsed;
        try
        {
            parsed = ParseFieldsStrict(outputText);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"OpenAI vision returned unparseable JSON. Model={model}. Output: {outputText[..Math.Min(outputText.Length, 300)]}", ex);
        }

        var usage = TryGetUsage(body);
        var schemaKeySet = new HashSet<string>(fieldKeys, StringComparer.OrdinalIgnoreCase);
        var results = new List<ExtractionCandidate>();

        // Emit schema fields (preserves existing confidence/review logic in the background service)
        foreach (var key in fieldKeys)
        {
            if (!parsed.TryGetValue(key, out var item))
                continue;

            var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = model,
                ["technique"] = "openai-vision-extract",
                ["tenant"] = ProviderHelpers.TenantHash(context.TenantId)
            };

            if (usage is not null)
            {
                metadata["prompt_tokens"] = usage.Value.PromptTokens;
                metadata["completion_tokens"] = usage.Value.CompletionTokens;
                metadata["total_tokens"] = usage.Value.TotalTokens;
            }

            if (escalated)
            {
                metadata["escalated_from"] = modelNano;
                metadata["escalation_reason"] = escalationReason ?? "nano_failed";
            }

            results.Add(new ExtractionCandidate(
                key, item.Value, item.Confidence, Stage, Name, metadata));
        }

        // Emit discovered fields (keys not in schema, non-empty values only)
        foreach (var (key, item) in parsed)
        {
            if (schemaKeySet.Contains(key))
                continue;
            if (string.IsNullOrWhiteSpace(item.Value))
                continue;

            var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = model,
                ["technique"] = "openai-vision-extract",
                ["tenant"] = ProviderHelpers.TenantHash(context.TenantId),
                ["is_discovered"] = true
            };

            if (usage is not null)
            {
                metadata["prompt_tokens"] = usage.Value.PromptTokens;
                metadata["completion_tokens"] = usage.Value.CompletionTokens;
                metadata["total_tokens"] = usage.Value.TotalTokens;
            }

            if (escalated)
            {
                metadata["escalated_from"] = modelNano;
                metadata["escalation_reason"] = escalationReason ?? "nano_failed";
            }

            results.Add(new ExtractionCandidate(
                key, item.Value, item.Confidence, Stage, Name, metadata));
        }

        return results;
    }

    private async Task<(string Model, string Body, bool Escalated, string? Reason)> CallWithFallback(
        ExtractionContext context, string prompt, IReadOnlyCollection<string> expectedFields, CancellationToken ct)
    {
        // Phase 1: Try nano with retry for transient errors
        string? nanoBody = null;
        Exception? lastTransient = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                nanoBody = await CallVisionApi(modelNano, context, prompt, ct);
                lastTransient = null;
                break;
            }
            catch (TransientExtractionException tex)
            {
                lastTransient = tex;
                if (attempt < 2)
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(10_000, 500 * (1 << attempt))), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                // Hard error (e.g. 400 Bad Request) -- fail immediately, do not escalate
                throw;
            }
        }

        // If all nano retries failed with transient errors, escalate to mini
        if (nanoBody is null)
        {
            var reason = $"nano_transient_exhausted:{lastTransient?.GetType().Name}:{lastTransient?.Message}";
            var miniBody = await CallVisionApi(modelMini, context, prompt, ct);
            return (modelMini, miniBody, true, reason);
        }

        // Phase 2: Check nano response quality -- escalate on low confidence or empty output
        var outputText = TryGetResponseText(nanoBody);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            var miniBody = await CallVisionApi(modelMini, context, prompt, ct);
            return (modelMini, miniBody, true, "nano_empty_response");
        }

        Dictionary<string, (string Value, decimal Confidence)>? parsed = null;
        try
        {
            parsed = ParseFieldsStrict(outputText);
        }
        catch (JsonException)
        {
            // Unparseable -- escalate to mini
            var miniBody = await CallVisionApi(modelMini, context, prompt, ct);
            return (modelMini, miniBody, true, "nano_unparseable_json");
        }

        // Compute average confidence over expected fields only (ignore hallucinated keys)
        if (parsed.Count > 0)
        {
            // Sum confidence only for expected fields; missing fields count as 0
            var totalConfidence = expectedFields
                .Sum(key => parsed.TryGetValue(key, out var v) ? v.Confidence : 0m);
            var avgConfidence = totalConfidence / expectedFields.Count;
            if (avgConfidence < Worker.ReviewRequiredThreshold)
            {
                var matchedCount = expectedFields.Count(key => parsed.ContainsKey(key));
                var reason = $"low_confidence:nano_avg={avgConfidence:F4},matched={matchedCount}/{expectedFields.Count}";
                var miniBody = await CallVisionApi(modelMini, context, prompt, ct);
                return (modelMini, miniBody, true, reason);
            }
        }
        else
        {
            // No fields parsed -- escalate
            var miniBody = await CallVisionApi(modelMini, context, prompt, ct);
            return (modelMini, miniBody, true, "nano_no_fields_parsed");
        }

        return (modelNano, nanoBody, false, null);
    }

    private async Task<string> CallVisionApi(string model, ExtractionContext context, string prompt, CancellationToken ct)
    {
        var imageBase64 = Convert.ToBase64String(context.FileBytes);
        var mediaType = GetMediaType(context.OriginalFileKey);
        var dataUrl = $"data:{mediaType};base64,{imageBase64}";

        object fileContent = mediaType.StartsWith("image/", StringComparison.Ordinal)
            ? new { type = "input_image", image_url = dataUrl }
            : new { type = "input_file", filename = Path.GetFileName(context.OriginalFileKey), file_data = dataUrl };

        var requestPayload = new
        {
            model,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = prompt },
                        fileContent
                    }
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
                throw new TransientExtractionException($"OpenAI vision failed ({(int)res.StatusCode}).");
            throw new InvalidOperationException($"OpenAI vision failed ({(int)res.StatusCode}): {body}");
        }

        return body;
    }

    private static string GetMediaType(string fileKey)
    {
        var ext = Path.GetExtension(fileKey).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    private static string TryGetResponseText(string rawResponse)
    {
        using var doc = JsonDocument.Parse(rawResponse);

        // Prefer top-level output_text (convenience field)
        if (doc.RootElement.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
        {
            var text = ot.GetString();
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

    internal readonly record struct TokenUsage(long PromptTokens, long CompletionTokens, long TotalTokens);

    private static TokenUsage? TryGetUsage(string rawResponse)
    {
        using var doc = JsonDocument.Parse(rawResponse);
        if (!doc.RootElement.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return null;

        var input = usage.TryGetProperty("input_tokens", out var it) && it.ValueKind == JsonValueKind.Number ? it.GetInt64() : 0;
        var output = usage.TryGetProperty("output_tokens", out var ot) && ot.ValueKind == JsonValueKind.Number ? ot.GetInt64() : 0;
        var total = usage.TryGetProperty("total_tokens", out var tt) && tt.ValueKind == JsonValueKind.Number ? tt.GetInt64() : input + output;
        return new TokenUsage(input, output, total);
    }

    private static Dictionary<string, (string Value, decimal Confidence)> ParseFieldsStrict(string jsonText)
    {
        // Strip markdown fences if model wraps output
        var text = jsonText.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = text.Split('\n');
            var startIdx = lines[0].TrimEnd().Length > 3 ? 1 : 1;
            var endIdx = lines.Length - 1;
            if (endIdx > 0 && lines[endIdx].Trim() == "```")
                endIdx--;
            text = string.Join('\n', lines[startIdx..(endIdx + 1)]);
        }

        var map = new Dictionary<string, (string Value, decimal Confidence)>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected JSON object at root.");

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
                continue;

            string value;
            if (prop.Value.TryGetProperty("value", out var v))
            {
                value = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString() ?? string.Empty,
                    JsonValueKind.Null => string.Empty,
                    JsonValueKind.Array => JsonSerializer.Serialize(v),
                    _ => v.GetRawText()
                };
            }
            else
            {
                value = string.Empty;
            }

            var confidence = prop.Value.TryGetProperty("confidence", out var c) && c.ValueKind is JsonValueKind.Number
                ? Math.Clamp(c.GetDecimal(), 0m, 1m)
                : 0.4m;

            map[prop.Name] = (value, Math.Round(confidence, 4, MidpointRounding.ToZero));
        }

        return map;
    }
}
