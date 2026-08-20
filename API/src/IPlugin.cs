namespace McAdminPlugins;

/// <summary>
/// The entry point of a plugin. Implement this once per plugin assembly; the host
/// finds it by scanning the .dll, builds it, and awaits <see cref="Load"/> during
/// startup.
///
/// Everything else is asked for through the constructor —
/// <see cref="IPluginNavigation"/> to add pages to the sidebar,
/// <see cref="IServerPluginFiles"/> to edit config in the Minecraft server's plugins
/// folder, or any service the host has registered.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Called once at startup, before the app serves its first request. Register
    /// navigation here. A plugin that throws is skipped and logged; the rest of the
    /// app carries on.
    /// </summary>
    Task Load();
}
