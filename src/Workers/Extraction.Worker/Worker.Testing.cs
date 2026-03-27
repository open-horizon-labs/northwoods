using System.Threading;
using System.Threading.Tasks;

namespace Extraction.Worker;

public sealed partial class Worker
{
    internal static decimal GetHighConfidenceThreshold() => HighConfidenceThreshold;

    internal static decimal GetReviewRequiredThreshold() => ReviewRequiredThreshold;

    internal static decimal GetEscalateThreshold() => EscalateThreshold;

    internal static bool RequiresReview(decimal confidence) => confidence < ReviewRequiredThreshold;

    internal static bool IsEscalatable(decimal confidence) => confidence < EscalateThreshold;

    internal static bool IsAutoAccept(decimal confidence) => confidence >= HighConfidenceThreshold;

    internal static FieldExtractionResult ResolveConsensusForTests(string key, IReadOnlyList<ExtractionCandidate> attempts)
        => FieldConsensus.Resolve(key, attempts);

    internal static Task<IReadOnlyList<FieldExtractionResult>> RunExtractionPipelineForTests(
        ExtractionContext context,
        IReadOnlyList<string> fieldKeys,
        IReadOnlyList<IExtractionProvider> providers,
        CancellationToken cancellationToken)
        => RunExtractionPipeline(context, fieldKeys, providers, cancellationToken);

}
