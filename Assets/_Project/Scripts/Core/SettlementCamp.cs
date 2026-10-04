using System;
using System.Collections.Generic;

// ПР-12К (канон v1.53 §28.9): временный лагерь у конкретного поселения.
// Это узел похода, а не второй Дом: ожидающие остаются в походном составе
// (едят из припасов похода, домашние функции выключены), но физически не
// находятся рядом с командиром, пока тот внутри поселения.
[Serializable]
public sealed class SettlementCampData
{
    // Поселение (LocationId), у входа в которое разбит лагерь.
    public string SettlementLocationId = string.Empty;
    // Положение стоянки на глобальной карте.
    public float MapXPercent;
    public float MapYPercent;
    // Командир сейчас внутри поселения (входящая группа с ним), а не в лагере.
    public bool CommanderInside;
    // Кто ждёт в лагере, пока командир внутри.
    public List<string> WaitingIds = new List<string>();
}

public static partial class SettlementCampService
{
    public static SettlementCampData Get(GameState state)
    {
        return state != null && state.HasActiveExpedition ? state.SettlementCamp : null;
    }

    public static bool HasCamp(GameState state)
    {
        SettlementCampData camp = Get(state);
        return camp != null && !string.IsNullOrEmpty(camp.SettlementLocationId);
    }

    // Ждёт снаружи, пока командир в поселении.
    public static bool IsWaiting(GameState state, string personId)
    {
        SettlementCampData camp = Get(state);
        return camp != null && camp.CommanderInside && camp.WaitingIds != null &&
               !string.IsNullOrEmpty(personId) && camp.WaitingIds.Contains(personId);
    }

    public static bool IsCommanderInside(GameState state) => HasCamp(state) && state.SettlementCamp.CommanderInside;

    // Отряд стоит у поселения (у его входа) — лагерь здесь возможен.
    public static LocationData SettlementHere(GameState state)
    {
        if (state == null || !state.HasActiveExpedition || state.ActiveExpedition.Phase != CommanderState.AtLocation)
            return null;
        LocationData location = state.FindLocation(state.ActiveExpedition.LocationId);
        return location != null && location.IsSettlement ? location : null;
    }

    // Почему участника не пустят в поселение; пусто — пустят. Правило
    // задаёт сам участник (авторская причина) и может снять флаг истории.
    public static string AdmissionBlockReason(GameState state, string personId)
    {
        ResidentState resident = HomePeopleService.Find(state, personId);
        if (resident == null || string.IsNullOrEmpty(resident.SettlementAdmissionBlock))
            return string.Empty;
        if (!string.IsNullOrEmpty(resident.SettlementAdmissionExceptionFlag) && state.Narrative != null &&
            state.Narrative.HasFlag(resident.SettlementAdmissionExceptionFlag))
            return string.Empty;
        return resident.SettlementAdmissionBlock;
    }

    // Живые участники похода, кроме командира, — кого можно оставить или взять.
    public static List<string> Companions(GameState state)
    {
        List<string> ids = new List<string>();
        CommanderData hero = state?.GetSelectedCommander();
        foreach (string personId in PartyPresence.ExpeditionIds(state))
        {
            if (hero != null && personId == hero.Id)
                continue;
            ResidentState resident = HomePeopleService.Find(state, personId);
            if (resident == null || resident.IsAlive)
                ids.Add(personId);
        }
        return ids;
    }

    // Войти в поселение с выбранной группой. Командир входит всегда;
    // остальные — оставшиеся ждут в лагере (разбивается, если его нет).
    // Недопущенного взять нельзя: причина — в reason.
    public static bool Enter(GameState state, IEnumerable<string> enteringIds, out string reason)
    {
        reason = string.Empty;
        LocationData settlement = SettlementHere(state);
        if (settlement == null)
        {
            reason = "Лагерь разбивают только у входа в поселение.";
            return false;
        }
        if (HasCamp(state) && state.SettlementCamp.SettlementLocationId != settlement.Id)
        {
            reason = "Сначала соберите отряд у прежнего лагеря.";
            return false;
        }

        HashSet<string> entering = new HashSet<string>(enteringIds ?? new List<string>());
        List<string> waiting = new List<string>();
        foreach (string personId in Companions(state))
        {
            string block = AdmissionBlockReason(state, personId);
            if (entering.Contains(personId) && !string.IsNullOrEmpty(block))
            {
                ResidentState resident = HomePeopleService.Find(state, personId);
                reason = (resident != null ? resident.DisplayName : personId) + " не допускается: " + block;
                return false;
            }
            if (!entering.Contains(personId))
                waiting.Add(personId);
        }

        if (waiting.Count == 0 && !HasCamp(state))
            return true; // все вошли вместе — лагерь не нужен

        if (!HasCamp(state))
        {
            state.SettlementCamp = new SettlementCampData
            {
                SettlementLocationId = settlement.Id,
                MapXPercent = state.ActiveExpedition.CurrentMapXPercent,
                MapYPercent = state.ActiveExpedition.CurrentMapYPercent
            };
        }
        state.SettlementCamp.WaitingIds = waiting;
        state.SettlementCamp.CommanderInside = true;
        return true;
    }

    // Командир вернулся к лагерю: все снова вместе (входящую группу можно
    // выбрать заново).
    public static void ReturnToCamp(GameState state)
    {
        if (HasCamp(state))
            state.SettlementCamp.CommanderInside = false;
    }

    // «Собраться и продолжить путь»: разделение снято, живые вместе.
    public static List<string> Gather(GameState state)
    {
        List<string> names = new List<string>();
        if (!HasCamp(state))
            return names;
        foreach (string personId in state.SettlementCamp.WaitingIds)
        {
            ResidentState resident = HomePeopleService.Find(state, personId);
            if (resident != null && resident.IsAlive)
                names.Add(resident.DisplayName);
        }
        state.SettlementCamp = null;
        return names;
    }

    // Порядок в данных: погибшие, ушедшие из похода и неизвестные ID
    // убираются из лагеря (без создания людей); отряд, ушедший от поселения,
    // уходит вместе — забытых у прежней стоянки нет. Строки — для донесений.
    public static List<string> Normalize(GameState state)
    {
        List<string> reports = new List<string>();
        if (state == null || state.SettlementCamp == null)
            return reports;
        if (!state.HasActiveExpedition || string.IsNullOrEmpty(state.SettlementCamp.SettlementLocationId))
        {
            state.SettlementCamp = null;
            return reports;
        }

        List<string> party = PartyPresence.ExpeditionIds(state);
        int removed = state.SettlementCamp.WaitingIds.RemoveAll(personId =>
        {
            ResidentState resident = HomePeopleService.Find(state, personId);
            return !party.Contains(personId) || (resident != null && !resident.IsAlive);
        });
        if (removed > 0)
            reports.Add("Лагерь: из списка ожидающих убраны те, кого нет в походе (" + removed + ").");

        ExpeditionData expedition = state.ActiveExpedition;
        bool stillHere = expedition.Phase == CommanderState.AtLocation &&
                         expedition.LocationId == state.SettlementCamp.SettlementLocationId;
        if (!stillHere)
        {
            List<string> names = Gather(state);
            reports.Add(names.Count > 0
                ? "Отряд собрался у лагеря и выступил вместе: " + string.Join(", ", names) + "."
                : "Лагерь у поселения свёрнут.");
        }
        return reports;
    }
}
