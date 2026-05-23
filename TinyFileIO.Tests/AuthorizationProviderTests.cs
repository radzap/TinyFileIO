using Microsoft.EntityFrameworkCore;
using TinyFileIO.Data;
using TinyFileIO.Models.Entities;
using TinyFileIO.Services;
using TinyFileIO.Tests.Infrastructure;

namespace TinyFileIO.Tests;

public sealed class AuthorizationProviderTests : IDisposable
{
    private readonly TempDirectory _tmp = new();
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public AuthorizationProviderTests()
        => _dbFactory = TestFactory.CreateInMemoryDbFactory(Guid.NewGuid().ToString());

    public void Dispose() => _tmp.Dispose();

    private AuthorizationProvider BuildStaticProvider(string user, string password)
    {
        var cfg = TestFactory.ConfigForStaticAccount(_tmp.Path, user, password);
        return new AuthorizationProvider(_dbFactory, cfg);
    }

    private AuthorizationProvider BuildDbProvider()
    {
        var cfg = TestFactory.ConfigFor(_tmp.Path);
        return new AuthorizationProvider(_dbFactory, cfg);
    }

    private async Task AddUserAsync(string username, string password, bool isSuperAdmin = false,
        CancellationToken ct = default)
    {
        await using var db = _dbFactory.CreateDbContext();
        db.Users.Add(new User { Username = username, Password = password, IsSuperAdmin = isSuperAdmin });
        await db.SaveChangesAsync(ct);
    }

    // ── AuthenticateAsync — static account ────────────────────────────────────

    [Fact]
    public async Task AuthenticateAsync_StaticAccount_CorrectCredentials_ReturnsSuperAdmin()
    {
        var sut = BuildStaticProvider("admin", "secret");
        var identity = await sut.AuthenticateAsync("admin", "secret", TestContext.Current.CancellationToken);
        Assert.NotNull(identity);
        Assert.True(identity!.IsSuperAdmin);
    }

    [Fact]
    public async Task AuthenticateAsync_StaticAccount_WrongPassword_ReturnsNull()
    {
        var sut = BuildStaticProvider("admin", "secret");
        Assert.Null(await sut.AuthenticateAsync("admin", "wrong", TestContext.Current.CancellationToken));
    }

    // ── AuthenticateAsync — database ──────────────────────────────────────────

    [Fact]
    public async Task AuthenticateAsync_DbUser_ValidCredentials_ReturnsIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddUserAsync("alice", "pass123", ct: ct);
        var sut = BuildDbProvider();
        var identity = await sut.AuthenticateAsync("alice", "pass123", ct);
        Assert.NotNull(identity);
        Assert.Equal("alice", identity!.Username);
    }

    [Fact]
    public async Task AuthenticateAsync_DbUser_UnknownUsername_ReturnsNull()
    {
        var sut = BuildDbProvider();
        Assert.Null(await sut.AuthenticateAsync("ghost", "pass", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthenticateAsync_DbUser_WrongPassword_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddUserAsync("bob", "correct", ct: ct);
        var sut = BuildDbProvider();
        Assert.Null(await sut.AuthenticateAsync("bob", "wrong", ct));
    }

    // ── FindS3CredentialAsync — static account ────────────────────────────────

    [Fact]
    public async Task FindS3CredentialAsync_StaticAccount_MatchingAccessKey_ReturnsIdentityAndSecret()
    {
        var sut = BuildStaticProvider("admin", "s3secret");
        var result = await sut.FindS3CredentialAsync("admin", TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("s3secret", result!.Value.s3Secret);
        Assert.True(result.Value.identity.IsSuperAdmin);
    }

    // ── FindS3CredentialAsync — database ──────────────────────────────────────

    [Fact]
    public async Task FindS3CredentialAsync_DbUser_KnownAccessKey_ReturnsIdentityAndSecret()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddUserAsync("carol", "mysecret", ct: ct);
        var sut = BuildDbProvider();
        var result = await sut.FindS3CredentialAsync("carol", ct);
        Assert.NotNull(result);
        Assert.Equal("mysecret", result!.Value.s3Secret);
    }

    [Fact]
    public async Task FindS3CredentialAsync_UnknownAccessKey_ReturnsNull()
    {
        var sut = BuildDbProvider();
        Assert.Null(await sut.FindS3CredentialAsync("nobody", TestContext.Current.CancellationToken));
    }
}
