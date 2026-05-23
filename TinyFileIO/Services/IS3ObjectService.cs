using TinyFileIO.Models.Api.Objects;

namespace TinyFileIO.Services;

/// <summary>
/// Storage back-end contract for object-level operations.
/// Implement this interface against your actual storage provider.
/// </summary>
public interface IS3ObjectService
{
    Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, Stream body, CancellationToken ct = default);

    /// <returns>Metadata plus a readable stream for the object body; null when the key does not exist.</returns>
    Task<(GetObjectResponse Meta, Stream Body)?> GetObjectAsync(GetObjectRequest request, CancellationToken ct = default);

    Task<GetObjectResponse?> HeadObjectAsync(GetObjectRequest request, CancellationToken ct = default);

    Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken ct = default);

    Task<DeleteObjectsResponse> DeleteObjectsAsync(string bucket, DeleteObjectsRequest request, CancellationToken ct = default);

    Task<CopyObjectResponse> CopyObjectAsync(CopyObjectRequest request, CancellationToken ct = default);
}
