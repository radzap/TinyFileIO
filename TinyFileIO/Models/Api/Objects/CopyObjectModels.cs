using System.Xml.Serialization;

namespace TinyFileIO.Models.Api.Objects;

/// <summary>Request body for CopyObject. Headers drive the operation.</summary>
public sealed class CopyObjectRequest
{
    public string DestinationBucket { get; set; } = string.Empty;
    public string DestinationKey { get; set; } = string.Empty;

    /// <summary>x-amz-copy-source value: "/source-bucket/source-key" (URL-encoded).</summary>
    public string CopySource { get; set; } = string.Empty;

    /// <summary>Version of the source object.</summary>
    public string? CopySourceVersionId { get; set; }

    /// <summary>"COPY" (default) or "REPLACE".</summary>
    public string MetadataDirective { get; set; } = "COPY";

    /// <summary>"COPY" (default) or "REPLACE".</summary>
    public string TaggingDirective { get; set; } = "COPY";

    // Conditional copy headers
    public string? CopySourceIfMatch { get; set; }
    public string? CopySourceIfNoneMatch { get; set; }
    public DateTimeOffset? CopySourceIfModifiedSince { get; set; }
    public DateTimeOffset? CopySourceIfUnmodifiedSince { get; set; }

    public string? StorageClass { get; set; }
    public string? ServerSideEncryption { get; set; }
    public string? CannedAcl { get; set; }
    public string? Tagging { get; set; }

    /// <summary>Replacement metadata when MetadataDirective is REPLACE.</summary>
    public Dictionary<string, string> UserMetadata { get; set; } = [];
}

[XmlRoot("CopyObjectResult", Namespace = "http://s3.amazonaws.com/doc/2006-03-01/")]
public sealed class CopyObjectResponse
{
    [XmlElement("LastModified")]
    public string LastModified { get; set; } = string.Empty;

    [XmlElement("ETag")]
    public string ETag { get; set; } = string.Empty;

    /// <summary>Version ID of the new copy (if versioning is enabled).</summary>
    [XmlIgnore]
    public string? VersionId { get; set; }
}
