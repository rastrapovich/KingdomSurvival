using System;
using System.Collections.Generic;

// ПР-08 (ProjectDocs/PR08_HERO_ITEMS_SPEC.md): вещи с настоящим владением.
// Канон §13.2: у бойца — оружие + защита/одежда + один особый предмет; у
// героя те же слоты, заплечная сумка и отдельно сюжетные вещи.
//
// 12Т (ProjectDocs/SETTLEMENTS_TRADE_TAVERN_SPEC.md §3): определения вещей —
// авторская «База предметов» (KingdomSurvival.ItemDatabase); ядро читает их
// через ItemCatalog.Current. Значения по умолчанию ниже совпадают с исходной
// базой: на них работают EditMode-тесты и первый засев базы. Содержание,
// цены и числа — [РАБОЧЕЕ].

// Новые значения — строго в конец: старые уже сериализованы в базе.
public enum ItemKind
{
    Weapon,
    Protection,
    Special,
    Story,
    Consumable,
    // 12Т: вещь для торговли — не надевается и не используется.
    TradeGood
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

// 12Т: что делает вещь при использовании. Новые значения — в конец.
public enum ItemUseEffectType
{
    // Вернуть HP живому присутствующему человеку (не выше максимума; тяжёлую
    // рану не лечит и не оживляет).
    RestoreHitPoints,
    // Снять изнеможение с присутствующего человека.
    RemoveExhaustion,
    // Добавить припасы активного похода (ArmySupply).
    AddSupplies,
    // Дать знание (NarrativeState.Knowledge) и, при необходимости, флаг этапа
    // поиска — то, что WorldMapKnowledgeService читает для областей поиска.
    GrantKnowledge
}

[Serializable]
public sealed class ItemUseEffect
{
    public ItemUseEffectType Type;
    // HP или припасы; для знания не используется.
    public int Amount;
    public string KnowledgeId = string.Empty;
    // Флаг этапа поиска (WorldMapSearchStageDefinition.RequiredKnowledgeFlags).
    public string StageFlagId = string.Empty;
    // Что узнал герой — строка итога и Хроники («На бересте — изгиб русла…»).
    public string KnowledgeText = string.Empty;

    public bool NeedsTarget => Type == ItemUseEffectType.RestoreHitPoints || Type == ItemUseEffectType.RemoveExhaustion;

    public ItemUseEffect Clone()
    {
        return (ItemUseEffect)MemberwiseClone();
    }
}

[Serializable]
public sealed class ItemDefinition
{
    public string Id;
    public string Name;
    public string Description;
    public ItemKind Kind;
    // Для оружия: каким боевым шаблонам подходит (пусто — любому бойцу).
    public string[] UnitTypes = new string[0];
    // Эффект «пока надето» — один расчёт для героя, бойцов и боя
    // (CombatStatsAssembler).
    public StatModifier Modifier;
    // Сколько применений у новой вещи; остаток — в экземпляре.
    public int Uses;

    // 12Т: торговля и авторские признаки.
    public int Price;
    public bool CanSell = true;
    public bool CanBuy = true;
    // Одна на мир: торговец не восстанавливает её после продажи.
    public bool Unique;
    // Нельзя продать, отдать или потерять (кроме сюжетных — они такие всегда).
    public bool Protected;
    // Авторская категория «особая вещь со своей историей»; слота не добавляет.
    public bool Artifact;
    public string Origin = string.Empty;
    // Подпись действия: «Использовать», «Изучить», «Выпить».
    public string UseVerb = string.Empty;
    public List<ItemUseEffect> UseEffects = new List<ItemUseEffect>();
    // Технические метки для авторских проверок (рядом — ItemPresent по ID).
    public string[] Tags = new string[0];

    public bool IsStory => Kind == ItemKind.Story;
    public bool IsConsumable => Kind == ItemKind.Consumable;
    // Есть что «использовать»: расходуемая вещь с эффектами.
    public bool HasUse => IsConsumable && Uses > 0 && UseEffects != null && UseEffects.Count > 0;
    // Сюжетная или защищённая вещь не уходит из рук героя.
    public bool IsTradeProtected => IsStory || Protected;
    public string UseLabel => string.IsNullOrWhiteSpace(UseVerb) ? "Использовать" : UseVerb.Trim();

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

    public ItemDefinition Clone()
    {
        ItemDefinition copy = (ItemDefinition)MemberwiseClone();
        copy.UnitTypes = UnitTypes != null ? (string[])UnitTypes.Clone() : new string[0];
        copy.Tags = Tags != null ? (string[])Tags.Clone() : new string[0];
        copy.UseEffects = new List<ItemUseEffect>();
        if (UseEffects != null)
        {
            foreach (ItemUseEffect effect in UseEffects)
            {
                if (effect != null)
                    copy.UseEffects.Add(effect.Clone());
            }
        }
        return copy;
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

// 12Т: набор определений вещей. Порядок — порядок базы (стабилен для UI).
public sealed class ItemCatalogData
{
    private readonly Dictionary<string, ItemDefinition> map = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
    private readonly List<ItemDefinition> ordered = new List<ItemDefinition>();

    public ItemCatalogData(IEnumerable<ItemDefinition> definitions)
    {
        if (definitions == null)
            return;
        foreach (ItemDefinition definition in definitions)
        {
            // Дубли ID отсекает редактор; здесь побеждает первая запись.
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || map.ContainsKey(definition.Id))
                continue;
            map[definition.Id] = definition;
            ordered.Add(definition);
        }
    }

    public ItemDefinition Find(string itemId)
    {
        return itemId != null && map.TryGetValue(itemId, out ItemDefinition definition) ? definition : null;
    }

    public IReadOnlyList<ItemDefinition> All => ordered;
    public int Count => ordered.Count;
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

    // 12Т: рабочие товары поселений [РАБОЧЕЕ].
    public const string Wax = "item.goods.wax";
    public const string Pelts = "item.goods.pelts";
    public const string SalveJar = "item.salve_jar";
    public const string BitterInfusion = "item.bitter_infusion";
    public const string TrailRusks = "item.trail_rusks";
    public const string BoarSpear = "item.boar_spear";
    public const string ScaleVest = "item.scale_vest";
    public const string AurochsHorn = "item.artifact.aurochs_horn";
    public const string BirchBarkScheme = "item.birch_bark_scheme";
    // Находка тайника у старого русла: вещь-знак деда возчика.
    public const string CarterTokenKnife = "item.carter_token_knife";

    // Знание тайника у старого русла — его даёт и рассказ возчика, и береста.
    public const string OldBedCacheKnowledge = "freeplay.cache.knowledge.old_bed";
    public const string OldBedCacheStageFlag = "freeplay.cache.stage.area";

    private static readonly string[] Archers = { "archer", "scout" };

    private static ItemCatalogData current;

    // Действующий каталог. В игре его назначает ItemDatabaseRuntime из «Базы
    // предметов» до загрузки сцены; без назначения — исходные определения.
    public static ItemCatalogData Current
    {
        get => current ?? (current = CreateDefault());
        set => current = value;
    }

    public static ItemCatalogData CreateDefault()
    {
        return new ItemCatalogData(DefaultDefinitions());
    }

    public static void ResetToDefaults()
    {
        current = CreateDefault();
    }

    public static ItemDefinition Find(string itemId)
    {
        return Current.Find(itemId);
    }

    public static IEnumerable<ItemDefinition> All => Current.All;

    // Исходная база: перенесённые вещи ПР-08 (поведение и числа прежние) и
    // рабочие товары 12Т. Засев «Базы предметов» берёт отсюда только
    // недостающие записи и ничего не перезаписывает.
    public static List<ItemDefinition> DefaultDefinitions()
    {
        return new List<ItemDefinition>
        {
            Weapon(Sword, "Меч", "Простой прямой меч — ухоженный, не парадный.", new StatModifier { Attack = 1 }, 12),
            Weapon(SwordAndShield, "Меч и щит", "Щит видел не один удар; кромка перебита заново.", new StatModifier { Defense = 1 }, 16),
            Weapon(Bow, "Лук", "Тугой лук на каждый день.", new StatModifier { Attack = 1 }, 12, Archers),
            Weapon(Spear, "Копьё", "Длинное копьё с потемневшим древком.", new StatModifier { Attack = 1 }, 10),
            Weapon(Knife, "Нож", "Короткий нож — больше для перевязок, чем для драки.", new StatModifier { Initiative = 1 }, 4),
            Weapon(Axe, "Топор", "Рабочий топор: лодку чинить и за себя постоять.", new StatModifier { Damage = 1 }, 6),
            Protection(PaddedCoat, "Стёганка", "Толстая стёганая одежда.", new StatModifier { Defense = 1 }, 10),
            Protection(MailShirt, "Кольчуга", "Тяжёлая, но надёжная.", new StatModifier { Defense = 2, Initiative = -1 }, 30),
            Protection(Jacket, "Кожаная куртка", "Лёгкая, не мешает двигаться.", new StatModifier { MaxHitPoints = 2 }, 8),
            Protection(FisherCoat, "Рыбацкий кожух", "Пропитан водой и дымом, зато тёплый.", new StatModifier { MaxHitPoints = 2 }, 6),
            new ItemDefinition
            {
                Id = HealerBag, Name = "Сумка лекаря", Kind = ItemKind.Special, Price = 10,
                Description = "Бинты, иглы, травы. Без неё Марта лечит хуже.",
                Tags = new[] { "healer" }
            },
            Protection(StoreroomMail, "Кольчуга из клети", "Лежала в клети без хозяина — чья, уже никто не помнит.",
                new StatModifier { Defense = 2, Initiative = -1 }, 30),
            Weapon(OldHuntingBow, "Старый охотничий лук", "Бьёт дальше нынешних — делали для зверя, не для драки.",
                new StatModifier { AttackRange = 1 }, 18, Archers),
            new ItemDefinition
            {
                Id = RopeWithHooks, Name = "Верёвка с крючьями", Kind = ItemKind.Special, Price = 6,
                Description = "Лада сплела из остатков настила. На подъёме и у воды выручает.",
                Tags = new[] { "rope" }
            },
            // Прежнее поведение: +2 припаса за применение, 3 применения.
            Consumable(DriedFish, "Сушёная рыба в дорогу", "Варвара насушила из улова. В походе — два дня еды на одного.", 3, 3,
                new ItemUseEffect { Type = ItemUseEffectType.AddSupplies, Amount = 2 }, "Съесть"),
            // Прежнее поведение: снимает изнеможение, 2 применения.
            Consumable(UlyanaHerbs, "Сумка трав Ульяны", "Отвар снимает изнеможение с одного человека.", 2, 5,
                new ItemUseEffect { Type = ItemUseEffectType.RemoveExhaustion }, "Дать отвар"),
            new ItemDefinition
            {
                Id = SevenToothGauge, Name = "Мерка семи зубьев", Kind = ItemKind.Story, CanSell = false, CanBuy = false,
                Description = "Старая мерка воды. Её нельзя потерять: она ещё понадобится."
            },

            // --- 12Т: рабочие товары [РАБОЧЕЕ] ---
            new ItemDefinition
            {
                Id = Wax, Name = "Круг воска", Kind = ItemKind.TradeGood, Price = 6,
                Description = "Жёлтый, пахнет летом. На торгу воск берут охотно — свечи нужны всем."
            },
            new ItemDefinition
            {
                Id = Pelts, Name = "Связка беличьих шкурок", Kind = ItemKind.TradeGood, Price = 9,
                Description = "Шкурки связаны по десять. Мех зимний, плотный."
            },
            Consumable(SalveJar, "Горшочек мази", "Гусиный жир с дёгтем и травой. Пахнет так, что раны затягиваются от одного стыда.", 2, 8,
                new ItemUseEffect { Type = ItemUseEffectType.RestoreHitPoints, Amount = 4 }, "Смазать раны"),
            Consumable(BitterInfusion, "Туесок горького отвара", "Корень, кора и что-то ещё, о чём травница не сказала. Сон как рукой снимает.", 1, 5,
                new ItemUseEffect { Type = ItemUseEffectType.RemoveExhaustion }, "Дать отвар"),
            Consumable(TrailRusks, "Мешок сухарей", "Ржаные сухари с солью — не пир, но в дороге держат.", 2, 5,
                new ItemUseEffect { Type = ItemUseEffectType.AddSupplies, Amount = 3 }, "Раздать"),
            Weapon(BoarSpear, "Рогатина", "Тяжёлое широкое перо с перекладиной: на зверя, но и человек отступит.",
                new StatModifier { Attack = 1, Damage = 1, Initiative = -1 }, 18),
            Protection(ScaleVest, "Чешуйчатая безрукавка", "Пластинки нашиты внахлёст на кожу. Легче кольчуги, держит хуже.",
                new StatModifier { Defense = 1, MaxHitPoints = 2 }, 24),
            new ItemDefinition
            {
                Id = AurochsHorn, Name = "Старый турий рог", Kind = ItemKind.Special, Price = 40, Unique = true, Artifact = true,
                Description = "Окован медью, на раструбе — зарубки. Кто дунет перед сшибкой, тот и встанет первым.",
                Origin = "Купцы говорят, что рог снят со стены сгоревшего двора. Чьего — не говорят.",
                Modifier = new StatModifier { Initiative = 2 }
            },
            new ItemDefinition
            {
                Id = BirchBarkScheme, Name = "Береста с рисунком русла", Kind = ItemKind.Consumable, Uses = 1, Price = 14,
                Unique = true, UseVerb = "Изучить",
                Description = "Кусок бересты, на нём ножом процарапаны изгиб воды, ива и три черты.",
                UseEffects = new List<ItemUseEffect>
                {
                    new ItemUseEffect
                    {
                        Type = ItemUseEffectType.GrantKnowledge,
                        KnowledgeId = OldBedCacheKnowledge,
                        StageFlagId = OldBedCacheStageFlag,
                        KnowledgeText = "Изгиб старого русла к восходу от торгового села, у расколотой ивы — три черты: там прятали."
                    }
                }
            },
            new ItemDefinition
            {
                Id = CarterTokenKnife, Name = "Нож с медной насечкой", Kind = ItemKind.Special, Price = 8,
                Description = "Короткий нож, рукоять обмотана медной проволокой в три витка. Чей-то знак.",
                Modifier = new StatModifier { Initiative = 1 },
                Tags = new[] { "carter_token" }
            }
        };
    }

    private static ItemDefinition Weapon(string id, string name, string description, StatModifier modifier, int price, string[] unitTypes = null)
    {
        return new ItemDefinition
        {
            Id = id, Name = name, Description = description, Kind = ItemKind.Weapon, Modifier = modifier, Price = price,
            UnitTypes = unitTypes ?? new string[0]
        };
    }

    private static ItemDefinition Protection(string id, string name, string description, StatModifier modifier, int price)
    {
        return new ItemDefinition { Id = id, Name = name, Description = description, Kind = ItemKind.Protection, Modifier = modifier, Price = price };
    }

    private static ItemDefinition Consumable(string id, string name, string description, int uses, int price, ItemUseEffect effect, string verb)
    {
        return new ItemDefinition
        {
            Id = id, Name = name, Description = description, Kind = ItemKind.Consumable, Uses = uses, Price = price,
            UseVerb = verb, UseEffects = new List<ItemUseEffect> { effect }
        };
    }
}
