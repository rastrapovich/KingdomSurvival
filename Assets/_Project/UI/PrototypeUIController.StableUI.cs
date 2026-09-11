using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool stableUiInitialized;
    private bool stableUiInitializing;
    private int renderedReportHash = int.MinValue;

    private VisualElement persistentCommanderPanel;
    private Button persistentCommanderExpeditionButton;
    private Label persistentCommanderStateLabel;
    private Label persistentCommanderTargetLabel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeStableUiRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();

        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeStableUi)
            .ExecuteLater(1);
    }

    private void TryInitializeStableUi()
    {
        if (stableUiInitialized || stableUiInitializing)
            return;

        stableUiInitializing = true;

        if (interfaceRoot == null || gameState == null)
        {
            stableUiInitializing = false;
            UIDocument document = GetComponent<UIDocument>();

            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInitializeStableUi)
                    .ExecuteLater(20);
            }

            return;
        }

        persistentCommanderPanel =
            interfaceRoot.Q<VisualElement>("persistent-commander-panel");
        persistentCommanderExpeditionButton =
            interfaceRoot.Q<Button>("persistent-commander-expedition-button");
        persistentCommanderStateLabel =
            interfaceRoot.Q<Label>("persistent-commander-state");
        persistentCommanderTargetLabel =
            interfaceRoot.Q<Label>("persistent-commander-target");

        if (persistentCommanderPanel == null ||
            persistentCommanderExpeditionButton == null ||
            persistentCommanderStateLabel == null ||
            persistentCommanderTargetLabel == null)
        {
            Debug.LogError("Stable UI: не найдена статическая плашка командира в Prototype_Main.uxml.");
            stableUiInitializing = false;
            return;
        }

        RebindStableUiCallbacks();
        RegisterRoyalReportRefresh();
        InitializeJourneySummaryUi();
        InitializeExpeditionViewsUi();

        stableUiInitialized = true;
        stableUiInitializing = false;
        RefreshStableUiAfterStateChange();
    }

    private void RebindStableUiCallbacks()
    {
        returnExpeditionButton.clicked -= OnExpeditionActionClicked;
        returnExpeditionButton.clicked += OnStableExpeditionActionClicked;
        researchExpeditionButton.clicked -= OnResearchExpeditionClicked;
        researchExpeditionButton.clicked += OnStableResearchExpeditionClicked;

        persistentCommanderExpeditionButton.clicked += ToggleQuickExpeditionPopup;

        navCapitalButton.clicked += OnStableNavigationChanged;
        navExpeditionsButton.clicked += OnStableNavigationChanged;

        restartGameButton.clicked += OnStablePostActionRefresh;
        incidentUnderstoodButton.clicked += OnStablePostActionRefresh;

        if (decisionOptionAButton != null)
            decisionOptionAButton.clicked += OnStablePostActionRefresh;
        if (decisionOptionBButton != null)
            decisionOptionBButton.clicked += OnStablePostActionRefresh;

        goldMinus10Button.clicked += OnStablePostActionRefresh;
        goldPlus10Button.clicked += OnStablePostActionRefresh;
        foodMinus10Button.clicked += OnStablePostActionRefresh;
        foodPlus10Button.clicked += OnStablePostActionRefresh;
        populationMinus10Button.clicked += OnStablePostActionRefresh;
        populationPlus10Button.clicked += OnStablePostActionRefresh;
        moodMinus10Button.clicked += OnStablePostActionRefresh;
        moodPlus10Button.clicked += OnStablePostActionRefresh;
    }

    private void OnStableNavigationChanged()
    {
        HideQuickExpeditionPopup();
        // Раздел 20 инструкции P08J: переход на Столицу/Экспедицию тоже
        // закрывает Journal — как и другие fullscreen-слои, он не должен
        // оставаться открытым поверх основной навигации.
        CloseJournal();
        RefreshPersistentCommanderNavigationState();
    }

    private void OnStablePostActionRefresh()
    {
        if (!stableUiInitialized)
            return;

        interfaceRoot.schedule
            .Execute(RefreshStableUiAfterStateChange)
            .ExecuteLater(1);
    }

    private void OnStableResearchExpeditionClicked()
    {
        if (isGameOver)
            return;

        string resultMessage;
        gameState.TryStartLocationResearch(out resultMessage);
        AddReport(resultMessage);
        RefreshStableUiAfterStateChange();
    }

    private void OnStableExpeditionActionClicked()
    {
        if (isGameOver)
            return;

        string resultMessage;
        bool cancelled = false;

        if (gameState.CanCancelPreparedExpedition)
        {
            cancelled =
                gameState.TryCancelPreparedExpedition(out resultMessage);

            if (cancelled)
                selectedFighterIds.Clear();
        }
        else
        {
            gameState.TryOrderReturn(out resultMessage);
        }

        AddReport(resultMessage);
        RefreshStableUiAfterStateChange();
    }

    private void OnStableArmyGoldPlusClicked()
    {
        if (!isGameOver && gameState.CanAdjustArmySupply && gameState.Gold > 0)
        {
            gameState.Gold--;
            gameState.ArmyGold++;
        }
        RefreshStableResourceUi();
    }

    private void OnStableArmyGoldMinusClicked()
    {
        if (!isGameOver && gameState.CanAdjustArmySupply && gameState.ArmyGold > 0)
        {
            gameState.ArmyGold--;
            gameState.Gold++;
        }
        RefreshStableResourceUi();
    }

    private void OnStableSupplyPlusClicked()
    {
        if (!isGameOver)
            gameState.TryAddArmySupply();
        RefreshStableResourceUi();
    }

    private void OnStableSupplyMinusClicked()
    {
        if (!isGameOver)
            gameState.TryRemoveArmySupply();
        RefreshStableResourceUi();
    }

    private void RefreshStableResourceUi()
    {
        goldLabel.text = "Золото: " + gameState.Gold;
        foodLabel.text = "Пища: " + gameState.Food;
        RefreshHeroScreenSupplyPanel();
    }

    private void RefreshStableUiAfterStateChange()
    {
        if (!stableUiInitialized || gameState == null)
            return;

        RefreshHeroScreenSupplyPanel();
        RefreshExpeditionPanel();
        RefreshExpeditionViewState();
        RefreshJournalNotificationState();
        if (IsJournalOpen)
            RefreshJournal();

        // P09 hotfix: Camp availability depends on transient blocking state.
        // Refresh it together with the rest of the persistent UI after every
        // state-changing action so the nav button cannot keep a stale disabled
        // state after a dialogue/mandatory modal has already closed.
        RefreshCampNavButtonState();
        if (IsCampScreenOpen)
            RefreshCampScreen();

        RefreshJourneySummaryFromState();
        RefreshIncidentNotifications();
        RefreshPersistentCommanderNavigationState();
        RefreshTimeControlAvailability();
        ScheduleRoyalReportsRefresh();
    }

    private void RefreshPersistentCommanderNavigationState()
    {
        if (persistentCommanderExpeditionButton == null)
            return;

        bool expeditionActive =
            openedScreen.HasValue && openedScreen.Value == MainScreen.Expeditions;
        SetPersistentNavActive(persistentCommanderExpeditionButton, expeditionActive);
    }

    private static void SetPersistentNavActive(Button button, bool active)
    {
        if (active)
            button.AddToClassList("persistent-nav-active");
        else
            button.RemoveFromClassList("persistent-nav-active");
    }

    private void RegisterRoyalReportRefresh()
    {
        reportHistoryLabel.RegisterCallback<GeometryChangedEvent>(
            _ => ScheduleRoyalReportsRefresh());
    }

    private void ScheduleRoyalReportsRefresh()
    {
        if (reportHistoryScroll == null || reportHistoryLabel == null)
            return;
        reportHistoryScroll.schedule
            .Execute(RenderRoyalReportsNewestFirst)
            .ExecuteLater(2);
    }

    private void RenderRoyalReportsNewestFirst()
    {
        if (reportHistory == null || reportHistoryLabel == null)
            return;

        int hash = 17;
        for (int i = 0; i < reportHistory.Count; i++)
        {
            string entry = reportHistory[i];
            hash = hash * 31 + (entry != null ? entry.GetHashCode() : 0);
            hash = hash * 31 +
                (i < reportReadStates.Count && reportReadStates[i] ? 1 : 0);
        }

        if (hash == renderedReportHash)
        {
            reportHistoryScroll.scrollOffset = Vector2.zero;
            return;
        }

        renderedReportHash = hash;
        List<string> newestFirst = new List<string>(reportHistory.Count);
        for (int i = reportHistory.Count - 1; i >= 0; i--)
        {
            string entry = reportHistory[i] ?? string.Empty;
            if (i < reportRequiresAcknowledgement.Count &&
                reportRequiresAcknowledgement[i])
            {
                bool isRead = i < reportReadStates.Count && reportReadStates[i];
                entry = (isRead ? "[ПРОЧИТАНО]\n" : "[НЕ ПРОЧИТАНО]\n") + entry;
            }
            entry = entry.Replace(
                "Откройте нужный экран круглой кнопкой слева сверху.",
                "Выберите нужный раздел в нижнем меню.");
            newestFirst.Add(entry);
        }

        string expected = string.Join("\n\n", newestFirst);
        if (reportHistoryLabel.text != expected)
            reportHistoryLabel.text = expected;
        reportHistoryScroll.scrollOffset = Vector2.zero;
    }
}
