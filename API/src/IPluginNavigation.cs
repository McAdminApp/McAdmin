namespace McAdminPlugins;

/// <summary>
/// The sidebar, as far as a plugin is concerned. Ask for it in the plugin's
/// constructor and add entries from <see cref="IPlugin.Load"/>.
/// </summary>
public interface IPluginNavigation
{
    void AddPage(PluginNavItem item);

    /// <inheritdoc cref="AddPage(PluginNavItem)"/>
    void AddPage(string text, string href, string? glyph = null,
        bool administratorOnly = false, int order = 0)
        => AddPage(new PluginNavItem(text, href, glyph, administratorOnly, order));
}
