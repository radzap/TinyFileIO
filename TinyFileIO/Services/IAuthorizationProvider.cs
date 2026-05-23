namespace TinyFileIO.Services;

/// <summary>
/// Represents an authenticated caller with resolved permissions.
/// </summary>
public sealed class CallerIdentity
{
    public string UserId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public bool IsSuperAdmin { get; init; }
}

/// <summary>
/// Result of an ACL check.
/// </summary>
public enum AclCheckResult
{
    Allowed,
    Denied,
    NotAuthenticated
}

/// <summary>
/// Provides authentication and per-bucket access-control checks.
/// Used by both the S3 API layer and the Blazor web UI.
/// </summary>
public interface IAuthorizationProvider
{
    /// <summary>
    /// Validates <paramref name="username"/> / <paramref name="password"/> and
    /// returns a <see cref="CallerIdentity"/> on success, or <c>null</c> on failure.
    /// </summary>
    Task<CallerIdentity?> AuthenticateAsync(string username, string password, CancellationToken ct = default);

    /// <summary>
    /// Checks whether <paramref name="caller"/> may perform <paramref name="permission"/>
    /// on <paramref name="bucketName"/>. Super-admins are always allowed.
    /// </summary>
    Task<AclCheckResult> CheckAccessAsync(CallerIdentity caller, string bucketName, BucketPermission permission, CancellationToken ct = default);

    /// <summary>
    /// Looks up a user by <paramref name="accessKeyId"/> (= username) and returns
    /// their plaintext S3 secret and identity for HMAC signature verification.
    /// Returns <c>null</c> if the user does not exist.
    /// </summary>
    Task<(CallerIdentity identity, string s3Secret)?> FindS3CredentialAsync(string accessKeyId, CancellationToken ct = default);

    /// <summary>Returns the identity for requests that carry no credentials (anonymous).</summary>
    CallerIdentity Anonymous { get; }
}

/// <summary>
/// The specific right being requested on a bucket.
/// </summary>
public enum BucketPermission
{
    Read,
    Add,
    Update,
    Delete
}
