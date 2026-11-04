using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Этап 03: лес только из отдельных деревьев (выбор по весам набора и
    // пространственным пятнам, без проверок имён), группы крон для выборочных
    // тушевых акцентов и пары ракурсов построек P50/P35.
    public static partial class MapLabArtBuilder
    {
        public sealed class ForestStats
        {
            public int Trees;
            public int Groups;
            public int AccentGroups;
            public int AccentTrees;
            public int Chains;
        }

        public static ForestStats LastForest = new ForestStats();

        private struct TreeRef
        {
            public MapLabArtEntry Entry;
            public int Index;      // в списке plan.Cards[Entry]
            public Vector2 P;
            public float Radius;   // полуширина кроны в мире
        }

        public static void PlanVegetation03(MapLabData data, MapLabSettings settings, MapLabArtSet set, Plan plan)
        {
            MapLabStage03Settings s3 = settings.Stage03;
            List<MapLabArtEntry> deciduous = new List<MapLabArtEntry>();
            List<MapLabArtEntry> conifer = new List<MapLabArtEntry>();
            List<MapLabArtEntry> bush = new List<MapLabArtEntry>();
            set.Collect(MapLabObjectKind.Deciduous, MapLabArtCategory.Vegetation, deciduous);
            set.Collect(MapLabObjectKind.Conifer, MapLabArtCategory.Vegetation, conifer);
            set.Collect(MapLabObjectKind.Bush, MapLabArtCategory.Vegetation, bush);
            deciduous.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            if (deciduous.Count > 0)
            {
                plan.Replaced.Add(MapLabObjectKind.Deciduous);
                plan.Replaced.Add(MapLabObjectKind.DeciduousGroup);
            }
            if (conifer.Count > 0)
                plan.Replaced.Add(MapLabObjectKind.Conifer);
            if (bush.Count > 0)
                plan.Replaced.Add(MapLabObjectKind.Bush);
            // Пятна одного вида: у каждого рисунка свой медленный шум; вес набора
            // умножается на его значение — рощи и смены пород без имён в коде.
            MapLabNoise grove = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "art03-grove", MapLabData.AlgorithmVersion));
            MapLabNoise tone = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "art03-tone", MapLabData.AlgorithmVersion));
            // Этап 04: крупный ритм — связные массы теплее/холоднее (медленный шум
            // в мире, общий для соседних деревьев), не случайный tint каждого дерева.
            MapLabNoise rhythm = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "art04-rhythm", MapLabData.AlgorithmVersion));
            float rhythmAmount = settings.IsStage04 ? settings.Stage04.ForestRhythm : 0f;
            List<float> weights = new List<float>();
            float treeSize = Mathf.Max(0.5f, settings.Vegetation.TreeSize);
            float range = s3.CrownToneRange;
            List<TreeRef> trees = new List<TreeRef>();
            for (int i = 0; i < data.Objects.Count; i++)
            {
                MapLabPlacement o = data.Objects[i];
                List<MapLabArtEntry> pool;
                float reference;
                switch (o.Kind)
                {
                    case MapLabObjectKind.Deciduous:
                    case MapLabObjectKind.DeciduousGroup:
                        pool = deciduous;
                        reference = treeSize * (o.Kind == MapLabObjectKind.DeciduousGroup ? 1.55f : 1f);
                        break;
                    case MapLabObjectKind.Conifer:
                        pool = conifer;
                        reference = treeSize;
                        break;
                    case MapLabObjectKind.Bush:
                        pool = bush;
                        reference = treeSize * 0.49f;
                        break;
                    default:
                        continue;
                }
                if (pool.Count == 0)
                    continue;
                Vector2 p = new Vector2(o.Position.x, o.Position.z);
                weights.Clear();
                for (int k = 0; k < pool.Count; k++)
                {
                    float g = grove.Fbm(p.x / 30f + k * 17.3f, p.y / 30f - k * 9.1f, 2) * 0.5f + 0.5f;
                    weights.Add(pool[k].EffectiveWeight * (0.3f + 1.4f * MapLabGeometry.SmoothStep(0.4f, 0.72f, g)));
                }
                // Спокойный массив: малый разброс яркости, связанный с местом.
                float place = tone.Fbm(p.x / 22f, p.y / 22f, 2);
                int copies = o.Kind == MapLabObjectKind.DeciduousGroup ? 3 : 1;
                for (int c = 0; c < copies; c++)
                {
                    MapLabArtEntry entry = Choose(pool, weights, Pick01(data.Seed, i, 11 + c * 7));
                    Vector3 basePoint = o.Position;
                    if (copies > 1)
                    {
                        float angle = (Pick01(data.Seed, i, 3) + c / 3f) * Mathf.PI * 2f;
                        Vector2 q = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reference * 0.22f;
                        basePoint = new Vector3(q.x, data.SampleHeight(q.x, q.y), q.y);
                    }
                    float scale = Mathf.Clamp(o.Scale / reference, entry.ScaleRange.x, entry.ScaleRange.y);
                    if (o.Kind != MapLabObjectKind.Bush && Pick01(data.Seed, i, 29 + c) < 0.05f)
                        scale *= 0.75f;
                    float slope = data.SampleSlope(basePoint.x, basePoint.z);
                    if (slope > entry.MaxSlope * 1.7f)
                        continue;
                    if (slope > entry.MaxSlope)
                        scale *= 0.8f;
                    float brightness = 1f + (place * 0.6f + (Pick01(data.Seed, i, 71 + c) * 2f - 1f) * 0.4f) * range;
                    MapLabArtInstance inst = Make(entry, basePoint, scale, brightness, settings.Art.PaletteTint, 0.5f, i);
                    inst.CrownMinV = entry.SolidRect.yMin + entry.SolidRect.height * 0.3f;
                    if (rhythmAmount > 0f)
                        inst.Warmth = Mathf.Clamp(rhythm.Fbm(basePoint.x / 48f, basePoint.z / 48f, 2) * 2.2f, -1f, 1f) * rhythmAmount;
                    float ink = s3.InkStyle.Width * 1.7f;
                    inst.InkUv = new Vector2(ink / Mathf.Max(0.1f, inst.Width), ink / Mathf.Max(0.1f, inst.Height));
                    plan.Add(entry, inst);
                    if (o.Kind != MapLabObjectKind.Bush)
                        trees.Add(new TreeRef { Entry = entry, Index = plan.Cards[entry].Count - 1, P = new Vector2(basePoint.x, basePoint.z), Radius = inst.Width * entry.SolidRect.width * 0.5f });
                }
            }
            MarkAccents(data, settings, plan, trees);
        }

        // Группы крон — ячейки Вороного мировой дрожащей сетки (стабильны при
        // том же seed). У части групп — цепочки «задних» крон: у такой кроны
        // нет соседа своей группы позади (выше по кадру), её верхняя кромка —
        // силуэт массы. Связные цепочки от AccentMinChain крон получают
        // акцент; одиночные кроны не обводятся.
        private static void MarkAccents(MapLabData data, MapLabSettings settings, Plan plan, List<TreeRef> trees)
        {
            MapLabStage03Settings s3 = settings.Stage03;
            ForestStats stats = new ForestStats { Trees = trees.Count };
            LastForest = stats;
            if (trees.Count == 0)
                return;
            float size = Mathf.Max(4f, s3.ForestGroupSize);
            uint stream = (uint)MapLabRandom.StreamSeed(data.Seed, "art03-groups", MapLabData.AlgorithmVersion);
            Dictionary<long, List<int>> groups = new Dictionary<long, List<int>>();
            long[] groupOf = new long[trees.Count];
            for (int t = 0; t < trees.Count; t++)
            {
                Vector2 p = trees[t].P;
                int gx = Mathf.FloorToInt(p.x / size), gz = Mathf.FloorToInt(p.y / size);
                float best = float.MaxValue;
                long id = 0;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = gx + dx, cz = gz + dz;
                    Vector2 site = new Vector2((cx + 0.15f + 0.7f * MapLabRandom.Hash01(cx, cz, stream)) * size, (cz + 0.15f + 0.7f * MapLabRandom.Hash01(cz, cx, stream ^ 0x5bd1e995u)) * size);
                    float d = (site - p).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        id = ((long)cx << 32) ^ (uint)cz;
                    }
                }
                groupOf[t] = id;
                if (!groups.TryGetValue(id, out List<int> list))
                    groups[id] = list = new List<int>();
                list.Add(t);
            }
            stats.Groups = groups.Count;
            // Метка группы для диагностики.
            for (int t = 0; t < trees.Count; t++)
            {
                long id = groupOf[t];
                SetInstance(plan, trees[t], inst => { inst.Group = MapLabRandom.Hash01((int)(id >> 32), (int)id, stream ^ 0x27d4eb2du); return inst; });
            }
            List<long> ids = new List<long>(groups.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                List<int> members = groups[id];
                if (members.Count < 6 || MapLabRandom.Hash01((int)(id >> 32), (int)id, stream ^ 0x165667b1u) >= s3.AccentShare)
                    continue;
                stats.AccentGroups++;
                // Задние кроны: никого своей группы непосредственно позади.
                List<int> back = new List<int>();
                foreach (int t in members)
                {
                    TreeRef a = trees[t];
                    bool covered = false;
                    foreach (int u in members)
                    {
                        if (u == t)
                            continue;
                        Vector2 d = trees[u].P - a.P;
                        float r = Mathf.Max(a.Radius, 0.3f);
                        if (d.y > r * 0.15f && d.y < r * 1.6f && Mathf.Abs(d.x) < r * 0.8f)
                        {
                            covered = true;
                            break;
                        }
                    }
                    if (!covered)
                        back.Add(t);
                }
                // Связные цепочки задних крон.
                bool[] seen = new bool[back.Count];
                for (int s = 0; s < back.Count; s++)
                {
                    if (seen[s])
                        continue;
                    List<int> chain = new List<int>();
                    Queue<int> queue = new Queue<int>();
                    queue.Enqueue(s);
                    seen[s] = true;
                    while (queue.Count > 0)
                    {
                        int k = queue.Dequeue();
                        chain.Add(back[k]);
                        for (int m = 0; m < back.Count; m++)
                        {
                            if (seen[m])
                                continue;
                            TreeRef a = trees[back[k]], b = trees[back[m]];
                            if (Vector2.Distance(a.P, b.P) < (a.Radius + b.Radius) * 1.25f)
                            {
                                seen[m] = true;
                                queue.Enqueue(m);
                            }
                        }
                    }
                    if (chain.Count < s3.AccentMinChain)
                        continue;
                    stats.Chains++;
                    float ridge = settings.IsStage04 ? settings.Stage04.ForestRhythm : 0f;
                    foreach (int t in chain)
                    {
                        SetInstance(plan, trees[t], inst => { inst.Accent = 1f; inst.Ridge = ridge; return inst; });
                        stats.AccentTrees++;
                    }
                }
            }
        }

        private static void SetInstance(Plan plan, TreeRef tree, System.Func<MapLabArtInstance, MapLabArtInstance> change)
        {
            List<MapLabArtInstance> list = plan.Cards[tree.Entry];
            list[tree.Index] = change(list[tree.Index]);
        }

        // Пары ракурсов: вариант избы → группа рисунка (данные настроек), кадр —
        // ближайший к pitchView. Тот же дом в обоих кадрах.
        public static void PlanHouses03(MapLabData data, MapLabSettings settings, MapLabArtSet set, float pitchView, Plan plan)
        {
            if (set == null)
                return;
            bool anyHouse = false, anyBarn = false;
            for (int i = 0; i < data.Objects.Count; i++)
            {
                MapLabPlacement o = data.Objects[i];
                if (o.Kind != MapLabObjectKind.House && o.Kind != MapLabObjectKind.Barn)
                    continue;
                string group = HouseGroup(settings, o);
                MapLabArtEntry entry = set.FindView(group, pitchView);
                if (entry == null)
                    continue;
                if (o.Kind == MapLabObjectKind.House) anyHouse = true; else anyBarn = true;
                float area = Mathf.Sqrt(Mathf.Max(1f, o.Size.x * o.Size.y));
                float reference = o.Kind == MapLabObjectKind.House ? 4.25f : 2.75f;
                float scale = Mathf.Clamp(area / reference, entry.ScaleRange.x, entry.ScaleRange.y);
                plan.Add(entry, Make(entry, o.Position, scale, 1f, 0f, 0.5f, i));
            }
            if (anyHouse)
                plan.Replaced.Add(MapLabObjectKind.House);
            if (anyBarn)
                plan.Replaced.Add(MapLabObjectKind.Barn);
        }

        public static string HouseGroup(MapLabSettings settings, MapLabPlacement o)
        {
            MapLabArtSettings art = settings.Art;
            if (o.Kind == MapLabObjectKind.Barn)
                return art.BarnGroup;
            string[] groups = art.HouseGroupsByVariant;
            if (groups == null || groups.Length == 0)
                return "";
            return groups[Mathf.Abs(o.Variant) % groups.Length];
        }
    }
}
