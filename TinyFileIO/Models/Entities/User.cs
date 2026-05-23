using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TinyFileIO.Models.Entities;

[Table("users")]
public sealed class User
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(256)]
    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    [Column("password")]
    public string Password { get; set; } = string.Empty;

    [Column("is_super_admin")]
    public bool IsSuperAdmin { get; set; }

    // Navigation
    public ICollection<UserAcl> Acls { get; set; } = [];
}
