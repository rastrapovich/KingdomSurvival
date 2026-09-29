using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // ПР-12Ж: составы каталога существ — до 8 противников в одном бою.
    public sealed class SandboxRosterCapacityTests
    {
        private static SandboxUnitDefinition Sheshka()
        {
            return new SandboxUnitDefinition("sheshka", "Шешка", SandboxUnitRole.Beast, 8, 2, 0, 2, 5, 8, 1, new string[0]);
        }

        private static IReadOnlyList<SandboxUnitDefinition> Fighters(int count)
        {
            return SandboxRoster.PlayerRoster.Take(count).ToList();
        }

        [Test]
        public void EightEnemies_FitOnArena_SpawnsDistinctAndConnected()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                SandboxBattle battle = SandboxRoster.CreateBattle(
                    Fighters(6),
                    Enumerable.Repeat(Sheshka(), SandboxRoster.MaxEnemies),
                    seed);
                List<SandboxUnitState> enemies = battle.Units.Where(unit => unit.Team == SandboxTeam.Enemy).ToList();
                Assert.AreEqual(8, enemies.Count);
                Assert.AreEqual(battle.Units.Count, battle.Units.Select(unit => unit.Position).Distinct().Count(), "seed " + seed);
                Assert.IsTrue(battle.Units.All(unit => SandboxArenaShape.Contains(unit.Position)), "seed " + seed);
                Assert.IsTrue(battle.Units.All(unit => battle.GetTerrain(unit.Position) == SandboxTerrain.Normal), "seed " + seed);

                // Каждый противник может дойти до отряда: спавны связаны.
                SandboxUnitState player = battle.Units.First(unit => unit.Team == SandboxTeam.Player);
                HashSet<HexCoord> reached = Flood(battle, player.Position);
                Assert.IsTrue(enemies.All(enemy => reached.Contains(enemy.Position)), "seed " + seed);
            }
        }

        [Test]
        public void NineEnemies_Rejected()
        {
            Assert.Throws<ArgumentException>(() => SandboxRoster.CreateBattle(
                Fighters(1),
                Enumerable.Repeat(Sheshka(), SandboxRoster.MaxEnemies + 1),
                1));
        }

        [Test]
        public void SwarmBattle_PlaysToTheEnd_WithEnemyTurns()
        {
            SandboxBattle battle = SandboxRoster.CreateBattle(Fighters(3), Enumerable.Repeat(Sheshka(), 6), 7);
            for (int step = 0; step < 2000 && battle.Phase == SandboxBattlePhase.InProgress; step++)
            {
                SandboxUnitState current = battle.CurrentUnit;
                if (current.Team == SandboxTeam.Enemy)
                {
                    SandboxEnemyPlanner.TakeCurrentTurn(battle);
                    continue;
                }

                // Отряд просто стоит в стойке: проверяется, что противники
                // доходят и бой заканчивается.
                if (!battle.TryGuard(current.Id, out _) && battle.CurrentUnit == current)
                    battle.EndActivation();
                else if (battle.CurrentUnit == current)
                    battle.EndActivation();
            }
            Assert.AreNotEqual(SandboxBattlePhase.InProgress, battle.Phase, "Бой с шестью противниками доигрывается.");
        }

        private static HashSet<HexCoord> Flood(SandboxBattle battle, HexCoord start)
        {
            HashSet<HexCoord> seen = new HashSet<HexCoord> { start };
            Queue<HexCoord> queue = new Queue<HexCoord>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                HexCoord cell = queue.Dequeue();
                foreach (HexCoord next in cell.Neighbors())
                {
                    if (!SandboxArenaShape.Contains(next) || battle.GetTerrain(next) == SandboxTerrain.Impassable || !seen.Add(next))
                        continue;
                    queue.Enqueue(next);
                }
            }
            return seen;
        }
    }
}
