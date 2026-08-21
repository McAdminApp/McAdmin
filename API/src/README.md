# Plugins

McAdmin loads plugins from a folder at startup. A plugin is an ordinary .NET assembly
built against `McAdminPlugins`, and it can do three things:

* describe pages that the host renders in its own design — no markup involved,
* bring its own Razor components when a page needs something the description cannot
  express, and
* read and write config files in the Minecraft server's own plugins folder, through a
  YAML parser that puts the file back the way it found it, comments and all.

Start with a described page. It is less code, it cannot drift out of step with the app's
look, and it cannot end up on a route that does not exist.

| Project   | Role |
|-----------|------|
| `API/src` | `McAdminPlugins` — the contract plugin authors build against. Distributed as a loose `.dll`. |
| `Web/src` | The web app. `Services/Plugins/` holds the loader and the implementations. |

---

## Writing a plugin

### 1. Create the project

Get `McAdminPlugins.dll` — it is archived as an artifact on the Jenkins build — and put
it somewhere in your project, for example a `lib/` folder.

A plugin that describes its pages, touches config files, or both is a plain
`Microsoft.NET.Sdk` project — it needs nothing from ASP.NET. Only a plugin that ships
Razor components of its own needs `Microsoft.NET.Sdk.Razor`:

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

public sealed class MyPlugin(IPluginPages pages, IServerPluginFiles files) : IPlugin
{
    public Task Load()
    {
        pages.AddPage(new PluginPage("essentials", "Essentials")
        {
            Description = "EssentialsX configuration.",
            Order = 10,
            Sections = [ /* see below */ ]
        });

        return Task.CompletedTask;
    }
}
```

The constructor goes through the host's DI container, so you can ask for any registered
service — `ILogger<MyPlugin>`, for instance. The instance lives for the lifetime of the
app and must therefore not hold on to anything scoped.

Hold the services you need in fields and close over them from the section callbacks, as
the example below does. A described page needs no static back door to reach them, which
is the other reason to prefer it over a component of your own.

### 3. Describe a page

Hand the host a `PluginPage`: a slug, a title and a list of sections. The host routes it
at `/addon/{slug}`, draws the heading, adds the sidebar entry and renders every section
with the same markup its own pages use.

```csharp
pages.AddPage(new PluginPage("essentials", "Essentials")
{
    Description = "Read from EssentialsX/config.yml. Nothing is written until you save.",
    Sections =
    [
        new PluginSettingsSection
        {
            Title = "General",
            LoadAsync = async ct =>
            {
                var config = await files.ReadYamlAsync("EssentialsX/config.yml", ct);

                return
                [
                    new PluginField("motd", "Message of the day")
                    {
                        Value = config.GetString("motd"),
                        Description = "Shown to players as they join."
                    },
                    new PluginField("god-mode", "God mode")
                    {
                        Kind = PluginFieldKind.Toggle,
                        Value = config.GetBool("god-mode") ? "true" : "false",
                        Group = "Gameplay",
                        RequiresRestart = true
                    }
                ];
            },
            SaveAsync = async (changes, ct) =>
            {
                await files.EditYamlAsync("EssentialsX/config.yml", config =>
                {
                    foreach (var (key, value) in changes) config.Set(key, value);
                }, ct);

                return PluginResult.Success();
            }
        }
    ]
});
```

A field's `Key` is a YAML path, which is what makes that save handler a one-liner: the
host hands back the keys that changed, and each one is already where the value goes.
Toggles are the one place to be deliberate — the host's checkbox reads the exact string
`"true"`, so load them through `GetBool` rather than `GetString`, or a file that says
`god-mode: yes` draws an unticked box.

That is the whole page. It gets the filter box, the grouped rows, per-row **Undo**, the
"2 unsaved changes" command bar and the green or red notice afterwards — the same ones
the server settings page has, because it is the same renderer.

**The sections**

| Section | What it draws |
|---------|---------------|
| `PluginSettingsSection` | The settings table: grouped rows, filter, Undo, and a save bar. `SaveAsync` gets only the keys that changed. |
| `PluginTableSection` | A read-only table with optional per-row buttons. A button with `Confirm` set asks in the row before it runs. |
| `PluginFormSection` | A handful of fields and one submit button. Clears itself on success unless `KeepValues` says otherwise. |
| `PluginActionsSection` | Buttons that do something and report back. |
| `PluginNoticeSection` | One of the four coloured banners. |
| `PluginTextSection` | Paragraphs, and a readout of label/value pairs. |

Sections load their own data, which is why every one of them takes callbacks rather than
a finished list: after a successful save or row action the host calls `LoadAsync` again,
so what is on screen is what is stored.

Handlers return a `PluginResult`, and the host turns it into a notice. Throwing works
too — the exception message is shown as the error and the rest of the app is unaffected:

```csharp
SaveAsync = async (changes, ct) =>
{
    if (changes.ContainsKey("port"))
        return PluginResult.Failure("The port cannot be changed while the server is up.");

    await WriteAsync(changes, ct);
    return PluginResult.Success("Written. Restart for it to take effect.");
}
```

Set `AdministratorOnly` to keep a page to administrators; unlike a nav flag that only
hides the link, the host enforces it on the route itself. Use `BuildAsync` instead of
`Sections` when the list of sections depends on data — one section per config file
found, say.

### 4. Or write the markup yourself

When a page needs something the sections cannot express, it can still be an ordinary
Razor component with `@page`. Register the sidebar entry through `IPluginNavigation`,
and make sure the route matches the `Href` you registered.

A component is constructed by Blazor, not by you, so it cannot reach the plugin instance
or anything the plugin was handed. Park what the page needs on a static, which is the
one thing a described page saves you from:

```csharp
public sealed class MyPlugin(IPluginNavigation nav, IServerPluginFiles files) : IPlugin
{
    public static IServerPluginFiles? Files { get; private set; }

    public Task Load()
    {
        Files = files;
        nav.AddPage("Essentials", "/essentials", order: 10);

        return Task.CompletedTask;
    }
}
```

```razor
@page "/essentials"
@rendermode InteractiveServer

<h1>Essentials</h1>
<p>Connected: @(MyPlugin.Files?.IsConnected)</p>

<button class="btn" @onclick="Save">Save</button>

@code {
    private Task Save() =>
        MyPlugin.Files!.EditYamlAsync("EssentialsX/config.yml",
            config => config.Set("enabled", true));
}
```

The page gets the host's `MainLayout` and its CSS classes automatically. Add
`@attribute [Authorize]` if it should not be open to signed-out visitors — the route is
open until you say otherwise, exactly like the app's own pages.

This is the path that costs you the app's design: every class you use here is one you
have to keep in step with `app.css` by hand, and a `PluginNavItem` whose `Href` has no
matching route lands the user on the 404 page. A described page has neither problem.

### 5. Build and drop it in

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

### `IPluginPages`

```csharp
void AddPage(PluginPage page);
void AddPage(string slug, string title, params PluginSection[] sections);
```

Registers a described page. The host routes it at `/addon/{slug}`, renders it, and adds
its sidebar entry unless `ShowInNavigation` is false. Slugs are lowercase letters,
digits and hyphens, and they are global: the second plugin to claim one fails to load,
alone, and the first keeps the route.

### `PluginPage`

| Member | Meaning |
|--------|---------|
| `Slug`, `Title` | Constructor arguments. The slug is the URL, the title is the heading and the sidebar label. |
| `Eyebrow`, `Description` | The small label above the heading, and the paragraph under it. |
| `Sections` | What goes on the page, top to bottom. |
| `BuildAsync` | Builds the section list per visit, for a page whose shape depends on data. Wins over `Sections`. |
| `Glyph`, `Order`, `NavigationText`, `ShowInNavigation` | The sidebar entry the host creates for the page. |
| `AdministratorOnly` | Enforced on the route, not just in the sidebar. |
| `Href` | Where the page ended up. Use it to link to it from elsewhere. |

Every editable value is a `PluginField`: a `Key`, a `Label`, a `Kind` (`Text`, `Number`,
`Toggle`, `Choice`, `Password`, `LongText`) and the `Value` as it is stored right now.
Values are strings in both directions — the host has no idea what your config format
wants, and a string survives `.properties`, YAML and JSON unchanged.

Handlers return a `PluginResult`: `PluginResult.Success("Saved.")`,
`PluginResult.Failure("...")`, or `PluginResult.None` to say nothing at all. An
exception is caught and shown as a failure, and a failed save keeps the user's edits
on screen.

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

Text in and text out, because config formats differ between Minecraft plugins. Nearly
all of them are YAML, though, so that one trip is made for you — see below. The server
also reads most of these files only at startup, so a change usually does not take effect
until it restarts.

### YAML

`McAdminPlugins.Yaml` holds a YAML parser built for exactly one job: changing a value in
a Minecraft plugin's `config.yml` without disturbing the rest of the file. EssentialsX
ships around a thousand lines of comments explaining its own settings, and a round trip
through an ordinary YAML library throws every one of them away. Here a save rewrites the
lines it has to and no others.

```csharp
using McAdminPlugins.Yaml;

var config = await files.ReadYamlAsync("EssentialsX/config.yml", ct);

config.GetString("motd");                    // "Welcome!"
config.GetBool("god-mode");                  // reads true, yes and on alike
config.GetInt("teleport-delay", 3);          // with a fallback
config.GetStringList("disabled-commands");   // ["nick", "ping"]
config.GetKeys("kits");                      // ["tools", "dtools"], in file order

config.Set("motd", "Welcome back!");
config.Set("god-mode", true);
config.SetList("disabled-commands", ["nick", "ping", "me"]);
config.Remove("obsolete-setting");

await files.WriteYamlAsync("EssentialsX/config.yml", config, ct);
```

`EditYamlAsync` is the three of those in one call, and is what a save handler wants:

```csharp
await files.EditYamlAsync("EssentialsX/config.yml", config =>
{
    foreach (var (key, value) in changes) config.Set(key, value);
}, ct);
```

Nothing is written if the callback throws, so a handler that gives up halfway leaves the
file as it found it. `ReadYamlOrEmptyAsync` hands back an empty document rather than
throwing when the file is not there yet, and `YamlDocument.Create()` starts one from
nothing.

**Paths** are the dotted form Bukkit's own configuration API uses — `"world-options.world.pvp"` —
with `[0]` to step into a list. A string converts on its own, so paths go in inline. Keys
that themselves contain a dot cannot be written that way; build those with
`YamlPath.Of("permissions", "essentials.fly")`, which takes its segments literally.

**What the file gets back.** Writing tries to change as little as it can:

| The file says | You write | The file ends up |
|---------------|-----------|------------------|
| `motd: 'Hi'` | `Set("motd", "Hello")` | `motd: 'Hello'` — the quotes stay |
| `god-mode: no` | `Set("god-mode", true)` | `god-mode: yes` — the spelling stays |
| `delay: 10  # ticks` | `Set("delay", 20L)` | `delay: 20  # ticks` — the comment stays |
| `signs: [sign]` | `SetList("signs", [...])` | stays inline; a `- item` list stays a list |
| nothing | `Set("a.b.c", "x")` | the blocks are created, indented like the rest of the file |

`Set(path, string)` writes the text as it stands, which is what a settings field wants —
the user typed it, the file gets it. `SetString` is the careful sibling: it quotes a
value that would otherwise read back as something else, so `SetString("prefix", "yes")`
lands as `prefix: 'yes'` rather than a boolean. There are also overloads for `bool`,
`long` and `double`.

**Reading is forgiving, writing is precise.** `GetBool` accepts `true`, `yes` and `on` in
any case and quoted or not, because an admin who wrote `'false'` meant the setting off.
Numbers understand `0x1F`, `0755` and `1_000`, the way SnakeYAML — which is what the
Minecraft server itself reads these files with — resolves them.

**Reaching past the helpers.** `Find` returns the `YamlNode` at a path — a `YamlScalar`,
`YamlMapping` or `YamlSequence` — and every node carries the `Line` it came from, which
is worth putting in an error message. A `YamlException` carries one too.

**What it does not do.** Anchors and aliases (`&x` / `*x`), tags, merge keys, multiple
documents in one file, and values that run across several lines without quotes or a `|`
block. None of these appear in the config files a Minecraft server writes; all of them
are refused with a message naming the line rather than parsed into something wrong.
Adding a key to a non-empty inline mapping (`{a: 1}`) is refused for the same reason —
write the whole value instead.

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

**`PluginRegistry`** is a singleton holding nav items, route assemblies, described
pages and load results. It implements `IPluginNavigation` and `IPluginPages`, which is
what plugins see. Everything is written during startup and only read afterwards.

**`Components/Addons/`** renders described pages. `AddonPage.razor` owns the single
route `/addon/{slug}`, looks the page up in the registry and draws the heading;
`AddonSection.razor` maps each section to the component that renders it. A described
page therefore needs nothing from the routing machinery below — the route exists whether
or not any plugin is loaded, so `AdditionalAssemblies` only matters to plugins that
bring components of their own.

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
* **A described page can only say what the sections can express.** The vocabulary is
  deliberately small, and the host owns the markup — that is what keeps addons looking
  like the app. A page that needs more than the sections offer has to bring its own
  Razor component, and pay for it in maintenance.
* **Plugins run with full privileges inside the app's process.** There is no sandbox
  beyond `IServerPluginFiles` staying inside the plugins folder — a plugin can otherwise
  do anything the app can. Only install code you trust.
