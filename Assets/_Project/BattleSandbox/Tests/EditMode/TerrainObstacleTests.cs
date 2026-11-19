using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф «Поверхности и окружение боевой арены», этап 2: объекты
    // местности, общая линия огня, мягкое укрытие, разрушение, мост.
    public sealed class TerrainObstacleTests
    {
        [SetUp]
        public void SetUp() => SandboxSurfaceRules.Current = new SandboxSurfaceRules();

        private static SandboxUnitState Unit(string id, SandboxTeam team, HexCoord at, int range = 1, int movement = 4, string[] tags = null, int initiative = -1) =>
            new SandboxUnitState(id, new SandboxUnitDefinition(id + "-type", id, team == SandboxTeam.Enemy ? SandboxUnitRole.Beast : SandboxUnitRole.Militia,
                30, 4, 2, 10, movement, initiative >= 0 ? initiative : team == SandboxTeam.Player ? 10 : 1, range, tags), team, at);

        private static SandboxBattle Battle(params SandboxUnitState[] units)
        {
            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxBattle battle = new SandboxBattle(9, 6, units, terrain);
            SandboxTerrainRules.RegisterBattle(battle.Units, terrain);
            return battle;
        }

        private static SandboxUnitState Archer(HexCoord at) =>
            Unit("archer", SandboxTeam.Player, at, range: 5, tags: new[] { SandboxCombatTagRules.Ranged });

        [Test]
        public void Boulder_BlocksWalkAndShot_DeepWaterBlocksOnlyWalk()
        {
            SandboxUnitState archer = Archer(new HexCoord(1, 2));
            SandboxUnitState foe = Unit("foe", SandboxTeam.Enemy, new HexCoord(5, 2));
            SandboxBattle battle = Battle(archer, foe);
            battle.Start();
            Assert.That(battle.PreviewAttack(archer.Id, foe.Id).IsValid, Is.True);

            battle.SetGround(new HexCoord(3, 2), SandboxGround.DeepWater);
            Assert.That(battle.PreviewAttack(archer.Id, foe.Id).IsValid, Is.True, "Глубокая вода стреле не мешает.");
            Assert.That(battle.IsEnterable(new HexCoord(3, 2)), Is.False);

            battle.SetGround(new HexCoord(3, 2), SandboxGround.Default);
            battle.PlaceTerrainObject(SandboxObjectKind.Boulder, new[] { new HexCoord(3, 2) });
            SandboxAttackPreview blocked = battle.PreviewAttack(archer.Id, foe.Id);
            Assert.That(blocked.IsValid, Is.False);
            Assert.That(blocked.Reason, Does.Contain("валун"), "Причина отказа названа.");
            Assert.That(battle.IsEnterable(new HexCoord(3, 2)), Is.False);
            Assert.That(battle.GetReachable(archer.Id).ContainsKey(new HexCoord(3, 2)), Is.False, "Через валун не пройти.");
        }

        [Test]
        public void LineOfFire_IsSymmetric()
        {
            SandboxBattle battle = Battle(Unit("a", SandboxTeam.Player, new HexCoord(0, 0)), Unit("b", SandboxTeam.Enemy, new HexCoord(8, 5)));
            for (int q1 = 0; q1 < 9; q1++)
                for (int r1 = 0; r1 < 6; r1++)
                {
                    HexCoord a = new HexCoord(q1, r1), b = new HexCoord(8 - q1, 5 - r1);
                    List<HexCoord> forward = SandboxBattle.SymmetricLine(a, b);
                    List<HexCoord> back = SandboxBattle.SymmetricLine(b, a);
                    back.Reverse();
                    Assert.That(back, Is.EqualTo(forward), a + " ↔ " + b);
                }
            battle.PlaceTerrainObject(SandboxObjectKind.Boulder, new[] { new HexCoord(4, 2) });
            Assert.That(battle.GetLineOfFire(new HexCoord(1, 2), new HexCoord(7, 2)).Clear,
                Is.EqualTo(battle.GetLineOfFire(new HexCoord(7, 2), new HexCoord(1, 2)).Clear));
        }

        [Test]
        public void LowWall_GivesCover_NotSummedWithForest()
        {
            SandboxUnitState archer = Archer(new HexCoord(1, 2));
            SandboxUnitState foe = Unit("foe", SandboxTeam.Enemy, new HexCoord(5, 2));
            SandboxBattle battle = Battle(archer, foe);
            battle.Start();
            int open = battle.PreviewAttack(archer.Id, foe.Id).Damage;
            battle.PlaceTerrainObject(SandboxObjectKind.StoneWall, new[] { new HexCoord(4, 2) });
            Assert.That(battle.PreviewAttack(archer.Id, foe.Id).IsValid, Is.True, "Низкая стенка не закрывает выстрел.");
            Assert.That(battle.GetCoverBonus(foe, archer.Position, true), Is.EqualTo(2));
            Assert.That(battle.PreviewAttack(archer.Id, foe.Id).Damage, Is.LessThan(open));
            battle.SetVegetation(foe.Position, SandboxVegetation.Forest);
            Assert.That(battle.GetCoverBonus(foe, archer.Position, true), Is.EqualTo(2), "Укрытия не складываются.");
        }

        [Test]
        public void Wattle_CanBeBroken_ThenOpensPathAndCover()
        {
            HexCoord fence = new HexCoord(2, 2);
            SandboxUnitState hero = Unit("hero", SandboxTeam.Player, new HexCoord(1, 2), movement: 1);
            SandboxUnitState foe = Unit("foe", SandboxTeam.Enemy, new HexCoord(8, 5));
            SandboxBattle battle = Battle(hero, foe);
            battle.Start();
            SandboxBattleObject wattle = battle.PlaceTerrainObject(SandboxObjectKind.Wattle, new[] { fence });
            Assert.That(wattle.HitPoints, Is.EqualTo(8));
            Assert.That(battle.GetReachable(hero.Id).ContainsKey(fence), Is.False);
            SandboxContextAction hit = battle.GetContextActions(hero.Id).FirstOrDefault(a => a.ObjectId == wattle.Id);
            Assert.That(hit, Is.Not.Null, "Плетень можно ударить.");
            Assert.That(battle.TryContextAction(hero.Id, hit, out string message), Is.True, message);
            Assert.That(wattle.IsDestroyed, Is.True, "Урон 10 ломает плетень (8 HP).");
            Assert.That(battle.IsEnterable(fence), Is.True, "После разрушения путь открыт.");
            Assert.That(battle.GetLineOfFire(new HexCoord(0, 2), new HexCoord(4, 2)).LowCover, Is.False, "И укрытия больше нет.");
        }

        [Test]
        public void Bridge_CrossesDeepWaterAndBog_FallenLogCostsTwo_DeadfallBlocks()
        {
            SandboxUnitState hero = Unit("hero", SandboxTeam.Player, new HexCoord(1, 2));
            SandboxBattle battle = Battle(hero, Unit("foe", SandboxTeam.Enemy, new HexCoord(8, 5)));
            HexCoord water = new HexCoord(3, 2), bog = new HexCoord(4, 2), log = new HexCoord(2, 3), dead = new HexCoord(2, 1);
            battle.SetGround(water, SandboxGround.DeepWater);
            battle.SetGround(bog, SandboxGround.Bog);
            battle.PlaceTerrainObject(SandboxObjectKind.Bridge, new[] { water, bog });
            battle.PlaceTerrainObject(SandboxObjectKind.FallenLog, new[] { log });
            battle.PlaceTerrainObject(SandboxObjectKind.Deadfall, new[] { dead });
            battle.Start();
            Assert.That(battle.IsEnterable(water), Is.True, "По мосту — через глубокую воду.");
            Assert.That(battle.GetStepCost(hero, water), Is.EqualTo(1));
            Assert.That(battle.GetStepCost(hero, bog), Is.EqualTo(1), "Мост перекрывает топь.");
            Assert.That(battle.GetStepCost(hero, log), Is.EqualTo(2), "Перешагнуть ствол — 2.");
            Assert.That(battle.IsEnterable(dead), Is.False, "Завал — обход.");
        }

        [Test]
        public void AbilityLineOfSight_AndShooterPosition_RespectBoulder()
        {
            SandboxUnitState archer = Archer(new HexCoord(1, 2));
            SandboxUnitState foe = Unit("foe", SandboxTeam.Enemy, new HexCoord(5, 2));
            SandboxBattle battle = Battle(archer, foe);
            battle.PlaceTerrainObject(SandboxObjectKind.Boulder, new[] { new HexCoord(3, 2) });
            battle.Start();
            Assert.That(battle.HasLineOfSight(archer.Position, foe.Position), Is.False, "Способности видят тот же валун.");
            Assert.That(battle.TryFindAttackPosition(archer.Id, foe.Id, out HexCoord from, out _), Is.True, "Стрелок ищет позицию с линией огня.");
            Assert.That(battle.GetLineOfFire(from, foe.Position).Clear || from.DistanceTo(foe.Position) <= 1, Is.True);
            Assert.That(from, Is.Not.EqualTo(archer.Position));
        }
    }
}
