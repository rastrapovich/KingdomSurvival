using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.FreePlay;
using NUnit.Framework;
using UnityEngine;

// ПР-12Б, история И-2 «Сухая шахта»: Остафий вспоминает о шахте после
// первого похода, отвалы и клеймо (Остафий в отряде читает сам), логово в
// дальней штольне с повтором после отхода, железо + уголь → топор, Остафий
// о клейме дома.
public sealed class FreePlayMineStoryTests
{
    private DialogueDatabaseAsset dialogues;

    [SetUp]
    public void SetUp()
    {
        Chapter01Content.Register();
        FreePlayContent.Register();
        dialogues = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
    }

    private static GameState NewFreePlay(int seed = 20260928)
    {
        GameState state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = seed }.CreateCampaign();
        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        state.Food = 40;
        state.ArmySupply = 30;
        return state;
    }

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

    private NarrativeDialogueView Play(GameState state, string dialogueId, params string[] choiceIds)
    {
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        Assert.IsTrue(session.Start(dialogues, dialogueId, state.GetSelectedCommander().HeroProfile ?? new HeroProfileData(),
            state.Narrative, out NarrativeDialogueView view, out string error, Party(state), new List<string>(),
            state.WorldSeed, 1 + (state.HasActiveExpedition ? state.ActiveExpedition.FighterIds.Count : 0), state), error);
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

    private static void Trip(GameState state)
    {
        int n = FreePlayContent.CountEntries(state, FreePlayContent.ReturnPrefix) + 1;
        Chronicle.Record(state, FreePlayContent.DeparturePrefix + n, "Выход в поход", "Отряд ушёл из Дома.");
        Chronicle.Record(state, FreePlayContent.ReturnPrefix + n, "Возвращение", "Отряд вернулся в Дом.");
    }

    private GameState AtMine(GameState state, params string[] party)
    {
        Trip(state);
        Play(state, FreePlayMineStory.OstafiyDialogueId, "freeplay.mine.ostafiy.go", "freeplay.mine.ostafiy.ok");
        FreePlayContent.RefreshWithReports(state);
        List<string> fighters = party.Where(id => state.FindFighter(id) != null).ToList();
        string retinue = party.FirstOrDefault(id => state.FindFighter(id) == null);
        Assert.IsTrue(state.TryStartExpedition(FreePlayMineStory.LocationId, fighters, out string message, retinue), message);
        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        return state;
    }

    private static CampaignBattleResult Result(GameState state, string battleId, CampaignBattleOutcome outcome)
    {
        CampaignBattleResult result = new CampaignBattleResult { BattleId = battleId, Outcome = outcome, Rounds = 3 };
        result.Survivors.Add(new CampaignBattleSurvivor { PersonId = state.GetSelectedCommander().Id, HitPoints = 20 });
        result.Survivors.Add(new CampaignBattleSurvivor { PersonId = "garrick", HitPoints = 20 });
        return result;
    }

    [Test]
    public void Scenes_InDatabase_Valid()
    {
        foreach (string id in new[] { FreePlayMineStory.OstafiyDialogueId, FreePlayMineStory.EntryDialogueId,
                     FreePlayMineStory.DeepDialogueId, FreePlayMineStory.MarkTalkDialogueId })
        {
            List<string> issues = new List<string>();
            dialogues.CollectValidationIssuesForDialogue(id, issues);
            Assert.IsEmpty(issues, id + ": " + string.Join("\n", issues));
        }
        Assert.IsNotNull(dialogues.FindSpeaker("torvin"));
    }

    [Test]
    public void OstafiyRemembers_AfterFirstTrip_MineOnMap()
    {
        GameState state = NewFreePlay();
        Assert.IsFalse(CampaignContent.DescribeHomeCares(state).Any(c => c.Id == "home.care.freeplay_mine_hint"), "До первого похода Остафий молчит.");
        Trip(state);
        HomeCareView hint = CampaignContent.DescribeHomeCares(state).Single(c => c.Id == "home.care.freeplay_mine_hint");
        Assert.AreEqual(FreePlayMineStory.OstafiyDialogueId, hint.DialogueId);

        Play(state, FreePlayMineStory.OstafiyDialogueId, "freeplay.mine.ostafiy.go", "freeplay.mine.ostafiy.ok");
        FreePlayContent.RefreshWithReports(state);
        LocationData mine = state.FindLocation(FreePlayMineStory.LocationId);
        Assert.IsTrue(mine.IsVisibleOnMap && mine.IsDiscovered);
        StringAssert.Contains("Дойти до старой шахты", CampaignContent.BuildGoals(state).Single(g => g.Id == "freeplay.goal.mine").CurrentStep);
        Assert.IsFalse(CampaignContent.BuildGoals(state).Any(g => g.Id == "freeplay.goal.place." + mine.Id), "Шахту ведёт история.");
        Assert.IsFalse(CampaignContent.DescribeHomeCares(state).Any(c => c.Id == "home.care.freeplay_mine_hint"));
    }

    [Test]
    public void Lair_RetreatThenReturn_VictoryGivesIron_AxeWithCoal()
    {
        GameState state = AtMine(NewFreePlay(), "garrick");
        Assert.AreEqual(FreePlayMineStory.EntryDialogueId, CampaignContent.LocationEntry(state, FreePlayMineStory.LocationId).DialogueId);
        Assert.IsNull(Play(state, FreePlayMineStory.EntryDialogueId, "freeplay.mine.entry.go_deep", "freeplay.mine.entry.deep_fight"));

        CampaignBattleRequest first = CampaignContent.BattleAfterDialogue(state, FreePlayMineStory.EntryDialogueId);
        Assert.IsNotNull(first);
        Assert.AreEqual(2, first.Enemies.Sum(e => e.Count));
        Assert.IsTrue(first.Enemies.All(e => e.UnitTypeId == "forest_beast"));
        Assert.IsTrue(first.AllowRetreat);
        CampaignBattleResult retreat = Result(state, first.BattleId, CampaignBattleOutcome.Retreat);
        CampaignBattleBridge.ApplyResult(state, retreat, new List<string>());
        List<string> reports = new List<string>();
        CampaignContent.OnBattleApplied(state, retreat, reports);
        StringAssert.Contains("Звери остались", reports.Single());

        Assert.AreEqual(FreePlayMineStory.DeepDialogueId, CampaignContent.LocationEntry(state, FreePlayMineStory.LocationId).DialogueId,
            "После отхода — вернуться в дальнюю штольню.");
        Assert.IsNull(CampaignContent.BattleAfterDialogue(state, FreePlayMineStory.EntryDialogueId), "Первый вход второй раз боя не даёт.");
        Assert.IsNull(Play(state, FreePlayMineStory.DeepDialogueId, "freeplay.mine.deep.go"));
        CampaignBattleRequest second = CampaignContent.BattleAfterDialogue(state, FreePlayMineStory.DeepDialogueId);
        Assert.AreNotEqual(first.BattleId, second.BattleId);

        CampaignBattleResult won = Result(state, second.BattleId, CampaignBattleOutcome.Victory);
        CampaignBattleBridge.ApplyResult(state, won, new List<string>());
        reports.Clear();
        CampaignContent.OnBattleApplied(state, won, reports);
        StringAssert.Contains("Дальняя штольня очищена", reports.Single());
        Assert.IsTrue(FreePlayMineStory.HasIron(state));
        Assert.IsNull(CampaignContent.LocationEntry(state, FreePlayMineStory.LocationId), "Дальше — обычный осмотр места.");

        Assert.IsEmpty(FreePlayContent.RefreshWithReports(state).Where(r => r.Contains("топор")), "Без угля топора нет.");
        state.Narrative.SetFlag(FreePlayCoalStory.Flags.Coal);
        StringAssert.Contains("топор", FreePlayContent.RefreshWithReports(state).Single(r => r.Contains("топор")));
        Assert.IsTrue(ItemService.Get(state).Items.Any(i => i.ItemId == ItemCatalog.Axe), "Топор в кладовой.");
        Assert.IsEmpty(FreePlayContent.RefreshWithReports(state).Where(r => r.Contains("топор")), "Один раз.");
    }

    [Test]
    public void Dumps_WithOstafiy_HeReadsTheMark_ThenTalksAtHome()
    {
        GameState state = null;
        for (int seed = 1; seed <= 80 && state == null; seed++)
        {
            GameState candidate = AtMine(NewFreePlay(seed), "garrick", HomePeopleService.OstafiyId);
            NarrativeDialogueView view = Play(candidate, FreePlayMineStory.EntryDialogueId, "freeplay.mine.entry.dumps");
            if (candidate.Narrative.HasFlag(FreePlayMineStory.Flags.IronDumps))
            {
                Assert.IsTrue(view.AvailableChoices.Any(c => c.ChoiceId == "freeplay.mine.entry.ask_ostafiy"), "Остафий в отряде — читает клеймо сам.");
                state = candidate;
            }
        }
        Assert.IsNotNull(state, "Хотя бы при одном seed отвалы дали железо.");

        // Ответ Остафия ставит флаг клейма (проверен выше, что он доступен).
        state.Narrative.SetFlag(FreePlayMineStory.Flags.Mark);
        FreePlayContent.RefreshWithReports(state);
        StringAssert.Contains("два угла", Chronicle.Find(state, "freeplay.mine.chronicle.mark").Text);
        Assert.IsNotNull(Chronicle.Find(state, "freeplay.mine.chronicle.iron"));

        HomeCareView talk = CampaignContent.DescribeHomeCares(state).Single(c => c.Id == "home.care.freeplay_mine_mark");
        Assert.AreEqual(FreePlayMineStory.MarkTalkDialogueId, talk.DialogueId);
        Assert.IsNull(Play(state, FreePlayMineStory.MarkTalkDialogueId, "freeplay.mine.mark_talk.remember"));
        CampaignContent.OnDialogueCompleted(state, FreePlayMineStory.MarkTalkDialogueId);
        StringAssert.Contains("старость любит сходства", Chronicle.Find(state, "freeplay.mine.chronicle.mark_talk").Text);
        Assert.IsFalse(CampaignContent.DescribeHomeCares(state).Any(c => c.Id == "home.care.freeplay_mine_mark"));
    }

    [Test]
    public void OstafiyMarkChoice_HiddenWithoutHim()
    {
        for (int seed = 1; seed <= 80; seed++)
        {
            GameState state = AtMine(NewFreePlay(seed), "garrick");
            NarrativeDialogueView view = Play(state, FreePlayMineStory.EntryDialogueId, "freeplay.mine.entry.dumps");
            if (!state.Narrative.HasFlag(FreePlayMineStory.Flags.IronDumps))
                continue;
            Assert.IsFalse(view.AvailableChoices.Any(c => c.ChoiceId == "freeplay.mine.entry.ask_ostafiy"));
            Assert.IsTrue(view.AvailableChoices.Any(c => c.ChoiceId == "freeplay.mine.entry.read_mark"), "Без Остафия — Расследование.");
            return;
        }
        Assert.Fail("Ни при одном seed отвалы не дали железо.");
    }
}
