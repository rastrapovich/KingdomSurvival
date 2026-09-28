namespace KingdomSurvival.Encounters
{
    // ПР-12В: встречи в лагере. Одна возможность на ночлег — после первого
    // часа сна (раньше, чем сюжетный бой главы у стоянки на втором часу).
    // Что выпадет и выпадет ли, решают пул CAMP_POOL_01 и условия самих
    // встреч; тишина допустима.
    public static class CampEncounterIds
    {
        public const string PoolId = "CAMP_POOL_01";
        public const string RegionId = "camp";
        public const double HoursIntoRest = 1.0;
    }

    public static class CampEncounters
    {
        // Возможность встречи на этом ночлеге или null (не спят, рано, уже
        // была). ID — день и эпоха ночёвки: повтор и загрузка не дублируют.
        public static EncounterOpportunity Opportunity(GameState state)
        {
            if (state == null || !CampRest.IsResting(state))
                return null;
            ExpeditionActivityData rest = state.ActiveExpedition.ActiveActivity;
            if (rest.TotalHours - rest.RemainingHours < CampEncounterIds.HoursIntoRest)
                return null;

            ProgressionStateData progression = CharacterProgressionService.EnsureState(state);
            string id = "camp:" + state.Day + ":" + progression.ExpeditionEpoch + ":" + progression.CampEpoch;
            EncounterRuntimeService.EnsureState(state);
            if (state.Encounters.WasOpportunityProcessed(id))
                return null;

            return new EncounterOpportunity
            {
                OpportunityId = id,
                PoolId = CampEncounterIds.PoolId,
                RegionId = CampEncounterIds.RegionId,
                WorldHour = state.Day * 24.0
            };
        }
    }
}
