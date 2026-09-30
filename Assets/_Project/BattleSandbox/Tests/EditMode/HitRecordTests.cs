using System.Collections.Generic;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // ПР-12З: записи ударов для представления. Модель считает исход один раз;
    // записи его только описывают и не меняют урон, ОД и ответные удары.
    public sealed class HitRecordTests
    {
        [Test]
        public void MeleeAttack_ThenRetaliation_AreRecordedInOrder()
        {
            SandboxBattle battle = Duel(new HexCoord(1, 1), new HexCoord(2, 1), 1, playerHp: 40, enemyHp: 40, enemyPerks: null);
            int mark = battle.HitRecords.Count;

            Assert.IsTrue(battle.TryAttack("player", "enemy", out string message), message);
            Assert.IsTrue(battle.TryResolvePendingRetaliation(out string retaliation), retaliation);
            List<SandboxHitRecord> records = battle.GetHitRecordsSince(mark);

            Assert.AreEqual(2, records.Count);
            Assert.AreEqual(SandboxHitKind.Attack, records[0].Kind);
            Assert.AreEqual("player", records[0].StrikerId);
            Assert.AreEqual("enemy", records[0].TargetId);
            Assert.AreEqual(40, records[0].TargetHitPointsBefore);
            Assert.AreEqual(battle.GetUnit("enemy").HitPoints, records[0].TargetHitPointsAfter);
            Assert.AreEqual(records[0].TargetHitPointsBefore - records[0].Damage, records[0].TargetHitPointsAfter);
            Assert.IsFalse(records[0].IsRanged);

            Assert.AreEqual(SandboxHitKind.Retaliation, records[1].Kind);
            Assert.AreEqual("enemy", records[1].StrikerId);
            Assert.AreEqual(battle.GetUnit("player").HitPoints, records[1].TargetHitPointsAfter);
        }

        [Test]
        public void RangedKill_IsRecordedOnce_WithDefeat()
        {
            SandboxBattle battle = Duel(new HexCoord(0, 1), new HexCoord(2, 1), 3, playerHp: 40, enemyHp: 5, enemyPerks: null);

            Assert.IsTrue(battle.TryAttack("player", "enemy", out string message), message);
            List<SandboxHitRecord> records = battle.GetHitRecordsSince(0);

            Assert.AreEqual(1, records.Count, "Урон применяется ровно один раз.");
            Assert.IsTrue(records[0].IsRanged);
            Assert.IsTrue(records[0].TargetDefeated);
            Assert.IsTrue(battle.GetUnit("enemy").IsDefeated);
            Assert.AreEqual(0, battle.GetHitRecordsSince(5).Count);
        }

        [Test]
        public void FirstStrike_IsRecordedBeforeTheBlow()
        {
            SandboxBattle battle = Duel(new HexCoord(1, 1), new HexCoord(2, 1), 1, playerHp: 100, enemyHp: 100,
                enemyPerks: new[] { SandboxPerks.FirstStrike }, enemyFirst: true);
            Assert.IsTrue(battle.TryGuard("enemy", out string guard), guard);
            battle.EndActivation();

            Assert.IsTrue(battle.TryAttack("player", "enemy", out string message), message);
            List<SandboxHitRecord> records = battle.GetHitRecordsSince(0);

            Assert.AreEqual(2, records.Count);
            Assert.AreEqual(SandboxHitKind.FirstStrike, records[0].Kind);
            Assert.AreEqual("enemy", records[0].StrikerId);
            Assert.AreEqual(SandboxHitKind.Attack, records[1].Kind);
        }

        [Test]
        public void NeighborIndex_MatchesNeighborOrder_OnEvenAndOddRows()
        {
            foreach (HexCoord center in new[] { new HexCoord(3, 2), new HexCoord(3, 3) })
            {
                int index = 0;
                foreach (HexCoord neighbor in center.Neighbors())
                    Assert.AreEqual(index++, center.GetNeighborIndex(neighbor));
                Assert.AreEqual(-1, center.GetNeighborIndex(center));
                Assert.AreEqual(-1, center.GetNeighborIndex(new HexCoord(center.Q + 2, center.R)));
            }
            Assert.AreEqual(0, new HexCoord(1, 1).GetNeighborIndex(new HexCoord(2, 1)), "Вправо.");
            Assert.AreEqual(3, new HexCoord(2, 1).GetNeighborIndex(new HexCoord(1, 1)), "Влево.");
        }

        private static SandboxBattle Duel(
            HexCoord playerPosition,
            HexCoord enemyPosition,
            int playerRange,
            int playerHp,
            int enemyHp,
            string[] enemyPerks,
            bool enemyFirst = false)
        {
            SandboxUnitDefinition player = new SandboxUnitDefinition(
                "player", "player", SandboxUnitRole.Militia, playerHp, 3, 3, 10, 3, enemyFirst ? 5 : 10, playerRange);
            SandboxUnitDefinition enemy = new SandboxUnitDefinition(
                "enemy", "enemy", SandboxUnitRole.Beast, enemyHp, 3, 3, 8, 3, enemyFirst ? 10 : 5, 1, null, enemyPerks);
            SandboxBattle battle = new SandboxBattle(
                5,
                4,
                new[]
                {
                    new SandboxUnitState("player", player, SandboxTeam.Player, playerPosition),
                    new SandboxUnitState("enemy", enemy, SandboxTeam.Enemy, enemyPosition)
                });
            battle.Start();
            return battle;
        }
    }
}
