namespace McServerMgmnt.Services;

/// <summary>How a setting is edited. Drives which control the settings table draws.</summary>
public enum SettingKind
{
    Text,
    Number,
    Toggle,
    Choice
}

/// <summary>
/// One line of server.properties, plus what the UI needs to render it sensibly.
/// Values are kept as strings so they round-trip to the properties file unchanged.
/// </summary>
public record ServerSetting(
    string Key,
    string Label,
    string Description,
    SettingKind Kind,
    string? Value,
    string Group = "General",
    IReadOnlyList<string>? Choices = null,
    int? Minimum = null,
    int? Maximum = null,
    bool RequiresRestart = false);

public interface IServerSettingsStore
{
    /// <summary>True once a real properties file is wired up. The page shows a banner while this is false.</summary>
    bool IsConnected { get; }

    Task<IReadOnlyList<ServerSetting>> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>Writes only the settings the user actually changed, keyed by properties key.</summary>
    Task SaveAsync(IReadOnlyDictionary<string, string> changes, CancellationToken ct = default);
}