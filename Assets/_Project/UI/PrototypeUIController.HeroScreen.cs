using System.Collections.Generic;
using KingdomSurvival.Chapter01;
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
    private VisualElement heroScreenRosterAvailableRow;
    private Label heroScreenRosterSelectedLabel;
    private Button heroScreenRosterConfirmButton;
    private VisualElement heroScreenRetinueRow;
    private VisualElement heroScreenTagsRow;
    private VisualElement heroScreenStatsGrid;
    private VisualElement heroScreenQualitiesGrid;
    private VisualElement heroScreenCompetenciesRow;
    private VisualElement heroScreenTraitsRow;
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
    private Label heroScreenArmyGoldLabel;
    private Button heroScreenArmyGoldMinusButton;
    private Button heroScreenArmyGoldPlusButton;
    private Label heroScreenSupplyValueLabel;
    private Button heroScreenSupplyMinusButton;
    private Button heroScreenSupplyPlusButton;
    private Label heroScreenSupplyConsumptionLabel;
    private Label heroScreenSupplyDaysLabel;

    private VisualElement heroScreenUnitCard;
    private VisualElement heroScreenUnitCardDimmer;
    private VisualElement heroScreenStatTooltip;
    private Label heroScreenStatTooltipTitle;
    private Label heroScreenStatTooltipText;
    private VisualElement heroScreenTagTooltip;
    private Label heroScreenTagTooltipTitle;
    private Label heroScreenTagTooltipText;
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

        // P08J: Journal и Hero Screen — два независимых fullscreen-слоя,
        // одновременно открытыми быть не должны (раздел 20 инструкции P08J).
        // P09-T05: Camp — третий такой слой, то же правило.
        CloseJournal();
        CloseCampScreen();

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
        heroScreenOverlay.Add(BuildHeroScreenJourneySummaryPanel());
        BuildHeroScreenUnitCard();
    }

    // Перенесено с удалённого экрана «Армия»: тот же журнал происшествий и
    // решений похода (PrototypeUIController.JourneySummary.cs), только его
    // контейнеры («journey-summary-block/-scroll/-list») теперь строятся
    // здесь — JourneySummary.cs находит их по тем же именам через Q<>().
    private VisualElement BuildHeroScreenJourneySummaryPanel()
    {
        VisualElement panel = CreateHeroScreenPanel("journey-summary-block", "СВОДКА ПОХОДА");
        panel.style.marginTop = 10f;
        panel.style.marginBottom = 0f;
        panel.style.flexShrink = 0f;
        panel.style.height = 150f;

        ScrollView scroll = new ScrollView { name = "journey-summary-scroll" };
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 0f;

        VisualElement list = new VisualElement { name = "journey-summary-list" };
        list.style.width = Length.Percent(100f);
        scroll.Add(list);

        panel.Add(scroll);
        return panel;
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

        ScrollView scroll = CreateHeroScreenColumnScroll("hero-screen-left-column-scroll");
        column.Add(scroll);
        VisualElement content = scroll.contentContainer;

        VisualElement identity = CreateHeroScreenPanel("hero-screen-identity", "ГЕРОЙ");

        heroScreenPortrait = new UnitPortraitElement { name = "hero-screen-portrait" };
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
        content.Add(identity);

        VisualElement states = CreateHeroScreenPanel("hero-screen-states", "СОСТОЯНИЯ");
        heroScreenStatesRow = CreateHeroScreenWrapRow("hero-screen-states-row");
        states.Add(heroScreenStatesRow);
        content.Add(states);

        content.Add(BuildHeroScreenSupplyPanel());

        return column;
    }

    // Перенесено с удалённого экрана «Армия»: то же снабжение похода,
    // только теперь оно живёт на экране героя.
    private VisualElement BuildHeroScreenSupplyPanel()
    {
        VisualElement panel = CreateHeroScreenPanel("hero-screen-supply", "ЗАПАСЫ ОТРЯДА");

        Label goldSubtitle = new Label("ЗОЛОТО");
        goldSubtitle.style.color = HeroScreenMuted;
        goldSubtitle.style.fontSize = 9f;
        goldSubtitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        goldSubtitle.style.marginBottom = 3f;
        panel.Add(goldSubtitle);

        VisualElement goldRow = new VisualElement();
        goldRow.style.flexDirection = FlexDirection.Row;
        goldRow.style.alignItems = Align.Center;
        goldRow.style.marginBottom = 6f;

        heroScreenArmyGoldMinusButton = new Button(OnStableArmyGoldMinusClicked) { text = "−" };
        StyleHeroScreenButton(heroScreenArmyGoldMinusButton, 30f, 26f);
        goldRow.Add(heroScreenArmyGoldMinusButton);

        heroScreenArmyGoldLabel = new Label("0");
        heroScreenArmyGoldLabel.style.color = HeroScreenText;
        heroScreenArmyGoldLabel.style.fontSize = 15f;
        heroScreenArmyGoldLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        heroScreenArmyGoldLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        heroScreenArmyGoldLabel.style.flexGrow = 1f;
        goldRow.Add(heroScreenArmyGoldLabel);

        heroScreenArmyGoldPlusButton = new Button(OnStableArmyGoldPlusClicked) { text = "+" };
        StyleHeroScreenButton(heroScreenArmyGoldPlusButton, 30f, 26f);
        goldRow.Add(heroScreenArmyGoldPlusButton);
        panel.Add(goldRow);

        Label supplySubtitle = new Label("СНАБЖЕНИЕ");
        supplySubtitle.style.color = HeroScreenMuted;
        supplySubtitle.style.fontSize = 9f;
        supplySubtitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        supplySubtitle.style.marginBottom = 3f;
        panel.Add(supplySubtitle);

        VisualElement supplyRow = new VisualElement();
        supplyRow.style.flexDirection = FlexDirection.Row;
        supplyRow.style.alignItems = Align.Center;
        supplyRow.style.marginBottom = 4f;

        heroScreenSupplyMinusButton = new Button(OnStableSupplyMinusClicked) { text = "−" };
        StyleHeroScreenButton(heroScreenSupplyMinusButton, 30f, 26f);
        supplyRow.Add(heroScreenSupplyMinusButton);

        heroScreenSupplyValueLabel = new Label("0");
        heroScreenSupplyValueLabel.style.color = HeroScreenText;
        heroScreenSupplyValueLabel.style.fontSize = 15f;
        heroScreenSupplyValueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        heroScreenSupplyValueLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        heroScreenSupplyValueLabel.style.flexGrow = 1f;
        supplyRow.Add(heroScreenSupplyValueLabel);

        heroScreenSupplyPlusButton = new Button(OnStableSupplyPlusClicked) { text = "+" };
        StyleHeroScreenButton(heroScreenSupplyPlusButton, 30f, 26f);
        supplyRow.Add(heroScreenSupplyPlusButton);
        panel.Add(supplyRow);

        heroScreenSupplyConsumptionLabel = CreateHeroScreenHint("Расход: —");
        panel.Add(heroScreenSupplyConsumptionLabel);
        heroScreenSupplyDaysLabel = CreateHeroScreenHint("Хватит на: —");
        panel.Add(heroScreenSupplyDaysLabel);

        return panel;
    }

    // Как в старом RefreshSupplyBlock/ApplyCompactSupplyText экрана «Армия»,
    // но нацелено на новую панель здесь, на экране героя.
    private void RefreshHeroScreenSupplyPanel()
    {
        if (heroScreenArmyGoldLabel == null || gameState == null)
            return;

        int dailyConsumption = gameState.HasActiveExpedition
            ? gameState.ExpeditionSupplyConsumption
            : selectedFighterIds.Count > 0
                ? selectedFighterIds.Count + 1
                : 1;
        int fullDays = dailyConsumption > 0
            ? gameState.ArmySupply / dailyConsumption
            : 0;
        bool canAdjust = gameState.CanAdjustArmySupply && !isGameOver;

        heroScreenArmyGoldLabel.text = gameState.ArmyGold.ToString();
        heroScreenSupplyValueLabel.text = gameState.ArmySupply.ToString();
        heroScreenSupplyConsumptionLabel.text =
            "Расход: " + dailyConsumption + " / день";
        heroScreenSupplyDaysLabel.text =
            "Хватит на " + fullDays + " " + GetDayWord(fullDays);

        heroScreenArmyGoldPlusButton.SetEnabled(canAdjust && gameState.Gold > 0);
        heroScreenArmyGoldMinusButton.SetEnabled(canAdjust && gameState.ArmyGold > 0);
        heroScreenSupplyPlusButton.SetEnabled(canAdjust && gameState.Food > 0);
        heroScreenSupplyMinusButton.SetEnabled(canAdjust && gameState.ArmySupply > 0);
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

        ScrollView scroll = CreateHeroScreenColumnScroll("hero-screen-center-column-scroll");
        column.Add(scroll);
        VisualElement content = scroll.contentContainer;

        // Качества, компетенции и боевые характеристики визуально не
        // смешиваются (§17 производственной инструкции по качествам и
        // проверкам) — три отдельные панели вместо одной общей сетки.
        VisualElement qualities = CreateHeroScreenPanel("hero-screen-qualities", "КАЧЕСТВА");
        heroScreenQualitiesGrid = new VisualElement { name = "hero-screen-qualities-grid" };
        heroScreenQualitiesGrid.style.flexDirection = FlexDirection.Row;
        heroScreenQualitiesGrid.style.flexWrap = Wrap.Wrap;
        qualities.Add(heroScreenQualitiesGrid);
        content.Add(qualities);

        VisualElement competencies = CreateHeroScreenPanel("hero-screen-competencies", "КОМПЕТЕНЦИИ");
        heroScreenCompetenciesRow = new VisualElement { name = "hero-screen-competencies-row" };
        competencies.Add(heroScreenCompetenciesRow);
        content.Add(competencies);

        VisualElement traits = CreateHeroScreenPanel("hero-screen-traits", "ОСОБЕННОСТИ");
        heroScreenTraitsRow = CreateHeroScreenWrapRow("hero-screen-traits-row");
        traits.Add(heroScreenTraitsRow);
        content.Add(traits);

        VisualElement stats = CreateHeroScreenPanel("hero-screen-stats", "БОЕВЫЕ ХАРАКТЕРИСТИКИ");
        heroScreenStatsGrid = new VisualElement { name = "hero-screen-stats-grid" };
        heroScreenStatsGrid.style.flexDirection = FlexDirection.Row;
        heroScreenStatsGrid.style.flexWrap = Wrap.Wrap;
        stats.Add(heroScreenStatsGrid);
        content.Add(stats);

        VisualElement tags = CreateHeroScreenPanel("hero-screen-tags", "ТЕГИ");
        heroScreenTagsRow = CreateHeroScreenWrapRow("hero-screen-tags-row");
        tags.Add(heroScreenTagsRow);
        content.Add(tags);

        VisualElement abilities = CreateHeroScreenPanel("hero-screen-abilities", "СПОСОБНОСТИ");
        heroScreenAbilitiesRow = CreateHeroScreenWrapRow("hero-screen-abilities-row");
        abilities.Add(heroScreenAbilitiesRow);
        content.Add(abilities);

        return column;
    }

    private VisualElement BuildHeroScreenRightColumn()
    {
        VisualElement column = new VisualElement { name = "hero-screen-right-column" };
        column.style.width = new Length(26f, LengthUnit.Percent);
        column.style.minWidth = 0f;

        ScrollView scroll = CreateHeroScreenColumnScroll("hero-screen-right-column-scroll");
        column.Add(scroll);
        VisualElement content = scroll.contentContainer;

        VisualElement equipment = CreateHeroScreenPanel("hero-screen-equipment", "СНАРЯЖЕНИЕ");
        heroScreenEquipmentGrid = new VisualElement { name = "hero-screen-equipment-grid" };
        heroScreenEquipmentGrid.style.flexDirection = FlexDirection.Row;
        heroScreenEquipmentGrid.style.flexWrap = Wrap.Wrap;
        equipment.Add(heroScreenEquipmentGrid);
        content.Add(equipment);

        VisualElement inventory = CreateHeroScreenPanel("hero-screen-inventory", "ИНВЕНТАРЬ");
        inventory.style.flexGrow = 1f;
        heroScreenInventoryGrid = new VisualElement { name = "hero-screen-inventory-grid" };
        heroScreenInventoryGrid.style.flexDirection = FlexDirection.Row;
        heroScreenInventoryGrid.style.flexWrap = Wrap.Wrap;
        inventory.Add(heroScreenInventoryGrid);
        content.Add(inventory);

        return column;
    }

    private static ScrollView CreateHeroScreenColumnScroll(string name)
    {
        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical) { name = name };
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 0f;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        return scroll;
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

        // P08-T03: реальный picker — "ДОСТУПНЫ В ДОМЕ" читает gameState.Fighters
        // напрямую (не фиксированный список имён), клик добавляет/убирает бойца
        // из selectedFighterIds. "ПОДТВЕРДИТЬ СОСТАВ" виден только когда сюжет
        // Главы 01 реально ждёт сбора отряда (N09 решение принято, поход ещё
        // не создан) — иначе состав уходит на карту обычным кликом по цели.
        Label availableLabel = new Label("ДОСТУПНЫ В ДОМЕ");
        availableLabel.style.color = HeroScreenMuted;
        availableLabel.style.fontSize = 9f;
        availableLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        availableLabel.style.marginTop = 8f;
        availableLabel.style.marginBottom = 4f;
        roster.Add(availableLabel);

        heroScreenRosterAvailableRow = new VisualElement { name = "hero-screen-roster-available-row" };
        heroScreenRosterAvailableRow.style.flexDirection = FlexDirection.Row;
        heroScreenRosterAvailableRow.style.flexWrap = Wrap.Wrap;
        roster.Add(heroScreenRosterAvailableRow);

        VisualElement confirmRow = new VisualElement();
        confirmRow.style.flexDirection = FlexDirection.Row;
        confirmRow.style.alignItems = Align.Center;
        confirmRow.style.justifyContent = Justify.SpaceBetween;
        confirmRow.style.marginTop = 8f;

        heroScreenRosterSelectedLabel = new Label("Выбрано: 0 / " + HeroScreenFighterSlots);
        heroScreenRosterSelectedLabel.style.color = HeroScreenMuted;
        heroScreenRosterSelectedLabel.style.fontSize = 10f;
        confirmRow.Add(heroScreenRosterSelectedLabel);

        heroScreenRosterConfirmButton = new Button(OnHeroScreenRosterConfirmClicked)
        {
            name = "hero-screen-roster-confirm-button",
            text = "ПОДТВЕРДИТЬ СОСТАВ"
        };
        StyleHeroScreenButton(heroScreenRosterConfirmButton, 190f, 30f);
        confirmRow.Add(heroScreenRosterConfirmButton);
        roster.Add(confirmRow);

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

        HeroProfileData heroProfile = commander != null ? commander.HeroProfile : null;
        RefreshHeroScreenQualities(heroProfile);
        RefreshHeroScreenCompetencies(heroProfile);
        RefreshHeroScreenTraits(heroProfile);
        RefreshHeroScreenStats(heroUnit, heroProfile);
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

    // Шесть качеств героя 1-10 (§2 инструкции по качествам и проверкам) —
    // отдельная панель, не связанная с боевыми характеристиками UnitDatabase.
    private void RefreshHeroScreenQualities(HeroProfileData hero)
    {
        heroScreenQualitiesGrid.Clear();
        AddHeroScreenQuality(HeroQuality.Strength, "Сила", "Физическое воздействие.", hero);
        AddHeroScreenQuality(HeroQuality.Dexterity, "Сноровка", "Координация, точность и скорость.", hero);
        AddHeroScreenQuality(HeroQuality.Fortitude, "Стойкость", "Здоровье и физические лишения.", hero);
        AddHeroScreenQuality(HeroQuality.Instinct, "Чутьё", "Наблюдение, следы и опасность.", hero);
        AddHeroScreenQuality(HeroQuality.Judgment, "Суждение", "Анализ, планирование и интерпретация.", hero);
        AddHeroScreenQuality(HeroQuality.Character, "Характер", "Сила личности, влияние и сопротивление давлению.", hero);
    }

    private void AddHeroScreenQuality(HeroQuality quality, string label, string meaning, HeroProfileData hero)
    {
        int value = hero != null ? hero.GetQuality(quality) : HeroProfileData.DefaultQualityValue;
        string explanation =
            label + " " + value + " из 10 (" + GetHeroScreenQualityRangeLabel(value) + "). " + meaning;

        VisualElement box = new VisualElement { name = "hero-screen-quality-" + HeroScreenSlug(label) };
        box.style.width = new Length(33f, LengthUnit.Percent);
        box.style.paddingLeft = 8f;
        box.style.paddingRight = 8f;
        box.style.paddingTop = 6f;
        box.style.paddingBottom = 6f;
        box.style.marginBottom = 4f;

        Label caption = new Label(label);
        caption.style.color = HeroScreenMuted;
        caption.style.fontSize = 10f;
        caption.pickingMode = PickingMode.Ignore;
        box.Add(caption);

        Label number = new Label(value + " / " + HeroProfileData.MaxQualityValue);
        number.style.color = HeroScreenText;
        number.style.fontSize = 18f;
        number.style.unityFontStyleAndWeight = FontStyle.Bold;
        number.pickingMode = PickingMode.Ignore;
        box.Add(number);

        box.RegisterCallback<PointerEnterEvent>(_ => ShowHeroScreenStatTooltip(box, label.ToUpperInvariant(), explanation));
        box.RegisterCallback<PointerLeaveEvent>(_ => HideHeroScreenStatTooltip());

        heroScreenQualitiesGrid.Add(box);
    }

    // Диапазоны интерпретации из §2 инструкции.
    private static string GetHeroScreenQualityRangeLabel(int value)
    {
        if (value <= 2) return "серьёзная слабость";
        if (value <= 4) return "ниже среднего";
        if (value <= 6) return "нормальное развитие";
        if (value <= 8) return "выраженный талант";
        return "исключительное качество";
    }

    // Следопытство — первая полностью реализованная компетенция (§3).
    // Архитектура допускает другие компетенции позже без переделки экрана.
    private void RefreshHeroScreenCompetencies(HeroProfileData hero)
    {
        heroScreenCompetenciesRow.Clear();
        int fieldcraft = hero != null ? hero.GetCompetency(NarrativeCompetencyIds.Fieldcraft) : 0;
        CreateHeroScreenStatRow(
            heroScreenCompetenciesRow,
            NarrativeCompetencyLabels.GetLabel(NarrativeCompetencyIds.Fieldcraft).ToUpperInvariant(),
            fieldcraft + " / 5",
            "Чтение следов, разведка, поиск скрытых мест, выбор маршрута, устройство лагеря, обнаружение засад и подготовка к дорожным встречам.");
    }

    // Особенности героя (§4): стабильные строковые ID, показываются как
    // теги с подсказкой — так же, как боевые теги существ из UnitDatabase.
    private void RefreshHeroScreenTraits(HeroProfileData hero)
    {
        heroScreenTraitsRow.Clear();

        if (hero != null && hero.HasTrait(NarrativeTraitIds.KnowsTheWay))
        {
            heroScreenTraitsRow.Add(CreateHeroScreenChip(
                "Знающий дорогу",
                HeroScreenGold,
                "При успешном обнаружении дорожный Encounter начинается в подготовленном состоянии: герой замечает событие раньше, может наблюдать, обойти или занять выгодную позицию."));
        }

        if (hero != null && hero.HasTrait(NarrativeTraitIds.Naturalist))
        {
            heroScreenTraitsRow.Add(CreateHeroScreenChip(
                "Натуралист",
                HeroScreenGold,
                "Открывает авторские блоки и варианты, связанные с растениями, животными, погодой, болезнями, водой и природными изменениями."));
        }

        if (heroScreenTraitsRow.childCount == 0)
            heroScreenTraitsRow.Add(CreateHeroScreenHint("У героя пока нет особенностей."));
    }

    private void RefreshHeroScreenStats(UnitDefinitionData unit, HeroProfileData hero)
    {
        heroScreenStatsGrid.Clear();
        AddHeroScreenStat("Здоровье", unit != null ? unit.MaxHitPoints : 0);
        AddHeroScreenStat("Атака", unit != null ? unit.Attack : 0);
        AddHeroScreenStat("Защита", unit != null ? unit.Defense : 0);
        AddHeroScreenStat("Урон", unit != null ? unit.Damage : 0);
        AddHeroScreenStat("Движение", unit != null ? unit.Movement : 0);
        AddHeroScreenInitiativeStat(unit, hero);
        AddHeroScreenStat("Дальность атаки", unit != null ? unit.AttackRange : 0);
    }

    // Единственная боевая характеристика командира, куда сейчас подключено
    // качество (§16: Сноровка -> Инициатива). Подсказка показывает
    // происхождение производного значения, как того требует §17.
    private void AddHeroScreenInitiativeStat(UnitDefinitionData unit, HeroProfileData hero)
    {
        int baseInitiative = unit != null ? unit.Initiative : 0;
        int dexterity = hero != null ? hero.GetQuality(HeroQuality.Dexterity) : HeroProfileData.DefaultQualityValue;
        int modifier = HeroCombatStatsBuilder.GetCombatModifier(dexterity);
        int finalInitiative = Mathf.Max(0, baseInitiative + modifier);

        string explanation = "База: " + baseInitiative;
        if (modifier != 0)
            explanation += "\nСноровка " + dexterity + ": " + (modifier > 0 ? "+" + modifier : modifier.ToString());

        VisualElement box = new VisualElement { name = "hero-screen-stat-initiative" };
        box.style.width = new Length(33f, LengthUnit.Percent);
        box.style.paddingLeft = 8f;
        box.style.paddingRight = 8f;
        box.style.paddingTop = 6f;
        box.style.paddingBottom = 6f;
        box.style.marginBottom = 4f;

        Label caption = new Label("Инициатива");
        caption.style.color = HeroScreenMuted;
        caption.style.fontSize = 10f;
        caption.pickingMode = PickingMode.Ignore;
        box.Add(caption);

        Label number = new Label(finalInitiative.ToString());
        number.style.color = HeroScreenText;
        number.style.fontSize = 18f;
        number.style.unityFontStyleAndWeight = FontStyle.Bold;
        number.pickingMode = PickingMode.Ignore;
        box.Add(number);

        box.RegisterCallback<PointerEnterEvent>(_ => ShowHeroScreenStatTooltip(box, "ИНИЦИАТИВА", explanation));
        box.RegisterCallback<PointerLeaveEvent>(_ => HideHeroScreenStatTooltip());

        heroScreenStatsGrid.Add(box);
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

    // P08-T03: реальный picker состава похода вместо презентации. Источник
    // истины — GameState.Fighters + selectedFighterIds (тот же набор, что
    // уже использует общий поток "выбрать бойцов -> кликнуть цель на
    // карте"), а во время активного похода — ActiveExpedition.FighterIds.
    // Никаких зашитых имён: список полностью пересобирается из состояния.
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

        bool expeditionActive = gameState.HasActiveExpedition;
        List<FighterData> slotFighters = new List<FighterData>();
        if (expeditionActive)
        {
            foreach (string fighterId in gameState.ActiveExpedition.FighterIds)
            {
                FighterData fighter = gameState.FindFighter(fighterId);
                if (fighter != null)
                    slotFighters.Add(fighter);
            }
        }
        else
        {
            foreach (FighterData fighter in gameState.Fighters)
            {
                if (selectedFighterIds.Contains(fighter.Id))
                    slotFighters.Add(fighter);
            }
        }

        for (int i = 0; i < HeroScreenFighterSlots; i++)
        {
            if (i < slotFighters.Count)
            {
                FighterData fighter = slotFighters[i];
                VisualElement card = CreateHeroScreenRosterCard(
                    "hero-screen-roster-fighter-" + (i + 1),
                    fighter.Name,
                    fighter.Role,
                    fighter.Level,
                    ResolveHeroScreenUnit(fighter),
                    false);

                if (!expeditionActive)
                    RegisterHeroScreenRosterSlotToggle(card, fighter.Id);

                heroScreenRosterRow.Add(card);
                continue;
            }

            heroScreenRosterRow.Add(CreateHeroScreenRosterCard(
                "hero-screen-roster-slot-" + (i + 1),
                "Пусто",
                expeditionActive ? "герой ушёл без него" : "место свободно",
                0,
                null,
                false));
        }

        RefreshHeroScreenRosterAvailable(slotFighters, expeditionActive);
    }

    private void RefreshHeroScreenRosterAvailable(List<FighterData> slotFighters, bool expeditionActive)
    {
        heroScreenRosterAvailableRow.Clear();

        bool canPick = !expeditionActive && !isGameOver;
        bool full = slotFighters.Count >= HeroScreenFighterSlots;

        if (!expeditionActive)
        {
            int shown = 0;
            foreach (FighterData fighter in gameState.Fighters)
            {
                if (selectedFighterIds.Contains(fighter.Id))
                    continue;

                heroScreenRosterAvailableRow.Add(CreateHeroScreenAvailableFighterChip(fighter, canPick && !full));
                shown++;
            }

            if (shown == 0)
                heroScreenRosterAvailableRow.Add(CreateHeroScreenHint("Все бойцы уже в составе."));
        }
        else
        {
            heroScreenRosterAvailableRow.Add(CreateHeroScreenHint("Отряд уже в походе."));
        }

        heroScreenRosterSelectedLabel.text = "Выбрано: " + slotFighters.Count + " / " + HeroScreenFighterSlots;

        // Кнопка нужна только пока сюжет Главы 01 реально ждёт сбора отряда
        // (N09 решение принято, но экспедиция ещё не создана) — обычный
        // поход по-прежнему отправляется кликом по цели на карте.
        bool chapter01AwaitingDeparture =
            !expeditionActive &&
            gameState.Narrative != null &&
            gameState.Narrative.HasFlag(Chapter01Ids.Flags.FarRouteUnlocked) &&
            !gameState.Narrative.HasFlag(Chapter01Ids.Flags.ExpeditionStarted);

        heroScreenRosterConfirmButton.style.display =
            chapter01AwaitingDeparture ? DisplayStyle.Flex : DisplayStyle.None;
        heroScreenRosterConfirmButton.SetEnabled(chapter01AwaitingDeparture && !isGameOver);
    }

    private void RegisterHeroScreenRosterSlotToggle(VisualElement card, string fighterId)
    {
        card.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || isGameOver)
                return;

            selectedFighterIds.Remove(fighterId);
            RefreshHeroScreen();
            RefreshStableUiAfterStateChange();
            evt.StopPropagation();
        });
    }

    private VisualElement CreateHeroScreenAvailableFighterChip(FighterData fighter, bool canAdd)
    {
        VisualElement chip = new VisualElement { name = "hero-screen-roster-available-" + fighter.Id };
        chip.style.flexDirection = FlexDirection.Row;
        chip.style.alignItems = Align.Center;
        chip.style.paddingLeft = 8f;
        chip.style.paddingRight = 8f;
        chip.style.paddingTop = 4f;
        chip.style.paddingBottom = 4f;
        chip.style.marginRight = 6f;
        chip.style.marginBottom = 6f;
        chip.style.backgroundColor = HeroScreenPanelDeep;
        SetHeroScreenBorder(chip, canAdd ? HeroScreenBorder : new Color(0.24f, 0.24f, 0.24f, 1f), 1f);
        SetHeroScreenRadius(chip, 3f);

        Label label = new Label(fighter.Name + " — " + fighter.Role);
        label.style.fontSize = 10f;
        label.style.color = canAdd ? HeroScreenText : HeroScreenMuted;
        label.pickingMode = PickingMode.Ignore;
        chip.Add(label);

        if (canAdd)
        {
            chip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || isGameOver)
                    return;

                if (selectedFighterIds.Count < HeroScreenFighterSlots)
                    selectedFighterIds.Add(fighter.Id);

                RefreshHeroScreen();
                RefreshStableUiAfterStateChange();
                evt.StopPropagation();
            });
        }

        return chip;
    }

    // Карта = единственный запуск похода (раздел 2/3/4 инструкции про карту
    // и время): подтверждение состава здесь только фиксирует выбор игрока
    // и не создаёт ActiveExpedition само по себе — герой остаётся у Дома,
    // стратегическое время остаётся на месте (RefreshAutoTimeState читает
    // HasActiveExpedition/Phase, которых тут ещё нет). selectedFighterIds
    // намеренно не очищается: тот же набор читает клик по карте
    // (GetSelectedFighterIdsInArmyOrder в IssueContinuousMapOrder), который
    // и создаёт настоящую экспедицию. ExpeditionStarted ставится не отсюда,
    // а в PrototypeUIController.ContinuousTime.cs по факту реального
    // движения — раньше этот флаг ошибочно стоял здесь, сразу по клику.
    private void OnHeroScreenRosterConfirmClicked()
    {
        if (isGameOver || gameState == null)
            return;

        List<string> selected = GetSelectedFighterIdsInArmyOrder();
        AddReport(
            "Состав похода подтверждён: " +
            (selected.Count > 0 ? "командир и " + GetFighterNames(selected) : "командир один") +
            ". Выберите цель на карте — отряд начнёт движение сразу после выбора.");

        CloseHeroScreen();
        RefreshStableUiAfterStateChange();
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

        VisualElement portrait = new UnitPortraitElement { name = name + "-portrait" };
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
    // Подробная карточка существа/бойца — по образцу SandboxFighterDetailsView
    // из BattleSandbox: модальное окно с затемнением, боксы характеристик и
    // теги с настоящими всплывающими подсказками по наведению.
    //
    // UI Toolkit в Player (не в редакторе) НЕ показывает встроенный
    // VisualElement.tooltip — поэтому здесь, как и в BattleSandbox,
    // подсказки рисуются вручную отдельными плавающими панелями.
    // ------------------------------------------------------------------

    private const float HeroScreenUnitCardWidth = 650f;
    private const float HeroScreenUnitCardHeight = 500f;

    private void BuildHeroScreenUnitCard()
    {
        heroScreenUnitCardDimmer = new VisualElement
        {
            name = "hero-screen-unit-card-dimmer",
            focusable = true
        };
        heroScreenUnitCardDimmer.style.position = Position.Absolute;
        heroScreenUnitCardDimmer.style.left = 0f;
        heroScreenUnitCardDimmer.style.right = 0f;
        heroScreenUnitCardDimmer.style.top = 0f;
        heroScreenUnitCardDimmer.style.bottom = 0f;
        heroScreenUnitCardDimmer.style.backgroundColor = new Color(0.01f, 0.015f, 0.02f, 0.74f);
        heroScreenUnitCardDimmer.style.display = DisplayStyle.None;
        heroScreenUnitCardDimmer.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || evt.target != heroScreenUnitCardDimmer)
                return;
            HideHeroScreenUnitCard();
            evt.StopPropagation();
        });
        heroScreenUnitCardDimmer.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape)
                return;
            HideHeroScreenUnitCard();
            evt.StopPropagation();
        });
        heroScreenOverlay.Add(heroScreenUnitCardDimmer);

        heroScreenUnitCard = new VisualElement { name = "hero-screen-unit-card" };
        heroScreenUnitCard.style.position = Position.Absolute;
        heroScreenUnitCard.style.left = Length.Percent(50f);
        heroScreenUnitCard.style.top = Length.Percent(50f);
        heroScreenUnitCard.style.marginLeft = -HeroScreenUnitCardWidth / 2f;
        heroScreenUnitCard.style.marginTop = -HeroScreenUnitCardHeight / 2f;
        heroScreenUnitCard.style.width = HeroScreenUnitCardWidth;
        heroScreenUnitCard.style.height = HeroScreenUnitCardHeight;
        heroScreenUnitCard.style.paddingLeft = 18f;
        heroScreenUnitCard.style.paddingRight = 18f;
        heroScreenUnitCard.style.paddingTop = 15f;
        heroScreenUnitCard.style.paddingBottom = 15f;
        heroScreenUnitCard.style.backgroundColor = new Color(0.105f, 0.12f, 0.145f, 0.995f);
        heroScreenUnitCard.style.display = DisplayStyle.None;
        SetHeroScreenBorder(heroScreenUnitCard, new Color(0.45f, 0.38f, 0.25f, 1f), 1f);
        SetHeroScreenRadius(heroScreenUnitCard, 6f);
        heroScreenOverlay.Add(heroScreenUnitCard);

        heroScreenStatTooltip = new VisualElement { name = "hero-screen-stat-tooltip", pickingMode = PickingMode.Ignore };
        heroScreenStatTooltip.style.display = DisplayStyle.None;
        heroScreenStatTooltip.style.position = Position.Absolute;
        heroScreenStatTooltip.style.width = 330f;
        heroScreenStatTooltip.style.paddingLeft = 12f;
        heroScreenStatTooltip.style.paddingRight = 12f;
        heroScreenStatTooltip.style.paddingTop = 10f;
        heroScreenStatTooltip.style.paddingBottom = 10f;
        heroScreenStatTooltip.style.backgroundColor = new Color(0.055f, 0.065f, 0.075f, 0.995f);
        SetHeroScreenBorder(heroScreenStatTooltip, new Color(0.58f, 0.47f, 0.26f, 1f), 1f);
        SetHeroScreenRadius(heroScreenStatTooltip, 4f);
        heroScreenStatTooltipTitle = new Label { style = { fontSize = 11f, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.91f, 0.76f, 0.43f, 1f) } };
        heroScreenStatTooltip.Add(heroScreenStatTooltipTitle);
        heroScreenStatTooltipText = new Label { style = { fontSize = 10f, marginTop = 5f, whiteSpace = WhiteSpace.Normal, color = new Color(0.76f, 0.76f, 0.72f, 1f) } };
        heroScreenStatTooltip.Add(heroScreenStatTooltipText);
        heroScreenOverlay.Add(heroScreenStatTooltip);

        heroScreenTagTooltip = new VisualElement { name = "hero-screen-tag-tooltip", pickingMode = PickingMode.Ignore };
        heroScreenTagTooltip.style.display = DisplayStyle.None;
        heroScreenTagTooltip.style.position = Position.Absolute;
        heroScreenTagTooltip.style.width = 330f;
        heroScreenTagTooltip.style.paddingLeft = 12f;
        heroScreenTagTooltip.style.paddingRight = 12f;
        heroScreenTagTooltip.style.paddingTop = 10f;
        heroScreenTagTooltip.style.paddingBottom = 10f;
        heroScreenTagTooltip.style.backgroundColor = new Color(0.055f, 0.065f, 0.075f, 0.995f);
        SetHeroScreenBorder(heroScreenTagTooltip, new Color(0.58f, 0.47f, 0.26f, 1f), 1f);
        SetHeroScreenRadius(heroScreenTagTooltip, 4f);
        heroScreenTagTooltipTitle = new Label { style = { fontSize = 14f, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.91f, 0.76f, 0.43f, 1f) } };
        heroScreenTagTooltip.Add(heroScreenTagTooltipTitle);
        heroScreenTagTooltipText = new Label { style = { fontSize = 13f, marginTop = 5f, whiteSpace = WhiteSpace.Normal, color = new Color(0.76f, 0.76f, 0.72f, 1f) } };
        heroScreenTagTooltip.Add(heroScreenTagTooltipText);
        heroScreenOverlay.Add(heroScreenTagTooltip);
    }

    private void ShowHeroScreenUnitCard(UnitDefinitionData unit)
    {
        if (heroScreenUnitCard == null || unit == null)
            return;

        heroScreenUnitCard.Clear();

        VisualElement header = new VisualElement();
        header.style.height = 40f;
        header.style.flexDirection = FlexDirection.Row;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.alignItems = Align.Center;

        Label title = new Label(unit.DisplayLabel.ToUpperInvariant()) { name = "hero-screen-unit-card-title" };
        title.style.color = HeroScreenGold;
        title.style.fontSize = 20f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.Add(title);

        Button close = new Button(HideHeroScreenUnitCard) { text = "×" };
        StyleHeroScreenButton(close, 36f, 32f);
        header.Add(close);
        heroScreenUnitCard.Add(header);

        VisualElement body = new VisualElement();
        body.style.flexGrow = 1f;
        body.style.flexDirection = FlexDirection.Row;
        body.style.marginTop = 10f;

        VisualElement portrait = new UnitPortraitElement { name = "hero-screen-unit-card-portrait" };
        portrait.style.width = 210f;
        portrait.style.height = 390f;
        portrait.style.flexShrink = 0f;
        portrait.style.marginRight = 20f;
        portrait.style.position = Position.Relative;
        portrait.style.backgroundColor = new Color(0.075f, 0.085f, 0.105f, 1f);
        SetHeroScreenBorder(portrait, new Color(0.27f, 0.30f, 0.34f, 1f), 1f);
        SetHeroScreenRadius(portrait, 4f);
        ApplyHeroScreenPortrait(portrait, unit);

        VisualElement portraitHealthBar = CreateHeroScreenHealthBar(unit.MaxHitPoints, unit.MaxHitPoints, 12f);
        portraitHealthBar.style.position = Position.Absolute;
        portraitHealthBar.style.left = 12f;
        portraitHealthBar.style.right = 12f;
        portraitHealthBar.style.bottom = 13f;
        portraitHealthBar.style.marginTop = 0f;
        portraitHealthBar.style.marginBottom = 0f;
        portrait.Add(portraitHealthBar);
        body.Add(portrait);

        VisualElement stats = new VisualElement();
        stats.style.flexGrow = 1f;

        Label section = new Label("БОЕВЫЕ ХАРАКТЕРИСТИКИ");
        section.style.fontSize = 12f;
        section.style.color = new Color(0.72f, 0.67f, 0.56f, 1f);
        section.style.unityFontStyleAndWeight = FontStyle.Bold;
        section.style.marginBottom = 8f;
        stats.Add(section);

        CreateHeroScreenStatRow(
            stats, "ЖИЗНИ", unit.MaxHitPoints.ToString(),
            "Текущий запас здоровья бойца. При 0 жизней боец выбывает из боя.");
        CreateHeroScreenStatRow(
            stats, "АТАКА", unit.Attack.ToString(),
            "Сравнивается с Защитой цели и определяет множитель наносимого урона.");
        CreateHeroScreenStatRow(
            stats, "ЗАЩИТА", unit.Defense.ToString(),
            "Сравнивается с Атакой противника и влияет на количество получаемого урона.");
        CreateHeroScreenStatRow(
            stats, "УРОН", unit.Damage.ToString(),
            "Базовое количество урона до применения результата сравнения Атаки и Защиты.");
        CreateHeroScreenStatRow(
            stats, "ХОД", unit.Movement.ToString(),
            "Запас движения бойца на активацию. Стоимость перемещения зависит от пройденных гексов и местности.");
        CreateHeroScreenStatRow(
            stats, "ИНИЦИАТИВА", unit.Initiative.ToString(),
            "Определяет порядок активации бойцов в начале каждого раунда.");
        CreateHeroScreenStatRow(
            stats, "ДАЛЬНОСТЬ АТАКИ", unit.AttackRange.ToString(),
            "Максимальное расстояние в гексах, с которого боец может атаковать цель.");

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
        stats.Add(tags);

        body.Add(stats);
        heroScreenUnitCard.Add(body);

        heroScreenUnitCardDimmer.style.display = DisplayStyle.Flex;
        heroScreenUnitCard.style.display = DisplayStyle.Flex;
        heroScreenUnitCardDimmer.BringToFront();
        heroScreenUnitCard.BringToFront();
        heroScreenUnitCardDimmer.Focus();
    }

    private void HideHeroScreenUnitCard()
    {
        HideHeroScreenStatTooltip();
        HideHeroScreenTagTooltip();

        if (heroScreenUnitCard != null)
            heroScreenUnitCard.style.display = DisplayStyle.None;
        if (heroScreenUnitCardDimmer != null)
            heroScreenUnitCardDimmer.style.display = DisplayStyle.None;
    }

    // Боксированная строка характеристики: подсветка и всплывающая подсказка
    // по наведению — как CreateStatRow в SandboxFighterDetailsView.
    private void CreateHeroScreenStatRow(VisualElement parent, string title, string value, string explanation)
    {
        Color idleColor = new Color(0.13f, 0.145f, 0.165f, 1f);
        Color hoverColor = new Color(0.20f, 0.19f, 0.14f, 1f);

        VisualElement row = new VisualElement();
        row.style.height = 34f;
        row.style.marginBottom = 4f;
        row.style.paddingLeft = 10f;
        row.style.paddingRight = 10f;
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.backgroundColor = idleColor;
        SetHeroScreenBorder(row, new Color(0.25f, 0.27f, 0.29f, 1f), 1f);
        SetHeroScreenRadius(row, 3f);

        Label titleLabel = new Label(title);
        titleLabel.style.fontSize = 10f;
        titleLabel.style.color = new Color(0.64f, 0.63f, 0.58f, 1f);
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLabel.pickingMode = PickingMode.Ignore;
        row.Add(titleLabel);

        Label valueLabel = new Label(value);
        valueLabel.style.fontSize = 13f;
        valueLabel.style.color = new Color(0.91f, 0.79f, 0.54f, 1f);
        valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        valueLabel.pickingMode = PickingMode.Ignore;
        row.Add(valueLabel);

        row.RegisterCallback<PointerEnterEvent>(_ =>
        {
            row.style.backgroundColor = hoverColor;
            ShowHeroScreenStatTooltip(row, title, explanation);
        });
        row.RegisterCallback<PointerLeaveEvent>(_ =>
        {
            row.style.backgroundColor = idleColor;
            HideHeroScreenStatTooltip();
        });

        parent.Add(row);
    }

    private void ShowHeroScreenStatTooltip(VisualElement anchor, string title, string explanation)
    {
        ShowHeroScreenTooltip(heroScreenStatTooltip, heroScreenStatTooltipTitle, heroScreenStatTooltipText, anchor, title, explanation);
    }

    private void HideHeroScreenStatTooltip()
    {
        if (heroScreenStatTooltip != null)
            heroScreenStatTooltip.style.display = DisplayStyle.None;
    }

    private void ShowHeroScreenTagTooltip(VisualElement anchor, string title, string explanation)
    {
        ShowHeroScreenTooltip(heroScreenTagTooltip, heroScreenTagTooltipTitle, heroScreenTagTooltipText, anchor, title.ToUpperInvariant(), explanation);
    }

    private void HideHeroScreenTagTooltip()
    {
        if (heroScreenTagTooltip != null)
            heroScreenTagTooltip.style.display = DisplayStyle.None;
    }

    private void ShowHeroScreenTooltip(
        VisualElement tooltip,
        Label tooltipTitle,
        Label tooltipText,
        VisualElement anchor,
        string title,
        string explanation)
    {
        if (tooltip == null || tooltipTitle == null || tooltipText == null || anchor == null)
            return;

        tooltipTitle.text = title;
        tooltipText.text = string.IsNullOrWhiteSpace(explanation)
            ? "Описание для этого пункта пока не задано."
            : explanation;
        tooltip.style.display = DisplayStyle.Flex;
        tooltip.BringToFront();

        Rect bounds = anchor.worldBound;
        Vector2 topRight = heroScreenOverlay.WorldToLocal(new Vector2(bounds.xMax, bounds.yMin));
        Vector2 topLeft = heroScreenOverlay.WorldToLocal(new Vector2(bounds.xMin, bounds.yMin));
        float rootWidth = heroScreenOverlay.resolvedStyle.width;
        float rootHeight = heroScreenOverlay.resolvedStyle.height;
        if (float.IsNaN(rootWidth) || rootWidth < 400f)
            rootWidth = 1280f;
        if (float.IsNaN(rootHeight) || rootHeight < 300f)
            rootHeight = 720f;

        float left = topRight.x + 10f;
        if (left + 330f > rootWidth - 12f)
            left = topLeft.x - 340f;
        tooltip.style.left = Mathf.Clamp(left, 12f, Mathf.Max(12f, rootWidth - 342f));
        tooltip.style.top = Mathf.Clamp(topRight.y, 12f, Mathf.Max(12f, rootHeight - 120f));
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

    private static void ApplyHeroScreenPortrait(VisualElement target, UnitDefinitionData unit)
    {
        if (target == null)
            return;

        UnitPortraitElement portrait = target as UnitPortraitElement;
        if (portrait != null)
        {
            portrait.SetPortrait(unit);
            return;
        }

        // Защитный fallback для старого внешнего элемента. Новые портретные
        // рамки создаются как UnitPortraitElement и всегда используют полный
        // Sprite; здесь не пытаемся вернуть legacy ScaleAndCrop.
        target.style.backgroundImage = StyleKeyword.None;
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

    // UI Toolkit в Player не показывает встроенный Label.tooltip, поэтому
    // описание при наведении рисуется вручную через heroScreenTagTooltip —
    // как всплывающая подсказка тега в BattleSandbox.
    private VisualElement CreateHeroScreenChip(string text, Color color, string description)
    {
        Label chip = new Label(text);
        chip.style.marginRight = 5f;
        chip.style.marginTop = 3f;
        chip.style.paddingLeft = 7f;
        chip.style.paddingRight = 7f;
        chip.style.paddingTop = 2f;
        chip.style.paddingBottom = 2f;
        chip.style.fontSize = 10f;
        chip.style.color = HeroScreenText;

        Color idleBackground = color;
        idleBackground.a = 0.42f;
        Color hoverBackground = color;
        hoverBackground.a = 0.65f;
        chip.style.backgroundColor = idleBackground;
        chip.style.borderTopLeftRadius = 3f;
        chip.style.borderTopRightRadius = 3f;
        chip.style.borderBottomLeftRadius = 3f;
        chip.style.borderBottomRightRadius = 3f;

        if (!string.IsNullOrWhiteSpace(description))
        {
            chip.RegisterCallback<PointerEnterEvent>(_ =>
            {
                chip.style.backgroundColor = hoverBackground;
                ShowHeroScreenTagTooltip(chip, text, description);
            });
            chip.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                chip.style.backgroundColor = idleBackground;
                HideHeroScreenTagTooltip();
            });
        }

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
        SetHeroScreenBorder(element, HeroScreenBorder, width);
    }

    private static void SetHeroScreenBorder(VisualElement element, Color color, float width)
    {
        element.style.borderLeftWidth = width;
        element.style.borderRightWidth = width;
        element.style.borderTopWidth = width;
        element.style.borderBottomWidth = width;
        element.style.borderLeftColor = color;
        element.style.borderRightColor = color;
        element.style.borderTopColor = color;
        element.style.borderBottomColor = color;
    }

    private static string HeroScreenSlug(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "unknown";
        return value.ToLowerInvariant().Replace(' ', '-');
    }
}
