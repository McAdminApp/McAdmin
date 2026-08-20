using McServerMgmnt.Data;
using Microsoft.EntityFrameworkCore;

namespace McServerMgmnt.Services;

public record OperationResult(bool Succeeded, string? Error = null)
{
    public static readonly OperationResult Success = new(true);

    public static OperationResult Fail(string error) => new(false, error);
}

/// <summary>All reads and writes against the account store.</summary>
public class UserService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Minimum length for passwords set through the UI. Change here to adjust the policy.</summary>
    public const int MinimumPasswordLength = 8;

    public const int MaximumUsernameLength = 64;

    /// <summary>Usernames are stored and compared lowercase, so "Steve" and "steve" are the same account.</summary>
    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    public async Task<List<UserAccount>> GetUsersAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.OrderBy(u => u.Username).ToListAsync(ct);
    }

    public async Task<UserAccount?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<UserAccount?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        var normalized = Normalize(username);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct);
    }

    /// <summary>Returns the account when the password matches, otherwise null. Stamps the sign-in time.</summary>
    public async Task<UserAccount?> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default)
    {
        var normalized = Normalize(username);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct);

        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<OperationResult> CreateUserAsync(string username, string password, string role, CancellationToken ct = default)
    {
        var normalized = Normalize(username);

        if (ValidateUsername(normalized) is { Succeeded: false } usernameError)
        {
            return usernameError;
        }

        if (ValidatePassword(password) is { Succeeded: false } passwordError)
        {
            return passwordError;
        }

        if (!UserRoles.All.Contains(role))
        {
            return OperationResult.Fail("Pick an account level.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (await db.Users.AnyAsync(u => u.Username == normalized, ct))
        {
            return OperationResult.Fail($"The name {normalized} is taken. Pick another one.");
        }

        db.Users.Add(new UserAccount
        {
            Username = normalized,
            PasswordHash = PasswordHasher.Hash(password),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return OperationResult.Success;
    }

    /// <summary>Changes a user's own password. Requires the current password.</summary>
    public async Task<OperationResult> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return OperationResult.Fail("That account no longer exists.");
        }

        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
        {
            return OperationResult.Fail("The current password is wrong.");
        }

        if (ValidatePassword(newPassword) is { Succeeded: false } error)
        {
            return error;
        }

        if (PasswordHasher.Verify(newPassword, user.PasswordHash))
        {
            return OperationResult.Fail("The new password matches the old one. Choose a different one.");
        }

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);
        return OperationResult.Success;
    }

    /// <summary>Administrator override: sets a password without knowing the old one.</summary>
    public async Task<OperationResult> ResetPasswordAsync(int userId, string newPassword, CancellationToken ct = default)
    {
        if (ValidatePassword(newPassword) is { Succeeded: false } error)
        {
            return error;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return OperationResult.Fail("That account no longer exists.");
        }

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = true;
        await db.SaveChangesAsync(ct);
        return OperationResult.Success;
    }

    public async Task<OperationResult> SetRoleAsync(int userId, string role, CancellationToken ct = default)
    {
        if (!UserRoles.All.Contains(role))
        {
            return OperationResult.Fail("Pick an account level.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return OperationResult.Fail("That account no longer exists.");
        }

        if (user.Role == role)
        {
            return OperationResult.Success;
        }

        if (user.IsAdministrator && await IsLastAdministratorAsync(db, userId, ct))
        {
            return OperationResult.Fail("This is the last administrator. Promote someone else first.");
        }

        user.Role = role;
        await db.SaveChangesAsync(ct);
        return OperationResult.Success;
    }

    public async Task<OperationResult> DeleteUserAsync(int userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return OperationResult.Success;
        }

        if (user.IsAdministrator && await IsLastAdministratorAsync(db, userId, ct))
        {
            return OperationResult.Fail("This is the last administrator. Promote someone else first.");
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        return OperationResult.Success;
    }

    public static OperationResult ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return OperationResult.Fail("Enter a username.");
        }

        if (username.Length > MaximumUsernameLength)
        {
            return OperationResult.Fail($"Usernames are at most {MaximumUsernameLength} characters.");
        }

        if (!username.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.'))
        {
            return OperationResult.Fail("Usernames can use letters, digits, underscore, hyphen and dot.");
        }

        return OperationResult.Success;
    }

    public static OperationResult ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return OperationResult.Fail("Enter a password.");
        }

        if (password.Length < MinimumPasswordLength)
        {
            return OperationResult.Fail($"Passwords need at least {MinimumPasswordLength} characters.");
        }

        return OperationResult.Success;
    }

    private static async Task<bool> IsLastAdministratorAsync(AppDbContext db, int excludedUserId, CancellationToken ct) =>
        await db.Users.CountAsync(u => u.Role == UserRoles.Administrator && u.Id != excludedUserId, ct) == 0;
}
