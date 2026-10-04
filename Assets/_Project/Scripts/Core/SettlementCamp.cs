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
}
