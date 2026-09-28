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
        public int firstChoiceOptions = 3;
        public int choiceOptionsGrowth = 1;
        public int maxChoiceOptions = 35;
    }

    [Serializable]
    public sealed class QualityRecord
    {
        public HeroQuality quality;
        public string displayName = string.Empty;
        [TextArea(2, 4)] public string description = string.Empty;
    }

    [Serializable]
    public sealed class FeatureRequirementRecord
    {
        public FeatureRequirementKind kind;
        public string id = string.Empty;
        public int value = 1;
    }

    [Serializable]
    public sealed class FeatureRankRecord
    {
        public string name = string.Empty;
        [TextArea(2, 5)] public string effect = string.Empty;
    }

    // Карточка особенности, приёма или приказа (каталог §0.1).
    [Serializable]
    public sealed class TraitRecord
    {
        public string id = string.Empty;
        public string displayName = string.Empty;
        [TextArea(2, 6)] public string description = string.Empty;
        public string code = string.Empty;
        public FeatureLayer layer;
        public string group = string.Empty;
        public FeatureOwner owner;
        public FeatureKind kind;
        public bool combat;
        public FeatureStatus status;
        public FeatureImplementation implementation;
        public FeatureSource sources;
        public FeatureLimit limit;
        [TextArea(1, 4)] public string unlockText = string.Empty;
        public List<FeatureRequirementRecord> requirements = new List<FeatureRequirementRecord>();
        public bool requirementsAnyOf;
        public List<FeatureRankRecord> ranks = new List<FeatureRankRecord>();
        public string dependency = string.Empty;
        [TextArea(1, 3)] public string support = string.Empty;
        [TextArea(1, 3)] public string display = string.Empty;
        public List<string> excludesIds = new List<string>();
        public List<string> opensIds = new List<string>();
        [TextArea(1, 4)] public string mergedFrom = string.Empty;
        [TextArea(1, 4)] public string note = string.Empty;

        public TraitCatalogEntry ToEntry()
        {
            TraitCatalogEntry entry = new TraitCatalogEntry
            {
                Id = id,
                Name = displayName ?? string.Empty,
                Description = description ?? string.Empty,
                Code = code ?? string.Empty,
                Layer = layer,
                Group = group ?? string.Empty,
                Owner = owner,
                Kind = kind,
                Combat = combat,
                Status = status,
                Implementation = implementation,
                Sources = sources,
                Limit = limit,
                UnlockText = unlockText ?? string.Empty,
                RequirementsAnyOf = requirementsAnyOf,
                Dependency = dependency ?? string.Empty,
                Support = support ?? string.Empty,
                Display = display ?? string.Empty,
                MergedFrom = mergedFrom ?? string.Empty,
                Note = note ?? string.Empty
            };
            foreach (FeatureRequirementRecord requirement in requirements ?? new List<FeatureRequirementRecord>())
            {
                if (requirement != null && !string.IsNullOrWhiteSpace(requirement.id))
                    entry.Requirements.Add(new FeatureRequirement { Kind = requirement.kind, Id = requirement.id, Value = requirement.value });
            }
            foreach (FeatureRankRecord rank in ranks ?? new List<FeatureRankRecord>())
            {
                if (rank != null)
                    entry.Ranks.Add(new FeatureRank { Name = rank.name ?? string.Empty, Effect = rank.effect ?? string.Empty });
            }
            if (excludesIds != null)
                entry.ExcludesIds.AddRange(excludesIds.FindAll(value => !string.IsNullOrWhiteSpace(value)));
            if (opensIds != null)
                entry.OpensIds.AddRange(opensIds.FindAll(value => !string.IsNullOrWhiteSpace(value)));
            return entry;
        }

        public static TraitRecord FromEntry(TraitCatalogEntry entry)
        {
            TraitRecord record = new TraitRecord
            {
                id = entry.Id ?? string.Empty,
                displayName = entry.Name ?? string.Empty,
                description = entry.Description ?? string.Empty,
                code = entry.Code ?? string.Empty,
                layer = entry.Layer,
                group = entry.Group ?? string.Empty,
                owner = entry.Owner,
                kind = entry.Kind,
                combat = entry.Combat,
                status = entry.Status,
                implementation = entry.Implementation,
                sources = entry.Sources,
                limit = entry.Limit,
                unlockText = entry.UnlockText ?? string.Empty,
                requirementsAnyOf = entry.RequirementsAnyOf,
                dependency = entry.Dependency ?? string.Empty,
                support = entry.Support ?? string.Empty,
                display = entry.Display ?? string.Empty,
                mergedFrom = entry.MergedFrom ?? string.Empty,
                note = entry.Note ?? string.Empty
            };
            foreach (FeatureRequirement requirement in entry.Requirements)
                record.requirements.Add(new FeatureRequirementRecord { kind = requirement.Kind, id = requirement.Id, value = requirement.Value });
            foreach (FeatureRank rank in entry.Ranks)
                record.ranks.Add(new FeatureRankRecord { name = rank.Name, effect = rank.Effect });
            record.excludesIds.AddRange(entry.ExcludesIds);
            record.opensIds.AddRange(entry.OpensIds);
            return record;
        }
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
        // 2 — карточки особенностей, приёмов и приказов (каталог 27.09.2026);
        // 3 — статус «Заглушка» у заглушек движка (12Е-4);
        // 4 — первая партия особенностей вне боя активна (12Е-5);
        // 5 — первая партия боевых особенностей активна (12Е-6).
        public const int CurrentSchemaVersion = 5;

        public int schemaVersion = CurrentSchemaVersion;
        public ProgressionGlobalRecord rules = new ProgressionGlobalRecord();
        public List<ProgressionProfileRecord> profiles = new List<ProgressionProfileRecord>();
        public List<QualityRecord> qualities = new List<QualityRecord>();
        public List<TraitRecord> traits = new List<TraitRecord>();
        public List<CompetencyRecord> competencies = new List<CompetencyRecord>();

        public TraitRecord FindTrait(string id)
        {
            if (string.IsNullOrEmpty(id) || traits == null)
                return null;
            return traits.Find(trait => trait != null && trait.id == id);
        }

        // Схема 1 → 2: у прежних особенностей (knows_the_way, naturalist)
        // появляются поля карточки, недостающие записи каталога добавляются.
        // Название и описание, правленные в базе, сохраняются. True — база
        // изменилась.
        public bool UpgradeSchema()
        {
            if (schemaVersion >= CurrentSchemaVersion)
                return false;
            if (traits == null)
                traits = new List<TraitRecord>();
            if (schemaVersion < 2)
                UpgradeToCatalogCards();
            foreach (TraitRecord trait in traits)
            {
                if (trait != null && trait.status == FeatureStatus.Candidate && ProgressionFeatureImplementations.IsStub(trait.id))
                    trait.status = FeatureStatus.Stub;
            }
            // Схемы 4 и 5: у новых реализованных особенностей — «Активна».
            if (schemaVersion < 5)
                UpgradeToFirstBatch();
            schemaVersion = CurrentSchemaVersion;
            return true;
        }

        // Схема 4: у реализованных особенностей — статус «Активна», предел и
        // заметка из значений по умолчанию (если в базе их не задавали);
        // «Знаю, что искать» честно помечена как ждущая сведений с уверенностью.
        private void UpgradeToFirstBatch()
        {
            ProgressionCatalog defaults = ProgressionCatalog.CreateDefault();
            foreach (TraitRecord trait in traits)
            {
                TraitCatalogEntry source = trait != null ? defaults.FindTrait(trait.id) : null;
                if (source == null)
                    continue;
                if (ProgressionFeatureImplementations.IsImplemented(trait.id) && trait.status == FeatureStatus.Candidate)
                    trait.status = FeatureStatus.Active;
                if (ProgressionFeatureImplementations.IsImplemented(trait.id) && trait.limit == FeatureLimit.None)
                    trait.limit = source.Limit;
                if (string.IsNullOrWhiteSpace(trait.note))
                    trait.note = source.Note;
                if (trait.id == "znayu_chto_iskat" && trait.support == "есть (сведения со степенью уверенности)")
                {
                    trait.support = source.Support;
                    trait.dependency = source.Dependency;
                    trait.implementation = source.Implementation;
                }
            }
        }

        private void UpgradeToCatalogCards()
        {
            foreach (TraitCatalogEntry entry in ProgressionCatalog.CreateDefault().Traits)
            {
                TraitRecord existing = FindTrait(entry.Id);
                if (existing == null)
                {
                    traits.Add(TraitRecord.FromEntry(entry));
                    continue;
                }
                TraitRecord filled = TraitRecord.FromEntry(entry);
                if (!string.IsNullOrWhiteSpace(existing.displayName))
                    filled.displayName = existing.displayName;
                if (!string.IsNullOrWhiteSpace(existing.description))
                    filled.description = existing.description;
                traits[traits.IndexOf(existing)] = filled;
            }
        }

        // Добавляет записи каталога по умолчанию, которых нет в базе (по ID).
        // Существующие не трогает. Возвращает число добавленных.
        public int AddMissingTraits()
        {
            if (traits == null)
                traits = new List<TraitRecord>();
            int added = 0;
            foreach (TraitCatalogEntry entry in ProgressionCatalog.CreateDefault().Traits)
            {
                if (FindTrait(entry.Id) != null)
                    continue;
                traits.Add(TraitRecord.FromEntry(entry));
                added++;
            }
            return added;
        }

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
                MaxToughnessChoices = rules.maxToughnessChoices,
                FirstChoiceOptions = rules.firstChoiceOptions,
                ChoiceOptionsGrowth = rules.choiceOptionsGrowth,
                MaxChoiceOptions = rules.maxChoiceOptions
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
                    catalog.Traits.Add(trait.ToEntry());
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
                maxToughnessChoices = source.MaxToughnessChoices,
                firstChoiceOptions = source.FirstChoiceOptions,
                choiceOptionsGrowth = source.ChoiceOptionsGrowth,
                maxChoiceOptions = source.MaxChoiceOptions
            };

            profiles = new List<ProgressionProfileRecord>();
            foreach (ProgressionProfile profile in source.Profiles)
                profiles.Add(ToRecord(profile));

            qualities = new List<QualityRecord>();
            foreach (QualityCatalogEntry quality in catalog.Qualities)
                qualities.Add(new QualityRecord { quality = quality.Quality, displayName = quality.Name, description = quality.Description });
            traits = new List<TraitRecord>();
            foreach (TraitCatalogEntry trait in catalog.Traits)
                traits.Add(TraitRecord.FromEntry(trait));
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
            if (rules.firstChoiceOptions < 1)
                errors.Add("Показ выбора: первый показ — хотя бы 1 карточка.");
            if (rules.maxChoiceOptions < rules.firstChoiceOptions)
                errors.Add("Показ выбора: предел меньше первого показа.");
            if (rules.firstChoiceOptions != 3 || rules.choiceOptionsGrowth != 1 || rules.maxChoiceOptions != 35)
                warnings.Add("Показ выбора " + rules.firstChoiceOptions + " / +" + rules.choiceOptionsGrowth + " / до " + rules.maxChoiceOptions + " — решение автора (каталог §0.1): 3, +1, до 35.");

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
            ValidateTraits(traitIds, competencyIds, errors, warnings);
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

        private void ValidateTraits(HashSet<string> traitIds, HashSet<string> competencyIds, List<string> errors, List<string> warnings)
        {
            HashSet<string> codes = new HashSet<string>();
            foreach (TraitRecord trait in traits)
            {
                if (trait == null || string.IsNullOrWhiteSpace(trait.id))
                    continue;
                string name = (string.IsNullOrWhiteSpace(trait.code) ? string.Empty : trait.code + " ") +
                              "«" + (string.IsNullOrWhiteSpace(trait.displayName) ? trait.id : trait.displayName) + "»";
                if (string.IsNullOrWhiteSpace(trait.displayName))
                    warnings.Add(name + ": нет названия.");
                if (!string.IsNullOrWhiteSpace(trait.code) && trait.code != "—" && !codes.Add(trait.code))
                    warnings.Add(name + ": номер каталога повторяется.");
                if (trait.ranks == null || trait.ranks.Count == 0)
                    errors.Add(name + ": нет ни одного ранга (нужно от 1 до " + TraitCatalogEntry.MaxRanks + ").");
                else if (trait.ranks.Count > TraitCatalogEntry.MaxRanks)
                    errors.Add(name + ": рангов больше " + TraitCatalogEntry.MaxRanks + ".");
                foreach (FeatureRequirementRecord requirement in trait.requirements ?? new List<FeatureRequirementRecord>())
                {
                    if (requirement == null)
                        continue;
                    switch (requirement.kind)
                    {
                        case FeatureRequirementKind.Competency:
                            if (!competencyIds.Contains(requirement.id))
                                errors.Add(name + ": требование — неизвестная компетенция «" + requirement.id + "».");
                            else if (requirement.value < 1 || requirement.value > CharacterProgression.MaxCompetencyRank)
                                errors.Add(name + ": ступень компетенции в требовании вне 1–5.");
                            break;
                        case FeatureRequirementKind.Quality:
                            if (!Enum.TryParse(requirement.id, out HeroQuality _))
                                errors.Add(name + ": требование — неизвестное качество «" + requirement.id + "».");
                            break;
                        case FeatureRequirementKind.Feature:
                            if (!traitIds.Contains(requirement.id))
                                errors.Add(name + ": требование — неизвестная особенность «" + requirement.id + "».");
                            break;
                    }
                }
                foreach (string id in trait.excludesIds ?? new List<string>())
                {
                    if (!traitIds.Contains(id))
                        errors.Add(name + ": взаимоисключение с неизвестной записью «" + id + "».");
                }
                foreach (string id in trait.opensIds ?? new List<string>())
                {
                    TraitRecord opened = FindTrait(id);
                    if (opened == null)
                        errors.Add(name + ": открывает неизвестную запись «" + id + "».");
                    else if (opened.layer == FeatureLayer.Feature)
                        warnings.Add(name + ": открывает «" + id + "», но это не приём и не приказ.");
                }
                bool implemented = ProgressionFeatureImplementations.IsImplemented(trait.id);
                bool stub = ProgressionFeatureImplementations.IsStub(trait.id);
                if (trait.status == FeatureStatus.Stub && !stub)
                    warnings.Add(name + ": статус «Заглушка», но в движке заглушки нет.");
                else if (stub && trait.status != FeatureStatus.Stub)
                    warnings.Add(name + ": в движке это заглушка, а статус — «" + FeatureLabels.Status(trait.status) + "».");
                if (trait.status == FeatureStatus.Active && !implemented)
                    warnings.Add(name + ": статус «Активна», но в игре нет её кода — в выбор она не попадёт.");
                else if (implemented && trait.status != FeatureStatus.Active)
                    warnings.Add(name + ": код в игре есть, а статус — «" + FeatureLabels.Status(trait.status) + "».");
            }
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
