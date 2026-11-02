using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomSurvival.MapLab
{
    // Один экземпляр нарисованной карточки: опора в мире и размеры холста.
    public struct MapLabArtInstance
    {
        public int ObjectIndex;   // индекс в data.Objects (-1 — оформление уступа)
        public Vector3 Base;
        public float Height;      // высота холста в мире
        public float Width;       // ширина холста в мире
        public Vector2 Pivot;
        public float Brightness;
        public float Tint;
        public float GroundShade;
    }

    // Выбор и сборка нарисованных карточек поверх готовых данных карты. Выбор
    // оформления — отдельный поток случайности от seed и индекса объекта:
    // переключение Procedural/Art не двигает дороги, дома и деревья и не
    // меняет seed географии.
    public static class MapLabArtBuilder
    {
        public const string PickStream = "art-pick";

        public sealed class Plan
        {
            public readonly Dictionary<MapLabArtEntry, List<MapLabArtInstance>> Cards = new Dictionary<MapLabArtEntry, List<MapLabArtInstance>>();
            // Какие типы объектов заменены рисунками (их процедурный вид не строится).
            public readonly HashSet<MapLabObjectKind> Replaced = new HashSet<MapLabObjectKind>();
            public int Count;

            public void Add(MapLabArtEntry entry, MapLabArtInstance instance)
            {
                if (!Cards.TryGetValue(entry, out List<MapLabArtInstance> list))
                    Cards[entry] = list = new List<MapLabArtInstance>();
                list.Add(instance);
                Count++;
            }
        }

        private static uint PickHash(int seed, int index, int salt)
        {
            uint stream = (uint)MapLabRandom.StreamSeed(seed, PickStream, MapLabData.AlgorithmVersion);
            return MapLabRandom.Hash(index, salt, stream);
        }

        private static float Pick01(int seed, int index, int salt)
        {
            return (PickHash(seed, index, salt) & 0xFFFFFF) / 16777216f;
        }

        public static MapLabArtEntry Choose(List<MapLabArtEntry> candidates, List<float> weights, float roll)
        {
            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
                total += Mathf.Max(0f, weights[i]);
            if (total <= 0f)
                return candidates.Count > 0 ? candidates[0] : null;
            float r = roll * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                r -= Mathf.Max(0f, weights[i]);
                if (r <= 0f)
                    return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        private static MapLabArtInstance Make(MapLabArtEntry entry, Vector3 basePoint, float scale, float brightness, float tint, float groundShade, int index)
        {
            float height = entry.CardHeight * scale;
            return new MapLabArtInstance
            {
                ObjectIndex = index,
                Base = basePoint,
                Height = height,
                Width = height * entry.Aspect,
                Pivot = entry.Pivot,
                Brightness = brightness,
                Tint = tint,
                GroundShade = groundShade,
            };
        }

        // ---------- Растительность ----------

        // Лес в три масштаба: территория (маска леса), группа крон (развёрнутая
        // карточка группы и пятна одного вида), отдельное дерево. Ядро — дуб,
        // опушка и луг — больше раскидистых и берёз; берёзы — рощицами по шуму.
        public static void PlanVegetation(MapLabData data, MapLabSettings settings, MapLabArtSet set, Plan plan)
        {
            if (set == null || settings.Art.Vegetation != MapLabArtMode.Art)
                return;
            List<MapLabArtEntry> deciduous = new List<MapLabArtEntry>();
            List<MapLabArtEntry> conifer = new List<MapLabArtEntry>();
            List<MapLabArtEntry> bush = new List<MapLabArtEntry>();
            set.Collect(MapLabObjectKind.Deciduous, MapLabArtCategory.Vegetation, deciduous);
            set.Collect(MapLabObjectKind.Conifer, MapLabArtCategory.Vegetation, conifer);
            set.Collect(MapLabObjectKind.Bush, MapLabArtCategory.Vegetation, bush);
            if (deciduous.Count > 0)
            {
                plan.Replaced.Add(MapLabObjectKind.Deciduous);
                plan.Replaced.Add(MapLabObjectKind.DeciduousGroup);
            }
            if (conifer.Count > 0)
                plan.Replaced.Add(MapLabObjectKind.Conifer);
            if (bush.Count > 0)
                plan.Replaced.Add(MapLabObjectKind.Bush);
            MapLabNoise grove = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "art-grove", MapLabData.AlgorithmVersion));
            List<float> weights = new List<float>();
            float treeSize = Mathf.Max(0.5f, settings.Vegetation.TreeSize);
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
                float forest = data.SampleMask(data.Forest, p.x, p.y);
                float g = grove.Fbm(p.x / 34f, p.y / 34f, 2) * 0.5f + 0.5f;
                weights.Clear();
                for (int k = 0; k < pool.Count; k++)
                    weights.Add(SpeciesWeight(pool[k], forest, g));
                int copies = o.Kind == MapLabObjectKind.DeciduousGroup ? 3 : 1;
                for (int c = 0; c < copies; c++)
                {
                    MapLabArtEntry entry = Choose(pool, weights, Pick01(data.Seed, i, 11 + c * 7));
                    Vector3 basePoint = o.Position;
                    if (copies > 1)
                    {
                        // Группа крон: три дерева тесным треугольником вместо одной карточки.
                        float angle = (Pick01(data.Seed, i, 3) + c / 3f) * Mathf.PI * 2f;
                        float radius = reference * 0.22f;
                        Vector2 q = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                        basePoint = new Vector3(q.x, data.SampleHeight(q.x, q.y), q.y);
                    }
                    float ratio = o.Scale / reference;
                    float scale = Mathf.Clamp(ratio, entry.ScaleRange.x, entry.ScaleRange.y);
                    // Редкий молодой подрост.
                    if (o.Kind != MapLabObjectKind.Bush && Pick01(data.Seed, i, 29 + c) < 0.05f)
                        scale *= 0.62f;
                    float slope = data.SampleSlope(basePoint.x, basePoint.z);
                    if (slope > entry.MaxSlope * 1.7f)
                        continue;
                    if (slope > entry.MaxSlope)
                        scale *= 0.78f;
                    plan.Add(entry, Make(entry, basePoint, scale, o.Brightness, settings.Art.PaletteTint * (0.5f + o.Tone * 0.5f), 1f, i));
                }
            }
        }

        private static float SpeciesWeight(MapLabArtEntry entry, float forest, float grove)
        {
            string id = entry.Id ?? "";
            float w = entry.Weight;
            if (id.Contains("Birch"))
                // Берёзы — рощицами и на опушке, мало в глубине.
                return w * Mathf.Lerp(1.4f, 0.35f, forest) * Mathf.Lerp(0.15f, 2.6f, Mathf.SmoothStep(0.45f, 0.75f, grove));
            if (id.Contains("Spreading"))
                // Раскидистые — одиночки на лугу и в опушке.
                return w * Mathf.Lerp(1.9f, 0.55f, forest);
            return w * Mathf.Lerp(0.8f, 1.15f, forest);
        }

        // ---------- Постройки и валуны ----------

        public static void PlanBuildings(MapLabData data, MapLabSettings settings, MapLabArtSet set, Plan plan)
        {
            if (set == null)
                return;
            List<MapLabArtEntry> houses = new List<MapLabArtEntry>();
            List<MapLabArtEntry> barns = new List<MapLabArtEntry>();
            set.Collect(MapLabObjectKind.House, MapLabArtCategory.Building, houses);
            set.Collect(MapLabObjectKind.Barn, MapLabArtCategory.Building, barns);
            if (houses.Count > 0)
                plan.Replaced.Add(MapLabObjectKind.House);
            if (barns.Count > 0)
                plan.Replaced.Add(MapLabObjectKind.Barn);
            for (int i = 0; i < data.Objects.Count; i++)
            {
                MapLabPlacement o = data.Objects[i];
                List<MapLabArtEntry> pool = o.Kind == MapLabObjectKind.House ? houses : o.Kind == MapLabObjectKind.Barn ? barns : null;
                if (pool == null || pool.Count == 0)
                    continue;
                MapLabArtEntry entry = pool[0];
                if (pool.Count > 1)
                {
                    // Кровля — как у процедурного дома того же варианта: солома или дранка.
                    bool thatch = o.Variant % 4 < 2;
                    foreach (MapLabArtEntry e in pool)
                        if ((e.Id ?? "").Contains("Thatch") == thatch)
                            entry = e;
                }
                float area = Mathf.Sqrt(Mathf.Max(1f, o.Size.x * o.Size.y));
                float reference = o.Kind == MapLabObjectKind.House ? 4.25f : 2.75f;
                float scale = Mathf.Clamp(area / reference, entry.ScaleRange.x, entry.ScaleRange.y);
                plan.Add(entry, Make(entry, o.Position, scale, o.Brightness, settings.Art.PaletteTint * 0.5f, 1f, i));
            }
        }

        public static void PlanBoulders(MapLabData data, MapLabSettings settings, MapLabArtSet set, Plan plan)
        {
            if (set == null || settings.Art.Boulders != MapLabArtMode.Art)
                return;
            List<MapLabArtEntry> rocks = new List<MapLabArtEntry>();
            set.Collect(MapLabObjectKind.Boulder, MapLabArtCategory.Rock, rocks);
            if (rocks.Count == 0)
                return;
            plan.Replaced.Add(MapLabObjectKind.Boulder);
            List<float> weights = new List<float>();
            foreach (MapLabArtEntry e in rocks)
                weights.Add(e.Weight);
            for (int i = 0; i < data.Objects.Count; i++)
            {
                MapLabPlacement o = data.Objects[i];
                if (o.Kind != MapLabObjectKind.Boulder)
                    continue;
                MapLabArtEntry entry = Choose(rocks, weights, Pick01(data.Seed, i, 41));
                float scale = Mathf.Lerp(entry.ScaleRange.x, entry.ScaleRange.y, Mathf.InverseLerp(0.6f, 2f, o.Scale));
                // Валун садится глубже в землю, чем дерево.
                Vector3 basePoint = o.Position - Vector3.up * 0.15f * scale;
                plan.Add(entry, Make(entry, basePoint, scale, o.Brightness, settings.Art.PaletteTint * 0.4f, 1f, i));
            }
        }

        // Декоративные фрагменты на обращённой к камере стороне уступа: короткие
        // участки Cliff_Short, окончания — Cliff_End. Без поворотов и отражений:
        // ставятся только там, где стенка смотрит на камеру; геометрия остаётся.
        public static void PlanCliffDecor(MapLabData data, MapLabSettings settings, MapLabArtSet set, float cameraAzimuth, Plan plan)
        {
            if (set == null || !settings.Art.CliffDecor)
                return;
            MapLabArtEntry wall = null, end = null;
            foreach (MapLabArtEntry e in set.Entries)
            {
                if (e == null || !e.HasTexture || e.Category != MapLabArtCategory.Cliff)
                    continue;
                if (e.ManifestKind == "GrassTransition")
                    end = e;
                else
                    wall = e;
            }
            if (wall == null)
                return;
            Vector2 toCamera = -new Vector2(Mathf.Sin(cameraAzimuth * Mathf.Deg2Rad), Mathf.Cos(cameraAzimuth * Mathf.Deg2Rad));
            float sharp = Mathf.Max(0.4f, data.CliffSharpness);
            for (int k = 0; k < data.CliffLines.Count; k++)
            {
                MapLabCliffLine line = data.CliffLines[k];
                List<MapLabCliffColumn> cols = line.Columns;
                float run = 0f;
                float nextAt = 0f;
                int runStart = -1;
                for (int i = 0; i <= cols.Count; i++)
                {
                    bool facing = i < cols.Count && Vector2.Dot(cols[i].Outward, toCamera) > 0.55f && cols[i].Top - cols[i].Bottom > 2.6f;
                    if (!facing)
                    {
                        // Ступенчатое окончание нарисовано слева от стенки (без отражения) —
                        // ставится только на левом по кадру конце длинного участка.
                        if (runStart >= 0 && end != null && run > 8f)
                        {
                            Vector2 right = new Vector2(Mathf.Cos(cameraAzimuth * Mathf.Deg2Rad), -Mathf.Sin(cameraAzimuth * Mathf.Deg2Rad));
                            MapLabCliffColumn first = cols[runStart], last = cols[i - 1];
                            MapLabCliffColumn left = Vector2.Dot(first.Start + first.End, right) <= Vector2.Dot(last.Start + last.End, right) ? first : last;
                            if (left.Top - left.Bottom > 3f)
                                AddCliffCard(data, plan, end, left, sharp, 0.85f, -1 - k);
                        }
                        run = 0f;
                        nextAt = 0f;
                        runStart = -1;
                        continue;
                    }
                    if (runStart < 0)
                        runStart = i;
                    MapLabCliffColumn c = cols[i];
                    float width = Vector2.Distance(c.Start, c.End);
                    if (run >= nextAt)
                    {
                        float cardHeight = (c.Top - c.Bottom - 0.6f) * 1.15f;
                        float roll = Pick01(data.Seed, k * 1000 + i, 53);
                        if (roll < 0.62f)
                        {
                            MapLabArtInstance inst = AddCliffCard(data, plan, wall, c, sharp, 0.95f + roll * 0.2f, -1 - k);
                            nextAt = run + inst.Width * Mathf.Lerp(0.95f, 1.6f, roll);
                        }
                        else
                        {
                            // Просвет: геометрия уступа видна между фрагментами.
                            nextAt = run + cardHeight * 1.4f;
                        }
                    }
                    run += width;
                }
            }
        }

        private static MapLabArtInstance AddCliffCard(MapLabData data, Plan plan, MapLabArtEntry entry, MapLabCliffColumn c, float sharp, float scale, int index)
        {
            Vector2 mid = (c.Start + c.End) * 0.5f + c.Outward * sharp * 0.6f;
            float bottom = c.Bottom + 0.7f;
            float wallHeight = Mathf.Max(1f, c.Top - bottom);
            float s = wallHeight * 1.15f / Mathf.Max(0.5f, entry.WorldHeight) * scale;
            MapLabArtInstance inst = Make(entry, new Vector3(mid.x, Mathf.Max(bottom, data.SampleHeight(mid.x, mid.y)) - 0.25f, mid.y), s, 1f, 0f, 0.8f, index);
            plan.Add(entry, inst);
            return inst;
        }

        // ---------- Меши ----------

        // Карточки одного рисунка, по чанкам карты. Каждая карточка — два квада,
        // разрезанных по строке опоры: часть ниже опоры сдвигается шейдером к
        // камере (видимое основание не уходит под землю).
        public static List<Mesh> BuildMeshes(MapLabData data, MapLabArtEntry entry, List<MapLabArtInstance> instances, float chunkSize)
        {
            Dictionary<int, List<int>> byChunk = new Dictionary<int, List<int>>();
            int chunks = Mathf.Max(1, Mathf.CeilToInt(data.Size / chunkSize));
            for (int i = 0; i < instances.Count; i++)
            {
                Vector3 p = instances[i].Base;
                int cx = Mathf.Clamp((int)(p.x / chunkSize), 0, chunks - 1);
                int cz = Mathf.Clamp((int)(p.z / chunkSize), 0, chunks - 1);
                int key = cz * chunks + cx;
                if (!byChunk.TryGetValue(key, out List<int> list))
                    byChunk[key] = list = new List<int>();
                list.Add(i);
            }
            List<Mesh> meshes = new List<Mesh>();
            List<int> keys = new List<int>(byChunk.Keys);
            keys.Sort();
            foreach (int key in keys)
            {
                List<int> list = byChunk[key];
                List<Vector3> positions = new List<Vector3>(list.Count * 8);
                List<Vector2> uv0 = new List<Vector2>(list.Count * 8);
                List<Vector2> uv1 = new List<Vector2>(list.Count * 8);
                List<Color> colors = new List<Color>(list.Count * 8);
                List<int> indices = new List<int>(list.Count * 12);
                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                foreach (int index in list)
                {
                    MapLabArtInstance o = instances[index];
                    Color color = new Color(Mathf.Clamp01(o.Brightness / 1.25f), Mathf.Clamp01(o.Tint), Mathf.Clamp01(o.GroundShade), 1f);
                    AppendCard(o, color, positions, uv0, uv1, colors, indices);
                    float reach = Mathf.Max(o.Width, o.Height) * 2.5f + 4f;
                    min = Vector3.Min(min, o.Base - new Vector3(reach, reach, reach));
                    max = Vector3.Max(max, o.Base + new Vector3(reach, reach, reach));
                }
                Mesh mesh = new Mesh { name = $"MapLab Art {entry.Id} {key}", hideFlags = HideFlags.DontSave };
                if (positions.Count > 65000)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(positions);
                mesh.SetUVs(0, uv0);
                mesh.SetUVs(1, uv1);
                mesh.SetColors(colors);
                mesh.SetTriangles(indices, 0, false);
                // Карточки разворачиваются и сдвигаются в шейдере — границы с запасом.
                mesh.bounds = new Bounds((min + max) * 0.5f, max - min);
                meshes.Add(mesh);
            }
            return meshes;
        }

        public static void AppendCard(MapLabArtInstance o, Color color, List<Vector3> positions, List<Vector2> uv0, List<Vector2> uv1, List<Color> colors, List<int> indices)
        {
            float x0 = -o.Pivot.x * o.Width, x1 = (1f - o.Pivot.x) * o.Width;
            float y0 = -o.Pivot.y * o.Height, y1 = (1f - o.Pivot.y) * o.Height;
            float v = Mathf.Clamp01(o.Pivot.y);
            // Нижний квад: [0..pivot], верхний: [pivot..1].
            float[] ys = { y0, 0f, 0f, y1 };
            float[] vs = { 0f, v, v, 1f };
            for (int part = 0; part < 2; part++)
            {
                float ya = ys[part * 2], yb = ys[part * 2 + 1];
                if (yb - ya < 1e-4f)
                    continue;
                int start = positions.Count;
                for (int k = 0; k < 4; k++)
                {
                    positions.Add(o.Base);
                    colors.Add(color);
                }
                float va = vs[part * 2], vb = vs[part * 2 + 1];
                uv0.Add(new Vector2(0f, va)); uv1.Add(new Vector2(x0, ya));
                uv0.Add(new Vector2(1f, va)); uv1.Add(new Vector2(x1, ya));
                uv0.Add(new Vector2(1f, vb)); uv1.Add(new Vector2(x1, yb));
                uv0.Add(new Vector2(0f, vb)); uv1.Add(new Vector2(x0, yb));
                indices.Add(start); indices.Add(start + 3); indices.Add(start + 2);
                indices.Add(start); indices.Add(start + 2); indices.Add(start + 1);
            }
        }
    }
}
