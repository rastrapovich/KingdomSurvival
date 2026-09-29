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

    // ПР-12Ж: размер существа — для жетона без рисунка сейчас и для
    // принудительного перемещения и контроля в ПР-16.
    public enum UnitSize
    {
        Medium,
        Small,
        Large
    }

    // Общие боевые кирпичи (BESTIARY.md COMBAT-B3), на которых строится
    // способность существа. Кирпичи реализуются в ПР-16.
    public enum UnitCombatBrick
    {
        AiPriority,
        ForcedMovement,
        GrappleTether,
        StatusMark,
        HazardTileState,
        Charge,
        AttachPersistent,
        CorpseInteraction,
        HiddenBurrow,
        Telegraph,
        Extension
    }

    public enum UnitAbilityStatus
    {
        // Записана в базе, но не действует: её кирпича ещё нет.
        WaitsForMechanic,
        Active
    }

    // Фирменная способность существа как данные: включается статусом в базе,
    // а не отдельным кодом под существо.
    [Serializable]
    public sealed class UnitAbilityData
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string title = string.Empty;
        [SerializeField, TextArea(2, 4)] private string description = string.Empty;
        [SerializeField] private UnitCombatBrick brick = UnitCombatBrick.Extension;
        [SerializeField] private UnitAbilityStatus status = UnitAbilityStatus.WaitsForMechanic;

        public string Id => id;
        public string Title => title;
        public string Description => description;
        public UnitCombatBrick Brick => brick;
        public UnitAbilityStatus Status => status;
        public bool IsActive => status == UnitAbilityStatus.Active;

        public static UnitAbilityData WaitingFor(string id, string title, UnitCombatBrick brick, string description)
        {
            return new UnitAbilityData
            {
                id = id ?? string.Empty,
                title = title ?? string.Empty,
                brick = brick,
                description = description ?? string.Empty,
                status = UnitAbilityStatus.WaitsForMechanic
            };
        }
    }

    [Serializable]
    public sealed class UnitEncounterSlot
    {
        [SerializeField] private string unitId = string.Empty;
        [SerializeField, Min(1)] private int count = 1;

        public string UnitId => unitId;
        public int Count => Mathf.Max(1, count);

        public UnitEncounterSlot()
        {
        }

        public UnitEncounterSlot(string unitId, int count)
        {
            this.unitId = unitId ?? string.Empty;
            this.count = Mathf.Max(1, count);
        }
    }

    // Готовый состав противников для тестового боя.
    [Serializable]
    public sealed class UnitEncounterPreset
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string title = string.Empty;
        [SerializeField, TextArea(1, 3)] private string purpose = string.Empty;
        [SerializeField] private List<UnitEncounterSlot> slots = new List<UnitEncounterSlot>();

        public string Id => id;
        public string Title => title;
        public string Purpose => purpose;
        public IReadOnlyList<UnitEncounterSlot> Slots => slots;

        public int TotalCount
        {
            get
            {
                int total = 0;
                if (slots != null)
                    foreach (UnitEncounterSlot slot in slots)
                        total += slot != null ? slot.Count : 0;
                return total;
            }
        }

        public static UnitEncounterPreset Create(string id, string title, string purpose, params UnitEncounterSlot[] slots)
        {
            return new UnitEncounterPreset
            {
                id = id ?? string.Empty,
                title = title ?? string.Empty,
                purpose = purpose ?? string.Empty,
                slots = new List<UnitEncounterSlot>(slots ?? Array.Empty<UnitEncounterSlot>())
            };
        }
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
        [SerializeField] private PortraitFitMode portraitFitMode = PortraitFitMode.Cover;
        [SerializeField, Min(0.05f)] private float portraitScale = 1f;
        [SerializeField] private Vector2 portraitOffsetNormalized = Vector2.zero;
        [SerializeField] private bool portraitFlipX;

        // Legacy-поле Базы существ до schemaVersion 1. Тогда Offset хранился
        // в пикселях preview 150x200. Оставляем его сериализованным, чтобы
        // MigrateIfNeeded мог безопасно перенести старую индивидуальную
        // кадрировку в нормализованный формат, не теряя правки пользователя.
        [SerializeField, HideInInspector] private Vector2 portraitOffset = Vector2.zero;
        [SerializeField] private Sprite battlefieldSprite;
        [SerializeField, Min(0.1f)] private float battlefieldScale = 1f;
        [SerializeField] private Vector2 battlefieldOffset = Vector2.zero;

        [Header("Тестовый бой")]
        [SerializeField, Min(0)] private int sandboxEncounterCount;

        [Header("Теги")]
        [SerializeField] private List<string> tagIds = new List<string>();

        [Header("Существо")]
        [SerializeField] private UnitSize size = UnitSize.Medium;
        [SerializeField] private List<UnitAbilityData> abilities = new List<UnitAbilityData>();

        public string Id => id;
        public UnitSize Size => size;
        public IReadOnlyList<UnitAbilityData> Abilities => abilities;
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
        public PortraitFitMode PortraitFitMode => portraitFitMode;
        public float PortraitScale => portraitScale > 0f
            ? Mathf.Max(0.05f, portraitScale)
            : 1f;
        public Vector2 PortraitOffsetNormalized => portraitOffsetNormalized;
        public bool PortraitFlipX => portraitFlipX;
        public Sprite BattlefieldSprite => battlefieldSprite;
        public float BattlefieldScale => Mathf.Max(0.1f, battlefieldScale);
        public Vector2 BattlefieldOffset => battlefieldOffset;
        public int SandboxEncounterCount => Mathf.Max(0, sandboxEncounterCount);
        public IReadOnlyList<string> TagIds => tagIds;

        // Существо каталога без рисунков: портрет и миниатюру назначает
        // художник позже, бой рисует жетон.
        public static UnitDefinitionData CreateCreature(
            string id,
            string displayLabel,
            UnitSize size,
            int maxHitPoints,
            int attack,
            int defense,
            int damage,
            int movement,
            int initiative,
            int attackRange,
            IEnumerable<string> tagIds,
            IEnumerable<UnitAbilityData> abilities)
        {
            return new UnitDefinitionData
            {
                id = id ?? string.Empty,
                displayLabel = displayLabel ?? string.Empty,
                category = UnitCategory.Creature,
                combatRole = UnitCombatRole.Creature,
                size = size,
                maxHitPoints = Mathf.Max(1, maxHitPoints),
                attack = Mathf.Max(0, attack),
                defense = Mathf.Max(0, defense),
                damage = Mathf.Max(1, damage),
                movement = Mathf.Max(1, movement),
                initiative = Mathf.Max(0, initiative),
                attackRange = Mathf.Max(1, attackRange),
                sandboxEncounterCount = 0,
                tagIds = new List<string>(tagIds ?? Array.Empty<string>()),
                abilities = new List<UnitAbilityData>(abilities ?? Array.Empty<UnitAbilityData>())
            };
        }

        internal void MigrateLegacyPortraitFraming()
        {
            portraitScale = portraitScale > 0f
                ? Mathf.Max(0.05f, portraitScale)
                : 1f;
            portraitFitMode = PortraitFitMode.Cover;
            portraitOffsetNormalized = UnitPortraitFraming.LegacyPixelsToNormalized(
                portraitOffset);
            portraitFlipX = false;
            portraitOffset = Vector2.zero;
        }

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
        // Схема 2 (ПР-12Ж): размер и способности существ, составы боя.
        public const int CurrentSchemaVersion = 2;

        // Сколько противников помещается в один бой (SandboxRoster.MaxEnemies).
        public const int MaxEncounterSize = 8;

        [SerializeField, HideInInspector] private int schemaVersion;
        [SerializeField] private List<UnitTagDefinition> tags = new List<UnitTagDefinition>();
        [SerializeField] private List<UnitDefinitionData> units = new List<UnitDefinitionData>();
        [SerializeField] private List<UnitEncounterPreset> encounterPresets = new List<UnitEncounterPreset>();

        public int SchemaVersion => schemaVersion;
        public IReadOnlyList<UnitTagDefinition> Tags => tags;
        public IReadOnlyList<UnitDefinitionData> Units => units;
        public IReadOnlyList<UnitEncounterPreset> EncounterPresets => encounterPresets;

        /// <summary>
        /// Переносит старое пиксельное кадрирование портретов в доли рамки
        /// (до схемы 1). Схема 2 новых полей не переносит: их значения по
        /// умолчанию подходят старым записям. Метод идемпотентен.
        /// </summary>
        public bool MigrateIfNeeded()
        {
            if (schemaVersion >= CurrentSchemaVersion)
                return false;

            if (schemaVersion < 1 && units != null)
            {
                for (int i = 0; i < units.Count; i++)
                    units[i]?.MigrateLegacyPortraitFraming();
            }

            if (encounterPresets == null)
                encounterPresets = new List<UnitEncounterPreset>();
            schemaVersion = CurrentSchemaVersion;
            return true;
        }

        // Для засева каталога: добавляет только отсутствующее.
        public bool AddUnitIfMissing(UnitDefinitionData unit)
        {
            if (unit == null || string.IsNullOrWhiteSpace(unit.Id) || FindById(unit.Id) != null)
                return false;
            units.Add(unit);
            return true;
        }

        public bool AddPresetIfMissing(UnitEncounterPreset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Id) || FindPreset(preset.Id) != null)
                return false;
            if (encounterPresets == null)
                encounterPresets = new List<UnitEncounterPreset>();
            encounterPresets.Add(preset);
            return true;
        }

        public UnitEncounterPreset FindPreset(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId) || encounterPresets == null)
                return null;
            foreach (UnitEncounterPreset preset in encounterPresets)
            {
                if (preset != null && string.Equals(preset.Id, presetId, StringComparison.Ordinal))
                    return preset;
            }
            return null;
        }

        // Существа, которые ждут рисунка художника. Это не ошибка базы:
        // бой рисует для них жетон.
        public void CollectArtGaps(List<string> gaps)
        {
            if (gaps == null)
                throw new ArgumentNullException(nameof(gaps));

            gaps.Clear();
            foreach (UnitDefinitionData unit in units)
            {
                if (unit == null || string.IsNullOrWhiteSpace(unit.Id))
                    continue;
                if (unit.Portrait == null && unit.BattlefieldSprite == null)
                    gaps.Add(unit.Id + ": ждёт портрета и миниатюры поля.");
                else if (unit.Portrait == null)
                    gaps.Add(unit.Id + ": ждёт портрета.");
                else if (unit.BattlefieldSprite == null)
                    gaps.Add(unit.Id + ": ждёт миниатюры поля.");
            }
        }

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
                // Отсутствие рисунка — не ошибка, а «ждёт рисунка»: CollectArtGaps.

                for (int tagIndex = 0; tagIndex < unit.TagIds.Count; tagIndex++)
                {
                    string tagId = unit.TagIds[tagIndex];
                    if (!tagIdSet.Contains(tagId))
                        issues.Add(unit.Id + ": неизвестный тег " + tagId + ".");
                }

                HashSet<string> abilityIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (UnitAbilityData ability in unit.Abilities)
                {
                    if (ability == null || string.IsNullOrWhiteSpace(ability.Id))
                        issues.Add(unit.Id + ": способность без ID.");
                    else if (!abilityIds.Add(ability.Id))
                        issues.Add(unit.Id + ": повторяющийся ID способности " + ability.Id + ".");
                    else if (string.IsNullOrWhiteSpace(ability.Title))
                        issues.Add(unit.Id + ": у способности " + ability.Id + " нет названия.");
                }
            }

            HashSet<string> presetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (UnitEncounterPreset preset in encounterPresets ?? new List<UnitEncounterPreset>())
            {
                if (preset == null || string.IsNullOrWhiteSpace(preset.Id))
                {
                    issues.Add("Состав боя без ID.");
                    continue;
                }
                if (!presetIds.Add(preset.Id))
                    issues.Add("Повторяющийся ID состава боя: " + preset.Id + ".");
                if (preset.TotalCount < 1 || preset.TotalCount > MaxEncounterSize)
                    issues.Add("Состав " + preset.Id + ": противников должно быть от 1 до " + MaxEncounterSize + ".");
                foreach (UnitEncounterSlot slot in preset.Slots)
                {
                    UnitDefinitionData member = slot != null ? FindById(slot.UnitId) : null;
                    if (member == null)
                        issues.Add("Состав " + preset.Id + ": неизвестное существо " + (slot != null ? slot.UnitId : "?") + ".");
                    else if (member.Category != UnitCategory.Creature)
                        issues.Add("Состав " + preset.Id + ": " + member.Id + " не существо.");
                }
            }
        }
    }
}
