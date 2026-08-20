namespace McAdminPlugins;

/// <summary>
/// One entry a plugin adds to the sidebar. <paramref name="Href"/> has to match a
/// route on one of the plugin's own components — the router only knows about pages
/// it can find, so a link to a route that does not exist lands on the 404 page.
/// </summary>
/// <param name="Text">The label shown in the sidebar.</param>
/// <param name="Href">Route to navigate to, for example "/my-plugin".</param>
/// <param name="Glyph">
/// CSS class for the little square icon, one of the glyph-* classes in app.css.
/// Null uses the generic plugin glyph.
/// </param>
/// <param name="AdministratorOnly">Hides the entry from everyone but administrators.</param>
/// <param name="Order">Sorts the plugin section. Ties fall back to alphabetical.</param>
public record PluginNavItem(
    string Text,
    string Href,
    string? Glyph = null,
    bool AdministratorOnly = false,
    int Order = 0);
