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

        // ПР-12Ж: столько противников помещается в один бой
        // (правая колонка полигона, противники кампании).
        public const int MaxEnemies = 8;

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
            return CreateBattle(fighters, enemyEncounter, terrainSeed);
        }

        // ПР-03: бой из готового упорядоченного состава — у кампании это
        // конкретные люди (герой и бойцы похода), в том числе несколько
        // бойцов одного типа. Юнит i получает ID "player:<тип>:<i+1>".
        // 12Е-6: прибавка к инициативе отряда в первом раунде («Засада»)
        // ставится до Start() — иначе очередь первого раунда уже построена.
        public static SandboxBattle CreateBattle(
            IReadOnlyList<SandboxUnitDefinition> fighters,
            IEnumerable<SandboxUnitDefinition> enemyEncounter,
            int? terrainSeed = null,
            int playerFirstRoundInitiativeBonus = 0,
            IEnumerable<HexCoord> disabledCells = null)
        {
            if (fighters == null)
                throw new ArgumentNullException(nameof(fighters));
            if (enemyEncounter == null)
                throw new ArgumentNullException(nameof(enemyEncounter));

            List<SandboxUnitDefinition> enemies = enemyEncounter
                .Where(definition => definition != null)
                .ToList();

            if (fighters.Count < 1 || fighters.Count > 6)
                throw new ArgumentException("Для полигона нужно выбрать от одного до шести бойцов.");
            if (enemies.Count < 1 || enemies.Count > MaxEnemies)
                throw new ArgumentException("В бою должно быть от одного до " + MaxEnemies + " противников.");

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
                new HexCoord(9, 4),
                // ПР-12Ж: места 5–8 для больших составов каталога.
                new HexCoord(8, 5),
                new HexCoord(8, 0),
                new HexCoord(8, 3),
                new HexCoord(8, 6)
            };

            // Отключённые на поле гексы (База полей боя). Если поле после этого
            // не вмещает бой, отключение не применяется.
            HashSet<HexCoord> blocked = disabledCells != null
                ? new HashSet<HexCoord>(disabledCells.Where(SandboxArenaShape.Contains))
                : new HashSet<HexCoord>();
            if (SandboxArenaShape.CellCount - blocked.Count < fighters.Count + enemies.Count + MinImpassableCells)
                blocked.Clear();

            HashSet<HexCoord> taken = new HashSet<HexCoord>();
            List<SandboxUnitState> units = new List<SandboxUnitState>();
            for (int i = 0; i < fighters.Count; i++)
            {
                units.Add(new SandboxUnitState(
                    "player:" + fighters[i].Id + ":" + (i + 1),
                    fighters[i],
                    SandboxTeam.Player,
                    PlaceSpawn(playerPositions[i], SandboxTeam.Player, blocked, taken)));
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                units.Add(new SandboxUnitState(
                    "enemy:" + enemies[i].Id + ":" + (i + 1),
                    enemies[i],
                    SandboxTeam.Enemy,
                    PlaceSpawn(enemyPositions[i], SandboxTeam.Enemy, blocked, taken)));
            }

            Random random = new Random(terrainSeed ?? Guid.NewGuid().GetHashCode());
            Dictionary<HexCoord, SandboxTerrain> terrain = GenerateTerrain(units, random, blocked);
            SandboxTerrainRules.RegisterBattle(units, terrain);

            SandboxBattle battle = new SandboxBattle(
                SandboxArenaShape.Width,
                SandboxArenaShape.Height,
                units,
                terrain,
                blocked);
            battle.PlayerFirstRoundInitiativeBonus = playerFirstRoundInitiativeBonus;
            battle.Start();
            return battle;
        }

        // Стартовый гекс: заданный, а если он отключён или занят — ближайший
        // свободный, ближе к своему краю поля.
        private static HexCoord PlaceSpawn(
            HexCoord preferred,
            SandboxTeam team,
            HashSet<HexCoord> blocked,
            HashSet<HexCoord> taken)
        {
            HexCoord chosen = preferred;
            if (blocked.Contains(preferred) || taken.Contains(preferred))
            {
                int side = team == SandboxTeam.Player ? 1 : -1;
                chosen = SandboxArenaShape.Cells()
                    .Where(cell => !blocked.Contains(cell) && !taken.Contains(cell))
                    .OrderBy(cell => cell.DistanceTo(preferred))
                    .ThenBy(cell => cell.Q * side)
                    .ThenBy(cell => cell.R)
                    .First();
            }

            taken.Add(chosen);
            return chosen;
        }

        private static Dictionary<HexCoord, SandboxTerrain> GenerateTerrain(
            IReadOnlyCollection<SandboxUnitState> units,
            Random random,
            HashSet<HexCoord> blocked)
        {
            HashSet<HexCoord> occupied = new HashSet<HexCoord>(units.Select(unit => unit.Position));
            List<HexCoord> candidates = new List<HexCoord>();
            for (int r = 0; r < SandboxArenaShape.Height; r++)
            {
                for (int q = 0; q < SandboxArenaShape.Width; q++)
                {
                    HexCoord coord = new HexCoord(q, r);
                    if (SandboxArenaShape.Contains(coord) && !occupied.Contains(coord) && !blocked.Contains(coord))
                        candidates.Add(coord);
                }
            }

            for (int attempt = 0; attempt < TerrainGenerationAttempts; attempt++)
            {
                Dictionary<HexCoord, SandboxTerrain> terrain = CreateBaseTerrain(blocked);
                Shuffle(candidates, random);

                int impassableCount = Math.Min(
                    random.Next(MinImpassableCells, MaxImpassableCells + 1), candidates.Count);
                int difficultCount = Math.Min(
                    random.Next(MinDifficultCells, MaxDifficultCells + 1), candidates.Count - impassableCount);

                for (int i = 0; i < impassableCount; i++)
                    terrain[candidates[i]] = SandboxTerrain.Impassable;
                for (int i = impassableCount; i < impassableCount + difficultCount; i++)
                    terrain[candidates[i]] = SandboxTerrain.Difficult;

                if (AllUnitSpawnsConnected(units, terrain))
                    return terrain;
            }

            return CreateSafeFallbackTerrain(candidates, units, random, blocked);
        }

        private static Dictionary<HexCoord, SandboxTerrain> CreateBaseTerrain(HashSet<HexCoord> blocked)
        {
            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>();
            foreach (HexCoord inactive in SandboxArenaShape.InactiveCells())
                terrain[inactive] = SandboxTerrain.Impassable;
            foreach (HexCoord disabled in blocked)
                terrain[disabled] = SandboxTerrain.Impassable;
            return terrain;
        }

        private static Dictionary<HexCoord, SandboxTerrain> CreateSafeFallbackTerrain(
            List<HexCoord> candidates,
            IReadOnlyCollection<SandboxUnitState> units,
            Random random,
            HashSet<HexCoord> blocked)
        {
            int impassableCount = Math.Min(MinImpassableCells, candidates.Count);
            int difficultCount = Math.Min(MinDifficultCells, candidates.Count - impassableCount);
            for (int attempt = 0; attempt < TerrainGenerationAttempts; attempt++)
            {
                Dictionary<HexCoord, SandboxTerrain> terrain = CreateBaseTerrain(blocked);
                Shuffle(candidates, random);

                for (int i = 0; i < impassableCount; i++)
                    terrain[candidates[i]] = SandboxTerrain.Impassable;
                for (int i = impassableCount; i < impassableCount + difficultCount; i++)
                    terrain[candidates[i]] = SandboxTerrain.Difficult;

                if (AllUnitSpawnsConnected(units, terrain))
                    return terrain;
            }

            // Отключённые гексы могли разорвать поле: тогда без случайных
            // преград, только трудная местность.
            Dictionary<HexCoord, SandboxTerrain> fallback = CreateBaseTerrain(blocked);
            for (int i = 0; i < Math.Min(MinDifficultCells, candidates.Count); i++)
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
