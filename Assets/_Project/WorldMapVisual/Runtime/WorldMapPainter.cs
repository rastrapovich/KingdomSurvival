using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Канон v1.58 §9.9: рисунок сгенерированной карты. Строится на CPU из
    // GeneratedWorldMap и WorldMapArtStyle — одинаково в редакторе и в игре,
    // без шейдеров и без сцены. Порядок слоёв:
    //   подложка (узор на всю карту + пятна оттенка + мелкий узор) →
    //   местность (подкраска и узор типа клетки, неровные мягкие края) →
    //   вода (озёра по клеткам, реки линиями с шириной; отмель и тушь берега) →
    //   контур леса → дороги и тропы → мосты →
    //   знаки сверху вниз по экрану (деревья, болотный камыш, холмы, горы,
    //   домики деревень, приметные места) → обработка (яркость, контраст,
    //   насыщенность, тепло, бумага, зерно, виньетка).
    public static class WorldMapPainter
    {
        public static Texture2D Paint(GeneratedWorldMap map, WorldMapArtStyle style, float resolutionOverride = 0f)
        {
            if (map == null || !map.IsValid)
                return null;
            PaintJob job = new PaintJob(map, style ?? WorldMapArtStyle.CreateDefault(), resolutionOverride);
            job.Run();
            return job.ToTexture();
        }

        // Размер рисунка в пикселях для данного качества.
        public static Vector2Int TextureSize(GeneratedWorldMap map, float resolution)
        {
            float r = Mathf.Clamp(resolution, 0.05f, 1f);
            return new Vector2Int(
                Mathf.Max(16, Mathf.RoundToInt(map.CanvasWidth * r)),
                Mathf.Max(16, Mathf.RoundToInt(map.CanvasHeight * r)));
        }

        private sealed class PaintJob
        {
            private readonly GeneratedWorldMap map;
            private readonly WorldMapArtStyle style;
            private readonly WorldMapHexGrid grid;
            private readonly int width;
            private readonly int height;
            private readonly float scale;
            private readonly float hexPx;
            private readonly float inkPx;
            private readonly float[] r;
            private readonly float[] g;
            private readonly float[] b;
            private readonly byte[] area;
            private readonly byte[] cellArea;
            private readonly WorldMapNoise noise;
            private readonly System.Random random;
            private readonly Dictionary<UnityEngine.Object, Image> images = new Dictionary<UnityEngine.Object, Image>();
            private float[] waterCoverage;

            public PaintJob(GeneratedWorldMap map, WorldMapArtStyle style, float resolutionOverride)
            {
                this.map = map;
                this.style = style;
                style.EnsureTerrainEntries();
                grid = map.CreateGrid();
                Vector2Int size = TextureSize(map, resolutionOverride > 0f ? resolutionOverride : style.Resolution);
                width = size.x;
                height = size.y;
                scale = width / map.CanvasWidth;
                hexPx = (float)grid.HexWidth * scale;
                inkPx = Mathf.Max(0f, style.InkWidth * scale);
                int count = width * height;
                r = new float[count];
                g = new float[count];
                b = new float[count];
                area = new byte[count];
                noise = new WorldMapNoise(WorldMapGenerator.StreamSeed(map.Seed, "art"));
                random = new System.Random(WorldMapGenerator.StreamSeed(map.Seed, "art-stamps"));

                // Дороги, тропы и русла рек рисуются линиями; под ними —
                // открытая земля, а не шестиугольные полосы.
                WorldMapTerrainLayer layer = map.CreateTerrainLayer();
                cellArea = layer.CopyCells();
                HashSet<int> lakes = new HashSet<int>(map.LakeCells ?? new List<int>());
                for (int i = 0; i < cellArea.Length; i++)
                {
                    WorldMapGameplayTerrainType t = (WorldMapGameplayTerrainType)cellArea[i];
                    if (t == WorldMapGameplayTerrainType.Road || t == WorldMapGameplayTerrainType.Trail ||
                        (t == WorldMapGameplayTerrainType.Water && !lakes.Contains(i)))
                    {
                        cellArea[i] = (byte)WorldMapGameplayTerrainType.OpenGround;
                    }
                }
            }

            public void Run()
            {
                MapAreas();
                PaintBase();
                PaintTerrain();
                PaintWater();
                PaintForestOutline();
                PaintRoads();
                PaintBridges();
                PaintStamps();
                PostProcess();
            }

            public Texture2D ToTexture()
            {
                Color32[] pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                {
                    int source = y * width;
                    int target = (height - 1 - y) * width;
                    for (int x = 0; x < width; x++)
                    {
                        int p = source + x;
                        pixels[target + x] = new Color32(ToByte(r[p]), ToByte(g[p]), ToByte(b[p]), 255);
                    }
                }
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "GeneratedWorldMap",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                return texture;
            }

            // --------------------------------------------------------------
            // Местность каждого пикселя: клетка под точкой с плавным сдвигом
            // шумом — границы рваные, а не шестиугольные.
            // --------------------------------------------------------------

            private void MapAreas()
            {
                const int step = 4;
                int lw = width / step + 2;
                int lh = height / step + 2;
                float[] jx = new float[lw * lh];
                float[] jy = new float[lw * lh];
                double roughness = style.EdgeRoughness * grid.HexWidth;
                double frequency = 1.0 / (grid.HexWidth * 1.1);
                for (int ly = 0; ly < lh; ly++)
                {
                    for (int lx = 0; lx < lw; lx++)
                    {
                        double cx = lx * step / scale;
                        double cy = ly * step / scale;
                        jx[ly * lw + lx] = (float)(noise.Fbm(cx * frequency, cy * frequency, 3, 0.55) * roughness);
                        jy[ly * lw + lx] = (float)(noise.Fbm(cx * frequency + 41.3, cy * frequency - 17.9, 3, 0.55) * roughness);
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    int ly = y / step;
                    float fy = (y - ly * step) / (float)step;
                    for (int x = 0; x < width; x++)
                    {
                        int lx = x / step;
                        float fx = (x - lx * step) / (float)step;
                        int i00 = ly * lw + lx;
                        float ox = Bilinear(jx[i00], jx[i00 + 1], jx[i00 + lw], jx[i00 + lw + 1], fx, fy);
                        float oy = Bilinear(jy[i00], jy[i00 + 1], jy[i00 + lw], jy[i00 + lw + 1], fx, fy);
                        double cx = (x + 0.5) / scale + ox;
                        double cy = (y + 0.5) / scale + oy;
                        area[y * width + x] = cellArea[grid.IndexOf(grid.CellAtPixel(cx, cy))];
                    }
                }
            }

            // --------------------------------------------------------------
            // Подложка
            // --------------------------------------------------------------

            private void PaintBase()
            {
                Image pattern = LoadImage(style.BasePattern);
                Image detail = LoadImage(style.DetailPattern);
                float tilePx = Mathf.Max(4f, style.BasePatternTileHexes * hexPx);
                float detailPx = Mathf.Max(2f, style.DetailPatternTileHexes * hexPx);
                double variationFrequency = 1.0 / Mathf.Max(4f, style.TintVariationScaleHexes * hexPx);
                Color baseColor = style.BaseColor;
                Color tint = style.BasePatternTint;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int p = y * width + x;
                        float cr = baseColor.r, cg = baseColor.g, cb = baseColor.b;
                        if (pattern != null)
                        {
                            Color s = pattern.SampleWrapped(x / tilePx, y / tilePx);
                            float k = style.BasePatternStrength * s.a;
                            cr += (s.r * tint.r - cr) * k;
                            cg += (s.g * tint.g - cg) * k;
                            cb += (s.b * tint.b - cb) * k;
                        }
                        if (detail != null)
                            Modulate(ref cr, ref cg, ref cb, detail, x / detailPx, y / detailPx, style.DetailPatternStrength);
                        if (style.TintVariation > 0f)
                        {
                            float v = (float)noise.Fbm(x * variationFrequency, y * variationFrequency, 3, 0.5) * style.TintVariation;
                            cr *= 1f + v * 1.1f;
                            cg *= 1f + v;
                            cb *= 1f + v * 0.7f;
                        }
                        r[p] = cr;
                        g[p] = cg;
                        b[p] = cb;
                    }
                }
            }

            // --------------------------------------------------------------
            // Местность: подкраска (с мягкими краями) и узоры
            // --------------------------------------------------------------

            private void PaintTerrain()
            {
                int count = width * height;
                float[] ar = new float[count], ag = new float[count], ab = new float[count], aa = new float[count];
                WorldMapTerrainArt[] arts = new WorldMapTerrainArt[16];
                foreach (WorldMapGameplayTerrainType type in WorldMapTerrainLabels.All)
                    arts[(int)type] = style.FindTerrain(type);

                for (int p = 0; p < count; p++)
                {
                    WorldMapTerrainArt art = arts[area[p]];
                    if (art == null || !art.Enabled)
                        continue;
                    ar[p] = art.Color.r;
                    ag[p] = art.Color.g;
                    ab[p] = art.Color.b;
                    aa[p] = art.Opacity;
                }

                int soft = Mathf.RoundToInt(style.EdgeSoftness * hexPx * 0.5f);
                if (soft > 0)
                {
                    // Премультипликация, чтобы прозрачные слои не тянули цвет к чёрному.
                    for (int p = 0; p < count; p++)
                    {
                        ar[p] *= aa[p];
                        ag[p] *= aa[p];
                        ab[p] *= aa[p];
                    }
                    for (int pass = 0; pass < 2; pass++)
                    {
                        BoxBlur(ar, soft);
                        BoxBlur(ag, soft);
                        BoxBlur(ab, soft);
                        BoxBlur(aa, soft);
                    }
                    for (int p = 0; p < count; p++)
                    {
                        if (aa[p] > 0.0001f)
                        {
                            ar[p] /= aa[p];
                            ag[p] /= aa[p];
                            ab[p] /= aa[p];
                        }
                    }
                }

                Image[] patterns = new Image[16];
                float[] tiles = new float[16];
                foreach (WorldMapGameplayTerrainType type in WorldMapTerrainLabels.All)
                {
                    WorldMapTerrainArt art = arts[(int)type];
                    if (art != null && art.Enabled && art.Pattern != null && art.PatternStrength > 0f)
                    {
                        patterns[(int)type] = LoadImage(art.Pattern);
                        tiles[(int)type] = Mathf.Max(2f, art.PatternTileHexes * hexPx);
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int p = y * width + x;
                        float k = aa[p];
                        if (k > 0f)
                        {
                            r[p] += (ar[p] - r[p]) * k;
                            g[p] += (ag[p] - g[p]) * k;
                            b[p] += (ab[p] - b[p]) * k;
                        }
                        Image pattern = patterns[area[p]];
                        if (pattern != null)
                        {
                            float tile = tiles[area[p]];
                            Color s = pattern.SampleWrapped(x / tile, y / tile);
                            float strength = arts[area[p]].PatternStrength * s.a;
                            r[p] += (s.r - r[p]) * strength;
                            g[p] += (s.g - g[p]) * strength;
                            b[p] += (s.b - b[p]) * strength;
                        }
                    }
                }
            }

            // --------------------------------------------------------------
            // Вода
            // --------------------------------------------------------------

            private void PaintWater()
            {
                int count = width * height;
                waterCoverage = new float[count];
                byte water = (byte)WorldMapGameplayTerrainType.Water;
                for (int p = 0; p < count; p++)
                {
                    if (area[p] == water)
                        waterCoverage[p] = 1f;
                }

                if (map.Rivers != null)
                {
                    foreach (GeneratedMapLine river in map.Rivers)
                    {
                        if (river?.Points == null || river.Points.Count < 2)
                            continue;
                        List<Vector2> points = ToPixels(river.Points);
                        float total = Length(points);
                        float walked = 0f;
                        for (int i = 0; i < points.Count - 1; i++)
                        {
                            float segment = Vector2.Distance(points[i], points[i + 1]);
                            float w0 = Mathf.Lerp(river.StartWidthHexes, river.EndWidthHexes, walked / Mathf.Max(1f, total));
                            float w1 = Mathf.Lerp(river.StartWidthHexes, river.EndWidthHexes, (walked + segment) / Mathf.Max(1f, total));
                            RasterCoverage(waterCoverage, points[i], points[i + 1],
                                Mathf.Max(0.8f, w0 * hexPx * style.RiverWidthScale * 0.5f),
                                Mathf.Max(0.8f, w1 * hexPx * style.RiverWidthScale * 0.5f));
                            walked += segment;
                        }
                    }
                }

                bool[] isWater = new bool[count];
                bool[] isLand = new bool[count];
                bool any = false;
                for (int p = 0; p < count; p++)
                {
                    isWater[p] = waterCoverage[p] >= 0.5f;
                    isLand[p] = !isWater[p];
                    any |= isWater[p];
                }
                if (!any)
                    return;

                float[] toWater = DistanceTransform(isWater);
                float[] toLand = DistanceTransform(isLand);
                float shorePx = Mathf.Max(1f, style.ShoreBand * hexPx);
                float deepPx = Mathf.Max(2f, hexPx * 0.9f);
                Image ripple = LoadImage(style.WaterPattern);
                float rippleTile = Mathf.Max(2f, style.WaterPatternTileHexes * hexPx);
                Color shallow = style.WaterShallowColor;
                Color deep = style.WaterDeepColor;
                Color ink = style.InkColor;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int p = y * width + x;
                        float cover = waterCoverage[p];
                        if (cover > 0f)
                        {
                            float depth = Mathf.Clamp01(toLand[p] / deepPx);
                            float wr = Mathf.Lerp(shallow.r, deep.r, depth);
                            float wg = Mathf.Lerp(shallow.g, deep.g, depth);
                            float wb = Mathf.Lerp(shallow.b, deep.b, depth);
                            if (toLand[p] < shorePx)
                            {
                                float band = 1f - toLand[p] / shorePx;
                                wr += (shallow.r * 1.25f - wr) * band * 0.6f;
                                wg += (shallow.g * 1.25f - wg) * band * 0.6f;
                                wb += (shallow.b * 1.2f - wb) * band * 0.6f;
                            }
                            if (ripple != null)
                            {
                                Color s = ripple.SampleWrapped(x / rippleTile, y / rippleTile);
                                float k = style.WaterPatternStrength * s.a;
                                wr += (s.r - wr) * k;
                                wg += (s.g - wg) * k;
                                wb += (s.b - wb) * k;
                            }
                            r[p] += (wr - r[p]) * cover;
                            g[p] += (wg - g[p]) * cover;
                            b[p] += (wb - b[p]) * cover;
                        }

                        if (inkPx > 0f && !isWater[p] && toWater[p] <= inkPx + 0.5f)
                        {
                            float k = Mathf.Clamp01(inkPx + 0.5f - toWater[p]) * 0.92f;
                            Blend(p, ink, k);
                        }
                    }
                }
            }

            private void PaintForestOutline()
            {
                if (inkPx <= 0f || style.ForestOutline <= 0f)
                    return;
                int count = width * height;
                byte forest = (byte)WorldMapGameplayTerrainType.Forest;
                bool[] mask = new bool[count];
                bool any = false;
                for (int p = 0; p < count; p++)
                {
                    mask[p] = area[p] == forest;
                    any |= mask[p];
                }
                if (!any)
                    return;
                float[] distance = DistanceTransform(mask);
                float edge = inkPx * 0.8f;
                for (int p = 0; p < count; p++)
                {
                    if (!mask[p] && distance[p] <= edge + 0.5f && waterCoverage[p] < 0.5f)
                        Blend(p, style.InkColor, Mathf.Clamp01(edge + 0.5f - distance[p]) * style.ForestOutline);
                }
            }

            // --------------------------------------------------------------
            // Дороги, тропы, мосты
            // --------------------------------------------------------------

            private void PaintRoads()
            {
                if (map.Roads == null)
                    return;
                float roadHalf = Mathf.Max(0.8f, style.RoadWidthHexes * hexPx * 0.5f);
                float trailHalf = Mathf.Max(0.6f, style.TrailWidthHexes * hexPx * 0.5f);
                List<List<Vector2>> roads = new List<List<Vector2>>();
                List<List<Vector2>> trails = new List<List<Vector2>>();
                foreach (GeneratedMapLine line in map.Roads)
                {
                    if (line?.Points == null || line.Points.Count < 2)
                        continue;
                    (line.Kind == GeneratedMapLine.TrailKind ? trails : roads).Add(ToPixels(line.Points));
                }

                if (style.RoadInk > 0f && inkPx > 0f)
                {
                    foreach (List<Vector2> road in roads)
                        StrokePolyline(road, roadHalf + inkPx * 0.6f, style.InkColor, style.RoadInk, 0f, 0f);
                }
                float dash = roadHalf * 6f;
                foreach (List<Vector2> road in roads)
                    StrokePolyline(road, roadHalf, style.RoadColor, 1f, style.RoadDashed ? dash : 0f, dash * 0.6f);

                float trailDash = Mathf.Max(3f, trailHalf * 5f);
                foreach (List<Vector2> trail in trails)
                    StrokePolyline(trail, trailHalf, style.TrailColor, 0.95f, style.TrailDashed ? trailDash : 0f, trailDash * 0.8f);
            }

            private void PaintBridges()
            {
                if (map.Bridges == null || map.Bridges.Count == 0)
                    return;
                float roadHalf = Mathf.Max(0.8f, style.RoadWidthHexes * hexPx * 0.5f);
                float length = Mathf.Max(roadHalf * 4f, hexPx * 0.7f);
                foreach (MapPointData bridge in map.Bridges)
                {
                    Vector2 center = ToPixel(bridge);
                    Vector2 direction = RoadDirectionAt(center);
                    Vector2 normal = new Vector2(-direction.y, direction.x);
                    Vector2 a = center - direction * length * 0.5f;
                    Vector2 c = center + direction * length * 0.5f;
                    float half = roadHalf * 1.5f;
                    FillPolygon(new[] { a + normal * half, c + normal * half, c - normal * half, a - normal * half },
                        style.BridgeColor, style.InkColor, Mathf.Max(1f, inkPx * 0.6f), 0f);
                    // Перила — две тёмные черты.
                    StrokeSegment(a + normal * half, c + normal * half, Mathf.Max(0.6f, inkPx * 0.35f), style.InkColor, 1f);
                    StrokeSegment(a - normal * half, c - normal * half, Mathf.Max(0.6f, inkPx * 0.35f), style.InkColor, 1f);
                }
            }

            private Vector2 RoadDirectionAt(Vector2 point)
            {
                Vector2 best = Vector2.right;
                float bestDistance = float.MaxValue;
                foreach (GeneratedMapLine line in map.Roads)
                {
                    if (line?.Points == null || line.Points.Count < 2)
                        continue;
                    for (int i = 0; i < line.Points.Count - 1; i++)
                    {
                        Vector2 a = ToPixel(line.Points[i]);
                        Vector2 c = ToPixel(line.Points[i + 1]);
                        float distance = DistanceToSegment(point, a, c, out _);
                        if (distance < bestDistance && (c - a).sqrMagnitude > 0.0001f)
                        {
                            bestDistance = distance;
                            best = (c - a).normalized;
                        }
                    }
                }
                return best;
            }

            // --------------------------------------------------------------
            // Знаки: деревья, камыш, холмы, горы, домики, места
            // --------------------------------------------------------------

            private struct Stamp
            {
                public float Y;
                public int Order;
                public Action Draw;
            }

            private void PaintStamps()
            {
                List<Stamp> stamps = new List<Stamp>();
                WorldMapTerrainLayer layer = map.CreateTerrainLayer();
                byte[] cells = layer.CopyCells();
                for (int i = 0; i < cells.Length; i++)
                {
                    grid.CellCenter(grid.CellAt(i), out double cx, out double cy);
                    Vector2 center = new Vector2((float)cx * scale, (float)cy * scale);
                    if (center.x < -hexPx || center.y < -hexPx || center.x > width + hexPx || center.y > height + hexPx)
                        continue;
                    switch ((WorldMapGameplayTerrainType)cells[i])
                    {
                        case WorldMapGameplayTerrainType.Forest:
                            if (style.DrawTrees)
                                AddTrees(stamps, center, style.TreesPerHex, WorldMapGameplayTerrainType.Forest);
                            break;
                        case WorldMapGameplayTerrainType.Swamp:
                            AddReeds(stamps, center);
                            if (style.DrawTrees)
                                AddTrees(stamps, center, style.SparseTreesPerHex, WorldMapGameplayTerrainType.Swamp);
                            break;
                        case WorldMapGameplayTerrainType.OpenGround:
                            if (style.DrawTrees)
                                AddTrees(stamps, center, style.SparseTreesPerHex * 0.2f, WorldMapGameplayTerrainType.OpenGround);
                            break;
                        case WorldMapGameplayTerrainType.Hills:
                            if (style.DrawHills)
                                AddRelief(stamps, center, style.HillsPerHex, false, false);
                            break;
                        case WorldMapGameplayTerrainType.Mountains:
                            if (style.DrawMountains)
                                AddRelief(stamps, center, style.MountainsPerHex, true, false);
                            break;
                        case WorldMapGameplayTerrainType.Cliffs:
                            if (style.DrawMountains)
                                AddRelief(stamps, center, style.MountainsPerHex, true, true);
                            break;
                    }
                }

                if (map.Villages != null)
                {
                    foreach (GeneratedMapSite village in map.Villages)
                        AddVillage(stamps, village);
                }
                if (map.Landmarks != null)
                {
                    foreach (GeneratedMapSite landmark in map.Landmarks)
                    {
                        if (landmark != null && landmark.DrawMark)
                            AddLandmark(stamps, landmark);
                    }
                }

                stamps.Sort((x, y) => x.Y != y.Y ? x.Y.CompareTo(y.Y) : x.Order.CompareTo(y.Order));
                foreach (Stamp stamp in stamps)
                    stamp.Draw();
            }

            private bool AreaAt(Vector2 point, WorldMapGameplayTerrainType type)
            {
                int x = (int)point.x, y = (int)point.y;
                if (x < 0 || y < 0 || x >= width || y >= height)
                    return false;
                int p = y * width + x;
                return area[p] == (byte)type && waterCoverage[p] < 0.2f;
            }

            private int Count(float perHex)
            {
                int whole = (int)perHex;
                return whole + (random.NextDouble() < perHex - whole ? 1 : 0);
            }

            private Vector2 Scatter(Vector2 center, float radiusHexes) =>
                center + new Vector2((float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1)) * radiusHexes * hexPx;

            private void AddTrees(List<Stamp> stamps, Vector2 center, float perHex, WorldMapGameplayTerrainType type)
            {
                int n = Count(perHex);
                for (int t = 0; t < n; t++)
                {
                    Vector2 at = Scatter(center, 0.6f);
                    if (!AreaAt(at, type))
                        continue;
                    float size = style.TreeSizeHexes * hexPx * (1f + ((float)random.NextDouble() * 2f - 1f) * style.TreeSizeJitter);
                    if (type != WorldMapGameplayTerrainType.Forest)
                        size *= 0.8f;
                    Sprite sprite = Pick(style.TreeSprites);
                    bool conifer = random.NextDouble() < style.ConiferShare;
                    bool flip = random.NextDouble() < 0.5;
                    float shade = 0.9f + (float)random.NextDouble() * 0.2f;
                    stamps.Add(new Stamp
                    {
                        Y = at.y,
                        Draw = sprite != null
                            ? () => DrawSprite(sprite, at, size, Color.white, flip)
                            : conifer
                                ? () => DrawConifer(at, size, shade)
                                : (Action)(() => DrawLeafTree(at, size, shade))
                    });
                }
            }

            private void AddReeds(List<Stamp> stamps, Vector2 center)
            {
                int n = Count(1.4f);
                for (int t = 0; t < n; t++)
                {
                    Vector2 at = Scatter(center, 0.5f);
                    if (!AreaAt(at, WorldMapGameplayTerrainType.Swamp))
                        continue;
                    float size = hexPx * 0.28f * (0.8f + (float)random.NextDouble() * 0.4f);
                    stamps.Add(new Stamp { Y = at.y, Draw = () => DrawReeds(at, size) });
                }
            }

            private void AddRelief(List<Stamp> stamps, Vector2 center, float perHex, bool mountain, bool cliff)
            {
                int n = Count(perHex);
                WorldMapGameplayTerrainType type = cliff ? WorldMapGameplayTerrainType.Cliffs
                    : mountain ? WorldMapGameplayTerrainType.Mountains : WorldMapGameplayTerrainType.Hills;
                for (int t = 0; t < n; t++)
                {
                    Vector2 at = Scatter(center, 0.35f);
                    if (!AreaAt(at, type))
                        at = center;
                    float baseSize = mountain ? style.MountainSizeHexes : style.HillSizeHexes;
                    float size = baseSize * hexPx * (0.8f + (float)random.NextDouble() * 0.4f);
                    Sprite sprite = Pick(mountain ? style.MountainSprites : style.HillSprites);
                    bool flip = random.NextDouble() < 0.5;
                    float skew = ((float)random.NextDouble() * 2f - 1f) * 0.15f;
                    stamps.Add(new Stamp
                    {
                        Y = at.y,
                        Draw = sprite != null
                            ? () => DrawSprite(sprite, at, size, Color.white, flip)
                            : mountain
                                ? () => DrawMountain(at, size, skew, cliff)
                                : (Action)(() => DrawHill(at, size, skew))
                    });
                }
            }

            private void AddVillage(List<Stamp> stamps, GeneratedMapSite village)
            {
                if (village == null)
                    return;
                Vector2 center = ToPixel(village.XPercent, village.YPercent);
                System.Random local = new System.Random(village.Variant);
                float size = style.VillageSizeHexes * hexPx;
                Sprite sprite = Pick(style.VillageSprites, local);
                if (sprite != null)
                {
                    float spriteHeight = size * (0.8f + village.Size * 0.2f);
                    stamps.Add(new Stamp { Y = center.y, Order = 1, Draw = () => DrawSprite(sprite, center + new Vector2(0f, spriteHeight * 0.35f), spriteHeight, Color.white, false) });
                }
                else
                {
                    int houses = 1 + village.Size * 2;
                    float spread = size * 0.32f * Mathf.Sqrt(village.Size);
                    for (int h = 0; h < houses; h++)
                    {
                        float angle = (float)(h / (double)houses * Math.PI * 2.0 + local.NextDouble() * 0.6);
                        float radius = h == 0 ? 0f : spread * (0.6f + (float)local.NextDouble() * 0.5f);
                        Vector2 at = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.7f);
                        float houseSize = size * 0.42f * (0.85f + (float)local.NextDouble() * 0.3f);
                        bool flip = local.NextDouble() < 0.5;
                        stamps.Add(new Stamp { Y = at.y, Order = 1, Draw = () => DrawHouse(at, houseSize, flip) });
                    }
                }

                if (village.IsStart && style.MarkStart)
                {
                    float ring = size * 0.62f;
                    stamps.Add(new Stamp { Y = center.y + ring, Order = 2, Draw = () => DrawStartMark(center, ring) });
                }
            }

            private void AddLandmark(List<Stamp> stamps, GeneratedMapSite landmark)
            {
                Vector2 at = ToPixel(landmark.XPercent, landmark.YPercent);
                WorldMapLandmarkArt art = style.FindLandmark(landmark.Kind);
                float size = (art != null ? art.SizeHexes : style.LandmarkSizeHexes) * hexPx;
                if (art != null && art.Sprite != null)
                {
                    Sprite sprite = art.Sprite;
                    Color tint = art.Tint;
                    stamps.Add(new Stamp { Y = at.y, Order = 1, Draw = () => DrawSprite(sprite, at + new Vector2(0f, size * 0.3f), size, tint, false) });
                    return;
                }
                string kind = (landmark.Kind ?? string.Empty).ToLowerInvariant();
                Action draw;
                if (kind.Contains("ruin"))
                    draw = () => DrawRuins(at, size, landmark.Variant);
                else if (kind.Contains("mine"))
                    draw = () => DrawMine(at, size);
                else if (kind.Contains("barrow") || kind.Contains("kurgan"))
                    draw = () => DrawBarrow(at, size);
                else if (kind.Contains("mill"))
                    draw = () => DrawMill(at, size);
                else
                    draw = () => DrawStandingStone(at, size);
                stamps.Add(new Stamp { Y = at.y, Order = 1, Draw = draw });
            }

            private Sprite Pick(List<Sprite> sprites, System.Random source = null)
            {
                if (sprites == null || sprites.Count == 0)
                    return null;
                Sprite sprite = sprites[(source ?? random).Next(sprites.Count)];
                return sprite;
            }

            // ---- рисунки кодом (тушь + заливка) ----

            private void DrawConifer(Vector2 bottom, float size, float shade)
            {
                Color fill = Mul(style.ConiferColor, shade);
                float w = size * 0.5f;
                float ink = Mathf.Max(0.7f, size * 0.06f);
                StrokeSegment(bottom, bottom - new Vector2(0f, size * 0.2f), Mathf.Max(0.5f, size * 0.04f), style.InkColor, 1f);
                FillPolygon(new[] { bottom + new Vector2(-w * 0.5f, -size * 0.15f), bottom + new Vector2(w * 0.5f, -size * 0.15f), bottom + new Vector2(0f, -size * 0.75f) },
                    fill, style.InkColor, ink, 0.22f);
                FillPolygon(new[] { bottom + new Vector2(-w * 0.38f, -size * 0.45f), bottom + new Vector2(w * 0.38f, -size * 0.45f), bottom + new Vector2(0f, -size) },
                    Mul(fill, 1.08f), style.InkColor, ink, 0.22f);
            }

            private void DrawLeafTree(Vector2 bottom, float size, float shade)
            {
                Color fill = Mul(style.LeafColor, shade);
                StrokeSegment(bottom, bottom - new Vector2(0f, size * 0.4f), Mathf.Max(0.6f, size * 0.05f), style.InkColor, 1f);
                FillEllipse(bottom - new Vector2(0f, size * 0.62f), size * 0.32f, size * 0.3f, fill, style.InkColor, Mathf.Max(0.7f, size * 0.06f), 0.28f);
            }

            private void DrawReeds(Vector2 at, float size)
            {
                float stroke = Mathf.Max(0.5f, size * 0.08f);
                for (int i = -1; i <= 1; i++)
                {
                    Vector2 foot = at + new Vector2(i * size * 0.22f, 0f);
                    StrokeSegment(foot, foot + new Vector2(i * size * 0.08f, -size * (i == 0 ? 0.9f : 0.65f)), stroke, style.InkColor, 0.85f);
                }
                StrokeSegment(at + new Vector2(-size * 0.45f, size * 0.08f), at + new Vector2(size * 0.45f, size * 0.08f), stroke, style.WaterDeepColor, 0.8f);
            }

            private void DrawHill(Vector2 bottom, float size, float skew)
            {
                float w = size * 0.8f;
                float h = size * 0.38f;
                Vector2 center = bottom;
                float ink = Mathf.Max(0.7f, inkPx * 0.7f);
                int minX = Mathf.FloorToInt(center.x - w - ink), maxX = Mathf.CeilToInt(center.x + w + ink);
                int minY = Mathf.FloorToInt(center.y - h - ink), maxY = Mathf.CeilToInt(center.y);
                for (int y = Mathf.Max(0, minY); y <= Mathf.Min(height - 1, maxY); y++)
                {
                    for (int x = Mathf.Max(0, minX); x <= Mathf.Min(width - 1, maxX); x++)
                    {
                        float dx = (x + 0.5f - center.x) / w - skew * (center.y - y) / h;
                        float dy = (y + 0.5f - center.y) / h;
                        float e = Mathf.Sqrt(dx * dx + dy * dy);
                        float edge = ink / Mathf.Min(w, h);
                        int p = y * width + x;
                        if (e <= 1f - edge)
                        {
                            float light = dx < 0f ? 1.08f : 0.86f;
                            Blend(p, Mul(style.HillColor, light), 0.85f * Mathf.Clamp01((1f - e) * 6f));
                        }
                        else if (e <= 1f + edge * 0.3f)
                            Blend(p, style.InkColor, 0.9f);
                    }
                }
                // Штрихи тени справа.
                for (int i = 0; i < 3; i++)
                {
                    float t = 0.25f + i * 0.2f;
                    Vector2 a = center + new Vector2(w * t, -h * Mathf.Sqrt(1f - t * t) * 0.85f);
                    StrokeSegment(a, a + new Vector2(w * 0.12f, h * 0.35f), Mathf.Max(0.5f, ink * 0.45f), style.InkColor, 0.7f);
                }
            }

            private void DrawMountain(Vector2 bottom, float size, float skew, bool cliff)
            {
                float w = size * (cliff ? 0.42f : 0.55f);
                float h = size * (cliff ? 0.95f : 0.85f);
                Vector2 peak = bottom + new Vector2(skew * w, -h);
                Vector2 left = bottom + new Vector2(-w, 0f);
                Vector2 right = bottom + new Vector2(w, 0f);
                Vector2 middle = bottom + new Vector2(skew * w * 0.5f + w * 0.1f, 0f);
                float ink = Mathf.Max(0.8f, inkPx * 0.8f);
                Color light = cliff ? Mul(style.MountainLightColor, 0.8f) : style.MountainLightColor;
                Color shadow = cliff ? Mul(style.MountainShadowColor, 0.8f) : style.MountainShadowColor;
                FillPolygon(new[] { left, middle, peak }, light, style.InkColor, 0f, 0f);
                FillPolygon(new[] { middle, right, peak }, shadow, style.InkColor, 0f, 0f);
                StrokeSegment(left, peak, ink * 0.5f, style.InkColor, 1f);
                StrokeSegment(peak, right, ink * 0.5f, style.InkColor, 1f);
                StrokeSegment(peak, Vector2.Lerp(peak, middle, 0.55f), ink * 0.3f, style.InkColor, 0.8f);
                if (cliff)
                {
                    Vector2 notch = Vector2.Lerp(left, peak, 0.55f);
                    StrokeSegment(notch, notch + new Vector2(w * 0.35f, h * 0.12f), ink * 0.35f, style.InkColor, 0.9f);
                }
            }

            private void DrawHouse(Vector2 bottom, float size, bool flip)
            {
                float w = size * 0.5f;
                float wall = size * 0.42f;
                float ink = Mathf.Max(0.6f, size * 0.07f);
                Vector2 bl = bottom + new Vector2(-w * 0.5f, 0f);
                Vector2 br = bottom + new Vector2(w * 0.5f, 0f);
                Vector2 tl = bl - new Vector2(0f, wall);
                Vector2 tr = br - new Vector2(0f, wall);
                FillPolygon(new[] { bl, br, tr, tl }, style.WallColor, style.InkColor, ink, 0f);
                Vector2 ridge = bottom - new Vector2(flip ? -w * 0.1f : w * 0.1f, wall + size * 0.42f);
                FillPolygon(new[] { tl - new Vector2(w * 0.15f, 0f), tr + new Vector2(w * 0.15f, 0f), ridge }, style.RoofColor, style.InkColor, ink, 0.18f);
                Vector2 door = bottom + new Vector2(flip ? w * 0.18f : -w * 0.18f, 0f);
                FillPolygon(new[] { door + new Vector2(-w * 0.1f, 0f), door + new Vector2(w * 0.1f, 0f), door + new Vector2(w * 0.1f, -wall * 0.55f), door + new Vector2(-w * 0.1f, -wall * 0.55f) },
                    style.InkColor, style.InkColor, 0f, 0f);
            }

            // Стоянка отряда: частокол кольцом.
            private void DrawStartMark(Vector2 center, float radius)
            {
                int posts = 18;
                float post = Mathf.Max(1f, radius * 0.12f);
                for (int i = 0; i < posts; i++)
                {
                    float angle = i / (float)posts * Mathf.PI * 2f;
                    Vector2 foot = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.65f);
                    StrokeSegment(foot, foot - new Vector2(0f, post * 1.6f), Mathf.Max(0.6f, post * 0.3f), style.InkColor, 0.95f);
                }
            }

            private void DrawRuins(Vector2 bottom, float size, int variant)
            {
                System.Random local = new System.Random(variant);
                float ink = Mathf.Max(0.6f, size * 0.05f);
                for (int i = 0; i < 3; i++)
                {
                    float x = (i - 1) * size * 0.28f;
                    float h = size * (0.25f + (float)local.NextDouble() * 0.45f);
                    float w = size * 0.18f;
                    Vector2 foot = bottom + new Vector2(x, 0f);
                    FillPolygon(new[] { foot + new Vector2(-w * 0.5f, 0f), foot + new Vector2(w * 0.5f, 0f), foot + new Vector2(w * 0.5f, -h * 0.8f), foot + new Vector2(0f, -h), foot + new Vector2(-w * 0.5f, -h * 0.9f) },
                        style.LandmarkColor, style.InkColor, ink, 0.2f);
                }
                StrokeSegment(bottom + new Vector2(-size * 0.5f, 0f), bottom + new Vector2(size * 0.5f, 0f), ink * 0.6f, style.InkColor, 0.8f);
            }

            private void DrawMine(Vector2 bottom, float size)
            {
                DrawHill(bottom, size * 1.1f, 0f);
                float ink = Mathf.Max(0.6f, size * 0.06f);
                FillEllipse(bottom - new Vector2(0f, size * 0.02f), size * 0.16f, size * 0.2f, style.InkColor, style.InkColor, 0f, 0f, true);
                StrokeSegment(bottom + new Vector2(-size * 0.18f, 0f), bottom + new Vector2(-size * 0.18f, -size * 0.22f), ink * 0.6f, style.BridgeColor, 1f);
                StrokeSegment(bottom + new Vector2(size * 0.18f, 0f), bottom + new Vector2(size * 0.18f, -size * 0.22f), ink * 0.6f, style.BridgeColor, 1f);
                StrokeSegment(bottom + new Vector2(-size * 0.22f, -size * 0.22f), bottom + new Vector2(size * 0.22f, -size * 0.22f), ink * 0.6f, style.BridgeColor, 1f);
            }

            private void DrawBarrow(Vector2 bottom, float size)
            {
                FillEllipse(bottom, size * 0.5f, size * 0.22f, Mul(style.HillColor, 0.9f), style.InkColor, Mathf.Max(0.7f, size * 0.05f), 0.25f, true);
                DrawStandingStone(bottom - new Vector2(0f, size * 0.18f), size * 0.45f);
            }

            private void DrawMill(Vector2 bottom, float size)
            {
                DrawHouse(bottom, size * 0.8f, false);
                Vector2 wheel = bottom + new Vector2(size * 0.32f, -size * 0.16f);
                float radius = size * 0.2f;
                FillEllipse(wheel, radius, radius, style.BridgeColor, style.InkColor, Mathf.Max(0.6f, size * 0.05f), 0f);
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * Mathf.PI / 4f;
                    Vector2 d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    StrokeSegment(wheel - d, wheel + d, Mathf.Max(0.4f, size * 0.025f), style.InkColor, 1f);
                }
            }

            private void DrawStandingStone(Vector2 bottom, float size)
            {
                float w = size * 0.16f;
                FillPolygon(new[] { bottom + new Vector2(-w, 0f), bottom + new Vector2(w, 0f), bottom + new Vector2(w * 0.7f, -size * 0.6f), bottom + new Vector2(-w * 0.4f, -size * 0.68f) },
                    style.LandmarkColor, style.InkColor, Mathf.Max(0.6f, size * 0.06f), 0.25f);
            }

            // --------------------------------------------------------------
            // Обработка
            // --------------------------------------------------------------

            private void PostProcess()
            {
                Image paper = LoadImage(style.PaperTexture);
                float paperTile = Mathf.Max(4f, style.PaperTileHexes * hexPx);
                float warmth = style.Warmth * 0.06f;
                float cx = width * 0.5f, cy = height * 0.5f;
                float maxD = Mathf.Sqrt(cx * cx + cy * cy);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int p = y * width + x;
                        float cr = r[p] + style.Brightness, cg = g[p] + style.Brightness, cb = b[p] + style.Brightness;
                        cr = (cr - 0.5f) * style.Contrast + 0.5f;
                        cg = (cg - 0.5f) * style.Contrast + 0.5f;
                        cb = (cb - 0.5f) * style.Contrast + 0.5f;
                        float gray = cr * 0.3f + cg * 0.59f + cb * 0.11f;
                        cr = gray + (cr - gray) * style.Saturation;
                        cg = gray + (cg - gray) * style.Saturation;
                        cb = gray + (cb - gray) * style.Saturation;
                        cr += warmth;
                        cb -= warmth;
                        if (paper != null)
                            Modulate(ref cr, ref cg, ref cb, paper, x / paperTile, y / paperTile, style.PaperStrength);
                        if (style.Grain > 0f)
                        {
                            float n = (Hash(x, y, map.Seed) - 0.5f) * style.Grain;
                            cr += n;
                            cg += n;
                            cb += n;
                        }
                        if (style.Vignette > 0f)
                        {
                            float dx = x - cx, dy = y - cy;
                            float d = Mathf.Sqrt(dx * dx + dy * dy) / maxD;
                            float v = 1f - style.Vignette * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.05f, d));
                            cr *= v;
                            cg *= v;
                            cb *= v;
                        }
                        r[p] = cr;
                        g[p] = cg;
                        b[p] = cb;
                    }
                }
            }

            // --------------------------------------------------------------
            // Примитивы
            // --------------------------------------------------------------

            private void Blend(int p, Color color, float amount)
            {
                if (amount <= 0f)
                    return;
                if (amount > 1f)
                    amount = 1f;
                r[p] += (color.r - r[p]) * amount;
                g[p] += (color.g - g[p]) * amount;
                b[p] += (color.b - b[p]) * amount;
            }

            // Капсула: заливка с мягким краем и, при штрихах, пропусками по длине.
            private void StrokePolyline(List<Vector2> points, float half, Color color, float alpha, float dash, float gap)
            {
                float walked = 0f;
                for (int i = 0; i < points.Count - 1; i++)
                {
                    float length = Vector2.Distance(points[i], points[i + 1]);
                    StrokeSegment(points[i], points[i + 1], half, color, alpha, dash, gap, walked);
                    walked += length;
                }
            }

            private void StrokeSegment(Vector2 a, Vector2 c, float half, Color color, float alpha, float dash = 0f, float gap = 0f, float startLength = 0f)
            {
                int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, c.x) - half - 1));
                int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, c.x) + half + 1));
                int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, c.y) - half - 1));
                int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, c.y) + half + 1));
                float length = Vector2.Distance(a, c);
                float period = dash + gap;
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                        float distance = DistanceToSegment(point, a, c, out float t);
                        float cover = Mathf.Clamp01(half - distance + 0.5f);
                        if (cover <= 0f)
                            continue;
                        if (dash > 0f && period > 0f && ((startLength + t * length) % period) > dash)
                            continue;
                        Blend(y * width + x, color, cover * alpha);
                    }
                }
            }

            private void RasterCoverage(float[] coverage, Vector2 a, Vector2 c, float halfA, float halfC)
            {
                float half = Mathf.Max(halfA, halfC);
                int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, c.x) - half - 1));
                int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, c.x) + half + 1));
                int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, c.y) - half - 1));
                int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, c.y) + half + 1));
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float distance = DistanceToSegment(new Vector2(x + 0.5f, y + 0.5f), a, c, out float t);
                        float cover = Mathf.Clamp01(Mathf.Lerp(halfA, halfC, t) - distance + 0.5f);
                        int p = y * width + x;
                        if (cover > coverage[p])
                            coverage[p] = cover;
                    }
                }
            }

            // Выпуклый многоугольник: заливка с подсветкой слева-сверху
            // (shade) и тушью по внутреннему краю.
            private void FillPolygon(Vector2[] points, Color fill, Color ink, float inkWidth, float shade)
            {
                float minXf = float.MaxValue, maxXf = float.MinValue, minYf = float.MaxValue, maxYf = float.MinValue;
                foreach (Vector2 point in points)
                {
                    minXf = Mathf.Min(minXf, point.x);
                    maxXf = Mathf.Max(maxXf, point.x);
                    minYf = Mathf.Min(minYf, point.y);
                    maxYf = Mathf.Max(maxYf, point.y);
                }
                int minX = Mathf.Max(0, Mathf.FloorToInt(minXf)), maxX = Mathf.Min(width - 1, Mathf.CeilToInt(maxXf));
                int minY = Mathf.Max(0, Mathf.FloorToInt(minYf)), maxY = Mathf.Min(height - 1, Mathf.CeilToInt(maxYf));
                if (minX > maxX || minY > maxY)
                    return;
                float area2 = 0f;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 p0 = points[i], p1 = points[(i + 1) % points.Length];
                    area2 += p0.x * p1.y - p1.x * p0.y;
                }
                float sign = area2 >= 0f ? 1f : -1f;
                float spanX = Mathf.Max(1f, maxXf - minXf), spanY = Mathf.Max(1f, maxYf - minYf);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                        float inside = float.MaxValue;
                        for (int i = 0; i < points.Length; i++)
                        {
                            Vector2 p0 = points[i], p1 = points[(i + 1) % points.Length];
                            Vector2 edge = p1 - p0;
                            float len = edge.magnitude;
                            if (len < 0.0001f)
                                continue;
                            float d = sign * (edge.x * (point.y - p0.y) - edge.y * (point.x - p0.x)) / len;
                            inside = Mathf.Min(inside, d);
                        }
                        if (inside < -0.5f)
                            continue;
                        float cover = Mathf.Clamp01(inside + 0.5f);
                        int p = y * width + x;
                        if (inkWidth > 0f && inside < inkWidth)
                            Blend(p, ink, cover);
                        else
                        {
                            float light = 1f + shade * (0.5f - (point.x - minXf) / spanX) + shade * 0.5f * (0.5f - (point.y - minYf) / spanY);
                            Blend(p, Mul(fill, light), cover);
                        }
                    }
                }
            }

            private void FillEllipse(Vector2 center, float rx, float ry, Color fill, Color ink, float inkWidth, float shade, bool upperHalf = false)
            {
                int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - rx - 1)), maxX = Mathf.Min(width - 1, Mathf.CeilToInt(center.x + rx + 1));
                int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - ry - 1)), maxY = Mathf.Min(height - 1, Mathf.CeilToInt(upperHalf ? center.y : center.y + ry + 1));
                float radius = Mathf.Max(0.5f, Mathf.Min(rx, ry));
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dx = (x + 0.5f - center.x) / Mathf.Max(0.5f, rx);
                        float dy = (y + 0.5f - center.y) / Mathf.Max(0.5f, ry);
                        float e = Mathf.Sqrt(dx * dx + dy * dy);
                        float inside = (1f - e) * radius;
                        if (inside < -0.5f)
                            continue;
                        float cover = Mathf.Clamp01(inside + 0.5f);
                        int p = y * width + x;
                        if (inkWidth > 0f && inside < inkWidth)
                            Blend(p, ink, cover);
                        else
                            Blend(p, Mul(fill, 1f + shade * (-dx * 0.5f - dy * 0.5f)), cover);
                    }
                }
            }

            private void DrawSprite(Sprite sprite, Vector2 bottom, float targetHeight, Color tint, bool flip)
            {
                Image image = LoadImage(sprite);
                if (image == null || targetHeight < 1f)
                    return;
                float targetWidth = targetHeight * image.Width / Mathf.Max(1f, image.Height);
                int minX = Mathf.Max(0, Mathf.FloorToInt(bottom.x - targetWidth * 0.5f));
                int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(bottom.x + targetWidth * 0.5f));
                int minY = Mathf.Max(0, Mathf.FloorToInt(bottom.y - targetHeight));
                int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(bottom.y));
                for (int y = minY; y <= maxY; y++)
                {
                    float v = (y + 0.5f - (bottom.y - targetHeight)) / targetHeight;
                    for (int x = minX; x <= maxX; x++)
                    {
                        float u = (x + 0.5f - (bottom.x - targetWidth * 0.5f)) / targetWidth;
                        if (flip)
                            u = 1f - u;
                        Color s = image.SampleClamped(u, v);
                        float a = s.a * tint.a;
                        if (a <= 0.003f)
                            continue;
                        Blend(y * width + x, new Color(s.r * tint.r, s.g * tint.g, s.b * tint.b), a);
                    }
                }
            }

            // Евклидово расстояние (фаска 1 / √2) до ближайшего пикселя маски.
            private float[] DistanceTransform(bool[] mask)
            {
                int count = width * height;
                float[] d = new float[count];
                const float big = 1e9f;
                const float diagonal = 1.41421356f;
                for (int p = 0; p < count; p++)
                    d[p] = mask[p] ? 0f : big;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int p = y * width + x;
                        float v = d[p];
                        if (v == 0f)
                            continue;
                        if (x > 0) v = Mathf.Min(v, d[p - 1] + 1f);
                        if (y > 0)
                        {
                            v = Mathf.Min(v, d[p - width] + 1f);
                            if (x > 0) v = Mathf.Min(v, d[p - width - 1] + diagonal);
                            if (x < width - 1) v = Mathf.Min(v, d[p - width + 1] + diagonal);
                        }
                        d[p] = v;
                    }
                }
                for (int y = height - 1; y >= 0; y--)
                {
                    for (int x = width - 1; x >= 0; x--)
                    {
                        int p = y * width + x;
                        float v = d[p];
                        if (v == 0f)
                            continue;
                        if (x < width - 1) v = Mathf.Min(v, d[p + 1] + 1f);
                        if (y < height - 1)
                        {
                            v = Mathf.Min(v, d[p + width] + 1f);
                            if (x < width - 1) v = Mathf.Min(v, d[p + width + 1] + diagonal);
                            if (x > 0) v = Mathf.Min(v, d[p + width - 1] + diagonal);
                        }
                        d[p] = v;
                    }
                }
                return d;
            }

            // Скользящее среднее по строкам и столбцам.
            private void BoxBlur(float[] values, int radius)
            {
                float[] temp = new float[values.Length];
                float inv = 1f / (radius * 2 + 1);
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    float sum = 0f;
                    for (int x = -radius; x <= radius; x++)
                        sum += values[row + Mathf.Clamp(x, 0, width - 1)];
                    for (int x = 0; x < width; x++)
                    {
                        temp[row + x] = sum * inv;
                        sum += values[row + Mathf.Min(width - 1, x + radius + 1)] - values[row + Mathf.Max(0, x - radius)];
                    }
                }
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;
                    for (int y = -radius; y <= radius; y++)
                        sum += temp[Mathf.Clamp(y, 0, height - 1) * width + x];
                    for (int y = 0; y < height; y++)
                    {
                        values[y * width + x] = sum * inv;
                        sum += temp[Mathf.Min(height - 1, y + radius + 1) * width + x] - temp[Mathf.Max(0, y - radius) * width + x];
                    }
                }
            }

            private Vector2 ToPixel(MapPointData point) => ToPixel(point.XPercent, point.YPercent);

            private Vector2 ToPixel(float xPercent, float yPercent) =>
                new Vector2(xPercent / 100f * width, yPercent / 100f * height);

            private List<Vector2> ToPixels(List<MapPointData> points)
            {
                List<Vector2> pixels = new List<Vector2>(points.Count);
                foreach (MapPointData point in points)
                    pixels.Add(ToPixel(point));
                return pixels;
            }

            private Image LoadImage(UnityEngine.Object source)
            {
                if (source == null)
                    return null;
                if (images.TryGetValue(source, out Image cached))
                    return cached;
                Image image = source is Sprite sprite ? Image.FromSprite(sprite) : source is Texture2D texture ? Image.FromTexture(texture) : null;
                images[source] = image;
                return image;
            }
        }

        // ------------------------------------------------------------------
        // Пиксели рисунка: строки сверху вниз, выборка с повтором или краем.
        // ------------------------------------------------------------------

        private sealed class Image
        {
            public int Width;
            public int Height;
            public Color32[] Pixels;
            public Color Average;

            public static Image FromTexture(Texture2D texture) =>
                FromRegion(texture, new RectInt(0, 0, texture.width, texture.height));

            public static Image FromSprite(Sprite sprite)
            {
                if (sprite.texture == null)
                    return null;
                Rect rect = sprite.textureRect;
                return FromRegion(sprite.texture, new RectInt(
                    Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y),
                    Mathf.Max(1, Mathf.RoundToInt(rect.width)), Mathf.Max(1, Mathf.RoundToInt(rect.height))));
            }

            private static Image FromRegion(Texture2D texture, RectInt region)
            {
                Color32[] all = ReadPixels(texture);
                if (all == null)
                    return null;
                int tw = texture.width;
                Image image = new Image { Width = region.width, Height = region.height, Pixels = new Color32[region.width * region.height] };
                double sr = 0, sg = 0, sb = 0;
                for (int y = 0; y < region.height; y++)
                {
                    int sourceRow = (region.y + region.height - 1 - y) * tw;
                    for (int x = 0; x < region.width; x++)
                    {
                        Color32 c = all[sourceRow + region.x + x];
                        image.Pixels[y * region.width + x] = c;
                        sr += c.r;
                        sg += c.g;
                        sb += c.b;
                    }
                }
                double n = Math.Max(1, image.Pixels.Length) * 255.0;
                image.Average = new Color((float)(sr / n), (float)(sg / n), (float)(sb / n), 1f);
                return image;
            }

            // Нечитаемую текстуру копируем через видеокарту — художнику не
            // нужно включать Read/Write у каждого рисунка.
            private static Color32[] ReadPixels(Texture2D texture)
            {
                if (texture.isReadable)
                {
                    try { return texture.GetPixels32(); }
                    catch (Exception) { /* формат без чтения — ниже копия через GPU */ }
                }
                RenderTexture previous = RenderTexture.active;
                RenderTexture temporary = null;
                Texture2D copy = null;
                try
                {
                    temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(texture, temporary);
                    RenderTexture.active = temporary;
                    copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                    copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                    copy.Apply(false, false);
                    return copy.GetPixels32();
                }
                catch (Exception)
                {
                    return null;
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (temporary != null)
                        RenderTexture.ReleaseTemporary(temporary);
                    if (copy != null)
                    {
                        if (Application.isPlaying)
                            UnityEngine.Object.Destroy(copy);
                        else
                            UnityEngine.Object.DestroyImmediate(copy);
                    }
                }
            }

            public Color SampleWrapped(float u, float v)
            {
                float x = (u - Mathf.Floor(u)) * Width - 0.5f;
                float y = (v - Mathf.Floor(v)) * Height - 0.5f;
                return Sample(x, y, true);
            }

            public Color SampleClamped(float u, float v) =>
                Sample(Mathf.Clamp01(u) * Width - 0.5f, Mathf.Clamp01(v) * Height - 0.5f, false);

            private Color Sample(float x, float y, bool wrap)
            {
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                Color32 c00 = Get(x0, y0, wrap), c10 = Get(x0 + 1, y0, wrap), c01 = Get(x0, y0 + 1, wrap), c11 = Get(x0 + 1, y0 + 1, wrap);
                const float k = 1f / 255f;
                return new Color(
                    Bilinear(c00.r, c10.r, c01.r, c11.r, fx, fy) * k,
                    Bilinear(c00.g, c10.g, c01.g, c11.g, fx, fy) * k,
                    Bilinear(c00.b, c10.b, c01.b, c11.b, fx, fy) * k,
                    Bilinear(c00.a, c10.a, c01.a, c11.a, fx, fy) * k);
            }

            private Color32 Get(int x, int y, bool wrap)
            {
                if (wrap)
                {
                    x = ((x % Width) + Width) % Width;
                    y = ((y % Height) + Height) % Height;
                }
                else
                {
                    x = Mathf.Clamp(x, 0, Width - 1);
                    y = Mathf.Clamp(y, 0, Height - 1);
                }
                return Pixels[y * Width + x];
            }
        }

        // ------------------------------------------------------------------
        // Мелочи
        // ------------------------------------------------------------------

        // Умножение на узор, нормированный по его среднему цвету: узор
        // добавляет фактуру, не меняя общий тон.
        private static void Modulate(ref float cr, ref float cg, ref float cb, Image image, float u, float v, float strength)
        {
            if (strength <= 0f)
                return;
            Color s = image.SampleWrapped(u, v);
            Color avg = image.Average;
            float k = strength * s.a;
            cr *= 1f + (s.r / Mathf.Max(0.05f, avg.r) - 1f) * k;
            cg *= 1f + (s.g / Mathf.Max(0.05f, avg.g) - 1f) * k;
            cb *= 1f + (s.b / Mathf.Max(0.05f, avg.b) - 1f) * k;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 c, out float t)
        {
            Vector2 ac = c - a;
            float lengthSq = ac.sqrMagnitude;
            t = lengthSq > 0.0001f ? Mathf.Clamp01(Vector2.Dot(point - a, ac) / lengthSq) : 0f;
            return Vector2.Distance(point, a + ac * t);
        }

        private static float Length(List<Vector2> points)
        {
            float total = 0f;
            for (int i = 0; i < points.Count - 1; i++)
                total += Vector2.Distance(points[i], points[i + 1]);
            return total;
        }

        private static float Bilinear(float a, float b, float c, float d, float fx, float fy) =>
            (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;

        private static Color Mul(Color color, float k) => new Color(color.r * k, color.g * k, color.b * k, color.a);

        private static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }

    // Рисунок сгенерированной карты в игре: строится один раз на партию и
    // переиспользуется, пока не сменится карта или облик.
    public static class WorldMapGeneratedArt
    {
        private static GeneratedWorldMap cachedMap;
        private static Texture2D cachedTexture;

        public static Texture2D Get(GeneratedWorldMap map, WorldMapArtStyle style)
        {
            if (map == null || !map.IsValid)
                return null;
            if (ReferenceEquals(map, cachedMap) && cachedTexture != null)
                return cachedTexture;
            Clear();
            cachedMap = map;
            cachedTexture = WorldMapPainter.Paint(map, style);
            return cachedTexture;
        }

        public static void Clear()
        {
            if (cachedTexture != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(cachedTexture);
                else
                    UnityEngine.Object.DestroyImmediate(cachedTexture);
            }
            cachedTexture = null;
            cachedMap = null;
        }
    }
}
