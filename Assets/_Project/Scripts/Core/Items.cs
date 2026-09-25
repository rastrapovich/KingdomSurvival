using System;
using System.Collections.Generic;

// ПР-08 (ProjectDocs/PR08_HERO_ITEMS_SPEC.md): вещи с настоящим владением.
// Канон §13.2: у бойца — оружие + защита/одежда + один особый предмет; у
// героя те же слоты, заплечная сумка и отдельно сюжетные вещи. Каталог
// вещей среза — в коде (ItemCatalog); содержание и числа — [РАБОЧЕЕ].

public enum ItemKind
{
    Weapon,
    Protection,
    Special,
    Story,
    Consumable
}

public enum ItemSlot
{
    None,
    Weapon,
    Protection,
    Special
}

// Прибавки к боевым числам шаблона UnitDatabase.
[Serializable]
public struct StatModifier
{
    public int MaxHitPoints;
    public int Attack;
    public int Defense;
    public int Damage;
    public int Movement;
    public int Initiative;
    public int AttackRange;

    public bool IsZero =>
        MaxHitPoints == 0 && Attack == 0 && Defense == 0 && Damage == 0 &&
        Movement == 0 && Initiative == 0 && AttackRange == 0;

    // «+1 атака, −1 инициатива» — для карточек и раскрытия чисел.
    public string Describe()
    {
        List<string> parts = new List<string>();
        Add(parts, MaxHitPoints, "HP");
        Add(parts, Attack, "атака");
        Add(parts, Defense, "защита");
        Add(parts, Damage, "урон");
        Add(parts, Movement, "движение");
        Add(parts, Initiative, "инициатива");
        Add(parts, AttackRange, "дальность");
        return string.Join(", ", parts);
    }

    private static void Add(List<string> parts, int value, string name)
    {
        if (value != 0)
            parts.Add((value > 0 ? "+" : "−") + Math.Abs(value) + " " + name);
    }
}

public sealed class ItemDefinition
{
    public string Id;
    public string Name;
    public string Description;
    public ItemKind Kind;
    // Для оружия: каким боевым шаблонам подходит (пусто — любому бойцу).
    public string[] UnitTypes = new string[0];
    public StatModifier Modifier;
    public int Uses;

    public bool IsStory => Kind == ItemKind.Story;
    public bool IsConsumable => Kind == ItemKind.Consumable;

    public ItemSlot Slot
    {
        get
        {
            switch (Kind)
            {
                case ItemKind.Weapon: return ItemSlot.Weapon;
                case ItemKind.Protection: return ItemSlot.Protection;
                case ItemKind.Special: return ItemSlot.Special;
                default: return ItemSlot.None;
            }
        }
    }
}

[Serializable]
public sealed class ItemInstanceData
{
    public string InstanceId = string.Empty;
    public string ItemId = string.Empty;
    // Пусто — кладовая Дома; иначе PersonId владельца.
    public string OwnerPersonId = string.Empty;
    public ItemSlot Slot = ItemSlot.None;
    public int UsesLeft;
}

[Serializable]
public sealed class InventoryData
{
    public List<ItemInstanceData> Items = new List<ItemInstanceData>();
    public int NextNumber = 1;
    // Однократные выдачи (стартовый набор, находки) — идемпотентность.
    public List<string> AppliedGrants = new List<string>();
}

public static class ItemCatalog
{
    // Стартовое снаряжение (правка §0.4 ТЗ): небольшие бонусы.
    public const string Sword = "item.sword";
    public const string SwordAndShield = "item.sword_and_shield";
    public const string Bow = "item.bow";
    public const string Spear = "item.spear";
    public const string Knife = "item.knife";
    public const string Axe = "item.axe";
    public const string PaddedCoat = "item.padded_coat";
    public const string MailShirt = "item.mail_shirt";
    public const string Jacket = "item.jacket";
    public const string FisherCoat = "item.fisher_coat";
    public const string HealerBag = "item.healer_bag";

    // Вещи первой главы (§8.1 ТЗ, утверждено).
    public const string StoreroomMail = "item.storeroom_mail";
    public const string OldHuntingBow = "item.old_hunting_bow";
    public const string RopeWithHooks = "item.rope_with_hooks";
    public const string DriedFish = "item.dried_fish";
    public const string UlyanaHerbs = "item.ulyana_herbs";

    // Сюжетная вещь главы: ID совпадает с меткой NarrativeState.Items.
    public const string SevenToothGauge = "chapter01.item.seven_tooth_gauge";

    private static readonly string[] Archers = { "archer", "scout" };

    private static readonly Dictionary<string, ItemDefinition> Definitions = BuildDefinitions();

    private static Dictionary<string, ItemDefinition> BuildDefinitions()
    {
        List<ItemDefinition> list = new List<ItemDefinition>
        {
            Weapon(Sword, "Меч", "Простой прямой меч — ухоженный, не парадный.", new StatModifier { Attack = 1 }),
            Weapon(SwordAndShield, "Меч и щит", "Щит видел не один удар; кромка перебита заново.", new StatModifier { Defense = 1 }),
            Weapon(Bow, "Лук", "Тугой лук на каждый день.", new StatModifier { Attack = 1 }, Archers),
            Weapon(Spear, "Копьё", "Длинное копьё с потемневшим древком.", new StatModifier { Attack = 1 }),
            Weapon(Knife, "Нож", "Короткий нож — больше для перевязок, чем для драки.", new StatModifier { Initiative = 1 }),
            Weapon(Axe, "Топор", "Рабочий топор: лодку чинить и за себя постоять.", new StatModifier { Damage = 1 }),
            Protection(PaddedCoat, "Стёганка", "Толстая стёганая одежда.", new StatModifier { Defense = 1 }),
            Protection(MailShirt, "Кольчуга", "Тяжёлая, но надёжная.", new StatModifier { Defense = 2, Initiative = -1 }),
            Protection(Jacket, "Кожаная куртка", "Лёгкая, не мешает двигаться.", new StatModifier { MaxHitPoints = 2 }),
            Protection(FisherCoat, "Рыбацкий кожух", "Пропитан водой и дымом, зато тёплый.", new StatModifier { MaxHitPoints = 2 }),
            new ItemDefinition
            {
                Id = HealerBag, Name = "Сумка лекаря", Kind = ItemKind.Special,
                Description = "Бинты, иглы, травы. Без неё Марта лечит хуже."
            },
            Protection(StoreroomMail, "Кольчуга из клети", "Лежала в клети без хозяина — чья, уже никто не помнит.",
                new StatModifier { Defense = 2, Initiative = -1 }),
            Weapon(OldHuntingBow, "Старый охотничий лук", "Бьёт дальше нынешних — делали для зверя, не для драки.",
                new StatModifier { AttackRange = 1 }, Archers),
            new ItemDefinition
            {
                Id = RopeWithHooks, Name = "Верёвка с крючьями", Kind = ItemKind.Special,
                Description = "Лада сплела из остатков настила. На подъёме и у воды выручает."
            },
            new ItemDefinition
            {
                Id = DriedFish, Name = "Сушёная рыба в дорогу", Kind = ItemKind.Consumable, Uses = 3,
                Description = "Варвара насушила из улова. В походе — два дня еды на одного."
            },
            new ItemDefinition
            {
                Id = UlyanaHerbs, Name = "Сумка трав Ульяны", Kind = ItemKind.Consumable, Uses = 2,
                Description = "Отвар снимает изнеможение с одного человека."
            },
            new ItemDefinition
            {
                Id = SevenToothGauge, Name = "Мерка семи зубьев", Kind = ItemKind.Story,
                Description = "Старая мерка воды. Её нельзя потерять: она ещё понадобится."
            }
        };

        Dictionary<string, ItemDefinition> map = new Dictionary<string, ItemDefinition>();
        foreach (ItemDefinition definition in list)
            map[definition.Id] = definition;
        return map;
    }

    private static ItemDefinition Weapon(string id, string name, string description, StatModifier modifier, string[] unitTypes = null)
    {
        return new ItemDefinition
        {
            Id = id, Name = name, Description = description, Kind = ItemKind.Weapon, Modifier = modifier,
            UnitTypes = unitTypes ?? new string[0]
        };
    }

    private static ItemDefinition Protection(string id, string name, string description, StatModifier modifier)
    {
        return new ItemDefinition { Id = id, Name = name, Description = description, Kind = ItemKind.Protection, Modifier = modifier };
    }

    public static ItemDefinition Find(string itemId)
    {
        return itemId != null && Definitions.TryGetValue(itemId, out ItemDefinition definition) ? definition : null;
    }

    public static IEnumerable<ItemDefinition> All => Definitions.Values;
}
