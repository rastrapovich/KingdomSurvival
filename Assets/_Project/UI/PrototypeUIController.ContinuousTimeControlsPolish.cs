using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool continuousControlsPolishInitialized;
    private Button continuousSpeed1Button;
    private Button continuousSpeed3Button;
    private Button continuousSpeed5Button;
    private Button continuousSpeed10Button;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeContinuousControlsPolishRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeContinuousControlsPolish)
            .ExecuteLater(130);
    }

    private void TryInitializeContinuousControlsPolish()
    {
        if (continuousControlsPolishInitialized)
            return;

        if (!continuousTimeInitialized ||
            interfaceRoot == null ||
            gameState == null ||
            timeToggleButton == null ||
            dayLabel == null ||
            continuousSpeedButton == null)
        {
            ScheduleContinuousControlsPolishRetry();
            return;
        }

        RebindContinuousTimeButtons();
        EnsureExtendedSpeedButtons();
        RepositionContinuousDayBox();

        interfaceRoot.focusable = true;
        interfaceRoot.RegisterCallback<KeyDownEvent>(
            OnContinuousGlobalKeyDown,
            TrickleDown.TrickleDown);
        interfaceRoot.Focus();

        RegisterContinuousPreparedRosterCallbacks();

        interfaceRoot.schedule
            .Execute(RefreshContinuousControlsPolish)
            .Every(100);

        continuousControlsPolishInitialized = true;
        RefreshContinuousControlsPolish();
    }

    private void ScheduleContinuousControlsPolishRetry()
    {
        UIDocument document = GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(TryInitializeContinuousControlsPolish)
            .ExecuteLater(60);
    }

    private void RefreshContinuousControlsPolish()
    {
        if (gameState == null || isGameOver)
        {
            RefreshExtendedSpeedButtons();
            return;
        }

        RefreshExtendedSpeedButtons();
        RefreshPreparedRosterInteractivity();
        RepositionContinuousDayBox();
    }

    private void OnContinuousGlobalKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Space)
            return;

        evt.StopImmediatePropagation();

        if (isGameOver || HasBlockingModalWork())
            return;

        OnContinuousPauseClicked();
    }

    private void EnsureExtendedSpeedButtons()
    {
        if (continuousSpeed1Button != null ||
            continuousSpeedButton == null ||
            continuousSpeedButton.parent == null)
        {
            return;
        }

        VisualElement host = continuousSpeedButton.parent;

        continuousSpeed1Button = CreateExtendedSpeedButton(
            ContinuousSimulationSystem.NormalSpeedMultiplier);
        continuousSpeed3Button = CreateExtendedSpeedButton(
            ContinuousSimulationSystem.FastSpeedMultiplier);
        continuousSpeed5Button = CreateExtendedSpeedButton(
            ContinuousSimulationSystem.VeryFastSpeedMultiplier);
        continuousSpeed10Button = CreateExtendedSpeedButton(
            ContinuousSimulationSystem.MaximumSpeedMultiplier);

        // Старую toggle-кнопку ×3 скрываем: у неё логика «×1 ↔ ×3», а новый
        // ряд скоростей должен состоять из четырёх явных переключателей.
        continuousSpeedButton.style.display = DisplayStyle.None;

        host.Add(continuousSpeed1Button);
        host.Add(continuousSpeed3Button);
        host.Add(continuousSpeed5Button);
        host.Add(continuousSpeed10Button);
    }

    private Button CreateExtendedSpeedButton(int multiplier)
    {
        Button button = new Button(
            () => OnContinuousExplicitSpeedClicked(multiplier))
        {
            text = "×" + multiplier,
            tooltip = "Скорость стратегического времени ×" + multiplier
        };

        button.style.width = 52f;
        button.style.height = 34f;
        button.style.marginRight = 4f;
        button.style.color = (Color)new Color32(231, 192, 101, 255);
        button.style.borderLeftWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderTopWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftColor = (Color)new Color32(132, 102, 48, 255);
        button.style.borderRightColor = (Color)new Color32(132, 102, 48, 255);
        button.style.borderTopColor = (Color)new Color32(132, 102, 48, 255);
        button.style.borderBottomColor = (Color)new Color32(132, 102, 48, 255);
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        return button;
    }

    private void OnContinuousExplicitSpeedClicked(int multiplier)
    {
        if (isGameOver)
            return;

        ContinuousSimulationSystem.SetSpeedMultiplier(gameState, multiplier);
        RefreshContinuousClockOnly();
        RefreshExtendedSpeedButtons();
    }

    private void RefreshExtendedSpeedButtons()
    {
        if (gameState == null)
            return;

        int current =
            ContinuousSimulationSystem.GetSpeedMultiplier(gameState);

        ApplyExtendedSpeedButtonState(
            continuousSpeed1Button,
            ContinuousSimulationSystem.NormalSpeedMultiplier,
            current);
        ApplyExtendedSpeedButtonState(
            continuousSpeed3Button,
            ContinuousSimulationSystem.FastSpeedMultiplier,
            current);
        ApplyExtendedSpeedButtonState(
            continuousSpeed5Button,
            ContinuousSimulationSystem.VeryFastSpeedMultiplier,
            current);
        ApplyExtendedSpeedButtonState(
            continuousSpeed10Button,
            ContinuousSimulationSystem.MaximumSpeedMultiplier,
            current);
    }

    private void ApplyExtendedSpeedButtonState(
        Button button,
        int multiplier,
        int current)
    {
        if (button == null)
            return;

        bool active = current == multiplier;
        button.text = "×" + multiplier + (active ? " ✓" : string.Empty);
        button.tooltip = active
            ? "Выбрана скорость ×" + multiplier + "."
            : "Переключить стратегическое время на ×" + multiplier + ".";
        button.style.backgroundColor = active
            ? (Color)new Color32(101, 77, 35, 255)
            : (Color)new Color32(61, 55, 40, 255);
        button.SetEnabled(!isGameOver);
    }

    private void RepositionContinuousDayBox()
    {
        if (dayLabel == null ||
            timeToggleButton == null ||
            dayLabel.parent == null ||
            timeToggleButton.parent == null)
        {
            return;
        }

        VisualElement dayBox = dayLabel.parent;
        VisualElement timeControlHost = timeToggleButton.parent;

        if (dayBox.parent != timeControlHost)
        {
            dayBox.RemoveFromHierarchy();
            timeControlHost.Add(dayBox);
            timeToggleButton.BringToFront();
        }

        dayBox.style.flexGrow = 0f;
        dayBox.style.flexShrink = 0f;
        dayBox.style.width = 158f;
        dayBox.style.minWidth = 158f;
        dayBox.style.marginLeft = 6f;
        dayBox.style.marginRight = 6f;
    }

    private void RegisterContinuousPreparedRosterCallbacks()
    {
        interfaceRoot.RegisterCallback<PointerDownEvent>(
            OnContinuousPreparedRosterPointerDown,
            TrickleDown.TrickleDown);
        interfaceRoot.RegisterCallback<PointerMoveEvent>(
            OnContinuousPreparedRosterPointerMove,
            TrickleDown.TrickleDown);
        interfaceRoot.RegisterCallback<PointerUpEvent>(
            OnContinuousPreparedRosterPointerUp,
            TrickleDown.TrickleDown);
    }

    private bool CanEditContinuousPreparedRoster()
    {
        return !isGameOver &&
               gameState != null &&
               gameState.HasActiveExpedition &&
               ContinuousPreparationCommands.CanEditPreparedRoster(gameState);
    }

    private void RefreshPreparedRosterInteractivity()
    {
        if (interfaceRoot == null ||
            gameState == null ||
            !gameState.HasActiveExpedition)
        {
            return;
        }

        bool editable = CanEditContinuousPreparedRoster();

        interfaceRoot.Query<VisualElement>(className: "fighter-card")
            .ForEach(card =>
            {
                card.SetEnabled(editable);
                card.tooltip = editable
                    ? "Пока отряд не начал движение, состав можно изменить."
                    : "Состав зафиксирован после начала движения.";
            });

        if (fighterSelectionHintLabel != null)
        {
            fighterSelectionHintLabel.text = editable
                ? "Приказ подготовлен. Пока армия не начала движение, бойцов можно переставлять между гарнизонами."
                : "Состав обоих гарнизонов зафиксирован до возвращения экспедиции.";
        }
    }

    private void OnContinuousPreparedRosterPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || !CanEditContinuousPreparedRoster())
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
        interfaceRoot.CapturePointer(evt.pointerId);
        evt.StopImmediatePropagation();
    }

    private void OnContinuousPreparedRosterPointerMove(PointerMoveEvent evt)
    {
        if (!CanEditContinuousPreparedRoster() ||
            stableDraggedCard == null ||
            stableDraggedPointerId != evt.pointerId ||
            !interfaceRoot.HasPointerCapture(evt.pointerId))
        {
            return;
        }

        if (!stableDragStarted &&
            Vector2.Distance(stableDragStartPosition, evt.position) >=
            FighterDragThreshold)
        {
            BeginStableArmyDrag(evt.position);
        }

        if (stableDragStarted)
        {
            UpdateStableArmyDragGhost(evt.position);
            SetStableDropHighlight(
                commanderGarrisonDropZone,
                commanderGarrisonDropZone.worldBound.Contains(evt.position));
            SetStableDropHighlight(
                capitalGarrisonDropZone,
                capitalGarrisonDropZone.worldBound.Contains(evt.position));
        }

        evt.StopImmediatePropagation();
    }

    private void OnContinuousPreparedRosterPointerUp(PointerUpEvent evt)
    {
        if (!CanEditContinuousPreparedRoster() ||
            stableDraggedCard == null ||
            stableDraggedPointerId != evt.pointerId)
        {
            return;
        }

        string fighterId = stableDraggedFighterId;
        bool wasDragging = stableDragStarted;
        bool toCommander =
            wasDragging &&
            commanderGarrisonDropZone.worldBound.Contains(evt.position);
        bool toCapital =
            wasDragging &&
            capitalGarrisonDropZone.worldBound.Contains(evt.position);

        CleanupStableArmyDrag();

        if (wasDragging && toCommander)
        {
            SetContinuousPreparedFighterAssignment(fighterId, true);
        }
        else if (wasDragging && toCapital)
        {
            SetContinuousPreparedFighterAssignment(fighterId, false);
        }
        else if (!wasDragging &&
                 gameState.ActiveExpedition != null)
        {
            bool currentlyWithCommander =
                gameState.ActiveExpedition.FighterIds.Contains(fighterId);
            SetContinuousPreparedFighterAssignment(
                fighterId,
                !currentlyWithCommander);
        }

        evt.StopImmediatePropagation();
    }

    private void SetContinuousPreparedFighterAssignment(
        string fighterId,
        bool withCommander)
    {
        if (!CanEditContinuousPreparedRoster())
            return;

        List<string> requested =
            new List<string>(gameState.ActiveExpedition.FighterIds);

        if (withCommander)
        {
            if (!requested.Contains(fighterId))
                requested.Add(fighterId);
        }
        else
        {
            requested.Remove(fighterId);
        }

        string resultMessage;
        if (!ContinuousPreparationCommands.TrySetPreparedRoster(
                gameState,
                requested,
                out resultMessage))
        {
            if (!string.IsNullOrWhiteSpace(resultMessage))
                AddReport(resultMessage);
            return;
        }

        selectedFighterIds.Clear();
        foreach (string id in gameState.ActiveExpedition.FighterIds)
            selectedFighterIds.Add(id);

        if (stableUiInitialized)
            RefreshStableUiAfterStateChange();

        RefreshContinuousTimeUi(true);
        RefreshPreparedRosterInteractivity();
    }
}
