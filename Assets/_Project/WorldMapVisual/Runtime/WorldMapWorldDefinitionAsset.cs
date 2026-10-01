using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace KingdomSurvival.WorldMapVisual
{
    // AM-01 (канон v1.33, §9.9): авторский постоянный мир — единственный
    // источник географии для новой партии. Asset хранит только сериализуемые
    // Unity-поля и преобразуется в чистый WorldMapDefinitionData через
    // ToData(); сам Core-класс не знает про ScriptableObject/UnityEngine.
    //
    // 12И (канон v1.50 §9.9): геймплейная разметка — шестиугольная сетка на
    // всё полотно, клетки размечаются кистью в Базе карты (режим «Местность»).
    // Прежние прямоугольные зоны и ломаные дороги читаются только ради
    // разового переноса в разметку (MigrateLegacyMarkup) и затем очищаются.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalWorldDefinition",
        menuName = "Kingdom Survival/Карта/World Definition")]
    public sealed class WorldMapWorldDefinitionAsset : ScriptableObject
    {
        [SerializeField] private string worldDefinitionId = "default-world";
        [SerializeField] private int geographyVersion = 1;

        [SerializeField] private string homeLocationId = "home";
        [SerializeField] private float homeXPercent = WorldMapNavigation.CapitalXPercent;
        [SerializeField] private float homeYPercent = WorldMapNavigation.CapitalYPercent;

        // Задача "Global Map Aspect" (WM-T04.9): полотно карты в пикселях.
        // Gameplay-координаты остаются 0..100; сетка и разметка считаются в
        // этих пикселях.
        public const float DefaultMapCanvasWidth = WorldMapHexGrid.DefaultCanvasWidth;
        public const float DefaultMapCanvasHeight = WorldMapHexGrid.DefaultCanvasHeight;

        [SerializeField] private float mapCanvasWidth = DefaultMapCanvasWidth;
        [SerializeField] private float mapCanvasHeight = DefaultMapCanvasHeight;

        // Размер сетки (клеток по ширине полотна) и разметка клеток в
        // записи WorldMapTerrainLayer.Encode.
        [SerializeField] private int hexesAcross = WorldMapHexGrid.DefaultHexesAcross;
        [SerializeField, TextArea] private string terrainCells = string.Empty;

        [SerializeField] private List<SpawnSlotEntry> spawnSlots =
            new List<SpawnSlotEntry>();

        // Устаревшая разметка до v1.50 — только для переноса.
        [SerializeField, HideInInspector, FormerlySerializedAs("terrainAreas")]
        private List<LegacyTerrainArea> legacyTerrainAreas = new List<LegacyTerrainArea>();
        [SerializeField, HideInInspector, FormerlySerializedAs("roads")]
        private List<LegacyRoad> legacyRoads = new List<LegacyRoad>();

        public string WorldDefinitionId => worldDefinitionId;
        public int GeographyVersion => geographyVersion;
        public float HomeXPercent => homeXPercent;
        public float HomeYPercent => homeYPercent;

        public float MapCanvasWidth => mapCanvasWidth > 1f ? mapCanvasWidth : DefaultMapCanvasWidth;
        public float MapCanvasHeight => mapCanvasHeight > 1f ? mapCanvasHeight : DefaultMapCanvasHeight;
        public int HexesAcross => WorldMapHexGrid.SanitizeHexesAcross(hexesAcross);
        public string TerrainCells => terrainCells ?? string.Empty;

        // Раздел 4/50 задачи "Global Map Aspect": защита от деления на ноль.
        public float GlobalMapAspect => MapCanvasWidth / MapCanvasHeight;

        public IReadOnlyList<SpawnSlotEntry> SpawnSlots => spawnSlots;
        public bool HasLegacyMarkup =>
            (legacyTerrainAreas != null && legacyTerrainAreas.Count > 0) ||
            (legacyRoads != null && legacyRoads.Count > 0);

        public WorldMapHexGrid CreateGrid() => new WorldMapHexGrid(MapCanvasWidth, MapCanvasHeight, HexesAcross);

        // Разметка для игры и редактора. Если перенос ещё не сделан, старые
        // зоны и дороги накладываются на лету — игра и предпросмотр совпадают.
        public WorldMapTerrainLayer BuildTerrainLayer()
        {
            WorldMapTerrainLayer layer = WorldMapTerrainLayer.Decode(CreateGrid(), terrainCells);
            if (HasLegacyMarkup && layer.IsEmpty)
                PaintLegacyMarkup(layer);
            return layer;
        }

        public WorldMapDefinitionData ToData()
        {
            WorldMapDefinitionData data = new WorldMapDefinitionData
            {
                WorldDefinitionId = worldDefinitionId,
                GeographyVersion = geographyVersion,
                HomeLocationId = homeLocationId,
                HomeXPercent = homeXPercent,
                HomeYPercent = homeYPercent,
                CanvasWidth = MapCanvasWidth,
                CanvasHeight = MapCanvasHeight,
                HexesAcross = HexesAcross,
                TerrainCells = BuildTerrainLayer().Encode()
            };

            if (spawnSlots != null)
            {
                foreach (SpawnSlotEntry slot in spawnSlots)
                {
                    if (slot == null || string.IsNullOrWhiteSpace(slot.Id))
                        continue;

                    data.SpawnSlots.Add(new WorldMapSpawnSlotDefinition
                    {
                        Id = slot.Id,
                        Tags = new List<string>(slot.Tags ?? new List<string>()),
                        MinXPercent = slot.MinXPercent,
                        MaxXPercent = slot.MaxXPercent,
                        MinYPercent = slot.MinYPercent,
                        MaxYPercent = slot.MaxYPercent
                    });
                }
            }

            return data;
        }

        // Авторские методы для заполнения ассета из редакторских инструментов
        // или разовых скриптов миграции. Не используются в рантайме игры.
        public void EditorSetWorldId(string id, int geographyVersion)
        {
            worldDefinitionId = id;
            this.geographyVersion = geographyVersion;
        }

        public void EditorSetHome(string locationId, float xPercent, float yPercent)
        {
            homeLocationId = locationId;
            homeXPercent = xPercent;
            homeYPercent = yPercent;
        }

        public void EditorSetTerrainLayer(WorldMapTerrainLayer layer)
        {
            if (layer == null)
                return;
            terrainCells = layer.IsEmpty ? string.Empty : layer.Encode();
        }

        // Смена размера клетки: разметка пересчитывается под новую сетку.
        public void EditorSetHexesAcross(int value)
        {
            int sanitized = WorldMapHexGrid.SanitizeHexesAcross(value);
            if (sanitized == HexesAcross)
                return;
            WorldMapTerrainLayer old = BuildTerrainLayer();
            hexesAcross = sanitized;
            EditorSetTerrainLayer(old.ResampleTo(CreateGrid()));
        }

        public void EditorAddSpawnSlot(SpawnSlotEntry slot)
        {
            spawnSlots.Add(slot);
        }

        public void EditorClearSpawnSlots() => spawnSlots.Clear();

        // Разовый перенос: старые зоны и дороги записываются в разметку
        // клеток (поверх уже нарисованного) и удаляются. True — что-то перенесено.
        public bool MigrateLegacyMarkup()
        {
            if (!HasLegacyMarkup)
                return false;
            WorldMapTerrainLayer layer = WorldMapTerrainLayer.Decode(CreateGrid(), terrainCells);
            PaintLegacyMarkup(layer);
            EditorSetTerrainLayer(layer);
            legacyTerrainAreas?.Clear();
            legacyRoads?.Clear();
            return true;
        }

        private void PaintLegacyMarkup(WorldMapTerrainLayer layer)
        {
            WorldMapHexGrid grid = layer.Grid;

            if (legacyTerrainAreas != null)
            {
                List<LegacyTerrainArea> ordered = new List<LegacyTerrainArea>();
                foreach (LegacyTerrainArea area in legacyTerrainAreas)
                {
                    if (area != null)
                        ordered.Add(area);
                }
                ordered.Sort((a, b) => a.Priority.CompareTo(b.Priority));

                for (int index = 0; index < grid.CellCount; index++)
                {
                    WorldMapHexCell cell = grid.CellAt(index);
                    grid.CellCenter(cell, out double x, out double y);
                    float xPercent = grid.PixelToPercentX(x);
                    float yPercent = grid.PixelToPercentY(y);
                    foreach (LegacyTerrainArea area in ordered)
                    {
                        if (xPercent >= area.MinXPercent && xPercent < area.MaxXPercent &&
                            yPercent >= area.MinYPercent && yPercent < area.MaxYPercent)
                        {
                            // Прежние классы: 0 — равнина, 1 — холмы, 2 — горы.
                            layer.Set(cell, area.Terrain == 2
                                ? WorldMapGameplayTerrainType.Mountains
                                : area.Terrain == 1
                                    ? WorldMapGameplayTerrainType.Hills
                                    : WorldMapGameplayTerrainType.OpenGround);
                        }
                    }
                }
            }

            if (legacyRoads != null)
            {
                foreach (LegacyRoad road in legacyRoads)
                {
                    if (road == null || !road.Enabled || road.Points == null)
                        continue;
                    for (int i = 1; i < road.Points.Count; i++)
                        PaintSegment(layer, road.Points[i - 1], road.Points[i], WorldMapGameplayTerrainType.Road);
                }
            }
        }

        private static void PaintSegment(WorldMapTerrainLayer layer, Vector2 fromPercent, Vector2 toPercent, WorldMapGameplayTerrainType terrain)
        {
            WorldMapHexGrid grid = layer.Grid;
            double ax = grid.PercentToPixelX(fromPercent.x);
            double ay = grid.PercentToPixelY(fromPercent.y);
            double bx = grid.PercentToPixelX(toPercent.x);
            double by = grid.PercentToPixelY(toPercent.y);
            double length = System.Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            int samples = System.Math.Max(1, (int)System.Math.Ceiling(length / (grid.HexRadius * 0.4)));
            for (int s = 0; s <= samples; s++)
            {
                double t = s / (double)samples;
                layer.Set(grid.CellAtPixel(ax + (bx - ax) * t, ay + (by - ay) * t), terrain);
            }
        }

        // AM-07.5: авторский слот появления малых локаций поверх готовой
        // карты, с тегами — заменяет захардкоженный WorldMapSpawnSlotRegistry
        // для миров, где художник уже разметил слоты (WorldMapPopulationService
        // использует его вместо registry, когда список непуст).
        [System.Serializable]
        public sealed class SpawnSlotEntry
        {
            public string Id;
            public List<string> Tags = new List<string>();
            public float MinXPercent;
            public float MaxXPercent;
            public float MinYPercent;
            public float MaxYPercent;
        }

        [System.Serializable]
        private sealed class LegacyTerrainArea
        {
            public string Id;
            public int Terrain;
            public float MinXPercent;
            public float MaxXPercent;
            public float MinYPercent;
            public float MaxYPercent;
            public int Priority;
        }

        [System.Serializable]
        private sealed class LegacyRoad
        {
            public string Id;
            public bool Enabled = true;
            public List<Vector2> Points = new List<Vector2>();
        }
    }
}
