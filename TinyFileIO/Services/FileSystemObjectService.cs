using System.Security.Cryptography;
using MimeKit;
using TinyFileIO.Models.Api.Common;
using TinyFileIO.Models.Api.Objects;

namespace TinyFileIO.Services;

/// <summary>
/// Object service backed by the native file system.
/// Objects are stored as plain files under <c>{StoreLocation}/{bucket}/{key}</c>.
/// Directory separators in keys are mapped to the host OS separator.
/// No metadata sidecar files are written; ETag is computed from the file bytes on write
/// and from file metadata on read (for speed).
/// </summary>
public sealed class FileSystemObjectService : IS3ObjectService
{
    private readonly string _root;
    private const int StreamCopyBufferSize = 81_920; // 80 KB

    public FileSystemObjectService(IConfiguration config)
    {
        _root = Path.GetFullPath(config["StoreLocation"]
            ?? throw new InvalidOperationException("StoreLocation is not configured."));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string BucketPath(string bucket) => Path.Combine(_root, bucket);

    private string ObjectPath(string bucket, string key)
        => Path.Combine(_root, bucket, key.Replace('/', Path.DirectorySeparatorChar));

    private void EnsureBucketExists(string bucket)
    {
        if (!Directory.Exists(BucketPath(bucket)))
            throw new KeyNotFoundException($"NoSuchBucket:{bucket}");
    }

    private static string ComputeETag(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, StreamCopyBufferSize, useAsync: true);
        var hash = MD5.HashData(stream);
        return $"\"{Convert.ToHexString(hash).ToLowerInvariant()}\"";
    }

    private static GetObjectResponse BuildMeta(string bucket, string key, string filePath)
    {
        var fi = new FileInfo(filePath);
        return new GetObjectResponse
        {
            Bucket = bucket,
            Key = key,
            ETag = ComputeETag(filePath),
            ContentLength = fi.Length,
            ContentType = MimeTypes.GetMimeType(filePath),
            LastModified = fi.LastWriteTimeUtc.ToString("R"),
            StorageClass = S3StorageClass.Standard
        };
    }

    // ── PutObject ─────────────────────────────────────────────────────────────

    public async Task<PutObjectResponse> PutObjectAsync(
        PutObjectRequest request, Stream body, CancellationToken ct = default)
    {
        EnsureBucketExists(request.Bucket);

        var destPath = ObjectPath(request.Bucket, request.Key);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        // Write to a temp file first to avoid partial writes being visible
        var tmpPath = destPath + ".tmp~";
        try
        {
            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write,
                FileShare.None, StreamCopyBufferSize, useAsync: true))
            {
                await body.CopyToAsync(fs, StreamCopyBufferSize, ct);
            }

            File.Move(tmpPath, destPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmpPath); } catch { /* best-effort */ }
            throw;
        }

        var etag = ComputeETag(destPath);
        return new PutObjectResponse { ETag = etag };
    }

    // ── GetObject ─────────────────────────────────────────────────────────────

    public Task<(GetObjectResponse Meta, Stream Body)?> GetObjectAsync(
        GetObjectRequest request, CancellationToken ct = default)
    {
        EnsureBucketExists(request.Bucket);

        var filePath = ObjectPath(request.Bucket, request.Key);
        if (!File.Exists(filePath))
            return Task.FromResult<(GetObjectResponse, Stream)?>(null);

        var meta = BuildMeta(request.Bucket, request.Key, filePath);

        // ── Conditional headers ────────────────────────────────────────────
        if (!string.IsNullOrEmpty(request.IfMatch) && request.IfMatch != meta.ETag)
            throw new InvalidOperationException("PreconditionFailed");

        if (!string.IsNullOrEmpty(request.IfNoneMatch) && request.IfNoneMatch == meta.ETag)
            throw new InvalidOperationException("NotModified");

        var lastMod = new FileInfo(filePath).LastWriteTimeUtc;
        if (request.IfModifiedSince.HasValue && lastMod <= request.IfModifiedSince.Value.UtcDateTime)
            throw new InvalidOperationException("NotModified");

        if (request.IfUnmodifiedSince.HasValue && lastMod > request.IfUnmodifiedSince.Value.UtcDateTime)
            throw new InvalidOperationException("PreconditionFailed");

        // ── Range request ──────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(request.Range))
        {
            var (start, end) = ParseRange(request.Range, meta.ContentLength);
            if (start < 0 || end >= meta.ContentLength || start > end)
                throw new InvalidOperationException("InvalidRange");

            var length = end - start + 1;
            meta.ContentRange = $"bytes {start}-{end}/{meta.ContentLength}";
            meta.ContentLength = length;

            var rangeStream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, StreamCopyBufferSize, useAsync: true);
            rangeStream.Seek(start, SeekOrigin.Begin);
            Stream sliced = new BoundedStream(rangeStream, length);
            return Task.FromResult<(GetObjectResponse, Stream)?>((meta, sliced));
        }

        Stream fullStream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, StreamCopyBufferSize, useAsync: true);
        return Task.FromResult<(GetObjectResponse, Stream)?>((meta, fullStream));
    }

    // ── HeadObject ────────────────────────────────────────────────────────────

    public Task<GetObjectResponse?> HeadObjectAsync(
        GetObjectRequest request, CancellationToken ct = default)
    {
        EnsureBucketExists(request.Bucket);

        var filePath = ObjectPath(request.Bucket, request.Key);
        if (!File.Exists(filePath))
            return Task.FromResult<GetObjectResponse?>(null);

        return Task.FromResult<GetObjectResponse?>(BuildMeta(request.Bucket, request.Key, filePath));
    }

    // ── DeleteObject ──────────────────────────────────────────────────────────

    public Task<DeleteObjectResponse> DeleteObjectAsync(
        DeleteObjectRequest request, CancellationToken ct = default)
    {
        EnsureBucketExists(request.Bucket);

        var filePath = ObjectPath(request.Bucket, request.Key);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            // Remove empty ancestor directories up to the bucket root
            TryPruneEmptyDirs(Path.GetDirectoryName(filePath)!, BucketPath(request.Bucket));
        }

        return Task.FromResult(new DeleteObjectResponse());
    }

    // ── DeleteObjects ─────────────────────────────────────────────────────────

    public Task<DeleteObjectsResponse> DeleteObjectsAsync(
        string bucket, DeleteObjectsRequest request, CancellationToken ct = default)
    {
        EnsureBucketExists(bucket);

        var deleted = new List<DeletedEntry>();
        var errors = new List<DeleteErrorEntry>();

        foreach (var obj in request.Objects)
        {
            try
            {
                var filePath = ObjectPath(bucket, obj.Key);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    TryPruneEmptyDirs(Path.GetDirectoryName(filePath)!, BucketPath(bucket));
                }
                deleted.Add(new DeletedEntry { Key = obj.Key });
            }
            catch (Exception ex)
            {
                errors.Add(new DeleteErrorEntry { Key = obj.Key, Code = "InternalError", Message = ex.Message });
            }
        }

        return Task.FromResult(new DeleteObjectsResponse { Deleted = deleted, Errors = errors });
    }

    // ── CopyObject ────────────────────────────────────────────────────────────

    public async Task<CopyObjectResponse> CopyObjectAsync(
        CopyObjectRequest request, CancellationToken ct = default)
    {
        // Parse x-amz-copy-source: /bucket/key or bucket/key
        var src = Uri.UnescapeDataString(request.CopySource).TrimStart('/');
        var slash = src.IndexOf('/');
        if (slash < 0) throw new InvalidOperationException("InvalidArgument");

        var srcBucket = src[..slash];
        var srcKey    = src[(slash + 1)..];

        EnsureBucketExists(srcBucket);
        EnsureBucketExists(request.DestinationBucket);

        var srcPath  = ObjectPath(srcBucket, srcKey);
        var destPath = ObjectPath(request.DestinationBucket, request.DestinationKey);

        if (!File.Exists(srcPath))
            throw new KeyNotFoundException("NoSuchKey");

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        await Task.Run(() => File.Copy(srcPath, destPath, overwrite: true), ct);

        var fi = new FileInfo(destPath);
        var etag = ComputeETag(destPath);

        return new CopyObjectResponse
        {
            ETag = etag,
            LastModified = fi.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
    }

    // ── Range parsing ─────────────────────────────────────────────────────────

    private static (long start, long end) ParseRange(string rangeHeader, long totalLength)
    {
        // Expected: "bytes=start-end" or "bytes=start-" or "bytes=-suffix"
        var value = rangeHeader.Replace("bytes=", string.Empty).Trim();
        var parts = value.Split('-');
        if (parts.Length != 2) return (-1, -1);

        if (string.IsNullOrEmpty(parts[0]))
        {
            // suffix range: -N → last N bytes
            if (!long.TryParse(parts[1], out var suffix)) return (-1, -1);
            return (totalLength - suffix, totalLength - 1);
        }

        if (!long.TryParse(parts[0], out var start)) return (-1, -1);
        var end = string.IsNullOrEmpty(parts[1]) ? totalLength - 1 : long.Parse(parts[1]);
        return (start, end);
    }

    // ── Directory pruning ─────────────────────────────────────────────────────

    private static void TryPruneEmptyDirs(string dir, string bucketRoot)
    {
        while (!string.Equals(dir, bucketRoot, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(dir)
               && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir)!;
        }
    }
}

/// <summary>
/// Wraps a stream and exposes only the first <paramref name="length"/> bytes.
/// Used for byte-range responses without copying data.
/// </summary>
internal sealed class BoundedStream(Stream inner, long length) : Stream
{
    private readonly Stream _inner = inner;
    private long _remaining = length;

    public override bool CanRead  => true;
    public override bool CanSeek  => false;
    public override bool CanWrite => false;
    public override long Length   => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0) return 0;
        var toRead = (int)Math.Min(count, _remaining);
        var read   = _inner.Read(buffer, offset, toRead);
        _remaining -= read;
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        if (_remaining <= 0) return 0;
        var toRead = (int)Math.Min(count, _remaining);
        var read   = await _inner.ReadAsync(buffer.AsMemory(offset, toRead), ct);
        _remaining -= read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_remaining <= 0) return 0;
        var toRead = (int)Math.Min(buffer.Length, _remaining);
        var read   = await _inner.ReadAsync(buffer[..toRead], ct);
        _remaining -= read;
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value)                 => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
