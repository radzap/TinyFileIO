using System.Security.Claims;
using TinyFileIO.Services;

namespace TinyFileIO.Endpoints;

/// <summary>
/// Minimal API endpoints for pre-signed URL management.
/// All endpoints require the /_tfio session cookie (Blazor web UI authentication).
/// </summary>
public static class S3ManagementEndpoints
{
    public static void MapS3ManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/_tfio/api/s3")
            .RequireAuthorization();

        // ── Pre-signed URLs ───────────────────────────────────────────────────

        /// POST /_tfio/api/s3/presign
        /// Body: { "method": "GET"|"PUT", "bucket": "...", "key": "...", "expiresInSeconds": 3600 }
        group.MapPost("/presign", async (
            PresignRequest req,
            ClaimsPrincipal user,
            IPresignedUrlService presigned,
            IAuthorizationProvider authz,
            HttpContext ctx) =>
        {
            var userId       = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var username     = user.FindFirstValue(ClaimTypes.Name) ?? userId;
            var isSuperAdmin = user.FindFirstValue("is_super_admin") == "true";

            var identity = new CallerIdentity
            {
                UserId       = userId,
                Username     = username,
                IsSuperAdmin = isSuperAdmin
            };

            var method     = (req.Method ?? "GET").ToUpperInvariant();
            var permission = method == "PUT" ? BucketPermission.Add : BucketPermission.Read;

            var access = await authz.CheckAccessAsync(identity, req.Bucket, permission);
            if (access != AclCheckResult.Allowed)
                return Results.Forbid();

            var token = presigned.Create(
                method:           method,
                bucket:           req.Bucket,
                key:              req.Key,
                userId:           userId,
                username:         username,
                isSuperAdmin:     isSuperAdmin,
                expiresInSeconds: req.ExpiresInSeconds > 0 ? req.ExpiresInSeconds : 3600);

            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var url = $"{baseUrl}/{req.Bucket}/{req.Key}?X-Tfio-Presign={Uri.EscapeDataString(token.Token)}";

            return Results.Ok(new
            {
                Url        = url,
                token.Token,
                token.ExpiresAt,
                Method     = method,
                token.Bucket,
                token.Key
            });
        });
    }

    private sealed record PresignRequest(
        string Method,
        string Bucket,
        string Key,
        int ExpiresInSeconds = 3600);
}
