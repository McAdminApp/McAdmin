using System.Reflection;
using McAdminPlugins;

namespace McServerMgmnt.Services.Plugins;

/// <summary>
/// What the loader found, kept for the lifetime of the app. The router reads
/// <see cref="RouteAssemblies"/> to discover plugin pages and NavMenu reads
/// <see cref="NavItems"/> to draw them. Described pages — the ones a plugin hands over
/// as data instead of Razor — land in <see cref="Pages"/> and are rendered by
/// Components/Addons/AddonPage.razor.
///
/// Everything is written once, during startup, and only read afterwards. The lock is
/// there because plugins are free to register from whatever thread their Load() ran
/// on, not because anything mutates later.
/// </summary>
public class PluginRegistry : IPluginNavigation, IPluginPages
{
    private readonly List<PluginNavItem> _navItems = [];
    private readonly List<Assembly> _routeAssemblies = [];
    private readonly List<LoadedPlugin> _plugins = [];
    private readonly Dictionary<string, PluginPage> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>Sidebar entries contributed by plugins, in the order they should render.</summary>
    public IReadOnlyList<PluginNavItem> NavItems
    {
        get
        {
            lock (_gate)
                return _navItems
                    .OrderBy(i => i.Order)
                    .ThenBy(i => i.Text, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
        }
    }

    /// <summary>Handed to the Router so it can route to components inside plugin assemblies.</summary>
    public IReadOnlyList<Assembly> RouteAssemblies
    {
        get
        {
            lock (_gate)
                return _routeAssemblies.ToArray();
        }
    }

    /// <summary>Every plugin the loader touched, failures included, for diagnostics.</summary>
    public IReadOnlyList<LoadedPlugin> Plugins
    {
        get
        {
            lock (_gate)
                return _plugins.ToArray();
        }
    }

    /// <summary>Described pages, in sidebar order.</summary>
    public IReadOnlyList<PluginPage> Pages
    {
        get
        {
            lock (_gate)
                return _pages.Values
                    .OrderBy(p => p.Order)
                    .ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
        }
    }

    public void AddPage(PluginNavItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Href);

        lock (_gate)
            _navItems.Add(item);
    }

    /// <summary>
    /// Registers a described page and, unless it asked to stay hidden, its sidebar entry.
    /// A slug that is already taken throws: two pages on one route is a plugin bug, and
    /// the loader turns the throw into a load failure for that plugin alone.
    /// </summary>
    public void AddPage(PluginPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        lock (_gate)
        {
            if (!_pages.TryAdd(page.Slug, page))
                throw new InvalidOperationException(
                    $"The page slug '{page.Slug}' is already registered by another plugin.");

            if (page.ShowInNavigation)
                _navItems.Add(new PluginNavItem(
                    page.NavigationText ?? page.Title,
                    page.Href,
                    page.Glyph,
                    page.AdministratorOnly,
                    page.Order));
        }
    }

    /// <summary>The page behind a route, or null for a slug nothing claimed.</summary>
    public PluginPage? FindPage(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        lock (_gate)
            return _pages.GetValueOrDefault(slug);
    }

    internal void AddAssembly(Assembly assembly)
    {
        lock (_gate)
        {
            if (!_routeAssemblies.Contains(assembly))
                _routeAssemblies.Add(assembly);
        }
    }

    internal void Add(LoadedPlugin plugin)
    {
        lock (_gate)
            _plugins.Add(plugin);
    }
}
