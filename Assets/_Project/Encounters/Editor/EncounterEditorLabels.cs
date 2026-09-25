using System.Collections.Generic;
using System.Globalization;

namespace KingdomSurvival.Encounters.Editor
{
    // Русские подписи окна энкаунтеров и фраза «когда выпадает». Данные
    // встречи не меняются — только их представление автору.
    public static class EncounterEditorLabels
    {
        public static string Status(EncounterStatus status)
        {
            switch (status)
            {
                case EncounterStatus.Draft: return "Черновик";
                case EncounterStatus.Production: return "Готова";
                case EncounterStatus.Disabled: return "Отключена";
                case EncounterStatus.Deprecated: return "Списана";
                default: return status.ToString();
            }
        }

        public static string Category(EncounterCategory category)
        {
            switch (category)
            {
                case EncounterCategory.Road: return "Дорога";
                case EncounterCategory.Camp: return "Стоянка";
                case EncounterCategory.Location: return "Место";
                default: return category.ToString();
            }
        }

        public static string Duration(EncounterDurationClass duration)
        {
            switch (duration)
            {
                case EncounterDurationClass.Reaction: return "Реакция";
                case EncounterDurationClass.Micro: return "Мини-сцена";
                case EncounterDurationClass.Short: return "Короткая";
                case EncounterDurationClass.Standard: return "Обычная";
                case EncounterDurationClass.Complex: return "Сложная";
                case EncounterDurationClass.QuestSeed: return "Завязка квеста";
                default: return duration.ToString();
            }
        }

        public static string Memory(EncounterMemoryClass memory)
        {
            switch (memory)
            {
                case EncounterMemoryClass.None: return "Мир не помнит";
                case EncounterMemoryClass.Local: return "Помнит место";
                case EncounterMemoryClass.Persistent: return "Помнит навсегда";
                default: return memory.ToString();
            }
        }

        public static string Function(EncounterFunction function)
        {
            switch (function)
            {
                case EncounterFunction.Atmosphere: return "Атмосфера";
                case EncounterFunction.WorldTexture: return "Фактура мира";
                case EncounterFunction.ResourcePressure: return "Нехватка ресурсов";
                case EncounterFunction.Risk: return "Риск";
                case EncounterFunction.CompanionCharacterization: return "Раскрытие спутника";
                case EncounterFunction.Relationship: return "Отношения";
                case EncounterFunction.Knowledge: return "Знание";
                case EncounterFunction.Foreshadowing: return "Предвестие";
                case EncounterFunction.WorldState: return "Состояние мира";
                case EncounterFunction.QuestSeed: return "Завязка квеста";
                case EncounterFunction.MapDiscovery: return "Открытие на карте";
                default: return function.ToString();
            }
        }

        public static string SelectionMode(EncounterSelectionMode mode)
        {
            return mode == EncounterSelectionMode.Direct ? "Вызывается напрямую" : "Из пула";
        }

        public static string ConditionType(NarrativeConditionType type)
        {
            switch (type)
            {
                case NarrativeConditionType.FlagSet: return "Событие / решение уже было";
                case NarrativeConditionType.KnowledgeKnown: return "Герой это знает";
                case NarrativeConditionType.RelationAtLeast: return "Отношение не ниже";
                case NarrativeConditionType.RelationAtMost: return "Отношение не выше";
                case NarrativeConditionType.CompanionPresent: return "Спутник рядом";
                case NarrativeConditionType.ItemPresent: return "Предмет есть у героя";
                case NarrativeConditionType.QualityAtLeast: return "Качество не ниже";
                case NarrativeConditionType.CompetencyAtLeast: return "Навык не ниже";
                case NarrativeConditionType.CheckSucceeded: return "Проверка была успешной";
                case NarrativeConditionType.CheckFailed: return "Проверка была провалена";
                case NarrativeConditionType.CheckNotAttempted: return "Проверка ещё не выполнялась";
                case NarrativeConditionType.TraitPresent: return "У героя есть особенность";
                case NarrativeConditionType.PartySizeAtLeast: return "В отряде не меньше";
                case NarrativeConditionType.PartySizeAtMost: return "В отряде не больше";
                case NarrativeConditionType.EffectApplied: return "Эффект уже применён";
                default: return type.ToString();
            }
        }

        public static bool ConditionUsesInt(NarrativeConditionType type)
        {
            return type == NarrativeConditionType.RelationAtLeast || type == NarrativeConditionType.RelationAtMost ||
                   type == NarrativeConditionType.QualityAtLeast || type == NarrativeConditionType.CompetencyAtLeast ||
                   type == NarrativeConditionType.PartySizeAtLeast || type == NarrativeConditionType.PartySizeAtMost;
        }

        public static bool ConditionUsesString(NarrativeConditionType type)
        {
            return type != NarrativeConditionType.QualityAtLeast &&
                   type != NarrativeConditionType.PartySizeAtLeast && type != NarrativeConditionType.PartySizeAtMost;
        }

        public static string DescribeCondition(NarrativeCondition condition)
        {
            if (condition == null)
                return string.Empty;
            string not = condition.Negate ? "НЕ " : string.Empty;
            string number = condition.IntParam.ToString(CultureInfo.InvariantCulture);
            switch (condition.Type)
            {
                case NarrativeConditionType.QualityAtLeast:
                    return not + NarrativeQualityLabels.GetLabel(condition.QualityParam) + " ≥ " + number;
                case NarrativeConditionType.CompetencyAtLeast:
                    return not + NarrativeCompetencyLabels.GetLabel(condition.StringParam) + " ≥ " + number;
                case NarrativeConditionType.RelationAtLeast:
                    return not + "отношение «" + condition.StringParam + "» ≥ " + number;
                case NarrativeConditionType.RelationAtMost:
                    return not + "отношение «" + condition.StringParam + "» ≤ " + number;
                case NarrativeConditionType.PartySizeAtLeast:
                    return not + "в отряде ≥ " + number;
                case NarrativeConditionType.PartySizeAtMost:
                    return not + "в отряде ≤ " + number;
                default:
                    return not + ConditionType(condition.Type).ToLowerInvariant() + " «" + condition.StringParam + "»";
            }
        }

        // «В дороге, один раз за игру, шанс 90%. Только если: … Не выпадает, если: …»
        public static string DescribeWhen(EncounterDefinition encounter)
        {
            if (encounter == null)
                return string.Empty;

            List<string> parts = new List<string>();
            string place = encounter.Category == EncounterCategory.Camp ? "На стоянке"
                : encounter.Category == EncounterCategory.Location ? "На месте"
                : "В дороге";
            if (encounter.AllowedRegionIds != null && encounter.AllowedRegionIds.Count > 0)
                place += " (регионы: " + string.Join(", ", encounter.AllowedRegionIds) + ")";
            parts.Add(place);

            if (encounter.UnlimitedOccurrences)
                parts.Add("сколько угодно раз");
            else if (encounter.MaxOccurrencesPerGame <= 1)
                parts.Add("один раз за игру");
            else
                parts.Add("до " + encounter.MaxOccurrencesPerGame + " раз за игру");
            if (encounter.CooldownHours > 0 && (encounter.UnlimitedOccurrences || encounter.MaxOccurrencesPerGame > 1))
                parts.Add("не чаще раза в " + encounter.CooldownHours + " ч");

            parts.Add(encounter.SelectionMode == EncounterSelectionMode.Direct
                ? "вызывается напрямую"
                : "шанс " + encounter.DiscoveryChancePercent + "%");

            string text = string.Join(", ", parts) + ".";

            List<string> onlyIf = new List<string>();
            if (encounter.RequiredLocationTags != null && encounter.RequiredLocationTags.Count > 0)
                onlyIf.Add("место с приметами " + string.Join(", ", encounter.RequiredLocationTags));
            if (encounter.RequiredConditions?.Conditions != null)
            {
                foreach (NarrativeCondition condition in encounter.RequiredConditions.Conditions)
                    onlyIf.Add(DescribeCondition(condition));
            }
            if (encounter.RequiredFlagsAll != null && encounter.RequiredFlagsAll.Count > 0)
                onlyIf.Add("есть флаги " + string.Join(", ", encounter.RequiredFlagsAll));
            if (encounter.RequiredFlagsAny != null && encounter.RequiredFlagsAny.Count > 0)
                onlyIf.Add("есть хотя бы один из флагов " + string.Join(", ", encounter.RequiredFlagsAny));
            if (onlyIf.Count > 0)
                text += " Только если: " + string.Join("; ", onlyIf) + ".";

            List<string> notIf = new List<string>();
            if (encounter.ForbiddenLocationTags != null && encounter.ForbiddenLocationTags.Count > 0)
                notIf.Add("место с приметами " + string.Join(", ", encounter.ForbiddenLocationTags));
            if (encounter.ForbiddenFlags != null && encounter.ForbiddenFlags.Count > 0)
                notIf.Add("есть флаги " + string.Join(", ", encounter.ForbiddenFlags));
            if (notIf.Count > 0)
                text += " Не выпадает, если: " + string.Join("; ", notIf) + ".";

            return text;
        }

        // «Дорога · раз за игру · 90%» — строка под названием в списке.
        public static string DescribeListLine(EncounterDefinition encounter)
        {
            string occurrences = encounter.UnlimitedOccurrences ? "без ограничений"
                : encounter.MaxOccurrencesPerGame <= 1 ? "раз за игру"
                : "до " + encounter.MaxOccurrencesPerGame + " раз";
            string chance = encounter.SelectionMode == EncounterSelectionMode.Direct
                ? "напрямую"
                : encounter.DiscoveryChancePercent + "%";
            return Category(encounter.Category) + " · " + Duration(encounter.DurationClass).ToLowerInvariant() +
                   " · " + occurrences + " · " + chance;
        }
    }
}
