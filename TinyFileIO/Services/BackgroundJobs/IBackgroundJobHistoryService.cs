using TinyFileIO.Models.Entities;

namespace TinyFileIO.Services.BackgroundJobs;

public interface IBackgroundJobHistoryService
{
    Task<BackgroundJobHistoryPage> GetPageAsync(int page, int pageSize, CancellationToken ct = default);
}

public sealed class BackgroundJobHistoryPage
{
    public IReadOnlyList<BackgroundJobRun> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
