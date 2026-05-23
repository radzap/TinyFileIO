namespace TinyFileIO.Services;

public sealed class UserDto
{
    public Guid Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public bool IsSuperAdmin { get; init; }
}

public sealed class AclRowDto
{
    /// <summary>null means the global rule.</summary>
    public string? BucketName { get; set; }
    public bool CanRead   { get; set; }
    public bool CanAdd    { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }

    public bool All
    {
        get => CanRead && CanAdd && CanUpdate && CanDelete;
        set { CanRead = CanAdd = CanUpdate = CanDelete = value; }
    }
}

public interface IUserManagementService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default);
    Task<UserDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(bool ok, string? error)> CreateAsync(string username, string password, bool isSuperAdmin, CancellationToken ct = default);
    Task<(bool ok, string? error)> UpdateUsernameAsync(Guid id, string newUsername, CancellationToken ct = default);
    Task<(bool ok, string? error)> UpdatePasswordAsync(Guid id, string newPassword, CancellationToken ct = default);
    Task<(bool ok, string? error)> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns ACL rows for a user: first row is global (BucketName == null), then one per known bucket.</summary>
    Task<List<AclRowDto>> GetAclRowsAsync(Guid userId, IEnumerable<string> bucketNames, CancellationToken ct = default);
    Task<(bool ok, string? error)> SaveAclRowsAsync(Guid userId, IEnumerable<AclRowDto> rows, CancellationToken ct = default);
}
