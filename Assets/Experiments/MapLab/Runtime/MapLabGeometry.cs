using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    public static class MapLabGeometry
    {
        // Сплайн Catmull-Rom через контрольные точки с шагом step (ед.).
        public static List<Vector2> CatmullRom(IList<Vector2> control, float step)
        {
            List<Vector2> result = new List<Vector2>();
            if (control == null || control.Count == 0)
                return result;
            if (control.Count == 1)
            {
                result.Add(control[0]);
                return result;
            }
            step = Mathf.Max(0.05f, step);
            for (int i = 0; i < control.Count - 1; i++)
            {
                Vector2 p0 = control[Mathf.Max(0, i - 1)];
                Vector2 p1 = control[i];
                Vector2 p2 = control[i + 1];
                Vector2 p3 = control[Mathf.Min(control.Count - 1, i + 2)];
                float length = Vector2.Distance(p1, p2);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / step));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    result.Add(CatmullPoint(p0, p1, p2, p3, t));
                }
            }
            result.Add(control[control.Count - 1]);
            return result;
        }

        public static Vector2 CatmullPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // Равномерная переразбивка ломаной.
        public static List<Vector2> Resample(IList<Vector2> points, float spacing)
        {
            List<Vector2> result = new List<Vector2>();
            if (points == null || points.Count == 0)
                return result;
            spacing = Mathf.Max(0.05f, spacing);
            result.Add(points[0]);
            float carry = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a = points[i - 1];
                Vector2 b = points[i];
                float length = Vector2.Distance(a, b);
                if (length < 1e-6f)
                    continue;
                float pos = spacing - carry;
                while (pos <= length)
                {
                    result.Add(Vector2.Lerp(a, b, pos / length));
                    pos += spacing;
                }
                carry = length - (pos - spacing);
            }
            if (Vector2.Distance(result[result.Count - 1], points[points.Count - 1]) > spacing * 0.3f)
                result.Add(points[points.Count - 1]);
            else
                result[result.Count - 1] = points[points.Count - 1];
            return result;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            t = lengthSq > 1e-9f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        // Ближайшая точка ломаной: расстояние, номер сегмента, параметр на нём,
        // знак стороны (+ слева по ходу) и касательная.
        public static float Nearest(IList<Vector2> line, Vector2 p, out int segment, out float t, out float side, out Vector2 tangent)
        {
            float best = float.MaxValue;
            segment = 0;
            t = 0f;
            side = 1f;
            tangent = Vector2.right;
            for (int i = 0; i < line.Count - 1; i++)
            {
                float d = DistanceToSegment(p, line[i], line[i + 1], out float st);
                if (d < best)
                {
                    best = d;
                    segment = i;
                    t = st;
                }
            }
            if (line.Count >= 2)
            {
                Vector2 a = line[segment];
                Vector2 b = line[segment + 1];
                tangent = (b - a).sqrMagnitude > 1e-9f ? (b - a).normalized : Vector2.right;
                Vector2 rel = p - (a + (b - a) * t);
                side = Cross(tangent, rel) >= 0f ? 1f : -1f;
            }
            return best;
        }

        public static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        public static Vector2 LeftNormal(Vector2 tangent)
        {
            return new Vector2(-tangent.y, tangent.x);
        }

        // Накопленная длина до каждой точки.
        public static float[] Stations(IList<Vector2> line)
        {
            float[] stations = new float[line.Count];
            for (int i = 1; i < line.Count; i++)
                stations[i] = stations[i - 1] + Vector2.Distance(line[i - 1], line[i]);
            return stations;
        }

        public static bool SegmentIntersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point, out float ta, out float tc)
        {
            point = Vector2.zero;
            ta = tc = 0f;
            Vector2 r = b - a;
            Vector2 s = d - c;
            float denom = Cross(r, s);
            if (Mathf.Abs(denom) < 1e-9f)
                return false;
            Vector2 ca = c - a;
            ta = Cross(ca, s) / denom;
            tc = Cross(ca, r) / denom;
            if (ta < 0f || ta > 1f || tc < 0f || tc > 1f)
                return false;
            point = a + r * ta;
            return true;
        }

        public static void Smooth(List<Vector2> points, int passes, float amount, bool keepEnds = true)
        {
            if (points.Count < 3)
                return;
            Vector2[] temp = new Vector2[points.Count];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    if (keepEnds && (i == 0 || i == points.Count - 1))
                    {
                        temp[i] = points[i];
                        continue;
                    }
                    Vector2 prev = points[Mathf.Max(0, i - 1)];
                    Vector2 next = points[Mathf.Min(points.Count - 1, i + 1)];
                    temp[i] = Vector2.Lerp(points[i], (prev + next) * 0.5f, amount);
                }
                for (int i = 0; i < points.Count; i++)
                    points[i] = temp[i];
            }
        }

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            if (Mathf.Approximately(edge0, edge1))
                return x < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
