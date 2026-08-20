# Plugins

McAdmin läser in plugins från en katalog vid uppstart. Ett plugin är en vanlig .NET-
assembly som bygger mot `McAdminPlugins` och kan göra två saker:

* lägga till sidor i navigationsmenyn, och
* läsa och skriva konfigfiler i Minecraft-serverns egen plugins-katalog.

| Projekt      | Roll |
|--------------|------|
| `src/plugin` | `McAdminPlugins` — kontraktet plugin-författare bygger mot. Packas som NuGet av Jenkins. |
| `src/web`    | Webbappen. `Services/Plugins/` innehåller laddaren och implementationerna. |

---

## Skriva ett plugin

### 1. Skapa projektet

Ett plugin som bara rör konfigfiler kan vara ett `Microsoft.NET.Sdk`-projekt. Ska det
ha sidor behövs `Microsoft.NET.Sdk.Razor`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="McAdminPlugins" Version="1.0.0" />
    </ItemGroup>
</Project>
```

`.nupkg`-filen ligger som artefakt på Jenkins-bygget. Under utveckling går det lika bra
med en `<ProjectReference Include="..\..\mcmngmt\src\plugin\McAdminPlugins.csproj" />`.

### 2. Implementera `IPlugin`

Värden hittar klassen genom att skanna assemblyn, bygger den, och inväntar `Load()`
en gång under uppstart. Allt pluginen behöver begärs i konstruktorn:

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
        nav.AddPage(new PluginNavItem("Rensa cache", "/essentials/admin",
            AdministratorOnly: true, Order: 20));

        return Task.CompletedTask;
    }
}
```

Konstruktorn går genom värdens DI-container, så vilken registrerad tjänst som helst går
att be om — till exempel `ILogger<MyPlugin>`. Instansen lever hela appens livstid och
får därför inte hålla i något scoped; sidor som behöver det injicerar själva.

### 3. Lägg till en sida

En helt vanlig Razor-komponent med `@page`. Routen måste matcha det `Href` du
registrerade:

```razor
@page "/essentials"
@rendermode InteractiveServer

<h1>Essentials</h1>
<p>Kopplad: @(MyPlugin.Files?.IsConnected)</p>

<button class="btn" @onclick="Save">Spara</button>

@code {
    private async Task Save()
    {
        var yaml = await MyPlugin.Files!.ReadTextAsync("EssentialsX/config.yml");
        await MyPlugin.Files.WriteTextAsync("EssentialsX/config.yml",
            yaml.Replace("enabled: false", "enabled: true"));
    }
}
```

Sidan får värdens `MainLayout` och dess CSS-klasser automatiskt. Lägg
`@attribute [Authorize]` på den om den inte ska vara öppen för oinloggade — routen är
öppen tills du säger något annat, precis som appens egna sidor.

### 4. Bygg och lägg på plats

```sh
dotnet build -c Release
```

Kopiera innehållet i `bin/Release/net10.0/` till en egen underkatalog i drop-in-mappen:

```
addons/
  MyPlugin/
    MyPlugin.dll
    McAdminPlugins.dll        # ofarlig, värden hoppar över den
  EttAnnatPlugin/
    EttAnnatPlugin.dll
```

Lösa `.dll`-filer direkt i `addons/` fungerar också, men en katalog per plugin är bättre
så fort ett plugin har egna beroenden — de probas bara i sin egen mapp.

Starta om appen. Plugins läses in vid uppstart och aldrig därefter.

---

## API:t

### `IPlugin`

```csharp
Task Load();
```

Anropas en gång, innan appen tar emot sin första request. Registrera navigation här. Ett
plugin som kastar hoppas över och loggas — resten av appen startar ändå.

### `IPluginNavigation`

```csharp
void AddPage(PluginNavItem item);
void AddPage(string text, string href, string? glyph = null,
             bool administratorOnly = false, int order = 0);
```

Entries hamnar under rubriken "Plugins" i sidomenyn, sorterade på `Order` och därefter
alfabetiskt. `AdministratorOnly` döljer posten för alla utom administratörer — det är en
UI-filtrering, så skydda även själva sidan med `@attribute [Authorize(Roles = ...)]`.

`Glyph` är en av `glyph-*`-klasserna i `app.css`; utelämnad ger den generiska
plugin-ikonen.

### `IServerPluginFiles`

Läs- och skrivåtkomst till Minecraft-serverns plugins-katalog — alltså där EssentialsX,
LuckPerms och andra har sina `config.yml`.

```csharp
bool IsConnected { get; }
IReadOnlyList<string> ListDirectories();
IReadOnlyList<string> ListFiles(string relativePath = "", string searchPattern = "*");
bool Exists(string relativePath);
Task<string> ReadTextAsync(string relativePath, CancellationToken ct = default);
Task WriteTextAsync(string relativePath, string contents, CancellationToken ct = default);
```

Alla vägar är relativa till plugins-katalogen. Vägar som pekar ut ur den — `..` eller en
absolut väg — avvisas med `UnauthorizedAccessException`. Kolla `IsConnected` innan du
gör något: är katalogen inte mountad kastar övriga anrop.

Det är text in och text ut med flit. Konfigformaten skiljer sig åt mellan Minecraft-
plugins, så YAML- eller JSON-parsning får det plugin sköta som vet vilket format det är.
Servern läser dessutom det mesta bara vid uppstart, så en ändring syns oftast först
efter en omstart.

---

## Konfiguration

Sektionen `Plugins` i `appsettings.json`, eller `Plugins__*` som miljövariabler:

| Nyckel              | Standard  | Betyder |
|---------------------|-----------|---------|
| `Enabled`           | `true`    | `false` startar appen utan att titta i drop-in-katalogen. |
| `Path`              | `addons`  | Drop-in-katalogen för plugin-assemblies. |
| `ServerPluginsPath` | `plugins` | Minecraft-serverns plugins-katalog, den `IServerPluginFiles` delar ut. |

Båda vägarna är relativa till arbetskatalogen (`/app` i containern) om de inte är
absoluta. I `docker-compose.yml` är `/app/plugins` bind-mountad mot serverns riktiga
plugins-katalog; `/app/addons` finns i imagen och kan mountas för att kunna släppa in
plugins utan att bygga om.

De två katalogerna är alltså **inte** samma sak: `addons` är McAdmins egna tillägg,
`plugins` är Minecraft-serverns.

---

## Hur det fungerar

Allt ligger i `src/web/Services/Plugins/`.

**`PluginLoader`** körs från `Program.cs` direkt efter `DbInitializer`, före första
requesten, eftersom routingen behöver assembly-listan uppe innan endpoints byggs.

Den går igenom `addons/` plus varje underkatalog och tittar på varje `.dll`:

1. Filer som inte är .NET-assemblies hoppas över (`BadImageFormatException`).
2. Assemblies vars enkla namn redan finns i processen hoppas över, liksom allt som
   börjar på `Microsoft.` eller `System.`. Det är den viktiga biten: ett plugins
   byggutdata innehåller en kopia av `McAdminPlugins.dll`, och laddades den kopian
   skulle vi få en andra `McAdminPlugins.IPlugin` som ser identisk ut men som inget
   castar till. Kontraktet ska alltid komma från värden.
3. Resten laddas med `AssemblyLoadContext.Default.LoadFromAssemblyPath`.
4. Typer som implementerar `IPlugin` byggs med `ActivatorUtilities.CreateInstance` från
   rot-providern, och `Load()` inväntas.
5. Assemblyn läggs i `PluginRegistry`, och en `LoadedPlugin` registreras — även när den
   misslyckades, så listan går att visa upp.

Varje plugin körs i sin egen try/catch. En trasig `Load()` loggas som `fail:` och
laddningen fortsätter med nästa.

**Default-kontexten, inte en privat.** Det är ett medvetet val. Blazor slår upp en
komponents assembly på namn när den kopplar upp en interaktiv circuit, och en assembly
som gömts i en egen `AssemblyLoadContext` hittas inte den vägen — sidan hade renderats
en gång och sedan aldrig blivit interaktiv. Priset är att plugins varken kan laddas ur
eller bytas ut medan appen kör: `.dll`-filen är låst av processen, så en uppdatering
kräver omstart.

En `Resolving`-hook på default-kontexten probar plugin-katalogerna, så ett plugin som
tar med sig egna beroenden får dem upplösta ur sin egen mapp.

**`PluginRegistry`** är singleton och håller nav-poster, route-assemblies och
laddningsresultat. Den implementerar `IPluginNavigation`, vilket är vad pluginen ser.
Allt skrivs under uppstart och läses bara därefter.

**Routing kräver två registreringar**, som täcker var sin halva av en request:

```csharp
// Program.cs — endpoints som svarar på första, serverrenderade träffen på URL:en
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(registry.RouteAssemblies.ToArray());
```

```razor
@* Routes.razor — samma lista, så navigering inifrån en levande circuit hittar rätt *@
<Router AppAssembly="typeof(Program).Assembly"
        AdditionalAssemblies="Plugins.RouteAssemblies" ... />
```

Med bara den ena ger plugin-sidan antingen 404 eller en återvändsgränd vid
klientnavigering.

**`NavMenu.razor`** läser `PluginRegistry.NavItems` och ritar en "Plugins"-sektion när
listan inte är tom. Poster med `AdministratorOnly` lindas i `AuthorizeView`.

---

## Begränsningar

* **Ingen hot reload.** Plugins läses in vid uppstart och `.dll`-filen är låst så länge
  appen kör. Byt plugin, starta om.
* **`wwwroot` i ett plugin serveras inte.** Statiska tillgångar i ett Razor-bibliotek
  kopplas in vid byggtid, och ett plugin laddas efteråt. Använd värdens CSS-klasser,
  eller lägg CSS och JS inline i komponenten.
* **Plugins kör med full behörighet i appens process.** Det finns ingen sandlåda utöver
  att `IServerPluginFiles` håller sig innanför plugins-katalogen — ett plugin kan i
  övrigt göra allt appen kan. Lägg bara in kod du litar på.
