using System.Xml.Serialization;
using TinyFileIO.Models.Api.Common;

namespace TinyFileIO.Models.Api.Multipart;

/// <summary>Query parameters for ListMultipartUploads (GET /{bucket}?uploads).</summary>
public sealed class ListMultipartUploadsRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string? Prefix { get; set; }
    public string? Delimiter { get; set; }

    /// <summary>1–1000, default 1000.</summary>
    public int MaxUploads { get; set; } = 1000;

    /// <summary>Return uploads with keys after this value (exclusive).</summary>
    public string? KeyMarker { get; set; }

    /// <summary>Used together with KeyMarker to paginate.</summary>
    public string? UploadIdMarker { get; set; }

    public string? EncodingType { get; set; }
}

public sealed class MultipartUploadEntry
{
    [XmlElement("Key")]
    public string Key { get; set; } = string.Empty;

    [XmlElement("UploadId")]
    public string UploadId { get; set; } = string.Empty;

    [XmlElement("Initiator")]
    public S3Owner Initiator { get; set; } = new();

    [XmlElement("Owner")]
    public S3Owner Owner { get; set; } = new();

    [XmlElement("StorageClass")]
    public string StorageClass { get; set; } = S3StorageClass.Standard;

    [XmlElement("Initiated")]
    public string Initiated { get; set; } = string.Empty;
}

[XmlRoot("ListMultipartUploadsResult", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class ListMultipartUploadsResponse
{
    [XmlElement("Bucket")]
    public string Bucket { get; set; } = string.Empty;

    [XmlElement("KeyMarker")]
    public string KeyMarker { get; set; } = string.Empty;

    [XmlElement("UploadIdMarker")]
    public string UploadIdMarker { get; set; } = string.Empty;

    [XmlElement("NextKeyMarker")]
    public string? NextKeyMarker { get; set; }

    [XmlElement("NextUploadIdMarker")]
    public string? NextUploadIdMarker { get; set; }

    [XmlElement("Prefix")]
    public string? Prefix { get; set; }

    [XmlElement("Delimiter")]
    public string? Delimiter { get; set; }

    [XmlElement("MaxUploads")]
    public int MaxUploads { get; set; }

    [XmlElement("IsTruncated")]
    public bool IsTruncated { get; set; }

    [XmlElement("EncodingType")]
    public string? EncodingType { get; set; }

    [XmlElement("Upload")]
    public List<MultipartUploadEntry> Uploads { get; set; } = [];

    [XmlElement("CommonPrefixes")]
    public List<Buckets.CommonPrefix> CommonPrefixes { get; set; } = [];
}
