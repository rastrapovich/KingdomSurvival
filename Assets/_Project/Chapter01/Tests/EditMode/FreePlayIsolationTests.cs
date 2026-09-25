using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.FreePlay;
using NUnit.Framework;
using UnityEngine;

// ПР-12А (канон v1.45 §6.0): свободная игра — отдельный режим на тех же
// системах. Первая глава в ней не запускается ни дома, ни в дороге, ни в
// лагере; сюжетная кампания создаётся и работает как раньше.
public sealed class FreePlayIsolationTests
{
    private const int Seed = 20260925;

    [SetUp]
    public void SetUp()
    {
        Chapter01Content.Register();
        FreePlayContent.Register();
    }

    private static GameState NewFreePlay()
    {
        GameState state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = Seed }.CreateCampaign();
        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        return state;
    }

    private static void AdvanceHours(GameState state, double hours)
    {
        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.Advance(state, (float)(hours / ContinuousSimulationSystem.GameHoursPerRealSecond), false);
    }

    private static void AssertNoChapterContent(GameState state, string when)
    {
        Assert.IsNull(Chapter01StoryDirector.GetAutoOpenHomeDialogueId(state), when + ": сцена Дома главы.");
        Assert.IsEmpty(Chapter01HomeActivities.GetAvailable(state), when + ": дела Дома главы.");
        Assert.IsFalse(Chapter01HomeActivities.IsWaitingForNight(state), when + ": ожидание ночного события главы.");
        Assert.IsNull(Chapter01StoryDirector.GetPendingRoadEventDialogueId(state), when + ": дорожная сцена главы.");
        Assert.IsNull(Chapter01StoryDirector.GetPendingLocationNarrativeDialogueId(state), when + ": сцена места главы.");
        Assert.IsNull(Chapter01StoryDirector.GetPendingFordAccessDialogueId(state), when + ": сцена брода.");
        Assert.IsNull(Chapter01CampBattle.GetPendingDialogueId(state), when + ": бой главы у стоянки.");
        Assert.IsNull(Chapter01CampSceneProvider.GetAvailableScene(state), when + ": лагерная сцена главы.");
        Assert.IsFalse(state.Narrative.Flags.Any(f => f.StartsWith("chapter01.")),
            when + ": флаги главы: " + string.Join(", ", state.Narrative.Flags));
        Assert.IsFalse(Chronicle.Get(state).Entries.Any(e => e.Id.StartsWith("ch01.")), when + ": история главы.");
    }

    [Test]
    public void NewFreePlay_IsFreePlay_ChapterInactive_StartingPlaceKnown_GoalsNotEmpty()
    {
        GameState state = NewFreePlay();

        Assert.AreEqual(CampaignStartOptions.FreePlayId, state.Configuration.CrisisId);
        Assert.IsTrue(CampaignContent.IsFreePlay(state));
        Assert.IsFalse(Chapter01Crisis.IsActive(state));

        LocationData ruins = state.FindLocation(FreePlayContent.StartingKnownLocationId);
        Assert.IsNotNull(ruins);
        Assert.IsTrue(ruins.IsVisibleOnMap && ruins.IsDiscovered, "Одно место известно с начала.");

        IReadOnlyList<JournalGoalViewData> goals = CampaignContent.BuildGoals(state);
        Assert.IsTrue(goals.Any(g => g.Id == FreePlayContent.RegionGoalId && g.State == JournalGoalState.Active));
        Assert.IsTrue(goals.Any(g => g.Title == ruins.Name), "Известное место — в «Делах».");
        Assert.IsNotEmpty(CampaignContent.DescribeHomeObjects(state), "Дом не пустой.");
    }

    [Test]
    public void FreePlay_SeveralDaysAtHome_ChapterNeverStarts()
    {
        GameState state = NewFreePlay();
        for (int hour = 0; hour < 72; hour++)
        {
            Chapter01Chronicle.Refresh(state);
            FreePlayContent.Refresh(state);
            AssertNoChapterContent(state, "час " + hour);
            AdvanceHours(state, 1.0);
        }
        Assert.GreaterOrEqual(state.Day, 3);
    }

    [Test]
    public void FreePlay_Expedition_CampAndReturn_NoChapter_HistoryAndModeSurviveSave()
    {
        GameState state = NewFreePlay();
        state.ArmySupply = 50;
        Assert.IsTrue(state.TryStartExpedition(FreePlayContent.StartingKnownLocationId, new List<string> { "garrick" }, out string message), message);
        FreePlayContent.Refresh(state);
        AdvanceHours(state, 2.0);
        AssertNoChapterContent(state, "в пути");

        Assert.IsTrue(CampRest.TryStartRest(state, out message), message);
        for (int hour = 0; hour < 4; hour++)
        {
            AdvanceHours(state, 1.0);
            AssertNoChapterContent(state, "ночлег, час " + hour);
        }

        Assert.IsTrue(state.TryOrderReturn(out message), message);
        for (int step = 0; step < 400 && state.HasActiveExpedition; step++)
        {
            FreePlayContent.Refresh(state);
            AdvanceHours(state, 1.0);
        }
        Assert.IsFalse(state.HasActiveExpedition, "Отряд физически вернулся.");
        FreePlayContent.Refresh(state);
        FreePlayContent.Refresh(state);
        AssertNoChapterContent(state, "после возвращения");

        Assert.IsNotNull(Chronicle.Find(state, FreePlayContent.DeparturePrefix + "1"));
        ChronicleEntryData back = Chronicle.Find(state, FreePlayContent.ReturnPrefix + "1");
        Assert.IsNotNull(back);
        Assert.AreEqual(FreePlayContent.DeparturePrefix + "1", back.CauseId);
        Assert.AreEqual(2, Chronicle.Get(state).Entries.Count(e => e.Id.StartsWith("freeplay.")), "Одна пара записей на поход.");
        Assert.IsTrue(HomePeopleService.IsHomePresent(state, HomePeopleService.Find(state, "garrick")), "Гаррик снова дома.");

        GameState restored = CampaignSaveService.RestoreCampaign(
            JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        Assert.IsTrue(CampaignContent.IsFreePlay(restored), "Режим переживает сохранение.");
        Assert.AreEqual(state.Configuration.WorldSeed, restored.Configuration.WorldSeed);
        FreePlayContent.Refresh(restored);
        Assert.AreEqual(2, Chronicle.Get(restored).Entries.Count(e => e.Id.StartsWith("freeplay.")), "Загрузка не дублирует записи.");
        StringAssert.Contains("вернулся", CampaignContent.BuildGoals(restored).First(g => g.Id == FreePlayContent.RegionGoalId).CurrentStep);
    }

    [Test]
    public void FreePlay_ChapterEntryPoints_StayClosed_EvenWithChapterFlags()
    {
        GameState state = NewFreePlay();
        state.ArmySupply = 50;
        state.Narrative.SetFlag(Chapter01Ids.Flags.ExpeditionStarted);
        state.Narrative.SetFlag(Chapter01Ids.Flags.CartResolved);
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string> { "garrick" }, out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        Assert.IsTrue(CampRest.TryStartRest(state, out message), message);
        state.ActiveExpedition.ActiveActivity.RemainingHours -= 3.0;

        Assert.IsNull(Chapter01CampBattle.GetPendingDialogueId(state), "Бой главы — только в её кампании.");
        Assert.IsNull(Chapter01CampSceneProvider.GetAvailableScene(state));
        Assert.IsNull(Chapter01StoryDirector.GetLocationEntryDialogueId(state, target.Id));
        StringAssert.DoesNotContain("брод", Chapter01StoryDirector.DescribeCampPlace(state));
    }

    [Test]
    public void StoryCampaign_StillUsesChapterContent()
    {
        GameState story = new CampaignSetup { WorldSeed = Seed }.CreateCampaign();

        Assert.IsTrue(Chapter01Crisis.IsActive(story));
        Assert.IsFalse(CampaignContent.IsFreePlay(story));
        Assert.AreEqual(Chapter01HomeView.DescribeObjects(story).Count, CampaignContent.DescribeHomeObjects(story).Count);
        Assert.AreEqual(Chapter01JournalProvider.Build(story).Count, CampaignContent.BuildGoals(story).Count);
        Assert.IsFalse(story.FindLocation(FreePlayContent.StartingKnownLocationId)?.IsVisibleOnMap ?? false,
            "Стартовое место свободной игры не открывается в сюжетной кампании.");
    }

    [Test]
    public void StartOptions_OfferFreePlayAndStory_BothValid()
    {
        CollectionAssert.AreEqual(
            new[] { CampaignStartOptions.FreePlayId, CampaignStartOptions.HomeOnForeignWaterCrisisId },
            CampaignStartOptions.Crises.Select(c => c.Id));
        Assert.IsTrue(new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId }.Validate(out string reason), reason);
        Assert.IsTrue(new CampaignSetup().Validate(out reason), reason);
    }
}
