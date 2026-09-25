using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Одна строка вкладки «Локации» в World Map Database. Здесь рядом
    // хранятся стартовые игровые параметры и оформление маркера, чтобы для
    // добавления новой локации не приходилось править GameState и отдельную
    // таблицу иконок вручную.
    [Serializable]
    public sealed class WorldMapLocationDefinition
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = "Новая локация";
        [SerializeField, TextArea(2, 5)] private string interactionDescription = string.Empty;
        [SerializeField, TextArea(2, 5)] private string researchResultText = string.Empty;
        [SerializeField] private string threat = "неизвестна";
        [SerializeField, Min(0f)] private double explorationHours;
        [SerializeField, Min(0)] private int rewardArmyGold;
        [SerializeField, Min(0)] private int rewardArmySupply;
        [SerializeField] private bool initiallyDiscovered;
        [SerializeField] private bool initiallyVisibleOnMap = true;
        [SerializeField] private string spawnSlotId = string.Empty;

        // AM-07.5: набор тегов, которым должен обладать слот появления
        // (WorldMapWorldDefinitionAsset.SpawnSlotEntry.Tags), чтобы Anchored-
        // локация могла в него попасть. Пусто — старое поведение (любой слот
        // по round-robin), без изменений.
        [SerializeField] private List<string> requiredSlotTags = new List<string>();

        [SerializeField] private WorldMapPlacementMode mode = WorldMapPlacementMode.Anchored;
        [SerializeField] private float fixedXPercent = 50f;
        [SerializeField] private float fixedYPercent = 50f;
        [SerializeField] private Sprite icon;
        [SerializeField] private Color iconTint = Color.white;
        [SerializeField, Range(0.25f, 3f)] private float iconScale = 1f;

        public string Id => id;
        public string DisplayName => displayName;
        public string InteractionDescription => interactionDescription;
        public string Threat => threat;
        public double ExplorationHours => explorationHours;
        public int RewardArmyGold => rewardArmyGold;
        public int RewardArmySupply => rewardArmySupply;
        public bool InitiallyDiscovered => initiallyDiscovered;
        public bool InitiallyVisibleOnMap => initiallyVisibleOnMap;
        public string SpawnSlotId => spawnSlotId;
        public IReadOnlyList<string> RequiredSlotTags => requiredSlotTags;
        public WorldMapPlacementMode Mode => mode;
        public float FixedXPercent => fixedXPercent;
        public float FixedYPercent => fixedYPercent;
        public Sprite Icon => icon;
        public Color IconTint => iconTint;
        public float IconScale => iconScale;

        public WorldMapLocationTemplateData ToRuntimeTemplate()
        {
            return new WorldMapLocationTemplateData
            {
                Id = id,
                Name = displayName,
                InteractionDescription = interactionDescription,
                ResearchResultText = researchResultText,
                Threat = threat,
                ExplorationHours = explorationHours,
                RewardArmyGold = rewardArmyGold,
                RewardArmySupply = rewardArmySupply,
                InitiallyDiscovered = initiallyDiscovered,
                InitiallyVisibleOnMap = initiallyVisibleOnMap,
                SpawnSlotId = spawnSlotId,
                RequiredSlotTags = new List<string>(requiredSlotTags ?? new List<string>()),
                Mode = mode,
                FixedXPercent = fixedXPercent,
                FixedYPercent = fixedYPercent
            };
        }
    }
}
