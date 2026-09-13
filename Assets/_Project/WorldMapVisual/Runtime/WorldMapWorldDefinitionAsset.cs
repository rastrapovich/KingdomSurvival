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

        [SerializeField] private List<TerrainAreaEntry> terrainAreas =
            new List<TerrainAreaEntry>();

        public string WorldDefinitionId => worldDefinitionId;
        public int GeographyVersion => geographyVersion;
        public float HomeXPercent => homeXPercent;
        public float HomeYPercent => homeYPercent;
        public IReadOnlyList<TerrainAreaEntry> TerrainAreas => terrainAreas;

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
                        MinXPercent = area.MinXPercent,
                        MaxXPercent = area.MaxXPercent,
                        MinYPercent = area.MinYPercent,
                        MaxYPercent = area.MaxYPercent,
                        Priority = area.Priority
                    });
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

        [System.Serializable]
        public sealed class TerrainAreaEntry
        {
            public string Id;
            public WorldMapTerrainType Terrain;
            public float MinXPercent;
            public float MaxXPercent;
            public float MinYPercent;
            public float MaxYPercent;
            public int Priority;
        }
    }
}
