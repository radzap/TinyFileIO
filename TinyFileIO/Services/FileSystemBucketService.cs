using System.Security.Cryptography;
using TinyFileIO.Models.Api.Buckets;
using TinyFileIO.Models.Api.Common;

namespace TinyFileIO.Services;

/// <summary>
/// Bucket service backed by the native file system.
/// Each sub-directory of <c>StoreLocation</c> is treated as a bucket.
/// Files placed directly in the root are ignored.
/// </summary>
public sealed class FileSystemBucketService : IS3BucketService
{
    private readonly string _root;
    private static readonly string Region = "us-east-1";

    public FileSystemBucketService(IConfiguration config)
    {
        _root = Path.GetFullPath(config["StoreLocation"]
            ?? throw new InvalidOperationException("StoreLocation is not configured."));
        Directory.CreateDirectory(_root);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string BucketPath(string bucket) => Path.Combine(_root, bucket);

    private static S3Owner OwnerFor(string ownerId) => new() { Id = ownerId, DisplayName = ownerId };

    // ── ListBuckets ───────────────────────────────────────────────────────────

    public Task<ListBucketsResponse> ListBucketsAsync(string ownerId, CancellationToken ct = default)
    {
        var buckets = new DirectoryInfo(_root)
            .GetDirectories()
            .Select(d => new BucketEntry
            {
                Name = d.Name,
                CreationDate = d.CreationTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            })
            .ToList();

        return Task.FromResult(new ListBucketsResponse
        {
            Owner = OwnerFor(ownerId),
            Buckets = buckets
        });
    }

    // ── CreateBucket ──────────────────────────────────────────────────────────

    public Task CreateBucketAsync(string bucket, string? region, string ownerId, CancellationToken ct = default)
    {
        var path = BucketPath(bucket);
        if (Directory.Exists(path))
            throw new InvalidOperationException("BucketAlreadyOwnedByYou");

        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    // ── BucketExists ──────────────────────────────────────────────────────────

    public Task<bool> BucketExistsAsync(string bucket, CancellationToken ct = default)
        => Task.FromResult(Directory.Exists(BucketPath(bucket)));

    // ── DeleteBucket ──────────────────────────────────────────────────────────

    public Task DeleteBucketAsync(string bucket, CancellationToken ct = default)
    {
        var path = BucketPath(bucket);
        if (!Directory.Exists(path))
            throw new KeyNotFoundException("NoSuchBucket");

        // Only directories that contain no user files (excluding multipart staging)
        var hasObjects = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();
        if (hasObjects)
            throw new InvalidOperationException("BucketNotEmpty");

        Directory.Delete(path, recursive: true);
        return Task.CompletedTask;
    }

    // ── GetBucketLocation ─────────────────────────────────────────────────────

    public Task<string?> GetBucketLocationAsync(string bucket, CancellationToken ct = default)
    {
        if (!Directory.Exists(BucketPath(bucket)))
            throw new KeyNotFoundException("NoSuchBucket");

        return Task.FromResult<string?>(Region);
    }

    // ── ListObjectsV2 ─────────────────────────────────────────────────────────

    public Task<ListObjectsV2Response> ListObjectsV2Async(
        string bucket, ListObjectsV2Request request, CancellationToken ct = default)
    {
        var bucketPath = BucketPath(bucket);
        if (!Directory.Exists(bucketPath))
            throw new KeyNotFoundException("NoSuchBucket");

        var allKeys = EnumerateKeys(bucketPath);

        var (contents, prefixes, isTruncated, nextToken) =
            ApplyListFilters(allKeys, request.Prefix, request.Delimiter,
                request.MaxKeys, request.ContinuationToken ?? request.StartAfter,
                isContinuationToken: request.ContinuationToken != null);

        var response = new ListObjectsV2Response
        {
            Name = bucket,
            Prefix = request.Prefix ?? string.Empty,
            KeyCount = contents.Count,
            MaxKeys = request.MaxKeys,
            Delimiter = request.Delimiter,
            IsTruncated = isTruncated,
            NextContinuationToken = nextToken,
            EncodingType = request.EncodingType,
            Contents = contents.Select(k => BuildObjectContent(bucketPath, k, request.FetchOwner)).ToList(),
            CommonPrefixes = prefixes.Select(p => new CommonPrefix { Prefix = p }).ToList()
        };

        return Task.FromResult(response);
    }

    // ── ListObjectsV1 ─────────────────────────────────────────────────────────

    public Task<ListObjectsV1Response> ListObjectsV1Async(
        string bucket, ListObjectsV1Request request, CancellationToken ct = default)
    {
        var bucketPath = BucketPath(bucket);
        if (!Directory.Exists(bucketPath))
            throw new KeyNotFoundException("NoSuchBucket");

        var allKeys = EnumerateKeys(bucketPath);

        var (contents, prefixes, isTruncated, nextMarker) =
            ApplyListFilters(allKeys, request.Prefix, request.Delimiter,
                request.MaxKeys, request.Marker, isContinuationToken: false);

        var response = new ListObjectsV1Response
        {
            Name = bucket,
            Prefix = request.Prefix ?? string.Empty,
            MaxKeys = request.MaxKeys,
            Delimiter = request.Delimiter,
            IsTruncated = isTruncated,
            Marker = request.Marker ?? string.Empty,
            NextMarker = isTruncated ? nextMarker : null,
            EncodingType = request.EncodingType,
            Contents = contents.Select(k => BuildObjectContent(bucketPath, k, false)).ToList(),
            CommonPrefixes = prefixes.Select(p => new CommonPrefix { Prefix = p }).ToList()
        };

        return Task.FromResult(response);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Returns all object keys (relative paths using forward slashes) in a bucket directory,
    /// excluding the multipart staging directory.</summary>
    private static IEnumerable<string> EnumerateKeys(string bucketPath)
    {
        var stagingDir = Path.Combine(bucketPath, FileSystemMultipartService.StagingDirName);
        return new DirectoryInfo(bucketPath)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.FullName.StartsWith(stagingDir, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.FullName[(bucketPath.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(k => k, StringComparer.Ordinal);
    }

    private static (List<string> keys, List<string> prefixes, bool isTruncated, string? nextMarker)
        ApplyListFilters(
            IEnumerable<string> allKeys,
            string? prefix,
            string? delimiter,
            int maxKeys,
            string? afterMarker,
            bool isContinuationToken)
    {
        var keys = allKeys.AsEnumerable();

        if (!string.IsNullOrEmpty(prefix))
            keys = keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal));

        if (!string.IsNullOrEmpty(afterMarker))
            keys = isContinuationToken
                ? keys.SkipWhile(k => string.Compare(k, afterMarker, StringComparison.Ordinal) <= 0)
                : keys.Where(k => string.Compare(k, afterMarker, StringComparison.Ordinal) > 0);

        var resultKeys = new List<string>();
        var commonPrefixes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            if (resultKeys.Count >= maxKeys)
                return (resultKeys, commonPrefixes.ToList(), true, resultKeys[^1]);

            if (!string.IsNullOrEmpty(delimiter))
            {
                var subKey = string.IsNullOrEmpty(prefix) ? key : key[prefix.Length..];
                var delimIdx = subKey.IndexOf(delimiter, StringComparison.Ordinal);
                if (delimIdx >= 0)
                {
                    var cp = (prefix ?? string.Empty) + subKey[..(delimIdx + delimiter.Length)];
                    commonPrefixes.Add(cp);
                    continue;
                }
            }

            resultKeys.Add(key);
        }

        return (resultKeys, commonPrefixes.ToList(), false, null);
    }

    private static ObjectContent BuildObjectContent(string bucketPath, string key, bool fetchOwner)
    {
        var filePath = Path.Combine(bucketPath, key.Replace('/', Path.DirectorySeparatorChar));
        var fi = new FileInfo(filePath);
        return new ObjectContent
        {
            Key = key,
            LastModified = fi.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ETag = ComputeETagFromInfo(fi),
            Size = fi.Length,
            StorageClass = S3StorageClass.Standard,
            Owner = fetchOwner ? new S3Owner { Id = "owner", DisplayName = "owner" } : null
        };
    }

    /// <summary>
    /// Derives a stable ETag from file metadata without reading content.
    /// Uses MD5 of "size:lastWriteUtcTicks" — fast and deterministic.
    /// For exact content ETag the full read is deferred to PutObject.
    /// </summary>
    private static string ComputeETagFromInfo(FileInfo fi)
    {
        var input = $"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}";
        var hash = MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return $"\"{Convert.ToHexString(hash).ToLowerInvariant()}\"";
    }
}
