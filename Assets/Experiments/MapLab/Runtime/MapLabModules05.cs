using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomSurvival.MapLab
{
    // Этап 05: ограды и мосты из PNG-модулей на локальных поверхностях. Модуль
    // vertical_front — вертикальная грань (заполнение вдоль ребра графа, столб
    // лицом к камере по горизонтали), horizontal_top — настил (U поперёк, V
    // вдоль). Ширина заполнения — из видимой рамки модуля: длинный пролёт —
    // повтор, короткий — обрезка, высота от длины не растягивается. Нет
    // модуля — нет детали (с отметкой в статистике), без молчаливой подмены
    // процедурной геометрией.
    public static class MapLabModules05
    {
        public const string RailsTwo = "Map05_Fence_Rails_Two_A";
        public const string RailsThree = "Map05_Fence_Rails_Three_B";
        public const string Wattle = "Map05_Fence_Wattle_Panel_A";
        public const string Gate = "Map05_Fence_Gate_Panel_A";
        public const string PostEnd = "Map05_Fence_Post_End_A";
        public const string PostCorner = "Map05_Fence_Post_Corner_B";
        public const string DeckA = "Map05_Bridge_Deck_Planks_A";
        public const string DeckB = "Map05_Bridge_Deck_Planks_B";
        public const string Ramp = "Map05_Bridge_EndRamp_Planks_A";
        public const string Abutment = "Map05_Bridge_StoneAbutment_A";
        public const string BridgePost = "Map05_Bridge_Post_A";
        public const string RailA = "Map05_Bridge_Rail_Infill_A";
        public const string RailB = "Map05_Bridge_Rail_Infill_B";
        public const string Trestle = "Map05_Bridge_Trestle_Frame_A";

        public sealed class Stats
        {
            public int Posts, Infills, Gates, Missing;
            public int Bridges, DeckPanels, BridgePosts, Trestles, Ramps, Abutments;
            public readonly List<string> MissingIds = new List<string>();
        }

        public sealed class Batch
        {
            public readonly Dictionary<MapLabArtEntry, MeshData> Meshes = new Dictionary<MapLabArtEntry, MeshData>();
            public readonly Stats Stats = new Stats();
        }

        public sealed class MeshData
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<int> Indices = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                Vector3 n = Vector3.Cross(d - a, b - a).normalized;
                int s = Positions.Count;
                Positions.Add(a); Positions.Add(b); Positions.Add(c); Positions.Add(d);
                for (int i = 0; i < 4; i++)
                    Normals.Add(n);
                Uv.Add(ua); Uv.Add(ub); Uv.Add(uc); Uv.Add(ud);
                Indices.Add(s); Indices.Add(s + 3); Indices.Add(s + 2);
                Indices.Add(s); Indices.Add(s + 2); Indices.Add(s + 1);
            }

            public Mesh ToMesh(string name)
            {
                Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                if (Positions.Count > 65000)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(Positions);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, Uv);
                mesh.SetTriangles(Indices, 0, true);
                return mesh;
            }
        }

        private static MeshData For(Batch batch, MapLabArtSet set, string id)
        {
            MapLabArtEntry e = set != null ? set.Find(id) : null;
            if (e == null || !e.HasTexture)
            {
                if (!batch.Stats.MissingIds.Contains(id))
                    batch.Stats.MissingIds.Add(id);
                batch.Stats.Missing++;
                return null;
            }
            if (!batch.Meshes.TryGetValue(e, out MeshData m))
                batch.Meshes[e] = m = new MeshData();
            return m;
        }

        private static MapLabArtEntry Entry(MapLabArtSet set, string id)
        {
            MapLabArtEntry e = set != null ? set.Find(id) : null;
            return e != null && e.HasTexture ? e : null;
        }

        // Видимая ширина модуля (мир) при данной видимой высоте.
        public static float ModuleWidth(MapLabArtEntry e, float height)
        {
            Rect r = e.ModuleRect;
            return height * (r.width * e.Width) / Mathf.Max(1f, r.height * e.Height);
        }

        public static Batch Build(MapLabData data, MapLabSettings settings, MapLabArtSet set)
        {
            Batch batch = new Batch();
            if (data.FenceGraph != null)
                BuildFences(data, set, batch);
            foreach (MapLabBridge bridge in data.Bridges)
                BuildBridge(data, settings, set, bridge, batch);
            return batch;
        }

        // ---------- Ограды ----------

        private static readonly float[] InfillLift = { 0.22f, 0.12f, -0.04f };
        private static readonly string[] InfillIds = { RailsTwo, RailsThree, Wattle };

        private static void BuildFences(MapLabData data, MapLabArtSet set, Batch batch)
        {
            MapLabFenceGraph g = data.FenceGraph;
            int[] degree = new int[g.Nodes.Count];
            bool[] gateNode = new bool[g.Nodes.Count];
            foreach (MapLabFenceEdge e in g.Edges)
            {
                degree[e.A]++;
                degree[e.B]++;
                if (e.Gate)
                {
                    gateNode[e.A] = true;
                    gateNode[e.B] = true;
                }
            }
            // Заполнения: одно на ребро.
            foreach (MapLabFenceEdge edge in g.Edges)
            {
                string id = edge.Gate ? Gate : InfillIds[Mathf.Clamp(edge.Style, 0, 2)];
                MapLabArtEntry entry = Entry(set, id);
                MeshData mesh = For(batch, set, id);
                if (entry == null || mesh == null)
                    continue;
                Vector2 a = g.Nodes[edge.A], b = g.Nodes[edge.B];
                float lift = edge.Gate ? 0.04f : InfillLift[Mathf.Clamp(edge.Style, 0, 2)];
                VerticalStrip(data, mesh, entry, a, b, entry.WorldHeight, lift, Hash01(a + b, 3));
                if (edge.Gate) batch.Stats.Gates++; else batch.Stats.Infills++;
            }
            // Столбы: один на узел. Концы ряда и ворота — концевой столб, углы и
            // примыкания — угловой, прямой промежуточный — концевой чуть ниже.
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                if (degree[i] == 0)
                    continue;
                bool corner = degree[i] >= 3 || (degree[i] == 2 && Bend(g, i) > 25f);
                string id = corner && !gateNode[i] ? PostCorner : PostEnd;
                MapLabArtEntry entry = Entry(set, id);
                MeshData mesh = For(batch, set, id);
                if (entry == null || mesh == null)
                    continue;
                float scale = degree[i] == 2 && !corner && !gateNode[i] ? 0.9f : 1f;
                Post(data, mesh, entry, g.Nodes[i], entry.WorldHeight * scale);
                batch.Stats.Posts++;
            }
        }

        private static float Bend(MapLabFenceGraph g, int node)
        {
            Vector2 p = g.Nodes[node];
            List<Vector2> dirs = new List<Vector2>(2);
            foreach (MapLabFenceEdge e in g.Edges)
            {
                if (e.A == node) dirs.Add((g.Nodes[e.B] - p).normalized);
                else if (e.B == node) dirs.Add((g.Nodes[e.A] - p).normalized);
            }
            if (dirs.Count != 2)
                return 0f;
            return 180f - Vector2.Angle(dirs[0], dirs[1]);
        }

        private static float Hash01(Vector2 p, int salt)
        {
            return MapLabRandom.Hash01(Mathf.RoundToInt(p.x * 8f), Mathf.RoundToInt(p.y * 8f) + salt * 7919, 0x5bd1e995u);
        }

        // Вертикальная полоса модуля вдоль a→b: низ — по земле в каждом куске
        // (параллелограмм на склоне), высота постоянна; повтор или обрезка по U.
        private static void VerticalStrip(MapLabData data, MeshData mesh, MapLabArtEntry e, Vector2 a, Vector2 b, float height, float lift, float phase, float baseA = float.NaN, float baseB = float.NaN)
        {
            float length = Vector2.Distance(a, b);
            if (length < 0.05f)
                return;
            Rect r = e.ModuleRect;
            float moduleWidth = Mathf.Max(0.1f, ModuleWidth(e, height));
            int pieces = Mathf.Max(1, Mathf.CeilToInt(length / moduleWidth - 0.15f));
            float pieceLength = length / pieces;
            // Небольшое сжатие или растяжение по U в пределах ±15 %, иначе — обрезка.
            float span = Mathf.Clamp(pieceLength / moduleWidth, 0.1f, 1f);
            if (pieceLength / moduleWidth > 0.85f)
                span = 1f;
            float u0 = r.xMin + (r.width * (1f - span)) * phase;
            float u1 = u0 + r.width * span;
            for (int k = 0; k < pieces; k++)
            {
                Vector2 pa = Vector2.Lerp(a, b, k / (float)pieces), pb = Vector2.Lerp(a, b, (k + 1) / (float)pieces);
                float ha = float.IsNaN(baseA) ? data.SampleHeight(pa.x, pa.y) : Mathf.Lerp(baseA, baseB, k / (float)pieces);
                float hb = float.IsNaN(baseA) ? data.SampleHeight(pb.x, pb.y) : Mathf.Lerp(baseA, baseB, (k + 1) / (float)pieces);
                Vector3 p0 = new Vector3(pa.x, ha + lift, pa.y), p1 = new Vector3(pb.x, hb + lift, pb.y);
                mesh.Quad(p0, p1, p1 + Vector3.up * height, p0 + Vector3.up * height,
                    new Vector2(u0, r.yMin), new Vector2(u1, r.yMin), new Vector2(u1, r.yMax), new Vector2(u0, r.yMax));
            }
        }

        // Столб: вертикальная грань лицом к камере по горизонтали (не разворот
        // к наклону камеры), чуть ближе к камере, чем заполнение в узле.
        private static void Post(MapLabData data, MeshData mesh, MapLabArtEntry e, Vector2 p, float height, float baseHeight = float.NaN)
        {
            Rect r = e.ModuleRect;
            float width = ModuleWidth(e, height);
            float h = float.IsNaN(baseHeight) ? data.SampleHeight(p.x, p.y) - 0.08f : baseHeight;
            Vector3 c = new Vector3(p.x, h, p.y - 0.06f);
            Vector3 right = Vector3.right * width * 0.5f;
            mesh.Quad(c - right, c + right, c + right + Vector3.up * height, c - right + Vector3.up * height,
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));
        }

        // ---------- Мосты ----------

        public struct BridgeLayout
        {
            public Vector2 LandA, LandB;   // сухие опоры настила на берегах
            public bool DryA, DryB;
            public float Deck;
            public int Panels;
            public int Trestles;
            public List<Vector2> TrestlePoints;
            public List<float> TrestleFloors;
        }

        // Непрерывный настил между сухими берегами: от концов моста — наружу,
        // пока точка не окажется на суше выше воды; козлы — только там, где
        // настил высоко над дном/землёй, ноги — до дна.
        public static BridgeLayout Layout(MapLabData data, MapLabSettings settings, MapLabBridge bridge)
        {
            BridgeLayout layout = new BridgeLayout { TrestlePoints = new List<Vector2>(), TrestleFloors = new List<float>() };
            Vector2 axis = (bridge.EndB - bridge.EndA).normalized;
            float level = bridge.WaterLevel;
            bool Dry(Vector2 p) => data.Contains(p.x, p.y) && !data.IsUnderWater(p.x, p.y, 0.05f) && data.SampleHeight(p.x, p.y) > level + 0.12f;
            Vector2 Walk(Vector2 from, Vector2 dir, out bool dry)
            {
                Vector2 p = from;
                for (int i = 0; i < 40; i++)
                {
                    if (Dry(p) && Dry(p + dir * 0.6f))
                    {
                        dry = true;
                        return p + dir * 0.6f;
                    }
                    p += dir * 0.25f;
                }
                dry = Dry(p);
                return p;
            }
            layout.LandA = Walk(bridge.EndA, -axis, out layout.DryA);
            layout.LandB = Walk(bridge.EndB, axis, out layout.DryB);
            layout.Deck = Mathf.Max(bridge.DeckHeight, level + 0.45f);
            float length = Vector2.Distance(layout.LandA, layout.LandB);
            float panel = Mathf.Max(0.5f, settings.Stage05.DeckPanelLength);
            layout.Panels = Mathf.Max(1, Mathf.RoundToInt(length / panel));
            float spacing = Mathf.Max(1.5f, settings.Stage05.TrestleSpacing);
            int count = Mathf.FloorToInt(length / spacing);
            for (int k = 1; k <= count; k++)
            {
                Vector2 p = Vector2.Lerp(layout.LandA, layout.LandB, k / (float)(count + 1));
                float floor = data.SampleHeight(p.x, p.y);
                if (layout.Deck - floor > 0.6f)
                {
                    layout.TrestlePoints.Add(p);
                    layout.TrestleFloors.Add(floor);
                }
            }
            layout.Trestles = layout.TrestlePoints.Count;
            return layout;
        }

        private static void BuildBridge(MapLabData data, MapLabSettings settings, MapLabArtSet set, MapLabBridge bridge, Batch batch)
        {
            BridgeLayout l = Layout(data, settings, bridge);
            batch.Stats.Bridges++;
            Vector2 axis = (l.LandB - l.LandA).normalized;
            Vector2 across = MapLabGeometry.LeftNormal(axis);
            float half = bridge.Width * 0.5f;
            float length = Vector2.Distance(l.LandA, l.LandB);
            float deck = l.Deck;
            // Настил: панели A/B по очереди, U поперёк, V вдоль; на внутренних
            // стыках V обрезан внутрь рамки — без двойной тёмной линии.
            for (int k = 0; k < l.Panels; k++)
            {
                string id = (k + Mathf.RoundToInt(l.LandA.x)) % 2 == 0 ? DeckA : DeckB;
                MapLabArtEntry e = Entry(set, id);
                MeshData mesh = For(batch, set, id);
                if (e == null || mesh == null)
                    continue;
                Rect r = e.ModuleRect;
                float inset = r.height * 0.07f;
                float v0 = k == 0 ? r.yMin : r.yMin + inset, v1 = k == l.Panels - 1 ? r.yMax : r.yMax - inset;
                Vector2 a = Vector2.Lerp(l.LandA, l.LandB, k / (float)l.Panels), b = Vector2.Lerp(l.LandA, l.LandB, (k + 1) / (float)l.Panels);
                Vector3 p0 = V(a - across * half, deck), p1 = V(a + across * half, deck), p2 = V(b + across * half, deck), p3 = V(b - across * half, deck);
                mesh.Quad(p0, p3, p2, p1, new Vector2(r.xMin, v0), new Vector2(r.xMin, v1), new Vector2(r.xMax, v1), new Vector2(r.xMax, v0));
                batch.Stats.DeckPanels++;
            }
            // Перила с обеих сторон, общие столбы.
            MapLabArtEntry post = Entry(set, BridgePost);
            int spans = Mathf.Max(1, Mathf.RoundToInt(length / 2.3f));
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 off = across * side * (half - 0.05f);
                for (int k = 0; k <= spans; k++)
                {
                    Vector2 p = Vector2.Lerp(l.LandA, l.LandB, k / (float)spans) + off;
                    MeshData pm = For(batch, set, BridgePost);
                    if (post != null && pm != null)
                    {
                        Post(data, pm, post, p, post.WorldHeight, deck - 0.12f);
                        batch.Stats.BridgePosts++;
                    }
                    if (k == spans)
                        break;
                    string rid = (k + side) % 2 == 0 ? RailA : RailB;
                    MapLabArtEntry rail = Entry(set, rid);
                    MeshData rm = For(batch, set, rid);
                    if (rail == null || rm == null)
                        continue;
                    Vector2 q = Vector2.Lerp(l.LandA, l.LandB, (k + 1) / (float)spans) + off;
                    VerticalStrip(data, rm, rail, p, q, rail.WorldHeight, 0.06f, 0.5f, deck, deck);
                }
            }
            // Козлы: поперёк моста, ноги — до дна или земли.
            MapLabArtEntry trestle = Entry(set, Trestle);
            for (int t = 0; t < l.TrestlePoints.Count; t++)
            {
                MeshData tm = For(batch, set, Trestle);
                if (trestle == null || tm == null)
                    break;
                Vector2 p = l.TrestlePoints[t];
                float floor = l.TrestleFloors[t] - 0.1f;
                float top = deck - 0.04f;
                float height = top - floor;
                Rect r = trestle.ModuleRect;
                float vBottom = height < trestle.WorldHeight ? r.yMax - r.height * (height / trestle.WorldHeight) : r.yMin;
                Vector2 wa = p - across * (half + 0.12f), wb = p + across * (half + 0.12f);
                tm.Quad(V(wa, floor), V(wb, floor), V(wb, top), V(wa, top), new Vector2(r.xMin, vBottom), new Vector2(r.xMax, vBottom), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));
                batch.Stats.Trestles++;
            }
            // Береговые устои (частично в земле) и съезды (широкий конец — к суше).
            MapLabArtEntry abut = Entry(set, Abutment), ramp = Entry(set, Ramp);
            for (int end = 0; end < 2; end++)
            {
                Vector2 land = end == 0 ? l.LandA : l.LandB;
                Vector2 outward = end == 0 ? -axis : axis;
                MeshData am = For(batch, set, Abutment);
                if (abut != null && am != null)
                {
                    Vector2 p = land - outward * 0.25f;
                    Vector2 wa = p - across * (half + 0.35f), wb = p + across * (half + 0.35f);
                    float top = deck - 0.06f, bottom = top - abut.WorldHeight * 1.15f;
                    Rect r = abut.ModuleRect;
                    am.Quad(V(wa, bottom), V(wb, bottom), V(wb, top), V(wa, top), new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));
                    batch.Stats.Abutments++;
                }
                float ground = data.SampleHeight(land.x, land.y);
                MeshData rmesh = For(batch, set, Ramp);
                if (ramp != null && rmesh != null && deck - ground > 0.05f)
                {
                    float rampLength = Mathf.Clamp((deck - ground) / 0.28f, 1.2f, 3.2f);
                    Vector2 far = land + outward * rampLength;
                    float farGround = data.SampleHeight(far.x, far.y) + 0.02f;
                    Rect r = ramp.ModuleRect;
                    // Узкий конец рисунка (верх холста) — у настила, широкий — к суше.
                    float wide = half / 0.62f;
                    Vector3 n0 = V(land - across * wide, deck), n1 = V(land + across * wide, deck);
                    Vector3 f0 = V(far - across * wide, farGround), f1 = V(far + across * wide, farGround);
                    rmesh.Quad(f0, f1, n1, n0, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));
                    batch.Stats.Ramps++;
                }
            }
        }

        private static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);
    }
}
