using System.Reflection;
using McAdminPlugins;

namespace McServerMgmnt.Services.Plugins;

/// <summary>
/// What the loader found, kept for the lifetime of the app. The router reads
/// <see cref="RouteAssemblies"/> to discover plugin pages and NavMenu reads
/// <see cref="NavItems"/> to draw them.
///
/// Everything is written once, during startup, and only read afterwards. The lock is
/// there because plugins are free to register from whatever thread their Load() ran
/// on, not because anything mutates later.
/// </summary>
public class PluginRegistry : IPluginNavigation
{
    private readonly List<PluginNavItem> _navItems = [];
    private readonly List<Assembly> _routeAssemblies = [];
    private readonly List<LoadedPlugin> _plugins = [];
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

    public void AddPage(PluginNavItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Href);

        lock (_gate)
            _navItems.Add(item);
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
