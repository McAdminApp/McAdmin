using McAdminPlugins.Yaml;

namespace McAdminPlugins;

/// <summary>
/// The short way between the Minecraft server's plugins folder and a
/// <see cref="YamlDocument"/>. <see cref="IServerPluginFiles"/> itself stays text in and
/// text out — config formats differ per Minecraft plugin — but nearly every one of them
/// is YAML, so this is the trip worth not writing out by hand each time.
/// </summary>
public static class ServerPluginFilesYaml
{
    /// <summary>
    /// Reads a config file. Throws <see cref="YamlException"/> — carrying the line — when
    /// the file is not YAML this parser can read, which is a message worth showing as-is.
    /// </summary>
    public static async Task<YamlDocument> ReadYamlAsync(
        this IServerPluginFiles files, string relativePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        return YamlDocument.Parse(await files.ReadTextAsync(relativePath, ct));
    }

    /// <summary>Reads the file if it is there, and hands back an empty document if it is not.</summary>
    public static async Task<YamlDocument> ReadYamlOrEmptyAsync(
        this IServerPluginFiles files, string relativePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        return files.Exists(relativePath)
            ? await files.ReadYamlAsync(relativePath, ct)
            : YamlDocument.Create();
    }

    public static Task WriteYamlAsync(
        this IServerPluginFiles files, string relativePath, YamlDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(document);

        return files.WriteTextAsync(relativePath, document.ToString(), ct);
    }

    /// <summary>
    /// Read, change, write back — the shape a settings section's save handler wants:
    ///
    /// <code>
    /// SaveAsync = async (changes, ct) =>
    /// {
    ///     await files.EditYamlAsync("EssentialsX/config.yml", config =>
    ///     {
    ///         foreach (var (key, value) in changes) config.Set(key, value);
    ///     }, ct);
    ///
    ///     return PluginResult.Success("Saved. Restart the server to apply it.");
    /// }
    /// </code>
    ///
    /// Nothing is written when <paramref name="edit"/> throws, so a handler that gives up
    /// halfway leaves the file as it found it.
    /// </summary>
    public static async Task<YamlDocument> EditYamlAsync(
        this IServerPluginFiles files, string relativePath, Action<YamlDocument> edit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(edit);

        var document = await files.ReadYamlAsync(relativePath, ct);
        edit(document);

        await files.WriteYamlAsync(relativePath, document, ct);

        return document;
    }
}
