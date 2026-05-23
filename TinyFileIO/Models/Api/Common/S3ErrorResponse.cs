using System.Xml.Serialization;

namespace TinyFileIO.Models.Api.Common;

[XmlRoot("Error")]
public sealed class S3ErrorResponse
{
    [XmlElement("Code")]
    public string Code { get; set; } = string.Empty;

    [XmlElement("Message")]
    public string Message { get; set; } = string.Empty;

    [XmlElement("Resource")]
    public string? Resource { get; set; }

    [XmlElement("BucketName")]
    public string? BucketName { get; set; }

    [XmlElement("Key")]
    public string? Key { get; set; }

    [XmlElement("RequestId")]
    public string RequestId { get; set; } = string.Empty;

    [XmlElement("HostId")]
    public string HostId { get; set; } = string.Empty;
}
