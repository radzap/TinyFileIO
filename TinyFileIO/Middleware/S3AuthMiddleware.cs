using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;
using TinyFileIO.Services;

namespace TinyFileIO.Middleware;

/// <summary>Represents an authenticated S3 caller stored in HttpContext.Items["S3Caller"].</summary>
public sealed record S3CallerInfo(string UserId, string Username, bool IsSuperAdmin);

/// <summary>
/// Validates S3 request authentication before the request reaches a controller.
/// AccessKeyId = username, SecretKey = plaintext password (stored in User.S3Secret).
/// Supports:
///   - TinyFileIO pre-signed token (query param <c>X-Tfio-Presign</c>) - TinyFileIO-specific opaque token.
///   - AWS SigV4 query-string pre-signed URLs (X-Amz-Signature + X-Amz-* params) - AWS SDK compatible.
///   - AWS SigV4 Authorization header (AWS4-HMAC-SHA256 ...)
///   - AWS SigV2 Authorization header (AWS AccessKeyId:Signature)
/// Sets <c>HttpContext.Items["S3Caller"]</c> to a <see cref="S3CallerInfo"/> on success.
/// Returns 403 XML on failure.
/// </summary>
public sealed class S3AuthMiddleware
{
    private const int ClockSkewMinutes = 15;
    private readonly RequestDelegate _next;

    public S3AuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IAuthorizationProvider authProvider,
        IPresignedUrlService presigned)
    {
        if (ShouldSkipAuth(context.Request))
        {
            await _next(context);
            return;
        }

        // ── 1. TinyFileIO native pre-signed token ────────────────────────────────
        if (context.Request.Query.TryGetValue("X-Tfio-Presign", out var tokenValues))
        {
            var token = tokenValues.FirstOrDefault() ?? string.Empty;
            var (bucket, key) = ExtractBucketKey(context);
            var validated = presigned.Validate(token, context.Request.Method, bucket, key);

            if (validated is null)
            {
                await WriteError(context, "AccessDenied",
                    "Pre-signed token is invalid or has expired.", 403);
                return;
            }

            context.Items["S3Caller"] = new S3CallerInfo(
                validated.UserId, validated.Username, validated.IsSuperAdmin);
            if (!await AuthorizeS3RequestAsync(context, authProvider, (S3CallerInfo)context.Items["S3Caller"]!))
                return;
            await _next(context);
            return;
        }

        // ── 2. SigV4 query-string pre-signed URL (AWS SDK compatible) ────────────
        if (context.Request.Query.ContainsKey("X-Amz-Signature"))
        {
            var caller = await VerifyPresignedSigV4Async(context, authProvider);
            if (caller is null) return;
            context.Items["S3Caller"] = caller;
            if (!await AuthorizeS3RequestAsync(context, authProvider, caller))
                return;
            await _next(context);
            return;
        }

        // ── 3. Authorization header ─────────────────────────────────────────────────
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            await WriteError(context, "AccessDenied", "Authentication is required for S3 API requests.", 403);
            return;
        }

        var auth = authHeader.ToString();
        S3CallerInfo? authenticatedCaller;

        if (auth.StartsWith("AWS4-HMAC-SHA256", StringComparison.OrdinalIgnoreCase))
        {
            var caller = await VerifySigV4Async(context, auth, authProvider);
            if (caller is null) return;
            authenticatedCaller = caller;
        }
        else if (auth.StartsWith("AWS ", StringComparison.OrdinalIgnoreCase))
        {
            var caller = await VerifySigV2Async(context, auth, authProvider);
            if (caller is null) return;
            authenticatedCaller = caller;
        }
        else
        {
            await WriteError(context, "AuthorizationHeaderMalformed",
                "The authorization header is malformed.", 400);
            return;
        }

        context.Items["S3Caller"] = authenticatedCaller;
        if (!await AuthorizeS3RequestAsync(context, authProvider, authenticatedCaller))
            return;

        await _next(context);
    }

    // ── SigV4 header ────────────────────────────────────────────────────────────────

    private static async Task<S3CallerInfo?> VerifySigV4Async(
        HttpContext context, string authHeader, IAuthorizationProvider authProvider)
    {
        if (!TryParseSigV4Header(authHeader,
                out var accessKeyId, out var credentialScope,
                out var signedHeaders, out var receivedSignature,
                out var date, out var region))
        {
            await WriteError(context, "AuthorizationHeaderMalformed",
                "The authorization header is malformed.", 400);
            return null;
        }

        var amzDate = context.Request.Headers["x-amz-date"].FirstOrDefault() ?? date;
        if (!TryParseAmzDate(amzDate, out var requestTime)
            || Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalMinutes) > ClockSkewMinutes)
        {
            await WriteError(context, "RequestTimeTooSkewed",
                "The difference between the request time and the server's time is too large.", 403);
            return null;
        }

        var cred = await authProvider.FindS3CredentialAsync(accessKeyId, context.RequestAborted);
        if (cred is null)
        {
            await WriteError(context, "InvalidAccessKeyId",
                "The access key Id you provided does not exist in our records.", 403);
            return null;
        }

        var payloadHash = context.Request.Headers["x-amz-content-sha256"].FirstOrDefault()
                          ?? "UNSIGNED-PAYLOAD";
        var canonicalRequest = BuildCanonicalRequest(context.Request, signedHeaders, payloadHash, excludeQueryParameters: null);
        var dateStamp = amzDate[..8];
        var stringToSign = BuildStringToSign(amzDate, credentialScope, canonicalRequest);
        var computedSignature = ComputeSigV4Signature(cred.Value.s3Secret, dateStamp, region, stringToSign);

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSignature),
                Encoding.UTF8.GetBytes(receivedSignature)))
        {
            await WriteError(context, "SignatureDoesNotMatch",
                "The request signature we calculated does not match the signature you provided.", 403);
            return null;
        }

        var id = cred.Value.identity;
        return new S3CallerInfo(id.UserId, id.Username, id.IsSuperAdmin);
    }

    // ── SigV4 query-string pre-signed ───────────────────────────────────────────────

    private static async Task<S3CallerInfo?> VerifyPresignedSigV4Async(
        HttpContext context, IAuthorizationProvider authProvider)
    {
        var q = context.Request.Query;
        var algorithm     = q["X-Amz-Algorithm"].FirstOrDefault() ?? string.Empty;
        var credentialRaw = q["X-Amz-Credential"].FirstOrDefault() ?? string.Empty;
        var amzDate       = q["X-Amz-Date"].FirstOrDefault() ?? string.Empty;
        var expiresStr    = q["X-Amz-Expires"].FirstOrDefault() ?? string.Empty;
        var signedHdrs    = q["X-Amz-SignedHeaders"].FirstOrDefault() ?? string.Empty;
        var receivedSig   = q["X-Amz-Signature"].FirstOrDefault() ?? string.Empty;

        if (!algorithm.Equals("AWS4-HMAC-SHA256", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(credentialRaw) || string.IsNullOrEmpty(amzDate)
            || string.IsNullOrEmpty(expiresStr) || string.IsNullOrEmpty(receivedSig))
        {
            await WriteError(context, "AuthorizationQueryParametersError",
                "Missing required X-Amz-* query parameters.", 400);
            return null;
        }

        if (!TryParseAmzDate(amzDate, out var signingTime)
            || !int.TryParse(expiresStr, out var expiresSecs))
        {
            await WriteError(context, "AuthorizationQueryParametersError",
                "X-Amz-Date or X-Amz-Expires is malformed.", 400);
            return null;
        }

        if (DateTimeOffset.UtcNow > signingTime.AddSeconds(expiresSecs))
        {
            await WriteError(context, "AccessDenied", "Request has expired.", 403);
            return null;
        }

        var credParts = credentialRaw.Split('/');
        if (credParts.Length < 5)
        {
            await WriteError(context, "AuthorizationQueryParametersError",
                "X-Amz-Credential is malformed.", 400);
            return null;
        }

        var accessKeyId     = credParts[0];
        var dateStamp       = credParts[1];
        var region          = credParts[2];
        var credentialScope = string.Join('/', credParts[1..]);

        var cred = await authProvider.FindS3CredentialAsync(accessKeyId, context.RequestAborted);
        if (cred is null)
        {
            await WriteError(context, "InvalidAccessKeyId",
                "The access key Id you provided does not exist in our records.", 403);
            return null;
        }

        var signedHeaders    = signedHdrs.Split(';');
        var canonicalRequest = BuildCanonicalRequest(context.Request, signedHeaders, "UNSIGNED-PAYLOAD",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "X-Amz-Signature" });
        var stringToSign     = BuildStringToSign(amzDate, credentialScope, canonicalRequest);
        var computedSig      = ComputeSigV4Signature(cred.Value.s3Secret, dateStamp, region, stringToSign);

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSig),
                Encoding.UTF8.GetBytes(receivedSig)))
        {
            await WriteError(context, "SignatureDoesNotMatch",
                "The request signature we calculated does not match the signature you provided.", 403);
            return null;
        }

        var id = cred.Value.identity;
        return new S3CallerInfo(id.UserId, id.Username, id.IsSuperAdmin);
    }

    // ── SigV2 header ────────────────────────────────────────────────────────────────

    private static async Task<S3CallerInfo?> VerifySigV2Async(
        HttpContext context, string authHeader, IAuthorizationProvider authProvider)
    {
        var rest  = authHeader["AWS ".Length..].Trim();
        var colon = rest.IndexOf(':');
        if (colon < 1)
        {
            await WriteError(context, "AuthorizationHeaderMalformed",
                "The authorization header is malformed.", 400);
            return null;
        }

        var accessKeyId       = rest[..colon];
        var receivedSignature = rest[(colon + 1)..];

        var cred = await authProvider.FindS3CredentialAsync(accessKeyId, context.RequestAborted);
        if (cred is null)
        {
            await WriteError(context, "InvalidAccessKeyId",
                "The access key Id you provided does not exist in our records.", 403);
            return null;
        }

        var stringToSign      = BuildSigV2StringToSign(context.Request);
        var computedSignature = ComputeSigV2Signature(cred.Value.s3Secret, stringToSign);

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSignature),
                Encoding.UTF8.GetBytes(receivedSignature)))
        {
            await WriteError(context, "SignatureDoesNotMatch",
                "The request signature we calculated does not match the signature you provided.", 403);
            return null;
        }

        var id = cred.Value.identity;
        return new S3CallerInfo(id.UserId, id.Username, id.IsSuperAdmin);
    }

    // ── SigV4 helpers ─────────────────────────────────────────────────────────────────

    private static bool TryParseSigV4Header(
        string header,
        out string accessKeyId, out string credentialScope,
        out string[] signedHeaders, out string signature,
        out string date, out string region)
    {
        accessKeyId = credentialScope = signature = date = region = string.Empty;
        signedHeaders = [];

        var payload = header["AWS4-HMAC-SHA256".Length..].Trim();
        var parts   = payload.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3) return false;

        string? rawCredential = null, rawSignedHeaders = null, rawSignature = null;
        foreach (var p in parts)
        {
            if (p.StartsWith("Credential=", StringComparison.OrdinalIgnoreCase))
                rawCredential = p["Credential=".Length..];
            else if (p.StartsWith("SignedHeaders=", StringComparison.OrdinalIgnoreCase))
                rawSignedHeaders = p["SignedHeaders=".Length..];
            else if (p.StartsWith("Signature=", StringComparison.OrdinalIgnoreCase))
                rawSignature = p["Signature=".Length..];
        }

        if (rawCredential is null || rawSignedHeaders is null || rawSignature is null)
            return false;

        var credParts = rawCredential.Split('/');
        if (credParts.Length < 5) return false;

        accessKeyId     = credParts[0];
        date            = credParts[1];
        region          = credParts[2];
        credentialScope = string.Join('/', credParts[1..]);
        signedHeaders   = rawSignedHeaders.Split(';');
        signature       = rawSignature;
        return true;
    }

    private static string BuildCanonicalRequest(HttpRequest request, string[] signedHeaders, string payloadHash, HashSet<string>? excludeQueryParameters)
    {
        var method       = request.Method.ToUpperInvariant();
        var uri          = request.Path.Value ?? "/";
        var canonicalUri = string.Join('/',
            uri.Split('/').Select(seg => Uri.EscapeDataString(seg)));

        var canonicalQs = string.Join('&', request.Query
            .Where(kv => excludeQueryParameters is null || !excludeQueryParameters.Contains(kv.Key))
            .SelectMany(kv => ExpandQueryValues(kv.Key, kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ThenBy(kv => kv.Value, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

        var sortedHeaders = signedHeaders.OrderBy(h => h, StringComparer.Ordinal).ToList();
        var headerLines   = new StringBuilder();
        foreach (var h in sortedHeaders)
        {
            var value = string.Equals(h, "host", StringComparison.OrdinalIgnoreCase)
                ? request.Host.Value ?? string.Empty
                : request.Headers.FirstOrDefault(header =>
                        string.Equals(header.Key, h, StringComparison.OrdinalIgnoreCase))
                    .Value.FirstOrDefault() ?? string.Empty;
            value = NormalizeHeaderValue(value);
            headerLines.Append(h.ToLowerInvariant()).Append(':').Append(value).Append('\n');
        }
        var signedHeaderStr = string.Join(';', sortedHeaders.Select(h => h.ToLowerInvariant()));

        return string.Join('\n',
            method, canonicalUri, canonicalQs,
            headerLines.ToString(), signedHeaderStr, payloadHash);
    }

    private static string BuildStringToSign(string amzDate, string credentialScope, string canonicalRequest)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest));
        return $"AWS4-HMAC-SHA256\n{amzDate}\n{credentialScope}\n{HexEncode(hash)}";
    }

    private static string ComputeSigV4Signature(
        string secretKey, string dateStamp, string region, string stringToSign)
    {
        var kDate    = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secretKey), dateStamp);
        var kRegion  = HmacSha256(kDate, region);
        var kService = HmacSha256(kRegion, "s3");
        var kSigning = HmacSha256(kService, "aws4_request");
        return HexEncode(HmacSha256(kSigning, stringToSign));
    }

    private static byte[] HmacSha256(byte[] key, string data)
        => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    // ── SigV2 helpers ─────────────────────────────────────────────────────────────────

    private static string BuildSigV2StringToSign(HttpRequest request)
    {
        var method      = request.Method.ToUpperInvariant();
        var contentMd5  = request.Headers.ContentMD5.FirstOrDefault() ?? string.Empty;
        var contentType = request.ContentType ?? string.Empty;
        var hasAmzDate = request.Headers.ContainsKey("x-amz-date");
        var date       = hasAmzDate ? string.Empty : request.Headers.Date.FirstOrDefault() ?? string.Empty;

        var amzHeaders = request.Headers
            .Where(h => h.Key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(h => h.Key.ToLowerInvariant(), StringComparer.Ordinal)
            .Select(h => $"{h.Key.ToLowerInvariant()}:{h.Value.FirstOrDefault()?.Trim()}");
        var canonicalAmzHeaders = string.Join('\n', amzHeaders);
        if (!string.IsNullOrEmpty(canonicalAmzHeaders)) canonicalAmzHeaders += "\n";

        var path = request.Path.Value ?? "/";
        var subResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "acl","cors","delete","lifecycle","location","logging","notification",
            "partNumber","policy","requestPayment","restore","tagging","torrent",
            "uploadId","uploads","versionId","versioning","versions","website"
        };
        var qs = request.Query
            .Where(kv => subResources.Contains(kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => string.IsNullOrEmpty(kv.Value) ? kv.Key : $"{kv.Key}={kv.Value}");
        var canonicalQs = string.Join('&', qs);
        var canonicalResource = string.IsNullOrEmpty(canonicalQs) ? path : $"{path}?{canonicalQs}";

        return $"{method}\n{contentMd5}\n{contentType}\n{date}\n{canonicalAmzHeaders}{canonicalResource}";
    }

    private static string ComputeSigV2Signature(string secretKey, string stringToSign)
        => Convert.ToBase64String(
            HMACSHA1.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(stringToSign)));

    // ── Utilities ────────────────────────────────────────────────────────────────────

    private static bool TryParseAmzDate(string amzDate, out DateTimeOffset dt)
    {
        dt = default;
        if (string.IsNullOrEmpty(amzDate)) return false;
        return DateTimeOffset.TryParseExact(
            amzDate, "yyyyMMddTHHmmssZ",
            null, System.Globalization.DateTimeStyles.AssumeUniversal, out dt);
    }

    private static string HexEncode(byte[] bytes) => Convert.ToHexStringLower(bytes);

    private static IEnumerable<KeyValuePair<string, string>> ExpandQueryValues(string key, StringValues values)
    {
        var encodedKey = Uri.EscapeDataString(key);
        if (values.Count == 0)
        {
            yield return new KeyValuePair<string, string>(encodedKey, string.Empty);
            yield break;
        }

        foreach (var value in values)
            yield return new KeyValuePair<string, string>(encodedKey, Uri.EscapeDataString(value ?? string.Empty));
    }

    private static string NormalizeHeaderValue(string value)
        => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool ShouldSkipAuth(HttpRequest request)
    {
        // Skip HTTP methods not used by the S3 protocol
        if (!HttpMethods.IsGet(request.Method)
            && !HttpMethods.IsPut(request.Method)
            && !HttpMethods.IsPost(request.Method)
            && !HttpMethods.IsDelete(request.Method)
            && !HttpMethods.IsHead(request.Method))
            return true;

        // Skip WebSocket upgrade requests (SignalR, Blazor Server transport)
        if (string.Equals(request.Headers.Upgrade, "websocket", StringComparison.OrdinalIgnoreCase))
            return true;

        var path = request.Path.Value ?? string.Empty;
        return path.StartsWith("/_tfio", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/Components/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/not-found", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/TinyFileIO.", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/app.", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/_blazor/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/favicon.ico", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> AuthorizeS3RequestAsync(
        HttpContext context, IAuthorizationProvider authProvider, S3CallerInfo caller)
    {
        var (bucket, key) = ExtractBucketKey(context);
        if (string.IsNullOrEmpty(bucket))
            return true;

        var identity = new CallerIdentity
        {
            UserId = caller.UserId,
            Username = caller.Username,
            IsSuperAdmin = caller.IsSuperAdmin
        };

        foreach (var permission in ResolveRequiredPermissions(context.Request, key))
        {
            var result = await authProvider.CheckAccessAsync(identity, bucket, permission, context.RequestAborted);
            if (result == AclCheckResult.Allowed)
                return true;

            if (result == AclCheckResult.NotAuthenticated)
            {
                await WriteError(context, "AccessDenied", "Authentication is required for S3 API requests.", 403);
                return false;
            }
        }

        await WriteError(context, "AccessDenied", "Access denied.", 403);
        return false;
    }

    private static BucketPermission[] ResolveRequiredPermissions(HttpRequest request, string key)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            return [BucketPermission.Read];

        if (HttpMethods.IsDelete(request.Method))
            return [BucketPermission.Delete];

        if (HttpMethods.IsPut(request.Method))
            return string.IsNullOrEmpty(key)
                ? [BucketPermission.Add]
                : [BucketPermission.Add, BucketPermission.Update];

        if (HttpMethods.IsPost(request.Method))
        {
            if (request.Query.ContainsKey("delete"))
                return [BucketPermission.Delete];

            return string.IsNullOrEmpty(key)
                ? [BucketPermission.Add]
                : [BucketPermission.Add, BucketPermission.Update];
        }

        return [BucketPermission.Read];
    }

    private static (string bucket, string key) ExtractBucketKey(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value?.TrimStart('/') ?? string.Empty;
        var slash = path.IndexOf('/');
        if (slash < 0) return (path, string.Empty);
        return (path[..slash], path[(slash + 1)..]);
    }

    private static async Task WriteError(HttpContext ctx, string code, string message, int status)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/xml";
        await ctx.Response.WriteAsync(
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            $"<Error><Code>{code}</Code><Message>{message}</Message>" +
            $"<RequestId>{ctx.Items["x-amz-request-id"]}</RequestId>" +
            $"<HostId>{ctx.Items["x-amz-id-2"]}</HostId></Error>");
    }
}

