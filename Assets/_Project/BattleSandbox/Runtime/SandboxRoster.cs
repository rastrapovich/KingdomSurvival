using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    public static class SandboxRoster
    {
        private static readonly SandboxUnitDefinition[] PlayerRosterData =
        {
            new SandboxUnitDefinition("guard", "Гвардеец", SandboxUnitRole.Guard, 18, 2, 4, 3, 3, 3, 1,
                new[] { "species.human", "combat.melee", "role.defender", "trait.armored" }),
            new SandboxUnitDefinition("archer", "Лучник", SandboxUnitRole.Archer, 11, 3, 1, 3, 3, 5, 4,
                new[] { "species.human", "combat.ranged" }),
            new SandboxUnitDefinition("healer", "Лекарь", SandboxUnitRole.Healer, 12, 1, 2, 3, 3, 3, 1,
                new[] { "species.human", "combat.melee", "role.support" }),
            new SandboxUnitDefinition("spearman", "Копейщик", SandboxUnitRole.Spearman, 15, 3, 3, 3, 3, 4, 1,
                new[] { "species.human", "combat.melee" }),
            new SandboxUnitDefinition("scout", "Разведчик", SandboxUnitRole.Scout, 12, 2, 2, 3, 4, 6, 1,
                new[] { "species.human", "combat.melee", "role.scout" }),
            new SandboxUnitDefinition("militia", "Ополченец", SandboxUnitRole.Militia, 14, 2, 2, 3, 3, 2, 1,
                new[] { "species.human", "combat.melee" })
        };

        private static readonly SandboxUnitDefinition[] EnemyRosterData =
        {
            new SandboxUnitDefinition("forest_beast_1", "Зверь", SandboxUnitRole.Beast, 10, 2, 1, 3, 4, 5, 1,
                new[] { SandboxUnitTags.Beast, "combat.melee" }),
            new SandboxUnitDefinition("forest_beast_2", "Зверь", SandboxUnitRole.Beast, 10, 2, 1, 3, 4, 5, 1,
                new[] { SandboxUnitTags.Beast, "combat.melee" }),
            new SandboxUnitDefinition("forest_beast_3", "Зверь", SandboxUnitRole.Beast, 14, 3, 2, 4, 4, 4, 1,
                new[] { SandboxUnitTags.Beast, "combat.melee" }),
            new SandboxUnitDefinition("forest_beast_4", "Зверь", SandboxUnitRole.Beast, 18, 4, 3, 5, 3, 2, 1,
                new[] { SandboxUnitTags.Beast, "combat.melee" })
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
            IEnumerable<SandboxUnitDefinition> enemyEncounter)
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

            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>();
            foreach (HexCoord inactive in SandboxArenaShape.InactiveCells())
                terrain[inactive] = SandboxTerrain.Impassable;

            terrain[new HexCoord(5, 1)] = SandboxTerrain.Impassable;
            terrain[new HexCoord(5, 2)] = SandboxTerrain.Impassable;
            terrain[new HexCoord(5, 4)] = SandboxTerrain.Impassable;
            terrain[new HexCoord(5, 5)] = SandboxTerrain.Impassable;
            terrain[new HexCoord(4, 3)] = SandboxTerrain.Difficult;
            terrain[new HexCoord(5, 3)] = SandboxTerrain.Difficult;
            terrain[new HexCoord(6, 3)] = SandboxTerrain.Difficult;

            SandboxBattle battle = new SandboxBattle(
                SandboxArenaShape.Width,
                SandboxArenaShape.Height,
                units,
                terrain);
            battle.Start();
            return battle;
        }
    }
}
