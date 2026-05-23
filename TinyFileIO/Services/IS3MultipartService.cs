using TinyFileIO.Models.Api.Multipart;

namespace TinyFileIO.Services;

/// <summary>
/// Storage back-end contract for multipart upload operations.
/// Implement this interface against your actual storage provider.
/// </summary>
public interface IS3MultipartService
{
    Task<CreateMultipartUploadResponse> CreateMultipartUploadAsync(CreateMultipartUploadRequest request, CancellationToken ct = default);

    Task<UploadPartResponse> UploadPartAsync(UploadPartRequest request, Stream partBody, CancellationToken ct = default);

    Task<UploadPartCopyResponse> UploadPartCopyAsync(UploadPartCopyRequest request, CancellationToken ct = default);

    Task<CompleteMultipartUploadResponse> CompleteMultipartUploadAsync(CompleteMultipartUploadRequest request, CancellationToken ct = default);

    Task AbortMultipartUploadAsync(AbortMultipartUploadRequest request, CancellationToken ct = default);

    Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(ListMultipartUploadsRequest request, CancellationToken ct = default);

    Task<ListPartsResponse> ListPartsAsync(ListPartsRequest request, CancellationToken ct = default);
}
