using System.Xml.Serialization;

namespace TinyFileIO.Models.Api.Multipart;

/// <summary>
/// Request body for CompleteMultipartUpload
/// (POST /{bucket}/{key}?uploadId=ID).
/// </summary>
[XmlRoot("CompleteMultipartUpload", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class CompleteMultipartUploadRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string UploadId { get; set; } = string.Empty;

    [XmlElement("Part")]
    public List<CompletePart> Parts { get; set; } = [];
}

public sealed class CompletePart
{
    [XmlElement("PartNumber")]
    public int PartNumber { get; set; }

    /// <summary>ETag returned by UploadPart / UploadPartCopy (quoted).</summary>
    [XmlElement("ETag")]
    public string ETag { get; set; } = string.Empty;
}

[XmlRoot("CompleteMultipartUploadResult", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class CompleteMultipartUploadResponse
{
    [XmlElement("Location")]
    public string Location { get; set; } = string.Empty;

    [XmlElement("Bucket")]
    public string Bucket { get; set; } = string.Empty;

    [XmlElement("Key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Composite ETag: "&lt;md5&gt;-&lt;partCount&gt;".</summary>
    [XmlElement("ETag")]
    public string ETag { get; set; } = string.Empty;

    [XmlIgnore]
    public string? VersionId { get; set; }
}
