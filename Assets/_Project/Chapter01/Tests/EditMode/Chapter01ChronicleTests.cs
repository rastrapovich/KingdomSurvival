using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;
using static Chapter01PlaythroughWalker;

// ПР-11 (ProjectDocs/PR11_CHRONICLE_SPEC.md): история главы пишется из
// пережитого, по разу и по порядку; сведения несут степень уверенности,
// слух подтверждается; текст сведения берётся из дающей его реплики.
public sealed class Chapter01ChronicleTests
{
    private static readonly string[] RequiredEntries =
    {
        "ch01.flood", "ch01.repair", "ch01.family", "ch01.departure", Chapter01Chronicle.FordAccessId,
        "ch01.downstream", "ch01.agreement", "ch01.return_decision", "ch01.returned",
        "ch01.road_growth", "ch01.council"
    };

    [TestCase(false, 0, CardStrategy.First)]
    [TestCase(true, 2, CardStrategy.Shortest)]
    public void WholeChapter_WritesHistoryOnce_InTimeOrder(bool preferLast, int council, CardStrategy cards)
    {
        GameState gameState = NewGame(20260925);
        PlayWholeChapter(gameState, new Options { PreferLastAnswer = preferLast, CouncilChoice = council, Cards = cards });

        List<ChronicleEntryData> entries = Chronicle.Get(gameState).Entries;
        List<string> ids = entries.Select(e => e.Id).ToList();
        CollectionAssert.IsSubsetOf(RequiredEntries, ids, "История: " + string.Join(", ", ids));
        CollectionAssert.AllItemsAreUnique(ids);

        for (int i = 1; i < entries.Count; i++)
        {
            double previous = entries[i - 1].Day * 24.0 + entries[i - 1].Hour;
            double current = entries[i].Day * 24.0 + entries[i].Hour;
            Assert.LessOrEqual(previous, current + 0.0001, entries[i - 1].Id + " позже " + entries[i].Id);
        }

        Assert.IsTrue(entries.All(e => !string.IsNullOrWhiteSpace(e.Title) && !string.IsNullOrWhiteSpace(e.Text)));

        int count = entries.Count;
        Chapter01Chronicle.Refresh(gameState);
        Chapter01Chronicle.Refresh(gameState);
        Assert.AreEqual(count, Chronicle.Get(gameState).Entries.Count, "Повторный опрос ничего не дописывает.");
    }

    [Test]
    public void FordReturn_ReferencesFordAccess_AsCause()
    {
        GameState gameState = NewGame(20260925);
        PlayWholeChapter(gameState, new Options());

        ChronicleEntryData fordReturn = Chronicle.Find(gameState, "ch01.ford_return");
        if (fordReturn == null)
            Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.FordReturnHandled),
                "Брод на обратном пути пройден, а записи нет.");
        else
            Assert.AreEqual(Chapter01Chronicle.FordAccessId, fordReturn.CauseId);
    }

    [Test]
    public void History_NotWrittenOutsideChapterCrisis_OrBeforeEvents()
    {
        GameState gameState = NewGame(20260925);
        Chapter01Chronicle.Refresh(gameState);
        Assert.IsNull(Chronicle.Find(gameState, "ch01.departure"));
        Assert.IsNull(Chronicle.Find(gameState, "ch01.council"));
    }

    [Test]
    public void Knowledge_Rumor_IsConfirmedByAgreement()
    {
        NarrativeStateData state = new NarrativeStateData();
        KnowledgeEntry loaf = Chapter01KnowledgeCatalog.Find(Chapter01Ids.Knowledge.SecondLoafIsRation);
        state.AddKnowledge(loaf.Id);

        Assert.AreEqual(KnowledgeCertainty.Rumor, loaf.Certainty);
        Assert.AreEqual("слух", Chapter01KnowledgeCatalog.CertaintyLabel(state, loaf));
        CollectionAssert.AreEqual(new[] { loaf }, Chapter01KnowledgeCatalog.Known(state));

        state.AddKnowledge(Chapter01Ids.Knowledge.OldAgreement);
        Assert.IsTrue(Chapter01KnowledgeCatalog.IsConfirmed(state, loaf));
        Assert.AreEqual("слух — подтвердилось", Chapter01KnowledgeCatalog.CertaintyLabel(state, loaf));
        Assert.AreEqual("установлено",
            Chapter01KnowledgeCatalog.CertaintyLabel(state, Chapter01KnowledgeCatalog.Find(Chapter01Ids.Knowledge.OldAgreement)));
    }

    [Test]
    public void Knowledge_Catalog_IdsAreUnique_AndLocationsExist()
    {
        CollectionAssert.AllItemsAreUnique(Chapter01KnowledgeCatalog.All.Select(e => e.Id));
        // Места главы появляются на карте по ходу сюжета — сверяем с их ID.
        foreach (KnowledgeEntry entry in Chapter01KnowledgeCatalog.All.Where(e => !string.IsNullOrEmpty(e.LocationId)))
            CollectionAssert.Contains(Chapter01Ids.Locations.All, entry.LocationId, entry.Id);
    }

    [Test]
    public void Knowledge_EveryCatalogEntry_HasSourceLineInDialogues()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Dictionary<string, DialogueKnowledgeSource> sources = DialogueKnowledgeSources.Build(database);

        List<string> missing = Chapter01KnowledgeCatalog.All.Where(e => !sources.ContainsKey(e.Id)).Select(e => e.Id).ToList();
        Assert.IsEmpty(missing, "Нет реплики-источника: " + string.Join(", ", missing));
        foreach (DialogueKnowledgeSource source in sources.Values)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(source.Text), source.KnowledgeId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(source.Source), source.KnowledgeId);
        }
    }

    [Test]
    public void CrisisJournal_BuildsChapterGoals()
    {
        GameState gameState = NewGame(20260925);
        Assert.AreEqual(Chapter01JournalProvider.Build(gameState).Count, CrisisJournal.BuildGoals(gameState).Count);
    }
}
