namespace TinyFileIO.Services;

/// <summary>
/// A short-lived pre-signed URL token stored in memory.
/// Covers both GET (download) and PUT (upload) operations.
/// </summary>
public sealed class PresignedToken
{
    public string Token { get; init; } = string.Empty;

    /// <summary>"GET" or "PUT"</summary>
    public string HttpMethod { get; init; } = "GET";

    public string Bucket { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;

    public string UserId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public bool IsSuperAdmin { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
}

/// <summary>
/// In-memory store for pre-signed URL tokens.
/// Tokens are validated on each S3 request before credentials checks.
/// </summary>
public interface IPresignedUrlService
{
    /// <summary>
    /// Creates a pre-signed token granting <paramref name="method"/> access
    /// to <c>bucket/key</c> for <paramref name="expiresInSeconds"/> seconds.
    /// Only callers with ACL access to the bucket may create GET tokens;
    /// super-admins may create PUT tokens for any bucket.
    /// </summary>
    PresignedToken Create(
        string method,
        string bucket,
        string key,
        string userId,
        string username,
        bool isSuperAdmin,
        int expiresInSeconds = 3600);

    /// <summary>
    /// Validates the token string and returns the token if valid for the given
    /// HTTP method, bucket and key. Returns <c>null</c> if not found or expired.
    /// </summary>
    PresignedToken? Validate(string token, string method, string bucket, string key);

    /// <summary>Removes expired tokens from memory (housekeeping).</summary>
    void Purge();
}
