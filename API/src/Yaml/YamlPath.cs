using System.Text;

namespace McAdminPlugins.Yaml;

/// <summary>One step of a <see cref="YamlPath"/>: a mapping key, or an index into a list.</summary>
public readonly struct YamlPathSegment : IEquatable<YamlPathSegment>
{
    private YamlPathSegment(string? key, int index)
    {
        Key = key;
        Index = index;
    }

    /// <summary>The key this step looks up, or null when the step is a list index.</summary>
    public string? Key { get; }

    /// <summary>The list position this step looks up. Only meaningful when <see cref="IsIndex"/>.</summary>
    public int Index { get; }

    public bool IsIndex => Key is null;

    public static YamlPathSegment ForKey(string key) =>
        new(key ?? throw new ArgumentNullException(nameof(key)), -1);

    public static YamlPathSegment ForIndex(int index) => index >= 0
        ? new YamlPathSegment(null, index)
        : throw new ArgumentOutOfRangeException(nameof(index), "A list index cannot be negative.");

    public override string ToString() => IsIndex ? $"[{Index}]" : Key!;

    public bool Equals(YamlPathSegment other) =>
        Index == other.Index && string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is YamlPathSegment other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Key, Index);
}

/// <summary>
/// Where a value sits in a document: <c>"worlds.world.spawn"</c>, the same dotted form
/// Bukkit's own configuration API uses, with <c>[0]</c> to step into a list —
/// <c>"kits.tools[1]"</c>.
///
/// A string converts on its own, so paths are written inline:
/// <c>document.GetString("commands.disabled[0]")</c>. Keys that contain a dot cannot be
/// written that way; build those with <see cref="Of"/>, which takes the segments as they
/// are: <c>YamlPath.Of("permissions", "essentials.fly")</c>.
/// </summary>
public readonly struct YamlPath : IEquatable<YamlPath>
{
    private readonly YamlPathSegment[]? _segments;

    private YamlPath(YamlPathSegment[] segments) => _segments = segments;

    /// <summary>The document itself — the mapping at the top of the file.</summary>
    public static YamlPath Empty => default;

    public IReadOnlyList<YamlPathSegment> Segments => _segments ?? [];

    public int Count => _segments?.Length ?? 0;

    public bool IsEmpty => Count == 0;

    public YamlPathSegment this[int index] => Segments[index];

    /// <summary>The last step. Throws when the path is empty.</summary>
    public YamlPathSegment Last => Count > 0
        ? _segments![^1]
        : throw new InvalidOperationException("An empty path has no last segment.");

    /// <summary>Everything but the last step. Empty for a one-step path.</summary>
    public YamlPath Parent => Count <= 1 ? Empty : new YamlPath(_segments![..^1]);

    /// <summary>Takes the segments literally, dots and all.</summary>
    public static YamlPath Of(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return new YamlPath([.. segments.Select(YamlPathSegment.ForKey)]);
    }

    /// <summary>Splits the dotted form. <c>""</c> is <see cref="Empty"/>.</summary>
    public static YamlPath Parse(string path)
    {
        if (string.IsNullOrEmpty(path)) return Empty;

        var segments = new List<YamlPathSegment>();

        foreach (var part in path.Split('.'))
        {
            var key = part;
            var indices = new List<int>();

            while (key.Length > 1 && key[^1] == ']')
            {
                var open = key.LastIndexOf('[');
                if (open < 0) break;

                if (!int.TryParse(key[(open + 1)..^1], out var index) || index < 0)
                    throw new ArgumentException(
                        $"'{part}' is not a valid step in a path; [n] takes a whole number from 0.", nameof(path));

                indices.Insert(0, index);
                key = key[..open];
            }

            if (key.Length > 0) segments.Add(YamlPathSegment.ForKey(key));
            else if (indices.Count == 0)
                throw new ArgumentException($"'{path}' has an empty step.", nameof(path));

            segments.AddRange(indices.Select(YamlPathSegment.ForIndex));
        }

        return new YamlPath([.. segments]);
    }

    public YamlPath Append(string key) =>
        new([.. Segments, YamlPathSegment.ForKey(key)]);

    public YamlPath Append(int index) =>
        new([.. Segments, YamlPathSegment.ForIndex(index)]);

    /// <summary>The first <paramref name="count"/> steps, for pointing at where a walk stopped.</summary>
    public YamlPath Take(int count) => count <= 0
        ? Empty
        : new YamlPath(_segments![..Math.Min(count, Count)]);

    public static implicit operator YamlPath(string path) => Parse(path);

    public override string ToString()
    {
        if (IsEmpty) return "";

        var text = new StringBuilder();

        foreach (var segment in _segments!)
        {
            if (segment.IsIndex) text.Append('[').Append(segment.Index).Append(']');
            else text.Append(text.Length > 0 ? "." : "").Append(segment.Key);
        }

        return text.ToString();
    }

    public bool Equals(YamlPath other) => Segments.SequenceEqual(other.Segments);

    public override bool Equals(object? obj) => obj is YamlPath other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in Segments) hash.Add(segment);
        return hash.ToHashCode();
    }

    public static bool operator ==(YamlPath left, YamlPath right) => left.Equals(right);

    public static bool operator !=(YamlPath left, YamlPath right) => !left.Equals(right);
}
