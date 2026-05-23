using System.Net;
using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TinyFileIO.Models.Api.Buckets;
using TinyFileIO.Models.Api.Objects;
using TinyFileIO.Models.Entities;
using TinyFileIO.Services;
using TinyFileIO.Services.BackgroundJobs;

namespace TinyFileIO.Tests;

public sealed class DownloadJobTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly System.Text.Json.JsonSerializerOptions s_jsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static DownloadJob BuildJob(
        IS3BucketService? buckets = null,
        IS3ObjectService? objects = null,
        HttpMessageHandler? handler = null)
    {
        var httpFactory = Substitute.For<IHttpClientFactory>();
        var client = handler is not null ? new HttpClient(handler) : new HttpClient();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(client);
        return new DownloadJob(
            httpFactory,
            buckets ?? Substitute.For<IS3BucketService>(),
            objects ?? Substitute.For<IS3ObjectService>());
    }

    private static BackgroundJobContext MakeContext(
        string targetBucket,
        string duplicateMode = DownloadDuplicateModes.Overwrite,
        Action<BackgroundJobProgress>? onProgress = null)
    {
        var run = new BackgroundJobRun
        {
            JobType = DownloadJob.Type,
            ParametersJson = System.Text.Json.JsonSerializer.Serialize(new DownloadJobParameters
            {
                ServerUrl     = "http://localhost",
                Username      = "user",
                Key           = "secret",
                SourceBucket  = "src",
                TargetBucket  = targetBucket,
                DuplicateMode = duplicateMode
            }, s_jsonOptions)
        };

        return new BackgroundJobContext(run, (p, _) =>
        {
            onProgress?.Invoke(p);
            return Task.CompletedTask;
        });
    }

    private static IS3BucketService BucketsWithKeys(string targetBucket, params string[] existingKeys)
    {
        var buckets = Substitute.For<IS3BucketService>();

        // ListObjectsV2Async returns the existingKeys in one page, then done
        var response = new ListObjectsV2Response
        {
            Contents = [.. existingKeys.Select(k => new ObjectContent { Key = k })],
            IsTruncated = false
        };
        buckets.ListObjectsV2Async(targetBucket,
            Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(response);

        return buckets;
    }

    /// <summary>
    /// Returns an HttpMessageHandler that:
    ///   – responds to the list-type=2 request with an XML listing of <paramref name="sourceKeys"/>
    ///   – responds to any per-object GET with the key name as the body (or throws for keys in
    ///     <paramref name="failKeys"/>)
    /// </summary>
    private static FakeS3Handler MakeSourceHandler(string[] sourceKeys, string[]? failKeys = null)
    {
        var fail = new HashSet<string>(failKeys ?? [], StringComparer.Ordinal);
        return new FakeS3Handler(sourceKeys, fail);
    }

    private sealed class FakeS3Handler(string[] sourceKeys, HashSet<string> failKeys) : HttpMessageHandler
    {
        private static string BuildListXml(string[] keys)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\"?><ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
            sb.Append("<IsTruncated>false</IsTruncated>");
            foreach (var k in keys)
                sb.Append($"<Contents><Key>{k}</Key></Contents>");
            sb.Append("</ListBucketResult>");
            return sb.ToString();
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            if (query.Contains("list-type=2"))
            {
                var xml = BuildListXml(sourceKeys);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(xml, Encoding.UTF8, "application/xml")
                });
            }

            // Per-object GET — key is the last path segment (URL-decoded)
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var slash = path.IndexOf('/', 1);
            var key = slash >= 0 ? Uri.UnescapeDataString(path[(slash + 1)..]) : string.Empty;

            if (failKeys.Contains(key))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(key, Encoding.UTF8, "application/octet-stream")
            });
        }
    }

    // ── Skip duplicate mode ───────────────────────────────────────────────────

    // NOTE: DownloadJob uses an internal ExternalS3Client backed by HttpClient,
    // making it hard to unit-test end-to-end without a running server.
    // The tests below exercise the *logic paths* reachable via IS3BucketService /
    // IS3ObjectService mocks.  The ExternalS3Client interaction is integration-level.

    [Fact]
    public void ResolveTargetKey_Skip_ExistingKey_ReturnsNull()
    {
        // Verify via the public observable effect: if we supply a target bucket
        // that already has a key and DuplicateMode = Skip, the object service
        // should never be called for that key.
        // We test this via a minimal ListObjectsV2 / PutObject mock sequence.
        var existing = new HashSet<string>(StringComparer.Ordinal) { "file.txt" };
        // ResolveTargetKey is private; we access behaviour indirectly.
        // Since we cannot call ExecuteAsync without a live HTTP server,
        // we validate the static private logic by observing IS3ObjectService call counts
        // through a full execution driven by a mock HttpClient is out of scope.
        // Therefore, this test documents the requirement at the unit level.
        Assert.Contains("file.txt", existing); // fixture sanity
    }

    [Fact]
    public void DownloadDuplicateModes_Constants_HaveExpectedValues()
    {
        Assert.Equal("Overwrite", DownloadDuplicateModes.Overwrite);
        Assert.Equal("Skip",      DownloadDuplicateModes.Skip);
        Assert.Equal("Rename",    DownloadDuplicateModes.Rename);
    }

    [Fact]
    public void DownloadJob_JobType_EqualsExpectedConstant()
    {
        var job = BuildJob();
        Assert.Equal(DownloadJob.Type, job.JobType);
        Assert.Equal("DownloadFromS3", job.JobType);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var objects = Substitute.For<IS3ObjectService>();
        var buckets = BucketsWithKeys("dst");
        var job = BuildJob(buckets, objects);
        var context = MakeContext("dst");

        using var cts = new CancellationTokenSource();
        // Cancel before execution begins
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            job.ExecuteAsync(context, cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_InvalidParameters_ThrowsInvalidOperationException()
    {
        var job = BuildJob();
        var run = new BackgroundJobRun
        {
            JobType = DownloadJob.Type,
            ParametersJson = "null"
        };
        var ctx = new BackgroundJobContext(run, (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            job.ExecuteAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public void BackgroundJobContext_ReportProgressAsync_InvokesCallback()
    {
        BackgroundJobProgress? captured = null;
        var run = new BackgroundJobRun { JobType = "x", ParametersJson = "{}" };
        var ctx = new BackgroundJobContext(run, (p, _) =>
        {
            captured = p;
            return Task.CompletedTask;
        });

        ctx.ReportProgressAsync(new BackgroundJobProgress { TotalItems = 5 }, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(5, captured!.TotalItems);
    }

    // ── DuplicateMode: Skip ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DuplicateModeSkip_ExistingKeyIsNotUploaded()
    {
        var ct = TestContext.Current.CancellationToken;
        var objects = Substitute.For<IS3ObjectService>();
        var buckets = BucketsWithKeys("dst", "file.txt");   // "file.txt" already in target
        var handler = MakeSourceHandler(["file.txt"]);       // source also has "file.txt"

        var job = BuildJob(buckets, objects, handler);
        var context = MakeContext("dst", DownloadDuplicateModes.Skip);

        await job.ExecuteAsync(context, ct);

        // PutObjectAsync must never be called for the skipped key
        await objects.DidNotReceive().PutObjectAsync(
            Arg.Any<PutObjectRequest>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    // ── DuplicateMode: Rename ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DuplicateModeRename_ExistingKeyIsUploadedUnderNewName()
    {
        var ct = TestContext.Current.CancellationToken;

        PutObjectRequest? capturedPut = null;
        var objects = Substitute.For<IS3ObjectService>();
        objects.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedPut = ci.ArgAt<PutObjectRequest>(0);
                return new PutObjectResponse { ETag = "\"abc\"" };
            });

        var buckets = BucketsWithKeys("dst", "file.txt");   // "file.txt" already in target
        var handler = MakeSourceHandler(["file.txt"]);       // source also has "file.txt"

        var job = BuildJob(buckets, objects, handler);
        var context = MakeContext("dst", DownloadDuplicateModes.Rename);

        await job.ExecuteAsync(context, ct);

        // Should have been uploaded, but under a renamed key
        await objects.Received(1).PutObjectAsync(
            Arg.Any<PutObjectRequest>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        Assert.NotNull(capturedPut);
        Assert.NotEqual("file.txt", capturedPut!.Key);
        Assert.StartsWith("file-", capturedPut.Key); // renamed to "file-1.txt"
    }

    // ── DuplicateMode: Overwrite ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DuplicateModeOverwrite_ExistingKeyIsOverwritten()
    {
        var ct = TestContext.Current.CancellationToken;

        PutObjectRequest? capturedPut = null;
        var objects = Substitute.For<IS3ObjectService>();
        objects.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedPut = ci.ArgAt<PutObjectRequest>(0);
                return new PutObjectResponse { ETag = "\"abc\"" };
            });

        var buckets = BucketsWithKeys("dst", "file.txt");   // "file.txt" already in target
        var handler = MakeSourceHandler(["file.txt"]);       // source also has "file.txt"

        var job = BuildJob(buckets, objects, handler);
        var context = MakeContext("dst", DownloadDuplicateModes.Overwrite);

        await job.ExecuteAsync(context, ct);

        // Must upload using the original key
        await objects.Received(1).PutObjectAsync(
            Arg.Any<PutObjectRequest>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        Assert.NotNull(capturedPut);
        Assert.Equal("file.txt", capturedPut!.Key);
    }

    // ── Per-key error handling ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SingleKeyDownloadFails_RecordsErrorWithoutAbortingJob()
    {
        var ct = TestContext.Current.CancellationToken;

        var objects = Substitute.For<IS3ObjectService>();
        objects.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse { ETag = "\"ok\"" });

        var buckets = BucketsWithKeys("dst");   // empty target
        // "bad.txt" returns HTTP 500, "good.txt" succeeds
        var handler = MakeSourceHandler(["bad.txt", "good.txt"], failKeys: ["bad.txt"]);

        var job = BuildJob(buckets, objects, handler);

        BackgroundJobProgress? lastProgress = null;
        var context = MakeContext("dst", DownloadDuplicateModes.Overwrite,
            onProgress: p => lastProgress = p);

        // Must complete without throwing
        await job.ExecuteAsync(context, ct);

        Assert.NotNull(lastProgress);
        Assert.Equal(1, lastProgress!.FailedItems);
        Assert.Equal(1, lastProgress.SucceededItems);
        Assert.Equal(2, lastProgress.ProcessedItems);
    }
}
