namespace McServerMgmnt.Services.Factories;

public class ServerSettingsStore : IServerSettingsStore
{
    private const string PROPERTIES_FILE = "server.properties";

    private static readonly ServerSetting[] ServerSettings =
    [
        // World
        new("level-name", "World folder", "The name of the world. This will be the name of the folder in which the world is saved.", SettingKind.Text, null, "World", RequiresRestart: true),
        new("level-seed", "World seed", "The seed used to generate the world. Leave blank to default to random.", SettingKind.Text, null, "World", RequiresRestart: true),
        new("level-type", "World type", "Defines the type of the world generator.", SettingKind.Choice, null, "World",
            Choices: ["minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified", "minecraft:single_biome_surface", "buffet", "default_1_1", "customized"],
            RequiresRestart: true),
        new("generate-structures", "Generate structures", "Defines whether structures (such as villages) will be generated.", SettingKind.Toggle, null, "World", RequiresRestart: true),
        new("generator-settings", "Generator settings", "The settings used to customize world generation. Follow its format and write the corresponding JSON string.", SettingKind.Text, null, "World", RequiresRestart: true),
        new("max-world-size", "Max world size", "The maximum allowed size of the world radius, in blocks. This only affects the chunks that are generated when the world is initially created, not the world border. Limited to 29999984.", SettingKind.Number, null, "World", Minimum: 1, Maximum: 29999984, RequiresRestart: true),
        new("initial-enabled-packs", "Enabled data packs", "Comma-separated list of datapacks to be enabled during world creation. Feature packs need to be explicitly enabled.", SettingKind.Text, null, "World", RequiresRestart: true),
        new("initial-disabled-packs", "Disabled data packs", "Comma-separated list of datapacks to not be auto-enabled on world creation.", SettingKind.Text, null, "World", RequiresRestart: true),

        // Players
        new("max-players", "Max players", "The maximum number of players that can play on the server at the same time.", SettingKind.Number, null, "Players", Minimum: 1),
        new("white-list", "Whitelist", "Enables a whitelist on the server. If enabled, the server will only allow selected users to connect.", SettingKind.Toggle, null, "Players"),
        new("enforce-whitelist", "Enforce whitelist", "If set to true, the server will kick players who are not on the whitelist.", SettingKind.Toggle, null, "Players"),
        new("online-mode", "Verify accounts with Mojang", "If set to true, the server checks all connecting players against Minecraft's account database. This requires all connected players to have a valid Minecraft account and makes it impossible for cracked players to connect.", SettingKind.Toggle, null, "Players", RequiresRestart: true),
        new("enforce-secure-profile", "Require signed profiles", "If set to true, players without a Mojang-signed public key will not be able to connect to the server.", SettingKind.Toggle, null, "Players", RequiresRestart: true),
        new("prevent-proxy-connections", "Block proxy connections", "If the ISP/AS sent from the server is different from the one from Mojang Studios' authentication server, the player is not allowed to join the server.", SettingKind.Toggle, null, "Players"),
        new("player-idle-timeout", "Idle kick", "If non-zero, players are kicked from the server if they are idle for more than that many minutes.", SettingKind.Number, null, "Players", Minimum: 0),
        new("spawn-protection", "Spawn protection", "Used to determine the side length of the spawn protection. The formula 2x+1 is used, so a value of 1 results in a side length of 3 blocks. Setting this to 0 disables spawn protection. There must be at least 1 operator for it to be enabled.", SettingKind.Number, null, "Players", Minimum: 0),
        new("op-permission-level", "Operator permission level", "Sets the default permission level for ops when using /op.", SettingKind.Choice, null, "Players", Choices: ["0", "1", "2", "3", "4"]),
        new("function-permission-level", "Function permission level", "Sets the default permission level for functions.", SettingKind.Choice, null, "Players", Choices: ["1", "2", "3", "4"]),

        // Gameplay
        new("gamemode", "Default game mode", "Defines the mode of gameplay.", SettingKind.Choice, null, "Gameplay", Choices: ["survival", "creative", "adventure", "spectator"]),
        new("force-gamemode", "Force game mode", "Force players to join in the default game mode. This will reset their previous game mode when they reconnect.", SettingKind.Toggle, null, "Gameplay"),
        new("difficulty", "Difficulty", "Defines the difficulty of the server.", SettingKind.Choice, null, "Gameplay", Choices: ["peaceful", "easy", "normal", "hard"]),
        new("hardcore", "Hardcore", "If set to true, players will be set to spectator mode if they die.", SettingKind.Toggle, null, "Gameplay", RequiresRestart: true),
        new("allow-flight", "Allow flight", "Means that users will not be kicked if they fly whilst in Survival mode. This is likely to occur through hacking, however there can be false positives.", SettingKind.Toggle, null, "Gameplay"),

        // Moderation
        new("chat-spam-threshold-seconds", "Chat spam threshold", "Defines how many messages per second a player must send to be kicked for spamming. To disable this, set to 0.", SettingKind.Number, null, "Moderation", Minimum: 0),
        new("command-spam-threshold-seconds", "Command spam threshold", "Defines how many commands per second a player must send to be kicked for spamming. To disable this, set to 0.", SettingKind.Number, null, "Moderation", Minimum: 0),
        new("text-filtering-config", "Text filtering config", "The path to the text filtering configuration file. Leave blank to disable text filtering.", SettingKind.Text, null, "Moderation", RequiresRestart: true),
        new("text-filtering-version", "Text filtering version", "The version of the configuration format used for text-filtering-config.", SettingKind.Choice, null, "Moderation", Choices: ["0", "1"], RequiresRestart: true),
        new("enable-code-of-conduct", "Code of conduct", "Whether the server will look for code of conduct files in the codeofconduct subfolder in the server folder. Each file should be named <language_code>.txt; the server shows the one matching the player's language, falling back to en_us.", SettingKind.Toggle, null, "Moderation"),
        new("log-ips", "Log player IPs", "Whether player IP addresses should be logged by the server. This does not impact the ability of plugins to log the IP addresses of players.", SettingKind.Toggle, null, "Moderation"),
        new("broadcast-console-to-ops", "Broadcast console to ops", "Send console command output to all online operators.", SettingKind.Toggle, null, "Moderation"),

        // Network
        new("server-ip", "Bind address", "The IP address to bind to. Leave blank to bind to all interfaces.", SettingKind.Text, null, "Network", RequiresRestart: true),
        new("server-port", "Port", "The port to listen on for connections.", SettingKind.Number, null, "Network", Minimum: 1, Maximum: 65535, RequiresRestart: true),
        new("motd", "MOTD", "The message of the day, displayed in the server list.", SettingKind.Text, null, "Network"),
        new("enable-status", "Answer status pings", "Makes the server appear on the server list and also enables the listener for getting server information. If turned off, the server will appear offline but players will still be able to connect.", SettingKind.Toggle, null, "Network"),
        new("hide-online-players", "Hide online players", "Hides the player list sent with the status request packets.", SettingKind.Toggle, null, "Network"),
        new("bug-report-link", "Bug report link", "A URL value used for the Report Server Bugs button in the Server Links client menu.", SettingKind.Text, null, "Network"),
        new("accept-transfers", "Accept transfers", "Whether this server accepts transfers from other servers using the transfer command/packet. If this is set to false, the server will disconnect the client.", SettingKind.Toggle, null, "Network"),
        new("network-compression-threshold", "Compression threshold", "The number of bytes of a packet before it is compressed. Setting to a negative value disables compression.", SettingKind.Number, null, "Network", Minimum: -1, RequiresRestart: true),
        new("rate-limit", "Packet rate limit", "Sets the maximum allowed number of packets that can be sent before getting kicked. Setting this to 0 disables the limit.", SettingKind.Number, null, "Network", Minimum: 0),

        // Performance
        new("view-distance", "View distance", "Sets the amount of world data the server sends the client, measured in chunks in each direction of the player (radius, not diameter). It determines the server-side viewing distance.", SettingKind.Number, null, "Performance", Minimum: 3, Maximum: 32),
        new("simulation-distance", "Simulation distance", "Sets the maximum distance from players that living entities may be located in order to be updated by the server, measured in chunks in each direction of the player (radius, not diameter). Entities outside this radius are not ticked and are not visible to players.", SettingKind.Number, null, "Performance", Minimum: 3, Maximum: 32),
        new("entity-broadcast-range-percentage", "Entity broadcast range", "Controls how close entities need to be before being sent to clients, expressed as a percentage of the default value. Higher values mean they are rendered from farther away, potentially causing more lag.", SettingKind.Number, null, "Performance", Minimum: 10, Maximum: 1000),
        new("max-tick-time", "Watchdog tick timeout", "The maximum number of milliseconds a single tick may take before the server watchdog considers the server crashed and forcibly shuts it down. Setting this to -1 disables the watchdog entirely.", SettingKind.Number, null, "Performance", Minimum: -1, RequiresRestart: true),
        new("max-chained-neighbor-updates", "Max chained neighbor updates", "Limits the number of consecutive neighbor updates before skipping subsequent updates. Negative values will disable the limit.", SettingKind.Number, null, "Performance"),
        new("pause-when-empty-seconds", "Pause when empty", "How many seconds have to pass after no player has been online before the server is paused. This is disabled by default because it is incompatible with what plugins expect and might do with no players online.", SettingKind.Number, null, "Performance", Minimum: -1),
        new("sync-chunk-writes", "Synchronous chunk writes", "Enables synchronous writing of chunk data. Has no effect on Paper by default, unless the corresponding system property is also set to true.", SettingKind.Toggle, null, "Performance", RequiresRestart: true),
        new("use-native-transport", "Native transport", "Provides a performance boost for Linux servers.", SettingKind.Toggle, null, "Performance", RequiresRestart: true),
        new("region-file-compression", "Region file compression", "Specifies the compression type used to compress region files. If set to none, region files will take up significantly more disk space, but it might make sense together with filesystem-level compression. gzip is only available on Paper.", SettingKind.Choice, null, "Performance", Choices: ["deflate", "lz4", "none", "gzip"], RequiresRestart: true),

        // Resource pack
        new("resource-pack", "Resource pack URL", "The URL to the server's resource pack.", SettingKind.Text, null, "Resource pack"),
        new("resource-pack-id", "Resource pack ID", "The UUID of the server resource pack to use.", SettingKind.Text, null, "Resource pack"),
        new("resource-pack-sha1", "Resource pack SHA-1", "The hash of the resource pack, used for verification. This is recommended to be set to ensure players are downloading the correct pack.", SettingKind.Text, null, "Resource pack"),
        new("resource-pack-prompt", "Resource pack prompt", "The message that is displayed when the client is prompted to download the resource pack.", SettingKind.Text, null, "Resource pack"),
        new("require-resource-pack", "Require resource pack", "If true, a player must have the given resource pack to connect. They will be kicked if they do not have it.", SettingKind.Toggle, null, "Resource pack"),

        // RCON & Query
        new("enable-rcon", "Enable RCON", "Enables remote access to the server console.", SettingKind.Toggle, null, "RCON & Query", RequiresRestart: true),
        new("rcon.password", "RCON password", "The password for the rcon server.", SettingKind.Text, null, "RCON & Query", RequiresRestart: true),
        new("rcon.port", "RCON port", "The port to start the rcon server on.", SettingKind.Number, null, "RCON & Query", Minimum: 1, Maximum: 65535, RequiresRestart: true),
        new("broadcast-rcon-to-ops", "Broadcast RCON to ops", "Send rcon command output to all online operators.", SettingKind.Toggle, null, "RCON & Query"),
        new("enable-query", "Enable query", "Enables the GameSpy4 protocol server listener. Used to get information about the server.", SettingKind.Toggle, null, "RCON & Query", RequiresRestart: true),
        new("query.port", "Query port", "The port for the query server. This is used to get information about the server.", SettingKind.Number, null, "RCON & Query", Minimum: 1, Maximum: 65535, RequiresRestart: true),

        // Management server
        new("management-server-enabled", "Enable management server", "Whether the Minecraft Server Management Protocol is enabled.", SettingKind.Toggle, null, "Management server", RequiresRestart: true),
        new("management-server-host", "Management host", "Controls the host that the Minecraft Server Management Protocol is started on.", SettingKind.Text, null, "Management server", RequiresRestart: true),
        new("management-server-port", "Management port", "Controls the port that the Minecraft Server Management Protocol is started on.", SettingKind.Number, null, "Management server", Minimum: 0, Maximum: 65535, RequiresRestart: true),
        new("management-server-secret", "Management secret", "Allows clients to supply an Authorization header with a server specific secret, which is forty alphanumeric characters long (A-Z, a-z and 0-9). The secret is automatically generated if the property is left empty.", SettingKind.Text, null, "Management server", RequiresRestart: true),
        new("management-server-tls-enabled", "Management TLS", "Controls whether the Minecraft Server Management Protocol uses TLS (Transport Layer Security).", SettingKind.Toggle, null, "Management server", RequiresRestart: true),
        new("management-server-tls-keystore", "TLS keystore path", "Controls the path to the keystore file used for TLS. A server will not start when TLS is enabled and no keystore is provided.", SettingKind.Text, null, "Management server", RequiresRestart: true),
        new("management-server-tls-keystore-password", "TLS keystore password", "Controls the password to the keystore file used for TLS. The password can also be supplied through an environment variable (MINECRAFT_MANAGEMENT_TLS_KEYSTORE_PASSWORD) or a JVM argument (-Dmanagement.tls.keystore.password=).", SettingKind.Text, null, "Management server", RequiresRestart: true),
        new("status-heartbeat-interval", "Status heartbeat interval", "Controls the intervals in which the management server sends heartbeat notifications to connected clients. It is disabled by default.", SettingKind.Number, null, "Management server", Minimum: 0),

        // Diagnostics
        new("debug", "Debug mode", "Enables the server's debug mode.", SettingKind.Toggle, null, "Diagnostics", RequiresRestart: true),
        new("enable-jmx-monitoring", "JMX monitoring", "Exposes an MBean with the object name net.minecraft.server:type=Server and two attributes, averageTickTime and tickTimes, exposing the tick times in milliseconds. Enabling JMX on the Java runtime also requires a couple of extra JVM flags on startup.", SettingKind.Toggle, null, "Diagnostics", RequiresRestart: true)
    ];

    public bool IsConnected { get; private set; }

    public async Task<IReadOnlyList<ServerSetting>> GetSettingsAsync(CancellationToken ct = default)
    {
        IsConnected = File.Exists(PROPERTIES_FILE);

        if (!IsConnected)
            return [];

        var file = await File.ReadAllLinesAsync(PROPERTIES_FILE, ct);

        foreach (var line in file)
        {
            if (!TryReadLine(line, out var key, out var value)) continue;

            var index = Array.FindIndex(ServerSettings,
                s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;

            ServerSettings[index] = ServerSettings[index] with { Value = value };
        }

        return ServerSettings;
    }

    /// <summary>
    /// Splits one server.properties line into key and value, or returns false for the
    /// blank and '#' comment lines the file opens with. Only the first '=' separates,
    /// so values that contain one themselves — resource pack URLs, generator-settings
    /// JSON, motd — survive intact.
    /// </summary>
    private static bool TryReadLine(string line, out string key, out string value)
    {
        key = value = string.Empty;

        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is '#' or '!')
            return false;

        var parts = trimmed.Split('=', 2);
        if (parts.Length != 2)
            return false;

        key = parts[0].TrimEnd();
        value = parts[1];

        return key.Length > 0;
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, string> changes, CancellationToken ct = default)
    {
        if(!IsConnected)
            throw new NotSupportedException(
                "server.properties är inte kopplad än och justeringar sparas inte.");

        foreach (var (setting, value) in changes)
        {
            await ModifySettingsFile(setting.Trim(), value.Trim(), ct);
        }
    }

    public async Task ModifySettingsFile(string key, string value, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(PROPERTIES_FILE, ct);

        for (var i = 0; i < lines.Length; i++)
        {
            if (!TryReadLine(lines[i], out var lineKey, out _)) continue;
            if (!lineKey.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

            lines[i] = $"{key}={value}";
        }

        await File.WriteAllLinesAsync(PROPERTIES_FILE, lines, ct);
    }
}
