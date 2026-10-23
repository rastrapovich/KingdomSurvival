using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KingdomSurvival.QuestWorkshopBridge.Editor
{
    // Минимальный JSON-разбор для файла Мастерской квестов. JsonUtility здесь
    // не подходит: проект мастерской глубже предела вложенности сериализатора
    // Unity, а необязательные объекты (проверка, встреча) должны отличаться от
    // пустых. Результат: Dictionary<string, object>, List<object>, string,
    // double, bool или null.
    public static class QuestWorkshopJson
    {
        public static object Parse(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            Reader reader = new Reader(text);
            reader.SkipWhitespace();
            object value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
                throw reader.Error("лишние символы после JSON");
            return value;
        }

        public static string Serialize(object value)
        {
            StringBuilder builder = new StringBuilder();
            Write(builder, value);
            return builder.ToString();
        }

        private static void Write(StringBuilder builder, object value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    break;
                case string text:
                    WriteString(builder, text);
                    break;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    break;
                case double number:
                    builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case float number:
                    builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case int number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case long number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object> map:
                    builder.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> pair in map)
                    {
                        if (!first)
                            builder.Append(',');
                        first = false;
                        WriteString(builder, pair.Key);
                        builder.Append(':');
                        Write(builder, pair.Value);
                    }
                    builder.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    builder.Append('[');
                    bool firstItem = true;
                    foreach (object item in list)
                    {
                        if (!firstItem)
                            builder.Append(',');
                        firstItem = false;
                        Write(builder, item);
                    }
                    builder.Append(']');
                    break;
                default:
                    WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (ch < 0x20)
                            builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(ch);
                        break;
                }
            }
            builder.Append('"');
        }

        private sealed class Reader
        {
            private readonly string text;
            private int position;

            public Reader(string text)
            {
                this.text = text;
            }

            public bool AtEnd => position >= text.Length;

            public FormatException Error(string message)
            {
                return new FormatException("JSON, позиция " + position + ": " + message);
            }

            public void SkipWhitespace()
            {
                while (position < text.Length && char.IsWhiteSpace(text[position]))
                    position++;
                // BOM в начале файла
                if (position == 0 && text.Length > 0 && text[0] == '﻿')
                {
                    position++;
                    SkipWhitespace();
                }
            }

            public object ReadValue()
            {
                if (AtEnd)
                    throw Error("неожиданный конец");

                char ch = text[position];
                switch (ch)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': return ReadLiteral("true", true);
                    case 'f': return ReadLiteral("false", false);
                    case 'n': return ReadLiteral("null", null);
                    default:
                        if (ch == '-' || (ch >= '0' && ch <= '9'))
                            return ReadNumber();
                        throw Error("неожиданный символ '" + ch + "'");
                }
            }

            private object ReadLiteral(string literal, object value)
            {
                if (string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0)
                    throw Error("ожидалось " + literal);
                position += literal.Length;
                return value;
            }

            private Dictionary<string, object> ReadObject()
            {
                Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.Ordinal);
                position++;
                SkipWhitespace();
                if (!AtEnd && text[position] == '}')
                {
                    position++;
                    return map;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[position] != '"')
                        throw Error("ожидалось имя поля");
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || text[position] != ':')
                        throw Error("ожидалось ':'");
                    position++;
                    SkipWhitespace();
                    map[key] = ReadValue();
                    SkipWhitespace();
                    if (AtEnd)
                        throw Error("неожиданный конец объекта");
                    if (text[position] == ',')
                    {
                        position++;
                        continue;
                    }
                    if (text[position] == '}')
                    {
                        position++;
                        return map;
                    }
                    throw Error("ожидалось ',' или '}'");
                }
            }

            private List<object> ReadArray()
            {
                List<object> list = new List<object>();
                position++;
                SkipWhitespace();
                if (!AtEnd && text[position] == ']')
                {
                    position++;
                    return list;
                }

                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd)
                        throw Error("неожиданный конец массива");
                    if (text[position] == ',')
                    {
                        position++;
                        continue;
                    }
                    if (text[position] == ']')
                    {
                        position++;
                        return list;
                    }
                    throw Error("ожидалось ',' или ']'");
                }
            }

            private string ReadString()
            {
                StringBuilder builder = new StringBuilder();
                position++;
                while (true)
                {
                    if (AtEnd)
                        throw Error("незакрытая строка");
                    char ch = text[position++];
                    if (ch == '"')
                        return builder.ToString();
                    if (ch != '\\')
                    {
                        builder.Append(ch);
                        continue;
                    }
                    if (AtEnd)
                        throw Error("незакрытая строка");
                    char escape = text[position++];
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
                                throw Error("короткая \\u-последовательность");
                            builder.Append((char)int.Parse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            position += 4;
                            break;
                        default:
                            throw Error("неизвестная escape-последовательность");
                    }
                }
            }

            private double ReadNumber()
            {
                int start = position;
                if (text[position] == '-')
                    position++;
                while (position < text.Length && "0123456789.eE+-".IndexOf(text[position]) >= 0)
                    position++;
                string token = text.Substring(start, position - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw Error("неверное число '" + token + "'");
                return value;
            }
        }
    }

    // Удобный доступ к разобранному JSON без исключений на отсутствующих полях.
    public readonly struct JsonNode
    {
        private readonly object value;

        public JsonNode(object value)
        {
            this.value = value;
        }

        public object Raw => value;
        public bool IsNull => value == null;
        public bool IsObject => value is Dictionary<string, object>;
        public bool IsArray => value is List<object>;

        public JsonNode this[string key] =>
            value is Dictionary<string, object> map && map.TryGetValue(key, out object child)
                ? new JsonNode(child)
                : default;

        public bool Has(string key)
        {
            return value is Dictionary<string, object> map && map.ContainsKey(key) && map[key] != null;
        }

        public string Str(string fallback = "")
        {
            switch (value)
            {
                case string text: return text;
                case double number: return number.ToString(CultureInfo.InvariantCulture);
                case bool flag: return flag ? "true" : "false";
                default: return fallback;
            }
        }

        public int Int(int fallback = 0)
        {
            switch (value)
            {
                case double number: return (int)Math.Round(number);
                case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed): return parsed;
                case bool flag: return flag ? 1 : 0;
                default: return fallback;
            }
        }

        public double Num(double fallback = 0)
        {
            switch (value)
            {
                case double number: return number;
                case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed): return parsed;
                default: return fallback;
            }
        }

        public bool Bool(bool fallback = false)
        {
            switch (value)
            {
                case bool flag: return flag;
                case double number: return Math.Abs(number) > 0.0001;
                case string text: return text == "true" || text == "1";
                default: return fallback;
            }
        }

        public IEnumerable<JsonNode> Items()
        {
            if (value is List<object> list)
            {
                foreach (object item in list)
                    yield return new JsonNode(item);
            }
        }

        public List<string> Strings()
        {
            List<string> result = new List<string>();
            foreach (JsonNode item in Items())
            {
                string text = item.Str().Trim();
                if (text.Length > 0)
                    result.Add(text);
            }
            return result;
        }
    }
}
