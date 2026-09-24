using System;
using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// ПР-02: домашняя часть главы N01–N10 идёт сама, без Debug. Скриптовый
// прогон против настоящего KingdomSurvivalDialogues.asset: берёт сцену из
// Chapter01StoryDirector.GetAutoOpenHomeDialogueId (как UI), проигрывает её
// headless с выбранной стратегией ответов и применяет те же последствия,
// что и UI при закрытии сцены. Доказывает, что при разных ответах и
// проверках глава не застревает до сбора отряда (в том числе у гейта N09).
public sealed class Chapter01HomeFlowTests
{
    private const int MaxScenes = 40;
    private const int MaxStepsPerScene = 200;

    private static DialogueDatabaseAsset LoadDatabase()
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база диалогов Resources/" + DialogueDatabaseAsset.ResourcesPath + ".asset");
        return database;
    }

    private static GameState NewGame(int seed)
    {
        GameState gameState = new GameState();
        gameState.CreateNewGame(seed);
        if (gameState.Narrative == null)
            gameState.Narrative = new NarrativeStateData();
        return gameState;
    }

    private static HeroProfileData HeroOf(GameState gameState)
    {
        CommanderData commander = gameState.GetSelectedCommander();
        if (commander == null)
            return new HeroProfileData();
        if (commander.HeroProfile == null)
            commander.HeroProfile = new HeroProfileData();
        return commander.HeroProfile;
    }

    // Проходит домашнюю часть так, как её показывает UI. Возвращает
    // порядок открытых сцен; бросает, если глава упёрлась в тупик.
    private static List<string> PlayHomePart(GameState gameState, Func<IReadOnlyList<NarrativeDialogueChoiceView>, int> pickChoice)
    {
        DialogueDatabaseAsset database = LoadDatabase();
        List<string> opened = new List<string>();

        for (int scene = 0; scene < MaxScenes; scene++)
        {
            string dialogueId = Chapter01StoryDirector.GetAutoOpenHomeDialogueId(gameState);
            if (string.IsNullOrEmpty(dialogueId))
                return opened;

            opened.Add(dialogueId);

            // Те же аргументы, что в PrototypeUIController.TryOpenNarrativeDialogueById.
            List<string> presentItemIds = new List<string>();
            if (gameState.Narrative.Items != null)
                presentItemIds.AddRange(gameState.Narrative.Items);

            NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
            bool started = session.Start(
                database, dialogueId, HeroOf(gameState), gameState.Narrative,
                out NarrativeDialogueView view, out string error,
                new List<string>(), presentItemIds, gameState.WorldSeed,
                Chapter01ContextBuilder.GetPartySize(gameState), gameState);
            Assert.IsTrue(started, dialogueId + ": " + error);

            bool ended = false;
            for (int step = 0; step < MaxStepsPerScene && !ended; step++)
            {
                Assert.IsNotEmpty(view.AvailableChoices, dialogueId + "/" + view.NodeId + ": нет доступных ответов.");
                int index = pickChoice(view.AvailableChoices);
                NarrativeDialogueSelectionResult result = session.SelectChoice(view.AvailableChoices[index].ChoiceId);
                ended = result.DialogueEnded;
                if (!ended)
                    view = result.View;
            }
            Assert.IsTrue(ended, dialogueId + ": сцена не завершилась за " + MaxStepsPerScene + " шагов.");

            // То же, что делает UI при закрытии сцены.
            Chapter01StoryDirector.HandleDialogueCompleted(gameState, dialogueId);
            if (dialogueId == Chapter01Ids.Dialogues.D10)
                gameState.Narrative.SetFlag(Chapter01Ids.Flags.PartyGatheringSeen);
        }

        Assert.Fail("Домашняя часть не закончилась за " + MaxScenes + " сцен: " + string.Join(" → ", opened));
        return opened;
    }

    private static int First(IReadOnlyList<NarrativeDialogueChoiceView> choices) => 0;
    private static int Last(IReadOnlyList<NarrativeDialogueChoiceView> choices) => choices.Count - 1;

    [TestCase("first", 20260924)]
    [TestCase("last", 20260924)]
    [TestCase("first", 7)]
    [TestCase("last", 7)]
    public void HomePart_ReachesPartyGathering_WithoutDebug(string strategy, int seed)
    {
        GameState gameState = NewGame(seed);
        Func<IReadOnlyList<NarrativeDialogueChoiceView>, int> pick = strategy == "first"
            ? (Func<IReadOnlyList<NarrativeDialogueChoiceView>, int>)First
            : Last;

        List<string> opened = PlayHomePart(gameState, pick);
        NarrativeStateData state = gameState.Narrative;
        string path = string.Join(" → ", opened);

        Assert.AreEqual(Chapter01Ids.Dialogues.D01, opened[0], "Новая партия должна начинаться с N01.");
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.FarRouteUnlocked),
            "Гейт N09 не открылся — глава застряла дома. Путь: " + path);
        Assert.IsTrue(state.HasFlag(Chapter01Ids.Flags.PartyGatheringSeen), "N10 не показан. Путь: " + path);
        Assert.AreEqual(Chapter01Ids.Dialogues.D10, opened[opened.Count - 1], "Путь: " + path);
        Assert.AreNotEqual(Chapter01RepairChoice.None, Chapter01StoryDirector.GetRepairChoice(state));
    }

    [Test]
    public void HomePart_DoesNotReopenAnyScene()
    {
        GameState gameState = NewGame(20260924);
        List<string> opened = PlayHomePart(gameState, First);

        Assert.AreEqual(new HashSet<string>(opened).Count, opened.Count, string.Join(" → ", opened));
    }

    [Test]
    public void AutoOpen_StopsAfterPartyGatheringUntilExpeditionStarts()
    {
        GameState gameState = NewGame(20260924);
        PlayHomePart(gameState, First);

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
