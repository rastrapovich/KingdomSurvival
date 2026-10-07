using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12О: небольшой строгий разбор JSON для manifest экспорта земли
    // (объект → Dictionary, массив → List, число → double, строка, bool,
    // null). Только редактор; игра manifest не читает.
    public static class GroundJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("Пустой JSON.");
            int index = 0;
            if (text.Length > 0 && text[0] == '﻿') index = 1;
            object value = ReadValue(text, ref index);
            SkipSpace(text, ref index);
            if (index != text.Length) throw Error(text, index, "лишние символы после JSON");
            return value;
        }

        private static object ReadValue(string text, ref int index)
        {
            SkipSpace(text, ref index);
            if (index >= text.Length) throw Error(text, index, "неожиданный конец");
            char c = text[index];
            switch (c)
            {
                case '{': return ReadObject(text, ref index);
                case '[': return ReadArray(text, ref index);
                case '"': return ReadString(text, ref index);
                case 't': Expect(text, ref index, "true"); return true;
                case 'f': Expect(text, ref index, "false"); return false;
                case 'n': Expect(text, ref index, "null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber(text, ref index);
                    // NaN/Infinity не входят в JSON: экспортёр пишет allow_nan=False.
                    throw Error(text, index, "неожиданный символ «" + c + "»");
            }
        }

        private static Dictionary<string, object> ReadObject(string text, ref int index)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            index++;
            SkipSpace(text, ref index);
            if (index < text.Length && text[index] == '}') { index++; return result; }
            while (true)
            {
                SkipSpace(text, ref index);
                if (index >= text.Length || text[index] != '"') throw Error(text, index, "ожидалось имя поля");
                string key = ReadString(text, ref index);
                SkipSpace(text, ref index);
                if (index >= text.Length || text[index] != ':') throw Error(text, index, "ожидалось «:»");
                index++;
                result[key] = ReadValue(text, ref index);
                SkipSpace(text, ref index);
                if (index >= text.Length) throw Error(text, index, "незакрытый объект");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == '}') { index++; return result; }
                throw Error(text, index, "ожидалось «,» или «}»");
            }
        }

        private static List<object> ReadArray(string text, ref int index)
        {
            List<object> result = new List<object>();
            index++;
            SkipSpace(text, ref index);
            if (index < text.Length && text[index] == ']') { index++; return result; }
            while (true)
            {
                result.Add(ReadValue(text, ref index));
                SkipSpace(text, ref index);
                if (index >= text.Length) throw Error(text, index, "незакрытый массив");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == ']') { index++; return result; }
                throw Error(text, index, "ожидалось «,» или «]»");
            }
        }

        private static string ReadString(string text, ref int index)
        {
            StringBuilder builder = new StringBuilder();
            index++;
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"') return builder.ToString();
                if (c != '\\') { builder.Append(c); continue; }
                if (index >= text.Length) break;
                char e = text[index++];
                switch (e)
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
                        if (index + 4 > text.Length) throw Error(text, index, "обрезанный \\u");
                        builder.Append((char)int.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        index += 4;
                        break;
                    default: throw Error(text, index, "неизвестное экранирование");
                }
            }
            throw Error(text, index, "незакрытая строка");
        }

        private static double ReadNumber(string text, ref int index)
        {
            int start = index;
            while (index < text.Length && "+-0123456789.eE".IndexOf(text[index]) >= 0) index++;
            string number = text.Substring(start, index - start);
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || double.IsInfinity(value))
                throw Error(text, start, "неверное число «" + number + "»");
            return value;
        }

        private static void Expect(string text, ref int index, string word)
        {
            if (string.CompareOrdinal(text, index, word, 0, word.Length) != 0) throw Error(text, index, "ожидалось " + word);
            index += word.Length;
        }

        private static void SkipSpace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        }

        private static FormatException Error(string text, int index, string what)
        {
            int line = 1;
            for (int i = 0; i < Math.Min(index, text.Length); i++) if (text[i] == '\n') line++;
            return new FormatException("Ошибка JSON в строке " + line + ": " + what + ".");
        }

        // ------------------------------------------------------------------
        // Доступ к полям
        // ------------------------------------------------------------------

        public static Dictionary<string, object> Obj(object value, string key) =>
            value is Dictionary<string, object> map && map.TryGetValue(key, out object child) ? child as Dictionary<string, object> : null;

        public static List<object> Arr(object value, string key) =>
            value is Dictionary<string, object> map && map.TryGetValue(key, out object child) ? child as List<object> : null;

        public static string Str(object value, string key) =>
            value is Dictionary<string, object> map && map.TryGetValue(key, out object child) ? child as string : null;

        public static bool Has(object value, string key) => value is Dictionary<string, object> map && map.ContainsKey(key);

        public static double? Num(object value, string key) =>
            value is Dictionary<string, object> map && map.TryGetValue(key, out object child) && child is double number ? number : (double?)null;

        public static bool? Bool(object value, string key) =>
            value is Dictionary<string, object> map && map.TryGetValue(key, out object child) && child is bool flag ? flag : (bool?)null;

        // Массив чисел заданной длины (или null).
        public static double[] Numbers(object value, string key, int length)
        {
            List<object> list = Arr(value, key);
            if (list == null || (length > 0 && list.Count != length)) return null;
            double[] result = new double[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is double number)) return null;
                result[i] = number;
            }
            return result;
        }
    }
}
