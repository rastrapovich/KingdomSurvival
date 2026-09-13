using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using KingdomSurvival.UnitDatabase;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Экран героя (UI-M03) — объединяет прежние «Отряд» и «Панель командира» в
/// один полноэкранный слой, вызываемый одной кнопкой из нижней полосы
/// оболочки.
///
/// Постоянная структура — Prototype_Main.uxml (hero-screen-overlay), общий
/// стиль — Prototype_SharedPanels.uss (.ks-*) + HeroScreen.uss, повторяемые
/// узлы неизвестного заранее количества (теги, особенности, компетенции,
/// доступные бойцы) — шаблоны в Assets/_Project/UI/Templates. Слоты
/// фиксированного количества (6 качеств, 7 боевых характеристик, состав
/// похода — командир + 4 бойца, снаряжение, инвентарь, свита, способности)
/// — статичные именованные узлы UXML, Controller только меняет их text/
/// display/классы, не создаёт дерево.
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
    private const string HeroScreenName = "Герой";
    private const int HeroScreenFighterSlots = 4;

    private static readonly Color HeroScreenTraitChipColor = new Color(0.867f, 0.710f, 0.388f, 1f);

    private static readonly string[] HeroScreenQualitySuffixes =
    {
        "strength", "dexterity", "fortitude", "instinct", "judgment", "character"
    };

    private static readonly string[] HeroScreenQualityCaptions =
    {
        "Сила", "Сноровка", "Стойкость", "Чутьё", "Суждение", "Характер"
    };

    private static readonly string[] HeroScreenQualityMeanings =
    {
        "Физическое воздействие.",
        "Координация, точность и скорость.",
        "Здоровье и физические лишения.",
        "Наблюдение, следы и опасность.",
        "Анализ, планирование и интерпретация.",
        "Сила личности, влияние и сопротивление давлению."
    };

    // Порядок совпадает с RefreshHeroScreenStats/ShowHeroScreenUnitCard —
    // индекс 5 (initiative) единственный с производным значением/подсказкой.
    private static readonly string[] HeroScreenStatSuffixes =
    {
        "health", "attack", "defense", "damage", "movement", "initiative", "attack-range"
    };

    private static readonly string[] HeroScreenUnitCardStatTitles =
    {
        "ЖИЗНИ", "АТАКА", "ЗАЩИТА", "УРОН", "ХОД", "ИНИЦИАТИВА", "ДАЛЬНОСТЬ АТАКИ"
    };

    private static readonly string[] HeroScreenUnitCardStatExplanations =
    {
        "Текущий запас здоровья бойца. При 0 жизней боец выбывает из боя.",
        "Сравнивается с Защитой цели и определяет множитель наносимого урона.",
        "Сравнивается с Атакой противника и влияет на количество получаемого урона.",
        "Базовое количество урона до применения результата сравнения Атаки и Защиты.",
        "Запас движения бойца на активацию. Стоимость перемещения зависит от пройденных гексов и местности.",
        "Определяет порядок активации бойцов в начале раунда.",
        "Максимальное расстояние в гексах, с которого боец может атаковать цель.",
    };

    private static readonly string[] HeroScreenRosterCardNames =
    {
        "hero-screen-roster-commander",
        "hero-screen-roster-fighter-1",
        "hero-screen-roster-fighter-2",
        "hero-screen-roster-fighter-3",
        "hero-screen-roster-fighter-4",
    };

    private sealed class HeroRosterCardRefs
    {
        public VisualElement Card;
        public VisualElement PortraitSlot;
        public UnitPortraitElement Portrait;
        public Label Name;
        public Label Role;
        public Label Hp;
        public Label Condition;
        public Label Level;
        public VisualElement HealthBar;
        public Label RoleChip;
        public Label Hint;
        public UnitDefinitionData CurrentUnit;
        public string ToggleFighterId;
    }

    private VisualElement heroScreenOverlay;
    private Button heroScreenCloseButton;

    private VisualElement heroScreenPortraitSlot;
    private UnitPortraitElement heroScreenPortrait;
    private Label heroScreenNameLabel;
    private Label heroScreenRoleLabel;
    private Label heroScreenLevelLabel;
    private VisualElement heroScreenExperienceFill;
    private Label heroScreenExperienceLabel;

    private Label heroScreenStatePhaseChip;

    private Label heroScreenArmyGoldLabel;
    private Button heroScreenArmyGoldMinusButton;
    private Button heroScreenArmyGoldPlusButton;
    private Label heroScreenSupplyValueLabel;
    private Button heroScreenSupplyMinusButton;
    private Button heroScreenSupplyPlusButton;
    private Label heroScreenSupplyConsumptionLabel;
    private Label heroScreenSupplyDaysLabel;

    private VisualElement[] heroScreenQualityBoxes;
    private Label[] heroScreenQualityValues;
    private string[] heroScreenQualityExplanations;

    private VisualElement heroScreenCompetenciesRow;
    private VisualElement heroScreenTraitsRow;
    private VisualElement heroScreenTagsRow;

    private Label[] heroScreenStatValues;
    private VisualElement heroScreenStatInitiativeBox;
    private string heroScreenInitiativeExplanation = string.Empty;

    private VisualElement heroScreenRosterAvailableRow;
    private Label heroScreenRosterSelectedLabel;
    private Button heroScreenRosterConfirmButton;
    private HeroRosterCardRefs[] heroScreenRosterCards;

    private VisualElement heroScreenUnitCardDimmer;
    private VisualElement heroScreenUnitCard;
    private Button heroScreenUnitCardCloseButton;
    private Label heroScreenUnitCardTitle;
    private VisualElement heroScreenUnitCardPortraitSlot;
    private UnitPortraitElement heroScreenUnitCardPortrait;
    private VisualElement heroScreenUnitCardPortraitHealthBar;
    private VisualElement[] heroScreenUnitCardStatRows;
    private Label[] heroScreenUnitCardStatValues;
    private VisualElement heroScreenUnitCardTagsRow;

    private VisualElement heroScreenStatTooltip;
    private Label heroScreenStatTooltipTitle;
    private Label heroScreenStatTooltipText;
    private VisualElement heroScreenTagTooltip;
    private Label heroScreenTagTooltipTitle;
    private Label heroScreenTagTooltipText;

    private Button heroScreenNavButton;

    private VisualTreeAsset heroChipTemplate;
    private VisualTreeAsset heroStatRowTemplate;
    private VisualTreeAsset heroRosterAvailableChipTemplate;
    private VisualTreeAsset heroHintTemplate;

    private bool heroScreenUiBound;

    private UnitDatabaseAsset heroScreenUnits;

    private bool IsHeroScreenOpen =>
        heroScreenOverlay != null && heroScreenOverlay.style.display == DisplayStyle.Flex;

    // ------------------------------------------------------------------
    // Инициализация — только поиск элементов и подписка на события.
    // ------------------------------------------------------------------

    private void InitializeHeroScreenUi()
    {
        if (interfaceRoot == null || heroScreenUiBound)
            return;

        heroScreenUnits = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);

        bool ok = true;
        ok &= BindHeroScreenChrome();
        ok &= BindHeroScreenIdentity();
        ok &= BindHeroScreenStatesAndSupply();
        ok &= BindHeroScreenQualitiesAndStats();
        ok &= BindHeroScreenDynamicPanels();
        ok &= BindHeroScreenRoster();
        ok &= BindHeroScreenUnitCard();
        ok &= BindHeroScreenTooltips();

        if (!ok)
            return;

        heroScreenUiBound = true;
        WireHeroScreenCallbacks();

        heroScreenNavButton = interfaceRoot.Q<Button>("nav-hero-button");
        if (heroScreenNavButton != null)
            heroScreenNavButton.clicked += ToggleHeroScreen;
    }

    private bool BindHeroScreenChrome()
    {
        heroScreenOverlay = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-overlay");
        heroScreenCloseButton = BindRequiredElement<Button>(interfaceRoot, HeroScreenName, "hero-screen-close-button");
        return heroScreenOverlay != null && heroScreenCloseButton != null;
    }

    private bool BindHeroScreenIdentity()
    {
        heroScreenPortraitSlot = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-portrait");
        heroScreenNameLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-name");
        heroScreenRoleLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-role");
        heroScreenLevelLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-level");
        heroScreenExperienceLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-experience-label");
        heroScreenExperienceFill = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-experience-fill");

        bool ok = heroScreenPortraitSlot != null && heroScreenNameLabel != null && heroScreenRoleLabel != null &&
                   heroScreenLevelLabel != null && heroScreenExperienceLabel != null && heroScreenExperienceFill != null;
        if (ok)
            heroScreenPortrait = CreateHeroScreenPortrait(heroScreenPortraitSlot);

        return ok && heroScreenPortrait != null;
    }

    private bool BindHeroScreenStatesAndSupply()
    {
        heroScreenStatePhaseChip = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-state-phase-chip");

        heroScreenArmyGoldLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-army-gold-label");
        heroScreenArmyGoldMinusButton = BindRequiredElement<Button>(interfaceRoot, HeroScreenName, "hero-screen-army-gold-minus-button");
        heroScreenArmyGoldPlusButton = BindRequiredElement<Button>(interfaceRoot, HeroScreenName, "hero-screen-army-gold-plus-button");
        heroScreenSupplyValueLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-supply-value-label");
        heroScreenSupplyMinusButton = BindRequiredElement<Button>(interfaceRoot, HeroScreenName, "hero-screen-supply-minus-button");
        heroScreenSupplyPlusButton = BindRequiredElement<Button>(interfaceRoot, HeroScreenName, "hero-screen-supply-plus-button");
        heroScreenSupplyConsumptionLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-supply-consumption-label");
        heroScreenSupplyDaysLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-supply-days-label");

        return heroScreenStatePhaseChip != null &&
               heroScreenArmyGoldLabel != null && heroScreenArmyGoldMinusButton != null && heroScreenArmyGoldPlusButton != null &&
               heroScreenSupplyValueLabel != null && heroScreenSupplyMinusButton != null && heroScreenSupplyPlusButton != null &&
               heroScreenSupplyConsumptionLabel != null && heroScreenSupplyDaysLabel != null;
    }

    private bool BindHeroScreenQualitiesAndStats()
    {
        heroScreenQualityBoxes = new VisualElement[HeroScreenQualitySuffixes.Length];
        heroScreenQualityValues = new Label[HeroScreenQualitySuffixes.Length];
        heroScreenQualityExplanations = new string[HeroScreenQualitySuffixes.Length];
        bool ok = true;
        for (int i = 0; i < HeroScreenQualitySuffixes.Length; i++)
        {
            heroScreenQualityBoxes[i] = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-quality-" + HeroScreenQualitySuffixes[i]);
            heroScreenQualityValues[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-quality-" + HeroScreenQualitySuffixes[i] + "-value");
            ok &= heroScreenQualityBoxes[i] != null && heroScreenQualityValues[i] != null;
        }

        heroScreenStatValues = new Label[HeroScreenStatSuffixes.Length];
        for (int i = 0; i < HeroScreenStatSuffixes.Length; i++)
        {
            heroScreenStatValues[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-stat-" + HeroScreenStatSuffixes[i] + "-value");
            ok &= heroScreenStatValues[i] != null;
        }

        heroScreenStatInitiativeBox = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-stat-initiative");
        ok &= heroScreenStatInitiativeBox != null;

        return ok;
    }

    private bool BindHeroScreenDynamicPanels()
    {
        heroScreenCompetenciesRow = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-competencies-row");
        heroScreenTraitsRow = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-traits-row");
        heroScreenTagsRow = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-tags-row");
        return heroScreenCompetenciesRow != null && heroScreenTraitsRow != null && heroScreenTagsRow != null;
    }

    private bool BindHeroScreenRoster()
    {
        heroScreenRosterAvailableRow = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-roster-available-row");
        heroScreenRosterSelectedLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-roster-selected-label");
        heroScreenRosterConfirmButton = BindRequiredElement<Button>(interfaceRoot, HeroScreenName, "hero-screen-roster-confirm-button");

        bool ok = heroScreenRosterAvailableRow != null && heroScreenRosterSelectedLabel != null && heroScreenRosterConfirmButton != null;

        heroScreenRosterCards = new HeroRosterCardRefs[HeroScreenRosterCardNames.Length];
        for (int i = 0; i < HeroScreenRosterCardNames.Length; i++)
        {
            HeroRosterCardRefs refs = BindHeroScreenRosterCardRefs(HeroScreenRosterCardNames[i]);
            heroScreenRosterCards[i] = refs;
            ok &= refs != null;
        }

        return ok;
    }

    private HeroRosterCardRefs BindHeroScreenRosterCardRefs(string cardName)
    {
        HeroRosterCardRefs refs = new HeroRosterCardRefs
        {
            Card = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, cardName),
            PortraitSlot = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, cardName + "-portrait"),
            Name = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, cardName + "-name"),
            Role = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, cardName + "-role"),
            Hp = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, cardName + "-hp"),
            Condition = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, cardName + "-condition"),
            Level = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, cardName + "-level"),
            HealthBar = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, cardName + "-healthbar"),
            RoleChip = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, cardName + "-role-chip"),
            Hint = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, cardName + "-hint"),
        };

        if (refs.Card == null || refs.PortraitSlot == null || refs.Name == null || refs.Role == null ||
            refs.Hp == null || refs.Condition == null || refs.Level == null || refs.HealthBar == null ||
            refs.RoleChip == null || refs.Hint == null)
            return null;

        refs.Portrait = CreateHeroScreenPortrait(refs.PortraitSlot);
        return refs;
    }

    private bool BindHeroScreenUnitCard()
    {
        heroScreenUnitCardDimmer = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-dimmer");
        heroScreenUnitCard = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card");
        heroScreenUnitCardCloseButton = BindRequiredElement<Button>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-close-button");
        heroScreenUnitCardTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-title");
        heroScreenUnitCardPortraitSlot = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-portrait");
        heroScreenUnitCardPortraitHealthBar = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-portrait-healthbar");
        heroScreenUnitCardTagsRow = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-tags");

        bool ok = heroScreenUnitCardDimmer != null && heroScreenUnitCard != null && heroScreenUnitCardCloseButton != null &&
                   heroScreenUnitCardTitle != null && heroScreenUnitCardPortraitSlot != null &&
                   heroScreenUnitCardPortraitHealthBar != null && heroScreenUnitCardTagsRow != null;

        heroScreenUnitCardStatRows = new VisualElement[HeroScreenStatSuffixes.Length];
        heroScreenUnitCardStatValues = new Label[HeroScreenStatSuffixes.Length];
        for (int i = 0; i < HeroScreenStatSuffixes.Length; i++)
        {
            heroScreenUnitCardStatRows[i] = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-stat-" + HeroScreenStatSuffixes[i]);
            heroScreenUnitCardStatValues[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-stat-" + HeroScreenStatSuffixes[i] + "-value");
            ok &= heroScreenUnitCardStatRows[i] != null && heroScreenUnitCardStatValues[i] != null;
        }

        if (ok)
            heroScreenUnitCardPortrait = CreateHeroScreenPortrait(heroScreenUnitCardPortraitSlot);

        return ok && heroScreenUnitCardPortrait != null;
    }

    private bool BindHeroScreenTooltips()
    {
        heroScreenStatTooltip = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-stat-tooltip");
        heroScreenStatTooltipTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-stat-tooltip-title");
        heroScreenStatTooltipText = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-stat-tooltip-text");
        heroScreenTagTooltip = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-tag-tooltip");
        heroScreenTagTooltipTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-tag-tooltip-title");
        heroScreenTagTooltipText = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-tag-tooltip-text");

        return heroScreenStatTooltip != null && heroScreenStatTooltipTitle != null && heroScreenStatTooltipText != null &&
               heroScreenTagTooltip != null && heroScreenTagTooltipTitle != null && heroScreenTagTooltipText != null;
    }

    // Один раз при инициализации создаёт настоящий рендерящий компонент
    // (Sprite+Cover/Contain — как у портрета Narrative, UI_ARCHITECTURE.md
    // §8/§22) внутри уже существующего статичного слота. Слот и его позиция
    // — UXML; какой именно Sprite показывать — решает ApplyHeroScreenPortrait
    // на каждый Refresh, не здесь.
    private static UnitPortraitElement CreateHeroScreenPortrait(VisualElement slot)
    {
        if (slot == null)
            return null;

        UnitPortraitElement portrait = new UnitPortraitElement();
        portrait.AddToClassList("hero-screen-portrait-fill");
        slot.Add(portrait);
        return portrait;
    }

    private void WireHeroScreenCallbacks()
    {
        heroScreenCloseButton.clicked += CloseHeroScreen;
        heroScreenArmyGoldMinusButton.clicked += OnStableArmyGoldMinusClicked;
        heroScreenArmyGoldPlusButton.clicked += OnStableArmyGoldPlusClicked;
        heroScreenSupplyMinusButton.clicked += OnStableSupplyMinusClicked;
        heroScreenSupplyPlusButton.clicked += OnStableSupplyPlusClicked;
        heroScreenRosterConfirmButton.clicked += OnHeroScreenRosterConfirmClicked;
        heroScreenUnitCardCloseButton.clicked += HideHeroScreenUnitCard;

        heroScreenStatePhaseChip.RegisterCallback<PointerEnterEvent>(_ =>
            ShowHeroScreenTagTooltip(heroScreenStatePhaseChip, heroScreenStatePhaseChip.text, "Текущее положение командира."));
        heroScreenStatePhaseChip.RegisterCallback<PointerLeaveEvent>(_ => HideHeroScreenTagTooltip());

        for (int i = 0; i < heroScreenQualityBoxes.Length; i++)
        {
            VisualElement box = heroScreenQualityBoxes[i];
            int index = i;
            string title = HeroScreenQualityCaptions[i].ToUpperInvariant();
            box.RegisterCallback<PointerEnterEvent>(_ => ShowHeroScreenStatTooltip(box, title, heroScreenQualityExplanations[index]));
            box.RegisterCallback<PointerLeaveEvent>(_ => HideHeroScreenStatTooltip());
        }

        heroScreenStatInitiativeBox.RegisterCallback<PointerEnterEvent>(_ =>
            ShowHeroScreenStatTooltip(heroScreenStatInitiativeBox, "ИНИЦИАТИВА", heroScreenInitiativeExplanation));
        heroScreenStatInitiativeBox.RegisterCallback<PointerLeaveEvent>(_ => HideHeroScreenStatTooltip());

        for (int i = 0; i < heroScreenRosterCards.Length; i++)
            WireHeroScreenRosterCardInteractions(heroScreenRosterCards[i]);

        for (int i = 0; i < heroScreenUnitCardStatRows.Length; i++)
        {
            VisualElement row = heroScreenUnitCardStatRows[i];
            string title = HeroScreenUnitCardStatTitles[i];
            string explanation = HeroScreenUnitCardStatExplanations[i];
            row.RegisterCallback<PointerEnterEvent>(_ => ShowHeroScreenStatTooltip(row, title, explanation));
            row.RegisterCallback<PointerLeaveEvent>(_ => HideHeroScreenStatTooltip());
        }

        heroScreenUnitCardDimmer.focusable = true;
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
    }

    // ПКМ на карточке — карточка существа/бойца (как в BattleSandbox). ЛКМ —
    // убрать бойца из состава, но только пока refs.ToggleFighterId задан
    // (RefreshHeroScreenRoster выставляет его только для реально убираемых
    // бойцов вне похода — не для командира и не во время активной
    // экспедиции). Регистрируется один раз на статичной карточке, а не при
    // каждом Refresh — читает refs.CurrentUnit/refs.ToggleFighterId в
    // момент клика, они обновляются в FillHeroScreenRosterCard.
    private void WireHeroScreenRosterCardInteractions(HeroRosterCardRefs refs)
    {
        refs.Card.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (isGameOver)
                return;

            if (evt.button == 1)
            {
                if (refs.CurrentUnit != null)
                {
                    ShowHeroScreenUnitCard(refs.CurrentUnit);
                    evt.StopPropagation();
                }
                return;
            }

            if (evt.button == 0 && refs.ToggleFighterId != null)
            {
                selectedFighterIds.Remove(refs.ToggleFighterId);
                RefreshHeroScreen();
                RefreshStableUiAfterStateChange();
                evt.StopPropagation();
            }
        });

        refs.RoleChip.RegisterCallback<PointerEnterEvent>(_ =>
            ShowHeroScreenTagTooltip(refs.RoleChip, refs.RoleChip.text, "Ключевая роль в бою."));
        refs.RoleChip.RegisterCallback<PointerLeaveEvent>(_ => HideHeroScreenTagTooltip());
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

        PauseForBlockingModal();
        heroScreenOverlay.style.display = DisplayStyle.Flex;
        heroScreenOverlay.BringToFront();
        RefreshHeroScreen();
    }

    private void CloseHeroScreen()
    {
        if (heroScreenOverlay == null)
            return;

        bool wasOpen = IsHeroScreenOpen;
        HideHeroScreenUnitCard();
        heroScreenOverlay.style.display = DisplayStyle.None;

        if (wasOpen)
            ResumeAfterBlockingModalIfReady();
    }

    // ------------------------------------------------------------------
    // Наполнение данными
    // ------------------------------------------------------------------

    private void RefreshHeroScreen()
    {
        if (!heroScreenUiBound || !IsHeroScreenOpen || gameState == null)
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
        RefreshHeroScreenRoster(commander, heroUnit);
    }

    // Как в старом RefreshSupplyBlock/ApplyCompactSupplyText экрана «Армия»,
    // но нацелено на новую панель здесь, на экране героя. Вызывается не
    // только из RefreshHeroScreen — независимо из общих refresh-путей
    // (StableUI.cs/PrototypeUIController.cs/Buildings.cs), поэтому сама
    // проверяет готовность экрана.
    private void RefreshHeroScreenSupplyPanel()
    {
        if (!heroScreenUiBound || gameState == null)
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

    // Шесть качеств героя 1-10 (§2 инструкции по качествам и проверкам) —
    // фиксированное количество, слоты статичны в UXML.
    private void RefreshHeroScreenQualities(HeroProfileData hero)
    {
        for (int i = 0; i < heroScreenQualityValues.Length; i++)
        {
            HeroQuality quality = (HeroQuality)i;
            int value = hero != null ? hero.GetQuality(quality) : HeroProfileData.DefaultQualityValue;
            heroScreenQualityValues[i].text = value + " / " + HeroProfileData.MaxQualityValue;
            heroScreenQualityExplanations[i] =
                HeroScreenQualityCaptions[i] + " " + value + " из 10 (" + GetHeroScreenQualityRangeLabel(value) + "). " +
                HeroScreenQualityMeanings[i];
        }
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
    // Список остаётся динамическим (шаблон HeroStatRow.uxml): архитектура
    // допускает другие компетенции позже без переделки экрана.
    private void RefreshHeroScreenCompetencies(HeroProfileData hero)
    {
        heroScreenCompetenciesRow.Clear();
        int fieldcraft = hero != null ? hero.GetCompetency(NarrativeCompetencyIds.Fieldcraft) : 0;
        AddHeroScreenStatRow(
            heroScreenCompetenciesRow,
            NarrativeCompetencyLabels.GetLabel(NarrativeCompetencyIds.Fieldcraft).ToUpperInvariant(),
            fieldcraft + " / 5",
            "Чтение следов, разведка, поиск скрытых мест, выбор маршрута, устройство лагеря, обнаружение засад и подготовка к дорожным встречам.");
    }

    private void AddHeroScreenStatRow(VisualElement parent, string title, string value, string explanation)
    {
        VisualTreeAsset template = LoadHeroStatRowTemplate();
        if (template == null)
            return;

        TemplateContainer instance = template.Instantiate();
        VisualElement row = instance.Q<VisualElement>("hero-screen-stat-row");
        Label titleLabel = instance.Q<Label>("hero-screen-stat-row-title");
        Label valueLabel = instance.Q<Label>("hero-screen-stat-row-value");

        if (titleLabel != null)
            titleLabel.text = title;
        if (valueLabel != null)
            valueLabel.text = value;

        if (row != null)
        {
            row.RegisterCallback<PointerEnterEvent>(_ => ShowHeroScreenStatTooltip(row, title, explanation));
            row.RegisterCallback<PointerLeaveEvent>(_ => HideHeroScreenStatTooltip());
        }

        parent.Add(instance);
    }

    // Особенности героя (§4): стабильные строковые ID, показываются как
    // теги с подсказкой — так же, как боевые теги существ из UnitDatabase.
    // Количество варьируется — динамический список (шаблон HeroChip.uxml).
    private void RefreshHeroScreenTraits(HeroProfileData hero)
    {
        heroScreenTraitsRow.Clear();

        if (hero != null && hero.HasTrait(NarrativeTraitIds.KnowsTheWay))
        {
            AddHeroScreenChip(
                heroScreenTraitsRow,
                "Знающий дорогу",
                HeroScreenTraitChipColor,
                "При успешном обнаружении дорожный Encounter начинается в подготовленном состоянии: герой замечает событие раньше, может наблюдать, обойти или занять выгодную позицию.");
        }

        if (hero != null && hero.HasTrait(NarrativeTraitIds.Naturalist))
        {
            AddHeroScreenChip(
                heroScreenTraitsRow,
                "Натуралист",
                HeroScreenTraitChipColor,
                "Открывает авторские блоки и варианты, связанные с растениями, животными, погодой, болезнями, водой и природными изменениями.");
        }

        if (heroScreenTraitsRow.childCount == 0)
            AddHeroScreenHint(heroScreenTraitsRow, "У героя пока нет особенностей.");
    }

    // Семь боевых характеристик — фиксированное количество, слоты статичны
    // в UXML (как и качества выше).
    private void RefreshHeroScreenStats(UnitDefinitionData unit, HeroProfileData hero)
    {
        heroScreenStatValues[0].text = (unit != null ? unit.MaxHitPoints : 0).ToString();
        heroScreenStatValues[1].text = (unit != null ? unit.Attack : 0).ToString();
        heroScreenStatValues[2].text = (unit != null ? unit.Defense : 0).ToString();
        heroScreenStatValues[3].text = (unit != null ? unit.Damage : 0).ToString();
        heroScreenStatValues[4].text = (unit != null ? unit.Movement : 0).ToString();
        RefreshHeroScreenInitiativeStat(unit, hero);
        heroScreenStatValues[6].text = (unit != null ? unit.AttackRange : 0).ToString();
    }

    // Единственная боевая характеристика командира, куда сейчас подключено
    // качество (§16: Сноровка -> Инициатива). Подсказка показывает
    // происхождение производного значения, как того требует §17.
    private void RefreshHeroScreenInitiativeStat(UnitDefinitionData unit, HeroProfileData hero)
    {
        int baseInitiative = unit != null ? unit.Initiative : 0;
        int dexterity = hero != null ? hero.GetQuality(HeroQuality.Dexterity) : HeroProfileData.DefaultQualityValue;
        int modifier = HeroCombatStatsBuilder.GetCombatModifier(dexterity);
        int finalInitiative = Mathf.Max(0, baseInitiative + modifier);

        string explanation = "База: " + baseInitiative;
        if (modifier != 0)
            explanation += "\nСноровка " + dexterity + ": " + (modifier > 0 ? "+" + modifier : modifier.ToString());

        heroScreenStatValues[5].text = finalInitiative.ToString();
        heroScreenInitiativeExplanation = explanation;
    }

    // Количество варьируется по unit.TagIds — динамический список (шаблон
    // HeroChip.uxml).
    private void RefreshHeroScreenTags(UnitDefinitionData unit)
    {
        heroScreenTagsRow.Clear();
        if (heroScreenUnits == null || unit == null)
        {
            AddHeroScreenHint(heroScreenTagsRow, "База существ недоступна.");
            return;
        }

        int shown = 0;
        for (int i = 0; i < unit.TagIds.Count; i++)
        {
            UnitTagDefinition tag = heroScreenUnits.FindTag(unit.TagIds[i]);
            if (tag == null)
                continue;
            AddHeroScreenChip(heroScreenTagsRow, tag.DisplayLabel, tag.Color, tag.Description);
            shown++;
        }

        if (shown == 0)
            AddHeroScreenHint(heroScreenTagsRow, "У героя пока нет тегов.");
    }

    private void RefreshHeroScreenStates(CommanderData commander)
    {
        heroScreenStatePhaseChip.text = commander != null && commander.State == CommanderState.InCastle
            ? "Дома"
            : "В пути";
    }

    // P08-T03: реальный picker состава похода. Источник истины —
    // GameState.Fighters + selectedFighterIds (тот же набор, что уже
    // использует общий поток "выбрать бойцов -> кликнуть цель на карте"), а
    // во время активного похода — ActiveExpedition.FighterIds. Слоты
    // (командир + 4 бойца) статичны в UXML — здесь только заполняются.
    private void RefreshHeroScreenRoster(CommanderData commander, UnitDefinitionData heroUnit)
    {
        FillHeroScreenRosterCard(
            heroScreenRosterCards[0],
            commander != null ? commander.Name : "Командир",
            "Командир",
            commander != null ? commander.Level : 1,
            heroUnit,
            true);
        heroScreenRosterCards[0].ToggleFighterId = null;

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
            HeroRosterCardRefs refs = heroScreenRosterCards[i + 1];
            if (i < slotFighters.Count)
            {
                FighterData fighter = slotFighters[i];
                FillHeroScreenRosterCard(refs, fighter.Name, fighter.Role, fighter.Level, ResolveHeroScreenUnit(fighter), false);
                refs.ToggleFighterId = expeditionActive ? null : fighter.Id;
            }
            else
            {
                FillHeroScreenRosterCard(
                    refs,
                    "Пусто",
                    expeditionActive ? "герой ушёл без него" : "место свободно",
                    0,
                    null,
                    false);
                refs.ToggleFighterId = null;
            }
        }

        RefreshHeroScreenRosterAvailable(slotFighters, expeditionActive);
    }

    private void FillHeroScreenRosterCard(
        HeroRosterCardRefs refs,
        string title,
        string role,
        int level,
        UnitDefinitionData unit,
        bool isCommander)
    {
        refs.CurrentUnit = unit;

        refs.Card.EnableInClassList("hero-screen-roster-card-filled", unit != null);
        refs.Card.EnableInClassList("hero-screen-roster-card-commander", isCommander);

        ApplyHeroScreenPortrait(refs.Portrait, unit);

        refs.Name.text = title;
        refs.Name.EnableInClassList("hero-screen-roster-card-name-commander", isCommander);
        refs.Role.text = role;

        refs.Hp.text = unit != null ? "HP " + unit.MaxHitPoints : "HP —";
        refs.Condition.text = unit != null ? "цел" : "—";
        refs.Level.text = level > 0 ? "ур. " + level : "—";

        // HP всегда MaxHitPoints/MaxHitPoints (см. HeroScreen.uss) — полоса
        // только показывается/скрывается, заполнение статично.
        refs.HealthBar.style.display = unit != null ? DisplayStyle.Flex : DisplayStyle.None;

        refs.RoleChip.style.display = unit != null ? DisplayStyle.Flex : DisplayStyle.None;
        if (unit != null)
            refs.RoleChip.text = HeroScreenRoleLabel(unit);

        refs.Hint.style.display = unit != null ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // Количество доступных бойцов варьируется — динамический список (шаблон
    // HeroRosterAvailableChip.uxml).
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

                AddHeroScreenRosterAvailableChip(fighter, canPick && !full);
                shown++;
            }

            if (shown == 0)
                AddHeroScreenHint(heroScreenRosterAvailableRow, "Все бойцы уже в составе.");
        }
        else
        {
            AddHeroScreenHint(heroScreenRosterAvailableRow, "Отряд уже в походе.");
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

    private void AddHeroScreenRosterAvailableChip(FighterData fighter, bool canAdd)
    {
        VisualTreeAsset template = LoadHeroRosterAvailableChipTemplate();
        if (template == null)
            return;

        TemplateContainer instance = template.Instantiate();
        VisualElement chip = instance.Q<VisualElement>("hero-screen-roster-available-chip");
        Label label = instance.Q<Label>("hero-screen-roster-available-chip-label");

        if (label != null)
        {
            label.text = fighter.Name + " — " + fighter.Role;
            label.EnableInClassList("hero-screen-roster-available-chip-label-disabled", !canAdd);
        }

        if (chip != null)
            chip.EnableInClassList("hero-screen-roster-available-chip-disabled", !canAdd);

        if (canAdd)
        {
            string fighterId = fighter.Id;
            instance.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || isGameOver)
                    return;

                if (selectedFighterIds.Count < HeroScreenFighterSlots)
                    selectedFighterIds.Add(fighterId);

                RefreshHeroScreen();
                RefreshStableUiAfterStateChange();
                evt.StopPropagation();
            });
        }

        heroScreenRosterAvailableRow.Add(instance);
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

    // ------------------------------------------------------------------
    // Подробная карточка существа/бойца — модальное окно с затемнением,
    // боксы характеристик и теги с настоящими всплывающими подсказками по
    // наведению. UI Toolkit в Player (не в редакторе) НЕ показывает
    // встроенный VisualElement.tooltip — поэтому, как и в BattleSandbox,
    // подсказки рисуются вручную отдельными плавающими панелями (легитимное
    // исключение §2 доктрины — поведение/позиционирование, не структура).
    // ------------------------------------------------------------------

    private void ShowHeroScreenUnitCard(UnitDefinitionData unit)
    {
        if (!heroScreenUiBound || unit == null)
            return;

        heroScreenUnitCardTitle.text = unit.DisplayLabel.ToUpperInvariant();
        ApplyHeroScreenPortrait(heroScreenUnitCardPortrait, unit);

        for (int i = 0; i < heroScreenUnitCardStatValues.Length; i++)
            heroScreenUnitCardStatValues[i].text = string.Empty;

        heroScreenUnitCardStatValues[0].text = unit.MaxHitPoints.ToString();
        heroScreenUnitCardStatValues[1].text = unit.Attack.ToString();
        heroScreenUnitCardStatValues[2].text = unit.Defense.ToString();
        heroScreenUnitCardStatValues[3].text = unit.Damage.ToString();
        heroScreenUnitCardStatValues[4].text = unit.Movement.ToString();
        heroScreenUnitCardStatValues[5].text = unit.Initiative.ToString();
        heroScreenUnitCardStatValues[6].text = unit.AttackRange.ToString();

        heroScreenUnitCardTagsRow.Clear();
        if (heroScreenUnits != null)
        {
            for (int i = 0; i < unit.TagIds.Count; i++)
            {
                UnitTagDefinition tag = heroScreenUnits.FindTag(unit.TagIds[i]);
                if (tag != null)
                    AddHeroScreenChip(heroScreenUnitCardTagsRow, tag.DisplayLabel, tag.Color, tag.Description);
            }
        }

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

    // ------------------------------------------------------------------
    // Динамические чипы/подсказки (Assets/_Project/UI/Templates) — раздел
    // 19/2 UI_ARCHITECTURE.md: количество неизвестно заранее (теги героя,
    // теги существа в карточке, особенности, доступные бойцы), поэтому
    // клонируются из шаблонов, а не создаются через new VisualElement/Label.
    // ------------------------------------------------------------------

    // UI Toolkit в Player не показывает встроенный Label.tooltip, поэтому
    // описание при наведении рисуется вручную через heroScreenTagTooltip —
    // как всплывающая подсказка тега в BattleSandbox.
    private void AddHeroScreenChip(VisualElement parent, string text, Color color, string description)
    {
        VisualTreeAsset template = LoadHeroChipTemplate();
        if (template == null)
            return;

        TemplateContainer instance = template.Instantiate();
        Label chip = instance.Q<Label>("hero-screen-chip");
        if (chip == null)
        {
            parent.Add(instance);
            return;
        }

        chip.text = text;

        Color idleBackground = color;
        idleBackground.a = 0.42f;
        Color hoverBackground = color;
        hoverBackground.a = 0.65f;
        chip.style.backgroundColor = idleBackground;

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

        parent.Add(instance);
    }

    private void AddHeroScreenHint(VisualElement parent, string text)
    {
        VisualTreeAsset template = LoadHeroHintTemplate();
        if (template == null)
            return;

        TemplateContainer instance = template.Instantiate();
        Label label = instance.Q<Label>("hero-screen-hint");
        if (label != null)
            label.text = text;

        parent.Add(instance);
    }

    private VisualTreeAsset LoadHeroChipTemplate()
    {
        if (heroChipTemplate == null)
            heroChipTemplate = Resources.Load<VisualTreeAsset>("Templates/HeroChip");
        return heroChipTemplate;
    }

    private VisualTreeAsset LoadHeroStatRowTemplate()
    {
        if (heroStatRowTemplate == null)
            heroStatRowTemplate = Resources.Load<VisualTreeAsset>("Templates/HeroStatRow");
        return heroStatRowTemplate;
    }

    private VisualTreeAsset LoadHeroRosterAvailableChipTemplate()
    {
        if (heroRosterAvailableChipTemplate == null)
            heroRosterAvailableChipTemplate = Resources.Load<VisualTreeAsset>("Templates/HeroRosterAvailableChip");
        return heroRosterAvailableChipTemplate;
    }

    private VisualTreeAsset LoadHeroHintTemplate()
    {
        if (heroHintTemplate == null)
            heroHintTemplate = Resources.Load<VisualTreeAsset>("Templates/HeroHint");
        return heroHintTemplate;
    }
}
