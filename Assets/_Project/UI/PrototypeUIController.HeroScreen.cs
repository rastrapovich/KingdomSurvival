using System.Collections.Generic;
using KingdomSurvival.UnitDatabase;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Экран героя: объединяет прежние «Отряд» и «Панель командира» в один
/// полноэкранный слой, вызываемый одной кнопкой из нижней полосы оболочки.
///
/// Боевые характеристики, портреты и теги берутся из `UnitDatabase` —
/// единственного утверждённого источника боевых данных. `FighterData`
/// прототипа хранит только имя, роль и уровень, поэтому характеристики
/// разрешаются по роли бойца.
///
/// Блоки «Состояния», «Способности», «Снаряжение», «Инвентарь» и шкала опыта
/// собраны как рабочие заглушки: соответствующие системы ещё не утверждены.
/// </summary>
public partial class PrototypeUIController
{
    private const int HeroScreenFighterSlots = 4;

    private static readonly Color HeroScreenBackdrop = new Color(0.055f, 0.063f, 0.078f, 0.97f);
    private static readonly Color HeroScreenPanel = new Color(0.164f, 0.180f, 0.212f, 1f);
    private static readonly Color HeroScreenPanelDeep = new Color(0.106f, 0.122f, 0.145f, 1f);
    private static readonly Color HeroScreenBorder = new Color(0.357f, 0.314f, 0.243f, 1f);
    private static readonly Color HeroScreenGold = new Color(0.867f, 0.710f, 0.388f, 1f);
    private static readonly Color HeroScreenText = new Color(0.886f, 0.859f, 0.800f, 1f);
    private static readonly Color HeroScreenMuted = new Color(0.612f, 0.604f, 0.580f, 1f);
    private static readonly Color HeroScreenSlotEmpty = new Color(0.129f, 0.145f, 0.173f, 1f);

    private VisualElement heroScreenOverlay;
    private VisualElement heroScreenRosterRow;
    private VisualElement heroScreenRetinueRow;
    private VisualElement heroScreenTagsRow;
    private VisualElement heroScreenStatsGrid;
    private VisualElement heroScreenStatesRow;
    private VisualElement heroScreenAbilitiesRow;
    private VisualElement heroScreenEquipmentGrid;
    private VisualElement heroScreenInventoryGrid;
    private VisualElement heroScreenPortrait;
    private Label heroScreenNameLabel;
    private Label heroScreenRoleLabel;
    private Label heroScreenLevelLabel;
    private VisualElement heroScreenExperienceFill;
    private Label heroScreenExperienceLabel;
    private VisualElement heroScreenUnitCard;
    private Button heroScreenNavButton;

    private UnitDatabaseAsset heroScreenUnits;

    private bool IsHeroScreenOpen =>
        heroScreenOverlay != null && heroScreenOverlay.style.display == DisplayStyle.Flex;

    // ------------------------------------------------------------------
    // Инициализация
    // ------------------------------------------------------------------

    private void InitializeHeroScreenUi()
    {
        if (interfaceRoot == null || heroScreenOverlay != null)
            return;

        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");
        if (screen == null)
            return;

        heroScreenUnits = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
        BuildHeroScreen(screen);

        heroScreenNavButton = interfaceRoot.Q<Button>("nav-hero-button");
        if (heroScreenNavButton != null)
            heroScreenNavButton.clicked += ToggleHeroScreen;
    }

    private void ToggleHeroScreen()
    {
        if (IsHeroScreenOpen)
            CloseHeroScreen();
        else
            OpenHeroScreen();
    }

    private void OpenHeroScreen()
    {
        if (heroScreenOverlay == null)
            return;

        heroScreenOverlay.style.display = DisplayStyle.Flex;
        heroScreenOverlay.BringToFront();
        RefreshHeroScreen();
    }

    private void CloseHeroScreen()
    {
        if (heroScreenOverlay == null)
            return;

        HideHeroScreenUnitCard();
        heroScreenOverlay.style.display = DisplayStyle.None;
    }

    // ------------------------------------------------------------------
    // Построение
    // ------------------------------------------------------------------

    private void BuildHeroScreen(VisualElement screen)
    {
        heroScreenOverlay = new VisualElement { name = "hero-screen-overlay" };
        heroScreenOverlay.style.position = Position.Absolute;
        heroScreenOverlay.style.left = 0f;
        heroScreenOverlay.style.right = 0f;
        heroScreenOverlay.style.top = 0f;
        heroScreenOverlay.style.bottom = 0f;
        heroScreenOverlay.style.backgroundColor = HeroScreenBackdrop;
        heroScreenOverlay.style.display = DisplayStyle.None;
        heroScreenOverlay.style.paddingLeft = 18f;
        heroScreenOverlay.style.paddingRight = 18f;
        heroScreenOverlay.style.paddingTop = 12f;
        heroScreenOverlay.style.paddingBottom = 12f;
        screen.Add(heroScreenOverlay);

        heroScreenOverlay.Add(BuildHeroScreenHeader());

        VisualElement columns = new VisualElement { name = "hero-screen-columns" };
        columns.style.flexDirection = FlexDirection.Row;
        columns.style.flexGrow = 1f;
        columns.style.minHeight = 0f;
        heroScreenOverlay.Add(columns);

        columns.Add(BuildHeroScreenLeftColumn());
        columns.Add(BuildHeroScreenCenterColumn());
        columns.Add(BuildHeroScreenRightColumn());

        heroScreenOverlay.Add(BuildHeroScreenRosterBar());
        BuildHeroScreenUnitCard();
    }

    private VisualElement BuildHeroScreenHeader()
    {
        VisualElement header = new VisualElement { name = "hero-screen-header" };
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.height = 44f;
        header.style.flexShrink = 0f;
        header.style.marginBottom = 10f;

        Label title = new Label("ГЕРОЙ") { name = "hero-screen-title" };
        title.style.color = HeroScreenGold;
        title.style.fontSize = 20f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.Add(title);

        Button close = new Button(CloseHeroScreen)
        {
            name = "hero-screen-close-button",
            text = "ЗАКРЫТЬ"
        };
        StyleHeroScreenButton(close, 120f, 32f);
        header.Add(close);
        return header;
    }

    private VisualElement BuildHeroScreenLeftColumn()
    {
        VisualElement column = new VisualElement { name = "hero-screen-left-column" };
        column.style.width = new Length(26f, LengthUnit.Percent);
        column.style.marginRight = 12f;
        column.style.minWidth = 0f;

        VisualElement identity = CreateHeroScreenPanel("hero-screen-identity", "ГЕРОЙ");

        heroScreenPortrait = new VisualElement { name = "hero-screen-portrait" };
        heroScreenPortrait.style.height = 240f;
        heroScreenPortrait.style.marginBottom = 8f;
        heroScreenPortrait.style.backgroundColor = HeroScreenPanelDeep;
        SetHeroScreenBorder(heroScreenPortrait, 1f);
        identity.Add(heroScreenPortrait);

        heroScreenNameLabel = new Label("Командир") { name = "hero-screen-name" };
        heroScreenNameLabel.style.color = HeroScreenText;
        heroScreenNameLabel.style.fontSize = 17f;
        heroScreenNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        identity.Add(heroScreenNameLabel);

        heroScreenRoleLabel = new Label("Командир поселения") { name = "hero-screen-role" };
        heroScreenRoleLabel.style.color = HeroScreenMuted;
        heroScreenRoleLabel.style.fontSize = 11f;
        heroScreenRoleLabel.style.marginBottom = 8f;
        identity.Add(heroScreenRoleLabel);

        identity.Add(BuildHeroScreenExperienceBlock());
        column.Add(identity);

        VisualElement states = CreateHeroScreenPanel("hero-screen-states", "СОСТОЯНИЯ");
        heroScreenStatesRow = CreateHeroScreenWrapRow("hero-screen-states-row");
        states.Add(heroScreenStatesRow);
        column.Add(states);

        return column;
    }

    private VisualElement BuildHeroScreenExperienceBlock()
    {
        VisualElement block = new VisualElement { name = "hero-screen-experience" };
        block.style.marginTop = 4f;

        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;

        heroScreenLevelLabel = new Label("Уровень 1") { name = "hero-screen-level" };
        heroScreenLevelLabel.style.color = HeroScreenGold;
        heroScreenLevelLabel.style.fontSize = 12f;
        heroScreenLevelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(heroScreenLevelLabel);

        heroScreenExperienceLabel = new Label("система опыта не продумана")
        {
            name = "hero-screen-experience-label"
        };
        heroScreenExperienceLabel.style.color = HeroScreenMuted;
        heroScreenExperienceLabel.style.fontSize = 9f;
        row.Add(heroScreenExperienceLabel);
        block.Add(row);

        VisualElement track = new VisualElement { name = "hero-screen-experience-track" };
        track.style.height = 12f;
        track.style.marginTop = 4f;
        track.style.backgroundColor = HeroScreenPanelDeep;
        SetHeroScreenBorder(track, 1f);
        block.Add(track);

        heroScreenExperienceFill = new VisualElement { name = "hero-screen-experience-fill" };
        heroScreenExperienceFill.style.height = Length.Percent(100f);
        heroScreenExperienceFill.style.width = Length.Percent(0f);
        heroScreenExperienceFill.style.backgroundColor = HeroScreenGold;
        track.Add(heroScreenExperienceFill);

        return block;
    }

    private VisualElement BuildHeroScreenCenterColumn()
    {
        VisualElement column = new VisualElement { name = "hero-screen-center-column" };
        column.style.flexGrow = 1f;
        column.style.minWidth = 0f;
        column.style.marginRight = 12f;

        VisualElement stats = CreateHeroScreenPanel("hero-screen-stats", "ХАРАКТЕРИСТИКИ");
        heroScreenStatsGrid = new VisualElement { name = "hero-screen-stats-grid" };
        heroScreenStatsGrid.style.flexDirection = FlexDirection.Row;
        heroScreenStatsGrid.style.flexWrap = Wrap.Wrap;
        stats.Add(heroScreenStatsGrid);
        column.Add(stats);

        VisualElement tags = CreateHeroScreenPanel("hero-screen-tags", "ТЕГИ");
        heroScreenTagsRow = CreateHeroScreenWrapRow("hero-screen-tags-row");
        tags.Add(heroScreenTagsRow);
        column.Add(tags);

        VisualElement abilities = CreateHeroScreenPanel("hero-screen-abilities", "СПОСОБНОСТИ");
        heroScreenAbilitiesRow = CreateHeroScreenWrapRow("hero-screen-abilities-row");
        abilities.Add(heroScreenAbilitiesRow);
        column.Add(abilities);

        return column;
    }

    private VisualElement BuildHeroScreenRightColumn()
    {
        VisualElement column = new VisualElement { name = "hero-screen-right-column" };
        column.style.width = new Length(26f, LengthUnit.Percent);
        column.style.minWidth = 0f;

        VisualElement equipment = CreateHeroScreenPanel("hero-screen-equipment", "СНАРЯЖЕНИЕ");
        heroScreenEquipmentGrid = new VisualElement { name = "hero-screen-equipment-grid" };
        heroScreenEquipmentGrid.style.flexDirection = FlexDirection.Row;
        heroScreenEquipmentGrid.style.flexWrap = Wrap.Wrap;
        equipment.Add(heroScreenEquipmentGrid);
        column.Add(equipment);

        VisualElement inventory = CreateHeroScreenPanel("hero-screen-inventory", "ИНВЕНТАРЬ");
        inventory.style.flexGrow = 1f;
        heroScreenInventoryGrid = new VisualElement { name = "hero-screen-inventory-grid" };
        heroScreenInventoryGrid.style.flexDirection = FlexDirection.Row;
        heroScreenInventoryGrid.style.flexWrap = Wrap.Wrap;
        inventory.Add(heroScreenInventoryGrid);
        column.Add(inventory);

        return column;
    }

    private VisualElement BuildHeroScreenRosterBar()
    {
        VisualElement bar = new VisualElement { name = "hero-screen-roster-bar" };
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.flexShrink = 0f;
        bar.style.marginTop = 10f;

        VisualElement roster = CreateHeroScreenPanel(
            "hero-screen-roster",
            "СОСТАВ ПОХОДА — командир и до четырёх бойцов");
        roster.style.flexGrow = 1f;
        roster.style.marginRight = 12f;
        roster.style.marginBottom = 0f;

        heroScreenRosterRow = new VisualElement { name = "hero-screen-roster-row" };
        heroScreenRosterRow.style.flexDirection = FlexDirection.Row;
        roster.Add(heroScreenRosterRow);
        bar.Add(roster);

        VisualElement retinue = CreateHeroScreenPanel("hero-screen-retinue", "СВИТА");
        retinue.style.width = 320f;
        retinue.style.flexShrink = 0f;
        retinue.style.marginBottom = 0f;

        heroScreenRetinueRow = new VisualElement { name = "hero-screen-retinue-row" };
        heroScreenRetinueRow.style.flexDirection = FlexDirection.Row;
        heroScreenRetinueRow.style.flexWrap = Wrap.Wrap;
        retinue.Add(heroScreenRetinueRow);
        bar.Add(retinue);

        return bar;
    }

    // ------------------------------------------------------------------
    // Наполнение данными
    // ------------------------------------------------------------------

    private void RefreshHeroScreen()
    {
        if (!IsHeroScreenOpen || gameState == null)
            return;

        CommanderData commander = gameState.GetSelectedCommander();
        UnitDefinitionData heroUnit = ResolveHeroScreenUnit(commander);

        if (commander != null)
        {
            heroScreenNameLabel.text = commander.Name;
            heroScreenRoleLabel.text = string.IsNullOrWhiteSpace(commander.Role)
                ? "Командир поселения"
                : commander.Role;
            heroScreenLevelLabel.text = "Уровень " + commander.Level;
        }

        ApplyHeroScreenPortrait(heroScreenPortrait, heroUnit);
        heroScreenExperienceFill.style.width = Length.Percent(0f);

        RefreshHeroScreenStats(heroUnit);
        RefreshHeroScreenTags(heroUnit);
        RefreshHeroScreenStates(commander);
        RefreshHeroScreenAbilities();
        RefreshHeroScreenSlots(heroScreenEquipmentGrid, HeroScreenEquipmentSlots, 76f);
        RefreshHeroScreenSlots(heroScreenInventoryGrid, HeroScreenInventorySlots, 62f);
        RefreshHeroScreenRoster(commander, heroUnit);
        RefreshHeroScreenRetinue();
    }

    private static readonly string[] HeroScreenEquipmentSlots =
    {
        "Оружие", "Щит", "Доспех", "Шлем", "Пояс", "Оберег"
    };

    private static readonly string[] HeroScreenInventorySlots =
    {
        "", "", "", "", "", "", "", "", "", "", "", ""
    };

    private void RefreshHeroScreenStats(UnitDefinitionData unit)
    {
        heroScreenStatsGrid.Clear();
        AddHeroScreenStat("Здоровье", unit != null ? unit.MaxHitPoints : 0);
        AddHeroScreenStat("Атака", unit != null ? unit.Attack : 0);
        AddHeroScreenStat("Защита", unit != null ? unit.Defense : 0);
        AddHeroScreenStat("Урон", unit != null ? unit.Damage : 0);
        AddHeroScreenStat("Движение", unit != null ? unit.Movement : 0);
        AddHeroScreenStat("Инициатива", unit != null ? unit.Initiative : 0);
        AddHeroScreenStat("Дальность атаки", unit != null ? unit.AttackRange : 0);
    }

    private void AddHeroScreenStat(string label, int value)
    {
        VisualElement box = new VisualElement
        {
            name = "hero-screen-stat-" + HeroScreenSlug(label)
        };
        box.style.width = new Length(33f, LengthUnit.Percent);
        box.style.paddingLeft = 8f;
        box.style.paddingRight = 8f;
        box.style.paddingTop = 6f;
        box.style.paddingBottom = 6f;
        box.style.marginBottom = 4f;

        Label caption = new Label(label);
        caption.style.color = HeroScreenMuted;
        caption.style.fontSize = 10f;
        box.Add(caption);

        Label number = new Label(value.ToString());
        number.style.color = HeroScreenText;
        number.style.fontSize = 18f;
        number.style.unityFontStyleAndWeight = FontStyle.Bold;
        box.Add(number);

        heroScreenStatsGrid.Add(box);
    }

    private void RefreshHeroScreenTags(UnitDefinitionData unit)
    {
        heroScreenTagsRow.Clear();
        if (heroScreenUnits == null || unit == null)
        {
            heroScreenTagsRow.Add(CreateHeroScreenHint("База существ недоступна."));
            return;
        }

        int shown = 0;
        for (int i = 0; i < unit.TagIds.Count; i++)
        {
            UnitTagDefinition tag = heroScreenUnits.FindTag(unit.TagIds[i]);
            if (tag == null)
                continue;
            heroScreenTagsRow.Add(CreateHeroScreenChip(tag.DisplayLabel, tag.Color, tag.Description));
            shown++;
        }

        if (shown == 0)
            heroScreenTagsRow.Add(CreateHeroScreenHint("У героя пока нет тегов."));
    }

    private void RefreshHeroScreenStates(CommanderData commander)
    {
        heroScreenStatesRow.Clear();
        string phase = commander != null && commander.State == CommanderState.InCastle
            ? "Дома"
            : "В пути";
        heroScreenStatesRow.Add(CreateHeroScreenChip(phase, HeroScreenGold, "Текущее положение командира."));
        heroScreenStatesRow.Add(CreateHeroScreenHint("Система состояний будет реализована позже."));
    }

    private void RefreshHeroScreenAbilities()
    {
        heroScreenAbilitiesRow.Clear();
        for (int i = 0; i < 3; i++)
        {
            VisualElement slot = new VisualElement
            {
                name = "hero-screen-ability-slot-" + (i + 1)
            };
            slot.style.width = 96f;
            slot.style.height = 96f;
            slot.style.marginRight = 8f;
            slot.style.marginBottom = 6f;
            slot.style.backgroundColor = HeroScreenSlotEmpty;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            SetHeroScreenBorder(slot, 1f);

            Label caption = new Label("Способность");
            caption.style.color = HeroScreenMuted;
            caption.style.fontSize = 9f;
            slot.Add(caption);
            heroScreenAbilitiesRow.Add(slot);
        }

        heroScreenAbilitiesRow.Add(CreateHeroScreenHint("Умения героя ещё не спроектированы."));
    }

    private void RefreshHeroScreenSlots(VisualElement grid, string[] captions, float size)
    {
        grid.Clear();
        for (int i = 0; i < captions.Length; i++)
        {
            VisualElement slot = new VisualElement
            {
                name = grid.name + "-slot-" + (i + 1)
            };
            slot.style.width = size;
            slot.style.height = size;
            slot.style.marginRight = 6f;
            slot.style.marginBottom = 6f;
            slot.style.backgroundColor = HeroScreenSlotEmpty;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            SetHeroScreenBorder(slot, 1f);

            if (!string.IsNullOrEmpty(captions[i]))
            {
                Label caption = new Label(captions[i]);
                caption.style.color = HeroScreenMuted;
                caption.style.fontSize = 9f;
                caption.style.whiteSpace = WhiteSpace.Normal;
                caption.style.unityTextAlign = TextAnchor.MiddleCenter;
                slot.Add(caption);
            }

            grid.Add(slot);
        }
    }

    private void RefreshHeroScreenRoster(CommanderData commander, UnitDefinitionData heroUnit)
    {
        heroScreenRosterRow.Clear();
        heroScreenRosterRow.Add(CreateHeroScreenRosterCard(
            "hero-screen-roster-commander",
            commander != null ? commander.Name : "Командир",
            "Командир",
            commander != null ? commander.Level : 1,
            heroUnit,
            true));

        List<FighterData> party = GetHeroScreenParty();
        List<UnitDefinitionData> creatures = GetHeroScreenCreatures();

        for (int i = 0; i < HeroScreenFighterSlots; i++)
        {
            if (i < party.Count)
            {
                FighterData fighter = party[i];
                heroScreenRosterRow.Add(CreateHeroScreenRosterCard(
                    "hero-screen-roster-fighter-" + (i + 1),
                    fighter.Name,
                    fighter.Role,
                    fighter.Level,
                    ResolveHeroScreenUnit(fighter),
                    false));
                continue;
            }

            // Пустые места занимают существа из базы как рабочие заглушки:
            // правая кнопка открывает их карточку.
            int creatureIndex = i - party.Count;
            UnitDefinitionData creature = creatureIndex < creatures.Count
                ? creatures[creatureIndex]
                : null;
            heroScreenRosterRow.Add(CreateHeroScreenRosterCard(
                "hero-screen-roster-slot-" + (i + 1),
                creature != null ? creature.DisplayLabel : "Пусто",
                creature != null ? "заглушка из базы существ" : "место свободно",
                0,
                creature,
                false));
        }
    }

    private void RefreshHeroScreenRetinue()
    {
        heroScreenRetinueRow.Clear();
        string[] retinue = { "Квартирмейстер", "Разведчик", "Полевой лекарь" };
        for (int i = 0; i < retinue.Length; i++)
        {
            VisualElement holder = new VisualElement
            {
                name = "hero-screen-retinue-" + (i + 1)
            };
            holder.style.width = 92f;
            holder.style.alignItems = Align.Center;
            holder.style.marginRight = 6f;

            VisualElement circle = new VisualElement();
            circle.style.width = 62f;
            circle.style.height = 62f;
            circle.style.backgroundColor = HeroScreenPanelDeep;
            circle.style.borderTopLeftRadius = 31f;
            circle.style.borderTopRightRadius = 31f;
            circle.style.borderBottomLeftRadius = 31f;
            circle.style.borderBottomRightRadius = 31f;
            SetHeroScreenBorder(circle, 1f);
            holder.Add(circle);

            Label caption = new Label(retinue[i]);
            caption.style.color = HeroScreenMuted;
            caption.style.fontSize = 9f;
            caption.style.whiteSpace = WhiteSpace.Normal;
            caption.style.unityTextAlign = TextAnchor.MiddleCenter;
            caption.style.marginTop = 3f;
            holder.Add(caption);

            heroScreenRetinueRow.Add(holder);
        }
    }

    // ------------------------------------------------------------------
    // Карточка участника состава
    // ------------------------------------------------------------------

    private VisualElement CreateHeroScreenRosterCard(
        string name,
        string title,
        string role,
        int level,
        UnitDefinitionData unit,
        bool isCommander)
    {
        VisualElement card = new VisualElement { name = name };
        card.style.width = 168f;
        card.style.marginRight = 8f;
        card.style.paddingLeft = 6f;
        card.style.paddingRight = 6f;
        card.style.paddingTop = 6f;
        card.style.paddingBottom = 6f;
        card.style.backgroundColor = unit == null ? HeroScreenSlotEmpty : HeroScreenPanelDeep;
        SetHeroScreenBorder(card, isCommander ? 2f : 1f);

        VisualElement portrait = new VisualElement { name = name + "-portrait" };
        portrait.style.height = 96f;
        portrait.style.marginBottom = 5f;
        portrait.style.backgroundColor = HeroScreenSlotEmpty;
        ApplyHeroScreenPortrait(portrait, unit);
        card.Add(portrait);

        Label nameLabel = new Label(title) { name = name + "-name" };
        nameLabel.style.color = isCommander ? HeroScreenGold : HeroScreenText;
        nameLabel.style.fontSize = 12f;
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
        card.Add(nameLabel);

        Label roleLabel = new Label(role) { name = name + "-role" };
        roleLabel.style.color = HeroScreenMuted;
        roleLabel.style.fontSize = 9f;
        roleLabel.style.whiteSpace = WhiteSpace.NoWrap;
        card.Add(roleLabel);

        // Состояние читается прямо на карточке: HP, ранение, опыт.
        VisualElement statusRow = new VisualElement { name = name + "-status" };
        statusRow.style.flexDirection = FlexDirection.Row;
        statusRow.style.justifyContent = Justify.SpaceBetween;
        statusRow.style.marginTop = 5f;

        Label hp = new Label(unit != null ? "HP " + unit.MaxHitPoints : "HP —")
        {
            name = name + "-hp"
        };
        hp.style.color = HeroScreenText;
        hp.style.fontSize = 11f;
        hp.style.unityFontStyleAndWeight = FontStyle.Bold;
        statusRow.Add(hp);

        Label condition = new Label(unit != null ? "цел" : "—") { name = name + "-condition" };
        condition.style.color = HeroScreenMuted;
        condition.style.fontSize = 10f;
        statusRow.Add(condition);

        Label experience = new Label(level > 0 ? "ур. " + level : "—")
        {
            name = name + "-level"
        };
        experience.style.color = HeroScreenGold;
        experience.style.fontSize = 10f;
        statusRow.Add(experience);
        card.Add(statusRow);

        if (unit != null)
        {
            card.Add(CreateHeroScreenHealthBar(unit.MaxHitPoints, unit.MaxHitPoints, 5f));

            card.Add(CreateHeroScreenChip(
                HeroScreenRoleLabel(unit),
                HeroScreenBorder,
                "Ключевая роль в бою."));

            Label hint = CreateHeroScreenHint("ПКМ: сведения");
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(hint);

            // Как в BattleSandbox: правая кнопка открывает подробную
            // карточку бойца/существа из UnitDatabase.
            card.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1)
                    return;

                ShowHeroScreenUnitCard(unit);
                evt.StopPropagation();
            });
        }

        return card;
    }

    // ------------------------------------------------------------------
    // Подробная карточка существа/бойца
    // ------------------------------------------------------------------

    private void BuildHeroScreenUnitCard()
    {
        heroScreenUnitCard = new VisualElement { name = "hero-screen-unit-card" };
        heroScreenUnitCard.style.position = Position.Absolute;
        heroScreenUnitCard.style.left = Length.Percent(32f);
        heroScreenUnitCard.style.top = Length.Percent(18f);
        heroScreenUnitCard.style.width = 520f;
        heroScreenUnitCard.style.paddingLeft = 14f;
        heroScreenUnitCard.style.paddingRight = 14f;
        heroScreenUnitCard.style.paddingTop = 12f;
        heroScreenUnitCard.style.paddingBottom = 12f;
        heroScreenUnitCard.style.backgroundColor = HeroScreenPanel;
        heroScreenUnitCard.style.display = DisplayStyle.None;
        SetHeroScreenBorder(heroScreenUnitCard, 2f);
        heroScreenOverlay.Add(heroScreenUnitCard);
    }

    private void ShowHeroScreenUnitCard(UnitDefinitionData unit)
    {
        if (heroScreenUnitCard == null || unit == null)
            return;

        heroScreenUnitCard.Clear();

        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.alignItems = Align.Center;
        header.style.marginBottom = 8f;

        Label title = new Label(unit.DisplayLabel) { name = "hero-screen-unit-card-title" };
        title.style.color = HeroScreenGold;
        title.style.fontSize = 16f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.Add(title);

        Button close = new Button(HideHeroScreenUnitCard) { text = "×" };
        StyleHeroScreenButton(close, 34f, 26f);
        header.Add(close);
        heroScreenUnitCard.Add(header);

        VisualElement body = new VisualElement();
        body.style.flexDirection = FlexDirection.Row;

        VisualElement portrait = new VisualElement { name = "hero-screen-unit-card-portrait" };
        portrait.style.width = 150f;
        portrait.style.height = 150f;
        portrait.style.marginRight = 12f;
        portrait.style.backgroundColor = HeroScreenPanelDeep;
        SetHeroScreenBorder(portrait, 1f);
        ApplyHeroScreenPortrait(portrait, unit);
        body.Add(portrait);

        VisualElement stats = new VisualElement();
        stats.style.flexGrow = 1f;
        stats.Add(CreateHeroScreenHealthBar(unit.MaxHitPoints, unit.MaxHitPoints, 6f));
        AddHeroScreenUnitCardRow(
            stats, "Здоровье", unit.MaxHitPoints,
            "Текущий запас здоровья бойца. При 0 жизней боец выбывает из боя.");
        AddHeroScreenUnitCardRow(
            stats, "Атака", unit.Attack,
            "Сравнивается с Защитой цели и определяет множитель наносимого урона.");
        AddHeroScreenUnitCardRow(
            stats, "Защита", unit.Defense,
            "Сравнивается с Атакой противника и влияет на количество получаемого урона.");
        AddHeroScreenUnitCardRow(
            stats, "Урон", unit.Damage,
            "Базовое количество урона до применения результата сравнения Атаки и Защиты.");
        AddHeroScreenUnitCardRow(
            stats, "Движение", unit.Movement,
            "Запас движения бойца на активацию.");
        AddHeroScreenUnitCardRow(
            stats, "Инициатива", unit.Initiative,
            "Определяет порядок активации бойцов в начале каждого раунда.");
        AddHeroScreenUnitCardRow(
            stats, "Дальность атаки", unit.AttackRange,
            "Максимальное расстояние в гексах, с которого боец может атаковать цель.");
        body.Add(stats);
        heroScreenUnitCard.Add(body);

        VisualElement tags = CreateHeroScreenWrapRow("hero-screen-unit-card-tags");
        tags.style.marginTop = 10f;
        if (heroScreenUnits != null)
        {
            for (int i = 0; i < unit.TagIds.Count; i++)
            {
                UnitTagDefinition tag = heroScreenUnits.FindTag(unit.TagIds[i]);
                if (tag != null)
                    tags.Add(CreateHeroScreenChip(tag.DisplayLabel, tag.Color, tag.Description));
            }
        }

        heroScreenUnitCard.Add(tags);
        heroScreenUnitCard.style.display = DisplayStyle.Flex;
        heroScreenUnitCard.BringToFront();
    }

    private void HideHeroScreenUnitCard()
    {
        if (heroScreenUnitCard != null)
            heroScreenUnitCard.style.display = DisplayStyle.None;
    }

    private void AddHeroScreenUnitCardRow(VisualElement host, string label, int value, string description)
    {
        VisualElement row = new VisualElement { tooltip = description };
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginBottom = 2f;

        Label caption = new Label(label);
        caption.style.color = HeroScreenMuted;
        caption.style.fontSize = 11f;
        row.Add(caption);

        Label number = new Label(value.ToString());
        number.style.color = HeroScreenText;
        number.style.fontSize = 12f;
        number.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(number);

        host.Add(row);
    }

    // Как в BattleSandbox: узкая полоса, закрашенная пропорционально
    // текущему HP от максимума (цвет зависит от доли здоровья).
    private static VisualElement CreateHeroScreenHealthBar(int hitPoints, int maxHitPoints, float height)
    {
        VisualElement bar = new VisualElement();
        bar.style.height = height;
        bar.style.marginTop = 4f;
        bar.style.marginBottom = 4f;
        bar.style.backgroundColor = new Color(0.055f, 0.06f, 0.07f, 1f);
        SetHeroScreenRadius(bar, 2f);
        bar.pickingMode = PickingMode.Ignore;

        VisualElement fill = new VisualElement();
        fill.style.height = Length.Percent(100f);
        fill.style.width = Length.Percent(
            Mathf.Clamp01((float)hitPoints / Mathf.Max(1, maxHitPoints)) * 100f);
        fill.style.backgroundColor = GetHeroScreenHealthColor(hitPoints, maxHitPoints);
        SetHeroScreenRadius(fill, 2f);
        fill.pickingMode = PickingMode.Ignore;
        bar.Add(fill);
        return bar;
    }

    private static Color GetHeroScreenHealthColor(int hitPoints, int maxHitPoints)
    {
        float fraction = Mathf.Clamp01((float)hitPoints / Mathf.Max(1, maxHitPoints));
        if (fraction > 0.60f)
            return new Color(0.32f, 0.62f, 0.40f, 1f);
        if (fraction > 0.30f)
            return new Color(0.72f, 0.57f, 0.25f, 1f);
        return new Color(0.66f, 0.28f, 0.26f, 1f);
    }

    private static void SetHeroScreenRadius(VisualElement element, float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }

    // ------------------------------------------------------------------
    // Разрешение данных и мелкие помощники
    // ------------------------------------------------------------------

    /// <summary>
    /// `FighterData` прототипа не хранит боевых характеристик, поэтому боец
    /// сопоставляется с записью `UnitDatabase` по идентификатору, затем по
    /// названию роли.
    /// </summary>
    private UnitDefinitionData ResolveHeroScreenUnit(FighterData fighter)
    {
        if (heroScreenUnits == null || fighter == null)
            return null;

        UnitDefinitionData byId = heroScreenUnits.FindById(fighter.Id);
        if (byId != null)
            return byId;

        IReadOnlyList<UnitDefinitionData> units = heroScreenUnits.Units;
        for (int i = 0; i < units.Count; i++)
        {
            UnitDefinitionData unit = units[i];
            if (unit != null &&
                unit.Category == UnitCategory.Fighter &&
                string.Equals(unit.DisplayLabel, fighter.Role, System.StringComparison.OrdinalIgnoreCase))
                return unit;
        }

        return null;
    }

    private List<FighterData> GetHeroScreenParty()
    {
        List<FighterData> party = new List<FighterData>();
        if (gameState == null || gameState.Fighters == null)
            return party;

        for (int i = 0; i < gameState.Fighters.Count && party.Count < HeroScreenFighterSlots; i++)
        {
            FighterData fighter = gameState.Fighters[i];
            if (fighter != null)
                party.Add(fighter);
        }

        return party;
    }

    private List<UnitDefinitionData> GetHeroScreenCreatures()
    {
        List<UnitDefinitionData> creatures = new List<UnitDefinitionData>();
        if (heroScreenUnits == null)
            return creatures;

        IReadOnlyList<UnitDefinitionData> units = heroScreenUnits.Units;
        for (int i = 0; i < units.Count; i++)
        {
            UnitDefinitionData unit = units[i];
            if (unit != null && unit.Category == UnitCategory.Creature)
                creatures.Add(unit);
        }

        return creatures;
    }

    private static void ApplyHeroScreenPortrait(VisualElement target, UnitDefinitionData unit)
    {
        if (target == null)
            return;

        if (unit == null || unit.Portrait == null)
        {
            target.style.backgroundImage = StyleKeyword.None;
            return;
        }

        target.style.backgroundImage = new StyleBackground(unit.Portrait);
        target.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
        target.style.scale = new Scale(new Vector3(unit.PortraitScale, unit.PortraitScale, 1f));
        target.style.translate = new Translate(unit.PortraitOffset.x, unit.PortraitOffset.y);
    }

    private static string HeroScreenRoleLabel(UnitDefinitionData unit)
    {
        switch (unit.CombatRole)
        {
            case UnitCombatRole.Guard: return "Защитник";
            case UnitCombatRole.Archer: return "Стрелок";
            case UnitCombatRole.Healer: return "Лекарь";
            case UnitCombatRole.Spearman: return "Копейщик";
            case UnitCombatRole.Scout: return "Разведчик";
            case UnitCombatRole.Militia: return "Ополчение";
            case UnitCombatRole.Creature: return "Существо";
            default: return "Особая роль";
        }
    }

    private static VisualElement CreateHeroScreenPanel(string name, string title)
    {
        VisualElement panel = new VisualElement { name = name };
        panel.style.backgroundColor = HeroScreenPanel;
        panel.style.paddingLeft = 10f;
        panel.style.paddingRight = 10f;
        panel.style.paddingTop = 8f;
        panel.style.paddingBottom = 8f;
        panel.style.marginBottom = 10f;
        SetHeroScreenBorder(panel, 1f);

        Label caption = new Label(title) { name = name + "-title" };
        caption.style.color = HeroScreenGold;
        caption.style.fontSize = 11f;
        caption.style.unityFontStyleAndWeight = FontStyle.Bold;
        caption.style.marginBottom = 6f;
        panel.Add(caption);
        return panel;
    }

    private static VisualElement CreateHeroScreenWrapRow(string name)
    {
        VisualElement row = new VisualElement { name = name };
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        row.style.alignItems = Align.Center;
        return row;
    }

    private static VisualElement CreateHeroScreenChip(string text, Color color, string tooltip)
    {
        Label chip = new Label(text);
        chip.tooltip = tooltip;
        chip.style.marginRight = 5f;
        chip.style.marginTop = 3f;
        chip.style.paddingLeft = 7f;
        chip.style.paddingRight = 7f;
        chip.style.paddingTop = 2f;
        chip.style.paddingBottom = 2f;
        chip.style.fontSize = 10f;
        chip.style.color = HeroScreenText;
        Color background = color;
        background.a = 0.42f;
        chip.style.backgroundColor = background;
        chip.style.borderTopLeftRadius = 3f;
        chip.style.borderTopRightRadius = 3f;
        chip.style.borderBottomLeftRadius = 3f;
        chip.style.borderBottomRightRadius = 3f;
        return chip;
    }

    private static Label CreateHeroScreenHint(string text)
    {
        Label hint = new Label(text);
        hint.style.color = HeroScreenMuted;
        hint.style.fontSize = 9f;
        hint.style.marginTop = 4f;
        hint.style.whiteSpace = WhiteSpace.Normal;
        return hint;
    }

    private static void StyleHeroScreenButton(Button button, float width, float height)
    {
        button.style.width = width;
        button.style.height = height;
        button.style.color = HeroScreenText;
        button.style.fontSize = 11f;
        button.style.backgroundColor = HeroScreenPanelDeep;
        SetHeroScreenBorder(button, 1f);
    }

    private static void SetHeroScreenBorder(VisualElement element, float width)
    {
        element.style.borderLeftWidth = width;
        element.style.borderRightWidth = width;
        element.style.borderTopWidth = width;
        element.style.borderBottomWidth = width;
        element.style.borderLeftColor = HeroScreenBorder;
        element.style.borderRightColor = HeroScreenBorder;
        element.style.borderTopColor = HeroScreenBorder;
        element.style.borderBottomColor = HeroScreenBorder;
    }

    private static string HeroScreenSlug(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "unknown";
        return value.ToLowerInvariant().Replace(' ', '-');
    }
}
