using System.Collections.Generic;
using KingdomSurvival.MapLab;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // 12У-D/F: план региона игры (GeneratedWorldMap генератора свободной игры) →
    // композиция MapLab. Один результат генерации даёт и разметку клеток игры
    // (навигация, туман, места), и подробный рисунок MapLab (канон §9.9.1):
    // реки, дороги и тропы — по линиям плана, озёра — по клеткам озёр, холмы —
    // по клеткам холмов и гор, лес и поля — сетками клеток плана, деревни — по
    // деревням плана, приметные места — знаками. Игровая область — холст плана
    // (прямоугольник) внутри квадратной карты MapLab с природным окружением.
    public static class MapLabRegionPlan
    {
        // Мировых единиц MapLab на одну клетку пути плана (ширина холста =
        // HexesAcross клеток). Деревня MapLab ~ 34 ед. — около четырёх клеток.
        public const float UnitsPerHex = 9f;

        public struct Mapping
        {
            public float Frame, PlayWidth, PlayHeight, OffsetZ;

            // Проценты холста (x вправо, y вниз) → мир MapLab (x вправо, z вглубь).
            public Vector2 ToWorld(float xPercent, float yPercent)
            {
                return new Vector2(Frame + xPercent / 100f * PlayWidth, OffsetZ + (1f - yPercent / 100f) * PlayHeight);
            }

            public Vector2 ToPercent(Vector2 world)
            {
                return new Vector2((world.x - Frame) / PlayWidth * 100f, (1f - (world.y - OffsetZ) / PlayHeight) * 100f);
            }
        }

        // Игровая ширина мира MapLab для плана (для General.MapSize до генерации).
        public static float PlayWidth(GeneratedWorldMap plan) => Mathf.Max(64f, plan.HexesAcross * UnitsPerHex);

        public static Mapping MappingFor(GeneratedWorldMap plan, MapLabSettings settings)
        {
            float playWidth = PlayWidth(plan);
            float playHeight = playWidth * plan.CanvasHeight / Mathf.Max(1f, plan.CanvasWidth);
            float frame = settings.FrameWidth;
            return new Mapping
            {
                Frame = frame,
                PlayWidth = playWidth,
                PlayHeight = playHeight,
                OffsetZ = frame + (playWidth - playHeight) * 0.5f,
            };
        }

        // Настроить профиль под план и собрать композицию (settings.RegionPlan).
        public static MapLabComposition Apply(GeneratedWorldMap plan, MapLabSettings settings)
        {
            settings.General.MapSize = PlayWidth(plan);
            settings.General.Seed = plan.Seed;
            MapLabComposition c = Build(plan, settings);
            settings.RegionPlan = c;
            return c;
        }

        public static MapLabComposition Build(GeneratedWorldMap plan, MapLabSettings settings)
        {
            Mapping m = MappingFor(plan, settings);
            WorldMapHexGrid grid = plan.CreateGrid();
            WorldMapTerrainLayer terrain = plan.CreateTerrainLayer();
            MapLabComposition c = new MapLabComposition
            {
                FromPlan = true,
                PlayRect = new Rect(m.Frame, m.OffsetZ, m.PlayWidth, m.PlayHeight),
                MapEdgeForest = 0f,
                LakeLevelFraction = 0.3f,
            };
            float hexWorld = m.PlayWidth / Mathf.Max(1, plan.HexesAcross);

            // Сетки леса и полей: регулярная сетка шагом в полклетки по холсту.
            int cols = Mathf.Max(2, plan.HexesAcross * 2 + 1);
            int rows = Mathf.Max(2, Mathf.RoundToInt(cols * m.PlayHeight / m.PlayWidth));
            c.PlanCols = cols;
            c.PlanRows = rows;
            c.PlanRect = new Rect(m.Frame, m.OffsetZ, m.PlayWidth, m.PlayHeight);
            c.PlanForest = new float[cols * rows];
            c.PlanField = new float[cols * rows];
            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < cols; x++)
                {
                    float xp = x / (float)(cols - 1) * 100f, yp = (1f - z / (float)(rows - 1)) * 100f;
                    WorldMapGameplayTerrainType t = terrain.GetAtPixel(grid.PercentToPixelX(xp), grid.PercentToPixelY(yp));
                    int i = z * cols + x;
                    c.PlanForest[i] = t == WorldMapGameplayTerrainType.Forest || t == WorldMapGameplayTerrainType.Swamp ? 1f : 0f;
                    c.PlanField[i] = t == WorldMapGameplayTerrainType.Field ? 1f : 0f;
                }
            }

            // Холмы и горы: по клетке — пологий холм; соседние сливаются в гряду.
            // Широкие и низкие: иначе каждая клетка — крутой бугорок, и уклон
            // красится камнем посреди луга (серые пятна); горы остаются круче.
            for (int i = 0; i < grid.CellCount; i++)
            {
                WorldMapGameplayTerrainType t = terrain.Get(grid.CellAt(i));
                if (t != WorldMapGameplayTerrainType.Hills && t != WorldMapGameplayTerrainType.Mountains)
                    continue;
                grid.CellCenter(grid.CellAt(i), out double px, out double py);
                Vector2 w = m.ToWorld(grid.PixelToPercentX(px), (float)(py / grid.CanvasHeight * 100.0));
                bool mountain = t == WorldMapGameplayTerrainType.Mountains;
                c.Hills.Add(new MapLabComposition.Hill { Center = w, Radius = Vector2.one * hexWorld * (mountain ? 1.9f : 2.0f), Height = mountain ? 0.5f : 0.17f });
            }

            // Озёра: связные клетки озёр — одно озеро из долей по клеткам.
            if (plan.LakeCells != null && plan.LakeCells.Count > 0)
            {
                HashSet<int> left = new HashSet<int>(plan.LakeCells);
                while (left.Count > 0)
                {
                    int seed = 0;
                    foreach (int any in left) { seed = any; break; }
                    List<int> cluster = new List<int>();
                    Queue<int> queue = new Queue<int>();
                    queue.Enqueue(seed);
                    left.Remove(seed);
                    while (queue.Count > 0)
                    {
                        int cell = queue.Dequeue();
                        cluster.Add(cell);
                        WorldMapHexCell hc = grid.CellAt(cell);
                        foreach (int other in new List<int>(left))
                        {
                            if (grid.CellAt(other).DistanceTo(hc) <= 1)
                            {
                                left.Remove(other);
                                queue.Enqueue(other);
                            }
                        }
                    }
                    MapLabComposition.Lake lake = new MapLabComposition.Lake { Depth = 2.2f };
                    Vector2 sum = Vector2.zero;
                    foreach (int cell in cluster)
                    {
                        grid.CellCenter(grid.CellAt(cell), out double px, out double py);
                        Vector2 w = m.ToWorld(grid.PixelToPercentX(px), (float)(py / grid.CanvasHeight * 100.0));
                        lake.Lobes.Add(new Vector4(w.x, w.y, hexWorld * 0.95f, hexWorld * 0.95f));
                        sum += w;
                    }
                    lake.Center = sum / cluster.Count;
                    c.Lakes.Add(lake);
                }
            }

            // Реки, дороги, тропы — по сглаженным линиям плана.
            float riverWidth = Mathf.Max(3f, settings.Water.RiverWidth);
            if (plan.Rivers != null)
            {
                foreach (GeneratedMapLine line in plan.Rivers)
                {
                    if (line?.Points == null || line.Points.Count < 2)
                        continue;
                    MapLabComposition.River river = new MapLabComposition.River
                    {
                        WidthScale = Mathf.Clamp((line.StartWidthHexes + line.EndWidthHexes) * 0.5f * hexWorld / riverWidth, 0.3f, 2.5f),
                    };
                    foreach (MapPointData p in line.Points)
                        river.Control.Add(m.ToWorld(p.XPercent, p.YPercent));
                    c.Rivers.Add(river);
                }
            }
            if (plan.Roads != null)
            {
                foreach (GeneratedMapLine line in plan.Roads)
                {
                    if (line?.Points == null || line.Points.Count < 2)
                        continue;
                    MapLabComposition.Road road = new MapLabComposition.Road
                    {
                        Kind = line.Kind == GeneratedMapLine.TrailKind ? MapLabRoadKind.Path : MapLabRoadKind.Road,
                    };
                    foreach (MapPointData p in line.Points)
                        road.Control.Add(m.ToWorld(p.XPercent, p.YPercent));
                    c.Roads.Add(road);
                }
            }

            // Деревни плана: размер плана 1–3 → малая / средняя / крупная.
            if (plan.Villages != null)
            {
                for (int v = 0; v < plan.Villages.Count; v++)
                {
                    GeneratedMapSite site = plan.Villages[v];
                    // Старт партии — лагерь у дороги (канон), а не поселение: в
                    // рисунке стоянка (шалаш и кострище) на расчищенной площадке,
                    // без домов, в которые нельзя войти.
                    if (site.IsStart)
                    {
                        Vector2 camp = m.ToWorld(site.XPercent, site.YPercent);
                        c.Reserved.Add(new MapLabComposition.Zone { Center = camp, Radius = hexWorld * 1.1f, Strength = 1f });
                        c.Flats.Add(new MapLabComposition.Zone { Center = camp, Radius = hexWorld * 1.6f, Strength = 0.6f });
                        c.Features.Add((camp + new Vector2(-hexWorld * 0.35f, hexWorld * 0.2f), "Camp_Tent_A", "start-camp"));
                        c.Features.Add((camp + new Vector2(hexWorld * 0.25f, -hexWorld * 0.1f), "Cold_Campfire_A", "start-camp"));
                        continue;
                    }
                    int size = Mathf.Clamp(site.Size, 1, 3);
                    float radius = Mathf.Max(settings.Settlement.Radius * (0.7f + 0.2f * size), hexWorld * 2.5f);
                    c.Settlements.Add(new MapLabComposition.Settlement
                    {
                        Center = m.ToWorld(site.XPercent, site.YPercent),
                        Radius = radius,
                        FieldRadius = radius + hexWorld * (1.5f + size * 1.0f),
                        Size = size,
                        // Тот же ID, что у места игры (GeneratedMapPlaces): стартовая деревня — не место.
                        Id = site.IsStart ? "start" : GeneratedMapPlaces.IdPrefix + GeneratedMapPlaces.VillageKind + "-" + v,
                        Family = site.Family ?? string.Empty,
                    });
                    c.Flats.Add(new MapLabComposition.Zone { Center = m.ToWorld(site.XPercent, site.YPercent), Radius = radius * 1.4f, Strength = 0.7f });
                }
            }
            if (c.Settlements.Count > 0)
            {
                c.HasSettlement = true;
                c.SettlementCenter = c.Settlements[0].Center;
                c.SettlementRadius = c.Settlements[0].Radius;
            }

            // Места и хозяйственные площадки: в рисунке — только расчищенная
            // площадка у тропы. Знак места показывает слой знаний игры, рисунок
            // не раскрывает неизвестное (гайд §6, дополнение о пространстве §6).
            if (plan.Landmarks != null)
            {
                foreach (GeneratedMapSite site in plan.Landmarks)
                {
                    Vector2 at = m.ToWorld(site.XPercent, site.YPercent);
                    c.Landmarks.Add((at, site.Kind, site.LocationId));
                    c.Reserved.Add(new MapLabComposition.Zone { Center = at, Radius = hexWorld * 0.75f, Strength = 1f });
                    if (!site.Hidden && !string.IsNullOrEmpty(site.Visual))
                        c.Features.Add((at, site.Visual, site.Kind));
                }
            }
            if (plan.Features != null)
            {
                foreach (GeneratedMapSite site in plan.Features)
                {
                    Vector2 at = m.ToWorld(site.XPercent, site.YPercent);
                    c.Reserved.Add(new MapLabComposition.Zone { Center = at, Radius = hexWorld * 0.6f, Strength = 1f });
                    if (!site.Hidden && !string.IsNullOrEmpty(site.Visual))
                        c.Features.Add((at, site.Visual, site.Kind));
                }
            }
            // Группы мира — в предпросмотре стоят в положении на 8 ч утра
            // первого дня, ракурс — по направлению движения (без зеркала).
            if (plan.Actors != null)
            {
                foreach (GeneratedMapActor actor in plan.Actors)
                {
                    if (string.IsNullOrEmpty(actor.FigureSet))
                        continue;
                    MapPointData at = WorldMapActorMotion.PositionAt(actor, 8.0, grid, out float dx, out float dy, out _);
                    string visual = actor.FigureSet + "_" + WorldMapActorMotion.Directions[WorldMapActorMotion.DirectionIndex(dx, dy)];
                    c.Features.Add((m.ToWorld(at.XPercent, at.YPercent), visual, "actor:" + actor.ActorId));
                }
            }
            if (plan.WorkSites != null)
                foreach (GeneratedMapSite site in plan.WorkSites)
                    c.Reserved.Add(new MapLabComposition.Zone { Center = m.ToWorld(site.XPercent, site.YPercent), Radius = hexWorld * 0.6f, Strength = 1f });
            return c;
        }
    }
}
