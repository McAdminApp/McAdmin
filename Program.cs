using McServerMgmnt.Components;
using McServerMgmnt.Data;
using McServerMgmnt.Services;
using McServerMgmnt.Services.Factories;
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
builder.Services.AddSingleton<ServerLogReader>();

// RCON needs a host and a password before it can drive anything. Without them the
// placeholder stays in place, so a checkout with no server behind it still runs and the
// console page says as much instead of failing to connect on every render.
builder.Services.AddSingleton<IMinecraftServerController>(sp =>
    sp.GetRequiredService<IOptions<RconOptions>>().Value.IsConfigured
        ? ActivatorUtilities.CreateInstance<RconServerController>(sp)
        : new PlaceholderServerController());

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
