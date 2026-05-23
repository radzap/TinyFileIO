using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TinyFileIO.Data;
using TinyFileIO.Models.Entities;

namespace TinyFileIO.Services.BackgroundJobs;

public sealed class BackgroundJobQueue : IBackgroundJobQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public BackgroundJobQueue(IDbContextFactory<AppDbContext> dbFactory)
        => _dbFactory = dbFactory;

    public async Task<Guid> EnqueueAsync<TParameters>(
        string jobType,
        TParameters parameters,
        string? targetBucket,
        string? createdByUserId,
        string? createdByUsername,
        CancellationToken ct = default)
    {
        var run = new BackgroundJobRun
        {
            JobType = jobType,
            Status = BackgroundJobStatuses.Queued,
            TargetBucket = targetBucket,
            CreatedByUserId = createdByUserId,
            CreatedByUsername = createdByUsername,
            ParametersJson = JsonSerializer.Serialize(parameters, JsonOptions)
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.BackgroundJobRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run.Id;
    }
}
