using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace KingdomSurvival.MapLab
{
    // Внешняя grayscale-карта высот читается как данные: без sRGB-декодирования
    // и без изменения настроек импорта исходного ассета.
    public static class MapLabHeightmapReader
    {
        public static float[] Read(Texture2D texture, int resolution, out string warning)
        {
            warning = null;
            if (texture == null)
                return null;
            float[] source;
            int width = texture.width;
            int height = texture.height;
            if (texture.isReadable)
            {
                // GetPixels отдаёт значения как они хранятся (без sRGB-перевода).
                Color[] pixels = texture.GetPixels();
                source = new float[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                    source[i] = pixels[i].r;
            }
            else
            {
                source = ReadThroughGpu(texture, texture.isDataSRGB);
                if (source == null)
                {
                    warning = "Карта высот не читается: включите Read/Write или назначьте другую карту.";
                    return null;
                }
            }
            GraphicsFormat format = texture.graphicsFormat;
            if (GraphicsFormatUtility.GetComponentCount(format) > 0 && GraphicsFormatUtility.GetBlockSize(format) <= 4 && !GraphicsFormatUtility.IsHalfFormat(format))
                warning = "Карта высот 8-битная — на плавных склонах возможны ступени; лучше 16 бит (R16).";
            if (texture.isDataSRGB)
                warning = (warning == null ? "" : warning + " ") + "Текстура импортирована как sRGB: значения переведены обратно в линейные данные; лучше снять sRGB у импорта.";

            float[] result = new float[resolution * resolution];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)(resolution - 1) * (width - 1);
                    float v = z / (float)(resolution - 1) * (height - 1);
                    int x0 = Mathf.Clamp((int)u, 0, width - 1);
                    int y0 = Mathf.Clamp((int)v, 0, height - 1);
                    int x1 = Mathf.Min(x0 + 1, width - 1);
                    int y1 = Mathf.Min(y0 + 1, height - 1);
                    float fx = u - x0;
                    float fy = v - y0;
                    float a = source[y0 * width + x0];
                    float b = source[y0 * width + x1];
                    float c = source[y1 * width + x0];
                    float d = source[y1 * width + x1];
                    result[z * resolution + x] = Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
                }
            }
            return result;
        }

        // Значение карты (0..1) → доля диапазона высот по Invert, Remap, Scale, Offset.
        public static float Remap(float value, MapLabReliefSettings relief)
        {
            float v = Mathf.Clamp01(float.IsNaN(value) ? 0f : value);
            if (relief.ExternalInvert)
                v = 1f - v;
            if (relief.ExternalRemap != null && relief.ExternalRemap.length > 0)
                v = relief.ExternalRemap.Evaluate(v);
            return v * relief.ExternalScale + relief.ExternalOffset;
        }

        private static float[] ReadThroughGpu(Texture2D texture, bool srgb)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            Texture2D copy = null;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(texture.width, texture.height, TextureFormat.RFloat, false, true);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                copy.Apply(false, false);
                Color[] pixels = copy.GetPixels();
                float[] values = new float[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                    values[i] = srgb ? Mathf.LinearToGammaSpace(pixels[i].r) : pixels[i].r;
                return values;
            }
            catch (System.Exception)
            {
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null)
                    Object.DestroyImmediate(copy);
            }
        }
    }
}
