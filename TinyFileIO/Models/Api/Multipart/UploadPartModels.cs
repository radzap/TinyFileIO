namespace TinyFileIO.Models.Api.Multipart;

/// <summary>
/// Query parameters and headers for UploadPart
/// (PUT /{bucket}/{key}?partNumber=N&amp;uploadId=ID).
/// The part body is read directly from the request stream.
/// </summary>
public sealed class UploadPartRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

    /// <summary>1–10 000.</summary>
    public int PartNumber { get; set; }

    public string UploadId { get; set; } = string.Empty;

    public long? ContentLength { get; set; }

    /// <summary>Base64(MD5(part body)) for integrity check.</summary>
    public string? ContentMd5 { get; set; }

    /// <summary>Hex SHA-256 of part body, or "UNSIGNED-PAYLOAD".</summary>
    public string? ContentSha256 { get; set; }
}

/// <summary>Headers returned after a successful UploadPart.</summary>
public sealed class UploadPartResponse
{
    /// <summary>Quoted MD5 ETag of the uploaded part. Must be sent in CompleteMultipartUpload.</summary>
    public string ETag { get; set; } = string.Empty;
}
