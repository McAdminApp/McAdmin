using System.Collections;

namespace McAdminPlugins.Yaml;

/// <summary>How a scalar was written down. Kept so a value can be put back the way it was found.</summary>
public enum YamlScalarStyle
{
    /// <summary><c>motd: Welcome</c> — unquoted, and therefore typed: <c>true</c> is a boolean.</summary>
    Plain,

    /// <summary><c>motd: 'Welcome'</c>. Always a string.</summary>
    SingleQuoted,

    /// <summary><c>motd: "Welcome\n"</c>. Always a string, and understands backslash escapes.</summary>
    DoubleQuoted,

    /// <summary>A <c>|</c> block. Line breaks are kept exactly.</summary>
    Literal,

    /// <summary>A <c>&gt;</c> block. Line breaks fold into spaces.</summary>
    Folded
}

/// <summary>
/// A value in a document: a <see cref="YamlScalar"/>, a <see cref="YamlMapping"/> or a
/// <see cref="YamlSequence"/>. Every node knows the lines it came from, which is how
/// <see cref="YamlDocument"/> can change one value and leave the rest of the file — the
/// comments above all — exactly as it was.
/// </summary>
public abstract class YamlNode
{
    /// <summary>0-based index of the first line the node occupies.</summary>
    internal int StartLine;

    /// <summary>0-based index of the last line the node occupies, inclusive.</summary>
    internal int EndLine;

    /// <summary>Column the node's own content starts at.</summary>
    internal int Indent;

    /// <summary>Column the raw text of the value starts at, on <see cref="StartLine"/>.</summary>
    internal int ValueColumn;

    /// <summary>Column just past the raw text, so a trailing comment survives a rewrite.</summary>
    internal int ValueEndColumn;

    /// <summary>True for <c>[a, b]</c> and <c>{a: 1}</c>, which live on one line.</summary>
    internal bool IsFlow;

    /// <summary>1-based line the node starts on. 0 for a node that was never in a file.</summary>
    public int Line => StartLine + 1;

    public YamlScalar? AsScalar() => this as YamlScalar;

    public YamlMapping? AsMapping() => this as YamlMapping;

    public YamlSequence? AsSequence() => this as YamlSequence;
}

/// <summary>
/// A single value. <see cref="Value"/> is the text with quoting and escapes already
/// undone, or null for <c>~</c>, <c>null</c> and a key with nothing after the colon.
///
/// The typed readers are deliberately forgiving: <c>'false'</c> in quotes is a string as
/// far as YAML is concerned, but an admin who wrote it meant the toggle to be off, so
/// <see cref="AsBool"/> reads it as one.
/// </summary>
public sealed class YamlScalar : YamlNode
{
    internal YamlScalar(string? value, YamlScalarStyle style)
    {
        Value = value;
        Style = style;
    }

    /// <summary>The block header the scalar was written with — <c>|</c>, <c>|-</c>, <c>&gt;</c>.</summary>
    internal string? BlockHeader;

    public string? Value { get; }

    public YamlScalarStyle Style { get; }

    public bool IsNull => Value is null;

    /// <summary>The value as a boolean, or null when it does not read as one.</summary>
    public bool? AsBool() => Value is not null && YamlScalarText.TryReadBool(Value, out var value) ? value : null;

    /// <summary>The value as a whole number. Understands <c>0x</c>, <c>0b</c>, leading-zero octal and <c>_</c>.</summary>
    public long? AsLong() => Value is not null && YamlScalarText.TryReadLong(Value, out var value) ? value : null;

    /// <inheritdoc cref="AsLong"/>
    public int? AsInt() => AsLong() is { } value and >= int.MinValue and <= int.MaxValue ? (int)value : null;

    /// <summary>The value as a number. Understands <c>.inf</c> and <c>.nan</c>.</summary>
    public double? AsDouble() => Value is not null && YamlScalarText.TryReadDouble(Value, out var value) ? value : null;

    public override string ToString() => Value ?? "";
}

/// <summary>
/// A block of <c>key: value</c> entries, in the order the file has them. A repeated key
/// keeps the last value, the way SnakeYAML — and therefore the Minecraft server — reads it.
/// </summary>
public sealed class YamlMapping : YamlNode, IReadOnlyCollection<KeyValuePair<string, YamlNode>>
{
    internal readonly record struct Entry(string Key, YamlNode Node, int KeyLine);

    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);

    internal IReadOnlyList<Entry> Entries => _entries;

    internal void Add(string key, YamlNode node, int keyLine)
    {
        if (_positions.TryGetValue(key, out var at))
        {
            _entries[at] = new Entry(key, node, keyLine);
            return;
        }

        _positions[key] = _entries.Count;
        _entries.Add(new Entry(key, node, keyLine));
    }

    internal Entry? FindEntry(string key) =>
        _positions.TryGetValue(key, out var at) ? _entries[at] : null;

    public int Count => _entries.Count;

    /// <summary>The keys, in file order.</summary>
    public IReadOnlyList<string> Keys => [.. _entries.Select(entry => entry.Key)];

    public bool ContainsKey(string key) => _positions.ContainsKey(key);

    public YamlNode? this[string key] => _positions.TryGetValue(key, out var at) ? _entries[at].Node : null;

    public bool TryGetValue(string key, out YamlNode node)
    {
        if (_positions.TryGetValue(key, out var at))
        {
            node = _entries[at].Node;
            return true;
        }

        node = null!;
        return false;
    }

    public IEnumerator<KeyValuePair<string, YamlNode>> GetEnumerator() =>
        _entries.Select(entry => new KeyValuePair<string, YamlNode>(entry.Key, entry.Node)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A list, written either as <c>- item</c> lines or inline as <c>[a, b]</c>.</summary>
public sealed class YamlSequence : YamlNode, IReadOnlyList<YamlNode>
{
    private readonly List<YamlNode> _items = [];

    internal void Add(YamlNode item) => _items.Add(item);

    public int Count => _items.Count;

    public YamlNode this[int index] => _items[index];

    /// <summary>The plain values. Nulls and nested lists or mappings are left out.</summary>
    public IReadOnlyList<string> ToStringList() =>
        [.. _items.OfType<YamlScalar>().Select(item => item.Value).OfType<string>()];

    public IEnumerator<YamlNode> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
