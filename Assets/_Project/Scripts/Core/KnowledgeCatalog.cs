using System.Collections.Generic;

// ПР-11 (ProjectDocs/PR11_CHRONICLE_SPEC.md §3): вкладка журнала «Сведения» —
// короткое название знания, степень уверенности, подтверждение слуха и
// место. Сам текст сведения берётся из реплики, которая его дала (база
// диалогов). Каталог общий и пуст по умолчанию: записи регистрирует
// содержание (канон v1.59: сведения удалённой кампании сняты вместе с ней).
public enum KnowledgeCertainty
{
    Rumor,
    Observation,
    Established
}

public sealed class KnowledgeEntry
{
    public string Id;
    public string Title;
    public KnowledgeCertainty Certainty;
    public string ConfirmedBy;
    public string LocationId;
}

public static class KnowledgeCatalog
{
    private static readonly List<KnowledgeEntry> Entries = new List<KnowledgeEntry>();

    public static void Register(KnowledgeEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Id) || Find(entry.Id) != null)
            return;
        Entries.Add(entry);
    }

    public static IReadOnlyList<KnowledgeEntry> All => Entries;

    public static KnowledgeEntry Find(string id)
    {
        foreach (KnowledgeEntry entry in Entries)
        {
            if (entry.Id == id)
                return entry;
        }
        return null;
    }

    // Открытые сведения в порядке каталога.
    public static List<KnowledgeEntry> Known(NarrativeStateData state)
    {
        List<KnowledgeEntry> known = new List<KnowledgeEntry>();
        if (state == null)
            return known;
        foreach (KnowledgeEntry entry in Entries)
        {
            if (state.HasKnowledge(entry.Id))
                known.Add(entry);
        }
        return known;
    }

    public static bool IsConfirmed(NarrativeStateData state, KnowledgeEntry entry)
    {
        return entry != null && !string.IsNullOrEmpty(entry.ConfirmedBy) &&
               state != null && state.HasKnowledge(entry.ConfirmedBy);
    }

    public static string CertaintyLabel(NarrativeStateData state, KnowledgeEntry entry)
    {
        switch (entry.Certainty)
        {
            case KnowledgeCertainty.Rumor:
                return IsConfirmed(state, entry) ? "слух — подтвердилось" : "слух";
            case KnowledgeCertainty.Observation:
                return "наблюдение";
            default:
                return "установлено";
        }
    }
}
