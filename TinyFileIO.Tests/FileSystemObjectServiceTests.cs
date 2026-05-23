using TinyFileIO.Models.Api.Objects;
using TinyFileIO.Services;
using TinyFileIO.Tests.Infrastructure;

namespace TinyFileIO.Tests;

public sealed class FileSystemObjectServiceTests : IDisposable
{
    private readonly TempDirectory _tmp = new();
    private readonly FileSystemObjectService _sut;

    public FileSystemObjectServiceTests()
        => _sut = new FileSystemObjectService(TestFactory.ConfigFor(_tmp.Path));

    public void Dispose() => _tmp.Dispose();

    private void CreateBucket(string name) => _tmp.Sub(name);

    private static async Task<string> PutAsync(FileSystemObjectService svc,
        string bucket, string key, string content,
        CancellationToken ct = default)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var req = new PutObjectRequest { Bucket = bucket, Key = key };
        var resp = await svc.PutObjectAsync(req, new MemoryStream(bytes), ct);
        return resp.ETag;
    }

    // ── PutObject ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PutObjectAsync_WritesBodyToExpectedPath()
    {
        CreateBucket("b");
        await PutAsync(_sut, "b", "hello.txt", "hello", TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Path.Combine(_tmp.Path, "b", "hello.txt")));
    }

    [Fact]
    public async Task PutObjectAsync_NestedKey_CreatesIntermediateDirectories()
    {
        CreateBucket("b");
        await PutAsync(_sut, "b", "a/b/c.txt", "data", TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Path.Combine(_tmp.Path, "b", "a", "b", "c.txt")));
    }

    [Fact]
    public async Task PutObjectAsync_ReturnsMd5ETag()
    {
        CreateBucket("b");
        var etag = await PutAsync(_sut, "b", "f.txt", "hello", TestContext.Current.CancellationToken);
        // ETag must be a quoted 32-char hex string
        Assert.Matches("^\"[0-9a-f]{32}\"$", etag);
    }

    [Fact]
    public async Task PutObjectAsync_MissingBucket_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.PutObjectAsync(new PutObjectRequest { Bucket = "ghost", Key = "k" },
                new MemoryStream(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutObjectAsync_ExceptionDuringWrite_TempFileIsCleanedUp()
    {
        CreateBucket("b");
        var destPath = Path.Combine(_tmp.Path, "b", "f.txt");
        var tmpPath = destPath + ".tmp~";

        // A stream that throws halfway through to simulate a write failure
        var faultyStream = new FaultyStream();

        await Assert.ThrowsAsync<IOException>(() =>
            _sut.PutObjectAsync(
                new PutObjectRequest { Bucket = "b", Key = "f.txt" },
                faultyStream,
                TestContext.Current.CancellationToken));

        Assert.False(File.Exists(tmpPath), "Temp file should have been deleted after exception.");
        Assert.False(File.Exists(destPath), "Destination file should not exist after failed write.");
    }

    /// <summary>Stream that throws <see cref="IOException"/> on the first read.</summary>
    private sealed class FaultyStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Simulated write failure");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ── GetObject ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetObjectAsync_MissingObject_ReturnsNull()
    {
        CreateBucket("b");
        var result = await _sut.GetObjectAsync(new GetObjectRequest { Bucket = "b", Key = "nope.txt" }, TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetObjectAsync_ExistingObject_ReturnsCorrectMetadata()
    {
        CreateBucket("b");
        var content = "hello world";
        var ct = TestContext.Current.CancellationToken;
        var putEtag = await PutAsync(_sut, "b", "f.txt", content, ct);

        var result = await _sut.GetObjectAsync(new GetObjectRequest { Bucket = "b", Key = "f.txt" }, ct);

        Assert.NotNull(result);
        var (meta, body) = result!.Value;
        await body.DisposeAsync();
        Assert.Equal(putEtag, meta.ETag);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(content), meta.ContentLength);
        Assert.False(string.IsNullOrEmpty(meta.LastModified));
        Assert.False(string.IsNullOrEmpty(meta.ContentType));
    }

    [Fact]
    public async Task GetObjectAsync_IfMatch_ThrowsPreconditionFailedOnMismatch()
    {
        CreateBucket("b");
        await PutAsync(_sut, "b", "f.txt", "data", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.GetObjectAsync(new GetObjectRequest
                { Bucket = "b", Key = "f.txt", IfMatch = "\"wrong\"" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetObjectAsync_IfNoneMatch_ThrowsNotModifiedOnMatch()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        var etag = await PutAsync(_sut, "b", "f.txt", "data", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.GetObjectAsync(new GetObjectRequest
                { Bucket = "b", Key = "f.txt", IfNoneMatch = etag }, ct));
    }

    [Fact]
    public async Task GetObjectAsync_IfModifiedSince_ThrowsNotModifiedWhenUnchanged()
    {
        CreateBucket("b");
        await PutAsync(_sut, "b", "f.txt", "data", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.GetObjectAsync(new GetObjectRequest
            {
                Bucket = "b", Key = "f.txt",
                IfModifiedSince = DateTimeOffset.UtcNow.AddHours(1)
            }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetObjectAsync_IfUnmodifiedSince_ThrowsPreconditionFailedWhenChanged()
    {
        CreateBucket("b");
        await PutAsync(_sut, "b", "f.txt", "data", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.GetObjectAsync(new GetObjectRequest
            {
                Bucket = "b", Key = "f.txt",
                IfUnmodifiedSince = DateTimeOffset.UtcNow.AddHours(-1)
            }, TestContext.Current.CancellationToken));
    }

    // ── HeadObject ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HeadObjectAsync_ExistingObject_ReturnsMetadataWithoutBody()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        var etag = await PutAsync(_sut, "b", "f.txt", "hello", ct);

        var meta = await _sut.HeadObjectAsync(new GetObjectRequest { Bucket = "b", Key = "f.txt" }, ct);

        Assert.NotNull(meta);
        Assert.Equal(etag, meta!.ETag);
    }

    [Fact]
    public async Task HeadObjectAsync_MissingObject_ReturnsNull()
    {
        CreateBucket("b");
        var meta = await _sut.HeadObjectAsync(new GetObjectRequest { Bucket = "b", Key = "nope.txt" }, TestContext.Current.CancellationToken);
        Assert.Null(meta);
    }

    // ── DeleteObject ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteObjectAsync_ExistingObject_RemovesFile()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        await PutAsync(_sut, "b", "f.txt", "data", ct);

        await _sut.DeleteObjectAsync(new DeleteObjectRequest { Bucket = "b", Key = "f.txt" }, ct);

        Assert.False(File.Exists(Path.Combine(_tmp.Path, "b", "f.txt")));
    }

    [Fact]
    public async Task DeleteObjectAsync_MissingObject_DoesNotThrow()
    {
        CreateBucket("b");
        var ex = await Record.ExceptionAsync(() =>
            _sut.DeleteObjectAsync(new DeleteObjectRequest { Bucket = "b", Key = "nope.txt" }, TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    // ── DeleteObjects ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteObjectsAsync_DeletesAllRequestedKeys()
    {
        CreateBucket("b");
        var ct = TestContext.Current.CancellationToken;
        await PutAsync(_sut, "b", "a.txt", "a", ct);
        await PutAsync(_sut, "b", "b.txt", "b", ct);

        var response = await _sut.DeleteObjectsAsync("b", new DeleteObjectsRequest
        {
            Objects = [new() { Key = "a.txt" }, new() { Key = "b.txt" }]
        }, ct);

        Assert.Equal(2, response.Deleted.Count);
        Assert.Empty(response.Errors);
        Assert.False(File.Exists(Path.Combine(_tmp.Path, "b", "a.txt")));
    }

    // ── CopyObject ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CopyObjectAsync_CopiesContentsAndReturnsNewETag()
    {
        CreateBucket("src");
        CreateBucket("dst");
        var ct = TestContext.Current.CancellationToken;
        var originalEtag = await PutAsync(_sut, "src", "orig.txt", "hello", ct);

        var resp = await _sut.CopyObjectAsync(new CopyObjectRequest
        {
            CopySource = "/src/orig.txt",
            DestinationBucket = "dst",
            DestinationKey = "copy.txt"
        }, ct);

        Assert.Equal(originalEtag, resp.ETag);
        Assert.True(File.Exists(Path.Combine(_tmp.Path, "dst", "copy.txt")));
    }

    [Fact]
    public async Task CopyObjectAsync_MissingSourceObject_ThrowsKeyNotFoundException()
    {
        CreateBucket("src");
        CreateBucket("dst");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.CopyObjectAsync(new CopyObjectRequest
            {
                CopySource = "/src/ghost.txt",
                DestinationBucket = "dst",
                DestinationKey = "copy.txt"
            }, TestContext.Current.CancellationToken));
    }
}
