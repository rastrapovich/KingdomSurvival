using System;
using System.Collections.Generic;

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
