using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private Button debugToggleButton;
    private VisualElement debugPanel;

    private Label debugGoldValueLabel;
    private Label debugFoodValueLabel;
    private Label debugPopulationValueLabel;
    private Label debugMoodValueLabel;
    private Label debugArmyGoldValueLabel;
    private Label debugArmySupplyValueLabel;
    private Label debugExpeditionStateLabel;

    private Button debugArmyGoldMinus10Button;
    private Button debugArmyGoldPlus10Button;
    private Button debugArmySupplyMinus10Button;
    private Button debugArmySupplyPlus10Button;
    private Button debugCapitalCrisisButton;
    private Button debugBackgroundIncidentButton;
    private Button debugSignificantDecisionButton;
    private Button debugResetGameButton;

    private bool debugMenuInitialized;

    private static bool DebugToolsAvailable
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return false;
#endif
        }
    }

    private void Start()
    {
        if (!DebugToolsAvailable || debugMenuInitialized)
            return;

        BuildDebugMenu();
        debugMenuInitialized = true;
        RefreshDebugMenu();
    }

    private void LateUpdate()
    {
        if (!debugMenuInitialized)
            return;

        if (isGameOver && debugPanel.resolvedStyle.display == DisplayStyle.Flex)
            debugPanel.style.display = DisplayStyle.None;

        RefreshDebugMenu();
    }

    private void BuildDebugMenu()
    {
        UIDocument document = GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;
        VisualElement screen = root.Q<VisualElement>("screen");
        VisualElement topBar = root.Q<VisualElement>(className: "top-bar");

        if (screen == null || topBar == null)
        {
            Debug.LogError("Prototype debug menu: не найден screen или top-bar.");
            return;
        }

        HideOldResourceTestContainers(root);

        debugToggleButton = new Button(ToggleDebugPanel)
        {
            text = "DEBUG",
            tooltip = "Инструменты разработчика"
        };
        debugToggleButton.AddToClassList("debug-toggle-button");
        topBar.Add(debugToggleButton);

        debugPanel = new VisualElement();
        debugPanel.AddToClassList("debug-panel");
        debugPanel.style.display = DisplayStyle.None;

        VisualElement header = new VisualElement();
        header.AddToClassList("debug-header");

        Label title = new Label("DEBUG MENU");
        title.AddToClassList("debug-title");
        header.Add(title);

        Button closeButton = new Button(() => debugPanel.style.display = DisplayStyle.None)
        {
            text = "×"
        };
        closeButton.AddToClassList("debug-close-button");
        header.Add(closeButton);
        debugPanel.Add(header);

        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.AddToClassList("debug-scroll");
        debugPanel.Add(scroll);

        AddDebugSectionTitle(scroll, "ГОРОД");

        debugGoldValueLabel = CreateDebugValueLabel();
        scroll.Add(CreateDebugResourceRow(
            "Золото",
            goldMinus10Button,
            debugGoldValueLabel,
            goldPlus10Button));

        debugFoodValueLabel = CreateDebugValueLabel();
        scroll.Add(CreateDebugResourceRow(
            "Пища",
            foodMinus10Button,
            debugFoodValueLabel,
            foodPlus10Button));

        debugPopulationValueLabel = CreateDebugValueLabel();
        scroll.Add(CreateDebugResourceRow(
            "Население",
            populationMinus10Button,
            debugPopulationValueLabel,
            populationPlus10Button));

        debugMoodValueLabel = CreateDebugValueLabel();
        scroll.Add(CreateDebugResourceRow(
            "Настроение",
            moodMinus10Button,
            debugMoodValueLabel,
            moodPlus10Button));

        AddDebugSectionTitle(scroll, "ОТРЯД");

        debugArmyGoldMinus10Button = CreateDebugStepButton("−10", () => AdjustDebugArmyGold(-10));
        debugArmyGoldPlus10Button = CreateDebugStepButton("+10", () => AdjustDebugArmyGold(10));
        debugArmyGoldValueLabel = CreateDebugValueLabel();
        scroll.Add(CreateDebugResourceRow(
            "Золото отряда",
            debugArmyGoldMinus10Button,
            debugArmyGoldValueLabel,
            debugArmyGoldPlus10Button));

        debugArmySupplyMinus10Button = CreateDebugStepButton("−10", () => AdjustDebugArmySupply(-10));
        debugArmySupplyPlus10Button = CreateDebugStepButton("+10", () => AdjustDebugArmySupply(10));
        debugArmySupplyValueLabel = CreateDebugValueLabel();
        scroll.Add(CreateDebugResourceRow(
            "Снабжение",
            debugArmySupplyMinus10Button,
            debugArmySupplyValueLabel,
            debugArmySupplyPlus10Button));

        AddDebugSectionTitle(scroll, "ЭКСПЕДИЦИЯ");

        debugExpeditionStateLabel = new Label();
        debugExpeditionStateLabel.AddToClassList("debug-state-label");
        scroll.Add(debugExpeditionStateLabel);

        debugCapitalCrisisButton = CreateDebugActionButton(
            "ВЫЗВАТЬ КРИЗИС СТОЛИЦЫ",
            DebugTriggerCapitalCrisis);
        scroll.Add(debugCapitalCrisisButton);

        debugBackgroundIncidentButton = CreateDebugActionButton(
            "ВЫЗВАТЬ ФОНОВОЕ ПРОИСШЕСТВИЕ",
            DebugTriggerBackgroundIncident);
        scroll.Add(debugBackgroundIncidentButton);

        debugSignificantDecisionButton = CreateDebugActionButton(
            "ВЫЗВАТЬ ЗНАЧИМОЕ СОБЫТИЕ",
            DebugTriggerSignificantDecision);
        scroll.Add(debugSignificantDecisionButton);

        AddDebugSectionTitle(scroll, "ПАРТИЯ");

        debugResetGameButton = CreateDebugActionButton(
            "СБРОСИТЬ ПАРТИЮ",
            DebugResetGame);
        debugResetGameButton.AddToClassList("debug-danger-button");
        scroll.Add(debugResetGameButton);

        Label note = new Label(
            "Debug доступен только в Unity Editor и Development Build. " +
            "Принудительные события не завершают день.");
        note.AddToClassList("debug-note");
        scroll.Add(note);

        screen.Add(debugPanel);
    }

    private void HideOldResourceTestContainers(VisualElement root)
    {
        root.Query<VisualElement>(className: "resource-test-controls")
            .ForEach(container => container.style.display = DisplayStyle.None);
    }

    private VisualElement CreateDebugResourceRow(
        string title,
        Button minusButton,
        Label valueLabel,
        Button plusButton)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("debug-resource-row");

        Label titleLabel = new Label(title);
        titleLabel.AddToClassList("debug-resource-name");
        row.Add(titleLabel);

        MoveButtonToDebugRow(minusButton, row);
        row.Add(valueLabel);
        MoveButtonToDebugRow(plusButton, row);

        return row;
    }

    private void MoveButtonToDebugRow(Button button, VisualElement row)
    {
        button.RemoveFromHierarchy();
        button.AddToClassList("debug-step-button");
        row.Add(button);
    }

    private Label CreateDebugValueLabel()
    {
        Label label = new Label("0");
        label.AddToClassList("debug-resource-value");
        return label;
    }

    private Button CreateDebugStepButton(string text, Action clicked)
    {
        Button button = new Button(clicked)
        {
            text = text
        };
        button.AddToClassList("debug-step-button");
        return button;
    }

    private Button CreateDebugActionButton(string text, Action clicked)
    {
        Button button = new Button(clicked)
        {
            text = text
        };
        button.AddToClassList("debug-action-button");
        return button;
    }

    private void AddDebugSectionTitle(VisualElement parent, string text)
    {
        Label label = new Label(text);
        label.AddToClassList("debug-section-title");
        parent.Add(label);
    }

    private void ToggleDebugPanel()
    {
        if (!DebugToolsAvailable || isGameOver)
            return;

        bool isOpen = debugPanel.resolvedStyle.display == DisplayStyle.Flex;
        debugPanel.style.display =
            isOpen ? DisplayStyle.None : DisplayStyle.Flex;

        RefreshDebugMenu();
    }

    private void RefreshDebugMenu()
    {
        if (!debugMenuInitialized || gameState == null)
            return;

        debugToggleButton.SetEnabled(!isGameOver);

        debugGoldValueLabel.text = gameState.Gold.ToString();
        debugFoodValueLabel.text = gameState.Food.ToString();
        debugPopulationValueLabel.text = gameState.Population.ToString();
        debugMoodValueLabel.text = gameState.Mood.ToString();
        debugArmyGoldValueLabel.text = gameState.ArmyGold.ToString();
        debugArmySupplyValueLabel.text = gameState.ArmySupply.ToString();

        bool available = !isGameOver;
        debugArmyGoldMinus10Button.SetEnabled(available && gameState.ArmyGold > 0);
        debugArmyGoldPlus10Button.SetEnabled(available);
        debugArmySupplyMinus10Button.SetEnabled(available && gameState.ArmySupply > 0);
        debugArmySupplyPlus10Button.SetEnabled(available);

        bool hasExpedition = gameState.HasActiveExpedition;
        bool hasDecision = gameState.HasPendingExpeditionDecision;

        debugCapitalCrisisButton.SetEnabled(available);
        debugBackgroundIncidentButton.SetEnabled(
            available && hasExpedition && !hasDecision);

        bool canCreateDecision = false;

        if (hasExpedition && !hasDecision)
        {
            ExpeditionData expedition = gameState.ActiveExpedition;
            canCreateDecision =
                expedition.DaysRemaining > 0 &&
                (expedition.Phase == CommanderState.TravellingToLocation ||
                 expedition.Phase == CommanderState.ReturningToCastle);
        }

        debugSignificantDecisionButton.SetEnabled(available && canCreateDecision);
        debugResetGameButton.SetEnabled(true);
        debugExpeditionStateLabel.text = BuildDebugExpeditionStateText();
    }

    private string BuildDebugExpeditionStateText()
    {
        if (!gameState.HasActiveExpedition)
        {
            return
                "Состояние: армия в столице\n" +
                "Активная экспедиция: нет\n" +
                "Голод экспедиции подряд: " +
                gameState.ConsecutiveExpeditionSupplyShortageDays +
                BuildDebugWorldLayoutText();
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        LocationData location = gameState.FindLocation(expedition.LocationId);
        CommanderData commander = gameState.FindCommander(expedition.CommanderId);

        string locationName = location != null
            ? location.TravelTargetName
            : "неизвестно";
        string commanderName = commander != null ? commander.Name : "неизвестно";
        string phaseText = gameState.HasPendingExpeditionDecision
            ? "ожидает приказа"
            : expedition.IsExplorationInProgress
                ? "исследует локацию"
                : GetCommanderStateText(expedition.Phase);

        string researchText = "не начато";

        if (location != null)
        {
            if (location.IsExplored)
            {
                researchText = "завершено";
            }
            else if (expedition.IsExplorationInProgress)
            {
                int completed = Math.Max(
                    0,
                    location.ExplorationDays - expedition.ExplorationDaysRemaining);
                researchText = completed + "/" + location.ExplorationDays;
            }
            else if (location.ExplorationDays > 0)
            {
                researchText = "0/" + location.ExplorationDays;
            }
            else
            {
                researchText = "не реализовано";
            }
        }

        return
            "Командир: " + commanderName + "\n" +
            "Цель: " + locationName + "\n" +
            "Состояние: " + phaseText + "\n" +
            "Осталось дней пути: " + expedition.DaysRemaining + "\n" +
            "Голод подряд: " + gameState.ConsecutiveExpeditionSupplyShortageDays + "\n" +
            "Исследование: " + researchText +
            BuildDebugWorldLayoutText();
    }

    private string BuildDebugWorldLayoutText()
    {
        string result = "\nSeed карты: " + gameState.WorldSeed;

        foreach (LocationData location in gameState.Locations)
        {
            result +=
                "\n" + location.RegionName + " → " + location.Name +
                (location.IsDiscovered ? " [открыта]" : " [скрыта]");
        }

        return result;
    }

    private void AdjustDebugArmyGold(int delta)
    {
        if (isGameOver)
            return;

        gameState.ArmyGold = Math.Max(0, gameState.ArmyGold + delta);
        RefreshInterface();
    }

    private void AdjustDebugArmySupply(int delta)
    {
        if (isGameOver)
            return;

        gameState.ArmySupply = Math.Max(0, gameState.ArmySupply + delta);
        RefreshInterface();
    }

    private void DebugTriggerCapitalCrisis()
    {
        if (isGameOver)
            return;

        DayResolutionResult resolved = null;

        for (int attempt = 0; attempt < 100; attempt++)
        {
            DayResolutionResult candidate = new DayResolutionResult();
            CapitalCrisisSystem.ResolveForDay(gameState, gameState.Day, candidate);

            if (candidate.NewExpeditionIncidents.Count > 0)
            {
                resolved = candidate;
                break;
            }
        }

        if (resolved == null)
        {
            AddReport("[DEBUG] Не удалось принудительно вызвать кризис столицы.");
            return;
        }

        ApplyDebugResolutionResult(resolved, "Кризис столицы вызван вручную.");
    }

    private void DebugTriggerBackgroundIncident()
    {
        if (isGameOver ||
            !gameState.HasActiveExpedition ||
            gameState.HasPendingExpeditionDecision)
        {
            AddReport("[DEBUG] Фоновое происшествие сейчас недоступно.");
            return;
        }

        DayResolutionResult resolved = null;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            DayResolutionResult candidate = new DayResolutionResult();
            ExpeditionIncidentSystem.ResolveForDay(
                gameState,
                gameState.Day,
                candidate);

            if (candidate.NewExpeditionIncidents.Count > 0)
            {
                resolved = candidate;
                break;
            }
        }

        if (resolved == null)
        {
            AddReport("[DEBUG] Не удалось создать подходящее фоновое происшествие.");
            return;
        }

        ApplyDebugResolutionResult(resolved, "Фоновое происшествие вызвано вручную.");
    }

    private void DebugTriggerSignificantDecision()
    {
        if (isGameOver ||
            !gameState.HasActiveExpedition ||
            gameState.HasPendingExpeditionDecision)
        {
            AddReport("[DEBUG] Значимое событие сейчас недоступно.");
            return;
        }

        DayResolutionResult resolved = null;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            DayResolutionResult candidate = new DayResolutionResult();
            ExpeditionDecisionSystem.ResolveForDay(
                gameState,
                gameState.Day,
                candidate);

            if (gameState.HasPendingExpeditionDecision)
            {
                resolved = candidate;
                break;
            }
        }

        if (resolved == null)
        {
            AddReport(
                "[DEBUG] Не удалось создать значимое событие: " +
                "нет доступного события или экспедиция не находится в пути.");
            return;
        }

        ApplyDebugResolutionResult(resolved, "Значимое событие вызвано вручную.");
    }

    private void ApplyDebugResolutionResult(
        DayResolutionResult result,
        string fallbackMessage)
    {
        if (result.NewExpeditionIncidents.Count > 0)
            unreadIncidents.AddRange(result.NewExpeditionIncidents);

        string resultMessage = result.Messages.Count > 0
            ? string.Join("\n", result.Messages)
            : fallbackMessage;

        AddReport("[DEBUG]\n" + resultMessage);
        RefreshInterface();
        CheckForDefeat();
    }

    private void DebugResetGame()
    {
        debugPanel.style.display = DisplayStyle.None;
        StartNewGame();
    }
}
