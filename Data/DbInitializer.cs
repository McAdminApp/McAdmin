using McServerMgmnt.Services;
using Microsoft.EntityFrameworkCore;

namespace McServerMgmnt.Data;

public static class DbInitializer
{
    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "admin";

    /// <summary>
    /// Creates the SQLite file if it is missing and seeds the first administrator.
    /// The seeded account is flagged so the UI nags until the password is changed.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var dbFactory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbInitializer));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);

        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        db.Users.Add(new UserAccount
        {
            Username = DefaultAdminUsername,
            PasswordHash = PasswordHasher.Hash(DefaultAdminPassword),
            Role = UserRoles.Administrator,
            CreatedAt = DateTimeOffset.UtcNow,
            MustChangePassword = true
        });

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Seeded the first administrator: username {Username}, password {Password}. Change it after signing in.",
            DefaultAdminUsername, DefaultAdminPassword);
    }
}
