using TinyFileIO.Services;
using TinyFileIO.Tests.Infrastructure;

namespace TinyFileIO.Tests;

public sealed class UserManagementServiceTests : IDisposable
{
    private readonly TempDirectory _tmp = new();
    private readonly UserManagementService _sut;

    public UserManagementServiceTests()
        => _sut = new UserManagementService(TestFactory.CreateInMemoryDbFactory(Guid.NewGuid().ToString()));

    public void Dispose() => _tmp.Dispose();

    private async Task<Guid> CreateUserAsync(string username = "alice", string password = "pass",
        bool isSuperAdmin = false, CancellationToken ct = default)
    {
        var (_, _) = await _sut.CreateAsync(username, password, isSuperAdmin, ct);
        var users = await _sut.GetAllAsync(ct);
        return users.First(u => u.Username == username).Id;
    }

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsSortedByUsername()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.CreateAsync("zara", "p", false, ct);
        await _sut.CreateAsync("alice", "p", false, ct);

        var users = await _sut.GetAllAsync(ct);

        Assert.Equal(2, users.Count);
        Assert.Equal("alice", users[0].Username);
        Assert.Equal("zara", users[1].Username);
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_KnownId_ReturnsUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await CreateUserAsync("bob", ct: ct);
        var user = await _sut.GetByIdAsync(id, ct);
        Assert.NotNull(user);
        Assert.Equal("bob", user!.Username);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _sut.GetByIdAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidInput_PersistsUserAndReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var (ok, error) = await _sut.CreateAsync("newuser", "pass", false, ct);
        Assert.True(ok);
        Assert.Null(error);
        var users = await _sut.GetAllAsync(ct);
        Assert.Contains(users, u => u.Username == "newuser");
    }

    [Theory]
    [InlineData("", "pass")]
    [InlineData("   ", "pass")]
    public async Task CreateAsync_BlankUsername_ReturnsError(string username, string password)
    {
        var (ok, error) = await _sut.CreateAsync(username, password, false, TestContext.Current.CancellationToken);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("user", "")]
    [InlineData("user", "   ")]
    public async Task CreateAsync_BlankPassword_ReturnsError(string username, string password)
    {
        var (ok, error) = await _sut.CreateAsync(username, password, false, TestContext.Current.CancellationToken);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_DuplicateUsername_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.CreateAsync("dup", "pass", false, ct);
        var (ok, error) = await _sut.CreateAsync("dup", "pass2", false, ct);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    // ── UpdateUsernameAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateUsernameAsync_ValidInput_ChangesUsernameAndReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await CreateUserAsync("original", ct: ct);
        var (ok, error) = await _sut.UpdateUsernameAsync(id, "updated", ct);
        Assert.True(ok);
        Assert.Null(error);
        var user = await _sut.GetByIdAsync(id, ct);
        Assert.Equal("updated", user!.Username);
    }

    [Fact]
    public async Task UpdateUsernameAsync_UnknownId_ReturnsError()
    {
        var (ok, error) = await _sut.UpdateUsernameAsync(Guid.NewGuid(), "newname", TestContext.Current.CancellationToken);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateUsernameAsync_BlankNewUsername_ReturnsError(string newName)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await CreateUserAsync(ct: ct);
        var (ok, error) = await _sut.UpdateUsernameAsync(id, newName, ct);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingUser_RemovesAndReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await CreateUserAsync("todelete", ct: ct);
        var (ok, error) = await _sut.DeleteAsync(id, ct);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Null(await _sut.GetByIdAsync(id, ct));
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsError()
    {
        var (ok, error) = await _sut.DeleteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.False(ok);
        Assert.NotNull(error);
    }
}
