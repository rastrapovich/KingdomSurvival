using System.Collections.Generic;
using KingdomSurvival.MapLab;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // Декор арены из Базы ассетов (назначение «арена» или «оба»): отдельные
    // рисунки, расставленные явно, без готовых PNG-лесов.
    // - Деревья — несимметричными группами по краям и углам кадра; дерево не
    //   стоит там, где его рисунок закрыл бы клетку поля на экране.
    // - Пни и бурьян — у краёв групп, не на клетках.
    // - Низкая трава — пятнами с паузами, мимо стартовых клеток бойцов.
    // - Непроходимая клетка — валун или крупный пень во всю клетку.
    // Свой поток случайности ("arena-decor"): рельеф, клетки и правила боя от
    // декора и его плотности не зависят.
    public sealed partial class MapLabArena
    {
        private const string DecorKind = "arena-decor";

        private readonly List<(Vector2 Point, float Half)> decorPlaced = new List<(Vector2, float)>();

        private void PlaceDecor(MapLabComposition c, List<Vector2> cells, Vector2 frameCenter)
        {
            MapLabRandom rng = MapLabRandom.ForStream(Spec.Seed, "arena-decor", MapLabData.AlgorithmVersion);
            float hex = Spec.HexWorld;
            float density = Mathf.Clamp(Spec.DecorDensity, 0f, 3f);
            float lean = cos / Mathf.Max(0.05f, sin);
            Rect frame = new Rect(Margin, zBottom, Spec.Aspect * k, depth);
            List<MapLabDecorArt> trees = Role(MapLabDecorRole.Tree), stumps = Role(MapLabDecorRole.Stump),
                tall = Role(MapLabDecorRole.TallPlant), low = Role(MapLabDecorRole.LowGrass),
                boulders = Role(MapLabDecorRole.Boulder), small = Role(MapLabDecorRole.SmallRock);
            List<Vector2> keep = new List<Vector2>();
            foreach (Vector2 point in Spec.KeepClear)
                keep.Add(ToWorld(point));
            List<Vector2> blocked = new List<Vector2>();
            decorPlaced.Clear();
            DecorFeatures = 0;

            void Add(Vector2 p, string id, float half)
            {
                c.Features.Add((p, id, DecorKind));
                decorPlaced.Add((p, half));
                DecorFeatures++;
            }

            // Рисунок в p закрывает клетку: стоит на ней (или вплотную) либо
            // клетка позади него на экране в пределах высоты рисунка.
            bool Covers(Vector2 p, float height, float half, float near)
            {
                foreach (Vector2 cell in cells)
                {
                    if (Vector2.Distance(p, cell) < near)
                        return true;
                    if (Mathf.Abs(cell.x - p.x) < half * 0.75f + hex * 0.8f && cell.y > p.y - hex * 0.3f && cell.y - p.y < height * lean + hex * 0.9f)
                        return true;
                }
                return false;
            }

            bool Crowded(Vector2 p, float half, float share)
            {
                foreach ((Vector2 point, float other) in decorPlaced)
                    if (Vector2.Distance(point, p) < (half + other) * share)
                        return true;
                return false;
            }

            bool Near(List<Vector2> points, Vector2 p, float distance)
            {
                foreach (Vector2 point in points)
                    if (Vector2.Distance(point, p) < distance)
                        return true;
                return false;
            }

            bool InView(Vector2 p, float height, float half) =>
                p.x > frame.xMin - half && p.x < frame.xMax + half && p.y > frame.yMin - height * lean && p.y < frame.yMax + 0.5f;

            // 1. Непроходимые клетки — валун или крупный пень во всю клетку.
            List<MapLabDecorArt> blockers = new List<MapLabDecorArt>(boulders);
            foreach (MapLabDecorArt stump in stumps)
                if (stump.HalfWidth > 0f)
                    blockers.Add(stump);
            int index = 0;
            foreach ((Vector2 center, MapLabArenaCellKind kind) in Spec.Cells)
            {
                index++;
                if (kind != MapLabArenaCellKind.Blocked)
                    continue;
                Vector2 p = ToWorld(center);
                blocked.Add(p);
                if (blockers.Count == 0)
                {
                    int old = (int)(MapLabRandom.StreamSeed(Spec.Seed, "arena-block-" + index, MapLabData.AlgorithmVersion) % (ulong)BlockArt.Length);
                    c.Features.Add((p, BlockArt[old], BlockKind));
                    continue;
                }
                // Валуны чаще пней; рисунок растягивается на ширину клетки.
                MapLabDecorArt art = boulders.Count > 0 && (stumps.Count == 0 || rng.Value() < 0.7f)
                    ? boulders[(int)(rng.Value() * boulders.Count) % boulders.Count]
                    : blockers[(int)(rng.Value() * blockers.Count) % blockers.Count];
                float height = art.Height * (hex * 1.5f) / Mathf.Max(0.05f, art.HalfWidth * 2f);
                MapLabArtEntry sized = MapLabArtAssets.AddSized(decorSet, art, MapLabArtAssets.BlockSuffix, height);
                if (sized == null)
                    continue;
                c.Features.Add((p, sized.Id, BlockKind));
                decorPlaced.Add((p, hex * 0.75f));
            }

            // 2. Группы деревьев по краям и углам.
            if (trees.Count > 0)
            {
                trees.Sort((a, b) => a.Height.CompareTo(b.Height));
                float typical = trees[trees.Count / 2].Height, typicalHalf = trees[trees.Count / 2].HalfWidth;
                List<(Vector2 Point, float Weight)> seeds = new List<(Vector2, float)>();
                for (float z = frame.yMin - typical * lean * 0.6f; z <= frame.yMax; z += hex * 1.2f)
                for (float x = frame.xMin - hex; x <= frame.xMax + hex; x += hex * 1.2f)
                {
                    Vector2 p = new Vector2(x + rng.Range(-0.5f, 0.5f) * hex, z + rng.Range(-0.5f, 0.5f) * hex);
                    if (Covers(p, typical * 0.8f, typicalHalf * 0.8f, hex * 1.4f))
                        continue;
                    Vector2 offset = p - frameCenter;
                    float corner = Mathf.Clamp01(Mathf.Abs(offset.x) / (frame.width * 0.5f)) * Mathf.Clamp01(Mathf.Abs(offset.y) / (frame.height * 0.5f));
                    seeds.Add((p, 0.35f + corner + rng.Value() * 0.4f));
                }
                int groups = Mathf.RoundToInt(Mathf.Lerp(9f, 13f, rng.Value()) * Mathf.Sqrt(Mathf.Max(0.2f, density)));
                List<Vector2> centers = new List<Vector2>();
                for (int g = 0; g < groups && seeds.Count > 0; g++)
                {
                    float total = 0f;
                    foreach ((Vector2 Point, float Weight) seed in seeds) total += seed.Weight;
                    float pick = rng.Value() * total;
                    int chosen = 0;
                    for (; chosen < seeds.Count - 1; chosen++)
                    {
                        pick -= seeds[chosen].Weight;
                        if (pick <= 0f) break;
                    }
                    Vector2 center = seeds[chosen].Point;
                    seeds.RemoveAll(seed => Vector2.Distance(seed.Point, center) < hex * 3.2f);
                    centers.Add(center);
                }
                foreach (Vector2 center in centers)
                {
                    // Группа — одна-две породы, ядро крупнее, к краю моложе.
                    MapLabDecorArt main = trees[(int)(rng.Value() * trees.Count) % trees.Count];
                    MapLabDecorArt second = trees[(int)(rng.Value() * trees.Count) % trees.Count];
                    int count = 3 + (int)(rng.Value() * 6f);
                    for (int attempt = 0, placed = 0; attempt < count * 6 && placed < count; attempt++)
                    {
                        float radius = Mathf.Sqrt(rng.Value());
                        float angle = rng.Value() * Mathf.PI * 2f;
                        Vector2 q = center + new Vector2(Mathf.Cos(angle) * hex * 2.6f, Mathf.Sin(angle) * hex * 1.8f) * radius;
                        MapLabDecorArt art = rng.Value() < 0.65f ? main : second;
                        if (radius > 0.7f && rng.Value() < 0.5f)
                            art = trees[(int)(rng.Value() * Mathf.Max(1, trees.Count / 2)) % trees.Count];
                        if (!InView(q, art.Height, art.HalfWidth) || Covers(q, art.Height, art.HalfWidth, hex * 1.3f) || Crowded(q, art.HalfWidth * 0.5f, 0.55f))
                            continue;
                        Add(q, art.Id, art.HalfWidth * 0.5f);
                        placed++;
                    }
                    // У края группы, ближе к полю — пень или бурьян.
                    Vector2 toward = (frameCenter - center).normalized;
                    for (int extra = 0; extra < 2; extra++)
                    {
                        List<MapLabDecorArt> pool = extra == 0 ? tall : stumps;
                        if (pool.Count == 0 || rng.Value() > 0.65f * Mathf.Min(1f, density))
                            continue;
                        MapLabDecorArt art = pool[(int)(rng.Value() * pool.Count) % pool.Count];
                        for (int attempt = 0; attempt < 6; attempt++)
                        {
                            Vector2 q = center + toward * hex * rng.Range(1.8f, 3.2f) + new Vector2(rng.Range(-1f, 1f), rng.Range(-0.6f, 0.6f)) * hex;
                            if (!InView(q, art.Height, art.HalfWidth) || Covers(q, art.Height, art.HalfWidth, hex * 1.0f) || Crowded(q, art.HalfWidth * 0.4f, 0.6f))
                                continue;
                            Add(q, art.Id, art.HalfWidth * 0.4f);
                            break;
                        }
                    }
                }
            }

            // 3. Низкая трава пятнами с паузами (поле и окраины), мимо стартов
            // и камней на клетках. Пятно — одна-две травы, разные размеры.
            if (low.Count > 0)
            {
                int patches = Mathf.RoundToInt(Mathf.Lerp(8f, 13f, rng.Value()) * density);
                for (int patch = 0; patch < patches; patch++)
                {
                    Vector2 center = new Vector2(rng.Range(frame.xMin, frame.xMax), rng.Range(frame.yMin, frame.yMax));
                    float spread = hex * rng.Range(0.6f, 1.6f);
                    MapLabDecorArt first = low[(int)(rng.Value() * low.Count) % low.Count];
                    MapLabDecorArt other = low[(int)(rng.Value() * low.Count) % low.Count];
                    int count = 2 + (int)(rng.Value() * 6f);
                    for (int i = 0; i < count; i++)
                    {
                        // Гуще к середине пятна, реже к краю (не ровный ковёр).
                        float radius = rng.Value() * rng.Value();
                        float angle = rng.Value() * Mathf.PI * 2f;
                        Vector2 q = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.7f) * spread * radius;
                        MapLabDecorArt art = rng.Value() < 0.7f ? first : other;
                        if (Near(keep, q, hex * 0.8f) || Near(blocked, q, hex * 0.75f) || Crowded(q, art.HalfWidth * 0.35f, 0.7f))
                            continue;
                        Add(q, art.Id, art.HalfWidth * 0.35f);
                    }
                }
            }

            // 4. Редкие мелкие камни на окраинах.
            if (small.Count > 0)
            {
                int rocks = Mathf.RoundToInt(rng.Range(2f, 5f) * density);
                for (int i = 0, attempt = 0; i < rocks && attempt < rocks * 8; attempt++)
                {
                    MapLabDecorArt art = small[(int)(rng.Value() * small.Count) % small.Count];
                    Vector2 q = new Vector2(rng.Range(frame.xMin, frame.xMax), rng.Range(frame.yMin, frame.yMax));
                    if (Near(keep, q, hex) || Near(cells, q, hex * 1.3f) || Crowded(q, art.HalfWidth * 0.5f, 0.8f))
                        continue;
                    Add(q, art.Id, art.HalfWidth * 0.5f);
                    i++;
                }
            }
        }

        private List<MapLabDecorArt> Role(MapLabDecorRole role) => decorArt.FindAll(art => art.Role == role);
    }
}
