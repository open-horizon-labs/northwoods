namespace Northwoods.Contracts;

public enum UserRole
{
    IntakeWorker,
    Reviewer,
    Admin
}

public enum ProcessingStatus
{
    Uploaded,
    Extracting,
    ReviewReady,
    Completed,
    Finalized,
    Failed
}

public sealed record LoginRequest(string Email, string Password, string? TenantId = null, UserRole? Role = null);

public sealed record LoginResponse(string AccessToken, string TenantId, UserRole Role);

public sealed record CreateIntakeRequest(string TemplateId, string FileName);

public sealed record CreateIntakeResponse(Guid IntakeId, ProcessingStatus Status);

public sealed record ConfidenceField(string FieldKey, string Value, decimal Confidence, bool RequiresReview);

public sealed record IntakeStatusResponse(
    Guid IntakeId,
    string TenantId,
    string TemplateId,
    ProcessingStatus Status,
    IReadOnlyList<ConfidenceField> Fields);

public sealed record ReviewQueueItem(Guid ReviewId, Guid IntakeId, string ApplicantName, string TemplateId, int UncertainFieldCount, DateTimeOffset UploadDate);

public sealed record SimilarCaseItem(
    Guid ReviewId,
    Guid IntakeId,
    string ApplicantName,
    string TemplateId,
    decimal MatchScore,
    string Summary);

public sealed record ReviewDetailResponse(
    Guid ReviewId,
    Guid IntakeId,
    string TenantId,
    string TemplateId,
    string SourceDocumentUrl,
    ProcessingStatus Status,
    IReadOnlyList<ConfidenceField> Fields,
    IReadOnlyList<SimilarCaseItem> SimilarCases,
    IReadOnlyList<string> AuditEvents);
public sealed record FinalizeReviewRequest(IReadOnlyList<ConfidenceField> Fields, string ReviewerNote);

public sealed record FinalizeReviewResponse(Guid ReviewId, ProcessingStatus Status);

public sealed record TemplateField(string Key, string Type, bool Required = false);

public sealed record TemplateDescriptor(
    string Id,
    string Name,
    IReadOnlyList<TemplateField> Fields,
    bool IsArchived = false,
    string? BlankPdfKey = null);

public sealed record CreateTemplateRequest(
    string Id,
    string Name,
    IReadOnlyList<TemplateField> Fields);

public sealed record UpdateTemplateRequest(
    string? Name,
    IReadOnlyList<TemplateField>? Fields);

public sealed record SearchResultItem(
    Guid IntakeId,
    string TemplateId,
    string ApplicantName,
    string Status,
    decimal Confidence,
    string Snippet);

public sealed record SearchResponse(string Query, IReadOnlyList<SearchResultItem> Results);

public sealed record CaseDocumentItem(
    Guid IntakeId,
    string TemplateId,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ConfidenceField> Fields);

public sealed record CaseAggregateResponse(
    string PersonKey,
    IReadOnlyList<CaseDocumentItem> Documents);