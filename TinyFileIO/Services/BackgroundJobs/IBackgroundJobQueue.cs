namespace TinyFileIO.Services.BackgroundJobs;

public interface IBackgroundJobQueue
{
    Task<Guid> EnqueueAsync<TParameters>(
        string jobType,
        TParameters parameters,
        string? targetBucket,
        string? createdByUserId,
        string? createdByUsername,
        CancellationToken ct = default);
}
