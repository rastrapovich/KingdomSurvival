using System;
using System.Collections.Generic;

// ПР-11 (ProjectDocs/PR11_CHRONICLE_SPEC.md §4): история — значимые
// события по игровому времени, по одной записи на событие (стабильный ID).
// Сохраняется вместе с кампанией; донесения в UI остаются лентой, а не
// хранилищем. Никаких тиков ресурсов и служебных вызовов.
[Serializable]
public sealed class ChronicleEntryData
{
    public string Id = string.Empty;
    public int Day;
    public double Hour;
    public string Title = string.Empty;
    public string Text = string.Empty;
    // Известное место записи (ID локации) — для «Показать на карте».
    public string LocationId = string.Empty;
    // Запись-причина: последствие ссылается на исходное решение (§6).
    public string CauseId = string.Empty;
}

[Serializable]
public sealed class ChronicleData
{
    public List<ChronicleEntryData> Entries = new List<ChronicleEntryData>();
    // Прочитанные записи истории и карточки сведений («НОВОЕ» снимается кликом).
    public List<string> SeenIds = new List<string>();
}

public static class Chronicle
{
    public static ChronicleData Get(GameState state)
    {
        if (state.Chronicle == null)
            state.Chronicle = new ChronicleData();
        if (state.Chronicle.Entries == null)
            state.Chronicle.Entries = new List<ChronicleEntryData>();
        if (state.Chronicle.SeenIds == null)
            state.Chronicle.SeenIds = new List<string>();
        return state.Chronicle;
    }

    public static bool Has(GameState state, string id)
    {
        return state != null && Find(state, id) != null;
    }

    public static ChronicleEntryData Find(GameState state, string id)
    {
        foreach (ChronicleEntryData entry in Get(state).Entries)
        {
            if (entry.Id == id)
                return entry;
        }
        return null;
    }

    // Записать один раз. Повторный вызов с тем же ID ничего не меняет.
    public static bool Record(GameState state, string id, string title, string text,
        string locationId = null, string causeId = null)
    {
        if (state == null || string.IsNullOrEmpty(id) || Has(state, id))
            return false;

        Get(state).Entries.Add(new ChronicleEntryData
        {
            Id = id,
            Day = state.Day,
            Hour = ContinuousSimulationSystem.GetClock(state).HourOfDay,
            Title = title ?? string.Empty,
            Text = text ?? string.Empty,
            LocationId = locationId ?? string.Empty,
            CauseId = causeId ?? string.Empty
        });
        return true;
    }

    public static bool IsSeen(GameState state, string id)
    {
        return state != null && Get(state).SeenIds.Contains(id);
    }

    public static void MarkSeen(GameState state, string id)
    {
        if (state == null || string.IsNullOrEmpty(id))
            return;
        List<string> seen = Get(state).SeenIds;
        if (!seen.Contains(id))
            seen.Add(id);
    }

    public static string FormatTime(ChronicleEntryData entry)
    {
        return "День " + entry.Day + ", " + ContinuousSimulationSystem.FormatClock(entry.Hour);
    }
}
