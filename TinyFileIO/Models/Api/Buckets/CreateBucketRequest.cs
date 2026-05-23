using System.Xml.Serialization;

namespace TinyFileIO.Models.Api.Buckets;

/// <summary>
/// Optional request body for CreateBucket. Omitted when creating in us-east-1.
/// </summary>
[XmlRoot("CreateBucketConfiguration", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class CreateBucketRequest
{
    /// <summary>
    /// Region constraint, e.g. "eu-west-1". Omit or leave empty for us-east-1.
    /// </summary>
    [XmlElement("LocationConstraint")]
    public string? LocationConstraint { get; set; }
}
