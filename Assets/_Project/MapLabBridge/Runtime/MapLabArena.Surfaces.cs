using System;
using System.Collections.Generic;
using KingdomSurvival.MapLab;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // Поверхность клетки боевой арены, нарисованная в краске земли MapLab
    // (а не полупрозрачной заливкой поверх кадра). Порядок — порядок слоёв:
    // основание, затем состояния клетки, затем временные области.
    public enum MapLabArenaSurface
    {
        Mud = 1,
        Bog,
        ShallowWater,
        DeepWater,
        Rubble,
        Snow,
        Ice,
        Cliff,
        Road,
        Sand,
        WetGround,
        LooseEarth,
        Subsided,
        Pit,
        Tar,
        Poison,
        Frost,
        Fire,
        Last = Fire,
    }

    public static class MapLabArenaSurfaces
    {
        public static ulong Bit(MapLabArenaSurface surface) => 1UL << (int)surface;
    }

    // Бриф «Поверхности арены», доработка 20.11.2026: лёд, грязь, снег, вода,
    // огонь и прочие поверхности клеток — в базовом слое покраски арены, до
    // мазков кисти: мазки, свет рельефа, бумага и цветокоррекция вида ложатся
    // на них так же, как на луг. Граница участка — по рёбрам гексов участка
    // (соседние клетки одной поверхности сливаются без швов) с неровной
    // живой кромкой. Смена поверхности в бою перекрашивает только задетые
    // тайлы в фоне (SetSurfaces).
    public sealed partial class MapLabArena
    {
        private const ulong WaterBits = (1UL << (int)MapLabArenaSurface.DeepWater) | (1UL << (int)MapLabArenaSurface.ShallowWater);

        // Клетки, покрытые настоящей рекой MapLab (ключи центров): их вода —
        // русло с берегами, а не краска; краска под водой не видна.
        private readonly HashSet<Vector2Int> riverCells = new HashSet<Vector2Int>();
        private readonly List<Vector2> riverWorld = new List<Vector2>();
        public bool HasRiver => riverCells.Count > 0;

        // Река арены (20.11.2026): клетки глубокой воды (и броды при них),
        // протянутые через поле, — русло MapLab, как на глобальной карте:
        // вырезанное дно, вода, берег, уходящий под воду, и береговая краска.
        // Линия — через середины клеток воды по рядам, с продолжением за край
        // кадра; ширина — по клеткам ряда. Броды и настил моста — мели.
        private void PlanRiver(MapLabComposition c)
        {
            riverCells.Clear();
            riverWorld.Clear();
            ulong deep = 1UL << (int)MapLabArenaSurface.DeepWater;
            ulong shallow = 1UL << (int)MapLabArenaSurface.ShallowWater;
            List<Vector2> deepCells = new List<Vector2>();
            List<(Vector2 Center, ulong Layers)> water = new List<(Vector2, ulong)>();
            foreach ((Vector2 center, ulong layers) in Spec.Surfaces)
            {
                if ((layers & deep) != 0UL)
                    deepCells.Add(center);
                if ((layers & WaterBits) != 0UL)
                    water.Add((center, layers));
            }
            if (deepCells.Count < 3)
                return;
            float step = Spec.HexWorld * Mathf.Sqrt(3f);
            List<Vector2> cells = new List<Vector2>();
            List<Vector2> shoals = new List<Vector2>();
            foreach ((Vector2 center, ulong layers) in water)
            {
                bool nearDeep = (layers & deep) != 0UL || deepCells.Exists(d => Vector2.Distance(ToWorld(d), ToWorld(center)) < step * 1.1f);
                if (!nearDeep)
                    continue;
                cells.Add(center);
                if ((layers & deep) == 0UL)
                    shoals.Add(center);
            }
            foreach ((Vector2 center, string kind) in Spec.Objects)
                if (kind == "bridge" && cells.Exists(w => Vector2.Distance(w, center) < 1e-3f))
                    shoals.Add(center);
            // Ряды кадра (Y вниз): середина воды ряда — точка русла.
            SortedDictionary<int, List<Vector2>> rows = new SortedDictionary<int, List<Vector2>>();
            foreach (Vector2 center in cells)
            {
                int row = Mathf.RoundToInt(center.y / (Spec.HexNominal * 1.5f * Spec.VerticalScale));
                if (!rows.TryGetValue(row, out List<Vector2> list))
                    rows[row] = list = new List<Vector2>();
                list.Add(center);
            }
            if (rows.Count < 3)
                return;
            List<Vector2> line = new List<Vector2>();
            List<int> widths = new List<int>();
            foreach (List<Vector2> list in rows.Values)
            {
                Vector2 sum = Vector2.zero;
                foreach (Vector2 p in list) sum += p;
                line.Add(sum / list.Count);
                widths.Add(list.Count);
            }
            // Сглаживание: поворот русла — дугой, а не изломом.
            for (int pass = 0; pass < 2; pass++)
            {
                List<Vector2> soft = new List<Vector2>(line);
                for (int i = 1; i < line.Count - 1; i++)
                    soft[i] = (line[i - 1] + line[i] * 2f + line[i + 1]) * 0.25f;
                line = soft;
            }
            // Продолжение за верх и низ кадра (река приходит и уходит за край).
            Vector2 head = line[0] + (line[0] - line[1]).normalized * Mathf.Max(0.05f, line[0].y + Spec.HexNominal * 3f);
            Vector2 tail = line[line.Count - 1] + (line[line.Count - 1] - line[line.Count - 2]).normalized * Mathf.Max(0.05f, 1f - line[line.Count - 1].y + Spec.HexNominal * 3f);
            MapLabComposition.River river = new MapLabComposition.River { MeanderScale = 0.15f, WidthScale = 1f };
            river.Control.Add(ToWorld(head));
            foreach (Vector2 p in line)
                river.Control.Add(ToWorld(p));
            river.Control.Add(ToWorld(tail));
            c.Rivers.Add(river);
            widths.Sort();
            int across = widths[widths.Count / 2];
            float hex = Spec.HexWorld;
            settings.Water.RiverWidth = Mathf.Clamp(Mathf.Sqrt(3f) * hex * across * 0.95f, 2f, 30f);
            settings.Water.RiverDepth = 1.0f;
            settings.Water.BankWidth = hex * 0.7f;
            // Вода ниже луга: берег сходит к воде откосом, без насыпного вала.
            settings.Water.LevelOffset = -0.45f;
            foreach (Vector2 p in shoals)
            {
                Vector2 w = ToWorld(p);
                c.RiverShoals.Add(new Vector3(w.x, w.y, hex * 0.95f));
            }
            foreach (Vector2 p in cells)
            {
                riverCells.Add(CellKey(p));
                riverWorld.Add(ToWorld(p));
            }
        }

        // Слои клетки, которые зритель действительно видит в рисунке арены:
        // на клетке реки — только сама вода (краска под ней скрыта).
        public ulong VisibleLayers(Vector2 center, ulong layers) =>
            riverCells.Contains(CellKey(center)) ? layers & WaterBits : layers;

        private ArenaSurfacePaint surfacePaint;
        private readonly Dictionary<Vector2Int, ulong> surfaceCells = new Dictionary<Vector2Int, ulong>();

        // Поверхности кадра применены к краске (перекраска не идёт).
        public bool SurfacesSettled => !Ready || !view.RepaintPending;

        private void PrepareSurfaces()
        {
            surfaceCells.Clear();
            foreach ((Vector2 center, ulong layers) in Spec.Surfaces)
                surfaceCells[CellKey(center)] = layers;
            surfacePaint = new ArenaSurfacePaint(Spec.Seed, Spec.HexWorld);
            surfacePaint.Current = BuildSurfaceSnapshot();
            view.SurfacePaint = surfacePaint;
        }

        private static Vector2Int CellKey(Vector2 center) => new Vector2Int(Mathf.RoundToInt(center.x * 1000f), Mathf.RoundToInt(center.y * 1000f));

        // Новые поверхности клеток (центр клетки в долях кадра, слои). Меняются
        // только задетые тайлы краски; true — перекраска заказана.
        public bool SetSurfaces(IEnumerable<(Vector2 Center, ulong Layers)> surfaces)
        {
            if (!Ready || surfacePaint == null)
                return false;
            Dictionary<Vector2Int, (Vector2 Center, ulong Layers)> next = new Dictionary<Vector2Int, (Vector2, ulong)>();
            foreach ((Vector2 center, ulong layers) in surfaces)
                next[CellKey(center)] = (center, layers);
            bool any = false;
            Rect changed = default;
            void Touch(Vector2 center)
            {
                Vector2 w = ToWorld(center);
                float reach = Spec.HexWorld * 1.6f;
                Rect r = new Rect(w.x - reach, w.y - reach, reach * 2f, reach * 2f);
                changed = any ? Rect.MinMaxRect(Mathf.Min(changed.xMin, r.xMin), Mathf.Min(changed.yMin, r.yMin), Mathf.Max(changed.xMax, r.xMax), Mathf.Max(changed.yMax, r.yMax)) : r;
                any = true;
            }
            foreach (KeyValuePair<Vector2Int, (Vector2 Center, ulong Layers)> pair in next)
                if (!surfaceCells.TryGetValue(pair.Key, out ulong old) || old != pair.Value.Layers)
                    Touch(pair.Value.Center);
            foreach ((Vector2 center, MapLabArenaCellKind _) in Spec.Cells)
            {
                Vector2Int key = CellKey(center);
                if (surfaceCells.TryGetValue(key, out ulong old) && old != 0 && !next.ContainsKey(key))
                    Touch(center);
            }
            if (!any)
                return false;
            surfaceCells.Clear();
            foreach (KeyValuePair<Vector2Int, (Vector2 Center, ulong Layers)> pair in next)
                surfaceCells[pair.Key] = pair.Value.Layers;
            Spec.Surfaces.Clear();
            foreach ((Vector2 center, ulong layers) in next.Values)
                Spec.Surfaces.Add((center, layers));
            surfacePaint.Current = BuildSurfaceSnapshot();
            return view.RepaintArea(changed) > 0;
        }

        // Готовые перекрашенные тайлы — в краску; true — кадр нужно перерисовать.
        public bool PumpSurfaces() => Ready && view.PumpRepaint();

        private ArenaSurfacePaint.Snapshot BuildSurfaceSnapshot()
        {
            List<(Vector2 World, ulong Layers)> cells = new List<(Vector2, ulong)>();
            foreach ((Vector2 center, MapLabArenaCellKind _) in Spec.Cells)
            {
                Vector2Int key = CellKey(center);
                surfaceCells.TryGetValue(key, out ulong layers);
                // Вода реки — русло MapLab: краской не дублируется (кромка
                // краски легла бы на берег).
                if (riverCells.Contains(key))
                    layers &= ~WaterBits;
                cells.Add((ToWorld(center), layers));
            }
            return ArenaSurfacePaint.Snapshot.Build(cells, Spec.HexWorld);
        }
    }

    internal sealed class ArenaSurfacePaint : IMapLabSurfacePaint
    {
        private static readonly float Sqrt3 = Mathf.Sqrt(3f);

        // Неизменяемый снимок: слои клеток на решётке гексов в мире арены.
        internal sealed class Snapshot
        {
            public Vector2 Origin;
            public float Radius;
            public int QMin, RMin, Cols, Rows;
            public ulong[] Layers;
            public ulong[] Near;

            public static Snapshot Build(List<(Vector2 World, ulong Layers)> cells, float radius)
            {
                Snapshot s = new Snapshot { Radius = radius };
                if (cells.Count == 0)
                {
                    s.Layers = s.Near = Array.Empty<ulong>();
                    return s;
                }
                s.Origin = cells[0].World;
                List<(int Q, int R, ulong Layers)> axial = new List<(int, int, ulong)>();
                int qMin = int.MaxValue, rMin = int.MaxValue, qMax = int.MinValue, rMax = int.MinValue;
                foreach ((Vector2 world, ulong layers) in cells)
                {
                    s.Axial(world, out int q, out int r);
                    axial.Add((q, r, layers));
                    qMin = Math.Min(qMin, q); rMin = Math.Min(rMin, r);
                    qMax = Math.Max(qMax, q); rMax = Math.Max(rMax, r);
                }
                // Поля в две клетки: у «внешних» клеток тоже есть соседи.
                s.QMin = qMin - 2;
                s.RMin = rMin - 2;
                s.Cols = qMax - qMin + 5;
                s.Rows = rMax - rMin + 5;
                s.Layers = new ulong[s.Cols * s.Rows];
                s.Near = new ulong[s.Cols * s.Rows];
                foreach ((int q, int r, ulong layers) in axial)
                    s.Layers[s.Index(q, r)] = layers;
                for (int r = s.RMin + 1; r < s.RMin + s.Rows - 1; r++)
                for (int q = s.QMin + 1; q < s.QMin + s.Cols - 1; q++)
                {
                    ulong near = s.At(q, r);
                    for (int d = 0; d < 6; d++)
                        near |= s.At(q + Dq[d], r + Dr[d]);
                    s.Near[s.Index(q, r)] = near;
                }
                return s;
            }

            public int Index(int q, int r) => (r - RMin) * Cols + (q - QMin);

            public ulong At(int q, int r)
            {
                int x = q - QMin, y = r - RMin;
                return x < 0 || y < 0 || x >= Cols || y >= Rows ? 0UL : Layers[y * Cols + x];
            }

            public ulong NearAt(int q, int r)
            {
                int x = q - QMin, y = r - RMin;
                return x < 0 || y < 0 || x >= Cols || y >= Rows ? 0UL : Near[y * Cols + x];
            }

            // Гексы с вершиной вверх (как доска): шаг по ряду √3·R, между рядами 1,5·R.
            public void Axial(Vector2 world, out int q, out int r)
            {
                Vector2 rel = world - Origin;
                float fr = rel.y / (1.5f * Radius);
                float fq = rel.x / (Sqrt3 * Radius) - fr * 0.5f;
                float fs = -fq - fr;
                int rq = Mathf.RoundToInt(fq), rr = Mathf.RoundToInt(fr), rs = Mathf.RoundToInt(fs);
                float dq = Mathf.Abs(rq - fq), dr = Mathf.Abs(rr - fr), ds = Mathf.Abs(rs - fs);
                if (dq > dr && dq > ds) rq = -rr - rs;
                else if (dr > ds) rr = -rq - rs;
                q = rq;
                r = rr;
            }

            public Vector2 Center(int q, int r) => Origin + new Vector2(Sqrt3 * Radius * (q + r * 0.5f), 1.5f * Radius * r);
        }

        private static readonly int[] Dq = { 1, 1, 0, -1, -1, 0 };
        private static readonly int[] Dr = { 0, -1, -1, 0, 1, 1 };
        private static readonly Vector2[] Dir = BuildDirections();

        private static Vector2[] BuildDirections()
        {
            Vector2[] dirs = new Vector2[6];
            for (int d = 0; d < 6; d++)
                dirs[d] = new Vector2(Sqrt3 * (Dq[d] + Dr[d] * 0.5f), 1.5f * Dr[d]).normalized;
            return dirs;
        }

        private readonly MapLabNoise edge, tone, fine;
        private readonly float radius;
        private volatile Snapshot current;

        public Snapshot Current
        {
            get => current;
            set => current = value;
        }

        public ArenaSurfacePaint(int seed, float hexRadius)
        {
            radius = Mathf.Max(0.1f, hexRadius);
            edge = new MapLabNoise(MapLabRandom.StreamSeed(seed, "arena-surface-edge", MapLabData.AlgorithmVersion));
            tone = new MapLabNoise(MapLabRandom.StreamSeed(seed, "arena-surface-tone", MapLabData.AlgorithmVersion));
            fine = new MapLabNoise(MapLabRandom.StreamSeed(seed, "arena-surface-fine", MapLabData.AlgorithmVersion));
        }

        public bool Apply(Vector2 world, MapLabPalette pal, ref Color color, out bool owns)
        {
            owns = false;
            Snapshot s = current;
            if (s == null || s.Layers.Length == 0)
                return false;
            s.Axial(world, out int q, out int r);
            ulong near = s.NearAt(q, r);
            if (near == 0UL)
                return false;
            ulong own = s.At(q, r);
            Vector2 rel = world - s.Center(q, r);
            float apothem = radius * Sqrt3 * 0.5f;
            // Неровная кромка: крупное колебание и мелкая «рваность» кисти.
            float wobble = edge.Fbm(world.x / (radius * 1.1f), world.y / (radius * 1.1f), 3) * 0.30f * apothem
                           + fine.Fbm(world.x / (radius * 0.22f), world.y / (radius * 0.22f), 2) * 0.07f * apothem;
            float soft = 0.08f * apothem;
            // Мягкий минимум по рёбрам: углы гекса скругляются, участок не
            // выглядит штампом клетки.
            float round = 0.28f * apothem;
            float centerShare = rel.magnitude / apothem;
            bool any = false;
            for (int layer = 1; layer <= (int)MapLabArenaSurface.Last; layer++)
            {
                ulong bit = 1UL << layer;
                if ((near & bit) == 0UL)
                    continue;
                bool inside = (own & bit) != 0UL;
                float sum = 0f;
                bool found = false;
                for (int d = 0; d < 6; d++)
                {
                    bool other = (s.At(q + Dq[d], r + Dr[d]) & bit) != 0UL;
                    if (other == inside)
                        continue;
                    float distance = apothem - Vector2.Dot(rel, Dir[d]);
                    sum += Mathf.Exp(-distance / round);
                    found = true;
                }
                if (!inside && !found)
                    continue;
                float best = found ? -round * Mathf.Log(sum) : apothem;
                float signed = inside ? best : -best;
                MapLabArenaSurface surface = (MapLabArenaSurface)layer;
                // Яма — только своя клетка, кромка у края без расползания.
                float shift = surface == MapLabArenaSurface.Pit ? -0.25f * apothem : 0f;
                float cover = MapLabGeometry.SmoothStep(-soft, soft, signed + wobble + shift);
                if (cover <= 0.002f)
                    continue;
                color = Color.Lerp(color, LayerColor(surface, world, color, pal, centerShare), cover * Strength(surface));
                color.a = 1f;
                any = true;
                if (cover > 0.5f)
                    owns = true;
            }
            return any;
        }

        private static float Strength(MapLabArenaSurface surface)
        {
            switch (surface)
            {
                case MapLabArenaSurface.WetGround: return 0.85f;
                case MapLabArenaSurface.Subsided: return 0.8f;
                default: return 1f;
            }
        }

        private float N(MapLabNoise noise, Vector2 w, float scale, int octaves = 2) =>
            Mathf.Clamp01(noise.Fbm(w.x / (radius * scale), w.y / (radius * scale), octaves) * 0.5f + 0.5f);

        private Color LayerColor(MapLabArenaSurface surface, Vector2 w, Color base0, MapLabPalette pal, float centerShare)
        {
            float big = N(tone, w, 1.3f);
            float small = N(fine, w, 0.28f);
            Color c;
            switch (surface)
            {
                case MapLabArenaSurface.Mud:
                    // Грязь: бурая разбитая земля, светлые гребни колей, редкие лужи.
                    c = Color.Lerp(Color.Lerp(pal.RoadEdge, pal.FieldFallow, 0.45f), pal.ForestFloor, 0.15f + 0.2f * big);
                    c = Color.Lerp(c, Color.Lerp(pal.Road, pal.FieldFallow, 0.5f), Ripple(w, 0.45f) * 0.45f);
                    c = Color.Lerp(c, Color.Lerp(pal.WaterShallow, pal.RoadEdge, 0.35f), MapLabGeometry.SmoothStep(0.7f, 0.78f, small) * 0.65f);
                    break;
                case MapLabArenaSurface.Bog:
                    // Болото: бурая сырая зелень, кочки посветлее, окна тёмной воды.
                    c = Color.Lerp(Color.Lerp(pal.ForestFloor, pal.Shore, 0.45f), pal.RoadEdge, 0.25f * big);
                    c = Color.Lerp(c, Color.Lerp(pal.GrassLush, pal.Reed, 0.4f), MapLabGeometry.SmoothStep(0.62f, 0.7f, N(tone, w + new Vector2(5.3f, 1.7f), 0.35f, 2)) * 0.7f);
                    c = Color.Lerp(c, Color.Lerp(pal.WaterDeep, pal.WaterShallow, 0.3f), MapLabGeometry.SmoothStep(0.6f, 0.66f, N(edge, w + new Vector2(2.1f, 8.4f), 0.5f, 2)) * 0.85f);
                    break;
                case MapLabArenaSurface.ShallowWater:
                    c = Color.Lerp(pal.WaterShallow, pal.WaterStroke, 0.12f + 0.22f * big);
                    c = Color.Lerp(c, pal.Shore, (1f - MapLabGeometry.SmoothStep(0.15f, 0.35f, small)) * 0.25f);
                    c = Color.Lerp(c, pal.WaterStroke, Ripple(w, 0.55f) * 0.45f);
                    break;
                case MapLabArenaSurface.DeepWater:
                    c = Color.Lerp(pal.WaterDeep * 0.88f, pal.WaterShallow, 0.22f * big);
                    c = Color.Lerp(c, pal.WaterStroke, Ripple(w, 0.8f) * 0.25f);
                    break;
                case MapLabArenaSurface.Rubble:
                {
                    // Щебень: тёплая утоптанная земля, на ней россыпь камней
                    // разного размера с тёмной тенью снизу (свет сверху-слева).
                    Color dirt = Color.Lerp(Color.Lerp(base0, pal.RoadEdge, 0.6f), pal.FieldFallow, 0.15f + 0.2f * big) * 0.92f;
                    Vector2 at = w + new Vector2(13.7f, 4.1f);
                    float stone = N(fine, at, 0.2f, 2);
                    float shade = N(fine, at + new Vector2(0f, radius * 0.05f), 0.2f, 2);
                    Color rock = Color.Lerp(Color.Lerp(pal.RockShadow, pal.RoadEdge, 0.35f), pal.RockLight, 0.45f * small);
                    c = Color.Lerp(dirt, dirt * 0.55f, MapLabGeometry.SmoothStep(0.55f, 0.59f, shade) * 0.9f);
                    c = Color.Lerp(c, rock, MapLabGeometry.SmoothStep(0.58f, 0.62f, stone));
                    break;
                }
                case MapLabArenaSurface.Snow:
                    c = Color.Lerp(pal.Paper, Color.white, 0.55f);
                    c = Color.Lerp(c, Color.Lerp(pal.ShadowTint, pal.Paper, 0.4f), 0.06f + 0.22f * big * small);
                    break;
                case MapLabArenaSurface.Ice:
                    c = Color.Lerp(pal.WaterStroke, pal.Paper, 0.25f + 0.25f * big);
                    c = Color.Lerp(c, pal.WaterShallow, Crack(w, 0.75f) * 0.55f);
                    c = Color.Lerp(c, Color.white, MapLabGeometry.SmoothStep(0.7f, 0.85f, small) * 0.35f);
                    break;
                case MapLabArenaSurface.Frost:
                    c = Color.Lerp(Color.Lerp(pal.WaterStroke, pal.Paper, 0.55f), Color.white, 0.25f + 0.2f * small);
                    c = Color.Lerp(c, pal.WaterShallow, Crack(w, 0.5f) * 0.4f);
                    break;
                case MapLabArenaSurface.Cliff:
                    c = Color.Lerp(pal.RockShadow, pal.Ink, 0.35f) * (0.9f + 0.2f * small);
                    break;
                case MapLabArenaSurface.Road:
                    c = Color.Lerp(pal.Road, pal.RoadEdge, 0.12f + 0.25f * small);
                    break;
                case MapLabArenaSurface.Sand:
                    c = Color.Lerp(pal.Road, pal.FieldOchre, 0.25f + 0.25f * big);
                    break;
                case MapLabArenaSurface.WetGround:
                    c = Color.Lerp(base0 * 0.7f, pal.RoadEdge * 0.8f, 0.35f + 0.2f * small);
                    break;
                case MapLabArenaSurface.LooseEarth:
                    c = Color.Lerp(pal.FieldFallow, pal.RoadEdge, 0.35f + 0.35f * small);
                    c *= 1f - MapLabGeometry.SmoothStep(0.62f, 0.7f, N(edge, w + new Vector2(3.3f, 9.1f), 0.14f, 1)) * 0.25f;
                    break;
                case MapLabArenaSurface.Subsided:
                    c = Color.Lerp(base0, pal.RoadEdge * 0.72f, 0.5f + 0.2f * big);
                    break;
                case MapLabArenaSurface.Pit:
                {
                    Color rim = Color.Lerp(pal.RoadEdge, pal.FieldFallow, 0.3f);
                    Color hole = Color.Lerp(pal.Ink * 0.75f, pal.RoadEdge * 0.45f, 0.3f * small);
                    c = Color.Lerp(hole, rim, MapLabGeometry.SmoothStep(0.45f, 0.7f, centerShare));
                    break;
                }
                case MapLabArenaSurface.Tar:
                    c = Color.Lerp(pal.Ink * 0.7f, pal.WaterDeep * 0.45f, 0.3f * big);
                    c = Color.Lerp(c, Color.Lerp(pal.RockLight, pal.Ink, 0.5f), MapLabGeometry.SmoothStep(0.74f, 0.82f, small) * 0.5f);
                    break;
                case MapLabArenaSurface.Poison:
                    c = Color.Lerp(base0, new Color(0.57f, 0.64f, 0.22f), 0.6f + 0.2f * big);
                    c = Color.Lerp(c, new Color(0.34f, 0.40f, 0.14f), MapLabGeometry.SmoothStep(0.62f, 0.72f, small) * 0.5f);
                    break;
                case MapLabArenaSurface.Fire:
                {
                    Color charred = Color.Lerp(pal.Ink, pal.RoadEdge, 0.3f + 0.2f * small);
                    Color ember = Color.Lerp(new Color(0.90f, 0.46f, 0.16f), new Color(0.98f, 0.72f, 0.30f), small);
                    c = Color.Lerp(charred, ember, MapLabGeometry.SmoothStep(0.35f, 0.62f, big * 0.6f + small * 0.4f) * 0.9f);
                    break;
                }
                default:
                    c = base0;
                    break;
            }
            c.a = 1f;
            return c;
        }

        // Тонкая светлая рябь по воде — короткие неровные полосы.
        private float Ripple(Vector2 w, float scale)
        {
            float v = N(edge, new Vector2(w.x * 0.35f, w.y * 1.6f), scale, 2);
            return 1f - MapLabGeometry.SmoothStep(0.0f, 0.035f, Mathf.Abs(v - 0.5f));
        }

        // Трещины льда — тонкие линии по нулевой отметке шума.
        private float Crack(Vector2 w, float scale)
        {
            float v = edge.Fbm(w.x / (radius * scale) + 7.3f, w.y / (radius * scale) - 2.9f, 3);
            return 1f - MapLabGeometry.SmoothStep(0.0f, 0.045f, Mathf.Abs(v));
        }
    }
}
