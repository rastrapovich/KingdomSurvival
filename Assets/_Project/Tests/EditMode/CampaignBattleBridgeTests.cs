using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

// ПР-03: черновой мост кампания ↔ бой. Запрос несёт героя и бойцов похода
// по их ID; итог применяется ровно один раз; павшие бойцы погибают насовсем,
// пал герой — HeroFell. CampaignSession передаёт запрос и итог через смену сцены.
public sealed class CampaignBattleBridgeTests
{
    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        CampaignSession.Reset();
    }

    private static GameState NewCampaignInExpedition(int fighters)
    {
        GameState state = new GameState();
        state.CreateNewGame(20260924);
        state.ArmySupply = 100;

        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        List<string> selected = state.Fighters.Take(fighters).Select(f => f.Id).ToList();
        Assert.IsTrue(state.TryStartExpedition(target.Id, selected, out string message), message);
        return state;
    }

    [Test]
    public void Request_HasHeroFirst_ThenExpeditionFighters_ByTheirIds()
    {
        GameState state = NewCampaignInExpedition(2);
        CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, "b1");

        Assert.AreEqual(3, request.Participants.Count);
        Assert.IsTrue(request.Participants[0].IsHero);
        Assert.AreEqual(state.GetSelectedCommander().Id, request.Participants[0].PersonId);
        Assert.AreEqual(CampaignBattleBridge.HeroFallbackUnitTypeId, request.Participants[0].UnitTypeId,
            "У героя нет записи в UnitDatabase — временная основа.");
        CollectionAssert.AreEqual(
            state.ActiveExpedition.FighterIds,
            request.Participants.Skip(1).Select(p => p.PersonId).ToList());
    }

    [Test]
    public void Request_HeroAlone_WhenNoFightersTaken()
    {
        GameState state = NewCampaignInExpedition(0);
        CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, "b1");

        Assert.AreEqual(1, request.Participants.Count);
        Assert.IsTrue(request.Participants[0].IsHero);
    }

    [Test]
    public void Victory_WithLosses_RemovesFallenFighters_Once()
    {
        GameState state = NewCampaignInExpedition(2);
        string fallen = state.ActiveExpedition.FighterIds[0];
        string survivor = state.ActiveExpedition.FighterIds[1];
        int fightersBefore = state.Fighters.Count;

        CampaignBattleResult result = new CampaignBattleResult
        {
            BattleId = "b1",
            Outcome = CampaignBattleOutcome.Victory,
            FallenPersonIds = new List<string> { fallen }
        };

        List<string> names = new List<string>();
        Assert.AreEqual(CampaignBattleApplyStatus.SquadSurvived, CampaignBattleBridge.ApplyResult(state, result, names));
        Assert.AreEqual(fightersBefore - 1, state.Fighters.Count);
        Assert.IsFalse(state.Fighters.Any(f => f.Id == fallen), "Павший погибает насовсем.");
        CollectionAssert.DoesNotContain(state.ActiveExpedition.FighterIds, fallen);
        CollectionAssert.Contains(state.ActiveExpedition.FighterIds, survivor);
        Assert.AreEqual(1, names.Count);

        Assert.AreEqual(CampaignBattleApplyStatus.AlreadyApplied, CampaignBattleBridge.ApplyResult(state, result, null));
        Assert.AreEqual(fightersBefore - 1, state.Fighters.Count, "Повторное применение не убивает второй раз.");
    }

    [Test]
    public void HeroFallen_IsReported_AndAppliedOnce()
    {
        GameState state = NewCampaignInExpedition(1);
        CampaignBattleResult result = new CampaignBattleResult
        {
            BattleId = "b2",
            Outcome = CampaignBattleOutcome.Defeat,
            FallenPersonIds = new List<string> { state.GetSelectedCommander().Id, state.ActiveExpedition.FighterIds[0] }
        };

        Assert.AreEqual(CampaignBattleApplyStatus.HeroFell, CampaignBattleBridge.ApplyResult(state, result, null));
        Assert.IsTrue(CampaignBattleBridge.IsApplied(state, "b2"));
        Assert.AreEqual(CampaignBattleApplyStatus.AlreadyApplied, CampaignBattleBridge.ApplyResult(state, result, null));
    }

    [Test]
    public void AppliedMark_SurvivesSaveLoad()
    {
        GameState state = NewCampaignInExpedition(1);
        CampaignBattleBridge.ApplyResult(state, new CampaignBattleResult { BattleId = "b3" }, null);

        CampaignSaveData data = CampaignSaveService.ExportCampaign(state, string.Empty, 0);
        GameState restored = CampaignSaveService.RestoreCampaign(
            UnityEngine.JsonUtility.FromJson<CampaignSaveData>(UnityEngine.JsonUtility.ToJson(data)));

        Assert.IsTrue(CampaignBattleBridge.IsApplied(restored, "b3"));
    }

    [Test]
    public void Session_CarriesRequestIntoBattle_AndResultBackOnce()
    {
        GameState state = NewCampaignInExpedition(1);
        CampaignSession.Begin(state);

        CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, "b4");
        CampaignSession.EnterBattle(request);
        Assert.AreSame(request, CampaignSession.PendingBattle);

        CampaignSession.CompleteBattle(new CampaignBattleResult { BattleId = "b4" });
        Assert.IsNull(CampaignSession.PendingBattle);

        Assert.IsNotNull(CampaignSession.TakeCompletedBattle());
        Assert.IsNull(CampaignSession.TakeCompletedBattle(), "Итог забирается один раз.");
        Assert.AreSame(state, CampaignSession.Current);
    }

    [Test]
    public void Session_RejectsResultOfAnotherBattle()
    {
        GameState state = NewCampaignInExpedition(1);
        CampaignSession.Begin(state);
        CampaignSession.EnterBattle(CampaignBattleBridge.CreateRequest(state, "b5"));

        Assert.Throws<System.InvalidOperationException>(() =>
            CampaignSession.CompleteBattle(new CampaignBattleResult { BattleId = "other" }));
    }
}
