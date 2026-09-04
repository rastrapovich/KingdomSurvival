using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private const float FighterDoubleClickWindowSeconds = 0.34f;

    private bool fighterDragRootTrackingInstalled;
    private Button capitalMoveAllButton;
    private Button commanderMoveAllButton;
    private string lastFighterClickId;
    private float lastFighterClickTime = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallFighterDragRootTrackingAfterSceneLoad()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInstallFighterDragRootTracking)
            .ExecuteLater(120);
    }

    private void TryInstallFighterDragRootTracking()
    {
        if (fighterDragRootTrackingInstalled)
            return;

        if (!stableUiInitialized ||
            interfaceRoot == null ||
            gameState == null ||
            commanderGarrisonDropZone == null ||
            capitalGarrisonDropZone == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInstallFighterDragRootTracking)
                    .ExecuteLater(60);
            }
            return;
        }

        interfaceRoot.UnregisterCallback<PointerDownEvent>(
            OnStableArmyPointerDown,
            TrickleDown.TrickleDown);

        interfaceRoot.RegisterCallback<PointerDownEvent>(
            OnUnifiedFighterPointerDown,
            TrickleDown.TrickleDown);
        interfaceRoot.RegisterCallback<PointerMoveEvent>(
            OnUnifiedFighterPointerMove,
            TrickleDown.TrickleDown);
        interfaceRoot.RegisterCallback<PointerUpEvent>(
            OnUnifiedFighterPointerUp,
            TrickleDown.TrickleDown);

        EnsureMoveAllButtons();
        interfaceRoot.schedule
            .Execute(RefreshArmyTransferControls)
            .Every(150);

        fighterDragRootTrackingInstalled = true;
        RefreshArmyTransferControls();
    }

    private bool CanEditArmyRosterNow()
    {
        if (isGameOver || gameState == null)
            return false;

        return !gameState.HasActiveExpedition ||
               CanEditContinuousPreparedRoster();
    }

    private void OnUnifiedFighterPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || !CanEditArmyRosterNow())
            return;

        VisualElement card = FindStableFighterCard(evt.target as VisualElement);
        if (card == null)
            return;

        string fighterId = card.userData as string;
        if (string.IsNullOrEmpty(fighterId))
            return;

        CleanupStableArmyDrag();
        stableDraggedFighterId = fighterId;
        stableDraggedPointerId = evt.pointerId;
        stableDragStartPosition = evt.position;
        stableDraggedCard = card;
        stableDragStarted = false;

        if (!interfaceRoot.HasPointerCapture(evt.pointerId))
            interfaceRoot.CapturePointer(evt.pointerId);

        evt.StopImmediatePropagation();
    }

    private void OnUnifiedFighterPointerMove(PointerMoveEvent evt)
    {
        if (stableDraggedCard == null ||
            stableDraggedPointerId != evt.pointerId)
            return;

        if (!CanEditArmyRosterNow())
        {
            CleanupStableArmyDrag();
            ResetFighterDoubleClick();
            return;
        }

        if (!stableDragStarted &&
            Vector2.Distance(stableDragStartPosition, evt.position) >=
            FighterDragThreshold)
        {
            BeginUnifiedArmyDrag(evt.position);
            ResetFighterDoubleClick();
        }

        if (stableDragStarted)
        {
            UpdateUnifiedArmyDragGhost(evt.position);
            SetStableDropHighlight(
                commanderGarrisonDropZone,
                commanderGarrisonDropZone.worldBound.Contains(evt.position));
            SetStableDropHighlight(
                capitalGarrisonDropZone,
                capitalGarrisonDropZone.worldBound.Contains(evt.position));
        }

        evt.StopImmediatePropagation();
    }

    private void OnUnifiedFighterPointerUp(PointerUpEvent evt)
    {
        if (stableDraggedCard == null ||
            stableDraggedPointerId != evt.pointerId)
            return;

        string fighterId = stableDraggedFighterId;
        bool wasDragging = stableDragStarted;
        bool toCommander =
            wasDragging && commanderGarrisonDropZone.worldBound.Contains(evt.position);
        bool toCapital =
            wasDragging && capitalGarrisonDropZone.worldBound.Contains(evt.position);

        CleanupStableArmyDrag();

        if (wasDragging)
        {
            ResetFighterDoubleClick();

            if (toCommander)
                SetFighterAssignmentFromUnifiedInput(fighterId, true);
            else if (toCapital)
                SetFighterAssignmentFromUnifiedInput(fighterId, false);
        }
        else
        {
            ProcessFighterDoubleClick(fighterId);
        }

        RefreshArmyTransferControls();
        evt.StopImmediatePropagation();
    }

    private void ProcessFighterDoubleClick(string fighterId)
    {
        if (string.IsNullOrEmpty(fighterId) || !CanEditArmyRosterNow())
            return;

        float now = Time.unscaledTime;
        bool doubleClick =
            lastFighterClickId == fighterId &&
            now - lastFighterClickTime <= FighterDoubleClickWindowSeconds;

        if (!doubleClick)
        {
            lastFighterClickId = fighterId;
            lastFighterClickTime = now;
            return;
        }

        ResetFighterDoubleClick();

        bool withCommander =
            gameState.HasActiveExpedition
                ? gameState.ActiveExpedition.FighterIds.Contains(fighterId)
                : selectedFighterIds.Contains(fighterId);

        SetFighterAssignmentFromUnifiedInput(fighterId, !withCommander);
    }

    private void ResetFighterDoubleClick()
    {
        lastFighterClickId = null;
        lastFighterClickTime = -10f;
    }

    private void SetFighterAssignmentFromUnifiedInput(
        string fighterId,
        bool withCommander)
    {
        if (!CanEditArmyRosterNow())
            return;

        int currentCount = gameState.HasActiveExpedition
            ? gameState.ActiveExpedition.FighterIds.Count
            : selectedFighterIds.Count;
        bool alreadyWithCommander = gameState.HasActiveExpedition
            ? gameState.ActiveExpedition.FighterIds.Contains(fighterId)
            : selectedFighterIds.Contains(fighterId);

        if (withCommander &&
            !alreadyWithCommander &&
            currentCount >= GameState.ExpeditionFighterSlots)
        {
            AddReport(
                "В походном отряде ровно четыре места для обычных бойцов. " +
                "Командир входит в состав автоматически и занимает отдельное место.");
            RefreshStableUiAfterStateChange();
            return;
        }

        if (gameState.HasActiveExpedition)
            SetContinuousPreparedFighterAssignment(fighterId, withCommander);
        else
            SetStableFighterAssignment(fighterId, withCommander);
    }

    private void BeginUnifiedArmyDrag(Vector2 pointerPosition)
    {
        FighterData fighter = gameState.FindFighter(stableDraggedFighterId);
        if (fighter == null || stableDraggedCard == null)
            return;

        stableDragStarted = true;
        float width = GetDraggedCardWidth();
        float height = GetDraggedCardHeight();

        stableDragGhost = new VisualElement();
        stableDragGhost.pickingMode = PickingMode.Ignore;
        stableDragGhost.style.position = Position.Absolute;
        stableDragGhost.style.width = width;
        stableDragGhost.style.height = height;
        stableDragGhost.style.paddingLeft = 4f;
        stableDragGhost.style.paddingRight = 4f;
        stableDragGhost.style.paddingTop = 4f;
        stableDragGhost.style.paddingBottom = 4f;
        stableDragGhost.style.backgroundColor =
            (Color)new Color32(54, 48, 38, 245);
        SetUnifiedGhostBorder(stableDragGhost);

        VisualElement image = new VisualElement();
        image.style.flexGrow = 1f;
        image.style.alignItems = Align.Center;
        image.style.justifyContent = Justify.Center;
        image.style.backgroundColor =
            (Color)new Color32(29, 33, 39, 255);

        Label role = new Label(fighter.Role);
        role.style.color = (Color)new Color32(231, 197, 127, 255);
        role.style.fontSize = 10f;
        role.style.unityFontStyleAndWeight = FontStyle.Bold;
        role.style.unityTextAlign = TextAnchor.MiddleCenter;
        role.style.whiteSpace = WhiteSpace.Normal;
        image.Add(role);

        stableDragGhost.Add(image);
        interfaceRoot.Add(stableDragGhost);
        stableDragGhost.BringToFront();
        UpdateUnifiedArmyDragGhost(pointerPosition);
    }

    private void UpdateUnifiedArmyDragGhost(Vector2 pointerPosition)
    {
        if (stableDragGhost == null || stableDraggedCard == null)
            return;

        float width = GetDraggedCardWidth();
        float height = GetDraggedCardHeight();

        stableDragGhost.style.width = width;
        stableDragGhost.style.height = height;
        stableDragGhost.style.left = pointerPosition.x - width * 0.5f;
        stableDragGhost.style.top = pointerPosition.y - height * 0.5f;
    }

    private float GetDraggedCardWidth()
    {
        float width = stableDraggedCard.resolvedStyle.width;
        return float.IsNaN(width) || width < 20f ? 76f : width;
    }

    private float GetDraggedCardHeight()
    {
        float height = stableDraggedCard.resolvedStyle.height;
        return float.IsNaN(height) || height < 20f ? 112f : height;
    }

    private static void SetUnifiedGhostBorder(VisualElement ghost)
    {
        Color border = (Color)new Color32(218, 176, 96, 255);
        ghost.style.borderLeftWidth = 2f;
        ghost.style.borderRightWidth = 2f;
        ghost.style.borderTopWidth = 2f;
        ghost.style.borderBottomWidth = 2f;
        ghost.style.borderLeftColor = border;
        ghost.style.borderRightColor = border;
        ghost.style.borderTopColor = border;
        ghost.style.borderBottomColor = border;
    }

    private void EnsureMoveAllButtons()
    {
        if (capitalMoveAllButton == null)
        {
            capitalMoveAllButton = CreateMoveAllButton(
                true,
                "Заполнить свободные места походного отряда бойцами из поселения.");
            capitalGarrisonDropZone.Add(capitalMoveAllButton);
        }

        if (commanderMoveAllButton == null)
        {
            commanderMoveAllButton = CreateMoveAllButton(
                false,
                "Вернуть всех выбранных бойцов в поселение.");
            commanderGarrisonDropZone.Add(commanderMoveAllButton);
        }
    }

    private Button CreateMoveAllButton(
        bool toCommander,
        string tooltipText)
    {
        Button button = new Button(() => MoveAllFighters(toCommander))
        {
            text = "ПЕРЕМЕСТИТЬ ВСЕХ",
            tooltip = tooltipText
        };

        button.name = toCommander
            ? "capital-move-all-button"
            : "commander-move-all-button";
        button.style.position = Position.Absolute;
        button.style.top = 5f;
        button.style.right = 8f;
        button.style.width = 132f;
        button.style.height = 22f;
        button.style.minHeight = 22f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.fontSize = 8f;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.backgroundColor =
            (Color)new Color32(52, 48, 39, 245);
        button.style.color =
            (Color)new Color32(226, 194, 128, 255);

        Color border = (Color)new Color32(120, 99, 58, 255);
        button.style.borderLeftWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderTopWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftColor = border;
        button.style.borderRightColor = border;
        button.style.borderTopColor = border;
        button.style.borderBottomColor = border;
        return button;
    }

    private void MoveAllFighters(bool toCommander)
    {
        if (!CanEditArmyRosterNow())
            return;

        ResetFighterDoubleClick();
        CleanupStableArmyDrag();

        if (gameState.HasActiveExpedition)
        {
            AddReport(
                "После создания приказа состав меняется по одному бойцу. " +
                "Походный лимит остаётся: командир + четыре бойца.");
            RefreshArmyTransferControls();
            return;
        }

        selectedFighterIds.Clear();
        if (toCommander)
        {
            foreach (FighterData fighter in gameState.Fighters)
            {
                if (selectedFighterIds.Count >= GameState.ExpeditionFighterSlots)
                    break;
                selectedFighterIds.Add(fighter.Id);
            }
        }

        RefreshStableUiAfterStateChange();
        RefreshArmyTransferControls();
    }

    private void RefreshArmyTransferControls()
    {
        if (capitalMoveAllButton == null ||
            commanderMoveAllButton == null ||
            gameState == null)
            return;

        bool canEdit = CanEditArmyRosterNow();
        bool prepared =
            gameState.HasActiveExpedition &&
            CanEditContinuousPreparedRoster();

        int commanderCount = gameState.HasActiveExpedition
            ? gameState.ActiveExpedition.FighterIds.Count
            : selectedFighterIds.Count;
        int capitalCount = Mathf.Max(0, gameState.Fighters.Count - commanderCount);

        capitalMoveAllButton.SetEnabled(
            canEdit &&
            !prepared &&
            capitalCount > 0 &&
            commanderCount < GameState.ExpeditionFighterSlots);
        commanderMoveAllButton.SetEnabled(
            canEdit && commanderCount > 0 && !prepared);

        if (!canEdit)
        {
            capitalMoveAllButton.tooltip = "Состав уже зафиксирован.";
            commanderMoveAllButton.tooltip = "Состав уже зафиксирован.";
            return;
        }

        capitalMoveAllButton.tooltip = prepared
            ? "Подготовленный приказ меняйте по одному бойцу или отмените его."
            : commanderCount >= GameState.ExpeditionFighterSlots
                ? "Все четыре места бойцов уже заполнены."
                : capitalCount > 0
                    ? "Заполнить свободные места отряда бойцами из поселения."
                    : "В поселении нет свободных бойцов.";

        commanderMoveAllButton.tooltip = prepared
            ? "Подготовленный приказ меняйте по одному бойцу или отмените его."
            : commanderCount > 0
                ? "Вернуть всех выбранных бойцов в поселение."
                : "У командира нет выбранных бойцов.";
    }
}
