using System.Xml.Serialization;

namespace TinyFileIO.Models.Api.Common;

public sealed class S3Owner
{
    [XmlElement("ID")]
    public string Id { get; set; } = string.Empty;

    [XmlElement("DisplayName")]
    public string DisplayName { get; set; } = string.Empty;
}
