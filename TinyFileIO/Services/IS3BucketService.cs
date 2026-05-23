using TinyFileIO.Models.Api.Buckets;

namespace TinyFileIO.Services;

/// <summary>
/// Storage back-end contract for bucket-level operations.
/// Implement this interface against your actual storage provider.
/// </summary>
public interface IS3BucketService
{
    Task<ListBucketsResponse> ListBucketsAsync(string ownerId, CancellationToken ct = default);

    Task CreateBucketAsync(string bucket, string? region, string ownerId, CancellationToken ct = default);

    /// <returns>True if the bucket exists and is owned by <paramref name="ownerId"/>.</returns>
    Task<bool> BucketExistsAsync(string bucket, CancellationToken ct = default);

    Task DeleteBucketAsync(string bucket, CancellationToken ct = default);

    Task<string?> GetBucketLocationAsync(string bucket, CancellationToken ct = default);

    Task<ListObjectsV2Response> ListObjectsV2Async(string bucket, ListObjectsV2Request request, CancellationToken ct = default);

    Task<ListObjectsV1Response> ListObjectsV1Async(string bucket, ListObjectsV1Request request, CancellationToken ct = default);
}
