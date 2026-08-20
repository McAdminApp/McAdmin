namespace McAdminPlugins;

/// <summary>Which of the host's four notice colours to use.</summary>
public enum PluginNoticeKind
{
    Info,
    Good,
    Warning,
    Error
}

/// <summary>How a button looks. The host owns the actual styling.</summary>
public enum PluginButtonStyle
{
    Default,
    Primary,
    Danger,
    Link
}

/// <summary>How a table cell is drawn.</summary>
public enum PluginCellStyle
{
    Normal,

    /// <summary>Bold, for the column that names the row.</summary>
    Strong,

    /// <summary>Monospace and dimmed, for ids, paths and other machine text.</summary>
    Mono,

    Muted,

    /// <summary>Drawn as a small pill.</summary>
    Tag
}

/// <summary>
/// One block on a plugin page. The host renders every section with the same markup its
/// own pages use, so a plugin describes what belongs on the page and never what it
/// looks like.
///
/// Sections load their own data, which is why the callbacks live here and not on
/// <see cref="PluginPage"/>: reloading after a save touches one section, not the page.
/// </summary>
public abstract record PluginSection
{
    /// <summary>Heading above the section. Null renders the section without a header.</summary>
    public string? Title { get; init; }

    /// <summary>A sentence under the heading.</summary>
    public string? Description { get; init; }
}

/// <summary>A coloured banner. Static text — for feedback on an action, return a
/// <see cref="PluginResult"/> instead and let the host place the notice.</summary>
public sealed record PluginNoticeSection : PluginSection
{
    public PluginNoticeKind Kind { get; init; } = PluginNoticeKind.Info;

    /// <summary>Leading text in bold, as in "No backups yet."</summary>
    public string? Heading { get; init; }

    public required string Text { get; init; }
}

/// <summary>
/// Prose and, optionally, a readout of label/value pairs — the panel the console page
/// uses for uptime, player count and the like.
/// </summary>
public sealed record PluginTextSection : PluginSection
{
    /// <summary>Rendered one paragraph per entry. Plain text; markup is escaped.</summary>
    public IReadOnlyList<string> Paragraphs { get; init; } = [];

    /// <summary>Label/value pairs drawn as a readout grid above the paragraphs.</summary>
    public IReadOnlyList<PluginFact> Facts { get; init; } = [];
}

/// <summary>One cell of a <see cref="PluginTextSection"/> readout.</summary>
public sealed record PluginFact(string Label, string Value);

/// <summary>
/// The settings table, the same one server.properties gets: grouped rows, a filter box,
/// per-row Undo, and a command bar that counts unsaved changes. Nothing is written
/// until the user saves, and only changed keys are handed over.
/// </summary>
public sealed record PluginSettingsSection : PluginSection
{
    /// <summary>
    /// Called when the page opens and again after every successful save. Return the
    /// current values; the host diffs edits against <see cref="PluginField.Value"/>.
    /// </summary>
    public required Func<CancellationToken, Task<IReadOnlyList<PluginField>>> LoadAsync { get; init; }

    /// <summary>
    /// Gets only the keys the user changed. Throwing reports the message as an error
    /// and leaves the edits in place so nothing is lost.
    /// </summary>
    public required Func<IReadOnlyDictionary<string, string>, CancellationToken, Task<PluginResult>> SaveAsync { get; init; }

    /// <summary>Draws the filter box in the section header. Turn off for a handful of rows.</summary>
    public bool Filterable { get; init; } = true;

    public string SaveLabel { get; init; } = "Save changes";

    /// <summary>Shown in place of the table when <see cref="LoadAsync"/> returns nothing.</summary>
    public string EmptyText { get; init; } = "Nothing to configure here.";
}

/// <summary>A read-only table with optional per-row buttons.</summary>
public sealed record PluginTableSection : PluginSection
{
    /// <summary>
    /// Column headers, left to right. Every row must have exactly this many cells;
    /// missing cells render as an em dash rather than throwing.
    /// </summary>
    public required IReadOnlyList<PluginColumn> Columns { get; init; }

    /// <summary>Called when the page opens and again after any row action succeeds.</summary>
    public required Func<CancellationToken, Task<IReadOnlyList<PluginRow>>> LoadAsync { get; init; }

    /// <summary>Buttons drawn in a trailing column. Empty leaves the column out.</summary>
    public IReadOnlyList<PluginRowAction> RowActions { get; init; } = [];

    public bool Filterable { get; init; } = true;

    public string EmptyTitle { get; init; } = "Nothing here yet";

    public string? EmptyText { get; init; }
}

/// <param name="Header">Column heading. Empty string for the actions column.</param>
public sealed record PluginColumn(string Header, PluginCellStyle Style = PluginCellStyle.Normal);

/// <summary>
/// One row. <paramref name="Cells"/> lines up with the section's columns.
/// </summary>
public sealed record PluginRow(params string?[] Cells)
{
    /// <summary>
    /// Passed back to row actions so a handler knows which row was clicked without
    /// having to parse a cell. Falls back to the first cell when unset.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>Small print under the first cell, for state like "Pending removal".</summary>
    public string? Note { get; init; }

    /// <summary>Highlights the row the way an unsaved change is highlighted.</summary>
    public bool Highlight { get; init; }
}

/// <summary>A button on every row of a table.</summary>
public sealed record PluginRowAction(
    string Label,
    Func<PluginRow, CancellationToken, Task<PluginResult>> InvokeAsync)
{
    public PluginButtonStyle Style { get; init; } = PluginButtonStyle.Default;

    /// <summary>Asks the user to confirm with this question before the handler runs.</summary>
    public string? Confirm { get; init; }

    /// <summary>Hides the button on rows it makes no sense for.</summary>
    public Func<PluginRow, bool>? IsVisible { get; init; }
}

/// <summary>
/// A small form: some fields and one submit button. Fields are cleared once the handler
/// reports success, which is what makes this the right shape for "add something".
/// </summary>
public sealed record PluginFormSection : PluginSection
{
    public required IReadOnlyList<PluginField> Fields { get; init; }

    /// <summary>Gets every field keyed by <see cref="PluginField.Key"/>, edited or not.</summary>
    public required Func<IReadOnlyDictionary<string, string>, CancellationToken, Task<PluginResult>> SubmitAsync { get; init; }

    public string SubmitLabel { get; init; } = "Save";

    /// <summary>Keeps what was typed after a success. Off by default.</summary>
    public bool KeepValues { get; init; }
}

/// <summary>A row of buttons that do something and report back.</summary>
public sealed record PluginActionsSection : PluginSection
{
    public required IReadOnlyList<PluginAction> Actions { get; init; }
}

/// <inheritdoc cref="PluginActionsSection"/>
public sealed record PluginAction(string Label, Func<CancellationToken, Task<PluginResult>> InvokeAsync)
{
    public PluginButtonStyle Style { get; init; } = PluginButtonStyle.Default;

    /// <inheritdoc cref="PluginRowAction.Confirm"/>
    public string? Confirm { get; init; }

    /// <summary>Explains what the button does, next to it.</summary>
    public string? Description { get; init; }
}
