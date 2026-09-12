using System.Collections.Generic;

namespace KingdomSurvival.Encounters
{
    // Диагностическая структура (§25): не только да/нет, но и ПОЧЕМУ.
    // Используется одинаково Runtime и будущим Editor Debug (Phase 2).
    public sealed class EncounterEligibilityResult
    {
        public bool Eligible;
        public List<string> BlockReasons = new List<string>();

        public static EncounterEligibilityResult Pass()
        {
            return new EncounterEligibilityResult { Eligible = true };
        }

        public static EncounterEligibilityResult Fail(string reason)
        {
            EncounterEligibilityResult result = new EncounterEligibilityResult { Eligible = false };
            if (!string.IsNullOrWhiteSpace(reason))
                result.BlockReasons.Add(reason);
            return result;
        }
    }
}
