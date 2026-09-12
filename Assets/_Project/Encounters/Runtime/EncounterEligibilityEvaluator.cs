using System;
using System.Collections.Generic;

namespace KingdomSurvival.Encounters
{
    // Чистая функция (§26, §27): не мутирует definition/context/runtimeState,
    // не бросает кубики. Собирает ВСЕ причины блокировки за один проход
    // (не fail-fast), чтобы Editor Debug (Phase 2) мог показать полный
    // список ✓/✗ как в §25 инструкции.
    public static class EncounterEligibilityEvaluator
    {
        public static EncounterEligibilityResult Evaluate(
            EncounterDefinition definition,
            NarrativeEvaluationContext narrativeContext,
            EncounterRuntimeStateData runtimeState,
            string regionId,
            IReadOnlyCollection<string> locationTags,
            double currentWorldHour)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (narrativeContext == null)
                throw new ArgumentNullException(nameof(narrativeContext));
            if (runtimeState == null)
                throw new ArgumentNullException(nameof(runtimeState));

            List<string> reasons = new List<string>();
            locationTags ??= Array.Empty<string>();

            if (definition.Status != EncounterStatus.Production)
                reasons.Add("Status = " + definition.Status + " (не Production).");

            if (definition.AllowedRegionIds != null && definition.AllowedRegionIds.Count > 0 &&
                !string.IsNullOrWhiteSpace(regionId) &&
                !definition.AllowedRegionIds.Contains(regionId))
            {
                reasons.Add("Регион " + regionId + " не входит в AllowedRegionIds.");
            }

            if (definition.RequiredLocationTags != null)
            {
                foreach (string requiredTag in definition.RequiredLocationTags)
                {
                    if (!ContainsTag(locationTags, requiredTag))
                        reasons.Add("Отсутствует обязательный тег локации: " + requiredTag + ".");
                }
            }

            if (definition.ForbiddenLocationTags != null)
            {
                foreach (string forbiddenTag in definition.ForbiddenLocationTags)
                {
                    if (ContainsTag(locationTags, forbiddenTag))
                        reasons.Add("Присутствует запрещённый тег локации: " + forbiddenTag + ".");
                }
            }

            EncounterRuntimeEntry entry = runtimeState.FindEntry(definition.EncounterId);

            if (!definition.UnlimitedOccurrences)
            {
                int timesStarted = entry?.TimesStarted ?? 0;
                if (timesStarted >= definition.MaxOccurrencesPerGame)
                {
                    reasons.Add("Лимит появлений исчерпан: " + timesStarted + "/" + definition.MaxOccurrencesPerGame + ".");
                }
            }

            if (definition.CooldownHours > 0 && entry != null && entry.LastStartedWorldHour >= 0)
            {
                double elapsed = currentWorldHour - entry.LastStartedWorldHour;
                if (elapsed < definition.CooldownHours)
                    reasons.Add("Cooldown не истёк: осталось " + (definition.CooldownHours - elapsed) + " ч.");
            }

            if (definition.RequiredFlagsAll != null)
            {
                foreach (string flagId in definition.RequiredFlagsAll)
                {
                    if (!narrativeContext.State.HasFlag(flagId))
                        reasons.Add("Отсутствует обязательный флаг: " + flagId + ".");
                }
            }

            if (definition.RequiredFlagsAny != null && definition.RequiredFlagsAny.Count > 0)
            {
                bool anyPresent = false;
                foreach (string flagId in definition.RequiredFlagsAny)
                {
                    if (narrativeContext.State.HasFlag(flagId))
                    {
                        anyPresent = true;
                        break;
                    }
                }

                if (!anyPresent)
                    reasons.Add("Ни один из RequiredFlagsAny не установлен.");
            }

            if (definition.ForbiddenFlags != null)
            {
                foreach (string flagId in definition.ForbiddenFlags)
                {
                    if (narrativeContext.State.HasFlag(flagId))
                        reasons.Add("Присутствует запрещённый флаг: " + flagId + ".");
                }
            }

            if (definition.RequiredConditions != null && !definition.RequiredConditions.Evaluate(narrativeContext))
                reasons.Add("Не выполнены RequiredConditions.");

            return reasons.Count == 0
                ? EncounterEligibilityResult.Pass()
                : new EncounterEligibilityResult { Eligible = false, BlockReasons = reasons };
        }

        private static bool ContainsTag(IReadOnlyCollection<string> tags, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || tags == null)
                return false;

            foreach (string candidate in tags)
            {
                if (string.Equals(candidate, tag, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
