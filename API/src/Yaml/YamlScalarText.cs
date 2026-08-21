using System.Globalization;
using System.Text;

namespace McAdminPlugins.Yaml;

/// <summary>
/// Turns the text of one scalar into a value and back. The resolution rules are
/// SnakeYAML's, because SnakeYAML is what Bukkit reads these files with: <c>yes</c> is a
/// boolean, <c>0755</c> is octal, and a quoted <c>'true'</c> is the four-letter word.
/// </summary>
internal static class YamlScalarText
{
    private static readonly string[] TrueWords = ["true", "yes", "on"];
    private static readonly string[] FalseWords = ["false", "no", "off"];

    /// <summary>Reads one scalar, quotes and all. <paramref name="raw"/> has no trailing comment.</summary>
    internal static YamlScalar Decode(string raw, int line)
    {
        if (raw.Length == 0) return new YamlScalar(null, YamlScalarStyle.Plain);

        if (raw[0] == '\'')
        {
            var end = SkipQuoted(raw, 0);
            if (end < 0) throw new YamlException("A single-quoted value is never closed.", line);
            if (end != raw.Length) throw new YamlException("There is text after the closing quote.", line);

            return new YamlScalar(raw[1..^1].Replace("''", "'"), YamlScalarStyle.SingleQuoted);
        }

        if (raw[0] == '"')
        {
            var end = SkipQuoted(raw, 0);
            if (end < 0) throw new YamlException("A double-quoted value is never closed.", line);
            if (end != raw.Length) throw new YamlException("There is text after the closing quote.", line);

            return new YamlScalar(Unescape(raw[1..^1], line), YamlScalarStyle.DoubleQuoted);
        }

        return new YamlScalar(IsNullWord(raw) ? null : raw, YamlScalarStyle.Plain);
    }

    /// <summary>Reads a key, which is a scalar that has to end up as text.</summary>
    internal static string DecodeKey(string raw, int line) =>
        raw.Length > 0 && raw[0] is '\'' or '"' ? Decode(raw, line).Value ?? "" : raw;

    private static bool IsNullWord(string text) => text is "~" or "null" or "Null" or "NULL";

    /// <summary>Everything up to a <c>#</c> that starts a comment, ignoring hashes inside quotes.</summary>
    internal static string CutComment(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '\'' or '"')
            {
                var end = SkipQuoted(text, i);
                if (end < 0) break;

                i = end - 1;
                continue;
            }

            if (text[i] == '#' && i > 0 && text[i - 1] == ' ') return text[..i].TrimEnd();
        }

        return text.TrimEnd();
    }

    /// <summary>Index just past the closing quote of the string starting at <paramref name="start"/>, or -1.</summary>
    internal static int SkipQuoted(string text, int start)
    {
        var quote = text[start];

        for (var i = start + 1; i < text.Length; i++)
        {
            if (quote == '"' && text[i] == '\\')
            {
                i++;
                continue;
            }

            if (text[i] != quote) continue;

            // '' inside a single-quoted string is one quote, not the end of it.
            if (quote == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
            {
                i++;
                continue;
            }

            return i + 1;
        }

        return -1;
    }

    private static string Unescape(string text, int line)
    {
        if (!text.Contains('\\')) return text;

        var result = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\')
            {
                result.Append(text[i]);
                continue;
            }

            if (++i == text.Length) throw new YamlException("A double-quoted value ends in a lone backslash.", line);

            switch (text[i])
            {
                case '0': result.Append('\0'); break;
                case 'a': result.Append('\a'); break;
                case 'b': result.Append('\b'); break;
                case 't' or '\t': result.Append('\t'); break;
                case 'n': result.Append('\n'); break;
                case 'v': result.Append('\v'); break;
                case 'f': result.Append('\f'); break;
                case 'r': result.Append('\r'); break;
                case 'e': result.Append('\u001b'); break;
                case 'N': result.Append('\u0085'); break;
                case '_': result.Append('\u00a0'); break;
                case 'L': result.Append('\u2028'); break;
                case 'P': result.Append('\u2029'); break;
                case 'x': result.Append((char)ReadCode(text, ref i, 2, line)); break;
                case 'u': result.Append((char)ReadCode(text, ref i, 4, line)); break;
                case 'U': result.Append(char.ConvertFromUtf32(ReadCode(text, ref i, 8, line))); break;
                default: result.Append(text[i]); break;
            }
        }

        return result.ToString();
    }

    private static int ReadCode(string text, ref int i, int digits, int line)
    {
        if (i + digits >= text.Length)
            throw new YamlException($"An escape needs {digits} hex digits after it.", line);

        var code = text.Substring(i + 1, digits);
        i += digits;

        return int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new YamlException($"'{code}' is not a hex escape.", line);
    }

    // ---- resolution -------------------------------------------------------------

    internal static bool TryReadBool(string text, out bool value)
    {
        var word = text.Trim();

        if (TrueWords.Contains(word, StringComparer.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (FalseWords.Contains(word, StringComparer.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    /// <summary>
    /// Writes <paramref name="value"/> the way <paramref name="sample"/> was written, so a
    /// file that says <c>enabled: no</c> gets <c>yes</c> back rather than <c>true</c>.
    /// </summary>
    internal static string SpellBool(string sample, bool value)
    {
        var word = sample.Trim();
        var family = -1;

        for (var i = 0; i < TrueWords.Length; i++)
        {
            if (!word.Equals(TrueWords[i], StringComparison.OrdinalIgnoreCase)
                && !word.Equals(FalseWords[i], StringComparison.OrdinalIgnoreCase)) continue;

            family = i;
            break;
        }

        if (family < 0) return value ? "true" : "false";

        var spelling = value ? TrueWords[family] : FalseWords[family];

        if (word.All(char.IsUpper)) return spelling.ToUpperInvariant();
        if (char.IsUpper(word[0])) return char.ToUpperInvariant(spelling[0]) + spelling[1..];

        return spelling;
    }

    internal static bool TryReadLong(string text, out long value)
    {
        value = 0;

        var word = text.Trim().Replace("_", "");
        if (word.Length == 0) return false;

        var sign = 1L;
        if (word[0] is '+' or '-')
        {
            if (word[0] == '-') sign = -1;
            word = word[1..];
            if (word.Length == 0) return false;
        }

        var (digits, radix) = word switch
        {
            ['0', 'x' or 'X', ..] => (word[2..], 16),
            ['0', 'b' or 'B', ..] => (word[2..], 2),
            ['0', 'o' or 'O', ..] => (word[2..], 8),
            ['0', ..] when word.Length > 1 => (word[1..], 8),
            _ => (word, 10)
        };

        if (digits.Length == 0) return false;

        if (radix == 10)
        {
            if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return false;

            value *= sign;
            return true;
        }

        foreach (var digit in digits)
        {
            var index = HexDigit(digit);
            if (index < 0 || index >= radix) return false;

            value = value * radix + index;
        }

        value *= sign;
        return true;
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    internal static bool TryReadDouble(string text, out double value)
    {
        var word = text.Trim().Replace("_", "");

        switch (word)
        {
            case ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF":
                value = double.PositiveInfinity;
                return true;
            case "-.inf" or "-.Inf" or "-.INF":
                value = double.NegativeInfinity;
                return true;
            case ".nan" or ".NaN" or ".NAN":
                value = double.NaN;
                return true;
        }

        return double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>True when writing the text unquoted would make it read back as something other than text.</summary>
    internal static bool ResolvesAsNonString(string text) =>
        text.Length == 0
        || IsNullWord(text)
        || TryReadBool(text, out _)
        || TryReadLong(text, out _)
        || TryReadDouble(text, out _);

    // ---- writing ----------------------------------------------------------------

    /// <summary>
    /// The text as it goes into the file. <paramref name="style"/> is what the value was
    /// written as before, and is honoured when it still can be: a value that used to be
    /// unquoted but no longer survives unquoted gets quotes.
    /// </summary>
    internal static string Encode(string? value, YamlScalarStyle style)
    {
        if (value is null) return "";
        if (value.Length == 0) return style == YamlScalarStyle.DoubleQuoted ? "\"\"" : "''";

        if (value.Any(c => c is '\n' or '\r')) return DoubleQuote(value);

        return style switch
        {
            YamlScalarStyle.DoubleQuoted => DoubleQuote(value),
            YamlScalarStyle.SingleQuoted => SingleQuote(value),
            _ when IsPlainSafe(value) => value,
            _ => SingleQuote(value)
        };
    }

    /// <summary>A key as it goes into the file, quoted only when it has to be.</summary>
    internal static string EncodeKey(string key) => IsPlainSafe(key) ? key : SingleQuote(key);

    private static string SingleQuote(string value) => $"'{value.Replace("'", "''")}'";

    private static string DoubleQuote(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': result.Append("\\\\"); break;
                case '"': result.Append("\\\""); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                default:
                    if (char.IsControl(c)) result.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    else result.Append(c);
                    break;
            }
        }

        return result.Append('"').ToString();
    }

    /// <summary>Whether the text can go in unquoted and come back unchanged.</summary>
    private static bool IsPlainSafe(string value)
    {
        if (value.Length == 0) return false;
        if (value != value.Trim()) return false;
        if (value.Any(char.IsControl)) return false;

        // Indicators that start something else entirely when they lead a value.
        if (value[0] is '#' or ',' or '[' or ']' or '{' or '}' or '&' or '*' or '!' or '|'
            or '>' or '\'' or '"' or '%' or '@' or '`') return false;

        // '-', '?' and ':' only lead something else when a space follows them.
        if (value[0] is '-' or '?' or ':' && (value.Length == 1 || value[1] == ' ')) return false;

        return !value.Contains(": ") && !value.EndsWith(':') && !value.Contains(" #");
    }
}
