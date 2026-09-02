using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    public static class SandboxRoster
    {
        private static readonly SandboxUnitDefinition[] PlayerRosterData =
        {
            new SandboxUnitDefinition("guard", "Гвардеец", SandboxUnitRole.Guard, 100, 3, 6, 3, 5, 1),
            new SandboxUnitDefinition("archer", "Лучник", SandboxUnitRole.Archer, 90, 4, 2, 3, 6, 4),
            new SandboxUnitDefinition("healer", "Лекарь", SandboxUnitRole.Healer, 85, 1, 2, 3, 4, 1),
            new SandboxUnitDefinition("spearman", "Копейщик", SandboxUnitRole.Spearman, 100, 3, 4, 3, 5, 1),
            new SandboxUnitDefinition("scout", "Разведчик", SandboxUnitRole.Scout, 90, 2, 3, 4, 8, 1),
            new SandboxUnitDefinition("militia", "Ополченец", SandboxUnitRole.Militia, 100, 2, 3, 3, 4, 1)
        };

        private static readonly SandboxUnitDefinition[] EnemyRosterData =
        {
            new SandboxUnitDefinition("forest_beast_1", "Зверь", SandboxUnitRole.Beast, 60, 5, 2, 4, 7, 1),
            new SandboxUnitDefinition("forest_beast_2", "Зверь", SandboxUnitRole.Beast, 60, 5, 2, 4, 7, 1),
            new SandboxUnitDefinition("forest_beast_3", "Зверь", SandboxUnitRole.Beast, 90, 6, 4, 4, 6, 1),
            new SandboxUnitDefinition("forest_beast_4", "Зверь", SandboxUnitRole.Beast, 100, 7, 5, 3, 3, 1)
        };

        public static IReadOnlyList<SandboxUnitDefinition> PlayerRoster => PlayerRosterData;
        public static IReadOnlyList<SandboxUnitDefinition> EnemyRoster => EnemyRosterData;

        public static SandboxBattle CreateDefaultBattle(IEnumerable<string> selectedFighterIds)
        {
            if (selectedFighterIds == null)
                throw new ArgumentNullException(nameof(selectedFighterIds));

            HashSet<string> selected = new HashSet<string>(selectedFighterIds);
            List<SandboxUnitDefinition> fighters = PlayerRosterData
                .Where(definition => selected.Contains(definition.Id))
                .ToList();

            if (fighters.Count < 1 || fighters.Count > 6)
                throw new ArgumentException("Для полигона нужно выбрать от одного до шести бойцов.");

            HexCoord[] playerPositions =
            {
                new HexCoord(0, 1),
                new HexCoord(0, 3),
                new HexCoord(0, 5),
                new HexCoord(1, 2),
                new HexCoord(1, 4),
                new HexCoord(1, 6)
            };

            HexCoord[] enemyPositions =
            {
                new HexCoord(8, 1),
                new HexCoord(8, 3),
                new HexCoord(8, 5),
                new HexCoord(7, 2)
            };

            List<SandboxUnitState> units = new List<SandboxUnitState>();
            for (int i = 0; i < fighters.Count; i++)
                units.Add(new SandboxUnitState(fighters[i], SandboxTeam.Player, playerPositions[i]));

            for (int i = 0; i < EnemyRosterData.Length; i++)
                units.Add(new SandboxUnitState(EnemyRosterData[i], SandboxTeam.Enemy, enemyPositions[i]));

            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>
            {
                { new HexCoord(4, 1), SandboxTerrain.Impassable },
                { new HexCoord(4, 2), SandboxTerrain.Impassable },
                { new HexCoord(4, 4), SandboxTerrain.Impassable },
                { new HexCoord(4, 5), SandboxTerrain.Impassable },
                { new HexCoord(3, 3), SandboxTerrain.Difficult },
                { new HexCoord(4, 3), SandboxTerrain.Difficult },
                { new HexCoord(5, 3), SandboxTerrain.Difficult }
            };

            SandboxBattle battle = new SandboxBattle(9, 7, units, terrain);
            battle.Start();
            return battle;
        }
    }
}
