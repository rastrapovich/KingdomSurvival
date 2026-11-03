using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Этап 05: редкие акварельные пятна из четырёх Wash PNG, запечённые в краску
    // луга. Не подложка и не повтор: пятна редкие, в мире от seed. Плотность
    // пигмента — из альфы и тёмности рисунка (полный градиент, без порога);
    // цвет — местный луг, сдвинутый к оттенку пятна и чуть темнее: светлый RGB
    // рисунка не превращается в белые заплаты. Не на дорогах, воде, полях и во
    // дворах (там пятно не наносится по ключу покраски).
    public static class MapLabWash
    {
        public sealed class Source
        {
            public string Id;
            public float[] Density;  // 0..1, строки снизу вверх
            public int Width, Height;
            public Vector3 Chroma;   // оттенок пятна / его яркость
            public Vector2 Opacity = new Vector2(0.04f, 0.12f);
        }

        public sealed class Patch
        {
            public Source Source;
            public Vector2 Center;
            public Vector2 HalfSize;
            public float Cos, Sin;
            public float Opacity;
        }

        // Сетка корзин для быстрого поиска пятен у текселя.
        public sealed class Layer
        {
            public readonly List<Patch> Patches = new List<Patch>();
            public float Bucket = 16f;
            public int Side;
            public List<int>[] Buckets;
        }

        public static Source FromPixels(string id, Color32[] pixels, int width, int height, Vector2 opacity)
        {
            // Уменьшение до ~128 по длинной стороне: пятно низкочастотное.
            int factor = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(width, height) / 128f));
            int w = Mathf.Max(1, width / factor), h = Mathf.Max(1, height / factor);
            Source s = new Source { Id = id, Width = w, Height = h, Density = new float[w * h], Opacity = opacity };
            double cr = 0, cg = 0, cb = 0, cw = 0;
            float max = 1e-4f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a = 0f, dark = 0f;
                    for (int dy = 0; dy < factor; dy++)
                    for (int dx = 0; dx < factor; dx++)
                    {
                        Color32 c = pixels[(y * factor + dy) * width + x * factor + dx];
                        float alpha = c.a / 255f;
                        float luma = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                        a += alpha;
                        dark += alpha * (1f - luma);
                        cr += c.r * alpha; cg += c.g * alpha; cb += c.b * alpha; cw += alpha;
                    }
                    float inv = 1f / (factor * factor);
                    a *= inv;
                    dark *= inv;
                    // Плотность: альфа, усиленная там, где пигмент темнее (осадок по краю).
                    float d = a * 0.55f + dark * 0.9f;
                    s.Density[y * w + x] = d;
                    max = Mathf.Max(max, d);
                }
            }
            for (int i = 0; i < s.Density.Length; i++)
                s.Density[i] = Mathf.Clamp01(s.Density[i] / max);
            if (cw > 0)
            {
                Vector3 mean = new Vector3((float)(cr / cw), (float)(cg / cw), (float)(cb / cw)) / 255f;
                float l = Mathf.Max(0.05f, 0.299f * mean.x + 0.587f * mean.y + 0.114f * mean.z);
                s.Chroma = mean / l;
            }
            else
                s.Chroma = Vector3.one;
            return s;
        }

        // Размещение: дрожащая сетка в мире, по одному кандидату на ячейку с
        // вероятностью; только на лугу (не дорога, вода, поле, двор, круча).
        public static Layer Place(MapLabData data, MapLabSettings settings, List<Source> sources)
        {
            Layer layer = new Layer();
            MapLabStage05Settings s5 = settings.Stage05;
            if (sources == null || sources.Count == 0 || s5.WashDensity <= 0f || s5.WashStrength <= 0f)
                return Finish(layer, data);
            float cell = Mathf.Sqrt(10000f / Mathf.Max(0.5f, s5.WashDensity)) * 0.75f;
            int cells = Mathf.CeilToInt(data.Size / cell);
            MapLabRandom rng = MapLabRandom.ForStream(data.Seed, "paint05-wash", MapLabData.AlgorithmVersion);
            for (int cz = 0; cz < cells; cz++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    float chance = rng.Value();
                    Vector2 p = new Vector2((cx + rng.Value()) * cell, (cz + rng.Value()) * cell);
                    float scale = s5.WashScale * rng.Range(0.7f, 1.35f);
                    float angle = rng.Range(0f, Mathf.PI * 2f);
                    int pick = rng.Range(0, sources.Count);
                    float opacity = rng.Value();
                    if (chance > 0.56f || !Meadow(data, p))
                        continue;
                    Source src = sources[pick];
                    float aspect = src.Width / (float)Mathf.Max(1, src.Height);
                    layer.Patches.Add(new Patch
                    {
                        Source = src,
                        Center = p,
                        HalfSize = new Vector2(scale * 0.5f, scale * 0.5f / Mathf.Max(0.3f, aspect)),
                        Cos = Mathf.Cos(angle),
                        Sin = Mathf.Sin(angle),
                        Opacity = Mathf.Lerp(src.Opacity.x, src.Opacity.y, opacity),
                    });
                }
            }
            return Finish(layer, data);
        }

        private static bool Meadow(MapLabData data, Vector2 p)
        {
            if (!data.Contains(p.x, p.y))
                return false;
            if (data.IsUnderWater(p.x, p.y, 0.5f) || (data.WaterDistance != null && data.Bilinear(data.WaterDistance, p.x, p.y) < 4f))
                return false;
            if (data.RoadDistance != null && data.Bilinear(data.RoadDistance, p.x, p.y) < 3f)
                return false;
            if (data.SampleMask(data.Field, p.x, p.y) > 0.15f || data.SampleMask(data.Settlement, p.x, p.y) > 0.3f)
                return false;
            return data.SampleMask(data.Cliff, p.x, p.y) < 0.1f;
        }

        private static Layer Finish(Layer layer, MapLabData data)
        {
            layer.Side = Mathf.Max(1, Mathf.CeilToInt(data.Size / layer.Bucket) + 1);
            layer.Buckets = new List<int>[layer.Side * layer.Side];
            for (int i = 0; i < layer.Patches.Count; i++)
            {
                Patch p = layer.Patches[i];
                float r = Mathf.Max(p.HalfSize.x, p.HalfSize.y);
                int x0 = Mathf.Clamp(Mathf.FloorToInt((p.Center.x - r) / layer.Bucket), 0, layer.Side - 1);
                int x1 = Mathf.Clamp(Mathf.FloorToInt((p.Center.x + r) / layer.Bucket), 0, layer.Side - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((p.Center.y - r) / layer.Bucket), 0, layer.Side - 1);
                int z1 = Mathf.Clamp(Mathf.FloorToInt((p.Center.y + r) / layer.Bucket), 0, layer.Side - 1);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int k = z * layer.Side + x;
                    if (layer.Buckets[k] == null)
                        layer.Buckets[k] = new List<int>(2);
                    layer.Buckets[k].Add(i);
                }
            }
            return layer;
        }

        // Плотность пятен в точке (сумма, не больше 1) и средний оттенок.
        public static float Density(Layer layer, Vector2 w, out Vector3 chroma)
        {
            chroma = Vector3.one;
            if (layer == null || layer.Buckets == null)
                return 0f;
            int bx = Mathf.FloorToInt(w.x / layer.Bucket), bz = Mathf.FloorToInt(w.y / layer.Bucket);
            if (bx < 0 || bz < 0 || bx >= layer.Side || bz >= layer.Side)
                return 0f;
            List<int> list = layer.Buckets[bz * layer.Side + bx];
            if (list == null)
                return 0f;
            float total = 0f;
            Vector3 hue = Vector3.zero;
            for (int k = 0; k < list.Count; k++)
            {
                Patch p = layer.Patches[list[k]];
                Vector2 d = w - p.Center;
                float u = (d.x * p.Cos + d.y * p.Sin) / p.HalfSize.x;
                float v = (-d.x * p.Sin + d.y * p.Cos) / p.HalfSize.y;
                if (u <= -1f || u >= 1f || v <= -1f || v >= 1f)
                    continue;
                float value = Bilinear(p.Source, (u * 0.5f + 0.5f) * (p.Source.Width - 1), (v * 0.5f + 0.5f) * (p.Source.Height - 1)) * p.Opacity;
                total += value;
                hue += p.Source.Chroma * value;
            }
            if (total <= 0f)
                return 0f;
            chroma = hue / total;
            return Mathf.Min(0.2f, total);
        }

        public static Color Apply(Layer layer, Vector2 w, Color c, in MapLabPaintFields.Sample s, float strength)
        {
            float d = Density(layer, w, out Vector3 chroma);
            if (d <= 0f)
                return c;
            // Плотность из альфы и тёмности (opacity 0.04–0.12 по паспорту), сила —
            // ручка WashStrength относительно середины диапазона (0.08).
            float t = Mathf.Clamp01(d * strength / 0.08f);
            float k = Mathf.Pow(Mathf.Max(0.3f, chroma.x), 0.45f), g = Mathf.Pow(Mathf.Max(0.3f, chroma.y), 0.45f), b = Mathf.Pow(Mathf.Max(0.3f, chroma.z), 0.45f);
            Color target = new Color(c.r * k * 0.82f, c.g * g * 0.82f, c.b * b * 0.82f, 1f);
            Color result = Color.Lerp(c, target, Mathf.Clamp01(t * 4.5f));
            result.a = 1f;
            return result;
        }

        private static float Bilinear(Source s, float x, float y)
        {
            int x0 = Mathf.Clamp((int)x, 0, s.Width - 1), y0 = Mathf.Clamp((int)y, 0, s.Height - 1);
            int x1 = Mathf.Min(x0 + 1, s.Width - 1), y1 = Mathf.Min(y0 + 1, s.Height - 1);
            float fx = x - x0, fy = y - y0;
            float a = s.Density[y0 * s.Width + x0], b = s.Density[y0 * s.Width + x1];
            float c = s.Density[y1 * s.Width + x0], d = s.Density[y1 * s.Width + x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
    }
}
