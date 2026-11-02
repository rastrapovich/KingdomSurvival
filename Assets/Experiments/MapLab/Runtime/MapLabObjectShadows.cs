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
            foreach (Caster c in casters)
            {
                if (c.Building)
                {
                    // Сдвиг тени кровли — до пересечения с рельефом под ней.
                    Vector2 shift = Hit(data, c.Base + Vector3.up * c.Height, L) - new Vector2(c.Base.x, c.Base.z);
                    List<Vector2> hull = BuildingHull(c, shift);
                    Polygon(result.Cast, res, texel, hull);
                    Polygon(result.Contact, res, texel, Footprint(c, 0.25f));
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
            result.Milliseconds = (float)watch.Elapsed.TotalMilliseconds;
            return result;
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

        private static void Ellipse(float[] mask, int res, float texel, Vector2 center, Vector2 axis, float a, float b)
        {
            float reach = Mathf.Max(a, b) + texel * 2f;
            Vector2 perp = new Vector2(-axis.y, axis.x);
            Bounds(res, texel, center, reach, out int x0, out int x1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 rel = new Vector2((x + 0.5f) * texel, (z + 0.5f) * texel) - center;
                float u = Vector2.Dot(rel, axis) / a, v = Vector2.Dot(rel, perp) / b;
                // Расстояние до края эллипса в мире (приближённо) → сглаживание ~1 тексель.
                float q = Mathf.Sqrt(u * u + v * v);
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
