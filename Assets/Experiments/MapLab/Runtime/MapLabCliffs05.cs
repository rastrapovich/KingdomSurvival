using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Этап 05: уступ — непрерывная стена вдоль контура с другим ритмом, а не
    // столбы-призмы этапа 04 в новой заливке. Вдоль линии — неравные широкие
    // плоскости (ширина плавно меняется по мировому шуму, соседние модули
    // согласованы), часть граней отступает вглубь (между ними — ступенька
    // боковой гранью, без щелей), у части — горизонтальная или косая полка,
    // у основания — редкие осыпи. Тёплый серо-охристый камень, перо — только
    // выбранные внешние контуры и несколько трещин. Верх: травяная крышка
    // одной полосой вдоль линии (общие вершины — без швов между плоскостями),
    // без светлого двойного обода этапа 04; неровный широкий травяной наплыв
    // рисует шейдер (Stage05).
    public static class MapLabCliffs05
    {
        public struct Stats
        {
            public int Planes;
            public int SetBack;
            public int Ledges;
            public int Screes;
            public float MeanPlaneWidth;
        }

        public static Stats LastStats;
        // Места осыпей (для декора Rock_Outcrop_D): мир XZ и высота подножия.
        public static readonly List<Vector3> ScreeSpots = new List<Vector3>();

        private struct Station
        {
            public Vector2 P;
            public Vector2 Outward;
            public float Bottom, Top, S;
        }

        public static Mesh Build(MapLabData data, MapLabSettings settings)
        {
            MapLabMeshBuilder mb = new MapLabMeshBuilder();
            MapLabStage05Settings s5 = settings.Stage05;
            MapLabRandom rng = MapLabRandom.ForStream(data.Seed, "cliff05-mesh", MapLabData.AlgorithmVersion);
            MapLabNoise rhythm = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "cliff05-rhythm", MapLabData.AlgorithmVersion));
            float w = Mathf.Max(0.4f, settings.Cliffs.CliffSharpness);
            Stats stats = default;
            ScreeSpots.Clear();
            float widthSum = 0f;
            for (int k = 0; k < data.CliffLines.Count; k++)
            {
                List<Station> st = Stations(data.CliffLines[k]);
                if (st.Count < 2)
                    continue;
                float total = st[st.Count - 1].S;
                // Границы плоскостей: ширина плавно меняется вдоль линии.
                List<int> cuts = new List<int> { 0 };
                float acc = 0f;
                float target = PlaneWidth(s5, rhythm, k, 0f, ref rng);
                for (int i = 1; i < st.Count; i++)
                {
                    acc += st[i].S - st[i - 1].S;
                    bool last = i == st.Count - 1;
                    if (acc >= target || last)
                    {
                        if (last && acc < target * 0.45f && cuts.Count > 1)
                            cuts[cuts.Count - 1] = i;
                        else
                            cuts.Add(i);
                        acc = 0f;
                        target = PlaneWidth(s5, rhythm, k, st[i].S, ref rng);
                    }
                }
                // Отступ каждой плоскости: крупные участки по шуму, внутри — немного случайно.
                int planes = cuts.Count - 1;
                float[] offset = new float[planes];
                for (int p = 0; p < planes; p++)
                {
                    float s = (st[cuts[p]].S + st[cuts[p + 1]].S) * 0.5f;
                    float n = rhythm.Fbm(s / 21f + k * 4.1f, 2.3f + k, 2);
                    bool back = n < -0.12f || rng.Chance(0.12f);
                    offset[p] = w * 1.2f + (back ? -s5.CliffSetback * rng.Range(0.55f, 1f) : rng.Range(0f, 0.3f) * w);
                    if (back) stats.SetBack++;
                }
                Vector2 Front(int i, float off) => st[i].P + st[i].Outward * off;
                // Общие задние точки крышки — по станциям (вершины общие у соседних плоскостей).
                for (int p = 0; p < planes; p++)
                {
                    int a = cuts[p], b = cuts[p + 1];
                    Station sa = st[a], sb = st[b];
                    Vector2 o = ((sa.Outward + sb.Outward) * 0.5f).normalized;
                    float off = offset[p];
                    float height = Mathf.Max(sa.Top - sa.Bottom, sb.Top - sb.Bottom);
                    float batter = Mathf.Min(0.9f * w, height * 0.1f);
                    float brightness = rng.Range(0.92f, 1.07f);
                    bool inked = rng.Value() < s5.CliffInkShare;
                    float ink = inked ? 0.85f : 0f;
                    Vector2 fa = Front(a, off), fb = Front(b, off);
                    Vector2 ta = fa - sa.Outward * batter, tb = fb - sb.Outward * batter;
                    float topA = sa.Top, topB = sb.Top;
                    bool crease = Vector2.Distance(fa, fb) > 2.6f && rng.Chance(0.55f);
                    bool ledge = height > 3.2f && rng.Value() < s5.CliffLedgeShare;
                    if (ledge)
                    {
                        stats.Ledges++;
                        float la = Mathf.Lerp(sa.Bottom, sa.Top, rng.Range(0.38f, 0.62f));
                        float lb = Mathf.Clamp(la + rng.Range(-0.7f, 0.7f), sb.Bottom + 0.8f, sb.Top - 0.8f);
                        float depth = rng.Range(0.3f, 0.8f) * w;
                        Vector2 ma = Vector2.Lerp(fa, ta, 0.45f), mbp = Vector2.Lerp(fb, tb, 0.45f);
                        Face(mb, fa, fb, sa.Bottom, sb.Bottom, ma, mbp, la, lb, o, brightness, ink, false);
                        // Полка: горизонтальная или косая (высоты концов разные).
                        Vector2 ua = ma - o * depth, ub = mbp - o * depth;
                        mb.Quad(V(ma, la), V(mbp, lb), V(ub, lb), V(ua, la), MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0f, brightness * 1.06f, false);
                        if (Vector3.Cross(V(ua, la) - V(ma, la), V(mbp, lb) - V(ma, la)).y < 0f)
                            FlipLast(mb);
                        Face(mb, ua, ub, la, lb, ta - o * depth, tb - o * depth, topA, topB, o, brightness * 1.02f, ink, false);
                        ta -= o * depth;
                        tb -= o * depth;
                    }
                    else if (crease)
                    {
                        // Плоскость с перегибом: две грани под небольшим углом.
                        float t = rng.Range(0.35f, 0.65f);
                        Vector2 fm = Vector2.Lerp(fa, fb, t) + o * rng.Range(-0.25f, 0.35f) * w;
                        Vector2 tm = fm - o * batter;
                        float bm = Mathf.Lerp(sa.Bottom, sb.Bottom, t), tmY = Mathf.Lerp(topA, topB, t) + rng.Signed() * 0.25f;
                        Face(mb, fa, fm, sa.Bottom, bm, ta, tm, topA, tmY, o, brightness, ink, false);
                        Face(mb, fm, fb, bm, sb.Bottom, tm, tb, tmY, topB, o, brightness * rng.Range(0.94f, 1.04f), ink * 0.6f, false);
                        Cap(mb, data, ta, tm, topA, tmY, st[a].P - st[a].Outward * (w * 1.2f + 0.6f), Vector2.Lerp(st[a].P, st[b].P, t) - o * (w * 1.2f + 0.6f));
                        Cap(mb, data, tm, tb, tmY, topB, Vector2.Lerp(st[a].P, st[b].P, t) - o * (w * 1.2f + 0.6f), st[b].P - st[b].Outward * (w * 1.2f + 0.6f));
                    }
                    else
                    {
                        Face(mb, fa, fb, sa.Bottom, sb.Bottom, ta, tb, topA, topB, o, brightness, ink, false);
                    }
                    if (!crease)
                        Cap(mb, data, ta, tb, topA, topB, st[a].P - st[a].Outward * (w * 1.2f + 0.6f), st[b].P - st[b].Outward * (w * 1.2f + 0.6f));
                    // Ступенька к соседней плоскости другого выноса — боковая грань без щели.
                    if (p + 1 < planes && Mathf.Abs(offset[p + 1] - off) > 0.05f)
                    {
                        Vector2 next = Front(b, offset[p + 1]);
                        Vector2 nextTop = next - sb.Outward * batter;
                        Face(mb, fb, next, sb.Bottom, sb.Bottom, tb, nextTop, topB, topB, (st[b].P - st[a].P).normalized * Mathf.Sign(off - offset[p + 1]), brightness * 0.93f, 0.6f, false);
                        Vector2 back = st[b].P - st[b].Outward * (w * 1.2f + 0.6f);
                        mb.Triangle(V(tb, topB), V(nextTop, topB), V(back, data.SampleHeight(back.x, back.y) - 0.08f), MapLabPaletteIndex.GrassB, MapLabPattern.Terrain, 0f);
                        FixUp(mb, 3);
                    }
                    // Концы линии: торцы до площадки сзади.
                    if (p == 0)
                        End(mb, data, st[a], fa, ta, w, brightness);
                    if (p == planes - 1)
                        End(mb, data, st[b], fb, tb, w, brightness);
                    // Осыпь у основания.
                    if (height > 2.4f && rng.Value() < s5.CliffScreeShare)
                    {
                        stats.Screes++;
                        float t0 = rng.Range(0.1f, 0.5f), t1 = Mathf.Min(1f, t0 + rng.Range(0.3f, 0.55f));
                        Vector2 a2 = Vector2.Lerp(fa, fb, t0), b2 = Vector2.Lerp(fa, fb, t1);
                        float reach = rng.Range(1f, 2.2f) * w;
                        Vector2 tip = Vector2.Lerp(a2, b2, rng.Range(0.35f, 0.65f)) + o * reach;
                        float ya = Mathf.Lerp(sa.Bottom, sb.Bottom, t0) + height * 0.16f, yb = Mathf.Lerp(sa.Bottom, sb.Bottom, t1) + height * 0.12f;
                        float yt = data.SampleHeight(tip.x, tip.y) - 0.06f;
                        mb.Triangle(V(a2, ya), V(b2, yb), V(tip, yt), MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0f, brightness * 0.9f);
                        FixUp(mb, 3);
                        ScreeSpots.Add(new Vector3(tip.x, yt, tip.y));
                    }
                    widthSum += Vector2.Distance(fa, fb);
                    stats.Planes++;
                }
            }
            stats.MeanPlaneWidth = stats.Planes > 0 ? widthSum / stats.Planes : 0f;
            LastStats = stats;
            return mb.VertexCount > 0 ? mb.ToMesh("MapLab Cliffs 05") : null;
        }

        private static float PlaneWidth(MapLabStage05Settings s5, MapLabNoise rhythm, int line, float s, ref MapLabRandom rng)
        {
            float n = rhythm.Fbm(s / 18f + line * 2.7f, line * 1.3f, 2) * 0.5f + 0.5f;
            return Mathf.Lerp(s5.CliffPlaneWidth.x, s5.CliffPlaneWidth.y, Mathf.Clamp01(n)) * rng.Range(0.8f, 1.2f);
        }

        // Станции линии: начала столбов и конец последнего (вынос и высоты — средние соседей).
        private static List<Station> Stations(MapLabCliffLine line)
        {
            List<Station> list = new List<Station>();
            List<MapLabCliffColumn> cols = line.Columns;
            float s = 0f;
            for (int i = 0; i <= cols.Count; i++)
            {
                MapLabCliffColumn prev = cols[Mathf.Max(0, i - 1)], next = cols[Mathf.Min(cols.Count - 1, i)];
                Vector2 p = i < cols.Count ? cols[i].Start : cols[cols.Count - 1].End;
                if (i > 0 && i < cols.Count && Vector2.Distance(cols[i - 1].End, cols[i].Start) > 0.05f)
                    p = (cols[i - 1].End + cols[i].Start) * 0.5f;
                if (list.Count > 0)
                    s += Vector2.Distance(list[list.Count - 1].P, p);
                Vector2 o = (prev.Outward + next.Outward).normalized;
                list.Add(new Station
                {
                    P = p,
                    Outward = o.sqrMagnitude > 0.5f ? o : next.Outward,
                    Bottom = Mathf.Min(prev.Bottom, next.Bottom),
                    Top = (prev.Top + next.Top) * 0.5f,
                    S = s,
                });
            }
            return list;
        }

        // Лицевая грань: нижнее ребро a→b, верхнее ta→tb; порядок вершин — так,
        // чтобы нормаль смотрела наружу (o).
        private static void Face(MapLabMeshBuilder mb, Vector2 a, Vector2 b, float ya, float yb, Vector2 ta, Vector2 tb, float yta, float ytb, Vector2 o, float brightness, float ink, bool allEdges)
        {
            Vector3 p0 = V(a, ya), p1 = V(b, yb), p2 = V(tb, ytb), p3 = V(ta, yta);
            Vector3 n = Vector3.Cross(p3 - p0, p1 - p0);
            if (n.x * o.x + n.z * o.y < 0f)
                mb.Quad(p1, p0, p3, p2, MapLabPaletteIndex.RockLight, MapLabPattern.Rock, ink, brightness, true);
            else
                mb.Quad(p0, p1, p2, p3, MapLabPaletteIndex.RockLight, MapLabPattern.Rock, ink, brightness, true);
        }

        // Травяная крышка от верхнего ребра назад под рельеф площадки.
        private static void Cap(MapLabMeshBuilder mb, MapLabData data, Vector2 ta, Vector2 tb, float ya, float yb, Vector2 backA, Vector2 backB)
        {
            float ba = Mathf.Min(data.SampleHeight(backA.x, backA.y) - 0.3f, Mathf.Max(ya, yb));
            float bb = Mathf.Min(data.SampleHeight(backB.x, backB.y) - 0.3f, Mathf.Max(ya, yb));
            mb.Quad(V(ta, ya - 0.02f), V(tb, yb - 0.02f), V(backB, bb), V(backA, ba), MapLabPaletteIndex.GrassB, MapLabPattern.Terrain, 0f, 1f, false);
            FixUp(mb, 4);
        }

        private static void End(MapLabMeshBuilder mb, MapLabData data, Station s, Vector2 front, Vector2 top, float w, float brightness)
        {
            Vector2 back = s.P - s.Outward * w * 2f;
            float yb = data.SampleHeight(back.x, back.y) - 0.1f;
            Vector3 p0 = V(back, s.Bottom), p1 = V(front, s.Bottom), p2 = V(top, s.Top), p3 = V(back, Mathf.Min(s.Top, yb));
            mb.Quad(p0, p1, p2, p3, MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0.4f, brightness * 0.95f, true);
        }

        // Последний примитив смотрит вверх (крышка, полка, осыпь).
        private static void FixUp(MapLabMeshBuilder mb, int vertices)
        {
            int start = mb.Positions.Count - vertices;
            Vector3 n = mb.Normals[start];
            if (n.y >= 0f)
                return;
            FlipLast(mb, vertices);
        }

        private static void FlipLast(MapLabMeshBuilder mb, int vertices = 4)
        {
            int start = mb.Positions.Count - vertices;
            for (int i = start; i < mb.Positions.Count; i++)
                mb.Normals[i] = -mb.Normals[i];
            int tri = vertices == 4 ? 6 : 3;
            for (int i = mb.Indices.Count - tri; i < mb.Indices.Count; i += 3)
            {
                int t = mb.Indices[i + 1];
                mb.Indices[i + 1] = mb.Indices[i + 2];
                mb.Indices[i + 2] = t;
            }
        }

        private static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);
    }
}
