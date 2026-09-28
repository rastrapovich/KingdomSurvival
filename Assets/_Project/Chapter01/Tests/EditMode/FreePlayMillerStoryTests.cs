using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.Encounters;
using KingdomSurvival.FreePlay;
using NUnit.Framework;
using UnityEngine;

// ПР-12Б, история И-3 «Мешок с клеймом»: продолжение утверждённой встречи
// без правки её текста, гость у ворот через пять дней, обе ветки разговора,
// память в Хронике.
public sealed class FreePlayMillerStoryTests
{
    private const int Seed = 20260928;

    private DialogueDatabaseAsset dialogues;
    private EncounterDatabaseAsset encounters;

    [SetUp]
    public void SetUp()
    {
        Chapter01Content.Register();
        FreePlayContent.Register();
        dialogues = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
        encounters = Resources.Load<EncounterDatabaseAsset>(EncounterDatabaseAsset.ResourcesPath);
    }

    private static GameState NewFreePlay()
    {
        GameState state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = Seed }.CreateCampaign();
        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        state.Food = 40;
        return state;
    }

    private NarrativeDialogueView Play(GameState state, string dialogueId, params string[] choiceIds)
    {
        NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
        Assert.IsTrue(session.Start(dialogues, dialogueId, state.GetSelectedCommander().HeroProfile ?? new HeroProfileData(),
            state.Narrative, out NarrativeDialogueView view, out string error, new List<string>(), new List<string>(),
            state.WorldSeed, 1, state), error);
        foreach (string choiceId in choiceIds)
        {
            view = Skip(session, view);
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

    // Встреча на дороге: начало записано, диалог пройден выбранной веткой.
    private GameState AfterSack(bool takeGrain)
    {
        GameState state = NewFreePlay();
        EncounterDefinition sack = encounters.FindById(FreePlayMillerStory.EncounterId);
        EncounterRuntimeService.RecordEncounterStarted(state, sack, state.Day * 24);
        Play(state, sack.DialogueId, takeGrain ? "try" : "leave", "exit");
        FreePlayContent.RefreshWithReports(state);
        return state;
    }

    [Test]
    public void GuestScene_InDatabase_Valid_SackSceneUnchanged()
    {
        List<string> issues = new List<string>();
        dialogues.CollectValidationIssuesForDialogue(FreePlayMillerStory.GuestDialogueId, issues);
        Assert.IsEmpty(issues, string.Join("\n", issues));
        Assert.IsNotNull(dialogues.FindSpeaker("miller_son"));
        Assert.AreEqual(DialogueProductionStatus.Approved, dialogues.FindDialogue("road_miller_sack_01").Status, "Утверждённую сцену не трогаем.");
    }

    [Test]
    public void Sack_RememberedInChronicle_GuestComesAfterFiveDays()
    {
        GameState state = AfterSack(takeGrain: true);
        Assert.IsTrue(FreePlayMillerStory.TookGrain(state));
        StringAssert.Contains("подобрали крупу", Chronicle.Find(state, "freeplay.miller.chronicle.sack").Text);
        Assert.IsFalse(FreePlayMillerStory.IsGuestWaiting(state));
        Assert.IsNull(CampaignContent.BuildGoals(state).FirstOrDefault(g => g.Id == "freeplay.goal.miller_guest"));

        state.Day += FreePlayMillerStory.GuestDelayDays;
        HomeCareView care = CampaignContent.DescribeHomeCares(state).Single(c => c.Id == "home.care.freeplay_miller_guest");
        Assert.AreEqual(FreePlayMillerStory.GuestDialogueId, care.DialogueId);
        Assert.IsTrue(care.ActionEnabled, "Командир дома — можно выйти к гостю.");
        Assert.AreEqual(JournalGoalState.Active, CampaignContent.BuildGoals(state).Single(g => g.Id == "freeplay.goal.miller_guest").State);
    }

    [Test]
    public void TookGrain_ReturnIt_Friend()
    {
        GameState state = AfterSack(takeGrain: true);
        state.Day += FreePlayMillerStory.GuestDelayDays;
        NarrativeDialogueView view = Play(state, FreePlayMillerStory.GuestDialogueId);
        CollectionAssert.AreEquivalent(new[] { "freeplay.miller.guest.return", "freeplay.miller.guest.eaten" },
            view.AvailableChoices.Select(c => c.ChoiceId), "Взяли крупу — разговор о крупе.");

        int food = state.Food;
        Assert.IsNull(Play(state, FreePlayMillerStory.GuestDialogueId, "freeplay.miller.guest.return", "freeplay.miller.guest.returned_bye"));
        Assert.AreEqual(food - 2, state.Food);
        StringAssert.Contains("ушёл с поклоном", FreePlayContent.RefreshWithReports(state).Single());
        Assert.IsFalse(CampaignContent.DescribeHomeCares(state).Any(c => c.Id == "home.care.freeplay_miller_guest"));
        Assert.AreEqual(JournalGoalState.Completed, CampaignContent.BuildGoals(state).Single(g => g.Id == "freeplay.goal.miller_guest").State);
        Assert.IsEmpty(FreePlayContent.RefreshWithReports(state), "Донесение один раз.");
    }

    [Test]
    public void LeftSack_GuestBringsFlour_OrLeavesCold()
    {
        GameState fed = AfterSack(takeGrain: false);
        StringAssert.Contains("переложили выше от воды", Chronicle.Find(fed, "freeplay.miller.chronicle.sack").Text);
        fed.Day += FreePlayMillerStory.GuestDelayDays;
        int food = fed.Food;
        Assert.IsNull(Play(fed, FreePlayMillerStory.GuestDialogueId, "freeplay.miller.guest.table", "freeplay.miller.guest.fed_bye"));
        Assert.AreEqual(food + 4, fed.Food, "Мешочек муки от дальнего мельника.");
        Assert.IsTrue(fed.Narrative.HasFlag(FreePlayMillerStory.Flags.Friend));

        GameState cold = AfterSack(takeGrain: false);
        cold.Day += FreePlayMillerStory.GuestDelayDays;
        Play(cold, FreePlayMillerStory.GuestDialogueId, "freeplay.miller.guest.off", "freeplay.miller.guest.cold_bye");
        Assert.IsTrue(cold.Narrative.HasFlag(FreePlayMillerStory.Flags.Cool));
        StringAssert.Contains("не оглянувшись", FreePlayContent.RefreshWithReports(cold).Single());
        Assert.AreEqual("freeplay.miller.chronicle.sack", Chronicle.Find(cold, "freeplay.miller.chronicle.guest").CauseId);
    }

    [Test]
    public void NoEncounter_NoGuest()
    {
        GameState state = NewFreePlay();
        state.Day += 30;
        FreePlayContent.RefreshWithReports(state);
        Assert.IsFalse(FreePlayMillerStory.IsGuestWaiting(state));
        Assert.IsNull(Chronicle.Find(state, "freeplay.miller.chronicle.sack"));
    }
}
