using System;
using System.Collections.Generic;
using KingdomSurvival.AnimationDatabase;
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

        // ПР-12З: набор из Базы анимаций. С ним размер и опору задаёт набор,
        // а BattlefieldScale/BattlefieldOffset не применяются (не дважды).
        public CreatureAnimationSetData AnimationSet { get; }
        public bool IsAnimated => AnimationSet != null;

        public SandboxUnitVisual(
            Sprite portrait,
            Sprite battlefieldSprite,
            float battlefieldScale,
            Vector2 battlefieldOffset,
            string tokenText = null,
            float tokenScale = 1f,
            CreatureAnimationSetData animationSet = null)
        {
            Portrait = portrait;
            BattlefieldSprite = battlefieldSprite;
            BattlefieldScale = Mathf.Max(0.1f, battlefieldScale);
            BattlefieldOffset = battlefieldOffset;
            TokenText = tokenText ?? string.Empty;
            TokenScale = Mathf.Clamp(tokenScale, 0.5f, 1.5f);
            AnimationSet = animationSet != null && animationSet.HasAnyFrames ? animationSet : null;
        }

        // Есть ли что рисовать картинкой, а не жетоном.
        public bool HasImage => IsAnimated || BattlefieldSprite != null;

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


    // Тип из Базы существ для выбора в полигоне: в любую из двух колонок.
    internal sealed class SandboxUnitOption
    {
        public SandboxUnitDefinition Definition { get; }
        public string Group { get; }

        public SandboxUnitOption(SandboxUnitDefinition definition, string group)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Group = group ?? string.Empty;
        }
    }

    internal sealed class SandboxUnitContent
    {
        private readonly Dictionary<string, SandboxUnitVisual> visuals;

        // Все типы базы по порядку: полигон ставит любого с любой стороны.
        public List<SandboxUnitOption> AllUnits { get; } = new List<SandboxUnitOption>();

        // Бойцы — основа людей кампании (UnitTypeId).
        public IReadOnlyList<SandboxUnitDefinition> PlayerRoster { get; }
        public bool UsesDatabaseAsset { get; }

        // ПР-10: существа по ID — враги из запроса боя кампании.
        public Dictionary<string, SandboxUnitDefinition> CreaturesById { get; } =
            new Dictionary<string, SandboxUnitDefinition>(StringComparer.Ordinal);

        public SandboxUnitContent(
            IReadOnlyList<SandboxUnitDefinition> playerRoster,
            Dictionary<string, SandboxUnitVisual> visuals,
            bool usesDatabaseAsset)
        {
            PlayerRoster = playerRoster ?? throw new ArgumentNullException(nameof(playerRoster));
            this.visuals = visuals ?? new Dictionary<string, SandboxUnitVisual>();
            UsesDatabaseAsset = usesDatabaseAsset;
        }

        // ПР-12З: таблица ракурсов и наборы; null — анимаций нет, всё статично.
        public CreatureAnimationDatabaseAsset AnimationDatabase { get; set; }

        public SandboxUnitDefinition FindUnit(string typeId)
        {
            foreach (SandboxUnitOption option in AllUnits)
            {
                if (string.Equals(option.Definition.Id, typeId, StringComparison.Ordinal))
                    return option.Definition;
            }
            return null;
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

            // Один и тот же набор работает во всех входах в бой: тестовом,
            // кампании, свободной игре — все идут через этот адаптер.
            CreatureAnimationDatabaseAsset animations = Resources.Load<CreatureAnimationDatabaseAsset>(
                CreatureAnimationDatabaseAsset.ResourcesPath);

            List<SandboxUnitDefinition> fighters = new List<SandboxUnitDefinition>();
            List<SandboxUnitOption> options = new List<SandboxUnitOption>();
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
                    SandboxUnitVisual.TokenScaleFor(source.Size),
                    animations != null ? animations.FindSet(source.AnimationSetId) : null);

                options.Add(new SandboxUnitOption(definition, GroupLabel(source.Category)));
                if (source.Category == UnitCategory.Fighter)
                    fighters.Add(definition);
                else if (source.Category == UnitCategory.Creature)
                    creatures[source.Id] = definition;
            }

            if (fighters.Count == 0 || options.Count == 0)
                return CreateFallback();

            SandboxUnitContent content = new SandboxUnitContent(fighters, visuals, true)
            {
                AnimationDatabase = animations
            };
            content.AllUnits.AddRange(options);
            foreach (KeyValuePair<string, SandboxUnitDefinition> creature in creatures)
                content.CreaturesById[creature.Key] = creature.Value;
            return content;
        }

        private static string GroupLabel(UnitCategory category)
        {
            switch (category)
            {
                case UnitCategory.Fighter: return "Бойцы";
                case UnitCategory.Creature: return "Существа";
                case UnitCategory.Commander: return "Командиры";
                default: return "Прочие";
            }
        }

        private static SandboxUnitContent CreateFallback()
        {
            SandboxUnitContent content = new SandboxUnitContent(
                SandboxRoster.PlayerRoster,
                new Dictionary<string, SandboxUnitVisual>(),
                false);
            foreach (SandboxUnitDefinition fighter in SandboxRoster.PlayerRoster)
                content.AllUnits.Add(new SandboxUnitOption(fighter, "Бойцы"));
            foreach (SandboxUnitDefinition beast in SandboxRoster.EnemyRoster)
            {
                content.AllUnits.Add(new SandboxUnitOption(beast, "Существа"));
                content.CreaturesById[beast.Id] = beast;
            }
            return content;
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
