using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.UnitDatabase
{
    public enum UnitCategory
    {
        Fighter,
        Creature,
        Commander,
        Other
    }

    public enum UnitCombatRole
    {
        Guard,
        Archer,
        Healer,
        Spearman,
        Scout,
        Militia,
        Creature,
        Custom
    }

    [Serializable]
    public sealed class UnitTagDefinition
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayLabel = string.Empty;
        [SerializeField] private string category = string.Empty;
        [SerializeField] private Color color = Color.gray;
        [SerializeField, TextArea(2, 4)] private string description = string.Empty;

        public string Id => id;
        public string DisplayLabel => displayLabel;
        public string Category => category;
        public Color Color => color;
        public string Description => description;
    }

    [Serializable]
    public sealed class UnitDefinitionData
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayLabel = string.Empty;
        [SerializeField] private UnitCategory category = UnitCategory.Fighter;
        [SerializeField] private UnitCombatRole combatRole = UnitCombatRole.Custom;

        [Header("Боевые характеристики")]
        [SerializeField, Min(1)] private int maxHitPoints = 100;
        [SerializeField, Min(0)] private int attack = 1;
        [SerializeField, Min(0)] private int defense = 1;
        [SerializeField, Min(1)] private int damage = 10;
        [SerializeField, Min(1)] private int movement = 3;
        [SerializeField, Min(0)] private int initiative = 1;
        [SerializeField, Min(1)] private int attackRange = 1;

        [Header("Изображения")]
        [SerializeField] private Sprite portrait;
        [SerializeField, Min(0.1f)] private float portraitScale = 1f;
        [SerializeField] private Vector2 portraitOffset = Vector2.zero;
        [SerializeField] private Sprite battlefieldSprite;
        [SerializeField, Min(0.1f)] private float battlefieldScale = 1f;
        [SerializeField] private Vector2 battlefieldOffset = Vector2.zero;

        [Header("Тестовый бой")]
        [SerializeField, Min(0)] private int sandboxEncounterCount;

        [Header("Теги")]
        [SerializeField] private List<string> tagIds = new List<string>();

        public string Id => id;
        public string DisplayLabel => displayLabel;
        public UnitCategory Category => category;
        public UnitCombatRole CombatRole => combatRole;
        public int MaxHitPoints => maxHitPoints;
        public int Attack => attack;
        public int Defense => defense;
        public int Damage => damage;
        public int Movement => movement;
        public int Initiative => initiative;
        public int AttackRange => attackRange;
        public Sprite Portrait => portrait;
        public float PortraitScale => Mathf.Max(0.1f, portraitScale);
        public Vector2 PortraitOffset => portraitOffset;
        public Sprite BattlefieldSprite => battlefieldSprite;
        public float BattlefieldScale => Mathf.Max(0.1f, battlefieldScale);
        public Vector2 BattlefieldOffset => battlefieldOffset;
        public int SandboxEncounterCount => Mathf.Max(0, sandboxEncounterCount);
        public IReadOnlyList<string> TagIds => tagIds;

        public bool HasTag(string tagId)
        {
            if (string.IsNullOrWhiteSpace(tagId) || tagIds == null)
                return false;

            for (int i = 0; i < tagIds.Count; i++)
            {
                if (string.Equals(tagIds[i], tagId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    [CreateAssetMenu(
        fileName = "KingdomSurvivalUnits",
        menuName = "Kingdom Survival/База существ")]
    public sealed class UnitDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "UnitDatabase/KingdomSurvivalUnits";

        [SerializeField] private List<UnitTagDefinition> tags = new List<UnitTagDefinition>();
        [SerializeField] private List<UnitDefinitionData> units = new List<UnitDefinitionData>();

        public IReadOnlyList<UnitTagDefinition> Tags => tags;
        public IReadOnlyList<UnitDefinitionData> Units => units;

        public UnitDefinitionData FindById(string typeId)
        {
            if (string.IsNullOrWhiteSpace(typeId) || units == null)
                return null;

            for (int i = 0; i < units.Count; i++)
            {
                UnitDefinitionData unit = units[i];
                if (unit != null && string.Equals(unit.Id, typeId, StringComparison.Ordinal))
                    return unit;
            }

            return null;
        }

        public UnitTagDefinition FindTag(string tagId)
        {
            if (string.IsNullOrWhiteSpace(tagId) || tags == null)
                return null;

            for (int i = 0; i < tags.Count; i++)
            {
                UnitTagDefinition tag = tags[i];
                if (tag != null && string.Equals(tag.Id, tagId, StringComparison.Ordinal))
                    return tag;
            }

            return null;
        }

        public void CollectValidationIssues(List<string> issues)
        {
            if (issues == null)
                throw new ArgumentNullException(nameof(issues));

            issues.Clear();
            HashSet<string> tagIdSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tags.Count; i++)
            {
                UnitTagDefinition tag = tags[i];
                if (tag == null || string.IsNullOrWhiteSpace(tag.Id))
                {
                    issues.Add("Тег #" + (i + 1) + ": отсутствует ID.");
                    continue;
                }

                if (!tagIdSet.Add(tag.Id))
                    issues.Add("Повторяющийся ID тега: " + tag.Id + ".");
            }

            HashSet<string> unitIdSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < units.Count; i++)
            {
                UnitDefinitionData unit = units[i];
                if (unit == null || string.IsNullOrWhiteSpace(unit.Id))
                {
                    issues.Add("Существо #" + (i + 1) + ": отсутствует ID типа.");
                    continue;
                }

                if (!unitIdSet.Add(unit.Id))
                    issues.Add("Повторяющийся ID типа: " + unit.Id + ".");
                if (string.IsNullOrWhiteSpace(unit.DisplayLabel))
                    issues.Add(unit.Id + ": отсутствует отображаемое название типа.");
                if (unit.MaxHitPoints < 1 || unit.Damage < 1 || unit.Movement < 1 || unit.AttackRange < 1)
                    issues.Add(unit.Id + ": одна из обязательных характеристик меньше 1.");
                if (unit.Portrait == null)
                    issues.Add(unit.Id + ": не назначен портрет.");
                if (unit.BattlefieldSprite == null)
                    issues.Add(unit.Id + ": не назначена миниатюра поля.");

                for (int tagIndex = 0; tagIndex < unit.TagIds.Count; tagIndex++)
                {
                    string tagId = unit.TagIds[tagIndex];
                    if (!tagIdSet.Contains(tagId))
                        issues.Add(unit.Id + ": неизвестный тег " + tagId + ".");
                }
            }
        }
    }
}
