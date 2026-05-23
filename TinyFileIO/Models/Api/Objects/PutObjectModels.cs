namespace TinyFileIO.Models.Api.Objects;

/// <summary>
/// Captures all headers and query parameters for a PutObject request.
/// The object body is read directly from the request stream.
/// </summary>
public sealed class PutObjectRequest
{
    /// <summary>Target bucket name (from route).</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Object key (from route).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>MIME type. Defaults to application/octet-stream.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Body length in bytes.</summary>
    public long? ContentLength { get; set; }

    /// <summary>Base64(MD5(body)) for server-side integrity check.</summary>
    public string? ContentMd5 { get; set; }

    /// <summary>Hex SHA-256 of body, or "UNSIGNED-PAYLOAD".</summary>
    public string? ContentSha256 { get; set; }

    /// <summary>x-amz-storage-class header value.</summary>
    public string? StorageClass { get; set; }

    /// <summary>x-amz-server-side-encryption header value (AES256 or aws:kms).</summary>
    public string? ServerSideEncryption { get; set; }

    /// <summary>Canned ACL from x-amz-acl header.</summary>
    public string? CannedAcl { get; set; }

    /// <summary>URL-encoded tags from x-amz-tagging header (key1=val1&amp;key2=val2).</summary>
    public string? Tagging { get; set; }

    /// <summary>User-defined metadata from x-amz-meta-* headers.</summary>
    public Dictionary<string, string> UserMetadata { get; set; } = [];
}

/// <summary>Headers returned after a successful PutObject.</summary>
public sealed class PutObjectResponse
{
    /// <summary>Quoted MD5 hex ETag, e.g. "d41d8cd98f00b204e9800998ecf8427e".</summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>Version ID when bucket versioning is enabled.</summary>
    public string? VersionId { get; set; }

    /// <summary>Encryption algorithm used, if any.</summary>
    public string? ServerSideEncryption { get; set; }
}
