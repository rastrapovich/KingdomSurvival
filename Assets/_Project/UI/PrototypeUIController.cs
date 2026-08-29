using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// ============================================================
// СВЯЗЬ ИГРОВЫХ ДАННЫХ С ИНТЕРФЕЙСОМ
// Этот компонент должен находиться рядом с UI Document.
// ============================================================

[RequireComponent(typeof(UIDocument))]
public class PrototypeUIController : MonoBehaviour
{
    private enum MainScreen
    {
        Capital,
        Army,
        Expeditions
    }

    private GameState gameState;

    private Button navCapitalButton;
    private Button navArmyButton;
    private Button navExpeditionsButton;

    private VisualElement capitalScreen;
    private VisualElement armyScreen;
    private VisualElement expeditionsScreen;

    private Label dayLabel;
    private Label goldLabel;
    private Label foodLabel;
    private Label populationLabel;
    private Label moodLabel;
    private Label foodConsumptionLabel;

    private Button endDayButton;

    private DropdownField commanderDropdown;
    private Label commanderDetailLabel;
    private Label armyStatusLabel;

    private Label expeditionStatusLabel;
    private Label reportLabel;

    private Button sendRuinsButton;
    private Button sendMineButton;
    private Button sendForestButton;

    private VisualElement activeExpeditionCard;
    private Label activeExpeditionTitle;
    private Label activeExpeditionDetails;
    private Label activeExpeditionLastReport;
    private Button returnExpeditionButton;

    private bool callbacksRegistered;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;

        FindInterfaceElements(root);

        if (!AllRequiredElementsExist())
        {
            Debug.LogError(
                "PrototypeUIController: в Prototype_Main.uxml " +
                "отсутствуют необходимые элементы.");

            enabled = false;
            return;
        }

        gameState = new GameState();
        gameState.CreateNewGame();

        ConfigureCommanderDropdown();
        RegisterCallbacks();

        reportLabel.text =
            "Прототип запущен. Откройте нужный экран круглой кнопкой слева сверху.";

        ShowScreen(MainScreen.Capital);
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
        foodLabel = root.Q<Label>("food-label");
        populationLabel = root.Q<Label>("population-label");
        moodLabel = root.Q<Label>("mood-label");
        foodConsumptionLabel = root.Q<Label>("food-consumption-label");
        endDayButton = root.Q<Button>("end-day-button");

        commanderDropdown = root.Q<DropdownField>("commander-dropdown");
        commanderDetailLabel = root.Q<Label>("commander-detail-label");
        armyStatusLabel = root.Q<Label>("army-status-label");

        expeditionStatusLabel = root.Q<Label>("expedition-status-label");
        reportLabel = root.Q<Label>("report-label");

        sendRuinsButton = root.Q<Button>("send-ruins-button");
        sendMineButton = root.Q<Button>("send-mine-button");
        sendForestButton = root.Q<Button>("send-forest-button");

        activeExpeditionCard = root.Q<VisualElement>("active-expedition-card");
        activeExpeditionTitle = root.Q<Label>("active-expedition-title");
        activeExpeditionDetails = root.Q<Label>("active-expedition-details");
        activeExpeditionLastReport = root.Q<Label>("active-expedition-last-report");
        returnExpeditionButton = root.Q<Button>("return-expedition-button");
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
            foodLabel != null &&
            populationLabel != null &&
            moodLabel != null &&
            foodConsumptionLabel != null &&
            endDayButton != null &&
            commanderDropdown != null &&
            commanderDetailLabel != null &&
            armyStatusLabel != null &&
            expeditionStatusLabel != null &&
            reportLabel != null &&
            sendRuinsButton != null &&
            sendMineButton != null &&
            sendForestButton != null &&
            activeExpeditionCard != null &&
            activeExpeditionTitle != null &&
            activeExpeditionDetails != null &&
            activeExpeditionLastReport != null &&
            returnExpeditionButton != null;
    }

    private void ShowScreen(MainScreen screen)
    {
        capitalScreen.style.display =
            screen == MainScreen.Capital
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        armyScreen.style.display =
            screen == MainScreen.Army
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        expeditionsScreen.style.display =
            screen == MainScreen.Expeditions
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        SetNavigationButtonActive(
            navCapitalButton,
            screen == MainScreen.Capital);

        SetNavigationButtonActive(
            navArmyButton,
            screen == MainScreen.Army);

        SetNavigationButtonActive(
            navExpeditionsButton,
            screen == MainScreen.Expeditions);
    }

    private void SetNavigationButtonActive(Button button, bool isActive)
    {
        if (isActive)
            button.AddToClassList("nav-button-active");
        else
            button.RemoveFromClassList("nav-button-active");
    }

    private void OnCapitalNavigationClicked()
    {
        ShowScreen(MainScreen.Capital);
    }

    private void OnArmyNavigationClicked()
    {
        ShowScreen(MainScreen.Army);
    }

    private void OnExpeditionsNavigationClicked()
    {
        ShowScreen(MainScreen.Expeditions);
    }

    private void ConfigureCommanderDropdown()
    {
        List<string> commanderNames = gameState.GetCommanderNames();
        commanderDropdown.choices = commanderNames;

        CommanderData selectedCommander = gameState.GetSelectedCommander();
        commanderDropdown.SetValueWithoutNotify(selectedCommander.Name);
    }

    private void RegisterCallbacks()
    {
        navCapitalButton.clicked += OnCapitalNavigationClicked;
        navArmyButton.clicked += OnArmyNavigationClicked;
        navExpeditionsButton.clicked += OnExpeditionsNavigationClicked;

        endDayButton.clicked += OnEndDayClicked;

        sendRuinsButton.clicked += OnSendRuinsClicked;
        sendMineButton.clicked += OnSendMineClicked;
        sendForestButton.clicked += OnSendForestClicked;

        returnExpeditionButton.clicked += OnExpeditionActionClicked;

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

        sendRuinsButton.clicked -= OnSendRuinsClicked;
        sendMineButton.clicked -= OnSendMineClicked;
        sendForestButton.clicked -= OnSendForestClicked;

        returnExpeditionButton.clicked -= OnExpeditionActionClicked;

        commanderDropdown.UnregisterValueChangedCallback(OnCommanderChanged);

        callbacksRegistered = false;
    }

    private void OnCommanderChanged(ChangeEvent<string> changeEvent)
    {
        bool commanderChanged =
            gameState.SelectCommanderByName(changeEvent.newValue);

        if (commanderChanged)
        {
            reportLabel.text =
                changeEvent.newValue +
                " назначен командиром армии.";
        }

        RefreshInterface();
    }

    private void OnEndDayClicked()
    {
        DayResolutionResult result = DayResolver.ResolveDay(gameState);
        reportLabel.text = string.Join("\n", result.Messages);
        RefreshInterface();
    }

    private void OnSendRuinsClicked()
    {
        TrySendExpedition("ruins");
    }

    private void OnSendMineClicked()
    {
        TrySendExpedition("mine");
    }

    private void OnSendForestClicked()
    {
        TrySendExpedition("forest");
    }

    private void TrySendExpedition(string locationId)
    {
        string resultMessage;

        gameState.TryStartExpedition(
            locationId,
            out resultMessage);

        reportLabel.text = resultMessage;
        RefreshInterface();
    }

    // До завершения дня эта кнопка отменяет приказ.
    // После завершения дня она отдаёт обычный приказ возвращаться.
    private void OnExpeditionActionClicked()
    {
        string resultMessage;

        if (gameState.CanCancelExpeditionBeforeDayEnd)
        {
            gameState.TryCancelExpeditionBeforeDayEnd(
                out resultMessage);
        }
        else
        {
            gameState.TryOrderReturn(out resultMessage);
        }

        reportLabel.text = resultMessage;
        RefreshInterface();
    }

    private void RefreshInterface()
    {
        dayLabel.text = "День: " + gameState.Day;
        goldLabel.text = "Золото: " + gameState.Gold;
        foodLabel.text = "Пища: " + gameState.Food;
        populationLabel.text = "Население: " + gameState.Population;
        moodLabel.text = "Настроение: " + gameState.Mood + "/100";
        foodConsumptionLabel.text =
            "Расход: " + gameState.DailyFoodConsumption + " в день";

        RefreshArmyPanel();
        RefreshExpeditionPanel();
    }

    private void RefreshArmyPanel()
    {
        CommanderData commander = gameState.GetSelectedCommander();

        commanderDetailLabel.text =
            "Выбран: " +
            commander.Name +
            " · " +
            GetCommanderStateText(commander.State);

        if (gameState.HasActiveExpedition)
        {
            armyStatusLabel.text =
                commander.Name +
                " → " +
                gameState.Fighters.Count +
                " отдельных воинов. " +
                "Армия находится вне столицы. " +
                "Столица не защищена этой армией.";
        }
        else
        {
            armyStatusLabel.text =
                commander.Name +
                " → " +
                gameState.Fighters.Count +
                " отдельных воинов. " +
                "Армия находится в столице и защищает её.";
        }
    }

    private void RefreshExpeditionPanel()
    {
        bool expeditionActive = gameState.HasActiveExpedition;

        commanderDropdown.SetEnabled(!expeditionActive);
        sendRuinsButton.SetEnabled(!expeditionActive);
        sendMineButton.SetEnabled(!expeditionActive);
        sendForestButton.SetEnabled(!expeditionActive);

        activeExpeditionCard.style.display =
            expeditionActive
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        if (!expeditionActive)
        {
            expeditionStatusLabel.text = "Активная экспедиция: нет";
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        LocationData location = gameState.FindLocation(expedition.LocationId);
        CommanderData commander = gameState.FindCommander(expedition.CommanderId);
        string stateText = GetCommanderStateText(expedition.Phase);

        expeditionStatusLabel.text =
            "Активная экспедиция: " +
            commander.Name +
            " · " +
            location.Name +
            " · " +
            stateText;

        activeExpeditionTitle.text =
            "ЭКСПЕДИЦИЯ: " + location.Name.ToUpper();

        string currentTask;
        string daysInformation;

        if (expedition.Phase == CommanderState.TravellingToLocation)
        {
            currentTask = "Добраться до цели";
            daysInformation =
                "Осталось дней пути: " + expedition.DaysRemaining;
        }
        else if (expedition.Phase == CommanderState.AtLocation)
        {
            currentTask = "Исследовать локацию";
            daysInformation =
                "Расстояние до столицы: " +
                location.DistanceDays +
                " дн.";
        }
        else
        {
            currentTask = "Вернуться в столицу";
            daysInformation =
                "Осталось дней пути: " + expedition.DaysRemaining;
        }

        string cancellationInformation =
            gameState.CanCancelExpeditionBeforeDayEnd
                ? "\nПриказ ещё можно отменить до завершения текущего дня."
                : string.Empty;

        activeExpeditionDetails.text =
            "Командир: " + commander.Name + "\n" +
            "Армия: " + expedition.FighterIds.Count + " отдельных воинов\n" +
            "Цель: " + location.Name + "\n" +
            "Состояние: " + stateText + "\n" +
            "Текущая задача: " + currentTask + "\n" +
            daysInformation + "\n" +
            "Снабжение: пока не рассчитывается" +
            cancellationInformation;

        activeExpeditionLastReport.text = reportLabel.text;

        bool canCancel = gameState.CanCancelExpeditionBeforeDayEnd;
        bool alreadyReturning =
            expedition.Phase == CommanderState.ReturningToCastle;

        if (canCancel)
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
