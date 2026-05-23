namespace TinyFileIO.Services.BackgroundJobs;

public interface IBackgroundJob
{
    string JobType { get; }

    Task ExecuteAsync(BackgroundJobContext context, CancellationToken ct);
}
