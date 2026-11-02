using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KingdomSurvival.MapLab.Editor
{
    // Маленький разборщик JSON для manifest пакета арта: объекты →
    // Dictionary, массивы → List, числа → double, null → null. Неизвестные
    // поля просто остаются в словаре. JsonUtility не подходит: в manifest есть
    // null на месте чисел и массивов.
    public static class MapLabJson
    {
        public static object Parse(string text)
        {
            if (text == null)
                throw new FormatException("Пустой JSON.");
            int i = 0;
            object value = ParseValue(text, ref i);
            SkipSpace(text, ref i);
            if (i != text.Length)
                throw new FormatException($"Лишние символы после JSON (позиция {i}).");
            return value;
        }

        public static Dictionary<string, object> AsObject(object value) => value as Dictionary<string, object>;

        public static List<object> AsArray(object value) => value as List<object>;

        public static string GetString(Dictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out object v) ? v as string : null;
        }

        public static bool TryGetNumber(Dictionary<string, object> obj, string key, out double number)
        {
            number = 0;
            if (obj == null || !obj.TryGetValue(key, out object v) || !(v is double d))
                return false;
            number = d;
            return true;
        }

        public static double GetNumber(Dictionary<string, object> obj, string key, double fallback)
        {
            return TryGetNumber(obj, key, out double d) ? d : fallback;
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback)
        {
            return obj != null && obj.TryGetValue(key, out object v) && v is bool b ? b : fallback;
        }

        public static double[] GetNumbers(Dictionary<string, object> obj, string key)
        {
            List<object> list = obj != null && obj.TryGetValue(key, out object v) ? v as List<object> : null;
            if (list == null)
                return null;
            double[] result = new double[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is double d))
                    return null;
                result[i] = d;
            }
            return result;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length)
                throw new FormatException("Неожиданный конец JSON.");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return result;
            }
            while (true)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new FormatException($"Ожидался ключ (позиция {i}).");
                string key = ParseString(s, ref i);
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException($"Ожидалось «:» (позиция {i}).");
                i++;
                result[key] = ParseValue(s, ref i);
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return result;
                }
                throw new FormatException($"Ожидалось «,» или «}}» (позиция {i}).");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            List<object> result = new List<object>();
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return result;
            }
            while (true)
            {
                result.Add(ParseValue(s, ref i));
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return result;
                }
                throw new FormatException($"Ожидалось «,» или «]» (позиция {i}).");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                    return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length)
                    break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length)
                            throw new FormatException("Обрезанный \\u в строке JSON.");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default:
                        throw new FormatException($"Неизвестная escape-последовательность \\{e}.");
                }
            }
            throw new FormatException("Незакрытая строка JSON.");
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
                i++;
            if (start == i || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new FormatException($"Неверное значение JSON (позиция {start}).");
            return d;
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException($"Ожидалось «{word}» (позиция {i}).");
            i += word.Length;
        }

        private static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }
    }
}
