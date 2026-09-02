using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CreditPincher.Core.Compat
{
    /// <summary>
    /// A tiny JSON reader/writer for the one flat settings object this app persists.
    ///
    /// <c>System.Text.Json</c> is not part of the .NET Framework that ships with Windows,
    /// and pulling it in would mean a NuGet restore and a second file next to the exe.
    /// The settings file is a handful of scalars and one array of numbers, so parsing it
    /// directly keeps the build to "compilers already on the machine" and the output to a
    /// single executable.
    ///
    /// The text produced is indented, uses the same PascalCase member names, and omits
    /// nulls — i.e. it is read/write compatible with the files the .NET 8 build wrote.
    /// </summary>
    public static class SimpleJson
    {
        /// <summary>
        /// Parses a JSON object into name → value, where values are <see cref="string"/>,
        /// <see cref="double"/>, <see cref="bool"/>, null, or a <c>List&lt;object&gt;</c>.
        /// Throws <see cref="FormatException"/> on malformed input.
        /// </summary>
        public static Dictionary<string, object> Parse(string text)
        {
            var position = 0;
            var value = ParseValue(text, ref position);
            SkipWhitespace(text, ref position);

            var members = value as Dictionary<string, object>;
            if (members == null)
            {
                throw new FormatException("Expected a JSON object at the root.");
            }

            return members;
        }

        /// <summary>Renders name → value as indented JSON. Null values are skipped.</summary>
        public static string Write(IList<KeyValuePair<string, object>> members)
        {
            var builder = new StringBuilder();
            builder.Append("{\n");

            var written = 0;
            for (var index = 0; index < members.Count; index++)
            {
                if (members[index].Value == null)
                {
                    continue;
                }

                if (written > 0)
                {
                    builder.Append(",\n");
                }

                builder.Append("  ");
                WriteString(builder, members[index].Key);
                builder.Append(": ");
                WriteValue(builder, members[index].Value);
                written++;
            }

            builder.Append("\n}");
            return builder.ToString();
        }

        // ----------------------------------------------------------------- reading

        private static object ParseValue(string text, ref int position)
        {
            SkipWhitespace(text, ref position);
            if (position >= text.Length)
            {
                throw new FormatException("Unexpected end of JSON input.");
            }

            var current = text[position];
            switch (current)
            {
                case '{':
                    return ParseObject(text, ref position);
                case '[':
                    return ParseArray(text, ref position);
                case '"':
                    return ParseString(text, ref position);
                case 't':
                    Expect(text, ref position, "true");
                    return true;
                case 'f':
                    Expect(text, ref position, "false");
                    return false;
                case 'n':
                    Expect(text, ref position, "null");
                    return null;
                default:
                    return ParseNumber(text, ref position);
            }
        }

        private static Dictionary<string, object> ParseObject(string text, ref int position)
        {
            var members = new Dictionary<string, object>(StringComparer.Ordinal);
            position++; // '{'
            SkipWhitespace(text, ref position);

            if (position < text.Length && text[position] == '}')
            {
                position++;
                return members;
            }

            while (true)
            {
                SkipWhitespace(text, ref position);
                var name = ParseString(text, ref position);

                SkipWhitespace(text, ref position);
                if (position >= text.Length || text[position] != ':')
                {
                    throw new FormatException("Expected ':' after a JSON member name.");
                }

                position++;
                members[name] = ParseValue(text, ref position);

                SkipWhitespace(text, ref position);
                if (position >= text.Length)
                {
                    throw new FormatException("Unterminated JSON object.");
                }

                if (text[position] == ',')
                {
                    position++;
                    continue;
                }

                if (text[position] == '}')
                {
                    position++;
                    return members;
                }

                throw new FormatException("Expected ',' or '}' in a JSON object.");
            }
        }

        private static List<object> ParseArray(string text, ref int position)
        {
            var items = new List<object>();
            position++; // '['
            SkipWhitespace(text, ref position);

            if (position < text.Length && text[position] == ']')
            {
                position++;
                return items;
            }

            while (true)
            {
                items.Add(ParseValue(text, ref position));

                SkipWhitespace(text, ref position);
                if (position >= text.Length)
                {
                    throw new FormatException("Unterminated JSON array.");
                }

                if (text[position] == ',')
                {
                    position++;
                    continue;
                }

                if (text[position] == ']')
                {
                    position++;
                    return items;
                }

                throw new FormatException("Expected ',' or ']' in a JSON array.");
            }
        }

        private static string ParseString(string text, ref int position)
        {
            if (position >= text.Length || text[position] != '"')
            {
                throw new FormatException("Expected a JSON string.");
            }

            position++;
            var builder = new StringBuilder();

            while (position < text.Length)
            {
                var current = text[position++];

                if (current == '"')
                {
                    return builder.ToString();
                }

                if (current != '\\')
                {
                    builder.Append(current);
                    continue;
                }

                if (position >= text.Length)
                {
                    break;
                }

                var escape = text[position++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (position + 4 > text.Length)
                        {
                            throw new FormatException("Truncated \\u escape in a JSON string.");
                        }

                        builder.Append((char)ushort.Parse(
                            text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        position += 4;
                        break;
                    default:
                        throw new FormatException("Unrecognised escape in a JSON string.");
                }
            }

            throw new FormatException("Unterminated JSON string.");
        }

        private static double ParseNumber(string text, ref int position)
        {
            var start = position;

            while (position < text.Length && "+-.eE0123456789".IndexOf(text[position]) >= 0)
            {
                position++;
            }

            double value;
            if (position == start ||
                !double.TryParse(
                    text.Substring(start, position - start),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                throw new FormatException("Expected a JSON number.");
            }

            return value;
        }

        private static void Expect(string text, ref int position, string literal)
        {
            if (position + literal.Length > text.Length ||
                string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0)
            {
                throw new FormatException("Expected '" + literal + "' in JSON input.");
            }

            position += literal.Length;
        }

        private static void SkipWhitespace(string text, ref int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }
        }

        // ----------------------------------------------------------------- writing

        private static void WriteValue(StringBuilder builder, object value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
                return;
            }

            if (value is string)
            {
                WriteString(builder, (string)value);
                return;
            }

            if (value is int)
            {
                builder.Append(((int)value).ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is double)
            {
                builder.Append(((double)value).ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            var items = value as IEnumerable<int>;
            if (items != null)
            {
                builder.Append('[');
                var first = true;
                foreach (var item in items)
                {
                    if (!first)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(item.ToString(CultureInfo.InvariantCulture));
                    first = false;
                }

                builder.Append(']');
                return;
            }

            throw new ArgumentException("Unsupported JSON value type: " + value.GetType());
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');

            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }
}
