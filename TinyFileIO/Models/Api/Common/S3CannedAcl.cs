namespace TinyFileIO.Models.Api.Common;

/// <summary>Well-known x-amz-acl canned ACL values.</summary>
public static class S3CannedAcl
{
    public const string Private = "private";
    public const string PublicRead = "public-read";
    public const string PublicReadWrite = "public-read-write";
    public const string AuthenticatedRead = "authenticated-read";
    public const string BucketOwnerRead = "bucket-owner-read";
    public const string BucketOwnerFullControl = "bucket-owner-full-control";
    public const string LogDeliveryWrite = "log-delivery-write";
}
