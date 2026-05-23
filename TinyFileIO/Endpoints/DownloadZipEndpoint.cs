using System.IO.Compression;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using TinyFileIO.Services;

namespace TinyFileIO.Endpoints;

/// <summary>
/// POST /_tfio/api/download-zip
/// Body: { "bucket": "...", "keys": ["key1", "folder/", ...] }
///
/// Resolves every key against the configured StoreLocation folder directly
/// (no S3 endpoint indirection). Folders are expanded recursively.
/// Streams a ZIP archive back without buffering the whole thing in memory.
/// </summary>
public static class DownloadZipEndpoint
{
    public static void MapDownloadZipEndpoint(this WebApplication app)
    {
        app.MapPost("/_tfio/api/download-zip", async (
            DownloadZipRequest req,
            ClaimsPrincipal user,
            IAuthorizationProvider authz,
            IConfiguration config,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (req.Keys is not { Length: > 0 })
                return Results.BadRequest("keys are required.");

            var userId       = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var username     = user.FindFirstValue(ClaimTypes.Name) ?? userId;
            var isSuperAdmin = user.FindFirstValue("is_super_admin") == "true";

            var identity = new CallerIdentity
            {
                UserId       = userId,
                Username     = username,
                IsSuperAdmin = isSuperAdmin
            };

            var root       = Path.GetFullPath(config["StoreLocation"]
                ?? throw new InvalidOperationException("StoreLocation is not configured."));
            var targets    = new List<(string FullPath, string ZipRoot)>();
            var wholeBuckets = string.IsNullOrWhiteSpace(req.Bucket);

            if (wholeBuckets)
            {
                foreach (var bucket in req.Keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var access = await authz.CheckAccessAsync(identity, bucket, BucketPermission.Read);
                    if (!isSuperAdmin && access != AclCheckResult.Allowed)
                        return Results.Forbid();

                    var bucketPath = Path.GetFullPath(Path.Combine(root, bucket));
                    if (!IsWithinRoot(bucketPath, root) || !Directory.Exists(bucketPath))
                        return Results.NotFound($"Bucket not found: {bucket}");

                    targets.Add((bucketPath, root));
                }
            }
            else
            {
                var access = await authz.CheckAccessAsync(identity, req.Bucket, BucketPermission.Read);
                if (!isSuperAdmin && access != AclCheckResult.Allowed)
                    return Results.Forbid();

                var bucketPath = Path.Combine(root, req.Bucket);
                if (!Directory.Exists(bucketPath))
                    return Results.NotFound("Bucket not found.");

                foreach (var key in req.Keys)
                {
                    var relPath  = key.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                    var fullPath = Path.GetFullPath(Path.Combine(bucketPath, relPath));

                    if (!IsWithinRoot(fullPath, bucketPath))
                        continue;

                    if (Directory.Exists(fullPath) || File.Exists(fullPath))
                        targets.Add((fullPath, bucketPath));
                }
            }

            if (targets.Count == 0)
                return Results.NotFound("No files found.");

            var zipName = wholeBuckets ? "buckets.zip" : req.Bucket + ".zip";

            ctx.Response.ContentType = "application/zip";
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{zipName}\"";

            // ZipArchive.Dispose() writes the central directory synchronously.
            // Allow sync I/O on the response body for this request only, then wrap
            // the response stream in a BufferedStream so the sync flush is absorbed
            // by the buffer and only async flushes reach Kestrel.
            var syncIo = ctx.Features.Get<IHttpBodyControlFeature>();
            if (syncIo is not null)
                syncIo.AllowSynchronousIO = true;

            await using var buffered = new BufferedStream(ctx.Response.Body, 81_920);

            await using (var zip = new ZipArchive(buffered, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (fullPath, zipRoot) in targets)
                {
                    ct.ThrowIfCancellationRequested();

                    if (Directory.Exists(fullPath))
                        await AddDirectoryAsync(zip, fullPath, zipRoot, ct);
                    else if (File.Exists(fullPath))
                        await AddFileAsync(zip, fullPath, zipRoot, ct);
                }
            } // ZipArchive.Dispose() flushes central directory → buffered stream absorbs the sync write

            await buffered.FlushAsync(ct);

            return Results.Empty;
        })
        .RequireAuthorization()
        .DisableAntiforgery();
    }

    private static async Task AddDirectoryAsync(
        ZipArchive zip, string dirPath, string bucketRoot, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            await AddFileAsync(zip, file, bucketRoot, ct);
        }
    }

    private static async Task AddFileAsync(
        ZipArchive zip, string filePath, string bucketRoot, CancellationToken ct)
    {
        // Entry name is relative to the bucket root, using forward slashes
        var entryName = Path.GetRelativePath(bucketRoot, filePath)
                            .Replace(Path.DirectorySeparatorChar, '/');

        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);

        // Preserve last-write timestamp
        entry.LastWriteTime = new FileInfo(filePath).LastWriteTime;

        await using var src  = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                              FileShare.Read, 81_920, useAsync: true);
        await using var dest = entry.Open();
        await src.CopyToAsync(dest, 81_920, ct);
    }

    private static bool IsWithinRoot(string fullPath, string root)
        => fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
           || string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase);
}

internal sealed record DownloadZipRequest(string Bucket, string[] Keys);
