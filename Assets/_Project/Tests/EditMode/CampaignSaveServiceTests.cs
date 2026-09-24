using NUnit.Framework;
using UnityEngine;

// AM-05 (канон v1.33, §9.9): Save/Load. Эти тесты закрывают конкретные,
// эмпирически найденные риски простой JSON-сериализации GameState —
// не гипотетические, а воспроизведённые через реальный JsonUtility.
public class CampaignSaveServiceTests
{
    [Test]
    public void JsonRoundTrip_RestoresTrueNullForActiveExpedition()
    {
        GameState state = new GameState();
        state.CreateNewGame(1);
        state.ActiveExpedition = null;

        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        string json = JsonUtility.ToJson(data);
        CampaignSaveData loaded = JsonUtility.FromJson<CampaignSaveData>(json);

        // Доказательство самой проблемы: JsonUtility не умеет сериализовать
        // null для полей-ссылок и подставляет вместо него default-объект —
        // без явного маркера loaded.State.ActiveExpedition здесь НЕ null.
        Assert.That(loaded.State.ActiveExpedition, Is.Not.Null,
            "Если это упало — JsonUtility научился сохранять null, и обходной код можно убирать.");

        GameState restored = CampaignSaveService.RestoreCampaign(loaded);
        Assert.That(restored.ActiveExpedition, Is.Null);
        Assert.That(restored.HasActiveExpedition, Is.False);
    }

    [Test]
    public void JsonRoundTrip_RestoresTrueNullForActivityAndDecisionOnRealExpedition()
    {
        GameState state = new GameState();
        state.CreateNewGame(1);
        state.ActiveExpedition = new ExpeditionData
        {
            IsActive = true,
            CommanderId = "commander",
            Phase = CommanderState.TravellingToLocation
        };
        state.ActiveExpedition.ActiveActivity = null;
        state.ActiveExpedition.PendingDecision = null;

        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        string json = JsonUtility.ToJson(data);
        CampaignSaveData loaded = JsonUtility.FromJson<CampaignSaveData>(json);
        GameState restored = CampaignSaveService.RestoreCampaign(loaded);

        Assert.That(restored.HasActiveExpedition, Is.True);
        Assert.That(restored.ActiveExpedition.HasTimedActivity, Is.False,
            "Без явного маркера HasActiveActivity JsonUtility подставляет фиктивный ActiveActivity, " +
            "и экспедиция выглядит занятой таймером, которого на самом деле нет.");
        Assert.That(restored.ActiveExpedition.PendingDecision, Is.Null);
    }

    [Test]
    public void JsonRoundTrip_PreservesRealActivityAndDecisionWhenPresent()
    {
        GameState state = new GameState();
        state.CreateNewGame(1);
        state.ActiveExpedition = new ExpeditionData { IsActive = true };
        state.ActiveExpedition.ActiveActivity = new ExpeditionActivityData
        {
            Id = "research",
            Kind = ExpeditionActivityKind.LocationResearch,
            TotalHours = 5.0,
            RemainingHours = 2.0
        };

        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        string json = JsonUtility.ToJson(data);
        CampaignSaveData loaded = JsonUtility.FromJson<CampaignSaveData>(json);
        GameState restored = CampaignSaveService.RestoreCampaign(loaded);

        Assert.That(restored.ActiveExpedition.HasTimedActivity, Is.True);
        Assert.That(restored.ActiveExpedition.ActiveActivity.RemainingHours, Is.EqualTo(2.0).Within(0.0001));
    }

    [Test]
    public void SaveLoad_PreservesFutureRandomCheckScheduleAfterRestore()
    {
        GameState original = new GameState();
        original.CreateNewGame(4242);
        ContinuousSimulationSystem.SetPaused(original, false);
        ContinuousSimulationSystem.SetSpeedMultiplier(
            original, ContinuousSimulationSystem.MaximumSpeedMultiplier);
        ContinuousSimulationSystem.Advance(original, 1f, false);

        ContinuousSimulationSnapshotData snapshot =
            ContinuousSimulationSystem.ExportSnapshot(original);

        GameState restored = new GameState();
        restored.CreateNewGame(4242);
        ContinuousSimulationSystem.RestoreSnapshot(restored, snapshot);
        ContinuousSimulationSystem.SetPaused(restored, false);
        ContinuousSimulationSystem.SetSpeedMultiplier(
            restored, ContinuousSimulationSystem.MaximumSpeedMultiplier);

        // Одинаковое дальнейшее продвижение (пересекает ровно одну полночь)
        // на оригинале и на восстановленной копии.
        ContinuousSimulationSystem.Advance(original, 5f, false);
        ContinuousSimulationSystem.Advance(restored, 5f, false);

        ContinuousSimulationSnapshotData afterOriginal =
            ContinuousSimulationSystem.ExportSnapshot(original);
        ContinuousSimulationSnapshotData afterRestored =
            ContinuousSimulationSystem.ExportSnapshot(restored);

        Assert.That(
            afterRestored.HourOfDay, Is.EqualTo(afterOriginal.HourOfDay).Within(1e-9));
        Assert.That(
            afterRestored.RandomDrawCount, Is.EqualTo(afterOriginal.RandomDrawCount));
        Assert.That(
            afterRestored.ExpeditionIncidentCheckHour,
            Is.EqualTo(afterOriginal.ExpeditionIncidentCheckHour).Within(1e-9),
            "Без прокрутки Random на RandomDrawCount шагов новый seed-based Random " +
            "начал бы с нуля и дал бы другое значение следующей проверки.");
        Assert.That(
            afterRestored.ExpeditionDecisionCheckHour,
            Is.EqualTo(afterOriginal.ExpeditionDecisionCheckHour).Within(1e-9));
    }

    [Test]
    public void SaveLoad_PreservesCompletedBarracksWithoutPaidRecruitment()
    {
        // ПР-06Б: платного найма нет — после сохранения казармы построены,
        // очередь бойцов не появляется.
        GameState state = new GameState();
        state.CreateNewGame(1);
        state.Gold = 1000;
        string message;
        BuildingSystem.TryStartConstruction(state, BuildingSystem.BarracksId, out message);
        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.SetSpeedMultiplier(
            state, ContinuousSimulationSystem.MaximumSpeedMultiplier);
        ContinuousSimulationSystem.Advance(state, 7f, false);
        Assert.That(BuildingSystem.TryStartRecruitment(state, out message), Is.False);

        CampaignSaveData data = CampaignSaveService.ExportCampaign(state);
        string json = JsonUtility.ToJson(data);
        CampaignSaveData loaded = JsonUtility.FromJson<CampaignSaveData>(json);
        GameState restored = CampaignSaveService.RestoreCampaign(loaded);

        Assert.That(BuildingSystem.IsCompleted(restored, BuildingSystem.BarracksId), Is.True);
        Assert.That(BuildingSystem.IsRecruitmentActive(restored), Is.False);
        Assert.That(restored.Fighters.Count, Is.EqualTo(state.Fighters.Count));
    }
}
