using System.Text.Json;

namespace Extraction.Worker;

/// <summary>
/// Shared parser for OpenAI Responses API output.
/// Both VisionProvider and NormalizerProvider return the same response shape;
/// this helper extracts the text content from either the convenience
/// <c>output_text</c> field or the nested <c>output[].content[].text</c> path.
/// </summary>
internal static class OpenAiResponseParser
{
    /// <summary>
    /// Extract the output text from an OpenAI Responses API JSON body.
    /// Returns <see cref="string.Empty"/> when no text is found.
    /// </summary>
    public static string ExtractOutputText(string rawResponse)
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
}
