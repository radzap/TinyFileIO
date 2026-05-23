using System.Xml.Serialization;

namespace TinyFileIO.Models.Api.Multipart;

/// <summary>
/// Headers for UploadPartCopy. Extends UploadPart with a server-side copy source.
/// </summary>
public sealed class UploadPartCopyRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int PartNumber { get; set; }
    public string UploadId { get; set; } = string.Empty;

    /// <summary>x-amz-copy-source: "/source-bucket/source-key" (URL-encoded).</summary>
    public string CopySource { get; set; } = string.Empty;

    /// <summary>x-amz-copy-source-range: "bytes=0-5242879".</summary>
    public string? CopySourceRange { get; set; }

    public string? CopySourceVersionId { get; set; }
    public string? CopySourceIfMatch { get; set; }
    public string? CopySourceIfNoneMatch { get; set; }
    public DateTimeOffset? CopySourceIfModifiedSince { get; set; }
    public DateTimeOffset? CopySourceIfUnmodifiedSince { get; set; }
}

[XmlRoot("CopyPartResult", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class UploadPartCopyResponse
{
    [XmlElement("LastModified")]
    public string LastModified { get; set; } = string.Empty;

    [XmlElement("ETag")]
    public string ETag { get; set; } = string.Empty;
}
