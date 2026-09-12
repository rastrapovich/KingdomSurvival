using System;

namespace KingdomSurvival.Encounters
{
    // Управляет тем, будет ли вообще Encounter на данной Opportunity, ДО
    // проверки отдельных Encounter (§12): защищает от ситуации "проверили
    // 30 энкаунтеров по 50% — почти всегда что-то да пройдёт".
    [Serializable]
    public sealed class EncounterPoolDefinition
    {
        public string PoolId = string.Empty;
        public string DisplayName = string.Empty;
        public bool Enabled = true;
        public int GlobalTriggerChancePercent = 100;
        public int MinimumHoursBetweenEncounters;

        // Event spam pacing для коротких Reactive Encounter (§125-127):
        // применяются Selector'ом ТОЛЬКО когда выбранный кандидат имеет
        // DurationClass.Reaction или .Micro — Complex/Standard/Short/QuestSeed
        // используют общий MinimumHoursBetweenEncounters выше и не считаются
        // в дневной лимит. 0 = не ограничено.
        public int MinimumHoursBetweenReactiveEncounters;
        public int MaxReactiveEncountersPerTravelDay;

        public string DesignerNotes = string.Empty;
    }
}
