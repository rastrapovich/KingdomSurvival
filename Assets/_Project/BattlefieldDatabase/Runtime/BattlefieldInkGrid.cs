using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // Перо для сетки боя — параметры пера дорог MapLab (MAP_STYLE_GUIDE.md §5
    // «Единое перо», §2.5 «Свободные окончания»). Значения по умолчанию —
    // профиль рисунка игры (Stage05); мост MapLab подставляет текущие значения
    // профиля при запуске (MapLabBridge). Длины — в радиусах гекса: радиус
    // клетки арены MapLab — 3 ед. мира, перо дорог задано в ед. мира.
    [Serializable]
    public sealed class BattlefieldInkPen
    {
        public Color Color = new Color(0.28f, 0.2f, 0.12f, 1f);
        // Толщина пера в радиусах гекса (0,13 ед. / 3 ед.).
        public float WidthShare = 0.13f / 3f;
        public float MinPixels = 1.6f;
        public float MaxPixels = 6f;
        // Плотность пигмента (непрозрачность в середине линии).
        public float Density = 0.88f;
        // Плавная смена толщины: доля и длина волны, в радиусах.
        public float Drift = 0.45f;
        public float DriftLength = 7f / 3f;
        // Разрывы: шаг между разрывами, в радиусах, и доля разрыва.
        public float BreakLength = 9f / 3f;
        public float BreakShare = 0.1f;
        // Зерно: мелкая неровность пигмента вдоль линии.
        public float Grain = 0.4f;
        // Свободные окончания у разрывов (§2.5).
        public bool Endings = true;

        public static BattlefieldInkPen Current { get; set; } = new BattlefieldInkPen();

        public BattlefieldInkPen Clone() => (BattlefieldInkPen)MemberwiseClone();
    }

    // Рисованная сетка гексов: каждое общее ребро — один раз (без двойных
    // линий), штрих тушью с нажимом, дрожью руки, редкими разрывами и
    // свободными окончаниями. Рисунок привязан к положению ребра в решётке
    // (в радиусах от первой клетки), поэтому при зуме и перерисовке не
    // меняется. У углов линия сужается — на стыке трёх рёбер нет кляксы.
    // Только вид: клетки, ходы и цели от неё не зависят.
    public static class BattlefieldInkGrid
    {
        // Одна полилиния штриха: точки, полуширина и непрозрачность в точках.
        public sealed class Stroke
        {
            public readonly List<Vector2> Points = new List<Vector2>();
            public readonly List<float> HalfWidths = new List<float>();
            public readonly List<float> Alphas = new List<float>();
        }

        public sealed class Result
        {
            public List<Stroke> Strokes;
            public int Edges;
            public int Gaps;
            public int Endings;
        }

        // Варианты оформления сетки (доля плотности — сетка спокойнее фигур).
        public struct Options
        {
            public float Opacity;       // множитель плотности пера, 0..1
            public float PixelScale;    // пикселей экрана на единицу рисунка (для пределов толщины)
            public bool Endings;
        }

        private const int Steps = 10;

        // centers — центры гексов (плоские, до подъёма рельефом); radius —
        // радиус клетки (используется, если клетка одна); verticalScale —
        // сжатие по высоте; raise — подъём точки по рельефу (null — без него).
        public static Result Build(IReadOnlyList<Vector2> centers, float radius, float verticalScale,
            Func<Vector2, Vector2> raise, BattlefieldInkPen pen, Options options)
        {
            Result result = new Result { Strokes = new List<Stroke>() };
            if (centers == null || centers.Count == 0 || pen == null)
                return result;
            verticalScale = Mathf.Max(0.05f, verticalScale);
            float lattice = LatticeRadius(centers, verticalScale, radius);
            if (lattice <= 0.01f)
                return result;
            Vector2 origin = centers[0];
            float quantum = lattice * 0.125f;
            HashSet<long> seen = new HashSet<long>();
            Vector2[] corners = new Vector2[6];
            float pixelScale = Mathf.Max(0.01f, options.PixelScale);
            float baseWidth = Mathf.Clamp(pen.WidthShare * lattice * pixelScale, pen.MinPixels, Mathf.Max(pen.MinPixels, pen.MaxPixels)) / pixelScale;
            float density = Mathf.Clamp01(pen.Density * Mathf.Clamp01(options.Opacity));
            foreach (Vector2 center in centers)
            {
                for (int i = 0; i < 6; i++)
                {
                    float angle = Mathf.Deg2Rad * (60f * i - 30f);
                    corners[i] = center + new Vector2(Mathf.Cos(angle) * lattice, Mathf.Sin(angle) * lattice * verticalScale);
                }
                for (int i = 0; i < 6; i++)
                {
                    Vector2 a = corners[i], b = corners[(i + 1) % 6];
                    long ka = Key(a, origin, quantum, verticalScale), kb = Key(b, origin, quantum, verticalScale);
                    long edgeKey = ka < kb ? ka * 1000003L + kb : kb * 1000003L + ka;
                    if (!seen.Add(edgeKey))
                        continue;
                    // Ребро рисуется от меньшего ключа к большему — у соседей одинаково.
                    if (kb < ka)
                        (a, b) = (b, a);
                    result.Edges++;
                    DrawEdge(result, a, b, lattice, verticalScale, raise, pen, baseWidth, density, options.Endings && pen.Endings, (uint)(edgeKey ^ (edgeKey >> 29)));
                }
            }
            return result;
        }

        // Радиус решётки по расстоянию между соседними центрами (√3·R); у
        // одиночной клетки — переданный радиус.
        private static float LatticeRadius(IReadOnlyList<Vector2> centers, float verticalScale, float fallback)
        {
            float best = float.MaxValue;
            int limit = Mathf.Min(centers.Count, 64);
            for (int i = 0; i < limit; i++)
                for (int j = i + 1; j < centers.Count; j++)
                {
                    Vector2 d = centers[j] - centers[i];
                    d.y /= verticalScale;
                    float m = d.magnitude;
                    if (m > 0.001f && m < best)
                        best = m;
                }
            return best < float.MaxValue ? best / Mathf.Sqrt(3f) : fallback;
        }

        private static long Key(Vector2 p, Vector2 origin, float quantum, float verticalScale)
        {
            long x = Mathf.RoundToInt((p.x - origin.x) / quantum);
            long y = Mathf.RoundToInt((p.y - origin.y) / verticalScale / quantum);
            return (x + 100000L) * 200003L + (y + 100000L);
        }

        private static void DrawEdge(Result result, Vector2 a, Vector2 b, float lattice, float verticalScale, Func<Vector2, Vector2> raise,
            BattlefieldInkPen pen, float baseWidth, float density, bool endings, uint seed)
        {
            Vector2 along = b - a;
            float length = along.magnitude;
            if (length < 1e-4f)
                return;
            Vector2 normal = new Vector2(-along.y, along.x) / length;
            // Разрыв: примерно раз на BreakLength радиусов, длиной около доли
            // шага; только в середине ребра — углы клеток остаются читаемыми.
            float edgeShare = length / lattice;
            float gapChance = Mathf.Clamp01(edgeShare / Mathf.Max(0.5f, pen.BreakLength) * Mathf.Clamp01(pen.BreakShare * 10f));
            float g0 = -1f, g1 = -1f;
            if (Hash01(seed, 1) < gapChance)
            {
                float gapLength = Mathf.Clamp(pen.BreakShare * pen.BreakLength / edgeShare * Mathf.Lerp(0.6f, 1.4f, Hash01(seed, 2)), 0.1f, 0.26f);
                float middle = Mathf.Lerp(0.3f, 0.7f, Hash01(seed, 3));
                g0 = Mathf.Clamp(middle - gapLength * 0.5f, 0.22f, 0.78f - gapLength);
                g1 = g0 + gapLength;
                result.Gaps++;
            }
            // Ослабленный участок пигмента (у части рёбер).
            float fadeCenter = Hash01(seed, 4), fadeDepth = Hash01(seed, 5) < 0.45f ? Mathf.Lerp(0.45f, 0.8f, Hash01(seed, 6)) : 0f;
            // Нажим — плавная волна вдоль ребра со сдвигом по положению ребра:
            // соседние рёбра продолжают одну и ту же «руку», но не повторяют рисунок.
            float wave = lattice * Mathf.Max(0.5f, pen.DriftLength);
            float phase = Hash01(seed, 7) * 10f;
            float wobble = 0.028f * lattice;
            Vector2 lift(float t) => raise != null ? raise(Vector2.Lerp(a, b, t)) : Vector2.Lerp(a, b, t);

            Stroke current = null;
            void Point(float t, float widthMul, float alphaMul)
            {
                // Дрожь руки: смещение поперёк, у углов — ноль (рёбра сходятся точно).
                float pin = Mathf.Sin(Mathf.PI * t);
                float off = (Smooth(seed + 11u, t * 2.3f) * 2f - 1f) * wobble * pin;
                Vector2 p = lift(t) + normal * off;
                float s = t * length / wave + phase;
                float press = 1f + pen.Drift * (Smooth(seed + 23u, s) * 2f - 1f);
                // Сужение к углам: на стыке трёх рёбер не собирается клякса.
                float corner = Mathf.Lerp(0.45f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) / 0.16f));
                float grain = 1f - pen.Grain * 0.18f * Hash01(seed + (uint)Mathf.RoundToInt(t * 97f), 9);
                float fade = 1f - fadeDepth * Mathf.Exp(-Mathf.Pow((t - fadeCenter) / 0.18f, 2f));
                current.Points.Add(p);
                current.HalfWidths.Add(baseWidth * 0.5f * press * corner * widthMul);
                current.Alphas.Add(density * grain * fade * alphaMul);
            }

            void Line(float t0, float t1, float taper0, float taper1)
            {
                if (t1 - t0 < 0.01f)
                    return;
                current = new Stroke();
                int steps = Mathf.Max(2, Mathf.CeilToInt(Steps * (t1 - t0)));
                for (int i = 0; i <= steps; i++)
                {
                    float t = Mathf.Lerp(t0, t1, i / (float)steps);
                    // Заострение у разрыва: последняя доля — к нулю.
                    float w = 1f;
                    if (taper0 > 0f) w *= Mathf.Lerp(0.25f, 1f, Mathf.Clamp01((t - t0) / taper0));
                    if (taper1 > 0f) w *= Mathf.Lerp(0.25f, 1f, Mathf.Clamp01((t1 - t) / taper1));
                    Point(t, w, 1f);
                }
                result.Strokes.Add(current);
            }

            if (g0 < 0f)
            {
                Line(0f, 1f, 0f, 0f);
                return;
            }
            float taper = Mathf.Clamp(baseWidth * 2.2f / length, 0.02f, 0.08f);
            Line(0f, g0, 0f, taper);
            Line(g1, 1f, taper, 0f);
            if (!endings || Hash01(seed, 12) > 0.65f)
                return;
            // Свободное окончание (§2.5): с одной стороны разрыва — просвет,
            // короткий штрих по ходу линии и одна-две уменьшающиеся точки; не
            // дальше половины просвета (хвосты двух концов не встречаются).
            bool fromStart = Hash01(seed, 13) < 0.5f;
            float span = (g1 - g0) * 0.5f;
            float dir = fromStart ? 1f : -1f;
            float origin = fromStart ? g0 : g1;
            float dashFrom = origin + dir * span * Mathf.Lerp(0.18f, 0.3f, Hash01(seed, 14));
            float dashTo = dashFrom + dir * span * Mathf.Lerp(0.28f, 0.42f, Hash01(seed, 15));
            current = new Stroke();
            for (int i = 0; i <= 3; i++)
                Point(Mathf.Lerp(dashFrom, dashTo, i / 3f), Mathf.Lerp(0.75f, 0.35f, i / 3f), 0.85f);
            result.Strokes.Add(current);
            result.Endings++;
            int dots = Hash01(seed, 16) < 0.55f ? 1 : 2;
            float at = dashTo;
            for (int d = 0; d < dots; d++)
            {
                at += dir * span * Mathf.Lerp(0.16f, 0.24f, Hash01(seed, 17u + (uint)d));
                if ((at - origin) * dir > span)
                    break;
                float dotLen = baseWidth * Mathf.Lerp(0.9f, 1.4f, Hash01(seed, 19u + (uint)d)) / length;
                current = new Stroke();
                float size = d == 0 ? 0.62f : 0.42f;
                Point(at - dir * dotLen * 0.5f, size * 0.6f, 0.75f);
                Point(at, size, 0.8f);
                Point(at + dir * dotLen * 0.5f, size * 0.6f, 0.75f);
                result.Strokes.Add(current);
            }
        }

        // ---------- треугольники ----------

        // Полилинии → лента треугольников с мягким краем (feather — ширина
        // растушёвки, в единицах рисунка): внутри — пигмент, к краю — ноль.
        // Без наложений внутри штриха (нет тёмных «бусин» на стыках отрезков).
        public static void Triangulate(List<Stroke> strokes, float feather, List<Vector2> positions, List<float> alphas, List<int> indices)
        {
            positions.Clear();
            alphas.Clear();
            indices.Clear();
            if (strokes == null)
                return;
            feather = Mathf.Max(0f, feather);
            foreach (Stroke stroke in strokes)
            {
                int n = stroke.Points.Count;
                if (n < 2)
                    continue;
                int start = positions.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector2 prev = stroke.Points[Mathf.Max(0, i - 1)], next = stroke.Points[Mathf.Min(n - 1, i + 1)];
                    Vector2 tangent = next - prev;
                    if (tangent.sqrMagnitude < 1e-10f)
                        tangent = Vector2.right;
                    tangent.Normalize();
                    Vector2 normal = new Vector2(-tangent.y, tangent.x);
                    float half = Mathf.Max(0f, stroke.HalfWidths[i]);
                    float a = Mathf.Clamp01(stroke.Alphas[i]);
                    // Тоньше растушёвки — бледнее, а не тоньше пикселя (иначе пропадает).
                    if (half * 2f < feather)
                    {
                        a *= Mathf.Max(0.15f, half * 2f / Mathf.Max(1e-4f, feather));
                        half = feather * 0.5f;
                    }
                    float inner = Mathf.Max(0f, half - feather * 0.5f), outer = half + feather * 0.5f;
                    Vector2 p = stroke.Points[i];
                    positions.Add(p - normal * outer); alphas.Add(0f);
                    positions.Add(p - normal * inner); alphas.Add(a);
                    positions.Add(p + normal * inner); alphas.Add(a);
                    positions.Add(p + normal * outer); alphas.Add(0f);
                }
                for (int i = 0; i < n - 1; i++)
                {
                    int s0 = start + i * 4, s1 = s0 + 4;
                    for (int k = 0; k < 3; k++)
                    {
                        indices.Add(s0 + k); indices.Add(s1 + k); indices.Add(s1 + k + 1);
                        indices.Add(s0 + k); indices.Add(s1 + k + 1); indices.Add(s0 + k + 1);
                    }
                }
            }
        }

        // ---------- стабильный шум ----------

        private static float Hash01(uint seed, uint salt)
        {
            unchecked
            {
                uint h = seed * 0x9E3779B1u ^ salt * 0x85EBCA77u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        // Гладкий одномерный шум 0..1.
        private static float Smooth(uint seed, float x)
        {
            float i = Mathf.Floor(x), f = x - i;
            float u = f * f * (3f - 2f * f);
            uint k = (uint)(int)i;
            return Mathf.Lerp(Hash01(seed, k), Hash01(seed, k + 1u), u);
        }
    }
}
