namespace McAdminPlugins;

/// <summary>
/// Described pages, as far as a plugin is concerned. Ask for it in the plugin's
/// constructor and register from <see cref="IPlugin.Load"/>; the host routes each page,
/// draws it, and adds its sidebar entry.
/// </summary>
public interface IPluginPages
{
    void AddPage(PluginPage page);

    /// <inheritdoc cref="AddPage(PluginPage)"/>
    void AddPage(string slug, string title, params PluginSection[] sections) =>
        AddPage(new PluginPage(slug, title) { Sections = sections });
}
