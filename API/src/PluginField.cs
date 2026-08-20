namespace McAdminPlugins;

/// <summary>How a value is edited. Picks the control the host draws for a field.</summary>
public enum PluginFieldKind
{
    Text,
    Number,
    Toggle,
    Choice,
    Password,
    LongText
}

/// <summary>
/// One editable value, in a settings table or a form. Values are strings in both
/// directions: the host has no idea what a plugin's config format wants, and a string
/// round-trips through .properties, YAML and JSON alike without being reinterpreted.
///
/// <see cref="Value"/> is what is stored right now — the host tracks edits against it
/// and only hands back the keys that actually differ.
/// </summary>
/// <param name="Key">Identifies the field in the dictionary handed to the save handler.</param>
/// <param name="Label">Shown to the user. The key is shown underneath in a monospace font.</param>
public sealed record PluginField(string Key, string Label)
{
    public PluginFieldKind Kind { get; init; } = PluginFieldKind.Text;

    public string? Value { get; init; }

    /// <summary>One line of help under the label. Keep it to a sentence.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Settings tables are grouped under a small heading row in this order. Fields
    /// sharing a group must be adjacent in the list; the host does not reorder them.
    /// </summary>
    public string Group { get; init; } = "General";

    /// <summary>The options for <see cref="PluginFieldKind.Choice"/>. Ignored otherwise.</summary>
    public IReadOnlyList<string>? Choices { get; init; }

    /// <summary>Bounds for <see cref="PluginFieldKind.Number"/>. Ignored otherwise.</summary>
    public int? Minimum { get; init; }

    /// <inheritdoc cref="Minimum"/>
    public int? Maximum { get; init; }

    /// <summary>Placeholder text for the empty control, useful on forms.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Blocks submitting a form while the field is blank. Forms only.</summary>
    public bool Required { get; init; }

    /// <summary>Tags a changed row with "Restart to apply". Settings tables only.</summary>
    public bool RequiresRestart { get; init; }

    /// <summary>Draws the key in the settings table. Off for forms, where it is noise.</summary>
    public bool ShowKey { get; init; } = true;
}
