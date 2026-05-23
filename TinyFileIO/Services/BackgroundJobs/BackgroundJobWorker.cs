using Microsoft.EntityFrameworkCore;
using TinyFileIO.Data;
using TinyFileIO.Models.Entities;

namespace TinyFileIO.Services.BackgroundJobs;

public sealed class BackgroundJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundJobWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    public BackgroundJobWorker(IServiceScopeFactory scopeFactory, ILogger<BackgroundJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchQueuedJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background job dispatch failed.");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private async Task DispatchQueuedJobsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var jobs = scope.ServiceProvider.GetServices<IBackgroundJob>().ToDictionary(j => j.JobType, StringComparer.OrdinalIgnoreCase);
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var queued = await db.BackgroundJobRuns
            .Where(j => j.Status == BackgroundJobStatuses.Queued)
            .OrderBy(j => j.CreatedUtc)
            .Take(50)
            .ToListAsync(ct);

        foreach (var run in queued)
        {
            if (!jobs.ContainsKey(run.JobType))
            {
                run.Status = BackgroundJobStatuses.Failed;
                run.StartedUtc = DateTime.UtcNow;
                run.FinishedUtc = run.StartedUtc;
                run.Error = $"No background job handler is registered for '{run.JobType}'.";
                continue;
            }

            var maxParallel = Math.Max(1, config.GetValue<int?>($"BackgroundJobs:MaxParallelPerType:{run.JobType}")
                ?? config.GetValue<int?>("BackgroundJobs:DefaultMaxParallelPerType")
                ?? 2);

            var runningCount = await db.BackgroundJobRuns.CountAsync(j =>
                j.JobType == run.JobType && j.Status == BackgroundJobStatuses.Running, ct);

            if (runningCount >= maxParallel)
                continue;

            run.Status = BackgroundJobStatuses.Running;
            run.StartedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            _ = Task.Run(() => RunJobAsync(run.Id, ct), CancellationToken.None);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task RunJobAsync(Guid runId, CancellationToken hostStoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

            await using var db = await dbFactory.CreateDbContextAsync(hostStoppingToken);
            var run = await db.BackgroundJobRuns.FirstAsync(j => j.Id == runId, hostStoppingToken);
            var job = scope.ServiceProvider.GetServices<IBackgroundJob>().First(j => string.Equals(j.JobType, run.JobType, StringComparison.OrdinalIgnoreCase));
            var context = new BackgroundJobContext(run, (progress, ct) => UpdateProgressAsync(dbFactory, runId, progress, ct));

            await job.ExecuteAsync(context, hostStoppingToken);

            await using var finishDb = await dbFactory.CreateDbContextAsync(hostStoppingToken);
            var finishedRun = await finishDb.BackgroundJobRuns.FirstAsync(j => j.Id == runId, hostStoppingToken);
            finishedRun.Status = BackgroundJobStatuses.Succeeded;
            finishedRun.FinishedUtc = DateTime.UtcNow;
            await finishDb.SaveChangesAsync(hostStoppingToken);
        }
        catch (OperationCanceledException) when (hostStoppingToken.IsCancellationRequested)
        {
            await MarkFailedAsync(runId, BackgroundJobStatuses.Canceled, "Application shutdown canceled the job.", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background job {RunId} failed.", runId);
            await MarkFailedAsync(runId, BackgroundJobStatuses.Failed, ex.Message, CancellationToken.None);
        }
    }

    private static async Task UpdateProgressAsync(
        IDbContextFactory<AppDbContext> dbFactory,
        Guid runId,
        BackgroundJobProgress progress,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = await db.BackgroundJobRuns.FirstAsync(j => j.Id == runId, ct);

        if (progress.TotalItems.HasValue) run.TotalItems = progress.TotalItems.Value;
        if (progress.ProcessedItems.HasValue) run.ProcessedItems = progress.ProcessedItems.Value;
        if (progress.SucceededItems.HasValue) run.SucceededItems = progress.SucceededItems.Value;
        if (progress.SkippedItems.HasValue) run.SkippedItems = progress.SkippedItems.Value;
        if (progress.FailedItems.HasValue) run.FailedItems = progress.FailedItems.Value;
        if (progress.BytesProcessed.HasValue) run.BytesProcessed = progress.BytesProcessed.Value;
        if (progress.StatsJson is not null) run.StatsJson = progress.StatsJson;

        await db.SaveChangesAsync(ct);
    }

    private async Task MarkFailedAsync(Guid runId, string status, string error, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = await db.BackgroundJobRuns.FirstOrDefaultAsync(j => j.Id == runId, ct);
        if (run is null) return;
        run.Status = status;
        run.Error = error;
        run.FinishedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
