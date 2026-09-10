using System;
using KingdomSurvival.Chapter01;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool continuousTimeInitialized;
    private GameState continuousBoundGameState;
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

        // Модель "движение = течение времени" (раздел 1/19 инструкции):
        // ручной Пуск/Пауза больше не часть обычного интерфейса — кнопка
        // остаётся в дереве (не ломает AllRequiredElementsExist и прочие
        // запросы), но скрыта и не кликабельна. Течение времени полностью
        // выводится из состояния экспедиции в RefreshAutoTimeState().
        timeToggleButton.style.display = DisplayStyle.None;

        RebindContinuousTimeButtons();
        RegisterContinuousMapInput();
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
            RefreshAutoTimeState();

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

    // Единственное место, где решается, идёт ли стратегическое время —
    // модель "движение = течение времени" (раздел 1/6/7 инструкции).
    // Вызывается каждый кадр до Advance(), поэтому не нужно расставлять
    // SetPaused по местам, где экспедиция стартует/останавливается: как
    // только Phase/ActiveActivity меняются, следующий же кадр это отразит.
    // Модальные окна и обязательные решения по-прежнему управляют паузой
    // через PauseForBlockingModal — здесь их работа не переопределяется.
    private void RefreshAutoTimeState()
    {
        if (gameState == null || isGameOver || HasBlockingModalWork())
            return;

        // P09-T01/T02/T03: продолжение "старого пути" после временного
        // крюка и фиксация прибытия в область поиска — тоже читаются
        // каждый кадр наравне с паузой, не отдельным колбэком по месту
        // клика (раздел "Единый принцип" инструкции про карту/время).
        Chapter01StoryDirector.RefreshRoadState(gameState);

        // Обязательная/необязательная дорожная встреча первого похода —
        // единственный производственный случай, когда сюжетный диалог
        // открывается САМ, без клика игрока (раздел "Обязательная дорожная
        // встреча": "не случайный RNG, гарантированное обучение"). Успешное
        // открытие уже само ставит PauseForBlockingModal внутри
        // TryOpenNarrativeDialogueById — выходим сразу, не давая коду ниже
        // пересчитать паузу этим же кадром.
        string pendingRoadEventDialogueId = Chapter01StoryDirector.GetPendingRoadEventDialogueId(gameState);
        if (!string.IsNullOrEmpty(pendingRoadEventDialogueId) &&
            TryOpenNarrativeDialogueById(pendingRoadEventDialogueId))
        {
            return;
        }

        bool shouldRun = ContinuousSimulationSystem.HasMovementOrActivityInProgress(gameState);
        ContinuousSimulationSystem.SetPaused(gameState, !shouldRun);

        // P08+: ExpeditionStarted должен означать физическое начало
        // движения, а не подтверждение состава в Hero Screen/N10 (раздел
        // 3/18 инструкции про карту и время) — ставится здесь, по факту
        // того, что отряд действительно тронулся, а не откуда-то из UI
        // выбора бойцов.
        if (shouldRun &&
            gameState.Narrative != null &&
            gameState.Narrative.HasFlag(Chapter01Ids.Flags.FarRouteUnlocked) &&
            !gameState.Narrative.HasFlag(Chapter01Ids.Flags.ExpeditionStarted))
        {
            Chapter01StoryDirector.HandleStoryExpeditionStarted(gameState);
        }
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
