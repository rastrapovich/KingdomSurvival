using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// ПР-07А-1 (PR07_HOME_SPEC §9.2): каталог построек выключен. Старые
// здания остаются данными сохранения, но не строятся, не дают доходов и не
// требуют содержания; незавершённая стройка из старого сохранения
// отменяется с однократным возвратом цены.
public class BuildingSystemTests
{
    [Test]
    public void CatalogConstruction_IsRejected_AndGoldUntouched()
    {
        GameState state = CreateState();
        state.Gold = 1000;

        foreach (BuildingDefinition definition in BuildingSystem.GetDefinitions())
        {
            Assert.IsFalse(BuildingSystem.TryStartConstruction(state, definition.Id, out string message), definition.Id);
            Assert.AreEqual(BuildingSystem.CatalogDisabledMessage, message);
        }

        Assert.AreEqual(1000, state.Gold);
        Assert.IsFalse(BuildingSystem.HasActiveConstruction(state));
    }

    [Test]
    public void DailyEconomy_IsBaseHouseholdOnly()
    {
        GameState state = CreateState();

        Assert.AreEqual(BuildingSystem.BaseDailyGoldIncome, BuildingSystem.GetDailyGoldIncome(state));
        Assert.AreEqual(BaseFood(state), BuildingSystem.GetDailyFoodIncome(state));
        Assert.AreEqual(0, BuildingSystem.GetDailyGoldUpkeep(state));
    }

    [Test]
    public void BarracksDoNotRecruitFightersForMoney()
    {
        GameState state = CreateState();
        state.Gold = 500;
        int before = state.Fighters.Count;

        Assert.IsFalse(BuildingSystem.CanRecruit(state));
        Assert.IsFalse(BuildingSystem.TryStartRecruitment(state, out string message));
        StringAssert.Contains("новые люди приходят в Дом из мира", message);
        Assert.AreEqual(500, state.Gold);
        Assert.AreEqual(before, state.Fighters.Count);
    }

    [Test]
    public void LegacyCompletedBuildings_GiveNoIncomeOrUpkeep()
    {
        GameState state = CreateState();
        BuildingSystem.RestoreSnapshot(state, Snapshot(
            (BuildingSystem.FieldsAndGranariesId, BuildingStatus.Completed),
            (BuildingSystem.MarketId, BuildingStatus.Completed),
            (BuildingSystem.BarracksId, BuildingStatus.Completed)));

        Assert.AreEqual(0, BuildingSystem.RetireLegacyCatalog(state), "Завершённые не компенсируются.");
        Assert.IsTrue(BuildingSystem.IsCompleted(state, BuildingSystem.MarketId), "Запись сохранена как данные.");
        Assert.AreEqual(BuildingSystem.BaseDailyGoldIncome, BuildingSystem.GetDailyGoldIncome(state));
        Assert.AreEqual(BaseFood(state), BuildingSystem.GetDailyFoodIncome(state));
        Assert.AreEqual(0, BuildingSystem.GetDailyGoldUpkeep(state));
        Assert.IsEmpty(BuildingSystem.ConsumeNotices(state));
    }

    [Test]
    public void LegacyUnfinishedConstruction_IsRefundedOnce_AndNeverCompletes()
    {
        GameState state = CreateState();
        state.Gold = 10;
        BuildingSystem.RestoreSnapshot(state, Snapshot(
            (BuildingSystem.MarketId, BuildingStatus.Constructing)));

        int refund = BuildingSystem.RetireLegacyCatalog(state);
        Assert.AreEqual(80, refund, "Цена рынка — из описания постройки.");
        Assert.AreEqual(90, state.Gold);
        Assert.AreEqual(0, BuildingSystem.RetireLegacyCatalog(state), "Повторно не возвращается.");
        Assert.AreEqual(90, state.Gold);

        List<string> notices = BuildingSystem.ConsumeNotices(state);
        Assert.AreEqual(1, notices.Count);
        StringAssert.Contains("возвращено 80 золота", notices[0]);

        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.SetSpeedMultiplier(state, ContinuousSimulationSystem.MaximumSpeedMultiplier);
        ContinuousSimulationSystem.Advance(state, 7f, false);
        Assert.IsFalse(BuildingSystem.IsCompleted(state, BuildingSystem.MarketId), "Отменённая стройка не достраивается.");
        Assert.IsEmpty(BuildingSystem.ConsumeNotices(state));
    }

    [Test]
    public void LegacySave_WithUnfinishedConstruction_RefundsOnLoad_ThenSavedStateDoesNotRepeat()
    {
        GameState state = CreateState();
        state.Gold = 0;
        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        data.BuildingSnapshot = Snapshot((BuildingSystem.CityWallsId, BuildingStatus.Constructing));

        GameState restored = CampaignSaveService.RestoreCampaign(
            JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(data)));
        Assert.AreEqual(140, restored.Gold);

        CampaignSaveData resaved = CampaignSaveService.ExportCampaign(restored);
        GameState again = CampaignSaveService.RestoreCampaign(
            JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(resaved)));
        Assert.AreEqual(140, again.Gold, "Новое сохранение не возвращает золото второй раз.");
    }

    private static BuildingSystemSnapshotData Snapshot(params (string id, BuildingStatus status)[] buildings)
    {
        BuildingSystemSnapshotData snapshot = new BuildingSystemSnapshotData();
        foreach ((string id, BuildingStatus status) in buildings)
        {
            snapshot.Buildings.Add(new BuildingStateData
            {
                BuildingId = id,
                Status = status,
                StartedAtGameHour = 0.0,
                CompletesAtGameHour = status == BuildingStatus.Constructing ? 10.0 : 0.0
            });
        }
        return snapshot;
    }

    private static int BaseFood(GameState state)
    {
        return state.BaseDailyFoodIncome > 0 ? state.BaseDailyFoodIncome : BuildingSystem.BaseDailyFoodIncome;
    }

    private static GameState CreateState()
    {
        GameState state = new GameState();
        state.CreateNewGame(12345);
        ContinuousSimulationSystem.Reset(state);
        BuildingSystem.Reset(state);
        return state;
    }
}
