using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф «Поверхности и окружение боевой арены», этап 3: временные
    // поверхности, опасность раз за цикл, взаимодействия, лёд и толчок.
    public sealed class TerrainHazardTests
    {
        [SetUp]
        public void SetUp() => SandboxSurfaceRules.Current = new SandboxSurfaceRules();

        private static SandboxUnitState Unit(string id, SandboxTeam team, HexCoord at, int movement = 6, string[] tags = null, int range = 1) =>
            new SandboxUnitState(id, new SandboxUnitDefinition(id + "-type", id, team == SandboxTeam.Enemy ? SandboxUnitRole.Beast : SandboxUnitRole.Militia,
                30, 4, 2, 10, movement, team == SandboxTeam.Player ? 10 : 1, range, tags), team, at);

        private static SandboxBattle Battle(params SandboxUnitState[] units)
        {
            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxBattle battle = new SandboxBattle(10, 6, units, terrain);
            SandboxTerrainRules.RegisterBattle(battle.Units, terrain);
            return battle;
        }

        private static string Force(SandboxBattle battle, SandboxUnitState unit, HexCoord cell) =>
            (string)typeof(SandboxBattle).GetMethod("ForceMove", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(battle, new object[] { unit, cell, "отброшен" });

        [Test]
        public void ThreeBurningCells_OneTickPerCycle_NewActivationTicksAgain()
        {
            SandboxUnitState hero = Unit("hero", SandboxTeam.Player, new HexCoord(0, 2));
            SandboxUnitState foe = Unit("foe", SandboxTeam.Enemy, new HexCoord(9, 5));
            SandboxBattle battle = Battle(hero, foe);
            battle.Start();
            foreach (int q in new[] { 1, 2, 3 })
                battle.AddEffect(new HexCoord(q, 2), SandboxEffectKind.Fire, 0);
            Assert.That(battle.TryMove(hero.Id, new HexCoord(3, 2), out string message), Is.True, message);
            Assert.That(hero.HitPoints, Is.EqualTo(28), "Три горящих клетки — один тик.");
            Assert.That(battle.PreviewHazardDamage(hero, new[] { new HexCoord(3, 2), new HexCoord(2, 2) }), Is.Zero, "Повторный вход до новой активации не повторяет урон.");
            battle.EndActivation();
            while (battle.CurrentUnit != null && battle.CurrentUnit.Id != hero.Id && battle.Phase == SandboxBattlePhase.InProgress)
                battle.EndActivation();
            Assert.That(hero.HitPoints, Is.EqualTo(26), "Остался в огне — новый тик на своей активации.");
        }

        [Test]
        public void WaterExtinguishes_ColdFreezes_FireMelts_ExpiryRestoresWater()
        {
            SandboxBattle battle = Battle(Unit("hero", SandboxTeam.Player, new HexCoord(0, 0)), Unit("foe", SandboxTeam.Enemy, new HexCoord(9, 5)));
            HexCoord fire = new HexCoord(2, 2), pool = new HexCoord(4, 2);
            battle.SetGround(pool, SandboxGround.ShallowWater);
            battle.Start();
            battle.AddEffect(fire, SandboxEffectKind.Fire, 0);
            battle.ApplyImpulse(SandboxImpulse.Water, new[] { fire });
            Assert.That(battle.HasEffect(fire, SandboxEffectKind.Fire), Is.False, "Вода тушит.");
            Assert.That(battle.AddEffect(pool, SandboxEffectKind.Fire, 2), Is.Null, "Огонь не ложится на воду.");
            Assert.That(battle.AddEffect(pool, SandboxEffectKind.Tar, 2), Is.Null, "Смола не ложится на воду.");

            battle.ApplyImpulse(SandboxImpulse.Cold, new[] { pool }, 1);
            Assert.That(battle.HasEffect(pool, SandboxEffectKind.Ice), Is.True, "Холод замораживает.");
            Assert.That(battle.GetStepCost(pool), Is.EqualTo(2));
            battle.ApplyImpulse(SandboxImpulse.Fire, new[] { pool });
            Assert.That(battle.HasEffect(pool, SandboxEffectKind.Ice), Is.False, "Огонь растапливает.");
            Assert.That(battle.HasShallowWater(pool), Is.True, "Под льдом — исходная вода.");
            Assert.That(battle.HasEffect(pool, SandboxEffectKind.Fire), Is.False, "На воде огонь не держится.");
        }

        [Test]
        public void Expiry_CreatedInR_WithD_RemovedAtStartOfRPlusDPlusOne()
        {
            SandboxUnitState hero = Unit("hero", SandboxTeam.Player, new HexCoord(0, 0));
            SandboxBattle battle = Battle(hero, Unit("foe", SandboxTeam.Enemy, new HexCoord(9, 5)));
            battle.Start();
            HexCoord cell = new HexCoord(5, 2);
            battle.AddEffect(cell, SandboxEffectKind.Smoke, 2);
            int created = battle.Round;
            while (battle.Round < created + 2 && battle.Phase == SandboxBattlePhase.InProgress)
                battle.EndActivation();
            Assert.That(battle.HasEffect(cell, SandboxEffectKind.Smoke), Is.True, "Держится весь раунд R+D.");
            while (battle.Round < created + 3 && battle.Phase == SandboxBattlePhase.InProgress)
                battle.EndActivation();
            Assert.That(battle.HasEffect(cell, SandboxEffectKind.Smoke), Is.False, "Снят в начале R+D+1.");
        }

        [Test]
        public void TarIgnites_WithoutDoubleTick_WebBurns_SpiderIgnoresWeb()
        {
            SandboxUnitState spider = Unit("spider", SandboxTeam.Enemy, new HexCoord(9, 5), tags: new[] { SandboxTerrainTags.SpiderStep });
            SandboxUnitState hero = Unit("hero", SandboxTeam.Player, new HexCoord(0, 2));
            SandboxBattle battle = Battle(hero, spider);
            battle.Start();
            HexCoord tar = new HexCoord(1, 2), web = new HexCoord(5, 5);
            battle.AddEffect(tar, SandboxEffectKind.Tar, 0);
            battle.AddEffect(web, SandboxEffectKind.Web, 0);
            Assert.That(battle.GetStepCost(hero, web), Is.EqualTo(2));
            Assert.That(battle.GetStepCost(spider, web), Is.EqualTo(1), "Паучья поступь — без замедления.");
            battle.ApplyImpulse(SandboxImpulse.Fire, new[] { tar, web });
            Assert.That(battle.HasEffect(tar, SandboxEffectKind.Tar), Is.False);
            Assert.That(battle.EffectsAt(tar).Count(e => e.Kind == SandboxEffectKind.Fire), Is.EqualTo(1), "Смола → один огонь.");
            Assert.That(battle.HasEffect(web, SandboxEffectKind.Web), Is.False, "Паутина сгорает.");
            Assert.That(battle.TryMove(hero.Id, tar, out string message), Is.True, message);
            Assert.That(hero.HitPoints, Is.EqualTo(28), "Один тик огня, без двойного урона поверхностей.");
        }

        [Test]
        public void Electricity_FollowsConnectedWater_Limited_Deterministic_NotOnBridge()
        {
            SandboxBattle battle = Battle(Unit("hero", SandboxTeam.Player, new HexCoord(0, 0)), Unit("foe", SandboxTeam.Enemy, new HexCoord(9, 5)));
            for (int q = 0; q < 10; q++)
                for (int r = 2; r < 4; r++)
                    battle.SetGround(new HexCoord(q, r), SandboxGround.ShallowWater);
            battle.SetGround(new HexCoord(4, 5), SandboxGround.ShallowWater);
            battle.PlaceTerrainObject(SandboxObjectKind.Bridge, new[] { new HexCoord(2, 2) });
            battle.Start();
            battle.ApplyImpulse(SandboxImpulse.Electric, new[] { new HexCoord(0, 2) });
            List<HexCoord> live = battle.CellEffects.Where(p => p.Value.Any(e => e.Kind == SandboxEffectKind.Electric)).Select(p => p.Key).OrderBy(c => c).ToList();
            Assert.That(live.Count, Is.EqualTo(SandboxHazardRules.ElectricMaxCells), "Не больше предела.");
            Assert.That(live.Contains(new HexCoord(4, 5)), Is.False, "Отдельная лужа не связана.");
            Assert.That(live.Contains(new HexCoord(2, 2)), Is.False, "Под мостом ток не идёт.");

            SandboxBattle again = Battle(Unit("hero", SandboxTeam.Player, new HexCoord(0, 0)), Unit("foe", SandboxTeam.Enemy, new HexCoord(9, 5)));
            for (int q = 0; q < 10; q++)
                for (int r = 2; r < 4; r++)
                    again.SetGround(new HexCoord(q, r), SandboxGround.ShallowWater);
            again.PlaceTerrainObject(SandboxObjectKind.Bridge, new[] { new HexCoord(2, 2) });
            again.Start();
            again.ApplyImpulse(SandboxImpulse.Electric, new[] { new HexCoord(0, 2) });
            Assert.That(again.CellEffects.Where(p => p.Value.Any(e => e.Kind == SandboxEffectKind.Electric)).Select(p => p.Key).OrderBy(c => c), Is.EqualTo(live), "Воспроизводимо.");
        }

        [Test]
        public void PushOntoIce_OneExtraStep_StopsAtObstacle_SureFootedHolds_NoDeepWater()
        {
            SandboxUnitState hero = Unit("hero", SandboxTeam.Player, new HexCoord(0, 0));
            SandboxUnitState foe = Unit("foe", SandboxTeam.Enemy, new HexCoord(2, 2));
            SandboxUnitState steady = Unit("steady", SandboxTeam.Enemy, new HexCoord(2, 4), tags: new[] { SandboxTerrainTags.SureFooted });
            SandboxBattle battle = Battle(hero, foe, steady);
            for (int q = 3; q <= 6; q++)
            {
                battle.SetGround(new HexCoord(q, 2), SandboxGround.Ice);
                battle.SetGround(new HexCoord(q, 4), SandboxGround.Ice);
            }
            battle.SetGround(new HexCoord(5, 0), SandboxGround.DeepWater);
            battle.Start();
            Force(battle, foe, new HexCoord(3, 2));
            Assert.That(foe.Position, Is.EqualTo(new HexCoord(4, 2)), "Один дополнительный шаг по льду, не больше.");
            Force(battle, steady, new HexCoord(3, 4));
            Assert.That(steady.Position, Is.EqualTo(new HexCoord(3, 4)), "Цепкая поступь — без скольжения.");
            battle.PlaceTerrainObject(SandboxObjectKind.Boulder, new[] { new HexCoord(6, 2) });
            Force(battle, foe, new HexCoord(5, 2));
            Assert.That(foe.Position, Is.EqualTo(new HexCoord(5, 2)), "Преграда на ледяном шаге — остановка.");
            string note = Force(battle, hero, new HexCoord(5, 0));
            Assert.That(note, Does.Contain("невозможно"), "Толчок не отправляет в глубокую воду.");
            Assert.That(hero.Position, Is.EqualTo(new HexCoord(0, 0)));
        }

        [Test]
        public void Smoke_BlocksRangedShot_RootsRemovedOnlyWithTheirSource()
        {
            SandboxUnitState archer = Unit("archer", SandboxTeam.Player, new HexCoord(1, 2), range: 5, tags: new[] { SandboxCombatTagRules.Ranged });
            SandboxUnitState foe = Unit("foe", SandboxTeam.Enemy, new HexCoord(5, 2));
            SandboxBattle battle = Battle(archer, foe);
            battle.Start();
            battle.AddEffect(new HexCoord(3, 2), SandboxEffectKind.Smoke, 2);
            Assert.That(battle.PreviewAttack(archer.Id, foe.Id).IsValid, Is.False, "Дым закрывает дальний выстрел.");

            SandboxBattleObject nest = battle.PlaceTerrainObject(SandboxObjectKind.RootNest, new[] { new HexCoord(7, 4) });
            battle.AddEffect(new HexCoord(7, 3), SandboxEffectKind.Roots, 0, 0, nest.Id);
            battle.AddEffect(new HexCoord(8, 3), SandboxEffectKind.Roots, 0);
            typeof(SandboxBattle).GetMethod("OnObjectDestroyed", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(battle, new object[] { nest });
            Assert.That(battle.HasEffect(new HexCoord(7, 3), SandboxEffectKind.Roots), Is.False, "Корни гнезда сняты.");
            Assert.That(battle.HasEffect(new HexCoord(8, 3), SandboxEffectKind.Roots), Is.True, "Независимые корни остались.");
        }
    }
}
