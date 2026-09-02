using System;
using System.Collections.Generic;
using KingdomSurvival.UnitDatabase;
using UnityEngine;

namespace KingdomSurvival.BattleSandbox
{
    internal sealed class SandboxUnitVisual
    {
        public static readonly SandboxUnitVisual Empty = new SandboxUnitVisual(
            null,
            null,
            1f,
            Vector2.zero);

        public Sprite Portrait { get; }
        public Sprite BattlefieldSprite { get; }
        public float BattlefieldScale { get; }
        public Vector2 BattlefieldOffset { get; }

        public SandboxUnitVisual(
            Sprite portrait,
            Sprite battlefieldSprite,
            float battlefieldScale,
            Vector2 battlefieldOffset)
        {
            Portrait = portrait;
            BattlefieldSprite = battlefieldSprite;
            BattlefieldScale = Mathf.Max(0.1f, battlefieldScale);
            BattlefieldOffset = battlefieldOffset;
        }
    }

    internal sealed class SandboxUnitContent
    {
        private readonly Dictionary<string, SandboxUnitVisual> visuals;

        public IReadOnlyList<SandboxUnitDefinition> PlayerRoster { get; }
        public IReadOnlyList<SandboxUnitDefinition> EnemyEncounter { get; }
        public bool UsesDatabaseAsset { get; }

        public SandboxUnitContent(
            IReadOnlyList<SandboxUnitDefinition> playerRoster,
            IReadOnlyList<SandboxUnitDefinition> enemyEncounter,
            Dictionary<string, SandboxUnitVisual> visuals,
            bool usesDatabaseAsset)
        {
            PlayerRoster = playerRoster ?? throw new ArgumentNullException(nameof(playerRoster));
            EnemyEncounter = enemyEncounter ?? throw new ArgumentNullException(nameof(enemyEncounter));
            this.visuals = visuals ?? new Dictionary<string, SandboxUnitVisual>();
            UsesDatabaseAsset = usesDatabaseAsset;
        }

        public SandboxUnitVisual GetVisual(string typeId)
        {
            SandboxUnitVisual visual;
            return !string.IsNullOrWhiteSpace(typeId) && visuals.TryGetValue(typeId, out visual)
                ? visual
                : SandboxUnitVisual.Empty;
        }

        public IReadOnlyDictionary<string, SandboxUnitVisual> Visuals => visuals;
    }

    internal static class SandboxUnitDatabaseAdapter
    {
        public static SandboxUnitContent Load()
        {
            UnitDatabaseAsset database = Resources.Load<UnitDatabaseAsset>(
                UnitDatabaseAsset.ResourcesPath);
            if (database == null)
                return CreateFallback();

            List<SandboxUnitDefinition> fighters = new List<SandboxUnitDefinition>();
            List<SandboxUnitDefinition> enemies = new List<SandboxUnitDefinition>();
            Dictionary<string, SandboxUnitVisual> visuals =
                new Dictionary<string, SandboxUnitVisual>(StringComparer.Ordinal);
            HashSet<string> acceptedIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < database.Units.Count; i++)
            {
                UnitDefinitionData source = database.Units[i];
                if (source == null || string.IsNullOrWhiteSpace(source.Id) ||
                    string.IsNullOrWhiteSpace(source.DisplayLabel) ||
                    !acceptedIds.Add(source.Id))
                {
                    continue;
                }

                SandboxUnitDefinition definition = new SandboxUnitDefinition(
                    source.Id,
                    source.DisplayLabel,
                    ConvertRole(source.CombatRole),
                    source.MaxHitPoints,
                    source.Attack,
                    source.Defense,
                    source.Damage,
                    source.Movement,
                    source.Initiative,
                    source.AttackRange,
                    source.TagIds);

                visuals[source.Id] = new SandboxUnitVisual(
                    source.Portrait,
                    source.BattlefieldSprite,
                    source.BattlefieldScale,
                    source.BattlefieldOffset);

                if (source.Category == UnitCategory.Fighter)
                {
                    fighters.Add(definition);
                    continue;
                }

                if (source.Category != UnitCategory.Creature)
                    continue;

                for (int count = 0; count < source.SandboxEncounterCount; count++)
                    enemies.Add(definition);
            }

            if (fighters.Count == 0 || enemies.Count == 0)
                return CreateFallback();

            return new SandboxUnitContent(fighters, enemies, visuals, true);
        }

        private static SandboxUnitContent CreateFallback()
        {
            return new SandboxUnitContent(
                SandboxRoster.PlayerRoster,
                SandboxRoster.EnemyRoster,
                new Dictionary<string, SandboxUnitVisual>(),
                false);
        }

        private static SandboxUnitRole ConvertRole(UnitCombatRole role)
        {
            switch (role)
            {
                case UnitCombatRole.Guard: return SandboxUnitRole.Guard;
                case UnitCombatRole.Archer: return SandboxUnitRole.Archer;
                case UnitCombatRole.Healer: return SandboxUnitRole.Healer;
                case UnitCombatRole.Spearman: return SandboxUnitRole.Spearman;
                case UnitCombatRole.Scout: return SandboxUnitRole.Scout;
                case UnitCombatRole.Militia: return SandboxUnitRole.Militia;
                default: return SandboxUnitRole.Beast;
            }
        }
    }
}
