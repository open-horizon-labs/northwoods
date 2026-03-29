using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Northwoods.Contracts;

/// <summary>
/// Shared OpenAI embedding generation and pgvector formatting.
/// Used by both the API (finalization) and the extraction worker (initial embed).
/// </summary>
public static class EmbeddingService
{
    public const int CaseEmbeddingDimensions = 1536;

    /// <summary>
    /// Generate an embedding vector for the given text using OpenAI text-embedding-3-small.
    /// </summary>
    /// <param name="httpClient">An HttpClient instance to use for the request.</param>
    /// <param name="text">The text to embed.</param>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The embedding vector, prompt token count, and total token count.</returns>
    public static async Task<(double[] Embedding, long PromptTokens, long TotalTokens)> GenerateCaseEmbeddingAsync(
        HttpClient httpClient, string text, string apiKey, CancellationToken ct)
    {
        var requestPayload = new
        {
            model = "text-embedding-3-small",
            input = text
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var res = await httpClient.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            var isTransient = (int)res.StatusCode >= 500
                              || res.StatusCode == HttpStatusCode.TooManyRequests
                              || res.StatusCode == HttpStatusCode.RequestTimeout;
            if (isTransient)
                throw new InvalidOperationException($"OpenAI embedding transient failure ({(int)res.StatusCode}).");
            throw new InvalidOperationException($"OpenAI embedding failed ({(int)res.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var dataArray = doc.RootElement.GetProperty("data");
        var embeddingElement = dataArray[0].GetProperty("embedding");
        var values = new double[CaseEmbeddingDimensions];
        var idx = 0;
        foreach (var el in embeddingElement.EnumerateArray())
        {
            if (idx < values.Length)
                values[idx++] = el.GetDouble();
        }

        long promptTokens = 0, totalTokens = 0;
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number)
                promptTokens = pt.GetInt64();
            if (usage.TryGetProperty("total_tokens", out var tt) && tt.ValueKind == JsonValueKind.Number)
                totalTokens = tt.GetInt64();
        }

        return (values, promptTokens, totalTokens);
    }

    /// <summary>
    /// Format a double array as a pgvector literal string: [v1,v2,...,vN]
    /// </summary>
    public static string ToPgVectorLiteral(double[] values)
    {
        return $"[{string.Join(',', values.Select(v => v.ToString(CultureInfo.InvariantCulture)))}]";
    }
}
