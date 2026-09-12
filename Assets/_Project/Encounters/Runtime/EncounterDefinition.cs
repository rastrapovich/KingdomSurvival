using System;
using System.Collections.Generic;

namespace KingdomSurvival.Encounters
{
    // Авторская запись правил появления одного Encounter. НЕ содержит сам
    // текст сцены — тот живёт в DialogueDatabaseAsset под DialogueId.
    // Обычный POCO (не [SerializeField] private + property): должен строиться
    // напрямую в NUnit-тестах через object initializer, как NarrativeCondition
    // и остальные narrative-модели в Scripts/Core.
    [Serializable]
    public sealed class EncounterDefinition
    {
        // Identity. EncounterId — постоянный идентификатор сохранения, после
        // выхода в Production не переименовывается (§6).
        public string EncounterId = string.Empty;
        public string DisplayName = string.Empty;
        public string Description = string.Empty;
        public EncounterStatus Status = EncounterStatus.Draft;
        public EncounterCategory Category = EncounterCategory.Road;
        public List<string> Tags = new List<string>();

        // Content
        public string DialogueId = string.Empty;
        public EncounterResolutionMode ResolutionMode = EncounterResolutionMode.DialogueDriven;

        // Selection
        public EncounterSelectionMode SelectionMode = EncounterSelectionMode.Pool;
        public string PoolId = string.Empty;
        public int DiscoveryChancePercent;
        public int SelectionWeight = 1;
        public bool UnlimitedOccurrences;
        public int MaxOccurrencesPerGame = 1;
        public int CooldownHours;

        // Location
        public List<string> AllowedRegionIds = new List<string>();
        public List<string> RequiredLocationTags = new List<string>();
        public List<string> ForbiddenLocationTags = new List<string>();

        // Conditions — переиспользует существующий язык условий Narrative
        // Dialogue (Scripts/Core/NarrativeConditions.cs), вторая система
        // условий не создаётся.
        public NarrativeConditionGroup RequiredConditions = new NarrativeConditionGroup();
        public List<string> RequiredFlagsAll = new List<string>();
        public List<string> RequiredFlagsAny = new List<string>();
        public List<string> ForbiddenFlags = new List<string>();

        // Memory. Ставятся EncounterRuntimeService на соответствующем этапе
        // жизненного цикла, не Selector'ом.
        public List<string> FlagsSetOnStart = new List<string>();
        public List<string> FlagsSetOnComplete = new List<string>();
        public List<string> ClearFlagsOnComplete = new List<string>();

        // Documentation
        public string DesignerNotes = string.Empty;
        public string FutureHooksNotes = string.Empty;
    }
}
