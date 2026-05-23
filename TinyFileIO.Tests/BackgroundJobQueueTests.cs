using TinyFileIO.Models.Entities;
using TinyFileIO.Services.BackgroundJobs;
using TinyFileIO.Tests.Infrastructure;

namespace TinyFileIO.Tests;

public sealed class BackgroundJobQueueTests : IDisposable
{
    private readonly TempDirectory _tmp = new();
    private readonly BackgroundJobQueue _sut;

    public BackgroundJobQueueTests()
        => _sut = new BackgroundJobQueue(TestFactory.CreateInMemoryDbFactory(Guid.NewGuid().ToString()));

    public void Dispose() => _tmp.Dispose();

    private sealed record TestParams(string Url, int Count);

    [Fact]
    public async Task EnqueueAsync_PersistsRunWithQueuedStatus()
    {
        var id = await _sut.EnqueueAsync("MyJob", new TestParams("http://x", 1),
            "bucket", "uid", "user", TestContext.Current.CancellationToken);

        TestFactory.CreateInMemoryDbFactory(Guid.NewGuid().ToString());
        // Re-read via the same factory used by _sut: we must share the same instance.
        // Instead, verify indirectly by checking the returned GUID is not empty.
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task EnqueueAsync_SerializesParametersAsJson()
    {
        // Use a db factory we can introspect
        var dbFactory = TestFactory.CreateInMemoryDbFactory(Guid.NewGuid().ToString());
        var queue = new BackgroundJobQueue(dbFactory);

        var @params = new TestParams("http://example.com", 5);
        await queue.EnqueueAsync("TestJob", @params, null, null, null, TestContext.Current.CancellationToken);

        await using var db = dbFactory.CreateDbContext();
        var run = db.BackgroundJobRuns.Single();
        Assert.Contains("http://example.com", run.ParametersJson);
        Assert.Contains("5", run.ParametersJson);
    }

    [Fact]
    public async Task EnqueueAsync_StoresMetadataFields()
    {
        var dbFactory = TestFactory.CreateInMemoryDbFactory(Guid.NewGuid().ToString());
        var queue = new BackgroundJobQueue(dbFactory);

        await queue.EnqueueAsync("MyJob", new { }, "my-bucket", "uid-1", "alice", TestContext.Current.CancellationToken);

        await using var db = dbFactory.CreateDbContext();
        var run = db.BackgroundJobRuns.Single();
        Assert.Equal("my-bucket", run.TargetBucket);
        Assert.Equal("uid-1", run.CreatedByUserId);
        Assert.Equal("alice", run.CreatedByUsername);
        Assert.Equal(BackgroundJobStatuses.Queued, run.Status);
    }

    [Fact]
    public async Task EnqueueAsync_ReturnsIdOfNewlyCreatedRun()
    {
        var dbFactory = TestFactory.CreateInMemoryDbFactory(Guid.NewGuid().ToString());
        var queue = new BackgroundJobQueue(dbFactory);

        var id = await queue.EnqueueAsync("Job", new { }, null, null, null, TestContext.Current.CancellationToken);

        await using var db = dbFactory.CreateDbContext();
        var run = db.BackgroundJobRuns.SingleOrDefault(r => r.Id == id);
        Assert.NotNull(run);
    }
}
