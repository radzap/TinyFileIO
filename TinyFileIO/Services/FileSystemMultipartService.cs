using System.Security.Cryptography;
using System.Text.Json;
using TinyFileIO.Models.Api.Common;
using TinyFileIO.Models.Api.Multipart;

namespace TinyFileIO.Services;

/// <summary>
/// Multipart upload service backed by the native file system.
///
/// Staging layout:
///   {StoreLocation}/{bucket}/.multipart/{uploadId}/meta.json   — upload metadata
///   {StoreLocation}/{bucket}/.multipart/{uploadId}/{partNumber} — part data
///
/// On CompleteMultipartUpload the parts are concatenated directly into the
/// final object path and the staging directory is removed.
/// </summary>
public sealed class FileSystemMultipartService : IS3MultipartService
{
    public const string StagingDirName = ".multipart";

    private readonly string _root;
    private const int StreamCopyBufferSize = 81_920;

    public FileSystemMultipartService(IConfiguration config)
    {
        _root = Path.GetFullPath(config["StoreLocation"]
            ?? throw new InvalidOperationException("StoreLocation is not configured."));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string StagingRoot(string bucket)
        => Path.Combine(_root, bucket, StagingDirName);

    private string UploadDir(string bucket, string uploadId)
        => Path.Combine(StagingRoot(bucket), uploadId);

    private string MetaFile(string bucket, string uploadId)
        => Path.Combine(UploadDir(bucket, uploadId), "meta.json");

    private string PartFile(string bucket, string uploadId, int partNumber)
        => Path.Combine(UploadDir(bucket, uploadId), partNumber.ToString("D5"));

    private string ObjectPath(string bucket, string key)
        => Path.Combine(_root, bucket, key.Replace('/', Path.DirectorySeparatorChar));

    private void EnsureBucketExists(string bucket)
    {
        if (!Directory.Exists(Path.Combine(_root, bucket)))
            throw new KeyNotFoundException($"NoSuchBucket:{bucket}");
    }

    private UploadMeta ReadMeta(string bucket, string uploadId)
    {
        var file = MetaFile(bucket, uploadId);
        if (!File.Exists(file))
            throw new KeyNotFoundException("NoSuchUpload");
        return JsonSerializer.Deserialize<UploadMeta>(File.ReadAllText(file))
               ?? throw new InvalidOperationException("Corrupt upload metadata.");
    }

    // ── CreateMultipartUpload ─────────────────────────────────────────────────

    public Task<CreateMultipartUploadResponse> CreateMultipartUploadAsync(
        CreateMultipartUploadRequest request, CancellationToken ct = default)
    {
        EnsureBucketExists(request.Bucket);

        var uploadId = Guid.NewGuid().ToString("N");
        var dir = UploadDir(request.Bucket, uploadId);
        Directory.CreateDirectory(dir);

        var meta = new UploadMeta
        {
            Bucket      = request.Bucket,
            Key         = request.Key,
            UploadId    = uploadId,
            ContentType = request.ContentType,
            Initiated   = DateTimeOffset.UtcNow
        };
        File.WriteAllText(MetaFile(request.Bucket, uploadId),
            JsonSerializer.Serialize(meta));

        return Task.FromResult(new CreateMultipartUploadResponse
        {
            Bucket   = request.Bucket,
            Key      = request.Key,
            UploadId = uploadId
        });
    }

    // ── UploadPart ────────────────────────────────────────────────────────────

    public async Task<UploadPartResponse> UploadPartAsync(
        UploadPartRequest request, Stream partBody, CancellationToken ct = default)
    {
        EnsureBucketExists(request.Bucket);
        _ = ReadMeta(request.Bucket, request.UploadId); // validate upload exists

        var partPath = PartFile(request.Bucket, request.UploadId, request.PartNumber);
        var tmpPath  = partPath + ".tmp~";

        try
        {
            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write,
                FileShare.None, StreamCopyBufferSize, useAsync: true))
            {
                await partBody.CopyToAsync(fs, StreamCopyBufferSize, ct);
            }
            File.Move(tmpPath, partPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmpPath); } catch { /* best-effort */ }
            throw;
        }

        var etag = ComputeETag(partPath);
        return new UploadPartResponse { ETag = etag };
    }

    // ── UploadPartCopy ────────────────────────────────────────────────────────

    public async Task<UploadPartCopyResponse> UploadPartCopyAsync(
        UploadPartCopyRequest request, CancellationToken ct = default)
    {
        EnsureBucketExists(request.Bucket);
        _ = ReadMeta(request.Bucket, request.UploadId);

        var src   = Uri.UnescapeDataString(request.CopySource).TrimStart('/');
        var slash = src.IndexOf('/');
        if (slash < 0) throw new InvalidOperationException("InvalidArgument");

        var srcBucket = src[..slash];
        var srcKey    = src[(slash + 1)..];
        var srcPath   = Path.Combine(_root, srcBucket, srcKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(srcPath)) throw new KeyNotFoundException("NoSuchKey");

        var partPath = PartFile(request.Bucket, request.UploadId, request.PartNumber);

        if (!string.IsNullOrEmpty(request.CopySourceRange))
        {
            var (start, end) = ParseRange(request.CopySourceRange, new FileInfo(srcPath).Length);
            await CopyRangeAsync(srcPath, partPath, start, end - start + 1, ct);
        }
        else
        {
            await Task.Run(() => File.Copy(srcPath, partPath, overwrite: true), ct);
        }

        var etag = ComputeETag(partPath);
        return new UploadPartCopyResponse
        {
            ETag         = etag,
            LastModified = new FileInfo(partPath).LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
    }

    // ── CompleteMultipartUpload ───────────────────────────────────────────────

    public async Task<CompleteMultipartUploadResponse> CompleteMultipartUploadAsync(
        CompleteMultipartUploadRequest request, CancellationToken ct = default)
    {
        EnsureBucketExists(request.Bucket);
        var meta = ReadMeta(request.Bucket, request.UploadId);

        var destPath = ObjectPath(request.Bucket, request.Key);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        var tmpPath = destPath + ".mpu~";
        try
        {
            await using (var dest = new FileStream(tmpPath, FileMode.Create, FileAccess.Write,
                FileShare.None, StreamCopyBufferSize, useAsync: true))
            {
                foreach (var part in request.Parts.OrderBy(p => p.PartNumber))
                {
                    var partPath = PartFile(request.Bucket, request.UploadId, part.PartNumber);
                    if (!File.Exists(partPath))
                        throw new InvalidOperationException($"InvalidPart:{part.PartNumber}");

                    await using var src = new FileStream(partPath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, StreamCopyBufferSize, useAsync: true);
                    await src.CopyToAsync(dest, StreamCopyBufferSize, ct);
                }
            }

            File.Move(tmpPath, destPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmpPath); } catch { /* best-effort */ }
            throw;
        }
        finally
        {
            TryDeleteUpload(request.Bucket, request.UploadId);
        }

        var etag = $"\"{ComputeETag(destPath).Trim('"')}-{request.Parts.Count}\"";

        return new CompleteMultipartUploadResponse
        {
            Bucket   = request.Bucket,
            Key      = request.Key,
            Location = $"/{request.Bucket}/{request.Key}",
            ETag     = etag
        };
    }

    // ── AbortMultipartUpload ──────────────────────────────────────────────────

    public Task AbortMultipartUploadAsync(
        AbortMultipartUploadRequest request, CancellationToken ct = default)
    {
        EnsureBucketExists(request.Bucket);
        _ = ReadMeta(request.Bucket, request.UploadId); // validate exists
        TryDeleteUpload(request.Bucket, request.UploadId);
        return Task.CompletedTask;
    }

    // ── ListMultipartUploads ──────────────────────────────────────────────────

    public Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(
        ListMultipartUploadsRequest request, CancellationToken ct = default)
    {
        EnsureBucketExists(request.Bucket);

        var stagingRoot = StagingRoot(request.Bucket);
        var uploads = new List<MultipartUploadEntry>();

        if (Directory.Exists(stagingRoot))
        {
            foreach (var dir in new DirectoryInfo(stagingRoot).GetDirectories())
            {
                var metaFile = Path.Combine(dir.FullName, "meta.json");
                if (!File.Exists(metaFile)) continue;

                var meta = JsonSerializer.Deserialize<UploadMeta>(File.ReadAllText(metaFile));
                if (meta is null) continue;

                if (!string.IsNullOrEmpty(request.Prefix) && !meta.Key.StartsWith(request.Prefix)) continue;

                uploads.Add(new MultipartUploadEntry
                {
                    Key        = meta.Key,
                    UploadId   = meta.UploadId,
                    Initiated  = meta.Initiated.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    StorageClass = S3StorageClass.Standard,
                    Owner      = new S3Owner { Id = "owner", DisplayName = "owner" },
                    Initiator  = new S3Owner { Id = "owner", DisplayName = "owner" }
                });
            }
        }

        uploads = uploads.OrderBy(u => u.Key).ThenBy(u => u.UploadId).ToList();

        return Task.FromResult(new ListMultipartUploadsResponse
        {
            Bucket       = request.Bucket,
            Prefix       = request.Prefix,
            Delimiter    = request.Delimiter,
            MaxUploads   = request.MaxUploads,
            IsTruncated  = false,
            KeyMarker    = request.KeyMarker ?? string.Empty,
            UploadIdMarker = request.UploadIdMarker ?? string.Empty,
            Uploads      = uploads
        });
    }

    // ── ListParts ─────────────────────────────────────────────────────────────

    public Task<ListPartsResponse> ListPartsAsync(
        ListPartsRequest request, CancellationToken ct = default)
    {
        EnsureBucketExists(request.Bucket);
        var meta = ReadMeta(request.Bucket, request.UploadId);

        var uploadDir = UploadDir(request.Bucket, request.UploadId);
        var parts = new DirectoryInfo(uploadDir)
            .EnumerateFiles()
            .Where(f => f.Name != "meta.json" && int.TryParse(f.Name, out _))
            .Select(f => new PartEntry
            {
                PartNumber   = int.Parse(f.Name),
                Size         = f.Length,
                LastModified = f.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ETag         = ComputeETag(f.FullName)
            })
            .Where(p => !request.PartNumberMarker.HasValue || p.PartNumber > request.PartNumberMarker.Value)
            .OrderBy(p => p.PartNumber)
            .ToList();

        var page = parts.Take(request.MaxParts).ToList();
        var truncated = parts.Count > request.MaxParts;

        return Task.FromResult(new ListPartsResponse
        {
            Bucket            = request.Bucket,
            Key               = request.Key,
            UploadId          = request.UploadId,
            MaxParts          = request.MaxParts,
            PartNumberMarker  = request.PartNumberMarker ?? 0,
            NextPartNumberMarker = truncated ? page[^1].PartNumber : null,
            IsTruncated       = truncated,
            StorageClass      = S3StorageClass.Standard,
            Parts             = page
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string ComputeETag(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, StreamCopyBufferSize, useAsync: false);
        var hash = MD5.HashData(stream);
        return $"\"{Convert.ToHexString(hash).ToLowerInvariant()}\"";
    }

    private void TryDeleteUpload(string bucket, string uploadId)
    {
        try { Directory.Delete(UploadDir(bucket, uploadId), recursive: true); } catch { /* best-effort */ }
    }

    private static (long start, long length) ParseRange(string rangeHeader, long totalLength)
    {
        var value = rangeHeader.Replace("bytes=", string.Empty).Trim();
        var parts = value.Split('-');
        if (parts.Length != 2) return (0, totalLength);
        if (!long.TryParse(parts[0], out var start)) return (0, totalLength);
        var end = string.IsNullOrEmpty(parts[1]) ? totalLength - 1 : long.Parse(parts[1]);
        return (start, end - start + 1);
    }

    private static async Task CopyRangeAsync(
        string srcPath, string destPath, long start, long length, CancellationToken ct)
    {
        await using var src  = new FileStream(srcPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, StreamCopyBufferSize, useAsync: true);
        await using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write,
            FileShare.None, StreamCopyBufferSize, useAsync: true);

        src.Seek(start, SeekOrigin.Begin);
        var remaining = length;
        var buffer    = new byte[StreamCopyBufferSize];

        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read   = await src.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0) break;
            await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }
    }
}

/// <summary>Serializable metadata stored per in-progress multipart upload.</summary>
internal sealed class UploadMeta
{
    public string Bucket      { get; set; } = string.Empty;
    public string Key         { get; set; } = string.Empty;
    public string UploadId    { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public DateTimeOffset Initiated { get; set; }
}
