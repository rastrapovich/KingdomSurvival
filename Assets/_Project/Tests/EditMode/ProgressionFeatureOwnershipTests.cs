using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// 12Е-2 и 12Е-3 плана: особенности у каждого человека (ранг, источник,
// save/load, выдача историей) и показ выбора 3 → 35 с активными
// особенностями каталога по требованиям, владельцу и взаимоисключениям.
public sealed class ProgressionFeatureOwnershipTests
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
    private readonly List<string> registered = new List<string>();

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
        foreach (string id in registered)
            ProgressionFeatureImplementations.Unregister(id);
        registered.Clear();
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

    private static void ReachChoice(GameState state, string personId, int level)
    {
        PersonProgressionData record = CharacterProgressionService.Get(state, personId);
        ProgressionProfile profile = CharacterProgressionService.ProfileFor(state, personId);
        CharacterProgressionService.AwardExperience(state, personId, "test.level." + level,
            profile.TotalExperienceForLevel(level) - record.Experience);
    }

    // Активная особенность-заготовка с требованием компетенции — чтобы
    // проверить отбор, не дожидаясь кода первой партии (12Е-5).
    private TraitCatalogEntry ActivateTestFeature(string id, FeatureOwner owner, params FeatureRequirement[] requirements)
    {
        TraitCatalogEntry entry = new TraitCatalogEntry
        {
            Id = id,
            Name = "Проба " + id,
            Description = "Проба.",
            Owner = owner,
            Status = FeatureStatus.Active,
            Sources = FeatureSource.LevelChoice
        };
        entry.Ranks.Add(new FeatureRank { Effect = "Первый ранг." });
        entry.Ranks.Add(new FeatureRank { Name = "Вторая проба", Effect = "Второй ранг." });
        entry.Requirements.AddRange(requirements);
        ProgressionCatalog.Current.Traits.Add(entry);
        ProgressionFeatureImplementations.Register(id);
        registered.Add(id);
        return entry;
    }

    // ------------------------------------------------------------------
    // 12Е-2

    [Test]
    public void Grant_FromStory_OnceBySource_SurvivesSaveLoad_AndShowsInCard()
    {
        GameState state = OnTheRoad("garrick");
        Assert.IsTrue(CharacterFeatureService.Grant(state, "garrick", NarrativeTraitIds.KnowsTheWay, 1,
            FeatureSource.Story, "story.test_ford", out string message), message);
        Assert.IsFalse(CharacterFeatureService.Grant(state, "garrick", NarrativeTraitIds.KnowsTheWay, 1,
            FeatureSource.Story, "story.test_ford", out _), "Тот же источник второй раз не выдаёт.");

        GameState restored = CampaignSaveService.RestoreCampaign(
            JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        OwnedFeature owned = CharacterFeatureService.GetFeatures(restored, "garrick").Single(f => f.FeatureId == NarrativeTraitIds.KnowsTheWay);
        Assert.AreEqual(1, owned.Rank);
        Assert.AreEqual(FeatureSource.Story, owned.Source);
        Assert.AreEqual("story.test_ford", owned.SourceId);
        Assert.AreEqual("Знающий дорогу", owned.Title);
        Assert.IsFalse(CharacterFeatureService.Grant(restored, "garrick", NarrativeTraitIds.KnowsTheWay, 1,
            FeatureSource.Story, "story.test_ford", out _), "Загрузка не открывает источник заново.");
        Assert.IsTrue(restored.Chronicle.Entries.Any(entry => entry.Text.Contains("Знающий дорогу")), "Получение записано в Хронику.");
    }

    [Test]
    public void Grant_ToCommander_AlsoWritesHeroProfile_ForDialogueConditions()
    {
        GameState state = OnTheRoad();
        string hero = Hero(state);
        Assert.IsFalse(state.GetSelectedCommander().HeroProfile.HasTrait(NarrativeTraitIds.KnowsTheWay));
        Assert.IsTrue(CharacterFeatureService.Grant(state, hero, NarrativeTraitIds.KnowsTheWay, 1, FeatureSource.Teacher, "teacher.test", out string message), message);
        Assert.IsTrue(state.GetSelectedCommander().HeroProfile.HasTrait(NarrativeTraitIds.KnowsTheWay));

        // Особенность, выданная диалогом прямо в профиль, видна как история.
        state.GetSelectedCommander().HeroProfile.GrantTrait(NarrativeTraitIds.Naturalist);
        OwnedFeature naturalist = CharacterFeatureService.GetFeatures(state, hero).Single(f => f.FeatureId == NarrativeTraitIds.Naturalist);
        Assert.AreEqual(FeatureSource.Story, naturalist.Source);

        Assert.IsTrue(CharacterFeatureService.Remove(state, hero, NarrativeTraitIds.KnowsTheWay));
        Assert.IsFalse(CharacterFeatureService.Has(state, hero, NarrativeTraitIds.KnowsTheWay));
    }

    [Test]
    public void Grant_RespectsOwner_Layer_AndRankCap()
    {
        GameState state = OnTheRoad("garrick");
        TraitCatalogEntry commanderOnly = ProgressionCatalog.Current.Traits.First(t => t.Owner == FeatureOwner.Commander && t.Layer == FeatureLayer.Feature);
        Assert.IsFalse(CharacterFeatureService.Grant(state, "garrick", commanderOnly.Id, 1, FeatureSource.Story, null, out string reason));
        StringAssert.Contains("только у Командира", reason);

        TraitCatalogEntry technique = ProgressionCatalog.Current.Traits.First(t => t.Layer == FeatureLayer.Technique);
        Assert.IsFalse(CharacterFeatureService.Grant(state, Hero(state), technique.Id, 1, FeatureSource.Story, null, out _),
            "Приём — не особенность.");

        TraitCatalogEntry probe = ActivateTestFeature("test_ranked", FeatureOwner.Both);
        Assert.IsTrue(CharacterFeatureService.Grant(state, "garrick", probe.Id, 9, FeatureSource.Story, null, out _));
        Assert.AreEqual(2, CharacterFeatureService.GetRank(state, "garrick", probe.Id), "Ранг не выше числа рангов карточки.");
        Assert.AreEqual("Проба test_ranked — Вторая проба", CharacterFeatureService.GetFeatures(state, "garrick").Single(f => f.FeatureId == probe.Id).Title);
    }

    [Test]
    public void Toughness_IsShownAsFeatureWithRank()
    {
        GameState state = OnTheRoad();
        string hero = Hero(state);
        ReachChoice(state, hero, 6);
        Assert.IsTrue(CharacterProgressionService.TryApplyChoice(state, hero, "toughness", out string message), message);
        Assert.IsTrue(CharacterProgressionService.TryApplyChoice(state, hero, "toughness", out message), message);
        Assert.AreEqual(2, CharacterFeatureService.GetRank(state, hero, CharacterFeatureService.ToughnessId));
    }

    [Test]
    public void KnowsTheWay_OnFighterInParty_PreparesRoadEncounter()
    {
        GameState state = OnTheRoad("garrick");
        HeroProfileData hero = new HeroProfileData { Instinct = 10 };
        hero.SetCompetency(NarrativeCompetencyIds.Fieldcraft, 5);
        ExpeditionIncidentSystem.RoadPredatorEntryState alone = ExpeditionIncidentSystem.ResolveRoadPredatorEntryState(
            hero, new NarrativeStateData(), "test.detect", out _);
        ExpeditionIncidentSystem.RoadPredatorEntryState withGuide = ExpeditionIncidentSystem.ResolveRoadPredatorEntryState(
            hero, new NarrativeStateData(), "test.detect", out _, partyKnowsTheWay: true);
        Assert.AreEqual(ExpeditionIncidentSystem.RoadPredatorEntryState.Aware, alone);
        Assert.AreEqual(ExpeditionIncidentSystem.RoadPredatorEntryState.Prepared, withGuide);
    }

    // ------------------------------------------------------------------
    // 12Е-3

    [Test]
    public void ChoiceOptionCount_Grows3To35()
    {
        Assert.AreEqual(3, CharacterProgression.ChoiceOptionCount(0));
        Assert.AreEqual(4, CharacterProgression.ChoiceOptionCount(1));
        Assert.AreEqual(35, CharacterProgression.ChoiceOptionCount(32), "33-й показ (уровень 99) — 35 карточек.");
        Assert.AreEqual(35, CharacterProgression.ChoiceOptionCount(100));
    }

    [Test]
    public void Choice_ShowsThreeThenFour_MixingPersonalNewAndNeutral()
    {
        GameState state = OnTheRoad();
        string hero = Hero(state);
        CharacterProgressionService.SetCompetencyRank(state, hero, NarrativeCompetencyIds.Rites, 0);
        CharacterProgressionService.AddPractice(state, hero, NarrativeCompetencyIds.Rites, 2);
        ReachChoice(state, hero, 6);

        List<DevelopmentOption> first = CharacterProgressionService.GetChoiceOptions(state, hero);
        Assert.AreEqual(3, first.Count);
        Assert.AreEqual(DevelopmentOptionKind.Deepen, first[0].Kind);
        Assert.AreEqual(DevelopmentOptionKind.Learn, first[1].Kind);
        Assert.AreEqual(DevelopmentOptionKind.Toughness, first[2].Kind);

        Assert.IsTrue(CharacterProgressionService.TryApplyChoice(state, hero, first[0].Id, out string message), message);
        Assert.AreEqual(4, CharacterProgressionService.GetChoiceOptions(state, hero).Count);
    }

    [Test]
    public void Choice_OffersOnlyActiveFeatures_WithMetRequirements()
    {
        GameState state = OnTheRoad("garrick");
        ProgressionRules.Current.FirstChoiceOptions = 35;
        ReachChoice(state, "garrick", 3);

        List<DevelopmentOption> options = CharacterProgressionService.GetChoiceOptions(state, "garrick");
        Assert.IsTrue(options.Any(o => o.FeatureId == NarrativeTraitIds.KnowsTheWay), "Активная особенность без требований — нейтральный вариант.");
        Assert.IsFalse(options.Any(o => o.FeatureId == NarrativeTraitIds.Naturalist), "Натуралист — только биография.");
        Assert.IsFalse(options.Any(o => o.Kind == DevelopmentOptionKind.Feature &&
                                        ProgressionCatalog.Current.FindTrait(o.FeatureId).Status != FeatureStatus.Active),
            "Кандидаты и заглушки не предлагаются.");

        TraitCatalogEntry needsSpear = ActivateTestFeature("test_spear", FeatureOwner.Both,
            new FeatureRequirement { Kind = FeatureRequirementKind.Competency, Id = NarrativeCompetencyIds.Spearcraft, Value = 2 });
        Assert.IsFalse(CharacterProgressionService.GetChoiceOptions(state, "garrick").Any(o => o.FeatureId == needsSpear.Id));
        CharacterProgressionService.SetCompetencyRank(state, "garrick", NarrativeCompetencyIds.Spearcraft, 2);
        DevelopmentOption offered = CharacterProgressionService.GetChoiceOptions(state, "garrick").Single(o => o.FeatureId == needsSpear.Id);
        Assert.IsTrue(offered.IsPersonal, "Выполненные требования — личный вариант.");
    }

    [Test]
    public void Choice_Feature_IsTaken_ThenNextRank_OneChronicleEntry()
    {
        GameState state = OnTheRoad("garrick");
        ProgressionRules.Current.FirstChoiceOptions = 35;
        ReachChoice(state, "garrick", 6);
        TraitCatalogEntry probe = ActivateTestFeature("test_two_ranks", FeatureOwner.Both);

        Assert.IsTrue(CharacterProgressionService.TryApplyChoice(state, "garrick", "feature:" + probe.Id, out string message), message);
        Assert.AreEqual(1, CharacterFeatureService.GetRank(state, "garrick", probe.Id));
        Assert.AreEqual(1, state.Chronicle.Entries.Count(entry => entry.Text.Contains(probe.Name)), "Одна запись Хроники на выбор.");

        DevelopmentOption next = CharacterProgressionService.GetChoiceOptions(state, "garrick").Single(o => o.FeatureId == probe.Id);
        Assert.AreEqual(2, next.FeatureRank);
        StringAssert.Contains("Вторая проба", next.Title);
        Assert.IsTrue(CharacterProgressionService.TryApplyChoice(state, "garrick", next.Id, out message), message);
        Assert.AreEqual(2, CharacterFeatureService.GetRank(state, "garrick", probe.Id));

        ReachChoice(state, "garrick", 9);
        Assert.IsFalse(CharacterProgressionService.GetChoiceOptions(state, "garrick").Any(o => o.FeatureId == probe.Id),
            "Взятую до последнего ранга особенность больше не предлагают.");
    }

    [Test]
    public void Choice_ExcludedAndOwnerRestricted_AreNotOffered()
    {
        GameState state = OnTheRoad("garrick");
        ProgressionRules.Current.FirstChoiceOptions = 35;
        ReachChoice(state, "garrick", 3);
        TraitCatalogEntry oneBlade = ActivateTestFeature("test_one_blade", FeatureOwner.Both);
        TraitCatalogEntry wall = ActivateTestFeature("test_wall", FeatureOwner.Both);
        TraitCatalogEntry command = ActivateTestFeature("test_command", FeatureOwner.Commander);
        oneBlade.ExcludesIds.Add(wall.Id);

        List<DevelopmentOption> before = CharacterProgressionService.GetChoiceOptions(state, "garrick");
        Assert.IsTrue(before.Any(o => o.FeatureId == wall.Id));
        Assert.IsFalse(before.Any(o => o.FeatureId == command.Id), "Командирская особенность бойцу не предлагается.");

        Assert.IsTrue(CharacterFeatureService.Grant(state, "garrick", oneBlade.Id, 1, FeatureSource.Biography, "bio.test", out string message), message);
        Assert.IsFalse(CharacterProgressionService.GetChoiceOptions(state, "garrick").Any(o => o.FeatureId == wall.Id),
            "Взаимоисключение: «Стена» не предлагается владельцу «Одного клинка».");
    }
}
