using System.Collections.Generic;

namespace KingdomSurvival.Encounters
{
    // Канон v1.46 §25.4 / v1.48 §27.4: законченная встреча — уникально
    // пережитое содержание. Не каждый малый Encounter обязан давать опыт:
    // реакция и микро — без опыта. Разные исходы одной встречи дают
    // одинаковый опыт; источник «encounter.<ID>» — один раз на человека.
    // Числа — рабочие [РАБОЧЕЕ][KINGDOM SURVIVAL] (§27.10: кривая открыта).
    public static class EncounterExperience
    {
        public const string SourcePrefix = "encounter.";

        // Опыт по классу длительности — из общих правил «Базы развития».
        public static int ForDurationClass(EncounterDurationClass durationClass)
        {
            ProgressionRules rules = ProgressionRules.Current;
            switch (durationClass)
            {
                case EncounterDurationClass.Reaction: return rules.EncounterReactionExperience;
                case EncounterDurationClass.Micro: return rules.EncounterMicroExperience;
                case EncounterDurationClass.Short: return rules.EncounterShortExperience;
                case EncounterDurationClass.Standard: return rules.EncounterStandardExperience;
                case EncounterDurationClass.Complex: return rules.EncounterComplexExperience;
                case EncounterDurationClass.QuestSeed: return rules.EncounterQuestSeedExperience;
                default: return 0;
            }
        }

        // Вызывается, когда диалог встречи дошёл до конца.
        public static List<ExperienceGain> Award(GameState state, EncounterDefinition encounter)
        {
            if (state == null || encounter == null || string.IsNullOrWhiteSpace(encounter.EncounterId))
                return new List<ExperienceGain>();
            return CharacterProgressionService.AwardShared(
                state,
                SourcePrefix + encounter.EncounterId,
                ForDurationClass(encounter.DurationClass),
                CharacterProgressionService.PartyPersonIds(state));
        }

        public static EncounterDefinition FindByDialogueId(EncounterDatabaseAsset database, string dialogueId)
        {
            if (database == null || string.IsNullOrEmpty(dialogueId) || database.Encounters == null)
                return null;
            foreach (EncounterDefinition encounter in database.Encounters)
            {
                if (encounter != null && encounter.DialogueId == dialogueId)
                    return encounter;
            }
            return null;
        }
    }
}
