using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class PrototypeUIController : MonoBehaviour
{
    private enum MainScreen
    {
        Capital,
        Army,
        Expeditions
    }

    private const int MaxIncidentNotificationButtons = 6;

    private GameState gameState;

    private Button navCapitalButton;
    private Button navArmyButton;
    private Button navExpeditionsButton;

    private VisualElement capitalScreen;
    private VisualElement armyScreen;
    private VisualElement expeditionsScreen;
    private MainScreen? openedScreen;

    private const float NavigationClickCooldownSeconds = 0.18f;
    private float lastNavigationClickTime = -NavigationClickCooldownSeconds;

    private Label dayLabel;
    private Label goldLabel;
    private Label goldIncomeLabel;
    private Label foodLabel;
    private Label foodIncomeLabel;
    private Label populationLabel;
    private Label moodLabel;
    private Label foodConsumptionLabel;

    private Button endDayButton;

    private DropdownField commanderDropdown;
    private Label commanderDetailLabel;
    private Label armyStatusLabel;

    private Button supplyMinusButton;
    private Label supplyValueLabel;
    private Button supplyPlusButton;
    private Label supplyConsumptionLabel;
    private Label supplyDaysLabel;
    private VisualElement fightersList;

    private Label expeditionStatusLabel;
    private Button sendRuinsButton;
    private Button sendMineButton;
    private Button sendForestButton;

    private VisualElement activeExpeditionCard;
    private Label activeExpeditionTitle;
    private Label activeExpeditionDetails;
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

    private readonly List<ExpeditionIncidentOccurrence> unreadIncidents =
        new List<ExpeditionIncidentOccurrence>();

    private ExpeditionIncidentOccurrence openedIncident;
    private ExpeditionDecisionOccurrence openedDecision;

    private bool callbacksRegistered;

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

        gameState = new GameState();
        gameState.CreateNewGame();

        reportHistoryLabel.enableRichText = true;

        CreateDecisionChoiceButtons();
        ConfigureCommanderDropdown();
        RegisterCallbacks();
        HideIncidentModal();

        AddReport(
            "Прототип запущен. Откройте нужный экран круглой кнопкой слева сверху.");

        CloseMainScreen();
        RefreshInterface();
    }

    private void FindInterfaceElements(VisualElement root)
    {
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

        commanderDropdown = root.Q<DropdownField>("commander-dropdown");
        commanderDetailLabel = root.Q<Label>("commander-detail-label");
        armyStatusLabel = root.Q<Label>("army-status-label");

        supplyMinusButton = root.Q<Button>("supply-minus-button");
        supplyValueLabel = root.Q<Label>("supply-value-label");
        supplyPlusButton = root.Q<Button>("supply-plus-button");
        supplyConsumptionLabel = root.Q<Label>("supply-consumption-label");
        supplyDaysLabel = root.Q<Label>("supply-days-label");
        fightersList = root.Q<VisualElement>("fighters-list");

        expeditionStatusLabel = root.Q<Label>("expedition-status-label");
        sendRuinsButton = root.Q<Button>("send-ruins-button");
        sendMineButton = root.Q<Button>("send-mine-button");
        sendForestButton = root.Q<Button>("send-forest-button");

        activeExpeditionCard = root.Q<VisualElement>("active-expedition-card");
        activeExpeditionTitle = root.Q<Label>("active-expedition-title");
        activeExpeditionDetails = root.Q<Label>("active-expedition-details");
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
            commanderDropdown != null &&
            commanderDetailLabel != null &&
            armyStatusLabel != null &&
            supplyMinusButton != null &&
            supplyValueLabel != null &&
            supplyPlusButton != null &&
            supplyConsumptionLabel != null &&
            supplyDaysLabel != null &&
            fightersList != null &&
            expeditionStatusLabel != null &&
            sendRuinsButton != null &&
            sendMineButton != null &&
            sendForestButton != null &&
            activeExpeditionCard != null &&
            activeExpeditionTitle != null &&
            activeExpeditionDetails != null &&
            returnExpeditionButton != null &&
            reportHistoryScroll != null &&
            reportHistoryLabel != null &&
            incidentNotificationStack != null &&
            incidentModalOverlay != null &&
            incidentModalTitle != null &&
            incidentModalDescription != null &&
            incidentModalConsequence != null &&
            incidentUnderstoodButton != null &&
            incidentModalTextColumn != null;
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

        supplyMinusButton.clicked += OnSupplyMinusClicked;
        supplyPlusButton.clicked += OnSupplyPlusClicked;

        sendRuinsButton.clicked += OnSendRuinsClicked;
        sendMineButton.clicked += OnSendMineClicked;
        sendForestButton.clicked += OnSendForestClicked;
        returnExpeditionButton.clicked += OnExpeditionActionClicked;
        incidentUnderstoodButton.clicked += OnIncidentUnderstoodClicked;

        commanderDropdown.RegisterValueChangedCallback(OnCommanderChanged);
        callbacksRegistered = true;
    }

    private void OnDisable()
    {
        if (!callbacksRegistered)
            return;

        navCapitalButton.clicked -= OnCapitalNavigationClicked;
        navArmyButton.clicked -= OnArmyNavigationClicked;
        navExpeditionsButton.clicked -= OnExpeditionsNavigationClicked;
        endDayButton.clicked -= OnEndDayClicked;

        supplyMinusButton.clicked -= OnSupplyMinusClicked;
        supplyPlusButton.clicked -= OnSupplyPlusClicked;

        sendRuinsButton.clicked -= OnSendRuinsClicked;
        sendMineButton.clicked -= OnSendMineClicked;
        sendForestButton.clicked -= OnSendForestClicked;
        returnExpeditionButton.clicked -= OnExpeditionActionClicked;
        incidentUnderstoodButton.clicked -= OnIncidentUnderstoodClicked;

        commanderDropdown.UnregisterValueChangedCallback(OnCommanderChanged);
        callbacksRegistered = false;
    }

    private void OnCommanderChanged(ChangeEvent<string> changeEvent)
    {
        if (gameState.SelectCommanderByName(changeEvent.newValue))
            AddReport(changeEvent.newValue + " назначен командиром армии.");

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
        int finishedDay = gameState.Day;
        DayResolutionResult result = DayResolver.ResolveDay(gameState);

        if (result.NewExpeditionIncidents.Count > 0)
            unreadIncidents.AddRange(result.NewExpeditionIncidents);

        AddReport(string.Join("\n", result.Messages), finishedDay);
        RefreshInterface();
    }

    private void OnSendRuinsClicked() => TrySendExpedition("ruins");
    private void OnSendMineClicked() => TrySendExpedition("mine");
    private void OnSendForestClicked() => TrySendExpedition("forest");

    private void TrySendExpedition(string locationId)
    {
        string resultMessage;
        gameState.TryStartExpedition(locationId, out resultMessage);
        AddReport(resultMessage);
        RefreshInterface();
    }

    private void OnExpeditionActionClicked()
    {
        string resultMessage;

        if (gameState.CanCancelExpeditionBeforeDayEnd)
            gameState.TryCancelExpeditionBeforeDayEnd(out resultMessage);
        else
            gameState.TryOrderReturn(out resultMessage);

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

        RefreshArmyPanel();
        RefreshExpeditionPanel();
        RefreshIncidentNotifications();
    }

    private void RefreshArmyPanel()
    {
        CommanderData commander = gameState.GetSelectedCommander();

        commanderDetailLabel.text =
            "Выбран: " + commander.Name + " · " + GetCommanderStateText(commander.State);

        if (gameState.HasActiveExpedition)
        {
            armyStatusLabel.text =
                commander.Name + " → " + gameState.Fighters.Count +
                " отдельных воинов. Армия находится вне столицы. " +
                "Столица не защищена этой армией.";
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
        int dailyConsumption = gameState.ExpeditionSupplyConsumption;
        int fullDays = gameState.FullSupplyDays;
        bool canAdjust = gameState.CanAdjustArmySupply;

        supplyValueLabel.text = gameState.ArmySupply.ToString();
        supplyConsumptionLabel.text =
            "Расход в походе: " + dailyConsumption + " / день";
        supplyDaysLabel.text =
            "Хватит на " + fullDays + " " + GetDayWord(fullDays);

        supplyPlusButton.SetEnabled(canAdjust && gameState.Food > 0);
        supplyMinusButton.SetEnabled(canAdjust && gameState.ArmySupply > 0);
    }

    private void RefreshFightersList()
    {
        fightersList.Clear();

        foreach (FighterData fighter in gameState.Fighters)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("fighter-card");

            Label roleLabel = new Label(fighter.Role);
            roleLabel.AddToClassList("fighter-role");
            card.Add(roleLabel);

            Label infoLabel = new Label(
                "Уровень " + fighter.Level + " · в строю");
            infoLabel.AddToClassList("fighter-info");
            card.Add(infoLabel);

            fightersList.Add(card);
        }
    }

    private void RefreshExpeditionPanel()
    {
        bool expeditionActive = gameState.HasActiveExpedition;
        bool awaitingDecision = gameState.HasPendingExpeditionDecision;

        commanderDropdown.SetEnabled(!expeditionActive);
        sendRuinsButton.SetEnabled(!expeditionActive);
        sendMineButton.SetEnabled(!expeditionActive);
        sendForestButton.SetEnabled(!expeditionActive);

        activeExpeditionCard.style.display =
            expeditionActive ? DisplayStyle.Flex : DisplayStyle.None;

        if (!expeditionActive)
        {
            expeditionStatusLabel.text = "Активная экспедиция: нет";
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        LocationData location = gameState.FindLocation(expedition.LocationId);
        CommanderData commander = gameState.FindCommander(expedition.CommanderId);
        string stateText = awaitingDecision
            ? "ожидает приказа"
            : GetCommanderStateText(expedition.Phase);

        expeditionStatusLabel.text =
            "Активная экспедиция: " + commander.Name + " · " +
            location.Name + " · " + stateText;

        activeExpeditionTitle.text = "ЭКСПЕДИЦИЯ: " + location.Name.ToUpper();

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
        else if (expedition.Phase == CommanderState.AtLocation)
        {
            currentTask = "Исследовать локацию";
            daysInformation =
                "Расстояние до столицы: " + location.DistanceDays + " дн.";
        }
        else
        {
            currentTask = "Вернуться в столицу";
            daysInformation = "Осталось дней пути: " + expedition.DaysRemaining;
        }

        activeExpeditionDetails.text =
            "Командир: " + commander.Name + "\n" +
            "Армия: " + expedition.FighterIds.Count + " отдельных воинов\n" +
            "Цель: " + location.Name + "\n" +
            "Состояние: " + stateText + "\n" +
            "Текущая задача: " + currentTask + "\n" +
            daysInformation;

        bool canCancel = gameState.CanCancelExpeditionBeforeDayEnd;
        bool alreadyReturning = expedition.Phase == CommanderState.ReturningToCastle;

        if (awaitingDecision)
        {
            returnExpeditionButton.SetEnabled(false);
            returnExpeditionButton.text = "Сначала требуется приказ";
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

    private void RefreshIncidentNotifications()
    {
        incidentNotificationStack.Clear();

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
        if (occurrence == null)
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
        if (occurrence == null)
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
