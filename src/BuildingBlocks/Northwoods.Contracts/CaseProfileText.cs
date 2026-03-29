using System;
using System.Collections.Generic;
using System.Linq;

namespace Northwoods.Contracts;

/// <summary>
/// Builds search text for case profiles from extraction results and reviewer input.
/// </summary>
public static class CaseProfileText
{
    /// <summary>
    /// Builds search text incorporating finalized field values, OCR segments, reviewer note,
    /// and any discovered (non-schema) fields so they remain searchable after finalization.
    /// </summary>
    public static string BuildFinalized(
        string templateId,
        IReadOnlyList<ConfidenceField> fields,
        IEnumerable<(string FieldKey, string RawValue)> ocrSegments,
        string? reviewerNote,
        IEnumerable<(string FieldKey, string Value)>? discoveredFields = null)
    {
        var fieldPairs = fields
            .Select(f => $"{f.FieldKey}: {f.Value}")
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        var ocrPairs = ocrSegments
            .Select(o => $"{o.FieldKey}: {o.RawValue}")
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        var fieldText = fieldPairs.Length > 0 ? string.Join(" | ", fieldPairs) : "(no fields)";
        var ocrText = ocrPairs.Length > 0 ? string.Join(" | ", ocrPairs) : "(no ocr segments)";
        var result = $"template={templateId}; fields={fieldText}; ocr={ocrText}";

        if (!string.IsNullOrWhiteSpace(reviewerNote))
            result += $"; reviewer_note={reviewerNote}";

        if (discoveredFields is not null)
        {
            var discoveredPairs = discoveredFields
                .Select(d => $"{d.FieldKey}: {d.Value}")
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();

            if (discoveredPairs.Length > 0)
                result += $"; discovered={string.Join(" | ", discoveredPairs)}";
        }

        return result;
    }
}
