using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using NUnit.Framework;
using static Chapter01PlaythroughWalker;

// ПР-02: домашняя часть главы N01–N10 через Дом, людей и место
// (Chapter01HomeActivities). Правила открытия — отдельными тестами на
// NarrativeState, прохождение — «игроком» Chapter01PlaythroughWalker против
// настоящей базы диалогов до сбора отряда.
public sealed class Chapter01HomeFlowTests
{
    private const double Noon = 12.0;
    private const double Midnight = 0.0;

    private static bool PartyGathered(GameState gameState)
    {
        return gameState.Narrative.HasFlag(Chapter01Ids.Flags.PartyGatheringSeen);
    }

    private static List<string> CardIds(NarrativeStateData state)
    {
        return Chapter01HomeActivities.GetAvailable(state, true).Select(a => a.DialogueId).ToList();
    }

    private static NarrativeStateData StateAfterWrongWater()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        state.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);
        state.SetFlag(Chapter01Ids.Flags.RepairOld);
        state.SetFlag(Chapter01Ids.Flags.WaterWrongActive);
        return state;
    }

    // --- Прохождение домашней части ---

    [TestCase(CardStrategy.First, 20260924)]
    [TestCase(CardStrategy.Last, 20260924)]
    [TestCase(CardStrategy.Shortest, 20260924)]
    [TestCase(CardStrategy.First, 7)]
    [TestCase(CardStrategy.Last, 7)]
    public void HomePart_ReachesPartyGathering_WithoutDebug(CardStrategy cards, int seed)
    {
        GameState gameState = NewGame(seed);
        Walk walk = PlayUntil(gameState, new Options
        {
            Cards = cards,
            PreferLastAnswer = cards == CardStrategy.Last
        }, PartyGathered);
        NarrativeStateData state = gameState.Narrative;

        Assert.AreEqual(Chapter01Ids.Dialogues.D01, walk.Scenes[0], "Новая партия начинается с N01.");
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.FarRouteUnlocked), "Гейт N09 не открылся. Путь: " + walk.Path);
        Assert.AreEqual(Chapter01Ids.Dialogues.D10, walk.Scenes.Last(), "Путь: " + walk.Path);
        Assert.AreEqual(walk.Scenes.Distinct().Count(), walk.Scenes.Count, "Сцена открылась дважды. Путь: " + walk.Path);
        Assert.AreNotEqual(Chapter01RepairChoice.None, Chapter01StoryDirector.GetRepairChoice(state));
    }

    [Test]
    public void NightScenes_OpenOnlyAtNight()
    {
        GameState gameState = NewGame(20260924);
        Walk walk = PlayUntil(gameState, new Options(), PartyGathered);

        Assert.IsTrue(Chapter01HomeActivities.IsNight(walk.HourWhenOpened(Chapter01Ids.Dialogues.D04)), "Паводок — ночью.");
        Assert.IsTrue(Chapter01HomeActivities.IsNight(walk.HourWhenOpened(Chapter01Ids.Dialogues.D06)), "Удар на мельнице — ночью.");
    }

    [Test]
    public void ShortestPath_MillOnly_SkipsOtherInvestigations_AndUsesMatchingText()
    {
        GameState gameState = NewGame(20260924);
        Walk walk = PlayUntil(gameState, new Options { Cards = CardStrategy.Shortest }, PartyGathered);

        CollectionAssert.Contains(walk.Scenes, Chapter01Ids.Dialogues.D07A);
        CollectionAssert.DoesNotContain(walk.Scenes, Chapter01Ids.Dialogues.D07B);
        CollectionAssert.DoesNotContain(walk.Scenes, Chapter01Ids.Dialogues.D07C);
        CollectionAssert.Contains(walk.RevealedBlocks, "chapter01.node.08_main_mill_only");
        CollectionAssert.DoesNotContain(walk.RevealedBlocks, "chapter01.node.08_main");
        CollectionAssert.Contains(walk.RevealedBlocks, "chapter01.node.09_main_partial");
        CollectionAssert.Contains(walk.RevealedBlocks, "chapter01.node.09.ulyana_no_cattle");
        CollectionAssert.Contains(walk.RevealedBlocks, "chapter01.node.09.lada_no_river");
    }

    [Test]
    public void LastCardPath_CattleAndRiver_WithoutMill_UsesMatchingText()
    {
        GameState gameState = NewGame(20260924);
        Walk walk = PlayUntil(gameState, new Options { Cards = CardStrategy.Last }, PartyGathered);

        CollectionAssert.DoesNotContain(walk.Scenes, Chapter01Ids.Dialogues.D07A);
        CollectionAssert.Contains(walk.RevealedBlocks, "chapter01.node.08_main_cattle_river");
        CollectionAssert.Contains(walk.RevealedBlocks, "chapter01.node.09.ulyana_main");
        CollectionAssert.Contains(walk.RevealedBlocks, "chapter01.node.09.lada_main");
    }

    [Test]
    public void AllInvestigations_KeepOriginalText()
    {
        GameState gameState = NewGame(20260924);
        Walk walk = PlayUntil(gameState, new Options { Cards = CardStrategy.First }, PartyGathered);

        CollectionAssert.IsSubsetOf(
            new[] { Chapter01Ids.Dialogues.D07A, Chapter01Ids.Dialogues.D07B, Chapter01Ids.Dialogues.D07C },
            walk.Scenes);
        CollectionAssert.Contains(walk.RevealedBlocks, "chapter01.node.08_main");
        CollectionAssert.Contains(walk.RevealedBlocks, "chapter01.node.09_main");
        CollectionAssert.DoesNotContain(walk.RevealedBlocks, "chapter01.node.09_main_partial");
    }

    // --- Правила дел в Доме ---

    [Test]
    public void NewGame_OpensMorningScene_WithoutCards()
    {
        NarrativeStateData state = new NarrativeStateData();

        Assert.AreEqual(Chapter01Ids.Dialogues.D01, Chapter01HomeActivities.GetAutoScene(state, true, Noon));
        Assert.IsEmpty(CardIds(state));
    }

    [Test]
    public void Flood_WaitsForNight()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        state.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);

        Assert.IsNull(Chapter01HomeActivities.GetAutoScene(state, true, Noon));
        Assert.IsTrue(Chapter01HomeActivities.IsWaitingForNight(state, true, Noon));
        Assert.AreEqual(Chapter01Ids.Dialogues.D04, Chapter01HomeActivities.GetAutoScene(state, true, Midnight));
        Assert.IsFalse(Chapter01HomeActivities.IsWaitingForNight(state, true, Midnight));
    }

    [Test]
    public void AfterFlood_DamInspectionIsUrgentCard()
    {
        NarrativeStateData state = new NarrativeStateData();
        state.SetFlag(Chapter01Ids.Flags.HomeIntroSeen);
        state.SetFlag(Chapter01Ids.Flags.HousePeopleMet);
        state.SetFlag(Chapter01Ids.Flags.FirstPressureSeen);
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);

        IReadOnlyList<Chapter01HomeActivity> cards = Chapter01HomeActivities.GetAvailable(state, true);
        Assert.AreEqual(1, cards.Count);
        Assert.AreEqual(Chapter01Ids.Dialogues.D05, cards[0].DialogueId);
        Assert.IsTrue(cards[0].Urgent);
    }

    [Test]
    public void Investigations_AreFreeOrder_SevenTeethAfterMillOrCattleAndRiver()
    {
        NarrativeStateData state = StateAfterWrongWater();
        CollectionAssert.AreEquivalent(
            new[] { Chapter01Ids.Dialogues.D07A, Chapter01Ids.Dialogues.D07B, Chapter01Ids.Dialogues.D07C },
            CardIds(state));

        state.SetFlag(Chapter01Ids.Flags.InvestigatedCattle);
        CollectionAssert.DoesNotContain(CardIds(state), Chapter01Ids.Dialogues.D08, "Одного скота мало.");

        state.SetFlag(Chapter01Ids.Flags.InvestigatedRiver);
        CollectionAssert.Contains(CardIds(state), Chapter01Ids.Dialogues.D08, "Скот и река — достаточно.");
        CollectionAssert.Contains(CardIds(state), Chapter01Ids.Dialogues.D07A, "Мельница остаётся делом по желанию.");

        NarrativeStateData millOnly = StateAfterWrongWater();
        millOnly.SetFlag(Chapter01Ids.Flags.InvestigatedMill);
        CollectionAssert.Contains(CardIds(millOnly), Chapter01Ids.Dialogues.D08, "Одной мельницы достаточно.");
    }

    [Test]
    public void Cards_DisappearWhenExpeditionStartsOrHeroIsAway()
    {
        NarrativeStateData state = StateAfterWrongWater();
        Assert.IsEmpty(Chapter01HomeActivities.GetAvailable(state, false), "Герой не дома — дел нет.");

        state.SetFlag(Chapter01Ids.Flags.ExpeditionStarted);
        Assert.IsEmpty(Chapter01HomeActivities.GetAvailable(state, true));
    }

    [Test]
    public void AutoOpen_StopsAfterPartyGatheringUntilExpeditionStarts()
    {
        GameState gameState = NewGame(20260924);
        PlayUntil(gameState, new Options(), PartyGathered);

        Assert.IsNull(Chapter01StoryDirector.GetAutoOpenHomeDialogueId(gameState));
        Assert.IsFalse(gameState.Narrative.HasFlag(Chapter01Ids.Flags.ExpeditionStarted));
    }

    [Test]
    public void AutoOpen_IsSilentDuringExpeditionPartOfChapter()
    {
        GameState gameState = NewGame(20260924);
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.ExpeditionStarted);

        Assert.IsNull(Chapter01StoryDirector.GetAutoOpenHomeDialogueId(gameState));
    }
}
