using System;
using System.Collections.Generic;

namespace KingdomSurvival.Encounters
{
    // PURE (§23): не ставит флаги, не двигает время, не увеличивает
    // occurrence, не открывает UI, не мутирует GameState/database/runtimeState.
    // Только возвращает EncounterSelectionResult. Алгоритм — §24.
    public static class EncounterSelector
    {
        public static EncounterSelectionResult Select(
            EncounterOpportunity opportunity,
            EncounterDatabaseAsset database,
            NarrativeEvaluationContext narrativeContext,
            EncounterRuntimeStateData runtimeState)
        {
            if (opportunity == null)
                throw new ArgumentNullException(nameof(opportunity));
            if (database == null)
                throw new ArgumentNullException(nameof(database));
            if (narrativeContext == null)
                throw new ArgumentNullException(nameof(narrativeContext));
            if (runtimeState == null)
                throw new ArgumentNullException(nameof(runtimeState));

            if (!string.IsNullOrWhiteSpace(opportunity.DirectEncounterId))
                return SelectDirect(opportunity, database, narrativeContext, runtimeState);

            if (string.IsNullOrWhiteSpace(opportunity.PoolId))
                return EncounterSelectionResult.None("no_pool_id");

            EncounterPoolDefinition pool = database.FindPool(opportunity.PoolId);
            if (pool == null)
                return EncounterSelectionResult.None("pool_not_found");
            if (!pool.Enabled)
                return EncounterSelectionResult.None("pool_disabled");

            double lastTriggered = runtimeState.GetPoolLastTriggeredWorldHour(pool.PoolId);
            if (pool.MinimumHoursBetweenEncounters > 0 && lastTriggered >= 0 &&
                opportunity.WorldHour - lastTriggered < pool.MinimumHoursBetweenEncounters)
            {
                return EncounterSelectionResult.None("pool_cooldown");
            }

            bool poolTriggered = EncounterDeterministicRandom.RollPercent(
                narrativeContext.WorldSeed, opportunity.OpportunityId, pool.PoolId, "pool_trigger",
                pool.GlobalTriggerChancePercent);
            if (!poolTriggered)
                return EncounterSelectionResult.None("pool_trigger_failed");

            List<EncounterDefinition> poolEncounters = database.GetByPool(pool.PoolId);
            List<EncounterDefinition> eligible = new List<EncounterDefinition>();
            List<string> skipped = new List<string>();

            foreach (EncounterDefinition encounter in poolEncounters)
            {
                if (encounter.Status != EncounterStatus.Production)
                {
                    skipped.Add(encounter.EncounterId);
                    continue;
                }

                EncounterEligibilityResult eligibility = EncounterEligibilityEvaluator.Evaluate(
                    encounter, narrativeContext, runtimeState,
                    opportunity.RegionId, opportunity.LocationTags, opportunity.WorldHour);

                if (eligibility.Eligible && !IsReactivePacingBlocked(pool, encounter, opportunity, runtimeState))
                    eligible.Add(encounter);
                else
                    skipped.Add(encounter.EncounterId);
            }

            List<EncounterDefinition> discovered = new List<EncounterDefinition>();
            foreach (EncounterDefinition encounter in eligible)
            {
                bool discoveredThis = EncounterDeterministicRandom.RollPercent(
                    narrativeContext.WorldSeed, opportunity.OpportunityId, encounter.EncounterId, "discovery",
                    encounter.DiscoveryChancePercent);

                if (discoveredThis)
                    discovered.Add(encounter);
                else
                    skipped.Add(encounter.EncounterId);
            }

            if (discovered.Count == 0)
            {
                return new EncounterSelectionResult
                {
                    HasSelection = false,
                    PoolId = pool.PoolId,
                    SkippedEncounterIds = skipped,
                    NoSelectionReason = "no_candidate_discovered"
                };
            }

            EncounterDefinition selected = discovered.Count == 1
                ? discovered[0]
                : EncounterDeterministicRandom.WeightedPick(
                    narrativeContext.WorldSeed, opportunity.OpportunityId, "weighted_selection",
                    discovered, e => e.SelectionWeight);

            skipped.Remove(selected.EncounterId);
            return new EncounterSelectionResult
            {
                HasSelection = true,
                SelectedEncounter = selected,
                PoolId = pool.PoolId,
                SkippedEncounterIds = skipped
            };
        }

        // Event spam pacing (§125-127): применяется ТОЛЬКО к Reaction/Micro —
        // Standard/Complex/Short/QuestSeed используют общий MinimumHoursBetweenEncounters
        // пула (уже проверен раньше в Select) и в эти лимиты не попадают.
        private static bool IsReactivePacingBlocked(
            EncounterPoolDefinition pool,
            EncounterDefinition encounter,
            EncounterOpportunity opportunity,
            EncounterRuntimeStateData runtimeState)
        {
            if (encounter.DurationClass != EncounterDurationClass.Reaction &&
                encounter.DurationClass != EncounterDurationClass.Micro)
                return false;

            if (pool.MinimumHoursBetweenReactiveEncounters > 0)
            {
                double lastReactive = runtimeState.GetPoolLastReactiveTriggeredWorldHour(pool.PoolId);
                if (lastReactive >= 0 && opportunity.WorldHour - lastReactive < pool.MinimumHoursBetweenReactiveEncounters)
                    return true;
            }

            if (pool.MaxReactiveEncountersPerTravelDay > 0)
            {
                int day = (int)(opportunity.WorldHour / 24.0);
                if (runtimeState.GetReactiveCountForDay(pool.PoolId, day) >= pool.MaxReactiveEncountersPerTravelDay)
                    return true;
            }

            return false;
        }

        private static EncounterSelectionResult SelectDirect(
            EncounterOpportunity opportunity,
            EncounterDatabaseAsset database,
            NarrativeEvaluationContext narrativeContext,
            EncounterRuntimeStateData runtimeState)
        {
            EncounterDefinition encounter = database.FindById(opportunity.DirectEncounterId);
            if (encounter == null)
                return EncounterSelectionResult.None("direct_encounter_not_found");

            EncounterEligibilityResult eligibility = EncounterEligibilityEvaluator.Evaluate(
                encounter, narrativeContext, runtimeState,
                opportunity.RegionId, opportunity.LocationTags, opportunity.WorldHour);

            return eligibility.Eligible
                ? EncounterSelectionResult.Selected(encounter, string.Empty)
                : EncounterSelectionResult.None("direct_encounter_not_eligible");
        }
    }
}
