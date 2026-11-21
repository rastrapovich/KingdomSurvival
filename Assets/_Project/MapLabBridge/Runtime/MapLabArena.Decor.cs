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
        // Где легла трава (пятна, ковёр, россыпь) — для проверки густоты на поле и за ним.
        private readonly List<Vector2> grassPoints = new List<Vector2>();
        private List<Vector2> decorCells = new List<Vector2>();

        // Доля травы на поле клеток (ближе 0,9 гекса к центру клетки) от всей травы в кадре.
        public float GrassOnFieldShare
        {
            get
            {
                Rect frame = new Rect(Margin, zBottom, Spec.Aspect * k, depth);
                int inside = 0, total = 0;
                foreach (Vector2 p in grassPoints)
                {
                    if (!frame.Contains(p)) continue;
                    total++;
                    foreach (Vector2 cell in decorCells)
                        if (Vector2.Distance(cell, p) < Spec.HexWorld * 0.9f) { inside++; break; }
                }
                return total > 0 ? inside / (float)total : 0f;
            }
        }

        private void PlaceDecor(MapLabComposition c, List<Vector2> cells, Vector2 frameCenter)
        {
            MapLabRandom rng = MapLabRandom.ForStream(Spec.Seed, "arena-decor", MapLabData.AlgorithmVersion);
            GrassZone zone = new GrassZone(this, cells);
            grassPoints.Clear();
            decorCells = cells;
            float hex = Spec.HexWorld;
            MapLabArenaLook look = Look;
            float density = Mathf.Clamp(Spec.DecorDensity, 0f, 5f);
            float lean = cos / Mathf.Max(0.05f, sin);
            Rect frame = new Rect(Margin, zBottom, Spec.Aspect * k, depth);
            List<MapLabDecorArt> trees = Role(MapLabDecorRole.Tree), stumps = Role(MapLabDecorRole.Stump),
                tall = Role(MapLabDecorRole.TallPlant), low = Role(MapLabDecorRole.LowGrass), tallGrass = Role(MapLabDecorRole.TallGrass),
                boulders = Role(MapLabDecorRole.Boulder), small = Role(MapLabDecorRole.SmallRock);
            List<Vector2> keep = new List<Vector2>();
            foreach (Vector2 point in Spec.KeepClear)
                keep.Add(ToWorld(point));
            List<Vector2> blocked = new List<Vector2>();
            // Вода реки: ни травы, ни мелочи.
            blocked.AddRange(riverWorld);
            decorPlaced.Clear();
            groundArtIds.Clear();
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
                MapLabDecorArt art = boulders.Count > 0 && (stumps.Count == 0 || rng.Value() < look.BlockBoulderShare)
                    ? MapLabArtAssets.Pick(boulders, rng.Value())
                    : MapLabArtAssets.Pick(blockers, rng.Value());
                float height = art.Height * (hex * 1.5f) / Mathf.Max(0.05f, art.HalfWidth * 2f);
                MapLabArtEntry sized = MapLabArtAssets.AddSized(decorSet, art, MapLabArtAssets.BlockSuffix, height);
                if (sized == null)
                    continue;
                c.Features.Add((p, sized.Id, BlockKind));
                decorPlaced.Add((p, hex * 0.75f));
            }

            // 1б. Объекты местности боя — рисунком своего слота, во всю клетку.
            List<MapLabDecorArt> objectArt = Role(MapLabDecorRole.ArenaObject);
            foreach ((Vector2 center, string kind) in Spec.Objects)
            {
                List<MapLabDecorArt> pool = objectArt.FindAll(art => art.ObjectKind == kind);
                if (pool.Count == 0)
                    continue;
                Vector2 p = ToWorld(center);
                MapLabDecorArt art = MapLabArtAssets.Pick(pool, rng.Value());
                float height = art.Height * (hex * 1.6f) / Mathf.Max(0.05f, art.HalfWidth * 2f);
                MapLabArtEntry sized = MapLabArtAssets.AddSized(decorSet, art, MapLabArtAssets.ObjectSuffix, height);
                if (sized == null)
                    continue;
                c.Features.Add((p, sized.Id, BlockKind));
                // Настил моста — пол: под сеткой, не в слое объектов.
                if (kind == "bridge" || kind == "bridge_deck")
                    groundArtIds.Add(sized.Id);
                decorPlaced.Add((p, hex * 0.75f));
                blocked.Add(p);
            }

            // 1в. Щебень: кроме краски земли — 2–3 мелких камня на клетке
            // (пол: под сеткой клеток, не в слое объектов).
            ulong rubbleBit = MapLabArenaSurfaces.Bit(MapLabArenaSurface.Rubble);
            if (small.Count > 0)
                foreach ((Vector2 center, ulong layers) in Spec.Surfaces)
                {
                    if ((layers & rubbleBit) == 0UL)
                        continue;
                    Vector2 p = ToWorld(center);
                    int stones = look.RubbleStonesMin + (int)(rng.Value() * Mathf.Max(1, look.RubbleStonesMax - look.RubbleStonesMin + 1));
                    for (int i = 0; i < stones; i++)
                    {
                        MapLabDecorArt art = MapLabArtAssets.Pick(small, rng.Value());
                        float angle = rng.Value() * Mathf.PI * 2f;
                        Vector2 q = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * hex * rng.Range(0.15f, 0.6f);
                        c.Features.Add((q, art.Id, DecorKind));
                        groundArtIds.Add(art.Id);
                        DecorFeatures++;
                    }
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
                    if (Covers(p, typical * 0.8f, typicalHalf * 0.8f, hex * (Mathf.Approximately(look.TreeClearance, 1.3f) ? 1.4f : look.TreeClearance + 0.1f)))
                        continue;
                    Vector2 offset = p - frameCenter;
                    float corner = Mathf.Clamp01(Mathf.Abs(offset.x) / (frame.width * 0.5f)) * Mathf.Clamp01(Mathf.Abs(offset.y) / (frame.height * 0.5f));
                    seeds.Add((p, 0.35f + corner + rng.Value() * 0.4f));
                }
                int groups = Mathf.RoundToInt(Mathf.Lerp(look.TreeGroupsMin, look.TreeGroupsMax, rng.Value()) * Mathf.Sqrt(Mathf.Max(0.2f, density)));
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
                    seeds.RemoveAll(seed => Vector2.Distance(seed.Point, center) < hex * look.TreeGroupGap);
                    centers.Add(center);
                }
                foreach (Vector2 center in centers)
                {
                    // Группа — одна-две породы, ядро крупнее, к краю моложе.
                    MapLabDecorArt main = MapLabArtAssets.Pick(trees, rng.Value());
                    MapLabDecorArt second = MapLabArtAssets.Pick(trees, rng.Value());
                    int count = look.TreesPerGroupMin + (int)(rng.Value() * Mathf.Max(1, look.TreesPerGroupMax - look.TreesPerGroupMin + 1));
                    for (int attempt = 0, placed = 0; attempt < count * 6 && placed < count; attempt++)
                    {
                        float radius = Mathf.Sqrt(rng.Value());
                        float angle = rng.Value() * Mathf.PI * 2f;
                        Vector2 q = center + new Vector2(Mathf.Cos(angle) * hex * 2.6f, Mathf.Sin(angle) * hex * 1.8f) * radius * look.TreeGroupSpread;
                        MapLabDecorArt art = rng.Value() < look.TreeMainSpecies ? main : second;
                        if (radius > 0.7f && rng.Value() < look.TreeYoungAtEdge)
                            art = trees[(int)(rng.Value() * Mathf.Max(1, trees.Count / 2)) % trees.Count];
                        if (!InView(q, art.Height, art.HalfWidth) || Covers(q, art.Height, art.HalfWidth, hex * look.TreeClearance) || Crowded(q, art.HalfWidth * 0.5f, 0.55f))
                            continue;
                        Add(q, art.Id, art.HalfWidth * 0.5f);
                        placed++;
                    }
                    // У края группы, ближе к полю — пень или бурьян.
                    Vector2 toward = (frameCenter - center).normalized;
                    for (int extra = 0; extra < 2; extra++)
                    {
                        List<MapLabDecorArt> pool = extra == 0 ? tall : stumps;
                        if (pool.Count == 0 || rng.Value() > (extra == 0 ? look.WeedChance : look.StumpChance) * Mathf.Min(1f, density))
                            continue;
                        MapLabDecorArt art = MapLabArtAssets.Pick(pool, rng.Value());
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
            if (low.Count > 0 && !zone.Uniform)
            {
                // Густота поля и окраины разная (ползунки «на поле» и «за полем»): пятна —
                // кандидаты с устойчивым жребием (свой поток на номер пятна, жребий по
                // положению), поэтому изменение густоты только добавляет или убирает траву
                // и не перемешивает всё поле; поле и окраина не влияют друг на друга.
                float patchBase = Mathf.Lerp(look.GrassPatchesMin, look.GrassPatchesMax, rng.Value()) * density;
                int candidates = Mathf.RoundToInt(patchBase * GrassZone.MaxMultiplier);
                float reach = Mathf.Max(0f, look.GrassOutside) * hex;
                for (int patch = 0; patch < candidates; patch++)
                {
                    MapLabRandom prng = MapLabRandom.ForStream(Spec.Seed, "arena-grass-patch-" + patch, MapLabData.AlgorithmVersion);
                    Vector2 center = new Vector2(prng.Range(frame.xMin - reach, frame.xMax + reach), prng.Range(frame.yMin - reach, frame.yMax + reach));
                    float centerDensity = zone.Density(center);
                    if (prng.Value() >= centerDensity / GrassZone.MaxMultiplier)
                        continue;
                    float spread = hex * prng.Range(look.GrassPatchSizeMin, look.GrassPatchSizeMax);
                    MapLabDecorArt first = MapLabArtAssets.Pick(low, prng.Value());
                    MapLabDecorArt other = MapLabArtAssets.Pick(low, prng.Value());
                    int count = look.GrassPerPatchMin + (int)(prng.Value() * Mathf.Max(1, look.GrassPerPatchMax - look.GrassPerPatchMin + 1));
                    for (int i = 0; i < count; i++)
                    {
                        float radius = prng.Value() * prng.Value();
                        float angle = prng.Value() * Mathf.PI * 2f;
                        Vector2 q = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.7f) * spread * radius;
                        MapLabDecorArt art = prng.Value() < 0.7f ? first : other;
                        if (tallGrass.Count > 0 && look.TallGrassShare > 0f && prng.Value() < look.TallGrassShare)
                            art = MapLabArtAssets.Pick(tallGrass, prng.Value());
                        float lot = prng.Value();
                        // Травинка по своей густоте относительно центра пятна.
                        if (lot >= Mathf.Clamp01(zone.Density(q) / Mathf.Max(0.05f, centerDensity)))
                            continue;
                        if ((look.GrassClearOfStart > 0f && Near(keep, q, hex * look.GrassClearOfStart)) || Near(blocked, q, hex * 0.75f) || Crowded(q, art.HalfWidth * 0.35f, look.GrassSpacing))
                            continue;
                        if (art.Role == MapLabDecorRole.TallGrass && Covers(q, art.Height, art.HalfWidth, hex * 0.9f))
                            continue;
                        Add(q, art.Id, art.HalfWidth * 0.35f);
                        if (art.Role != MapLabDecorRole.TallGrass)
                            groundArtIds.Add(art.Id);
                        grassPoints.Add(q);
                    }
                }
            }
            if (low.Count > 0 && zone.Uniform)
            {
                int patches = Mathf.RoundToInt(Mathf.Lerp(look.GrassPatchesMin, look.GrassPatchesMax, rng.Value()) * density * zone.Scale);
                float outside = Mathf.Max(0f, look.GrassOutside) * hex;
                for (int patch = 0; patch < patches; patch++)
                {
                    Vector2 center = new Vector2(rng.Range(frame.xMin - outside, frame.xMax + outside), rng.Range(frame.yMin - outside, frame.yMax + outside));
                    // На поле и за полем — своя густота (пятно реже там, где она меньше).
                    if (!zone.Accept(center))
                        continue;
                    float spread = hex * rng.Range(look.GrassPatchSizeMin, look.GrassPatchSizeMax);
                    MapLabDecorArt first = MapLabArtAssets.Pick(low, rng.Value());
                    MapLabDecorArt other = MapLabArtAssets.Pick(low, rng.Value());
                    int count = look.GrassPerPatchMin + (int)(rng.Value() * Mathf.Max(1, look.GrassPerPatchMax - look.GrassPerPatchMin + 1));
                    for (int i = 0; i < count; i++)
                    {
                        // Гуще к середине пятна, реже к краю (не ровный ковёр).
                        float radius = rng.Value() * rng.Value();
                        float angle = rng.Value() * Mathf.PI * 2f;
                        Vector2 q = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.7f) * spread * radius;
                        MapLabDecorArt art = rng.Value() < 0.7f ? first : other;
                        if (tallGrass.Count > 0 && look.TallGrassShare > 0f && rng.Value() < look.TallGrassShare)
                            art = MapLabArtAssets.Pick(tallGrass, rng.Value());
                        if (!zone.Accept(q))
                            continue;
                        if ((look.GrassClearOfStart > 0f && Near(keep, q, hex * look.GrassClearOfStart)) || Near(blocked, q, hex * 0.75f) || Crowded(q, art.HalfWidth * 0.35f, look.GrassSpacing))
                            continue;
                        if (art.Role == MapLabDecorRole.TallGrass && Covers(q, art.Height, art.HalfWidth, hex * 0.9f))
                            continue;
                        Add(q, art.Id, art.HalfWidth * 0.35f);
                        if (art.Role != MapLabDecorRole.TallGrass)
                            groundArtIds.Add(art.Id);
                        grassPoints.Add(q);
                    }
                }
            }

            // 4. Редкие мелкие камни на окраинах.
            if (small.Count > 0)
            {
                int rocks = Mathf.RoundToInt(rng.Range(look.SmallRocksMin, look.SmallRocksMax) * density);
                for (int i = 0, attempt = 0; i < rocks && attempt < rocks * 8; attempt++)
                {
                    MapLabDecorArt art = MapLabArtAssets.Pick(small, rng.Value());
                    Vector2 q = new Vector2(rng.Range(frame.xMin, frame.xMax), rng.Range(frame.yMin, frame.yMax));
                    if (Near(keep, q, hex) || Near(cells, q, hex * 1.3f) || Crowded(q, art.HalfWidth * 0.5f, 0.8f))
                        continue;
                    Add(q, art.Id, art.HalfWidth * 0.5f);
                    i++;
                }
            }

            // 5. Травяной ковёр и россыпь травинок («Генератор арен»): дрожащая
            // решётка по всему кадру, свой поток случайности — прочий декор от
            // них не зависит. Пол: под сеткой клеток.
            PlaceCarpet(c, frame, keep, blocked, low, zone);
        }

        private void PlaceCarpet(MapLabComposition c, Rect frame, List<Vector2> keep, List<Vector2> blocked, List<MapLabDecorArt> low, GrassZone zone)
        {
            MapLabArenaLook look = Look;
            float hex = Spec.HexWorld;
            float hexArea = hex * hex * 2.598f;
            List<MapLabArtEntry> tufts = new List<MapLabArtEntry>(), inks = new List<MapLabArtEntry>();
            if (look.GrassCarpet > 0f)
                CarpetArt(look.GrassCarpetScale, tufts, inks);
            MapLabRandom rng = MapLabRandom.ForStream(Spec.Seed, "arena-carpet", MapLabData.AlgorithmVersion);
            MapLabNoise noise = new MapLabNoise(MapLabRandom.StreamSeed(Spec.Seed, "arena-carpet-noise", MapLabData.AlgorithmVersion));
            float outside = Mathf.Max(0f, look.GrassOutside) * hex + hex * 0.5f;
            float lean = cos / Mathf.Max(0.05f, sin);
            Rect area = new Rect(frame.xMin - outside, frame.yMin - outside - hex * lean, frame.width + outside * 2f, frame.height + outside * 2f + hex * lean);

            bool Clear(Vector2 q)
            {
                foreach (Vector2 point in blocked)
                    if (Vector2.Distance(point, q) < hex * 0.6f)
                        return false;
                if (look.GrassClearOfStart > 0f)
                    foreach (Vector2 point in keep)
                        if (Vector2.Distance(point, q) < hex * look.GrassClearOfStart * 0.6f)
                            return false;
                return true;
            }

            // Плешины и густые места — по шуму; patchiness 0 — ровно.
            bool Keep(Vector2 q)
            {
                float n = noise.Fbm(q.x / (hex * 3.5f), q.y / (hex * 3.5f), 3) * 0.5f + 0.5f;
                float density = Mathf.Lerp(1f, Mathf.Clamp01((n - 0.25f) * 2f), Mathf.Clamp01(look.GrassCarpetPatchiness));
                return rng.Value() < density;
            }

            void Grid(float perHex, System.Action<Vector2, System.Func<float>> place)
            {
                if (perHex <= 0f)
                    return;
                if (!zone.Uniform)
                {
                    // Густота на поле и за полем — по плавной карте густоты: решётка
                    // с запасом (до максимума ползунка), точка остаётся по устойчивому
                    // жребию. Больше густоты — только добавляются точки, остальные на
                    // месте; поле и окраина независимы.
                    float fine = Mathf.Min(perHex * GrassZone.MaxMultiplier, 120f);
                    float fineStep = Mathf.Sqrt(hexArea / fine);
                    int iz = 0;
                    for (float z = area.yMin; z <= area.yMax; z += fineStep, iz++)
                    {
                        int ix = 0;
                        for (float x = area.xMin; x <= area.xMax; x += fineStep, ix++)
                        {
                            Vector2 q = new Vector2(x + (GrassZone.Lot(ix, iz, 1u) - 0.5f) * 0.9f * fineStep, z + (GrassZone.Lot(ix, iz, 2u) - 0.5f) * 0.9f * fineStep);
                            float want = Mathf.Min(1f, perHex * zone.Density(q) / fine);
                            if (GrassZone.Lot(ix, iz, 3u) >= want || !Clear(q))
                                continue;
                            float patch = Mathf.Lerp(1f, Mathf.Clamp01((noise.Fbm(q.x / (hex * 3.5f), q.y / (hex * 3.5f), 3) * 0.5f + 0.5f - 0.25f) * 2f), Mathf.Clamp01(look.GrassCarpetPatchiness));
                            if (GrassZone.Lot(ix, iz, 4u) >= patch)
                                continue;
                            int roll = 0;
                            place(q, () => GrassZone.Lot(ix, iz, 5u + (uint)roll++));
                        }
                    }
                    return;
                }
                float step = Mathf.Sqrt(hexArea / Mathf.Min(perHex * zone.Scale, 120f));
                for (float z = area.yMin; z <= area.yMax; z += step)
                for (float x = area.xMin; x <= area.xMax; x += step)
                {
                    Vector2 q = new Vector2(x + rng.Range(-0.45f, 0.45f) * step, z + rng.Range(-0.45f, 0.45f) * step);
                    if (Clear(q) && Keep(q) && zone.Accept(q))
                        place(q, rng.Value);
                }
            }

            if (tufts.Count + inks.Count > 0)
                Grid(look.GrassCarpet, (q, roll) =>
                {
                    List<MapLabArtEntry> pool = inks.Count > 0 && (tufts.Count == 0 || roll() < look.GrassCarpetInk) ? inks : tufts;
                    MapLabArtEntry art = MapLabArtAssets.PickEntry(pool, roll());
                    c.Features.Add((q, art.Id, DecorKind));
                    groundArtIds.Add(art.Id);
                    DecorFeatures++;
                    grassPoints.Add(q);
                });
            List<MapLabDecorArt> tallGrass = Role(MapLabDecorRole.TallGrass);
            // Высокая трава закрывала бы бойцов: мимо клеток поля и клеток за ней на экране.
            bool OverField(Vector2 p, MapLabDecorArt a)
            {
                foreach (Vector2 cell in decorCells)
                {
                    if (Vector2.Distance(p, cell) < hex * 0.9f)
                        return true;
                    if (Mathf.Abs(cell.x - p.x) < a.HalfWidth * 0.75f + hex * 0.8f && cell.y > p.y - hex * 0.3f && cell.y - p.y < a.Height * lean + hex * 0.9f)
                        return true;
                }
                return false;
            }
            if (low.Count > 0)
                Grid(look.GrassSingles, (q, roll) =>
                {
                    MapLabDecorArt art = MapLabArtAssets.Pick(low, roll());
                    if (tallGrass.Count > 0 && look.TallGrassShare > 0f && roll() < look.TallGrassShare)
                    {
                        MapLabDecorArt tallArt = MapLabArtAssets.Pick(tallGrass, roll());
                        if (OverField(q, tallArt))
                            return;
                        c.Features.Add((q, tallArt.Id, DecorKind));
                        DecorFeatures++;
                        grassPoints.Add(q);
                        return;
                    }
                    c.Features.Add((q, art.Id, DecorKind));
                    groundArtIds.Add(art.Id);
                    DecorFeatures++;
                    grassPoints.Add(q);
                });
        }

        // Пучки травы карты (набор этапа 05) — копии в наборе арены своего размера.
        private void CarpetArt(float scale, List<MapLabArtEntry> tufts, List<MapLabArtEntry> inks)
        {
            // Низкая трава пакета «Grass_*» (три вида) — вместо мелких пучков карты мира.
            // Размер записи уже с множителем арены, ковёр — свой масштаб; нет пакета —
            // как прежде.
            bool anyPack = false;
            foreach (MapLabDecorArt art in decorArt)
            {
                if (!art.Pack || art.Role != MapLabDecorRole.LowGrass || decorSet == null)
                    continue;
                anyPack = true;
                // Приоритет 0 — в ковёр не берётся (записи остаются в Базе, но не рисуются).
                if (art.Priority <= 0f)
                    continue;
                MapLabArtEntry packed = decorSet.Find(art.Id);
                if (packed == null || !packed.HasTexture)
                    continue;
                // «#» в ID — рисунок вида берётся у основной записи («art:…»).
                string packId = art.Id + "#carpet";
                MapLabArtEntry packCopy = decorSet.Find(packId);
                if (packCopy == null)
                {
                    packCopy = JsonUtility.FromJson<MapLabArtEntry>(JsonUtility.ToJson(packed));
                    packCopy.Id = packId;
                    packCopy.Texture = packed.Texture;
                    packCopy.WorldHeight = packed.WorldHeight * Mathf.Max(0.1f, scale);
                    packCopy.Weight = art.Priority;
                    decorSet.Entries.Add(packCopy);
                }
                (art.Ink ? inks : tufts).Add(packCopy);
            }
            if (anyPack || tufts.Count + inks.Count > 0)
                return;
            MapLabArtSet source = MapLabGameAssets.Load()?.ArtSet05;
            if (source == null || decorSet == null)
                return;
            foreach (MapLabArtEntry entry in source.Entries)
            {
                if (entry == null || !entry.HasTexture || (entry.Role != "grass_tuft" && entry.Role != "grass_ink_tuft"))
                    continue;
                string id = "carpet:" + entry.Id;
                MapLabArtEntry copy = decorSet.Find(id);
                if (copy == null)
                {
                    copy = JsonUtility.FromJson<MapLabArtEntry>(JsonUtility.ToJson(entry));
                    copy.Id = id;
                    copy.Texture = entry.Texture;
                    // Пучок карты рядом с бойцами мелок: × размер гекса арены / 3.
                    copy.WorldHeight = entry.WorldHeight * Mathf.Max(0.1f, scale) * Spec.HexWorld / 3f;
                    decorSet.Entries.Add(copy);
                }
                (entry.Role == "grass_ink_tuft" ? inks : tufts).Add(copy);
            }
        }

        // Приоритет 0 — рисунок в этой роли не берётся.
        private List<MapLabDecorArt> Role(MapLabDecorRole role) => decorArt.FindAll(art => art.Role == role && art.Priority > 0f);
    }
}

namespace KingdomSurvival.MapLabBridge
{
    public sealed partial class MapLabArena
    {
        // Густота травы на поле клеток и за ним («Генератор арен»): на поле —
        // GrassInField, дальше полутора-двух гексов от клеток — GrassOutsideField,
        // между ними плавный переход шириной GrassFieldEdge. Обе 1 — как прежде
        // (без лишних бросков случайности: прежние seed дают прежний декор).
        private sealed class GrassZone
        {
            private readonly MapLabArena arena;
            private readonly List<Vector2> cells;
            private readonly float inField, outside, edge, max;
            private readonly MapLabRandom rng;
            public readonly bool Uniform;
            // Наибольшее значение ползунков густоты (на поле и за полем).
            public const float MaxMultiplier = 4f;

            // Устойчивый жребий 0..1 по ячейке решётки: не зависит от порядка бросков.
            public static float Lot(int ix, int iz, uint salt)
            {
                unchecked
                {
                    uint h = (uint)ix * 73856093u ^ (uint)iz * 19349663u ^ salt * 83492791u;
                    h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15; h *= 0x27d4eb2du; h ^= h >> 16;
                    return (h & 0xFFFFFFu) / 16777216f;
                }
            }

            public GrassZone(MapLabArena arena, List<Vector2> cells)
            {
                this.arena = arena;
                this.cells = cells;
                MapLabArenaLook look = arena.Look;
                inField = Mathf.Max(0f, look.GrassInField);
                outside = Mathf.Max(0f, look.GrassOutsideField);
                edge = Mathf.Max(0.1f, look.GrassFieldEdge) * arena.Spec.HexWorld;
                max = Mathf.Max(inField, outside);
                Uniform = Mathf.Approximately(inField, 1f) && Mathf.Approximately(outside, 1f);
                rng = MapLabRandom.ForStream(arena.Spec.Seed, "arena-grass-zone", MapLabData.AlgorithmVersion);
            }

            // Во сколько раз больше кандидатов ставить (потом часть отсеется).
            public float Scale => Uniform ? 1f : max;

            public float Density(Vector2 p)
            {
                float nearest = float.MaxValue;
                foreach (Vector2 cell in cells)
                    nearest = Mathf.Min(nearest, Vector2.Distance(cell, p));
                float start = arena.Spec.HexWorld * 0.9f;
                float t = Mathf.Clamp01((nearest - start) / edge);
                t = t * t * (3f - 2f * t);
                return Mathf.Lerp(inField, outside, t);
            }

            public bool Accept(Vector2 p)
            {
                if (Uniform) return true;
                if (max <= 0f) return false;
                return rng.Value() < Density(p) / max;
            }
        }
    }
}
