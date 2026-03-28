using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Extraction.Worker;

internal sealed partial class PaddleOcrProvider(string pythonPath, string scriptPath) : IExtractionProvider
{
    public string Name => "paddleocr";
    public string Stage => "ocr";
    public int Order => 0;

    public async Task<IReadOnlyList<ExtractionCandidate>> ExtractAsync(
        ExtractionContext context,
        IReadOnlyCollection<string> fieldKeys,
        IReadOnlyDictionary<string, List<ExtractionCandidate>>? priorAttempts,
        CancellationToken ct)
    {
        var script = ResolveScriptPath(scriptPath);
        if (!File.Exists(script))
        {
            throw new InvalidOperationException($"Paddle OCR script not found at '{script}'.");
        }

        var sw = Stopwatch.StartNew();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{script}\" --file \"{context.LocalFilePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        sw.Stop();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Paddle OCR failed with exit code {process.ExitCode}: {stderr}");
        }

        using var doc = JsonDocument.Parse(stdout);
        var text = doc.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? string.Empty
            : string.Empty;

        var candidates = new List<ExtractionCandidate>(fieldKeys.Count);
        foreach (var key in fieldKeys)
        {
            var value = InferFieldValue(key, text);
            var confidence = InferConfidence(key, text, value);
            candidates.Add(new ExtractionCandidate(
                key,
                value,
                confidence,
                Stage,
                Name,
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["script"] = Path.GetFileName(script),
                    ["file_size"] = context.ByteLength,
                    ["tenant"] = ProviderHelpers.TenantHash(context.TenantId),
                    ["technique"] = "paddleocr+label-regex",
                    ["processing_ms"] = sw.ElapsedMilliseconds
                }));
        }

        return candidates;
    }

    private static string ResolveScriptPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var baseDir = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDir, configuredPath));
    }

    private static string InferFieldValue(string fieldKey, string text)
    {
        var normalized = text.Replace("\r", "\n", StringComparison.Ordinal);

        return fieldKey switch
        {
            "applicantName" => CaptureLabel(normalized, ["applicant name", "name"]),
            "dateOfBirth" => CaptureLabel(normalized, ["date of birth", "dob"], DateRegex()),
            "address" => CaptureLabel(normalized, ["address"]),
            "householdSize" => CaptureLabel(normalized, ["household size"], NumericRegex()),
            "monthlyIncome" => CaptureLabel(normalized, ["monthly income", "income"], CurrencyRegex()),
            "requestedServices" => CaptureLabel(normalized, ["requested services", "services"], null, maxWords: 16),
            "notes" => CaptureLabel(normalized, ["notes", "comments"], null, maxWords: 24),
            _ => string.Empty
        };
    }

    private static decimal InferConfidence(string fieldKey, string text, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0.35m;
        }

        var lower = text.ToLowerInvariant();
        var hasExplicitLabel = fieldKey switch
        {
            "applicantName" => lower.Contains("applicant name", StringComparison.Ordinal),
            "dateOfBirth" => lower.Contains("date of birth", StringComparison.Ordinal) || lower.Contains("dob", StringComparison.Ordinal),
            "address" => lower.Contains("address", StringComparison.Ordinal),
            "householdSize" => lower.Contains("household size", StringComparison.Ordinal),
            "monthlyIncome" => lower.Contains("monthly income", StringComparison.Ordinal) || lower.Contains("income", StringComparison.Ordinal),
            "requestedServices" => lower.Contains("requested services", StringComparison.Ordinal) || lower.Contains("services", StringComparison.Ordinal),
            "notes" => lower.Contains("notes", StringComparison.Ordinal),
            _ => false
        };

        var baseConfidence = hasExplicitLabel ? 0.84m : 0.68m;
        if (value.Length > 60)
        {
            baseConfidence -= 0.08m;
        }

        return Math.Round(Math.Clamp(baseConfidence, 0.20m, 0.95m), 4, MidpointRounding.ToZero);
    }

    private static string CaptureLabel(string text, string[] labels, Regex? preferredValueRegex = null, int maxWords = 8)
    {
        foreach (var label in labels)
        {
            var lineRegex = new Regex($@"(?im)^\s*{Regex.Escape(label)}\s*[:\-]\s*(?<value>.+)$", RegexOptions.Compiled);
            var match = lineRegex.Match(text);
            if (match.Success)
            {
                var candidate = match.Groups["value"].Value.Trim();
                if (preferredValueRegex is null)
                {
                    return TrimWords(candidate, maxWords);
                }

                var preferred = preferredValueRegex.Match(candidate);
                return preferred.Success ? preferred.Value.Trim() : TrimWords(candidate, maxWords);
            }
        }

        if (preferredValueRegex is not null)
        {
            var fallback = preferredValueRegex.Match(text);
            if (fallback.Success)
            {
                return fallback.Value.Trim();
            }
        }

        return string.Empty;
    }

    private static string TrimWords(string value, int maxWords)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length <= maxWords)
        {
            return value.Trim();
        }

        return string.Join(' ', words.Take(maxWords));
    }

    [GeneratedRegex(@"\b\d{1,2}/\d{1,2}/\d{2,4}\b", RegexOptions.Compiled)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\$?[0-9][0-9,]*(?:\.[0-9]{2})?", RegexOptions.Compiled)]
    private static partial Regex CurrencyRegex();

    [GeneratedRegex(@"\b\d+\b", RegexOptions.Compiled)]
    private static partial Regex NumericRegex();
}
