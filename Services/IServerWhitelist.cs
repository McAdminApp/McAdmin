namespace McServerMgmnt.Services;

public interface IServerWhitelist
{
    /// <summary>True once a real whitelist file is wired up. The page shows a banner while this is false.</summary>
    bool IsConnected { get; }
    Task<IReadOnlyList<ServerSetting>> GetWhitelistAsync(CancellationToken ct = default);
    Task SaveAsync(IReadOnlyDictionary<string, string> changes, CancellationToken ct = default);
}