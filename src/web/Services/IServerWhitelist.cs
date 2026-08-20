using McServerMgmnt.Data;

namespace McServerMgmnt.Services;

public interface IServerWhitelist
{
    /// <summary>True once a real whitelist file is wired up. The page shows a banner while this is false.</summary>
    bool IsConnected { get; }
    Task<IReadOnlyList<Player>> GetWhitelistAsync(CancellationToken ct = default);

    /// <summary>Adds one player to the whitelist. The UUID is resolved from the name.</summary>
    Task SaveAsync(Player player, CancellationToken ct = default);

    /// <summary>Takes one player off the whitelist.</summary>
    Task RemoveAsync(string playerName, CancellationToken ct = default);
    Task<Player> GetPlayer(string playerName, CancellationToken ct = default);
}