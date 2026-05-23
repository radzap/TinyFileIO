using System.Xml.Serialization;
using TinyFileIO.Models.Api.Common;

namespace TinyFileIO.Models.Api.Buckets;

[XmlRoot("ListAllMyBucketsResult", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class ListBucketsResponse
{
    [XmlElement("Owner")]
    public S3Owner Owner { get; set; } = new();

    [XmlArray("Buckets")]
    [XmlArrayItem("Bucket")]
    public List<BucketEntry> Buckets { get; set; } = [];
}

public sealed class BucketEntry
{
    [XmlElement("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>ISO 8601 UTC creation time, e.g. 2024-01-01T00:00:00.000Z</summary>
    [XmlElement("CreationDate")]
    public string CreationDate { get; set; } = string.Empty;
}
