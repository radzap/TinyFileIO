using TinyFileIO.Models.Api.Multipart;
using TinyFileIO.Services;
using TinyFileIO.Services.BackgroundJobs;
using TinyFileIO.Tests.Infrastructure;

namespace TinyFileIO.Tests;

public sealed class FileSystemMultipartServiceTests : IDisposable
{
    private readonly TempDirectory _tmp = new();
    private readonly FileSystemMultipartService _sut;

    public FileSystemMultipartServiceTests()
        => _sut = new FileSystemMultipartService(TestFactory.ConfigFor(_tmp.Path));

    public void Dispose() => _tmp.Dispose();

    private void CreateBucket(string name) => _tmp.Sub(name);

    private async Task<string> StartUploadAsync(string bucket, string key = "obj.bin",
        CancellationToken ct = default)
    {
        var resp = await _sut.CreateMultipartUploadAsync(
            new CreateMultipartUploadRequest { Bucket = bucket, Key = key }, ct);
        return resp.UploadId;
    }

    private async Task<string> UploadPartAsync(string bucket, string uploadId, int part, byte[] data,
        CancellationToken ct = default)
    {
        var resp = await _sut.UploadPartAsync(
            new UploadPartRequest { Bucket = bucket, UploadId = uploadId, PartNumber = part },
            new MemoryStream(data), ct);
        return resp.ETag;
    }

    // ── CreateMultipartUpload ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateMultipartUploadAsync_CreatesStagingDirAndMetaJson()
    {
        CreateBucket("b");
        var uploadId = await StartUploadAsync("b", ct: TestContext.Current.CancellationToken);

        var stagingDir = Path.Combine(_tmp.Path, "b", FileSystemMultipartService.StagingDirName, uploadId);
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(Path.Combine(stagingDir, "meta.json")));
    }

    [Fact]
    public async Task CreateMultipartUploadAsync_ReturnsUniqueUploadIds()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        var id1 = await StartUploadAsync("b", ct: ct);
        var id2 = await StartUploadAsync("b", ct: ct);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task CreateMultipartUploadAsync_MissingBucket_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.CreateMultipartUploadAsync(
                new CreateMultipartUploadRequest { Bucket = "ghost", Key = "k" }, TestContext.Current.CancellationToken));
    }

    // ── UploadPart ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadPartAsync_WritesPartFileToStagingDir()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        var uploadId = await StartUploadAsync("b", ct: ct);
        var data = "part-data"u8.ToArray();

        await UploadPartAsync("b", uploadId, 1, data, ct);

        var partFile = Path.Combine(_tmp.Path, "b", FileSystemMultipartService.StagingDirName, uploadId, "00001");
        Assert.True(File.Exists(partFile));
        Assert.Equal(data, await File.ReadAllBytesAsync(partFile, ct));
    }

    [Fact]
    public async Task UploadPartAsync_ReturnsMd5ETag()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        var uploadId = await StartUploadAsync("b", ct: ct);
        var etag = await UploadPartAsync("b", uploadId, 1, "data"u8.ToArray(), ct);
        Assert.Matches("^\"[0-9a-f]{32}\"$", etag);
    }

    [Fact]
    public async Task UploadPartAsync_UnknownUploadId_ThrowsKeyNotFoundException()
    {
        CreateBucket("b");
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UploadPartAsync(
                new UploadPartRequest { Bucket = "b", UploadId = "nope", PartNumber = 1 },
                new MemoryStream("x"u8.ToArray()), TestContext.Current.CancellationToken));
    }

    // ── CompleteMultipartUpload ───────────────────────────────────────────────

    [Fact]
    public async Task CompleteMultipartUploadAsync_ConcatenatesPartsInOrder()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        var uploadId = await StartUploadAsync("b", "obj.txt", ct);
        var e1 = await UploadPartAsync("b", uploadId, 1, "Hello "u8.ToArray(), ct);
        var e2 = await UploadPartAsync("b", uploadId, 2, "World"u8.ToArray(), ct);

        await _sut.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            Bucket = "b", Key = "obj.txt", UploadId = uploadId,
            Parts = [new() { PartNumber = 1, ETag = e1 }, new() { PartNumber = 2, ETag = e2 }]
        }, ct);

        var content = await File.ReadAllTextAsync(Path.Combine(_tmp.Path, "b", "obj.txt"), ct);
        Assert.Equal("Hello World", content);
    }

    [Fact]
    public async Task CompleteMultipartUploadAsync_RemovesStagingDirectory()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        var uploadId = await StartUploadAsync("b", ct: ct);
        var e1 = await UploadPartAsync("b", uploadId, 1, "x"u8.ToArray(), ct);

        await _sut.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            Bucket = "b", Key = "obj.bin", UploadId = uploadId,
            Parts = [new() { PartNumber = 1, ETag = e1 }]
        }, ct);

        var stagingDir = Path.Combine(_tmp.Path, "b", FileSystemMultipartService.StagingDirName, uploadId);
        Assert.False(Directory.Exists(stagingDir));
    }

    [Fact]
    public async Task CompleteMultipartUploadAsync_ReturnsCompositeETag()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        var uploadId = await StartUploadAsync("b", ct: ct);
        var e1 = await UploadPartAsync("b", uploadId, 1, "x"u8.ToArray(), ct);

        var resp = await _sut.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            Bucket = "b", Key = "obj.bin", UploadId = uploadId,
            Parts = [new() { PartNumber = 1, ETag = e1 }]
        }, ct);

        // Composite ETag format: "md5-partCount"
        Assert.Matches("^\"[0-9a-f]{32}-\\d+\"$", resp.ETag);
    }

    [Fact]
    public async Task CompleteMultipartUploadAsync_UnknownUploadId_ThrowsKeyNotFoundException()
    {
        CreateBucket("b");
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
            {
                Bucket = "b", Key = "k", UploadId = "nope",
                Parts = []
            }, TestContext.Current.CancellationToken));
    }

    // ── AbortMultipartUpload ──────────────────────────────────────────────────

    [Fact]
    public async Task AbortMultipartUploadAsync_DeletesStagingDirectory()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        var uploadId = await StartUploadAsync("b", ct: ct);

        await _sut.AbortMultipartUploadAsync(
            new AbortMultipartUploadRequest { Bucket = "b", UploadId = uploadId, Key = "obj.bin" }, ct);

        var stagingDir = Path.Combine(_tmp.Path, "b", FileSystemMultipartService.StagingDirName, uploadId);
        Assert.False(Directory.Exists(stagingDir));
    }

    [Fact]
    public async Task AbortMultipartUploadAsync_UnknownUploadId_ThrowsKeyNotFoundException()
    {
        CreateBucket("b");
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.AbortMultipartUploadAsync(
                new AbortMultipartUploadRequest { Bucket = "b", UploadId = "nope", Key = "k" }, TestContext.Current.CancellationToken));
    }

    // ── ListMultipartUploads ──────────────────────────────────────────────────

    [Fact]
    public async Task ListMultipartUploadsAsync_ListsInProgressUploads()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        var id1 = await StartUploadAsync("b", "k1", ct);
        var id2 = await StartUploadAsync("b", "k2", ct);

        var resp = await _sut.ListMultipartUploadsAsync(
            new ListMultipartUploadsRequest { Bucket = "b" }, ct);

        Assert.Equal(2, resp.Uploads.Count);
        Assert.Contains(resp.Uploads, u => u.UploadId == id1);
        Assert.Contains(resp.Uploads, u => u.UploadId == id2);
    }

    // ── ListParts ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListPartsAsync_ListsUploadedParts()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        var uploadId = await StartUploadAsync("b", ct: ct);
        await UploadPartAsync("b", uploadId, 1, "part1"u8.ToArray(), ct);
        await UploadPartAsync("b", uploadId, 2, "part2"u8.ToArray(), ct);

        var resp = await _sut.ListPartsAsync(
            new ListPartsRequest { Bucket = "b", UploadId = uploadId, Key = "obj.bin" }, ct);

        Assert.Equal(2, resp.Parts.Count);
        Assert.Contains(resp.Parts, p => p.PartNumber == 1);
        Assert.Contains(resp.Parts, p => p.PartNumber == 2);
        Assert.All(resp.Parts, p => Assert.True(p.Size > 0));
        Assert.All(resp.Parts, p => Assert.Matches("^\"[0-9a-f]{32}\"$", p.ETag));
    }
}
