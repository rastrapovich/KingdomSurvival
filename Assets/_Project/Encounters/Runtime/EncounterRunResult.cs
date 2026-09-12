using KingdomSurvival.DialogueDatabase;

namespace KingdomSurvival.Encounters
{
    public sealed class EncounterRunResult
    {
        public bool Started;
        public string EncounterId = string.Empty;
        public NarrativeDialogueView DialogueView;
        public string Error = string.Empty;

        public static EncounterRunResult Failed(string error)
        {
            return new EncounterRunResult { Started = false, Error = error ?? string.Empty };
        }

        public static EncounterRunResult Ok(string encounterId, NarrativeDialogueView view)
        {
            return new EncounterRunResult { Started = true, EncounterId = encounterId ?? string.Empty, DialogueView = view };
        }
    }
}
