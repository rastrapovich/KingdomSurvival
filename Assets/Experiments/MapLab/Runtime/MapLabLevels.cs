using System;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Уровни, как в Photoshop (19.11.2026): входные чёрная, белая и гамма
    // (средние тона), выходные чёрная и белая. Значения 0–255, как в Photoshop;
    // гамма — 1,00 без изменений, больше — светлее средние тона. Считаются в
    // sRGB поверх коррекции «яркость / тон / насыщенность». Рабочая настройка
    // облика: исходный PNG не меняется.
    [Serializable]
    public sealed class MapLabLevels
    {
        [Range(0f, 253f)] public float InputBlack;
        [Range(2f, 255f)] public float InputWhite = 255f;
        [Range(0.1f, 9.99f)] public float Gamma = 1f;
        [Range(0f, 255f)] public float OutputBlack;
        [Range(0f, 255f)] public float OutputWhite = 255f;

        public bool IsIdentity =>
            InputBlack < 0.01f && InputWhite > 254.99f && Mathf.Abs(Gamma - 1f) < 1e-3f &&
            OutputBlack < 0.01f && OutputWhite > 254.99f;

        // Для шейдеров (ноль — без изменений, так что пустой блок свойств
        // ничего не портит): x — вход чёрная, y — 1 − вход белая, z — гамма − 1,
        // w — выход чёрная; второй вектор: x — 1 − выход белая.
        public Vector4 Shader => IsIdentity
            ? Vector4.zero
            : new Vector4(InputBlack / 255f, 1f - InputWhite / 255f, Gamma - 1f, OutputBlack / 255f);

        public Vector4 ShaderOut => IsIdentity ? Vector4.zero : new Vector4(1f - OutputWhite / 255f, 0f, 0f, 0f);

        public string Key => IsIdentity ? "-" : $"{InputBlack:0.##}/{InputWhite:0.##}/{Gamma:0.###}/{OutputBlack:0.##}/{OutputWhite:0.##}";

        public MapLabLevels Clone() => new MapLabLevels
        {
            InputBlack = InputBlack, InputWhite = InputWhite, Gamma = Gamma, OutputBlack = OutputBlack, OutputWhite = OutputWhite
        };

        public void CopyFrom(MapLabLevels other)
        {
            if (other == null)
            {
                Reset();
                return;
            }
            InputBlack = other.InputBlack;
            InputWhite = other.InputWhite;
            Gamma = other.Gamma;
            OutputBlack = other.OutputBlack;
            OutputWhite = other.OutputWhite;
        }

        public void Reset()
        {
            InputBlack = 0f;
            InputWhite = 255f;
            Gamma = 1f;
            OutputBlack = 0f;
            OutputWhite = 255f;
        }

        public bool SameAs(MapLabLevels other) =>
            other != null && Mathf.Abs(InputBlack - other.InputBlack) < 1e-4f && Mathf.Abs(InputWhite - other.InputWhite) < 1e-4f &&
            Mathf.Abs(Gamma - other.Gamma) < 1e-5f && Mathf.Abs(OutputBlack - other.OutputBlack) < 1e-4f &&
            Mathf.Abs(OutputWhite - other.OutputWhite) < 1e-4f;

        // Значение sRGB 0..1 → 0..1 (то же, что MapLabLevels в шейдерах).
        public float Apply(float srgb)
        {
            float inB = InputBlack / 255f, inW = InputWhite / 255f;
            float s = Mathf.Clamp01((srgb - inB) / Mathf.Max(inW - inB, 1e-3f));
            s = Mathf.Pow(s, 1f / Mathf.Max(0.01f, Gamma));
            return Mathf.Clamp01(Mathf.Lerp(OutputBlack / 255f, OutputWhite / 255f, s));
        }
    }

    // Коррекция цвета на процессоре — для картинок, которые рисует интерфейс
    // (облака тумана), а не шейдеры MapLab. Тот же порядок и та же формула:
    // яркость / тон / насыщенность в линейном цвете (MapLabColorAdjust), затем
    // уровни в sRGB (MapLabLevels).
    public static class MapLabColorMath
    {
        public static bool IsNeutral(float brightness, float hue, float saturation, MapLabLevels levels) =>
            Mathf.Abs(brightness - 1f) < 1e-4f && Mathf.Abs(hue) < 1e-3f && Mathf.Abs(saturation - 1f) < 1e-4f &&
            (levels == null || levels.IsIdentity);

        // Новая текстура с коррекцией (RGBA32, sRGB, мип-карты) или null, если
        // коррекции нет. Источник может быть сжатым и нечитаемым: копия
        // снимается через временную цель отрисовки.
        public static Texture2D CreateAdjusted(Texture2D source, float brightness, float hue, float saturation, MapLabLevels levels)
        {
            if (source == null || IsNeutral(brightness, hue, saturation, levels))
                return null;
            int w = source.width, h = source.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            {
                name = source.name + " (коррекция)",
                wrapMode = source.wrapMode,
                filterMode = source.filterMode,
                anisoLevel = source.anisoLevel,
                hideFlags = HideFlags.DontSave
            };
            result.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            Color32[] pixels = result.GetPixels32();
            Adjust(pixels, brightness, hue, saturation, levels);
            result.SetPixels32(pixels);
            result.Apply(true, true);
            return result;
        }

        // Тень облака на земле (19.11.2026): размытый силуэт по альфе рисунка —
        // белый цвет (окраску задаёт подкраска элемента) и мягкая прозрачность.
        // Небольшая (ширина width px): тень мягкая, крупные пиксели не видны,
        // а считать её быстро. pad — поле с каждой стороны (доля ширины и
        // высоты), чтобы размытие не обрезалось краем холста; blur — радиус
        // размытия, доля ширины.
        public static Texture2D CreateShadow(Texture2D source, float pad, float blur, int width = 256)
        {
            if (source == null || source.width <= 0 || source.height <= 0)
                return null;
            pad = Mathf.Clamp(pad, 0f, 0.4f);
            int w = Mathf.Max(16, width);
            int h = Mathf.Max(8, Mathf.RoundToInt(w * source.height / (float)source.width));
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            // Рисунок — в середину с полями: uv' = (uv − pad) / (1 − 2·pad).
            float inner = 1f - 2f * pad;
            Graphics.Blit(source, rt, new Vector2(1f / inner, 1f / inner), new Vector2(-pad / inner, -pad / inner));
            RenderTexture.active = rt;
            Texture2D read = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] pixels = read.GetPixels32();
            UnityEngine.Object.DestroyImmediate(read);

            float[] alpha = new float[w * h];
            for (int i = 0; i < alpha.Length; i++)
            {
                // За краем рисунка (поле) — пусто, даже если выборка размазала край.
                int x = i % w, y = i / w;
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                bool inside = u >= pad && u <= 1f - pad && v >= pad && v <= 1f - pad;
                alpha[i] = inside ? pixels[i].a / 255f : 0f;
            }
            int radius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0f, blur) * w));
            float[] buffer = new float[alpha.Length];
            // Три прохода прямоугольного размытия ≈ гауссово.
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(alpha, buffer, w, h, radius, true);
                BoxBlur(buffer, alpha, w, h, radius, false);
            }
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha[i]) * 255f));

            Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            {
                name = source.name + " (тень)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            result.SetPixels32(pixels);
            // Маленькая — остаётся читаемой (проверки, повторный пересчёт).
            result.Apply(true, false);
            return result;
        }

        private static void BoxBlur(float[] src, float[] dst, int w, int h, int r, bool horizontal)
        {
            int length = horizontal ? w : h, lines = horizontal ? h : w;
            float norm = 1f / (2 * r + 1);
            for (int line = 0; line < lines; line++)
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++)
                    sum += Sample(src, w, h, line, Mathf.Clamp(k, 0, length - 1), horizontal);
                for (int i = 0; i < length; i++)
                {
                    int index = horizontal ? line * w + i : i * w + line;
                    dst[index] = sum * norm;
                    sum += Sample(src, w, h, line, Mathf.Min(i + r + 1, length - 1), horizontal);
                    sum -= Sample(src, w, h, line, Mathf.Max(i - r, 0), horizontal);
                }
            }
        }

        private static float Sample(float[] src, int w, int h, int line, int i, bool horizontal) =>
            horizontal ? src[line * w + i] : src[i * w + line];

        public static void Adjust(Color32[] pixels, float brightness, float hue, float saturation, MapLabLevels levels)
        {
            // Таблицы: sRGB-байт → линейный цвет; уровни — по каналу.
            float[] toLinear = new float[256];
            byte[] lut = new byte[256];
            bool hsv = Mathf.Abs(brightness - 1f) > 1e-4f || Mathf.Abs(hue) > 1e-3f || Mathf.Abs(saturation - 1f) > 1e-4f;
            for (int i = 0; i < 256; i++)
            {
                toLinear[i] = Mathf.Pow(i / 255f, 2.2f);
                float v = levels != null && !levels.IsIdentity ? levels.Apply(i / 255f) : i / 255f;
                lut[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
            }
            float cs = Mathf.Cos(hue * Mathf.Deg2Rad), sn = Mathf.Sin(hue * Mathf.Deg2Rad);
            for (int p = 0; p < pixels.Length; p++)
            {
                Color32 c = pixels[p];
                if (c.a == 0)
                    continue;
                byte r = c.r, g = c.g, b = c.b;
                if (hsv)
                {
                    float lr = toLinear[r], lg = toLinear[g], lb = toLinear[b];
                    float y = lr * 0.299f + lg * 0.587f + lb * 0.114f;
                    float i0 = lr * 0.596f - lg * 0.274f - lb * 0.322f;
                    float q0 = lr * 0.211f - lg * 0.523f + lb * 0.312f;
                    float i1 = (i0 * cs - q0 * sn) * saturation;
                    float q1 = (i0 * sn + q0 * cs) * saturation;
                    r = ToByte((y + 0.956f * i1 + 0.621f * q1) * brightness);
                    g = ToByte((y - 0.272f * i1 - 0.647f * q1) * brightness);
                    b = ToByte((y - 1.106f * i1 + 1.703f * q1) * brightness);
                }
                pixels[p] = new Color32(lut[r], lut[g], lut[b], c.a);
            }
        }

        private static byte ToByte(float linear) =>
            (byte)Mathf.RoundToInt(Mathf.Pow(Mathf.Clamp01(linear), 1f / 2.2f) * 255f);
    }
}
