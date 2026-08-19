using System.Net.Sockets;
using System.Text.Json;

namespace McServerMgmnt.Services.Rcon;

/// <summary>What Docker knows about the container the Minecraft server runs in.</summary>
public record ContainerState(
    bool Exists,
    bool Running,
    string Status,
    int ExitCode,
    DateTimeOffset? StartedAt)
{
    public static readonly ContainerState Missing = new(false, false, "missing", 0, null);
}

/// <summary>
/// The little slice of the Docker Engine API this app needs, spoken over the mounted
/// unix socket. RCON can stop a server but never start one, so start and restart come
/// from here instead.
/// </summary>
public sealed class DockerContainers(string socketPath) : IDisposable
{
    private readonly HttpClient http = CreateClient(socketPath);

    private static HttpClient CreateClient(string socketPath)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };

        // The host name is ignored — every request goes down the socket — but HttpClient wants one.
        return new HttpClient(handler) { BaseAddress = new Uri("http://docker") };
    }

    public async Task<ContainerState> InspectAsync(string name, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"/containers/{Uri.EscapeDataString(name)}/json", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ContainerState.Missing;
        }

        await ThrowIfFailedAsync(response, $"inspect container '{name}'", ct);

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);

        if (!json.RootElement.TryGetProperty("State", out var state))
        {
            return ContainerState.Missing;
        }

        var startedAt = state.TryGetProperty("StartedAt", out var started)
                        && DateTimeOffset.TryParse(started.GetString(), out var parsed)
                        && parsed.Year > 1
            ? parsed
            : (DateTimeOffset?)null;

        return new ContainerState(
            Exists: true,
            Running: state.TryGetProperty("Running", out var running) && running.GetBoolean(),
            Status: state.TryGetProperty("Status", out var status) ? status.GetString() ?? "unknown" : "unknown",
            ExitCode: state.TryGetProperty("ExitCode", out var exit) ? exit.GetInt32() : 0,
            StartedAt: startedAt);
    }

    public async Task StartAsync(string name, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"/containers/{Uri.EscapeDataString(name)}/start", null, ct);

        // 304 means it was already running, which is the state the caller wanted anyway.
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            return;
        }

        await ThrowIfFailedAsync(response, $"start container '{name}'", ct);
    }

    /// <summary>Fallback for when RCON cannot be reached: ask Docker to signal the server instead.</summary>
    public async Task StopAsync(string name, int timeoutSeconds, CancellationToken ct = default)
    {
        using var response = await http.PostAsync(
            $"/containers/{Uri.EscapeDataString(name)}/stop?t={timeoutSeconds}", null, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            return;
        }

        await ThrowIfFailedAsync(response, $"stop container '{name}'", ct);
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(
            $"Docker refused to {what}: {(int)response.StatusCode} {response.ReasonPhrase}. {detail}".Trim());
    }

    public void Dispose() => http.Dispose();
}
