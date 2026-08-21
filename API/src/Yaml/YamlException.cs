namespace McAdminPlugins.Yaml;

/// <summary>
/// Thrown when a file cannot be read as the YAML subset this parser understands.
/// <see cref="Line"/> is 1-based and points at the line that could not be made sense
/// of — which is the part worth putting in front of whoever has to fix the file.
/// </summary>
public sealed class YamlException(string problem, int line = 0, int column = 0)
    : Exception(Describe(problem, line, column))
{
    /// <summary>1-based line the parser gave up on, or 0 when the problem has no line.</summary>
    public int Line { get; } = line;

    /// <summary>1-based column, or 0 when only the line is known.</summary>
    public int Column { get; } = column;

    private static string Describe(string problem, int line, int column) => line switch
    {
        <= 0 => problem,
        _ when column > 0 => $"{problem} (line {line}, column {column})",
        _ => $"{problem} (line {line})"
    };
}
