using McAdminPlugins;
using McServerMgmnt.Components;
using McServerMgmnt.Data;
using McServerMgmnt.Services;
using McServerMgmnt.Services.Factories;
using McServerMgmnt.Services.Plugins;
using McServerMgmnt.Services.Rcon;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Account storage: a single SQLite file next to the app.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("AccountsDb")
                      ?? "Data Source=mcservermgmnt.db"));

builder.Services.AddScoped<UserService>();

// Server management.
builder.Services.Configure<RconOptions>(builder.Configuration.GetSection(RconOptions.SectionName));
builder.Services.AddSingleton<IServerSettingsStore, ServerSettingsStore>();
builder.Services.AddSingleton<IServerWhitelist, ServerWhitelistStore>();
builder.Services.AddSingleton<ServerLogReader>();

// RCON needs a host and a password before it can drive anything. Without them the
// placeholder stays in place, so a checkout with no server behind it still runs and the
// console page says as much instead of failing to connect on every render.
builder.Services.AddSingleton<IMinecraftServerController>(sp =>
    sp.GetRequiredService<IOptions<RconOptions>>().Value.IsConfigured
        ? ActivatorUtilities.CreateInstance<RconServerController>(sp)
        : new PlaceholderServerController());

// Plugins. The registry is what the router and the sidebar read from; the file store
// is the only way a plugin reaches the Minecraft server's plugins folder.
builder.Services.Configure<PluginOptions>(builder.Configuration.GetSection(PluginOptions.SectionName));
builder.Services.AddSingleton<PluginRegistry>();
builder.Services.AddSingleton<IPluginNavigation>(sp => sp.GetRequiredService<PluginRegistry>());
builder.Services.AddSingleton<IServerPluginFiles, ServerPluginFiles>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "McServerMgmnt.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.LoginPath = "/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);

// Before the first request: the router needs the plugin assemblies to route into them.
await PluginLoader.LoadAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Sign out is a POST so a stray link cannot end someone's session.
app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context);
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization();

app.MapStaticAssets();

// Two registrations are needed for a plugin page to be reachable, and they cover
// different halves of the request. AddAdditionalAssemblies creates the endpoints that
// answer the first, server-rendered hit on the URL; the Router in Routes.razor gets the
// same list so navigating there from inside a live circuit resolves too. With only one
// of them a plugin page either 404s or dead-ends on client-side navigation.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(app.Services.GetRequiredService<PluginRegistry>().RouteAssemblies.ToArray());

app.Run();
