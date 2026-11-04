using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Стилизованные тени объектов этапа 03, запечённые в мировую маску:
    // R — падающая тень (собранная форма), G — контактная у основания.
    // Формы объединяются максимумом, поэтому плотный лес не копит черноту.
    // Тень кроны — проекция центра кроны вдоль света до пересечения с
    // фактическим рельефом (на склоне и у уступа ложится туда, куда падает),
    // форма — приближённый эллипс кроны, не точная тень от силуэта спрайта.
    // Тень дома — заметание основания вдоль света на высоту кровли.
    public static class MapLabObjectShadows
    {
        public sealed class Result
        {
            public bool Gradient;
            public int Resolution;
            public float[] Cast;
            public float[] Contact;
            public int Shapes;
            public float Milliseconds;
        }

        public struct Caster
        {
            public Vector3 Base;
            public float Radius;        // радиус кроны (мир)
            public float CenterHeight;  // высота центра кроны над опорой
            public float ContactRadius;
            public bool Building;
            public Vector2 Size;        // основание постройки
            public float Yaw;           // градусы
            public float Height;        // высота кровли
        }

        public static Result Compute(MapLabData data, MapLabSettings settings, List<Caster> casters)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            int res = Mathf.Clamp(Mathf.CeilToInt(data.Size * Mathf.Clamp(settings.Stage03.ShadowTexelsPerUnit, 1f, 8f)), 64, 4096);
            Result result = new Result { Resolution = res, Cast = new float[res * res], Contact = new float[res * res] };
            float texel = data.Size / res;
            Vector3 L = settings.LightDirection;
            Vector2 lightDir = new Vector2(L.x, L.z);
            float horizontal = lightDir.magnitude;
            lightDir = horizontal > 1e-4f ? lightDir / horizontal : Vector2.right;
            float stretch = 1f / Mathf.Max(0.35f, L.y);
            // Этап 04: контакт — плавное поле плотности (1 у опоры → 0), а не
            // вторая бинарная форма; шейдер умножает его на маску падающей тени.
            bool gradient = settings.IsStage04;
            MapLabStage04Settings s4 = settings.Stage04;
            float lobes = gradient ? s4.CrownLobes : 0f;
            uint lobeSeed = (uint)MapLabRandom.StreamSeed(data.Seed, "shadow-lobes", MapLabData.AlgorithmVersion);
            result.Gradient = gradient;
            foreach (Caster c in casters)
            {
                if (c.Building)
                {
                    // Сдвиг тени кровли — до пересечения с рельефом под ней.
                    Vector2 shift = Hit(data, c.Base + Vector3.up * c.Height, L) - new Vector2(c.Base.x, c.Base.z);
                    List<Vector2> hull = BuildingHull(c, shift);
                    Polygon(result.Cast, res, texel, hull);
                    if (gradient)
                        PolygonFalloff(result.Contact, res, texel, Footprint(c, 0f), 1.2f * s4.ContactReach);
                    else
                        Polygon(result.Contact, res, texel, Footprint(c, 0.25f));
                }
                else if (gradient)
                {
                    // Этап 05: тень не отрывается от ствола — пятно кроны ближе к опоре
                    // (точка кроны ниже), от опоры к нему — сплошная расширяющаяся
                    // форма по рельефу (Hit — пересечение луча с землёй).
                    bool attached = settings.IsStage05;
                    Vector3 crown = c.Base + Vector3.up * c.CenterHeight * (attached ? 0.62f : 1f);
                    Vector2 hit = Hit(data, crown, L);
                    Vector2 foot = new Vector2(c.Base.x, c.Base.z);
                    // Стабильные крупные выступы контура: фаза — от места опоры.
                    float phase = MapLabRandom.Hash01(Mathf.RoundToInt(foot.x * 16f), Mathf.RoundToInt(foot.y * 16f), lobeSeed) * 6.283f;
                    Ellipse(result.Cast, res, texel, hit, lightDir, c.Radius * stretch, c.Radius, lobes, phase);
                    int neck = attached ? 6 : 3;
                    for (int k = 1; k <= neck; k++)
                    {
                        float t = k / (neck + 1f);
                        float r = attached ? Mathf.Lerp(c.ContactRadius * 1.7f, c.Radius * 0.95f, Mathf.Sqrt(t)) : Mathf.Lerp(c.ContactRadius * 1.1f, c.Radius * 0.85f, t);
                        Ellipse(result.Cast, res, texel, Vector2.Lerp(foot, hit, t), lightDir, r * Mathf.Lerp(1f, stretch, t), r, lobes * 0.5f, phase + k);
                    }
                    Ellipse(result.Cast, res, texel, foot, lightDir, c.ContactRadius * (attached ? 1.7f : 1.2f), c.ContactRadius * (attached ? 1.4f : 1.05f));
                    // Градиент: максимум у ствола, вытянут по свету к пятну кроны.
                    float reach = Mathf.Max(c.ContactRadius, c.Radius * s4.ContactReach);
                    Falloff(result.Contact, res, texel, Vector2.Lerp(foot, hit, 0.18f), lightDir, reach * 1.25f, reach * 0.8f);
                }
                else
                {
                    Vector3 crown = c.Base + Vector3.up * c.CenterHeight;
                    Vector2 hit = Hit(data, crown, L);
                    // Одна собранная форма: пятно кроны и «шейка» от ствола к нему,
                    // контактная тень лежит внутри неё, а не вторым кругом.
                    Vector2 foot = new Vector2(c.Base.x, c.Base.z);
                    Ellipse(result.Cast, res, texel, hit, lightDir, c.Radius * stretch, c.Radius);
                    for (int k = 1; k <= 3; k++)
                    {
                        float t = k / 4f;
                        float r = Mathf.Lerp(c.ContactRadius * 1.1f, c.Radius * 0.85f, t);
                        Ellipse(result.Cast, res, texel, Vector2.Lerp(foot, hit, t), lightDir, r * Mathf.Lerp(1f, stretch, t), r);
                    }
                    Ellipse(result.Cast, res, texel, foot, lightDir, c.ContactRadius * 1.2f, c.ContactRadius * 1.05f);
                    Ellipse(result.Contact, res, texel, foot, lightDir, c.ContactRadius * 0.9f, c.ContactRadius * 0.75f);
                }
                result.Shapes++;
            }
            if (settings.IsStage05 && settings.Stage05.ShadowEdgeLoss > 0f)
                LiveEdge(result.Cast, res, texel, data.Seed, settings.Stage05);
            result.Milliseconds = (float)watch.Elapsed.TotalMilliseconds;
            return result;
        }

        // Этап 05: живой внешний край. Единый градиент от опоры сохраняется (поле
        // контакта не трогаем); у внешнего края падающей тени в отдельных зонах —
        // крупнее пиксельного шума и стабильных в мире (шум от seed и мировых
        // координат) — плотность плавно уходит к краю, в остальных край остаётся
        // чётким. Близость к краю — по размытой копии маски (без жёстких углов).
        public static void LiveEdge(float[] cast, int res, float texel, int seed, MapLabStage05Settings s5)
        {
            int radius = Mathf.Clamp(Mathf.RoundToInt(0.9f / texel), 1, 12);
            float[] blur = BoxBlur(cast, res, radius);
            blur = BoxBlur(blur, res, radius);
            MapLabNoise zones = new MapLabNoise(MapLabRandom.StreamSeed(seed, "shadow05-edge", MapLabData.AlgorithmVersion));
            float scale = Mathf.Max(1f, s5.ShadowEdgeScale);
            float loss = Mathf.Clamp01(s5.ShadowEdgeLoss);
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = z * res + x;
                    float c = cast[i];
                    if (c <= 0f || blur[i] > 0.97f)
                        continue;
                    float wx = (x + 0.5f) * texel, wz = (z + 0.5f) * texel;
                    float zone = zones.Fbm(wx / scale, wz / scale, 2) * 0.5f + 0.5f;
                    // Доля зон с потерей плотности ~ loss; переход между зонами плавный.
                    float soft = MapLabGeometry.SmoothStep(1f - loss - 0.08f, 1f - loss + 0.08f, zone);
                    if (soft <= 0f)
                        continue;
                    float inner = MapLabGeometry.SmoothStep(0.3f, 0.95f, blur[i]);
                    cast[i] = c * Mathf.Lerp(1f, inner, soft);
                }
            }
        }

        private static float[] BoxBlur(float[] src, int res, int r)
        {
            float[] tmp = new float[src.Length];
            float[] dst = new float[src.Length];
            float inv = 1f / (r * 2 + 1);
            for (int z = 0; z < res; z++)
            {
                float sum = 0f;
                int row = z * res;
                for (int x = -r; x <= r; x++)
                    sum += src[row + Mathf.Clamp(x, 0, res - 1)];
                for (int x = 0; x < res; x++)
                {
                    tmp[row + x] = sum * inv;
                    sum += src[row + Mathf.Min(res - 1, x + r + 1)] - src[row + Mathf.Max(0, x - r)];
                }
            }
            for (int x = 0; x < res; x++)
            {
                float sum = 0f;
                for (int z = -r; z <= r; z++)
                    sum += tmp[Mathf.Clamp(z, 0, res - 1) * res + x];
                for (int z = 0; z < res; z++)
                {
                    dst[z * res + x] = sum * inv;
                    sum += tmp[Mathf.Min(res - 1, z + r + 1) * res + x] - tmp[Mathf.Max(0, z - r) * res + x];
                }
            }
            return dst;
        }

        // Точка, где луч от центра кроны против света встречает рельеф.
        public static Vector2 Hit(MapLabData data, Vector3 from, Vector3 light)
        {
            Vector3 dir = -light;
            float step = 0.25f;
            Vector3 p = from;
            for (int i = 0; i < 400; i++)
            {
                Vector3 q = p + dir * step;
                if (!data.Contains(q.x, q.z))
                    return new Vector2(q.x, q.z);
                if (q.y <= data.SampleHeight(q.x, q.z))
                {
                    // Уточнение пересечения делением отрезка.
                    Vector3 a = p, b = q;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 m = (a + b) * 0.5f;
                        if (m.y <= data.SampleHeight(m.x, m.z)) b = m; else a = m;
                    }
                    return new Vector2(b.x, b.z);
                }
                p = q;
            }
            return new Vector2(p.x, p.z);
        }

        private static List<Vector2> Footprint(Caster c, float grow)
        {
            float yaw = c.Yaw * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw)), v = new Vector2(-u.y, u.x);
            Vector2 center = new Vector2(c.Base.x, c.Base.z);
            // Основание по рисунку: нарисованный сруб мельче по глубине, чем
            // участок размещения (Size — с запасом под двор и ограду).
            float hu = c.Size.x * 0.5f * 0.95f + grow, hv = c.Size.y * 0.5f * 0.7f + grow;
            return new List<Vector2> { center - u * hu - v * hv, center + u * hu - v * hv, center + u * hu + v * hv, center - u * hu + v * hv };
        }

        private static List<Vector2> BuildingHull(Caster c, Vector2 shift)
        {
            List<Vector2> points = Footprint(c, 0.05f);
            int count = points.Count;
            // Конёк — по середине: тень кровли уже основания на треть.
            for (int i = 0; i < count; i++)
                points.Add(Vector2.Lerp(points[i], Vector2.Lerp(points[i], points[(i + 2) % count], 0.5f), 0.35f) + shift);
            return ConvexHull(points);
        }

        private static List<Vector2> ConvexHull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            List<Vector2> hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 p = points[pass == 0 ? i : points.Count - 1 - i];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }

        // Плавное поле плотности эллипса: 1 в центре, 0 на краю (без порога).
        private static void Falloff(float[] mask, int res, float texel, Vector2 center, Vector2 axis, float a, float b)
        {
            float reach = Mathf.Max(a, b) + texel * 2f;
            Vector2 perp = new Vector2(-axis.y, axis.x);
            Bounds(res, texel, center, reach, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 rel = new Vector2((x + 0.5f) * texel, (z + 0.5f) * texel) - center;
                float u = Vector2.Dot(rel, axis) / a, v = Vector2.Dot(rel, perp) / b;
                float q = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                float density = q * q * (3f - 2f * q);
                int i = z * res + x;
                if (density > mask[i])
                    mask[i] = density;
            }
        }

        // Плотность у основания постройки: 1 под стенами, плавно к нулю за reach.
        private static void PolygonFalloff(float[] mask, int res, float texel, List<Vector2> poly, float reach)
        {
            Vector2 min = poly[0], max = poly[0];
            foreach (Vector2 p in poly)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            int x0 = Mathf.Clamp(Mathf.FloorToInt((min.x - reach) / texel), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((max.x + reach) / texel), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((min.y - reach) / texel), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((max.y + reach) / texel), 0, res - 1);
            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
                area += Cross(Vector2.zero, poly[i], poly[(i + 1) % poly.Count]);
            float sign = area >= 0f ? 1f : -1f;
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) * texel, (z + 0.5f) * texel);
                float d = float.MinValue;
                for (int k = 0; k < poly.Count; k++)
                {
                    Vector2 a = poly[k], b = poly[(k + 1) % poly.Count];
                    Vector2 e = (b - a).normalized;
                    d = Mathf.Max(d, Vector2.Dot(p - a, new Vector2(e.y, -e.x) * sign));
                }
                float q = 1f - Mathf.Clamp01(d / reach);
                float density = q * q * (3f - 2f * q);
                int i = z * res + x;
                if (density > mask[i])
                    mask[i] = density;
            }
        }

        private static void Ellipse(float[] mask, int res, float texel, Vector2 center, Vector2 axis, float a, float b, float lobes = 0f, float phase = 0f)
        {
            float reach = Mathf.Max(a, b) * (1f + lobes * 0.2f) + texel * 2f;
            Vector2 perp = new Vector2(-axis.y, axis.x);
            Bounds(res, texel, center, reach, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 rel = new Vector2((x + 0.5f) * texel, (z + 0.5f) * texel) - center;
                float u = Vector2.Dot(rel, axis) / a, v = Vector2.Dot(rel, perp) / b;
                // Расстояние до края эллипса в мире (приближённо) → сглаживание ~1 тексель.
                float q = Mathf.Sqrt(u * u + v * v);
                if (lobes > 0f)
                {
                    // Несколько крупных согласованных выступов, не рябь.
                    float angle = Mathf.Atan2(v, u);
                    q /= 1f + lobes * (0.11f * Mathf.Sin(3f * angle + phase) + 0.07f * Mathf.Sin(5f * angle + phase * 1.7f));
                }
                float d = (q - 1f) * Mathf.Min(a, b);
                float cover = Mathf.Clamp01(0.5f - d / (texel * 1.2f));
                int i = z * res + x;
                if (cover > mask[i])
                    mask[i] = cover;
            }
        }

        private static void Polygon(float[] mask, int res, float texel, List<Vector2> poly)
        {
            if (poly.Count < 3)
                return;
            Vector2 min = poly[0], max = poly[0];
            foreach (Vector2 p in poly)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            int x0 = Mathf.Clamp(Mathf.FloorToInt(min.x / texel) - 2, 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(max.x / texel) + 2, 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(min.y / texel) - 2, 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt(max.y / texel) + 2, 0, res - 1);
            // Обход против часовой стрелки: внутри — все ребра слева.
            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
                area += Cross(Vector2.zero, poly[i], poly[(i + 1) % poly.Count]);
            float sign = area >= 0f ? 1f : -1f;
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) * texel, (z + 0.5f) * texel);
                float d = float.MinValue;
                for (int k = 0; k < poly.Count; k++)
                {
                    Vector2 a = poly[k], b = poly[(k + 1) % poly.Count];
                    Vector2 e = (b - a).normalized;
                    Vector2 n = new Vector2(e.y, -e.x) * sign;
                    d = Mathf.Max(d, Vector2.Dot(p - a, n));
                }
                float cover = Mathf.Clamp01(0.5f - d / (texel * 1.2f));
                int i = z * res + x;
                if (cover > mask[i])
                    mask[i] = cover;
            }
        }

        private static void Bounds(int res, float texel, Vector2 center, float reach, out int x0, out int x1, out int z0, out int z1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt((center.x - reach) / texel), 0, res - 1);
            x1 = Mathf.Clamp(Mathf.CeilToInt((center.x + reach) / texel), 0, res - 1);
            z0 = Mathf.Clamp(Mathf.FloorToInt((center.y - reach) / texel), 0, res - 1);
            z1 = Mathf.Clamp(Mathf.CeilToInt((center.y + reach) / texel), 0, res - 1);
        }
    }
}
