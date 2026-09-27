using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// 12Е-4 плана: единые события для особенностей, диспетчер, пределы
// «1/поход / до лагеря / 1/бой» в сохранении и переброс без сейв-скама.
public sealed class FeatureEventsTests
{
    private sealed class FixedStats : IUnitStatsProvider
    {
        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = new UnitCombatStats { MaxHitPoints = 20, Attack = 2, Defense = 2, Damage = 3, Movement = 3, Initiative = 3, AttackRange = 1 };
            return !string.IsNullOrEmpty(unitTypeId);
        }
    }

    private const string ProbeId = "test_probe_feature";
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
        FeatureDispatcher.Unregister(ProbeId);
        ProgressionRules.Current = ProgressionRules.CreateDefault();
        ProgressionCatalog.Current = ProgressionCatalog.CreateDefault();
        GameState.UnitStatsProvider = previousProvider;
    }

    private static GameState OnTheRoad(params string[] fighters)
    {
        GameState state = new CampaignSetup { WorldSeed = 20260927 }.CreateCampaign();
        state.ArmySupply = 50;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, fighters.ToList(), out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        return state;
    }

    private static GameState SaveLoad(GameState state)
    {
        return CampaignSaveService.RestoreCampaign(
            JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
    }

    // Проба-особенность в каталоге с заданным пределом.
    private static TraitCatalogEntry AddProbe(FeatureLimit limit)
    {
        TraitCatalogEntry entry = new TraitCatalogEntry
        {
            Id = ProbeId,
            Name = "Проба",
            Description = "Проба.",
            Status = FeatureStatus.Active,
            Sources = FeatureSource.LevelChoice,
            Limit = limit
        };
        entry.Ranks.Add(new FeatureRank { Effect = "Проба." });
        ProgressionCatalog.Current.Traits.Add(entry);
        return entry;
    }

    // ------------------------------------------------------------------
    // Диспетчер

    [Test]
    public void Raise_CallsOnlyOwnersHandlers_RecordsActivation()
    {
        GameState state = OnTheRoad("garrick", "edric");
        AddProbe(FeatureLimit.None);
        List<string> called = new List<string>();
        FeatureDispatcher.Register(ProbeId, FeatureTrigger.CampNight, (featureEvent, owner, rank) =>
        {
            called.Add(owner + ":" + rank);
            FeatureDispatcher.Activate(featureEvent, owner, ProbeId, "Проба сработала.");
        });
        Assert.IsTrue(ProgressionFeatureImplementations.IsImplemented(ProbeId), "Подписка делает особенность реализованной.");
        Assert.IsTrue(CharacterFeatureService.Grant(state, "garrick", ProbeId, 1, FeatureSource.Story, null, out string message), message);

        List<FeatureActivation> activations = FeatureDispatcher.Raise(new FeatureEvent { Trigger = FeatureTrigger.CampNight, State = state });

        CollectionAssert.AreEqual(new[] { "garrick:1" }, called, "Срабатывает только у владельца.");
        Assert.AreEqual(1, activations.Count);
        Assert.AreEqual("Проба сработала.", activations[0].Text);
        Assert.AreEqual(ProbeId, state.Progression.RecentFeatureActivations.Last().FeatureId);
    }

    [Test]
    public void Raise_SameEventKey_HandledOnce_EvenAfterLoad()
    {
        GameState state = OnTheRoad("garrick");
        AddProbe(FeatureLimit.None);
        int calls = 0;
        FeatureDispatcher.Register(ProbeId, FeatureTrigger.LocationEntered, (featureEvent, owner, rank) => calls++);
        CharacterFeatureService.Grant(state, "garrick", ProbeId, 1, FeatureSource.Story, null, out _);

        FeatureEvent Arrival(GameState target) => new FeatureEvent { Trigger = FeatureTrigger.LocationEntered, State = target, EventKey = "arrival:test" };
        FeatureDispatcher.Raise(Arrival(state));
        FeatureDispatcher.Raise(Arrival(state));
        Assert.AreEqual(1, calls);

        GameState restored = SaveLoad(state);
        FeatureDispatcher.Raise(Arrival(restored));
        Assert.AreEqual(1, calls, "Загрузка не повторяет обработанное событие.");
    }

    [Test]
    public void Stubs_AreRegistered_NeverCalled_AndMarkedInCatalog()
    {
        string[] stubs = { "bystraya_perezaryadka", "berezhyot_strely", "vstrechaet_pervym", "shchit_tovarishcha", "polevoy_remont" };
        foreach (string id in stubs)
        {
            Assert.IsTrue(ProgressionFeatureImplementations.IsStub(id), id);
            Assert.IsFalse(ProgressionFeatureImplementations.IsImplemented(id), id);
            Assert.IsTrue(ProgressionFeatureImplementations.TryGetStubTrigger(id, out FeatureTrigger trigger));
            Assert.IsFalse(FeatureTriggers.IsRaised(trigger), id + ": ждёт события, которого игра ещё не сообщает.");
            Assert.AreEqual(FeatureStatus.Stub, ProgressionCatalog.Current.FindTrait(id).Status, id);
        }

        GameState state = OnTheRoad("garrick");
        Assert.IsFalse(CharacterProgressionService.GetChoiceOptions(state, "garrick").Any(o => stubs.Contains(o.FeatureId)),
            "Заглушки в выбор не попадают.");
    }

    [Test]
    public void Register_ReplacesStub()
    {
        ProgressionFeatureImplementations.RegisterStub(ProbeId, FeatureTrigger.ItemDamaged);
        Assert.IsTrue(ProgressionFeatureImplementations.IsStub(ProbeId));
        FeatureDispatcher.Register(ProbeId, FeatureTrigger.CampNight, (featureEvent, owner, rank) => { });
        Assert.IsFalse(ProgressionFeatureImplementations.IsStub(ProbeId));
        Assert.IsTrue(ProgressionFeatureImplementations.IsImplemented(ProbeId));
    }

    // ------------------------------------------------------------------
    // События в игре

    [Test]
    public void Game_RaisesExpeditionCampBattleAndReturnEvents()
    {
        GameState state = OnTheRoad("garrick");
        int epoch = state.Progression.ExpeditionEpoch;
        Assert.GreaterOrEqual(epoch, 1, "Выход в поход — новая эпоха.");

        AddProbe(FeatureLimit.None);
        List<FeatureTrigger> seen = new List<FeatureTrigger>();
        foreach (FeatureTrigger trigger in new[] { FeatureTrigger.CampNight, FeatureTrigger.BattleStarted, FeatureTrigger.BattleEnded, FeatureTrigger.ReturnedHome })
            FeatureDispatcher.Register(ProbeId, trigger, (featureEvent, owner, rank) => seen.Add(featureEvent.Trigger));
        CharacterFeatureService.Grant(state, "garrick", ProbeId, 1, FeatureSource.Story, null, out _);

        int camp = state.Progression.CampEpoch;
        CampRest.CompleteRest(state, new List<string>());
        Assert.AreEqual(camp + 1, state.Progression.CampEpoch);

        CampaignBattleBridge.CreateRequest(state, "test.battle");
        CampaignBattleBridge.CreateRequest(state, "test.battle");
        CampaignBattleResult result = new CampaignBattleResult { BattleId = "test.battle", Outcome = CampaignBattleOutcome.Victory, Rounds = 1 };
        CampaignBattleBridge.ApplyResult(state, result, new List<string>());
        CampaignBattleBridge.ApplyResult(state, result, new List<string>());

        state.CompleteExpeditionReturn();

        CollectionAssert.AreEqual(
            new[] { FeatureTrigger.CampNight, FeatureTrigger.BattleStarted, FeatureTrigger.BattleEnded, FeatureTrigger.ReturnedHome },
            seen, "Каждое событие — один раз.");
    }

    [Test]
    public void ActiveCheck_RaisesCheckResolved_ForCommander()
    {
        GameState state = OnTheRoad();
        CommanderData hero = state.GetSelectedCommander();
        AddProbe(FeatureLimit.None);
        List<NarrativeCheckResult> results = new List<NarrativeCheckResult>();
        FeatureDispatcher.Register(ProbeId, FeatureTrigger.CheckResolved, (featureEvent, owner, rank) => results.Add(featureEvent.CheckResult));
        CharacterFeatureService.Grant(state, hero.Id, ProbeId, 1, FeatureSource.Story, null, out _);

        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = "test.check.resolved",
            Kind = NarrativeCheckKind.ActiveReturnable,
            Quality = HeroQuality.Instinct,
            CompetencyId = NarrativeCompetencyIds.Observation,
            Difficulty = 7
        };
        NarrativeEvaluationContext context = new NarrativeEvaluationContext(hero.HeroProfile, state.Narrative, worldSeed: state.WorldSeed, gameState: state);
        NarrativeCheckAttempt attempt = NarrativeCheckResolver.TryResolveActive(spec, context, state.WorldSeed);

        Assert.AreEqual(1, results.Count);
        Assert.AreSame(attempt.Result, results[0]);
    }

    // ------------------------------------------------------------------
    // Пределы

    [Test]
    public void Limit_Expedition_SurvivesLoad_ResetsOnNextExpedition()
    {
        GameState state = OnTheRoad("garrick");
        AddProbe(FeatureLimit.Expedition);
        Assert.IsTrue(FeatureLimits.TryConsume(state, "garrick", ProbeId));
        Assert.IsFalse(FeatureLimits.TryConsume(state, "garrick", ProbeId), "1/поход.");
        Assert.IsTrue(FeatureLimits.TryConsume(state, "edric", ProbeId), "Предел — у каждого человека свой.");

        GameState restored = SaveLoad(state);
        Assert.IsFalse(FeatureLimits.CanUse(restored, "garrick", ProbeId), "Загрузка не сбрасывает «1/поход».");

        CampRest.CompleteRest(restored, new List<string>());
        Assert.IsFalse(FeatureLimits.CanUse(restored, "garrick", ProbeId), "Ночёвка не сбрасывает «1/поход».");

        FeatureDispatcher.Raise(new FeatureEvent { Trigger = FeatureTrigger.ExpeditionStarted, State = restored });
        Assert.IsTrue(FeatureLimits.TryConsume(restored, "garrick", ProbeId), "Новый поход — новый предел.");
        Assert.AreEqual(1, restored.Progression.FeatureUses.Count(use => use.PersonId == "garrick" && use.FeatureId == ProbeId),
            "Отметки прошлого похода убраны.");
    }

    [Test]
    public void Limit_UntilCamp_ResetsAfterNight()
    {
        GameState state = OnTheRoad("garrick");
        AddProbe(FeatureLimit.UntilCamp);
        Assert.IsTrue(FeatureLimits.TryConsume(state, "garrick", ProbeId));
        Assert.IsFalse(FeatureLimits.CanUse(state, "garrick", ProbeId));
        CampRest.CompleteRest(state, new List<string>());
        Assert.IsTrue(FeatureLimits.CanUse(state, "garrick", ProbeId), "После ночёвки — снова можно.");
    }

    [Test]
    public void Limit_Battle_NeedsKey_AndCountsPerBattle()
    {
        GameState state = OnTheRoad("garrick");
        AddProbe(FeatureLimit.Battle);
        Assert.IsFalse(FeatureLimits.CanUse(state, "garrick", ProbeId), "Без ID боя непонятно, какой бой.");
        Assert.IsTrue(FeatureLimits.TryConsume(state, "garrick", ProbeId, "battle.a"));
        Assert.IsFalse(FeatureLimits.TryConsume(state, "garrick", ProbeId, "battle.a"));
        Assert.IsTrue(FeatureLimits.TryConsume(state, "garrick", ProbeId, "battle.b"));
        Assert.IsTrue(FeatureLimits.TryConsume(state, "garrick", ProbeId, "battle.c", timesPerScope: 2));
        Assert.IsTrue(FeatureLimits.TryConsume(state, "garrick", ProbeId, "battle.c", timesPerScope: 2));
        Assert.IsFalse(FeatureLimits.TryConsume(state, "garrick", ProbeId, "battle.c", timesPerScope: 2));
    }

    [Test]
    public void Limit_None_AlwaysAllowed()
    {
        GameState state = OnTheRoad();
        AddProbe(FeatureLimit.None);
        for (int i = 0; i < 5; i++)
            Assert.IsTrue(FeatureLimits.TryConsume(state, state.GetSelectedCommander().Id, ProbeId));
    }

    // ------------------------------------------------------------------
    // Переброс

    private static NarrativeStateData StateWithFailedCheck(string checkId, NarrativeCheckKind kind, int seed, out NarrativeCheckResult first)
    {
        NarrativeStateData narrative = new NarrativeStateData();
        HeroProfileData hero = new HeroProfileData { Instinct = 1 };
        NarrativeCheckSpec spec = new NarrativeCheckSpec { CheckId = checkId, Kind = kind, Quality = HeroQuality.Instinct, Difficulty = 12 };
        first = NarrativeCheckResolver.TryResolveActive(spec, new NarrativeEvaluationContext(hero, narrative, worldSeed: seed), seed).Result;
        return narrative;
    }

    [Test]
    public void Reroll_ChangesLowerDie_IsDeterministic_AndOnlyOnce()
    {
        NarrativeStateData narrative = StateWithFailedCheck("test.reroll", NarrativeCheckKind.ActiveReturnable, 77, out NarrativeCheckResult first);
        Assert.IsTrue(NarrativeCheckReroll.CanReroll(narrative, "test.reroll"));

        NarrativeCheckResult rerolled = NarrativeCheckReroll.Reroll(narrative, 77, "test.reroll", "feature:schastlivchik");
        Assert.IsNotNull(rerolled);
        Assert.AreEqual("feature:schastlivchik", rerolled.RerolledBy);
        Assert.AreEqual(Mathf.Min(first.DieOne, first.DieTwo), rerolled.RerolledDieBefore, "Перебрасывается меньший кубик.");
        Assert.AreEqual(Mathf.Max(first.DieOne, first.DieTwo), first.DieOne <= first.DieTwo ? rerolled.DieTwo : rerolled.DieOne);
        Assert.AreEqual(rerolled.DieOne + rerolled.DieTwo + (first.Total - first.DieOne - first.DieTwo), rerolled.Total);
        Assert.AreEqual(rerolled.Total >= rerolled.Difficulty, rerolled.Success);
        Assert.AreSame(rerolled, narrative.FindHistory("test.reroll").LatestResult, "Переброс заменяет результат, а не добавляет попытку.");
        Assert.AreEqual(1, narrative.FindHistory("test.reroll").Attempts.Count);
        Assert.IsFalse(NarrativeCheckReroll.CanReroll(narrative, "test.reroll"), "Второй раз тот же результат не перебрасывается.");
        Assert.IsNull(NarrativeCheckReroll.Reroll(narrative, 77, "test.reroll", "feature:other"));

        // Тот же мир, та же проверка — тот же переброс (загрузка ничего не даёт).
        NarrativeStateData again = StateWithFailedCheck("test.reroll", NarrativeCheckKind.ActiveReturnable, 77, out _);
        NarrativeCheckResult same = NarrativeCheckReroll.Reroll(again, 77, "test.reroll", "feature:schastlivchik");
        Assert.AreEqual(rerolled.DieOne, same.DieOne);
        Assert.AreEqual(rerolled.DieTwo, same.DieTwo);
    }

    [Test]
    public void Reroll_AfterSaveLoad_GivesSameResult_AndUpdatesLock()
    {
        GameState state = OnTheRoad();
        CommanderData hero = state.GetSelectedCommander();
        hero.HeroProfile.Instinct = 1;
        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = "test.reroll.save", Kind = NarrativeCheckKind.ActiveReturnable, Quality = HeroQuality.Instinct, Difficulty = 12
        };
        NarrativeCheckResolver.TryResolveActive(spec, new NarrativeEvaluationContext(hero.HeroProfile, state.Narrative, worldSeed: state.WorldSeed, gameState: state), state.WorldSeed);

        GameState restored = SaveLoad(state);
        NarrativeCheckResult before = NarrativeCheckReroll.Reroll(state.Narrative, state.WorldSeed, spec.CheckId, "feature:probe");
        NarrativeCheckResult after = NarrativeCheckReroll.Reroll(restored.Narrative, restored.WorldSeed, spec.CheckId, "feature:probe");
        Assert.AreEqual(before.Total, after.Total, "Переброс после загрузки — тот же.");
        Assert.AreEqual(!after.Success, restored.Narrative.IsCheckLocked(spec.CheckId), "Возвратная проверка заперта только при провале.");
        Assert.AreEqual("feature:probe", SaveLoad(restored).Narrative.FindHistory(spec.CheckId).LatestResult.RerolledBy);
    }

    [Test]
    public void Reroll_NotForPassiveChecks()
    {
        NarrativeStateData narrative = new NarrativeStateData();
        NarrativeCheckSpec spec = new NarrativeCheckSpec { CheckId = "test.passive", Kind = NarrativeCheckKind.Passive, Quality = HeroQuality.Instinct, Difficulty = 10 };
        NarrativeCheckResolver.ResolvePassive(spec, new NarrativeEvaluationContext(new HeroProfileData(), narrative));
        Assert.IsFalse(NarrativeCheckReroll.CanReroll(narrative, "test.passive"), "У пассивной проверки нет кубиков.");
    }
}
