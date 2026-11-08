using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Генерация данных карты по шагам брифа:
    // пресет → основа высот → площадки и понижения водоёмов → уступы → русла →
    // дороги и мосты → окончательная поверхность → нормали и маски → поля →
    // модули скал → растительность и постройки (MapLabPlacer) → свет и тени.
    // Unity-объектов не создаёт; результат — MapLabData.
    public static class MapLabGenerator
    {
        // Возвращает true, если пользователь отменил генерацию.
        public delegate bool Progress(string stage, float fraction);

        public sealed class CancelledException : Exception
        {
            public CancelledException() : base("Генерация MapLab отменена.") { }
        }

        public static MapLabData Generate(MapLabSettings settings, Progress progress = null)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            Stopwatch total = Stopwatch.StartNew();
            float frame = settings.FrameWidth;
            MapLabData data = new MapLabData(settings.Resolution, settings.WorldSide)
            {
                Seed = settings.General.Seed,
                Preset = settings.General.Preset,
                PlayRect = new Rect(frame, frame, settings.General.MapSize, settings.General.MapSize),
            };
            data.Composition = MapLabComposition.Build(settings, data.Seed);
            if (data.Composition.PlayRect.width > 0f)
                data.PlayRect = data.Composition.PlayRect;
            Builder builder = new Builder(settings, data, progress);
            builder.Run();
            Stopwatch objects = Stopwatch.StartNew();
            Report(progress, "Растительность и постройки", 0.8f);
            MapLabPlacer.Place(data, settings);
            Report(progress, "Свет и тени", 0.92f);
            MapLabShade.Compute(data, settings);
            data.Stats.ObjectsMs = (float)objects.Elapsed.TotalMilliseconds;
            data.Stats.GenerateMs = (float)total.Elapsed.TotalMilliseconds;
            return data;
        }

        // Пересобрать только объекты (лес, кусты, постройки): дороги, поля и рельеф прежние.
        public static void RebuildObjects(MapLabData data, MapLabSettings settings)
        {
            MapLabPlacer.Place(data, settings);
            MapLabShade.Compute(data, settings);
        }

        internal static void Report(Progress progress, string stage, float fraction)
        {
            if (progress != null && progress(stage, fraction))
                throw new CancelledException();
        }

        private sealed class Builder
        {
            private readonly MapLabSettings settings;
            private readonly MapLabData data;
            private readonly MapLabComposition composition;
            private readonly Progress progress;
            private readonly int n;
            private readonly float range;
            private readonly float[] cliffSigned;
            private readonly float[] roadDistance;

            public Builder(MapLabSettings settings, MapLabData data, Progress progress)
            {
                this.settings = settings;
                this.data = data;
                this.progress = progress;
                composition = data.Composition;
                n = data.Resolution;
                range = Mathf.Max(0.5f, settings.Relief.HeightRange);
                cliffSigned = new float[n * n];
                roadDistance = new float[n * n];
                data.LakeField = new float[n * n];
                for (int i = 0; i < n * n; i++)
                {
                    cliffSigned[i] = float.MaxValue;
                    roadDistance[i] = float.MaxValue;
                    data.LakeField[i] = -1f;
                }
            }

            public void Run()
            {
                Stopwatch terrain = Stopwatch.StartNew();
                Report(progress, "Основа высот", 0.02f);
                BuildBaseRelief();
                ApplyExternalHeightmap();
                ApplyValley();
                Smooth(data.Heights, settings.Relief.Smoothing);
                ApplyFlats();
                Report(progress, "Скальные уступы", 0.15f);
                ApplyCliffs();
                Report(progress, "Озёра", 0.25f);
                ApplyLakes();
                Report(progress, "Русла", 0.32f);
                ApplyRivers();
                Report(progress, "Дороги и мосты", 0.42f);
                BuildRoads();
                ClampHeights();
                Report(progress, "Нормали и маски", 0.55f);
                ComputeNormals(data);
                ComputeMasks();
                Report(progress, "Поля", 0.65f);
                BuildFields();
                Report(progress, "Модули скал", 0.72f);
                BuildCliffColumns();
                data.Stats.TerrainMs = (float)terrain.Elapsed.TotalMilliseconds;
            }

            private Vector2 World(int x, int z)
            {
                return new Vector2(x * data.Cell, z * data.Cell);
            }

            private int Seed => data.Seed;

            private MapLabNoise Noise(string stream)
            {
                return new MapLabNoise(MapLabRandom.StreamSeed(Seed, stream, MapLabData.AlgorithmVersion));
            }

            // Широкие холмы и долины, немного средних форм, ограниченный мелкий шум.
            private void BuildBaseRelief()
            {
                MapLabNoise broad = Noise("relief-broad");
                MapLabNoise medium = Noise("relief-medium");
                MapLabNoise fine = Noise("relief-fine");
                MapLabReliefSettings relief = settings.Relief;
                float hillWidth = Mathf.Max(10f, relief.HillWidth);
                bool procedural = relief.Source != MapLabHeightSource.ExternalHeightmap;
                for (int z = 0; z < n; z++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        if (!procedural)
                        {
                            data.Heights[data.Index(x, z)] = 0f;
                            continue;
                        }
                        Vector2 p = World(x, z);
                        float hills = 0f;
                        for (int i = 0; i < composition.Hills.Count; i++)
                        {
                            MapLabComposition.Hill hill = composition.Hills[i];
                            Vector2 d = p - hill.Center;
                            float q = (d.x * d.x) / (hill.Radius.x * hill.Radius.x) + (d.y * d.y) / (hill.Radius.y * hill.Radius.y);
                            hills += hill.Height * Mathf.Exp(-q * 1.7f);
                        }
                        float b = broad.Fbm(p.x / hillWidth, p.y / hillWidth, 3) * 0.5f + 0.5f;
                        float m = medium.Fbm(p.x / (hillWidth * 0.3f), p.y / (hillWidth * 0.3f), 3);
                        float f = fine.Fbm(p.x / 7f, p.y / 7f, 2);
                        float h = relief.HillHeight * (0.68f * Mathf.Min(hills, 1.3f) + 0.32f * b);
                        h += relief.MediumForms * 0.16f * m + relief.FineNoise * 0.06f * f;
                        data.Heights[data.Index(x, z)] = Mathf.Max(0f, h) * range;
                    }
                }
            }

            private void ApplyExternalHeightmap()
            {
                MapLabReliefSettings relief = settings.Relief;
                if (relief.Source == MapLabHeightSource.Procedural || relief.ExternalHeightmap == null)
                    return;
                float[] values = MapLabHeightmapReader.Read(relief.ExternalHeightmap, n, out string warning);
                if (!string.IsNullOrEmpty(warning))
                    UnityEngine.Debug.LogWarning("MapLab: " + warning, relief.ExternalHeightmap);
                if (values == null)
                    return;
                for (int i = 0; i < values.Length; i++)
                    data.Heights[i] += MapLabHeightmapReader.Remap(values[i], relief) * range;
            }

            private void ApplyValley()
            {
                if (composition.ValleyLine.Count < 2 || composition.ValleyHalfWidth <= 0f)
                    return;
                List<Vector2> line = MapLabGeometry.CatmullRom(composition.ValleyLine, 3f);
                float floor = range * 0.06f;
                float half = composition.ValleyHalfWidth;
                for (int z = 0; z < n; z++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float d = MapLabGeometry.Nearest(line, World(x, z), out _, out _, out _, out _);
                        float w = 1f - MapLabGeometry.SmoothStep(half * 0.45f, half * 1.25f, d);
                        int i = data.Index(x, z);
                        float h = data.Heights[i];
                        data.Heights[i] = h - w * composition.ValleyDepth * Mathf.Max(0f, h - floor);
                    }
                }
            }

            private void ApplyFlats()
            {
                for (int k = 0; k < composition.Flats.Count; k++)
                {
                    MapLabComposition.Zone zone = composition.Flats[k];
                    float sum = 0f;
                    int count = 0;
                    for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                    {
                        if (Vector2.Distance(World(x, z), zone.Center) <= zone.Radius * 0.7f)
                        {
                            sum += data.Heights[data.Index(x, z)];
                            count++;
                        }
                    }
                    if (count == 0)
                        continue;
                    float mean = sum / count;
                    for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(World(x, z), zone.Center);
                        float w = zone.Strength * (1f - MapLabGeometry.SmoothStep(zone.Radius * 0.55f, zone.Radius, d));
                        int i = data.Index(x, z);
                        data.Heights[i] = Mathf.Lerp(data.Heights[i], mean, w);
                    }
                }
            }

            // Ступенчатый перепад по авторской линии — отдельно от плавных холмов.
            private void ApplyCliffs()
            {
                MapLabNoise jag = Noise("cliff-jag");
                MapLabCliffSettings cliffs = settings.Cliffs;
                float w = Mathf.Max(0.4f, cliffs.CliffSharpness);
                data.CliffSharpness = w;
                for (int k = 0; k < composition.Cliffs.Count; k++)
                {
                    MapLabComposition.Cliff source = composition.Cliffs[k];
                    if (cliffs.CliffHeight <= 0.01f || source.Control.Count < 2)
                        continue;
                    List<Vector2> line = MapLabGeometry.CatmullRom(source.Control, 1f);
                    float[] stations = MapLabGeometry.Stations(line);
                    float total = stations[stations.Length - 1];
                    for (int i = 1; i < line.Count - 1; i++)
                    {
                        Vector2 t = (line[i + 1] - line[i - 1]).normalized;
                        float offset = jag.Fbm(stations[i] / 14f, k * 3.1f, 2) * cliffs.Jaggedness * 3f;
                        line[i] += MapLabGeometry.LeftNormal(t) * offset;
                    }
                    MapLabCliffLine cliff = new MapLabCliffLine
                    {
                        Height = cliffs.CliffHeight * source.HeightScale,
                        UpperSide = source.UpperSide >= 0f ? 1f : -1f,
                    };
                    for (int i = 0; i < line.Count; i++)
                    {
                        Vector2 p = line[i];
                        float inside = Mathf.Min(Mathf.Min(p.x, data.Size - p.x), Mathf.Min(p.y, data.Size - p.y));
                        // Концы внутри карты сходят в травяной склон; ушедшие за край — нет.
                        float startTaper = InsideMap(line[0]) ? MapLabGeometry.SmoothStep(0f, 22f, stations[i]) : 1f;
                        float endTaper = InsideMap(line[line.Count - 1]) ? MapLabGeometry.SmoothStep(0f, 22f, total - stations[i]) : 1f;
                        float heightNoise = 0.8f + 0.2f * (jag.Fbm(stations[i] / 30f, 7.7f + k, 2) * 0.5f + 0.5f);
                        cliff.Points.Add(p);
                        cliff.Taper.Add(Mathf.Min(startTaper, endTaper) * heightNoise * (inside > -10f ? 1f : 0f));
                    }
                    data.CliffLines.Add(cliff);

                    Vector2 min = line[0], max = line[0];
                    for (int i = 1; i < line.Count; i++)
                    {
                        min = Vector2.Min(min, line[i]);
                        max = Vector2.Max(max, line[i]);
                    }
                    float reach = source.PlateauWidth * 2.2f + w * 3f;
                    Vector2 startTangent = (line[1] - line[0]).normalized;
                    Vector2 endTangent = (line[line.Count - 1] - line[line.Count - 2]).normalized;
                    min -= Vector2.one * reach;
                    max += Vector2.one * reach;
                    for (int z = 0; z < n; z++)
                    {
                        for (int x = 0; x < n; x++)
                        {
                            Vector2 p = World(x, z);
                            if (p.x < min.x || p.y < min.y || p.x > max.x || p.y > max.y)
                                continue;
                            float d = MapLabGeometry.Nearest(line, p, out int seg, out float t, out float side, out _);
                            // Сужение — взвешенное по ближним точкам линии: при смене
                            // ближайшего сегмента высота не скачет (без «рёбер» от концов).
                            float taperSum = 0f, weightSum = 0f;
                            for (int q = 0; q < line.Count; q += 2)
                            {
                                float dd = (line[q] - p).sqrMagnitude / (14f * 14f);
                                float wq = 1f / (1f + dd * dd);
                                taperSum += cliff.Taper[q] * wq;
                                weightSum += wq;
                            }
                            float taper = weightSum > 1e-6f ? taperSum / weightSum : 0f;
                            // За концом линии — расстояние до продолжения касательной и
                            // плавное затухание по выходу за конец: поле непрерывно,
                            // без «рёбер» от смены ближайшей точки.
                            bool beyondStart = seg == 0 && t <= 0f;
                            bool beyondEnd = seg == line.Count - 2 && t >= 1f;
                            float sd = d * side * cliff.UpperSide;
                            if (beyondStart || beyondEnd)
                            {
                                Vector2 anchor = beyondStart ? line[0] : line[line.Count - 1];
                                sd = MapLabGeometry.Cross(beyondStart ? startTangent : endTangent, p - anchor) * cliff.UpperSide;
                            }
                            if (InsideMap(line[0]))
                                taper *= 1f - MapLabGeometry.SmoothStep(0f, 14f, Vector2.Dot(line[0] - p, startTangent));
                            if (InsideMap(line[line.Count - 1]))
                                taper *= 1f - MapLabGeometry.SmoothStep(0f, 14f, Vector2.Dot(p - line[line.Count - 1], endTangent));
                            float step = MapLabGeometry.SmoothStep(-w, w, sd);
                            step = step * step * (3f - 2f * step);
                            float plateau = 1f - MapLabGeometry.SmoothStep(source.PlateauWidth, source.PlateauWidth * 2.2f, sd);
                            int index = data.Index(x, z);
                            data.Heights[index] += cliff.Height * taper * step * plateau;
                            float signedValue = taper > 0.12f ? sd : float.MaxValue;
                            if (Mathf.Abs(signedValue) < Mathf.Abs(cliffSigned[index]))
                                cliffSigned[index] = signedValue;
                            float mask = taper * (1f - MapLabGeometry.SmoothStep(w * 0.9f, w * 2.6f, Mathf.Abs(sd)));
                            data.Cliff[index] = Mathf.Max(data.Cliff[index], mask);
                        }
                    }
                }
            }

            private bool InsideMap(Vector2 p)
            {
                return p.x > 1f && p.y > 1f && p.x < data.Size - 1f && p.y < data.Size - 1f;
            }

            // Озеро: отдельное понижение с неровным берегом; сухой берег гарантированно
            // выше уровня, чтобы вода не выходила за котловину.
            private void ApplyLakes()
            {
                MapLabNoise edge = Noise("lake-edge");
                for (int k = 0; k < composition.Lakes.Count; k++)
                {
                    MapLabComposition.Lake source = composition.Lakes[k];
                    float[] field = new float[n * n];
                    List<float> ring = new List<float>();
                    for (int z = 0; z < n; z++)
                    {
                        for (int x = 0; x < n; x++)
                        {
                            Vector2 p = World(x, z);
                            float f = -10f;
                            for (int l = 0; l < source.Lobes.Count; l++)
                            {
                                Vector4 lobe = source.Lobes[l];
                                float dx = (p.x - lobe.x) / Mathf.Max(1f, lobe.z);
                                float dz = (p.y - lobe.y) / Mathf.Max(1f, lobe.w);
                                f = Mathf.Max(f, 1f - Mathf.Sqrt(dx * dx + dz * dz));
                            }
                            f += edge.Warped(p.x / 22f, p.y / 22f, 3, 0.8f) * 0.16f;
                            for (int l = 0; l < source.Islands.Count; l++)
                            {
                                Vector3 island = source.Islands[l];
                                float di = Vector2.Distance(p, new Vector2(island.x, island.y)) / Mathf.Max(1f, island.z);
                                f = Mathf.Min(f, (di - 1f) * 0.45f + edge.Fbm(p.x / 6f, p.y / 6f, 2) * 0.06f);
                            }
                            int index = data.Index(x, z);
                            field[index] = f;
                            if (Mathf.Abs(f) < 0.05f)
                                ring.Add(data.Heights[index]);
                        }
                    }
                    if (ring.Count == 0)
                        continue;
                    ring.Sort();
                    float level = ring[Mathf.Clamp(ring.Count / 5, 0, ring.Count - 1)] - 0.4f + settings.Water.LevelOffset;
                    level = Mathf.Max(level, 0.3f);
                    MapLabLake lake = new MapLabLake { Center = source.Center, Level = level };
                    lake.Lobes.AddRange(source.Lobes);
                    data.Lakes.Add(lake);
                    const float band = 0.28f;
                    for (int i = 0; i < field.Length; i++)
                    {
                        float f = field[i];
                        float h = data.Heights[i];
                        if (f > 0f)
                        {
                            float deep = MapLabGeometry.SmoothStep(0f, 0.32f, f);
                            data.Heights[i] = Mathf.Min(h, level - 0.3f - source.Depth * deep);
                            data.WaterLevel[i] = level;
                            data.Water[i] = (byte)MapLabWaterKind.Lake;
                        }
                        else if (f > -band)
                        {
                            float t = -f / band;
                            float minH = level + 0.22f + 1.1f * t * t;
                            float keep = MapLabGeometry.SmoothStep(0.9f, 1f, t);
                            if (h < minH)
                                h = Mathf.Lerp(minH, h, keep);
                            else if (data.Cliff[i] < 0.2f)
                                h = Mathf.Lerp(minH + (h - minH) * 0.4f, h, MapLabGeometry.SmoothStep(0.15f, 1f, t));
                            data.Heights[i] = h;
                        }
                        data.LakeField[i] = Mathf.Max(data.LakeField[i], f);
                    }
                }
            }

            // Русло по контрольным точкам: монотонный продольный профиль воды,
            // вырезанное дно и сухие берега чуть выше уровня.
            private void ApplyRivers()
            {
                MapLabNoise meander = Noise("river-meander");
                MapLabWaterSettings water = settings.Water;
                for (int k = 0; k < composition.Rivers.Count; k++)
                {
                    MapLabComposition.River source = composition.Rivers[k];
                    if (source.Control.Count < 2)
                        continue;
                    List<Vector2> line = MapLabGeometry.CatmullRom(source.Control, 2f);
                    float[] st = MapLabGeometry.Stations(line);
                    for (int i = 1; i < line.Count - 1; i++)
                    {
                        Vector2 t = (line[i + 1] - line[i - 1]).normalized;
                        line[i] += MapLabGeometry.LeftNormal(t) * meander.Fbm(st[i] / 45f, k * 5.3f, 2) * 5f;
                    }
                    line = MapLabGeometry.Resample(line, 1.5f);
                    st = MapLabGeometry.Stations(line);
                    MapLabRiver river = new MapLabRiver();
                    float[] natural = new float[line.Count];
                    for (int i = 0; i < line.Count; i++)
                    {
                        float width = water.RiverWidth * source.WidthScale * (1f + 0.2f * meander.Fbm(st[i] / 60f, 11.3f + k, 2));
                        // Русло не уже двух с лишним клеток сетки высот: иначе вода не
                        // заливается (ни одна клетка не попадает внутрь), а линия реки
                        // остаётся — мост встаёт на сухое место, мельница — без воды.
                        width = Mathf.Max(Mathf.Max(2f, data.Cell * 2.4f), width);
                        river.Points.Add(line[i]);
                        river.Widths.Add(width);
                        Vector2 p = line[i];
                        Vector2 tangent = (line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)]).normalized;
                        Vector2 normal = MapLabGeometry.LeftNormal(tangent);
                        float h = data.SampleHeight(p.x, p.y);
                        h = Mathf.Min(h, data.SampleHeight(p.x + normal.x * width * 0.5f, p.y + normal.y * width * 0.5f));
                        h = Mathf.Min(h, data.SampleHeight(p.x - normal.x * width * 0.5f, p.y - normal.y * width * 0.5f));
                        natural[i] = h;
                    }
                    float[] smooth = new float[natural.Length];
                    for (int i = 0; i < natural.Length; i++)
                    {
                        float sum = 0f;
                        int count = 0;
                        for (int j = Mathf.Max(0, i - 6); j <= Mathf.Min(natural.Length - 1, i + 6); j++)
                        {
                            sum += natural[j];
                            count++;
                        }
                        smooth[i] = Mathf.Min(natural[i], sum / count);
                    }
                    float lakeLevel = float.NaN;
                    if (source.FromLake >= 0 && source.FromLake < data.Lakes.Count)
                        lakeLevel = data.Lakes[source.FromLake].Level;
                    float intoLevel = float.NaN;
                    if (source.IntoLake >= 0 && source.IntoLake < data.Lakes.Count)
                        intoLevel = data.Lakes[source.IntoLake].Level;
                    float level = !float.IsNaN(lakeLevel) ? lakeLevel : smooth[0] - 0.7f + water.LevelOffset;
                    for (int i = 0; i < line.Count; i++)
                    {
                        // Вода не поднимается вниз по течению и не выше дна долины.
                        // У истока из озера вода держит уровень озера, пока русло не вышло из котловины.
                        bool atSource = !float.IsNaN(lakeLevel) && data.Bilinear(data.LakeField, line[i].x, line[i].y) > -0.35f;
                        if (!atSource)
                            level = Mathf.Min(level, smooth[i] - 0.55f + water.LevelOffset);
                        if (!float.IsNaN(lakeLevel))
                            level = Mathf.Min(level, lakeLevel);
                        float value = float.IsNaN(intoLevel) ? level : Mathf.Max(level, intoLevel);
                        river.Levels.Add(Mathf.Max(0.2f, value));
                    }
                    data.Rivers.Add(river);
                    CarveRiver(river, water);
                }
            }

            private void CarveRiver(MapLabRiver river, MapLabWaterSettings water)
            {
                float bank = Mathf.Max(1f, water.BankWidth);
                float[] best = new float[n * n];
                float[] station = new float[n * n];
                for (int i = 0; i < best.Length; i++)
                    best[i] = float.MaxValue;
                float[] st = MapLabGeometry.Stations(river.Points);
                float maxWidth = 0f;
                for (int i = 0; i < river.Widths.Count; i++)
                    maxWidth = Mathf.Max(maxWidth, river.Widths[i]);
                float reach = maxWidth * 0.5f + bank + 2f;
                for (int s = 0; s < river.Points.Count - 1; s++)
                {
                    Vector2 a = river.Points[s];
                    Vector2 b = river.Points[s + 1];
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / data.Cell));
                    int x1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach) / data.Cell));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach) / data.Cell));
                    int z1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + reach) / data.Cell));
                    for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = MapLabGeometry.DistanceToSegment(World(x, z), a, b, out float t);
                        int index = data.Index(x, z);
                        if (d < best[index])
                        {
                            best[index] = d;
                            station[index] = s + t;
                        }
                    }
                }
                for (int index = 0; index < best.Length; index++)
                {
                    float d = best[index];
                    if (d == float.MaxValue)
                        continue;
                    float sv = station[index];
                    int s0 = Mathf.Clamp(Mathf.FloorToInt(sv), 0, river.Points.Count - 1);
                    int s1 = Mathf.Min(s0 + 1, river.Points.Count - 1);
                    float f = sv - s0;
                    float width = Mathf.Lerp(river.Widths[s0], river.Widths[s1], f);
                    float level = Mathf.Lerp(river.Levels[s0], river.Levels[s1], f);
                    float inner = width * 0.5f;
                    float h = data.Heights[index];
                    if (d < inner)
                    {
                        float bed = level - water.RiverDepth;
                        float q = d / inner;
                        h = Mathf.Min(h, Mathf.Lerp(bed, level - 0.18f, q * q));
                        float current = data.WaterLevel[index];
                        data.WaterLevel[index] = float.IsNaN(current) ? level : Mathf.Max(current, level);
                        if (data.Water[index] == (byte)MapLabWaterKind.Dry)
                            data.Water[index] = (byte)MapLabWaterKind.River;
                    }
                    else if (d < inner + bank)
                    {
                        float t = (d - inner) / bank;
                        float minH = level + 0.22f + 0.6f * t;
                        if (h < minH && data.Water[index] == (byte)MapLabWaterKind.Dry)
                            h = Mathf.Lerp(minH, h, MapLabGeometry.SmoothStep(0.55f, 1f, t));
                        else if (h > minH && data.Cliff[index] < 0.2f)
                            h = Mathf.Lerp(minH + (h - minH) * 0.45f, h, MapLabGeometry.SmoothStep(0f, 1f, t));
                    }
                    data.Heights[index] = h;
                }
            }

            private void BuildRoads()
            {
                MapLabNoise wander = Noise("road-wander");
                MapLabRoadSettings roads = settings.RoadsAndFields;
                for (int k = 0; k < composition.Roads.Count; k++)
                {
                    MapLabComposition.Road source = composition.Roads[k];
                    if (source.Control.Count < 2)
                        continue;
                    List<Vector2> points = MapLabGeometry.CatmullRom(source.Control, 2f);
                    float[] st = MapLabGeometry.Stations(points);
                    for (int i = 1; i < points.Count - 1; i++)
                    {
                        Vector2 t = (points[i + 1] - points[i - 1]).normalized;
                        points[i] += MapLabGeometry.LeftNormal(t) * wander.Fbm(st[i] / 30f, k * 2.7f, 2) * 2.5f;
                    }
                    RelaxRoad(points);
                    SnapEnd(points, true);
                    SnapEnd(points, false);
                    MapLabPolyline road = new MapLabPolyline
                    {
                        Kind = source.Kind,
                        Width = source.Kind == MapLabRoadKind.Road ? roads.RoadWidth : roads.PathWidth,
                    };
                    road.Points.AddRange(MapLabGeometry.Resample(points, 1f));
                    data.Roads.Add(road);
                }
                for (int r = 0; r < data.Roads.Count; r++)
                    PlaceBridges(data.Roads[r]);
                for (int r = 0; r < data.Roads.Count; r++)
                    BenchRoad(data.Roads[r]);
            }

            // Конец тропы рядом с уже проложенной дорогой примыкает к ней (развилка
            // без «креста» и висящих хвостов).
            private void SnapEnd(List<Vector2> points, bool start)
            {
                if (points.Count < 3)
                    return;
                Vector2 end = start ? points[0] : points[points.Count - 1];
                float best = 14f;
                Vector2 target = end;
                for (int r = 0; r < data.Roads.Count; r++)
                {
                    List<Vector2> line = data.Roads[r].Points;
                    float d = MapLabGeometry.Nearest(line, end, out int seg, out float t, out _, out _);
                    if (d < best)
                    {
                        best = d;
                        target = Vector2.Lerp(line[seg], line[seg + 1], t);
                    }
                }
                if (target == end)
                    return;
                // Ближние к развилке точки убираем: тропа отходит от дороги без крюка.
                if (start)
                {
                    while (points.Count > 3 && Vector2.Distance(points[1], target) < 8f)
                        points.RemoveAt(1);
                    points[0] = target;
                }
                else
                {
                    while (points.Count > 3 && Vector2.Distance(points[points.Count - 2], target) < 8f)
                        points.RemoveAt(points.Count - 2);
                    points[points.Count - 1] = target;
                }
                // Подход к развилке — плавный.
                MapLabGeometry.Smooth(points, 2, 0.3f);
            }

            // Дорога огибает непроходимый уступ, крутизну и озеро; конечные точки
            // и сочленения остаются на месте.
            private void RelaxRoad(List<Vector2> points)
            {
                if (points.Count < 3)
                    return;
                Vector2[] original = points.ToArray();
                for (int iteration = 0; iteration < 28; iteration++)
                {
                    for (int i = 2; i < points.Count - 2; i++)
                    {
                        Vector2 p = points[i];
                        const float e = 1.5f;
                        float gx = Cost(p + new Vector2(e, 0f)) - Cost(p - new Vector2(e, 0f));
                        float gz = Cost(p + new Vector2(0f, e)) - Cost(p - new Vector2(0f, e));
                        Vector2 g = new Vector2(gx, gz) / (2f * e);
                        if (g.sqrMagnitude > 4f)
                            g = g.normalized * 2f;
                        p -= g * 1.4f;
                        p = Vector2.Lerp(p, original[i], 0.04f);
                        Vector2 offset = p - original[i];
                        if (offset.magnitude > 16f)
                            p = original[i] + offset.normalized * 16f;
                        points[i] = p;
                    }
                    MapLabGeometry.Smooth(points, 1, 0.35f);
                }
            }

            private float Cost(Vector2 p)
            {
                if (!data.Contains(p.x, p.y))
                    return 0f;
                float c = data.Bilinear(data.Cliff, p.x, p.y) * 9f;
                float lake = data.Bilinear(data.LakeField, p.x, p.y);
                c += MapLabGeometry.SmoothStep(-0.2f, 0.05f, lake) * 7f;
                float h = data.SampleHeight(p.x, p.y);
                float hx = data.SampleHeight(p.x + 2f, p.y) - data.SampleHeight(p.x - 2f, p.y);
                float hz = data.SampleHeight(p.x, p.y + 2f) - data.SampleHeight(p.x, p.y - 2f);
                float slope = Mathf.Sqrt(hx * hx + hz * hz) / 4f;
                c += MapLabGeometry.SmoothStep(0.12f, 0.5f, slope) * 3f;
                return c + h * 0.0f;
            }

            // Мост ставится по окончательному руслу: поперёк локального течения,
            // концы — на сухих берегах; дорога подводится к мосту прямо.
            private void PlaceBridges(MapLabPolyline road)
            {
                for (int r = 0; r < data.Rivers.Count; r++)
                {
                    MapLabRiver river = data.Rivers[r];
                    for (int i = 0; i < road.Points.Count - 1; i++)
                    {
                        for (int j = 0; j < river.Points.Count - 1; j++)
                        {
                            if (!MapLabGeometry.SegmentIntersection(road.Points[i], road.Points[i + 1], river.Points[j], river.Points[j + 1], out Vector2 hit, out _, out float tr))
                                continue;
                            if (!data.Contains(hit.x, hit.y))
                                continue;
                            // Моста без воды не бывает: пересечение с сухой линией реки — просто дорога.
                            if (!data.IsUnderWater(hit.x, hit.y, 0f))
                                continue;
                            Vector2 flow = (river.Points[j + 1] - river.Points[j]).normalized;
                            Vector2 across = MapLabGeometry.LeftNormal(flow);
                            Vector2 roadDir = (road.Points[i + 1] - road.Points[i]).normalized;
                            if (Vector2.Dot(across, roadDir) < 0f)
                                across = -across;
                            float width = Mathf.Lerp(river.Widths[j], river.Widths[j + 1], tr);
                            float level = Mathf.Lerp(river.Levels[j], river.Levels[j + 1], tr);
                            float half = width * 0.5f + 2f;
                            for (int guard = 0; guard < 24; guard++)
                            {
                                Vector2 a = hit - across * half;
                                Vector2 b = hit + across * half;
                                if (Dry(a, level) && Dry(b, level))
                                    break;
                                half += 0.5f;
                            }
                            Vector2 endA = hit - across * half;
                            Vector2 endB = hit + across * half;
                            float deck = Mathf.Max(level + 1.1f, Mathf.Max(data.SampleHeight(endA.x, endA.y), data.SampleHeight(endB.x, endB.y)) + 0.15f);
                            data.Bridges.Add(new MapLabBridge
                            {
                                EndA = endA,
                                EndB = endB,
                                DeckHeight = deck,
                                Width = Mathf.Max(2.4f, road.Width + 0.8f),
                                WaterLevel = level,
                            });
                            SpliceBridge(road, hit, endA, endB, across, half);
                            return;
                        }
                    }
                }
            }

            private bool Dry(Vector2 p, float level)
            {
                if (!data.Contains(p.x, p.y))
                    return true;
                return data.SampleHeight(p.x, p.y) > level + 0.3f && !data.IsUnderWater(p.x, p.y, 0.2f);
            }

            private static void SpliceBridge(MapLabPolyline road, Vector2 hit, Vector2 endA, Vector2 endB, Vector2 across, float half)
            {
                float cut = half + 6f;
                int first = -1, last = -1;
                for (int i = 0; i < road.Points.Count; i++)
                {
                    if (Vector2.Distance(road.Points[i], hit) <= cut)
                    {
                        if (first < 0) first = i;
                        last = i;
                    }
                }
                if (first < 0)
                    return;
                List<Vector2> replacement = new List<Vector2>
                {
                    endA - across * 4f,
                    endA - across * 2f,
                    endA,
                    endB,
                    endB + across * 2f,
                    endB + across * 4f,
                };
                road.Points.RemoveRange(first, last - first + 1);
                road.Points.InsertRange(first, replacement);
                List<Vector2> resampled = MapLabGeometry.Resample(road.Points, 1f);
                road.Points.Clear();
                road.Points.AddRange(resampled);
            }

            // Дорога слегка выравнивает полосу под собой (полка на склоне).
            private void BenchRoad(MapLabPolyline road)
            {
                int count = road.Points.Count;
                if (count < 2)
                    return;
                float[] profile = new float[count];
                for (int i = 0; i < count; i++)
                    profile[i] = data.SampleHeight(road.Points[i].x, road.Points[i].y);
                float[] smooth = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float sum = 0f;
                    int c = 0;
                    for (int j = Mathf.Max(0, i - 5); j <= Mathf.Min(count - 1, i + 5); j++)
                    {
                        sum += profile[j];
                        c++;
                    }
                    smooth[i] = sum / c;
                }
                float half = road.Width * 0.5f;
                float reach = half + 2f;
                for (int s = 0; s < count - 1; s++)
                {
                    Vector2 a = road.Points[s];
                    Vector2 b = road.Points[s + 1];
                    if (OnBridge(a, b))
                        continue;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / data.Cell));
                    int x1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach) / data.Cell));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach) / data.Cell));
                    int z1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + reach) / data.Cell));
                    for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = MapLabGeometry.DistanceToSegment(World(x, z), a, b, out float t);
                        int index = data.Index(x, z);
                        if (d < roadDistance[index])
                            roadDistance[index] = d - half;
                        if (d > reach || data.Water[index] != (byte)MapLabWaterKind.Dry || data.Cliff[index] > 0.3f)
                            continue;
                        float target = Mathf.Lerp(smooth[s], smooth[s + 1], t);
                        float w = (1f - MapLabGeometry.SmoothStep(half, reach, d)) * 0.6f;
                        data.Heights[index] = Mathf.Lerp(data.Heights[index], target, w);
                    }
                }
            }

            private bool OnBridge(Vector2 a, Vector2 b)
            {
                for (int i = 0; i < data.Bridges.Count; i++)
                {
                    MapLabBridge bridge = data.Bridges[i];
                    Vector2 mid = (a + b) * 0.5f;
                    float d = MapLabGeometry.DistanceToSegment(mid, bridge.EndA, bridge.EndB, out float t);
                    if (d < 1f && t > 0.02f && t < 0.98f)
                        return true;
                }
                return false;
            }

            private void ClampHeights()
            {
                float min = float.MaxValue, max = float.MinValue;
                for (int i = 0; i < data.Heights.Length; i++)
                {
                    float h = data.Heights[i];
                    if (float.IsNaN(h) || float.IsInfinity(h))
                        h = 0f;
                    data.Heights[i] = h;
                    min = Mathf.Min(min, h);
                    max = Mathf.Max(max, h);
                }
                data.MinHeight = min;
                data.MaxHeight = Mathf.Max(min + 0.01f, max);
            }

            private void ComputeMasks()
            {
                MapLabNoise rockNoise = Noise("rock-patches");
                ShoreDistance();
                for (int z = 0; z < n; z++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        int i = data.Index(x, z);
                        Vector2 p = World(x, z);
                        float slope = data.SlopeDegrees[i];
                        float rocky = MapLabGeometry.SmoothStep(26f, 42f, slope) * (0.75f + 0.25f * rockNoise.Fbm(p.x / 9f, p.y / 9f, 2));
                        // Под крышкой уступа (верхняя сторона) камень не красим — крышка
                        // берёт цвет поверхности и должна быть травяной, как площадка.
                        float sdc = cliffSigned[i];
                        float cap = sdc < 1e5f ? MapLabGeometry.SmoothStep(-settings.Cliffs.CliffSharpness * 1.3f, -settings.Cliffs.CliffSharpness * 0.9f, sdc) * (1f - MapLabGeometry.SmoothStep(settings.Cliffs.CliffSharpness * 2.2f, settings.Cliffs.CliffSharpness * 3f, sdc)) : 0f;
                        data.Rock[i] = Mathf.Clamp01(Mathf.Max(data.Cliff[i], rocky) * (1f - cap));
                        float road = roadDistance[i];
                        data.Road[i] = road == float.MaxValue ? 0f : 1f - MapLabGeometry.SmoothStep(-0.4f, 0.4f, road);
                        float settlement = 0f;
                        foreach (MapLabComposition.Settlement st in composition.Settlements)
                        {
                            float d = Vector2.Distance(p, st.Center);
                            settlement = Mathf.Max(settlement, 1f - MapLabGeometry.SmoothStep(st.Radius * 0.75f, st.Radius * 1.1f, d));
                        }
                        data.Settlement[i] = settlement;
                    }
                }
                data.RoadDistance = roadDistance;
                data.CliffSigned = cliffSigned;
                foreach (MapLabComposition.Settlement st in composition.Settlements)
                    data.SettlementSites.Add(new Vector3(st.Center.x, st.Center.y, st.Radius));
            }

            // Береговая полоса: расстояние до воды по сетке (двухпроходная фаска).
            private void ShoreDistance()
            {
                float bank = Mathf.Max(1f, settings.Water.BankWidth);
                float[] dist = new float[n * n];
                float big = 1e6f;
                for (int i = 0; i < dist.Length; i++)
                    dist[i] = data.Water[i] != (byte)MapLabWaterKind.Dry && data.Heights[i] < data.WaterLevel[i] ? 0f : big;
                float c = data.Cell;
                float diagonal = c * 1.4142f;
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = data.Index(x, z);
                    float d = dist[i];
                    if (x > 0) d = Mathf.Min(d, dist[i - 1] + c);
                    if (z > 0) d = Mathf.Min(d, dist[i - n] + c);
                    if (x > 0 && z > 0) d = Mathf.Min(d, dist[i - n - 1] + diagonal);
                    if (x < n - 1 && z > 0) d = Mathf.Min(d, dist[i - n + 1] + diagonal);
                    dist[i] = d;
                }
                for (int z = n - 1; z >= 0; z--)
                for (int x = n - 1; x >= 0; x--)
                {
                    int i = data.Index(x, z);
                    float d = dist[i];
                    if (x < n - 1) d = Mathf.Min(d, dist[i + 1] + c);
                    if (z < n - 1) d = Mathf.Min(d, dist[i + n] + c);
                    if (x < n - 1 && z < n - 1) d = Mathf.Min(d, dist[i + n + 1] + diagonal);
                    if (x > 0 && z < n - 1) d = Mathf.Min(d, dist[i + n - 1] + diagonal);
                    dist[i] = d;
                }
                for (int i = 0; i < dist.Length; i++)
                    data.Shore[i] = dist[i] >= big ? 0f : 1f - MapLabGeometry.SmoothStep(0f, bank * 0.9f, dist[i]);
                data.WaterDistance = dist;
            }

            // Поля — нерегулярные участки (ячейки Вороного) с собственной осью борозд.
            private void BuildFields()
            {
                MapLabRoadSettings roads = settings.RoadsAndFields;
                if (composition.Settlements.Count == 0 || roads.FieldDensity <= 0.001f)
                {
                    data.Fields = new MapLabFields(data.FieldBlocks, roads.FieldStripWidth, Seed);
                    return;
                }
                MapLabRandom rng = MapLabRandom.ForStream(Seed, "fields", MapLabData.AlgorithmVersion);
                float spacing = 30f;
                // 12У-E (мастер-бриф §9.14–9.25): у деревень плана — группы
                // направленных полос от выездов и дорог с луговыми промежутками.
                if (composition.FromPlan)
                {
                    BuildFieldGroups(rng);
                    return;
                }
                // 12У-E: участки вокруг каждого поселения; первое — как прежде (тот же
                // расход случайности), у следующих — без повторных участков в общей зоне.
                foreach (MapLabComposition.Settlement st in composition.Settlements)
                {
                Vector2 center = st.Center;
                float outer = st.FieldRadius > 0f ? st.FieldRadius : roads.FieldRadius;
                int before = data.FieldBlocks.Count;
                for (float gz = center.y - outer; gz <= center.y + outer; gz += spacing)
                {
                    for (float gx = center.x - outer; gx <= center.x + outer; gx += spacing)
                    {
                        Vector2 site = new Vector2(gx + rng.Signed() * spacing * 0.38f, gz + rng.Signed() * spacing * 0.38f);
                        float d = Vector2.Distance(site, center);
                        if (d > outer * 1.1f || !data.Contains(site.x, site.y))
                            continue;
                        bool taken = false;
                        for (int k = 0; k < before && !taken; k++)
                            taken = (data.FieldBlocks[k].Site - site).sqrMagnitude < spacing * spacing * 0.36f;
                        if (taken)
                            continue;
                        MapLabFieldBlock block = new MapLabFieldBlock
                        {
                            Site = site,
                            ToneSeed = (int)rng.NextUInt(),
                        };
                        float angle;
                        if (NearestRoadTangent(site, 45f, out Vector2 tangent))
                            angle = Mathf.Atan2(tangent.y, tangent.x);
                        else
                        {
                            int gi = data.Index(Mathf.Clamp(Mathf.RoundToInt(site.x / data.Cell), 0, n - 1), Mathf.Clamp(Mathf.RoundToInt(site.y / data.Cell), 0, n - 1));
                            Vector2 down = data.Downhill[gi];
                            angle = down.sqrMagnitude > 0.01f ? Mathf.Atan2(down.x, -down.y) : rng.Range(0f, Mathf.PI);
                        }
                        if (rng.Chance(0.35f))
                            angle += Mathf.PI * 0.5f;
                        block.Angle = angle + rng.Signed() * 0.2f;
                        block.Cultivated = d > st.Radius * 0.7f && rng.Chance(0.45f + 0.5f * roads.FieldDensity);
                        data.FieldBlocks.Add(block);
                    }
                }
                }
                data.Fields = new MapLabFields(data.FieldBlocks, roads.FieldStripWidth, Seed);
                MapLabNoise fieldEdge = Noise("field-edge");
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    Vector2 p = World(x, z);
                    int i = data.Index(x, z);
                    data.Field[i] = FieldAllowed(p, i, fieldEdge) ? 1f : 0f;
                }
            }

            // Группы полей (§9.17–9.19): якоря — точки дорог на выезде из деревни
            // (ленты вдоль дороги или полосы поперёк, «от задних дворов») и
            // свободные стороны; группа — вытянутое пятно из узких участков
            // общего направления (разной длины и ширины); вокруг — луговые
            // участки, которые обрывают группу и оставляют просветы.
            private void BuildFieldGroups(MapLabRandom rng)
            {
                MapLabRoadSettings roads = settings.RoadsAndFields;
                List<(Vector2 Center, Vector2 Axis, Vector2 Half)> groups = new List<(Vector2, Vector2, Vector2)>();
                foreach (MapLabComposition.Settlement st in composition.Settlements)
                {
                    int size = Mathf.Clamp(st.Size, 1, 3);
                    int min = size == 1 ? 2 : size == 2 ? 3 : 4, max = size == 1 ? 4 : size == 2 ? 5 : 7;
                    int want = Mathf.RoundToInt(Mathf.Lerp(min, max, roads.FieldDensity * rng.Range(0.7f, 1.1f)));
                    float ring = st.Radius * rng.Range(1.0f, 1.25f);
                    List<(Vector2 P, Vector2 T)> anchors = new List<(Vector2, Vector2)>();
                    foreach (MapLabPolyline road in data.Roads)
                    {
                        if (road.Points.Count < 2 || road.Widening)
                            continue;
                        List<Vector2> pts = MapLabGeometry.Resample(road.Points, 4f);
                        for (int i = 1; i < pts.Count - 1; i++)
                        {
                            float d = Vector2.Distance(pts[i], st.Center);
                            if (d > ring && d < ring + 6f)
                                anchors.Add((pts[i], (pts[i + 1] - pts[i - 1]).normalized));
                        }
                    }
                    // Свободные стороны: «задние дворы» — радиально от центра.
                    for (int k = 0; k < 6; k++)
                    {
                        float a = rng.Range(0f, Mathf.PI * 2f);
                        Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        anchors.Add((st.Center + dir * ring, new Vector2(-dir.y, dir.x)));
                    }
                    List<Vector2> usedDirections = new List<Vector2>();
                    int placed = 0;
                    for (int attempt = 0; attempt < anchors.Count * 2 && placed < want; attempt++)
                    {
                        (Vector2 p, Vector2 t) = anchors[rng.Range(0, anchors.Count)];
                        Vector2 radial = (p - st.Center).normalized;
                        bool spread = false;
                        foreach (Vector2 u in usedDirections)
                            spread |= Vector2.Dot(u, radial) > 0.64f;
                        if (spread)
                            continue;
                        // Лента вдоль дороги или полосы поперёк (от дворов).
                        bool along = rng.Chance(0.5f);
                        Vector2 axis = along ? t : radial;
                        float length = rng.Range(26f, 58f) * Mathf.Lerp(0.8f, 1.15f, size / 3f);
                        float width = rng.Range(14f, 28f);
                        Vector2 side = Vector2.Dot(new Vector2(-t.y, t.x), radial) >= 0f ? new Vector2(-t.y, t.x) : new Vector2(t.y, -t.x);
                        Vector2 center = along ? p + side * (width * 0.5f + 4f) : p + radial * (length * 0.5f + 2f);
                        if (!data.Contains(center.x, center.y) || composition.SamplePlan(composition.PlanForest, center) > 0.5f)
                            continue;
                        Vector2 half = along ? new Vector2(length * 0.5f, width * 0.5f) : new Vector2(width * 0.5f, length * 0.5f);
                        Vector2 groupAxis = along ? axis : new Vector2(-radial.y, radial.x);
                        groups.Add((center, groupAxis, half));
                        usedDirections.Add(radial);
                        placed++;
                        // Участки группы: узкие полосы поперёк оси группы, разной ширины;
                        // борозды — вдоль полос (общее направление группы).
                        Vector2 u1 = groupAxis, v1 = new Vector2(-u1.y, u1.x);
                        float furrow = Mathf.Atan2(v1.y, v1.x) + rng.Signed() * 0.08f;
                        float x = -half.x;
                        while (x < half.x)
                        {
                            float strip = rng.Range(6f, 12f);
                            float rows = Mathf.Max(1f, Mathf.Round(half.y * 2f / rng.Range(16f, 26f)));
                            for (int r = 0; r < rows; r++)
                            {
                                float y = -half.y + (r + 0.5f) * half.y * 2f / rows;
                                Vector2 site = center + u1 * (x + strip * 0.5f) + v1 * (y + rng.Signed() * 1.5f);
                                if (!data.Contains(site.x, site.y))
                                    continue;
                                data.FieldBlocks.Add(new MapLabFieldBlock
                                {
                                    Site = site,
                                    ToneSeed = (int)rng.NextUInt(),
                                    Angle = furrow + rng.Signed() * 0.05f,
                                    // Редкий участок в группе — зелень (пар, луг).
                                    Cultivated = rng.Chance(0.88f),
                                });
                            }
                            x += strip;
                        }
                    }
                }
                // Луговые участки вокруг групп обрывают ячейки и оставляют просветы.
                foreach (MapLabComposition.Settlement st in composition.Settlements)
                {
                    float outer = (st.FieldRadius > 0f ? st.FieldRadius : roads.FieldRadius) * 1.5f;
                    for (float gz = st.Center.y - outer; gz <= st.Center.y + outer; gz += 18f)
                    {
                        for (float gx = st.Center.x - outer; gx <= st.Center.x + outer; gx += 18f)
                        {
                            Vector2 site = new Vector2(gx + rng.Signed() * 5f, gz + rng.Signed() * 5f);
                            if (!data.Contains(site.x, site.y))
                                continue;
                            bool inside = false;
                            foreach ((Vector2 c, Vector2 axis, Vector2 h) in groups)
                            {
                                Vector2 q = site - c;
                                float a = Vector2.Dot(q, axis), b = Vector2.Dot(q, new Vector2(-axis.y, axis.x));
                                inside |= Mathf.Abs(a) < h.x + 7f && Mathf.Abs(b) < h.y + 7f;
                            }
                            if (!inside)
                                data.FieldBlocks.Add(new MapLabFieldBlock { Site = site, ToneSeed = (int)rng.NextUInt(), Angle = 0f, Cultivated = false });
                        }
                    }
                }
                data.Fields = new MapLabFields(data.FieldBlocks, roads.FieldStripWidth, Seed);
                MapLabNoise fieldEdge = Noise("field-edge");
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    Vector2 p = World(x, z);
                    int i = data.Index(x, z);
                    data.Field[i] = FieldAllowed(p, i, fieldEdge) ? 1f : 0f;
                }
            }

            private bool FieldAllowed(Vector2 p, int index, MapLabNoise edge)
            {
                MapLabRoadSettings roads = settings.RoadsAndFields;
                if (!data.Fields.Evaluate(p, out MapLabFields.Sample sample) || !data.FieldBlocks[sample.Block].Cultivated)
                    return false;
                // 12У-C: поля — только в игровой области, с отступом от её края.
                if (data.PlayRect.width > 0f && (p.x < data.PlayRect.xMin + 6f || p.x > data.PlayRect.xMax - 6f || p.y < data.PlayRect.yMin + 6f || p.y > data.PlayRect.yMax - 6f))
                    return false;
                // 12У-D/F: клетки полей плана разрешены и у самой деревни; остальные
                // поля — группы MapLab вокруг деревень плана (§9.14: много небольших полей).
                bool planField = composition.PlanField != null && composition.SamplePlan(composition.PlanField, p) > 0.5f;
                // Лес плана главнее полей: там игра ждёт леса.
                if (!planField && composition.PlanForest != null && composition.SamplePlan(composition.PlanForest, p) > 0.35f)
                    return false;
                MapLabComposition.Settlement near = composition.Nearest(p, out float d);
                if (near == null)
                    return false;
                // Внутренняя и внешняя граница пашни — неровные, не круг.
                float wobble = 1f + 0.3f * edge.Fbm(p.x / 22f, p.y / 22f, 2);
                float fieldRadius = near.FieldRadius > 0f ? near.FieldRadius : roads.FieldRadius;
                // Поля плана лежат вплотную к деревне: от дворов их отделяет маска застройки.
                // 12У-E: группы полос — от края дворов до дальнего края группы.
                float inner = composition.FromPlan ? 0.75f : planField ? 0.3f : 0.8f;
                float outerBound = composition.FromPlan ? fieldRadius * 1.6f : fieldRadius * (0.85f + 0.3f * wobble);
                if (d < near.Radius * inner * wobble || d > outerBound)
                    return false;
                if (data.SlopeDegrees[index] > roads.FieldMaxSlope)
                    return false;
                if (data.Shore[index] > 0.35f || data.Water[index] != (byte)MapLabWaterKind.Dry)
                    return false;
                if (data.Cliff[index] > 0.05f || data.Rock[index] > 0.3f)
                    return false;
                return roadDistance[index] > 0.3f;
            }

            private bool NearestRoadTangent(Vector2 p, float maxDistance, out Vector2 tangent)
            {
                tangent = Vector2.right;
                float best = maxDistance;
                bool found = false;
                for (int r = 0; r < data.Roads.Count; r++)
                {
                    float d = MapLabGeometry.Nearest(data.Roads[r].Points, p, out _, out _, out _, out Vector2 t);
                    if (d < best)
                    {
                        best = d;
                        tangent = t;
                        found = true;
                    }
                }
                return found;
            }

            // Модули скальной стенки вдоль линии: начало/конец, короткая/длинная
            // стенка, внешний/внутренний поворот, переход в травяной склон.
            private void BuildCliffColumns()
            {
                MapLabRandom rng = MapLabRandom.ForStream(Seed, "cliff-columns", MapLabData.AlgorithmVersion);
                MapLabCliffSettings cliffs = settings.Cliffs;
                float w = Mathf.Max(0.4f, cliffs.CliffSharpness);
                data.CliffSharpness = w;
                for (int k = 0; k < data.CliffLines.Count; k++)
                {
                    MapLabCliffLine cliff = data.CliffLines[k];
                    List<Vector2> line = cliff.Points;
                    if (line.Count < 2)
                        continue;
                    float[] st = MapLabGeometry.Stations(line);
                    float total = st[st.Length - 1];
                    float s = 0f;
                    int segment = 0;
                    while (s < total)
                    {
                        float width = cliffs.ColumnWidth * rng.Range(0.5f, 2.2f);
                        float s1 = Mathf.Min(total, s + width);
                        Vector2 a = PointAt(line, st, s, ref segment, out float taperA, cliff);
                        int segB = segment;
                        Vector2 b = PointAt(line, st, s1, ref segB, out float taperB, cliff);
                        float taper = Mathf.Min(taperA, taperB);
                        Vector2 tangent = (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : Vector2.right;
                        Vector2 upper = MapLabGeometry.LeftNormal(tangent) * cliff.UpperSide;
                        Vector2 outward = -upper;
                        if (taper > 0.3f && InsideMap(a) && InsideMap(b))
                        {
                            float bottom = Mathf.Min(SampleH(a + outward * w * 1.9f), SampleH(b + outward * w * 1.9f)) - 0.7f;
                            float top = Mathf.Max(SampleH(a + upper * w * 1.5f), SampleH(b + upper * w * 1.5f));
                            top += rng.Signed() * 0.25f * cliffs.Jaggedness - 0.05f;
                            top = Mathf.Min(top, bottom + cliff.Height * 1.6f + 2f);
                            if (top - bottom > 0.9f)
                            {
                                cliff.Columns.Add(new MapLabCliffColumn
                                {
                                    Start = a,
                                    End = b,
                                    Outward = outward,
                                    Bottom = bottom,
                                    Top = top,
                                    Module = MapLabCliffModule.ShortWall,
                                });
                            }
                        }
                        s = s1 - width * rng.Range(0.05f, 0.15f);
                        if (s1 >= total)
                            break;
                    }
                    TagModules(cliff);
                }
            }

            private void TagModules(MapLabCliffLine cliff)
            {
                List<MapLabCliffColumn> columns = cliff.Columns;
                int runStart = 0;
                for (int i = 0; i < columns.Count; i++)
                {
                    MapLabCliffColumn c = columns[i];
                    bool gapBefore = i == 0 || Vector2.Distance(columns[i - 1].End, c.Start) > 2f;
                    bool gapAfter = i == columns.Count - 1 || Vector2.Distance(c.End, columns[i + 1].Start) > 2f;
                    if (gapBefore)
                        runStart = i;
                    float height = c.Top - c.Bottom;
                    if (gapBefore)
                        c.Module = MapLabCliffModule.Start;
                    else if (gapAfter)
                        c.Module = MapLabCliffModule.End;
                    else if (height < cliff.Height * 0.4f)
                        c.Module = MapLabCliffModule.GrassTransition;
                    else
                    {
                        Vector2 t0 = (columns[i - 1].End - columns[i - 1].Start).normalized;
                        Vector2 t1 = (columns[i + 1].End - columns[i + 1].Start).normalized;
                        float turn = MapLabGeometry.Cross(t0, t1) * cliff.UpperSide;
                        if (Mathf.Abs(turn) > 0.22f)
                            c.Module = turn > 0f ? MapLabCliffModule.InnerCorner : MapLabCliffModule.OuterCorner;
                        else
                            c.Module = i - runStart > 4 ? MapLabCliffModule.LongWall : MapLabCliffModule.ShortWall;
                    }
                    columns[i] = c;
                }
            }

            private Vector2 PointAt(List<Vector2> line, float[] st, float s, ref int segment, out float taper, MapLabCliffLine cliff)
            {
                while (segment < line.Count - 2 && st[segment + 1] < s)
                    segment++;
                float length = Mathf.Max(1e-5f, st[segment + 1] - st[segment]);
                float t = Mathf.Clamp01((s - st[segment]) / length);
                taper = Mathf.Lerp(cliff.Taper[segment], cliff.Taper[segment + 1], t);
                return Vector2.Lerp(line[segment], line[segment + 1], t);
            }

            private float SampleH(Vector2 p)
            {
                return data.SampleHeight(p.x, p.y);
            }
        }

        public static void Smooth(float[] heights, int passes)
        {
            int count = heights.Length;
            int n = Mathf.RoundToInt(Mathf.Sqrt(count));
            if (n * n != count || passes <= 0)
                return;
            float[] temp = new float[count];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float sum = 0f;
                    float w = 0f;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = Mathf.Clamp(x + dx, 0, n - 1);
                        int zz = Mathf.Clamp(z + dz, 0, n - 1);
                        float k = dx == 0 && dz == 0 ? 4f : (dx == 0 || dz == 0 ? 2f : 1f);
                        sum += heights[zz * n + xx] * k;
                        w += k;
                    }
                    temp[z * n + x] = sum / w;
                }
                Array.Copy(temp, heights, count);
            }
        }

        // Нормали из общих данных высот (центральные разности по всей карте) —
        // соседние чанки получают одинаковые нормали на стыке.
        public static void ComputeNormals(MapLabData data)
        {
            int n = data.Resolution;
            float c = data.Cell;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int xl = Mathf.Max(0, x - 1), xr = Mathf.Min(n - 1, x + 1);
                    int zd = Mathf.Max(0, z - 1), zu = Mathf.Min(n - 1, z + 1);
                    float dx = (data.Heights[data.Index(xr, z)] - data.Heights[data.Index(xl, z)]) / ((xr - xl) * c);
                    float dz = (data.Heights[data.Index(x, zu)] - data.Heights[data.Index(x, zd)]) / ((zu - zd) * c);
                    Vector3 normal = new Vector3(-dx, 1f, -dz).normalized;
                    int i = data.Index(x, z);
                    data.Normals[i] = normal;
                    data.SlopeDegrees[i] = Mathf.Acos(Mathf.Clamp(normal.y, -1f, 1f)) * Mathf.Rad2Deg;
                    Vector2 down = new Vector2(-dx, -dz);
                    data.Downhill[i] = down.sqrMagnitude > 1e-6f ? down.normalized : Vector2.zero;
                }
            }
        }
    }
}
