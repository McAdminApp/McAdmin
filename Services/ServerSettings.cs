namespace McServerMgmnt.Services;

/// <summary>How a setting is edited. Drives which control the settings table draws.</summary>
public enum SettingKind
{
    Text,
    Number,
    Toggle,
    Choice
}

/// <summary>
/// One line of server.properties, plus what the UI needs to render it sensibly.
/// Values are kept as strings so they round-trip to the properties file unchanged.
/// </summary>
public record ServerSetting(
    string Key,
    string Label,
    string Description,
    SettingKind Kind,
    string? Value,
    string Group = "General",
    IReadOnlyList<string>? Choices = null,
    int? Minimum = null,
    int? Maximum = null,
    bool RequiresRestart = false);

/// <summary>
/// The seam for server.properties. Implement this against the real file (or over
/// the same gRPC channel as <see cref="IMinecraftServerController"/>) and register
/// it in Program.cs in place of <see cref="PlaceholderServerSettingsStore"/>; the
/// settings page needs no changes.
/// </summary>
public interface IServerSettingsStore
{
    /// <summary>True once a real properties file is wired up. The page shows a banner while this is false.</summary>
    bool IsConnected { get; }

    Task<IReadOnlyList<ServerSetting>> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>Writes only the settings the user actually changed, keyed by properties key.</summary>
    Task SaveAsync(IReadOnlyDictionary<string, string> changes, CancellationToken ct = default);
}

/// <summary>
/// Stand-in until the real properties file is wired up. It returns a representative
/// slice of server.properties so the table has something to lay out, and refuses to save.
/// Replace the whole class — the sample list is only here to show the shape.
/// </summary>
public class PlaceholderServerSettingsStore : IServerSettingsStore
{
    private static readonly ServerSetting[] Sample =
    [
        // World
        new("level-name", "World folder", "Directory the world is loaded from.",
            SettingKind.Text, "world", "World", RequiresRestart: true),
        new("level-seed", "World seed", "Seed used when generating a new world. Leave empty for random.",
            SettingKind.Text, "", "World", RequiresRestart: true),
        new("level-type", "World type", "Terrain generator used for new chunks.",
            SettingKind.Choice, "minecraft:normal", "World",
            Choices: ["minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified"],
            RequiresRestart: true),
        new("allow-nether", "Allow the Nether", "Lets players travel through Nether portals.",
            SettingKind.Toggle, "true", "World"),
        new("spawn-protection", "Spawn protection", "Blocks around spawn that only operators can build in.",
            SettingKind.Number, "16", "World", Minimum: 0, Maximum: 256),

        // Players
        new("max-players", "Player limit", "How many players can be online at once.",
            SettingKind.Number, "20", "Players", Minimum: 1, Maximum: 1000),
        new("white-list", "Whitelist", "Only players on the whitelist can join.",
            SettingKind.Toggle, "false", "Players"),
        new("online-mode", "Verify accounts with Mojang", "Turn off only on a private network. Off means anyone can join under any name.",
            SettingKind.Toggle, "true", "Players", RequiresRestart: true),
        new("player-idle-timeout", "Idle kick", "Minutes before an idle player is kicked. 0 never kicks.",
            SettingKind.Number, "0", "Players", Minimum: 0, Maximum: 120),

        // Gameplay
        new("gamemode", "Default game mode", "Mode new players start in.",
            SettingKind.Choice, "survival", "Gameplay",
            Choices: ["survival", "creative", "adventure", "spectator"]),
        new("difficulty", "Difficulty", "How hard mobs and hunger hit.",
            SettingKind.Choice, "easy", "Gameplay",
            Choices: ["peaceful", "easy", "normal", "hard"]),
        new("hardcore", "Hardcore", "Death is permanent and players switch to spectator.",
            SettingKind.Toggle, "false", "Gameplay", RequiresRestart: true),
        new("pvp", "Player versus player", "Lets players damage each other.",
            SettingKind.Toggle, "true", "Gameplay"),
        new("enable-command-block", "Command blocks", "Lets command blocks run in the world.",
            SettingKind.Toggle, "false", "Gameplay", RequiresRestart: true),

        // Network
        new("motd", "Server description", "The line shown under the server name in the multiplayer list.",
            SettingKind.Text, "A Minecraft Server", "Network"),
        new("server-port", "Port", "TCP port the server listens on.",
            SettingKind.Number, "25565", "Network", Minimum: 1, Maximum: 65535, RequiresRestart: true),
        new("view-distance", "View distance", "Chunks sent to each player. Lower this first if the server is struggling.",
            SettingKind.Number, "10", "Network", Minimum: 3, Maximum: 32),
        new("simulation-distance", "Simulation distance", "Chunks that keep ticking around each player.",
            SettingKind.Number, "10", "Network", Minimum: 3, Maximum: 32),
        new("enable-status", "Answer status pings", "Shows the server as online in the multiplayer list.",
            SettingKind.Toggle, "true", "Network")
    ];

    public bool IsConnected => false;

    public Task<IReadOnlyList<ServerSetting>> GetSettingsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ServerSetting>>(Sample);

    public Task SaveAsync(IReadOnlyDictionary<string, string> changes, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "No server.properties file is wired up yet. Implement IServerSettingsStore and register it in Program.cs.");
}
