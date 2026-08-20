namespace McServerMgmnt.Services.Plugins;

/// <summary>
/// Where the two plugin-related folders live, bound from the "Plugins" configuration
/// section. Both are relative to the working directory (/app in the container) unless
/// given as absolute paths.
/// </summary>
public class PluginOptions
{
    public const string SectionName = "Plugins";

    /// <summary>
    /// Drop-in folder for plugin assemblies built against McAdminPlugins. Either a flat
    /// folder of .dll files or one subfolder per plugin, which is the better layout as
    /// soon as a plugin brings dependencies of its own.
    /// </summary>
    public string Path { get; set; } = "addons";

    /// <summary>
    /// The Minecraft server's plugins folder, bind-mounted into the container. This is
    /// what <see cref="ServerPluginFiles"/> hands out — not the folder above.
    /// </summary>
    public string ServerPluginsPath { get; set; } = "plugins";

    /// <summary>Set false to boot without touching the drop-in folder at all.</summary>
    public bool Enabled { get; set; } = true;
}
