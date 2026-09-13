using System;
using System.Collections.Generic;

// AM-04 (канон v1.33, §9.9): режим размещения локации. Fixed — точные
// координаты, не зависящие от WorldSeed (мельница/брод завязки не должны
// уплыть в другой регион). Anchored — прежнее поведение WM-07: точка
// выбирается случайно внутри авторской зоны-слота. Temporary — появляется/
// исчезает по условиям мира (зоны Encounter, AM-08) — такие локации пока не
// участвуют в начальном наполнении партии, это не регрессия, а честная
// граница текущего этапа.
public enum WorldMapPlacementMode
{
    Anchored,
    Fixed,
    Temporary
}

// Редактируемое описание локации до начала партии. Класс остаётся чистым
// C#-контрактом без UnityEngine, чтобы KingdomSurvival.Core по-прежнему
// собирался с noEngineReferences=true. WorldMapDatabaseAsset преобразует
// свои сериализованные записи в эти данные перед GameState.CreateNewGame.
[Serializable]
public sealed class WorldMapLocationTemplateData
{
    public string Id;
    public string Name;
    public string InteractionDescription;
    public string Threat;
    public double ExplorationHours;
    public int RewardArmyGold;
    public int RewardArmySupply;
    public bool InitiallyDiscovered;
    public bool InitiallyVisibleOnMap;
    public string SpawnSlotId;

    // AM-07.5: вместо (или в дополнение к) конкретному SpawnSlotId, Anchored-
    // локация может потребовать набор тегов ("Forest", "NearRoad" — см.
    // WorldMapSpawnSlotDefinition.Tags). Population-сервис выбирает случайный
    // слот только среди тех, что содержат ВСЕ перечисленные теги — локация
    // никогда не окажется в реке/на вершине/в поселении, если автор не
    // разрешил там слот с такими тегами. Пусто — старое поведение (round-robin
    // по всем слотам) без изменений.
    public List<string> RequiredSlotTags = new List<string>();

    public WorldMapPlacementMode Mode = WorldMapPlacementMode.Anchored;
    public float FixedXPercent;
    public float FixedYPercent;

    public LocationData CreateRuntimeLocation()
    {
        LocationData location = new LocationData(
            Id,
            Name,
            0.0,
            Threat,
            ExplorationHours,
            RewardArmyGold,
            RewardArmySupply);

        location.InteractionDescription = InteractionDescription ?? string.Empty;
        location.IsDiscovered = InitiallyDiscovered;
        location.IsVisibleOnMap = InitiallyVisibleOnMap;
        return location;
    }
}

// Fallback только для чистых Core-тестов и запуска GameState без Unity-
// ассетов. В реальной сцене PrototypeUIController передаёт записи из
// WorldMapDatabaseAsset, поэтому добавленные художником локации участвуют в
// новой игре без правки GameState.
public static class WorldMapLocationDefaults
{
    public static IReadOnlyList<WorldMapLocationTemplateData> Create()
    {
        return new List<WorldMapLocationTemplateData>
        {
            new WorldMapLocationTemplateData
            {
                Id = "ruins",
                Name = "Затопленные руины",
                Threat = "низкая",
                ExplorationHours = 2.0,
                RewardArmyGold = 100,
                RewardArmySupply = 200,
                InitiallyDiscovered = false,
                InitiallyVisibleOnMap = false
            },
            new WorldMapLocationTemplateData
            {
                Id = "mine",
                Name = "Старая шахта",
                Threat = "средняя",
                ExplorationHours = 5.0,
                RewardArmyGold = 300,
                InitiallyDiscovered = false,
                InitiallyVisibleOnMap = false
            },
            new WorldMapLocationTemplateData
            {
                Id = "forest",
                Name = "Чёрный лес",
                Threat = "высокая",
                InitiallyDiscovered = false,
                InitiallyVisibleOnMap = false
            }
        };
    }
}
