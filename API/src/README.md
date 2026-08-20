# Plugins

McAdmin loads plugins from a folder at startup. A plugin is an ordinary .NET assembly
built against `McAdminPlugins`, and it can do two things:

* add pages to the navigation menu, and
* read and write config files in the Minecraft server's own plugins folder.

| Project   | Role |
|-----------|------|
| `API/src` | `McAdminPlugins` — the contract plugin authors build against. Distributed as a loose `.dll`. |
| `Web/src` | The web app. `Services/Plugins/` holds the loader and the implementations. |

---

## Writing a plugin

### 1. Create the project

Get `McAdminPlugins.dll` — it is archived as an artifact on the Jenkins build — and put
it somewhere in your project, for example a `lib/` folder.

A plugin that only touches config files can be a plain `Microsoft.NET.Sdk` project. One
that adds pages needs `Microsoft.NET.Sdk.Razor`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
    </PropertyGroup>
    <ItemGroup>
        <Reference Include="McAdminPlugins">
            <HintPath>lib\McAdminPlugins.dll</HintPath>
            <!-- The host already has its own copy and always loads that one.
                 Private=false keeps it out of your bin/ so you don't ship a duplicate. -->
            <Private>false</Private>
        </Reference>
    </ItemGroup>
</Project>
```

If it ends up in `bin/` anyway that is harmless — the host skips the copy. With the
source next to you, pointing straight at the project works just as well during
development: `<ProjectReference Include="..\..\mcmngmt\API\src\McAdminPlugins.csproj" />`.

Use a `McAdminPlugins.dll` from the same build as the app you are targeting. The
contract carries no version of its own, so an outdated .dll against a newer app is not
caught until load time.

### 2. Implement `IPlugin`

The host finds the class by scanning the assembly, constructs it, and awaits `Load()`
once during startup. Everything a plugin needs is requested through its constructor:

```csharp
using McAdminPlugins;

namespace MyPlugin;

public sealed class MyPlugin(IPluginNavigation nav, IServerPluginFiles files) : IPlugin
{
    public static IServerPluginFiles? Files { get; private set; }

    public Task Load()
    {
        Files = files;

        nav.AddPage("Essentials", "/essentials", order: 10);
        nav.AddPage(new PluginNavItem("Clear cache", "/essentials/admin",
            AdministratorOnly: true, Order: 20));

        return Task.CompletedTask;
    }
}
```

The constructor goes through the host's DI container, so you can ask for any registered
service — `ILogger<MyPlugin>`, for instance. The instance lives for the lifetime of the
app and must therefore not hold on to anything scoped; pages that need scoped services
inject them themselves.

### 3. Add a page

An ordinary Razor component with `@page`. The route has to match the `Href` you
registered:

```razor
@page "/essentials"
@rendermode InteractiveServer

<h1>Essentials</h1>
<p>Connected: @(MyPlugin.Files?.IsConnected)</p>

<button class="btn" @onclick="Save">Save</button>

@code {
    private async Task Save()
    {
        var yaml = await MyPlugin.Files!.ReadTextAsync("EssentialsX/config.yml");
        await MyPlugin.Files.WriteTextAsync("EssentialsX/config.yml",
            yaml.Replace("enabled: false", "enabled: true"));
    }
}
```

The page gets the host's `MainLayout` and its CSS classes automatically. Add
`@attribute [Authorize]` if it should not be open to signed-out visitors — the route is
open until you say otherwise, exactly like the app's own pages.

### 4. Build and drop it in

```sh
dotnet build -c Release
```

Copy the contents of `bin/Release/net10.0/` into a folder of its own inside the drop-in
directory:

```
addons/
  MyPlugin/
    MyPlugin.dll
    McAdminPlugins.dll        # harmless, the host skips it
  AnotherPlugin/
    AnotherPlugin.dll
```

Loose `.dll` files directly in `addons/` work too, but one folder per plugin is better
as soon as a plugin has dependencies of its own — those are only probed for in the
plugin's own folder.

Restart the app. Plugins are read at startup and never again.

---

## The API

### `IPlugin`

```csharp
Task Load();
```

Called once, before the app takes its first request. Register navigation here. A plugin
that throws is skipped and logged; the rest of the app starts anyway.

### `IPluginNavigation`

```csharp
void AddPage(PluginNavItem item);
void AddPage(string text, string href, string? glyph = null,
             bool administratorOnly = false, int order = 0);
```

Entries land under a "Plugins" heading in the sidebar, sorted by `Order` and then
alphabetically. `AdministratorOnly` hides the entry from everyone but administrators —
that is a UI filter, so protect the page itself with
`@attribute [Authorize(Roles = ...)]` as well.

`Glyph` is one of the `glyph-*` classes in `app.css`; leaving it out gives the generic
plugin icon.

### `IServerPluginFiles`

Read and write access to the Minecraft server's plugins folder — where EssentialsX,
LuckPerms and the rest keep their `config.yml`.

```csharp
bool IsConnected { get; }
IReadOnlyList<string> ListDirectories();
IReadOnlyList<string> ListFiles(string relativePath = "", string searchPattern = "*");
bool Exists(string relativePath);
Task<string> ReadTextAsync(string relativePath, CancellationToken ct = default);
Task WriteTextAsync(string relativePath, string contents, CancellationToken ct = default);
```

Every path is relative to the plugins folder. Paths that point out of it — `..` or an
absolute path — are refused with `UnauthorizedAccessException`. Check `IsConnected`
before doing anything: if the folder is not mounted, the other calls throw.

Text in and text out, deliberately. Config formats differ between Minecraft plugins, so
parsing YAML or JSON is left to the plugin that knows which format it is dealing with.
The server also reads most of these files only at startup, so a change usually does not
take effect until it restarts.

---

## Configuration

The `Plugins` section in `appsettings.json`, or `Plugins__*` as environment variables:

| Key                 | Default   | Meaning |
|---------------------|-----------|---------|
| `Enabled`           | `true`    | `false` starts the app without looking in the drop-in folder at all. |
| `Path`              | `addons`  | The drop-in folder for plugin assemblies. |
| `ServerPluginsPath` | `plugins` | The Minecraft server's plugins folder, the one `IServerPluginFiles` hands out. |

Both paths are relative to the working directory (`/app` in the container) unless given
as absolute paths. In `docker-compose.yml`, `/app/plugins` is bind-mounted against the
server's real plugins folder; `/app/addons` exists in the image and can be mounted so
plugins can be dropped in without rebuilding.

The two folders are **not** the same thing: `addons` holds McAdmin's own extensions,
`plugins` belongs to the Minecraft server.

---

## How it works

Everything lives in `Web/src/Services/Plugins/`.

**`PluginLoader`** runs from `Program.cs` right after `DbInitializer`, before the first
request, because routing needs the assembly list in place before endpoints are built.

It walks `addons/` plus each of its subfolders and looks at every `.dll`:

1. Files that are not .NET assemblies are skipped (`BadImageFormatException`).
2. Assemblies whose simple name is already in the process are skipped, as is anything
   starting with `Microsoft.` or `System.`. This is the part that matters: a plugin's
   build output contains a copy of `McAdminPlugins.dll`, and loading that copy would
   give us a second `McAdminPlugins.IPlugin` that looks identical but casts to nothing.
   The contract must always come from the host.
3. The rest are loaded with `AssemblyLoadContext.Default.LoadFromAssemblyPath`.
4. Types implementing `IPlugin` are built with `ActivatorUtilities.CreateInstance` from
   the root provider, and `Load()` is awaited.
5. The assembly goes into `PluginRegistry`, and a `LoadedPlugin` is recorded — including
   for the ones that failed, so the list can be shown.

Each plugin runs in its own try/catch. A `Load()` that throws is logged as `fail:` and
loading continues with the next one.

**The default context, not a private one.** This is a deliberate choice. Blazor resolves
a component's assembly by name when it wires up an interactive circuit, and an assembly
hidden away in its own `AssemblyLoadContext` cannot be found that way — the page would
render once and then never become interactive. The price is that plugins can neither be
unloaded nor replaced while the app is running: the `.dll` is locked by the process, so
updating one means restarting.

A `Resolving` hook on the default context probes the plugin folders, so a plugin that
brings dependencies of its own gets them resolved out of its own folder.

**`PluginRegistry`** is a singleton holding nav items, route assemblies and load
results. It implements `IPluginNavigation`, which is what plugins see. Everything is
written during startup and only read afterwards.

**Routing needs two registrations**, covering one half of a request each:

```csharp
// Program.cs — the endpoints that answer the first, server-rendered hit on the URL
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(registry.RouteAssemblies.ToArray());
```

```razor
@* Routes.razor — the same list, so navigating there from a live circuit resolves too *@
<Router AppAssembly="typeof(Program).Assembly"
        AdditionalAssemblies="Plugins.RouteAssemblies" ... />
```

With only one of them, a plugin page either 404s or dead-ends on client-side navigation.

**`NavMenu.razor`** reads `PluginRegistry.NavItems` and draws a "Plugins" section when
the list is not empty. Entries marked `AdministratorOnly` are wrapped in `AuthorizeView`.

---

## Limitations

* **No hot reload.** Plugins are read at startup and the `.dll` stays locked for as long
  as the app runs. Swap a plugin, then restart.
* **A plugin's `wwwroot` is not served.** Static assets in a Razor class library are
  wired up at build time, and a plugin is loaded after that. Use the host's CSS classes,
  or put CSS and JS inline in the component.
* **Plugins run with full privileges inside the app's process.** There is no sandbox
  beyond `IServerPluginFiles` staying inside the plugins folder — a plugin can otherwise
  do anything the app can. Only install code you trust.
