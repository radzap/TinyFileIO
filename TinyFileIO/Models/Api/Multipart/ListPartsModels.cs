using System.Xml.Serialization;

namespace TinyFileIO.Models.Api.Multipart;

/// <summary>Query parameters for ListParts (GET /{bucket}/{key}?uploadId=ID).</summary>
public sealed class ListPartsRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string UploadId { get; set; } = string.Empty;

    /// <summary>1–1000, default 1000.</summary>
    public int MaxParts { get; set; } = 1000;

    /// <summary>Return parts with PartNumber greater than this value.</summary>
    public int? PartNumberMarker { get; set; }

    public string? EncodingType { get; set; }
}

public sealed class PartEntry
{
    [XmlElement("PartNumber")]
    public int PartNumber { get; set; }

    [XmlElement("LastModified")]
    public string LastModified { get; set; } = string.Empty;

    [XmlElement("ETag")]
    public string ETag { get; set; } = string.Empty;

    [XmlElement("Size")]
    public long Size { get; set; }
}

[XmlRoot("ListPartsResult", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class ListPartsResponse
{
    [XmlElement("Bucket")]
    public string Bucket { get; set; } = string.Empty;

    [XmlElement("Key")]
    public string Key { get; set; } = string.Empty;

    [XmlElement("UploadId")]
    public string UploadId { get; set; } = string.Empty;

    [XmlElement("PartNumberMarker")]
    public int PartNumberMarker { get; set; }

    [XmlElement("NextPartNumberMarker")]
    public int? NextPartNumberMarker { get; set; }

    [XmlElement("MaxParts")]
    public int MaxParts { get; set; }

    [XmlElement("IsTruncated")]
    public bool IsTruncated { get; set; }

    [XmlElement("StorageClass")]
    public string StorageClass { get; set; } = Common.S3StorageClass.Standard;

    [XmlElement("EncodingType")]
    public string? EncodingType { get; set; }

    [XmlElement("Initiator")]
    public Common.S3Owner? Initiator { get; set; }

    [XmlElement("Owner")]
    public Common.S3Owner? Owner { get; set; }

    [XmlElement("Part")]
    public List<PartEntry> Parts { get; set; } = [];
}
