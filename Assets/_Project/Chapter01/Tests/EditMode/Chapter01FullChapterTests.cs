using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using NUnit.Framework;
using static Chapter01PlaythroughWalker;

// ПР-02-T02: скриптовый прогон всей главы от новой партии до Совета — без
// Debug, через дела в Доме, ночные события, поход и возвращение
// (см. Chapter01PlaythroughWalker). Ответы — стратегией, на Совете —
// заданный исход; дела — «первое», «последнее» или «короткий путь».
public sealed class Chapter01FullChapterTests
{
    // Узлы, обязательные в любом прохождении. Расследования N07 — по
    // выбору: мельница, или скот и река (вариант (б)).
    private static readonly string[] RequiredScenes =
    {
        Chapter01Ids.Dialogues.D01, Chapter01Ids.Dialogues.D02, Chapter01Ids.Dialogues.D03,
        Chapter01Ids.Dialogues.D04, Chapter01Ids.Dialogues.D05, Chapter01Ids.Dialogues.D06,
        Chapter01Ids.Dialogues.D08, Chapter01Ids.Dialogues.D09, Chapter01Ids.Dialogues.D10,
        Chapter01Ids.Dialogues.D11, Chapter01Ids.Dialogues.D11B, Chapter01Ids.Dialogues.D11C,
        Chapter01Ids.Dialogues.D12, Chapter01Ids.Dialogues.D13, Chapter01Ids.Dialogues.D14,
        Chapter01Ids.Dialogues.D14Half, Chapter01Ids.Dialogues.D15, Chapter01Ids.Dialogues.D16,
        Chapter01Ids.Dialogues.D17,
        // ПР-06Б: семья у ворот (дело в Доме после N05) и выход на берег у брода.
        Chapter01Ids.Dialogues.GateFamily, Chapter01Ids.Dialogues.D12B,
        // ПР-08: выбор развития героя после возвращения, до Совета.
        Chapter01Ids.Dialogues.D16B
    };

    private static readonly string[] InvestigationScenes =
    {
        Chapter01Ids.Dialogues.D07A, Chapter01Ids.Dialogues.D07B, Chapter01Ids.Dialogues.D07C
    };

    private static string CouncilOutcome(NarrativeStateData state)
    {
        if (state.HasFlag(Chapter01Ids.Flags.CouncilOldOrderRestored)) return "old_order";
        if (state.HasFlag(Chapter01Ids.Flags.CouncilNewOrderCreated)) return "new_order";
        if (state.HasFlag(Chapter01Ids.Flags.CouncilWaterKeptForHome)) return "water_for_home";
        return "none";
    }

    private static int CountCouncilOutcomes(NarrativeStateData state)
    {
        int count = 0;
        if (state.HasFlag(Chapter01Ids.Flags.CouncilOldOrderRestored)) count++;
        if (state.HasFlag(Chapter01Ids.Flags.CouncilNewOrderCreated)) count++;
        if (state.HasFlag(Chapter01Ids.Flags.CouncilWaterKeptForHome)) count++;
        return count;
    }

    [TestCase(false, 0, CardStrategy.First)]
    [TestCase(false, 1, CardStrategy.First)]
    [TestCase(false, 2, CardStrategy.First)]
    [TestCase(true, 0, CardStrategy.Last)]
    [TestCase(true, 1, CardStrategy.Last)]
    [TestCase(true, 2, CardStrategy.Last)]
    [TestCase(false, 0, CardStrategy.Shortest)]
    [TestCase(true, 2, CardStrategy.Shortest)]
    public void WholeChapter_FromNewGameToCouncil_WithoutDebug(bool preferLast, int councilChoice, CardStrategy cards)
    {
        GameState gameState = NewGame(20260924);
        Walk walk = PlayWholeChapter(gameState, new Options
        {
            PreferLastAnswer = preferLast,
            CouncilChoice = councilChoice,
            Cards = cards
        });
        NarrativeStateData state = gameState.Narrative;

        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.CouncilCompleted), walk.Path);
        Assert.AreEqual(1, CountCouncilOutcomes(state), "Ровно один исход Совета. Путь: " + walk.Path);
        Assert.AreEqual(Chapter01Ids.Dialogues.D01, walk.Scenes.First());
        Assert.AreEqual(Chapter01Ids.Dialogues.D17, walk.Scenes.Last());
        Assert.AreEqual(walk.Scenes.Distinct().Count(), walk.Scenes.Count, "Сцена открылась дважды. Путь: " + walk.Path);
        Assert.IsFalse(gameState.HasActiveExpedition, "К Совету отряд должен быть дома.");

        CollectionAssert.IsSubsetOf(RequiredScenes, walk.Scenes, "Путь: " + walk.Path);
        CollectionAssert.IsSubsetOf(walk.Scenes, RequiredScenes.Concat(InvestigationScenes).ToList(), "Путь: " + walk.Path);
        CollectionAssert.AreEqual(
            new[] { "выход в поход", "исследование области", "путь к людям ниже по течению" },
            walk.Actions);

        TestContext.WriteLine("Путь: " + walk.Path);
        TestContext.WriteLine("Ремонт: " + Chapter01StoryDirector.GetRepairChoice(state) +
                              ", Совет: " + CouncilOutcome(state) + ", день " + gameState.Day);
    }

    [Test]
    public void WholeChapter_EachCouncilChoiceGivesDifferentOutcome()
    {
        HashSet<string> outcomes = new HashSet<string>();
        for (int choice = 0; choice < 3; choice++)
        {
            GameState gameState = NewGame(20260924);
            PlayWholeChapter(gameState, new Options { CouncilChoice = choice });
            outcomes.Add(CouncilOutcome(gameState.Narrative));
        }

        Assert.AreEqual(3, outcomes.Count, "Исходы: " + string.Join(", ", outcomes));
    }

    [Test]
    public void WholeChapter_BothRepairBranchesReachCouncil()
    {
        HashSet<Chapter01RepairChoice> repairs = new HashSet<Chapter01RepairChoice>();
        foreach (bool preferLast in new[] { false, true })
        {
            GameState gameState = NewGame(20260924);
            PlayWholeChapter(gameState, new Options { PreferLastAnswer = preferLast });
            Assert.IsTrue(Chapter01StoryDirector.IsChapterComplete(gameState.Narrative));
            repairs.Add(Chapter01StoryDirector.GetRepairChoice(gameState.Narrative));
        }

        CollectionAssert.AreEquivalent(
            new[] { Chapter01RepairChoice.Old, Chapter01RepairChoice.New },
            repairs.ToList(),
            "Стратегии «первый/последний» должны пройти обе ветки ремонта.");
    }
}
