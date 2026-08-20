using System.Reflection;
using System.Runtime.Loader;
using McAdminPlugins;
using Microsoft.Extensions.Options;

namespace McServerMgmnt.Services.Plugins;

/// <summary>
/// Finds .dll files in the drop-in folder, loads the ones that carry an
/// <see cref="IPlugin"/>, and awaits their Load(). Runs once from Program.cs, before
/// the first request, because the router needs the assembly list up front.
/// </summary>
public static class PluginLoader
{
    /// <summary>
    /// Assemblies that must always come from the host. A plugin's build output
    /// contains a copy of McAdminPlugins.dll and the framework it compiled against;
    /// loading those copies would give us a second McAdminPlugins.IPlugin type that
    /// looks identical and casts to nothing.
    /// </summary>
    private static bool IsHostAssembly(string simpleName, HashSet<string> loaded) =>
        loaded.Contains(simpleName)
        || simpleName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
        || simpleName.StartsWith("System.", StringComparison.OrdinalIgnoreCase);

    public static async Task LoadAsync(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<PluginOptions>>().Value;
        var registry = services.GetRequiredService<PluginRegistry>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PluginLoader));

        if (!options.Enabled)
        {
            logger.LogInformation("Plugin-laddning avstängd via konfiguration.");
            return;
        }

        var root = Path.GetFullPath(options.Path);
        if (!Directory.Exists(root))
        {
            logger.LogInformation("Ingen plugin-katalog på {Path}, hoppar över.", root);
            return;
        }

        // Each plugin may sit loose in the folder or in a subfolder of its own with its
        // dependencies next to it. Subfolders are probed for those dependencies.
        var probeFolders = Directory.GetDirectories(root).Append(root).ToArray();
        AssemblyLoadContext.Default.Resolving += (_, name) => ProbeFolders(probeFolders, name);

        foreach (var file in probeFolders.SelectMany(folder => Directory.EnumerateFiles(folder, "*.dll")).Order())
        {
            await LoadFileAsync(file, registry, services, logger);
        }

        var ok = registry.Plugins.Count(p => p.IsLoaded);
        logger.LogInformation("Plugins: {Loaded} laddade, {Failed} misslyckades, katalog {Path}.",
            ok, registry.Plugins.Count - ok, root);
    }

    private static async Task LoadFileAsync(string file, PluginRegistry registry,
        IServiceProvider services, ILogger logger)
    {
        // Whatever is already in the process wins — see IsHostAssembly. This also skips
        // the framework assemblies that get copied into a plugin's publish output.
        var loaded = AssemblyLoadContext.Default.Assemblies
            .Select(a => a.GetName().Name)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string simpleName;
        try
        {
            simpleName = AssemblyName.GetAssemblyName(file).Name ?? Path.GetFileNameWithoutExtension(file);
        }
        catch (BadImageFormatException)
        {
            // Native library sitting in the folder. Not ours to load.
            return;
        }

        if (IsHostAssembly(simpleName, loaded))
            return;

        Assembly assembly;
        try
        {
            // Deliberately the default context and not a private one: Blazor resolves a
            // component's assembly by name when it sets up an interactive circuit, and
            // an assembly hidden in its own load context is not findable that way — the
            // page would render once and then never become interactive. The trade-off is
            // that plugins cannot be unloaded or reloaded without restarting the app.
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kunde inte läsa {File} som .NET-assembly.", file);
            return;
        }

        var pluginTypes = FindPluginTypes(assembly, logger);
        if (pluginTypes.Length == 0)
            return;

        foreach (var type in pluginTypes)
        {
            try
            {
                // Built from the root provider, so a plugin holds nothing scoped. Pages
                // that need per-request services inject them themselves.
                var plugin = (IPlugin)ActivatorUtilities.CreateInstance(services, type);
                await plugin.Load();

                registry.AddAssembly(assembly);
                registry.Add(LoadedPlugin.From(type, file));

                logger.LogInformation("Laddade plugin {Plugin} från {File}.", type.FullName, file);
            }
            catch (Exception ex)
            {
                // One broken plugin must not keep the app from starting.
                registry.Add(LoadedPlugin.From(type, file) with { Error = ex.Message });
                logger.LogError(ex, "Plugin {Plugin} i {File} kunde inte laddas.", type.FullName, file);
            }
        }
    }

    private static Type[] FindPluginTypes(Assembly assembly, ILogger logger)
    {
        try
        {
            return assembly.GetTypes()
                .Where(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
                .ToArray();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A plugin built against a different version of something. Take the types
            // that did load rather than dropping the whole assembly.
            logger.LogWarning(ex, "Alla typer i {Assembly} kunde inte läsas.", assembly.GetName().Name);

            return ex.Types
                .Where(t => t is not null && typeof(IPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
                .ToArray()!;
        }
    }

    /// <summary>
    /// Last-chance lookup for a dependency a plugin brought along but that is not next
    /// to the app itself.
    /// </summary>
    private static Assembly? ProbeFolders(string[] folders, AssemblyName name)
    {
        if (name.Name is null) return null;

        foreach (var folder in folders)
        {
            var candidate = Path.Combine(folder, name.Name + ".dll");
            if (File.Exists(candidate))
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
        }

        return null;
    }
}
