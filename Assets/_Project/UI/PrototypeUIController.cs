using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public partial class PrototypeUIController : MonoBehaviour
{
    private enum MainScreen
    {
        Capital,
        Army,
        Expeditions
    }

    private const int MaxIncidentNotificationButtons = 6;
    private const float NavigationClickCooldownSeconds = 0.18f;
    private const float FighterDragThreshold = 6f;

    private GameState gameState;
    private MainScreen? openedScreen;
    private float lastNavigationClickTime = -NavigationClickCooldownSeconds;
    private bool callbacksRegistered;
    private bool isGameOver;
    private VisualElement interfaceRoot;

    private Button navCapitalButton;
    private Button navArmyButton;
    private Button navExpeditionsButton;
    private VisualElement capitalScreen;
    private VisualElement armyScreen;
    private VisualElement expeditionsScreen;

    private Label dayLabel;
    private Label goldLabel;
    private Label goldIncomeLabel;
    private Label foodLabel;
    private Label foodIncomeLabel;
    private Label populationLabel;
    private Label moodLabel;
    private Label foodConsumptionLabel;
    private Button endDayButton;

    private Button goldMinus10Button;
    private Button goldPlus10Button;
    private Button foodMinus10Button;
    private Button foodPlus10Button;
    private Button populationMinus10Button;
    private Button populationPlus10Button;
    private Button moodMinus10Button;
    private Button moodPlus10Button;

    private DropdownField commanderDropdown;
    private Label commanderDetailLabel;
    private Label armyStatusLabel;
    private Label armyGoldLabel;
    private Button armyGoldMinusButton;
    private Button armyGoldPlusButton;
    private Button supplyMinusButton;
    private Label supplyValueLabel;
    private Button supplyPlusButton;
    private Label supplyConsumptionLabel;
    private Label supplyDaysLabel;
    private Label fighterSelectionHintLabel;
    private VisualElement commanderGarrisonDropZone;
    private VisualElement commanderGarrisonList;
    private Label commanderGarrisonSummaryLabel;
    private Label commanderGarrisonEmptyLabel;
    private VisualElement capitalGarrisonDropZone;
    private VisualElement capitalGarrisonList;
    private Label capitalGarrisonSummaryLabel;
    private Label capitalGarrisonEmptyLabel;

    private readonly HashSet<string> selectedFighterIds =
        new HashSet<string>();

    private string draggedFighterId;
    private int draggedFighterPointerId = -1;
    private Vector2 fighterDragStartPosition;
    private bool fighterDragStarted;
    private VisualElement draggedFighterCard;
    private VisualElement fighterDragGhost;

    private Label expeditionStatusLabel;
    private VisualElement activeExpeditionCard;
    private Label activeExpeditionTitle;
    private Label activeExpeditionDetails;
    private Button researchExpeditionButton;
    private Button returnExpeditionButton;

    private ScrollView reportHistoryScroll;
    private Label reportHistoryLabel;
    private readonly List<string> reportHistory = new List<string>();

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

        CreateDecisionChoiceButtons();
        RegisterCallbacks();
        StartNewGame();
    }

    private void FindInterfaceElements(VisualElement root)
    {
        interfaceRoot = root;
        navCapitalButton = root.Q<Button>("nav-capital-button");
        navArmyButton = root.Q<Button>("nav-army-button");
        navExpeditionsButton = root.Q<Button>("nav-expeditions-button");

        capitalScreen = root.Q<VisualElement>("capital-screen");
        armyScreen = root.Q<VisualElement>("army-screen");
        expeditionsScreen = root.Q<VisualElement>("expeditions-screen");

        dayLabel = root.Q<Label>("day-label");
        goldLabel = root.Q<Label>("gold-label");
        goldIncomeLabel = root.Q<Label>("gold-income-label");
        foodLabel = root.Q<Label>("food-label");
        foodIncomeLabel = root.Q<Label>("food-income-label");
        populationLabel = root.Q<Label>("population-label");
        moodLabel = root.Q<Label>("mood-label");
        foodConsumptionLabel = root.Q<Label>("food-consumption-label");
        endDayButton = root.Q<Button>("end-day-button");

        goldMinus10Button = root.Q<Button>("gold-minus10-button");
        goldPlus10Button = root.Q<Button>("gold-plus10-button");
        foodMinus10Button = root.Q<Button>("food-minus10-button");
        foodPlus10Button = root.Q<Button>("food-plus10-button");
        populationMinus10Button = root.Q<Button>("population-minus10-button");
        populationPlus10Button = root.Q<Button>("population-plus10-button");
        moodMinus10Button = root.Q<Button>("mood-minus10-button");
        moodPlus10Button = root.Q<Button>("mood-plus10-button");

        commanderDropdown = root.Q<DropdownField>("commander-dropdown");
        commanderDetailLabel = root.Q<Label>("commander-detail-label");
        armyStatusLabel = root.Q<Label>("army-status-label");
        armyGoldLabel = root.Q<Label>("army-gold-label");
        armyGoldMinusButton = root.Q<Button>("army-gold-minus-button");
        armyGoldPlusButton = root.Q<Button>("army-gold-plus-button");

        supplyMinusButton = root.Q<Button>("supply-minus-button");
        supplyValueLabel = root.Q<Label>("supply-value-label");
        supplyPlusButton = root.Q<Button>("supply-plus-button");
        supplyConsumptionLabel = root.Q<Label>("supply-consumption-label");
        supplyDaysLabel = root.Q<Label>("supply-days-label");
        fighterSelectionHintLabel =
            root.Q<Label>("fighter-selection-hint-label");
        commanderGarrisonDropZone =
            root.Q<VisualElement>("commander-garrison-drop-zone");
        commanderGarrisonList =
            root.Q<VisualElement>("commander-garrison-list");
        commanderGarrisonSummaryLabel =
            root.Q<Label>("commander-garrison-summary-label");
        commanderGarrisonEmptyLabel =
            root.Q<Label>("commander-garrison-empty-label");
        capitalGarrisonDropZone =
            root.Q<VisualElement>("capital-garrison-drop-zone");
        capitalGarrisonList =
            root.Q<VisualElement>("capital-garrison-list");
        capitalGarrisonSummaryLabel =
            root.Q<Label>("capital-garrison-summary-label");
        capitalGarrisonEmptyLabel =
            root.Q<Label>("capital-garrison-empty-label");

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
            navArmyButton != null &&
            navExpeditionsButton != null &&
            capitalScreen != null &&
            armyScreen != null &&
            expeditionsScreen != null &&
            dayLabel != null &&
            goldLabel != null &&
            goldIncomeLabel != null &&
            foodLabel != null &&
            foodIncomeLabel != null &&
            populationLabel != null &&
            moodLabel != null &&
            foodConsumptionLabel != null &&
            endDayButton != null &&
            goldMinus10Button != null &&
            goldPlus10Button != null &&
            foodMinus10Button != null &&
            foodPlus10Button != null &&
            populationMinus10Button != null &&
            populationPlus10Button != null &&
            moodMinus10Button != null &&
            moodPlus10Button != null &&
            commanderDropdown != null &&
            commanderDetailLabel != null &&
            armyStatusLabel != null &&
            armyGoldLabel != null &&
            armyGoldMinusButton != null &&
            armyGoldPlusButton != null &&
            supplyMinusButton != null &&
            supplyValueLabel != null &&
            supplyPlusButton != null &&
            supplyConsumptionLabel != null &&
            supplyDaysLabel != null &&
            fighterSelectionHintLabel != null &&
            commanderGarrisonDropZone != null &&
            commanderGarrisonList != null &&
            commanderGarrisonSummaryLabel != null &&
            commanderGarrisonEmptyLabel != null &&
            capitalGarrisonDropZone != null &&
            capitalGarrisonList != null &&
            capitalGarrisonSummaryLabel != null &&
            capitalGarrisonEmptyLabel != null &&
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
        CleanupFighterDrag();
        gameState = new GameState();
        gameState.CreateNewGame();

        isGameOver = false;
        lastNavigationClickTime = -NavigationClickCooldownSeconds;
        unreadIncidents.Clear();
        reportHistory.Clear();
        selectedFighterIds.Clear();
        ResetWorldMapSelection();
        reportHistoryLabel.text = string.Empty;

        ConfigureCommanderDropdown();
        HideIncidentModal();
        HideGameOver();

        AddReport(
            "Прототип запущен. На экране «Армия» перенесите бойцов в " +
            "гарнизон командира, затем выберите цель на карте экспедиций.");

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
        armyScreen.style.display =
            screen == MainScreen.Army ? DisplayStyle.Flex : DisplayStyle.None;
        expeditionsScreen.style.display =
            screen == MainScreen.Expeditions ? DisplayStyle.Flex : DisplayStyle.None;

        SetNavigationButtonActive(navCapitalButton, screen == MainScreen.Capital);
        SetNavigationButtonActive(navArmyButton, screen == MainScreen.Army);
        SetNavigationButtonActive(navExpeditionsButton, screen == MainScreen.Expeditions);
    }

    private void CloseMainScreen()
    {
        openedScreen = null;
        capitalScreen.style.display = DisplayStyle.None;
        armyScreen.style.display = DisplayStyle.None;
        expeditionsScreen.style.display = DisplayStyle.None;

        SetNavigationButtonActive(navCapitalButton, false);
        SetNavigationButtonActive(navArmyButton, false);
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

    private void OnArmyNavigationClicked()
    {
        if (CanProcessNavigationClick())
            ToggleScreen(MainScreen.Army);
    }

    private void OnExpeditionsNavigationClicked()
    {
        if (CanProcessNavigationClick())
            ToggleScreen(MainScreen.Expeditions);
    }

    private void ConfigureCommanderDropdown()
    {
        commanderDropdown.choices = gameState.GetCommanderNames();
        CommanderData selectedCommander = gameState.GetSelectedCommander();
        commanderDropdown.SetValueWithoutNotify(selectedCommander.Name);
    }

    private void RegisterCallbacks()
    {
        navCapitalButton.clicked += OnCapitalNavigationClicked;
        navArmyButton.clicked += OnArmyNavigationClicked;
        navExpeditionsButton.clicked += OnExpeditionsNavigationClicked;
        endDayButton.clicked += OnEndDayClicked;

        goldMinus10Button.clicked += OnGoldMinus10Clicked;
        goldPlus10Button.clicked += OnGoldPlus10Clicked;
        foodMinus10Button.clicked += OnFoodMinus10Clicked;
        foodPlus10Button.clicked += OnFoodPlus10Clicked;
        populationMinus10Button.clicked += OnPopulationMinus10Clicked;
        populationPlus10Button.clicked += OnPopulationPlus10Clicked;
        moodMinus10Button.clicked += OnMoodMinus10Clicked;
        moodPlus10Button.clicked += OnMoodPlus10Clicked;

        armyGoldMinusButton.clicked += OnArmyGoldMinusClicked;
        armyGoldPlusButton.clicked += OnArmyGoldPlusClicked;
        supplyMinusButton.clicked += OnSupplyMinusClicked;
        supplyPlusButton.clicked += OnSupplyPlusClicked;

        RegisterWorldMapCallbacks();
        researchExpeditionButton.clicked += OnResearchExpeditionClicked;
        returnExpeditionButton.clicked += OnExpeditionActionClicked;
        incidentUnderstoodButton.clicked += OnIncidentUnderstoodClicked;
        restartGameButton.clicked += OnRestartGameClicked;

        commanderDropdown.RegisterValueChangedCallback(OnCommanderChanged);
        callbacksRegistered = true;
    }

    private void OnDisable()
    {
        CleanupFighterDrag();

        if (!callbacksRegistered)
            return;

        navCapitalButton.clicked -= OnCapitalNavigationClicked;
        navArmyButton.clicked -= OnArmyNavigationClicked;
        navExpeditionsButton.clicked -= OnExpeditionsNavigationClicked;
        endDayButton.clicked -= OnEndDayClicked;

        goldMinus10Button.clicked -= OnGoldMinus10Clicked;
        goldPlus10Button.clicked -= OnGoldPlus10Clicked;
        foodMinus10Button.clicked -= OnFoodMinus10Clicked;
        foodPlus10Button.clicked -= OnFoodPlus10Clicked;
        populationMinus10Button.clicked -= OnPopulationMinus10Clicked;
        populationPlus10Button.clicked -= OnPopulationPlus10Clicked;
        moodMinus10Button.clicked -= OnMoodMinus10Clicked;
        moodPlus10Button.clicked -= OnMoodPlus10Clicked;

        armyGoldMinusButton.clicked -= OnArmyGoldMinusClicked;
        armyGoldPlusButton.clicked -= OnArmyGoldPlusClicked;
        supplyMinusButton.clicked -= OnSupplyMinusClicked;
        supplyPlusButton.clicked -= OnSupplyPlusClicked;

        UnregisterWorldMapCallbacks();
        researchExpeditionButton.clicked -= OnResearchExpeditionClicked;
        returnExpeditionButton.clicked -= OnExpeditionActionClicked;
        incidentUnderstoodButton.clicked -= OnIncidentUnderstoodClicked;
        restartGameButton.clicked -= OnRestartGameClicked;

        commanderDropdown.UnregisterValueChangedCallback(OnCommanderChanged);
        callbacksRegistered = false;
    }

    private void OnCommanderChanged(ChangeEvent<string> changeEvent)
    {
        if (gameState.SelectCommanderByName(changeEvent.newValue))
            AddReport(changeEvent.newValue + " назначен командиром армии.");

        RefreshInterface();
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

    private void OnArmyGoldPlusClicked()
    {
        if (!isGameOver && gameState.CanAdjustArmySupply && gameState.Gold > 0)
        {
            gameState.Gold--;
            gameState.ArmyGold++;
        }

        RefreshInterface();
    }

    private void OnArmyGoldMinusClicked()
    {
        if (!isGameOver && gameState.CanAdjustArmySupply && gameState.ArmyGold > 0)
        {
            gameState.ArmyGold--;
            gameState.Gold++;
        }

        RefreshInterface();
    }

    private void OnSupplyPlusClicked()
    {
        gameState.TryAddArmySupply();
        RefreshInterface();
    }

    private void OnSupplyMinusClicked()
    {
        gameState.TryRemoveArmySupply();
        RefreshInterface();
    }

    private void OnEndDayClicked()
    {
        if (isGameOver)
            return;

        bool expeditionWasActive = gameState.HasActiveExpedition;
        int finishedDay = gameState.Day;
        DayResolutionResult result = DayResolver.ResolveDay(gameState);

        if (expeditionWasActive && !gameState.HasActiveExpedition)
            selectedFighterIds.Clear();

        if (result.NewExpeditionIncidents.Count > 0)
            unreadIncidents.AddRange(result.NewExpeditionIncidents);

        AddReport(string.Join("\n", result.Messages), finishedDay);
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
        bool cancelledBeforeDayEnd = false;

        if (gameState.CanCancelExpeditionBeforeDayEnd)
        {
            cancelledBeforeDayEnd =
                gameState.TryCancelExpeditionBeforeDayEnd(out resultMessage);
        }
        else
            gameState.TryOrderReturn(out resultMessage);

        if (cancelledBeforeDayEnd)
            selectedFighterIds.Clear();

        AddReport(resultMessage);
        RefreshInterface();
    }

    private void AddReport(string message, int? dayOverride = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        int reportDay = dayOverride ?? gameState.Day;
        reportHistory.Add("День " + reportDay + "\n" + message);
        reportHistoryLabel.text = string.Join("\n\n", reportHistory);

        reportHistoryScroll.schedule.Execute(() =>
        {
            reportHistoryScroll.verticalScroller.value =
                reportHistoryScroll.verticalScroller.highValue;
        }).ExecuteLater(1);
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
        RefreshArmyPanel();
        RefreshExpeditionPanel();
        RefreshIncidentNotifications();
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

    private void RefreshArmyPanel()
    {
        CommanderData commander = gameState.GetSelectedCommander();

        commanderDetailLabel.text =
            "Выбран: " + commander.Name + " · " + GetCommanderStateText(commander.State);

        if (gameState.HasActiveExpedition)
        {
            armyStatusLabel.text =
                commander.Name + " и " +
                gameState.ActiveExpedition.FighterIds.Count +
                " бойцов находятся в экспедиции. В столице осталось: " +
                gameState.GarrisonFighterCount + ".";
        }
        else
        {
            armyStatusLabel.text =
                commander.Name + " → " + gameState.Fighters.Count +
                " отдельных воинов. Армия находится в столице и защищает её.";
        }

        RefreshSupplyBlock();
        RefreshFightersList();
    }

    private void RefreshSupplyBlock()
    {
        int dailyConsumption = gameState.HasActiveExpedition
            ? gameState.ExpeditionSupplyConsumption
            : selectedFighterIds.Count > 0
                ? selectedFighterIds.Count + 1
                : 0;
        int fullDays = dailyConsumption > 0
            ? gameState.ArmySupply / dailyConsumption
            : 0;
        bool canAdjust = gameState.CanAdjustArmySupply && !isGameOver;

        armyGoldLabel.text = gameState.ArmyGold.ToString();
        supplyValueLabel.text = gameState.ArmySupply.ToString();
        supplyConsumptionLabel.text = dailyConsumption > 0
            ? "Расход выбранного отряда: " + dailyConsumption + " / день"
            : "Расход: выберите бойцов";
        supplyDaysLabel.text = dailyConsumption > 0
            ? "Хватит на " + fullDays + " " + GetDayWord(fullDays)
            : "Дни снабжения пока не рассчитаны";

        armyGoldPlusButton.SetEnabled(canAdjust && gameState.Gold > 0);
        armyGoldMinusButton.SetEnabled(canAdjust && gameState.ArmyGold > 0);
        supplyPlusButton.SetEnabled(canAdjust && gameState.Food > 0);
        supplyMinusButton.SetEnabled(canAdjust && gameState.ArmySupply > 0);
    }

    private void RefreshFightersList()
    {
        commanderGarrisonList.Clear();
        capitalGarrisonList.Clear();

        bool expeditionActive = gameState.HasActiveExpedition;
        fighterSelectionHintLabel.text = expeditionActive
            ? "Состав обоих гарнизонов зафиксирован до возвращения экспедиции."
            : "Перетаскивайте карточки между гарнизонами. Щелчок по карточке также перемещает бойца.";

        List<string> commanderFighterIds = expeditionActive
            ? new List<string>(gameState.ActiveExpedition.FighterIds)
            : GetSelectedFighterIdsInArmyOrder();
        List<string> capitalFighterIds = new List<string>();

        foreach (FighterData fighter in gameState.Fighters)
        {
            bool withCommander = commanderFighterIds.Contains(fighter.Id);

            if (!withCommander)
                capitalFighterIds.Add(fighter.Id);

            Button card = CreateFighterCard(
                fighter,
                withCommander,
                expeditionActive);

            if (withCommander)
                commanderGarrisonList.Add(card);
            else
                capitalGarrisonList.Add(card);
        }

        int commanderPower =
            gameState.CalculateDefensePower(commanderFighterIds);
        int capitalPower =
            gameState.CalculateDefensePower(capitalFighterIds);
        int totalPower = gameState.TotalArmyDefensePower;

        commanderGarrisonSummaryLabel.text =
            commanderFighterIds.Count + " " +
            GetFighterWord(commanderFighterIds.Count) +
            " · сила " + commanderPower;
        capitalGarrisonSummaryLabel.text =
            capitalFighterIds.Count + " " +
            GetFighterWord(capitalFighterIds.Count) +
            " · оборона " + capitalPower + "/" + totalPower;

        commanderGarrisonEmptyLabel.style.display =
            commanderFighterIds.Count == 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        capitalGarrisonEmptyLabel.style.display =
            capitalFighterIds.Count == 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        if (capitalFighterIds.Count == 0)
            capitalGarrisonDropZone.AddToClassList("army-roster-empty-danger");
        else
            capitalGarrisonDropZone.RemoveFromClassList("army-roster-empty-danger");
    }

    private Button CreateFighterCard(
        FighterData fighter,
        bool withCommander,
        bool expeditionActive)
    {
        string fighterId = fighter.Id;
        Button card = new Button();
        card.AddToClassList("fighter-card");
        card.AddToClassList(
            withCommander
                ? "fighter-card-selected"
                : "fighter-card-garrison");
        card.tooltip = expeditionActive
            ? "Состав зафиксирован до возвращения"
            : "Перетащите бойца в другой гарнизон";
        card.SetEnabled(!expeditionActive && !isGameOver);

        VisualElement imagePlaceholder = new VisualElement();
        imagePlaceholder.AddToClassList("fighter-image-placeholder");

        Label imagePlaceholderLabel = new Label("ИЗОБРАЖЕНИЕ");
        imagePlaceholderLabel.AddToClassList("fighter-image-placeholder-text");
        imagePlaceholder.Add(imagePlaceholderLabel);
        card.Add(imagePlaceholder);

        Label nameLabel = new Label(fighter.Name);
        nameLabel.AddToClassList("fighter-name");
        card.Add(nameLabel);

        Label roleLabel = new Label(fighter.Role);
        roleLabel.AddToClassList("fighter-role");
        card.Add(roleLabel);

        Label infoLabel = new Label(
            "Ур. " + fighter.Level + " · оборона " +
            fighter.DefensePower);
        infoLabel.AddToClassList("fighter-info");
        card.Add(infoLabel);

        string assignmentText;

        if (expeditionActive)
        {
            assignmentText = withCommander
                ? "В ЭКСПЕДИЦИИ"
                : "ГАРНИЗОН СТОЛИЦЫ";
        }
        else
        {
            assignmentText = withCommander
                ? "С КОМАНДИРОМ"
                : "ЗАЩИЩАЕТ СТОЛИЦУ";
        }

        Label assignmentLabel = new Label(assignmentText);
        assignmentLabel.AddToClassList("fighter-assignment");
        card.Add(assignmentLabel);

        card.RegisterCallback<PointerDownEvent>(
            evt => OnFighterPointerDown(evt, fighterId, card));
        card.RegisterCallback<PointerMoveEvent>(OnFighterPointerMove);
        card.RegisterCallback<PointerUpEvent>(OnFighterPointerUp);

        return card;
    }

    private void OnFighterPointerDown(
        PointerDownEvent pointerEvent,
        string fighterId,
        VisualElement card)
    {
        if (pointerEvent.button != 0 ||
            isGameOver ||
            gameState.HasActiveExpedition)
        {
            return;
        }

        CleanupFighterDrag();
        draggedFighterId = fighterId;
        draggedFighterPointerId = pointerEvent.pointerId;
        fighterDragStartPosition = pointerEvent.position;
        draggedFighterCard = card;
        fighterDragStarted = false;
        card.CapturePointer(pointerEvent.pointerId);
        pointerEvent.StopPropagation();
    }

    private void OnFighterPointerMove(PointerMoveEvent pointerEvent)
    {
        if (draggedFighterCard == null ||
            draggedFighterPointerId != pointerEvent.pointerId ||
            !draggedFighterCard.HasPointerCapture(pointerEvent.pointerId))
        {
            return;
        }

        if (!fighterDragStarted &&
            Vector2.Distance(fighterDragStartPosition, pointerEvent.position) >=
            FighterDragThreshold)
        {
            BeginFighterDrag(pointerEvent.position);
        }

        if (fighterDragStarted)
        {
            UpdateFighterDragGhost(pointerEvent.position);
            UpdateFighterDropHighlights(pointerEvent.position);
        }

        pointerEvent.StopPropagation();
    }

    private void OnFighterPointerUp(PointerUpEvent pointerEvent)
    {
        if (draggedFighterCard == null ||
            draggedFighterPointerId != pointerEvent.pointerId)
        {
            return;
        }

        string fighterId = draggedFighterId;
        bool wasDragging = fighterDragStarted;
        bool droppedToCommander =
            commanderGarrisonDropZone.worldBound.Contains(pointerEvent.position);
        bool droppedToCapital =
            capitalGarrisonDropZone.worldBound.Contains(pointerEvent.position);

        if (draggedFighterCard.HasPointerCapture(pointerEvent.pointerId))
            draggedFighterCard.ReleasePointer(pointerEvent.pointerId);

        CleanupFighterDrag();

        if (!wasDragging)
            ToggleFighterAssignment(fighterId);
        else if (droppedToCommander)
            MoveFighterToCommander(fighterId, true);
        else if (droppedToCapital)
            MoveFighterToCommander(fighterId, false);

        pointerEvent.StopPropagation();
    }

    private void BeginFighterDrag(Vector2 pointerPosition)
    {
        FighterData fighter = gameState.FindFighter(draggedFighterId);

        if (fighter == null || interfaceRoot == null)
            return;

        fighterDragStarted = true;
        draggedFighterCard.AddToClassList("fighter-card-dragging");

        fighterDragGhost = new VisualElement();
        fighterDragGhost.AddToClassList("fighter-drag-ghost");
        fighterDragGhost.pickingMode = PickingMode.Ignore;

        Label nameLabel = new Label(fighter.Name);
        nameLabel.AddToClassList("fighter-drag-ghost-name");
        fighterDragGhost.Add(nameLabel);

        Label roleLabel = new Label(
            fighter.Role + " · оборона " + fighter.DefensePower);
        roleLabel.AddToClassList("fighter-drag-ghost-role");
        fighterDragGhost.Add(roleLabel);

        interfaceRoot.Add(fighterDragGhost);
        fighterDragGhost.BringToFront();
        UpdateFighterDragGhost(pointerPosition);
    }

    private void UpdateFighterDragGhost(Vector2 pointerPosition)
    {
        if (fighterDragGhost == null)
            return;

        fighterDragGhost.style.left = pointerPosition.x - 66f;
        fighterDragGhost.style.top = pointerPosition.y - 37f;
    }

    private void UpdateFighterDropHighlights(Vector2 pointerPosition)
    {
        bool overCommander =
            commanderGarrisonDropZone.worldBound.Contains(pointerPosition);
        bool overCapital =
            capitalGarrisonDropZone.worldBound.Contains(pointerPosition);

        SetDropZoneHighlighted(commanderGarrisonDropZone, overCommander);
        SetDropZoneHighlighted(capitalGarrisonDropZone, overCapital);
    }

    private void SetDropZoneHighlighted(
        VisualElement dropZone,
        bool highlighted)
    {
        if (highlighted)
            dropZone.AddToClassList("army-roster-drop-hover");
        else
            dropZone.RemoveFromClassList("army-roster-drop-hover");
    }

    private void CleanupFighterDrag()
    {
        if (draggedFighterCard != null &&
            draggedFighterPointerId >= 0 &&
            draggedFighterCard.HasPointerCapture(draggedFighterPointerId))
        {
            draggedFighterCard.ReleasePointer(draggedFighterPointerId);
        }

        if (draggedFighterCard != null)
            draggedFighterCard.RemoveFromClassList("fighter-card-dragging");

        if (fighterDragGhost != null)
            fighterDragGhost.RemoveFromHierarchy();

        if (commanderGarrisonDropZone != null)
            commanderGarrisonDropZone.RemoveFromClassList("army-roster-drop-hover");

        if (capitalGarrisonDropZone != null)
            capitalGarrisonDropZone.RemoveFromClassList("army-roster-drop-hover");

        draggedFighterId = null;
        draggedFighterPointerId = -1;
        draggedFighterCard = null;
        fighterDragGhost = null;
        fighterDragStarted = false;
    }

    private void ToggleFighterAssignment(string fighterId)
    {
        if (isGameOver || gameState.HasActiveExpedition)
            return;

        MoveFighterToCommander(
            fighterId,
            !selectedFighterIds.Contains(fighterId));
    }

    private void MoveFighterToCommander(
        string fighterId,
        bool moveToCommander)
    {
        if (isGameOver || gameState.HasActiveExpedition)
            return;

        bool changed = moveToCommander
            ? selectedFighterIds.Add(fighterId)
            : selectedFighterIds.Remove(fighterId);

        if (!changed)
            return;

        RefreshArmyPanel();
        RefreshExpeditionPanel();
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

        commanderDropdown.SetEnabled(controlsAvailable && !expeditionActive);
        RefreshWorldMapPanel();

        activeExpeditionCard.style.display =
            expeditionActive ? DisplayStyle.Flex : DisplayStyle.None;

        if (!expeditionActive)
        {
            expeditionStatusLabel.text = hasSelectedFighters
                ? "Подготовка экспедиции: выбрано бойцов — " +
                  selectedFighterIds.Count + ". Выберите цель на карте."
                : "Активная экспедиция: нет. Сначала выберите бойцов на экране «Армия».";
            researchExpeditionButton.style.display = DisplayStyle.None;
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        LocationData location = gameState.FindLocation(expedition.LocationId);
        CommanderData commander = gameState.FindCommander(expedition.CommanderId);
        bool exploring = expedition.IsExplorationInProgress;
        bool researchImplemented = location.ExplorationDays > 0;

        string stateText = awaitingDecision
            ? "ожидает приказа"
            : exploring
                ? "исследует локацию"
                : GetCommanderStateText(expedition.Phase);

        expeditionStatusLabel.text =
            "Активная экспедиция: " + commander.Name + " · " +
            location.TravelTargetName + " · " + stateText;

        activeExpeditionTitle.text =
            "ЭКСПЕДИЦИЯ: " + location.TravelTargetName.ToUpper();

        string currentTask;
        string daysInformation;

        if (awaitingDecision)
        {
            currentTask = "Ожидает приказа короля";
            daysInformation = "Осталось дней пути: " + expedition.DaysRemaining;
        }
        else if (expedition.Phase == CommanderState.TravellingToLocation)
        {
            currentTask = "Добраться до цели";
            daysInformation = "Осталось дней пути: " + expedition.DaysRemaining;
        }
        else if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            currentTask = "Вернуться в столицу";
            daysInformation = "Осталось дней пути: " + expedition.DaysRemaining;
        }
        else if (exploring)
        {
            currentTask = "Исследовать локацию";
            daysInformation =
                "Осталось дней исследования: " + expedition.ExplorationDaysRemaining;
        }
        else if (location.IsExplored)
        {
            currentTask = "Локация исследована";
            daysInformation =
                "Расстояние до столицы: " + location.DistanceDays + " дн.";
        }
        else if (researchImplemented)
        {
            currentTask = "Выбрать: исследовать или возвращаться";
            daysInformation =
                "Исследование займёт: " + location.ExplorationDays + " " +
                GetDayWord(location.ExplorationDays);
        }
        else
        {
            currentTask = "Исследование этой локации пока не реализовано";
            daysInformation =
                "Расстояние до столицы: " + location.DistanceDays + " дн.";
        }

        activeExpeditionDetails.text =
            "Командир: " + commander.Name + "\n" +
            "Бойцы: " + GetFighterNames(expedition.FighterIds) + "\n" +
            "Сила отряда: " + gameState.ExpeditionDefensePower + "\n" +
            "Гарнизон столицы: " + gameState.GarrisonFighterCount +
            " " + GetFighterWord(gameState.GarrisonFighterCount) +
            " · оборона " + gameState.GarrisonDefensePower +
            "/" + gameState.TotalArmyDefensePower + "\n" +
            "Цель: " + location.TravelTargetName + "\n" +
            "Состояние: " + stateText + "\n" +
            "Текущая задача: " + currentTask + "\n" +
            daysInformation;

        RefreshResearchButton(
            controlsAvailable,
            awaitingDecision,
            expedition,
            location,
            researchImplemented);

        bool canCancel = gameState.CanCancelExpeditionBeforeDayEnd;
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
        else if (expedition.IsExplorationInProgress)
        {
            researchExpeditionButton.SetEnabled(false);
            researchExpeditionButton.text =
                "ИССЛЕДОВАНИЕ — " + expedition.ExplorationDaysRemaining + " " +
                GetDayWord(expedition.ExplorationDaysRemaining).ToUpper();
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
    }

    private void OpenDecision(ExpeditionDecisionOccurrence occurrence)
    {
        if (occurrence == null || isGameOver)
            return;

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
    }

    private void OnIncidentUnderstoodClicked()
    {
        if (openedIncident == null)
        {
            HideIncidentModal();
            return;
        }

        int readIncidentId = openedIncident.Id;

        unreadIncidents.RemoveAll(
            occurrence => occurrence.Id == readIncidentId);

        HideIncidentModal();
        RefreshIncidentNotifications();
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

        if (!ExpeditionDecisionSystem.TryApplyChoice(
                gameState,
                optionId,
                out resultMessage))
        {
            incidentModalConsequence.text = resultMessage;
            return;
        }

        AddReport(resultMessage);
        HideIncidentModal();
        RefreshInterface();
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
        endDayButton.SetEnabled(false);
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
        endDayButton.SetEnabled(true);
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
