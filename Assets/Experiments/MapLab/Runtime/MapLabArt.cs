using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Временные процедурные ассеты, чтобы сцена работала без назначенных текстур:
    // атлас крон, бесшовные фактуры мазка, бумаги и воды, текстура палитры.
    // Всё создаётся в памяти (HideFlags.DontSave) и не пишется в проект.
    public static class MapLabArt
    {
        public const int AtlasColumns = 8;
        public const int AtlasRows = 4;
        public const int Cell = 256;

        // Ячейки атласа.
        public const int DeciduousFirst = 0;   // 12 вариантов: 0..11
        public const int DeciduousCount = 12;
        public const int ConiferFirst = 12;    // 4 варианта
        public const int ConiferCount = 4;
        public const int GroupFirst = 16;      // 4 варианта
        public const int GroupCount = 4;
        public const int BushFirst = 20;       // 6 вариантов
        public const int BushCount = 6;
        public const int ReedFirst = 26;       // 4 варианта
        public const int ReedCount = 4;

        // Направление света в плоскости рисунка: сверху слева, к зрителю.
        private static readonly Vector3 SpriteLight = new Vector3(-0.55f, 0.62f, 0.56f).normalized;

        public static Texture2D CreatePaletteTexture(MapLabPalette palette)
        {
            Texture2D texture = new Texture2D(MapLabPaletteIndex.Count, 1, TextureFormat.RGBA32, false, false)
            {
                name = "MapLab Palette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            WritePalette(texture, palette);
            return texture;
        }

        public static void WritePalette(Texture2D texture, MapLabPalette palette)
        {
            Color32[] pixels = new Color32[MapLabPaletteIndex.Count];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = palette.Get(i);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        // Бесшовная фактура (линейная): R — направленный мазок, G — зерно,
        // B — вертикальные штрихи камня, A — волокна бумаги.
        public static Texture2D CreateDetailTexture(int seed)
        {
            const int size = 512;
            uint s = (uint)seed * 747796405u + 2891336453u;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;
                    float stroke = MapLabTileNoise.Fbm(u * 8f, v * 32f, 8, 3, s);
                    float grain = MapLabTileNoise.Fbm(u * 64f, v * 64f, 64, 2, s + 11u);
                    float streak = MapLabTileNoise.Fbm(u * 48f, v * 6f, 48, 3, s + 23u);
                    float fiber = MapLabTileNoise.Fbm(u * 24f, v * 96f, 24, 2, s + 37u) * 0.5f + MapLabTileNoise.Fbm(u * 96f, v * 24f, 96, 2, s + 41u) * 0.5f;
                    pixels[y * size + x] = new Color32(ToByte(stroke), ToByte(grain), ToByte(streak), ToByte(fiber));
                }
            }
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "MapLab Detail",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            return texture;
        }

        // Фактура воды: R — редкие короткие светлые штрихи, G — медленная рябь.
        public static Texture2D CreateWaterTexture(int seed)
        {
            const int size = 512;
            uint s = (uint)seed * 2654435761u + 97u;
            float[] dashes = new float[size * size];
            MapLabRandom rng = new MapLabRandom(s);
            // Короткие изогнутые штрихи с утолщением в середине (как мазок кистью).
            for (int k = 0; k < 420; k++)
            {
                int cx = rng.Range(0, size);
                int cy = rng.Range(0, size);
                float length = rng.Range(14f, 44f);
                float bend = rng.Range(-0.012f, 0.012f);
                float thickness = rng.Range(2.2f, 4.5f);
                float strength = rng.Range(0.45f, 1f);
                for (int i = -(int)length; i <= (int)length; i++)
                {
                    float t = i / length;
                    float fade = 1f - t * t;
                    float half = thickness * (0.35f + 0.65f * fade);
                    float yc = cy + bend * i * i;
                    for (int dy = -5; dy <= 5; dy++)
                    {
                        float cover = Mathf.Clamp01(half - Mathf.Abs(dy + (yc - Mathf.Round(yc))));
                        if (cover <= 0f)
                            continue;
                        int x = ((cx + i) % size + size) % size;
                        int y = ((Mathf.RoundToInt(yc) + dy) % size + size) % size;
                        dashes[y * size + x] = Mathf.Max(dashes[y * size + x], cover * fade * strength);
                    }
                }
            }
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float ripple = MapLabTileNoise.Fbm(u * 6f, v * 18f, 6, 3, s + 5u);
                pixels[y * size + x] = new Color32(ToByte(dashes[y * size + x]), ToByte(ripple), 0, 255);
            }
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "MapLab Water",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            return texture;
        }

        public static Rect CellRect(int cell)
        {
            int column = cell % AtlasColumns;
            int row = cell / AtlasColumns;
            return new Rect(column / (float)AtlasColumns, row / (float)AtlasRows, 1f / AtlasColumns, 1f / AtlasRows);
        }

        // Атлас крон: R — тон (свет/тень), G — линия, B — вариация тона лопасти, A — силуэт.
        // Свет запечён сверху слева; поворачивать и зеркалить карточки нельзя.
        public static Texture2D CreateVegetationAtlas(int seed)
        {
            int width = AtlasColumns * Cell, height = AtlasRows * Cell;
            float[] value = new float[width * height];
            float[] ink = new float[width * height];
            float[] tone = new float[width * height];
            float[] alpha = new float[width * height];
            Canvas canvas = new Canvas(width, value, ink, tone, alpha);
            MapLabRandom rng = new MapLabRandom(MapLabRandom.StreamSeed(seed, "atlas", MapLabData.AlgorithmVersion));
            for (int i = 0; i < DeciduousCount; i++)
                PaintDeciduous(canvas, Origin(DeciduousFirst + i), ref rng, 1f);
            for (int i = 0; i < ConiferCount; i++)
                PaintConifer(canvas, Origin(ConiferFirst + i), ref rng);
            for (int i = 0; i < GroupCount; i++)
                PaintGroup(canvas, Origin(GroupFirst + i), ref rng);
            for (int i = 0; i < BushCount; i++)
                PaintBush(canvas, Origin(BushFirst + i), ref rng);
            for (int i = 0; i < ReedCount; i++)
                PaintReeds(canvas, Origin(ReedFirst + i), ref rng);
            canvas.Outline();

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
            {
                name = "MapLab Vegetation Atlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2,
                hideFlags = HideFlags.DontSave,
            };
            Color32[] level = new Color32[width * height];
            for (int i = 0; i < level.Length; i++)
                level[i] = new Color32(ToByte(value[i]), ToByte(ink[i]), ToByte(tone[i]), ToByte(alpha[i]));
            texture.SetPixels32(level, 0);
            // Свои mip-уровни: альфа не «тает» с расстоянием, кроны не худеют.
            int w = width, h = height;
            for (int mip = 1; mip < texture.mipmapCount; mip++)
            {
                int nw = Mathf.Max(1, w / 2), nh = Mathf.Max(1, h / 2);
                Color32[] next = new Color32[nw * nh];
                for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    float sr = 0, sg = 0, sb = 0, sa = 0, wsum = 0;
                    float amax = 0f;
                    for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int sx = Mathf.Min(w - 1, x * 2 + dx), sy = Mathf.Min(h - 1, y * 2 + dy);
                        Color32 c = level[sy * w + sx];
                        float a = c.a / 255f;
                        sr += c.r * a; sg += c.g * a; sb += c.b * a; sa += a; wsum += a;
                        amax = Mathf.Max(amax, a);
                    }
                    float inv = wsum > 1e-4f ? 1f / wsum : 0f;
                    float outA = Mathf.Lerp(sa * 0.25f, amax, 0.45f);
                    next[y * nw + x] = new Color32((byte)(sr * inv), (byte)(sg * inv), (byte)(sb * inv), ToByte(outA));
                }
                texture.SetPixels32(next, mip);
                level = next;
                w = nw;
                h = nh;
            }
            texture.Apply(false, false);
            return texture;
        }

        private static Vector2Int Origin(int cell)
        {
            return new Vector2Int(cell % AtlasColumns * Cell, cell / AtlasColumns * Cell);
        }

        private sealed class Canvas
        {
            public readonly int Width;
            public readonly float[] Value, Ink, Tone, Alpha;
            public readonly int[] Owner;
            private int nextOwner = 1;

            public Canvas(int width, float[] value, float[] ink, float[] tone, float[] alpha)
            {
                Width = width;
                Value = value;
                Ink = ink;
                Tone = tone;
                Alpha = alpha;
                Owner = new int[value.Length];
            }

            public int NewOwner()
            {
                return nextOwner++;
            }

            // Лопасть кроны: шар с собственным светом, листовые комки, тень
            // от общего объёма кроны. Перекрывает уже нарисованное (передний план).
            public void Lobe(Vector2Int origin, Vector2 center, float radius, Vector2 crownCenter, float crownRadius, float lobeTone, uint seed)
            {
                int owner = NewOwner();
                int x0 = Mathf.FloorToInt(center.x - radius - 2), x1 = Mathf.CeilToInt(center.x + radius + 2);
                int y0 = Mathf.FloorToInt(center.y - radius - 2), y1 = Mathf.CeilToInt(center.y + radius + 2);
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (x < 0 || y < 0 || x >= Cell || y >= Cell)
                            continue;
                        float dx = x - center.x, dy = y - center.y;
                        // Край лопасти — неровный (листовые комки по окружности).
                        float angle = Mathf.Atan2(dy, dx);
                        float wobble = 1f + 0.09f * Mathf.Sin(angle * 7f + seed % 13) + 0.05f * Mathf.Sin(angle * 13f + seed % 7);
                        float rr = radius * wobble;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d > rr + 0.8f)
                            continue;
                        float cover = Mathf.Clamp01(rr + 0.8f - d);
                        int gi = (origin.y + y) * Width + origin.x + x;
                        float q = Mathf.Clamp01(d / rr);
                        Vector3 n = new Vector3(dx / rr, dy / rr, Mathf.Sqrt(Mathf.Max(0f, 1f - q * q))).normalized;
                        float own = Vector3.Dot(n, SpriteLight);
                        Vector2 cr = new Vector2(x, y) - crownCenter;
                        float cq = Mathf.Clamp01(cr.magnitude / crownRadius);
                        Vector3 cn = new Vector3(cr.x / crownRadius, cr.y / crownRadius, Mathf.Sqrt(Mathf.Max(0f, 1f - cq * cq))).normalized;
                        float body = Vector3.Dot(cn, SpriteLight);
                        float clumps = MapLabTileNoise.Fbm((origin.x + x) / 7f, (origin.y + y) / 7f, 4096, 2, seed) - 0.5f;
                        float v = 0.12f + 0.55f * MapLabGeometry.SmoothStep(-0.35f, 0.95f, own) + 0.38f * MapLabGeometry.SmoothStep(-0.6f, 0.9f, body);
                        v += clumps * 0.28f;
                        v = Mathf.Clamp01(v);
                        float edgeInk = MapLabGeometry.SmoothStep(rr - 3.2f, rr - 0.6f, d) * MapLabGeometry.SmoothStep(0.25f, -0.35f, own);
                        float a = Alpha[gi];
                        float blend = cover;
                        Value[gi] = Mathf.Lerp(Value[gi], v, blend);
                        Tone[gi] = Mathf.Lerp(Tone[gi], lobeTone, blend);
                        // Линия только там, где лопасть лежит поверх другой лопасти или у силуэта на теневой стороне.
                        float overlap = a > 0.5f && Owner[gi] != owner ? 1f : 0.4f;
                        Ink[gi] = Mathf.Lerp(Ink[gi], edgeInk * overlap * 0.85f, blend);
                        Alpha[gi] = Mathf.Max(a, cover);
                        if (cover > 0.5f)
                            Owner[gi] = owner;
                    }
                }
            }

            public void Pixel(Vector2Int origin, int x, int y, float v, float ink, float tone, float cover)
            {
                if (x < 0 || y < 0 || x >= Cell || y >= Cell)
                    return;
                int gi = (origin.y + y) * Width + origin.x + x;
                Value[gi] = Mathf.Lerp(Value[gi], v, cover);
                Ink[gi] = Mathf.Lerp(Ink[gi], ink, cover);
                Tone[gi] = Mathf.Lerp(Tone[gi], tone, cover);
                Alpha[gi] = Mathf.Max(Alpha[gi], cover);
            }

            // Тонкий прерывистый контур силуэта: сильнее на теневой (нижней правой) стороне.
            public void Outline()
            {
                int height = Value.Length / Width;
                float[] result = (float[])Ink.Clone();
                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < Width - 1; x++)
                    {
                        int i = y * Width + x;
                        if (Alpha[i] < 0.5f)
                            continue;
                        float right = Alpha[i + 1] < 0.5f ? 1f : 0f;
                        float down = Alpha[i - Width] < 0.5f ? 1f : 0f;
                        float left = Alpha[i - 1] < 0.5f ? 1f : 0f;
                        float up = Alpha[i + Width] < 0.5f ? 1f : 0f;
                        float edge = Mathf.Max(right * 0.85f, down * 0.9f);
                        edge = Mathf.Max(edge, Mathf.Max(left, up) * 0.25f);
                        if (edge <= 0f)
                            continue;
                        float gap = MapLabTileNoise.Value(x / 9f, y / 9f, 4096, 1234u);
                        edge *= MapLabGeometry.SmoothStep(0.25f, 0.45f, gap);
                        result[i] = Mathf.Max(result[i], edge);
                    }
                }
                System.Array.Copy(result, Ink, Ink.Length);
            }
        }

        private static void PaintDeciduous(Canvas canvas, Vector2Int origin, ref MapLabRandom rng, float scale)
        {
            // Короткий ствол — у основания карточки.
            float trunkTop = 70f * scale;
            for (int y = 2; y < trunkTop; y++)
            for (int x = -5; x <= 5; x++)
            {
                float cover = Mathf.Clamp01(5.5f - Mathf.Abs(x) - y * 0.02f);
                canvas.Pixel(origin, 128 + x, y, x < 0 ? 0.32f : 0.16f, 0.25f, 0.5f, cover);
            }
            Vector2 crown = new Vector2(128f + rng.Signed() * 6f, 146f * scale + rng.Signed() * 5f);
            float rx = rng.Range(92f, 108f) * scale;
            float ry = rng.Range(84f, 98f) * scale;
            float crownRadius = Mathf.Max(rx, ry);
            int lobes = rng.Range(11, 16);
            Vector3[] list = new Vector3[lobes + 8];
            int count = 0;
            // Венец лопастей по силуэту и заполнение середины.
            for (int i = 0; i < 8; i++)
            {
                float a = (i + rng.Signed() * 0.3f) / 8f * Mathf.PI * 2f;
                float rad = rng.Range(30f, 42f) * scale;
                list[count++] = new Vector3(crown.x + Mathf.Cos(a) * (rx - rad * 0.92f), crown.y + Mathf.Sin(a) * (ry - rad * 0.92f), rad);
            }
            for (int i = 0; i < lobes; i++)
            {
                Vector2 o = rng.InsideUnitCircle();
                float rad = rng.Range(26f, 40f) * scale;
                list[count++] = new Vector3(crown.x + o.x * (rx - rad) * 0.85f, crown.y + o.y * (ry - rad) * 0.85f, rad);
            }
            // Сначала дальние (верхние) лопасти, затем ближние (нижние).
            System.Array.Sort(list, 0, count, new LobeOrder());
            uint seed = rng.NextUInt();
            for (int i = 0; i < count; i++)
                canvas.Lobe(origin, new Vector2(list[i].x, list[i].y), list[i].z, crown, crownRadius, rng.Value(), seed + (uint)i * 31u);
        }

        private sealed class LobeOrder : System.Collections.Generic.IComparer<Vector3>
        {
            public int Compare(Vector3 a, Vector3 b)
            {
                return b.y.CompareTo(a.y);
            }
        }

        private static void PaintGroup(Canvas canvas, Vector2Int origin, ref MapLabRandom rng)
        {
            // Две дальние кроны и одна ближняя на одной карточке.
            Vector3[] crowns =
            {
                new Vector3(80f + rng.Signed() * 6f, 150f, rng.Range(52f, 60f)),
                new Vector3(176f + rng.Signed() * 6f, 156f, rng.Range(50f, 58f)),
                new Vector3(128f + rng.Signed() * 8f, 108f, rng.Range(56f, 64f)),
            };
            uint seed = rng.NextUInt();
            for (int c = 0; c < crowns.Length; c++)
            {
                Vector2 center = new Vector2(crowns[c].x, crowns[c].y);
                float radius = crowns[c].z;
                int lobes = rng.Range(7, 10);
                Vector3[] list = new Vector3[lobes + 6];
                int count = 0;
                for (int i = 0; i < 6; i++)
                {
                    float a = (i + rng.Signed() * 0.3f) / 6f * Mathf.PI * 2f;
                    float rad = rng.Range(19f, 25f);
                    list[count++] = new Vector3(center.x + Mathf.Cos(a) * (radius - rad * 0.9f), center.y + Mathf.Sin(a) * (radius - rad * 0.9f), rad);
                }
                for (int i = 0; i < lobes; i++)
                {
                    Vector2 o = rng.InsideUnitCircle();
                    float rad = rng.Range(16f, 24f);
                    list[count++] = new Vector3(center.x + o.x * (radius - rad) * 0.8f, center.y + o.y * (radius - rad) * 0.8f, rad);
                }
                System.Array.Sort(list, 0, count, new LobeOrder());
                float lobeTone = rng.Value();
                for (int i = 0; i < count; i++)
                    canvas.Lobe(origin, new Vector2(list[i].x, list[i].y), list[i].z, center, radius, Mathf.Clamp01(lobeTone + rng.Signed() * 0.2f), seed + (uint)(c * 100 + i) * 17u);
            }
        }

        private static void PaintBush(Canvas canvas, Vector2Int origin, ref MapLabRandom rng)
        {
            Vector2 center = new Vector2(128f, rng.Range(70f, 82f));
            float radius = rng.Range(62f, 74f);
            int lobes = rng.Range(7, 11);
            Vector3[] list = new Vector3[lobes + 6];
            int count = 0;
            for (int i = 0; i < 6; i++)
            {
                float a = (i + rng.Signed() * 0.3f) / 6f * Mathf.PI * 2f;
                float rad = rng.Range(24f, 32f);
                list[count++] = new Vector3(center.x + Mathf.Cos(a) * (radius - rad * 0.9f) * 1.15f, center.y + Mathf.Sin(a) * (radius - rad * 0.9f) * 0.8f, rad);
            }
            for (int i = 0; i < lobes; i++)
            {
                Vector2 o = rng.InsideUnitCircle();
                float rad = rng.Range(20f, 30f);
                list[count++] = new Vector3(center.x + o.x * (radius - rad) * 0.9f, center.y + o.y * (radius - rad) * 0.6f, rad);
            }
            System.Array.Sort(list, 0, count, new LobeOrder());
            uint seed = rng.NextUInt();
            for (int i = 0; i < count; i++)
            {
                Vector2 p = new Vector2(list[i].x, Mathf.Max(list[i].y, list[i].z + 2f));
                canvas.Lobe(origin, p, list[i].z, center, radius, rng.Value(), seed + (uint)i * 13u);
            }
        }

        private static void PaintConifer(Canvas canvas, Vector2Int origin, ref MapLabRandom rng)
        {
            float cx = 128f + rng.Signed() * 4f;
            float baseY = rng.Range(26f, 34f);
            float top = rng.Range(238f, 250f);
            float halfWidth = rng.Range(56f, 66f);
            int tiers = rng.Range(5, 8);
            uint seed = rng.NextUInt();
            for (int y = 2; y < baseY + 8; y++)
            for (int x = -4; x <= 4; x++)
                canvas.Pixel(origin, Mathf.RoundToInt(cx) + x, y, x < 0 ? 0.3f : 0.15f, 0.3f, 0.5f, Mathf.Clamp01(4.5f - Mathf.Abs(x)));
            // Ярусы — от нижнего широкого к верхнему узкому, каждый перекрывает нижний.
            for (int t = 0; t < tiers; t++)
            {
                float f0 = t / (float)tiers;
                float tierBottom = Mathf.Lerp(baseY, top - 40f, f0) - 6f;
                float tierTop = Mathf.Lerp(baseY + 70f, top, (t + 1f) / tiers);
                float tierHalf = halfWidth * (1f - f0 * 0.78f) * rng.Range(0.92f, 1.06f);
                float tierTone = rng.Value();
                for (int y = Mathf.FloorToInt(tierBottom); y <= Mathf.CeilToInt(tierTop); y++)
                {
                    float k = Mathf.InverseLerp(tierTop, tierBottom, y);
                    float jag = (Mathf.Sin(y * 0.9f + seed % 17) * 0.5f + 0.5f) * 5f;
                    float half = tierHalf * Mathf.Pow(Mathf.Clamp01(k), 0.85f) - jag * k;
                    for (int x = -(int)tierHalf - 2; x <= (int)tierHalf + 2; x++)
                    {
                        float ax = Mathf.Abs(x);
                        float cover = Mathf.Clamp01(half + 0.8f - ax);
                        // Нижний край яруса — зубчатый.
                        float lowEdge = tierBottom + (1f - ax / Mathf.Max(1f, tierHalf)) * 10f + Mathf.Sin(x * 0.7f + t) * 2.5f;
                        if (y < lowEdge)
                            cover *= Mathf.Clamp01(1f - (lowEdge - y) * 0.5f);
                        if (cover <= 0f)
                            continue;
                        float side = x / Mathf.Max(1f, tierHalf);
                        float v = Mathf.Lerp(0.95f, 0.25f, side * 0.5f + 0.5f) * Mathf.Lerp(0.8f, 1f, 1f - k * 0.5f);
                        v += (MapLabTileNoise.Value((origin.x + cx + x) / 4f, (origin.y + y) / 4f, 4096, seed) - 0.5f) * 0.22f;
                        float edgeInk = (y < lowEdge + 2.5f && y >= lowEdge - 1f ? 0.75f : 0f) * (side > -0.4f ? 1f : 0.3f);
                        canvas.Pixel(origin, Mathf.RoundToInt(cx) + x, y, Mathf.Clamp01(v), edgeInk, tierTone, cover);
                    }
                }
            }
        }

        private static void PaintReeds(Canvas canvas, Vector2Int origin, ref MapLabRandom rng)
        {
            int blades = rng.Range(12, 19);
            for (int i = 0; i < blades; i++)
            {
                float x0 = 128f + rng.Signed() * 70f;
                float height = rng.Range(120f, 220f);
                float lean = rng.Signed() * 0.35f;
                float tone = rng.Value();
                for (int y = 0; y < height; y++)
                {
                    float t = y / height;
                    float x = x0 + lean * y * t;
                    float w = Mathf.Lerp(2.6f, 0.8f, t);
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        float cover = Mathf.Clamp01(w - Mathf.Abs(dx + (x - Mathf.Round(x))));
                        float v = Mathf.Lerp(0.35f, 0.95f, t) * (dx < 0 ? 1f : 0.75f);
                        canvas.Pixel(origin, Mathf.RoundToInt(x) + dx, y, v, 0f, tone, cover);
                    }
                }
                // Метёлка.
                if (rng.Chance(0.5f))
                {
                    float x = x0 + lean * height;
                    for (int y = (int)height - 22; y < height; y++)
                    for (int dx = -3; dx <= 3; dx++)
                        canvas.Pixel(origin, Mathf.RoundToInt(x) + dx, y, 0.3f, 0.2f, 1f, Mathf.Clamp01(3.2f - Mathf.Abs(dx)));
                }
            }
        }

        private static byte ToByte(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
        }
    }
}
