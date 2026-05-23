using Microsoft.AspNetCore.Mvc;
using TinyFileIO.Models.Api.Buckets;
using TinyFileIO.Services;

namespace TinyFileIO.Controllers;

/// <summary>
/// Handles S3-compatible bucket-level operations (Phase 1).
///
/// Route disambiguation:
///   GET  /          → ListBuckets
///   PUT  /{bucket}  → CreateBucket
///   DELETE /{bucket}→ DeleteBucket
///   HEAD /{bucket}  → HeadBucket
///   GET  /{bucket}?location     → GetBucketLocation
///   GET  /{bucket}?list-type=2  → ListObjectsV2
///   GET  /{bucket}              → ListObjectsV1
/// </summary>
[ApiController]
public sealed class BucketController : S3ControllerBase
{
    private readonly IS3BucketService _buckets;

    public BucketController(IS3BucketService buckets, IS3XmlSerializer xml) : base(xml)
    {
        _buckets = buckets;
    }

    // ── ListBuckets ───────────────────────────────────────────────────────────

    /// <summary>GET /</summary>
    [HttpGet("/")]
    public async Task<IActionResult> ListBuckets(CancellationToken ct)
    {
        var result = await _buckets.ListBucketsAsync(GetOwnerId(), ct);
        return XmlOk(result);
    }

    // ── CreateBucket ──────────────────────────────────────────────────────────

    /// <summary>PUT /{bucket}</summary>
    [HttpPut("/{bucket}")]
    public async Task<IActionResult> CreateBucket(string bucket, CancellationToken ct)
    {
        CreateBucketRequest? body = null;
        if (Request.ContentLength > 0)
        {
            body = Xml.Deserialize<CreateBucketRequest>(Request.Body);
            if (body is null)
                return MalformedXml();
        }

        try
        {
            await _buckets.CreateBucketAsync(bucket, body?.LocationConstraint, GetOwnerId(), ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "BucketAlreadyOwnedByYou")
        {
            return S3Error("BucketAlreadyOwnedByYou",
                "Your previous request to create the named bucket succeeded and you already own it.",
                StatusCodes.Status409Conflict, bucketName: bucket);
        }
        catch (InvalidOperationException ex) when (ex.Message == "BucketAlreadyExists")
        {
            return S3Error("BucketAlreadyExists",
                "The requested bucket name is not available.",
                StatusCodes.Status409Conflict, bucketName: bucket);
        }

        Response.Headers.Location = $"/{bucket}";
        return Ok();
    }

    // ── HeadBucket ────────────────────────────────────────────────────────────

    /// <summary>HEAD /{bucket}</summary>
    [HttpHead("/{bucket}")]
    public async Task<IActionResult> HeadBucket(string bucket, CancellationToken ct)
    {
        if (!await _buckets.BucketExistsAsync(bucket, ct))
            return NotFound();
        return Ok();
    }

    // ── DeleteBucket ──────────────────────────────────────────────────────────

    /// <summary>DELETE /{bucket}</summary>
    [HttpDelete("/{bucket}")]
    public async Task<IActionResult> DeleteBucket(string bucket, CancellationToken ct)
    {
        if (!await _buckets.BucketExistsAsync(bucket, ct))
            return NoSuchBucket(bucket);

        try
        {
            await _buckets.DeleteBucketAsync(bucket, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "BucketNotEmpty")
        {
            return S3Error("BucketNotEmpty",
                "The bucket you tried to delete is not empty.",
                StatusCodes.Status409Conflict, bucketName: bucket);
        }

        return NoContent();
    }

    // ── GetBucketLocation ─────────────────────────────────────────────────────

    /// <summary>GET /{bucket}?location</summary>
    [HttpGet("/{bucket}")]
    [ActionName(nameof(GetBucketLocation))]
    public async Task<IActionResult> GetBucketLocation(string bucket, CancellationToken ct,
        [FromQuery(Name = "location")] string? location = null)
    {
        if (location is null)
            return await ListObjects(bucket, ct);   // fall through to listing

        if (!await _buckets.BucketExistsAsync(bucket, ct))
            return NoSuchBucket(bucket);

        var region = await _buckets.GetBucketLocationAsync(bucket, ct);
        var response = new GetBucketLocationResponse { Region = region };
        return XmlOk(response);
    }

    // ── ListObjectsV2 / ListObjectsV1 ─────────────────────────────────────────

    private async Task<IActionResult> ListObjects(string bucket, CancellationToken ct)
    {
        if (!await _buckets.BucketExistsAsync(bucket, ct))
            return NoSuchBucket(bucket);

        var q = Request.Query;

        // V2 when list-type=2 is present
        if (q.TryGetValue("list-type", out var listType) && listType == "2")
        {
            var req = new ListObjectsV2Request
            {
                Prefix = q["prefix"],
                Delimiter = q["delimiter"],
                MaxKeys = int.TryParse(q["max-keys"], out var mk) ? Math.Clamp(mk, 1, 1000) : 1000,
                ContinuationToken = q["continuation-token"],
                StartAfter = q["start-after"],
                FetchOwner = q["fetch-owner"] == "true",
                EncodingType = q["encoding-type"]
            };
            var result = await _buckets.ListObjectsV2Async(bucket, req, ct);
            return XmlOk(result);
        }
        else
        {
            var req = new ListObjectsV1Request
            {
                Prefix = q["prefix"],
                Delimiter = q["delimiter"],
                MaxKeys = int.TryParse(q["max-keys"], out var mk) ? Math.Clamp(mk, 1, 1000) : 1000,
                Marker = q["marker"],
                EncodingType = q["encoding-type"]
            };
            var result = await _buckets.ListObjectsV1Async(bucket, req, ct);
            return XmlOk(result);
        }
    }
}
