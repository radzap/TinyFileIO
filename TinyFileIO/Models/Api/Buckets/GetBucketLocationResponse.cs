using System.Xml.Serialization;

namespace TinyFileIO.Models.Api.Buckets;

[XmlRoot("LocationConstraint", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class GetBucketLocationResponse
{
    /// <summary>
    /// Region string, e.g. "eu-west-1". Empty string / null means us-east-1.
    /// </summary>
    [XmlText]
    public string? Region { get; set; }
}
