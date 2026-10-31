using System.Linq;
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
        FreePlayContent.Register();
        encounters = Resources.Load<EncounterDatabaseAsset>(EncounterDatabaseAsset.ResourcesPath);
    }

    private static GameState Resting(double hoursIntoRest)
    {
        GameState state = new CampaignSetup { WorldSeed = 20260928 }.CreateCampaign();
        TestPeople.AdmitAll(state);
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
    public void CampPool_Seeded_QuietWithoutCampEncounters()
    {
        // Канон v1.59: единственная лагерная встреча («Следы вожака») ушла
        // вместе с историей «Уголь для Лады»; пул и механика остаются.
        EncounterDatabaseAsset encounters = Resources.Load<EncounterDatabaseAsset>(EncounterDatabaseAsset.ResourcesPath);
        Assert.IsNotNull(encounters.FindPool(CampEncounterIds.PoolId));
        GameState quiet = Resting(1.5);
        EncounterSelectionResult none = EncounterRuntimeService.SelectEncounter(quiet, new HeroProfileData(),
            CampEncounters.Opportunity(quiet), encounters);
        Assert.IsFalse(none.HasSelection, "Тихая ночь допустима.");
    }
}
