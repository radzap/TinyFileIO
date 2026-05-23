using TinyFileIO.Models.Entities;

namespace TinyFileIO.Services.BackgroundJobs;

public sealed class BackgroundJobContext
{
    private readonly Func<BackgroundJobProgress, CancellationToken, Task> _reportProgress;

    public BackgroundJobContext(BackgroundJobRun run, Func<BackgroundJobProgress, CancellationToken, Task> reportProgress)
    {
        Run = run;
        _reportProgress = reportProgress;
    }

    public BackgroundJobRun Run { get; }

    public Task ReportProgressAsync(BackgroundJobProgress progress, CancellationToken ct = default)
        => _reportProgress(progress, ct);
}

public sealed class BackgroundJobProgress
{
    public int? TotalItems { get; init; }
    public int? ProcessedItems { get; init; }
    public int? SucceededItems { get; init; }
    public int? SkippedItems { get; init; }
    public int? FailedItems { get; init; }
    public long? BytesProcessed { get; init; }
    public string? StatsJson { get; init; }
}
