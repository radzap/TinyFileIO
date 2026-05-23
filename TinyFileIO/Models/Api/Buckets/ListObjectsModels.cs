using System.Xml.Serialization;
using TinyFileIO.Models.Api.Common;

namespace TinyFileIO.Models.Api.Buckets;

/// <summary>Query parameters for ListObjectsV2 (list-type=2).</summary>
public sealed class ListObjectsV2Request
{
    /// <summary>Key prefix filter.</summary>
    public string? Prefix { get; set; }

    /// <summary>Grouping delimiter (typically "/").</summary>
    public string? Delimiter { get; set; }

    /// <summary>Maximum number of keys to return. 1–1000, default 1000.</summary>
    public int MaxKeys { get; set; } = 1000;

    /// <summary>Opaque token from a previous response's NextContinuationToken.</summary>
    public string? ContinuationToken { get; set; }

    /// <summary>Return keys lexicographically after this value (exclusive).</summary>
    public string? StartAfter { get; set; }

    /// <summary>When true, include Owner element for each object.</summary>
    public bool FetchOwner { get; set; }

    /// <summary>"url" to URL-encode keys in the response.</summary>
    public string? EncodingType { get; set; }
}

/// <summary>Query parameters for ListObjects V1 (legacy).</summary>
public sealed class ListObjectsV1Request
{
    public string? Prefix { get; set; }
    public string? Delimiter { get; set; }
    public int MaxKeys { get; set; } = 1000;

    /// <summary>Return keys after this marker (exclusive).</summary>
    public string? Marker { get; set; }

    public string? EncodingType { get; set; }
}

// ── Shared response models ────────────────────────────────────────────────────

public sealed class ObjectContent
{
    [XmlElement("Key")]
    public string Key { get; set; } = string.Empty;

    [XmlElement("LastModified")]
    public string LastModified { get; set; } = string.Empty;

    [XmlElement("ETag")]
    public string ETag { get; set; } = string.Empty;

    [XmlElement("Size")]
    public long Size { get; set; }

    [XmlElement("StorageClass")]
    public string StorageClass { get; set; } = S3StorageClass.Standard;

    [XmlElement("Owner")]
    public S3Owner? Owner { get; set; }
}

public sealed class CommonPrefix
{
    [XmlElement("Prefix")]
    public string Prefix { get; set; } = string.Empty;
}

[XmlRoot("ListBucketResult", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class ListObjectsV2Response
{
    [XmlElement("Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("Prefix")]
    public string Prefix { get; set; } = string.Empty;

    [XmlElement("KeyCount")]
    public int KeyCount { get; set; }

    [XmlElement("MaxKeys")]
    public int MaxKeys { get; set; }

    [XmlElement("Delimiter")]
    public string? Delimiter { get; set; }

    [XmlElement("IsTruncated")]
    public bool IsTruncated { get; set; }

    /// <summary>Only present when IsTruncated is true.</summary>
    [XmlElement("NextContinuationToken")]
    public string? NextContinuationToken { get; set; }

    [XmlElement("StartAfter")]
    public string? StartAfter { get; set; }

    [XmlElement("EncodingType")]
    public string? EncodingType { get; set; }

    [XmlElement("Contents")]
    public List<ObjectContent> Contents { get; set; } = [];

    [XmlElement("CommonPrefixes")]
    public List<CommonPrefix> CommonPrefixes { get; set; } = [];
}

[XmlRoot("ListBucketResult", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class ListObjectsV1Response
{
    [XmlElement("Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("Prefix")]
    public string Prefix { get; set; } = string.Empty;

    [XmlElement("Marker")]
    public string Marker { get; set; } = string.Empty;

    /// <summary>Only present when IsTruncated is true.</summary>
    [XmlElement("NextMarker")]
    public string? NextMarker { get; set; }

    [XmlElement("MaxKeys")]
    public int MaxKeys { get; set; }

    [XmlElement("Delimiter")]
    public string? Delimiter { get; set; }

    [XmlElement("IsTruncated")]
    public bool IsTruncated { get; set; }

    [XmlElement("EncodingType")]
    public string? EncodingType { get; set; }

    [XmlElement("Contents")]
    public List<ObjectContent> Contents { get; set; } = [];

    [XmlElement("CommonPrefixes")]
    public List<CommonPrefix> CommonPrefixes { get; set; } = [];
}
