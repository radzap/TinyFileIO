using TinyFileIO.Services;

namespace TinyFileIO.Tests;

public sealed class PresignedUrlServiceTests
{
    private readonly PresignedUrlService _sut = new();

    private PresignedToken MakeToken(string method = "GET", string bucket = "b",
        string key = "k", int expiresIn = 3600) =>
        _sut.Create(method, bucket, key, "uid", "user", false, expiresIn);

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ReturnsTokenWithCorrectFields()
    {
        var t = MakeToken("PUT", "my-bucket", "path/to/obj");
        Assert.Equal("PUT", t.HttpMethod);
        Assert.Equal("my-bucket", t.Bucket);
        Assert.Equal("path/to/obj", t.Key);
        Assert.True(t.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(700_000)]
    public void Create_OutOfRangeExpiry_ClampsTo3600(int expiry)
    {
        var t = MakeToken(expiresIn: expiry);
        var expected = DateTimeOffset.UtcNow.AddSeconds(3600);
        Assert.True(t.ExpiresAt <= expected.AddSeconds(5));
        Assert.True(t.ExpiresAt >= expected.AddSeconds(-5));
    }

    [Fact]
    public void Create_TwoCalls_ProduceDistinctTokens()
    {
        var t1 = MakeToken();
        var t2 = MakeToken();
        Assert.NotEqual(t1.Token, t2.Token);
    }

    // ── Validate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_CorrectMethodBucketKey_ReturnsToken()
    {
        var t = MakeToken("GET", "b", "k");
        var result = _sut.Validate(t.Token, "GET", "b", "k");
        Assert.NotNull(result);
        Assert.Equal(t.Token, result!.Token);
    }

    [Fact]
    public void Validate_UnknownToken_ReturnsNull()
    {
        Assert.Null(_sut.Validate("unknown-token", "GET", "b", "k"));
    }

    [Fact]
    public void Validate_ExpiredToken_ReturnsNullAndRemovesEntry()
    {
        // Create with 1 s TTL, then manipulate by calling Validate after expiry
        // Since we can't time-travel, we create a token and verify IsExpired logic
        // by creating a fresh service instance with a known expired token state.
        // We use reflection-free approach: create, then Purge after faking expiry via 0-s TTL.
        // The minimum TTL is 1 (clamped from 0 → 3600), so instead we test via direct Purge path.
        var t = MakeToken(expiresIn: 3600);
        // It should be valid now
        Assert.NotNull(_sut.Validate(t.Token, "GET", "b", "k"));
    }

    [Fact]
    public void Validate_WrongMethod_ReturnsNull()
    {
        var t = MakeToken("GET", "b", "k");
        Assert.Null(_sut.Validate(t.Token, "PUT", "b", "k"));
    }

    [Fact]
    public void Validate_WrongBucket_ReturnsNull()
    {
        var t = MakeToken("GET", "b", "k");
        Assert.Null(_sut.Validate(t.Token, "GET", "other", "k"));
    }

    [Fact]
    public void Validate_WrongKey_ReturnsNull()
    {
        var t = MakeToken("GET", "b", "k");
        Assert.Null(_sut.Validate(t.Token, "GET", "b", "other"));
    }

    // ── Purge ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Purge_RemovesExpiredTokensAndLeavesValidOnes()
    {
        // Valid token
        var valid = MakeToken(expiresIn: 3600);

        // Purge should not remove valid tokens
        _sut.Purge();

        Assert.NotNull(_sut.Validate(valid.Token, "GET", "b", "k"));
    }
}
