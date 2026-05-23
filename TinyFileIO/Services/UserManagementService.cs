using Microsoft.EntityFrameworkCore;
using TinyFileIO.Data;
using TinyFileIO.Models.Entities;

namespace TinyFileIO.Services;

public sealed class UserManagementService : IUserManagementService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public UserManagementService(IDbContextFactory<AppDbContext> dbFactory)
        => _dbFactory = dbFactory;

    public async Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new UserDto { Id = u.Id, Username = u.Username, IsSuperAdmin = u.IsSuperAdmin })
            .ToListAsync(ct);
    }

    public async Task<UserDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return u is null ? null : new UserDto { Id = u.Id, Username = u.Username, IsSuperAdmin = u.IsSuperAdmin };
    }

    public async Task<(bool ok, string? error)> CreateAsync(
        string username, string password, bool isSuperAdmin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return (false, "Username is required.");
        if (string.IsNullOrWhiteSpace(password)) return (false, "Password is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            return (false, $"Username '{username}' is already taken.");

        db.Users.Add(new User
        {
            Username     = username.Trim(),
            Password     = password,
            IsSuperAdmin = isSuperAdmin
        });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool ok, string? error)> UpdateUsernameAsync(
        Guid id, string newUsername, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newUsername)) return (false, "Username is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return (false, "User not found.");

        if (await db.Users.AnyAsync(u => u.Username == newUsername && u.Id != id, ct))
            return (false, $"Username '{newUsername}' is already taken.");

        user.Username = newUsername.Trim();
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool ok, string? error)> UpdatePasswordAsync(
        Guid id, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword)) return (false, "Password is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return (false, "User not found.");

        user.Password = newPassword;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool ok, string? error)> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return (false, "User not found.");

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<List<AclRowDto>> GetAclRowsAsync(
        Guid userId, IEnumerable<string> bucketNames, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.UserAcls
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);

        var rows = new List<AclRowDto>();

        // Global row first (BucketName == null)
        var global = existing.FirstOrDefault(a => a.BucketName == null);
        rows.Add(new AclRowDto
        {
            BucketName = null,
            CanRead    = global?.CanRead   ?? false,
            CanAdd     = global?.CanAdd    ?? false,
            CanUpdate  = global?.CanUpdate ?? false,
            CanDelete  = global?.CanDelete ?? false
        });

        foreach (var bucket in bucketNames.OrderBy(b => b))
        {
            var rule = existing.FirstOrDefault(a => a.BucketName == bucket);
            rows.Add(new AclRowDto
            {
                BucketName = bucket,
                CanRead    = rule?.CanRead   ?? false,
                CanAdd     = rule?.CanAdd    ?? false,
                CanUpdate  = rule?.CanUpdate ?? false,
                CanDelete  = rule?.CanDelete ?? false
            });
        }

        return rows;
    }

    public async Task<(bool ok, string? error)> SaveAclRowsAsync(
        Guid userId, IEnumerable<AclRowDto> rows, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existing = await db.UserAcls
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            var entity = existing.FirstOrDefault(a => a.BucketName == row.BucketName);

            // Remove rows where all permissions are false
            var hasAny = row.CanRead || row.CanAdd || row.CanUpdate || row.CanDelete;

            if (entity is not null)
            {
                if (!hasAny)
                {
                    db.UserAcls.Remove(entity);
                }
                else
                {
                    entity.CanRead   = row.CanRead;
                    entity.CanAdd    = row.CanAdd;
                    entity.CanUpdate = row.CanUpdate;
                    entity.CanDelete = row.CanDelete;
                }
            }
            else if (hasAny)
            {
                db.UserAcls.Add(new UserAcl
                {
                    UserId     = userId,
                    BucketName = row.BucketName,
                    CanRead    = row.CanRead,
                    CanAdd     = row.CanAdd,
                    CanUpdate  = row.CanUpdate,
                    CanDelete  = row.CanDelete
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return (true, null);
    }
}
