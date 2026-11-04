using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Растительность и постройки. Отдельные потоки seed: «forest», «singles»,
    // «bushes», «rocks», «reeds», «houses» — изменение плотности кустов не
    // сдвигает деревья и дома.
    public static partial class MapLabPlacer
    {
        // Постройки текущей расстановки — для быстрых проверок отступа деревьев.
        private static readonly List<MapLabPlacement> buildings = new List<MapLabPlacement>();

        public static void Place(MapLabData data, MapLabSettings settings)
        {
            data.Objects.Clear();
            data.Fences.Clear();
            data.Yards.Clear();
            data.YardPaths.Clear();
            data.YardRects.Clear();
            data.Piers.Clear();
            data.Props.Clear();
            data.FenceGraph = null;
            data.Plaza = Vector3.zero;
            data.Roads.RemoveAll(r => r.Widening);
            PlaceSettlement(data, settings);
            buildings.Clear();
            for (int i = 0; i < data.Objects.Count; i++)
                if (data.Objects[i].Kind == MapLabObjectKind.House || data.Objects[i].Kind == MapLabObjectKind.Barn)
                    buildings.Add(data.Objects[i]);
            BuildForestMask(data, settings);
            PlaceForest(data, settings);
            PlaceSingles(data, settings);
            PlaceBushes(data, settings);
            PlaceRocks(data, settings);
            PlaceReeds(data, settings);
            PlaceHaystacks(data, settings);
        }

        private static ulong Stream(MapLabData data, string name)
        {
            return MapLabRandom.StreamSeed(data.Seed, name, MapLabData.AlgorithmVersion);
        }

        // ---------- Поселение ----------

        private static void PlaceSettlement(MapLabData data, MapLabSettings settings)
        {
            MapLabComposition c = data.Composition;
            MapLabSettlementSettings s = settings.Settlement;
            if (c == null || !c.HasSettlement || s.HousesMax <= 0)
            {
                if (c != null && c.Pier)
                    PlacePier(data, c.SettlementCenter);
                return;
            }
            if (settings.IsStage05)
            {
                PlaceVillage05(data, settings);
                if (c.Pier)
                    PlacePier(data, c.SettlementCenter);
                return;
            }
            MapLabRandom rng = MapLabRandom.ForStream(data.Seed, "houses", MapLabData.AlgorithmVersion);
            int target = rng.Range(Mathf.Min(s.HousesMin, s.HousesMax), Mathf.Max(s.HousesMin, s.HousesMax) + 1);
            float scale = s.BuildingScale;
            float radius = c.SettlementRadius;
            List<MapLabPlacement> houses = new List<MapLabPlacement>();

            for (int r = 0; r < data.Roads.Count && houses.Count < target; r++)
            {
                MapLabPolyline road = data.Roads[r];
                float sinceLast = rng.Range(0f, 4f);
                for (int i = 1; i < road.Points.Count - 1 && houses.Count < target; i++)
                {
                    Vector2 p = road.Points[i];
                    float dc = Vector2.Distance(p, c.SettlementCenter);
                    if (dc > radius)
                        continue;
                    sinceLast += Vector2.Distance(road.Points[i - 1], p);
                    float spacing = rng.Range(6.5f, 9.5f) * scale;
                    if (sinceLast < spacing)
                        continue;
                    sinceLast = 0f;
                    Vector2 tangent = (road.Points[i + 1] - road.Points[i - 1]).normalized;
                    Vector2 normal = MapLabGeometry.LeftNormal(tangent);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (houses.Count >= target || !rng.Chance(0.82f))
                            continue;
                        Vector2 size = new Vector2(rng.Range(3.8f, 5.4f), rng.Range(3.2f, 4.2f)) * scale;
                        bool gableToRoad = rng.Chance(0.4f);
                        float depth = gableToRoad ? size.x : size.y;
                        Vector2 center = p + normal * side * (road.Width * 0.5f + 1.6f * scale + depth * 0.5f) + tangent * rng.Signed() * 1.2f;
                        float yaw = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg + (gableToRoad ? 90f : 0f) + rng.Signed() * 6f;
                        TryAddHouse(data, houses, center, size, yaw, side, normal, ref rng, s, radius);
                    }
                }
            }
            for (int attempt = 0; attempt < 300 && houses.Count < Mathf.Max(s.HousesMin, Mathf.Min(target, s.HousesMin + 2)); attempt++)
            {
                Vector2 center = c.SettlementCenter + rng.InsideUnitCircle() * radius;
                Vector2 size = new Vector2(rng.Range(3.8f, 5.4f), rng.Range(3.2f, 4.2f)) * scale;
                float yaw = rng.Range(0f, 180f);
                Vector2 toCenter = (c.SettlementCenter - center).normalized;
                TryAddHouse(data, houses, center, size, yaw, 1, toCenter, ref rng, s, radius * 1.1f);
            }
            if (c.Pier)
                PlacePier(data, c.SettlementCenter);
        }

        private static void TryAddHouse(MapLabData data, List<MapLabPlacement> houses, Vector2 center, Vector2 size, float yaw, int side,
            Vector2 roadNormal, ref MapLabRandom rng, MapLabSettlementSettings s, float radius)
        {
            if (!BuildableSpot(data, center, Mathf.Max(size.x, size.y) * 0.6f, 14f))
                return;
            float footprint = Mathf.Max(size.x, size.y) * 0.5f;
            if (MinRoadDistance(data, center) < footprint * 0.8f + 0.8f)
                return;
            for (int i = 0; i < houses.Count; i++)
            {
                float minDistance = (Mathf.Max(houses[i].Size.x, houses[i].Size.y) * 0.5f + footprint) * 0.95f + 1.4f;
                if (Vector2.Distance(new Vector2(houses[i].Position.x, houses[i].Position.z), center) < minDistance)
                    return;
            }
            int variant = rng.Range(0, 4);
            // Этап 02: изба ставится в ту ориентацию, в которой нарисована её
            // картинка (рисунок нельзя крутить). Одинаково для геометрии и PNG.
            if (s.AlignToDrawnView)
                yaw = DrawnYaw(s, s.UseVariantYaw ? 10 + variant : variant % 4 < 2 ? 0 : 1, yaw);
            MapLabPlacement house = new MapLabPlacement
            {
                Kind = MapLabObjectKind.House,
                Position = new Vector3(center.x, data.SampleHeight(center.x, center.y), center.y),
                Yaw = yaw,
                Scale = 1f,
                Size = size,
                Variant = variant,
                Brightness = rng.Range(0.9f, 1.08f),
                Tone = rng.Value(),
            };
            houses.Add(house);
            data.Objects.Add(house);
            Vector2 back = -roadNormal * side;
            float yawRad = yaw * Mathf.Deg2Rad;
            Vector2 axisU = new Vector2(Mathf.Cos(yawRad), Mathf.Sin(yawRad));
            Vector2 axisV = MapLabGeometry.LeftNormal(axisU);
            Vector2 backV = Vector2.Dot(axisV, back) >= 0f ? axisV : -axisV;
            float hw = size.x * 0.5f, hd = size.y * 0.5f;
            float yardDepth = rng.Range(4.5f, 7f) * s.BuildingScale;
            if (rng.Chance(0.4f))
            {
                Vector2 barnSize = new Vector2(rng.Range(2.4f, 3.4f), rng.Range(2.2f, 2.8f)) * s.BuildingScale;
                float barnYaw = s.AlignToDrawnView ? DrawnYaw(s, 2, yaw) : yaw + rng.Signed() * 8f;
                // Амбар — в глубине двора, у одного из углов.
                Vector2 barnCenter = center + backV * (hd + barnSize.y * 0.5f + 1.4f) + axisU * rng.Signed() * Mathf.Max(0f, hw - barnSize.x * 0.3f);
                yardDepth = Mathf.Max(yardDepth, barnSize.y + 2.6f);
                if (BuildableSpot(data, barnCenter, 1.8f, 16f) && MinRoadDistance(data, barnCenter) > 2.5f)
                {
                    MapLabPlacement barn = new MapLabPlacement
                    {
                        Kind = MapLabObjectKind.Barn,
                        Position = new Vector3(barnCenter.x, data.SampleHeight(barnCenter.x, barnCenter.y), barnCenter.y),
                        Yaw = barnYaw,
                        Scale = 1f,
                        Size = barnSize,
                        Variant = rng.Range(0, 3),
                        Brightness = rng.Range(0.85f, 1f),
                    };
                    houses.Add(barn);
                    data.Objects.Add(barn);
                }
            }
            // Двор: огород в глубине (покраска) и ограда вокруг дома и двора с
            // воротами со стороны дороги; от ворот — короткая тропинка к дороге.
            float yardHalf = Mathf.Min(yardDepth * 0.5f, hw + 1.4f);
            Vector2 gardenCenter = center + backV * (hd + yardDepth * 0.55f);
            data.Yards.Add(new Vector4(gardenCenter.x, gardenCenter.y, yardHalf, Mathf.Atan2(backV.y, backV.x)));
            float rectV0 = -(hd + 1.3f), rectV1 = hd + yardDepth + 0.4f;
            data.YardRects.Add(new MapLabYardRect { Center = center + backV * ((rectV0 + rectV1) * 0.5f), AxisU = axisU, Half = new Vector2(hw + 1.6f, (rectV1 - rectV0) * 0.5f) });
            if (s.Fences && rng.Chance(0.7f))
            {
                float u0 = -(hw + 1.6f), u1 = hw + 1.6f;
                float v0 = -(hd + 1.3f), v1 = hd + yardDepth + 0.4f;
                Vector2 P(float u, float v) => center + axisU * u + backV * v;
                // Ворота на передней стороне, ближе к той половине, где дверь.
                float gate = rng.Signed() * hw * 0.4f;
                const float gateHalf = 0.95f;
                AddFence(data, P(u0, v0), P(gate - gateHalf, v0));
                AddFence(data, P(gate + gateHalf, v0), P(u1, v0));
                AddFence(data, P(u1, v0), P(u1, v1));
                AddFence(data, P(u1, v1), P(u0, v1));
                AddFence(data, P(u0, v1), P(u0, v0));
                Vector2 gatePoint = P(gate, v0);
                float best = float.MaxValue;
                Vector2 onRoad = gatePoint;
                for (int r = 0; r < data.Roads.Count; r++)
                {
                    float d = MapLabGeometry.Nearest(data.Roads[r].Points, gatePoint, out int segment, out float t, out _, out _);
                    if (d < best && segment + 1 < data.Roads[r].Points.Count)
                    {
                        best = d;
                        onRoad = Vector2.Lerp(data.Roads[r].Points[segment], data.Roads[r].Points[segment + 1], t);
                    }
                }
                if (best < 16f && best > 1.2f)
                {
                    MapLabPolyline path = new MapLabPolyline { Width = 1.1f, Kind = MapLabRoadKind.Path };
                    path.Points.Add(P(gate, v0 + 0.6f));
                    path.Points.Add(Vector2.Lerp(gatePoint, onRoad, 0.5f) + MapLabGeometry.LeftNormal((onRoad - gatePoint).normalized) * rng.Signed() * 0.5f);
                    path.Points.Add(onRoad);
                    data.YardPaths.Add(path);
                }
            }
        }

        // Ориентация конька, в которой нарисована картинка: 0 — изба с соломой,
        // 1 — изба с дранкой, 2 — амбар (углы этапа 02); 10 + вариант — изба
        // этапа 03 по варианту. Ось конька без направления: из двух вариантов
        // берётся ближайший к предложенному.
        public static float DrawnYaw(MapLabSettlementSettings s, int type, float suggested)
        {
            float drawn;
            if (type >= 10)
                drawn = s.DrawnRidgeYawVariants[Mathf.Clamp(type - 10, 0, 3)];
            else if (type == 2)
                drawn = s.UseVariantYaw ? s.DrawnBarnYaw : s.DrawnRidgeYaw.z;
            else
                drawn = type == 0 ? s.DrawnRidgeYaw.x : s.DrawnRidgeYaw.y;
            return Mathf.Abs(Mathf.DeltaAngle(suggested, drawn)) <= 90f ? drawn : drawn + 180f;
        }

        private static void AddFence(MapLabData data, Vector2 a, Vector2 b)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 1.5f));
            Vector2 prev = a;
            bool inside = Buildable(data, a);
            for (int i = 1; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                bool ok = Buildable(data, p) && MinRoadDistance(data, p) > 0.6f;
                if (ok && inside)
                    data.Fences.Add(new MapLabFence { A = prev, B = p });
                prev = p;
                inside = ok;
            }
        }

        private static bool Buildable(MapLabData data, Vector2 p)
        {
            return data.Contains(p.x, p.y) && !data.IsUnderWater(p.x, p.y, 0.4f) && data.SampleMask(data.Cliff, p.x, p.y) < 0.15f;
        }

        private static bool BuildableSpot(MapLabData data, Vector2 p, float radius, float maxSlope)
        {
            if (!data.Contains(p.x - radius, p.y - radius) || !data.Contains(p.x + radius, p.y + radius))
                return false;
            for (int k = 0; k < 5; k++)
            {
                Vector2 q = k == 0 ? p : p + new Vector2(k == 1 ? radius : k == 2 ? -radius : 0f, k == 3 ? radius : k == 4 ? -radius : 0f);
                if (data.IsUnderWater(q.x, q.y, 0.7f) || data.SampleMask(data.Shore, q.x, q.y) > 0.65f)
                    return false;
                if (data.SampleMask(data.Cliff, q.x, q.y) > 0.08f || data.SampleSlope(q.x, q.y) > maxSlope)
                    return false;
            }
            return true;
        }

        private static float MinRoadDistance(MapLabData data, Vector2 p)
        {
            float best = float.MaxValue;
            for (int r = 0; r < data.Roads.Count; r++)
            {
                float d = MapLabGeometry.Nearest(data.Roads[r].Points, p, out _, out _, out _, out _) - data.Roads[r].Width * 0.5f;
                best = Mathf.Min(best, d);
            }
            for (int b = 0; b < data.Bridges.Count; b++)
                best = Mathf.Min(best, MapLabGeometry.DistanceToSegment(p, data.Bridges[b].EndA, data.Bridges[b].EndB, out _) - data.Bridges[b].Width * 0.5f);
            return best;
        }

        private static void PlacePier(MapLabData data, Vector2 from)
        {
            if (data.Lakes.Count == 0)
                return;
            MapLabLake lake = data.Lakes[0];
            Vector2 dir = (lake.Center - from).normalized;
            Vector2 p = from;
            for (int i = 0; i < 200; i++)
            {
                p += dir * 0.5f;
                // Пристань — только в озере (не в вытекающей реке).
                int pi = data.Index(Mathf.Clamp(Mathf.RoundToInt(p.x / data.Cell), 0, data.Resolution - 1), Mathf.Clamp(Mathf.RoundToInt(p.y / data.Cell), 0, data.Resolution - 1));
                if (data.IsUnderWater(p.x, p.y, -0.05f) && data.Water[pi] == (byte)MapLabWaterKind.Lake)
                {
                    Vector2 shore = p - dir * 1.5f;
                    data.Piers.Add(new MapLabPier
                    {
                        Shore = shore,
                        Tip = p + dir * 7f,
                        DeckHeight = lake.Level + 0.5f,
                        Width = 1.6f,
                    });
                    return;
                }
            }
        }

        // ---------- Лес ----------

        // 12У-C: лес окружения — широкие массы неровной глубины: за игровой
        // областью плотно, внутрь заходит на разную глубину; в углах глубже
        // (расстояние сразу до двух сторон); местами луговые языки у кромки.
        // Исключения (дороги, вода, уступы, поля) применяются после — коридоры
        // путей и рек проходят сквозь лес.
        public static float FrameForest(Vector2 p, Rect play, float frame, MapLabNoise depth, MapLabNoise tongue)
        {
            float dx = Mathf.Max(play.xMin - p.x, p.x - play.xMax), dz = Mathf.Max(play.yMin - p.y, p.y - play.yMax);
            float signed = dx > 0f || dz > 0f ? new Vector2(Mathf.Max(0f, dx), Mathf.Max(0f, dz)).magnitude : Mathf.Max(dx, dz);
            float wobble = depth.Fbm(p.x / 70f, p.y / 70f, 2) * frame * 0.45f;
            float f = MapLabGeometry.SmoothStep(-frame * 0.35f, frame * 0.2f, signed + wobble);
            float t = MapLabGeometry.SmoothStep(0.58f, 0.72f, tongue.Fbm(p.x / 34f, p.y / 34f, 2) * 0.5f + 0.5f);
            return f * (1f - t * (1f - MapLabGeometry.SmoothStep(-frame * 0.1f, frame * 0.5f, signed)));
        }

        // Маска лесного массива: связный силуэт с выступами, заливами, полянами
        // и оторванными рощами; ядро плотнее опушки.
        private static void BuildForestMask(MapLabData data, MapLabSettings settings)
        {
            MapLabVegetationSettings v = settings.Vegetation;
            MapLabComposition c = data.Composition;
            MapLabNoise mass = new MapLabNoise(Stream(data, "forest-mass"));
            MapLabNoise edge = new MapLabNoise(Stream(data, "forest-edge"));
            MapLabNoise glade = new MapLabNoise(Stream(data, "forest-glade"));
            // 12У-C: природное окружение за игровой областью — свой поток.
            MapLabNoise frameDepth = new MapLabNoise(Stream(data, "frame-forest"));
            MapLabNoise frameTongue = new MapLabNoise(Stream(data, "frame-tongue"));
            Rect play = data.PlayRect;
            float frame = play.width > 0f ? play.xMin : 0f;
            int n = data.Resolution;
            float threshold = Mathf.Lerp(0.98f, 0.28f, v.ForestDensity);
            float warp = 0.4f + 1.4f * v.EdgeRoughness;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = data.Index(x, z);
                    Vector2 p = data.GridToWorld(x, z);
                    float f = mass.Warped(p.x / 85f, p.y / 85f, 4, warp) * 0.5f + 0.5f;
                    f += edge.Fbm(p.x / 16f, p.y / 16f, 2) * 0.12f * v.EdgeRoughness;
                    // 12У-D/F: лес плана — там, где у игры клетки «Лес» (кромка — шумом).
                    if (c != null && c.PlanForest != null)
                        f = c.SamplePlan(c.PlanForest, p) * 0.9f + edge.Fbm(p.x / 16f, p.y / 16f, 2) * 0.16f + (threshold - 0.45f);
                    if (c != null)
                    {
                        for (int k = 0; k < c.ForestZones.Count; k++)
                        {
                            MapLabComposition.Zone zone = c.ForestZones[k];
                            f += zone.Strength * (1f - MapLabGeometry.SmoothStep(zone.Radius * 0.3f, zone.Radius, Vector2.Distance(p, zone.Center)));
                        }
                        float toEdge = Mathf.Min(Mathf.Min(p.x, data.Size - p.x), Mathf.Min(p.y, data.Size - p.y));
                        f += c.MapEdgeForest * (1f - MapLabGeometry.SmoothStep(0f, data.Size * 0.16f, toEdge));
                    }
                    float forest = MapLabGeometry.SmoothStep(threshold - 0.06f, threshold + 0.06f, f) * MapLabGeometry.SmoothStep(0f, 0.08f, v.ForestDensity);
                    float g = glade.Fbm(p.x / 28f, p.y / 28f, 2);
                    forest *= 1f - MapLabGeometry.SmoothStep(0.32f, 0.5f, g) * 0.95f;
                    if (frame > 0f)
                        forest = Mathf.Max(forest, FrameForest(p, play, frame, frameDepth, frameTongue) * MapLabGeometry.SmoothStep(0f, 0.08f, v.ForestDensity));
                    // Исключения: дворы, поля, дорога, вода, стенки уступов, крутизна.
                    forest *= 1f - data.Settlement[i];
                    if (c != null)
                    {
                        foreach (MapLabComposition.Settlement st in c.Settlements)
                            forest *= MapLabGeometry.SmoothStep(st.Radius * 1.0f, st.Radius * 1.9f, Vector2.Distance(p, st.Center));
                    }
                    forest *= 1f - Mathf.Clamp01(data.Field[i] * 1.5f);
                    float road = data.RoadDistance != null ? data.RoadDistance[i] : float.MaxValue;
                    forest *= MapLabGeometry.SmoothStep(1.2f, 4.5f, road);
                    float wd = data.WaterDistance != null ? data.WaterDistance[i] : float.MaxValue;
                    forest *= MapLabGeometry.SmoothStep(0.8f, 3.5f, wd);
                    forest *= 1f - CliffBlock(data, data.CliffSigned != null ? data.CliffSigned[i] : float.MaxValue);
                    forest *= 1f - MapLabGeometry.SmoothStep(34f, 44f, data.SlopeDegrees[i]);
                    data.Forest[i] = Mathf.Clamp01(forest);
                }
            }
            // Поля и дворы раздвигают лес чуть шире, чем сами.
            float[] dilated = new float[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float m = 0f;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = Mathf.Clamp(x + dx, 0, n - 1);
                    int zz = Mathf.Clamp(z + dz, 0, n - 1);
                    m = Mathf.Max(m, data.Field[data.Index(xx, zz)]);
                }
                dilated[data.Index(x, z)] = m;
            }
            for (int i = 0; i < dilated.Length; i++)
                data.Forest[i] *= 1f - dilated[i];
        }

        // Запрет посадки у уступа: только полоса самой стенки и осыпи у подножия.
        // На площадке за кромкой лес подходит вплотную, как на эталоне.
        private static float CliffBlock(MapLabData data, float sd)
        {
            if (sd > 1e5f)
                return 0f;
            float w = data.CliffSharpness;
            return MapLabGeometry.SmoothStep(-3.4f * w, -2.5f * w, sd) * (1f - MapLabGeometry.SmoothStep(1.4f * w, 2f * w, sd));
        }

        private static float CliffBlockAt(MapLabData data, Vector2 p)
        {
            if (data.CliffSigned == null)
                return 0f;
            data.GridCoords(p.x, p.y, out int x0, out int z0, out float fx, out float fz);
            int ix = fx < 0.5f ? x0 : x0 + 1;
            int iz = fz < 0.5f ? z0 : z0 + 1;
            return CliffBlock(data, data.CliffSigned[data.Index(ix, iz)]);
        }

        private static bool CanPlant(MapLabData data, Vector2 p, float roadClearance)
        {
            if (!data.Contains(p.x, p.y))
                return false;
            if (data.IsUnderWater(p.x, p.y, 0.45f))
                return false;
            if (CliffBlockAt(data, p) > 0.5f || data.SampleSlope(p.x, p.y) > 42f)
                return false;
            if (data.RoadDistance != null && data.Bilinear(data.RoadDistance, p.x, p.y) < roadClearance)
                return false;
            for (int b = 0; b < data.Bridges.Count; b++)
                if (MapLabGeometry.DistanceToSegment(p, data.Bridges[b].EndA, data.Bridges[b].EndB, out _) < 4f)
                    return false;
            // Дворы и огороды свободны от леса и одиночных деревьев.
            for (int y = 0; y < data.YardRects.Count; y++)
                if (data.YardRects[y].Contains(p, 1.2f))
                    return false;
            for (int i = 0; i < data.Piers.Count; i++)
                if (MapLabGeometry.DistanceToSegment(p, data.Piers[i].Shore, data.Piers[i].Tip, out _) < 3f)
                    return false;
            return true;
        }

        private static bool NearBuilding(MapLabData data, Vector2 p, float clearance)
        {
            for (int i = 0; i < buildings.Count; i++)
            {
                MapLabPlacement o = buildings[i];
                float r = Mathf.Max(o.Size.x, o.Size.y) * 0.6f + clearance;
                if ((new Vector2(o.Position.x, o.Position.z) - p).sqrMagnitude < r * r)
                    return true;
            }
            return false;
        }

        private static void PlaceForest(MapLabData data, MapLabSettings settings)
        {
            MapLabVegetationSettings v = settings.Vegetation;
            if (v.ForestDensity <= 0.001f)
                return;
            MapLabRandom rng = new MapLabRandom(Stream(data, "forest"));
            MapLabNoise tone = new MapLabNoise(Stream(data, "forest-tone"));
            MapLabNoise conifer = new MapLabNoise(Stream(data, "forest-conifer"));
            float size = v.TreeSize;
            float spacing = size * 0.6f;
            int cells = Mathf.CeilToInt(data.Size / spacing);
            for (int gz = 0; gz < cells; gz++)
            {
                for (int gx = 0; gx < cells; gx++)
                {
                    Vector2 p = new Vector2((gx + 0.5f + rng.Signed() * 0.46f) * spacing, (gz + 0.5f + rng.Signed() * 0.46f) * spacing);
                    float jitterValue = rng.Value();
                    float variation = rng.Signed();
                    float toneJitter = rng.Value();
                    float brightness = rng.Value();
                    float groupRoll = rng.Value();
                    if (!data.Contains(p.x, p.y))
                        continue;
                    float m = data.SampleMask(data.Forest, p.x, p.y);
                    if (m < 0.04f || jitterValue > m * 1.25f)
                        continue;
                    if (!CanPlant(data, p, 1.4f) || NearBuilding(data, p, 2.5f))
                        continue;
                    float slope = data.SampleSlope(p.x, p.y);
                    float coniferScore = (conifer.Fbm(p.x / 45f, p.y / 45f, 2) * 0.5f + 0.5f) + slope / 90f * 0.6f + (data.SampleHeight(p.x, p.y) - data.MinHeight) / Mathf.Max(1f, data.MaxHeight - data.MinHeight) * 0.25f;
                    bool isConifer = coniferScore > 1.05f - v.ConiferShare * 0.9f;
                    float scale = size * (1f + v.SizeVariation * variation) * Mathf.Lerp(0.82f, 1.06f, m);
                    MapLabObjectKind kind = isConifer ? MapLabObjectKind.Conifer : MapLabObjectKind.Deciduous;
                    // Группа крон на одной карточке — только на пологом месте в ядре.
                    if (!isConifer && m > 0.9f && slope < 7f && groupRoll < 0.1f)
                    {
                        kind = MapLabObjectKind.DeciduousGroup;
                        scale *= 1.55f;
                    }
                    data.Objects.Add(new MapLabPlacement
                    {
                        Kind = kind,
                        Position = new Vector3(p.x, data.SampleHeight(p.x, p.y), p.y),
                        Scale = scale,
                        Variant = (int)(rng.NextUInt() & 0xFF),
                        Tone = Mathf.Clamp01(tone.Fbm(p.x / 38f, p.y / 38f, 2) * 0.7f + 0.5f + (toneJitter - 0.5f) * 0.35f),
                        Brightness = Mathf.Lerp(0.86f, 1.06f, brightness) * Mathf.Lerp(1.04f, 0.94f, m),
                    });
                }
            }
        }

        // Одиночные деревья на лугах, вдоль межей и дорог.
        private static void PlaceSingles(MapLabData data, MapLabSettings settings)
        {
            MapLabVegetationSettings v = settings.Vegetation;
            MapLabRandom rng = new MapLabRandom(Stream(data, "singles"));
            MapLabNoise tone = new MapLabNoise(Stream(data, "forest-tone"));
            float step = 10f;
            int cells = Mathf.CeilToInt(data.Size / step);
            for (int gz = 0; gz < cells; gz++)
            for (int gx = 0; gx < cells; gx++)
            {
                Vector2 p = new Vector2((gx + 0.5f + rng.Signed() * 0.45f) * step, (gz + 0.5f + rng.Signed() * 0.45f) * step);
                float roll = rng.Value();
                float scaleRoll = rng.Signed();
                int variant = (int)(rng.NextUInt() & 0xFF);
                if (!data.Contains(p.x, p.y) || roll > v.SingleTrees * 0.32f)
                    continue;
                if (data.SampleMask(data.Forest, p.x, p.y) > 0.05f || data.SampleMask(data.Field, p.x, p.y) > 0.2f)
                    continue;
                if (!CanPlant(data, p, 2f) || NearBuilding(data, p, 2.2f))
                    continue;
                AddTree(data, p, v.TreeSize * (0.95f + 0.2f * scaleRoll), variant, tone, rng.Value());
            }
            // Межи между наделами и обочины.
            if (data.Fields != null && data.Fields.Count > 0)
            {
                float hedge = 3.2f;
                int hc = Mathf.CeilToInt(data.Size / hedge);
                for (int gz = 0; gz < hc; gz++)
                for (int gx = 0; gx < hc; gx++)
                {
                    Vector2 p = new Vector2((gx + 0.5f) * hedge + rng.Signed() * 0.8f, (gz + 0.5f) * hedge + rng.Signed() * 0.8f);
                    float roll = rng.Value();
                    int variant = (int)(rng.NextUInt() & 0xFF);
                    if (roll > 0.3f * (0.4f + v.SingleTrees) || !data.Contains(p.x, p.y))
                        continue;
                    if (!data.Fields.Evaluate(p, out MapLabFields.Sample sample) || sample.BorderDistance > 1.1f)
                        continue;
                    if (data.SampleMask(data.Field, p.x, p.y) < 0.2f || !CanPlant(data, p, 1.5f) || NearBuilding(data, p, 2f))
                        continue;
                    if (rng.Chance(0.55f))
                        AddTree(data, p, v.TreeSize * rng.Range(0.75f, 1f), variant, tone, rng.Value());
                    else
                        AddBush(data, p, v.TreeSize * rng.Range(0.4f, 0.6f), variant, rng.Value());
                }
            }
            for (int r = 0; r < data.Roads.Count; r++)
            {
                MapLabPolyline road = data.Roads[r];
                for (int i = 2; i < road.Points.Count - 2; i += 7)
                {
                    float roll = rng.Value();
                    float sideRoll = rng.Value();
                    int variant = (int)(rng.NextUInt() & 0xFF);
                    if (roll > 0.14f * (0.4f + v.SingleTrees))
                        continue;
                    Vector2 t = (road.Points[i + 1] - road.Points[i - 1]).normalized;
                    Vector2 p = road.Points[i] + MapLabGeometry.LeftNormal(t) * (sideRoll < 0.5f ? -1f : 1f) * (road.Width * 0.5f + 2.6f);
                    if (data.SampleMask(data.Forest, p.x, p.y) > 0.2f || !CanPlant(data, p, 1.8f) || NearBuilding(data, p, 2.5f))
                        continue;
                    AddTree(data, p, v.TreeSize * rng.Range(0.8f, 1.05f), variant, tone, rng.Value());
                }
            }
        }

        private static void AddTree(MapLabData data, Vector2 p, float scale, int variant, MapLabNoise tone, float brightness)
        {
            data.Objects.Add(new MapLabPlacement
            {
                Kind = MapLabObjectKind.Deciduous,
                Position = new Vector3(p.x, data.SampleHeight(p.x, p.y), p.y),
                Scale = scale,
                Variant = variant,
                Tone = Mathf.Clamp01(tone.Fbm(p.x / 38f, p.y / 38f, 2) * 0.7f + 0.35f),
                Brightness = Mathf.Lerp(0.94f, 1.08f, brightness),
            });
        }

        private static void AddBush(MapLabData data, Vector2 p, float scale, int variant, float tone)
        {
            data.Objects.Add(new MapLabPlacement
            {
                Kind = MapLabObjectKind.Bush,
                Position = new Vector3(p.x, data.SampleHeight(p.x, p.y), p.y),
                Scale = scale,
                Variant = variant,
                Tone = tone,
                Brightness = 1f,
            });
        }

        private static void PlaceBushes(MapLabData data, MapLabSettings settings)
        {
            MapLabVegetationSettings v = settings.Vegetation;
            if (v.Bushes <= 0.001f)
                return;
            MapLabRandom rng = new MapLabRandom(Stream(data, "bushes"));
            float step = 4.5f;
            int cells = Mathf.CeilToInt(data.Size / step);
            for (int gz = 0; gz < cells; gz++)
            for (int gx = 0; gx < cells; gx++)
            {
                Vector2 p = new Vector2((gx + 0.5f + rng.Signed() * 0.45f) * step, (gz + 0.5f + rng.Signed() * 0.45f) * step);
                float roll = rng.Value();
                int variant = (int)(rng.NextUInt() & 0xFF);
                float scaleRoll = rng.Value();
                float tone = rng.Value();
                if (!data.Contains(p.x, p.y))
                    continue;
                float m = data.SampleMask(data.Forest, p.x, p.y);
                // Подлесок опушки, редкие кусты на лугу и у дворов.
                float chance = m > 0.06f && m < 0.5f ? 0.5f : (m <= 0.06f ? 0.035f : 0f);
                if (data.SampleMask(data.Settlement, p.x, p.y) > 0.3f)
                    chance = 0.12f;
                if (roll > chance * v.Bushes * 1.6f)
                    continue;
                if (data.SampleMask(data.Field, p.x, p.y) > 0.2f || !CanPlant(data, p, 1.2f) || NearBuilding(data, p, 1.6f))
                    continue;
                AddBush(data, p, v.TreeSize * Mathf.Lerp(0.38f, 0.6f, scaleRoll), variant, tone);
            }
        }

        // Валуны группами: осыпь у подножия уступов, каменистые берега, выходы в лесу.
        private static void PlaceRocks(MapLabData data, MapLabSettings settings)
        {
            MapLabRandom rng = new MapLabRandom(Stream(data, "rocks"));
            for (int k = 0; k < data.CliffLines.Count; k++)
            {
                MapLabCliffLine line = data.CliffLines[k];
                for (int i = 0; i < line.Columns.Count; i++)
                {
                    MapLabCliffColumn c = line.Columns[i];
                    if (!rng.Chance(0.32f))
                        continue;
                    int count = rng.Range(1, 4);
                    Vector2 basePoint = Vector2.Lerp(c.Start, c.End, rng.Value()) + c.Outward * (settings.Cliffs.CliffSharpness * 2.2f + rng.Range(0.3f, 2.5f));
                    for (int j = 0; j < count; j++)
                    {
                        Vector2 p = basePoint + rng.InsideUnitCircle() * 1.6f;
                        AddRock(data, p, rng.Range(0.45f, 1.2f) * Mathf.Clamp((c.Top - c.Bottom) / 6f, 0.6f, 1.4f), ref rng);
                    }
                }
            }
            float step = 3f;
            int cells = Mathf.CeilToInt(data.Size / step);
            MapLabNoise cluster = new MapLabNoise(Stream(data, "rocks-cluster"));
            for (int gz = 0; gz < cells; gz++)
            for (int gx = 0; gx < cells; gx++)
            {
                Vector2 p = new Vector2((gx + 0.5f + rng.Signed() * 0.45f) * step, (gz + 0.5f + rng.Signed() * 0.45f) * step);
                float roll = rng.Value();
                if (!data.Contains(p.x, p.y))
                    continue;
                float clusterValue = cluster.Fbm(p.x / 18f, p.y / 18f, 2);
                if (clusterValue < 0.04f)
                    continue;
                float shore = data.SampleMask(data.Shore, p.x, p.y);
                bool water = data.IsUnderWater(p.x, p.y, 0f);
                float chance = 0f;
                if (!water && shore > 0.45f)
                    chance = 0.3f;
                else if (water)
                {
                    float level = data.SampleWaterLevel(p.x, p.y);
                    float depth = level - data.SampleHeight(p.x, p.y);
                    if (depth < 0.45f)
                        chance = 0.16f;
                }
                else if (data.SampleMask(data.Forest, p.x, p.y) > 0.6f)
                    chance = 0.012f;
                if (roll > chance)
                    continue;
                if (data.RoadDistance != null && data.Bilinear(data.RoadDistance, p.x, p.y) < 1f)
                    continue;
                if (NearBuilding(data, p, 1.5f))
                    continue;
                AddRock(data, p, rng.Range(0.35f, 0.9f), ref rng);
            }
        }

        private static void AddRock(MapLabData data, Vector2 p, float scale, ref MapLabRandom rng)
        {
            if (!data.Contains(p.x, p.y))
                return;
            float h = data.SampleHeight(p.x, p.y);
            data.Objects.Add(new MapLabPlacement
            {
                Kind = MapLabObjectKind.Boulder,
                Position = new Vector3(p.x, h, p.y),
                Yaw = rng.Range(0f, 360f),
                Scale = scale,
                Variant = rng.Range(0, 6),
                Brightness = rng.Range(0.9f, 1.05f),
            });
        }

        // Камыш — редкими группами на мелководье пологого берега.
        private static void PlaceReeds(MapLabData data, MapLabSettings settings)
        {
            MapLabRandom rng = new MapLabRandom(Stream(data, "reeds"));
            MapLabNoise cluster = new MapLabNoise(Stream(data, "reeds-cluster"));
            float step = 1.5f;
            int cells = Mathf.CeilToInt(data.Size / step);
            for (int gz = 0; gz < cells; gz++)
            for (int gx = 0; gx < cells; gx++)
            {
                Vector2 p = new Vector2((gx + 0.5f + rng.Signed() * 0.45f) * step, (gz + 0.5f + rng.Signed() * 0.45f) * step);
                float roll = rng.Value();
                int variant = (int)(rng.NextUInt() & 0xFF);
                float scaleRoll = rng.Value();
                if (!data.Contains(p.x, p.y))
                    continue;
                float level = data.SampleWaterLevel(p.x, p.y);
                if (float.IsNaN(level))
                    continue;
                float depth = level - data.SampleHeight(p.x, p.y);
                if (depth < 0.02f || depth > 0.85f || data.SampleSlope(p.x, p.y) > 18f)
                    continue;
                // В реке — только у самого берега и реже.
                data.GridCoords(p.x, p.y, out int rx, out int rz, out float rfx, out float rfz);
                bool river = data.Water[data.Index(rfx < 0.5f ? rx : rx + 1, rfz < 0.5f ? rz : rz + 1)] == (byte)MapLabWaterKind.River;
                if (river && (depth > 0.3f || roll > 0.25f))
                    continue;
                if (cluster.Fbm(p.x / 14f, p.y / 14f, 2) < 0.02f || roll > 0.6f)
                    continue;
                data.Objects.Add(new MapLabPlacement
                {
                    Kind = MapLabObjectKind.Reed,
                    Position = new Vector3(p.x, level - 0.08f, p.y),
                    Scale = Mathf.Lerp(1.3f, 2f, scaleRoll),
                    Variant = variant,
                    Tone = rng.Value(),
                    Brightness = 1f,
                });
            }
        }

        private static void PlaceHaystacks(MapLabData data, MapLabSettings settings)
        {
            if (data.Fields == null || data.Fields.Count == 0 || data.Composition == null || data.Composition.Settlements.Count == 0)
                return;
            MapLabRandom rng = new MapLabRandom(Stream(data, "haystacks"));
            int target = rng.Range(3, 9);
            for (int attempt = 0; attempt < 400 && target > 0; attempt++)
            {
                MapLabComposition.Settlement st = data.Composition.Settlements.Count > 1 ? data.Composition.Settlements[rng.Range(0, data.Composition.Settlements.Count)] : data.Composition.Settlements[0];
                Vector2 p = st.Center + rng.InsideUnitCircle() * (st.FieldRadius > 0f ? st.FieldRadius : settings.RoadsAndFields.FieldRadius) * 0.75f;
                if (!data.Contains(p.x, p.y) || data.SampleMask(data.Field, p.x, p.y) < 0.95f)
                    continue;
                if (!data.Fields.Evaluate(p, out MapLabFields.Sample sample) || sample.BorderDistance < 3f)
                    continue;
                data.Objects.Add(new MapLabPlacement
                {
                    Kind = MapLabObjectKind.Haystack,
                    Position = new Vector3(p.x, data.SampleHeight(p.x, p.y), p.y),
                    Scale = rng.Range(0.9f, 1.2f) * settings.Settlement.BuildingScale,
                    Yaw = rng.Range(0f, 360f),
                    Brightness = rng.Range(0.92f, 1.04f),
                });
                target--;
            }
        }
    }
}
