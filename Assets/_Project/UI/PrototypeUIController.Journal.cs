using System;
using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Журнал целей (P08J, UI-M02) — read-only fullscreen-слой поверх
/// Chapter01JournalProvider.Build. Первый экран, мигрированный на
/// ProjectDocs/UI_ARCHITECTURE.md после Camp: постоянная структура —
/// Prototype_Main.uxml (journal-overlay), общий стиль — Prototype_
/// SharedPanels.uss (.ks-*) + Journal.uss, повторяемая строка цели —
/// Assets/_Project/UI/Templates/Resources/Templates/JournalGoalRow.uxml.
/// Controller здесь только находит уже существующие элементы через
/// BindRequiredElement и наполняет их данными — не создаёт постоянное
/// дерево через `new VisualElement`.
///
/// Журнал ничего не решает и не хранит о сюжетном прогрессе: он полностью
/// пересобирается из GameState/NarrativeState при каждом RefreshJournal.
/// seenJournalRevisionIds — единственное собственное состояние экрана, и
/// это чисто UI-пометка "видел ли игрок эту редакцию текста в этой
/// сессии", а не сюжетный флаг.
/// </summary>
public partial class PrototypeUIController
{
    private const string JournalScreenName = "Журнал";

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

    private VisualTreeAsset journalGoalRowTemplate;

    private bool journalUiBound;
    private string selectedJournalGoalId;

    // Session-only: сбрасывается при перезапуске игры (раздел 29
    // инструкции). Сюжетный прогресс живёт в NarrativeState отдельно и не
    // теряется — сбрасывается только декоративная отметка «НОВОЕ».
    private readonly HashSet<string> seenJournalRevisionIds = new HashSet<string>();

    private bool IsJournalOpen =>
        journalOverlay != null && journalOverlay.style.display == DisplayStyle.Flex;

    // ------------------------------------------------------------------
    // Инициализация — только поиск элементов и подписка на события.
    // ------------------------------------------------------------------

    private void InitializeJournalUi()
    {
        if (interfaceRoot == null || journalUiBound)
            return;

        journalOverlay = BindRequiredElement<VisualElement>(interfaceRoot, JournalScreenName, "journal-overlay");
        journalCloseButton = BindRequiredElement<Button>(interfaceRoot, JournalScreenName, "journal-close-button");
        journalGoalsTabButton = BindRequiredElement<Button>(interfaceRoot, JournalScreenName, "journal-tab-goals");
        journalChronicleTabButton = BindRequiredElement<Button>(interfaceRoot, JournalScreenName, "journal-tab-chronicle");

        journalMainSection = BindRequiredElement<VisualElement>(interfaceRoot, JournalScreenName, "journal-main-section");
        journalOptionalSection = BindRequiredElement<VisualElement>(interfaceRoot, JournalScreenName, "journal-optional-section");
        journalCompletedSection = BindRequiredElement<VisualElement>(interfaceRoot, JournalScreenName, "journal-completed-section");

        journalMainList = BindRequiredElement<VisualElement>(interfaceRoot, JournalScreenName, "journal-main-section-list");
        journalOptionalList = BindRequiredElement<VisualElement>(interfaceRoot, JournalScreenName, "journal-optional-section-list");
        journalCompletedList = BindRequiredElement<VisualElement>(interfaceRoot, JournalScreenName, "journal-completed-section-list");

        journalDetailCategory = BindRequiredElement<Label>(interfaceRoot, JournalScreenName, "journal-detail-panel-title");
        journalDetailTitle = BindRequiredElement<Label>(interfaceRoot, JournalScreenName, "journal-detail-title");
        journalDetailDescription = BindRequiredElement<Label>(interfaceRoot, JournalScreenName, "journal-detail-description");
        journalDetailCurrentStep = BindRequiredElement<Label>(interfaceRoot, JournalScreenName, "journal-detail-current-step");

        if (journalOverlay == null ||
            journalCloseButton == null ||
            journalGoalsTabButton == null ||
            journalChronicleTabButton == null ||
            journalMainSection == null ||
            journalOptionalSection == null ||
            journalCompletedSection == null ||
            journalMainList == null ||
            journalOptionalList == null ||
            journalCompletedList == null ||
            journalDetailCategory == null ||
            journalDetailTitle == null ||
            journalDetailDescription == null ||
            journalDetailCurrentStep == null)
            return;

        journalUiBound = true;

        journalCloseButton.clicked += CloseJournal;
        // Хроника — отдельный будущий раздел (раздел 18 инструкции): вкладка
        // существует в UXML и задизейблена (SetEnabled(false) + tooltip уже
        // заданы там же), а не подменяется «КОРОЛЕВСКИМИ ДОНЕСЕНИЯМИ» (другая,
        // исторически накопившаяся система UI). У неё нет обработчика клика —
        // она никогда не будет доступна для нажатия, пока не включена явно.
        journalChronicleTabButton.SetEnabled(false);

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
        // P09-T05 расширяет это правило на Camp — тот же принцип, третий слой.
        CloseHeroScreen();
        CloseCampScreen();

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
    // Наполнение данными
    // ------------------------------------------------------------------

    // Единственная точка входа в Chapter01JournalProvider.Build — вызывается
    // при каждом открытии и при каждом относящемся к делу изменении
    // состояния, пока журнал открыт (RefreshStableUiAfterStateChange /
    // RefreshInterface). Никакого собственного Update-цикла.
    private void RefreshJournal()
    {
        if (!journalUiBound || gameState == null)
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
            if (row == null)
                continue;

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
        VisualTreeAsset template = LoadJournalGoalRowTemplate();
        if (template == null)
            return null;

        bool isSelected = string.Equals(goal.Id, selectedJournalGoalId, StringComparison.Ordinal);
        bool isUnseen = !seenJournalRevisionIds.Contains(goal.RevisionId);

        TemplateContainer instance = template.Instantiate();

        VisualElement row = instance.Q<VisualElement>("journal-goal-row");
        if (row != null)
            row.EnableInClassList("journal-goal-row-selected", isSelected);

        Label title = instance.Q<Label>("journal-goal-row-title");
        if (title != null)
            title.text = (isSelected ? "▶ " : "") + goal.Title;

        // v1 использует один badge «НОВОЕ» и для впервые появившихся, и для
        // обновлённых записей (раздел 26 инструкции: различать было бы
        // точнее, но это сознательно упрощено — RevisionId уже достаточно,
        // чтобы не пропустить ни один смысловой шаг цели).
        Label badge = instance.Q<Label>("journal-goal-row-badge");
        if (badge != null)
            badge.style.display = isUnseen ? DisplayStyle.Flex : DisplayStyle.None;

        Label subtitle = instance.Q<Label>("journal-goal-row-subtitle");
        if (subtitle != null)
            subtitle.text = goal.Category == JournalGoalCategory.Main ? "Основная" : "Дополнительная";

        instance.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            SelectJournalGoal(goal);
            evt.StopPropagation();
        });

        return instance;
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

    // ------------------------------------------------------------------
    // Шаблоны (Assets/_Project/UI/Templates) — раздел 19 UI_ARCHITECTURE.md.
    // ------------------------------------------------------------------

    private VisualTreeAsset LoadJournalGoalRowTemplate()
    {
        if (journalGoalRowTemplate == null)
            journalGoalRowTemplate = Resources.Load<VisualTreeAsset>("Templates/JournalGoalRow");
        return journalGoalRowTemplate;
    }
}
