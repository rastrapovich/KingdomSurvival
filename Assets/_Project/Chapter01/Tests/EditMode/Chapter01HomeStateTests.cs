using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using NUnit.Framework;
using UnityEngine;

// P04-T04 — типизированный resolver визуальной рифмы Дома N01 → N16.
// Chapter01HomeState не хранит второй источник истины: все проверки здесь
// строят NarrativeStateData через существующие Chapter01Ids.Flags и читают
// Chapter01HomeState.ResolveCurrent — ни одного нового persistent-поля
// помимо HomeBaselineCaptured не вводится.
public sealed class Chapter01HomeStateTests
{
    private static NarrativeStateData NewState()
    {
        return new NarrativeStateData();
    }

    [Test]
    public void Baseline_MatchesFreshState_WithZeroChangedMotifs()
    {
        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(NewState());

        Assert.AreEqual(Chapter01HomeWaterState.Normal, snapshot.Water);
        Assert.AreEqual(Chapter01HomeMillState.RunningNormally, snapshot.Mill);
        Assert.AreEqual(Chapter01HomeLivestockState.Calm, snapshot.Livestock);
        Assert.AreEqual(Chapter01HomeWalkwayState.OldIntact, snapshot.Walkway);
        Assert.AreEqual(Chapter01HomeSoundState.FamiliarMorning, snapshot.Sound);
        Assert.AreEqual(0, Chapter01HomeState.CountChangedMotifs(snapshot));
        Assert.IsEmpty(Chapter01HomeState.GetChangedMotifs(snapshot));
    }

    [Test]
    public void Flood_ChangesWater()
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);

        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);

        Assert.AreNotEqual(Chapter01HomeWaterState.Normal, snapshot.Water);
        Assert.AreEqual(Chapter01HomeWaterState.FloodDisturbed, snapshot.Water);
    }

    [Test]
    public void MillDestroyed_SetsMillAndWalkway_AndSilencesSound()
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed);

        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);

        Assert.AreEqual(Chapter01HomeMillState.DamagedOrStopped, snapshot.Mill);
        Assert.AreEqual(Chapter01HomeWalkwayState.Destroyed, snapshot.Walkway);
        Assert.AreEqual(Chapter01HomeSoundState.MillSilent, snapshot.Sound);
    }

    [Test]
    public void LivestockLost_SetsLivestock()
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.FloodLivestockLost);

        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);

        Assert.AreEqual(Chapter01HomeLivestockState.Lost, snapshot.Livestock);
    }

    [Test]
    public void WrongWater_SetsWater_AndLivestockAvoidsWater_WhenNotLost()
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.WaterWrongActive);

        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);

        Assert.AreEqual(Chapter01HomeWaterState.Wrong, snapshot.Water);
        Assert.AreEqual(Chapter01HomeLivestockState.AvoidingWater, snapshot.Livestock);
        Assert.AreEqual(Chapter01HomeSoundState.UnevenWaterAndMill, snapshot.Sound);
    }

    [Test]
    public void LivestockLost_TakesPriorityOver_AvoidingWater()
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.WaterWrongActive);
        state.SetFlag(Chapter01Ids.Flags.FloodLivestockLost);

        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);

        Assert.AreEqual(Chapter01HomeLivestockState.Lost, snapshot.Livestock);
    }

    [Test]
    public void WrongWater_TakesPriorityOver_CompletedRepair()
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.WaterWrongActive);
        state.SetFlag(Chapter01Ids.Flags.RepairCompleted);
        state.SetFlag(Chapter01Ids.Flags.RepairNew);

        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);

        Assert.AreEqual(Chapter01HomeWaterState.Wrong, snapshot.Water);
    }

    [Test]
    public void OldRepair_Completed_RestoresMillAndWalkway_ButNotToRunningBaseline()
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed);
        state.SetFlag(Chapter01Ids.Flags.RepairOld);
        state.SetFlag(Chapter01Ids.Flags.RepairCompleted);

        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);

        Assert.AreEqual(Chapter01HomeMillState.RestoredOldWay, snapshot.Mill);
        Assert.AreEqual(Chapter01HomeWalkwayState.RestoredOldWay, snapshot.Walkway);
        Assert.AreEqual(Chapter01HomeWaterState.RestoredOldPattern, snapshot.Water);

        // Раздел 5 инструкции: старый ремонт не возвращает буквально
        // исходное состояние — след ремонта должен быть виден в N16.
        Assert.AreNotEqual(Chapter01HomeMillState.RunningNormally, snapshot.Mill);
        Assert.AreNotEqual(Chapter01HomeWalkwayState.OldIntact, snapshot.Walkway);
    }

    [Test]
    public void NewRepair_Completed_RebuildsMillAndWalkway()
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed);
        state.SetFlag(Chapter01Ids.Flags.RepairNew);
        state.SetFlag(Chapter01Ids.Flags.RepairCompleted);

        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);

        Assert.AreEqual(Chapter01HomeMillState.RestoredNewWay, snapshot.Mill);
        Assert.AreEqual(Chapter01HomeWalkwayState.RebuiltNewWay, snapshot.Walkway);
        Assert.AreEqual(Chapter01HomeWaterState.AlteredByNewRepair, snapshot.Water);
    }

    [Test]
    public void OldAndNewRepairBranches_ProduceDistinguishableSnapshots()
    {
        NarrativeStateData oldState = NewState();
        oldState.SetFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed);
        oldState.SetFlag(Chapter01Ids.Flags.RepairOld);
        oldState.SetFlag(Chapter01Ids.Flags.RepairCompleted);

        NarrativeStateData newState = NewState();
        newState.SetFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed);
        newState.SetFlag(Chapter01Ids.Flags.RepairNew);
        newState.SetFlag(Chapter01Ids.Flags.RepairCompleted);

        Chapter01HomeSnapshot oldSnapshot = Chapter01HomeState.ResolveCurrent(oldState);
        Chapter01HomeSnapshot newSnapshot = Chapter01HomeState.ResolveCurrent(newState);

        Assert.AreNotEqual(oldSnapshot.Mill, newSnapshot.Mill);
        Assert.AreNotEqual(oldSnapshot.Walkway, newSnapshot.Walkway);
        Assert.AreNotEqual(oldSnapshot.Water, newSnapshot.Water);
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void RealisticFirstChapterEvents_ProduceAtLeastThreeChangedMotifs_ForBothRepairBranches(
        bool repairOld, bool repairNew)
    {
        NarrativeStateData state = NewState();
        state.SetFlag(Chapter01Ids.Flags.FloodHappened);
        state.SetFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed);
        state.SetFlag(Chapter01Ids.Flags.WaterWrongActive);
        if (repairOld) state.SetFlag(Chapter01Ids.Flags.RepairOld);
        if (repairNew) state.SetFlag(Chapter01Ids.Flags.RepairNew);
        state.SetFlag(Chapter01Ids.Flags.RepairCompleted);

        Chapter01HomeSnapshot snapshot = Chapter01HomeState.ResolveCurrent(state);
        int changedCount = Chapter01HomeState.CountChangedMotifs(snapshot);

        Assert.That(changedCount, Is.GreaterThanOrEqualTo(3),
            "Изменившихся мотивов: " + changedCount + " (" +
            string.Join(", ", Chapter01HomeState.GetChangedMotifs(snapshot)) + ")");
    }

    [Test]
    public void Registry_StillHasNoEmptyOrDuplicateIds_AfterHomeBaselineCaptured()
    {
        IReadOnlyList<string> issues = Chapter01Ids.ValidateRegistry();
        Assert.That(issues, Is.Empty, string.Join("\n", issues));
        Assert.That(Chapter01Ids.Flags.All, Does.Contain(Chapter01Ids.Flags.HomeBaselineCaptured));
    }

    [Test]
    public void NarrativeState_Survives_JsonRoundTrip_WithSameResolvedSnapshot()
    {
        NarrativeStateData before = NewState();
        before.SetFlag(Chapter01Ids.Flags.FloodHappened);
        before.SetFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed);
        before.SetFlag(Chapter01Ids.Flags.RepairOld);
        before.SetFlag(Chapter01Ids.Flags.RepairCompleted);
        before.SetFlag(Chapter01Ids.Flags.HomeBaselineCaptured);

        string json = JsonUtility.ToJson(before);
        NarrativeStateData after = JsonUtility.FromJson<NarrativeStateData>(json);

        Chapter01HomeSnapshot beforeSnapshot = Chapter01HomeState.ResolveCurrent(before);
        Chapter01HomeSnapshot afterSnapshot = Chapter01HomeState.ResolveCurrent(after);

        Assert.AreEqual(beforeSnapshot.Water, afterSnapshot.Water);
        Assert.AreEqual(beforeSnapshot.Mill, afterSnapshot.Mill);
        Assert.AreEqual(beforeSnapshot.Livestock, afterSnapshot.Livestock);
        Assert.AreEqual(beforeSnapshot.Walkway, afterSnapshot.Walkway);
        Assert.AreEqual(beforeSnapshot.Sound, afterSnapshot.Sound);
        Assert.IsTrue(after.HasFlag(Chapter01Ids.Flags.HomeBaselineCaptured));
    }

    [Test]
    public void ResolveCurrent_ThrowsOnNullState()
    {
        Assert.Throws<System.ArgumentNullException>(() => Chapter01HomeState.ResolveCurrent(null));
    }
}
