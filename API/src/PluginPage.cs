using System.Text.RegularExpressions;

namespace McAdminPlugins;

/// <summary>
/// A whole page, described rather than drawn. The host owns the route, the heading, the
/// sidebar entry and every pixel of the markup; the plugin says which sections belong on
/// the page and where their data comes from.
///
/// This is the alternative to shipping Razor components. A plugin that needs markup the
/// section vocabulary cannot express is still free to bring its own components and
/// register them through <see cref="IPluginNavigation"/> — the two live side by side.
/// </summary>
public sealed partial record PluginPage
{
    /// <summary>Every described page is routed under here, as "/addon/{slug}".</summary>
    public const string RoutePrefix = "/addon/";

    /// <param name="slug">
    /// The last part of the URL: lowercase letters, digits and hyphens. Unique across all
    /// plugins — the host keeps the first page to claim a slug and reports the rest as a
    /// load failure.
    /// </param>
    /// <param name="title">The page heading, and the sidebar label unless
    /// <see cref="NavigationText"/> says otherwise.</param>
    public PluginPage(string slug, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (!SlugPattern().IsMatch(slug))
            throw new ArgumentException(
                $"'{slug}' is not a usable slug. Use lowercase letters, digits and hyphens, as in 'world-backups'.",
                nameof(slug));

        Slug = slug;
        Title = title;
    }

    public string Slug { get; }

    public string Title { get; }

    /// <summary>The small label above the heading. Defaults to the plugin's name.</summary>
    public string? Eyebrow { get; init; }

    /// <summary>The paragraph under the heading. Say what the page writes to, and when.</summary>
    public string? Description { get; init; }

    /// <summary>The sections, top to bottom. Ignored when <see cref="BuildAsync"/> is set.</summary>
    public IReadOnlyList<PluginSection> Sections { get; init; } = [];

    /// <summary>
    /// Builds the section list on every visit, for pages whose shape depends on data —
    /// one section per config file found, say. Sections load their own contents, so this
    /// is only needed when the list itself varies.
    /// </summary>
    public Func<CancellationToken, Task<IReadOnlyList<PluginSection>>>? BuildAsync { get; init; }

    /// <inheritdoc cref="PluginNavItem.Glyph"/>
    public string? Glyph { get; init; }

    /// <inheritdoc cref="PluginNavItem.AdministratorOnly"/>
    public bool AdministratorOnly { get; init; }

    /// <inheritdoc cref="PluginNavItem.Order"/>
    public int Order { get; init; }

    /// <summary>Sidebar label, when the page title is too long for it.</summary>
    public string? NavigationText { get; init; }

    /// <summary>
    /// Set false for a page that is reachable but not listed — a detail page another
    /// section links to, for instance.
    /// </summary>
    public bool ShowInNavigation { get; init; } = true;

    /// <summary>Where the page lives. Link to it from other pages with this.</summary>
    public string Href => RoutePrefix + Slug;

    /// <summary>The host calls this when rendering. Plugins have no reason to.</summary>
    public Task<IReadOnlyList<PluginSection>> GetSectionsAsync(CancellationToken ct = default) =>
        BuildAsync?.Invoke(ct) ?? Task.FromResult(Sections);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
