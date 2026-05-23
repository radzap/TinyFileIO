namespace TinyFileIO.Models.Api.Multipart;

/// <summary>Query parameters for AbortMultipartUpload (DELETE /{bucket}/{key}?uploadId=ID).</summary>
public sealed class AbortMultipartUploadRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string UploadId { get; set; } = string.Empty;
}
