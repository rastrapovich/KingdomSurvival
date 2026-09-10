using System;
using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Журнал целей (P08J) — read-only fullscreen-слой поверх
/// Chapter01JournalProvider.Build, по архитектурному образцу Hero Screen
/// (программный VisualElement, отдельный partial-файл, повторно использует
/// его визуальный язык — CreateHeroScreenPanel/SetHeroScreenBorder/цвета).
///
/// Журнал ничего не решает и не хранит о сюжетном прогрессе: он полностью
/// пересобирается из GameState/NarrativeState при каждом RefreshJournal.
/// seenJournalRevisionIds — единственное собственное состояние экрана, и
/// это чисто UI-пометка "видел ли игрок эту редакцию текста в этой
/// сессии", а не сюжетный флаг.
/// </summary>
public partial class PrototypeUIController
{
    private VisualElement journalOverlay;

    private Button journalNavButton;
    private Button journalGoalsTabButton;
    private Button journalChronicleTabButton;
    private Button journalCloseButton;

    private VisualElement journalMainSection;
    private VisualElement journalOptionalSection;
    private VisualElement journalCompletedSection;

    private VisualElement journalMainList;
    private VisualElement journalOptionalList;
    private VisualElement journalCompletedList;

    private Label journalDetailCategory;
    private Label journalDetailTitle;
    private Label journalDetailDescription;
    private Label journalDetailCurrentStep;

    private string selectedJournalGoalId;

    // Session-only: сбрасывается при перезапуске игры (раздел 29
    // инструкции). Сюжетный прогресс живёт в NarrativeState отдельно и не
    // теряется — сбрасывается только декоративная отметка «НОВОЕ».
    private readonly HashSet<string> seenJournalRevisionIds = new HashSet<string>();

    private bool IsJournalOpen =>
        journalOverlay != null && journalOverlay.style.display == DisplayStyle.Flex;

    // ------------------------------------------------------------------
    // Инициализация
    // ------------------------------------------------------------------

    private void InitializeJournalUi()
    {
        if (interfaceRoot == null || journalOverlay != null)
            return;

        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");
        if (screen == null)
            return;

        BuildJournalScreen(screen);

        journalNavButton = interfaceRoot.Q<Button>("nav-journal-button");
        if (journalNavButton != null)
            journalNavButton.clicked += ToggleJournal;
    }

    private void ToggleJournal()
    {
        if (IsJournalOpen)
            CloseJournal();
        else
            OpenJournal();
    }

    private void OpenJournal()
    {
        if (journalOverlay == null)
            return;

        // Раздел 20 инструкции: Journal и Hero Screen не накладываются друг
        // на друга — один fullscreen overlay всегда закрывает другой.
        CloseHeroScreen();

        journalOverlay.style.display = DisplayStyle.Flex;
        journalOverlay.BringToFront();
        RefreshJournal();
    }

    private void CloseJournal()
    {
        if (journalOverlay == null)
            return;

        journalOverlay.style.display = DisplayStyle.None;
    }

    // ------------------------------------------------------------------
    // Построение
    // ------------------------------------------------------------------

    private void BuildJournalScreen(VisualElement screen)
    {
        journalOverlay = new VisualElement { name = "journal-overlay" };
        journalOverlay.style.position = Position.Absolute;
        journalOverlay.style.left = 0f;
        journalOverlay.style.right = 0f;
        journalOverlay.style.top = 0f;
        journalOverlay.style.bottom = 0f;
        journalOverlay.style.backgroundColor = HeroScreenBackdrop;
        journalOverlay.style.display = DisplayStyle.None;
        journalOverlay.style.paddingLeft = 18f;
        journalOverlay.style.paddingRight = 18f;
        journalOverlay.style.paddingTop = 12f;
        journalOverlay.style.paddingBottom = 12f;
        screen.Add(journalOverlay);

        journalOverlay.Add(BuildJournalHeader());
        journalOverlay.Add(BuildJournalTabs());

        VisualElement columns = new VisualElement { name = "journal-columns" };
        columns.style.flexDirection = FlexDirection.Row;
        columns.style.flexGrow = 1f;
        columns.style.minHeight = 0f;
        journalOverlay.Add(columns);

        columns.Add(BuildJournalGoalsColumn());
        columns.Add(BuildJournalDetailColumn());
    }

    private VisualElement BuildJournalHeader()
    {
        VisualElement header = new VisualElement { name = "journal-header" };
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.height = 44f;
        header.style.flexShrink = 0f;
        header.style.marginBottom = 10f;

        Label title = new Label("ЖУРНАЛ") { name = "journal-title" };
        title.style.color = HeroScreenGold;
        title.style.fontSize = 20f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.Add(title);

        journalCloseButton = new Button(CloseJournal)
        {
            name = "journal-close-button",
            text = "ЗАКРЫТЬ"
        };
        StyleHeroScreenButton(journalCloseButton, 120f, 32f);
        header.Add(journalCloseButton);
        return header;
    }

    // Хроника — отдельный будущий раздел (раздел 18 инструкции): вкладка
    // существует, но задизейблена в v1, а не подменяется «КОРОЛЕВСКИМИ
    // ДОНЕСЕНИЯМИ» (другая, исторически накопившаяся система UI).
    private VisualElement BuildJournalTabs()
    {
        VisualElement tabs = new VisualElement { name = "journal-tabs" };
        tabs.style.flexDirection = FlexDirection.Row;
        tabs.style.flexShrink = 0f;
        tabs.style.marginBottom = 10f;

        journalGoalsTabButton = new Button { name = "journal-tab-goals", text = "ЦЕЛИ" };
        StyleHeroScreenButton(journalGoalsTabButton, 140f, 32f);
        journalGoalsTabButton.style.marginRight = 8f;
        journalGoalsTabButton.AddToClassList("nav-button-active");
        tabs.Add(journalGoalsTabButton);

        journalChronicleTabButton = new Button { name = "journal-tab-chronicle", text = "ХРОНИКА" };
        StyleHeroScreenButton(journalChronicleTabButton, 140f, 32f);
        journalChronicleTabButton.SetEnabled(false);
        journalChronicleTabButton.tooltip = "Хроника событий будет подключена позже.";
        tabs.Add(journalChronicleTabButton);

        return tabs;
    }

    private VisualElement BuildJournalGoalsColumn()
    {
        VisualElement column = new VisualElement { name = "journal-goals-column" };
        column.style.width = new Length(33f, LengthUnit.Percent);
        column.style.marginRight = 12f;
        column.style.minWidth = 0f;

        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical) { name = "journal-goals-scroll" };
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 0f;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        column.Add(scroll);
        VisualElement content = scroll.contentContainer;

        journalMainSection = BuildJournalGoalSection("journal-main-section", "ОСНОВНАЯ ЦЕЛЬ", out journalMainList);
        content.Add(journalMainSection);

        journalOptionalSection = BuildJournalGoalSection("journal-optional-section", "ДОПОЛНИТЕЛЬНЫЕ", out journalOptionalList);
        content.Add(journalOptionalSection);

        journalCompletedSection = BuildJournalGoalSection("journal-completed-section", "ЗАВЕРШЁННЫЕ", out journalCompletedList);
        content.Add(journalCompletedSection);

        return column;
    }

    private VisualElement BuildJournalGoalSection(string name, string title, out VisualElement list)
    {
        VisualElement panel = CreateHeroScreenPanel(name, title);
        list = new VisualElement { name = name + "-list" };
        panel.Add(list);
        return panel;
    }

    private VisualElement BuildJournalDetailColumn()
    {
        VisualElement column = new VisualElement { name = "journal-detail-column" };
        column.style.flexGrow = 1f;
        column.style.minWidth = 0f;

        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical) { name = "journal-detail-scroll" };
        scroll.style.flexGrow = 1f;
        scroll.style.minHeight = 0f;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        column.Add(scroll);
        VisualElement content = scroll.contentContainer;

        VisualElement panel = CreateHeroScreenPanel("journal-detail-panel", "ВЫБЕРИТЕ ЦЕЛЬ");
        panel.style.flexGrow = 1f;
        journalDetailCategory = panel.Q<Label>("journal-detail-panel-title");

        journalDetailTitle = new Label(string.Empty) { name = "journal-detail-title" };
        journalDetailTitle.style.color = HeroScreenText;
        journalDetailTitle.style.fontSize = 17f;
        journalDetailTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        journalDetailTitle.style.whiteSpace = WhiteSpace.Normal;
        journalDetailTitle.style.marginBottom = 8f;
        panel.Add(journalDetailTitle);

        journalDetailDescription = new Label(string.Empty) { name = "journal-detail-description" };
        journalDetailDescription.style.color = HeroScreenText;
        journalDetailDescription.style.fontSize = 12f;
        journalDetailDescription.style.whiteSpace = WhiteSpace.Normal;
        journalDetailDescription.style.marginBottom = 14f;
        panel.Add(journalDetailDescription);

        Label stepCaption = new Label("ТЕКУЩИЙ ШАГ") { name = "journal-detail-step-caption" };
        stepCaption.style.color = HeroScreenMuted;
        stepCaption.style.fontSize = 10f;
        stepCaption.style.unityFontStyleAndWeight = FontStyle.Bold;
        stepCaption.style.marginBottom = 4f;
        panel.Add(stepCaption);

        journalDetailCurrentStep = new Label(string.Empty) { name = "journal-detail-current-step" };
        journalDetailCurrentStep.style.color = HeroScreenText;
        journalDetailCurrentStep.style.fontSize = 12f;
        journalDetailCurrentStep.style.whiteSpace = WhiteSpace.Normal;
        panel.Add(journalDetailCurrentStep);

        content.Add(panel);
        return column;
    }

    // ------------------------------------------------------------------
    // Наполнение данными
    // ------------------------------------------------------------------

    // Единственная точка входа в Chapter01JournalProvider.Build — вызывается
    // при каждом открытии и при каждом относящемся к делу изменении
    // состояния, пока журнал открыт (RefreshStableUiAfterStateChange /
    // RefreshInterface). Никакого собственного Update-цикла.
    private void RefreshJournal()
    {
        if (journalOverlay == null || gameState == null)
            return;

        IReadOnlyList<JournalGoalViewData> goals = Chapter01JournalProvider.Build(gameState);

        journalMainList.Clear();
        journalOptionalList.Clear();
        journalCompletedList.Clear();

        bool hasMain = false;
        bool hasOptional = false;
        bool hasCompleted = false;

        foreach (JournalGoalViewData goal in goals)
        {
            if (goal.State == JournalGoalState.Hidden)
                continue;

            VisualElement row = CreateJournalGoalRow(goal);

            if (goal.State == JournalGoalState.Completed || goal.State == JournalGoalState.Failed)
            {
                journalCompletedList.Add(row);
                hasCompleted = true;
            }
            else if (goal.Category == JournalGoalCategory.Main)
            {
                journalMainList.Add(row);
                hasMain = true;
            }
            else
            {
                journalOptionalList.Add(row);
                hasOptional = true;
            }
        }

        journalMainSection.style.display = hasMain ? DisplayStyle.Flex : DisplayStyle.None;
        journalOptionalSection.style.display = hasOptional ? DisplayStyle.Flex : DisplayStyle.None;
        journalCompletedSection.style.display = hasCompleted ? DisplayStyle.Flex : DisplayStyle.None;

        RefreshJournalDetailPanel(FindJournalGoal(goals, selectedJournalGoalId));
    }

    private void RefreshJournalDetailPanel(JournalGoalViewData selected)
    {
        if (selected == null)
        {
            selectedJournalGoalId = null;
            journalDetailCategory.text = "ВЫБЕРИТЕ ЦЕЛЬ";
            journalDetailTitle.text = string.Empty;
            journalDetailDescription.text = string.Empty;
            journalDetailCurrentStep.text = string.Empty;
            return;
        }

        journalDetailCategory.text = selected.Category == JournalGoalCategory.Main
            ? "ОСНОВНАЯ ЦЕЛЬ"
            : "ДОПОЛНИТЕЛЬНАЯ ЦЕЛЬ";
        journalDetailTitle.text = selected.Title;
        journalDetailDescription.text = selected.Description;
        journalDetailCurrentStep.text = selected.CurrentStep;
    }

    // Кнопка «Журнал •» — минимальная индикация, не полноценная
    // notification-система (раздел 28 инструкции). Вызывается из общего
    // refresh независимо от того, открыт ли сам журнал.
    private void RefreshJournalNotificationState()
    {
        if (journalNavButton == null || gameState == null)
            return;

        IReadOnlyList<JournalGoalViewData> goals = Chapter01JournalProvider.Build(gameState);
        bool hasUnseen = false;

        foreach (JournalGoalViewData goal in goals)
        {
            if (goal.State == JournalGoalState.Hidden)
                continue;
            if (!seenJournalRevisionIds.Contains(goal.RevisionId))
            {
                hasUnseen = true;
                break;
            }
        }

        journalNavButton.text = hasUnseen ? "Журнал •" : "Журнал";
    }

    private VisualElement CreateJournalGoalRow(JournalGoalViewData goal)
    {
        bool isSelected = string.Equals(goal.Id, selectedJournalGoalId, StringComparison.Ordinal);

        VisualElement row = new VisualElement { name = "journal-goal-row-" + goal.Id };
        row.style.paddingLeft = 6f;
        row.style.paddingRight = 6f;
        row.style.paddingTop = 6f;
        row.style.paddingBottom = 6f;
        row.style.marginBottom = 4f;
        row.style.backgroundColor = isSelected ? HeroScreenPanelDeep : Color.clear;
        SetHeroScreenRadius(row, 3f);

        VisualElement titleRow = new VisualElement();
        titleRow.style.flexDirection = FlexDirection.Row;
        titleRow.style.justifyContent = Justify.SpaceBetween;
        titleRow.style.alignItems = Align.Center;
        row.Add(titleRow);

        Label title = new Label((isSelected ? "▶ " : "") + goal.Title) { name = "journal-goal-row-title" };
        title.style.color = HeroScreenText;
        title.style.fontSize = 12f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.pickingMode = PickingMode.Ignore;
        titleRow.Add(title);

        // v1 использует один badge «НОВОЕ» и для впервые появившихся, и для
        // обновлённых записей (раздел 26 инструкции: различать было бы
        // точнее, но это сознательно упрощено — RevisionId уже достаточно,
        // чтобы не пропустить ни один смысловой шаг цели).
        if (!seenJournalRevisionIds.Contains(goal.RevisionId))
        {
            Label badge = new Label("НОВОЕ") { name = "journal-goal-row-badge" };
            badge.style.color = HeroScreenGold;
            badge.style.fontSize = 9f;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.pickingMode = PickingMode.Ignore;
            titleRow.Add(badge);
        }

        Label subtitle = new Label(goal.Category == JournalGoalCategory.Main ? "Основная" : "Дополнительная")
        {
            name = "journal-goal-row-subtitle"
        };
        subtitle.style.color = HeroScreenMuted;
        subtitle.style.fontSize = 9f;
        subtitle.pickingMode = PickingMode.Ignore;
        row.Add(subtitle);

        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            SelectJournalGoal(goal);
            evt.StopPropagation();
        });

        return row;
    }

    // Отметка «прочитано» — только по явному клику на конкретную запись
    // (раздел 27 инструкции), не при простом открытии журнала.
    private void SelectJournalGoal(JournalGoalViewData goal)
    {
        selectedJournalGoalId = goal.Id;
        seenJournalRevisionIds.Add(goal.RevisionId);

        RefreshJournal();
        RefreshJournalNotificationState();
    }

    private static JournalGoalViewData FindJournalGoal(IReadOnlyList<JournalGoalViewData> goals, string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        foreach (JournalGoalViewData goal in goals)
        {
            if (string.Equals(goal.Id, id, StringComparison.Ordinal))
                return goal;
        }
        return null;
    }
}
