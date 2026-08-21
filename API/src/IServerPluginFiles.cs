namespace McAdminPlugins;

/// <summary>
/// Read and write access to the Minecraft server's own plugins folder — the place
/// Paper, Spigot and friends keep their config.yml files. Every path is relative to
/// that folder, and paths that try to climb out of it with ".." are rejected.
///
/// This is deliberately plain text in and out: config formats differ per Minecraft
/// plugin, so parsing is left to whoever knows the format. Nearly all of them are YAML,
/// though, and <see cref="ServerPluginFilesYaml"/> makes that trip in one call — through
/// a parser that leaves the file's comments and layout alone.
/// </summary>
public interface IServerPluginFiles
{
    /// <summary>
    /// False when the plugins folder is not mounted. Everything else throws while this
    /// is false, so show a banner instead of an error page.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>The per-plugin folders sitting in the plugins directory, by name.</summary>
    IReadOnlyList<string> ListDirectories();

    /// <summary>
    /// Files under <paramref name="relativePath"/>, as paths that can be handed
    /// straight back to <see cref="ReadTextAsync"/>. Pass "" for the root.
    /// </summary>
    IReadOnlyList<string> ListFiles(string relativePath = "", string searchPattern = "*");

    bool Exists(string relativePath);

    Task<string> ReadTextAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Overwrites the file, creating the folder if it is missing. The Minecraft server
    /// reads most config files only at startup, so a restart is usually needed before
    /// the change takes effect.
    /// </summary>
    Task WriteTextAsync(string relativePath, string contents, CancellationToken ct = default);
}
