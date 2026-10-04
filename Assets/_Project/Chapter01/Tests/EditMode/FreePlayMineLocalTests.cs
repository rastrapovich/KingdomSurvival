using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.FreePlay;
using KingdomSurvival.UnitDatabase;
using NUnit.Framework;
using UnityEngine;

// ПР-12К (канон v1.53 §28.3, §28.6): пилот «Старая шахта» на локальной карте.
// Прежние итоги истории (железо, клеймо, логово, топор) идут через те же
// флаги; бой — на месте с конкретными зверями; после победы и выхода
// звери и железо не возвращаются; старое завершённое сохранение не
// сбрасывается; Остафий отвечает, только если он здесь.
public sealed class FreePlayMineLocalTests
{
    private DialogueDatabaseAsset dialogues;

    [SetUp]
    public void SetUp()
    {
        Chapter01Content.Register();
        FreePlayContent.Register();
        dialogues = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
    }

    private static GameState NewFreePlay(int seed = 20261004)
    {
        GameState state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = seed }.CreateCampaign();
        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        state.Food = 40;
        state.ArmySupply = 30;
        return state;
    }

    private static GameState AtMine(GameState state, params string[] party)
    {
        state.Narrative.SetFlag(FreePlayMineStory.Flags.Known);
        FreePlayContent.RefreshWithReports(state);
        List<string> fighters = party.Where(id => state.FindFighter(id) != null).ToList();
        string retinue = party.FirstOrDefault(id => state.FindFighter(id) == null);
        Assert.IsTrue(state.TryStartExpedition(FreePlayMineStory.LocationId, fighters, out string message, retinue), message);
        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        return state;
    }

    private NarrativeDialogueView Play(GameState state, string dialogueId, params string[] choiceIds)
    {
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        Assert.IsTrue(session.Start(dialogues, dialogueId, state.GetSelectedCommander().HeroProfile ?? new HeroProfileData(),
            state.Narrative, out NarrativeDialogueView view, out string error,
            Chapter01ContextBuilder.GetPresentCompanionIds(state), new List<string>(),
            state.WorldSeed, Chapter01ContextBuilder.GetPartySize(state), state), error);
        foreach (string choiceId in choiceIds)
        {
            view = Skip(session, view);
            Assert.IsNotNull(view, "Разговор кончился раньше ответа " + choiceId);
            Assert.IsTrue(view.AvailableChoices.Any(c => c.ChoiceId == choiceId),
                "Нет ответа " + choiceId + ": " + string.Join(", ", view.AvailableChoices.Select(c => c.ChoiceId)));
            NarrativeDialogueSelectionResult result = session.SelectChoice(choiceId);
            view = result.DialogueEnded ? null : result.View;
        }
        return view == null ? null : Skip(session, view);
    }

    private static NarrativeDialogueView Skip(NarrativeDialogueRuntimeSession session, NarrativeDialogueView view)
    {
        for (int i = 0; i < 20 && view != null && view.AvailableChoices.Count == 1 &&
                        view.AvailableChoices[0].Kind == DialogueChoiceKind.Continue; i++)
        {
            NarrativeDialogueSelectionResult result = session.SelectChoice(view.AvailableChoices[0].ChoiceId);
            view = result.DialogueEnded ? null : result.View;
        }
        return view;
    }

    private static Dictionary<string, LocalCellData> CellsNearPassage(GameState state)
    {
        LocalCellData[] cells = { new LocalCellData(6, 3), new LocalCellData(5, 3), new LocalCellData(5, 4), new LocalCellData(4, 3), new LocalCellData(4, 4) };
        Dictionary<string, LocalCellData> result = new Dictionary<string, LocalCellData>();
        int index = 0;
        foreach (string id in PartyPresence.BattleCandidateIds(state))
            result[id] = cells[index++];
        return result;
    }

    private static List<string> Validate(LocalLocationDefinition definition)
    {
        BattlefieldDatabaseAsset battlefields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
        DialogueDatabaseAsset dialogueDatabase = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        UnitDatabaseAsset units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
        return LocalLocationValidator.Validate(definition, battlefields,
            id => dialogueDatabase.FindDialogue(id) != null,
            id => units.FindById(id) != null);
    }

    [Test]
    public void MineDefinition_AndInspectorDatabase_PassValidation()
    {
        List<string> errors = Validate(FreePlayMineLocal.Create());
        Assert.IsEmpty(errors, string.Join("\n", errors));

        LocalLocationDatabaseAsset asset = Resources.Load<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(asset, "База исследуемых мест для Inspector заведена.");
        LocalLocationDefinition mine = asset.locations.Single(location => location.Id == FreePlayMineLocal.LocalLocationId);
        errors = Validate(mine);
        Assert.IsEmpty(errors, string.Join("\n", errors));
        Assert.IsTrue(mine.PlaceholderArt, "Фон шахты — заглушка, рисунка нет.");
    }

    // Основания предметов из Базы локаций — те же препятствия, что стены:
    // предмет на входе делает место непригодным, а не «проходимым насквозь».
    [Test]
    public void VisualFootprints_AreObstacles_ForValidation()
    {
        BattlefieldDatabaseAsset battlefields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
        LocalLocationDefinition mine = FreePlayMineLocal.Create();
        List<string> errors = LocalLocationValidator.Validate(mine, battlefields, null, null,
            new[] { new KingdomSurvival.BattleSandbox.HexCoord(0, 3) });
        Assert.IsTrue(errors.Any(error => error.Contains("вход")), string.Join("\n", errors));

        LocalLocationGeometry geometry = new LocalLocationGeometry(mine, battlefields.FindById(mine.BattlefieldId),
            new[] { new KingdomSurvival.BattleSandbox.HexCoord(3, 3) });
        Assert.IsFalse(geometry.IsPassable(new KingdomSurvival.BattleSandbox.HexCoord(3, 3)));
        CollectionAssert.Contains(geometry.BlockedCells, new KingdomSurvival.BattleSandbox.HexCoord(3, 3),
            "Бой на месте получает те же клетки.");
    }

    [Test]
    public void MineScenes_InDatabase_Valid()
    {
        foreach (string id in new[] { FreePlayMineLocal.DumpsDialogueId, FreePlayMineLocal.MarkDialogueId,
                     FreePlayMineLocal.SignsDialogueId, FreePlayMineLocal.LairDialogueId })
        {
            List<string> issues = new List<string>();
            dialogues.CollectValidationIssuesForDialogue(id, issues);
            Assert.IsEmpty(issues, id + ": " + string.Join("\n", issues));
        }
    }

    [Test]
    public void LocalMap_OnlyInFreePlay_AtTheMine()
    {
        GameState free = NewFreePlay();
        Assert.IsNotNull(LocalLocationCatalog.ForWorldLocation(free, FreePlayMineStory.LocationId));
        Assert.IsNull(LocalLocationCatalog.ForWorldLocation(free, "ruins"), "Остальные места — прежний текстовый вход.");

        GameState chapter = new CampaignSetup { CrisisId = CampaignStartOptions.HomeOnForeignWaterCrisisId, WorldSeed = 7 }.CreateCampaign();
        Assert.IsNull(LocalLocationCatalog.ForWorldLocation(chapter, FreePlayMineStory.LocationId), "В главе шахта — как раньше.");
    }

    [Test]
    public void Dumps_GiveIronOnce_ThroughTheExistingFlag()
    {
        for (int seed = 1; seed <= 80; seed++)
        {
            GameState state = AtMine(NewFreePlay(seed), "garrick");
            LocalLocationDefinition mine = LocalLocationCatalog.ForWorldLocation(state, FreePlayMineStory.LocationId);
            Assert.IsTrue(LocalExplorationService.Enter(state, mine, null, out string reason), reason);
            LocalObjectDefinition dumps = mine.FindObject(FreePlayMineLocal.DumpsObjectId);
            Assert.IsTrue(LocalExplorationService.IsObjectAvailable(state, mine, dumps));

            Assert.IsTrue(LocalExplorationService.MarkInteraction(state, mine, dumps));
            NarrativeDialogueView after = Play(state, FreePlayMineLocal.DumpsDialogueId, "freeplay.mine.local.dumps.search");
            Assert.AreEqual(1, after.AvailableChoices.Count, "После проверки — один ответ, закрывающий сцену.");
            Assert.IsTrue(state.Narrative.HasFlag(FreePlayMineStory.Flags.Entered));
            Assert.IsFalse(LocalExplorationService.IsObjectAvailable(state, mine, dumps), "Отвалы перебирают один раз.");
            Assert.IsFalse(LocalExplorationService.MarkInteraction(state, mine, dumps));
            if (!state.Narrative.HasFlag(FreePlayMineStory.Flags.IronDumps))
                continue;

            FreePlayContent.RefreshWithReports(state);
            Assert.IsTrue(FreePlayMineStory.HasIron(state));
            Assert.IsNotNull(Chronicle.Find(state, "freeplay.mine.chronicle.iron"), "Хроника о железе — прежняя.");
            return;
        }
        Assert.Fail("Ни при одном seed отвалы не дали железа.");
    }

    [Test]
    public void Mark_OstafiyAnswersOnlyWhenPresent()
    {
        GameState without = AtMine(NewFreePlay(), "garrick");
        NarrativeDialogueView view = Play(without, FreePlayMineLocal.MarkDialogueId);
        Assert.IsFalse(view.AvailableChoices.Any(c => c.ChoiceId == "freeplay.mine.local.mark.ask_ostafiy"), "Остафия нет — он не отвечает.");
        Assert.IsTrue(view.AvailableChoices.Any(c => c.ChoiceId == "freeplay.mine.local.mark.read"));

        GameState with = AtMine(NewFreePlay(), "garrick", HomePeopleService.OstafiyId);
        Assert.IsNull(Play(with, FreePlayMineLocal.MarkDialogueId, "freeplay.mine.local.mark.ask_ostafiy", "freeplay.mine.local.mark.ostafiy_done"));
        Assert.IsTrue(with.Narrative.HasFlag(FreePlayMineStory.Flags.Mark));
        FreePlayContent.RefreshWithReports(with);
        StringAssert.Contains("два угла", Chronicle.Find(with, "freeplay.mine.chronicle.mark").Text);
    }

    [Test]
    public void Lair_IsFoughtOnTheSpot_WithTheseBeasts_AndNeverReturns()
    {
        GameState state = AtMine(NewFreePlay(), "garrick", CampRest.AgnessaId);
        LocalLocationDefinition mine = LocalLocationCatalog.ForWorldLocation(state, FreePlayMineStory.LocationId);
        Assert.IsTrue(LocalExplorationService.Enter(state, mine, null, out _));
        LocalEncounterDefinition lair = LocalExplorationService.EncounterAt(state, mine, 6, 3);
        Assert.IsNotNull(lair, "Шаг за проход поднимает зверей.");
        Assert.IsNull(LocalExplorationService.EncounterAt(state, mine, 4, 3), "У входа тихо.");

        CampaignBattleRequest request = LocalExplorationService.BuildEncounterRequest(state, mine, lair, CellsNearPassage(state));
        Assert.IsTrue(request.IsLocal);
        Assert.AreEqual(FreePlayMineLocal.BattlefieldId, request.BattlefieldId);
        StringAssert.StartsWith(FreePlayMineStory.LairBattlePrefix, request.BattleId);
        Assert.AreEqual(2, request.Enemies.Count);
        Assert.IsTrue(request.Enemies.All(enemy => enemy.UnitTypeId == "forest_beast" && enemy.HasCell && !string.IsNullOrEmpty(enemy.InstanceId)));
        Assert.IsTrue(request.AllowRetreat);
        Assert.AreEqual(PartyPresence.IsPresent(state, CampRest.AgnessaId), request.PreparedStart, "Подготовленное начало — если Агнесса здесь.");

        CampaignBattleResult won = new CampaignBattleResult
        {
            BattleId = request.BattleId,
            Outcome = CampaignBattleOutcome.Victory,
            Rounds = 3,
            SourceKind = CampaignBattleSourceKind.Local,
            LocalLocationId = mine.Id,
            EncounterId = lair.Id
        };
        won.Survivors.Add(new CampaignBattleSurvivor { PersonId = state.GetSelectedCommander().Id, HitPoints = 10, HasCell = true, CellQ = 7, CellR = 3 });
        foreach (CampaignBattleEnemy enemy in request.Enemies)
            won.Enemies.Add(new CampaignBattleEnemyRecord { UnitTypeId = enemy.UnitTypeId, InstanceId = enemy.InstanceId, Defeated = true, MaxHitPoints = 10 });
        Assert.AreEqual(CampaignBattleApplyStatus.SquadSurvived, CampaignBattleBridge.ApplyResult(state, won, new List<string>()));
        List<string> reports = new List<string>();
        CampaignContent.OnBattleApplied(state, won, reports);
        StringAssert.Contains("Дальняя штольня очищена", reports.Single());
        Assert.IsTrue(FreePlayMineStory.HasIron(state), "Железо из забоя — прежний итог.");

        LocalExplorationService.Exit(state);
        Assert.IsTrue(LocalExplorationService.Enter(state, mine, null, out _));
        Assert.IsEmpty(LocalExplorationService.AliveEnemies(state, mine), "Звери не возрождаются.");
        Assert.IsNull(LocalExplorationService.EncounterAt(state, mine, 6, 3));
        Assert.IsTrue(LocalExplorationService.IsObjectAvailable(state, mine, mine.FindObject(FreePlayMineLocal.FaceObjectId)),
            "Сухой забой виден после очищения.");
        Assert.IsFalse(LocalExplorationService.IsObjectAvailable(state, mine, mine.FindObject(FreePlayMineLocal.SignsObjectId)));
    }

    [Test]
    public void OldSave_WithClearedLair_StaysCleared_OnTheLocalMap()
    {
        GameState state = AtMine(NewFreePlay(), "garrick");
        // Партия до ПР-12К: всё сделано старым путём, данных места нет.
        state.Narrative.SetFlag(FreePlayMineStory.Flags.Entered);
        state.Narrative.SetFlag(FreePlayMineStory.Flags.IronDumps);
        state.Narrative.SetFlag(FreePlayMineStory.Flags.LairCleared);
        LocalLocationDefinition mine = LocalLocationCatalog.ForWorldLocation(state, FreePlayMineStory.LocationId);

        Assert.IsTrue(LocalExplorationService.Enter(state, mine, null, out _));

        Assert.IsEmpty(LocalExplorationService.AliveEnemies(state, mine), "Очищенное логово не наполняется зверями.");
        Assert.IsFalse(LocalExplorationService.IsObjectAvailable(state, mine, mine.FindObject(FreePlayMineLocal.DumpsObjectId)),
            "Отвалы уже перебраны — железо не выдаётся второй раз.");
        Assert.IsNull(LocalExplorationService.EncounterAt(state, mine, 6, 3));
    }
}
