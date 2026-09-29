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

        // ПР-12Ж: жетон существа без миниатюры — буквы названия и размер.
        public string TokenText { get; }
        public float TokenScale { get; }

        public SandboxUnitVisual(
            Sprite portrait,
            Sprite battlefieldSprite,
            float battlefieldScale,
            Vector2 battlefieldOffset,
            string tokenText = null,
            float tokenScale = 1f)
        {
            Portrait = portrait;
            BattlefieldSprite = battlefieldSprite;
            BattlefieldScale = Mathf.Max(0.1f, battlefieldScale);
            BattlefieldOffset = battlefieldOffset;
            TokenText = tokenText ?? string.Empty;
            TokenScale = Mathf.Clamp(tokenScale, 0.5f, 1.5f);
        }

        // «Кровяной клещень» → «КК», «Волк» → «ВО».
        public static string MakeTokenText(string displayLabel)
        {
            if (string.IsNullOrWhiteSpace(displayLabel))
                return string.Empty;
            string[] words = displayLabel.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            string text = words.Length >= 2
                ? words[0].Substring(0, 1) + words[1].Substring(0, 1)
                : words[0].Substring(0, Math.Min(2, words[0].Length));
            return text.ToUpperInvariant();
        }

        public static float TokenScaleFor(UnitSize size)
        {
            switch (size)
            {
                case UnitSize.Small: return 0.8f;
                case UnitSize.Large: return 1.2f;
                default: return 1f;
            }
        }
    }

    // ПР-12Ж: готовый состав противников тестового боя из Базы существ.
    internal sealed class SandboxEncounterChoice
    {
        public string Id { get; }
        public string Title { get; }
        public string Purpose { get; }
        public IReadOnlyList<SandboxUnitDefinition> Enemies { get; }

        public SandboxEncounterChoice(string id, string title, string purpose, IReadOnlyList<SandboxUnitDefinition> enemies)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Purpose = purpose ?? string.Empty;
            Enemies = enemies ?? new List<SandboxUnitDefinition>();
        }
    }

    internal sealed class SandboxUnitContent
    {
        // ПР-12Ж: все существа базы по порядку (свой состав) и готовые составы.
        public List<SandboxUnitDefinition> Creatures { get; } = new List<SandboxUnitDefinition>();
        public List<SandboxEncounterChoice> Presets { get; } = new List<SandboxEncounterChoice>();

        private readonly Dictionary<string, SandboxUnitVisual> visuals;

        public IReadOnlyList<SandboxUnitDefinition> PlayerRoster { get; }
        public IReadOnlyList<SandboxUnitDefinition> EnemyEncounter { get; }
        public bool UsesDatabaseAsset { get; }

        // ПР-10: существа по ID — враги из запроса боя кампании.
        public Dictionary<string, SandboxUnitDefinition> CreaturesById { get; } =
            new Dictionary<string, SandboxUnitDefinition>(StringComparer.Ordinal);

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
            foreach (SandboxUnitDefinition enemy in enemyEncounter)
            {
                if (enemy != null && !CreaturesById.ContainsKey(enemy.Id))
                    CreaturesById[enemy.Id] = enemy;
            }
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
            Dictionary<string, SandboxUnitDefinition> creatures =
                new Dictionary<string, SandboxUnitDefinition>(StringComparer.Ordinal);

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
                    source.BattlefieldOffset,
                    source.Category == UnitCategory.Creature
                        ? SandboxUnitVisual.MakeTokenText(source.DisplayLabel)
                        : string.Empty,
                    SandboxUnitVisual.TokenScaleFor(source.Size));

                if (source.Category == UnitCategory.Fighter)
                {
                    fighters.Add(definition);
                    continue;
                }

                if (source.Category != UnitCategory.Creature)
                    continue;
                creatures[source.Id] = definition;

                for (int count = 0; count < source.SandboxEncounterCount; count++)
                    enemies.Add(definition);
            }

            if (fighters.Count == 0 || enemies.Count == 0)
                return CreateFallback();

            SandboxUnitContent content = new SandboxUnitContent(fighters, enemies, visuals, true);
            foreach (KeyValuePair<string, SandboxUnitDefinition> creature in creatures)
                content.CreaturesById[creature.Key] = creature.Value;
            foreach (UnitDefinitionData source in database.Units)
            {
                if (source != null && source.Category == UnitCategory.Creature &&
                    creatures.TryGetValue(source.Id, out SandboxUnitDefinition creature) &&
                    !content.Creatures.Contains(creature))
                {
                    content.Creatures.Add(creature);
                }
            }
            foreach (UnitEncounterPreset preset in database.EncounterPresets)
            {
                List<SandboxUnitDefinition> members = ExpandPreset(preset, creatures);
                if (members.Count > 0)
                    content.Presets.Add(new SandboxEncounterChoice(preset.Id, preset.Title, preset.Purpose, members));
            }
            return content;
        }

        // Состав с неизвестным существом или больше MaxEnemies не собирается.
        internal static List<SandboxUnitDefinition> ExpandPreset(
            UnitEncounterPreset preset,
            IReadOnlyDictionary<string, SandboxUnitDefinition> creatures)
        {
            List<SandboxUnitDefinition> members = new List<SandboxUnitDefinition>();
            if (preset == null || creatures == null)
                return members;
            foreach (UnitEncounterSlot slot in preset.Slots)
            {
                if (slot == null || !creatures.TryGetValue(slot.UnitId, out SandboxUnitDefinition creature))
                    return new List<SandboxUnitDefinition>();
                for (int i = 0; i < slot.Count; i++)
                    members.Add(creature);
            }
            return members.Count <= SandboxRoster.MaxEnemies ? members : new List<SandboxUnitDefinition>();
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
