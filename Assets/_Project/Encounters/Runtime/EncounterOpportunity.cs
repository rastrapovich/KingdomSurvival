using System;
using System.Collections.Generic;

namespace KingdomSurvival.Encounters
{
    // Дискретный момент "сейчас теоретически может произойти Encounter"
    // (§21). OpportunityId должен быть стабильным между save/load — вызывающая
    // сторона отвечает за то, чтобы один и тот же игровой момент всегда
    // порождал один и тот же OpportunityId (§22).
    public sealed class EncounterOpportunity
    {
        public string OpportunityId = string.Empty;

        // Для SelectionMode.Pool.
        public string PoolId = string.Empty;

        // Для SelectionMode.Direct — запуск конкретного EncounterId в обход пула.
        public string DirectEncounterId = string.Empty;

        // Допущение MVP: "мировые часы" = Day * 24 (нет точной модели часа
        // дня на уровне GameState/ExpeditionIncidentSystem). Достаточно для
        // точности CooldownHours/MinimumHoursBetweenEncounters на уровне дня.
        public double WorldHour;

        public string RegionId = string.Empty;
        public List<string> LocationTags = new List<string>();
    }
}
