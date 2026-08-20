using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace McServerMgmnt.Services.Rcon;

/// <summary>Thrown when the server rejects the RCON password.</summary>
public class RconAuthenticationException(string message) : Exception(message);

/// <summary>
/// Source RCON client, which is the protocol Minecraft speaks on rcon.port.
///
/// One packet is: int32 length, int32 request id, int32 type, the body as a
/// null-terminated string, and one more null byte. Everything is little-endian and the
/// length counts everything after itself. The server echoes the request id back, which is
/// how a failed login is reported: id -1.
///
/// The connection is kept open between commands and re-established on demand, because
/// Minecraft drops every RCON connection when it shuts down.
/// </summary>
public sealed class RconClient(string host, int port, string password, TimeSpan timeout) : IDisposable
{
    private const int TypeCommand = 2;
    private const int TypeAuth = 3;

    /// <summary>The verdict packet after a login. Shares its number with TypeCommand; the protocol is like that.</summary>
    private const int TypeAuthResponse = 2;

    /// <summary>Vanilla splits anything longer than this across several response packets.</summary>
    private const int MaxBodyLength = 4096;

    private readonly SemaphoreSlim gate = new(1, 1);
    private TcpClient? client;
    private NetworkStream? stream;
    private int nextId;

    /// <summary>
    /// Runs one command and returns whatever the server printed.
    /// </summary>
    /// <param name="tolerateDisconnect">
    /// Set for commands that end the server — "stop" kills the connection before it can
    /// answer, and that is a success, not a failure.
    /// </param>
    public async Task<string> ExecuteAsync(string command, bool tolerateDisconnect = false,
        CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            try
            {
                return await SendAsync(command, tolerateDisconnect, ct);
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException)
            {
                // A socket left over from a previous run of the server looks alive until we
                // write to it. Drop it and try once more against a fresh connection.
                Close();
                return await SendAsync(command, tolerateDisconnect, ct);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>True when the server accepts a connection and the password. Used to tell "running" from "still starting".</summary>
    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            await ExecuteAsync("list", ct: ct);
            return true;
        }
        catch (RconAuthenticationException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> SendAsync(string command, bool tolerateDisconnect, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            await EnsureConnectedAsync(deadline.Token);

            var id = NextId();
            await WritePacketAsync(id, TypeCommand, command, deadline.Token);
            return await ReadResponseAsync(id, deadline.Token);
        }
        catch (Exception ex) when (tolerateDisconnect && ex is IOException or SocketException or EndOfStreamException)
        {
            // The server shut down mid-answer, which is exactly what we asked it to do.
            Close();
            return string.Empty;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Close();
            throw new TimeoutException($"The server did not answer RCON within {timeout.TotalSeconds:0} seconds.");
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (client is { Connected: true } && stream is not null)
        {
            return;
        }

        Close();

        var fresh = new TcpClient { NoDelay = true };
        await fresh.ConnectAsync(host, port, ct);

        client = fresh;
        stream = fresh.GetStream();

        var id = NextId();
        await WritePacketAsync(id, TypeAuth, password, ct);

        // Some servers answer with an empty response packet before the auth verdict, so
        // read until the verdict actually shows up.
        while (true)
        {
            var packet = await ReadPacketAsync(ct);

            if (packet.Type != TypeAuthResponse)
            {
                continue;
            }

            if (packet.Id == -1)
            {
                Close();
                throw new RconAuthenticationException(
                    "The server rejected the RCON password. Check McServer:RconPassword against rcon.password in server.properties.");
            }

            return;
        }
    }

    private async Task<string> ReadResponseAsync(int id, CancellationToken ct)
    {
        var body = new StringBuilder();

        while (true)
        {
            var packet = await ReadPacketAsync(ct);

            if (packet.Id != id)
            {
                // Left over from an earlier command that timed out. Skip it.
                continue;
            }

            body.Append(packet.Body);

            // A body that fills a packet exactly means the rest is already on its way.
            var more = packet.Body.Length >= MaxBodyLength || (stream?.DataAvailable ?? false);
            if (!more)
            {
                return body.ToString();
            }
        }
    }

    private async Task WritePacketAsync(int id, int type, string body, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var packet = new byte[12 + payload.Length + 2];

        BinaryPrimitives.WriteInt32LittleEndian(packet, 10 + payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), type);
        payload.CopyTo(packet.AsSpan(12));
        // The last two bytes terminate the body and the (always empty) trailing string.

        await stream!.WriteAsync(packet, ct);
        await stream.FlushAsync(ct);
    }

    private async Task<RconPacket> ReadPacketAsync(CancellationToken ct)
    {
        var header = new byte[4];
        await stream!.ReadExactlyAsync(header, ct);

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 10 or > MaxBodyLength + 16)
        {
            throw new InvalidDataException($"RCON sent a packet of {length} bytes, which is not a length this protocol uses.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, ct);

        return new RconPacket(
            BinaryPrimitives.ReadInt32LittleEndian(payload),
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4)),
            Encoding.UTF8.GetString(payload, 8, length - 10));
    }

    private int NextId() => Interlocked.Increment(ref nextId);

    private void Close()
    {
        stream?.Dispose();
        client?.Dispose();
        stream = null;
        client = null;
    }

    public void Dispose()
    {
        Close();
        gate.Dispose();
    }

    private readonly record struct RconPacket(int Id, int Type, string Body);
}
