namespace McServerMgmnt.Services;

public enum ServerState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

/// <summary>Everything the console page needs to draw one moment in the server's life.</summary>
public record ServerSnapshot(
    string Name,
    ServerState State,
    string Version,
    int PlayersOnline,
    int PlayerSlots,
    TimeSpan Uptime,
    string Address,
    string? StatusDetail = null);

/// <summary>
/// One player currently online. Ping is nullable because it is not always knowable —
/// RCON, for one, has no way to ask for latency.
/// </summary>
public record OnlinePlayer(string Name, TimeSpan SessionLength, int? Ping);

public record ConsoleLine(DateTimeOffset Timestamp, string Level, string Text);

/// <summary>
/// The seam for real server management. Implement this over gRPC (or RCON, or a
/// process wrapper) and register it in Program.cs in place of
/// <see cref="PlaceholderServerController"/>; the console page needs no changes.
/// </summary>
public interface IMinecraftServerController
{
    /// <summary>True once a real backend is wired up. The console shows a banner while this is false.</summary>
    bool IsConnected { get; }

    Task<ServerSnapshot> GetSnapshotAsync(CancellationToken ct = default);

    Task<IReadOnlyList<OnlinePlayer>> GetPlayersAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ConsoleLine>> GetRecentLogAsync(int lines = 50, CancellationToken ct = default);

    Task StartAsync(CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

    Task RestartAsync(CancellationToken ct = default);

    /// <summary>Runs a console command, e.g. "say hello". Returns whatever the server printed.</summary>
    Task<string> SendCommandAsync(string command, CancellationToken ct = default);
}

/// <summary>
/// Stand-in used until the real controller exists. It returns fixed sample data so
/// the console page has something to lay out, and refuses every action.
/// </summary>
public class PlaceholderServerController : IMinecraftServerController
{
    private static readonly ConsoleLine[] SampleLog =
    [
        new(DateTimeOffset.Now.AddMinutes(-4), "info", "Starting minecraft server version 1.21.4"),
        new(DateTimeOffset.Now.AddMinutes(-4), "info", "Loading properties"),
        new(DateTimeOffset.Now.AddMinutes(-3), "info", "Preparing level \"world\""),
        new(DateTimeOffset.Now.AddMinutes(-3), "warn", "Can't keep up! Is the server overloaded?"),
        new(DateTimeOffset.Now.AddMinutes(-2), "info", "Done (18.402s)! For help, type \"help\""),
        new(DateTimeOffset.Now.AddMinutes(-1), "join", "Steve joined the game")
    ];

    public bool IsConnected => false;

    public Task<ServerSnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
        Task.FromResult(new ServerSnapshot(
            Name: "Sample world",
            State: ServerState.Stopped,
            Version: "1.21.4",
            PlayersOnline: 0,
            PlayerSlots: 20,
            Uptime: TimeSpan.Zero,
            Address: "localhost:25565",
            StatusDetail: "No server backend is wired up yet."));

    public Task<IReadOnlyList<OnlinePlayer>> GetPlayersAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OnlinePlayer>>([]);

    public Task<IReadOnlyList<ConsoleLine>> GetRecentLogAsync(int lines = 50, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ConsoleLine>>(SampleLog.TakeLast(lines).ToList());

    public Task StartAsync(CancellationToken ct = default) => throw NotWiredUp();

    public Task StopAsync(CancellationToken ct = default) => throw NotWiredUp();

    public Task RestartAsync(CancellationToken ct = default) => throw NotWiredUp();

    public Task<string> SendCommandAsync(string command, CancellationToken ct = default) => throw NotWiredUp();

    private static NotSupportedException NotWiredUp() =>
        new("No server backend is wired up yet. Implement IMinecraftServerController and register it in Program.cs.");
}
