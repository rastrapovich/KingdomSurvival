public static class LocationArrivalDecisionFactory
{
    private const string LocationDiscoveryDefinitionId = "location_discovered";
    private const string InvestigateLocationOptionId =
        "investigate_discovered_location";
    private const string CancelArrivalOptionId =
        "continue_interrupted_route";
    private const string UnavailableInvestigateOptionId =
        "arrival_investigate_unavailable";

    private static int nextArrivalDecisionId = -100000;

    public static bool TryCreate(
        GameState state,
        out ExpeditionDecisionOccurrence occurrence)
    {
        occurrence = null;

        if (state == null ||
            !state.HasActiveExpedition ||
            state.HasPendingExpeditionDecision)
        {
            return false;
        }

        ExpeditionData expedition = state.ActiveExpedition;

        if (expedition.Phase != CommanderState.AtLocation ||
            expedition.IsLocationResearchInProgress)
        {
            return false;
        }

        LocationData location = state.FindLocation(expedition.LocationId);

        if (location == null || location.IsWaypoint)
            return false;

        bool researchImplemented =
            location.ExplorationHours > 0 && !location.IsExplored;

        occurrence = new ExpeditionDecisionOccurrence
        {
            Id = nextArrivalDecisionId--,
            Day = state.Day,
            DefinitionId = LocationDiscoveryDefinitionId,
            Title = "Армия прибыла в локацию «" + location.Name + "»",
            Description =
                "Армия находится внутри локации «" + location.Name +
                "». Время остановлено до решения игрока.",
            OptionA = new ExpeditionDecisionOptionView
            {
                Id = researchImplemented
                    ? InvestigateLocationOptionId
                    : UnavailableInvestigateOptionId,
                Label = "Исследовать",
                ConsequencePreview = researchImplemented
                    ? "Начать исследование локации"
                    : location.IsExplored
                        ? "Локация уже исследована"
                        : "Исследование этой локации пока не реализовано"
            },
            OptionB = new ExpeditionDecisionOptionView
            {
                Id = CancelArrivalOptionId,
                Label = "Отменить",
                ConsequencePreview =
                    "Не начинать исследование и остаться в локации"
            }
        };

        expedition.PendingDecision = occurrence;
        return true;
    }
}
