using Microsoft.AspNetCore.Mvc;
using TinyFileIO.Models.Api.Multipart;
using TinyFileIO.Models.Api.Objects;
using TinyFileIO.Services;

namespace TinyFileIO.Controllers;

/// <summary>
/// Handles S3-compatible object-level and multipart operations.
///
///   PUT    /{bucket}/{**key}              → PutObject or CopyObject
///   PUT    /{bucket}/{**key}?partNumber&amp;uploadId → UploadPart or UploadPartCopy
///   GET    /{bucket}/{**key}              → GetObject
///   GET    /{bucket}/{**key}?uploadId     → ListParts
///   HEAD   /{bucket}/{**key}              → HeadObject
///   DELETE /{bucket}/{**key}              → DeleteObject
///   DELETE /{bucket}/{**key}?uploadId     → AbortMultipartUpload
///   POST   /{bucket}?delete               → DeleteObjects (batch)
///   POST   /{bucket}/{**key}?uploads      → CreateMultipartUpload
///   POST   /{bucket}/{**key}?uploadId     → CompleteMultipartUpload
///   GET    /{bucket}?uploads              → ListMultipartUploads
/// </summary>
[ApiController]
public sealed class ObjectController : S3ControllerBase
{
    private readonly IS3ObjectService _objects;
    private readonly IS3MultipartService _multipart;
    private readonly ILogger<ObjectController> _logger;

    public ObjectController(IS3ObjectService objects, IS3MultipartService multipart,
        IS3XmlSerializer xml, ILogger<ObjectController> logger) : base(xml)
    {
        _objects = objects;
        _multipart = multipart;
        _logger = logger;
    }

    // ── PutObject / CopyObject ────────────────────────────────────────────────

    /// <summary>PUT /{bucket}/{**key}</summary>
    [HttpPut("/{bucket}/{**key}")]
    public async Task<IActionResult> PutObject(string bucket, string key, CancellationToken ct,
        [FromQuery(Name = "partNumber")] int? partNumber = null,
        [FromQuery(Name = "uploadId")] string? uploadId = null)
    {
        if (partNumber is not null || uploadId is not null)
            return await UploadPart(bucket, key, partNumber, uploadId, ct);

        // CopyObject when x-amz-copy-source is present
        if (Request.Headers.TryGetValue("x-amz-copy-source", out var copySourceHeader))
        {
            return await CopyObject(bucket, key, copySourceHeader.ToString(), ct);
        }

        var request = new PutObjectRequest
        {
            Bucket = bucket,
            Key = key,
            ContentType = Request.ContentType ?? "application/octet-stream",
            ContentLength = S3ChunkedDecodingStream.GetDecodedContentLength(Request.Headers) ?? Request.ContentLength,
            ContentMd5 = Request.Headers["Content-MD5"],
            ContentSha256 = Request.Headers["x-amz-content-sha256"],
            StorageClass = Request.Headers["x-amz-storage-class"],
            ServerSideEncryption = Request.Headers["x-amz-server-side-encryption"],
            CannedAcl = Request.Headers["x-amz-acl"],
            Tagging = Request.Headers["x-amz-tagging"],
            UserMetadata = ExtractUserMetadata()
        };

        PutObjectResponse result;
        try
        {
            var body = S3ChunkedDecodingStream.IsAwsChunked(Request.Headers)
                ? new S3ChunkedDecodingStream(Request.Body)
                : Request.Body;

            result = await _objects.PutObjectAsync(request, body, ct);
        }
        catch (KeyNotFoundException)
        {
            return NoSuchBucket(bucket);
        }

        Response.Headers.ETag = result.ETag;
        if (result.VersionId is not null)
            Response.Headers["x-amz-version-id"] = result.VersionId;
        if (result.ServerSideEncryption is not null)
            Response.Headers["x-amz-server-side-encryption"] = result.ServerSideEncryption;

        return Ok();
    }

    private async Task<IActionResult> UploadPart(string bucket, string key, int? partNumber,
        string? uploadId, CancellationToken ct)
    {
        if (partNumber is null || uploadId is null)
            return S3Error("InvalidRequest", "Missing partNumber or uploadId.", StatusCodes.Status400BadRequest);

        // UploadPartCopy when x-amz-copy-source is present
        if (Request.Headers.TryGetValue("x-amz-copy-source", out var copySourceHeader))
        {
            return await UploadPartCopy(bucket, key, partNumber.Value, uploadId, copySourceHeader.ToString(), ct);
        }

        var request = new UploadPartRequest
        {
            Bucket = bucket,
            Key = key,
            PartNumber = partNumber.Value,
            UploadId = uploadId,
            ContentLength = S3ChunkedDecodingStream.GetDecodedContentLength(Request.Headers) ?? Request.ContentLength,
            ContentMd5 = Request.Headers["Content-MD5"],
            ContentSha256 = Request.Headers["x-amz-content-sha256"]
        };

        UploadPartResponse result;
        try
        {
            var body = S3ChunkedDecodingStream.IsAwsChunked(Request.Headers)
                ? new S3ChunkedDecodingStream(Request.Body)
                : Request.Body;

            result = await _multipart.UploadPartAsync(request, body, ct);
        }
        catch (KeyNotFoundException)
        {
            return NoSuchUpload(uploadId);
        }

        Response.Headers.ETag = result.ETag;
        return Ok();
    }

    private async Task<IActionResult> UploadPartCopy(string bucket, string key, int partNumber,
        string uploadId, string copySourceHeader, CancellationToken ct)
    {
        var request = new UploadPartCopyRequest
        {
            Bucket = bucket,
            Key = key,
            PartNumber = partNumber,
            UploadId = uploadId,
            CopySource = copySourceHeader,
            CopySourceRange = Request.Headers["x-amz-copy-source-range"],
            CopySourceVersionId = Request.Headers["x-amz-copy-source-version-id"],
            CopySourceIfMatch = Request.Headers["x-amz-copy-source-if-match"],
            CopySourceIfNoneMatch = Request.Headers["x-amz-copy-source-if-none-match"]
        };

        if (DateTimeOffset.TryParse(Request.Headers["x-amz-copy-source-if-modified-since"], out var ms))
            request.CopySourceIfModifiedSince = ms;
        if (DateTimeOffset.TryParse(Request.Headers["x-amz-copy-source-if-unmodified-since"], out var us))
            request.CopySourceIfUnmodifiedSince = us;

        UploadPartCopyResponse result;
        try
        {
            result = await _multipart.UploadPartCopyAsync(request, ct);
        }
        catch (KeyNotFoundException)
        {
            return NoSuchUpload(uploadId);
        }

        return XmlOk(result);
    }

    private async Task<IActionResult> CopyObject(string destBucket, string destKey,
        string copySourceHeader, CancellationToken ct)
    {
        var (sourceBucket, sourceKey) = ParseCopySource(copySourceHeader);

        var request = new CopyObjectRequest
        {
            DestinationBucket = destBucket,
            DestinationKey = destKey,
            CopySource = copySourceHeader,
            CopySourceVersionId = Request.Headers["x-amz-copy-source-version-id"],
            MetadataDirective = Request.Headers["x-amz-metadata-directive"].FirstOrDefault() ?? "COPY",
            TaggingDirective = Request.Headers["x-amz-tagging-directive"].FirstOrDefault() ?? "COPY",
            CopySourceIfMatch = Request.Headers["x-amz-copy-source-if-match"],
            CopySourceIfNoneMatch = Request.Headers["x-amz-copy-source-if-none-match"],
            StorageClass = Request.Headers["x-amz-storage-class"],
            ServerSideEncryption = Request.Headers["x-amz-server-side-encryption"],
            CannedAcl = Request.Headers["x-amz-acl"],
            Tagging = Request.Headers["x-amz-tagging"],
            UserMetadata = ExtractUserMetadata()
        };

        if (DateTimeOffset.TryParse(Request.Headers["x-amz-copy-source-if-modified-since"], out var modifiedSince))
            request.CopySourceIfModifiedSince = modifiedSince;
        if (DateTimeOffset.TryParse(Request.Headers["x-amz-copy-source-if-unmodified-since"], out var unmodifiedSince))
            request.CopySourceIfUnmodifiedSince = unmodifiedSince;

        CopyObjectResponse result;
        try
        {
            result = await _objects.CopyObjectAsync(request, ct);
        }
        catch (KeyNotFoundException ex) when (ex.Message.Contains("bucket"))
        {
            return NoSuchBucket(sourceBucket);
        }
        catch (KeyNotFoundException ex) when (ex.Message.Contains("key"))
        {
            return NoSuchKey(sourceBucket, sourceKey);
        }

        if (result.VersionId is not null)
            Response.Headers["x-amz-version-id"] = result.VersionId;

        return XmlOk(result);
    }

    // ── GetObject ─────────────────────────────────────────────────────────────

    /// <summary>GET /{bucket}/{**key}</summary>
    [HttpGet("/{bucket}/{**key}")]
    public async Task<IActionResult> GetObject(string bucket, string key, CancellationToken ct,
        [FromQuery(Name = "uploadId")] string? uploadId = null)
    {
        if (uploadId is not null)
            return await ListParts(bucket, key, uploadId, ct);

        var request = BuildGetObjectRequest(bucket, key);

        var found = await _objects.GetObjectAsync(request, ct);
        if (found is null)
            return NoSuchKey(bucket, key);

        var (meta, body) = found.Value;
        ApplyObjectResponseHeaders(meta);

        if (meta.ContentRange is not null)
        {
            Response.Headers.ContentRange = meta.ContentRange;
            return new FileStreamResult(body, AdjustContentType(meta.ContentType)) { EnableRangeProcessing = false };
        }

        return new FileStreamResult(body, AdjustContentType(meta.ContentType)) { EnableRangeProcessing = true };
    }

    private async Task<IActionResult> ListParts(string bucket, string key, string uploadId, CancellationToken ct)
    {
        var q = Request.Query;
        var request = new ListPartsRequest
        {
            Bucket = bucket,
            Key = key,
            UploadId = uploadId,
            MaxParts = int.TryParse(q["max-parts"], out var mp) ? Math.Clamp(mp, 1, 1000) : 1000,
            PartNumberMarker = int.TryParse(q["part-number-marker"], out var pnm) ? pnm : null,
            EncodingType = q["encoding-type"]
        };

        ListPartsResponse result;
        try
        {
            result = await _multipart.ListPartsAsync(request, ct);
        }
        catch (KeyNotFoundException)
        {
            return NoSuchUpload(uploadId);
        }

        return XmlOk(result);
    }

    // ── HeadObject ────────────────────────────────────────────────────────────

    /// <summary>HEAD /{bucket}/{**key}</summary>
    [HttpHead("/{bucket}/{**key}")]
    public async Task<IActionResult> HeadObject(string bucket, string key, CancellationToken ct)
    {
        var request = BuildGetObjectRequest(bucket, key);

        var meta = await _objects.HeadObjectAsync(request, ct);
        if (meta is null)
            return NotFound();

        ApplyObjectResponseHeaders(meta);
        return Ok();
    }

    // ── DeleteObject ──────────────────────────────────────────────────────────

    /// <summary>DELETE /{bucket}/{**key}</summary>
    [HttpDelete("/{bucket}/{**key}")]
    public async Task<IActionResult> DeleteObject(string bucket, string key, CancellationToken ct,
        [FromQuery(Name = "uploadId")] string? uploadId = null)
    {
        if (uploadId is not null)
            return await AbortMultipartUpload(bucket, key, uploadId, ct);

        var request = new DeleteObjectRequest
        {
            Bucket = bucket,
            Key = key,
            VersionId = Request.Query["versionId"]
        };

        var result = await _objects.DeleteObjectAsync(request, ct);

        if (result.VersionId is not null)
            Response.Headers["x-amz-version-id"] = result.VersionId;
        if (result.DeleteMarker.HasValue)
            Response.Headers["x-amz-delete-marker"] = result.DeleteMarker.Value.ToString().ToLowerInvariant();

        return NoContent();
    }

    private async Task<IActionResult> AbortMultipartUpload(string bucket, string key,
        string uploadId, CancellationToken ct)
    {
        var request = new AbortMultipartUploadRequest
        {
            Bucket = bucket,
            Key = key,
            UploadId = uploadId
        };

        try
        {
            await _multipart.AbortMultipartUploadAsync(request, ct);
        }
        catch (KeyNotFoundException)
        {
            return NoSuchUpload(uploadId);
        }

        return NoContent();
    }

    // ── DeleteObjects (batch) ─────────────────────────────────────────────────

    /// <summary>POST /{bucket}?delete</summary>
    [HttpPost("/{bucket}")]
    public async Task<IActionResult> DeleteObjects(string bucket, CancellationToken ct,
        [FromQuery(Name = "uploads")] string? uploads = null,
        [FromQuery(Name = "delete")] string? deleteParam = null)
    {
        if (Request.Query.ContainsKey("uploads"))
            return await ListMultipartUploads(bucket, ct);

        if (!Request.Query.ContainsKey("delete"))
        {
            _logger.LogWarning(
                "Unsupported POST {Method} {Path}{QueryString} Host={Host} ContentType={ContentType}",
                Request.Method, Request.Path, Request.QueryString, Request.Host, Request.ContentType);
            return S3Error("InvalidRequest", "Unsupported POST operation.", StatusCodes.Status400BadRequest);
        }

        var requestBody = S3ChunkedDecodingStream.IsAwsChunked(Request.Headers)
            ? new S3ChunkedDecodingStream(Request.Body)
            : Request.Body;

        var body = await Xml.DeserializeAsync<DeleteObjectsRequest>(requestBody, ct);
        if (body is null)
            return MalformedXml();

        body.Bucket = bucket;

        var result = await _objects.DeleteObjectsAsync(bucket, body, ct);
        return XmlOk(result);
    }

    /// <summary>GET /{bucket}?uploads</summary>
    [HttpGet("/{bucket}")]
    [RequireQueryParameter("uploads")]
    public async Task<IActionResult> ListMultipartUploads(string bucket, CancellationToken ct,
        [FromQuery(Name = "uploads")] string? uploads = null)
    {
        if (!Request.Query.ContainsKey("uploads"))
            return S3Error("InvalidRequest", "Missing uploads parameter.", StatusCodes.Status400BadRequest);

        var q = Request.Query;
        var request = new ListMultipartUploadsRequest
        {
            Bucket = bucket,
            Prefix = q["prefix"],
            Delimiter = q["delimiter"],
            MaxUploads = int.TryParse(q["max-uploads"], out var mu) ? Math.Clamp(mu, 1, 1000) : 1000,
            KeyMarker = q["key-marker"],
            UploadIdMarker = q["upload-id-marker"],
            EncodingType = q["encoding-type"]
        };

        var result = await _multipart.ListMultipartUploadsAsync(request, ct);
        return XmlOk(result);
    }

    /// <summary>POST /{bucket}/{**key}?uploads or ?uploadId</summary>
    [HttpPost("/{bucket}/{**key}")]
    public async Task<IActionResult> InitiateOrCompleteMultipartUpload(string bucket, string key, CancellationToken ct,
        [FromQuery(Name = "uploads")] string? uploads = null,
        [FromQuery(Name = "uploadId")] string? uploadId = null)
    {
        if (uploadId is not null)
            return await CompleteMultipartUpload(bucket, key, uploadId, ct);

        if (Request.Query.ContainsKey("uploads"))
            return await CreateMultipartUpload(bucket, key, ct);

        _logger.LogWarning(
            "Unsupported POST {Method} {Path}{QueryString} Host={Host} ContentType={ContentType}",
            Request.Method, Request.Path, Request.QueryString, Request.Host, Request.ContentType);
        return S3Error("InvalidRequest", "Unsupported POST operation.", StatusCodes.Status400BadRequest);
    }

    private async Task<IActionResult> CreateMultipartUpload(string bucket, string key, CancellationToken ct)
    {
        var request = new CreateMultipartUploadRequest
        {
            Bucket = bucket,
            Key = key,
            ContentType = Request.ContentType ?? "application/octet-stream",
            StorageClass = Request.Headers["x-amz-storage-class"],
            ServerSideEncryption = Request.Headers["x-amz-server-side-encryption"],
            CannedAcl = Request.Headers["x-amz-acl"],
            Tagging = Request.Headers["x-amz-tagging"],
            UserMetadata = ExtractUserMetadata()
        };

        var result = await _multipart.CreateMultipartUploadAsync(request, ct);
        return XmlOk(result);
    }

    private async Task<IActionResult> CompleteMultipartUpload(string bucket, string key,
        string uploadId, CancellationToken ct)
    {
        var requestBody = S3ChunkedDecodingStream.IsAwsChunked(Request.Headers)
            ? new S3ChunkedDecodingStream(Request.Body)
            : Request.Body;

        var body = await Xml.DeserializeAsync<CompleteMultipartUploadRequest>(requestBody, ct);
        if (body is null)
        {
            _logger.LogWarning(
                "CompleteMultipartUpload XML could not be parsed for {Bucket}/{Key} uploadId={UploadId}; completing from staged parts.",
                bucket, key, uploadId);
            body = new CompleteMultipartUploadRequest();
        }

        body.Bucket = bucket;
        body.Key = key;
        body.UploadId = uploadId;

        CompleteMultipartUploadResponse result;
        try
        {
            result = await _multipart.CompleteMultipartUploadAsync(body, ct);
        }
        catch (KeyNotFoundException)
        {
            return NoSuchUpload(uploadId);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("InvalidPart", StringComparison.Ordinal))
        {
            return S3Error("InvalidPart", "One or more of the specified parts could not be found.",
                StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException ex) when (ex.Message == "EntityTooSmall")
        {
            return S3Error("EntityTooSmall",
                "Your proposed upload is smaller than the minimum allowed object size.",
                StatusCodes.Status400BadRequest);
        }

        if (result.VersionId is not null)
            Response.Headers["x-amz-version-id"] = result.VersionId;

        return XmlOk(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private GetObjectRequest BuildGetObjectRequest(string bucket, string key)
    {
        var req = new GetObjectRequest
        {
            Bucket = bucket,
            Key = key,
            VersionId = Request.Query["versionId"],
            IfMatch = Request.Headers.IfMatch,
            IfNoneMatch = Request.Headers.IfNoneMatch,
            ResponseContentType = Request.Query["response-content-type"],
            ResponseContentDisposition = Request.Query["response-content-disposition"],
            ResponseCacheControl = Request.Query["response-cache-control"],
            ResponseContentEncoding = Request.Query["response-content-encoding"],
            ResponseContentLanguage = Request.Query["response-content-language"],
            ResponseExpires = Request.Query["response-expires"]
        };

        if (DateTimeOffset.TryParse(Request.Headers.IfModifiedSince, out var ims))
            req.IfModifiedSince = ims;
        if (DateTimeOffset.TryParse(Request.Headers.IfUnmodifiedSince, out var ius))
            req.IfUnmodifiedSince = ius;

        return req;
    }

    private void ApplyObjectResponseHeaders(GetObjectResponse meta)
    {
        Response.Headers.ETag = meta.ETag;
        Response.Headers.LastModified = meta.LastModified;
        Response.ContentLength = meta.ContentLength;

        if (meta.VersionId is not null)
            Response.Headers["x-amz-version-id"] = meta.VersionId;
        if (meta.DeleteMarker.HasValue)
            Response.Headers["x-amz-delete-marker"] = meta.DeleteMarker.Value.ToString().ToLowerInvariant();
        if (meta.StorageClass is not null)
            Response.Headers["x-amz-storage-class"] = meta.StorageClass;
        if (meta.ServerSideEncryption is not null)
            Response.Headers["x-amz-server-side-encryption"] = meta.ServerSideEncryption;
        if (meta.ContentEncoding is not null)
            Response.Headers.ContentEncoding = meta.ContentEncoding;
        if (meta.ContentDisposition is not null)
            Response.Headers.ContentDisposition = meta.ContentDisposition;
        if (meta.CacheControl is not null)
            Response.Headers.CacheControl = meta.CacheControl;

        foreach (var (k, v) in meta.UserMetadata)
            Response.Headers[$"x-amz-meta-{k}"] = v;
    }

    private Dictionary<string, string> ExtractUserMetadata()
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in Request.Headers)
        {
            if (header.Key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase))
            {
                var metaKey = header.Key["x-amz-meta-".Length..].ToLowerInvariant();
                meta[metaKey] = header.Value.ToString();
            }
        }
        return meta;
    }

    private string AdjustContentType(string contentType)
    {
        // This is for bucket browsing in UI for rendering markdown files as HTML.
        if (contentType == "text/markdown" && Request.Query.ContainsKey("X-Tfio-Presign"))
        {
            return "text/html";
        }

        return contentType;
    }
}
