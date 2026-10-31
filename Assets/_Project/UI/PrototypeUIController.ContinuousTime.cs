using System;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool continuousTimeInitialized;
    private GameState continuousBoundGameState;
    private float continuousDetailsRefreshTimer;
    private bool continuousDebugAutopauseRegistered;
    private const float MaxSimulationFrameSeconds = 0.1f;
    // Сглаживание времени кадра: неровные кадры не дают рывков бегущей фигуры.
    private const float FrameSecondsSmoothing = 0.15f;
    private float smoothedFrameSeconds;

    // Бриф правок п. 1: метки профайлера пути «час/сутки → донесение → UI»
    // (замер WorldMapPerformancePlayModeTests, фаза смены суток).
    private static readonly ProfilerMarker AdvanceMarker = new ProfilerMarker("KS.Simulation.Advance");
    private static readonly ProfilerMarker BatchMarker = new ProfilerMarker("KS.Batch.Process");
    private static readonly ProfilerMarker BatchReportMarker = new ProfilerMarker("KS.Batch.AddReport");
    private static readonly ProfilerMarker BatchModalsMarker = new ProfilerMarker("KS.Batch.QueueModals");
    private static readonly ProfilerMarker BatchInterfaceMarker = new ProfilerMarker("KS.Batch.RefreshInterface");
    private static readonly ProfilerMarker BatchStableMarker = new ProfilerMarker("KS.Batch.RefreshStableUi");
    private static readonly ProfilerMarker BatchTimeUiMarker = new ProfilerMarker("KS.Batch.RefreshTimeUi");
    private static readonly ProfilerMarker BatchNextModalMarker = new ProfilerMarker("KS.Batch.ShowNextModal");

    // 12И: без синхронизации с монитором (уровень качества Very Low) кадры
    // идут неровно, и карта за героем едет рывками. Синхронизация — на всю
    // игру; если её нет, частота кадров ограничена частотой экрана.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureFramePacing()
    {
        if (Application.isBatchMode)
            return;

        QualitySettings.vSyncCount = 1;
        double refresh = Screen.currentResolution.refreshRateRatio.value;
        Application.targetFrameRate = refresh > 1.0 ? Mathf.RoundToInt((float)refresh) : 60;
    }

    // Одиночный долгий кадр (загрузка сцены, подвисание) отбрасывается, а не
    // размазывается на следующие кадры: учитывается не больше двух обычных.
    private float NextFrameSeconds()
    {
        float raw = Mathf.Min(Time.unscaledDeltaTime, MaxSimulationFrameSeconds);
        if (smoothedFrameSeconds <= 0f)
        {
            smoothedFrameSeconds = Mathf.Min(raw, 1f / 60f);
            return smoothedFrameSeconds;
        }

        raw = Mathf.Min(raw, smoothedFrameSeconds * 2f);
        smoothedFrameSeconds = Mathf.Lerp(smoothedFrameSeconds, raw, FrameSecondsSmoothing);
        return smoothedFrameSeconds;
    }

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

        // ПР-01: часы сбрасываются только у кампании без своего хода времени —
        // загруженная и вернувшаяся в сцену сохраняют время суток и маршрут.
        if (!ContinuousSimulationSystem.HasRuntimeState(gameState))
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

    }

    private void RegisterContinuousMapInput()
    {
        worldMap.RegisterCallback<PointerDownEvent>(
            OnContinuousMapPointerDown,
            TrickleDown.TrickleDown);
        // 12И: зажатая кнопка — бег за курсором.
        worldMap.RegisterCallback<PointerMoveEvent>(OnContinuousMapPointerMove);
        worldMap.RegisterCallback<PointerUpEvent>(OnContinuousMapPointerUp);
        worldMap.RegisterCallback<PointerCaptureOutEvent>(OnContinuousMapPointerCaptureOut);
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
        // ПР-12К: исследуемое место — и закрытие его экрана, когда партии нет.
        TickLocalExploration(Time.unscaledDeltaTime);
        // 12Т: экран поселения — пока командир внутри и поверх ничего нет.
        TickSettlementScreen();
        TickWorldMapSearchAreas();
        TickWorldMapFog();
        TickRewardToasts();
        TickEventDialogue();

        if (gameState == null)
            return;

        if (!continuousTimeInitialized)
        {
            TryInitializeContinuousTime();
            return;
        }

        if (continuousBoundGameState != gameState)
        {
            if (!ContinuousSimulationSystem.HasRuntimeState(gameState))
                ContinuousSimulationSystem.Reset(gameState);
            continuousBoundGameState = gameState;
            RebindContinuousTimeButtons();
            RefreshContinuousTimeUi(true);
        }

        float frameSeconds = NextFrameSeconds();
        if (!isGameOver)
        {
            RefreshAutoTimeState();

            // 12И: бегущий отряд проходит 2 клетки в секунду — долгий кадр
            // (загрузка сцены, подвисание) не должен переносить героя скачком.
            // Бриф правок п. 18: темп множит время симуляции кадра — путь и
            // дела идут быстрее в реальных секундах при той же цене в часах.
            ContinuousSimulationBatch batch;
            using (AdvanceMarker.Auto())
                batch = ContinuousSimulationSystem.Advance(gameState, frameSeconds * MapTempo);

            if (batch.HasReportableContent)
            {
                using (BatchMarker.Auto())
                    ProcessContinuousSimulationBatch(batch);
            }
        }

        RegisterContinuousDebugAutopause();
        EnsureMapTempoBar();
        EnsureReportsDock();
        EnsureDeferredWorldMapUi();
        RefreshContinuousClockOnly();
        TickWorldMapHero(frameSeconds);

        continuousDetailsRefreshTimer += Time.unscaledDeltaTime;
        if (continuousDetailsRefreshTimer >= 0.20f)
        {
            continuousDetailsRefreshTimer = 0f;
            RefreshContinuousTimeUi(true);
        }
    }

    // 12И: части карты, которые поднимаются отложенно, запускаются через
    // RuntimeInitializeOnLoadMethod только при первой загрузке сцены. После
    // повторной загрузки (возврат из боя, загрузка партии) их поднимает этот
    // страховочный вызов — раз в полсекунды, пока всё не готово.
    private int deferredWorldMapUiFrame;

    private void EnsureDeferredWorldMapUi()
    {
        if (worldMapHeroInitialized &&
            worldMapPolishInitialized &&
            worldMapInteractionPolishInitialized &&
            worldMapLocationActionsInitialized)
        {
            return;
        }

        if (++deferredWorldMapUiFrame % 30 != 1)
            return;

        if (!worldMapPolishInitialized)
            TryInitializeWorldMapPolish();
        if (!worldMapInteractionPolishInitialized)
            TryInitializeWorldMapInteractionPolish();
        if (!worldMapLocationActionsInitialized)
            TryInitializeWorldMapLocationActions();
        if (!worldMapHeroInitialized)
            TryInitializeWorldMapHero();
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
            using (BatchReportMarker.Auto())
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

        using (BatchModalsMarker.Auto())
            QueueStrategicResultModals(batch.Result, reportIndex);

        // Дорожный Encounter (PrototypeUIController.Encounters.cs) пробует
        // открыться только когда этот же пакет не претендует ни на
        // MandatoryNotice, ни на паузу от чего-то другого (прибытие,
        // находка, решение) — TryOpenNarrativeDialogueById дополнительно
        // сам проверяет HasBlockingModalWorkExceptCamp и просто ничего не
        // делает, если что-то всё же блокирует.
        if (!batch.RequestAutoPause && batch.MandatoryNotice == null)
            TryResolveRoadEncounterOpportunity(batch.Result);

        using (BatchInterfaceMarker.Auto())
            RefreshInterface();
        if (stableUiInitialized)
        {
            using (BatchStableMarker.Auto())
                RefreshStableUiAfterStateChange();
        }

        using (BatchTimeUiMarker.Auto())
            RefreshContinuousTimeUi(true);
        using (BatchNextModalMarker.Auto())
            TryShowNextQueuedModal();
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
        if (gameState == null || isGameOver)
            return;

        // ПР-12К (канон v1.53 §28.3): в исследуемом месте мировые часы стоят —
        // время тратят шаги и действия (ContinuousSimulationSystem.AdvanceLocalHours).
        if (LocalExplorationService.IsActive(gameState))
        {
            ContinuousSimulationSystem.SetPaused(gameState, true);
            return;
        }

        // Бриф правок п. 11: чтение журнала время не двигает.
        if (IsJournalOpen)
        {
            ContinuousSimulationSystem.SetPaused(gameState, true);
            return;
        }

        if (HasBlockingModalWork())
            return;

        // Бриф исправлений п. 13: время идёт только при движении и явно
        // начатом деле похода.
        bool shouldRun = ContinuousSimulationSystem.HasMovementOrActivityInProgress(gameState);
        ContinuousSimulationSystem.SetPaused(gameState, !shouldRun);
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

}
