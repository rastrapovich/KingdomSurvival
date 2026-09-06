using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool continuousTimeInitialized;
    private GameState continuousBoundGameState;
    private Button continuousSpeedButton;
    private float continuousDetailsRefreshTimer;
    private bool continuousDebugAutopauseRegistered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeContinuousTimeRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeContinuousTime)
            .ExecuteLater(80);
    }

    private void TryInitializeContinuousTime()
    {
        if (continuousTimeInitialized)
            return;

        if (interfaceRoot == null ||
            gameState == null ||
            timeToggleButton == null ||
            worldMap == null ||
            worldMapCapitalButton == null ||
            returnExpeditionButton == null ||
            researchExpeditionButton == null)
        {
            ScheduleContinuousTimeRetry();
            return;
        }

        RebindContinuousTimeButtons();
        RegisterContinuousMapInput();
        EnsureContinuousSpeedButton();
        RegisterContinuousDebugAutopause();

        ContinuousSimulationSystem.Reset(gameState);
        continuousBoundGameState = gameState;
        continuousTimeInitialized = true;
        RefreshContinuousTimeUi(true);
    }

    private void ScheduleContinuousTimeRetry()
    {
        UIDocument document = GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(TryInitializeContinuousTime)
            .ExecuteLater(40);
    }

    private void RebindContinuousTimeButtons()
    {
        timeToggleButton.clicked -= OnContinuousPauseClicked;
        timeToggleButton.clicked += OnContinuousPauseClicked;

        returnExpeditionButton.clicked -= OnExpeditionActionClicked;
        returnExpeditionButton.clicked -= OnStableExpeditionActionClicked;
        returnExpeditionButton.clicked -= OnContinuousExpeditionActionClicked;
        returnExpeditionButton.clicked += OnContinuousExpeditionActionClicked;

        researchExpeditionButton.clicked -= OnResearchExpeditionClicked;
        researchExpeditionButton.clicked -= OnStableResearchExpeditionClicked;
        researchExpeditionButton.clicked -= OnContinuousResearchClicked;
        researchExpeditionButton.clicked += OnContinuousResearchClicked;

        worldMapCapitalButton.clicked -= OnWorldMapCapitalClicked;
        worldMapCapitalButton.clicked -= OnContinuousCapitalClicked;
        worldMapCapitalButton.clicked += OnContinuousCapitalClicked;
    }

    private void RegisterContinuousMapInput()
    {
        worldMap.RegisterCallback<PointerDownEvent>(
            OnContinuousMapPointerDown,
            TrickleDown.TrickleDown);
    }

    private void EnsureContinuousSpeedButton()
    {
        if (continuousSpeedButton != null)
            return;

        VisualElement topBar =
            interfaceRoot.Q<VisualElement>(className: "top-bar");

        if (topBar == null)
            return;

        continuousSpeedButton = new Button(OnContinuousSpeedClicked)
        {
            text = "×3",
            tooltip = "Ускорить течение времени в 3 раза"
        };

        continuousSpeedButton.style.width = 58f;
        continuousSpeedButton.style.height = 34f;
        continuousSpeedButton.style.marginRight = 6f;
        continuousSpeedButton.style.backgroundColor =
            (Color)new Color32(61, 55, 40, 255);
        continuousSpeedButton.style.color =
            (Color)new Color32(231, 192, 101, 255);
        continuousSpeedButton.style.borderLeftWidth = 1f;
        continuousSpeedButton.style.borderRightWidth = 1f;
        continuousSpeedButton.style.borderTopWidth = 1f;
        continuousSpeedButton.style.borderBottomWidth = 1f;
        continuousSpeedButton.style.borderLeftColor =
            (Color)new Color32(132, 102, 48, 255);
        continuousSpeedButton.style.borderRightColor =
            (Color)new Color32(132, 102, 48, 255);
        continuousSpeedButton.style.borderTopColor =
            (Color)new Color32(132, 102, 48, 255);
        continuousSpeedButton.style.borderBottomColor =
            (Color)new Color32(132, 102, 48, 255);
        continuousSpeedButton.style.unityFontStyleAndWeight = FontStyle.Bold;

        topBar.Add(continuousSpeedButton);
    }

    private void RegisterContinuousDebugAutopause()
    {
        if (continuousDebugAutopauseRegistered)
            return;

        if (debugSignificantDecisionButton != null)
            debugSignificantDecisionButton.clicked += OnContinuousDebugDecisionCompleted;

        continuousDebugAutopauseRegistered = debugSignificantDecisionButton != null;
    }

    private void OnContinuousDebugDecisionCompleted()
    {
        if (gameState == null || isGameOver || !gameState.HasPendingExpeditionDecision)
            return;

        PauseForBlockingModal();
        OpenDecision(gameState.ActiveExpedition.PendingDecision);
        RefreshContinuousClockOnly();
    }

    private void Update()
    {
        RefreshNarrativePresentationFrame();

        if (gameState == null)
            return;

        if (!continuousTimeInitialized)
        {
            TryInitializeContinuousTime();
            return;
        }

        if (continuousBoundGameState != gameState)
        {
            ContinuousSimulationSystem.Reset(gameState);
            continuousBoundGameState = gameState;
            RebindContinuousTimeButtons();
            RefreshContinuousTimeUi(true);
        }

        if (!isGameOver)
        {
            ContinuousSimulationBatch batch =
                ContinuousSimulationSystem.Advance(
                    gameState,
                    Time.unscaledDeltaTime);

            if (batch.HasReportableContent)
                ProcessContinuousSimulationBatch(batch);
        }

        RegisterContinuousDebugAutopause();
        RefreshContinuousClockOnly();
        RefreshContinuousMapMarker();

        continuousDetailsRefreshTimer += Time.unscaledDeltaTime;
        if (continuousDetailsRefreshTimer >= 0.20f)
        {
            continuousDetailsRefreshTimer = 0f;
            RefreshContinuousTimeUi(true);
        }
    }

    private void ProcessContinuousSimulationBatch(
        ContinuousSimulationBatch batch)
    {
        if (batch.RequestAutoPause)
            PauseForBlockingModal(true);

        if (batch.MandatoryNotice == null &&
            batch.Result.NewExpeditionIncidents.Count > 0)
        {
            unreadIncidents.AddRange(batch.Result.NewExpeditionIncidents);
        }

        string reportText = string.Join("\n", batch.Result.Messages);
        if (string.IsNullOrWhiteSpace(reportText) && batch.MandatoryNotice != null)
            reportText = batch.MandatoryNotice.Title;

        int reportIndex = -1;
        if (!string.IsNullOrWhiteSpace(reportText))
        {
            reportText =
                "[" + ContinuousSimulationSystem.FormatClock(batch.EventHour) + "]\n" +
                NormalizeContinuousReportText(reportText);
            reportIndex = AddReport(reportText, batch.ReportDay);
        }

        if (batch.MandatoryNotice == null)
        {
            RegisterIncidentReports(
                batch.Result.NewExpeditionIncidents,
                reportIndex);
        }

        if (batch.MandatoryNotice != null)
        {
            QueueNotice(batch.MandatoryNotice, reportIndex);
            MarkReportUnread(reportIndex);
        }

        QueueStrategicResultModals(batch.Result, reportIndex);

        RefreshInterface();
        if (stableUiInitialized)
            RefreshStableUiAfterStateChange();

        RefreshContinuousTimeUi(true);
        TryShowNextQueuedModal();
        CheckForDefeat();
    }

    private void OnContinuousPauseClicked()
    {
        if (isGameOver || HasBlockingModalWork())
            return;

        ContinuousSimulationSystem.TogglePause(gameState);
        RefreshContinuousClockOnly();
    }

    private void OnContinuousSpeedClicked()
    {
        if (isGameOver)
            return;

        ContinuousSimulationSystem.ToggleSpeed(gameState);
        RefreshContinuousClockOnly();
    }

    private void OnContinuousResearchClicked()
    {
        if (isGameOver)
            return;

        string ignoredMessage;
        bool started =
            gameState.TryStartLocationResearch(out ignoredMessage);

        if (!started)
        {
            AddReport(NormalizeContinuousReportText(ignoredMessage));
            RefreshContinuousTimeUi(true);
            return;
        }

        LocationData location =
            gameState.FindLocation(gameState.ActiveExpedition.LocationId);
        double hours =
            location != null ? location.ExplorationHours : 0.0;

        AddReport(
            "Исследование начато. Расчётное время: " +
            ContinuousExpeditionCommands.FormatHours(hours) + ".");
        RefreshContinuousTimeUi(true);
    }

    private void OnContinuousExpeditionActionClicked()
    {
        if (isGameOver || !gameState.HasActiveExpedition)
            return;

        if (gameState.HasPendingExpeditionDecision ||
            gameState.ActiveExpedition.IsLocationResearchInProgress)
        {
            return;
        }

        string resultMessage;

        if (gameState.CanCancelPreparedExpedition)
        {
            gameState.TryCancelPreparedExpedition(out resultMessage);
            resultMessage =
                "Приказ на отправку отменён. Командир и выбранные бойцы " +
                "остаются в поселении; течение времени не изменилось.";
        }
        else
        {
            ContinuousExpeditionCommands.TryOrderReturn(
                gameState,
                out resultMessage);
        }

        AddReport(NormalizeContinuousReportText(resultMessage));
        RefreshInterface();
        if (stableUiInitialized)
            RefreshStableUiAfterStateChange();
        RefreshContinuousTimeUi(true);
    }

    private void OnContinuousCapitalClicked()
    {
        if (!gameState.HasActiveExpedition ||
            gameState.HasPendingExpeditionDecision ||
            gameState.ActiveExpedition.IsLocationResearchInProgress)
        {
            return;
        }

        OnContinuousExpeditionActionClicked();
    }
}
