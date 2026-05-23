using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace TinyFileIO.Services;

/// <inheritdoc />
public sealed class PresignedUrlService : IPresignedUrlService
{
    // token → PresignedToken
    private readonly ConcurrentDictionary<string, PresignedToken> _store = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public PresignedToken Create(
        string method,
        string bucket,
        string key,
        string userId,
        string username,
        bool isSuperAdmin,
        int expiresInSeconds = 3600)
    {
        if (expiresInSeconds is < 1 or > 604_800)
            expiresInSeconds = 3600;

        var token = new PresignedToken
        {
            Token       = GenerateToken(),
            HttpMethod  = method.ToUpperInvariant(),
            Bucket      = bucket,
            Key         = key,
            UserId      = userId,
            Username    = username,
            IsSuperAdmin = isSuperAdmin,
            ExpiresAt   = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds),
            CreatedAt   = DateTimeOffset.UtcNow
        };

        _store[token.Token] = token;
        return token;
    }

    /// <inheritdoc />
    public PresignedToken? Validate(string token, string method, string bucket, string key)
    {
        if (!_store.TryGetValue(token, out var t))
            return null;

        if (t.IsExpired)
        {
            _store.TryRemove(token, out _);
            return null;
        }

        if (!string.Equals(t.HttpMethod, method.ToUpperInvariant(), StringComparison.Ordinal))
            return null;

        if (!string.Equals(t.Bucket, bucket, StringComparison.Ordinal))
            return null;

        if (!string.Equals(t.Key, key, StringComparison.Ordinal))
            return null;

        return t;
    }

    /// <inheritdoc />
    public void Purge()
    {
        foreach (var (key, token) in _store)
        {
            if (token.IsExpired)
                _store.TryRemove(key, out _);
        }
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
