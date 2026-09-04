using System.Collections.Generic;

public static class ContinuousPreparationCommands
{
    public static bool CanEditPreparedRoster(GameState state)
    {
        if (state == null || !state.HasActiveExpedition)
            return false;

        ExpeditionData expedition = state.ActiveExpedition;

        return expedition != null &&
               expedition.Phase == CommanderState.TravellingToLocation &&
               !state.HasPendingExpeditionDecision &&
               !expedition.HasTimedActivity &&
               !ContinuousSimulationSystem.HasExpeditionStartedMoving(state);
    }

    public static bool TrySetPreparedRoster(
        GameState state,
        IEnumerable<string> fighterIds,
        out string resultMessage)
    {
        resultMessage = "Состав уже нельзя изменить.";

        if (!CanEditPreparedRoster(state))
            return false;

        if (fighterIds == null)
        {
            resultMessage = "Состав отряда не задан.";
            return false;
        }

        HashSet<string> requestedIds = new HashSet<string>();

        foreach (string fighterId in fighterIds)
        {
            if (string.IsNullOrEmpty(fighterId) ||
                state.FindFighter(fighterId) == null)
            {
                resultMessage = "В составе найден неизвестный боец.";
                return false;
            }

            if (!requestedIds.Add(fighterId))
            {
                resultMessage = "Один боец указан в составе несколько раз.";
                return false;
            }
        }

        if (requestedIds.Count != GameState.ExpeditionFighterSlots)
        {
            resultMessage =
                "Подготовленный поход должен содержать ровно " +
                GameState.ExpeditionFighterSlots +
                " обычных бойцов. Командир входит автоматически.";
            return false;
        }

        List<string> orderedIds = new List<string>();

        foreach (FighterData fighter in state.Fighters)
        {
            if (requestedIds.Contains(fighter.Id))
                orderedIds.Add(fighter.Id);
        }

        ExpeditionData expedition = state.ActiveExpedition;
        expedition.FighterIds.Clear();
        expedition.FighterIds.AddRange(orderedIds);

        resultMessage =
            "Состав подготовленного похода изменён: командир + " +
            expedition.FighterIds.Count + " бойца.";
        return true;
    }
}
