using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.ProgressionDatabase
{
    // «База развития»: всё про опыт и уровни — общие правила, карта развития
    // до 100-го уровня для Командира и каждого типа персонажа из Базы
    // существ (бойцы, противники, существа), перечни качеств, особенностей и
    // компетенций. Теги живут в Базе существ; окно базы редактирует их там.
    // В игре правила и перечни попадают в ядро через ProgressionDatabaseRuntime.

    [Serializable]
    public sealed class ProgressionLevelRecord
    {
        public int experienceToNext;
        public bool choice;
        public int hitPoints;
        public int attack;
        public int defense;
        public int damage;
        public int movement;
        public int initiative;
        public int battleExperience;
        public string note = string.Empty;
    }

    [Serializable]
    public sealed class CompetencyRankRecord
    {
        public string competencyId = string.Empty;
        public int rank = 1;
    }

    [Serializable]
    public sealed class ProgressionProfileRecord
    {
        public string id = string.Empty;
        public string displayName = string.Empty;
        public bool progresses = true;
        public int startingLevel = 1;
        public string meleeCompetencyId = NarrativeCompetencyIds.ChoppingWeapons;
        public string rangedCompetencyId = NarrativeCompetencyIds.Shooting;
        public List<CompetencyRankRecord> startingCompetencies = new List<CompetencyRankRecord>();
        public List<string> choiceCompetencies = new List<string>();
        public List<ProgressionLevelRecord> levels = new List<ProgressionLevelRecord>();
    }

    [Serializable]
    public sealed class ProgressionGlobalRecord
    {
        public int participationPercent = 60;
        public int retreatBankPercent = 50;
        public List<int> repeatBattlePercents = new List<int>();
        public int enemyHitPointWeight = 5;
        public int enemyStatWeight = 10;
        public int practicePerUse = 2;
        public List<int> repeatPracticePoints = new List<int>();
        public List<int> practiceToNextRank = new List<int>();
        public int practiceCeiling = 3;
        public int combatBonusFirstRank = 3;
        public int combatBonusSecondRank = 5;
        public int explorationExperience = 30;
        public int encounterReactionExperience;
        public int encounterMicroExperience;
        public int encounterShortExperience = 20;
        public int encounterStandardExperience = 40;
        public int encounterComplexExperience = 80;
        public int encounterQuestSeedExperience = 30;
        public int toughnessHitPoints = 2;
        public int maxToughnessChoices = 3;
    }

    [Serializable]
    public sealed class QualityRecord
    {
        public HeroQuality quality;
        public string displayName = string.Empty;
        [TextArea(2, 4)] public string description = string.Empty;
    }

    [Serializable]
    public sealed class TraitRecord
    {
        public string id = string.Empty;
        public string displayName = string.Empty;
        [TextArea(2, 4)] public string description = string.Empty;
    }

    [Serializable]
    public sealed class CompetencyRecord
    {
        public string id = string.Empty;
        public string displayName = string.Empty;
        [TextArea(2, 4)] public string description = string.Empty;
        public bool fighterCatalog;
    }

    [CreateAssetMenu(fileName = "KingdomSurvivalProgression", menuName = "Kingdom Survival/База развития")]
    public sealed class ProgressionDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "ProgressionDatabase/KingdomSurvivalProgression";
        public const string AssetPath = "Assets/_Project/ProgressionDatabase/Resources/ProgressionDatabase/KingdomSurvivalProgression.asset";
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public ProgressionGlobalRecord rules = new ProgressionGlobalRecord();
        public List<ProgressionProfileRecord> profiles = new List<ProgressionProfileRecord>();
        public List<QualityRecord> qualities = new List<QualityRecord>();
        public List<TraitRecord> traits = new List<TraitRecord>();
        public List<CompetencyRecord> competencies = new List<CompetencyRecord>();

        public ProgressionProfileRecord FindProfile(string id)
        {
            if (string.IsNullOrEmpty(id) || profiles == null)
                return null;
            foreach (ProgressionProfileRecord profile in profiles)
            {
                if (profile != null && profile.id == id)
                    return profile;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // База → ядро
        // ------------------------------------------------------------------

        public ProgressionRules ToRules()
        {
            ProgressionRules result = new ProgressionRules
            {
                ParticipationPercent = rules.participationPercent,
                RetreatBankPercent = rules.retreatBankPercent,
                RepeatBattlePercents = ToArray(rules.repeatBattlePercents, new[] { 100 }),
                EnemyHitPointWeight = rules.enemyHitPointWeight,
                EnemyStatWeight = rules.enemyStatWeight,
                PracticePerUse = rules.practicePerUse,
                RepeatPracticePoints = ToArray(rules.repeatPracticePoints, new int[0]),
                PracticeToNextRank = ToArray(rules.practiceToNextRank, new[] { 3, 6, 10, 15, 20 }),
                PracticeCeiling = rules.practiceCeiling,
                CombatBonusFirstRank = rules.combatBonusFirstRank,
                CombatBonusSecondRank = rules.combatBonusSecondRank,
                ExplorationExperience = rules.explorationExperience,
                EncounterReactionExperience = rules.encounterReactionExperience,
                EncounterMicroExperience = rules.encounterMicroExperience,
                EncounterShortExperience = rules.encounterShortExperience,
                EncounterStandardExperience = rules.encounterStandardExperience,
                EncounterComplexExperience = rules.encounterComplexExperience,
                EncounterQuestSeedExperience = rules.encounterQuestSeedExperience,
                ToughnessHitPoints = rules.toughnessHitPoints,
                MaxToughnessChoices = rules.maxToughnessChoices
            };

            foreach (ProgressionProfileRecord record in profiles)
            {
                if (record != null && !string.IsNullOrWhiteSpace(record.id))
                    result.SetProfile(ToProfile(record));
            }
            return result;
        }

        public static ProgressionProfile ToProfile(ProgressionProfileRecord record)
        {
            ProgressionProfile profile = new ProgressionProfile
            {
                Id = record.id,
                DisplayName = record.displayName ?? string.Empty,
                Progresses = record.progresses,
                StartingLevel = Mathf.Clamp(record.startingLevel, 1, ProgressionProfile.LevelCount),
                MeleeCompetencyId = record.meleeCompetencyId ?? string.Empty,
                RangedCompetencyId = record.rangedCompetencyId ?? string.Empty
            };

            if (record.startingCompetencies != null)
            {
                foreach (CompetencyRankRecord starting in record.startingCompetencies)
                {
                    if (starting != null && !string.IsNullOrWhiteSpace(starting.competencyId))
                        profile.StartingCompetencies.Add(new CompetencyRank { CompetencyId = starting.competencyId, Rank = starting.rank });
                }
            }
            if (record.choiceCompetencies != null)
            {
                foreach (string id in record.choiceCompetencies)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        profile.ChoiceCompetencies.Add(id);
                }
            }

            for (int i = 0; i < ProgressionProfile.LevelCount; i++)
            {
                ProgressionLevelRecord level = record.levels != null && i < record.levels.Count ? record.levels[i] : null;
                profile.Levels[i] = level == null
                    ? ProgressionProfile.DefaultLevel(i + 1)
                    : new ProgressionLevel
                    {
                        ExperienceToNext = i + 1 >= ProgressionProfile.LevelCount ? 0 : level.experienceToNext,
                        Choice = level.choice,
                        Bonus = new StatModifier
                        {
                            MaxHitPoints = level.hitPoints,
                            Attack = level.attack,
                            Defense = level.defense,
                            Damage = level.damage,
                            Movement = level.movement,
                            Initiative = level.initiative
                        },
                        BattleExperience = level.battleExperience,
                        Note = level.note ?? string.Empty
                    };
            }
            return profile;
        }

        public ProgressionCatalog ToCatalog()
        {
            ProgressionCatalog catalog = new ProgressionCatalog();
            foreach (QualityRecord quality in qualities)
            {
                if (quality != null)
                    catalog.Qualities.Add(new QualityCatalogEntry { Quality = quality.quality, Name = quality.displayName, Description = quality.description });
            }
            foreach (TraitRecord trait in traits)
            {
                if (trait != null && !string.IsNullOrWhiteSpace(trait.id))
                    catalog.Traits.Add(new TraitCatalogEntry { Id = trait.id, Name = trait.displayName, Description = trait.description });
            }
            foreach (CompetencyRecord competency in competencies)
            {
                if (competency != null && !string.IsNullOrWhiteSpace(competency.id))
                {
                    catalog.Competencies.Add(new CompetencyCatalogEntry
                    {
                        Id = competency.id,
                        Name = competency.displayName,
                        Description = competency.description,
                        FighterCatalog = competency.fighterCatalog
                    });
                }
            }
            return catalog;
        }

        // ------------------------------------------------------------------
        // Ядро → база (заполнение значениями по умолчанию)
        // ------------------------------------------------------------------

        public void FillFrom(ProgressionRules source, ProgressionCatalog catalog)
        {
            schemaVersion = CurrentSchemaVersion;
            rules = new ProgressionGlobalRecord
            {
                participationPercent = source.ParticipationPercent,
                retreatBankPercent = source.RetreatBankPercent,
                repeatBattlePercents = new List<int>(source.RepeatBattlePercents ?? new int[0]),
                enemyHitPointWeight = source.EnemyHitPointWeight,
                enemyStatWeight = source.EnemyStatWeight,
                practicePerUse = source.PracticePerUse,
                repeatPracticePoints = new List<int>(source.RepeatPracticePoints ?? new int[0]),
                practiceToNextRank = new List<int>(source.PracticeToNextRank ?? new int[0]),
                practiceCeiling = source.PracticeCeiling,
                combatBonusFirstRank = source.CombatBonusFirstRank,
                combatBonusSecondRank = source.CombatBonusSecondRank,
                explorationExperience = source.ExplorationExperience,
                encounterReactionExperience = source.EncounterReactionExperience,
                encounterMicroExperience = source.EncounterMicroExperience,
                encounterShortExperience = source.EncounterShortExperience,
                encounterStandardExperience = source.EncounterStandardExperience,
                encounterComplexExperience = source.EncounterComplexExperience,
                encounterQuestSeedExperience = source.EncounterQuestSeedExperience,
                toughnessHitPoints = source.ToughnessHitPoints,
                maxToughnessChoices = source.MaxToughnessChoices
            };

            profiles = new List<ProgressionProfileRecord>();
            foreach (ProgressionProfile profile in source.Profiles)
                profiles.Add(ToRecord(profile));

            qualities = new List<QualityRecord>();
            foreach (QualityCatalogEntry quality in catalog.Qualities)
                qualities.Add(new QualityRecord { quality = quality.Quality, displayName = quality.Name, description = quality.Description });
            traits = new List<TraitRecord>();
            foreach (TraitCatalogEntry trait in catalog.Traits)
                traits.Add(new TraitRecord { id = trait.Id, displayName = trait.Name, description = trait.Description });
            competencies = new List<CompetencyRecord>();
            foreach (CompetencyCatalogEntry competency in catalog.Competencies)
            {
                competencies.Add(new CompetencyRecord
                {
                    id = competency.Id,
                    displayName = competency.Name,
                    description = competency.Description,
                    fighterCatalog = competency.FighterCatalog
                });
            }
        }

        public static ProgressionProfileRecord ToRecord(ProgressionProfile profile)
        {
            ProgressionProfileRecord record = new ProgressionProfileRecord
            {
                id = profile.Id,
                displayName = profile.DisplayName ?? string.Empty,
                progresses = profile.Progresses,
                startingLevel = profile.StartingLevel,
                meleeCompetencyId = profile.MeleeCompetencyId ?? string.Empty,
                rangedCompetencyId = profile.RangedCompetencyId ?? string.Empty
            };
            foreach (CompetencyRank starting in profile.StartingCompetencies)
                record.startingCompetencies.Add(new CompetencyRankRecord { competencyId = starting.CompetencyId, rank = starting.Rank });
            record.choiceCompetencies.AddRange(profile.ChoiceCompetencies);
            for (int level = 1; level <= ProgressionProfile.LevelCount; level++)
            {
                ProgressionLevel source = profile.GetLevel(level);
                record.levels.Add(new ProgressionLevelRecord
                {
                    experienceToNext = source.ExperienceToNext,
                    choice = source.Choice,
                    hitPoints = source.Bonus.MaxHitPoints,
                    attack = source.Bonus.Attack,
                    defense = source.Bonus.Defense,
                    damage = source.Bonus.Damage,
                    movement = source.Bonus.Movement,
                    initiative = source.Bonus.Initiative,
                    battleExperience = source.BattleExperience,
                    note = source.Note ?? string.Empty
                });
            }
            return record;
        }

        // ------------------------------------------------------------------
        // Проверка: ошибки ломают расчёт, предупреждения — отклонения от
        // канона v1.48 §27 и подозрительные числа.
        // ------------------------------------------------------------------

        public void CollectValidationIssues(List<string> errors, List<string> warnings)
        {
            if (rules == null)
            {
                errors.Add("Нет общих правил.");
                return;
            }

            if (rules.participationPercent < 0 || rules.participationPercent > 100)
                errors.Add("Доля участия в банке боя должна быть от 0 до 100%.");
            else if (rules.participationPercent != 60)
                warnings.Add("Доля участия " + rules.participationPercent + "% — канон v1.48 §27.2 утверждает 60/40.");
            if (rules.practiceToNextRank == null || rules.practiceToNextRank.Count < CharacterProgression.MaxCompetencyRank)
                errors.Add("Практика до ступени: нужно " + CharacterProgression.MaxCompetencyRank + " значений (0→1 … 4→5).");
            else
            {
                for (int i = 0; i < rules.practiceToNextRank.Count; i++)
                {
                    if (rules.practiceToNextRank[i] <= 0)
                        errors.Add("Практика до ступени " + (i + 1) + " должна быть больше нуля.");
                }
            }
            if (rules.practiceCeiling < 1 || rules.practiceCeiling > CharacterProgression.MaxCompetencyRank)
                errors.Add("Потолок собственной практики должен быть от 1 до 5.");
            if (rules.repeatBattlePercents == null || rules.repeatBattlePercents.Count == 0)
                errors.Add("Повтор боя: нужен хотя бы один процент (первый бой).");

            HashSet<string> competencyIds = new HashSet<string>();
            foreach (CompetencyRecord competency in competencies)
            {
                if (competency == null || string.IsNullOrWhiteSpace(competency.id))
                    errors.Add("Компетенция без ID.");
                else if (!competencyIds.Add(competency.id))
                    errors.Add("Повторяется ID компетенции «" + competency.id + "».");
                else if (string.IsNullOrWhiteSpace(competency.displayName))
                    warnings.Add("У компетенции «" + competency.id + "» нет названия.");
            }

            HashSet<string> traitIds = new HashSet<string>();
            foreach (TraitRecord trait in traits)
            {
                if (trait == null || string.IsNullOrWhiteSpace(trait.id))
                    errors.Add("Особенность без ID.");
                else if (!traitIds.Add(trait.id))
                    errors.Add("Повторяется ID особенности «" + trait.id + "».");
            }
            foreach (string required in new[] { NarrativeTraitIds.KnowsTheWay, NarrativeTraitIds.Naturalist })
            {
                if (!traitIds.Contains(required))
                    warnings.Add("Особенность «" + required + "» используется игрой, но её нет в перечне.");
            }

            foreach (HeroQuality quality in Enum.GetValues(typeof(HeroQuality)))
            {
                if (qualities.Find(entry => entry != null && entry.quality == quality) == null)
                    errors.Add("В перечне нет качества " + quality + ".");
            }

            HashSet<string> profileIds = new HashSet<string>();
            foreach (ProgressionProfileRecord profile in profiles)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.id))
                {
                    errors.Add("Профиль развития без ID.");
                    continue;
                }
                if (!profileIds.Add(profile.id))
                {
                    errors.Add("Повторяется профиль «" + profile.id + "».");
                    continue;
                }
                ValidateProfile(profile, competencyIds, errors, warnings);
            }
            if (!profileIds.Contains(ProgressionRules.HeroProfileId))
                errors.Add("Нет профиля Командира («" + ProgressionRules.HeroProfileId + "»).");
        }

        private static void ValidateProfile(ProgressionProfileRecord profile, HashSet<string> competencyIds,
            List<string> errors, List<string> warnings)
        {
            string name = "«" + (string.IsNullOrWhiteSpace(profile.displayName) ? profile.id : profile.displayName) + "»";
            if (profile.levels == null || profile.levels.Count != ProgressionProfile.LevelCount)
            {
                errors.Add(name + ": в карте должно быть ровно " + ProgressionProfile.LevelCount + " уровней.");
                return;
            }

            bool canonChoices = true;
            for (int i = 0; i < ProgressionProfile.LevelCount - 1; i++)
            {
                ProgressionLevelRecord level = profile.levels[i];
                if (level == null || level.experienceToNext <= 0)
                    errors.Add(name + ", уровень " + (i + 1) + ": опыт до следующего должен быть больше нуля.");
            }
            for (int i = 0; i < ProgressionProfile.LevelCount; i++)
            {
                ProgressionLevelRecord level = profile.levels[i];
                if (level != null && level.choice != ((i + 1) % 3 == 0))
                    canonChoices = false;
            }
            if (profile.progresses && !canonChoices)
                warnings.Add(name + ": уровни выбора отличаются от канона v1.48 §27.1.1 (каждые 3 уровня).");

            if (profile.startingLevel < 1 || profile.startingLevel > ProgressionProfile.LevelCount)
                errors.Add(name + ": стартовый уровень вне 1–100.");

            foreach (CompetencyRankRecord starting in profile.startingCompetencies)
            {
                if (starting == null || !competencyIds.Contains(starting.competencyId))
                    errors.Add(name + ": стартовая компетенция «" + starting?.competencyId + "» не найдена в перечне.");
                else if (starting.rank < 0 || starting.rank > CharacterProgression.MaxCompetencyRank)
                    errors.Add(name + ": ступень стартовой компетенции «" + starting.competencyId + "» вне 0–5.");
            }
            foreach (string id in profile.choiceCompetencies)
            {
                if (!competencyIds.Contains(id))
                    errors.Add(name + ": компетенция выбора «" + id + "» не найдена в перечне.");
            }
            foreach (string id in new[] { profile.meleeCompetencyId, profile.rangedCompetencyId })
            {
                if (!string.IsNullOrEmpty(id) && !competencyIds.Contains(id))
                    errors.Add(name + ": оружейная компетенция «" + id + "» не найдена в перечне.");
            }
        }

        private static int[] ToArray(List<int> values, int[] fallback)
        {
            return values != null && values.Count > 0 ? values.ToArray() : fallback;
        }
    }
}
