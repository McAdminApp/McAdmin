using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace McServerMgmnt.Services.Rcon;

/// <summary>
/// Drives a real Minecraft server. Commands and shutdown go over RCON; start and restart
/// go through the Docker socket, because RCON dies with the server it lives in and cannot
/// bring one back. Log lines come from the server's own latest.log.
/// </summary>
public sealed partial class RconServerController : IMinecraftServerController, IDisposable
{
    /// <summary>Both shapes of the "list" reply: "There are 2 of a max of 20" and the older "2/20".</summary>
    [GeneratedRegex(@"(?<online>\d+)\s*(?:of a max of|/)\s*(?<max>\d+)\s*players? online:?\s*(?<names>.*)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ListReply { get; }

    /// <summary>Minecraft's colour codes, which have no business in a HTML table.</summary>
    [GeneratedRegex("§[0-9a-fk-orA-FK-OR]")]
    private static partial Regex ColourCodes { get; }

    /// <summary>
    /// The page asks for the snapshot and the player list back to back; one "list" covers
    /// both. Kept under the page's one second refresh so every tick still sees fresh counts.
    /// </summary>
    private static readonly TimeSpan ListCacheWindow = TimeSpan.FromMilliseconds(750);

    private readonly RconOptions options;
    private readonly IServerSettingsStore settings;
    private readonly ServerLogReader logReader;
    private readonly ILogger<RconServerController> logger;

    private readonly RconClient rcon;
    private readonly DockerContainers? docker;

    private readonly SemaphoreSlim listGate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> firstSeen = new(StringComparer.OrdinalIgnoreCase);

    private PlayerListing? cachedListing;
    private DateTimeOffset cachedAt;
    private string? cachedVersion;
    private DateTimeOffset? versionReadFor;
    private DateTimeOffset? runningSince;
    private volatile bool stopping;

    public RconServerController(
        IOptions<RconOptions> options,
        IServerSettingsStore settings,
        ServerLogReader logReader,
        ILogger<RconServerController> logger)
    {
        this.options = options.Value;
        this.settings = settings;
        this.logReader = logReader;
        this.logger = logger;

        rcon = new RconClient(
            this.options.Host,
            this.options.RconPort,
            this.options.RconPassword,
            TimeSpan.FromSeconds(this.options.CommandTimeoutSeconds));

        if (this.options.CanControlContainer)
        {
            docker = new DockerContainers(this.options.DockerSocket);
        }
        else if (!string.IsNullOrWhiteSpace(this.options.ContainerName))
        {
            logger.LogWarning(
                "McServer:ContainerName is set but the Docker socket at {Socket} is not there, so start and restart are unavailable.",
                this.options.DockerSocket);
        }
    }

    /// <summary>
    /// Whether this backend is wired up at all — not whether the server happens to be
    /// running. A stopped server is a state the page draws, not a missing backend.
    /// </summary>
    public bool IsConnected => options.IsConfigured;

    public async Task<ServerSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var container = await InspectAsync(ct);
        var listing = await GetListingAsync(ct);
        var properties = await ReadPropertiesAsync(ct);

        var state = DetermineState(container, listing is not null);
        var port = properties.Port ?? 25565;

        return new ServerSnapshot(
            Name: properties.Motd is { Length: > 0 } motd ? Clean(motd) : "Minecraft server",
            State: state,
            Version: VersionFor(container) ?? "Unknown",
            PlayersOnline: listing?.Online ?? 0,
            PlayerSlots: listing?.Slots ?? properties.MaxPlayers ?? 0,
            Uptime: UptimeFor(container, state),
            Address: options.Address is { Length: > 0 } address ? address : $"{options.Host}:{port}",
            StatusDetail: DetailFor(state, container));
    }

    public async Task<IReadOnlyList<OnlinePlayer>> GetPlayersAsync(CancellationToken ct = default)
    {
        var listing = await GetListingAsync(ct);

        if (listing is null || listing.Names.Count == 0)
        {
            lock (firstSeen)
            {
                firstSeen.Clear();
            }

            return [];
        }

        var now = DateTimeOffset.UtcNow;

        lock (firstSeen)
        {
            foreach (var gone in firstSeen.Keys.Except(listing.Names, StringComparer.OrdinalIgnoreCase).ToList())
            {
                firstSeen.Remove(gone);
            }

            foreach (var name in listing.Names)
            {
                firstSeen.TryAdd(name, now);
            }

            return
            [
                .. listing.Names.Select(name => new OnlinePlayer(
                    Name: name,
                    // Only as far back as this app first saw them: RCON reports who is online, never since when.
                    SessionLength: now - firstSeen.GetValueOrDefault(name, now),
                    // RCON has no way to ask for latency.
                    Ping: null))
            ];
        }
    }

    public Task<IReadOnlyList<ConsoleLine>> GetRecentLogAsync(int lines = 50, CancellationToken ct = default) =>
        Task.FromResult(logReader.ReadTail(options.LogPath, Math.Max(lines, options.LogTailLines)));

    public async Task StartAsync(CancellationToken ct = default)
    {
        var containers = RequireContainerControl("start");
        await containers.StartAsync(options.ContainerName, ct);

        logger.LogInformation("Started container {Container}.", options.ContainerName);
        Invalidate();
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        stopping = true;

        try
        {
            var stoppedOverRcon = await TryRconStopAsync(ct);

            if (docker is not null)
            {
                // RCON alone is not enough. A container that exits on its own is brought
                // straight back by restart: always and restart: unless-stopped, so a clean
                // shutdown would look like it worked and then undo itself. Telling Docker to
                // stop it marks it as deliberately stopped, which the policy honours. It is a
                // no-op when the container has already exited.
                await docker.StopAsync(options.ContainerName, options.StopTimeoutSeconds, ct);
            }
            else if (!stoppedOverRcon)
            {
                RequireContainerControl("stop");
            }

            await WaitForExitAsync(ct);
        }
        finally
        {
            stopping = false;
            Invalidate();
        }
    }

    public async Task RestartAsync(CancellationToken ct = default)
    {
        // Checked up front so a restart never stops a server it has no way to start again.
        RequireContainerControl("restart");

        await StopAsync(ct);
        await StartAsync(ct);
    }

    public async Task<string> SendCommandAsync(string command, CancellationToken ct = default)
    {
        var trimmed = (command ?? string.Empty).Trim().TrimStart('/');

        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Type a command to run.", nameof(command));
        }

        // "stop" takes the connection down with the server, which is a success here.
        var ending = trimmed.Equals("stop", StringComparison.OrdinalIgnoreCase);

        var response = await rcon.ExecuteAsync(trimmed, tolerateDisconnect: ending, ct: ct);
        Invalidate();

        return Clean(response);
    }

    private async Task<bool> TryRconStopAsync(CancellationToken ct)
    {
        try
        {
            await rcon.ExecuteAsync("stop", tolerateDisconnect: true, ct: ct);
            return true;
        }
        catch (RconAuthenticationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Nothing listening: either it is already down, or it never came up. Docker decides.
            logger.LogInformation(ex, "RCON would not take the stop command; falling back to Docker.");
            return false;
        }
    }

    private async Task WaitForExitAsync(CancellationToken ct)
    {
        if (docker is null)
        {
            return;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.StopTimeoutSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await InspectAsync(ct);

            if (state is null || !state.Running)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        throw new TimeoutException(
            $"The server was asked to stop but container '{options.ContainerName}' was still running after {options.StopTimeoutSeconds} seconds.");
    }

    private DockerContainers RequireContainerControl(string verb)
    {
        if (docker is not null)
        {
            return docker;
        }

        throw new NotSupportedException(
            $"RCON cannot {verb} a server — once it is down there is no listener left. Mount {options.DockerSocket} " +
            "into this container and set McServer:ContainerName to the Minecraft container.");
    }

    private ServerState DetermineState(ContainerState? container, bool rconAnswering)
    {
        if (container is null)
        {
            return rconAnswering ? ServerState.Running : ServerState.Stopped;
        }

        if (!container.Exists)
        {
            return ServerState.Faulted;
        }

        if (!container.Running)
        {
            return IsCleanExit(container.ExitCode) ? ServerState.Stopped : ServerState.Faulted;
        }

        if (stopping)
        {
            return ServerState.Stopping;
        }

        // The container is up well before the server finishes loading and opens RCON.
        return rconAnswering ? ServerState.Running : ServerState.Starting;
    }

    /// <summary>
    /// Whether an exit code means "someone asked it to stop" rather than "it fell over".
    /// A server told to stop through Docker exits on a signal — 143 for SIGTERM, 137 when
    /// the grace period ran out and it was killed — and neither is a fault to report.
    /// </summary>
    private static bool IsCleanExit(int exitCode) => exitCode is 0 or 130 or 137 or 143;

    private string? DetailFor(ServerState state, ContainerState? container) => state switch
    {
        ServerState.Running => null,
        ServerState.Starting => "The container is up but RCON is not answering yet — the server is still loading.",
        ServerState.Stopping => "Waiting for the server to finish shutting down.",
        ServerState.Faulted when container is { Exists: false } =>
            $"There is no container called '{options.ContainerName}' on this host.",
        ServerState.Faulted => $"The server exited with code {container?.ExitCode}.",
        _ when docker is null =>
            "RCON is not answering. Start and restart need the Docker socket, which is not mounted.",
        _ => "The server is stopped."
    };

    private TimeSpan UptimeFor(ContainerState? container, ServerState state)
    {
        if (state is not (ServerState.Running or ServerState.Starting))
        {
            runningSince = null;
            return TimeSpan.Zero;
        }

        // Docker knows exactly when the container came up; without it, count from first sight.
        var since = container?.StartedAt ?? (runningSince ??= DateTimeOffset.UtcNow);
        return DateTimeOffset.UtcNow - since;
    }

    private string? VersionFor(ContainerState? container)
    {
        var startedAt = container?.StartedAt;

        // The banner only changes when the server restarts, so re-read the log head then.
        if (cachedVersion is not null && versionReadFor == startedAt)
        {
            return cachedVersion;
        }

        cachedVersion = logReader.ReadVersion(options.LogPath);
        versionReadFor = startedAt;

        return cachedVersion;
    }

    private async Task<ContainerState?> InspectAsync(CancellationToken ct)
    {
        if (docker is null)
        {
            return null;
        }

        try
        {
            return await docker.InspectAsync(options.ContainerName, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not ask Docker about container {Container}.", options.ContainerName);
            return null;
        }
    }

    private async Task<PlayerListing?> GetListingAsync(CancellationToken ct)
    {
        await listGate.WaitAsync(ct);

        try
        {
            if (cachedListing is not null && DateTimeOffset.UtcNow - cachedAt < ListCacheWindow)
            {
                return cachedListing;
            }

            string reply;

            try
            {
                reply = await rcon.ExecuteAsync("list", ct: ct);
            }
            catch (RconAuthenticationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "RCON is not answering on {Host}:{Port}.", options.Host, options.RconPort);
                cachedListing = null;
                cachedAt = DateTimeOffset.UtcNow;
                return null;
            }

            cachedListing = ParseListing(reply);
            cachedAt = DateTimeOffset.UtcNow;

            return cachedListing;
        }
        finally
        {
            listGate.Release();
        }
    }

    private static PlayerListing? ParseListing(string reply)
    {
        var match = ListReply.Match(Clean(reply));

        if (!match.Success)
        {
            return null;
        }

        var names = match.Groups["names"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new PlayerListing(
            int.Parse(match.Groups["online"].Value),
            int.Parse(match.Groups["max"].Value),
            names);
    }

    /// <summary>Reads what server.properties already tells us, so nothing is configured twice.</summary>
    private async Task<(string? Motd, int? MaxPlayers, int? Port)> ReadPropertiesAsync(CancellationToken ct)
    {
        try
        {
            var all = await settings.GetSettingsAsync(ct);

            string? Value(string key) => all.FirstOrDefault(s => s.Key == key)?.Value;

            return (
                Value("motd"),
                int.TryParse(Value("max-players"), out var max) ? max : null,
                int.TryParse(Value("server-port"), out var port) ? port : null);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read server.properties for the console page.");
            return (null, null, null);
        }
    }

    private void Invalidate() => cachedListing = null;

    private static string Clean(string text) => ColourCodes.Replace(text, string.Empty).Trim();

    public void Dispose()
    {
        rcon.Dispose();
        docker?.Dispose();
        listGate.Dispose();
    }

    private sealed record PlayerListing(int Online, int Slots, IReadOnlyList<string> Names);
}
