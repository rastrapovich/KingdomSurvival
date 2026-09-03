using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    public static class SandboxRoster
    {
        private const int MinDifficultCells = 6;
        private const int MaxDifficultCells = 10;
        private const int MinImpassableCells = 3;
        private const int MaxImpassableCells = 5;
        private const int TerrainGenerationAttempts = 64;

        private static readonly SandboxUnitDefinition[] PlayerRosterData =
        {
            new SandboxUnitDefinition("guard", "Гвардеец", SandboxUnitRole.Guard, 18, 2, 4, 3, 3, 3, 1,
                new[] { SandboxCombatTagRules.Human, SandboxCombatTagRules.Defender, SandboxCombatTagRules.Armored }),
            new SandboxUnitDefinition("archer", "Лучник", SandboxUnitRole.Archer, 11, 3, 1, 3, 3, 5, 4,
                new[] { SandboxCombatTagRules.Human, SandboxCombatTagRules.Ranged }),
            new SandboxUnitDefinition("healer", "Лекарь", SandboxUnitRole.Healer, 12, 1, 2, 3, 3, 3, 1,
                new[] { SandboxCombatTagRules.Human }),
            new SandboxUnitDefinition("spearman", "Копейщик", SandboxUnitRole.Spearman, 15, 3, 3, 3, 3, 4, 1,
                new[] { SandboxCombatTagRules.Human, SandboxCombatTagRules.BeastSlayer }),
            new SandboxUnitDefinition("scout", "Разведчик", SandboxUnitRole.Scout, 12, 2, 2, 3, 4, 6, 1,
                new[] { SandboxCombatTagRules.Human }),
            new SandboxUnitDefinition("militia", "Ополченец", SandboxUnitRole.Militia, 14, 2, 2, 3, 3, 2, 1,
                new[] { SandboxCombatTagRules.Human })
        };

        private static readonly SandboxUnitDefinition[] EnemyRosterData =
        {
            new SandboxUnitDefinition("forest_beast_1", "Зверь", SandboxUnitRole.Beast, 10, 2, 1, 3, 4, 5, 1,
                new[] { SandboxCombatTagRules.Beast }),
            new SandboxUnitDefinition("forest_beast_2", "Зверь", SandboxUnitRole.Beast, 10, 2, 1, 3, 4, 5, 1,
                new[] { SandboxCombatTagRules.Beast }),
            new SandboxUnitDefinition("forest_beast_3", "Зверь", SandboxUnitRole.Beast, 14, 3, 2, 4, 4, 4, 1,
                new[] { SandboxCombatTagRules.Beast }),
            new SandboxUnitDefinition("forest_beast_4", "Зверь", SandboxUnitRole.Beast, 18, 4, 3, 5, 3, 2, 1,
                new[] { SandboxCombatTagRules.Beast, SandboxCombatTagRules.HumanSlayer })
        };

        public static IReadOnlyList<SandboxUnitDefinition> PlayerRoster => PlayerRosterData;
        public static IReadOnlyList<SandboxUnitDefinition> EnemyRoster => EnemyRosterData;

        public static SandboxBattle CreateDefaultBattle(IEnumerable<string> selectedFighterTypeIds)
        {
            return CreateDefaultBattle(
                selectedFighterTypeIds,
                PlayerRosterData,
                EnemyRosterData);
        }

        public static SandboxBattle CreateDefaultBattle(
            IEnumerable<string> selectedFighterTypeIds,
            IEnumerable<SandboxUnitDefinition> playerRoster,
            IEnumerable<SandboxUnitDefinition> enemyEncounter,
            int? terrainSeed = null)
        {
            if (selectedFighterTypeIds == null)
                throw new ArgumentNullException(nameof(selectedFighterTypeIds));
            if (playerRoster == null)
                throw new ArgumentNullException(nameof(playerRoster));
            if (enemyEncounter == null)
                throw new ArgumentNullException(nameof(enemyEncounter));

            HashSet<string> selected = new HashSet<string>(selectedFighterTypeIds);
            List<SandboxUnitDefinition> fighters = playerRoster
                .Where(definition => definition != null && selected.Contains(definition.Id))
                .ToList();
            List<SandboxUnitDefinition> enemies = enemyEncounter
                .Where(definition => definition != null)
                .ToList();

            if (fighters.Count < 1 || fighters.Count > 6)
                throw new ArgumentException("Для полигона нужно выбрать от одного до шести бойцов.");
            if (enemies.Count < 1 || enemies.Count > 4)
                throw new ArgumentException("Тестовая засада должна содержать от одного до четырёх существ.");

            HexCoord[] playerPositions =
            {
                new HexCoord(2, 0),
                new HexCoord(1, 1),
                new HexCoord(1, 2),
                new HexCoord(0, 3),
                new HexCoord(1, 4),
                new HexCoord(1, 5)
            };

            HexCoord[] enemyPositions =
            {
                new HexCoord(8, 1),
                new HexCoord(9, 2),
                new HexCoord(9, 3),
                new HexCoord(9, 4)
            };

            List<SandboxUnitState> units = new List<SandboxUnitState>();
            for (int i = 0; i < fighters.Count; i++)
            {
                units.Add(new SandboxUnitState(
                    "player:" + fighters[i].Id + ":" + (i + 1),
                    fighters[i],
                    SandboxTeam.Player,
                    playerPositions[i]));
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                units.Add(new SandboxUnitState(
                    "enemy:" + enemies[i].Id + ":" + (i + 1),
                    enemies[i],
                    SandboxTeam.Enemy,
                    enemyPositions[i]));
            }

            Random random = new Random(terrainSeed ?? Guid.NewGuid().GetHashCode());
            Dictionary<HexCoord, SandboxTerrain> terrain = GenerateTerrain(units, random);
            SandboxTerrainRules.RegisterBattle(units, terrain);

            SandboxBattle battle = new SandboxBattle(
                SandboxArenaShape.Width,
                SandboxArenaShape.Height,
                units,
                terrain);
            battle.Start();
            return battle;
        }

        private static Dictionary<HexCoord, SandboxTerrain> GenerateTerrain(
            IReadOnlyCollection<SandboxUnitState> units,
            Random random)
        {
            HashSet<HexCoord> occupied = new HashSet<HexCoord>(units.Select(unit => unit.Position));
            List<HexCoord> candidates = new List<HexCoord>();
            for (int r = 0; r < SandboxArenaShape.Height; r++)
            {
                for (int q = 0; q < SandboxArenaShape.Width; q++)
                {
                    HexCoord coord = new HexCoord(q, r);
                    if (SandboxArenaShape.Contains(coord) && !occupied.Contains(coord))
                        candidates.Add(coord);
                }
            }

            for (int attempt = 0; attempt < TerrainGenerationAttempts; attempt++)
            {
                Dictionary<HexCoord, SandboxTerrain> terrain = CreateBaseTerrain();
                Shuffle(candidates, random);

                int impassableCount = random.Next(MinImpassableCells, MaxImpassableCells + 1);
                int difficultCount = random.Next(MinDifficultCells, MaxDifficultCells + 1);

                for (int i = 0; i < impassableCount; i++)
                    terrain[candidates[i]] = SandboxTerrain.Impassable;
                for (int i = impassableCount; i < impassableCount + difficultCount; i++)
                    terrain[candidates[i]] = SandboxTerrain.Difficult;

                if (AllUnitSpawnsConnected(units, terrain))
                    return terrain;
            }

            return CreateSafeFallbackTerrain(candidates, units, random);
        }

        private static Dictionary<HexCoord, SandboxTerrain> CreateBaseTerrain()
        {
            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>();
            foreach (HexCoord inactive in SandboxArenaShape.InactiveCells())
                terrain[inactive] = SandboxTerrain.Impassable;
            return terrain;
        }

        private static Dictionary<HexCoord, SandboxTerrain> CreateSafeFallbackTerrain(
            List<HexCoord> candidates,
            IReadOnlyCollection<SandboxUnitState> units,
            Random random)
        {
            for (int attempt = 0; attempt < TerrainGenerationAttempts; attempt++)
            {
                Dictionary<HexCoord, SandboxTerrain> terrain = CreateBaseTerrain();
                Shuffle(candidates, random);

                for (int i = 0; i < MinImpassableCells; i++)
                    terrain[candidates[i]] = SandboxTerrain.Impassable;
                for (int i = MinImpassableCells; i < MinImpassableCells + MinDifficultCells; i++)
                    terrain[candidates[i]] = SandboxTerrain.Difficult;

                if (AllUnitSpawnsConnected(units, terrain))
                    return terrain;
            }

            Dictionary<HexCoord, SandboxTerrain> fallback = CreateBaseTerrain();
            for (int i = 0; i < MinDifficultCells; i++)
                fallback[candidates[i]] = SandboxTerrain.Difficult;
            return fallback;
        }

        private static bool AllUnitSpawnsConnected(
            IReadOnlyCollection<SandboxUnitState> units,
            IReadOnlyDictionary<HexCoord, SandboxTerrain> terrain)
        {
            SandboxUnitState first = units.FirstOrDefault();
            if (first == null)
                return false;

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
                    if (terrain.TryGetValue(next, out SandboxTerrain value) &&
                        value == SandboxTerrain.Impassable)
                    {
                        continue;
                    }

                    visited.Add(next);
                    frontier.Enqueue(next);
                }
            }

            return units.All(unit => visited.Contains(unit.Position));
        }

        private static void Shuffle(List<HexCoord> cells, Random random)
        {
            for (int i = cells.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                HexCoord temp = cells[i];
                cells[i] = cells[j];
                cells[j] = temp;
            }
        }
    }
}
