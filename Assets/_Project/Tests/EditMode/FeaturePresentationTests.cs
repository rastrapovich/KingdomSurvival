using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

// 12Е-7 плана: сработавшая особенность видна — одна форма «кто, какая,
// что дала», номера срабатываний для показа «что нового», отдельные строки
// особенностей в итогах ночлега, боя и дорожного события.
public sealed class FeaturePresentationTests
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

    private static void Give(GameState state, string personId, string featureId)
    {
        Assert.IsTrue(CharacterFeatureService.Grant(state, personId, featureId, 1, FeatureSource.Story, null, out string message), message);
    }

    [Test]
    public void Line_SaysWhoWhichAndWhat()
    {
        GameState state = OnTheRoad("garrick");
        FeatureActivation activation = new FeatureActivation
        {
            PersonId = "garrick",
            FeatureId = FeatureIds.Lead,
            Text = "следующее Расследование +1."
        };

        Assert.AreEqual(CharacterProgressionService.DisplayName(state, "garrick") + " — «Зацепка»", FeaturePresentation.Title(state, activation));
        string line = FeaturePresentation.Line(state, activation);
        Assert.AreEqual(FeaturePresentation.Tag + " " + FeaturePresentation.Title(state, activation) + ": следующее Расследование +1.", line);
        Assert.IsTrue(FeaturePresentation.IsFeatureLine(line));
        Assert.IsFalse(FeaturePresentation.IsFeatureLine("[БОЙ] Победа."));
    }

    [Test]
    public void Activations_AreNumbered_SinceReturnsOnlyNewer()
    {
        GameState state = OnTheRoad("garrick");
        int before = FeatureDispatcher.LastActivationSequence(state);
        FeatureDispatcher.Record(state, new FeatureActivation { PersonId = "garrick", FeatureId = FeatureIds.Lead, Text = "раз" });
        int middle = FeatureDispatcher.LastActivationSequence(state);
        FeatureDispatcher.Record(state, new FeatureActivation { PersonId = "garrick", FeatureId = FeatureIds.Lead, Text = "два" });

        Assert.AreEqual(before + 2, FeatureDispatcher.LastActivationSequence(state));
        CollectionAssert.AreEqual(new[] { "раз", "два" }, FeatureDispatcher.ActivationsSince(state, before).Select(a => a.Text));
        CollectionAssert.AreEqual(new[] { "два" }, FeatureDispatcher.ActivationsSince(state, middle).Select(a => a.Text));
        Assert.AreEqual(0, FeatureDispatcher.ActivationsSince(state, FeatureDispatcher.LastActivationSequence(state)).Count);
    }

    [Test]
    public void CampMorning_FeatureOnItsOwnLine()
    {
        GameState state = OnTheRoad("garrick", "edric");
        Give(state, "garrick", FeatureIds.FieldMedic);
        ResidentState edric = HomePeopleService.Find(state, "edric");
        edric.HasCombatState = true;
        edric.MaxHitPoints = 20;
        edric.CurrentHitPoints = 4;

        List<string> messages = new List<string>();
        CampRest.CompleteRest(state, messages);

        string[] lines = messages.Single().Split('\n');
        Assert.IsFalse(FeaturePresentation.IsFeatureLine(lines[0]), "Сначала итог ночи.");
        string feature = lines.Single(FeaturePresentation.IsFeatureLine);
        StringAssert.Contains("«Полевой лекарь»", feature);
        StringAssert.Contains("выхаживает " + edric.DisplayName, feature);
    }

    [Test]
    public void BattleAftermath_FeatureNotesAreFeatureLines()
    {
        GameState state = OnTheRoad("garrick");
        Give(state, "garrick", CombatFeatureIds.StillStanding);
        CampaignBattleResult result = new CampaignBattleResult { BattleId = "test.present", Outcome = CampaignBattleOutcome.Victory, Rounds = 2 };
        result.Survivors.Add(new CampaignBattleSurvivor { PersonId = state.GetSelectedCommander().Id, HitPoints = 20 });
        result.Survivors.Add(new CampaignBattleSurvivor { PersonId = "garrick", HitPoints = 10 });
        result.ForcedHeavyWoundIds.Add("garrick");
        List<string> notes = new List<string>();

        CampaignBattleBridge.ApplyResult(state, result, new List<string>(), notes);

        string feature = notes.Single(FeaturePresentation.IsFeatureLine);
        StringAssert.Contains("«Ещё на ногах»", feature);
        StringAssert.Contains(CharacterProgressionService.DisplayName(state, "garrick"), feature);
    }

    [Test]
    public void RoadLoss_FeatureNoteIsFeatureLine()
    {
        GameState state = OnTheRoad("garrick");
        Give(state, "garrick", FeatureIds.RainyDayStock);
        List<string> notes = new List<string>();

        Assert.AreEqual(2, FeatureEffects.ReduceEventSupplyLoss(state, 3, notes));

        Assert.IsTrue(FeaturePresentation.IsFeatureLine(notes.Single()));
        StringAssert.Contains("«Запас на чёрный день»", notes.Single());
    }
}
