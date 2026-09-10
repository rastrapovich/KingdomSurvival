using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using NUnit.Framework;

// P08J — Журнал целей v1. Chapter01JournalProvider.Build — чистое чтение
// GameState/NarrativeState, ничего не хранит и не решает; до FarRouteUnlocked
// журнал P08 обязан быть пуст, даже при искусственно выставленных
// optional-знаниях. Опциональные записи обязаны точно следовать уже
// существующему Chapter01StoryDirector.GetDepartureOptionalGoals — не
// дублировать условие вручную.
public sealed class Chapter01JournalTests
{
    private static GameState NewGameState()
    {
        GameState gameState = new GameState();
        gameState.CreateNewGame(20260910);
        return gameState;
    }

    private static JournalGoalViewData Find(IReadOnlyList<JournalGoalViewData> goals, string id)
    {
        foreach (JournalGoalViewData goal in goals)
        {
            if (goal.Id == id)
                return goal;
        }
        return null;
    }

    [Test]
    public void Build_NullGameState_ReturnsEmpty()
    {
        Assert.IsEmpty(Chapter01JournalProvider.Build(null));
    }

    [Test]
    public void Build_NullNarrative_ReturnsEmpty()
    {
        GameState gameState = NewGameState();
        gameState.Narrative = null;
        Assert.IsEmpty(Chapter01JournalProvider.Build(gameState));
    }

    // Раздел 9/37 инструкции: сначала FarRouteUnlocked, потом Journal goals —
    // даже искусственно выставленные optional-знания не должны давать записи
    // раньше решения N09.
    [Test]
    public void Build_BeforeFarRouteUnlocked_ReturnsEmpty_EvenWithOptionalKnowledge()
    {
        GameState gameState = NewGameState();
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.SecondLoafIsRation);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.OldCustom);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.SevenToothObject);

        Assert.IsEmpty(Chapter01JournalProvider.Build(gameState));
    }

    [Test]
    public void Build_OnlyFarRouteUnlocked_ReturnsOnlyMainGoal()
    {
        GameState gameState = NewGameState();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);

        IReadOnlyList<JournalGoalViewData> goals = Chapter01JournalProvider.Build(gameState);

        Assert.AreEqual(1, goals.Count);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail, goals[0].Id);
        Assert.AreEqual(JournalGoalCategory.Main, goals[0].Category);
        Assert.AreEqual(JournalGoalState.Active, goals[0].State);
    }

    [Test]
    public void Build_OnlySecondLoafKnowledge_AddsSecondLoafGoal_NotSevenTooth()
    {
        GameState gameState = NewGameState();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.SecondLoafIsRation);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.OldCustom);

        IReadOnlyList<JournalGoalViewData> goals = Chapter01JournalProvider.Build(gameState);

        Assert.AreEqual(2, goals.Count);
        Assert.IsNotNull(Find(goals, Chapter01Ids.JournalGoals.OldWaterTrail));
        Assert.IsNotNull(Find(goals, Chapter01Ids.JournalGoals.SecondLoaf));
        Assert.IsNull(Find(goals, Chapter01Ids.JournalGoals.SevenToothGauge));
    }

    [Test]
    public void Build_OnlySevenToothKnowledge_AddsSevenToothGoal_NotSecondLoaf()
    {
        GameState gameState = NewGameState();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.SevenToothObject);

        IReadOnlyList<JournalGoalViewData> goals = Chapter01JournalProvider.Build(gameState);

        Assert.AreEqual(2, goals.Count);
        Assert.IsNotNull(Find(goals, Chapter01Ids.JournalGoals.OldWaterTrail));
        Assert.IsNotNull(Find(goals, Chapter01Ids.JournalGoals.SevenToothGauge));
        Assert.IsNull(Find(goals, Chapter01Ids.JournalGoals.SecondLoaf));
    }

    [Test]
    public void Build_AllKnowledge_ReturnsOneMainAndTwoOptional()
    {
        GameState gameState = NewGameState();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.SecondLoafIsRation);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.OldCustom);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.SevenToothObject);

        IReadOnlyList<JournalGoalViewData> goals = Chapter01JournalProvider.Build(gameState);

        int mainCount = 0;
        int optionalCount = 0;
        foreach (JournalGoalViewData goal in goals)
        {
            if (goal.Category == JournalGoalCategory.Main)
                mainCount++;
            else
                optionalCount++;
        }

        Assert.AreEqual(3, goals.Count);
        Assert.AreEqual(1, mainCount);
        Assert.AreEqual(2, optionalCount);
    }

    // Защита от рассинхронизации D09 и Journal (раздел 38 инструкции):
    // набор опциональных Journal-целей обязан точно соответствовать
    // Chapter01StoryDirector.GetDepartureOptionalGoals для любой комбинации
    // знаний, а не дублировать условие отдельным кодом.
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void OptionalGoals_MatchStoryDirectorApi(bool loaf, bool tooth)
    {
        GameState gameState = NewGameState();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        if (loaf)
        {
            gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.SecondLoafIsRation);
            gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.OldCustom);
        }
        if (tooth)
            gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.SevenToothObject);

        IReadOnlyList<string> storyDirectorGoals = Chapter01StoryDirector.GetDepartureOptionalGoals(gameState.Narrative);
        IReadOnlyList<JournalGoalViewData> journalGoals = Chapter01JournalProvider.Build(gameState);

        int journalOptionalCount = 0;
        foreach (JournalGoalViewData goal in journalGoals)
        {
            if (goal.Category == JournalGoalCategory.Optional)
                journalOptionalCount++;
        }

        Assert.AreEqual(storyDirectorGoals.Count, journalOptionalCount);
        Assert.AreEqual(loaf, Find(journalGoals, Chapter01Ids.JournalGoals.SecondLoaf) != null);
        Assert.AreEqual(tooth, Find(journalGoals, Chapter01Ids.JournalGoals.SevenToothGauge) != null);
    }

    [Test]
    public void MainGoal_UpdatesCurrentStepAndRevision_WhenExpeditionStarted_SameId()
    {
        GameState gameState = NewGameState();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);

        JournalGoalViewData beforeExpedition = Find(Chapter01JournalProvider.Build(gameState), Chapter01Ids.JournalGoals.OldWaterTrail);
        Assert.IsNotNull(beforeExpedition);
        Assert.AreEqual("Собрать отряд и подготовиться к выходу.", beforeExpedition.CurrentStep);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail + ":prepare", beforeExpedition.RevisionId);

        gameState.Narrative.SetFlag(Chapter01Ids.Flags.ExpeditionStarted);

        JournalGoalViewData afterExpedition = Find(Chapter01JournalProvider.Build(gameState), Chapter01Ids.JournalGoals.OldWaterTrail);
        Assert.IsNotNull(afterExpedition);
        Assert.AreEqual("Следовать по старому ходу воды за пределы знакомых дорог.", afterExpedition.CurrentStep);
        Assert.AreEqual(Chapter01Ids.JournalGoals.OldWaterTrail + ":travel", afterExpedition.RevisionId);

        Assert.AreEqual(beforeExpedition.Id, afterExpedition.Id);
    }

    [Test]
    public void Build_NoDuplicateIds()
    {
        GameState gameState = NewGameState();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.SecondLoafIsRation);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.OldCustom);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.SevenToothObject);

        IReadOnlyList<JournalGoalViewData> goals = Chapter01JournalProvider.Build(gameState);

        HashSet<string> ids = new HashSet<string>();
        foreach (JournalGoalViewData goal in goals)
            Assert.IsTrue(ids.Add(goal.Id), "Дублирующийся JournalGoalViewData.Id: " + goal.Id);
    }

    // Journal Provider — чистое чтение: вызов Build не должен менять
    // NarrativeState (раздел 41 инструкции).
    [Test]
    public void Build_DoesNotMutateNarrativeState()
    {
        GameState gameState = NewGameState();
        gameState.Narrative.SetFlag(Chapter01Ids.Flags.FarRouteUnlocked);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.SecondLoafIsRation);
        gameState.Narrative.AddKnowledge(Chapter01Ids.Knowledge.OldCustom);

        List<string> flagsBefore = new List<string>(gameState.Narrative.Flags);
        List<string> knowledgeBefore = new List<string>(gameState.Narrative.Knowledge);

        Chapter01JournalProvider.Build(gameState);

        CollectionAssert.AreEqual(flagsBefore, gameState.Narrative.Flags);
        CollectionAssert.AreEqual(knowledgeBefore, gameState.Narrative.Knowledge);
    }
}
