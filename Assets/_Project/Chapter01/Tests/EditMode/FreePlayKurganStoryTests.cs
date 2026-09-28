using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.Encounters;
using KingdomSurvival.FreePlay;
using NUnit.Framework;
using UnityEngine;

// ПР-12Б, история И-4 «Разрытый курган»: находка кургана (осмотр руин или
// «Свежая земля»), следы, семья у костра и её приём в Дом, находка,
// разрытый курган и «кто-то ходит» у Дома.
public sealed class FreePlayKurganStoryTests
{
    private DialogueDatabaseAsset dialogues;

    [SetUp]
    public void SetUp()
    {
        Chapter01Content.Register();
        FreePlayContent.Register();
        dialogues = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
    }

    private static GameState NewFreePlay()
    {
        GameState state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = 20260928 }.CreateCampaign();
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

    private static GameState AtKurgan(params string[] fighters)
    {
        GameState state = NewFreePlay();
        state.FindLocation(FreePlayKurganStory.RuinsLocationId).IsExplored = true;
        FreePlayContent.RefreshWithReports(state);
        Assert.IsTrue(state.TryStartExpedition(FreePlayKurganStory.LocationId, fighters.ToList(), out string message), message);
        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        return state;
    }

    [Test]
    public void Scenes_Valid_EncounterFreePlayOnly()
    {
        foreach (string id in new[] { FreePlayKurganStory.MoundDialogueId, FreePlayKurganStory.WalkingDialogueId, FreePlayKurganStory.FreshEarthDialogueId })
        {
            List<string> issues = new List<string>();
            dialogues.CollectValidationIssuesForDialogue(id, issues);
            Assert.IsEmpty(issues, id + ": " + string.Join("\n", issues));
        }
        Assert.IsNotNull(dialogues.FindSpeaker("refugee"));
        Assert.IsNotNull(dialogues.FindSpeaker("refugee_woman"));
        EncounterDefinition earth = Resources.Load<EncounterDatabaseAsset>(EncounterDatabaseAsset.ResourcesPath)
            .FindById("FREEPLAY_KURGAN_FRESH_EARTH_01");
        CollectionAssert.Contains(earth.RequiredFlagsAll, FreePlayCoalStory.ModeFlag);
        CollectionAssert.Contains(earth.ForbiddenFlags, FreePlayKurganStory.Flags.Known);
    }

    [Test]
    public void RuinsExplored_KurganAppears_NearRuins_Reachable()
    {
        GameState state = NewFreePlay();
        FreePlayContent.RefreshWithReports(state);
        Assert.IsNull(FreePlayKurganStory.FindKurgan(state), "До осмотра руин кургана нет.");

        state.FindLocation(FreePlayKurganStory.RuinsLocationId).IsExplored = true;
        StringAssert.Contains("разрытый курган", FreePlayContent.RefreshWithReports(state).Single());
        LocationData kurgan = FreePlayKurganStory.FindKurgan(state);
        Assert.IsTrue(kurgan.IsVisibleOnMap && kurgan.IsDiscovered);
        Assert.Greater(WorldMapNavigation.FindPath(WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent,
            kurgan.MapXPercent, kurgan.MapYPercent).Count, 0);
        Assert.AreEqual(JournalGoalState.Active, CampaignContent.BuildGoals(state).Single(g => g.Id == "freeplay.goal.kurgan").State);
        Assert.IsFalse(CampaignContent.BuildGoals(state).Any(g => g.Id == "freeplay.goal.place." + kurgan.Id));
        Assert.IsEmpty(FreePlayContent.RefreshWithReports(state), "Один раз.");
    }

    [Test]
    public void FreshEarth_RevealsKurgan()
    {
        GameState state = NewFreePlay();
        Assert.IsNull(Play(state, FreePlayKurganStory.FreshEarthDialogueId, "freeplay.kurgan.earth.mark", "freeplay.kurgan.earth.go"));
        FreePlayContent.RefreshWithReports(state);
        Assert.IsNotNull(FreePlayKurganStory.FindKurgan(state));
    }

    [Test]
    public void WithAgnessa_FamilyJoinsHome_ReturnsFind_NoWalking()
    {
        GameState state = AtKurgan("garrick", CampRest.AgnessaId);
        Assert.AreEqual(FreePlayKurganStory.MoundDialogueId, CampaignContent.LocationEntry(state, FreePlayKurganStory.LocationId).DialogueId);
        NarrativeDialogueView start = Play(state, FreePlayKurganStory.MoundDialogueId);
        Assert.IsFalse(start.AvailableChoices.Any(c => c.ChoiceId == "freeplay.kurgan.mound.track"), "Агнесса читает следы сама — без проверки.");

        int population = state.Population;
        Assert.IsNull(Play(state, FreePlayKurganStory.MoundDialogueId,
            "freeplay.kurgan.mound.follow_agnessa", "freeplay.kurgan.mound.home_return", "freeplay.kurgan.mound.joined_returned_go"));
        List<string> reports = FreePlayContent.RefreshWithReports(state);
        Assert.IsTrue(reports.Any(r => r.Contains("Гордей, Злата и маленький Мишка")));
        Assert.AreEqual(population + 3, state.Population);
        Assert.IsNotNull(state.FindFighter(FreePlayKurganStory.GordeyId), "Гордей — боец Дома.");
        Assert.IsTrue(FreePlayKurganStory.IsMoundFilled(state));
        Assert.IsNull(CampaignContent.LocationEntry(state, FreePlayKurganStory.LocationId));

        state.Day += 20;
        Assert.IsEmpty(FreePlayContent.RefreshWithReports(state), "Курган засыпан — у Дома тихо.");
        Assert.AreEqual(JournalGoalState.Completed, CampaignContent.BuildGoals(state).Single(g => g.Id == "freeplay.goal.kurgan").State);
        Assert.IsFalse(FreePlayContent.RefreshWithReports(state).Any(), "Семья принимается один раз.");
    }

    [Test]
    public void LetGo_MoundOpen_SomeoneWalks_AnswerAtHome()
    {
        GameState state = AtKurgan("garrick", CampRest.AgnessaId);
        Play(state, FreePlayKurganStory.MoundDialogueId,
            "freeplay.kurgan.mound.follow_agnessa", "freeplay.kurgan.mound.let_go", "freeplay.kurgan.mound.let_go_bye");
        FreePlayContent.RefreshWithReports(state);
        StringAssert.Contains("остался разрытым", Chronicle.Find(state, "freeplay.kurgan.chronicle.outcome").Text);

        state.Day += FreePlayKurganStory.WalkingDelayDays - 1;
        Assert.IsEmpty(FreePlayContent.RefreshWithReports(state));
        state.Day += 1;
        StringAssert.Contains("собаки лают", FreePlayContent.RefreshWithReports(state).Single());
        HomeCareView care = CampaignContent.DescribeHomeCares(state).Single(c => c.Id == "home.care.freeplay_kurgan_walking");
        Assert.AreEqual(FreePlayKurganStory.WalkingDialogueId, care.DialogueId);
        StringAssert.Contains("кто-то ходит", CampaignContent.BuildGoals(state).Single(g => g.Id == "freeplay.goal.kurgan").CurrentStep.ToLowerInvariant());

        int food = state.Food;
        Assert.IsNull(Play(state, FreePlayKurganStory.WalkingDialogueId, "freeplay.kurgan.walking.fill", "freeplay.kurgan.walking.filled_ok"));
        Assert.AreEqual(food - 3, state.Food);
        StringAssert.Contains("больше не лают", FreePlayContent.RefreshWithReports(state).Single());
        Assert.IsTrue(FreePlayKurganStory.IsMoundFilled(state));
        Assert.IsFalse(CampaignContent.DescribeHomeCares(state).Any(c => c.Id == "home.care.freeplay_kurgan_walking"));
        Assert.AreEqual(JournalGoalState.Completed, CampaignContent.BuildGoals(state).Single(g => g.Id == "freeplay.goal.kurgan").State);
    }

    [Test]
    public void TakeFind_Gold_Once_WithoutAgnessa_TrackIsACheck()
    {
        GameState state = AtKurgan("garrick");
        NarrativeDialogueView start = Play(state, FreePlayKurganStory.MoundDialogueId);
        Assert.IsTrue(start.AvailableChoices.Any(c => c.ChoiceId == "freeplay.kurgan.mound.track"), "Без Агнессы — Следопытство.");
        Assert.IsFalse(start.AvailableChoices.Any(c => c.ChoiceId == "freeplay.kurgan.mound.follow_agnessa"));

        state.Narrative.SetFlag(FreePlayKurganStory.Flags.TookFind);
        state.Narrative.SetFlag(FreePlayKurganStory.Flags.FamilyLeft);
        int gold = state.Gold;
        Assert.IsTrue(FreePlayContent.RefreshWithReports(state).Any(r => r.Contains("выменяли")));
        Assert.AreEqual(gold + FreePlayKurganStory.FindGold, state.Gold);
        FreePlayContent.RefreshWithReports(state);
        Assert.AreEqual(gold + FreePlayKurganStory.FindGold, state.Gold, "Один раз.");
    }

    [Test]
    public void FillYourself_NoWalking()
    {
        GameState state = AtKurgan("garrick");
        Assert.IsNull(Play(state, FreePlayKurganStory.MoundDialogueId, "freeplay.kurgan.mound.fill", "freeplay.kurgan.mound.filled_go"));
        FreePlayContent.RefreshWithReports(state);
        state.Day += 20;
        Assert.IsEmpty(FreePlayContent.RefreshWithReports(state));
        StringAssert.Contains("засыпан", Chronicle.Find(state, "freeplay.kurgan.chronicle.outcome").Text);
    }
}
