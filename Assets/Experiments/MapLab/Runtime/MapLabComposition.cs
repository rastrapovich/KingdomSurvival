using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Композиционный пресет: художественно направляемые контрольные точки.
    // Seed сдвигает их в допустимых пределах; географию «с нуля» он не изобретает.
    // Все координаты — в единицах мира (x — вправо по кадру, y — вглубь кадра = Z).
    public sealed class MapLabComposition
    {
        public sealed class Hill
        {
            public Vector2 Center;
            public Vector2 Radius;
            public float Height; // доля диапазона высот
        }

        public sealed class Cliff
        {
            public readonly List<Vector2> Control = new List<Vector2>();
            public float HeightScale = 1f;
            public float UpperSide = 1f; // +1 — верхняя площадка слева по ходу линии
            public float PlateauWidth = 40f;
        }

        public sealed class Lake
        {
            public Vector2 Center;
            public readonly List<Vector4> Lobes = new List<Vector4>();
            public float Depth = 2.2f;
            public readonly List<Vector3> Islands = new List<Vector3>(); // x, z, радиус
        }

        public sealed class River
        {
            public readonly List<Vector2> Control = new List<Vector2>();
            public float WidthScale = 1f;
            public int FromLake = -1;  // индекс озера-истока
            public int IntoLake = -1;  // индекс озера-устья
        }

        public sealed class Road
        {
            public readonly List<Vector2> Control = new List<Vector2>();
            public MapLabRoadKind Kind = MapLabRoadKind.Road;
        }

        public sealed class Zone
        {
            public Vector2 Center;
            public float Radius;
            public float Strength;
        }

        public readonly List<Hill> Hills = new List<Hill>();
        public readonly List<Cliff> Cliffs = new List<Cliff>();
        public readonly List<Lake> Lakes = new List<Lake>();
        public readonly List<River> Rivers = new List<River>();
        public readonly List<Road> Roads = new List<Road>();
        public readonly List<Zone> ForestZones = new List<Zone>();
        public readonly List<Zone> Flats = new List<Zone>();
        // Долины: линия, полуширина, глубина (доля диапазона).
        public readonly List<Vector2> ValleyLine = new List<Vector2>();
        public float ValleyHalfWidth;
        public float ValleyDepth;
        public Vector2 SettlementCenter;
        public float SettlementRadius;
        public bool HasSettlement;
        public bool Pier;
        public float LakeLevelFraction = 0.3f;
        public float MapEdgeForest; // сила рамки леса у краёв карты
        public Vector2 FlatStrokeDirection = new Vector2(1f, 0.3f);

        public static MapLabComposition Build(MapLabSettings settings, int seed)
        {
            MapLabRandom rng = MapLabRandom.ForStream(seed, "composition", MapLabData.AlgorithmVersion);
            float s = settings.General.MapSize;
            MapLabComposition c = new MapLabComposition();
            float jitter = 0.025f;

            Vector2 P(float u, float v)
            {
                return new Vector2((u + rng.Signed() * jitter) * s, (v + rng.Signed() * jitter) * s);
            }

            Vector2 Fixed(float u, float v)
            {
                return new Vector2(u * s, v * s);
            }

            switch (settings.General.Preset)
            {
                case MapLabPresetKind.HillAndLedge:
                {
                    c.Hills.Add(new Hill { Center = P(0.60f, 0.64f), Radius = new Vector2(0.30f, 0.26f) * s, Height = 0.95f });
                    c.Hills.Add(new Hill { Center = P(0.22f, 0.86f), Radius = new Vector2(0.26f, 0.2f) * s, Height = 0.45f });
                    c.Hills.Add(new Hill { Center = P(0.92f, 0.18f), Radius = new Vector2(0.22f, 0.2f) * s, Height = 0.3f });
                    Cliff ledge = new Cliff { HeightScale = 1f, UpperSide = 1f, PlateauWidth = 0.1f * s };
                    ledge.Control.Add(P(0.22f, 0.50f));
                    ledge.Control.Add(P(0.33f, 0.43f));
                    ledge.Control.Add(P(0.47f, 0.40f));
                    ledge.Control.Add(P(0.60f, 0.42f));
                    ledge.Control.Add(P(0.71f, 0.47f));
                    ledge.Control.Add(P(0.78f, 0.56f));
                    c.Cliffs.Add(ledge);
                    c.Flats.Add(new Zone { Center = P(0.55f, 0.62f), Radius = 0.13f * s, Strength = 0.8f });
                    Road road = new Road();
                    road.Control.Add(Fixed(-0.02f, 0.27f));
                    road.Control.Add(P(0.20f, 0.30f));
                    road.Control.Add(P(0.42f, 0.27f));
                    road.Control.Add(P(0.64f, 0.29f));
                    road.Control.Add(P(0.84f, 0.33f));
                    road.Control.Add(Fixed(1.02f, 0.40f));
                    c.Roads.Add(road);
                    Road path = new Road { Kind = MapLabRoadKind.Path };
                    path.Control.Add(P(0.84f, 0.33f));
                    path.Control.Add(P(0.90f, 0.50f));
                    path.Control.Add(P(0.80f, 0.66f));
                    path.Control.Add(P(0.62f, 0.70f));
                    c.Roads.Add(path);
                    c.ForestZones.Add(new Zone { Center = P(0.15f, 0.88f), Radius = 0.22f * s, Strength = 0.6f });
                    c.ForestZones.Add(new Zone { Center = P(0.92f, 0.85f), Radius = 0.18f * s, Strength = 0.5f });
                    c.ForestZones.Add(new Zone { Center = P(0.12f, 0.08f), Radius = 0.15f * s, Strength = 0.4f });
                    c.FlatStrokeDirection = new Vector2(1f, 0.15f);
                    break;
                }
                case MapLabPresetKind.ForestLake:
                {
                    c.Hills.Add(new Hill { Center = P(0.86f, 0.55f), Radius = new Vector2(0.24f, 0.5f) * s, Height = 0.75f });
                    c.Hills.Add(new Hill { Center = P(0.12f, 0.20f), Radius = new Vector2(0.25f, 0.22f) * s, Height = 0.45f });
                    c.Hills.Add(new Hill { Center = P(0.18f, 0.92f), Radius = new Vector2(0.3f, 0.18f) * s, Height = 0.4f });
                    Lake lake = new Lake { Center = P(0.40f, 0.60f), Depth = 2.4f };
                    float ls = settings.Water.LakeScale;
                    lake.Lobes.Add(new Vector4(lake.Center.x, lake.Center.y, 0.17f * s * ls, 0.12f * s * ls));
                    lake.Lobes.Add(new Vector4(lake.Center.x - 0.12f * s * ls, lake.Center.y + 0.06f * s * ls, 0.1f * s * ls, 0.08f * s * ls));
                    lake.Lobes.Add(new Vector4(lake.Center.x + 0.11f * s * ls, lake.Center.y - 0.07f * s * ls, 0.11f * s * ls, 0.08f * s * ls));
                    lake.Lobes.Add(new Vector4(lake.Center.x - 0.03f * s * ls + rng.Signed() * 6f, lake.Center.y - 0.09f * s * ls, 0.09f * s * ls, 0.06f * s * ls));
                    lake.Islands.Add(new Vector3(lake.Center.x - 0.04f * s + rng.Signed() * 5f, lake.Center.y + 0.05f * s, 0.035f * s));
                    c.Lakes.Add(lake);
                    c.LakeLevelFraction = 0.22f;
                    // Ярусы гряды идут по диагонали: стенки смотрят на юго-запад, к камере.
                    Cliff upper = new Cliff { HeightScale = 1f, UpperSide = 1f, PlateauWidth = 0.12f * s };
                    upper.Control.Add(P(0.47f, 1.03f));
                    upper.Control.Add(P(0.58f, 0.88f));
                    upper.Control.Add(P(0.69f, 0.74f));
                    upper.Control.Add(P(0.78f, 0.60f));
                    upper.Control.Add(P(0.88f, 0.46f));
                    upper.Control.Add(Fixed(1.03f, 0.36f));
                    c.Cliffs.Add(upper);
                    Cliff second = new Cliff { HeightScale = 0.8f, UpperSide = 1f, PlateauWidth = 0.12f * s };
                    second.Control.Add(P(0.64f, 1.03f));
                    second.Control.Add(P(0.74f, 0.91f));
                    second.Control.Add(P(0.84f, 0.78f));
                    second.Control.Add(P(0.93f, 0.66f));
                    second.Control.Add(Fixed(1.03f, 0.58f));
                    c.Cliffs.Add(second);
                    River outlet = new River { WidthScale = 0.75f, FromLake = 0 };
                    outlet.Control.Add(P(0.50f, 0.47f));
                    outlet.Control.Add(P(0.57f, 0.32f));
                    outlet.Control.Add(P(0.66f, 0.20f));
                    outlet.Control.Add(P(0.78f, 0.10f));
                    outlet.Control.Add(Fixed(0.92f, -0.03f));
                    c.Rivers.Add(outlet);
                    River inlet = new River { WidthScale = 0.6f, IntoLake = 0 };
                    inlet.Control.Add(Fixed(-0.03f, 0.95f));
                    inlet.Control.Add(P(0.08f, 0.86f));
                    inlet.Control.Add(P(0.17f, 0.76f));
                    inlet.Control.Add(P(0.26f, 0.68f));
                    c.Rivers.Add(inlet);
                    c.HasSettlement = true;
                    c.SettlementCenter = P(0.58f, 0.40f);
                    c.SettlementRadius = settings.Settlement.Radius;
                    c.Pier = true;
                    c.Flats.Add(new Zone { Center = c.SettlementCenter, Radius = settings.Settlement.Radius * 1.3f, Strength = 0.85f });
                    Road south = new Road { Kind = MapLabRoadKind.Path };
                    south.Control.Add(Fixed(-0.02f, 0.42f));
                    south.Control.Add(P(0.12f, 0.38f));
                    south.Control.Add(P(0.28f, 0.34f));
                    south.Control.Add(P(0.44f, 0.34f));
                    south.Control.Add(c.SettlementCenter + new Vector2(-4f, -6f));
                    south.Control.Add(P(0.64f, 0.30f));
                    south.Control.Add(P(0.74f, 0.24f));
                    south.Control.Add(P(0.88f, 0.16f));
                    south.Control.Add(Fixed(1.02f, 0.12f));
                    c.Roads.Add(south);
                    Road north = new Road { Kind = MapLabRoadKind.Path };
                    north.Control.Add(c.SettlementCenter + new Vector2(2f, 4f));
                    north.Control.Add(P(0.63f, 0.54f));
                    north.Control.Add(P(0.56f, 0.70f));
                    north.Control.Add(P(0.47f, 0.82f));
                    north.Control.Add(P(0.40f, 0.92f));
                    north.Control.Add(Fixed(0.36f, 1.02f));
                    c.Roads.Add(north);
                    Road west = new Road { Kind = MapLabRoadKind.Path };
                    west.Control.Add(P(0.12f, 0.38f));
                    west.Control.Add(P(0.09f, 0.54f));
                    west.Control.Add(P(0.14f, 0.66f));
                    c.Roads.Add(west);
                    c.ForestZones.Add(new Zone { Center = P(0.15f, 0.15f), Radius = 0.3f * s, Strength = 0.7f });
                    c.ForestZones.Add(new Zone { Center = P(0.2f, 0.95f), Radius = 0.25f * s, Strength = 0.5f });
                    c.ForestZones.Add(new Zone { Center = P(0.9f, 0.9f), Radius = 0.2f * s, Strength = 0.5f });
                    c.ForestZones.Add(new Zone { Center = P(0.42f, 0.10f), Radius = 0.2f * s, Strength = 0.5f });
                    c.MapEdgeForest = 0.35f;
                    c.FlatStrokeDirection = new Vector2(1f, -0.2f);
                    break;
                }
                default:
                {
                    c.Hills.Add(new Hill { Center = P(0.15f, 0.85f), Radius = new Vector2(0.3f, 0.25f) * s, Height = 0.8f });
                    c.Hills.Add(new Hill { Center = P(0.88f, 0.96f), Radius = new Vector2(0.3f, 0.2f) * s, Height = 0.95f });
                    c.Hills.Add(new Hill { Center = P(0.85f, 0.15f), Radius = new Vector2(0.3f, 0.2f) * s, Height = 0.7f });
                    c.Hills.Add(new Hill { Center = P(0.10f, 0.20f), Radius = new Vector2(0.25f, 0.2f) * s, Height = 0.55f });
                    // Река входит справа и течёт под скальным северным берегом,
                    // затем поворачивает к югу и уходит в нижний край.
                    River river = new River { WidthScale = 1f };
                    river.Control.Add(Fixed(1.03f, 0.80f));
                    river.Control.Add(P(0.88f, 0.74f));
                    river.Control.Add(P(0.74f, 0.66f));
                    river.Control.Add(P(0.63f, 0.54f));
                    river.Control.Add(P(0.57f, 0.38f));
                    river.Control.Add(P(0.47f, 0.22f));
                    river.Control.Add(P(0.36f, 0.10f));
                    river.Control.Add(Fixed(0.28f, -0.03f));
                    c.Rivers.Add(river);
                    for (int i = 0; i < river.Control.Count; i++)
                        c.ValleyLine.Add(river.Control[i]);
                    c.ValleyHalfWidth = 0.2f * s;
                    c.ValleyDepth = 0.55f;
                    Cliff gorge = new Cliff { HeightScale = 1f, UpperSide = -1f, PlateauWidth = 0.12f * s };
                    gorge.Control.Add(Fixed(1.03f, 0.90f));
                    gorge.Control.Add(P(0.92f, 0.85f));
                    gorge.Control.Add(P(0.82f, 0.80f));
                    gorge.Control.Add(P(0.73f, 0.77f));
                    c.Cliffs.Add(gorge);
                    Cliff outcrop = new Cliff { HeightScale = 0.8f, UpperSide = -1f, PlateauWidth = 0.08f * s };
                    outcrop.Control.Add(P(0.54f, 0.13f));
                    outcrop.Control.Add(P(0.60f, 0.10f));
                    outcrop.Control.Add(P(0.66f, 0.04f));
                    c.Cliffs.Add(outcrop);
                    c.HasSettlement = true;
                    c.SettlementCenter = P(0.38f, 0.52f);
                    c.SettlementRadius = settings.Settlement.Radius;
                    c.Flats.Add(new Zone { Center = c.SettlementCenter, Radius = settings.Settlement.Radius * 1.5f, Strength = 0.75f });
                    Road main = new Road();
                    main.Control.Add(Fixed(-0.02f, 0.30f));
                    main.Control.Add(P(0.14f, 0.36f));
                    main.Control.Add(P(0.27f, 0.44f));
                    main.Control.Add(c.SettlementCenter + new Vector2(0f, -3f));
                    main.Control.Add(P(0.50f, 0.50f));
                    main.Control.Add(P(0.62f, 0.47f));
                    main.Control.Add(P(0.74f, 0.50f));
                    main.Control.Add(P(0.86f, 0.58f));
                    main.Control.Add(Fixed(1.02f, 0.62f));
                    c.Roads.Add(main);
                    Road north = new Road();
                    north.Control.Add(c.SettlementCenter + new Vector2(-2f, 2f));
                    north.Control.Add(P(0.34f, 0.66f));
                    north.Control.Add(P(0.30f, 0.82f));
                    north.Control.Add(Fixed(0.27f, 1.02f));
                    c.Roads.Add(north);
                    Road south = new Road { Kind = MapLabRoadKind.Path };
                    south.Control.Add(c.SettlementCenter + new Vector2(-6f, -6f));
                    south.Control.Add(P(0.30f, 0.34f));
                    south.Control.Add(P(0.22f, 0.18f));
                    south.Control.Add(Fixed(0.16f, -0.02f));
                    c.Roads.Add(south);
                    Road east = new Road { Kind = MapLabRoadKind.Path };
                    east.Control.Add(P(0.74f, 0.50f));
                    east.Control.Add(P(0.80f, 0.36f));
                    east.Control.Add(P(0.86f, 0.20f));
                    east.Control.Add(Fixed(0.94f, -0.02f));
                    c.Roads.Add(east);
                    Road farm = new Road { Kind = MapLabRoadKind.Path };
                    farm.Control.Add(c.SettlementCenter + new Vector2(-3f, 1f));
                    farm.Control.Add(P(0.24f, 0.56f));
                    farm.Control.Add(P(0.12f, 0.62f));
                    farm.Control.Add(Fixed(-0.02f, 0.66f));
                    c.Roads.Add(farm);
                    c.ForestZones.Add(new Zone { Center = P(0.10f, 0.90f), Radius = 0.25f * s, Strength = 0.6f });
                    c.ForestZones.Add(new Zone { Center = P(0.90f, 0.20f), Radius = 0.25f * s, Strength = 0.6f });
                    c.ForestZones.Add(new Zone { Center = P(0.92f, 0.82f), Radius = 0.2f * s, Strength = 0.5f });
                    c.ForestZones.Add(new Zone { Center = P(0.08f, 0.08f), Radius = 0.18f * s, Strength = 0.5f });
                    c.ForestZones.Add(new Zone { Center = P(0.50f, 0.85f), Radius = 0.12f * s, Strength = 0.35f });
                    c.MapEdgeForest = 0.4f;
                    c.FlatStrokeDirection = new Vector2(1f, 0.35f);
                    break;
                }
            }
            return c;
        }
    }
}
