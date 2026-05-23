using System.Xml.Serialization;

namespace TinyFileIO.Models.Api.Objects;

/// <summary>
/// Request body for DeleteObjects (POST /{bucket}?delete).
/// </summary>
[XmlRoot("Delete", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class DeleteObjectsRequest
{
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// When true, only errors are returned in the response (no Deleted entries).
    /// </summary>
    [XmlElement("Quiet")]
    public bool Quiet { get; set; }

    [XmlElement("Object")]
    public List<DeleteObjectEntry> Objects { get; set; } = [];
}

public sealed class DeleteObjectEntry
{
    [XmlElement("Key")]
    public string Key { get; set; } = string.Empty;

    [XmlElement("VersionId")]
    public string? VersionId { get; set; }
}

[XmlRoot("DeleteResult", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class DeleteObjectsResponse
{
    [XmlElement("Deleted")]
    public List<DeletedEntry> Deleted { get; set; } = [];

    [XmlElement("Error")]
    public List<DeleteErrorEntry> Errors { get; set; } = [];
}

public sealed class DeletedEntry
{
    [XmlElement("Key")]
    public string Key { get; set; } = string.Empty;

    [XmlElement("VersionId")]
    public string? VersionId { get; set; }

    [XmlElement("DeleteMarker")]
    public bool? DeleteMarker { get; set; }

    [XmlElement("DeleteMarkerVersionId")]
    public string? DeleteMarkerVersionId { get; set; }
}

public sealed class DeleteErrorEntry
{
    [XmlElement("Key")]
    public string Key { get; set; } = string.Empty;

    [XmlElement("VersionId")]
    public string? VersionId { get; set; }

    [XmlElement("Code")]
    public string Code { get; set; } = string.Empty;

    [XmlElement("Message")]
    public string Message { get; set; } = string.Empty;
}
