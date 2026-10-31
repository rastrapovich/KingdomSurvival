using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

// Аудит A-02 (решение пользователя 31.10.2026): тяжёлая рана заживает за
// ночлеги без боя, под крышей — вдвое, лекарь в отряде ускоряет, знахарь в
// поселении лечит сразу за плату. Числа — рабочие (WoundRecovery).
public sealed class WoundRecoveryTests
{
    private sealed class FixedStats : IUnitStatsProvider
    {
        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = new UnitCombatStats { MaxHitPoints = 20, Attack = 2, Defense = 2, Damage = 3, Movement = 3, Initiative = 3, AttackRange = 1 };
            return !string.IsNullOrEmpty(unitTypeId);
        }
    }

    private IUnitStatsProvider previousProvider;

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
        previousProvider = GameState.UnitStatsProvider;
        GameState.UnitStatsProvider = new FixedStats();
    }

    [TearDown]
    public void TearDown()
    {
        GameState.UnitStatsProvider = previousProvider;
    }

    private static GameState OnTheRoad(params string[] fighterIds)
    {
        GameState state = new CampaignSetup { WorldSeed = 20261031 }.CreateCampaign();
        TestPeople.AdmitAll(state);
        state.ArmySupply = 50;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string>(fighterIds), out string message), message);
        return state;
    }

    private static void Night(GameState state)
    {
        state.ActiveExpedition.ActiveActivity = null;
        CampRest.CompleteRest(state, new List<string>());
    }

    [Test]
    public void CampNights_HealHeavyWound_AfterThree()
    {
        GameState state = OnTheRoad("garrick");
        Assert.IsNull(WoundRecovery.FindHealer(state, PartyPresence.PresentIds(state)), "Лекаря в отряде нет.");
        ResidentState garrick = PeopleService.Find(state, "garrick");
        WoundRecovery.Begin(garrick);
        Assert.AreEqual(3, WoundRecovery.NightsLeft(garrick));

        Night(state);
        Night(state);
        Assert.AreEqual(ResidentInjury.Recovering, garrick.Injury, "Две ночи в лагере — ещё ранен.");
        Assert.AreEqual(1, WoundRecovery.NightsLeft(garrick));

        Night(state);
        Assert.AreEqual(ResidentInjury.None, garrick.Injury, "Третья ночь — оправился.");
        Assert.AreEqual(0, garrick.WoundRecoveryPoints);
    }

    [Test]
    public void HealerInParty_AddsPoint_ButNotToHimself()
    {
        GameState state = OnTheRoad("garrick", "marta");
        Assert.AreEqual(CampRest.MartaId, WoundRecovery.FindHealer(state, PartyPresence.PresentIds(state)));
        ResidentState garrick = PeopleService.Find(state, "garrick");
        ResidentState marta = PeopleService.Find(state, CampRest.MartaId);
        WoundRecovery.Begin(garrick);
        WoundRecovery.Begin(marta);

        Night(state);
        Assert.AreEqual(2, garrick.WoundRecoveryPoints, "Ночь в лагере + лекарь рядом.");
        Assert.AreEqual(1, marta.WoundRecoveryPoints, "Себя лекарь не ускоряет.");

        Night(state);
        Assert.AreEqual(ResidentInjury.None, garrick.Injury, "С лекарем — за две ночи.");
        Assert.AreEqual(ResidentInjury.Recovering, marta.Injury);
    }

    [Test]
    public void NewWound_RestartsCount()
    {
        GameState state = OnTheRoad("garrick");
        ResidentState garrick = PeopleService.Find(state, "garrick");
        WoundRecovery.Begin(garrick);
        Night(state);
        Night(state);
        Assert.AreEqual(2, garrick.WoundRecoveryPoints);

        WoundRecovery.Begin(garrick);
        Assert.AreEqual(0, garrick.WoundRecoveryPoints, "Новая тяжёлая рана — счёт с нуля.");
        Assert.AreEqual(3, WoundRecovery.NightsLeft(garrick));
    }

    [Test]
    public void ApplyNight_RoofGivesTwo()
    {
        GameState state = OnTheRoad("garrick");
        ResidentState garrick = PeopleService.Find(state, "garrick");
        WoundRecovery.Begin(garrick);
        WoundRecovery.ApplyNight(state, new List<string> { "garrick" }, null);
        Assert.AreEqual(2, garrick.WoundRecoveryPoints);
    }

    [Test]
    public void HealerVisit_AllOrNothing_SpendsGold()
    {
        GameState state = OnTheRoad("garrick", "marta");
        ResidentState garrick = PeopleService.Find(state, "garrick");
        ResidentState marta = PeopleService.Find(state, CampRest.MartaId);
        Assert.IsFalse(WoundRecovery.CanVisitHealer(state, 10, out _), "Раненых нет — знахарь не нужен.");

        WoundRecovery.Begin(garrick);
        WoundRecovery.Begin(marta);
        state.ArmyGold = 15;
        Assert.IsFalse(WoundRecovery.TryVisitHealer(state, 10, out string reason), "На двоих нужно 20.");
        StringAssert.Contains("20", reason);
        Assert.AreEqual(15, state.ArmyGold);
        Assert.AreEqual(ResidentInjury.Recovering, garrick.Injury);
        Assert.AreEqual(ResidentInjury.Recovering, marta.Injury);

        state.ArmyGold = 25;
        Assert.IsTrue(WoundRecovery.TryVisitHealer(state, 10, out string message), message);
        Assert.AreEqual(5, state.ArmyGold);
        Assert.AreEqual(ResidentInjury.None, garrick.Injury);
        Assert.AreEqual(ResidentInjury.None, marta.Injury);
    }

    [Test]
    public void OldSave_WithoutPoints_StartsFromZero()
    {
        ResidentState resident = new ResidentState { PersonId = "old", DisplayName = "Старый", Injury = ResidentInjury.Recovering, WoundRecoveryPoints = 2 };
        string json = Regex.Replace(JsonUtility.ToJson(resident), "\"WoundRecoveryPoints\":\\d+,?", string.Empty);
        StringAssert.DoesNotContain("WoundRecoveryPoints", json);

        ResidentState loaded = JsonUtility.FromJson<ResidentState>(json);
        Assert.AreEqual(ResidentInjury.Recovering, loaded.Injury);
        Assert.AreEqual(0, loaded.WoundRecoveryPoints);
        Assert.AreEqual(3, WoundRecovery.NightsLeft(loaded));
    }
}
