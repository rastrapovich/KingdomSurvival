using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    public static partial class MapLabArtBuilder
    {
        public sealed class Decor05Stats
        {
            public int Grass, Reeds, Rocks, Outcrops, Hay, Props, Houses, Barns, InkGrass;
            public readonly List<string> MissingRoles = new List<string>();
        }

        public static Decor05Stats LastDecor05 = new Decor05Stats();

        // Этап 05: дома и сараи — один выбранный при расстановке ракурс (группа +
        // ViewTag), при любом приближении; рисунок по наклону не переключается.
        public static void PlanHouses05(MapLabData data, MapLabSettings settings, MapLabArtSet set, Plan plan)
        {
            LastDecor05 = new Decor05Stats();
            if (set == null)
                return;
            bool anyHouse = false, anyBarn = false;
            for (int i = 0; i < data.Objects.Count; i++)
            {
                MapLabPlacement o = data.Objects[i];
                if ((o.Kind != MapLabObjectKind.House && o.Kind != MapLabObjectKind.Barn) || o.Yard <= 0 || o.View < 0 || o.View > 3)
                    continue;
                string group = HouseGroup(settings, o);
                string tag = MapLabHouseViews.Tags[o.View];
                MapLabArtEntry entry = null;
                foreach (MapLabArtEntry e in set.Entries)
                    if (e != null && e.HasTexture && e.Category == MapLabArtCategory.Building && e.VariantGroup == group && e.ViewTag == tag)
                        entry = e;
                if (entry == null)
                    continue;
                if (o.Kind == MapLabObjectKind.House) { anyHouse = true; LastDecor05.Houses++; } else { anyBarn = true; LastDecor05.Barns++; }
                float scale = settings.Settlement.BuildingScale * (o.Kind == MapLabObjectKind.Barn ? 0.85f : 1f);
                plan.Add(entry, Make(entry, o.Position, scale, o.Brightness, 0f, 0.5f, i));
            }
            if (anyHouse)
                plan.Replaced.Add(MapLabObjectKind.House);
            if (anyBarn)
                plan.Replaced.Add(MapLabObjectKind.Barn);
        }

        private static List<MapLabArtEntry> ByRole(MapLabArtSet set, params string[] roles)
        {
            List<MapLabArtEntry> list = new List<MapLabArtEntry>();
            foreach (MapLabArtEntry e in set.Entries)
                if (e != null && e.HasTexture && e.EffectiveWeight > 0f && System.Array.IndexOf(roles, e.Role) >= 0)
                    list.Add(e);
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        private static MapLabArtEntry Pick(List<MapLabArtEntry> list, float roll)
        {
            if (list.Count == 0)
                return null;
            List<float> w = new List<float>(list.Count);
            foreach (MapLabArtEntry e in list)
                w.Add(e.EffectiveWeight);
            return Choose(list, w, roll);
        }

        // Этап 05: трава, тростник, камни, сено и реквизит двора — укоренённые
        // спрайты по правилам места (не равномерная посыпка): трава у стволов,
        // столбов, камней, меж и кромок троп, с большими тихими участками;
        // тростник — на мелководье и мокром берегу с просветами, не у подходов
        // к мостам; камни — у берега и подножия уступа, редко на лугу; сено — у
        // края убранного поля или в глубине двора. Размер — по рисунку (рост
        // относительно дома и героя), корни — на земле, без отражений.
        public static void PlanDecor05(MapLabData data, MapLabSettings settings, MapLabArtSet set, MapLabShore shore, Plan plan)
        {
            if (set == null)
                return;
            MapLabStage05Settings s5 = settings.Stage05;
            List<MapLabArtEntry> grass = ByRole(set, "grass_tuft");
            List<MapLabArtEntry> reeds = ByRole(set, "reed_clump");
            List<MapLabArtEntry> rocks = ByRole(set, "boulder", "pebble_cluster");
            List<MapLabArtEntry> outcrop = ByRole(set, "small_outcrop");
            List<MapLabArtEntry> hay = ByRole(set, "haystack", "hay_sheaf");
            MapLabNoise quiet = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "decor05-quiet", MapLabData.AlgorithmVersion));
            MapLabNoise gaps = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "decor05-reed-gaps", MapLabData.AlgorithmVersion));
            MapLabRandom rng = MapLabRandom.ForStream(data.Seed, "decor05", MapLabData.AlgorithmVersion);
            List<Vector2> taken = new List<Vector2>();
            // Проверка «внутри постройки» смотрит только дома и сараи — отбираем их один раз
            // (раньше каждая травинка перебирала все объекты карты: квадратичный рост).
            List<MapLabPlacement> buildings = data.Objects.FindAll(o => o.Kind == MapLabObjectKind.House || o.Kind == MapLabObjectKind.Barn);

            bool Quiet(Vector2 p) => quiet.Fbm(p.x / 28f, p.y / 28f, 2) * 0.5f + 0.5f < 0.42f;
            bool NearBridge(Vector2 p)
            {
                foreach (MapLabBridge b in data.Bridges)
                    if (MapLabGeometry.DistanceToSegment(p, b.EndA, b.EndB, out _) < b.Width * 0.5f + 5f)
                        return true;
                return false;
            }
            bool Free(Vector2 p, float r)
            {
                if (!data.Contains(p.x, p.y) || MapLabPlacer.InsideBuilding(buildings, p, 0.3f))
                    return false;
                if (data.RoadDistance != null && data.Bilinear(data.RoadDistance, p.x, p.y) < 0.15f)
                    return false;
                foreach (MapLabPolyline road in data.Roads)
                    if (road.Widening && MapLabGeometry.Nearest(road.Points, p, out _, out _, out _, out _) < road.Width * 0.5f + 0.2f)
                        return false;
                return true;
            }
            void Add(List<MapLabArtEntry> pool, Vector2 p, float scale, ref int counter, float sink = 0f, bool water = false)
            {
                MapLabArtEntry e = Pick(pool, rng.Value());
                if (e == null)
                    return;
                float h = data.SampleHeight(p.x, p.y);
                if (water)
                {
                    float level = data.SampleWaterLevel(p.x, p.y);
                    if (!float.IsNaN(level))
                        h = Mathf.Max(h, level - 0.04f);
                }
                Vector3 basePoint = new Vector3(p.x, h - sink, p.y);
                float s = Mathf.Lerp(e.ScaleRange.x, e.ScaleRange.y, rng.Value()) * scale;
                plan.Add(e, Make(e, basePoint, s, rng.Range(0.94f, 1.04f), settings.Art.PaletteTint * 0.35f, 1f, -1));
                taken.Add(p);
                counter++;
            }

            // --- Трава ---
            float gd = s5.GrassDensity;
            if (grass.Count > 0 && gd > 0f)
            {
                plan.Replaced.Add(MapLabObjectKind.Grass);
                foreach (MapLabPlacement o in data.Objects)
                {
                    Vector2 p = new Vector2(o.Position.x, o.Position.z);
                    bool tree = o.Kind == MapLabObjectKind.Deciduous || o.Kind == MapLabObjectKind.Conifer;
                    bool rock = o.Kind == MapLabObjectKind.Boulder;
                    if (!tree && !rock)
                        continue;
                    if (tree && data.SampleMask(data.Forest, p.x, p.y) > 0.3f)
                        continue;
                    if (rng.Value() > (tree ? 0.75f : 0.55f) * gd || Quiet(p))
                        continue;
                    int n = rng.Range(1, tree ? 4 : 3);
                    float r0 = tree ? o.Scale * 0.12f + 0.35f : o.Scale * 0.45f;
                    for (int k = 0; k < n; k++)
                    {
                        float a = rng.Range(0f, Mathf.PI * 2f);
                        Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rng.Range(r0, r0 + 0.9f);
                        if (Free(q, 0.2f) && !data.IsUnderWater(q.x, q.y, 0.1f))
                            Add(grass, q, 1f, ref LastDecor05.Grass);
                    }
                }
                if (data.FenceGraph != null)
                {
                    foreach (Vector2 node in data.FenceGraph.Nodes)
                    {
                        if (rng.Value() > 0.32f * gd || Quiet(node))
                            continue;
                        Vector2 q = node + rng.InsideUnitCircle() * 0.35f + Vector2.down * 0.15f;
                        if (Free(q, 0.2f))
                            Add(grass, q, 0.9f, ref LastDecor05.Grass);
                    }
                }
                // Кромки троп.
                List<MapLabPolyline> paths = new List<MapLabPolyline>(data.YardPaths);
                foreach (MapLabPolyline r in data.Roads)
                    if (r.Kind == MapLabRoadKind.Path)
                        paths.Add(r);
                foreach (MapLabPolyline path in paths)
                {
                    float next = rng.Range(0f, 4f), acc = 0f;
                    for (int i = 1; i < path.Points.Count; i++)
                    {
                        Vector2 a = path.Points[i - 1], b = path.Points[i];
                        float len = Vector2.Distance(a, b);
                        Vector2 dir = len > 1e-4f ? (b - a) / len : Vector2.right;
                        while (next <= acc + len)
                        {
                            Vector2 q = a + dir * (next - acc) + MapLabGeometry.LeftNormal(dir) * (rng.Chance(0.5f) ? 1f : -1f) * (path.Width * 0.5f + rng.Range(0.2f, 0.5f));
                            if (!Quiet(q) && Free(q, 0.2f) && !data.IsUnderWater(q.x, q.y, 0.1f))
                                Add(grass, q, 0.9f, ref LastDecor05.Grass);
                            next += rng.Range(3.5f, 8f) / gd;
                        }
                        acc += len;
                    }
                }
                // Межи полей.
                if (data.Fields != null)
                {
                    int samples = Mathf.RoundToInt(data.Size * data.Size / 30f * gd);
                    for (int k = 0; k < samples; k++)
                    {
                        Vector2 q = new Vector2(rng.Value(), rng.Value()) * data.Size;
                        if (data.SampleMask(data.Field, q.x, q.y) < 0.4f || !data.Fields.Evaluate(q, out MapLabFields.Sample fs) || fs.BorderDistance > 0.3f)
                            continue;
                        if (Quiet(q) || !Free(q, 0.2f))
                            continue;
                        Add(grass, q, 0.85f, ref LastDecor05.Grass);
                    }
                }
            }
            else if (grass.Count == 0)
                LastDecor05.MissingRoles.Add("grass_tuft");

            // --- R03: контурная трава (GrassInk) — редко у троп, корней, камней и
            // местами на тихом лугу; разные варианты, масштаб и пустоты.
            List<MapLabArtEntry> inkGrass = ByRole(set, "grass_ink_tuft");
            if (inkGrass.Count > 0 && s5.InkGrassDensity > 0f)
            {
                float id = s5.InkGrassDensity;
                MapLabNoise patches = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "decor05-inkgrass", MapLabData.AlgorithmVersion));
                void Ink(Vector2 q)
                {
                    if (!Free(q, 0.15f) || data.IsUnderWater(q.x, q.y, 0.1f) || data.SampleMask(data.Forest, q.x, q.y) > 0.45f || data.SampleMask(data.Field, q.x, q.y) > 0.4f)
                        return;
                    MapLabArtEntry e = Pick(inkGrass, rng.Value());
                    float scale = rng.Range(0.6f, 1.4f);
                    plan.Add(e, Make(e, new Vector3(q.x, data.SampleHeight(q.x, q.y) - 0.01f, q.y), scale, 1f, 0f, 0.6f, -1));
                    LastDecor05.InkGrass++;
                }
                foreach (MapLabPlacement o in data.Objects)
                {
                    bool root = o.Kind == MapLabObjectKind.Deciduous || o.Kind == MapLabObjectKind.Conifer || o.Kind == MapLabObjectKind.Boulder;
                    if (!root || rng.Value() > 0.22f * id)
                        continue;
                    Vector2 p = new Vector2(o.Position.x, o.Position.z);
                    if (data.SampleMask(data.Forest, p.x, p.y) > 0.3f)
                        continue;
                    float a = rng.Range(0f, Mathf.PI * 2f);
                    Ink(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rng.Range(0.4f, 1.3f));
                }
                List<MapLabPolyline> edges = new List<MapLabPolyline>(data.YardPaths);
                edges.AddRange(data.Roads);
                foreach (MapLabPolyline road in edges)
                {
                    if (road.Widening)
                        continue;
                    List<Vector2> pts = MapLabGeometry.Resample(road.Points, 3f);
                    for (int i = 1; i < pts.Count; i++)
                    {
                        if (rng.Value() > 0.08f * id)
                            continue;
                        Vector2 dir = (pts[i] - pts[i - 1]).normalized;
                        Ink(pts[i] + MapLabGeometry.LeftNormal(dir) * (rng.Chance(0.5f) ? 1f : -1f) * (road.Width * 0.5f + rng.Range(0.3f, 0.8f)));
                    }
                }
                // Тихий луг: небольшие группы в редких местах, между ними пусто.
                int meadow = Mathf.RoundToInt(data.Size * data.Size / 160f * id);
                for (int k = 0; k < meadow; k++)
                {
                    Vector2 q = new Vector2(rng.Value(), rng.Value()) * data.Size;
                    if (patches.Fbm(q.x / 19f, q.y / 19f, 2) * 0.5f + 0.5f < 0.66f)
                        continue;
                    int n = rng.Range(1, 3);
                    for (int j = 0; j < n; j++)
                        Ink(q + rng.InsideUnitCircle() * 0.9f);
                }
            }

            // --- Тростник ---
            if (reeds.Count > 0 && s5.ReedDensity > 0f && shore != null)
            {
                plan.Replaced.Add(MapLabObjectKind.Reed);
                float step = 1.4f / Mathf.Max(0.3f, s5.ReedDensity);
                for (float z = 0f; z < data.Size; z += step)
                {
                    for (float x = 0f; x < data.Size; x += step)
                    {
                        Vector2 p = new Vector2(x + rng.Range(0f, step), z + rng.Range(0f, step));
                        float above = shore.Sample(p);
                        // Мелководье и мокрый берег: чуть ниже и чуть выше кромки.
                        if (above > 0.18f || above < -0.3f)
                            continue;
                        if (gaps.Fbm(p.x / 9f, p.y / 9f, 2) * 0.5f + 0.5f < 0.53f || NearBridge(p) || !Free(p, 0.3f))
                            continue;
                        int n = rng.Range(1, 4);
                        for (int k = 0; k < n; k++)
                        {
                            Vector2 q = p + rng.InsideUnitCircle() * 0.55f;
                            if (shore.Sample(q) < 0.25f)
                                Add(reeds, q, 1f, ref LastDecor05.Reeds, 0.02f, true);
                        }
                    }
                }
            }
            else if (reeds.Count == 0)
                LastDecor05.MissingRoles.Add("reed_clump");

            // --- Камни: берег, подножие уступа, редко на лугу (валуны генератора) ---
            if (rocks.Count > 0 && s5.RockDensity > 0f)
            {
                plan.Replaced.Add(MapLabObjectKind.Boulder);
                foreach (MapLabPlacement o in data.Objects)
                {
                    if (o.Kind != MapLabObjectKind.Boulder)
                        continue;
                    Vector2 p = new Vector2(o.Position.x, o.Position.z);
                    bool meadow = (data.WaterDistance == null || data.Bilinear(data.WaterDistance, p.x, p.y) > 4f) && data.SampleMask(data.Cliff, p.x, p.y) < 0.05f && data.SampleMask(data.Rock, p.x, p.y) < 0.3f;
                    if (meadow && rng.Value() > 0.35f)
                        continue;
                    Add(rocks, p, Mathf.Lerp(0.8f, 1.15f, Mathf.InverseLerp(0.6f, 2f, o.Scale)), ref LastDecor05.Rocks, 0.06f);
                }
                if (shore != null)
                {
                    float step = 7f / Mathf.Max(0.3f, s5.RockDensity);
                    for (float z = 0f; z < data.Size; z += step)
                    for (float x = 0f; x < data.Size; x += step)
                    {
                        Vector2 p = new Vector2(x + rng.Range(0f, step), z + rng.Range(0f, step));
                        float above = shore.Sample(p);
                        if (above < -0.05f || above > 0.35f || rng.Value() > 0.45f || NearBridge(p) || !Free(p, 0.4f))
                            continue;
                        Add(rocks, p, rng.Range(0.6f, 0.95f), ref LastDecor05.Rocks, 0.06f);
                    }
                }
                foreach (MapLabCliffLine line in data.CliffLines)
                {
                    foreach (MapLabCliffColumn col in line.Columns)
                    {
                        if (rng.Value() > 0.16f * s5.RockDensity)
                            continue;
                        Vector2 p = Vector2.Lerp(col.Start, col.End, rng.Value()) + col.Outward * (settings.Cliffs.CliffSharpness * 1.5f + rng.Range(0.6f, 1.8f));
                        if (Free(p, 0.4f) && !data.IsUnderWater(p.x, p.y, 0.1f))
                            Add(rocks, p, rng.Range(0.75f, 1.1f), ref LastDecor05.Rocks, 0.06f);
                    }
                }
            }
            // Rock_Outcrop_D — только местный декор осыпи у подножия.
            if (outcrop.Count > 0)
            {
                foreach (Vector3 spot in MapLabCliffs05.ScreeSpots)
                {
                    Vector2 p = new Vector2(spot.x, spot.z);
                    if (rng.Value() > 0.6f || !Free(p, 0.5f))
                        continue;
                    Add(outcrop, p, rng.Range(0.8f, 1.05f), ref LastDecor05.Outcrops, 0.1f);
                }
            }

            // --- Сено: край убранного поля (стога генератора) и глубина двора ---
            if (hay.Count > 0 && s5.HayDensity > 0f)
            {
                plan.Replaced.Add(MapLabObjectKind.Haystack);
                foreach (MapLabPlacement o in data.Objects)
                {
                    if (o.Kind != MapLabObjectKind.Haystack || rng.Value() > s5.HayDensity)
                        continue;
                    Vector2 p = new Vector2(o.Position.x, o.Position.z);
                    if (Free(p, 0.6f))
                        Add(hay, p, 1f, ref LastDecor05.Hay, 0.03f);
                }
            }

            // --- Реквизит двора и колодец ---
            foreach (MapLabProp prop in data.Props)
            {
                List<MapLabArtEntry> pool = ByRole(set, prop.Role);
                if (pool.Count == 0)
                {
                    if (!LastDecor05.MissingRoles.Contains(prop.Role))
                        LastDecor05.MissingRoles.Add(prop.Role);
                    continue;
                }
                MapLabArtEntry e = Pick(pool, rng.Value());
                plan.Add(e, Make(e, prop.Position - Vector3.up * 0.03f, prop.Scale * settings.Settlement.BuildingScale, rng.Range(0.95f, 1.03f), 0f, 0.8f, -1));
                if (prop.Role == "haystack" || prop.Role == "hay_sheaf") LastDecor05.Hay++; else LastDecor05.Props++;
            }
        }

        // 12У-E: видимые объекты плана — рисунок по ID из набора выбранных мест.
        // Нет набора или записи — объект пропускается, ID записывается в отчёт.
        public static readonly List<string> MissingFeatureArt = new List<string>();
        public static int LastFeatures;

        public static void PlanFeatures(MapLabData data, MapLabArtSet set, Plan plan)
        {
            MissingFeatureArt.Clear();
            LastFeatures = 0;
            MapLabComposition c = data.Composition;
            if (c == null || c.Features.Count == 0)
                return;
            MapLabRandom rng = MapLabRandom.ForStream(data.Seed, "features", MapLabData.AlgorithmVersion);
            foreach ((Vector2 p, string visual, string kind) in c.Features)
            {
                MapLabArtEntry e = set != null ? set.Find(visual) : null;
                if (e == null || !e.HasTexture || !e.Enabled)
                {
                    if (!MissingFeatureArt.Contains(visual))
                        MissingFeatureArt.Add(visual);
                    continue;
                }
                Vector3 at = new Vector3(p.x, data.SampleHeight(p.x, p.y), p.y);
                plan.Add(e, Make(e, at - Vector3.up * 0.03f, Mathf.Lerp(e.ScaleRange.x, e.ScaleRange.y, rng.Value()), rng.Range(0.96f, 1.03f), 0f, 0.85f, -1));
                LastFeatures++;
            }
        }

        // Скалы ревизии 06: осыпи-спрайты (50°) у подножия рисованной стены.
        public static void PlanCliffScree06(MapLabData data, MapLabSettings settings, MapLabCliffs06.Kit kit, Plan plan)
        {
            if (kit == null || kit.Scree.Count == 0)
                return;
            MapLabRandom rng = MapLabRandom.ForStream(data.Seed, "cliff06-scree", MapLabData.AlgorithmVersion);
            foreach (Vector3 spot in MapLabCliffs06.ScreeSpots)
            {
                MapLabArtEntry e = Pick(kit.Scree, rng.Value());
                float s = Mathf.Lerp(e.ScaleRange.x, e.ScaleRange.y, rng.Value());
                plan.Add(e, Make(e, spot - Vector3.up * 0.05f, s, rng.Range(0.95f, 1.03f), 0f, 1f, -1));
                LastDecor05.Rocks++;
            }
        }

        // Мелкие спрайты, которые плавно гаснут вдали (без появления рывком).
        public static bool IsDetail05(MapLabArtEntry e)
        {
            return e != null && (e.Role == "grass_tuft" || e.Role == "grass_ink_tuft" || e.Role == "reed_clump" || e.Role == "pebble_cluster" || e.Role == "hay_sheaf");
        }
    }
}
