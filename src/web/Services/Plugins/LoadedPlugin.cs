using System.Reflection;

namespace McServerMgmnt.Services.Plugins;

/// <summary>One plugin the host tried to load, whether or not it worked out.</summary>
public record LoadedPlugin(string Name, string Version, string File, string? Error = null)
{
    public bool IsLoaded => Error is null;

    public static LoadedPlugin From(Type pluginType, string file) => new(
        pluginType.Assembly.GetName().Name ?? pluginType.Name,
        pluginType.Assembly.GetName().Version?.ToString() ?? "0.0.0",
        file);
}
