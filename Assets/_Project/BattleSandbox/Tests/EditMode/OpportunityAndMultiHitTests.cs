using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф завершения, этапы 5–6: реакции на уход из соседства и несколько
    // попаданий одного действия.
    public sealed class OpportunityAndMultiHitTests
    {
        private static SandboxUnitState U(string id, SandboxTeam team, int q, int r, int init, int range = 1, int hp = 20, params string[] tags)
        {
            return new SandboxUnitState(id, new SandboxUnitDefinition(id, id, SandboxUnitRole.Militia, hp, 2, 2, 3, 4, init, range, tags), team, new HexCoord(q, r));
        }

        private static SandboxBattle Build(params SandboxUnitState[] units)
        {
            Dictionary<HexCoord, SandboxTerrain> map = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxTerrainRules.RegisterBattle(units, map);
            SandboxBattle battle = new SandboxBattle(14, 10, units, map);
            battle.Start();
            return battle;
        }

        private const string Opportunist = "passive.20261119.249";

        [Test]
        public void LeavingAdjacency_TriggersStrike_OncePerRound()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 4, 4, 9), U("guard", SandboxTeam.Enemy, 5, 4, 5, tags: Opportunist));
            SandboxUnitState hero = b.GetUnit("hero");
            Assert.IsTrue(b.TryMove("hero", new HexCoord(2, 4), out string m), m);
            Assert.AreEqual(17, hero.HitPoints, "удар за выход: " + m);
            Assert.IsTrue(b.CombatEvents.Any(e => e.Kind == SandboxCombatEventKind.HitResolved && e.TargetUnitId == "hero"));
        }

        [Test]
        public void SteppingBetweenAdjacentHexes_IsNotLeaving()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 4, 4, 9), U("guard", SandboxTeam.Enemy, 5, 4, 5, tags: Opportunist));
            HexCoord stillAdjacent = new HexCoord(5, 4).Neighbors().First(c => c != new HexCoord(4, 4) && c.DistanceTo(new HexCoord(4, 4)) == 1 && b.IsFreeCell(c));
            Assert.IsTrue(b.TryMove("hero", stillAdjacent, out _));
            Assert.AreEqual(20, b.GetUnit("hero").HitPoints);
        }

        [Test]
        public void HitAndRun_AttackedTargetDoesNotReact()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 4, 4, 9, tags: "passive.20261119b.008"), U("guard", SandboxTeam.Enemy, 5, 4, 5, tags: Opportunist));
            SandboxUnitState hero = b.GetUnit("hero");
            hero.ActionPoints = 1;
            Assert.IsTrue(b.TryAttack("hero", "guard", out _));
            if (b.HasPendingRetaliation) b.TryResolvePendingRetaliation(out _);
            int hp = hero.HitPoints;
            hero.ActionPoints = 1;
            hero.RemainingMovement = 3;
            Assert.IsTrue(b.TryMove("hero", new HexCoord(2, 4), out string m), m);
            Assert.AreEqual(hp, hero.HitPoints, "атакованный этим ходом не бьёт за выход");
        }

        [Test]
        public void LegStrike_ZeroesMovement_KeepsActionFromPlace()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 4, 4, 9), U("guard", SandboxTeam.Enemy, 5, 4, 5, tags: "passive.20261119.103"));
            SandboxUnitState hero = b.GetUnit("hero");
            Assert.IsTrue(b.TryMove("hero", new HexCoord(1, 4), out string m), m);
            Assert.AreNotEqual(new HexCoord(1, 4), hero.Position, "движение прервано реакцией");
            Assert.AreEqual(0, hero.RemainingMovement);
            Assert.AreEqual("hero", b.CurrentUnit.Id, "действие с места осталось");
            Assert.IsTrue(hero.ActsWithoutMovement);
        }

        [Test]
        public void Pursuer_StepsAfterRetreatingEnemy()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 4, 4, 9), U("hound", SandboxTeam.Enemy, 5, 4, 5, tags: "tactic.pursuer"));
            Assert.IsTrue(b.TryMove("hero", new HexCoord(2, 4), out _));
            Assert.AreEqual(2, b.GetUnit("hound").Position.DistanceTo(b.GetUnit("hero").Position), "один шаг следом (было 3)");
            Assert.AreEqual(20, b.GetUnit("hero").HitPoints, "бесплатно не бьёт");
        }

        [Test]
        public void Avenger_StrikesEnemyThatHitAdjacentAlly()
        {
            SandboxBattle b = Build(U("foe", SandboxTeam.Enemy, 4, 4, 9), U("ally", SandboxTeam.Player, 5, 4, 5), U("avenger", SandboxTeam.Player, 4, 5, 4, tags: "passive.20261119b.010"));
            Assert.IsTrue(b.TryAttack("foe", "ally", out _));
            Assert.Less(b.GetUnit("foe").HitPoints, 20, "мститель ответил");
        }

        [Test]
        public void TwoShots_ShieldTakesFirst_SecondDamages()
        {
            SandboxBattle b = Build(U("archer", SandboxTeam.Enemy, 2, 4, 9, range: 3, tags: "passive.20261119.226"), U("hero", SandboxTeam.Player, 4, 4, 5));
            SandboxUnitState hero = b.GetUnit("hero");
            b.ApplyStatus("status.132", hero, hero);
            Assert.IsTrue(b.TryAttack("archer", "hero", out string m), m);
            Assert.IsFalse(hero.HasStatus("status.132"), "первый снаряд потратил щит");
            Assert.Less(hero.HitPoints, 20, "второй видит новое состояние: " + m);
            Assert.AreEqual(2, b.CombatEvents.Count(e => e.Kind == SandboxCombatEventKind.HitResolved && e.TargetUnitId == "hero"));
        }

        [Test]
        public void TwoShots_OncePerTurnPoison_NotDoubled()
        {
            SandboxBattle b = Build(U("archer", SandboxTeam.Enemy, 2, 4, 9, range: 3, tags: new[] { "passive.20261119.226", "passive.20261119.052" }), U("hero", SandboxTeam.Player, 4, 4, 5));
            b.TryAttack("archer", "hero", out _);
            Assert.AreEqual(1, b.CombatEvents.Count(e => e.Kind == SandboxCombatEventKind.StatusApplied && e.StatusId == SandboxStatusCatalog.Poison));
        }

        [Test]
        public void TwoMeleeHits_SecondAfterRetaliation_HalfAttack_CancelledIfAttackerDies()
        {
            SandboxBattle b = Build(U("brute", SandboxTeam.Enemy, 4, 4, 9, tags: "passive.20261119.227"), U("hero", SandboxTeam.Player, 5, 4, 5));
            Assert.IsTrue(b.TryAttack("brute", "hero", out _));
            Assert.IsTrue(b.HasPendingRetaliation);
            Assert.AreEqual(17, b.GetUnit("hero").HitPoints, "второе — после ответа");
            Assert.IsTrue(b.TryResolvePendingRetaliation(out string m), m);
            Assert.AreEqual(15, b.GetUnit("hero").HitPoints, "половина Атаки: 1 против 2 → ×0,875 → 2: " + m);

            SandboxBattle d = Build(U("brute", SandboxTeam.Enemy, 4, 4, 9, hp: 3, tags: "passive.20261119.227"), U("hero", SandboxTeam.Player, 5, 4, 5));
            d.TryAttack("brute", "hero", out _);
            d.TryResolvePendingRetaliation(out _);
            Assert.IsTrue(d.GetUnit("brute").IsDefeated);
            Assert.AreEqual(17, d.GetUnit("hero").HitPoints, "погибший атакующий второго удара не наносит");
        }

        [Test]
        public void ReactionDoesNotTriggerRetaliationOnReaction()
        {
            SandboxBattle b = Build(U("hero", SandboxTeam.Player, 4, 4, 9), U("guard", SandboxTeam.Enemy, 5, 4, 5, tags: Opportunist));
            b.TryMove("hero", new HexCoord(2, 4), out _);
            Assert.IsFalse(b.HasPendingRetaliation);
            Assert.AreEqual(20, b.GetUnit("guard").HitPoints);
        }
    }
}
