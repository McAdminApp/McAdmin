using System.Text;
using System.Text.RegularExpressions;

namespace McServerMgmnt.Services.Rcon;

/// <summary>
/// Reads the tail of the server's latest.log. RCON only ever returns the answer to a
/// command, never the server's own output, so the console panel comes from the log file
/// the server writes — mounted read-only.
/// </summary>
public sealed partial class ServerLogReader(ILogger<ServerLogReader> logger)
{
    /// <summary>Enough to cover the requested number of lines many times over without reading a whole day's log.</summary>
    private const int TailBytes = 256 * 1024;

    /// <summary>The version banner is in the first handful of lines, so only the head is scanned.</summary>
    private const int HeadBytes = 64 * 1024;

    /// <summary>
    /// The console page re-reads the log on every refresh, so a log that cannot be read
    /// would otherwise repeat the same stack trace forever. Complain once per distinct reason.
    /// </summary>
    private string? lastFailure;

    /// <summary>
    /// Matches both shapes a Minecraft log line comes in:
    /// "[14:02:11] [Server thread/INFO]: text" and "[14:02:11 INFO]: text", with any number
    /// of extra bracketed tags in between.
    /// </summary>
    [GeneratedRegex(@"^\[(?<time>\d{1,2}:\d{2}:\d{2})(?:\s+(?<inline>[A-Za-z]+))?\]\s*(?<tags>(?:\[[^\]]*\]\s*)*):\s?(?<message>.*)$")]
    private static partial Regex LogLine { get; }

    [GeneratedRegex(@"Starting minecraft server version (?<version>[^\s,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex VanillaVersion { get; }

    [GeneratedRegex(@"\(MC:\s*(?<version>[^)]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ServerSoftwareVersion { get; }

    public IReadOnlyList<ConsoleLine> ReadTail(string path, int lines)
    {
        if (string.IsNullOrWhiteSpace(path) || lines <= 0)
        {
            return [];
        }

        var slice = ReadSlice(path, fromEnd: true);
        if (slice is null)
        {
            return [];
        }

        var (text, startedMidFile) = slice.Value;

        // Only a read that began mid-file cuts a line in half; a whole file starts clean.
        var usable = text.Split('\n')
            .Skip(startedMidFile ? 1 : 0)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToList();
        if (usable.Count > lines)
        {
            usable = usable.Skip(usable.Count - lines).ToList();
        }

        return Parse(usable, LastWriteDate(path));
    }

    /// <summary>Pulls the server version out of the startup banner. Null when the log says nothing about it.</summary>
    public string? ReadVersion(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var slice = ReadSlice(path, fromEnd: false);
        if (slice is null)
        {
            return null;
        }

        var text = slice.Value.Text;

        var vanilla = VanillaVersion.Match(text);
        if (vanilla.Success)
        {
            return vanilla.Groups["version"].Value;
        }

        // Paper, Spigot and friends print their own name first and the Minecraft version in brackets.
        var software = ServerSoftwareVersion.Match(text);
        return software.Success ? software.Groups["version"].Value.Trim() : null;
    }

    /// <summary>Reads one end of the file, and reports whether it had to start mid-file to do it.</summary>
    private (string Text, bool StartedMidFile)? ReadSlice(string path, bool fromEnd)
    {
        try
        {
            // The server holds the log open for writing, so nothing here may claim it exclusively.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var window = fromEnd ? TailBytes : HeadBytes;
            var length = (int)Math.Min(file.Length, window);
            var startedMidFile = fromEnd && file.Length > window;

            if (startedMidFile)
            {
                file.Seek(-window, SeekOrigin.End);
            }

            var buffer = new byte[length];
            file.ReadExactly(buffer);

            lastFailure = null;

            return (Encoding.UTF8.GetString(buffer), startedMidFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            var reason = $"{path}: {ex.Message}";

            if (reason == lastFailure)
            {
                logger.LogDebug(ex, "Still cannot read the server log at {Path}.", path);
            }
            else
            {
                lastFailure = reason;
                logger.LogWarning(
                    "Could not read the server log at {Path}: {Reason}. The console panel stays empty until this is fixed. " +
                    "The app runs as uid 1654, so it needs the Minecraft server's group to read the log directory.",
                    path, ex.Message);
            }

            return null;
        }
    }

    private static DateOnly LastWriteDate(string path)
    {
        try
        {
            return DateOnly.FromDateTime(File.GetLastWriteTime(path));
        }
        catch (IOException)
        {
            return DateOnly.FromDateTime(DateTime.Now);
        }
    }

    /// <summary>
    /// Turns raw lines into console lines. Log timestamps carry no date, so the newest line
    /// is dated by the file itself and the date steps back a day wherever the clock jumps
    /// forward as we walk backwards through the file.
    /// </summary>
    private static List<ConsoleLine> Parse(List<string> lines, DateOnly newestDate)
    {
        var parsed = new List<(TimeSpan? Time, string Level, string Text)>(lines.Count);

        foreach (var line in lines)
        {
            var match = LogLine.Match(line);

            if (!match.Success)
            {
                // Continuation of the line before it — a stack trace, usually.
                parsed.Add((null, parsed.Count > 0 ? parsed[^1].Level : "info", line));
                continue;
            }

            var tags = match.Groups["tags"].Value;
            var inline = match.Groups["inline"].Value;
            var message = match.Groups["message"].Value;

            parsed.Add((
                TimeSpan.TryParse(match.Groups["time"].Value, out var time) ? time : null,
                LevelOf(string.IsNullOrEmpty(tags) ? inline : tags, message),
                message));
        }

        // Stack traces and other wrapped output carry no timestamp of their own; they
        // continue the line above, so fill forwards before anything gets dated.
        for (var i = 1; i < parsed.Count; i++)
        {
            if (parsed[i].Time is null)
            {
                parsed[i] = parsed[i] with { Time = parsed[i - 1].Time };
            }
        }

        var result = new ConsoleLine[parsed.Count];
        var date = newestDate;
        TimeSpan? later = null;

        for (var i = parsed.Count - 1; i >= 0; i--)
        {
            var (time, level, text) = parsed[i];
            var stamp = time ?? later ?? TimeSpan.Zero;

            if (later is not null && stamp > later)
            {
                date = date.AddDays(-1);
            }

            later = stamp;

            var moment = date.ToDateTime(TimeOnly.MinValue).Add(stamp);
            result[i] = new ConsoleLine(new DateTimeOffset(moment, TimeZoneInfo.Local.GetUtcOffset(moment)), level, text);
        }

        return [.. result];
    }

    private static string LevelOf(string tag, string message)
    {
        if (message.EndsWith("joined the game", StringComparison.OrdinalIgnoreCase))
        {
            return "join";
        }

        if (tag.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("FATAL", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("SEVERE", StringComparison.OrdinalIgnoreCase))
        {
            return "error";
        }

        return tag.Contains("WARN", StringComparison.OrdinalIgnoreCase) ? "warn" : "info";
    }
}
