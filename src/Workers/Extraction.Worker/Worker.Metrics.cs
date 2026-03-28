using System.Threading;

namespace Extraction.Worker;

internal sealed class WorkerMetrics
{
    private long _extractionSuccessCount;
    private long _extractionFailureCount;

    public void IncrementExtractionSuccess() => Interlocked.Increment(ref _extractionSuccessCount);
    public void IncrementExtractionFailure() => Interlocked.Increment(ref _extractionFailureCount);

    public long ExtractionSuccessCount => Interlocked.Read(ref _extractionSuccessCount);
    public long ExtractionFailureCount => Interlocked.Read(ref _extractionFailureCount);
}
