using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TinyFileIO.Models.Entities;

/// <summary>
/// Per-user access control entry.
/// When <see cref="BucketName"/> is null the entry is a global ACL rule
/// that applies to all buckets.
/// </summary>
[Table("user_acls")]
public sealed class UserAcl
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>
    /// Target bucket name. NULL means the rule applies globally to all buckets.
    /// </summary>
    [MaxLength(63)]
    [Column("bucket_name")]
    public string? BucketName { get; set; }

    [Column("can_read")]
    public bool CanRead { get; set; }

    [Column("can_add")]
    public bool CanAdd { get; set; }

    [Column("can_update")]
    public bool CanUpdate { get; set; }

    [Column("can_delete")]
    public bool CanDelete { get; set; }

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
