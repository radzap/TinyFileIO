using TinyFileIO.Models.Api.Buckets;
using TinyFileIO.Services;
using TinyFileIO.Tests.Infrastructure;

namespace TinyFileIO.Tests;

public sealed class FileSystemBucketServiceTests : IDisposable
{
    private readonly TempDirectory _tmp = new();
    private readonly FileSystemBucketService _sut;

    public FileSystemBucketServiceTests()
        => _sut = new FileSystemBucketService(TestFactory.ConfigFor(_tmp.Path));

    public void Dispose() => _tmp.Dispose();

    // ── ListBuckets ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListBucketsAsync_EmptyRoot_ReturnsEmptyList()
    {
        var result = await _sut.ListBucketsAsync("user1", TestContext.Current.CancellationToken);
        Assert.Empty(result.Buckets);
    }

    [Fact]
    public async Task ListBucketsAsync_WithSubDirectories_ReturnsOneEntryPerDirectory()
    {
        _tmp.Sub("alpha");
        _tmp.Sub("beta");

        var result = await _sut.ListBucketsAsync("user1", TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Buckets.Count);
        Assert.Contains(result.Buckets, b => b.Name == "alpha");
        Assert.Contains(result.Buckets, b => b.Name == "beta");
        Assert.All(result.Buckets, b => Assert.False(string.IsNullOrEmpty(b.CreationDate)));
    }

    // ── CreateBucket ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateBucketAsync_NewBucket_CreatesDirectory()
    {
        await _sut.CreateBucketAsync("mybucket", null, "user1", TestContext.Current.CancellationToken);
        Assert.True(Directory.Exists(Path.Combine(_tmp.Path, "mybucket")));
    }

    [Fact]
    public async Task CreateBucketAsync_Duplicate_ThrowsInvalidOperationException()
    {
        _tmp.Sub("dup");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateBucketAsync("dup", null, "user1", TestContext.Current.CancellationToken));
    }

    // ── BucketExists ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BucketExistsAsync_ExistingBucket_ReturnsTrue()
    {
        _tmp.Sub("exists");
        Assert.True(await _sut.BucketExistsAsync("exists", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BucketExistsAsync_MissingBucket_ReturnsFalse()
    {
        Assert.False(await _sut.BucketExistsAsync("does-not-exist", TestContext.Current.CancellationToken));
    }

    // ── DeleteBucket ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteBucketAsync_EmptyBucket_RemovesDirectory()
    {
        _tmp.Sub("empty");
        await _sut.DeleteBucketAsync("empty", TestContext.Current.CancellationToken);
        Assert.False(Directory.Exists(Path.Combine(_tmp.Path, "empty")));
    }

    [Fact]
    public async Task DeleteBucketAsync_MissingBucket_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.DeleteBucketAsync("ghost", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteBucketAsync_NonEmptyBucket_ThrowsInvalidOperationException()
    {
        var bucketPath = _tmp.Sub("nonempty");
        File.WriteAllText(Path.Combine(bucketPath, "file.txt"), "data");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.DeleteBucketAsync("nonempty", TestContext.Current.CancellationToken));
    }

    // ── GetBucketLocation ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetBucketLocationAsync_ExistingBucket_ReturnsUsEast1()
    {
        _tmp.Sub("located");
        var loc = await _sut.GetBucketLocationAsync("located", TestContext.Current.CancellationToken);
        Assert.Equal("us-east-1", loc);
    }

    [Fact]
    public async Task GetBucketLocationAsync_MissingBucket_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.GetBucketLocationAsync("nowhere", TestContext.Current.CancellationToken));
    }

    // ── ListObjectsV2 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListObjectsV2Async_PrefixFilter_ReturnsMatchingObjects()
    {
        var bucket = _tmp.Sub("bkt");
        File.WriteAllText(Path.Combine(bucket, "img-1.png"), "x");
        File.WriteAllText(Path.Combine(bucket, "img-2.png"), "x");
        File.WriteAllText(Path.Combine(bucket, "doc-1.txt"), "x");

        var result = await _sut.ListObjectsV2Async("bkt",
            new ListObjectsV2Request { Prefix = "img" }, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Contents.Count);
        Assert.All(result.Contents, c => Assert.StartsWith("img", c.Key));
    }

    [Fact]
    public async Task ListObjectsV2Async_MaxKeys_TruncatesAndSetsIsTruncated()
    {
        var bucket = _tmp.Sub("bkt2");
        for (var i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(bucket, $"f{i}.txt"), "x");

        var result = await _sut.ListObjectsV2Async("bkt2",
            new ListObjectsV2Request { MaxKeys = 3 }, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Contents.Count);
        Assert.True(result.IsTruncated);
        Assert.NotNull(result.NextContinuationToken);
    }

    [Fact]
    public async Task ListObjectsV2Async_ContinuationToken_PagesCorrectly()
    {
        var bucket = _tmp.Sub("bkt3");
        for (var i = 0; i < 4; i++)
            File.WriteAllText(Path.Combine(bucket, $"f{i}.txt"), "x");

        var ct = TestContext.Current.CancellationToken;
        var page1 = await _sut.ListObjectsV2Async("bkt3",
            new ListObjectsV2Request { MaxKeys = 2 }, ct);

        var page2 = await _sut.ListObjectsV2Async("bkt3",
            new ListObjectsV2Request { MaxKeys = 2, ContinuationToken = page1.NextContinuationToken }, ct);

        Assert.Equal(2, page1.Contents.Count);
        Assert.Equal(2, page2.Contents.Count);
        Assert.False(page2.IsTruncated);
        // No duplicate keys across pages
        Assert.Empty(page1.Contents.Select(c => c.Key)
            .Intersect(page2.Contents.Select(c => c.Key)));
    }

    [Fact]
    public async Task ListObjectsV2Async_Delimiter_GroupsCommonPrefixes()
    {
        var bucket = _tmp.Sub("bkt4");
        Directory.CreateDirectory(Path.Combine(bucket, "a"));
        File.WriteAllText(Path.Combine(bucket, "a", "1.txt"), "x");
        File.WriteAllText(Path.Combine(bucket, "a", "2.txt"), "x");
        File.WriteAllText(Path.Combine(bucket, "b.txt"), "x");

        var result = await _sut.ListObjectsV2Async("bkt4",
            new ListObjectsV2Request { Delimiter = "/" }, TestContext.Current.CancellationToken);

        Assert.Contains(result.CommonPrefixes, p => p.Prefix == "a/");
        Assert.Contains(result.Contents, c => c.Key == "b.txt");
    }

    [Fact]
    public async Task ListObjectsV2Async_MissingBucket_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.ListObjectsV2Async("ghost", new ListObjectsV2Request(), TestContext.Current.CancellationToken));
    }
}
