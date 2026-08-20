namespace McAdminPlugins;

/// <summary>
/// What a handler wants the host to tell the user. The host turns it into the same
/// green or red notice its own pages use, so a plugin never renders feedback itself.
///
/// A handler that throws is caught and reported as a failure with the exception
/// message, which means the lazy path — just throw — is also the correct one.
/// </summary>
/// <param name="Ok">False draws the message as an error and keeps pending edits.</param>
/// <param name="Message">Shown above the section. Null shows nothing at all.</param>
public sealed record PluginResult(bool Ok, string? Message = null)
{
    /// <summary>Nothing to say. The section reloads and no notice appears.</summary>
    public static PluginResult None { get; } = new(true);

    public static PluginResult Success(string? message = null) => new(true, message);

    public static PluginResult Failure(string message) => new(false, message);
}
