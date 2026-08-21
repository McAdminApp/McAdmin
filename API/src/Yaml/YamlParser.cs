using System.Text;

namespace McAdminPlugins.Yaml;

/// <summary>
/// Reads a file into a tree of <see cref="YamlNode"/>, one line at a time, recording
/// where every value came from so it can be put back without touching anything else.
///
/// It understands the YAML a Minecraft server writes and reads: block mappings, block
/// and inline lists, the four scalar styles, <c>|</c> and <c>&gt;</c> blocks, and
/// comments anywhere. It does not understand anchors, tags, multiple documents or
/// values that run across lines unquoted, and says so rather than guessing.
/// </summary>
internal sealed class YamlParser
{
    private readonly string[] _lines;
    private int _index;

    private YamlParser(string[] lines) => _lines = lines;

    /// <summary>The mapping at the top of the file. Empty when the file holds nothing but comments.</summary>
    internal static YamlMapping Parse(IReadOnlyList<string> lines) => new YamlParser([.. lines]).ParseDocument();

    /// <summary>
    /// Index of the colon that ends the key in <paramref name="text"/>, or -1 when the
    /// text is not a <c>key: value</c> entry. A colon only ends a key when a space or the
    /// end of the line follows it, which is what keeps <c>url: http://x</c> in one piece.
    /// </summary>
    internal static int FindKeyEnd(string text)
    {
        if (text.Length == 0) return -1;

        if (text[0] is '\'' or '"')
        {
            var quoted = YamlScalarText.SkipQuoted(text, 0);
            if (quoted < 0) return -1;

            while (quoted < text.Length && text[quoted] == ' ') quoted++;

            return quoted < text.Length && text[quoted] == ':' ? quoted : -1;
        }

        if (text[0] is '[' or '{' or '#' or '&' or '*') return -1;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '#' && i > 0 && text[i - 1] == ' ') return -1;
            if (text[i] != ':') continue;
            if (i + 1 == text.Length || text[i + 1] == ' ') return i;
        }

        return -1;
    }

    internal static bool IsSequenceEntry(string text) =>
        text.Length > 0 && text[0] == '-' && (text.Length == 1 || text[1] == ' ');

    private YamlMapping ParseDocument()
    {
        SkipToContent();

        if (_index < _lines.Length && _lines[_index].TrimEnd() == "---")
        {
            _index++;
            SkipToContent();
        }

        if (_index >= _lines.Length)
            return new YamlMapping { StartLine = 0, EndLine = _lines.Length - 1, Indent = 0 };

        var node = ParseBlock(IndentOf(_index));

        SkipToContent();

        if (_index < _lines.Length)
            throw new YamlException(
                _lines[_index].TrimStart().StartsWith("---", StringComparison.Ordinal)
                    ? "This file holds more than one YAML document, which is not supported."
                    : "This line does not follow on from the entry above it.",
                _index + 1);

        return node as YamlMapping
               ?? throw new YamlException("The top of the file is not a set of key: value entries.", node.Line);
    }

    /// <summary>Whatever starts on the current line, which must already be a content line.</summary>
    private YamlNode ParseBlock(int indent)
    {
        var rest = _lines[_index][indent..];

        if (IsSequenceEntry(rest)) return ParseSequence(indent);
        if (FindKeyEnd(rest) >= 0) return ParseMapping(indent);

        var node = ParseInline(YamlScalarText.CutComment(rest), _index, indent);
        _index++;

        return node;
    }

    private YamlMapping ParseMapping(int indent)
    {
        var mapping = new YamlMapping { Indent = indent, StartLine = _index, EndLine = _index };

        while (true)
        {
            SkipToContent();
            if (_index >= _lines.Length) break;

            var column = IndentOf(_index);
            if (column < indent) break;

            if (column > indent)
                throw new YamlException(
                    "This line is indented further than the entry above it. A value that runs "
                    + "across several lines has to be quoted or written as a | block.",
                    _index + 1, column + 1);

            var rest = _lines[_index][column..];

            var keyEnd = FindKeyEnd(rest);
            if (keyEnd < 0) break;

            var keyLine = _index;
            var key = YamlScalarText.DecodeKey(rest[..keyEnd].TrimEnd(), keyLine + 1);
            var value = ParseValue(indent, column + keyEnd + 1);

            mapping.Add(key, value, keyLine);
            mapping.EndLine = Math.Max(mapping.EndLine, value.EndLine);
        }

        return mapping;
    }

    /// <summary>What follows a key's colon: the rest of the line, or the block underneath it.</summary>
    private YamlNode ParseValue(int keyIndent, int afterColon)
    {
        var keyLine = _index;
        var line = _lines[keyLine];
        var rest = afterColon < line.Length ? line[afterColon..] : "";
        var trimmed = rest.TrimStart();
        var column = afterColon + (rest.Length - trimmed.Length);

        if (trimmed.Length > 0 && trimmed[0] is '|' or '>') return ParseBlockScalar(keyIndent, keyLine, column);

        if (trimmed.Length > 0 && trimmed[0] != '#')
        {
            var inline = ParseInline(YamlScalarText.CutComment(trimmed), keyLine, column);
            _index++;

            return inline;
        }

        // Nothing but a comment after the colon, so the value is underneath — or absent.
        _index++;
        SkipToContent();

        if (_index < _lines.Length)
        {
            var next = IndentOf(_index);

            if (next > keyIndent) return ParseBlock(next);

            // A list is allowed to sit at the same indent as the key it belongs to.
            if (next == keyIndent && IsSequenceEntry(_lines[_index][next..])) return ParseSequence(next);
        }

        return Missing(keyLine, afterColon);
    }

    private YamlSequence ParseSequence(int indent)
    {
        var sequence = new YamlSequence { Indent = indent, StartLine = _index, EndLine = _index };

        while (true)
        {
            SkipToContent();
            if (_index >= _lines.Length) break;

            var column = IndentOf(_index);
            if (column != indent) break;

            var line = _lines[_index];
            if (!IsSequenceEntry(line[column..])) break;

            var dashLine = _index;
            var content = column + 1;
            while (content < line.Length && line[content] == ' ') content++;

            var tail = content < line.Length ? line[content..] : "";
            YamlNode item;

            if (tail.Length == 0 || tail[0] == '#')
            {
                _index++;
                SkipToContent();

                item = _index < _lines.Length && IndentOf(_index) > indent
                    ? ParseBlock(IndentOf(_index))
                    : Missing(dashLine, column + 1);
            }
            else if (tail[0] is '|' or '>')
            {
                item = ParseBlockScalar(indent, dashLine, content);
            }
            else if (IsSequenceEntry(tail) || FindKeyEnd(tail) >= 0)
            {
                // "- key: value" and "- - nested". Blanking the dash on the parser's own
                // copy of the line lets the ordinary indentation rules take it from here.
                _lines[dashLine] = new string(' ', content) + tail;
                item = ParseBlock(content);
            }
            else
            {
                item = ParseInline(YamlScalarText.CutComment(tail), dashLine, content);
                _index++;
            }

            sequence.Add(item);
            sequence.EndLine = Math.Max(sequence.EndLine, item.EndLine);
        }

        return sequence;
    }

    /// <summary>A <c>|</c> or <c>&gt;</c> block: the header, then every line indented past the key.</summary>
    private YamlScalar ParseBlockScalar(int parentIndent, int headerLine, int column)
    {
        var header = YamlScalarText.CutComment(_lines[headerLine][column..]);
        var folded = header[0] == '>';
        var chomp = '\0';
        var contentIndent = -1;

        foreach (var c in header[1..])
        {
            if (c is '-' or '+') chomp = c;
            else if (c is >= '1' and <= '9') contentIndent = parentIndent + (c - '0');
            else throw new YamlException($"'{header}' is not a block value header.", headerLine + 1, column + 1);
        }

        var body = new List<string>();
        var lastContent = headerLine;
        _index = headerLine + 1;

        while (_index < _lines.Length)
        {
            var raw = _lines[_index];

            if (raw.Trim().Length == 0)
            {
                body.Add("");
                _index++;
                continue;
            }

            var indent = IndentOf(_index);

            if (contentIndent < 0)
            {
                if (indent <= parentIndent) break;
                contentIndent = indent;
            }

            if (indent < contentIndent) break;

            body.Add(raw[contentIndent..]);
            lastContent = _index;
            _index++;
        }

        int endLine;

        if (chomp == '+')
        {
            endLine = _index - 1;
        }
        else
        {
            while (body.Count > 0 && body[^1].Length == 0) body.RemoveAt(body.Count - 1);

            endLine = lastContent;
            _index = lastContent + 1;
        }

        var text = folded ? Fold(body) : string.Join("\n", body);

        text = chomp switch
        {
            '-' => text,
            '+' => text + "\n",
            _ => text.Length > 0 ? text + "\n" : ""
        };

        return new YamlScalar(text, folded ? YamlScalarStyle.Folded : YamlScalarStyle.Literal)
        {
            StartLine = headerLine,
            EndLine = endLine,
            Indent = column,
            ValueColumn = column,
            ValueEndColumn = column + header.Length,
            BlockHeader = header
        };
    }

    /// <summary>Folded blocks join their lines with a space; a blank line is a real break.</summary>
    private static string Fold(IReadOnlyList<string> body)
    {
        var text = new StringBuilder();
        var breaks = 0;

        foreach (var line in body)
        {
            if (line.Length == 0)
            {
                breaks++;
                continue;
            }

            if (text.Length > 0) text.Append(breaks > 0 ? new string('\n', breaks) : " ");

            breaks = 0;
            text.Append(line);
        }

        return text.Append('\n', breaks).ToString();
    }

    /// <summary>One value on one line: <c>[a, b]</c>, <c>{a: 1}</c>, or a scalar.</summary>
    private static YamlNode ParseInline(string raw, int line, int column)
    {
        YamlNode node;

        if (raw.Length > 0 && raw[0] is '[' or '{')
        {
            var reader = new FlowReader(raw, line, column);
            node = reader.ReadNode();
            reader.EnsureNothingFollows();
        }
        else
        {
            node = YamlScalarText.Decode(raw, line + 1);
        }

        node.StartLine = line;
        node.EndLine = line;
        node.Indent = column;
        node.ValueColumn = column;
        node.ValueEndColumn = column + raw.Length;

        return node;
    }

    /// <summary>A key with nothing after it. The empty spot is remembered so a value can be put there.</summary>
    private static YamlScalar Missing(int line, int column) => new(null, YamlScalarStyle.Plain)
    {
        StartLine = line,
        EndLine = line,
        Indent = column,
        ValueColumn = column,
        ValueEndColumn = column
    };

    private int IndentOf(int index)
    {
        var line = _lines[index];
        var indent = 0;

        while (indent < line.Length && line[indent] == ' ') indent++;

        if (indent < line.Length && line[indent] == '\t')
            throw new YamlException("Tabs cannot be used to indent YAML. Use spaces.", index + 1, indent + 1);

        return indent;
    }

    private void SkipToContent()
    {
        while (_index < _lines.Length)
        {
            var trimmed = _lines[_index].TrimStart();
            if (trimmed.Length > 0 && trimmed[0] != '#') return;

            _index++;
        }
    }

    /// <summary>
    /// Reads the inline forms. Every node it builds records where in the line it sits, so
    /// one field of a <c>{x: 0, y: 64}</c> can be rewritten without disturbing the others.
    /// </summary>
    private sealed class FlowReader(string text, int line, int baseColumn)
    {
        private int _at;

        internal YamlNode ReadNode()
        {
            SkipSpace();

            if (_at >= text.Length) throw Problem("a value was expected");

            var start = _at;

            switch (text[_at])
            {
                case '[': return ReadSequence(start);
                case '{': return ReadMapping(start);
                default:
                    var raw = ReadScalarText(stopAtColon: false);

                    return Place(YamlScalarText.Decode(raw, line + 1), start, start + raw.Length);
            }
        }

        internal void EnsureNothingFollows()
        {
            SkipSpace();

            if (_at < text.Length) throw Problem("there is text after the closing bracket");
        }

        private YamlSequence ReadSequence(int start)
        {
            var sequence = new YamlSequence { IsFlow = true };
            _at++;

            while (true)
            {
                SkipSpace();
                if (_at >= text.Length) throw Problem("the list is never closed with ']'");
                if (text[_at] == ']')
                {
                    _at++;
                    break;
                }

                sequence.Add(ReadNode());
                SkipSpace();

                if (_at < text.Length && text[_at] == ',')
                {
                    _at++;
                    continue;
                }

                if (_at < text.Length && text[_at] == ']')
                {
                    _at++;
                    break;
                }

                throw Problem("',' or ']' was expected");
            }

            return Place(sequence, start, _at);
        }

        private YamlMapping ReadMapping(int start)
        {
            var mapping = new YamlMapping { IsFlow = true };
            _at++;

            while (true)
            {
                SkipSpace();
                if (_at >= text.Length) throw Problem("the mapping is never closed with '}'");
                if (text[_at] == '}')
                {
                    _at++;
                    break;
                }

                var key = ReadScalarText(stopAtColon: true);
                SkipSpace();

                if (_at >= text.Length || text[_at] != ':') throw Problem("':' was expected after a key");
                _at++;

                mapping.Add(YamlScalarText.DecodeKey(key, line + 1), ReadNode(), line);
                SkipSpace();

                if (_at < text.Length && text[_at] == ',')
                {
                    _at++;
                    continue;
                }

                if (_at < text.Length && text[_at] == '}')
                {
                    _at++;
                    break;
                }

                throw Problem("',' or '}' was expected");
            }

            return Place(mapping, start, _at);
        }

        private string ReadScalarText(bool stopAtColon)
        {
            SkipSpace();
            var start = _at;

            if (_at < text.Length && text[_at] is '\'' or '"')
            {
                var end = YamlScalarText.SkipQuoted(text, _at);
                if (end < 0) throw Problem("a quoted value is never closed");

                _at = end;

                return text[start..end];
            }

            while (_at < text.Length
                   && text[_at] is not (',' or ']' or '}')
                   && !(stopAtColon && text[_at] == ':')) _at++;

            return text[start.._at].TrimEnd();
        }

        private void SkipSpace()
        {
            while (_at < text.Length && text[_at] == ' ') _at++;
        }

        private T Place<T>(T node, int start, int end) where T : YamlNode
        {
            node.StartLine = line;
            node.EndLine = line;
            node.Indent = baseColumn + start;
            node.ValueColumn = baseColumn + start;
            node.ValueEndColumn = baseColumn + end;

            return node;
        }

        private YamlException Problem(string what) =>
            new($"This inline value could not be read: {what}.", line + 1);
    }
}
