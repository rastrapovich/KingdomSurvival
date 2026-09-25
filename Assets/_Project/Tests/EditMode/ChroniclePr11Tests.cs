using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// ПР-11 (ProjectDocs/PR11_CHRONICLE_SPEC.md §4): история — одна запись на
// событие, со временем события; переживает сохранение; бой и смерть
// записываются сами.
public sealed class ChroniclePr11Tests
{
    private static GameState NewState()
    {
        return new CampaignSetup { WorldSeed = 20260925 }.CreateCampaign();
    }

    [Test]
    public void Record_IsIdempotent_AndKeepsEventTime()
    {
        GameState state = NewState();
        Assert.IsTrue(Chronicle.Record(state, "test.flood", "Паводок", "Пришла вода.", "loc", "cause"));
        ChronicleEntryData first = Chronicle.Find(state, "test.flood");
        int day = first.Day;
        double hour = first.Hour;

        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.Advance(state, (float)(5.0 / ContinuousSimulationSystem.GameHoursPerRealSecond), false);
        Assert.IsFalse(Chronicle.Record(state, "test.flood", "Другое", "Другое."));

        Assert.AreEqual(1, Chronicle.Get(state).Entries.Count);
        Assert.AreEqual("Пришла вода.", first.Text);
        Assert.AreEqual(day, first.Day);
        Assert.AreEqual(hour, first.Hour, 0.0001);
        Assert.AreEqual("loc", first.LocationId);
        Assert.AreEqual("cause", first.CauseId);
    }

    [Test]
    public void Chronicle_AndSeenMarks_SurviveSave()
    {
        GameState state = NewState();
        Chronicle.Record(state, "test.a", "А", "Первое.");
        Chronicle.Record(state, "test.b", "Б", "Второе.", null, "test.a");
        Chronicle.MarkSeen(state, "test.a");
        Chronicle.MarkSeen(state, "test.a");

        GameState restored = CampaignSaveService.RestoreCampaign(
            JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));

        CollectionAssert.AreEqual(new[] { "test.a", "test.b" }, Chronicle.Get(restored).Entries.Select(e => e.Id));
        Assert.AreEqual("test.a", Chronicle.Find(restored, "test.b").CauseId);
        Assert.IsTrue(Chronicle.IsSeen(restored, "test.a"));
        Assert.IsFalse(Chronicle.IsSeen(restored, "test.b"));
        Assert.AreEqual(1, Chronicle.Get(restored).SeenIds.Count);
    }

    [Test]
    public void Death_OutsideBattle_IsRecordedOnce()
    {
        GameState state = NewState();
        ResidentState person = HomePeopleService.Find(state, "garrick");
        HomePeopleService.MarkDead(state, "garrick", "hunger");
        HomePeopleService.MarkDead(state, "garrick", "hunger");

        ChronicleEntryData entry = Chronicle.Find(state, "death.garrick");
        Assert.IsNotNull(entry);
        StringAssert.Contains(person.DisplayName, entry.Text);
        Assert.AreEqual(1, Chronicle.Get(state).Entries.Count(e => e.Id == "death.garrick"));
    }

    [Test]
    public void Battle_IsRecorded_WithOutcomeAndFallen_NoSeparateDeathEntry()
    {
        IUnitStatsProvider previous = GameState.UnitStatsProvider;
        GameState.UnitStatsProvider = new FixedStats();
        try
        {
            GameState state = NewState();
            state.ArmySupply = 50;
            LocationData target = state.Locations.First(location => !location.IsWaypoint);
            Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string> { "garrick", "edric" }, out string message), message);
            state.ActiveExpedition.RouteIndex = 1;
            string hero = state.GetSelectedCommander().Id;
            string edricName = HomePeopleService.Find(state, "edric").DisplayName;

            CampaignBattleResult result = new CampaignBattleResult
            {
                BattleId = "test.beasts",
                Outcome = CampaignBattleOutcome.Victory,
                Rounds = 3
            };
            result.FallenPersonIds.Add("edric");
            result.Survivors.Add(new CampaignBattleSurvivor { PersonId = hero, HitPoints = 20 });
            result.Survivors.Add(new CampaignBattleSurvivor { PersonId = "garrick", HitPoints = 20 });
            CampaignBattleBridge.ApplyResult(state, result, new List<string>());

            ChronicleEntryData entry = Chronicle.Find(state, "battle.test.beasts");
            Assert.IsNotNull(entry);
            StringAssert.StartsWith("Победа.", entry.Text);
            StringAssert.Contains(edricName, entry.Text);
            Assert.IsNull(Chronicle.Find(state, "death.edric"), "Гибель в бою — часть записи боя.");
        }
        finally
        {
            GameState.UnitStatsProvider = previous;
        }
    }

    private sealed class FixedStats : IUnitStatsProvider
    {
        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = new UnitCombatStats { MaxHitPoints = 20, Attack = 2, Defense = 2, Damage = 3, Movement = 3, Initiative = 3, AttackRange = 1 };
            return !string.IsNullOrEmpty(unitTypeId);
        }
    }
}
