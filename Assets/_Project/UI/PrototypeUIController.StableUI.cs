using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool stableUiInitialized;
    private bool stableUiInitializing;
    private int renderedReportHash = int.MinValue;
    private IVisualElementScheduledItem supplyHoldSchedule;
    private int supplyHoldDelta;
    private bool supplyHoldRepeated;

    private VisualElement persistentCommanderPanel;
    private VisualElement persistentCommanderGarrisonHost;
    private Button persistentCommanderArmyButton;
    private Button persistentCommanderExpeditionButton;
    private Label persistentCommanderStateLabel;
    private Label persistentCommanderTargetLabel;

    private string stableDraggedFighterId;
    private int stableDraggedPointerId = -1;
    private Vector2 stableDragStartPosition;
    private bool stableDragStarted;
    private VisualElement stableDraggedCard;
    private VisualElement stableDragGhost;

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
        persistentCommanderGarrisonHost =
            interfaceRoot.Q<VisualElement>("persistent-commander-garrison-host");
        persistentCommanderArmyButton =
            interfaceRoot.Q<Button>("persistent-commander-army-button");
        persistentCommanderExpeditionButton =
            interfaceRoot.Q<Button>("persistent-commander-expedition-button");
        persistentCommanderStateLabel =
            interfaceRoot.Q<Label>("persistent-commander-state");
        persistentCommanderTargetLabel =
            interfaceRoot.Q<Label>("persistent-commander-target");

        if (persistentCommanderPanel == null ||
            persistentCommanderGarrisonHost == null ||
            persistentCommanderArmyButton == null ||
            persistentCommanderExpeditionButton == null ||
            persistentCommanderStateLabel == null ||
            persistentCommanderTargetLabel == null)
        {
            Debug.LogError("Stable UI: не найдена статическая плашка командира в Prototype_Main.uxml.");
            stableUiInitializing = false;
            return;
        }

        RebindStableUiCallbacks();
        RegisterStableArmyDragCallbacks();
        RegisterRoyalReportRefresh();
        InitializeJourneySummaryUi();
        InitializeExpeditionViewsUi();

        stableUiInitialized = true;
        stableUiInitializing = false;
        RefreshStableUiAfterStateChange();
    }

    private void RebindStableUiCallbacks()
    {
        armyGoldMinusButton.clicked -= OnArmyGoldMinusClicked;
        armyGoldPlusButton.clicked -= OnArmyGoldPlusClicked;
        supplyMinusButton.clicked -= OnSupplyMinusClicked;
        supplyPlusButton.clicked -= OnSupplyPlusClicked;
        armyGoldMinusButton.clicked += OnStableArmyGoldMinusClicked;
        armyGoldPlusButton.clicked += OnStableArmyGoldPlusClicked;
        supplyMinusButton.clicked += OnStableSupplyMinusClicked;
        supplyPlusButton.clicked += OnStableSupplyPlusClicked;
        RegisterSupplyHoldCallbacks(supplyMinusButton, -1);
        RegisterSupplyHoldCallbacks(supplyPlusButton, 1);

        returnExpeditionButton.clicked -= OnExpeditionActionClicked;
        returnExpeditionButton.clicked += OnStableExpeditionActionClicked;
        researchExpeditionButton.clicked -= OnResearchExpeditionClicked;
        researchExpeditionButton.clicked += OnStableResearchExpeditionClicked;

        persistentCommanderArmyButton.clicked += OnPersistentCommanderArmyClicked;
        persistentCommanderExpeditionButton.clicked += ToggleQuickExpeditionPopup;

        navCapitalButton.clicked += OnStableNavigationChanged;
        navArmyButton.clicked += OnStableNavigationChanged;
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

        commanderDropdown.RegisterValueChangedCallback(
            _ => RefreshStableUiAfterStateChange());
    }

    private void OnPersistentCommanderArmyClicked()
    {
        if (!CanProcessNavigationClick())
            return;

        ToggleScreen(MainScreen.Army);
        HideQuickExpeditionPopup();
        RefreshPersistentCommanderNavigationState();
    }

    private void OnStableNavigationChanged()
    {
        HideQuickExpeditionPopup();
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

    private void TrySendExpeditionFromStableUi(string locationId)
    {
        if (isGameOver)
            return;

        string resultMessage;
        List<string> selectedIds = GetSelectedFighterIdsInArmyOrder();
        bool started =
            gameState.TryStartExpedition(locationId, selectedIds, out resultMessage);

        if (started)
        {
            CommanderData commander =
                gameState.FindCommander(gameState.ActiveExpedition.CommanderId);

            // Приказ уже существует, но мир ещё не продвинулся.
            if (commander != null)
                commander.State = CommanderState.InCastle;
        }

        AddReport(resultMessage);
        RefreshStableUiAfterStateChange();
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
        if (ConsumeRepeatedSupplyClick())
            return;

        if (!isGameOver)
            gameState.TryAddArmySupply();
        RefreshStableResourceUi();
    }

    private void OnStableSupplyMinusClicked()
    {
        if (ConsumeRepeatedSupplyClick())
            return;

        if (!isGameOver)
            gameState.TryRemoveArmySupply();
        RefreshStableResourceUi();
    }

    private void RefreshStableResourceUi()
    {
        goldLabel.text = "Золото: " + gameState.Gold;
        foodLabel.text = "Пища: " + gameState.Food;
        RefreshSupplyBlock();
        ApplyCompactSupplyText();
    }

    private void RefreshStableUiAfterStateChange()
    {
        if (!stableUiInitialized || gameState == null)
            return;

        RefreshFighterContainersWithoutRebuild();
        RefreshSupplyBlock();
        ApplyCompactSupplyText();
        RefreshExpeditionPanel();
        RefreshExpeditionViewState();
        RefreshJourneySummaryFromState();
        RefreshIncidentNotifications();
        RefreshPersistentCommanderNavigationState();
        RefreshTimeControlAvailability();
        ScheduleRoyalReportsRefresh();
    }

    private void ApplyCompactSupplyText()
    {
        int dailyConsumption = gameState.HasActiveExpedition
            ? gameState.ExpeditionSupplyConsumption
            : selectedFighterIds.Count > 0
                ? selectedFighterIds.Count + 1
                : 0;
        int fullDays = dailyConsumption > 0
            ? gameState.ArmySupply / dailyConsumption
            : 0;

        supplyConsumptionLabel.text = dailyConsumption > 0
            ? "Расход: " + dailyConsumption + " / день"
            : "Расход: —";
        supplyDaysLabel.text = dailyConsumption > 0
            ? "Хватит на: " + fullDays + " дн."
            : "Хватит на: —";
    }

    private void RefreshFighterContainersWithoutRebuild()
    {
        if (commanderGarrisonList == null ||
            capitalGarrisonList == null ||
            gameState == null)
            return;

        Dictionary<string, VisualElement> cards =
            new Dictionary<string, VisualElement>();
        CollectExistingFighterCards(commanderGarrisonList, cards);
        CollectExistingFighterCards(capitalGarrisonList, cards);

        List<string> commanderIds = gameState.HasActiveExpedition
            ? new List<string>(gameState.ActiveExpedition.FighterIds)
            : GetSelectedFighterIdsInArmyOrder();

        foreach (FighterData fighter in gameState.Fighters)
        {
            VisualElement card;

            if (!cards.TryGetValue(fighter.Id, out card))
            {
                card = FindCardByLegacyName(fighter.Name);
                if (card != null)
                    cards[fighter.Id] = card;
            }

            if (card == null)
                continue;

            bool withCommander = commanderIds.Contains(fighter.Id);
            VisualElement desiredParent =
                withCommander ? commanderGarrisonList : capitalGarrisonList;

            if (card.parent != desiredParent)
            {
                card.RemoveFromHierarchy();
                desiredParent.Add(card);
            }

            ConfigureCompactFighterCard(
                card,
                fighter,
                withCommander,
                gameState.HasActiveExpedition);
        }

        if (capitalGarrisonList.childCount == 0)
            capitalGarrisonDropZone.AddToClassList("army-roster-empty-danger");
        else
            capitalGarrisonDropZone.RemoveFromClassList("army-roster-empty-danger");
    }

    private void CollectExistingFighterCards(
        VisualElement list,
        Dictionary<string, VisualElement> cards)
    {
        if (list == null)
            return;

        list.Query<VisualElement>(className: "fighter-card")
            .ForEach(card =>
            {
                string id = card.userData as string;
                if (!string.IsNullOrEmpty(id))
                {
                    cards[id] = card;
                    return;
                }

                Label nameLabel = card.Q<Label>(className: "fighter-name");
                if (nameLabel == null)
                    return;

                foreach (FighterData fighter in gameState.Fighters)
                {
                    if (fighter.Name == nameLabel.text)
                    {
                        card.userData = fighter.Id;
                        cards[fighter.Id] = card;
                        break;
                    }
                }
            });
    }

    private VisualElement FindCardByLegacyName(string fighterName)
    {
        VisualElement found = null;

        interfaceRoot.Query<VisualElement>(className: "fighter-card")
            .ForEach(card =>
            {
                if (found != null)
                    return;

                Label name = card.Q<Label>(className: "fighter-name");
                if (name != null && name.text == fighterName)
                    found = card;
            });

        return found;
    }

    private void ConfigureCompactFighterCard(
        VisualElement card,
        FighterData fighter,
        bool withCommander,
        bool expeditionActive)
    {
        card.userData = fighter.Id;
        card.SetEnabled(!expeditionActive && !isGameOver);
        card.tooltip = expeditionActive
            ? "Состав зафиксирован до возвращения экспедиции"
            : "Перетащите бойца в другой гарнизон";

        card.RemoveFromClassList("fighter-card-selected");
        card.RemoveFromClassList("fighter-card-garrison");
        card.AddToClassList(
            withCommander ? "fighter-card-selected" : "fighter-card-garrison");

        VisualElement image =
            card.Q<VisualElement>(className: "fighter-image-placeholder");
        if (image == null)
        {
            image = new VisualElement();
            image.AddToClassList("fighter-image-placeholder");
            card.Add(image);
        }

        Label role =
            image.Q<Label>(className: "fighter-image-placeholder-text");
        if (role == null)
        {
            role = new Label();
            role.AddToClassList("fighter-image-placeholder-text");
            image.Add(role);
        }
        role.text = fighter.Role;
    }

    private void RegisterStableArmyDragCallbacks()
    {
        interfaceRoot.RegisterCallback<PointerDownEvent>(
            OnStableArmyPointerDown,
            TrickleDown.TrickleDown);
        interfaceRoot.RegisterCallback<PointerMoveEvent>(OnStableArmyPointerMove);
        interfaceRoot.RegisterCallback<PointerUpEvent>(OnStableArmyPointerUp);
    }

    private void OnStableArmyPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || isGameOver || gameState == null || gameState.HasActiveExpedition)
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

    private void OnStableArmyPointerMove(PointerMoveEvent evt)
    {
        if (stableDraggedCard == null ||
            stableDraggedPointerId != evt.pointerId ||
            !interfaceRoot.HasPointerCapture(evt.pointerId))
            return;

        if (!stableDragStarted &&
            Vector2.Distance(stableDragStartPosition, evt.position) >= FighterDragThreshold)
            BeginStableArmyDrag(evt.position);

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

    private void OnStableArmyPointerUp(PointerUpEvent evt)
    {
        if (stableDraggedCard == null || stableDraggedPointerId != evt.pointerId)
            return;

        string fighterId = stableDraggedFighterId;
        bool wasDragging = stableDragStarted;
        bool toCommander =
            wasDragging && commanderGarrisonDropZone.worldBound.Contains(evt.position);
        bool toCapital =
            wasDragging && capitalGarrisonDropZone.worldBound.Contains(evt.position);

        CleanupStableArmyDrag();

        if (wasDragging && toCommander)
            SetStableFighterAssignment(fighterId, true);
        else if (wasDragging && toCapital)
            SetStableFighterAssignment(fighterId, false);

        evt.StopImmediatePropagation();
    }

    private void SetStableFighterAssignment(string fighterId, bool withCommander)
    {
        if (gameState.HasActiveExpedition || isGameOver)
            return;

        if (withCommander)
            selectedFighterIds.Add(fighterId);
        else
            selectedFighterIds.Remove(fighterId);

        RefreshFighterContainersWithoutRebuild();
        RefreshSupplyBlock();
        ApplyCompactSupplyText();
        RefreshExpeditionPanel();
        RefreshExpeditionViewState();
    }

    private VisualElement FindStableFighterCard(VisualElement element)
    {
        VisualElement current = element;
        while (current != null && current != interfaceRoot)
        {
            if (current.ClassListContains("fighter-card"))
                return current;
            current = current.parent;
        }
        return null;
    }

    private void BeginStableArmyDrag(Vector2 pointerPosition)
    {
        FighterData fighter = gameState.FindFighter(stableDraggedFighterId);
        if (fighter == null)
            return;

        stableDragStarted = true;
        stableDraggedCard.style.opacity = 0.32f;
        stableDragGhost = new VisualElement();
        stableDragGhost.pickingMode = PickingMode.Ignore;
        stableDragGhost.style.position = Position.Absolute;
        stableDragGhost.style.width = 104;
        stableDragGhost.style.height = 78;
        stableDragGhost.style.paddingLeft = 5;
        stableDragGhost.style.paddingRight = 5;
        stableDragGhost.style.paddingTop = 5;
        stableDragGhost.style.paddingBottom = 5;
        stableDragGhost.style.backgroundColor = (Color)new Color32(54, 48, 38, 255);
        stableDragGhost.style.borderLeftWidth = 2;
        stableDragGhost.style.borderRightWidth = 2;
        stableDragGhost.style.borderTopWidth = 2;
        stableDragGhost.style.borderBottomWidth = 2;
        stableDragGhost.style.borderLeftColor = (Color)new Color32(218, 176, 96, 255);
        stableDragGhost.style.borderRightColor = (Color)new Color32(218, 176, 96, 255);
        stableDragGhost.style.borderTopColor = (Color)new Color32(218, 176, 96, 255);
        stableDragGhost.style.borderBottomColor = (Color)new Color32(218, 176, 96, 255);

        VisualElement image = new VisualElement();
        image.style.width = Length.Percent(100);
        image.style.height = Length.Percent(100);
        image.style.alignItems = Align.Center;
        image.style.justifyContent = Justify.Center;
        image.style.backgroundColor = (Color)new Color32(29, 33, 39, 255);

        Label role = new Label(fighter.Role);
        role.style.color = (Color)new Color32(231, 197, 127, 255);
        role.style.fontSize = 10;
        role.style.unityFontStyleAndWeight = FontStyle.Bold;
        role.style.unityTextAlign = TextAnchor.MiddleCenter;
        role.style.whiteSpace = WhiteSpace.Normal;
        image.Add(role);
        stableDragGhost.Add(image);
        interfaceRoot.Add(stableDragGhost);
        stableDragGhost.BringToFront();
        UpdateStableArmyDragGhost(pointerPosition);
    }

    private void UpdateStableArmyDragGhost(Vector2 pointerPosition)
    {
        if (stableDragGhost == null)
            return;
        stableDragGhost.style.left = pointerPosition.x - 52f;
        stableDragGhost.style.top = pointerPosition.y - 39f;
    }

    private void CleanupStableArmyDrag()
    {
        if (interfaceRoot != null &&
            stableDraggedPointerId >= 0 &&
            interfaceRoot.HasPointerCapture(stableDraggedPointerId))
            interfaceRoot.ReleasePointer(stableDraggedPointerId);

        if (stableDraggedCard != null)
            stableDraggedCard.style.opacity = 1f;
        if (stableDragGhost != null)
            stableDragGhost.RemoveFromHierarchy();

        SetStableDropHighlight(commanderGarrisonDropZone, false);
        SetStableDropHighlight(capitalGarrisonDropZone, false);
        stableDraggedFighterId = null;
        stableDraggedPointerId = -1;
        stableDraggedCard = null;
        stableDragGhost = null;
        stableDragStarted = false;
    }

    private static void SetStableDropHighlight(VisualElement zone, bool highlighted)
    {
        if (zone == null)
            return;
        if (highlighted)
            zone.AddToClassList("army-roster-drop-hover");
        else
            zone.RemoveFromClassList("army-roster-drop-hover");
    }

    private void RefreshPersistentCommanderNavigationState()
    {
        if (persistentCommanderArmyButton == null || persistentCommanderExpeditionButton == null)
            return;

        bool armyActive =
            openedScreen.HasValue && openedScreen.Value == MainScreen.Army;
        bool expeditionActive =
            openedScreen.HasValue && openedScreen.Value == MainScreen.Expeditions;
        SetPersistentNavActive(persistentCommanderArmyButton, armyActive);
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
