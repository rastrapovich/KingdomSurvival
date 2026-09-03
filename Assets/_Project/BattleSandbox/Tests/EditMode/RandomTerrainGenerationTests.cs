using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    public sealed class RandomTerrainGenerationTests
    {
        [Test]
        public void DefaultBattle_GeneratesExpectedTerrainCountsAndKeepsSpawnsClear()
        {
            for (int seed = 0; seed < 12; seed++)
            {
                SandboxBattle battle = CreateBattle(seed);
                int difficult = 0;
                int impassable = 0;

                for (int r = 0; r < SandboxArenaShape.Height; r++)
                {
                    for (int q = 0; q < SandboxArenaShape.Width; q++)
                    {
                        HexCoord coord = new HexCoord(q, r);
                        if (!SandboxArenaShape.Contains(coord))
                            continue;

                        SandboxTerrain terrain = battle.GetTerrain(coord);
                        if (terrain == SandboxTerrain.Difficult)
                            difficult++;
                        else if (terrain == SandboxTerrain.Impassable)
                            impassable++;
                    }
                }

                Assert.That(difficult, Is.InRange(6, 10), "Seed " + seed);
                Assert.That(impassable, Is.InRange(3, 5), "Seed " + seed);
                Assert.That(
                    battle.Units.All(unit => battle.GetTerrain(unit.Position) == SandboxTerrain.Normal),
                    Is.True,
                    "Seed " + seed);
            }
        }

        [Test]
        public void DefaultBattle_AlwaysKeepsAllStartingUnitsConnected()
        {
            for (int seed = 0; seed < 24; seed++)
            {
                SandboxBattle battle = CreateBattle(seed);
                SandboxUnitState first = battle.Units[0];
                HashSet<HexCoord> visited = new HashSet<HexCoord> { first.Position };
                Queue<HexCoord> frontier = new Queue<HexCoord>();
                frontier.Enqueue(first.Position);

                while (frontier.Count > 0)
                {
                    HexCoord current = frontier.Dequeue();
                    foreach (HexCoord next in current.Neighbors())
                    {
                        if (!SandboxArenaShape.Contains(next) || visited.Contains(next))
                            continue;
                        if (battle.GetTerrain(next) == SandboxTerrain.Impassable)
                            continue;

                        visited.Add(next);
                        frontier.Enqueue(next);
                    }
                }

                Assert.That(
                    battle.Units.All(unit => visited.Contains(unit.Position)),
                    Is.True,
                    "Seed " + seed);
            }
        }

        private static SandboxBattle CreateBattle(int seed)
        {
            return SandboxRoster.CreateDefaultBattle(
                new[] { "guard", "archer", "spearman", "scout" },
                SandboxRoster.PlayerRoster,
                SandboxRoster.EnemyRoster,
                seed);
        }
    }
}
