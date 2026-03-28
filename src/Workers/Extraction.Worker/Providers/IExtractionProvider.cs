using System.Security.Cryptography;
using System.Text;

namespace Extraction.Worker;

internal readonly record struct ExtractionContext(
    Guid DocumentId,
    string TenantId,
    string TemplateId,
    string OriginalFileKey,
    string LocalFilePath,
    int ByteLength,
    byte[] FileBytes);

internal sealed record ExtractionCandidate(
    string FieldKey,
    string Value,
    decimal Confidence,
    string Stage,
    string Provider,
    Dictionary<string, object>? Metadata = null);

internal sealed record FieldExtractionResult(
    string FieldKey,
    string FinalValue,
    decimal SystemConfidence,
    string NormalizedValue,
    IReadOnlyList<ExtractionCandidate> AllAttempts,
    IReadOnlyList<string> ProviderSequence);

internal interface IExtractionProvider
{
    string Name { get; }
    string Stage { get; }
    int Order { get; }

    Task<IReadOnlyList<ExtractionCandidate>> ExtractAsync(
        ExtractionContext context,
        IReadOnlyCollection<string> fieldKeys,
        IReadOnlyDictionary<string, List<ExtractionCandidate>>? priorAttempts,
        CancellationToken ct);
}

internal sealed class TransientExtractionException(string message) : Exception(message)
{
}

internal static class ProviderHelpers
{
    public static string TenantHash(string tenantId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(tenantId));
        return Convert.ToHexString(hash.AsSpan(0, 4));
    }
}
