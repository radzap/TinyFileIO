using Microsoft.EntityFrameworkCore;
using TinyFileIO.Models.Entities;

namespace TinyFileIO.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserAcl> UserAcls => Set<UserAcl>();
    public DbSet<BackgroundJobRun> BackgroundJobRuns => Set<BackgroundJobRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── User ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username)
             .IsUnique();
        });

        // ── UserAcl ───────────────────────────────────────────────────────────
        modelBuilder.Entity<UserAcl>(e =>
        {
            // One user → many ACL entries
            e.HasOne(a => a.User)
             .WithMany(u => u.Acls)
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // Unique constraint: one rule per user per bucket (null = global)
            e.HasIndex(a => new { a.UserId, a.BucketName })
             .IsUnique();
        });

        // ── BackgroundJobRun ─────────────────────────────────────────────────
        modelBuilder.Entity<BackgroundJobRun>(e =>
        {
            e.HasIndex(j => new { j.JobType, j.Status });
            e.HasIndex(j => j.CreatedUtc);
        });
    }
}
