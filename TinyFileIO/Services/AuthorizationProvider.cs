using Microsoft.EntityFrameworkCore;
using TinyFileIO.Data;
using TinyFileIO.Models.Entities;

namespace TinyFileIO.Services;

/// <summary>
/// Authorization provider backed by the SQLite database.
/// When <c>UseStaticAccount</c> is <c>true</c> in appsettings, the configured
/// <c>StaticUser</c> / <c>StaticPassword</c> is treated as a super-admin and is
/// accepted without a database round-trip.
/// </summary>
public sealed class AuthorizationProvider : IAuthorizationProvider
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly string? _staticUser;
    private readonly string? _staticPassword;
    private readonly bool _useStatic;

    public CallerIdentity Anonymous { get; } = new CallerIdentity
    {
        UserId = "anonymous",
        Username = "anonymous",
        IsSuperAdmin = false
    };

    public AuthorizationProvider(IDbContextFactory<AppDbContext> dbFactory, IConfiguration config)
    {
        _dbFactory = dbFactory;
        _useStatic = config.GetValue<bool>("UseStaticAccount");
        _staticUser = config["StaticUser"];
        _staticPassword = config["StaticPassword"];
    }

    public async Task<CallerIdentity?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (_useStatic
            && !string.IsNullOrEmpty(_staticUser)
            && !string.IsNullOrEmpty(_staticPassword)
            && string.Equals(username, _staticUser, StringComparison.Ordinal)
            && string.Equals(password, _staticPassword, StringComparison.Ordinal))
        {
            return new CallerIdentity
            {
                UserId = "static-admin",
                Username = _staticUser,
                IsSuperAdmin = true
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        if (user is null)
            return null;

        if (user.Password != password)
            return null;

        return new CallerIdentity
        {
            UserId = user.Id.ToString(),
            Username = user.Username,
            IsSuperAdmin = user.IsSuperAdmin
        };
    }

    public async Task<(CallerIdentity identity, string s3Secret)?> FindS3CredentialAsync(
        string accessKeyId, CancellationToken ct = default)
    {
        // Static account: username acts as AccessKeyId, password as S3 secret
        if (_useStatic
            && !string.IsNullOrEmpty(_staticUser)
            && !string.IsNullOrEmpty(_staticPassword)
            && string.Equals(accessKeyId, _staticUser, StringComparison.Ordinal))
        {
            var identity = new CallerIdentity
            {
                UserId      = "static-admin",
                Username    = _staticUser,
                IsSuperAdmin = true
            };
            return (identity, _staticPassword ?? string.Empty);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == accessKeyId, ct);

        if (user is null) return null;

        return (new CallerIdentity
        {
            UserId      = user.Id.ToString(),
            Username    = user.Username,
            IsSuperAdmin = user.IsSuperAdmin
        }, user.Password);
    }

    public async Task<AclCheckResult> CheckAccessAsync(
        CallerIdentity caller,
        string bucketName,
        BucketPermission permission,
        CancellationToken ct = default)
    {
        if (caller.IsSuperAdmin)
            return AclCheckResult.Allowed;

        if (caller == Anonymous)
            return AclCheckResult.NotAuthenticated;

        if (!Guid.TryParse(caller.UserId, out var userId))
            return AclCheckResult.Denied;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Prefer bucket-specific rule; fall back to global rule (BucketName == null)
        var rule = await db.UserAcls
            .AsNoTracking()
            .Where(a => a.UserId == userId && (a.BucketName == bucketName || a.BucketName == null))
            .OrderByDescending(a => a.BucketName != null) // bucket-specific first
            .FirstOrDefaultAsync(ct);

        if (rule is null)
            return AclCheckResult.Denied;

        var allowed = permission switch
        {
            BucketPermission.Read   => rule.CanRead,
            BucketPermission.Add    => rule.CanAdd,
            BucketPermission.Update => rule.CanUpdate,
            BucketPermission.Delete => rule.CanDelete,
            _                       => false
        };

        return allowed ? AclCheckResult.Allowed : AclCheckResult.Denied;
    }

    }
