using System;
using System.Collections.Generic;
using System.Linq;
using Northwoods.Contracts;
using Xunit;

namespace Northwoods.Worker.UnitTests;

public class CaseProfileTextTests
{
    [Fact]
    public void BuildFinalized_IncludesCorrectedFieldValues()
    {
        var fields = new List<ConfidenceField>
        {
            new("applicantName", "Jamie A. Carter", 0.95m, false),
            new("dateOfBirth", "03/15/1988", 0.92m, false),
        };
        var ocrSegments = new[]
        {
            ("applicantName", "JAMIE CARTER"),
            ("dateOfBirth", "3/15/88"),
        };

        var result = CaseProfileText.BuildFinalized("general-assistance", fields, ocrSegments, null);

        Assert.Contains("applicantName: Jamie A. Carter", result);
        Assert.Contains("dateOfBirth: 03/15/1988", result);
        Assert.Contains("applicantName: JAMIE CARTER", result);
        Assert.Contains("template=general-assistance", result);
        Assert.DoesNotContain("reviewer_note", result);
    }

    [Fact]
    public void BuildFinalized_IncludesReviewerNote()
    {
        var fields = new List<ConfidenceField>
        {
            new("applicantName", "Jamie Carter", 0.90m, false),
        };
        var ocrSegments = Array.Empty<(string, string)>();

        var result = CaseProfileText.BuildFinalized(
            "general-assistance", fields, ocrSegments,
            "Name had middle initial visible on re-examination");

        Assert.Contains("applicantName: Jamie Carter", result);
        Assert.Contains("reviewer_note=Name had middle initial visible on re-examination", result);
    }

    [Fact]
    public void BuildFinalized_EmptyReviewerNote_OmitsSection()
    {
        var fields = new List<ConfidenceField>
        {
            new("applicantName", "Jamie Carter", 0.90m, false),
        };
        var ocrSegments = Array.Empty<(string, string)>();

        var result = CaseProfileText.BuildFinalized("general-assistance", fields, ocrSegments, "");

        Assert.DoesNotContain("reviewer_note", result);
    }

    [Fact]
    public void BuildFinalized_WhitespaceOnlyReviewerNote_OmitsSection()
    {
        var fields = new List<ConfidenceField>
        {
            new("applicantName", "Jamie Carter", 0.90m, false),
        };

        var result = CaseProfileText.BuildFinalized("general-assistance", fields, Array.Empty<(string, string)>(), "   ");

        Assert.DoesNotContain("reviewer_note", result);
    }

    [Fact]
    public void BuildFinalized_NoFields_ShowsPlaceholder()
    {
        var result = CaseProfileText.BuildFinalized(
            "general-assistance",
            Array.Empty<ConfidenceField>(),
            Array.Empty<(string, string)>(),
            "Some reviewer comment");

        Assert.Contains("fields=(no fields)", result);
        Assert.Contains("ocr=(no ocr segments)", result);
        Assert.Contains("reviewer_note=Some reviewer comment", result);
    }

    [Fact]
    public void BuildFinalized_DeduplicatesOcrSegments()
    {
        var fields = new List<ConfidenceField>
        {
            new("name", "Test", 0.90m, false),
        };
        var ocrSegments = new[]
        {
            ("name", "TEST"),
            ("name", "TEST"),  // duplicate
            ("name", "test"),  // case-insensitive duplicate
        };

        var result = CaseProfileText.BuildFinalized("test-template", fields, ocrSegments, null);

        // Should only contain one OCR segment for "name: TEST"
        var ocrSection = result.Split("ocr=")[1].Split(";")[0];
        Assert.Equal("name: TEST", ocrSection);
    }

    [Fact]
    public void BuildFinalized_LimitsOcrSegmentsToTwelve()
    {
        var fields = new List<ConfidenceField>
        {
            new("name", "Test", 0.90m, false),
        };
        var ocrSegments = Enumerable.Range(1, 20)
            .Select(i => ($"field{i}", $"value{i}"))
            .ToArray();

        var result = CaseProfileText.BuildFinalized("test-template", fields, ocrSegments, null);

        var ocrSection = result.Split("ocr=")[1].Split(";")[0];
        var pipeCount = ocrSection.Count(c => c == '|');
        // 12 segments joined by " | " means 11 pipes
        Assert.Equal(11, pipeCount);
    }
}
