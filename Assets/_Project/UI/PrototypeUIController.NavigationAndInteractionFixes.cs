using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private const int AcceleratedSupplyInitialDelayMs = 360;

    private bool navigationAndInteractionFixesInitialized;
    private Button acceleratedSupplyHoldButton;
    private int acceleratedSupplyPointerId = -1;
    private int acceleratedSupplyDelta;
    private int acceleratedSupplyRepeatCount;
    private bool acceleratedSupplyRepeated;
    private IVisualElementScheduledItem acceleratedSupplySchedule;
    private Vector2 latestPortraitDragPointerPosition;

    private readonly Dictionary<string, Button> quickLocationActionButtons =
        new Dictionary<string, Button>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeNavigationAndInteractionFixesRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeNavigationAndInteractionFixes)
            .ExecuteLater(180);
    }

    private void TryInitializeNavigationAndInteractionFixes()
    {
        if (navigationAndInteractionFixesInitialized)
            return;

        if (!stableUiInitialized ||
            interfaceRoot == null ||
            gameState == null ||
            navExpeditionsButton == null ||
            persistentCommanderExpeditionButton == null ||
            quickExpeditionPopup == null ||
            supplyMinusButton == null ||
            supplyPlusButton == null)
        {
            ScheduleNavigationAndInteractionFixesRetry();
            return;
        }

        ApplyMapAndLocationsLabels();
        EnsureQuickLocationActionButtons();
        RegisterAcceleratedSupplyInput();

        interfaceRoot.RegisterCallback<PointerMoveEvent>(
            OnPortraitDragGhostPointerMove,
            TrickleDown.TrickleDown);

        quickExpeditionPopup.schedule
            .Execute(RefreshQuickLocationActionButtons)
            .Every(120);

        navigationAndInteractionFixesInitialized = true;
        RefreshQuickLocationActionButtons();
    }

    private void ScheduleNavigationAndInteractionFixesRetry()
    {
        UIDocument document = GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(TryInitializeNavigationAndInteractionFixes)
            .ExecuteLater(60);
    }

    private void ApplyMapAndLocationsLabels()
    {
        navExpeditionsButton.text = "Карта";
        navExpeditionsButton.tooltip = "Глобальная карта";

        persistentCommanderExpeditionButton.text = "ЛОКАЦИИ";
        persistentCommanderExpeditionButton.tooltip = "Открытые локации";

        if (expeditionsScreen != null)
        {
            Label mapTitle = expeditionsScreen.Q<Label>(className: "panel-title");
            if (mapTitle != null)
                mapTitle.text = "КАРТА";
        }

        Label quickTitle = quickExpeditionPopup.Q<Label>();
        if (quickTitle != null)
            quickTitle.text = "ОТКРЫТЫЕ ЛОКАЦИИ";
    }

    private void EnsureQuickLocationActionButtons()
    {
        if (gameState == null)
            return;

        foreach (LocationData location in gameState.Locations)
        {
            VisualElement card;
            if (!quickExpeditionCards.TryGetValue(location.Id, out card) || card == null)
                continue;

            Button existing = card.Q<Button>("quick-location-action-" + location.Id);
            if (existing != null)
            {
                quickLocationActionButtons[location.Id] = existing;
                continue;
            }

            VisualElement row = card.childCount > 1 ? card.ElementAt(1) : null;
            VisualElement info = row != null && row.childCount > 1
                ? row.ElementAt(1)
                : null;

            if (info == null)
                continue;

            string capturedId = location.Id;
            Button actionButton = new Button(
                () => OnQuickLocationActionClicked(capturedId));
            actionButton.name = "quick-location-action-" + location.Id;
            actionButton.style.height = 22f;
            actionButton.style.minHeight = 22f;
            actionButton.style.marginTop = 2f;
            actionButton.style.marginBottom = 0f;
            actionButton.style.marginLeft = 0f;
            actionButton.style.marginRight = 0f;
            actionButton.style.paddingTop = 0f;
            actionButton.style.paddingBottom = 0f;
            actionButton.style.paddingLeft = 4f;
            actionButton.style.paddingRight = 4f;
            actionButton.style.fontSize = 8f;
            actionButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            actionButton.style.backgroundColor =
                (Color)new Color32(57, 50, 38, 255);
            actionButton.style.color =
                (Color)new Color32(229, 193, 116, 255);
            SetExpeditionBorder(actionButton, 1f, ExpeditionRgb(112, 91, 56));
            SetExpeditionRadius(actionButton, 3f);

            info.Add(actionButton);
            quickLocationActionButtons[location.Id] = actionButton;

            Button imageButton;
            if (quickExpeditionImageButtons.TryGetValue(location.Id, out imageButton) &&
                imageButton != null)
            {
                imageButton.pickingMode = PickingMode.Ignore;
                imageButton.focusable = false;
                imageButton.tabIndex = -1;
            }

            Label legacyImageLabel;
            if (quickExpeditionImageLabels.TryGetValue(location.Id, out legacyImageLabel) &&
                legacyImageLabel != null)
            {
                legacyImageLabel.style.display = DisplayStyle.None;
            }

            if (imageButton != null &&
                imageButton.Q<Label>("quick-location-static-image-label") == null)
            {
                Label staticImageLabel = new Label(
                    location.IsDiscovered
                        ? "ИЗОБРАЖЕНИЕ\nЛОКАЦИИ"
                        : "НЕИЗВЕДАННАЯ\nОБЛАСТЬ");
                staticImageLabel.name = "quick-location-static-image-label";
                staticImageLabel.pickingMode = PickingMode.Ignore;
                staticImageLabel.style.fontSize = 8f;
                staticImageLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                staticImageLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                staticImageLabel.style.whiteSpace = WhiteSpace.Normal;
                staticImageLabel.style.color = ExpeditionRgb(129, 136, 146);
                imageButton.Add(staticImageLabel);
            }
        }
    }

    private void RefreshQuickLocationActionButtons()
    {
        if (!navigationAndInteractionFixesInitialized || gameState == null)
            return;

        EnsureQuickLocationActionButtons();

        bool hasExpedition = gameState.HasActiveExpedition;
        bool blockedByDecision = gameState.HasPendingExpeditionDecision;
        bool blockedByResearch =
            hasExpedition && gameState.ActiveExpedition.IsLocationResearchInProgress;

        foreach (LocationData location in gameState.Locations)
        {
            Button button;
            if (!quickLocationActionButtons.TryGetValue(location.Id, out button) ||
                button == null)
            {
                continue;
            }

            if (isGameOver)
            {
                button.text = "НЕДОСТУПНО";
                button.SetEnabled(false);
                continue;
            }

            if (!hasExpedition)
            {
                button.text = "ОТПРАВИТЬ";
                button.tooltip = "Отправить отряд к этой локации.";
                button.SetEnabled(selectedFighterIds.Count > 0);
                continue;
            }

            if (blockedByDecision || blockedByResearch)
            {
                button.text = "НЕДОСТУПНО";
                button.tooltip = blockedByDecision
                    ? "Сначала примите обязательное решение."
                    : "Нельзя менять маршрут во время исследования.";
                button.SetEnabled(false);
                continue;
            }

            ExpeditionData expedition = gameState.ActiveExpedition;
            bool locationIsCurrentTarget =
                expedition.Phase != CommanderState.ReturningToCastle &&
                !expedition.IsScoutingTarget &&
                expedition.LocationId == location.Id;

            if (locationIsCurrentTarget)
            {
                button.text = "ТЕКУЩАЯ ЦЕЛЬ";
                button.tooltip = "Армия уже направляется сюда или находится здесь.";
                button.SetEnabled(false);
                continue;
            }

            button.text = gameState.CanCancelPreparedExpedition
                ? "ИЗМЕНИТЬ ЦЕЛЬ"
                : "ИЗМЕНИТЬ МАРШРУТ";
            button.tooltip =
                "Построить новый прямой маршрут от текущей позиции армии.";
            button.SetEnabled(true);
        }
    }

    private void OnQuickLocationActionClicked(string locationId)
    {
        if (isGameOver || gameState == null)
            return;

        LocationData location = gameState.FindLocation(locationId);
        if (location == null || !location.IsVisibleOnMap || location.IsWaypoint)
            return;

        if (gameState.HasActiveExpedition)
        {
            if (gameState.HasPendingExpeditionDecision)
            {
                AddReport("Сначала требуется принять обязательное решение.");
                return;
            }

            if (gameState.ActiveExpedition.IsLocationResearchInProgress)
            {
                AddReport("Нельзя менять маршрут во время исследования локации.");
                return;
            }

            bool alreadyTarget =
                gameState.ActiveExpedition.Phase != CommanderState.ReturningToCastle &&
                !gameState.ActiveExpedition.IsScoutingTarget &&
                gameState.ActiveExpedition.LocationId == location.Id;
            if (alreadyTarget)
                return;
        }

        IssueImmediateMapOrder(
            location.MapXPercent,
            location.MapYPercent,
            location.Id);
        HideQuickExpeditionPopup();
        RefreshQuickLocationActionButtons();
    }

    private void OnPortraitDragGhostPointerMove(PointerMoveEvent evt)
    {
        if (stableDraggedCard == null)
            return;

        latestPortraitDragPointerPosition = evt.position;
        interfaceRoot.schedule
            .Execute(ApplyPortraitDragGhostGeometry)
            .ExecuteLater(1);
    }

    private void ApplyPortraitDragGhostGeometry()
    {
        if (stableDragGhost == null || stableDraggedCard == null)
            return;

        float width = stableDraggedCard.resolvedStyle.width;
        float height = stableDraggedCard.resolvedStyle.height;

        if (float.IsNaN(width) || width < 20f)
            width = 76f;
        if (float.IsNaN(height) || height < 20f)
            height = 112f;

        stableDragGhost.style.width = width;
        stableDragGhost.style.height = height;
        stableDragGhost.style.left = latestPortraitDragPointerPosition.x - width * 0.5f;
        stableDragGhost.style.top = latestPortraitDragPointerPosition.y - height * 0.5f;
    }

    private void RegisterAcceleratedSupplyInput()
    {
        interfaceRoot.RegisterCallback<PointerDownEvent>(
            OnAcceleratedSupplyPointerDown,
            TrickleDown.TrickleDown);
        interfaceRoot.RegisterCallback<PointerUpEvent>(
            OnAcceleratedSupplyPointerUp,
            TrickleDown.TrickleDown);
        interfaceRoot.RegisterCallback<PointerCaptureOutEvent>(
            OnAcceleratedSupplyPointerCaptureOut,
            TrickleDown.TrickleDown);
    }

    private void OnAcceleratedSupplyPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0)
            return;

        Button button = FindSupplyButton(evt.target as VisualElement);
        if (button == null)
            return;

        // Перехватываем событие раньше старого Button/Clickable и старых
        // supply-hold callbacks. Так короткий клик и удержание имеют ровно один
        // источник истины и не могут сработать дважды.
        evt.StopImmediatePropagation();

        if (isGameOver || gameState == null || !gameState.CanAdjustArmySupply)
            return;

        int delta = button == supplyPlusButton ? 1 : -1;
        bool canTransfer = delta > 0 ? gameState.Food > 0 : gameState.ArmySupply > 0;
        if (!canTransfer)
            return;

        StopAcceleratedSupplyHold();
        acceleratedSupplyHoldButton = button;
        acceleratedSupplyPointerId = evt.pointerId;
        acceleratedSupplyDelta = delta;
        acceleratedSupplyRepeatCount = 0;
        acceleratedSupplyRepeated = false;

        if (!interfaceRoot.HasPointerCapture(evt.pointerId))
            interfaceRoot.CapturePointer(evt.pointerId);

        ScheduleAcceleratedSupplyStep(AcceleratedSupplyInitialDelayMs);
    }

    private void OnAcceleratedSupplyPointerUp(PointerUpEvent evt)
    {
        if (acceleratedSupplyHoldButton == null ||
            evt.pointerId != acceleratedSupplyPointerId)
        {
            return;
        }

        evt.StopImmediatePropagation();

        if (!acceleratedSupplyRepeated)
            TransferAcceleratedSupplyOnce();

        StopAcceleratedSupplyHold();
    }

    private void OnAcceleratedSupplyPointerCaptureOut(PointerCaptureOutEvent evt)
    {
        if (acceleratedSupplyHoldButton == null)
            return;

        StopAcceleratedSupplyHold();
    }

    private Button FindSupplyButton(VisualElement target)
    {
        VisualElement current = target;
        while (current != null && current != interfaceRoot)
        {
            if (current == supplyPlusButton)
                return supplyPlusButton;
            if (current == supplyMinusButton)
                return supplyMinusButton;
            current = current.parent;
        }
        return null;
    }

    private void ScheduleAcceleratedSupplyStep(int delayMs)
    {
        if (acceleratedSupplyHoldButton == null || acceleratedSupplyDelta == 0)
            return;

        acceleratedSupplySchedule =
            interfaceRoot.schedule.Execute(PerformAcceleratedSupplyStep);
        acceleratedSupplySchedule.ExecuteLater(delayMs);
    }

    private void PerformAcceleratedSupplyStep()
    {
        if (acceleratedSupplyHoldButton == null ||
            gameState == null ||
            isGameOver ||
            !gameState.CanAdjustArmySupply)
        {
            StopAcceleratedSupplyHold();
            return;
        }

        bool canTransfer = acceleratedSupplyDelta > 0
            ? gameState.Food > 0
            : gameState.ArmySupply > 0;
        if (!canTransfer)
        {
            StopAcceleratedSupplyHold();
            return;
        }

        TransferAcceleratedSupplyOnce();
        acceleratedSupplyRepeated = true;
        acceleratedSupplyRepeatCount++;

        int nextDelay = acceleratedSupplyRepeatCount < 5
            ? 170
            : acceleratedSupplyRepeatCount < 14
                ? 95
                : 50;
        ScheduleAcceleratedSupplyStep(nextDelay);
    }

    private void TransferAcceleratedSupplyOnce()
    {
        if (gameState == null || acceleratedSupplyDelta == 0)
            return;

        if (acceleratedSupplyDelta > 0)
            gameState.TryAddArmySupply();
        else
            gameState.TryRemoveArmySupply();

        RefreshStableResourceUi();
    }

    private void StopAcceleratedSupplyHold()
    {
        if (acceleratedSupplySchedule != null)
            acceleratedSupplySchedule.Pause();

        if (interfaceRoot != null &&
            acceleratedSupplyPointerId >= 0 &&
            interfaceRoot.HasPointerCapture(acceleratedSupplyPointerId))
        {
            interfaceRoot.ReleasePointer(acceleratedSupplyPointerId);
        }

        acceleratedSupplySchedule = null;
        acceleratedSupplyHoldButton = null;
        acceleratedSupplyPointerId = -1;
        acceleratedSupplyDelta = 0;
        acceleratedSupplyRepeatCount = 0;
        acceleratedSupplyRepeated = false;
    }
}
