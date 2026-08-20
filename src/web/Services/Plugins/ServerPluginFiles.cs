using McAdminPlugins;
using Microsoft.Extensions.Options;

namespace McServerMgmnt.Services.Plugins;

/// <summary>
/// Serves the Minecraft server's plugins folder to plugins, and nothing else. Every
/// path a plugin hands in is resolved against the root and checked afterwards, so
/// "../../etc/passwd" and rooted paths land outside the root and are refused.
/// </summary>
public class ServerPluginFiles(IOptions<PluginOptions> options) : IServerPluginFiles
{
    private readonly string _root = Path.GetFullPath(options.Value.ServerPluginsPath);

    public bool IsConnected => Directory.Exists(_root);

    public IReadOnlyList<string> ListDirectories()
    {
        if (!IsConnected) return [];

        return Directory.EnumerateDirectories(_root)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> ListFiles(string relativePath = "", string searchPattern = "*")
    {
        if (!IsConnected) return [];

        var directory = Resolve(relativePath);
        if (!Directory.Exists(directory)) return [];

        return Directory.EnumerateFiles(directory, searchPattern)
            .Select(path => Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public bool Exists(string relativePath) =>
        IsConnected && File.Exists(Resolve(relativePath));

    public Task<string> ReadTextAsync(string relativePath, CancellationToken ct = default)
    {
        EnsureConnected();
        return File.ReadAllTextAsync(Resolve(relativePath), ct);
    }

    public async Task WriteTextAsync(string relativePath, string contents, CancellationToken ct = default)
    {
        EnsureConnected();

        var path = Resolve(relativePath);
        var directory = Path.GetDirectoryName(path);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, contents, ct);
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new DirectoryNotFoundException(
                $"Minecraft-serverns plugins-katalog är inte mountad på '{_root}'.");
    }

    /// <summary>
    /// Turns a plugin-supplied relative path into a full one, and refuses anything that
    /// does not end up under the plugins root.
    /// </summary>
    private string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));

        if (full != _root && !full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(
                $"'{relativePath}' pekar utanför Minecraft-serverns plugins-katalog.");

        return full;
    }
}
