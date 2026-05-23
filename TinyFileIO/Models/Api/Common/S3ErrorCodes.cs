namespace TinyFileIO.Models.Api.Common;

/// <summary>Well-known S3 error code strings.</summary>
public static class S3ErrorCodes
{
    public const string AccessDenied = "AccessDenied";
    public const string AuthorizationHeaderMalformed = "AuthorizationHeaderMalformed";
    public const string BadDigest = "BadDigest";
    public const string BucketAlreadyExists = "BucketAlreadyExists";
    public const string BucketAlreadyOwnedByYou = "BucketAlreadyOwnedByYou";
    public const string BucketNotEmpty = "BucketNotEmpty";
    public const string EntityTooLarge = "EntityTooLarge";
    public const string EntityTooSmall = "EntityTooSmall";
    public const string IncompleteBody = "IncompleteBody";
    public const string InternalError = "InternalError";
    public const string InvalidAccessKeyId = "InvalidAccessKeyId";
    public const string InvalidArgument = "InvalidArgument";
    public const string InvalidBucketName = "InvalidBucketName";
    public const string InvalidDigest = "InvalidDigest";
    public const string InvalidPart = "InvalidPart";
    public const string InvalidPartOrder = "InvalidPartOrder";
    public const string InvalidRange = "InvalidRange";
    public const string InvalidRequest = "InvalidRequest";
    public const string InvalidSecurity = "InvalidSecurity";
    public const string InvalidStorageClass = "InvalidStorageClass";
    public const string InvalidToken = "InvalidToken";
    public const string InvalidURI = "InvalidURI";
    public const string KeyTooLongError = "KeyTooLongError";
    public const string MalformedXML = "MalformedXML";
    public const string MetadataTooLarge = "MetadataTooLarge";
    public const string MethodNotAllowed = "MethodNotAllowed";
    public const string MissingContentLength = "MissingContentLength";
    public const string MissingRequestBodyError = "MissingRequestBodyError";
    public const string MissingSecurityHeader = "MissingSecurityHeader";
    public const string NoSuchBucket = "NoSuchBucket";
    public const string NoSuchKey = "NoSuchKey";
    public const string NoSuchUpload = "NoSuchUpload";
    public const string NoSuchVersion = "NoSuchVersion";
    public const string NotImplemented = "NotImplemented";
    public const string OperationAborted = "OperationAborted";
    public const string RequestTimeout = "RequestTimeout";
    public const string RequestTimeTooSkewed = "RequestTimeTooSkewed";
    public const string ServiceUnavailable = "ServiceUnavailable";
    public const string SignatureDoesNotMatch = "SignatureDoesNotMatch";
    public const string TooManyBuckets = "TooManyBuckets";
    public const string XAmzContentSHA256Mismatch = "XAmzContentSHA256Mismatch";
}
