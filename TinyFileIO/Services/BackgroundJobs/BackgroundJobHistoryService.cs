using Microsoft.EntityFrameworkCore;
using TinyFileIO.Data;

namespace TinyFileIO.Services.BackgroundJobs;

public sealed class BackgroundJobHistoryService : IBackgroundJobHistoryService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public BackgroundJobHistoryService(IDbContextFactory<AppDbContext> dbFactory)
        => _dbFactory = dbFactory;

    public async Task<BackgroundJobHistoryPage> GetPageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.BackgroundJobRuns.AsNoTracking().OrderByDescending(j => j.CreatedUtc);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new BackgroundJobHistoryPage
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }
}
