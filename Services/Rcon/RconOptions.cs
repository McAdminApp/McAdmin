namespace McServerMgmnt.Services.Rcon;

/// <summary>
/// Everything the RCON-backed controller needs, bound from the "McServer" configuration
/// section. Nothing here has a useful default for a real deployment except the ports, so
/// <see cref="IsConfigured"/> decides whether the RCON controller is wired up at all.
/// </summary>
public class RconOptions
{
    public const string SectionName = "McServer";

    /// <summary>Host the server's RCON listener is on. Under compose this is the service name.</summary>
    public string Host { get; set; } = "minecraft";

    /// <summary>Matches rcon.port in server.properties.</summary>
    public int RconPort { get; set; } = 25575;

    /// <summary>Matches rcon.password in server.properties. Empty leaves the placeholder controller in place.</summary>
    public string RconPassword { get; set; } = string.Empty;

    /// <summary>Shown on the console page. Falls back to Host plus server-port from server.properties.</summary>
    public string? Address { get; set; }

    /// <summary>
    /// The container running the Minecraft server. RCON can stop a server but never start
    /// one — once it is down there is no listener left — so start and restart go through
    /// the Docker socket instead.
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    public string DockerSocket { get; set; } = "/var/run/docker.sock";

    /// <summary>The server's latest.log, mounted read-only. Empty leaves the log panel empty.</summary>
    public string LogPath { get; set; } = string.Empty;

    /// <summary>How long to wait for the server to finish shutting down after "stop".</summary>
    public int StopTimeoutSeconds { get; set; } = 90;

    /// <summary>Per-command RCON timeout, connect included.</summary>
    public int CommandTimeoutSeconds { get; set; } = 10;

    /// <summary>How many log lines the console panel reads at a time.</summary>
    public int LogTailLines { get; set; } = 200;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(RconPassword);

    /// <summary>True when start and restart are possible: a container name plus a reachable socket.</summary>
    public bool CanControlContainer =>
        !string.IsNullOrWhiteSpace(ContainerName) && File.Exists(DockerSocket);
}
