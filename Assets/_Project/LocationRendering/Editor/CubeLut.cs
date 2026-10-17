using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12М: таблица цвета LUT — из файла .cube (Adobe / Resolve: 3D или 1D)
    // или из полоски любого размера (N² × N). Даёт цвет для любого входа
    // (трилинейно) — по ней строится полоска URP 1024×32 (LutTools).
    // Вход и выход — в гамме экрана, как у полоски URP; LUT для LOG-съёмки
    // (вход камеры) дадут неверный цвет — нужны LUT «для Rec.709 / sRGB».
    public sealed class CubeLut
    {
        public int Size { get; private set; }
        public bool OneDimensional { get; private set; }
        public string Title { get; private set; } = "";
        private Vector3[] table;
        private Vector3 domainMin = Vector3.zero, domainMax = Vector3.one;

        // Разбор .cube. Ошибка — по-русски, с номером строки.
        public static CubeLut Parse(IEnumerable<string> lines, out string error)
        {
            error = null;
            CubeLut lut = new CubeLut();
            List<Vector3> values = new List<Vector3>();
            int number = 0;
            foreach (string raw in lines)
            {
                number++;
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string key = parts[0].ToUpperInvariant();
                if (key == "TITLE") { lut.Title = line.Substring(5).Trim().Trim('"'); continue; }
                if (key == "LUT_3D_SIZE" || key == "LUT_1D_SIZE")
                {
                    if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) || size < 2 || size > 256)
                    {
                        error = "Строка " + number + ": неверный размер таблицы.";
                        return null;
                    }
                    lut.Size = size;
                    lut.OneDimensional = key == "LUT_1D_SIZE";
                    continue;
                }
                if (key == "DOMAIN_MIN" || key == "DOMAIN_MAX" || key == "LUT_3D_INPUT_RANGE" || key == "LUT_1D_INPUT_RANGE")
                {
                    if (key.EndsWith("INPUT_RANGE"))
                    {
                        if (parts.Length < 3 || !Number(parts[1], out float low) || !Number(parts[2], out float high)) { error = "Строка " + number + ": неверный диапазон входа."; return null; }
                        lut.domainMin = Vector3.one * low;
                        lut.domainMax = Vector3.one * high;
                    }
                    else
                    {
                        if (!Triple(parts, 1, out Vector3 domain)) { error = "Строка " + number + ": неверная граница входа."; return null; }
                        if (key == "DOMAIN_MIN") lut.domainMin = domain; else lut.domainMax = domain;
                    }
                    continue;
                }
                if (char.IsLetter(parts[0][0])) continue; // прочие ключи (LUT_IN_VIDEO_RANGE и т. п.)
                if (!Triple(parts, 0, out Vector3 value)) { error = "Строка " + number + ": ожидались три числа цвета."; return null; }
                values.Add(value);
            }
            if (lut.Size == 0) { error = "В файле нет LUT_3D_SIZE или LUT_1D_SIZE — это не .cube."; return null; }
            int expected = lut.OneDimensional ? lut.Size : lut.Size * lut.Size * lut.Size;
            if (values.Count != expected)
            {
                error = "Цветов в таблице " + values.Count + ", а для размера " + lut.Size + " нужно " + expected + ".";
                return null;
            }
            for (int axis = 0; axis < 3; axis++)
                if (lut.domainMax[axis] <= lut.domainMin[axis]) { error = "Граница входа: максимум не больше минимума."; return null; }
            lut.table = values.ToArray();
            return lut;
        }

        // Таблица из полоски N² × N (x = синий · N + красный, y = зелёный снизу вверх).
        public static CubeLut FromStrip(Color[] pixels, int width, int height)
        {
            int size = height;
            if (size < 2 || width != size * size || pixels.Length != width * height) return null;
            CubeLut lut = new CubeLut { Size = size, table = new Vector3[size * size * size] };
            for (int b = 0; b < size; b++)
                for (int g = 0; g < size; g++)
                    for (int r = 0; r < size; r++)
                    {
                        Color c = pixels[g * width + b * size + r];
                        lut.table[r + g * size + b * size * size] = new Vector3(c.r, c.g, c.b);
                    }
            return lut;
        }

        public Color Sample(Color input)
        {
            Vector3 c = new Vector3(input.r, input.g, input.b);
            Vector3 t = new Vector3(
                Mathf.Clamp01((c.x - domainMin.x) / (domainMax.x - domainMin.x)),
                Mathf.Clamp01((c.y - domainMin.y) / (domainMax.y - domainMin.y)),
                Mathf.Clamp01((c.z - domainMin.z) / (domainMax.z - domainMin.z))) * (Size - 1);
            Vector3 result = OneDimensional
                ? new Vector3(Line(t.x).x, Line(t.y).y, Line(t.z).z)
                : Trilinear(t);
            return new Color(Mathf.Clamp01(result.x), Mathf.Clamp01(result.y), Mathf.Clamp01(result.z), 1);
        }

        private Vector3 Line(float t)
        {
            int i = Mathf.Min((int)t, Size - 2);
            return Vector3.Lerp(table[i], table[i + 1], t - i);
        }

        private Vector3 Trilinear(Vector3 t)
        {
            int r = Mathf.Min((int)t.x, Size - 2), g = Mathf.Min((int)t.y, Size - 2), b = Mathf.Min((int)t.z, Size - 2);
            float fr = t.x - r, fg = t.y - g, fb = t.z - b;
            Vector3 At(int dr, int dg, int db) => table[(r + dr) + (g + dg) * Size + (b + db) * Size * Size];
            Vector3 c00 = Vector3.Lerp(At(0, 0, 0), At(1, 0, 0), fr), c10 = Vector3.Lerp(At(0, 1, 0), At(1, 1, 0), fr);
            Vector3 c01 = Vector3.Lerp(At(0, 0, 1), At(1, 0, 1), fr), c11 = Vector3.Lerp(At(0, 1, 1), At(1, 1, 1), fr);
            return Vector3.Lerp(Vector3.Lerp(c00, c10, fg), Vector3.Lerp(c01, c11, fg), fb);
        }

        private static bool Number(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static bool Triple(string[] parts, int start, out Vector3 value)
        {
            value = default;
            if (parts.Length < start + 3) return false;
            if (!Number(parts[start], out float r) || !Number(parts[start + 1], out float g) || !Number(parts[start + 2], out float b)) return false;
            value = new Vector3(r, g, b);
            return true;
        }
    }
}
