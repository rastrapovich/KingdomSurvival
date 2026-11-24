using System.Collections.Generic;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф «Поверхности и окружение боевой арены», этап 1: совместимость,
    // стоимость по максимуму, пассивные исключения, укрытие и топь.
    public sealed class SurfaceRulesTests
    {
        [SetUp]
        public void SetUp() => SandboxSurfaceRules.Current = new SandboxSurfaceRules();

        [TearDown]
        public void TearDown() => SandboxSurfaceRules.Current = new SandboxSurfaceRules();

        private static SandboxUnitState Unit(string id, SandboxTeam team, HexCoord at, int range = 1, int movement = 4, string[] tags = null) =>
            new SandboxUnitState(id, new SandboxUnitDefinition(id + "-type", id, team == SandboxTeam.Enemy ? SandboxUnitRole.Beast : SandboxUnitRole.Militia,
                30, 4, 2, 10, movement, team == SandboxTeam.Player ? 10 : 1, range, tags), team, at);

        private static SandboxBattle Battle(Dictionary<HexCoord, SandboxTerrain> terrain, params SandboxUnitState[] units)
        {
            terrain ??= new Dictionary<HexCoord, SandboxTerrain>();
            SandboxBattle battle = new SandboxBattle(8, 6, units, terrain);
            SandboxTerrainRules.RegisterBattle(battle.Units, terrain);
            return battle;
        }

        [Test]
        public void OldArena_KeepsHillsBlockersAndCosts()
        {
            HexCoord hill = new HexCoord(2, 2), wall = new HexCoord(3, 2), plain = new HexCoord(1, 1);
            SandboxUnitState hero = Unit("hero", SandboxTeam.Player, new HexCoord(0, 0));
            SandboxBattle battle = Battle(new Dictionary<HexCoord, SandboxTerrain> { { hill, SandboxTerrain.Difficult }, { wall, SandboxTerrain.Impassable } },
                hero, Unit("foe", SandboxTeam.Enemy, new HexCoord(7, 5)));
            battle.Start();
            Assert.That(battle.GetStepCost(hill), Is.EqualTo(2));
            Assert.That(battle.GetStepCost(hero, plain), Is.EqualTo(1));
            Assert.That(battle.IsEnterable(wall), Is.False);
            Assert.That(battle.GetGround(plain), Is.EqualTo(SandboxGround.Default));
            Assert.That(SandboxTerrainRules.GetDefenseBonus(hero), Is.Zero);
        }

        [Test]
        public void ForestHill_CostsTwo_EachPassiveRemovesOnlyItsComponent()
        {
            HexCoord cell = new HexCoord(1, 0);
            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain> { { cell, SandboxTerrain.Difficult } };
            SandboxUnitState plain = Unit("plain", SandboxTeam.Player, new HexCoord(0, 0));
            SandboxUnitState forester = Unit("forester", SandboxTeam.Player, new HexCoord(0, 1), tags: new[] { SandboxTerrainTags.ForestWalker });
            SandboxUnitState both = Unit("both", SandboxTeam.Player, new HexCoord(0, 2), tags: new[] { SandboxTerrainTags.ForestWalker, SandboxTerrainTags.Highlander });
            SandboxBattle battle = Battle(terrain, plain, forester, both, Unit("foe", SandboxTeam.Enemy, new HexCoord(7, 5)));
            battle.SetVegetation(cell, SandboxVegetation.Forest);
            Assert.That(battle.GetStepCost(plain, cell), Is.EqualTo(2), "Лесной холм — максимум, а не сумма.");
            Assert.That(battle.GetStepCost(forester, cell), Is.EqualTo(2), "Лесной ходок всё ещё платит за холм.");
            Assert.That(battle.GetStepCost(both, cell), Is.EqualTo(1), "Два подходящих исключения — 1.");
        }

        [Test]
        public void BlockingGround_CannotBeBoughtWithMovement_SwimmerEntersDeepWater()
        {
            HexCoord start = new HexCoord(3, 3), cliff = new HexCoord(4, 3), deep = new HexCoord(3, 4);
            SandboxUnitState runner = Unit("runner", SandboxTeam.Player, start, movement: 99);
            SandboxUnitState swimmer = Unit("swimmer", SandboxTeam.Enemy, new HexCoord(7, 5), tags: new[] { SandboxTerrainTags.Swimmer });
            SandboxBattle battle = Battle(null, runner, swimmer);
            battle.SetGround(cliff, SandboxGround.Cliff);
            battle.SetGround(deep, SandboxGround.DeepWater);
            battle.Start();
            IReadOnlyDictionary<HexCoord, int> reachable = battle.GetReachable(runner.Id);
            Assert.That(reachable.ContainsKey(cliff), Is.False);
            Assert.That(reachable.ContainsKey(deep), Is.False, "Глубокая вода — не для обычной пехоты.");
            Assert.That(reachable.Count, Is.GreaterThan(10), "Остальное поле доступно.");
            Assert.That(battle.IsEnterableFor(swimmer, deep), Is.True);
            Assert.That(battle.IsEnterableFor(swimmer, cliff), Is.False);
        }

        [Test]
        public void ForestCover_OnlyAgainstRangedShot_NotAdjacent_CappedWithHill()
        {
            HexCoord target = new HexCoord(4, 2);
            SandboxUnitState archer = Unit("archer", SandboxTeam.Player, new HexCoord(1, 2), range: 4, tags: new[] { SandboxCombatTagRules.Ranged });
            SandboxUnitState foe = Unit("foe", SandboxTeam.Enemy, target);
            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxBattle battle = Battle(terrain, archer, foe);
            battle.Start();
            SandboxAttackPreview open = battle.PreviewAttack(archer.Id, foe.Id);
            battle.SetVegetation(target, SandboxVegetation.Forest);
            SandboxAttackPreview forest = battle.PreviewAttack(archer.Id, foe.Id);
            Assert.That(open.IsValid && forest.IsValid, Is.True);
            Assert.That(forest.Damage, Is.LessThan(open.Damage), "Лес укрывает от выстрела.");
            Assert.That(battle.GetCoverBonus(foe, new HexCoord(3, 2), projectile: true), Is.Zero, "В упор лес не укрывает.");
            Assert.That(battle.GetCoverBonus(foe, archer.Position, projectile: false), Is.Zero, "Не снаряд — нет укрытия.");

            SandboxUnitState onHill = Unit("onHill", SandboxTeam.Enemy, new HexCoord(5, 3));
            Dictionary<HexCoord, SandboxTerrain> hill = new Dictionary<HexCoord, SandboxTerrain> { { onHill.Position, SandboxTerrain.Difficult } };
            SandboxBattle hillBattle = Battle(hill, Unit("a2", SandboxTeam.Player, new HexCoord(0, 0)), onHill);
            hillBattle.SetVegetation(onHill.Position, SandboxVegetation.Forest);
            Assert.That(hillBattle.GetCoverBonus(onHill, new HexCoord(0, 0), true), Is.EqualTo(2), "Холм 2 + лес 2 = предел 4.");
            SandboxSurfaceRules.Current.TerrainDefenseCap = 3;
            Assert.That(hillBattle.GetCoverBonus(onHill, new HexCoord(0, 0), true), Is.EqualTo(1), "Сверх предела не складывается.");
        }

        [Test]
        public void Bog_LowersDefense_SwampWalkerIgnoresIt()
        {
            HexCoord bog = new HexCoord(2, 2);
            SandboxUnitState plain = Unit("plain", SandboxTeam.Player, bog);
            SandboxUnitState walker = Unit("walker", SandboxTeam.Enemy, new HexCoord(3, 2), tags: new[] { SandboxTerrainTags.SwampWalker });
            SandboxBattle battle = Battle(null, plain, walker);
            decimal before = SandboxCombatTagRules.GetEffectiveDefense(plain);
            battle.SetGround(bog, SandboxGround.Bog);
            battle.SetGround(walker.Position, SandboxGround.Bog);
            Assert.That(SandboxCombatTagRules.GetEffectiveDefense(plain), Is.EqualTo(before - 1));
            Assert.That(SandboxCombatTagRules.GetEffectiveDefense(walker), Is.EqualTo(2m), "Болотный ходок без штрафа.");
            Assert.That(battle.GetStepCost(walker, bog), Is.EqualTo(1));
            Assert.That(battle.DescribeCell(walker, bog), Does.Contain("Болотный ходок"));
            Assert.That(battle.DescribeCell(plain, bog), Does.Contain("−1 защиты"));
        }

        [Test]
        public void GroundWater_IsTheSameWaterAsAbilities()
        {
            HexCoord ford = new HexCoord(2, 1);
            SandboxBattle battle = Battle(null, Unit("hero", SandboxTeam.Player, new HexCoord(0, 0)), Unit("foe", SandboxTeam.Enemy, new HexCoord(7, 5)));
            battle.SetGround(ford, SandboxGround.ShallowWater);
            battle.Start();
            Assert.That(battle.HasCellState(ford, SandboxCellState.ShallowWater), Is.True);
            Assert.That(battle.GetStepCost(ford), Is.EqualTo(2));
        }

        // Карточка бойца «Состояния и эффекты»: местность под ним — холм даёт
        // защиту, лес — укрытие от дальних снарядов, ровная земля — ничего.
        [Test]
        public void FighterCardTerrain_NamesHillDefenseAndForestCover()
        {
            HexCoord hill = new HexCoord(1, 0), forest = new HexCoord(2, 0), plain = new HexCoord(3, 0);
            SandboxUnitState onHill = Unit("hill", SandboxTeam.Player, hill);
            SandboxUnitState inForest = Unit("forest", SandboxTeam.Player, forest);
            SandboxUnitState onPlain = Unit("plain", SandboxTeam.Player, plain);
            SandboxBattle battle = Battle(new Dictionary<HexCoord, SandboxTerrain> { { hill, SandboxTerrain.Difficult } },
                onHill, inForest, onPlain, Unit("foe", SandboxTeam.Enemy, new HexCoord(7, 5)));
            battle.SetVegetation(forest, SandboxVegetation.Forest);

            SandboxBattle.SplitCellDescription(battle.DescribeCell(onHill, hill), out string hillName, out string hillText);
            Assert.That(hillName, Is.EqualTo("Холм"));
            Assert.That(hillText, Does.Contain("защиты от высоты"));

            SandboxBattle.SplitCellDescription(battle.DescribeCell(inForest, forest), out string forestName, out string forestText);
            Assert.That(forestName, Is.EqualTo("Лес"));
            Assert.That(forestText, Does.Contain("защиты от дальних снарядов"));

            SandboxBattle.SplitCellDescription(battle.DescribeCell(onPlain, plain), out _, out string plainText);
            Assert.That(plainText, Does.Contain("Бонусов и штрафов защиты нет"));
        }
    }
}
