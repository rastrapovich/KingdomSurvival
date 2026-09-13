using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // AM-01 (канон v1.33, §9.9): авторский постоянный мир — единственный
    // источник географии для новой партии. Asset хранит только сериализуемые
    // Unity-поля и преобразуется в чистый WorldMapDefinitionData через
    // ToData(); сам Core-класс не знает про ScriptableObject/UnityEngine.
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

        // Задача "Global Map Aspect" (WM-T04.9): reference canvas, который
        // определяет ТОЛЬКО геометрические пропорции глобальной карты — не
        // требование к разрешению какой-либо Texture (раздел 2/49 задачи).
        // Gameplay-координаты остаются 0..100 независимо от этих чисел.
        // Старые ассеты без этих полей получают default через инициализатор
        // при десериализации — пересоздавать вручную не нужно (раздел 4).
        public const float DefaultMapCanvasWidth = 4160f;
        public const float DefaultMapCanvasHeight = 2560f;

        [SerializeField] private float mapCanvasWidth = DefaultMapCanvasWidth;
        [SerializeField] private float mapCanvasHeight = DefaultMapCanvasHeight;

        [SerializeField] private List<TerrainAreaEntry> terrainAreas =
            new List<TerrainAreaEntry>();
        [SerializeField] private List<SpawnSlotEntry> spawnSlots =
            new List<SpawnSlotEntry>();

        // Задача "gameplay-география дорог" (WM-T01/T02): настройки классов
        // gameplay-местности (Traversable/MovementMultiplier) и авторские
        // дороги — независимый слой от terrainAreas выше (тот отвечает
        // только за стоимость пути в WorldMapNavigation.FindPath).
        [SerializeField] private List<GameplayTerrainSettingsEntry> gameplayTerrainSettings =
            new List<GameplayTerrainSettingsEntry>();
        [SerializeField] private List<RoadEntry> roads = new List<RoadEntry>();

        public string WorldDefinitionId => worldDefinitionId;
        public int GeographyVersion => geographyVersion;
        public float HomeXPercent => homeXPercent;
        public float HomeYPercent => homeYPercent;

        public float MapCanvasWidth => mapCanvasWidth;
        public float MapCanvasHeight => mapCanvasHeight;

        // Раздел 4/50 задачи: защита от divide-by-zero и невалидных значений —
        // невалидный/нулевой/отрицательный размер откатывается на default,
        // а не роняет Preview или производит Infinity/NaN дальше по цепочке.
        public float GlobalMapAspect
        {
            get
            {
                float width = mapCanvasWidth > 0f ? mapCanvasWidth : DefaultMapCanvasWidth;
                float height = mapCanvasHeight > 0f ? mapCanvasHeight : DefaultMapCanvasHeight;
                return width / height;
            }
        }
        public IReadOnlyList<TerrainAreaEntry> TerrainAreas => terrainAreas;
        public IReadOnlyList<SpawnSlotEntry> SpawnSlots => spawnSlots;
        public IReadOnlyList<GameplayTerrainSettingsEntry> GameplayTerrainSettings => gameplayTerrainSettings;
        public IReadOnlyList<RoadEntry> Roads => roads;

        public WorldMapDefinitionData ToData()
        {
            WorldMapDefinitionData data = new WorldMapDefinitionData
            {
                WorldDefinitionId = worldDefinitionId,
                GeographyVersion = geographyVersion,
                HomeLocationId = homeLocationId,
                HomeXPercent = homeXPercent,
                HomeYPercent = homeYPercent
            };

            if (terrainAreas != null)
            {
                foreach (TerrainAreaEntry area in terrainAreas)
                {
                    if (area == null || string.IsNullOrWhiteSpace(area.Id))
                        continue;

                    data.TerrainAreas.Add(new WorldMapTerrainAreaData
                    {
                        Id = area.Id,
                        Terrain = area.Terrain,
                        Tags = new List<string>(area.Tags ?? new List<string>()),
                        MinXPercent = area.MinXPercent,
                        MaxXPercent = area.MaxXPercent,
                        MinYPercent = area.MinYPercent,
                        MaxYPercent = area.MaxYPercent,
                        Priority = area.Priority
                    });
                }
            }

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

            if (gameplayTerrainSettings != null)
            {
                foreach (GameplayTerrainSettingsEntry entry in gameplayTerrainSettings)
                {
                    if (entry == null)
                        continue;

                    data.GameplayTerrainSettings.Add(new WorldMapGameplayTerrainSettings
                    {
                        Terrain = entry.Terrain,
                        Traversable = entry.Traversable,
                        MovementMultiplier = entry.MovementMultiplier
                    });
                }
            }

            if (roads != null)
            {
                foreach (RoadEntry road in roads)
                {
                    if (road == null || string.IsNullOrWhiteSpace(road.Id))
                        continue;

                    WorldMapRoadDefinition roadData = new WorldMapRoadDefinition
                    {
                        Id = road.Id,
                        DisplayName = road.DisplayName,
                        Enabled = road.Enabled,
                        Width = road.Width
                    };

                    if (road.Points != null)
                    {
                        foreach (Vector2 point in road.Points)
                            roadData.Points.Add(new MapPointData(point.x, point.y));
                    }

                    data.Roads.Add(roadData);
                }
            }

            return data;
        }

        // Авторские методы для заполнения ассета из редакторских инструментов
        // (AM-03) или разовых скриптов миграции (AM-02). Не используются в
        // рантайме игры — только на этапе авторинга.
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

        public void EditorAddTerrainArea(TerrainAreaEntry area)
        {
            terrainAreas.Add(area);
        }

        public void EditorClearTerrainAreas() => terrainAreas.Clear();

        // Задача "Terrain Area Preview Authoring" (WM-T04.8): точечное
        // удаление одной зоны (создание/перемещение/resize/удаление мышью
        // в Preview) — в отличие от EditorClearTerrainAreas, которая
        // очищает всё сразу.
        public void EditorRemoveTerrainAreaAt(int index)
        {
            if (index >= 0 && index < terrainAreas.Count)
                terrainAreas.RemoveAt(index);
        }

        public void EditorAddSpawnSlot(SpawnSlotEntry slot)
        {
            spawnSlots.Add(slot);
        }

        public void EditorClearSpawnSlots() => spawnSlots.Clear();

        public void EditorAddRoad(RoadEntry road)
        {
            roads.Add(road);
        }

        public void EditorClearRoads() => roads.Clear();

        public void EditorEnsureDefaultGameplayTerrainSettings()
        {
            if (gameplayTerrainSettings.Count > 0)
                return;

            gameplayTerrainSettings.Add(new GameplayTerrainSettingsEntry
            { Terrain = WorldMapGameplayTerrainType.OpenGround, Traversable = true, MovementMultiplier = 1.00f });
            gameplayTerrainSettings.Add(new GameplayTerrainSettingsEntry
            { Terrain = WorldMapGameplayTerrainType.Road, Traversable = true, MovementMultiplier = 1.30f });
            gameplayTerrainSettings.Add(new GameplayTerrainSettingsEntry
            { Terrain = WorldMapGameplayTerrainType.Field, Traversable = true, MovementMultiplier = 0.90f });
            gameplayTerrainSettings.Add(new GameplayTerrainSettingsEntry
            { Terrain = WorldMapGameplayTerrainType.Forest, Traversable = true, MovementMultiplier = 0.70f });
            gameplayTerrainSettings.Add(new GameplayTerrainSettingsEntry
            { Terrain = WorldMapGameplayTerrainType.Water, Traversable = false, MovementMultiplier = 1.00f });
        }

        [System.Serializable]
        public sealed class TerrainAreaEntry
        {
            public string Id;
            public WorldMapTerrainType Terrain;

            // AM-07.5: чисто описательные теги ("Forest", "Shore", "Road" —
            // что нарисовано на арте в этой области), не влияют на стоимость
            // движения (это делает только Terrain).
            public List<string> Tags = new List<string>();

            public float MinXPercent;
            public float MaxXPercent;
            public float MinYPercent;
            public float MaxYPercent;
            public int Priority;
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
        public sealed class GameplayTerrainSettingsEntry
        {
            public WorldMapGameplayTerrainType Terrain;
            public bool Traversable = true;
            public float MovementMultiplier = 1f;
        }

        // WM-T02: дорога — упорядоченный путь точек в тех же процентных
        // координатах карты (0..100), что и всё остальное авторство, плюс
        // ширина gameplay-зоны в тех же единицах — НЕ в пикселях фоновой
        // текстуры (раздел 6 задачи). Не GameObject сцены — обычные
        // сериализуемые данные ассета.
        [System.Serializable]
        public sealed class RoadEntry
        {
            public string Id;
            public string DisplayName;
            public bool Enabled = true;

            // Ширина gameplay-зоны дороги в координатах карты (проценты
            // 0..100 по обеим осям) — не разрешение PNG фона.
            public float Width = 1f;

            public List<Vector2> Points = new List<Vector2>();
        }
    }
}
