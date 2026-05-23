using System.Net;
using Microsoft.AspNetCore.Mvc;
using TinyFileIO.Models.Api.Common;
using TinyFileIO.Services;

namespace TinyFileIO.Controllers;

/// <summary>
/// Base class for all S3-compatible API controllers.
/// Provides XML serialization helpers and standard error responses.
/// </summary>
[ApiController]
public abstract class S3ControllerBase : ControllerBase
{
    protected readonly IS3XmlSerializer Xml;

    protected S3ControllerBase(IS3XmlSerializer xml)
    {
        Xml = xml;
    }

    // ── Response helpers ──────────────────────────────────────────────────────

    /// <summary>Returns an XML-serialized 200 OK response.</summary>
    protected ContentResult XmlOk<T>(T body)
        => XmlContent(body, StatusCodes.Status200OK);

    /// <summary>Returns a serialized XML response with the given status code.</summary>
    protected ContentResult XmlContent<T>(T body, int statusCode)
    {
        var xml = Xml.Serialize(body);
        return new ContentResult
        {
            Content = xml,
            ContentType = "application/xml",
            StatusCode = statusCode
        };
    }

    /// <summary>Returns an S3-formatted XML error response.</summary>
    protected ContentResult S3Error(
        string code,
        string message,
        int statusCode,
        string? resource = null,
        string? bucketName = null,
        string? key = null)
    {
        var requestId = HttpContext.Items["x-amz-request-id"] as string ?? string.Empty;
        var hostId = HttpContext.Items["x-amz-id-2"] as string ?? string.Empty;

        var error = new S3ErrorResponse
        {
            Code = code,
            Message = message,
            Resource = resource,
            BucketName = bucketName,
            Key = key,
            RequestId = requestId,
            HostId = hostId
        };

        return XmlContent(error, statusCode);
    }

    protected ContentResult NoSuchBucket(string bucket)
        => S3Error(S3ErrorCodes.NoSuchBucket, "The specified bucket does not exist.",
            StatusCodes.Status404NotFound, bucketName: bucket);

    protected ContentResult NoSuchKey(string bucket, string key)
        => S3Error(S3ErrorCodes.NoSuchKey, "The specified key does not exist.",
            StatusCodes.Status404NotFound, bucketName: bucket, key: key);

    protected ContentResult NoSuchUpload(string uploadId)
        => S3Error(S3ErrorCodes.NoSuchUpload, "The specified upload does not exist.",
            StatusCodes.Status404NotFound, resource: uploadId);

    protected ContentResult InvalidArgument(string message)
        => S3Error(S3ErrorCodes.InvalidArgument, message, StatusCodes.Status400BadRequest);

    protected ContentResult MalformedXml()
        => S3Error(S3ErrorCodes.MalformedXML, "The XML you provided was not well-formed.",
            StatusCodes.Status400BadRequest);

    protected ContentResult InternalError(string message = "We encountered an internal error. Please try again.")
        => S3Error(S3ErrorCodes.InternalError, message, StatusCodes.Status500InternalServerError);

    // ── Request helpers ───────────────────────────────────────────────────────

    /// <summary>Resolves the authenticated owner/user ID from the request context.</summary>
    protected string GetOwnerId()
        => HttpContext.User.Identity?.Name ?? "anonymous";

    /// <summary>Parses the x-amz-copy-source header into bucket and key segments.</summary>
    protected static (string sourceBucket, string sourceKey) ParseCopySource(string copySource)
    {
        var decoded = WebUtility.UrlDecode(copySource).TrimStart('/');
        var slash = decoded.IndexOf('/');
        if (slash < 1)
            return (decoded, string.Empty);
        return (decoded[..slash], decoded[(slash + 1)..]);
    }
}
