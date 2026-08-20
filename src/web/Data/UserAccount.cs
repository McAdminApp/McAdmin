using System.ComponentModel.DataAnnotations;

namespace McServerMgmnt.Data;

/// <summary>Account levels. Stored as a string in SQLite so the values stay readable in the DB.</summary>
public static class UserRoles
{
    public const string User = "User";
    public const string Administrator = "Administrator";

    public static readonly string[] All = [User, Administrator];
}

public class UserAccount
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash in the format produced by <see cref="Services.PasswordHasher"/>.</summary>
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>One of <see cref="UserRoles"/>.</summary>
    [Required, MaxLength(32)]
    public string Role { get; set; } = UserRoles.User;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Set on the seeded admin account; cleared the first time the password is changed.</summary>
    public bool MustChangePassword { get; set; }

    public bool IsAdministrator => Role == UserRoles.Administrator;
}
