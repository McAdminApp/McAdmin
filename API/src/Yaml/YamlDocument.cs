using System.Globalization;

namespace McAdminPlugins.Yaml;

/// <summary>
/// A YAML file, kept as the lines it was read from. Reading walks a parsed tree; writing
/// changes the lines the value sits on and leaves every other byte of the file alone.
///
/// That last part is the whole point. A Minecraft plugin's <c>config.yml</c> is mostly
/// comments — EssentialsX ships around a thousand lines of them — and a round trip
/// through an ordinary YAML library throws all of it away. Here, saving one field
/// rewrites one line:
///
/// <code>
/// var config = await files.ReadYamlAsync("EssentialsX/config.yml", ct);
///
/// config.Set("motd", "Welcome!");     // quoting, key order and comments all survive
/// config.Set("god-mode", true);       // written as 'yes' when the file said 'no'
///
/// await files.WriteYamlAsync("EssentialsX/config.yml", config, ct);
/// </code>
///
/// Values are looked up by path, in the dotted form Bukkit itself uses — see
/// <see cref="YamlPath"/>. Reading is forgiving and writing is careful: a quoted
/// <c>'false'</c> still reads as a boolean, while a value written back keeps the quoting
/// style it was found with.
///
/// A document is not thread-safe, and is meant to be read, changed and written inside
/// one handler rather than held onto.
/// </summary>
public sealed class YamlDocument
{
    private readonly List<string> _lines;
    private readonly string _newline;
    private readonly bool _finalNewline;

    private YamlMapping _root = null!;
    private int _indentStep = 2;

    private YamlDocument(List<string> lines, string newline, bool finalNewline)
    {
        _lines = lines;
        _newline = newline;
        _finalNewline = finalNewline;

        Reparse();
    }

    /// <summary>
    /// Reads a file. Throws <see cref="YamlException"/> — carrying the line — when the
    /// text is not YAML this parser can make sense of.
    /// </summary>
    public static YamlDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<string>();
        var finalNewline = text.Length == 0;

        if (text.Length > 0)
        {
            lines.AddRange(text.Split('\n').Select(line => line.EndsWith('\r') ? line[..^1] : line));

            if (lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
                finalNewline = true;
            }
        }

        return new YamlDocument(lines, text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", finalNewline);
    }

    /// <summary>An empty document, for writing a config file that is not there yet.</summary>
    public static YamlDocument Create() => new([], "\n", true);

    /// <summary>The mapping at the top of the file.</summary>
    public YamlMapping Root => _root;

    public bool IsEmpty => _root.Count == 0;

    /// <summary>How many spaces one level of nesting is, taken from the file itself.</summary>
    public int IndentStep => _indentStep;

    /// <summary>The file as it now stands, ready to be written back.</summary>
    public override string ToString() =>
        _lines.Count == 0 ? "" : string.Join(_newline, _lines) + (_finalNewline ? _newline : "");

    // ---- reading ----------------------------------------------------------------

    /// <summary>The node at <paramref name="path"/>, or null when nothing is there.</summary>
    public YamlNode? Find(YamlPath path)
    {
        YamlNode? node = _root;

        foreach (var segment in path.Segments)
        {
            node = Step(node, segment);
            if (node is null) return null;
        }

        return node;
    }

    public bool Contains(YamlPath path) => Find(path) is not null;

    /// <summary>The value as text, or null when the key is missing or explicitly null.</summary>
    public string? GetString(YamlPath path) => (Find(path) as YamlScalar)?.Value;

    public string GetString(YamlPath path, string fallback) => GetString(path) ?? fallback;

    /// <summary>Reads <c>true</c>, <c>yes</c> and <c>on</c> — quoted or not — as true.</summary>
    public bool GetBool(YamlPath path, bool fallback = false) => (Find(path) as YamlScalar)?.AsBool() ?? fallback;

    public int GetInt(YamlPath path, int fallback = 0) => (Find(path) as YamlScalar)?.AsInt() ?? fallback;

    public long GetLong(YamlPath path, long fallback = 0) => (Find(path) as YamlScalar)?.AsLong() ?? fallback;

    public double GetDouble(YamlPath path, double fallback = 0) => (Find(path) as YamlScalar)?.AsDouble() ?? fallback;

    /// <summary>The list at <paramref name="path"/>. Empty when there is no list there.</summary>
    public IReadOnlyList<string> GetStringList(YamlPath path) => (Find(path) as YamlSequence)?.ToStringList() ?? [];

    /// <summary>The block of entries at <paramref name="path"/>, or null when it is not one.</summary>
    public YamlMapping? GetSection(YamlPath path) => Find(path) as YamlMapping;

    /// <summary>The keys directly under <paramref name="path"/>, in file order. Empty path means the top of the file.</summary>
    public IReadOnlyList<string> GetKeys(YamlPath path) => GetSection(path)?.Keys ?? [];

    // ---- writing ----------------------------------------------------------------

    /// <summary>
    /// Writes <paramref name="value"/> as it stands, which is what a settings page wants:
    /// the field handed it a string and the file gets that string. Existing quoting is
    /// kept, and a key the file spells as <c>no</c> gets <c>yes</c> rather than
    /// <c>true</c>. Null clears the value, leaving the key behind.
    ///
    /// Use <see cref="SetString"/> when the value has to come back as text even if it
    /// reads like a number or a boolean.
    /// </summary>
    public void Set(YamlPath path, string? value)
    {
        var existing = Find(path) as YamlScalar;

        var text = value is not null && SpellsBool(existing, out var sample) && YamlScalarText.TryReadBool(value, out var wanted)
            ? YamlScalarText.SpellBool(sample, wanted)
            : value;

        Write(path, text, existing?.Style ?? YamlScalarStyle.Plain);
    }

    /// <summary>Writes text that has to stay text — <c>'yes'</c> and <c>'25565'</c> get their quotes.</summary>
    public void SetString(YamlPath path, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var style = (Find(path) as YamlScalar)?.Style ?? YamlScalarStyle.Plain;

        Write(path, value, style is YamlScalarStyle.Plain ? StyleForString(value) : style);
    }

    /// <summary>Writes a boolean in whichever spelling the file already uses.</summary>
    public void Set(YamlPath path, bool value) =>
        Write(path,
            SpellsBool(Find(path) as YamlScalar, out var sample)
                ? YamlScalarText.SpellBool(sample, value)
                : value ? "true" : "false",
            YamlScalarStyle.Plain);

    public void Set(YamlPath path, long value) =>
        Write(path, value.ToString(CultureInfo.InvariantCulture), YamlScalarStyle.Plain);

    public void Set(YamlPath path, double value) =>
        Write(path, value switch
        {
            double.PositiveInfinity => ".inf",
            double.NegativeInfinity => "-.inf",
            _ when double.IsNaN(value) => ".nan",
            _ => value.ToString("R", CultureInfo.InvariantCulture)
        }, YamlScalarStyle.Plain);

    /// <summary>
    /// Writes a list. An empty one becomes <c>key: []</c>; otherwise the entries are
    /// written the way the file already writes them — inline if they were inline, one
    /// <c>- item</c> per line if they were not.
    /// </summary>
    public void SetList(YamlPath path, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (path.IsEmpty || path.Last.IsIndex)
            throw new ArgumentException("A list is written to a key.", nameof(path));

        var items = values.ToArray();

        // Make sure the key is there, so from here on there is a line to work from.
        if (!Contains(path)) Write(path, null, YamlScalarStyle.Plain);

        var parent = path.Count == 1 ? _root : Find(path.Parent) as YamlMapping;

        if (parent?.FindEntry(path.Last.Key!) is not { } entry)
            throw new YamlException($"'{path}' could not be written.");

        var node = entry.Node;
        var keyLine = entry.KeyLine;
        var line = _lines[keyLine];
        var keyIndent = LeadingSpaces(line);
        var keyEnd = YamlParser.FindKeyEnd(line[keyIndent..]);

        if (keyEnd < 0) throw new YamlException($"'{path}' is not a key.", keyLine + 1);

        var head = line[..(keyIndent + keyEnd + 1)];
        var tail = TrailingComment(line, head.Length);
        var wasInline = node is YamlSequence { IsFlow: true };
        var listIndent = node is YamlSequence { IsFlow: false } block && block.StartLine > keyLine
            ? block.Indent
            : keyIndent + _indentStep;

        if (node.EndLine > keyLine) _lines.RemoveRange(keyLine + 1, node.EndLine - keyLine);

        if (items.Length == 0 || wasInline)
        {
            var inline = string.Join(", ", items.Select(EncodeItem));
            _lines[keyLine] = (head + " [" + inline + "]" + tail).TrimEnd();
        }
        else
        {
            _lines[keyLine] = (head + tail).TrimEnd();
            _lines.InsertRange(keyLine + 1, items.Select(item => new string(' ', listIndent) + "- " + EncodeItem(item)));
        }

        Reparse();
    }

    /// <summary>
    /// Takes the key out, along with the comment block sitting directly above it — those
    /// explain the key, and leaving them behind to describe nothing is worse than losing
    /// them. Returns false when there was nothing there.
    /// </summary>
    public bool Remove(YamlPath path)
    {
        if (path.IsEmpty || path.Last.IsIndex) return false;

        var parent = path.Count == 1 ? _root : Find(path.Parent) as YamlMapping;
        if (parent?.FindEntry(path.Last.Key!) is not { } entry) return false;

        if (parent.IsFlow)
            throw new YamlException($"'{path}' sits inside an inline mapping; write the whole value instead.");

        var from = entry.KeyLine;
        var to = Math.Max(entry.Node.EndLine, from);
        var indent = LeadingSpaces(_lines[from]);

        while (from > 0)
        {
            var above = _lines[from - 1];
            var trimmed = above.TrimStart();

            if (trimmed.Length == 0 || trimmed[0] != '#' || LeadingSpaces(above) != indent) break;

            from--;
        }

        _lines.RemoveRange(from, to - from + 1);
        Reparse();

        return true;
    }

    // ---- the machinery ----------------------------------------------------------

    private void Write(YamlPath path, string? text, YamlScalarStyle style)
    {
        if (path.IsEmpty) throw new ArgumentException("A path is needed to write a value.", nameof(path));

        if (text is not null) text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        if (Find(path) is { } node) Replace(node, text, style);
        else Insert(path, text, style);

        Reparse();
    }

    /// <summary>
    /// Puts a new value where the old one was. Everything before the value on the line —
    /// the indent, the key — and everything after it — a trailing comment — is kept as
    /// the characters they already are.
    /// </summary>
    private void Replace(YamlNode node, string? text, YamlScalarStyle style)
    {
        var line = _lines[node.StartLine];
        var head = line[..Math.Min(node.ValueColumn, line.Length)];
        var tail = node.ValueEndColumn < line.Length ? line[node.ValueEndColumn..] : "";
        // "key:" and "-" need a space before the value; "[", "{" and "," already read right.
        var gap = head.Length > 0 && head[^1] is not (' ' or '[' or '{' or ',') ? " " : "";

        if (node.EndLine > node.StartLine)
            _lines.RemoveRange(node.StartLine + 1, node.EndLine - node.StartLine);

        if (text is not null && text.Contains('\n'))
        {
            var (header, body) = BlockLines(text);
            var indent = LeadingSpaces(line) + _indentStep;

            _lines[node.StartLine] = (head + gap + header + tail).TrimEnd();
            _lines.InsertRange(node.StartLine + 1, body.Select(item => Indented(item, indent)));

            return;
        }

        var encoded = YamlScalarText.Encode(text, style);
        _lines[node.StartLine] = (head + (encoded.Length > 0 ? gap + encoded : "") + tail).TrimEnd();
    }

    /// <summary>Adds a key that is not there yet, creating whatever blocks it needs on the way down.</summary>
    private void Insert(YamlPath path, string? text, YamlScalarStyle style)
    {
        YamlNode node = _root;
        var depth = 0;

        while (depth < path.Count && Step(node, path[depth]) is { } next)
        {
            node = next;
            depth++;
        }

        for (var i = depth; i < path.Count; i++)
            if (path[i].IsIndex)
                throw new YamlException($"'{path}' cannot be created; list entries are written with SetList.");

        int at;
        int indent;

        switch (node)
        {
            case YamlMapping { IsFlow: false } mapping:
                at = mapping.EndLine + 1;
                indent = mapping.Indent;
                break;

            // "key:", "key: {}" and "key: []" are all room for a block of entries.
            case YamlScalar { IsNull: true }:
            case YamlMapping { IsFlow: true, Count: 0 }:
            case YamlSequence { IsFlow: true, Count: 0 }:
                Replace(node, null, YamlScalarStyle.Plain);
                at = node.StartLine + 1;
                indent = LeadingSpaces(_lines[node.StartLine]) + _indentStep;
                break;

            default:
                throw new YamlException(node.IsFlow
                    ? $"'{path}' cannot be created; '{path.Take(depth)}' is written inline and cannot take more entries."
                    : $"'{path}' cannot be created; '{path.Take(depth)}' already holds a value.");
        }

        var lines = new List<string>();
        var column = indent;

        for (var i = depth; i < path.Count - 1; i++)
        {
            lines.Add(new string(' ', column) + YamlScalarText.EncodeKey(path[i].Key!) + ":");
            column += _indentStep;
        }

        var key = new string(' ', column) + YamlScalarText.EncodeKey(path.Last.Key!) + ":";

        if (text is not null && text.Contains('\n'))
        {
            var (header, body) = BlockLines(text);

            lines.Add(key + " " + header);
            lines.AddRange(body.Select(item => Indented(item, column + _indentStep)));
        }
        else
        {
            var encoded = YamlScalarText.Encode(text, style);
            lines.Add(encoded.Length > 0 ? key + " " + encoded : key);
        }

        _lines.InsertRange(at, lines);
    }

    private void Reparse()
    {
        _root = YamlParser.Parse(_lines);

        var step = 0;
        Measure(_root);

        _indentStep = step > 0 ? step : 2;

        void Measure(YamlNode node)
        {
            switch (node)
            {
                case YamlMapping mapping:
                    foreach (var entry in mapping.Entries)
                    {
                        // Only a value on a line of its own says anything about indentation.
                        if (entry.Node.StartLine > entry.KeyLine && entry.Node.Indent > mapping.Indent)
                        {
                            var candidate = entry.Node.Indent - mapping.Indent;
                            step = step == 0 ? candidate : Math.Min(step, candidate);
                        }

                        Measure(entry.Node);
                    }

                    break;

                case YamlSequence sequence:
                    foreach (var item in sequence) Measure(item);

                    break;
            }
        }
    }

    private static YamlNode? Step(YamlNode node, YamlPathSegment segment)
    {
        if (segment.IsIndex)
            return node is YamlSequence sequence && segment.Index < sequence.Count ? sequence[segment.Index] : null;

        return node is YamlMapping mapping ? mapping[segment.Key!] : null;
    }

    private static bool SpellsBool(YamlScalar? scalar, out string sample)
    {
        sample = "";

        if (scalar is not { Style: YamlScalarStyle.Plain, Value: { } value }) return false;
        if (!YamlScalarText.TryReadBool(value, out _)) return false;

        sample = value;

        return true;
    }

    private static YamlScalarStyle StyleForString(string value) =>
        YamlScalarText.ResolvesAsNonString(value) ? YamlScalarStyle.SingleQuoted : YamlScalarStyle.Plain;

    private static string EncodeItem(string item) => YamlScalarText.Encode(item, StyleForString(item));

    /// <summary>A multi-line value becomes a literal block, with a header that keeps its trailing newlines.</summary>
    private static (string Header, string[] Body) BlockLines(string text)
    {
        var trailing = 0;
        var body = text;

        while (body.EndsWith('\n'))
        {
            trailing++;
            body = body[..^1];
        }

        var lines = body.Split('\n');

        return trailing switch
        {
            0 => ("|-", lines),
            1 => ("|", lines),
            _ => ("|+", [.. lines, .. Enumerable.Repeat("", trailing - 1)])
        };
    }

    private static string Indented(string line, int indent) => line.Length == 0 ? "" : new string(' ', indent) + line;

    private static int LeadingSpaces(string line)
    {
        var count = 0;

        while (count < line.Length && line[count] == ' ') count++;

        return count;
    }

    /// <summary>The comment at the end of the line, whitespace and all, so a rewrite can put it back.</summary>
    private static string TrailingComment(string line, int from)
    {
        var at = -1;

        for (var i = from; i < line.Length; i++)
        {
            if (line[i] is '\'' or '"')
            {
                var end = YamlScalarText.SkipQuoted(line, i);
                if (end < 0) break;

                i = end - 1;
                continue;
            }

            if (line[i] != '#' || i == 0 || line[i - 1] != ' ') continue;

            at = i;
            break;
        }

        if (at < 0) return "";

        while (at > from && line[at - 1] == ' ') at--;

        return line[at..];
    }
}
