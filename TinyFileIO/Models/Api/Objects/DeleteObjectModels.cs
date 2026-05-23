namespace TinyFileIO.Models.Api.Objects;

/// <summary>Query parameters for a DeleteObject request.</summary>
public sealed class DeleteObjectRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

    /// <summary>Specific version to permanently delete.</summary>
    public string? VersionId { get; set; }
}

/// <summary>Headers returned after DeleteObject (versioning-aware).</summary>
public sealed class DeleteObjectResponse
{
    /// <summary>Version ID of the delete marker (or the version deleted).</summary>
    public string? VersionId { get; set; }

    /// <summary>True when a delete marker was created (versioning enabled).</summary>
    public bool? DeleteMarker { get; set; }
}
