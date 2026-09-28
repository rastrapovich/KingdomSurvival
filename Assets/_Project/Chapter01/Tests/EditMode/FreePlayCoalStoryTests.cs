using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.FreePlay;
using NUnit.Framework;
using UnityEngine;

// ПР-12Б, история И-1 «Уголь для Лады» (PR12BV_FREE_PLAY_CONTENT_SPEC.md §4,
// LORE.md §17.1): зацепка в Доме, разговор с Ладой, хутор на карте, три пути
// на хутор через настоящую сессию диалога, бой с вожаком и его итог,
// скорость ремонта без угля, сохранение.
public sealed class FreePlayCoalStoryTests
{
    private const int Seed = 20260928;

    private DialogueDatabaseAsset database;

    [SetUp]
    public void SetUp()
    {
        Chapter01Content.Register();
        FreePlayContent.Register();
        database = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
    }

    private static GameState NewFreePlay()
    {
        GameState state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = Seed }.CreateCampaign();
        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        state.Food = 40;
        state.ArmySupply = 30;
        return state;
    }

    private static bool Flag(GameState state, string flag) => state.Narrative.HasFlag(flag);

    private static List<string> Party(GameState state)
    {
        List<string> party = new List<string>();
        if (state.HasActiveExpedition)
        {
            party.AddRange(state.ActiveExpedition.FighterIds);
            if (state.ActiveExpedition.RetinueIds != null)
                party.AddRange(state.ActiveExpedition.RetinueIds);
        }
        return party;
    }

    // Проходит разговор: «читать дальше» пропускается, затем выбираются
    // ответы по порядку. Возвращает последний показ (null — разговор кончился).
    private NarrativeDialogueView Play(GameState state, string dialogueId, params string[] choiceIds)
    {
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        Assert.IsTrue(session.Start(database, dialogueId, state.GetSelectedCommander().HeroProfile ?? new HeroProfileData(),
            state.Narrative, out NarrativeDialogueView view, out string error, Party(state), new List<string>(),
            state.WorldSeed, 1 + (state.HasActiveExpedition ? state.ActiveExpedition.FighterIds.Count : 0), state), error);

        foreach (string choiceId in choiceIds)
        {
            view = SkipContinue(session, view);
            Assert.IsNotNull(view, "Разговор кончился раньше ответа " + choiceId);
            Assert.IsTrue(view.AvailableChoices.Any(c => c.ChoiceId == choiceId),
                "Нет ответа " + choiceId + ": " + string.Join(", ", view.AvailableChoices.Select(c => c.ChoiceId)));
            NarrativeDialogueSelectionResult result = session.SelectChoice(choiceId);
            view = result.DialogueEnded ? null : result.View;
        }
        return view == null ? null : SkipContinue(session, view);
    }

    private static NarrativeDialogueView SkipContinue(NarrativeDialogueRuntimeSession session, NarrativeDialogueView view)
    {
        for (int i = 0; i < 20 && view != null && view.AvailableChoices.Count == 1 &&
                        view.AvailableChoices[0].Kind == DialogueChoiceKind.Continue; i++)
        {
            NarrativeDialogueSelectionResult result = session.SelectChoice(view.AvailableChoices[0].ChoiceId);
            view = result.DialogueEnded ? null : result.View;
        }
        return view;
    }

    private GameState AtHutor(params string[] party)
    {
        GameState state = NewFreePlay();
        Play(state, FreePlayCoalStory.LadaDialogueId, "freeplay.coal.lada.go", "freeplay.coal.lada.take");
        FreePlayContent.RefreshWithReports(state);
        List<string> fighters = party.Where(id => state.FindFighter(id) != null).ToList();
        string retinue = party.FirstOrDefault(id => state.FindFighter(id) == null);
        Assert.IsTrue(state.TryStartExpedition(FreePlayCoalStory.LocationId, fighters, out string message, retinue), message);
        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        return state;
    }

    [Test]
    public void SeedScenes_AreInDatabase_AndValid()
    {
        foreach (string id in new[] { FreePlayCoalStory.LadaDialogueId, FreePlayCoalStory.HutorDialogueId, FreePlayCoalStory.HutorAfterDialogueId })
        {
            Assert.IsNotNull(database.FindDialogue(id), id);
            List<string> issues = new List<string>();
            database.CollectValidationIssuesForDialogue(id, issues);
            Assert.IsEmpty(issues, id + ": " + string.Join("\n", issues));
        }
        Assert.IsNotNull(database.FindSpeaker("coalburner"));
        Assert.IsNotNull(database.FindSpeaker("agnessa"));
    }

    [Test]
    public void Start_LadaCare_HalfSpeedRepair_NoHutorYet()
    {
        GameState state = NewFreePlay();
        Assert.IsNull(FreePlayCoalStory.FindHutor(state));
        Assert.AreEqual(FreePlayCoalStory.WorkRateWithoutCoal, CampaignContent.HomeWorkRate(state));

        JournalGoalViewData goal = CampaignContent.BuildGoals(state).Single(g => g.Id == "freeplay.goal.coal");
        Assert.AreEqual(JournalGoalState.Active, goal.State);
        StringAssert.Contains("Поговорить с Ладой", goal.CurrentStep);

        IReadOnlyList<HomeCareView> cares = CampaignContent.DescribeHomeCares(state);
        HomeCareView coal = cares.Single(c => c.Id == "home.care.freeplay_coal");
        Assert.AreEqual(HomeCareAction.OpenDialogue, coal.Action);
        Assert.AreEqual(FreePlayCoalStory.LadaDialogueId, coal.DialogueId);
        HomeCareView deck = cares.Single(c => c.Id == "home.care.yard_deck");
        Assert.AreEqual(HomeCareAction.StartYardDeck, deck.Action);
        StringAssert.Contains("вдвое медленнее", deck.Status);
    }

    [Test]
    public void LadaTalk_PutsHutorOnMap_NearForest_Reachable()
    {
        GameState state = NewFreePlay();
        Assert.IsNull(Play(state, FreePlayCoalStory.LadaDialogueId, "freeplay.coal.lada.go", "freeplay.coal.lada.take"));
        Assert.IsTrue(Flag(state, FreePlayCoalStory.Flags.Asked));
        FreePlayContent.RefreshWithReports(state);

        LocationData hutor = FreePlayCoalStory.FindHutor(state);
        Assert.IsNotNull(hutor);
        Assert.IsTrue(hutor.IsVisibleOnMap && hutor.IsDiscovered);
        LocationData forest = state.FindLocation(FreePlayCoalStory.ForestLocationId);
        float toForest = Distance(hutor.MapXPercent, hutor.MapYPercent, forest.MapXPercent, forest.MapYPercent);
        float homeToForest = Distance(WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent, forest.MapXPercent, forest.MapYPercent);
        Assert.Less(toForest, homeToForest * 0.5f, "Хутор у кромки леса.");
        Assert.Greater(WorldMapNavigation.FindPath(WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent,
            hutor.MapXPercent, hutor.MapYPercent).Count, 0, "До хутора есть путь.");

        StringAssert.Contains("Дойти до хутора", CampaignContent.BuildGoals(state).Single(g => g.Id == "freeplay.goal.coal").CurrentStep);
        Assert.IsFalse(CampaignContent.BuildGoals(state).Any(g => g.Id == "freeplay.goal.place." + FreePlayCoalStory.LocationId),
            "Хутор ведёт своя история, не отдельное «Дело» места.");
        FreePlayContent.RefreshWithReports(state);
        Assert.AreEqual(1, state.Locations.Count(l => l.Id == FreePlayCoalStory.LocationId), "Хутор не дублируется.");
    }

    private static float Distance(float ax, float ay, float bx, float by)
    {
        return Mathf.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }

    [Test]
    public void Trade_BreadForCoal_FullSpeed_GoalDone()
    {
        GameState state = AtHutor("garrick");
        Assert.AreEqual(FreePlayCoalStory.HutorDialogueId, CampaignContent.LocationEntry(state, FreePlayCoalStory.LocationId).DialogueId);
        int food = state.Food;

        Assert.IsNull(Play(state, FreePlayCoalStory.HutorDialogueId, "freeplay.coal.hutor.trade", "freeplay.coal.hutor.trade_leave"));
        Assert.AreEqual(food - FreePlayCoalStory.TradeFood, state.Food);
        Assert.IsNull(CampaignContent.BattleAfterDialogue(state, FreePlayCoalStory.HutorDialogueId), "Обмен — без боя.");

        List<string> reports = FreePlayContent.RefreshWithReports(state);
        Assert.IsTrue(FreePlayCoalStory.HasCoal(state));
        StringAssert.Contains("в обмен на хлеб", reports.Single());
        Assert.AreEqual(1.0, CampaignContent.HomeWorkRate(state));
        Assert.AreEqual(JournalGoalState.Completed, CampaignContent.BuildGoals(state).Single(g => g.Id == "freeplay.goal.coal").State);
        Assert.IsNull(CampaignContent.LocationEntry(state, FreePlayCoalStory.LocationId), "Уголь получен — сцена не повторяется.");
        Assert.IsFalse(CampaignContent.DescribeHomeCares(state).Any(c => c.Id == "home.care.freeplay_coal"));
        Assert.IsEmpty(FreePlayContent.RefreshWithReports(state), "Донесение один раз.");
    }

    [Test]
    public void Fence_OnlyWithLada_CostsSupplies_GivesCoal()
    {
        GameState without = AtHutor("garrick");
        NarrativeDialogueView view = Play(without, FreePlayCoalStory.HutorDialogueId);
        Assert.IsFalse(view.AvailableChoices.Any(c => c.ChoiceId == "freeplay.coal.hutor.fence"), "Без Лады изгородь не поставить.");
        Assert.IsTrue(view.DisabledChoices.Any(c => c.ChoiceId == "freeplay.coal.hutor.fence"), "Но видно, что так можно.");

        GameState state = AtHutor("garrick", HomePeopleService.LadaId);
        int supplies = state.ArmySupply;
        Assert.IsNull(Play(state, FreePlayCoalStory.HutorDialogueId, "freeplay.coal.hutor.fence", "freeplay.coal.hutor.fence_leave"));
        Assert.AreEqual(supplies - FreePlayCoalStory.FenceSupplies, state.ArmySupply);
        StringAssert.Contains("огневую изгородь", FreePlayContent.RefreshWithReports(state).Single());
        Assert.IsTrue(FreePlayCoalStory.HasCoal(state));
    }

    [Test]
    public void Hunt_StartsBattleWithLeader_PreparedWithAgnessa()
    {
        GameState plain = AtHutor("garrick");
        Assert.IsNull(Play(plain, FreePlayCoalStory.HutorDialogueId, "freeplay.coal.hutor.hunt", "freeplay.coal.hutor.wait"));
        CampaignBattleRequest request = CampaignContent.BattleAfterDialogue(plain, FreePlayCoalStory.HutorDialogueId);
        Assert.IsNotNull(request);
        Assert.AreEqual(FreePlayCoalStory.BattleId, request.BattleId);
        Assert.IsTrue(request.AllowRetreat);
        CollectionAssert.AreEquivalent(new[] { "forest_beast_alpha", "forest_beast" }, request.Enemies.Select(e => e.UnitTypeId));
        Assert.IsFalse(request.PreparedStart, "Тропы не нашли, Агнессы нет — ждут у ям.");
        Assert.IsNull(CampaignContent.LocationEntry(plain, FreePlayCoalStory.LocationId), "Пока идёт бой, сцена не открывается.");

        GameState scout = AtHutor("garrick", CampRest.AgnessaId);
        Play(scout, FreePlayCoalStory.HutorDialogueId, "freeplay.coal.hutor.hunt", "freeplay.coal.hutor.wait");
        Assert.IsTrue(CampaignContent.BattleAfterDialogue(scout, FreePlayCoalStory.HutorDialogueId).PreparedStart, "Агнесса знает тропу.");

        scout.Narrative.SetFlag(FreePlayCoalStory.Flags.Trail);
        plain.Narrative.SetFlag(FreePlayCoalStory.Flags.Trail);
        Assert.IsTrue(FreePlayCoalStory.CreateRequest(plain).PreparedStart, "Найденная тропа — подготовленное начало.");
    }

    private static CampaignBattleResult Result(GameState state, CampaignBattleOutcome outcome)
    {
        CampaignBattleResult result = new CampaignBattleResult { BattleId = FreePlayCoalStory.BattleId, Outcome = outcome, Rounds = 3 };
        result.Survivors.Add(new CampaignBattleSurvivor { PersonId = state.GetSelectedCommander().Id, HitPoints = 20 });
        result.Survivors.Add(new CampaignBattleSurvivor { PersonId = "garrick", HitPoints = 20 });
        return result;
    }

    [Test]
    public void Victory_DrivesLeaderAway_Coal()
    {
        GameState state = AtHutor("garrick");
        Play(state, FreePlayCoalStory.HutorDialogueId, "freeplay.coal.hutor.hunt", "freeplay.coal.hutor.wait");
        CampaignBattleResult result = Result(state, CampaignBattleOutcome.Victory);
        Assert.AreEqual(CampaignBattleApplyStatus.SquadSurvived, CampaignBattleBridge.ApplyResult(state, result, new List<string>()));
        List<string> reports = new List<string>();
        CampaignContent.OnBattleApplied(state, result, reports);

        Assert.IsTrue(Flag(state, FreePlayCoalStory.Flags.LeaderDriven));
        StringAssert.Contains("Вожак ушёл", reports.Single());
        StringAssert.Contains("за отогнанного вожака", FreePlayContent.RefreshWithReports(state).Single());
        Assert.IsTrue(FreePlayCoalStory.HasCoal(state));
    }

    [Test]
    public void Retreat_HutorMoves_TradeTwiceAsDear()
    {
        GameState state = AtHutor("garrick");
        Play(state, FreePlayCoalStory.HutorDialogueId, "freeplay.coal.hutor.hunt", "freeplay.coal.hutor.wait");
        CampaignBattleResult result = Result(state, CampaignBattleOutcome.Retreat);
        CampaignBattleBridge.ApplyResult(state, result, new List<string>());
        List<string> reports = new List<string>();
        CampaignContent.OnBattleApplied(state, result, reports);

        Assert.IsTrue(Flag(state, FreePlayCoalStory.Flags.HutorMoved));
        Assert.IsFalse(FreePlayCoalStory.HasCoal(state));
        Assert.IsNotNull(Chronicle.Find(state, "freeplay.coal.chronicle.moved"));
        StringAssert.Contains("вдвое дороже", CampaignContent.BuildGoals(state).Single(g => g.Id == "freeplay.goal.coal").CurrentStep);
        Assert.IsNull(CampaignContent.BattleAfterDialogue(state, FreePlayCoalStory.HutorDialogueId), "Второго боя из этой сцены нет.");

        LocationEntryView entry = CampaignContent.LocationEntry(state, FreePlayCoalStory.LocationId);
        Assert.AreEqual(FreePlayCoalStory.HutorAfterDialogueId, entry.DialogueId);
        int food = state.Food;
        Assert.IsNull(Play(state, FreePlayCoalStory.HutorAfterDialogueId, "freeplay.coal.after.pay", "freeplay.coal.after.go"));
        Assert.AreEqual(food - FreePlayCoalStory.TradeFoodAfterRetreat, state.Food);
        FreePlayContent.RefreshWithReports(state);
        Assert.IsTrue(FreePlayCoalStory.HasCoal(state));
    }

    [Test]
    public void YardDeck_WithoutCoal_HalfSpeed_WithCoal_Full()
    {
        GameState slow = NewFreePlay();
        GameState fast = NewFreePlay();
        fast.Narrative.SetFlag(FreePlayCoalStory.Flags.Coal);
        slow.Gold = fast.Gold = 1000;
        Assert.IsTrue(HomeLife.TryStartYardDeck(slow, out string message), message);
        Assert.IsTrue(HomeLife.TryStartYardDeck(fast, out message), message);

        HomeLife.Advance(slow, 4.0, new List<string>());
        HomeLife.Advance(fast, 4.0, new List<string>());
        double slowDone = HomeLife.FindWork(slow, HomeLife.YardDeckWorkId).DoneWork;
        double fastDone = HomeLife.FindWork(fast, HomeLife.YardDeckWorkId).DoneWork;
        Assert.Greater(fastDone, 0.0);
        Assert.AreEqual(fastDone * FreePlayCoalStory.WorkRateWithoutCoal, slowDone, 0.0001, "Без угля — вдвое медленнее.");
    }

    [Test]
    public void StoryCampaign_NotAffected()
    {
        GameState story = new CampaignSetup { WorldSeed = Seed }.CreateCampaign();
        Assert.AreEqual(1.0, CampaignContent.HomeWorkRate(story), "Глава: скорость работ прежняя.");
        Assert.IsNull(CampaignContent.LocationEntry(story, FreePlayCoalStory.LocationId));
        Assert.IsEmpty(CampaignContent.Refresh(story));
    }

    [Test]
    public void HutorAndCoal_SurviveSave_NoDuplicateChronicle()
    {
        GameState state = AtHutor("garrick");
        Play(state, FreePlayCoalStory.HutorDialogueId, "freeplay.coal.hutor.trade", "freeplay.coal.hutor.trade_leave");
        FreePlayContent.RefreshWithReports(state);

        GameState restored = CampaignSaveService.RestoreCampaign(
            JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        Assert.IsNotNull(FreePlayCoalStory.FindHutor(restored));
        Assert.IsTrue(FreePlayCoalStory.HasCoal(restored));
        Assert.IsEmpty(FreePlayContent.RefreshWithReports(restored), "Загрузка не повторяет донесение.");
        Assert.AreEqual(1, Chronicle.Get(restored).Entries.Count(e => e.Id == "freeplay.coal.chronicle.coal"));
    }
}
