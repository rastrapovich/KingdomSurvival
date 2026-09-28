using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// 12Е-8 плана: следы развития (решения автора 28.09.2026) — семь боевых
// следов, ступени «нет → замечено (1 бой) → характерно (3 боя)», бой
// засчитывается один раз, «характерно» открывает особенность личным
// вариантом вместо требования, след виден ступенью.
public sealed class ProgressionTracesTests
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
        ProgressionRules.Current = ProgressionRules.CreateDefault();
        ProgressionCatalog.Current = ProgressionCatalog.CreateDefault();
        previousProvider = GameState.UnitStatsProvider;
        GameState.UnitStatsProvider = new FixedStats();
    }

    [TearDown]
    public void TearDown()
    {
        ProgressionRules.Current = ProgressionRules.CreateDefault();
        ProgressionCatalog.Current = ProgressionCatalog.CreateDefault();
        GameState.UnitStatsProvider = previousProvider;
    }

    private static GameState OnTheRoad(params string[] fighters)
    {
        GameState state = new CampaignSetup { WorldSeed = 20260928 }.CreateCampaign();
        state.ArmySupply = 50;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, fighters.ToList(), out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        return state;
    }

    private static string Hero(GameState state) => state.GetSelectedCommander().Id;

    // Боевое состояние заводит запрос боя (CampaignBattleBridge.CreateRequest).
    private static int Max(GameState state, string personId)
    {
        ResidentState resident = HomePeopleService.Find(state, personId);
        HomePeopleService.EnsureCombatState(resident);
        return resident.MaxHitPoints;
    }

    private static CampaignBattleResult Battle(string battleId, CampaignBattleOutcome outcome = CampaignBattleOutcome.Victory)
    {
        return new CampaignBattleResult { BattleId = battleId, Outcome = outcome, Rounds = 2 };
    }

    private static void Healthy(GameState state, CampaignBattleResult result, params string[] people)
    {
        foreach (string personId in people)
            result.Survivors.Add(new CampaignBattleSurvivor { PersonId = personId, HitPoints = Max(state, personId) });
    }

    [Test]
    public void Stages_NoticedAfterOne_CharacteristicAfterThree_SameBattleOnce()
    {
        GameState state = OnTheRoad("garrick");
        Assert.AreEqual(TraceStage.None, ProgressionTraces.Stage(state, "garrick", TraceIds.Stance));

        Assert.IsTrue(ProgressionTraces.Note(state, "garrick", TraceIds.Stance, "battle:a", out TraceStage stage));
        Assert.AreEqual(TraceStage.Noticed, stage);
        Assert.IsFalse(ProgressionTraces.Note(state, "garrick", TraceIds.Stance, "battle:a", out _), "Тот же бой второй раз не засчитывается.");
        Assert.IsFalse(ProgressionTraces.Note(state, "garrick", TraceIds.Stance, "battle:b", out stage), "Второй бой ступень не меняет.");
        Assert.AreEqual(TraceStage.Noticed, stage);
        Assert.IsTrue(ProgressionTraces.Note(state, "garrick", TraceIds.Stance, "battle:c", out stage));
        Assert.AreEqual(TraceStage.Characteristic, stage);
        Assert.AreEqual(3, ProgressionTraces.SituationCount(state, "garrick", TraceIds.Stance));
    }

    [Test]
    public void Thresholds_ComeFromRules()
    {
        Assert.AreEqual(1, ProgressionRules.Current.TraceNoticedSituations);
        Assert.AreEqual(3, ProgressionRules.Current.TraceCharacteristicSituations);
        ProgressionRules.Current.TraceCharacteristicSituations = 2;
        Assert.AreEqual(TraceStage.Characteristic, ProgressionTraces.StageFor(2));
    }

    [Test]
    public void Battle_ContributionTraces_AndLines()
    {
        GameState state = OnTheRoad("garrick", "edric");
        CampaignBattleResult result = Battle("test.traces");
        Healthy(state, result, Hero(state), "garrick", "edric");
        result.Contributions.Add(new CampaignBattleContribution { PersonId = "garrick", DamagePrevented = 2, TimesAttacked = 3, Retaliations = 2 });
        result.Contributions.Add(new CampaignBattleContribution { PersonId = "edric", TimesAttacked = 2, Retaliations = 1, ShotFromPlace = true });
        List<string> notes = new List<string>();

        CampaignBattleBridge.ApplyResult(state, result, new List<string>(), notes);

        Assert.AreEqual(TraceStage.Noticed, ProgressionTraces.Stage(state, "garrick", TraceIds.Stance));
        Assert.AreEqual(TraceStage.Noticed, ProgressionTraces.Stage(state, "garrick", TraceIds.ManyDefenses));
        Assert.AreEqual(TraceStage.Noticed, ProgressionTraces.Stage(state, "garrick", TraceIds.ManyRetaliations));
        Assert.AreEqual(TraceStage.None, ProgressionTraces.Stage(state, "edric", TraceIds.ManyDefenses), "Две атаки — ещё не «много защит».");
        Assert.AreEqual(TraceStage.None, ProgressionTraces.Stage(state, "edric", TraceIds.ManyRetaliations));
        Assert.AreEqual(TraceStage.Noticed, ProgressionTraces.Stage(state, "edric", TraceIds.ShotFromPlace));

        List<string> traceLines = notes.Where(ProgressionTraces.IsTraceLine).ToList();
        Assert.AreEqual(4, traceLines.Count);
        Assert.IsTrue(traceLines.Any(line => line.Contains(CharacterProgressionService.DisplayName(state, "garrick")) &&
                                             line.Contains("«защищался в стойке»: замечено")));
    }

    [Test]
    public void Battle_BadlyWoundedAndHeavyWound()
    {
        GameState state = OnTheRoad("garrick", "edric");
        CampaignBattleResult result = Battle("test.wounds");
        Healthy(state, result, Hero(state));
        result.Survivors.Add(new CampaignBattleSurvivor { PersonId = "garrick", HitPoints = Max(state, "garrick") / 4 });
        result.Survivors.Add(new CampaignBattleSurvivor { PersonId = "edric", HitPoints = Max(state, "edric") / 2 });

        CampaignBattleBridge.ApplyResult(state, result, new List<string>());

        Assert.AreEqual(TraceStage.Noticed, ProgressionTraces.Stage(state, "garrick", TraceIds.FoughtBadlyWounded));
        Assert.AreEqual(TraceStage.Noticed, ProgressionTraces.Stage(state, "garrick", TraceIds.SurvivedHeavyWound), "Четверть — тяжёлая рана, и он выжил.");
        Assert.AreEqual(TraceStage.None, ProgressionTraces.Stage(state, "edric", TraceIds.FoughtBadlyWounded));
        Assert.AreEqual(TraceStage.None, ProgressionTraces.Stage(state, "edric", TraceIds.SurvivedHeavyWound));
    }

    [Test]
    public void HeavyWound_FromEarlier_DoesNotCountAgain()
    {
        GameState state = OnTheRoad("garrick");
        HomePeopleService.Find(state, "garrick").Injury = ResidentInjury.Recovering;
        CampaignBattleResult result = Battle("test.old_wound");
        Healthy(state, result, Hero(state), "garrick");

        CampaignBattleBridge.ApplyResult(state, result, new List<string>());

        Assert.AreEqual(TraceStage.None, ProgressionTraces.Stage(state, "garrick", TraceIds.SurvivedHeavyWound));
    }

    [Test]
    public void RetreatForWounded_OnlyWhenSomeoneFellOrBadlyHurt()
    {
        GameState withFallen = OnTheRoad("garrick", "edric");
        CampaignBattleResult retreat = Battle("test.retreat", CampaignBattleOutcome.Retreat);
        Healthy(withFallen, retreat, Hero(withFallen), "edric");
        retreat.FallenPersonIds.Add("garrick");
        CampaignBattleBridge.ApplyResult(withFallen, retreat, new List<string>());
        Assert.AreEqual(TraceStage.Noticed, ProgressionTraces.Stage(withFallen, Hero(withFallen), TraceIds.RetreatForWounded));

        GameState healthy = OnTheRoad("garrick");
        CampaignBattleResult calmRetreat = Battle("test.calm", CampaignBattleOutcome.Retreat);
        Healthy(healthy, calmRetreat, Hero(healthy), "garrick");
        CampaignBattleBridge.ApplyResult(healthy, calmRetreat, new List<string>());
        Assert.AreEqual(TraceStage.None, ProgressionTraces.Stage(healthy, Hero(healthy), TraceIds.RetreatForWounded), "Все целы — выгоду не прерывали.");

        GameState victory = OnTheRoad("garrick", "edric");
        CampaignBattleResult win = Battle("test.win");
        Healthy(victory, win, Hero(victory), "edric");
        win.FallenPersonIds.Add("garrick");
        CampaignBattleBridge.ApplyResult(victory, win, new List<string>());
        Assert.AreEqual(TraceStage.None, ProgressionTraces.Stage(victory, Hero(victory), TraceIds.RetreatForWounded));
    }

    private static List<DevelopmentOption> Options(GameState state, string personId)
    {
        ProgressionRules.Current.FirstChoiceOptions = 35;
        CharacterProgressionService.AwardExperience(state, personId, "test.lvl", CharacterProgression.TotalExperienceForLevel(4));
        return CharacterProgressionService.GetChoiceOptions(state, personId);
    }

    [Test]
    public void Characteristic_OpensFeature_InsteadOfRequirement_AsPersonal()
    {
        GameState state = OnTheRoad("garrick");
        Assert.Less(CharacterProgressionService.GetCompetencyRank(state, "garrick", NarrativeCompetencyIds.Shooting), 2);
        Assert.IsFalse(Options(state, "garrick").Any(o => o.FeatureId == CombatFeatureIds.ColdEye),
            "Без «Стрельбы 2» и без следа «Холодный глаз» не предлагается.");

        foreach (string battle in new[] { "battle:1", "battle:2" })
            ProgressionTraces.Note(state, "garrick", TraceIds.ShotFromPlace, battle, out _);
        Assert.IsFalse(CharacterProgressionService.GetChoiceOptions(state, "garrick").Any(o => o.FeatureId == CombatFeatureIds.ColdEye),
            "«Замечено» ещё не открывает.");

        ProgressionTraces.Note(state, "garrick", TraceIds.ShotFromPlace, "battle:3", out _);
        DevelopmentOption option = CharacterProgressionService.GetChoiceOptions(state, "garrick")
            .Single(o => o.FeatureId == CombatFeatureIds.ColdEye);
        Assert.IsTrue(option.IsPersonal);
        StringAssert.Contains("Открыта следом: «стрелял с места»", option.Description);
    }

    [Test]
    public void Characteristic_MakesNeutralFeaturePersonal()
    {
        GameState state = OnTheRoad("garrick");
        DevelopmentOption before = Options(state, "garrick").Single(o => o.FeatureId == CombatFeatureIds.Stubborn);
        Assert.IsFalse(before.IsPersonal, "Без следа «Упрямец» — нейтральный вариант.");

        foreach (string battle in new[] { "battle:1", "battle:2", "battle:3" })
            ProgressionTraces.Note(state, "garrick", TraceIds.FoughtBadlyWounded, battle, out _);
        Assert.IsTrue(CharacterProgressionService.GetChoiceOptions(state, "garrick").Single(o => o.FeatureId == CombatFeatureIds.Stubborn).IsPersonal);
    }

    [Test]
    public void Traces_SurviveSaveAndLoad_AndAreVisible()
    {
        GameState state = OnTheRoad("garrick");
        ProgressionTraces.Note(state, "garrick", TraceIds.ShotFromPlace, "battle:1", out _);

        CampaignSaveData data = CampaignSaveService.ExportCampaign(state, string.Empty, 0);
        GameState restored = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(data)));

        Assert.AreEqual(TraceStage.Noticed, ProgressionTraces.Stage(restored, "garrick", TraceIds.ShotFromPlace));
        KeyValuePair<TraceDefinition, TraceStage> visible = ProgressionTraces.Visible(restored, "garrick").Single();
        Assert.AreEqual("стрелял с места", visible.Key.Name);
        Assert.IsFalse(ProgressionTraces.Note(restored, "garrick", TraceIds.ShotFromPlace, "battle:1", out _), "После загрузки тот же бой не засчитывается.");
    }

    [Test]
    public void EveryTrace_OpensAnImplementedFeature()
    {
        Assert.AreEqual(7, ProgressionTraces.Definitions.Count);
        foreach (TraceDefinition definition in ProgressionTraces.Definitions)
        {
            Assert.IsTrue(ProgressionFeatureImplementations.IsImplemented(definition.FeatureId), definition.Id);
            StringAssert.Contains("«" + definition.Name + "»", ProgressionCatalog.Current.FindTrait(definition.FeatureId).UnlockText, definition.Id);
        }
    }
}
