using System.Xml.Serialization;

namespace TinyFileIO.Models.Api.Multipart;

/// <summary>Headers for CreateMultipartUpload (POST /{bucket}/{key}?uploads).</summary>
public sealed class CreateMultipartUploadRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";
    public string? StorageClass { get; set; }
    public string? ServerSideEncryption { get; set; }
    public string? CannedAcl { get; set; }
    public string? Tagging { get; set; }
    public Dictionary<string, string> UserMetadata { get; set; } = [];
}

[XmlRoot("InitiateMultipartUploadResult", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class CreateMultipartUploadResponse
{
    [XmlElement("Bucket")]
    public string Bucket { get; set; } = string.Empty;

    [XmlElement("Key")]
    public string Key { get; set; } = string.Empty;

    [XmlElement("UploadId")]
    public string UploadId { get; set; } = string.Empty;
}
