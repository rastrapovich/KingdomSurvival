using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

// 12Е-5 плана: первая партия особенностей вне боя (каталог §6.1) с
// вариантами, утверждёнными автором 27.09.2026.
public sealed class FeatureFirstBatchTests
{
    private sealed class FixedStats : IUnitStatsProvider
    {
        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = new UnitCombatStats { MaxHitPoints = 20, Attack = 2, Defense = 2, Damage = 3, Movement = 3, Initiative = 3, AttackRange = 1 };
            return !string.IsNullOrEmpty(unitTypeId);
        }
    }

    // Недостижимая сложность: исход до и после переброса — всегда провал,
    // поэтому переброс срабатывает при любых кубиках.
    private const int Impossible = 40;

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
        GameState state = new CampaignSetup { WorldSeed = 20260927 }.CreateCampaign();
        state.ArmySupply = 50;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, fighters.ToList(), out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        return state;
    }

    private static string Hero(GameState state) => state.GetSelectedCommander().Id;

    private static void Give(GameState state, string personId, string featureId)
    {
        Assert.IsTrue(CharacterFeatureService.Grant(state, personId, featureId, 1, FeatureSource.Story, null, out string message), message);
    }

    private static NarrativeEvaluationContext Context(GameState state, string sceneId = "")
    {
        CommanderData hero = state.GetSelectedCommander();
        return new NarrativeEvaluationContext(hero.HeroProfile, state.Narrative, worldSeed: state.WorldSeed, gameState: state)
        {
            SceneId = sceneId
        };
    }

    private static NarrativeCheckResult Check(GameState state, string checkId, string competencyId, int difficulty, string sceneId = "",
        NarrativeCheckKind kind = NarrativeCheckKind.ActiveReturnable)
    {
        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = checkId, Kind = kind, Quality = HeroQuality.Instinct, CompetencyId = competencyId, Difficulty = difficulty
        };
        return NarrativeCheckResolver.TryResolveActive(spec, Context(state, sceneId), state.WorldSeed).Result;
    }

    private static void RaiseCheck(GameState state, string competencyId, bool success, int total, int difficulty, string sceneId)
    {
        FeatureDispatcher.Raise(new FeatureEvent
        {
            Trigger = FeatureTrigger.CheckResolved,
            State = state,
            PersonId = Hero(state),
            CheckId = "test.raised." + competencyId + "." + total,
            CompetencyId = competencyId,
            CheckResult = new NarrativeCheckResult("test.raised", 1, success, 3, 3, 1, 0, 0, 0, total, difficulty),
            SceneId = sceneId,
            WorldSeed = state.WorldSeed,
            Narrative = state.Narrative
        });
    }

    // ------------------------------------------------------------------
    // Каталог и выбор

    [Test]
    public void FirstBatch_IsActive_AndCheckFeaturesOfferedOnlyToCommander()
    {
        string[] batch =
        {
            FeatureIds.Lucky, FeatureIds.AlmostThere, FeatureIds.SecondVersion, FeatureIds.FirstGlance, FeatureIds.Lead,
            FeatureIds.RainyDayStock, FeatureIds.SpareRoute, FeatureIds.FieldCamp, FeatureIds.FieldMedic, FeatureIds.Mentor
        };
        foreach (string id in batch)
        {
            Assert.IsTrue(ProgressionFeatureImplementations.IsImplemented(id), id);
            Assert.AreEqual(FeatureStatus.Active, ProgressionCatalog.Current.FindTrait(id).Status, id);
        }
        Assert.AreEqual(FeatureStatus.Candidate, ProgressionCatalog.Current.FindTrait("znayu_chto_iskat").Status,
            "«Знаю, что искать» ждёт сведений со степенью уверенности.");
        Assert.AreEqual(FeatureLimit.Expedition, ProgressionCatalog.Current.FindTrait(FeatureIds.Lucky).Limit);
        Assert.AreEqual(FeatureLimit.Expedition, ProgressionCatalog.Current.FindTrait(FeatureIds.RainyDayStock).Limit);

        GameState state = OnTheRoad("garrick");
        ProgressionRules.Current.FirstChoiceOptions = 35;
        CharacterProgressionService.AwardExperience(state, "garrick", "test.lvl", CharacterProgression.TotalExperienceForLevel(4));
        CharacterProgressionService.AwardExperience(state, Hero(state), "test.lvl", CharacterProgression.TotalExperienceForLevel(3));
        List<DevelopmentOption> fighter = CharacterProgressionService.GetChoiceOptions(state, "garrick");
        List<DevelopmentOption> hero = CharacterProgressionService.GetChoiceOptions(state, Hero(state));
        Assert.IsFalse(fighter.Any(o => o.FeatureId == FeatureIds.Lucky), "Проверки проходит Командир — бойцу Счастливчик не нужен.");
        Assert.IsTrue(hero.Any(o => o.FeatureId == FeatureIds.Lucky));
        Assert.IsTrue(fighter.Any(o => o.FeatureId == FeatureIds.RainyDayStock), "Походная особенность — и бойцу.");
    }

    [Test]
    public void OnlyImplementedRankIsOffered()
    {
        GameState state = OnTheRoad("garrick");
        ProgressionRules.Current.FirstChoiceOptions = 35;
        CharacterProgressionService.AwardExperience(state, "garrick", "test.lvl", CharacterProgression.TotalExperienceForLevel(4));
        CharacterProgressionService.SetCompetencyRank(state, "garrick", NarrativeCompetencyIds.Healing, 3);
        CharacterProgressionService.SetCompetencyRank(state, "garrick", NarrativeCompetencyIds.Herbalism, 3);
        Give(state, "garrick", FeatureIds.FieldMedic);
        Assert.IsFalse(CharacterProgressionService.GetChoiceOptions(state, "garrick").Any(o => o.FeatureId == FeatureIds.FieldMedic),
            "Ранг II «Полевого лекаря» ещё без кода.");
    }

    // ------------------------------------------------------------------
    // Проверки

    [Test]
    public void Lucky_RerollsFailedCheck_OncePerExpedition()
    {
        GameState state = OnTheRoad();
        Give(state, Hero(state), FeatureIds.Lucky);

        NarrativeCheckResult first = Check(state, "test.lucky.1", NarrativeCompetencyIds.Observation, Impossible);
        Assert.AreEqual("feature:" + FeatureIds.Lucky, first.RerolledBy, "Провал — переброс сам.");
        Assert.IsTrue(state.Progression.RecentFeatureActivations.Any(a => a.FeatureId == FeatureIds.Lucky && a.Text.Contains("переброс")));

        NarrativeCheckResult second = Check(state, "test.lucky.2", NarrativeCompetencyIds.Observation, Impossible);
        Assert.IsEmpty(second.RerolledBy, "Раз за поход.");

        FeatureDispatcher.Raise(new FeatureEvent { Trigger = FeatureTrigger.ExpeditionStarted, State = state });
        NarrativeCheckResult third = Check(state, "test.lucky.3", NarrativeCompetencyIds.Observation, Impossible);
        Assert.AreEqual("feature:" + FeatureIds.Lucky, third.RerolledBy, "Новый поход — снова.");
    }

    [Test]
    public void Lucky_DoesNotFireOnSuccess()
    {
        GameState state = OnTheRoad();
        Give(state, Hero(state), FeatureIds.Lucky);
        NarrativeCheckResult result = Check(state, "test.lucky.easy", NarrativeCompetencyIds.Observation, 1);
        Assert.IsTrue(result.Success);
        Assert.IsEmpty(result.RerolledBy);
        Assert.IsTrue(FeatureLimits.CanUse(state, Hero(state), FeatureIds.Lucky), "Успех предел не тратит.");
    }

    [Test]
    public void AlmostThere_NarrowMiss_GivesRerollToNextCheckInSameScene()
    {
        GameState state = OnTheRoad();
        Give(state, Hero(state), FeatureIds.AlmostThere);

        RaiseCheck(state, NarrativeCompetencyIds.Insight, success: false, total: 8, difficulty: 10, sceneId: "scene#1");
        NarrativeCheckResult otherScene = Check(state, "test.almost.other", NarrativeCompetencyIds.Negotiation, Impossible, "scene#2");
        Assert.IsEmpty(otherScene.RerolledBy, "Только в той же сцене.");
        NarrativeCheckResult next = Check(state, "test.almost.next", NarrativeCompetencyIds.Negotiation, Impossible, "scene#1");
        Assert.AreEqual("feature:" + FeatureIds.AlmostThere, next.RerolledBy);
        NarrativeCheckResult after = Check(state, "test.almost.after", NarrativeCompetencyIds.Negotiation, Impossible, "scene#1");
        Assert.IsEmpty(after.RerolledBy, "Переброс — только следующей проверке.");

        RaiseCheck(state, NarrativeCompetencyIds.Insight, success: false, total: 5, difficulty: 10, sceneId: "scene#1");
        Assert.IsEmpty(Check(state, "test.almost.far", NarrativeCompetencyIds.Negotiation, Impossible, "scene#1").RerolledBy,
            "Провал больше чем на 2 — не «почти».");
    }

    [Test]
    public void Lead_SuccessfulObservation_GivesPlusOneToNextInvestigation_VisibleInBreakdown()
    {
        GameState state = OnTheRoad();
        Give(state, Hero(state), FeatureIds.Lead);
        RaiseCheck(state, NarrativeCompetencyIds.Observation, success: true, total: 10, difficulty: 8, sceneId: "scene#7");

        NarrativeCheckSpec investigation = new NarrativeCheckSpec
        {
            CheckId = "test.lead.investigation", Kind = NarrativeCheckKind.ActiveReturnable, Quality = HeroQuality.Judgment,
            CompetencyId = NarrativeCompetencyIds.Investigation, Difficulty = 8
        };
        NarrativeCheckSpec insight = new NarrativeCheckSpec
        {
            CheckId = "test.lead.insight", Kind = NarrativeCheckKind.ActiveReturnable, Quality = HeroQuality.Judgment,
            CompetencyId = NarrativeCompetencyIds.Insight, Difficulty = 8
        };
        NarrativeCheckMathBreakdown withLead = NarrativeCheckResolver.ComputeBreakdown(investigation, Context(state, "scene#7"));
        Assert.AreEqual(1, withLead.AppliedContextModifier);
        Assert.AreEqual("Зацепка", withLead.AppliedModifiers.Single().Label);
        Assert.AreEqual(0, NarrativeCheckResolver.ComputeBreakdown(investigation, Context(state, "scene#8")).AppliedContextModifier);
        Assert.AreEqual(0, NarrativeCheckResolver.ComputeBreakdown(insight, Context(state, "scene#7")).AppliedContextModifier);

        NarrativeCheckResult result = NarrativeCheckResolver.TryResolveActive(investigation, Context(state, "scene#7"), state.WorldSeed).Result;
        Assert.AreEqual(1, result.AppliedContextModifier);
        Assert.AreEqual(0, NarrativeCheckResolver.ComputeBreakdown(investigation, Context(state, "scene#7")).AppliedContextModifier,
            "Прибавка — одной проверке.");
    }

    [Test]
    public void SecondVersion_FailedReturnableCheck_UnlocksOnNewKnowledge()
    {
        GameState state = OnTheRoad();
        Give(state, Hero(state), FeatureIds.SecondVersion);
        Check(state, "test.second", NarrativeCompetencyIds.Investigation, Impossible);
        Assert.IsTrue(state.Narrative.IsCheckLocked("test.second"));

        NarrativeEffect sameKnowledge = new NarrativeEffect { Type = NarrativeEffectType.AddKnowledge, StringParam = "test.fact", EffectExecutionId = "test.effect.fact" };
        sameKnowledge.Apply(Context(state));
        Assert.IsFalse(state.Narrative.IsCheckLocked("test.second"), "Новое сведение — можно вернуться.");
        Assert.IsTrue(state.Progression.RecentFeatureActivations.Any(a => a.FeatureId == FeatureIds.SecondVersion));
    }

    [Test]
    public void FirstGlance_FirstObservationAfterArrival_GetsReroll()
    {
        GameState state = OnTheRoad();
        Give(state, Hero(state), FeatureIds.FirstGlance);
        state.ActiveExpedition.Phase = CommanderState.AtLocation;
        FeatureDispatcher.Raise(new FeatureEvent
        {
            Trigger = FeatureTrigger.LocationEntered, State = state, LocationId = state.ActiveExpedition.LocationId
        });

        Assert.IsEmpty(Check(state, "test.glance.other", NarrativeCompetencyIds.Insight, Impossible).RerolledBy, "Только Наблюдательность.");
        Assert.AreEqual("feature:" + FeatureIds.FirstGlance, Check(state, "test.glance.1", NarrativeCompetencyIds.Observation, Impossible).RerolledBy);
        Assert.IsEmpty(Check(state, "test.glance.2", NarrativeCompetencyIds.Observation, Impossible).RerolledBy, "Только первая.");
    }

    // ------------------------------------------------------------------
    // Поход и лагерь

    [Test]
    public void RainyDayStock_And_SpareRoute_ReduceFirstLossOncePerExpedition()
    {
        GameState state = OnTheRoad("garrick");
        Give(state, "garrick", FeatureIds.RainyDayStock);
        Give(state, "garrick", FeatureIds.SpareRoute);
        List<string> notes = new List<string>();

        Assert.AreEqual(2, FeatureEffects.ReduceEventSupplyLoss(state, 3, notes));
        Assert.AreEqual(3, FeatureEffects.ReduceEventSupplyLoss(state, 3, notes), "1/поход.");
        Assert.AreEqual(1, FeatureEffects.ReduceRoadDelay(state, 2, notes));
        Assert.AreEqual(2, FeatureEffects.ReduceRoadDelay(state, 2, notes), "1/поход.");
        Assert.AreEqual(2, notes.Count);
        StringAssert.Contains("Запас на чёрный день", notes[0]);
    }

    [Test]
    public void FeatureOfFighterLeftHome_DoesNothing()
    {
        GameState state = OnTheRoad("garrick");
        Give(state, "edric", FeatureIds.RainyDayStock);
        Assert.AreEqual(3, FeatureEffects.ReduceEventSupplyLoss(state, 3, null), "Особенность принадлежит человеку: оставил дома — её нет.");
    }

    [Test]
    public void FieldCamp_AddsCampAction()
    {
        GameState state = OnTheRoad("garrick");
        Assert.AreEqual(2, CampRest.ActionsPerNight(state));
        Give(state, "garrick", FeatureIds.FieldCamp);
        Assert.AreEqual(3, CampRest.ActionsPerNight(state));
    }

    [Test]
    public void FieldMedic_HealsMostWoundedUpToQuarter_AtNight()
    {
        GameState state = OnTheRoad("garrick", "edric");
        Give(state, "garrick", FeatureIds.FieldMedic);
        ResidentState edric = HomePeopleService.Find(state, "edric");
        edric.HasCombatState = true;
        edric.MaxHitPoints = 20;
        edric.CurrentHitPoints = 4;
        ResidentState garrick = HomePeopleService.Find(state, "garrick");
        garrick.HasCombatState = true;
        garrick.MaxHitPoints = 20;
        garrick.CurrentHitPoints = 18;

        List<string> messages = new List<string>();
        CampRest.CompleteRest(state, messages);
        Assert.AreEqual(9, edric.CurrentHitPoints, "Самому тяжёлому — четверть максимума (5).");
        Assert.AreEqual(18, garrick.CurrentHitPoints);
        StringAssert.Contains("Полевой лекарь", messages.Single());
    }

    [Test]
    public void Mentor_SharedPracticeSinceLastCamp_GivesStudentOnePractice()
    {
        GameState state = OnTheRoad("garrick");
        string hero = Hero(state);
        Give(state, hero, FeatureIds.Mentor);
        CharacterProgressionService.SetCompetencyRank(state, hero, NarrativeCompetencyIds.ChoppingWeapons, 4);
        CharacterProgressionService.SetCompetencyRank(state, "garrick", NarrativeCompetencyIds.ChoppingWeapons, 1);
        CharacterProgressionService.AddPractice(state, hero, NarrativeCompetencyIds.ChoppingWeapons, 1);
        CharacterProgressionService.AddPractice(state, "garrick", NarrativeCompetencyIds.ChoppingWeapons, 1);
        int before = CharacterProgressionService.Get(state, "garrick").FindCompetency(NarrativeCompetencyIds.ChoppingWeapons).Practice;

        List<string> messages = new List<string>();
        CampRest.CompleteRest(state, messages);
        Assert.AreEqual(before + 1, CharacterProgressionService.Get(state, "garrick").FindCompetency(NarrativeCompetencyIds.ChoppingWeapons).Practice);
        StringAssert.Contains("Наставник", messages.Single());

        CampRest.CompleteRest(state, new List<string>());
        Assert.AreEqual(before + 1, CharacterProgressionService.Get(state, "garrick").FindCompetency(NarrativeCompetencyIds.ChoppingWeapons).Practice,
            "Без новой совместной практики — ничего; урок не считается применением.");
    }

    [Test]
    public void Mentor_NeedsRankFour()
    {
        GameState state = OnTheRoad("garrick");
        string hero = Hero(state);
        Give(state, hero, FeatureIds.Mentor);
        CharacterProgressionService.SetCompetencyRank(state, hero, NarrativeCompetencyIds.ChoppingWeapons, 3);
        CharacterProgressionService.SetCompetencyRank(state, "garrick", NarrativeCompetencyIds.ChoppingWeapons, 1);
        CharacterProgressionService.AddPractice(state, hero, NarrativeCompetencyIds.ChoppingWeapons, 1);
        CharacterProgressionService.AddPractice(state, "garrick", NarrativeCompetencyIds.ChoppingWeapons, 1);
        int before = CharacterProgressionService.Get(state, "garrick").FindCompetency(NarrativeCompetencyIds.ChoppingWeapons).Practice;
        CampRest.CompleteRest(state, new List<string>());
        Assert.AreEqual(before, CharacterProgressionService.Get(state, "garrick").FindCompetency(NarrativeCompetencyIds.ChoppingWeapons).Practice);
    }
}
