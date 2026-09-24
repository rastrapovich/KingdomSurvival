using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using NUnit.Framework;
using UnityEngine;

// «Игрок» для скриптовых прогонов главы (ПР-02). Делает то же, что в Play
// Mode: открывает сцены в порядке опроса PrototypeUIController (LateUpdate:
// возвращение → сцены домашней части, которые открываются сами;
// RefreshAutoTimeState: дорога → место), выбирает дела в Доме, сам выходит
// в поход, исследует область, идёт к людям ниже по течению и входит к ним,
// а между этим двигает настоящее игровое время через
// ContinuousSimulationSystem.Advance — в том числе ждёт ночи для N04/N06.
// Если нет ни сцены, ни дела, ни действия, ни хода времени — тупик, тест падает.
public static class Chapter01PlaythroughWalker
{
    private const int MaxIterations = 20000;
    private const int MaxStepsPerScene = 300;
    private const double AdvanceStepGameHours = 1.0;

    public enum CardStrategy
    {
        First,
        Last,
        // Самый короткий путь: дела, ведущие дальше (N08/N09), раньше остальных.
        Shortest
    }

    public sealed class Walk
    {
        public readonly List<string> Scenes = new List<string>();
        public readonly List<double> SceneHours = new List<double>();
        public readonly List<string> Actions = new List<string>();
        public readonly List<string> Cards = new List<string>();
        public readonly HashSet<string> RevealedBlocks = new HashSet<string>();
        public string Path => string.Join(" → ", Scenes);

        public double HourWhenOpened(string dialogueId)
        {
            int index = Scenes.IndexOf(dialogueId);
            Assert.GreaterOrEqual(index, 0, dialogueId + " не открывался. Путь: " + Path);
            return SceneHours[index];
        }
    }

    public sealed class Options
    {
        public bool PreferLastAnswer;
        public int CouncilChoice;
        public CardStrategy Cards = CardStrategy.First;
        // ПР-06Б: ответ семье у ворот (по умолчанию — принять всех).
        public bool DeclineFamily;
    }

    public static GameState NewGame(int seed)
    {
        GameState gameState = new GameState();
        gameState.CreateNewGame(seed);
        if (gameState.Narrative == null)
            gameState.Narrative = new NarrativeStateData();
        return gameState;
    }

    public static Walk PlayUntil(GameState gameState, Options options, Func<GameState, bool> stop)
    {
        DialogueDatabaseAsset database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(database, "Не найдена база диалогов Resources/" + DialogueDatabaseAsset.ResourcesPath + ".asset");

        Walk walk = new Walk();
        float stepSeconds = (float)(AdvanceStepGameHours / ContinuousSimulationSystem.GameHoursPerRealSecond);

        for (int i = 0; i < MaxIterations; i++)
        {
            if (stop(gameState))
                return walk;

            Assert.IsFalse(gameState.HasPendingExpeditionDecision, "Неожиданное решение похода.\nПуть: " + walk.Path);

            string scene = NextScene(gameState, options, walk);
            if (!string.IsNullOrEmpty(scene))
            {
                PlayScene(gameState, database, scene, options, walk);
                continue;
            }

            if (TryMapAction(gameState, walk))
                continue;

            bool timeShouldRun = ContinuousSimulationSystem.HasMovementOrActivityInProgress(gameState) ||
                                 Chapter01HomeActivities.IsWaitingForNight(gameState);
            Assert.IsTrue(timeShouldRun,
                "Тупик: нет сцены, дела, действия и хода времени.\nПуть: " + walk.Path +
                "\nДела: " + string.Join(", ", walk.Cards) +
                "\nДействия: " + string.Join(", ", walk.Actions) +
                "\nФаза похода: " + (gameState.HasActiveExpedition ? gameState.ActiveExpedition.Phase.ToString() : "нет"));

            ContinuousSimulationSystem.SetPaused(gameState, false);
            ContinuousSimulationSystem.Advance(gameState, stepSeconds, false);
        }

        Assert.Fail("Прогон не завершился за " + MaxIterations + " итераций.\nПуть: " + walk.Path);
        return walk;
    }

    public static Walk PlayWholeChapter(GameState gameState, Options options)
    {
        return PlayUntil(gameState, options, g => Chapter01StoryDirector.IsChapterComplete(g.Narrative));
    }

    // Зеркало PrototypeUIController.RefreshChapter01ReturnFlow, включая его
    // побочные действия (физический поворот домой, цена обратной дороги).
    private static string NextReturnFlowScene(GameState gameState)
    {
        NarrativeStateData state = gameState.Narrative;
        Chapter01ReturnFlow.EnsureAgreementKnowledge(state);

        if (Chapter01StoryDirector.CanOpenFinalCouncil(state))
            return Chapter01Ids.Dialogues.D17;
        if (state.HasFlag(Chapter01Ids.Flags.DownstreamContact) &&
            !state.HasFlag(Chapter01Ids.Flags.AgreementRevealed))
            return Chapter01Ids.Dialogues.D14;
        if (state.HasFlag(Chapter01Ids.Flags.AgreementRevealed) &&
            !state.HasFlag(Chapter01Ids.Flags.ReturnStarted))
            return Chapter01Ids.Dialogues.D14Half;
        if (!state.HasFlag(Chapter01Ids.Flags.ReturnStarted))
            return null;

        Chapter01ReturnFlow.EnsurePhysicalReturn(gameState, out _);
        if (Chapter01ReturnFlow.IsReturnRoadSceneReady(gameState))
        {
            Chapter01ReturnFlow.TryMarkRoadEchoReported(state);
            return Chapter01Ids.Dialogues.D15;
        }

        if (state.HasFlag(Chapter01Ids.Flags.ReturnRoadTraveled))
            Chapter01ReturnFlow.TryApplyReturnRoadConsequence(gameState);
        if (!Chapter01ReturnFlow.IsHomecomingReady(gameState))
            return null;

        Chapter01ReturnFlow.TryMarkHomeEchoReported(state);
        return Chapter01Ids.Dialogues.D16;
    }

    // Сцена, которую игра откроет сама, или очевидный клик игрока: лагерь
    // после телеги, вход в поселение, дело в Доме.
    private static string NextScene(GameState gameState, Options options, Walk walk)
    {
        string scene = NextReturnFlowScene(gameState);
        if (!string.IsNullOrEmpty(scene))
            return scene;

        scene = Chapter01StoryDirector.GetAutoOpenHomeDialogueId(gameState);
        if (!string.IsNullOrEmpty(scene))
            return scene;

        Chapter01StoryDirector.RefreshRoadState(gameState);
        scene = Chapter01StoryDirector.GetPendingRoadEventDialogueId(gameState);
        if (!string.IsNullOrEmpty(scene))
            return scene;

        scene = Chapter01StoryDirector.GetPendingLocationNarrativeDialogueId(gameState);
        if (!string.IsNullOrEmpty(scene))
            return scene;

        scene = Chapter01StoryDirector.GetPendingFordAccessDialogueId(gameState);
        if (!string.IsNullOrEmpty(scene))
            return scene;

        CampSceneViewData? camp = Chapter01CampSceneProvider.GetAvailableScene(gameState);
        if (camp.HasValue)
            return camp.Value.DialogueId;

        if (gameState.HasActiveExpedition)
        {
            scene = Chapter01StoryDirector.GetLocationEntryDialogueId(gameState, gameState.ActiveExpedition.LocationId);
            if (!string.IsNullOrEmpty(scene))
                return scene;
        }

        IReadOnlyList<Chapter01HomeActivity> cards = Chapter01HomeActivities.GetAvailable(gameState);
        if (cards.Count > 0)
        {
            Chapter01HomeActivity card = PickCard(cards, options.Cards);
            walk.Cards.Add(card.Title);
            return card.DialogueId;
        }

        return null;
    }

    private static Chapter01HomeActivity PickCard(IReadOnlyList<Chapter01HomeActivity> cards, CardStrategy strategy)
    {
        switch (strategy)
        {
            case CardStrategy.Last:
                return cards[cards.Count - 1];
            case CardStrategy.Shortest:
                foreach (Chapter01HomeActivity card in cards)
                {
                    if (card.DialogueId == Chapter01Ids.Dialogues.D08 || card.DialogueId == Chapter01Ids.Dialogues.D09)
                        return card;
                }
                return cards[0];
            default:
                return cards[0];
        }
    }

    // Действия игрока на карте. True — что-то сделано.
    private static bool TryMapAction(GameState gameState, Walk walk)
    {
        NarrativeStateData state = gameState.Narrative;

        if (state.HasFlag(Chapter01Ids.Flags.PartyGatheringSeen) &&
            !state.HasFlag(Chapter01Ids.Flags.ExpeditionStarted) &&
            !gameState.HasActiveExpedition)
        {
            List<string> fighters = gameState.Fighters.Take(2).Select(f => f.Id).ToList();
            gameState.ArmySupply = 1000;
            Assert.IsTrue(gameState.TryStartExpedition(
                Chapter01Ids.Locations.OldWaterSearch, fighters, out string message), message);
            Chapter01StoryDirector.HandleStoryExpeditionStarted(gameState);
            walk.Actions.Add("выход в поход");
            return true;
        }

        if (!gameState.HasActiveExpedition)
            return false;

        ExpeditionData expedition = gameState.ActiveExpedition;
        if (expedition.Phase == CommanderState.AtLocation &&
            expedition.LocationId == Chapter01Ids.Locations.OldWaterSearch &&
            !expedition.HasTimedActivity)
        {
            LocationData oldWater = gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch);
            if (oldWater != null && !oldWater.IsExplored)
            {
                Assert.IsTrue(gameState.TryStartLocationResearch(out string message), message);
                walk.Actions.Add("исследование области");
                return true;
            }
        }

        LocationData downstream = gameState.FindLocation(Chapter01Ids.Locations.DownstreamSettlement);
        if (downstream != null &&
            state.HasFlag(Chapter01Ids.Flags.OldFordFound) &&
            !state.HasFlag(Chapter01Ids.Flags.DownstreamContact) &&
            expedition.LocationId != downstream.Id &&
            !expedition.HasTimedActivity)
        {
            Assert.IsTrue(gameState.TryChangeExpeditionRoute(
                downstream.MapXPercent, downstream.MapYPercent, downstream.Id, out string message), message);
            walk.Actions.Add("путь к людям ниже по течению");
            return true;
        }

        return false;
    }

    private static void PlayScene(
        GameState gameState,
        DialogueDatabaseAsset database,
        string dialogueId,
        Options options,
        Walk walk)
    {
        walk.Scenes.Add(dialogueId);
        walk.SceneHours.Add(ContinuousSimulationSystem.GetClock(gameState).HourOfDay);

        // Те же аргументы, что в PrototypeUIController.TryOpenNarrativeDialogueById.
        List<string> companions = Chapter01ContextBuilder.GetPresentCompanionIds(gameState);
        List<string> items = new List<string>();
        if (gameState.Narrative.Items != null)
            items.AddRange(gameState.Narrative.Items);

        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        bool started = session.Start(
            database, dialogueId, gameState.GetSelectedCommander().HeroProfile ?? new HeroProfileData(),
            gameState.Narrative, out NarrativeDialogueView view, out string error,
            companions, items, gameState.WorldSeed,
            Chapter01ContextBuilder.GetPartySize(gameState), gameState);
        Assert.IsTrue(started, dialogueId + ": " + error + "\nПуть: " + walk.Path);

        for (int step = 0; step < MaxStepsPerScene; step++)
        {
            foreach (NarrativeDialogueVisibleBlock block in view.VisibleTextBlocks)
                walk.RevealedBlocks.Add(block.BlockId);

            Assert.IsNotEmpty(view.AvailableChoices,
                dialogueId + "/" + view.NodeId + ": нет доступных ответов.\nПуть: " + walk.Path);
            int index = PickAnswer(dialogueId, view.AvailableChoices, options);
            NarrativeDialogueSelectionResult result = session.SelectChoice(view.AvailableChoices[index].ChoiceId);
            if (result.DialogueEnded)
            {
                // То же, что UI делает при закрытии сцены.
                Chapter01StoryDirector.HandleDialogueCompleted(gameState, dialogueId);
                if (dialogueId == Chapter01Ids.Dialogues.D10)
                    gameState.Narrative.SetFlag(Chapter01Ids.Flags.PartyGatheringSeen);
                return;
            }
            view = result.View;
        }

        Assert.Fail(dialogueId + ": сцена не завершилась за " + MaxStepsPerScene + " шагов.\nПуть: " + walk.Path);
    }

    // Единственный ответ — берём его; на Совете — заданный исход; иначе
    // стратегия «первый» или «последний».
    private static int PickAnswer(string dialogueId, IReadOnlyList<NarrativeDialogueChoiceView> choices, Options options)
    {
        if (choices.Count == 1)
            return 0;
        if (dialogueId == Chapter01Ids.Dialogues.D17)
            return Mathf.Min(options.CouncilChoice, choices.Count - 1);
        // Семья у ворот: «Потом поговорим»/«Дайте подумать» оставили бы
        // предложение открытым — «игрок» отвечает по существу.
        if (dialogueId == Chapter01Ids.Dialogues.GateFamily)
            return options.DeclineFamily && choices.Count == 3 ? 1 : 0;
        return options.PreferLastAnswer ? choices.Count - 1 : 0;
    }
}
