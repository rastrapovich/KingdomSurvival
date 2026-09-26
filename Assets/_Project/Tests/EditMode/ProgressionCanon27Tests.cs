using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Канон v1.48 §27 (+ §§25.4, 26.1, каталог v1.49 §27.11): уровень 1–100,
// выбор каждые 3 уровня, единый боевой банк 60/40 без награды за последний
// удар, практика компетенций отдельно от общего опыта, антифарм, наставник.
public sealed class ProgressionCanon27Tests
{
    private sealed class FixedStats : IUnitStatsProvider
    {
        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = new UnitCombatStats { MaxHitPoints = 20, Attack = 2, Defense = 2, Damage = 3, Movement = 3, Initiative = 3, AttackRange = unitTypeId == "archer" ? 4 : 1 };
            return !string.IsNullOrEmpty(unitTypeId);
        }
    }

    private IUnitStatsProvider previousProvider;

    [SetUp]
    public void SetUp()
    {
        previousProvider = GameState.UnitStatsProvider;
        GameState.UnitStatsProvider = new FixedStats();
    }

    [TearDown]
    public void TearDown()
    {
        GameState.UnitStatsProvider = previousProvider;
    }

    private static GameState OnTheRoad(params string[] fighters)
    {
        GameState state = new CampaignSetup { WorldSeed = 20260926 }.CreateCampaign();
        state.ArmySupply = 50;
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, fighters.ToList(), out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        return state;
    }

    private static string Hero(GameState state) => state.GetSelectedCommander().Id;

    private static CampaignBattleEnemyRecord Beast(int hp = 10, int attack = 2, int defense = 1, int damage = 3, string typeId = "forest_beast")
    {
        return new CampaignBattleEnemyRecord { UnitTypeId = typeId, MaxHitPoints = hp, Attack = attack, Defense = defense, Damage = damage, Defeated = true };
    }

    private static CampaignBattleResult Battle(GameState state, string id, CampaignBattleOutcome outcome,
        IEnumerable<CampaignBattleContribution> contributions, params CampaignBattleEnemyRecord[] enemies)
    {
        CampaignBattleResult result = new CampaignBattleResult { BattleId = id, Outcome = outcome, Rounds = 2 };
        foreach (CampaignBattleContribution contribution in contributions)
        {
            result.Contributions.Add(contribution);
            result.Survivors.Add(new CampaignBattleSurvivor { PersonId = contribution.PersonId, HitPoints = 20 });
        }
        result.Enemies.AddRange(enemies);
        return result;
    }

    // ------------------------------------------------------------------
    // Кривая уровней

    [Test]
    public void Curve_Level1To100_WithCap()
    {
        Assert.AreEqual(0, CharacterProgression.TotalExperienceForLevel(1));
        Assert.AreEqual(100, CharacterProgression.TotalExperienceForLevel(2));
        Assert.AreEqual(225, CharacterProgression.TotalExperienceForLevel(3));
        Assert.AreEqual(131175, CharacterProgression.TotalExperienceForLevel(100));
        Assert.AreEqual(1, CharacterProgression.LevelForExperience(99));
        Assert.AreEqual(2, CharacterProgression.LevelForExperience(100));
        Assert.AreEqual(100, CharacterProgression.LevelForExperience(int.MaxValue / 2));
        Assert.AreEqual(0, CharacterProgression.ExperienceToNextLevel(100));
    }

    [Test]
    public void Experience_OneSourceOncePerPerson_LevelSyncedToRoster()
    {
        GameState state = OnTheRoad("garrick");
        ExperienceGain first = CharacterProgressionService.AwardExperience(state, "garrick", "test.source", 120);
        Assert.IsNotNull(first);
        Assert.AreEqual(2, first.NewLevel);
        Assert.IsNull(CharacterProgressionService.AwardExperience(state, "garrick", "test.source", 120), "Повтор источника не даёт опыта.");
        Assert.AreEqual(2, state.Fighters.First(f => f.Id == "garrick").Level, "Карточка состава видит тот же уровень.");
        Assert.IsNotNull(CharacterProgressionService.AwardExperience(state, Hero(state), "test.source", 10), "XP героя и бойца учитываются отдельно.");
    }

    [Test]
    public void Retinue_DoesNotProgress()
    {
        GameState state = new CampaignSetup { WorldSeed = 1 }.CreateCampaign();
        Assert.IsNull(CharacterProgressionService.AwardExperience(state, "lada", "test.source", 500));
    }

    // ------------------------------------------------------------------
    // Банк боя 60/40

    [Test]
    public void Distribute_CanonExample_500()
    {
        List<BattleExperienceShare> shares = BattleExperience.Distribute(500,
            new[] { "miroslav", "ostafiy", "volchek", "radovan", "fighter" },
            new[] { 25, 20, 30, 20, 5 });
        CollectionAssert.AreEqual(new[] { 60, 60, 60, 60, 60 }, shares.Select(s => s.Participation).ToArray());
        CollectionAssert.AreEqual(new[] { 50, 40, 60, 40, 10 }, shares.Select(s => s.Contribution).ToArray());
        CollectionAssert.AreEqual(new[] { 110, 100, 120, 100, 70 }, shares.Select(s => s.Total).ToArray());
    }

    [Test]
    public void Distribute_PartySizeDoesNotGrowBank_NoRoundingLoss()
    {
        Assert.AreEqual(500, BattleExperience.Distribute(500, new[] { "a", "b" }, new[] { 1, 0 }).Sum(s => s.Total));
        Assert.AreEqual(500, BattleExperience.Distribute(500, new[] { "a", "b", "c", "d", "e" }, new[] { 3, 0, 0, 1, 0 }).Sum(s => s.Total));
        Assert.AreEqual(7, BattleExperience.Distribute(7, new[] { "a", "b", "c" }, new[] { 1, 1, 1 }).Sum(s => s.Total));

        List<BattleExperienceShare> idle = BattleExperience.Distribute(100, new[] { "a", "b" }, new[] { 0, 0 });
        Assert.AreEqual(idle[0].Total, idle[1].Total, "Без вклада 40% делятся поровну, а не пропадают.");
    }

    [Test]
    public void Battle_AwardsBank_OnlyOnce_TakenDamageIsNotContribution()
    {
        GameState state = OnTheRoad("garrick");
        string hero = Hero(state);
        CampaignBattleResult result = Battle(state, "test.bank", CampaignBattleOutcome.Victory, new[]
        {
            new CampaignBattleContribution { PersonId = hero, DamageDealt = 30, UsedMeleeAttack = true },
            // Гаррик только принимал удары — вклада нет, участие есть.
            new CampaignBattleContribution { PersonId = "garrick" }
        }, Beast(), Beast(), Beast(18, 4, 3, 5));
        int bank = BattleExperience.ComputeBank(result.Enemies);
        Assert.AreEqual(430, bank);

        List<string> notes = new List<string>();
        CampaignBattleBridge.ApplyResult(state, result, new List<string>(), notes);

        PersonProgressionData heroRecord = CharacterProgressionService.Get(state, hero);
        PersonProgressionData garrick = CharacterProgressionService.Get(state, "garrick");
        Assert.AreEqual(129 + 172, heroRecord.Experience, "Участие 258/2 + весь вклад 172.");
        Assert.AreEqual(129, garrick.Experience - CharacterProgression.TotalExperienceForLevel(garrick.StartingLevel));
        Assert.IsTrue(notes.Any(n => n.StartsWith("Опыт боя")), string.Join(" | ", notes));

        CampaignBattleBridge.ApplyResult(state, result, new List<string>());
        Assert.AreEqual(301, heroRecord.Experience, "Повторное применение итога не дублирует опыт.");
    }

    [Test]
    public void Battle_SameEnemiesAgain_TeachLess_RetreatHalves()
    {
        GameState state = OnTheRoad();
        string hero = Hero(state);
        CampaignBattleContribution[] solo = { new CampaignBattleContribution { PersonId = hero, DamageDealt = 10 } };

        CampaignBattleBridge.ApplyResult(state, Battle(state, "test.first", CampaignBattleOutcome.Victory, solo, Beast()), new List<string>());
        int afterFirst = CharacterProgressionService.Get(state, hero).Experience;
        Assert.AreEqual(110, afterFirst);

        CampaignBattleBridge.ApplyResult(state, Battle(state, "test.second", CampaignBattleOutcome.Victory, solo, Beast()), new List<string>());
        int afterSecond = CharacterProgressionService.Get(state, hero).Experience;
        Assert.AreEqual(55, afterSecond - afterFirst, "Тот же состав противников — вдвое меньше.");

        CampaignBattleBridge.ApplyResult(state, Battle(state, "test.retreat", CampaignBattleOutcome.Retreat, solo, Beast(14, 3, 2, 4, "forest_beast_strong")), new List<string>());
        int bank = CharacterProgression.EnemyExperience(14, 3, 2, 4);
        Assert.AreEqual(bank / 2, CharacterProgressionService.Get(state, hero).Experience - afterSecond, "Отход — половина банка.");
    }

    [Test]
    public void Battle_HeroFell_NoExperience()
    {
        GameState state = OnTheRoad("garrick");
        string hero = Hero(state);
        CampaignBattleResult result = Battle(state, "test.fell", CampaignBattleOutcome.Defeat,
            new[] { new CampaignBattleContribution { PersonId = "garrick", DamageDealt = 5 } }, Beast());
        result.FallenPersonIds.Add(hero);
        result.Contributions.Add(new CampaignBattleContribution { PersonId = hero });
        CampaignBattleBridge.ApplyResult(state, result, new List<string>());
        PersonProgressionData garrick = CharacterProgressionService.Get(state, "garrick");
        Assert.AreEqual(CharacterProgression.TotalExperienceForLevel(garrick.StartingLevel), garrick.Experience);
    }

    // ------------------------------------------------------------------
    // Практика

    [Test]
    public void Battle_PracticeWeaponAndShield_SeparateFromLevel()
    {
        GameState state = OnTheRoad("torvin", "garrick");
        CampaignBattleResult result = Battle(state, "test.practice", CampaignBattleOutcome.Victory, new[]
        {
            new CampaignBattleContribution { PersonId = "torvin", DamageDealt = 8, UsedMeleeAttack = true },
            new CampaignBattleContribution { PersonId = "garrick", DamagePrevented = 3 }
        }, Beast());
        CampaignBattleBridge.ApplyResult(state, result, new List<string>());

        Assert.AreEqual(CharacterProgression.PracticePerUse,
            CharacterProgressionService.Get(state, "torvin").FindCompetency(NarrativeCompetencyIds.Spearcraft).Practice,
            "Копейщик практикует копейное дело.");
        Assert.AreEqual(CharacterProgression.PracticePerUse,
            CharacterProgressionService.Get(state, "garrick").FindCompetency(NarrativeCompetencyIds.ShieldAndLine).Practice,
            "Стойка, не пропустившая урон, — практика щита и строя.");
    }

    [Test]
    public void Practice_GrowsToCeiling_ThenNeedsMentor()
    {
        GameState state = OnTheRoad();
        string hero = Hero(state);
        string id = NarrativeCompetencyIds.Observation;

        PracticeGain gain = CharacterProgressionService.AddPractice(state, hero, id, 3);
        Assert.AreEqual(1, gain.NewRank);
        Assert.AreEqual(1, state.GetSelectedCommander().HeroProfile.GetCompetency(id), "Ступень героя — в HeroProfile, её читают проверки.");

        CharacterProgressionService.AddPractice(state, hero, id, 100);
        Assert.AreEqual(CharacterProgression.PracticeCeiling, CharacterProgressionService.GetCompetencyRank(state, hero, id),
            "Собственной практикой — только до средней ступени.");
        Assert.IsTrue(CharacterProgressionService.AddPractice(state, hero, id, 50).ReachedCeiling);
        Assert.AreEqual(CharacterProgression.PracticeCeiling, CharacterProgressionService.GetCompetencyRank(state, hero, id));

        Assert.IsTrue(CharacterProgressionService.Teach(state, hero, id, 5));
        Assert.AreEqual(CharacterProgression.PracticeCeiling, CharacterProgressionService.GetCompetencyRank(state, hero, id),
            "Наставник даёт принцип, но ступень ещё нужно освоить.");
        CharacterProgressionService.AddPractice(state, hero, id, CharacterProgression.PracticeToNextRank(3));
        Assert.AreEqual(4, CharacterProgressionService.GetCompetencyRank(state, hero, id));
    }

    [Test]
    public void Practice_RepeatedSameContent_FadesToNothing()
    {
        GameState state = OnTheRoad();
        string hero = Hero(state);
        int total = 0;
        for (int i = 0; i < 20; i++)
        {
            PracticeGain gain = CharacterProgressionService.AddPractice(state, hero, NarrativeCompetencyIds.Hunting,
                CharacterProgression.PracticePerUse, "same_wolves");
            total += gain != null ? gain.Points : 0;
        }
        Assert.Less(total, 20 * CharacterProgression.PracticePerUse);
        Assert.IsNull(CharacterProgressionService.AddPractice(state, hero, NarrativeCompetencyIds.Hunting,
            CharacterProgression.PracticePerUse, "same_wolves"), "Сотый безопасный повтор почти ничему не учит.");
    }

    [Test]
    public void ActiveCheck_FirstAttemptGivesPractice_EvenOnFailure()
    {
        GameState state = OnTheRoad();
        CommanderData hero = state.GetSelectedCommander();
        NarrativeEvaluationContext context = new NarrativeEvaluationContext(hero.HeroProfile, state.Narrative, gameState: state);
        NarrativeCheckSpec spec = new NarrativeCheckSpec
        {
            CheckId = "test.practice_check",
            Kind = NarrativeCheckKind.ActiveReturnable,
            Quality = HeroQuality.Instinct,
            CompetencyId = NarrativeCompetencyIds.Investigation,
            Difficulty = NarrativeDifficulty.Legendary + 8
        };

        NarrativeCheckAttempt attempt = NarrativeCheckResolver.TryResolveActive(spec, context, 1);
        Assert.IsFalse(attempt.Result.Success);
        Assert.AreEqual(CharacterProgression.PracticePerUse,
            CharacterProgressionService.Get(state, hero.Id).FindCompetency(NarrativeCompetencyIds.Investigation).Practice);
    }

    // ------------------------------------------------------------------
    // Значимый выбор каждые 3 уровня

    [Test]
    public void Choice_EveryThreeLevels_PersonalAndNeutral()
    {
        GameState state = OnTheRoad("torvin");
        Assert.AreEqual(0, CharacterProgressionService.PendingChoices(state, "torvin"));
        CharacterProgressionService.AddPractice(state, "torvin", NarrativeCompetencyIds.Spearcraft, 2);
        CharacterProgressionService.AwardExperience(state, "torvin", "test.level3", CharacterProgression.TotalExperienceForLevel(3));
        Assert.AreEqual(3, CharacterProgressionService.Get(state, "torvin").Level);
        Assert.AreEqual(1, CharacterProgressionService.PendingChoices(state, "torvin"));

        List<DevelopmentOption> options = CharacterProgressionService.GetChoiceOptions(state, "torvin");
        Assert.IsTrue(options.Any(o => o.IsPersonal && o.CompetencyId == NarrativeCompetencyIds.Spearcraft), "Из того, что делал.");
        Assert.IsTrue(options.Any(o => !o.IsPersonal && o.Kind == DevelopmentOptionKind.Learn), "Нейтральное новое дело.");
        Assert.IsTrue(options.Any(o => o.Kind == DevelopmentOptionKind.Toughness));
        Assert.IsTrue(options.Where(o => o.CompetencyId != null).All(o => NarrativeCompetencyIds.FighterCatalog.Contains(o.CompetencyId)),
            "Каталог бойца уже каталога Командира.");

        Assert.IsTrue(CharacterProgressionService.TryApplyChoice(state, "torvin", "deepen:" + NarrativeCompetencyIds.Spearcraft, out string message), message);
        Assert.AreEqual(2, CharacterProgressionService.GetCompetencyRank(state, "torvin", NarrativeCompetencyIds.Spearcraft));
        Assert.AreEqual(0, CharacterProgressionService.PendingChoices(state, "torvin"));
        Assert.IsFalse(CharacterProgressionService.TryApplyChoice(state, "torvin", "toughness", out _), "Второй выбор — только через 3 уровня.");
    }

    [Test]
    public void Choice_Toughness_RaisesMaxHitPoints_Limited()
    {
        GameState state = OnTheRoad();
        string hero = Hero(state);
        int before = CombatStatsAssembler.Compute(state, hero).Final.MaxHitPoints;
        CharacterProgressionService.AwardExperience(state, hero, "test.lvl", CharacterProgression.TotalExperienceForLevel(12));
        Assert.AreEqual(4, CharacterProgressionService.PendingChoices(state, hero));

        for (int i = 0; i < CharacterProgression.MaxToughnessChoices; i++)
            Assert.IsTrue(CharacterProgressionService.TryApplyChoice(state, hero, "toughness", out string message), message);
        Assert.AreEqual(before + CharacterProgression.MaxToughnessChoices * CharacterProgression.ToughnessHitPoints,
            CombatStatsAssembler.Compute(state, hero).Final.MaxHitPoints);
        Assert.IsFalse(CharacterProgressionService.GetChoiceOptions(state, hero).Any(o => o.Kind == DevelopmentOptionKind.Toughness),
            "Здоровье растёт очень ограниченно.");
    }

    [Test]
    public void NewcomerWithPastLife_NoBacklogOfChoices()
    {
        GameState state = new CampaignSetup { WorldSeed = 2 }.CreateCampaign();
        state.Fighters.Add(new FighterData("veteran", "Ветеран", "Дружинник", 7, 3, "guard"));
        PersonProgressionData record = CharacterProgressionService.Get(state, "veteran");
        Assert.AreEqual(7, record.Level);
        Assert.AreEqual(0, CharacterProgressionService.PendingChoices(state, "veteran"), "Прошлые выборы — часть биографии.");
        Assert.AreEqual(1, CharacterProgressionService.GetCompetencyRank(state, "veteran", NarrativeCompetencyIds.ShieldAndLine));
    }

    // ------------------------------------------------------------------
    // Боевые числа растут от владения, а не от уровня

    [Test]
    public void CombatStats_FromCompetencyNotFromLevel()
    {
        GameState state = OnTheRoad("torvin");
        int attack = CombatStatsAssembler.Compute(state, "torvin").Final.Attack;
        CharacterProgressionService.AwardExperience(state, "torvin", "test.big", 50000);
        Assert.AreEqual(attack, CombatStatsAssembler.Compute(state, "torvin").Final.Attack, "Уровень не раздувает числа.");

        CharacterProgressionService.SetCompetencyRank(state, "torvin", NarrativeCompetencyIds.Spearcraft, 3);
        Assert.AreEqual(attack + 1, CombatStatsAssembler.Compute(state, "torvin").Final.Attack);
    }

    // ------------------------------------------------------------------
    // Эффекты и сохранение

    [Test]
    public void NarrativeEffect_GrantExperience_OncePerExecutionId()
    {
        GameState state = OnTheRoad("garrick");
        CommanderData hero = state.GetSelectedCommander();
        NarrativeEvaluationContext context = new NarrativeEvaluationContext(hero.HeroProfile, state.Narrative, gameState: state);
        NarrativeEffect effect = new NarrativeEffect { EffectExecutionId = "test.effect.xp", Type = NarrativeEffectType.GrantExperience, IntParam = 40 };
        effect.Apply(context);
        effect.Apply(context);
        Assert.AreEqual(40, CharacterProgressionService.Get(state, hero.Id).Experience);
        Assert.AreEqual(40, CharacterProgressionService.Get(state, "garrick").Experience -
                            CharacterProgression.TotalExperienceForLevel(CharacterProgressionService.Get(state, "garrick").StartingLevel));
    }

    [Test]
    public void SaveLoad_KeepsProgression_AndDoesNotReAward()
    {
        GameState state = OnTheRoad("garrick");
        string hero = Hero(state);
        CharacterProgressionService.AwardExperience(state, hero, "test.saved", 250);
        CharacterProgressionService.AddPractice(state, "garrick", NarrativeCompetencyIds.ShieldAndLine, 1);

        GameState restored = CampaignSaveService.RestoreCampaign(
            JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));

        Assert.AreEqual(3, CharacterProgressionService.Get(restored, hero).Level);
        Assert.AreEqual(1, CharacterProgressionService.PendingChoices(restored, hero));
        Assert.AreEqual(1, CharacterProgressionService.Get(restored, "garrick").FindCompetency(NarrativeCompetencyIds.ShieldAndLine).Practice);
        Assert.IsNull(CharacterProgressionService.AwardExperience(restored, hero, "test.saved", 250), "Загрузка не открывает источник заново.");
    }

    [Test]
    public void OldSaveWithoutProgression_GetsDefaults()
    {
        GameState state = new CampaignSetup { WorldSeed = 3 }.CreateCampaign();
        state.Progression = null;
        GameState restored = CampaignSaveService.RestoreCampaign(CampaignSaveService.ExportCampaign(state));
        Assert.IsNotNull(restored.Progression);
        Assert.AreEqual(1, CharacterProgressionService.Get(restored, Hero(restored)).Level);
    }

    [Test]
    public void Catalog_Has24CanonCompetencies_WithLabels()
    {
        Assert.AreEqual(24, NarrativeCompetencyIds.Known.Count);
        Assert.AreEqual(24, NarrativeCompetencyIds.Known.Distinct().Count());
        Assert.AreEqual("Копейное дело", NarrativeCompetencyLabels.GetLabel(NarrativeCompetencyIds.Spearcraft));
        Assert.AreEqual("Следопытство", NarrativeCompetencyLabels.GetLabel(NarrativeCompetencyIds.Fieldcraft));
        foreach (string id in NarrativeCompetencyIds.Known)
        {
            Assert.AreNotEqual(id, NarrativeCompetencyLabels.GetLabel(id), id);
            Assert.IsNotEmpty(NarrativeCompetencyLabels.GetDescription(id), id);
        }
    }
}
