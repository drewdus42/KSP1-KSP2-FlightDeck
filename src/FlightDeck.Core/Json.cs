using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlightDeck.Core
{
    public class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
        public JsonException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Minimal dependency free JSON reader/writer. KSP ships no JSON library and its
    /// UnityEngine.JsonUtility cannot handle the dictionaries this API needs.
    /// </summary>
    public static class Json
    {
        private const int MaxDepth = 32;

        public static string Serialize(object value)
        {
            var builder = new StringBuilder(256);
            WriteValue(builder, value, 0);
            return builder.ToString();
        }

        public static object Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }

            int index = 0;
            object value = ParseValue(text, ref index, 0);
            SkipWhitespace(text, ref index);
            if (index < text.Length)
            {
                throw Error("Unexpected trailing characters", text, index);
            }

            return value;
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            var result = value as Dictionary<string, object>;
            if (result != null)
            {
                return result;
            }

            var generic = value as IDictionary<string, object>;
            if (generic != null)
            {
                return new Dictionary<string, object>(generic, StringComparer.Ordinal);
            }

            return null;
        }

        public static object Get(IDictionary<string, object> source, string key)
        {
            if (source == null || key == null)
            {
                return null;
            }

            object value;
            if (source.TryGetValue(key, out value))
            {
                return value;
            }

            foreach (var pair in source)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            return null;
        }

        public static object Get(IDictionary<string, object> source, params string[] keys)
        {
            foreach (var key in keys)
            {
                object value = Get(source, key);
                if (value != null)
                {
                    return value;
                }
            }

            return null;
        }

        public static string GetString(IDictionary<string, object> source, params string[] keys)
        {
            object value = Get(source, keys);
            if (value == null)
            {
                return null;
            }

            var text = value as string;
            return text ?? Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static double? GetDouble(IDictionary<string, object> source, params string[] keys)
        {
            object value = Get(source, keys);
            if (value == null)
            {
                return null;
            }

            if (value is double) return (double)value;
            if (value is float) return (float)value;
            if (value is long) return (long)value;
            if (value is int) return (int)value;
            if (value is bool) return ((bool)value) ? 1d : 0d;

            double parsed;
            if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return null;
        }

        public static int? GetInt(IDictionary<string, object> source, params string[] keys)
        {
            double? value = GetDouble(source, keys);
            if (!value.HasValue)
            {
                return null;
            }

            return (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
        }

        public static bool? GetBool(IDictionary<string, object> source, params string[] keys)
        {
            object value = Get(source, keys);
            if (value == null)
            {
                return null;
            }

            if (value is bool) return (bool)value;
            if (value is double) return (double)value != 0d;
            if (value is long) return (long)value != 0L;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;

            double number;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return number != 0d;
            }

            return null;
        }

        private static void WriteValue(StringBuilder builder, object value, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new JsonException("Maximum JSON depth exceeded while serializing.");
            }

            if (value == null)
            {
                builder.Append("null");
                return;
            }

            var text = value as string;
            if (text != null)
            {
                WriteString(builder, text);
                return;
            }

            if (value is bool)
            {
                builder.Append(((bool)value) ? "true" : "false");
                return;
            }

            if (value is char)
            {
                WriteString(builder, value.ToString());
                return;
            }

            if (value is Enum)
            {
                WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            if (IsNumber(value))
            {
                WriteNumber(builder, value);
                return;
            }

            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                WriteDictionary(builder, dictionary, depth);
                return;
            }

            var untypedDictionary = value as IDictionary;
            if (untypedDictionary != null)
            {
                WriteUntypedDictionary(builder, untypedDictionary, depth);
                return;
            }

            var enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                builder.Append('[');
                bool first = true;
                foreach (object item in enumerable)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteValue(builder, item, depth + 1);
                }

                builder.Append(']');
                return;
            }

            WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteDictionary(StringBuilder builder, IDictionary<string, object> dictionary, int depth)
        {
            builder.Append('{');
            bool first = true;
            foreach (var pair in dictionary)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                WriteString(builder, pair.Key);
                builder.Append(':');
                WriteValue(builder, pair.Value, depth + 1);
            }

            builder.Append('}');
        }

        private static void WriteUntypedDictionary(StringBuilder builder, IDictionary dictionary, int depth)
        {
            builder.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                WriteString(builder, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                builder.Append(':');
                WriteValue(builder, entry.Value, depth + 1);
            }

            builder.Append('}');
        }

        private static bool IsNumber(object value)
        {
            return value is sbyte || value is byte || value is short || value is ushort
                || value is int || value is uint || value is long || value is ulong
                || value is float || value is double || value is decimal;
        }

        private static void WriteNumber(StringBuilder builder, object value)
        {
            if (value is float)
            {
                WriteFloatingPoint(builder, (float)value);
                return;
            }

            if (value is double)
            {
                WriteFloatingPoint(builder, (double)value);
                return;
            }

            if (value is decimal)
            {
                builder.Append(((decimal)value).ToString(CultureInfo.InvariantCulture));
                return;
            }

            builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteFloatingPoint(StringBuilder builder, double value)
        {
            // KSP loves producing NaN/Infinity for empty orbits. JSON has no representation
            // for those, and the Stream Deck UI shows "null" as an empty readout.
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                builder.Append("null");
                return;
            }

            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '\u007f')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private static object ParseValue(string text, ref int index, int depth)
        {
            if (depth > MaxDepth)
            {
                throw Error("Maximum JSON depth exceeded", text, index);
            }

            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                throw Error("Unexpected end of JSON", text, index);
            }

            char c = text[index];
            switch (c)
            {
                case '{':
                    return ParseObject(text, ref index, depth);
                case '[':
                    return ParseArray(text, ref index, depth);
                case '"':
                    return ParseString(text, ref index);
                case 't':
                    Expect(text, ref index, "true");
                    return true;
                case 'f':
                    Expect(text, ref index, "false");
                    return false;
                case 'n':
                    Expect(text, ref index, "null");
                    return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                    {
                        return ParseNumber(text, ref index);
                    }

                    throw Error("Unexpected character '" + c + "'", text, index);
            }
        }

        private static Dictionary<string, object> ParseObject(string text, ref int index, int depth)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            index++; // '{'
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != '"')
                {
                    throw Error("Expected a property name", text, index);
                }

                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                {
                    throw Error("Expected ':' after property name", text, index);
                }

                index++;
                object value = ParseValue(text, ref index, depth + 1);
                result[key] = value;

                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw Error("Unterminated object", text, index);
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == '}')
                {
                    index++;
                    return result;
                }

                throw Error("Expected ',' or '}'", text, index);
            }
        }

        private static List<object> ParseArray(string text, ref int index, int depth)
        {
            var result = new List<object>();
            index++; // '['
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return result;
            }

            while (true)
            {
                result.Add(ParseValue(text, ref index, depth + 1));
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw Error("Unterminated array", text, index);
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == ']')
                {
                    index++;
                    return result;
                }

                throw Error("Expected ',' or ']'", text, index);
            }
        }

        private static string ParseString(string text, ref int index)
        {
            index++; // opening quote
            var builder = new StringBuilder();
            while (true)
            {
                if (index >= text.Length)
                {
                    throw Error("Unterminated string", text, index);
                }

                char c = text[index++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= text.Length)
                {
                    throw Error("Unterminated escape sequence", text, index);
                }

                char escape = text[index++];
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
                        if (index + 4 > text.Length)
                        {
                            throw Error("Truncated unicode escape", text, index);
                        }

                        int code;
                        if (!int.TryParse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            throw Error("Invalid unicode escape", text, index);
                        }

                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw Error("Invalid escape sequence '\\" + escape + "'", text, index);
                }
            }
        }

        private static object ParseNumber(string text, ref int index)
        {
            int start = index;
            if (text[index] == '-')
            {
                index++;
            }

            bool isFloatingPoint = false;
            while (index < text.Length)
            {
                char c = text[index];
                if (c >= '0' && c <= '9')
                {
                    index++;
                    continue;
                }

                if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                {
                    isFloatingPoint = true;
                    index++;
                    continue;
                }

                break;
            }

            string token = text.Substring(start, index - start);
            if (!isFloatingPoint)
            {
                long integer;
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                {
                    return integer;
                }
            }

            double number;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }

            throw Error("Invalid number '" + token + "'", text, start);
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length || string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw Error("Expected '" + literal + "'", text, index);
            }

            index += literal.Length;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length)
            {
                char c = text[index];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    index++;
                    continue;
                }

                break;
            }
        }

        private static JsonException Error(string message, string text, int index)
        {
            return new JsonException(message + " (at position " + index.ToString(CultureInfo.InvariantCulture) + ")");
        }
    }
}
