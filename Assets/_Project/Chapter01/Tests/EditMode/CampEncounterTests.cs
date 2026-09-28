using System.Linq;
using KingdomSurvival.Chapter01;
using KingdomSurvival.Encounters;
using KingdomSurvival.FreePlay;
using NUnit.Framework;
using UnityEngine;

// ПР-12В: встречи в лагере — одна возможность на ночлег после первого часа
// сна, пул CAMP_POOL_01; первая лагерная встреча «Следы вожака» — только
// пока вожак жив и ходит у дороги или хутор ушёл.
public sealed class CampEncounterTests
{
    private EncounterDatabaseAsset encounters;

    [SetUp]
    public void SetUp()
    {
        Chapter01Content.Register();
        FreePlayContent.Register();
        encounters = Resources.Load<EncounterDatabaseAsset>(EncounterDatabaseAsset.ResourcesPath);
    }

    private static GameState Resting(double hoursIntoRest)
    {
        GameState state = new CampaignSetup { CrisisId = CampaignStartOptions.FreePlayId, WorldSeed = 20260928 }.CreateCampaign();
        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        state.ArmySupply = 50;
        LocationData target = state.Locations.First(l => !l.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, new System.Collections.Generic.List<string> { "garrick" }, out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        Assert.IsTrue(CampRest.TryStartRest(state, out message), message);
        state.ActiveExpedition.ActiveActivity.RemainingHours -= hoursIntoRest;
        FreePlayContent.RefreshWithReports(state);
        return state;
    }

    [Test]
    public void Opportunity_OncePerNight_AfterFirstHour()
    {
        Assert.IsNull(CampEncounters.Opportunity(Resting(0.5)), "Первый час — рано.");
        GameState state = Resting(1.5);
        EncounterOpportunity opportunity = CampEncounters.Opportunity(state);
        Assert.IsNotNull(opportunity);
        Assert.AreEqual(CampEncounterIds.PoolId, opportunity.PoolId);
        state.Encounters.MarkOpportunityProcessed(opportunity.OpportunityId);
        Assert.IsNull(CampEncounters.Opportunity(state), "Одна возможность на ночлег.");
    }

    [Test]
    public void CampPool_Seeded_LeaderSignsOnlyWhileLeaderRoams()
    {
        Assert.IsNotNull(encounters.FindPool(CampEncounterIds.PoolId));
        EncounterDefinition signs = encounters.FindById("FREEPLAY_COAL_LEADER_SIGNS_01");
        Assert.IsNotNull(signs);
        Assert.AreEqual(EncounterCategory.Camp, signs.Category);
        KingdomSurvival.DialogueDatabase.DialogueDatabaseAsset dialogues =
            Resources.Load<KingdomSurvival.DialogueDatabase.DialogueDatabaseAsset>(KingdomSurvival.DialogueDatabase.DialogueDatabaseAsset.ResourcesPath);
        System.Collections.Generic.List<string> issues = new System.Collections.Generic.List<string>();
        dialogues.CollectValidationIssuesForDialogue(signs.DialogueId, issues);
        Assert.IsEmpty(issues, string.Join("\n", issues));

        GameState quiet = Resting(1.5);
        EncounterSelectionResult none = EncounterRuntimeService.SelectEncounter(quiet, new HeroProfileData(),
            CampEncounters.Opportunity(quiet), encounters);
        Assert.IsFalse(none.HasSelection, "Вожак у ям не выходил — ночь тихая.");

        GameState roaming = Resting(1.5);
        roaming.Narrative.SetFlag(FreePlayCoalStory.Flags.LeaderOnRoad);
        EncounterSelectionResult selection = EncounterRuntimeService.SelectEncounter(roaming, new HeroProfileData(),
            CampEncounters.Opportunity(roaming), encounters);
        Assert.IsTrue(selection.HasSelection);
        Assert.AreEqual("FREEPLAY_COAL_LEADER_SIGNS_01", selection.SelectedEncounter.EncounterId);

        roaming.Narrative.SetFlag(FreePlayCoalStory.Flags.LeaderKilled);
        Assert.IsFalse(EncounterRuntimeService.SelectEncounter(roaming, new HeroProfileData(),
            CampEncounters.Opportunity(roaming), encounters).HasSelection, "Вожак убит — не приходит.");
    }

    [Test]
    public void TrailSeenAtNight_PreparesTrailBattle()
    {
        GameState state = Resting(1.5);
        state.Narrative.SetFlag(FreePlayCoalStory.Flags.HutorMoved);
        Assert.IsFalse(FreePlayCoalStory.CreateTrailRequest(state).PreparedStart);
        state.Narrative.SetFlag(FreePlayCoalStory.Flags.LeaderTrailSeen);
        Assert.IsTrue(FreePlayCoalStory.CreateTrailRequest(state).PreparedStart, "Видели, куда он уходит.");
    }
}
