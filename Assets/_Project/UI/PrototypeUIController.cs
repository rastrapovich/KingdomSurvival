using System;
using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public partial class PrototypeUIController : MonoBehaviour
{
    private enum MainScreen
    {
        Capital,
        Expeditions
    }

    private const int MaxIncidentNotificationButtons = 6;
    private const float NavigationClickCooldownSeconds = 0.18f;

    private GameState gameState;
    private MainScreen? openedScreen;
    private float lastNavigationClickTime = -NavigationClickCooldownSeconds;
    private bool callbacksRegistered;
    private bool isGameOver;
    private VisualElement interfaceRoot;

    private Button navCapitalButton;
    private Button navExpeditionsButton;
    private VisualElement capitalScreen;
    private VisualElement expeditionsScreen;

    private Label dayLabel;
    private Label goldLabel;
    private Label goldIncomeLabel;
    private Label foodLabel;
    private Label foodIncomeLabel;
    private Label populationLabel;
    private Label moodLabel;
    private Label foodConsumptionLabel;
    private Button timeToggleButton;

    private Button goldMinus10Button;
    private Button goldPlus10Button;
    private Button foodMinus10Button;
    private Button foodPlus10Button;
    private Button populationMinus10Button;
    private Button populationPlus10Button;
    private Button moodMinus10Button;
    private Button moodPlus10Button;

    private readonly HashSet<string> selectedFighterIds =
        new HashSet<string>();

    private Label expeditionStatusLabel;
    private VisualElement activeExpeditionCard;
    private Label activeExpeditionTitle;
    private Label activeExpeditionDetails;
    private Button researchExpeditionButton;
    private Button returnExpeditionButton;

    private ScrollView reportHistoryScroll;
    private Label reportHistoryLabel;
    private readonly List<string> reportHistory = new List<string>();
    private readonly List<bool> reportRequiresAcknowledgement = new List<bool>();
    private readonly List<bool> reportReadStates = new List<bool>();

    private VisualElement incidentNotificationStack;
    private VisualElement incidentModalOverlay;
    private Label incidentModalTitle;
    private Label incidentModalDescription;
    private Label incidentModalConsequence;
    private Button incidentUnderstoodButton;
    private VisualElement incidentModalTextColumn;
    private Button decisionOptionAButton;
    private Button decisionOptionBButton;

    private VisualElement gameOverOverlay;
    private Label gameOverDaysLabel;
    private Button restartGameButton;

    private readonly List<ExpeditionIncidentOccurrence> unreadIncidents =
        new List<ExpeditionIncidentOccurrence>();

    private ExpeditionIncidentOccurrence openedIncident;
    private ExpeditionDecisionOccurrence openedDecision;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;

        FindInterfaceElements(root);

        if (!AllRequiredElementsExist())
        {
            Debug.LogError(
                "PrototypeUIController: в Prototype_Main.uxml отсутствуют необходимые элементы.");
            enabled = false;
            return;
        }

        reportHistoryLabel.enableRichText = true;

        // Мост кампании с базой существ/бойцов: боевые агрегаты GameState
        // (TotalArmyDefensePower и т.п.) начинают читать настоящие
        // характеристики из UnitDatabase вместо legacy-полей прототипа.
        if (GameState.UnitStatsProvider == null)
            GameState.UnitStatsProvider = new KingdomSurvival.UnitDatabase.UnitDatabaseStatsProvider();

        CreateDecisionChoiceButtons();
        RegisterCallbacks();
        StartNewGame();
        InitializeHeroScreenUi();
        InitializeJournalUi();
        InitializeCampUi();
        InitializeNarrativeDialogueUi();
        InitializeUILayoutScreens();
    }

    private void FindInterfaceElements(VisualElement root)
    {
        interfaceRoot = root;
        navCapitalButton = root.Q<Button>("nav-capital-button");
        navExpeditionsButton = root.Q<Button>("nav-expeditions-button");

        capitalScreen = root.Q<VisualElement>("capital-screen");
        expeditionsScreen = root.Q<VisualElement>("expeditions-screen");

        dayLabel = root.Q<Label>("day-label");
        goldLabel = root.Q<Label>("gold-label");
        goldIncomeLabel = root.Q<Label>("gold-income-label");
        foodLabel = root.Q<Label>("food-label");
        foodIncomeLabel = root.Q<Label>("food-income-label");
        populationLabel = root.Q<Label>("population-label");
        moodLabel = root.Q<Label>("mood-label");
        foodConsumptionLabel = root.Q<Label>("food-consumption-label");
        timeToggleButton = root.Q<Button>("time-toggle-button");

        goldMinus10Button = root.Q<Button>("gold-minus10-button");
        goldPlus10Button = root.Q<Button>("gold-plus10-button");
        foodMinus10Button = root.Q<Button>("food-minus10-button");
        foodPlus10Button = root.Q<Button>("food-plus10-button");
        populationMinus10Button = root.Q<Button>("population-minus10-button");
        populationPlus10Button = root.Q<Button>("population-plus10-button");
        moodMinus10Button = root.Q<Button>("mood-minus10-button");
        moodPlus10Button = root.Q<Button>("mood-plus10-button");

        expeditionStatusLabel = root.Q<Label>("expedition-status-label");
        FindWorldMapElements(root);

        activeExpeditionCard = root.Q<VisualElement>("active-expedition-card");
        activeExpeditionTitle = root.Q<Label>("active-expedition-title");
        activeExpeditionDetails = root.Q<Label>("active-expedition-details");
        researchExpeditionButton = root.Q<Button>("research-expedition-button");
        returnExpeditionButton = root.Q<Button>("return-expedition-button");

        reportHistoryScroll = root.Q<ScrollView>("report-history-scroll");
        reportHistoryLabel = root.Q<Label>("report-history-label");

        incidentNotificationStack =
            root.Q<VisualElement>("incident-notification-stack");
        incidentModalOverlay =
            root.Q<VisualElement>("incident-modal-overlay");
        incidentModalTitle = root.Q<Label>("incident-modal-title");
        incidentModalDescription = root.Q<Label>("incident-modal-description");
        incidentModalConsequence = root.Q<Label>("incident-modal-consequence");
        incidentUnderstoodButton = root.Q<Button>("incident-understood-button");
        incidentModalTextColumn =
            root.Q<VisualElement>(className: "incident-modal-text-column");

        gameOverOverlay = root.Q<VisualElement>("game-over-overlay");
        gameOverDaysLabel = root.Q<Label>("game-over-days-label");
        restartGameButton = root.Q<Button>("restart-game-button");
    }

    private bool AllRequiredElementsExist()
    {
        return
            navCapitalButton != null &&
            navExpeditionsButton != null &&
            capitalScreen != null &&
            expeditionsScreen != null &&
            dayLabel != null &&
            goldLabel != null &&
            goldIncomeLabel != null &&
            foodLabel != null &&
            foodIncomeLabel != null &&
            populationLabel != null &&
            moodLabel != null &&
            foodConsumptionLabel != null &&
            timeToggleButton != null &&
            goldMinus10Button != null &&
            goldPlus10Button != null &&
            foodMinus10Button != null &&
            foodPlus10Button != null &&
            populationMinus10Button != null &&
            populationPlus10Button != null &&
            moodMinus10Button != null &&
            moodPlus10Button != null &&
            expeditionStatusLabel != null &&
            WorldMapElementsExist() &&
            activeExpeditionCard != null &&
            activeExpeditionTitle != null &&
            activeExpeditionDetails != null &&
            researchExpeditionButton != null &&
            returnExpeditionButton != null &&
            reportHistoryScroll != null &&
            reportHistoryLabel != null &&
            incidentNotificationStack != null &&
            incidentModalOverlay != null &&
            incidentModalTitle != null &&
            incidentModalDescription != null &&
            incidentModalConsequence != null &&
            incidentUnderstoodButton != null &&
            incidentModalTextColumn != null &&
            gameOverOverlay != null &&
            gameOverDaysLabel != null &&
            restartGameButton != null;
    }

    private void CreateDecisionChoiceButtons()
    {
        decisionOptionAButton = new Button(OnDecisionOptionAClicked);
        decisionOptionAButton.AddToClassList("incident-understood-button");
        decisionOptionAButton.text = "Вариант 1";
        decisionOptionAButton.style.width = 360;
        decisionOptionAButton.style.height = 54;
        decisionOptionAButton.style.alignSelf = Align.FlexStart;

        decisionOptionBButton = new Button(OnDecisionOptionBClicked);
        decisionOptionBButton.AddToClassList("incident-understood-button");
        decisionOptionBButton.text = "Вариант 2";
        decisionOptionBButton.style.width = 360;
        decisionOptionBButton.style.height = 54;
        decisionOptionBButton.style.alignSelf = Align.FlexStart;

        incidentModalTextColumn.Add(decisionOptionAButton);
        incidentModalTextColumn.Add(decisionOptionBButton);
    }

    private void StartNewGame()
    {
        gameState = new GameState();
        WorldMapDatabaseAsset mapDatabase = WorldMapVisualRuntime.LoadDatabase();
        gameState.CreateNewGame(
            null,
            mapDatabase != null
                ? mapDatabase.BuildRuntimeLocationTemplates()
                : null);

        if (quickExpeditionPopup != null)
            BindQuickExpeditionPopup();

        isGameOver = false;
        lastNavigationClickTime = -NavigationClickCooldownSeconds;
        unreadIncidents.Clear();
        reportHistory.Clear();
        reportRequiresAcknowledgement.Clear();
        reportReadStates.Clear();
        selectedFighterIds.Clear();
        // P08J: «НОВОЕ» — session-only UI-пометка, не сюжетный прогресс
        // (раздел 29 инструкции) — на новой игре сбрасывается вместе с
        // остальным сеансовым состоянием интерфейса.
        seenJournalRevisionIds.Clear();
        selectedJournalGoalId = null;
        ClearQueuedModals();
        ResetWorldMapSelection();
        reportHistoryLabel.text = string.Empty;

        HideIncidentModal();
        HideGameOver();

        AddReport(
            "Прототип запущен. Выберите цель на карте экспедиций — герой " +
            "отправится в путь с текущим составом отряда.");

        CloseMainScreen();
        RefreshInterface();
    }

    private void ToggleScreen(MainScreen screen)
    {
        if (openedScreen.HasValue && openedScreen.Value == screen)
        {
            CloseMainScreen();
            return;
        }

        OpenScreen(screen);
    }

    private void OpenScreen(MainScreen screen)
    {
        openedScreen = screen;

        capitalScreen.style.display =
            screen == MainScreen.Capital ? DisplayStyle.Flex : DisplayStyle.None;
        expeditionsScreen.style.display =
            screen == MainScreen.Expeditions ? DisplayStyle.Flex : DisplayStyle.None;

        SetNavigationButtonActive(navCapitalButton, screen == MainScreen.Capital);
        SetNavigationButtonActive(navExpeditionsButton, screen == MainScreen.Expeditions);
    }

    private void CloseMainScreen()
    {
        openedScreen = null;
        capitalScreen.style.display = DisplayStyle.None;
        expeditionsScreen.style.display = DisplayStyle.None;

        SetNavigationButtonActive(navCapitalButton, false);
        SetNavigationButtonActive(navExpeditionsButton, false);
    }

    private void SetNavigationButtonActive(Button button, bool isActive)
    {
        if (isActive)
            button.AddToClassList("nav-button-active");
        else
            button.RemoveFromClassList("nav-button-active");
    }

    private bool CanProcessNavigationClick()
    {
        float currentTime = Time.unscaledTime;

        if (currentTime - lastNavigationClickTime < NavigationClickCooldownSeconds)
            return false;

        lastNavigationClickTime = currentTime;
        return true;
    }

    private void OnCapitalNavigationClicked()
    {
        if (CanProcessNavigationClick())
            ToggleScreen(MainScreen.Capital);
    }

    private void OnExpeditionsNavigationClicked()
    {
        if (CanProcessNavigationClick())
            ToggleScreen(MainScreen.Expeditions);
    }

    private void RegisterCallbacks()
    {
        navCapitalButton.clicked += OnCapitalNavigationClicked;
        navExpeditionsButton.clicked += OnExpeditionsNavigationClicked;

        goldMinus10Button.clicked += OnGoldMinus10Clicked;
        goldPlus10Button.clicked += OnGoldPlus10Clicked;
        foodMinus10Button.clicked += OnFoodMinus10Clicked;
        foodPlus10Button.clicked += OnFoodPlus10Clicked;
        populationMinus10Button.clicked += OnPopulationMinus10Clicked;
        populationPlus10Button.clicked += OnPopulationPlus10Clicked;
        moodMinus10Button.clicked += OnMoodMinus10Clicked;
        moodPlus10Button.clicked += OnMoodPlus10Clicked;

        RegisterWorldMapCallbacks();
        researchExpeditionButton.clicked += OnResearchExpeditionClicked;
        returnExpeditionButton.clicked += OnExpeditionActionClicked;
        incidentUnderstoodButton.clicked += OnIncidentUnderstoodClicked;
        restartGameButton.clicked += OnRestartGameClicked;

        callbacksRegistered = true;
    }

    private void OnDisable()
    {
        if (!callbacksRegistered)
            return;

        navCapitalButton.clicked -= OnCapitalNavigationClicked;
        navExpeditionsButton.clicked -= OnExpeditionsNavigationClicked;

        goldMinus10Button.clicked -= OnGoldMinus10Clicked;
        goldPlus10Button.clicked -= OnGoldPlus10Clicked;
        foodMinus10Button.clicked -= OnFoodMinus10Clicked;
        foodPlus10Button.clicked -= OnFoodPlus10Clicked;
        populationMinus10Button.clicked -= OnPopulationMinus10Clicked;
        populationPlus10Button.clicked -= OnPopulationPlus10Clicked;
        moodMinus10Button.clicked -= OnMoodMinus10Clicked;
        moodPlus10Button.clicked -= OnMoodPlus10Clicked;

        UnregisterWorldMapCallbacks();
        researchExpeditionButton.clicked -= OnResearchExpeditionClicked;
        returnExpeditionButton.clicked -= OnExpeditionActionClicked;
        incidentUnderstoodButton.clicked -= OnIncidentUnderstoodClicked;
        restartGameButton.clicked -= OnRestartGameClicked;

        callbacksRegistered = false;
    }

    private void OnGoldMinus10Clicked() => AdjustGold(-10);
    private void OnGoldPlus10Clicked() => AdjustGold(10);
    private void OnFoodMinus10Clicked() => AdjustFood(-10);
    private void OnFoodPlus10Clicked() => AdjustFood(10);
    private void OnPopulationMinus10Clicked() => AdjustPopulation(-10);
    private void OnPopulationPlus10Clicked() => AdjustPopulation(10);
    private void OnMoodMinus10Clicked() => AdjustMood(-10);
    private void OnMoodPlus10Clicked() => AdjustMood(10);

    private void AdjustGold(int delta)
    {
        if (isGameOver)
            return;

        gameState.Gold = Math.Max(0, gameState.Gold + delta);
        RefreshInterface();
    }

    private void AdjustFood(int delta)
    {
        if (isGameOver)
            return;

        gameState.Food = Math.Max(0, gameState.Food + delta);
        RefreshInterface();
    }

    private void AdjustPopulation(int delta)
    {
        if (isGameOver)
            return;

        gameState.Population = Math.Max(0, gameState.Population + delta);
        RefreshInterface();
    }

    private void AdjustMood(int delta)
    {
        if (isGameOver)
            return;

        gameState.Mood = Math.Max(0, Math.Min(100, gameState.Mood + delta));
        RefreshInterface();
        CheckForDefeat();
    }

    private void OnRestartGameClicked()
    {
        StartNewGame();
    }

    private void TrySendExpedition(string locationId)
    {
        if (isGameOver)
            return;

        string resultMessage;
        List<string> selectedIds = GetSelectedFighterIdsInArmyOrder();
        gameState.TryStartExpedition(locationId, selectedIds, out resultMessage);
        AddReport(resultMessage);
        RefreshInterface();
    }

    private void OnResearchExpeditionClicked()
    {
        if (isGameOver)
            return;

        string resultMessage;
        gameState.TryStartLocationResearch(out resultMessage);
        AddReport(resultMessage);
        RefreshInterface();
    }

    private void OnExpeditionActionClicked()
    {
        string resultMessage;
        bool cancelledBeforeMovement = false;

        if (gameState.CanCancelPreparedExpedition)
        {
            cancelledBeforeMovement =
                gameState.TryCancelPreparedExpedition(out resultMessage);
        }
        else
            gameState.TryOrderReturn(out resultMessage);

        if (cancelledBeforeMovement)
            selectedFighterIds.Clear();

        AddReport(resultMessage);
        RefreshInterface();
    }

    private int AddReport(string message, int? dayOverride = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return -1;

        int reportDay = dayOverride ?? gameState.Day;
        reportHistory.Add("День " + reportDay + "\n" + message);
        reportRequiresAcknowledgement.Add(false);
        reportReadStates.Add(true);
        reportHistoryLabel.text = string.Join("\n\n", reportHistory);

        reportHistoryScroll.schedule.Execute(() =>
        {
            reportHistoryScroll.verticalScroller.value =
                reportHistoryScroll.verticalScroller.highValue;
        }).ExecuteLater(1);

        return reportHistory.Count - 1;
    }

    private void RefreshInterface()
    {
        dayLabel.text = "День: " + gameState.Day;
        goldLabel.text = "Золото: " + gameState.Gold;
        goldIncomeLabel.text = "+" + gameState.DailyGoldIncome;
        foodLabel.text = "Пища: " + gameState.Food;
        foodIncomeLabel.text = "+" + gameState.DailyFoodIncome;
        populationLabel.text = "Население: " + gameState.Population;
        moodLabel.text = "Настроение: " + gameState.Mood + "/100";
        foodConsumptionLabel.text =
            "Расход: " + gameState.DailyFoodConsumption + " в день";

        RefreshResourceTestButtons();
        RefreshHeroScreenSupplyPanel();
        RefreshExpeditionPanel();
        RefreshIncidentNotifications();

        // P08J: Narrative-поток (завершение N09/N10) обновляет интерфейс
        // через этот метод, не через RefreshStableUiAfterStateChange — без
        // этого кнопка «Журнал •» отставала бы до следующего несвязанного
        // действия игрока.
        RefreshJournalNotificationState();
        if (IsJournalOpen)
            RefreshJournal();

        // P09-T05: та же логика, что у кнопки «Журнал •» выше — кнопка
        // «Лагерь» и содержимое открытого экрана лагеря обязаны отражать
        // состояние сразу, а не только при следующем несвязанном клике.
        RefreshCampNavButtonState();
        if (IsCampScreenOpen)
            RefreshCampScreen();
    }

    private void RefreshResourceTestButtons()
    {
        bool available = !isGameOver;

        goldMinus10Button.SetEnabled(available && gameState.Gold > 0);
        goldPlus10Button.SetEnabled(available);
        foodMinus10Button.SetEnabled(available && gameState.Food > 0);
        foodPlus10Button.SetEnabled(available);
        populationMinus10Button.SetEnabled(available && gameState.Population > 0);
        populationPlus10Button.SetEnabled(available);
        moodMinus10Button.SetEnabled(available && gameState.Mood > 0);
        moodPlus10Button.SetEnabled(available && gameState.Mood < 100);
    }

    private List<string> GetSelectedFighterIdsInArmyOrder()
    {
        List<string> result = new List<string>();

        foreach (FighterData fighter in gameState.Fighters)
        {
            if (selectedFighterIds.Contains(fighter.Id))
                result.Add(fighter.Id);
        }

        return result;
    }

    private string GetFighterNames(List<string> fighterIds)
    {
        List<string> names = new List<string>();

        foreach (string fighterId in fighterIds)
        {
            FighterData fighter = gameState.FindFighter(fighterId);

            if (fighter != null)
                names.Add(fighter.Name);
        }

        return names.Count > 0
            ? string.Join(", ", names)
            : "—";
    }

    private string GetFighterWord(int count)
    {
        int lastTwoDigits = count % 100;

        if (lastTwoDigits >= 11 && lastTwoDigits <= 14)
            return "бойцов";

        switch (count % 10)
        {
            case 1:
                return "боец";
            case 2:
            case 3:
            case 4:
                return "бойца";
            default:
                return "бойцов";
        }
    }

    private void RefreshExpeditionPanel()
    {
        bool expeditionActive = gameState.HasActiveExpedition;
        bool awaitingDecision = gameState.HasPendingExpeditionDecision;
        bool controlsAvailable = !isGameOver;
        bool hasSelectedFighters = selectedFighterIds.Count > 0;

        RefreshWorldMapPanel();

        activeExpeditionCard.style.display =
            expeditionActive ? DisplayStyle.Flex : DisplayStyle.None;

        if (!expeditionActive)
        {
            expeditionStatusLabel.text = hasSelectedFighters
                ? "Подготовка экспедиции: выбрано бойцов — " +
                  selectedFighterIds.Count + ". Выберите цель на карте."
                : "Активная экспедиция: нет. Выберите цель на карте — герой " +
                  "может отправиться и один.";
            researchExpeditionButton.style.display = DisplayStyle.None;
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        LocationData location = gameState.FindLocation(expedition.LocationId);
        CommanderData commander = gameState.FindCommander(expedition.CommanderId);
        bool exploring = expedition.IsLocationResearchInProgress;
        bool researchImplemented = location != null && location.ExplorationHours > 0;
        string targetName = location != null
            ? location.TravelTargetName
            : expedition.IsScoutingTarget ? "точка разведки" : "выбранный сектор";

        string stateText = awaitingDecision
            ? "ожидает приказа"
            : exploring
                ? "исследует локацию"
                : expedition.IsRoadStopInProgress
                    ? expedition.ActiveActivity.DisplayName.ToLowerInvariant()
                : GetCommanderStateText(expedition.Phase);

        expeditionStatusLabel.text =
            "Активная экспедиция: " + commander.Name + " · " +
            targetName + " · " + stateText;

        activeExpeditionTitle.text =
            "ЭКСПЕДИЦИЯ: " + targetName.ToUpper();

        string currentTask;
        string timingInformation;

        if (awaitingDecision)
        {
            currentTask = "Ожидает приказа короля";
            timingInformation = "Время остановлено до решения.";
        }
        else if (exploring)
        {
            currentTask = "Исследовать локацию";
            timingInformation =
                "До завершения исследования: " +
                ContinuousExpeditionCommands.FormatHours(
                    expedition.ActiveActivity.RemainingHours);
        }
        else if (expedition.IsRoadStopInProgress)
        {
            currentTask = expedition.ActiveActivity.DisplayName;
            timingInformation =
                "До продолжения маршрута: " +
                ContinuousExpeditionCommands.FormatHours(
                    expedition.ActiveActivity.RemainingHours);
        }
        else if (expedition.Phase == CommanderState.TravellingToLocation)
        {
            currentTask = "Добраться до цели";
            timingInformation = "До цели: " +
                ContinuousExpeditionCommands.FormatHours(
                    ContinuousSimulationSystem.GetTravelHoursRemaining(gameState));
        }
        else if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            currentTask = "Вернуться в столицу";
            timingInformation = "До столицы: " +
                ContinuousExpeditionCommands.FormatHours(
                    ContinuousSimulationSystem.GetTravelHoursRemaining(gameState));
        }
        else if (location != null && location.IsExplored)
        {
            currentTask = "Локация исследована";
            timingInformation =
                "Расстояние до столицы: " +
                ContinuousExpeditionCommands.FormatHours(
                    location.TravelHoursFromCapital);
        }
        else if (researchImplemented)
        {
            currentTask = "Выбрать: исследовать или возвращаться";
            timingInformation =
                "Исследование займёт: " +
                ContinuousExpeditionCommands.FormatHours(
                    location.ExplorationHours);
        }
        else if (location != null)
        {
            currentTask = "Исследование этой локации пока не реализовано";
            timingInformation =
                "Расстояние до столицы: " +
                ContinuousExpeditionCommands.FormatHours(
                    location.TravelHoursFromCapital);
        }
        else
        {
            currentTask = "Разведка сектора завершена";
            timingInformation = "Можно приказать возвращаться";
        }

        activeExpeditionDetails.text =
            "Командир: " + commander.Name + "\n" +
            "Бойцы: " + GetFighterNames(expedition.FighterIds) + "\n" +
            "Сила отряда: " + gameState.ExpeditionDefensePower + "\n" +
            "Гарнизон столицы: " + gameState.GarrisonFighterCount +
            " " + GetFighterWord(gameState.GarrisonFighterCount) +
            " · оборона " + gameState.GarrisonDefensePower +
            "/" + gameState.TotalArmyDefensePower + "\n" +
            "Цель: " + targetName + "\n" +
            "Состояние: " + stateText + "\n" +
            "Текущая задача: " + currentTask + "\n" +
            timingInformation;

        RefreshResearchButton(
            controlsAvailable,
            awaitingDecision,
            expedition,
            location,
            researchImplemented);

        bool canCancel = gameState.CanCancelPreparedExpedition;
        bool alreadyReturning = expedition.Phase == CommanderState.ReturningToCastle;

        if (!controlsAvailable)
        {
            returnExpeditionButton.SetEnabled(false);
            returnExpeditionButton.text = "Партия завершена";
        }
        else if (awaitingDecision)
        {
            returnExpeditionButton.SetEnabled(false);
            returnExpeditionButton.text = "Сначала требуется приказ";
        }
        else if (exploring)
        {
            returnExpeditionButton.SetEnabled(false);
            returnExpeditionButton.text = "Идёт исследование";
        }
        else if (canCancel)
        {
            returnExpeditionButton.SetEnabled(true);
            returnExpeditionButton.text = "Отменить отправку";
        }
        else if (alreadyReturning)
        {
            returnExpeditionButton.SetEnabled(false);
            returnExpeditionButton.text = "Возвращение уже приказано";
        }
        else
        {
            returnExpeditionButton.SetEnabled(true);
            returnExpeditionButton.text = "Приказать возвращаться";
        }
    }

    private void RefreshResearchButton(
        bool controlsAvailable,
        bool awaitingDecision,
        ExpeditionData expedition,
        LocationData location,
        bool researchImplemented)
    {
        bool atLocation = expedition.Phase == CommanderState.AtLocation;
        bool shouldShow = atLocation && researchImplemented;

        researchExpeditionButton.style.display =
            shouldShow ? DisplayStyle.Flex : DisplayStyle.None;

        if (!shouldShow)
            return;

        if (!controlsAvailable)
        {
            researchExpeditionButton.SetEnabled(false);
            researchExpeditionButton.text = "Партия завершена";
        }
        else if (awaitingDecision)
        {
            researchExpeditionButton.SetEnabled(false);
            researchExpeditionButton.text = "Сначала требуется приказ";
        }
        else if (expedition.IsLocationResearchInProgress)
        {
            researchExpeditionButton.SetEnabled(false);
            researchExpeditionButton.text =
                "ИССЛЕДОВАНИЕ — " +
                ContinuousExpeditionCommands.FormatHours(
                    expedition.ActiveActivity.RemainingHours);
        }
        else if (location.IsExplored)
        {
            researchExpeditionButton.SetEnabled(false);
            researchExpeditionButton.text = "ИССЛЕДОВАНО";
        }
        else
        {
            researchExpeditionButton.SetEnabled(gameState.CanResearchActiveLocation);
            researchExpeditionButton.text = "ИССЛЕДОВАТЬ";
        }
    }

    private void RefreshIncidentNotifications()
    {
        incidentNotificationStack.Clear();

        if (isGameOver)
            return;

        bool hasDecision = gameState.HasPendingExpeditionDecision;
        int availableBackgroundSlots =
            hasDecision
                ? MaxIncidentNotificationButtons - 1
                : MaxIncidentNotificationButtons;

        int unreadCount = unreadIncidents.Count;

        if (unreadCount <= availableBackgroundSlots)
        {
            foreach (ExpeditionIncidentOccurrence occurrence in unreadIncidents)
                incidentNotificationStack.Add(CreateIncidentButton(occurrence));
        }
        else
        {
            int visibleIncidentCount = availableBackgroundSlots - 1;
            int hiddenCount = unreadCount - visibleIncidentCount;

            Button overflowButton =
                new Button(() => OpenIncident(unreadIncidents[0]));
            overflowButton.text = "+" + hiddenCount;
            overflowButton.tooltip =
                "Ещё " + hiddenCount + " непрочитанных происшествий";
            overflowButton.AddToClassList("incident-notification-button");
            overflowButton.AddToClassList("incident-overflow");
            incidentNotificationStack.Add(overflowButton);

            int firstVisibleIndex = unreadCount - visibleIncidentCount;

            for (int i = firstVisibleIndex; i < unreadCount; i++)
                incidentNotificationStack.Add(CreateIncidentButton(unreadIncidents[i]));
        }

        if (hasDecision)
        {
            Button decisionButton = new Button(
                () => OpenDecision(gameState.ActiveExpedition.PendingDecision));
            decisionButton.text = "!";
            decisionButton.tooltip = "Требуется приказ по экспедиции";
            decisionButton.AddToClassList("incident-notification-button");
            decisionButton.AddToClassList("incident-mixed");
            incidentNotificationStack.Add(decisionButton);
        }
    }

    private Button CreateIncidentButton(ExpeditionIncidentOccurrence occurrence)
    {
        Button button = new Button(() => OpenIncident(occurrence));
        button.text = string.Empty;
        button.tooltip = "Непрочитанное происшествие";
        button.AddToClassList("incident-notification-button");
        button.AddToClassList(GetIncidentToneClass(occurrence.Tone));
        return button;
    }

    private string GetIncidentToneClass(ExpeditionIncidentTone tone)
    {
        switch (tone)
        {
            case ExpeditionIncidentTone.Positive:
                return "incident-positive";
            case ExpeditionIncidentTone.Negative:
                return "incident-negative";
            case ExpeditionIncidentTone.Mixed:
                return "incident-mixed";
            default:
                return "incident-overflow";
        }
    }

    private void OpenIncident(ExpeditionIncidentOccurrence occurrence)
    {
        if (occurrence == null || isGameOver)
            return;

        PauseForBlockingModal();
        openedDecision = null;
        openedIncident = occurrence;
        incidentModalTitle.text =
            "ДЕНЬ " + occurrence.Day + " · " + occurrence.Title.ToUpper();
        incidentModalDescription.text = occurrence.Description;
        incidentModalConsequence.text =
            "Последствие: " + occurrence.ConsequenceText;

        incidentUnderstoodButton.style.display = DisplayStyle.Flex;
        decisionOptionAButton.style.display = DisplayStyle.None;
        decisionOptionBButton.style.display = DisplayStyle.None;
        incidentModalOverlay.style.display = DisplayStyle.Flex;
        RefreshTimeControlAvailability();
    }

    private void OpenDecision(ExpeditionDecisionOccurrence occurrence)
    {
        if (occurrence == null || isGameOver)
            return;

        PauseForBlockingModal();
        openedIncident = null;
        openedDecision = occurrence;

        incidentModalTitle.text =
            "ДЕНЬ " + occurrence.Day + " · " + occurrence.Title.ToUpper();
        incidentModalDescription.text = occurrence.Description;
        incidentModalConsequence.text =
            "Требуется приказ. Экспедиция не будет продвигаться, пока решение не принято.";

        decisionOptionAButton.text =
            occurrence.OptionA.Label + "\n" + occurrence.OptionA.ConsequencePreview;
        decisionOptionBButton.text =
            occurrence.OptionB.Label + "\n" + occurrence.OptionB.ConsequencePreview;

        decisionOptionAButton.SetEnabled(
            ExpeditionDecisionSystem.CanChooseOption(
                gameState,
                occurrence.OptionA.Id));
        decisionOptionBButton.SetEnabled(
            ExpeditionDecisionSystem.CanChooseOption(
                gameState,
                occurrence.OptionB.Id));

        incidentUnderstoodButton.style.display = DisplayStyle.None;
        decisionOptionAButton.style.display = DisplayStyle.Flex;
        decisionOptionBButton.style.display = DisplayStyle.Flex;
        incidentModalOverlay.style.display = DisplayStyle.Flex;
        RefreshTimeControlAvailability();
    }

    private void OnIncidentUnderstoodClicked()
    {
        if (activeQueuedModal != null && activeQueuedModal.Decision == null)
        {
            FinishActiveQueuedModal();
            return;
        }

        if (openedIncident == null)
        {
            HideIncidentModal();
            ResumeAfterBlockingModalIfReady();
            return;
        }

        int readIncidentId = openedIncident.Id;

        unreadIncidents.RemoveAll(
            occurrence => occurrence.Id == readIncidentId);

        AcknowledgeIncidentReport(readIncidentId);

        HideIncidentModal();
        RefreshIncidentNotifications();
        ResumeAfterBlockingModalIfReady();
    }

    private void OnDecisionOptionAClicked()
    {
        if (openedDecision == null)
            return;

        ResolveOpenedDecisionChoice(openedDecision.OptionA.Id);
    }

    private void OnDecisionOptionBClicked()
    {
        if (openedDecision == null)
            return;

        ResolveOpenedDecisionChoice(openedDecision.OptionB.Id);
    }

    private void ResolveOpenedDecisionChoice(string optionId)
    {
        string resultMessage;
        ExpeditionReturnSnapshot returnSnapshot = CaptureExpeditionReturnSnapshot();

        if (!ExpeditionDecisionSystem.TryApplyChoice(
                gameState,
                optionId,
                out resultMessage))
        {
            incidentModalConsequence.text = resultMessage;
            return;
        }

        int resultReportIndex = AddReport(resultMessage);
        StrategicSimulationResult decisionResult = new StrategicSimulationResult();
        AddReturnNoticeIfCompleted(decisionResult, returnSnapshot);

        if (decisionResult.ExpeditionReturnNotice != null)
        {
            QueueNotice(decisionResult.ExpeditionReturnNotice, resultReportIndex);
            MarkReportUnread(resultReportIndex);
        }

        if (activeQueuedModal != null)
            FinishActiveQueuedModal();
        else
            HideIncidentModal();

        RefreshInterface();
        TryShowNextQueuedModal();
        ResumeAfterBlockingModalIfReady();
    }

    private void HideIncidentModal()
    {
        openedIncident = null;
        openedDecision = null;

        if (decisionOptionAButton != null)
            decisionOptionAButton.style.display = DisplayStyle.None;

        if (decisionOptionBButton != null)
            decisionOptionBButton.style.display = DisplayStyle.None;

        if (incidentUnderstoodButton != null)
            incidentUnderstoodButton.style.display = DisplayStyle.Flex;

        incidentModalOverlay.style.display = DisplayStyle.None;
    }

    private void CheckForDefeat()
    {
        if (isGameOver || gameState.Mood > 0)
            return;

        isGameOver = true;
        int survivedDays = Math.Max(0, gameState.Day - 1);

        HideIncidentModal();
        ClearQueuedModals();
        timeToggleButton.SetEnabled(false);
        gameOverDaysLabel.text =
            "Вы удерживали трон: " + survivedDays + " " + GetDayWord(survivedDays);
        gameOverOverlay.style.display = DisplayStyle.Flex;

        RefreshResourceTestButtons();
        RefreshExpeditionPanel();
        RefreshIncidentNotifications();
    }

    private void HideGameOver()
    {
        isGameOver = false;
        timeToggleButton.SetEnabled(true);
        gameOverOverlay.style.display = DisplayStyle.None;
    }

    private string GetDayWord(int value)
    {
        int lastTwoDigits = value % 100;

        if (lastTwoDigits >= 11 && lastTwoDigits <= 14)
            return "дней";

        switch (value % 10)
        {
            case 1:
                return "день";
            case 2:
            case 3:
            case 4:
                return "дня";
            default:
                return "дней";
        }
    }

    private string GetCommanderStateText(CommanderState state)
    {
        switch (state)
        {
            case CommanderState.InCastle:
                return "в замке";
            case CommanderState.TravellingToLocation:
                return "в пути к цели";
            case CommanderState.AtLocation:
                return "действует в локации";
            case CommanderState.ReturningToCastle:
                return "возвращается в замок";
            default:
                return "состояние неизвестно";
        }
    }
}
