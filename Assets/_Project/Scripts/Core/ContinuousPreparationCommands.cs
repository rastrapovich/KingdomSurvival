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

            ResidentState resident = HomePeopleService.Find(state, fighterId);
            if (resident != null && resident.Injury == ResidentInjury.Recovering)
            {
                resultMessage = resident.DisplayName + " ранен(а) и восстанавливается — в поход не пойдёт.";
                return false;
            }

            if (state.ActiveExpedition.RetinueIds != null && state.ActiveExpedition.RetinueIds.Contains(fighterId))
            {
                resultMessage = "Один человек не может быть и бойцом, и специалистом.";
                return false;
            }
        }

        if (requestedIds.Count > GameState.ExpeditionFighterSlots)
        {
            resultMessage =
                "В подготовленный поход можно взять не больше " +
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
        ExpeditionPreparation.RememberExpeditionRoster(state);

        resultMessage =
            "Состав подготовленного похода изменён: командир + " +
            expedition.FighterIds.Count + " бойца.";
        return true;
    }

    // ПР-06А: свита — 0–1 небоевой специалист (null/пусто — без специалиста).
    // Выбор только готовит состав: пока отряд не тронулся, человек дома и
    // работает.
    public static bool TrySetPreparedRetinue(
        GameState state,
        string personId,
        out string resultMessage)
    {
        resultMessage = "Свиту уже нельзя изменить.";
        if (!CanEditPreparedRoster(state))
            return false;

        ExpeditionData expedition = state.ActiveExpedition;
        if (expedition.RetinueIds == null)
            expedition.RetinueIds = new List<string>();

        if (string.IsNullOrEmpty(personId))
        {
            expedition.RetinueIds.Clear();
            ExpeditionPreparation.RememberExpeditionRoster(state);
            resultMessage = "Поход без специалиста.";
            return true;
        }

        if (expedition.FighterIds.Contains(personId))
        {
            resultMessage = "Один человек не может быть и бойцом, и специалистом.";
            return false;
        }

        if (!HomePeopleService.CanJoinAsRetinue(state, personId, out string reason))
        {
            resultMessage = "Нельзя взять в свиту: " + reason + ".";
            return false;
        }

        expedition.RetinueIds.Clear();
        expedition.RetinueIds.Add(personId);
        ExpeditionPreparation.RememberExpeditionRoster(state);
        resultMessage = "Специалист в походе: " + HomePeopleService.Find(state, personId).DisplayName + ".";
        return true;
    }
}
