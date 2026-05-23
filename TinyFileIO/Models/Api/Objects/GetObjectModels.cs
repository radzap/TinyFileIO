namespace TinyFileIO.Models.Api.Objects;

/// <summary>
/// Captures all headers and query parameters for a GetObject / HeadObject request.
/// </summary>
public sealed class GetObjectRequest
{
    /// <summary>Bucket name (from route).</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Object key (from route).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Specific version to retrieve.</summary>
    public string? VersionId { get; set; }

    // ── Byte-range ────────────────────────────────────────────────────────────

    /// <summary>Raw Range header value, e.g. "bytes=0-1023".</summary>
    public string? Range { get; set; }

    // ── Conditional headers ───────────────────────────────────────────────────

    public string? IfMatch { get; set; }
    public string? IfNoneMatch { get; set; }
    public DateTimeOffset? IfModifiedSince { get; set; }
    public DateTimeOffset? IfUnmodifiedSince { get; set; }

    // ── Response header overrides ─────────────────────────────────────────────

    public string? ResponseContentType { get; set; }
    public string? ResponseContentDisposition { get; set; }
    public string? ResponseCacheControl { get; set; }
    public string? ResponseContentEncoding { get; set; }
    public string? ResponseContentLanguage { get; set; }
    public string? ResponseExpires { get; set; }
}

/// <summary>
/// Metadata returned for GetObject / HeadObject. The body stream is handled
/// separately by the controller.
/// </summary>
public sealed class GetObjectResponse
{
    public string Bucket { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

    public string ETag { get; set; } = string.Empty;
    public long ContentLength { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public string LastModified { get; set; } = string.Empty;

    public string? VersionId { get; set; }
    public bool? DeleteMarker { get; set; }
    public string? StorageClass { get; set; }
    public string? ServerSideEncryption { get; set; }
    public string? ContentEncoding { get; set; }
    public string? ContentDisposition { get; set; }
    public string? CacheControl { get; set; }
    public string? ExpiresHeader { get; set; }

    /// <summary>Present for partial (206) responses: "bytes start-end/total".</summary>
    public string? ContentRange { get; set; }

    /// <summary>User-defined metadata (without the x-amz-meta- prefix).</summary>
    public Dictionary<string, string> UserMetadata { get; set; } = [];
}
