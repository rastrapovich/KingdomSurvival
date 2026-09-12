using System;

namespace KingdomSurvival.Encounters
{
    // Справочник МЕТАДАННЫХ флага, не хранилище значений. Фактическое
    // состояние ("истинен ли флаг сейчас") остаётся в NarrativeStateData.Flags
    // (Scripts/Core/NarrativeState.cs) — этот реестр не дублирует его, а
    // документирует, что означает FlagId и зачем он создан (§28, §67).
    [Serializable]
    public sealed class EncounterFlagDefinition
    {
        public string FlagId = string.Empty;
        public string DisplayName = string.Empty;
        public string Description = string.Empty;
        public string Category = string.Empty;
        public EncounterFlagStatus Status = EncounterFlagStatus.Active;
        public string FutureUseNotes = string.Empty;
    }
}
