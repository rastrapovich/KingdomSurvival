using System.Collections.Generic;

namespace KingdomSurvival.Encounters
{
    public sealed class EncounterSelectionResult
    {
        public bool HasSelection;
        public EncounterDefinition SelectedEncounter;
        public string PoolId = string.Empty;
        public List<string> SkippedEncounterIds = new List<string>();
        public string NoSelectionReason = string.Empty;

        public static EncounterSelectionResult None(string reason)
        {
            return new EncounterSelectionResult { HasSelection = false, NoSelectionReason = reason ?? string.Empty };
        }

        public static EncounterSelectionResult Selected(EncounterDefinition encounter, string poolId)
        {
            return new EncounterSelectionResult
            {
                HasSelection = true,
                SelectedEncounter = encounter,
                PoolId = poolId ?? string.Empty
            };
        }
    }
}
