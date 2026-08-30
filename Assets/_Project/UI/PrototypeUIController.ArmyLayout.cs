using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool stableArmyLayoutInitialized;
    private bool stableArmyPointerCallbacksRegistered;
    private bool stableArmyResourceCallbacksBound;

    private IVisualElementScheduledItem stableArmyInitItem;
    private IVisualElementScheduledItem stableArmyMaintenanceItem;

    private string stableDraggedFighterId;
    private int stableDraggedPointerId = -1;
    private Vector2 stableDragStartPosition;
    private bool stableDragStarted;
    private VisualElement stableDraggedCard;
    private VisualElement stableDragGhost;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeStableArmyLayoutRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();

        if (document == null)
            return;

        controller.stableArmyInitItem = document.rootVisualElement.schedule
            .Execute(controller.TryInitializeStableArmyLayout)
            .Every(100);
    }

    private void TryInitializeStableArmyLayout()
    {
        if (stableArmyLayoutInitialized)
            return;

        if (interfaceRoot == null ||
            gameState == null ||
            armyScreen == null ||
            commanderGarrisonDropZone == null ||
            commanderGarrisonList == null ||
            capitalGarrisonDropZone == null ||
            capitalGarrisonList == null ||
            !journeySummaryInitialized ||
            journeySummaryBlock == null)
        {
            return;
        }

        BindStableArmyResourceButtons();
        RegisterStableArmyPointerCallbacks();
        BuildStableArmyLayout();
        RefreshStableArmyCards();

        stableArmyMaintenanceItem = interfaceRoot.schedule
            .Execute(MaintainStableArmyCards)
            .Every(100);

        stableArmyLayoutInitialized = true;
        stableArmyInitItem?.Pause();
    }

    private void BindStableArmyResourceButtons()
    {
        if (stableArmyResourceCallbacksBound)
            return;

        armyGoldMinusButton.clicked -= OnArmyGoldMinusClicked;
        armyGoldPlusButton.clicked -= OnArmyGoldPlusClicked;
        supplyMinusButton.clicked -= OnSupplyMinusClicked;
        supplyPlusButton.clicked -= OnSupplyPlusClicked;

        armyGoldMinusButton.clicked += OnStableArmyGoldMinusClicked;
        armyGoldPlusButton.clicked += OnStableArmyGoldPlusClicked;
        supplyMinusButton.clicked += OnStableSupplyMinusClicked;
        supplyPlusButton.clicked += OnStableSupplyPlusClicked;

        stableArmyResourceCallbacksBound = true;
    }

    private void OnStableArmyGoldPlusClicked()
    {
        if (!isGameOver && gameState.CanAdjustArmySupply && gameState.Gold > 0)
        {
            gameState.Gold--;
            gameState.ArmyGold++;
        }

        RefreshStableArmyResourceValues();
    }

    private void OnStableArmyGoldMinusClicked()
    {
        if (!isGameOver &&
            gameState.CanAdjustArmySupply &&
            gameState.ArmyGold > 0)
        {
            gameState.ArmyGold--;
            gameState.Gold++;
        }

        RefreshStableArmyResourceValues();
    }

    private void OnStableSupplyPlusClicked()
    {
        if (!isGameOver)
            gameState.TryAddArmySupply();

        RefreshStableArmyResourceValues();
    }

    private void OnStableSupplyMinusClicked()
    {
        if (!isGameOver)
            gameState.TryRemoveArmySupply();

        RefreshStableArmyResourceValues();
    }

    private void RefreshStableArmyResourceValues()
    {
        goldLabel.text = "Золото: " + gameState.Gold;
        foodLabel.text = "Пища: " + gameState.Food;

        RefreshSupplyBlock();

        VisualElement supplyBlock =
            armyScreen.Q<VisualElement>(className: "military-supply-block");

        if (supplyBlock != null)
            ConfigureStableSupplyBlock(supplyBlock);
    }

    private void RegisterStableArmyPointerCallbacks()
    {
        if (stableArmyPointerCallbacksRegistered || interfaceRoot == null)
            return;

        interfaceRoot.RegisterCallback<PointerDownEvent>(
            OnStableArmyPointerDown,
            TrickleDown.TrickleDown);
        interfaceRoot.RegisterCallback<PointerMoveEvent>(
            OnStableArmyPointerMove);
        interfaceRoot.RegisterCallback<PointerUpEvent>(
            OnStableArmyPointerUp);

        stableArmyPointerCallbacksRegistered = true;
    }

    private void BuildStableArmyLayout()
    {
        VisualElement armyPanel =
            armyScreen.Q<VisualElement>(className: "army-panel");
        VisualElement topRow =
            armyScreen.Q<VisualElement>(className: "commander-supply-row");
        VisualElement commanderProfile =
            armyScreen.Q<VisualElement>(className: "commander-profile-column");
        VisualElement supplyBlock =
            armyScreen.Q<VisualElement>(className: "military-supply-block");
        VisualElement transferBoard =
            armyScreen.Q<VisualElement>(className: "army-transfer-board");
        VisualElement transferArrows =
            armyScreen.Q<VisualElement>(className: "army-transfer-arrows");

        if (armyPanel == null ||
            topRow == null ||
            commanderProfile == null ||
            supplyBlock == null ||
            transferBoard == null)
        {
            return;
        }

        RemoveOuterArmyScroll(armyPanel);

        armyScreen.style.width = Length.Percent(100);
        armyScreen.style.height = Length.Percent(100);
        armyScreen.style.minHeight = 0;

        armyPanel.style.width = Length.Percent(100);
        armyPanel.style.height = Length.Percent(100);
        armyPanel.style.minHeight = 0;
        armyPanel.style.marginBottom = 0;
        armyPanel.style.paddingLeft = 0;
        armyPanel.style.paddingRight = 0;
        armyPanel.style.paddingTop = 0;
        armyPanel.style.paddingBottom = 0;
        armyPanel.style.backgroundColor = ArmyStableRgb(18, 21, 26);
        SetStableArmyBorder(armyPanel, 0, Color.clear);

        HideLegacyArmyLabels(armyPanel);

        topRow.style.width = Length.Percent(82);
        topRow.style.height = Length.Percent(56);
        topRow.style.minHeight = 300;
        topRow.style.maxHeight = 390;
        topRow.style.flexGrow = 0;
        topRow.style.flexShrink = 1;
        topRow.style.flexDirection = FlexDirection.Row;
        topRow.style.alignItems = Align.Stretch;
        topRow.style.justifyContent = Justify.FlexStart;
        topRow.style.alignSelf = Align.FlexStart;
        topRow.style.marginBottom = 5;

        ConfigureStableCommanderBlock(commanderProfile);
        ConfigureStableJourneyBlock();
        ConfigureStableSupplyBlock(supplyBlock);

        if (transferArrows != null)
            transferArrows.style.display = DisplayStyle.None;

        PutCapitalGarrisonFirst(transferBoard);
        RemoveRosterScroll(capitalGarrisonDropZone, capitalGarrisonList);
        RemoveRosterScroll(commanderGarrisonDropZone, commanderGarrisonList);

        transferBoard.style.width = Length.Percent(82);
        transferBoard.style.height = Length.Percent(42);
        transferBoard.style.minHeight = 220;
        transferBoard.style.maxHeight = 280;
        transferBoard.style.flexGrow = 0;
        transferBoard.style.flexShrink = 1;
        transferBoard.style.flexDirection = FlexDirection.Column;
        transferBoard.style.alignItems = Align.Stretch;
        transferBoard.style.alignSelf = Align.FlexStart;
        transferBoard.style.marginTop = 0;
        transferBoard.style.marginBottom = 0;

        ConfigureStableGarrisonZone(capitalGarrisonDropZone, capitalGarrisonList, true);
        ConfigureStableGarrisonZone(
            commanderGarrisonDropZone,
            commanderGarrisonList,
            false);

        RefreshStableArmyDropZoneColors(false, false);
    }

    private void RemoveOuterArmyScroll(VisualElement armyPanel)
    {
        ScrollView armyScroll =
            armyScreen.Q<ScrollView>(className: "military-army-column");

        if (armyScroll == null || armyPanel.parent == armyScreen)
            return;

        armyPanel.RemoveFromHierarchy();
        armyScroll.RemoveFromHierarchy();
        armyScreen.Add(armyPanel);
    }

    private void HideLegacyArmyLabels(VisualElement armyPanel)
    {
        Label title = armyPanel.Q<Label>(className: "panel-title");
        Label description = armyPanel.Q<Label>(className: "panel-description");

        if (title != null)
            title.style.display = DisplayStyle.None;

        if (description != null)
            description.style.display = DisplayStyle.None;

        if (armyStatusLabel != null)
            armyStatusLabel.style.display = DisplayStyle.None;

        if (fighterSelectionHintLabel != null)
            fighterSelectionHintLabel.style.display = DisplayStyle.None;
    }

    private void ConfigureStableCommanderBlock(VisualElement commanderProfile)
    {
        commanderProfile.style.width = Length.Percent(33);
        commanderProfile.style.minWidth = 210;
        commanderProfile.style.height = Length.Percent(100);
        commanderProfile.style.marginRight = 5;
        commanderProfile.style.paddingLeft = 10;
        commanderProfile.style.paddingRight = 10;
        commanderProfile.style.paddingTop = 10;
        commanderProfile.style.paddingBottom = 9;
        commanderProfile.style.backgroundColor = ArmyStableRgb(42, 46, 54);
        SetStableArmyBorder(
            commanderProfile,
            1,
            ArmyStableRgb(91, 80, 62));
        SetStableArmyRadius(commanderProfile, 5);

        Label sectionTitle =
            commanderProfile.Q<Label>(className: "commander-section-title");

        if (sectionTitle != null)
        {
            sectionTitle.style.color = ArmyStableRgb(219, 181, 107);
            sectionTitle.style.fontSize = 13;
            sectionTitle.style.marginBottom = 6;
        }

        VisualElement image =
            commanderProfile.Q<VisualElement>(
                className: "commander-image-placeholder");

        if (image != null)
        {
            image.style.width = Length.Percent(100);
            image.style.height = Length.Percent(68);
            image.style.minHeight = 160;
            image.style.maxHeight = 270;
            image.style.marginBottom = 8;
            image.style.backgroundColor = ArmyStableRgb(31, 35, 42);
            SetStableArmyBorder(image, 1, ArmyStableRgb(72, 78, 88));
            SetStableArmyRadius(image, 4);
        }

        commanderDropdown.style.width = Length.Percent(100);
        commanderDropdown.style.marginTop = 0;
        commanderDropdown.style.marginBottom = 4;

        commanderDetailLabel.style.color = ArmyStableRgb(184, 184, 178);
        commanderDetailLabel.style.fontSize = 10;
        commanderDetailLabel.style.whiteSpace = WhiteSpace.Normal;
    }

    private void ConfigureStableJourneyBlock()
    {
        if (journeySummaryBlock == null)
            return;

        journeySummaryBlock.style.width = Length.Percent(37);
        journeySummaryBlock.style.minWidth = 220;
        journeySummaryBlock.style.height = Length.Percent(100);
        journeySummaryBlock.style.minHeight = 0;
        journeySummaryBlock.style.marginLeft = 0;
        journeySummaryBlock.style.marginRight = 5;
        journeySummaryBlock.style.marginTop = 0;
        journeySummaryBlock.style.marginBottom = 0;
        journeySummaryBlock.style.paddingLeft = 10;
        journeySummaryBlock.style.paddingRight = 10;
        journeySummaryBlock.style.paddingTop = 10;
        journeySummaryBlock.style.paddingBottom = 10;
        journeySummaryBlock.style.backgroundColor = ArmyStableRgb(45, 49, 56);
        SetStableArmyBorder(
            journeySummaryBlock,
            1,
            ArmyStableRgb(103, 86, 61));
        SetStableArmyRadius(journeySummaryBlock, 5);

        Label title =
            journeySummaryBlock.Q<Label>(className: "supply-title");

        if (title != null)
        {
            title.style.color = ArmyStableRgb(222, 185, 109);
            title.style.fontSize = 13;
            title.style.marginBottom = 7;
        }

        if (journeySummaryScroll != null)
        {
            journeySummaryScroll.style.flexGrow = 1;
            journeySummaryScroll.style.minHeight = 0;
            journeySummaryScroll.style.width = Length.Percent(100);
        }
    }

    private void ConfigureStableSupplyBlock(VisualElement supplyBlock)
    {
        supplyBlock.style.width = Length.Percent(28);
        supplyBlock.style.minWidth = 205;
        supplyBlock.style.height = 176;
        supplyBlock.style.minHeight = 176;
        supplyBlock.style.maxHeight = 176;
        supplyBlock.style.marginLeft = 0;
        supplyBlock.style.marginRight = 0;
        supplyBlock.style.marginTop = 0;
        supplyBlock.style.marginBottom = 0;
        supplyBlock.style.paddingLeft = 11;
        supplyBlock.style.paddingRight = 11;
        supplyBlock.style.paddingTop = 10;
        supplyBlock.style.paddingBottom = 9;
        supplyBlock.style.backgroundColor = ArmyStableRgb(48, 49, 53);
        SetStableArmyBorder(
            supplyBlock,
            1,
            ArmyStableRgb(111, 94, 62));
        SetStableArmyRadius(supplyBlock, 5);

        Label title = supplyBlock.Q<Label>(className: "supply-title");

        if (title != null)
        {
            title.style.color = ArmyStableRgb(225, 190, 116);
            title.style.fontSize = 13;
            title.style.marginBottom = 6;
        }

        supplyBlock.Query<Label>(className: "supply-subtitle")
            .ForEach(label =>
            {
                label.style.marginTop = 1;
                label.style.marginBottom = 2;
                label.style.fontSize = 9;
                label.style.color = ArmyStableRgb(185, 180, 164);
            });

        supplyBlock.Query<VisualElement>(className: "supply-controls")
            .ForEach(row =>
            {
                row.style.height = 28;
                row.style.minHeight = 28;
                row.style.marginBottom = 3;
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
            });

        supplyBlock.Query<Button>(className: "supply-step-button")
            .ForEach(button =>
            {
                button.style.width = 32;
                button.style.minWidth = 32;
                button.style.maxWidth = 32;
                button.style.height = 26;
                button.style.minHeight = 26;
                button.style.paddingLeft = 0;
                button.style.paddingRight = 0;
            });

        supplyBlock.Query<Label>(className: "supply-value")
            .ForEach(label =>
            {
                label.style.minWidth = 42;
                label.style.marginLeft = 5;
                label.style.marginRight = 5;
                label.style.fontSize = 14;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
            });

        supplyBlock.Query<Label>(className: "supply-note")
            .ForEach(label =>
            {
                label.style.marginTop = 1;
                label.style.marginBottom = 0;
                label.style.fontSize = 9;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.color = ArmyStableRgb(160, 160, 154);
            });
    }

    private void PutCapitalGarrisonFirst(VisualElement transferBoard)
    {
        if (capitalGarrisonDropZone.parent != transferBoard ||
            commanderGarrisonDropZone.parent != transferBoard)
        {
            return;
        }

        capitalGarrisonDropZone.RemoveFromHierarchy();
        commanderGarrisonDropZone.RemoveFromHierarchy();
        transferBoard.Add(capitalGarrisonDropZone);
        transferBoard.Add(commanderGarrisonDropZone);
    }

    private void RemoveRosterScroll(
        VisualElement zone,
        VisualElement list)
    {
        if (zone == null || list == null)
            return;

        ScrollView scroll =
            zone.Q<ScrollView>(className: "army-roster-scroll");

        if (scroll == null)
            return;

        list.RemoveFromHierarchy();
        scroll.RemoveFromHierarchy();
        zone.Add(list);
    }

    private void ConfigureStableGarrisonZone(
        VisualElement zone,
        VisualElement list,
        bool capital)
    {
        if (zone == null || list == null)
            return;

        zone.style.position = Position.Relative;
        zone.style.width = Length.Percent(100);
        zone.style.height = Length.Percent(49);
        zone.style.minHeight = 104;
        zone.style.maxHeight = 136;
        zone.style.flexGrow = 1;
        zone.style.flexShrink = 1;
        zone.style.marginBottom = capital ? 5 : 0;
        zone.style.paddingLeft = 10;
        zone.style.paddingRight = 10;
        zone.style.paddingTop = 7;
        zone.style.paddingBottom = 7;
        SetStableArmyRadius(zone, 5);

        Label title = zone.Q<Label>(className: "army-roster-title");
        Label summary = zone.Q<Label>(className: "army-roster-summary");
        Label empty = zone.Q<Label>(className: "army-roster-empty-label");

        if (title != null)
        {
            title.style.marginBottom = 1;
            title.style.fontSize = 11;
            title.style.color = capital
                ? ArmyStableRgb(151, 192, 165)
                : ArmyStableRgb(220, 181, 105);
        }

        if (summary != null)
        {
            summary.style.marginBottom = 3;
            summary.style.fontSize = 9;
            summary.style.color = ArmyStableRgb(164, 166, 162);
        }

        list.style.width = Length.Percent(100);
        list.style.height = 80;
        list.style.minHeight = 80;
        list.style.maxHeight = 80;
        list.style.minWidth = Length.Percent(100);
        list.style.flexDirection = FlexDirection.Row;
        list.style.flexWrap = Wrap.NoWrap;
        list.style.alignItems = Align.Center;
        list.style.flexGrow = 0;
        list.style.flexShrink = 0;

        if (empty != null)
        {
            empty.style.left = 10;
            empty.style.right = 10;
            empty.style.top = 42;
            empty.style.bottom = 7;
            empty.style.fontSize = 9;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
        }
    }

    private void MaintainStableArmyCards()
    {
        if (!stableArmyLayoutInitialized ||
            gameState == null ||
            armyScreen == null)
        {
            return;
        }

        if (StableArmyCardsNeedRefresh(capitalGarrisonList) ||
            StableArmyCardsNeedRefresh(commanderGarrisonList))
        {
            RefreshStableArmyCards();
        }
    }

    private bool StableArmyCardsNeedRefresh(VisualElement list)
    {
        if (list == null)
            return false;

        bool needsRefresh = false;

        list.Query<VisualElement>(className: "fighter-card")
            .ForEach(card =>
            {
                if (needsRefresh)
                    return;

                if (!(card.userData is string) ||
                    card.childCount != 1 ||
                    card.Q<Label>(className: "fighter-name") != null)
                {
                    needsRefresh = true;
                }
            });

        return needsRefresh;
    }

    private void RefreshStableArmyCards()
    {
        PrepareStableArmyCardsInList(capitalGarrisonList, true);
        PrepareStableArmyCardsInList(commanderGarrisonList, false);
        RefreshStableArmyDropZoneColors(false, false);
    }

    private void PrepareStableArmyCardsInList(
        VisualElement list,
        bool capital)
    {
        if (list == null)
            return;

        list.Query<VisualElement>(className: "fighter-card")
            .ForEach(card => PrepareStableArmyCard(card, capital));
    }

    private void PrepareStableArmyCard(
        VisualElement card,
        bool capital)
    {
        FighterData fighter = ResolveStableFighterForCard(card);

        if (fighter == null)
            return;

        card.userData = fighter.Id;
        card.tooltip = gameState.HasActiveExpedition
            ? "Состав зафиксирован до возвращения экспедиции"
            : "Перетащите бойца в другой гарнизон";
        card.SetEnabled(!gameState.HasActiveExpedition && !isGameOver);

        VisualElement image =
            card.Q<VisualElement>(className: "fighter-image-placeholder");

        if (image == null)
        {
            image = new VisualElement();
            image.AddToClassList("fighter-image-placeholder");
        }

        Label typeLabel =
            image.Q<Label>(className: "fighter-image-placeholder-text");

        if (typeLabel == null)
        {
            typeLabel = new Label();
            typeLabel.AddToClassList("fighter-image-placeholder-text");
            image.Add(typeLabel);
        }

        typeLabel.text = fighter.Role;

        card.Clear();
        card.Add(image);

        card.style.width = 104;
        card.style.minWidth = 104;
        card.style.maxWidth = 104;
        card.style.height = 78;
        card.style.minHeight = 78;
        card.style.maxHeight = 78;
        card.style.flexGrow = 0;
        card.style.flexShrink = 0;
        card.style.marginRight = 7;
        card.style.marginBottom = 0;
        card.style.paddingLeft = 5;
        card.style.paddingRight = 5;
        card.style.paddingTop = 5;
        card.style.paddingBottom = 5;
        card.style.backgroundColor = capital
            ? ArmyStableRgb(42, 57, 51)
            : ArmyStableRgb(58, 50, 38);
        SetStableArmyBorder(
            card,
            1,
            capital
                ? ArmyStableRgb(78, 118, 95)
                : ArmyStableRgb(139, 109, 65));
        SetStableArmyRadius(card, 4);

        image.style.width = Length.Percent(100);
        image.style.height = Length.Percent(100);
        image.style.marginBottom = 0;
        image.style.alignItems = Align.Center;
        image.style.justifyContent = Justify.Center;
        image.style.backgroundColor = ArmyStableRgb(29, 33, 39);
        SetStableArmyBorder(image, 1, ArmyStableRgb(69, 76, 86));
        SetStableArmyRadius(image, 3);

        typeLabel.style.color = capital
            ? ArmyStableRgb(168, 202, 180)
            : ArmyStableRgb(226, 191, 119);
        typeLabel.style.fontSize = 10;
        typeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        typeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        typeLabel.style.whiteSpace = WhiteSpace.Normal;
    }

    private FighterData ResolveStableFighterForCard(VisualElement card)
    {
        if (card == null || gameState == null)
            return null;

        string storedId = card.userData as string;

        if (!string.IsNullOrEmpty(storedId))
            return gameState.FindFighter(storedId);

        Label nameLabel = card.Q<Label>(className: "fighter-name");

        if (nameLabel == null)
            return null;

        foreach (FighterData fighter in gameState.Fighters)
        {
            if (fighter.Name == nameLabel.text)
                return fighter;
        }

        return null;
    }

    private void OnStableArmyPointerDown(PointerDownEvent pointerEvent)
    {
        if (pointerEvent.button != 0 ||
            isGameOver ||
            gameState == null ||
            gameState.HasActiveExpedition)
        {
            return;
        }

        VisualElement target = pointerEvent.target as VisualElement;
        VisualElement card = FindStableArmyFighterCard(target);

        if (card == null)
            return;

        FighterData fighter = ResolveStableFighterForCard(card);

        if (fighter == null)
            return;

        CleanupStableArmyDrag();

        stableDraggedFighterId = fighter.Id;
        stableDraggedPointerId = pointerEvent.pointerId;
        stableDragStartPosition = pointerEvent.position;
        stableDraggedCard = card;
        stableDragStarted = false;

        interfaceRoot.CapturePointer(pointerEvent.pointerId);
        pointerEvent.StopImmediatePropagation();
    }

    private void OnStableArmyPointerMove(PointerMoveEvent pointerEvent)
    {
        if (stableDraggedCard == null ||
            stableDraggedPointerId != pointerEvent.pointerId ||
            interfaceRoot == null ||
            !interfaceRoot.HasPointerCapture(pointerEvent.pointerId))
        {
            return;
        }

        if (!stableDragStarted &&
            Vector2.Distance(
                stableDragStartPosition,
                pointerEvent.position) >= FighterDragThreshold)
        {
            BeginStableArmyDrag(pointerEvent.position);
        }

        if (stableDragStarted)
        {
            UpdateStableArmyDragGhost(pointerEvent.position);

            bool overCommander =
                commanderGarrisonDropZone.worldBound.Contains(
                    pointerEvent.position);
            bool overCapital =
                capitalGarrisonDropZone.worldBound.Contains(
                    pointerEvent.position);

            RefreshStableArmyDropZoneColors(overCapital, overCommander);
        }

        pointerEvent.StopImmediatePropagation();
    }

    private void OnStableArmyPointerUp(PointerUpEvent pointerEvent)
    {
        if (stableDraggedCard == null ||
            stableDraggedPointerId != pointerEvent.pointerId)
        {
            return;
        }

        string fighterId = stableDraggedFighterId;
        bool wasDragging = stableDragStarted;
        bool droppedToCommander =
            wasDragging &&
            commanderGarrisonDropZone.worldBound.Contains(
                pointerEvent.position);
        bool droppedToCapital =
            wasDragging &&
            capitalGarrisonDropZone.worldBound.Contains(
                pointerEvent.position);

        if (interfaceRoot != null &&
            interfaceRoot.HasPointerCapture(pointerEvent.pointerId))
        {
            interfaceRoot.ReleasePointer(pointerEvent.pointerId);
        }

        CleanupStableArmyDrag();

        if (wasDragging && droppedToCommander)
        {
            MoveFighterToCommander(fighterId, true);
            RefreshStableArmyCards();
        }
        else if (wasDragging && droppedToCapital)
        {
            MoveFighterToCommander(fighterId, false);
            RefreshStableArmyCards();
        }

        pointerEvent.StopImmediatePropagation();
    }

    private void BeginStableArmyDrag(Vector2 pointerPosition)
    {
        FighterData fighter = gameState.FindFighter(stableDraggedFighterId);

        if (fighter == null || interfaceRoot == null)
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
        stableDragGhost.style.backgroundColor = ArmyStableRgb(54, 48, 38);
        stableDragGhost.style.opacity = 0.96f;
        SetStableArmyBorder(
            stableDragGhost,
            2,
            ArmyStableRgb(218, 176, 96));
        SetStableArmyRadius(stableDragGhost, 4);

        VisualElement image = new VisualElement();
        image.style.width = Length.Percent(100);
        image.style.height = Length.Percent(100);
        image.style.alignItems = Align.Center;
        image.style.justifyContent = Justify.Center;
        image.style.backgroundColor = ArmyStableRgb(29, 33, 39);
        SetStableArmyBorder(image, 1, ArmyStableRgb(79, 83, 91));
        SetStableArmyRadius(image, 3);

        Label role = new Label(fighter.Role);
        role.style.color = ArmyStableRgb(231, 197, 127);
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
        {
            interfaceRoot.ReleasePointer(stableDraggedPointerId);
        }

        if (stableDraggedCard != null)
            stableDraggedCard.style.opacity = 1f;

        if (stableDragGhost != null)
            stableDragGhost.RemoveFromHierarchy();

        RefreshStableArmyDropZoneColors(false, false);

        stableDraggedFighterId = null;
        stableDraggedPointerId = -1;
        stableDraggedCard = null;
        stableDragGhost = null;
        stableDragStarted = false;
    }

    private void RefreshStableArmyDropZoneColors(
        bool capitalHighlighted,
        bool commanderHighlighted)
    {
        if (capitalGarrisonDropZone != null)
        {
            bool empty =
                capitalGarrisonList != null &&
                capitalGarrisonList.childCount == 0;

            Color background = empty
                ? ArmyStableRgb(61, 42, 43)
                : ArmyStableRgb(38, 54, 48);
            Color border = empty
                ? ArmyStableRgb(129, 74, 70)
                : ArmyStableRgb(75, 112, 91);

            if (capitalHighlighted)
            {
                background = ArmyStableRgb(48, 72, 60);
                border = ArmyStableRgb(126, 174, 140);
            }

            capitalGarrisonDropZone.style.backgroundColor = background;
            SetStableArmyBorder(
                capitalGarrisonDropZone,
                capitalHighlighted ? 2 : 1,
                border);
        }

        if (commanderGarrisonDropZone != null)
        {
            Color background = commanderHighlighted
                ? ArmyStableRgb(71, 61, 43)
                : ArmyStableRgb(50, 44, 34);
            Color border = commanderHighlighted
                ? ArmyStableRgb(218, 177, 98)
                : ArmyStableRgb(132, 103, 62);

            commanderGarrisonDropZone.style.backgroundColor = background;
            SetStableArmyBorder(
                commanderGarrisonDropZone,
                commanderHighlighted ? 2 : 1,
                border);
        }
    }

    private VisualElement FindStableArmyFighterCard(VisualElement element)
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

    private static Color ArmyStableRgb(byte r, byte g, byte b)
    {
        return new Color32(r, g, b, 255);
    }

    private static void SetStableArmyBorder(
        VisualElement element,
        float width,
        Color color)
    {
        if (element == null)
            return;

        element.style.borderLeftWidth = width;
        element.style.borderRightWidth = width;
        element.style.borderTopWidth = width;
        element.style.borderBottomWidth = width;
        element.style.borderLeftColor = color;
        element.style.borderRightColor = color;
        element.style.borderTopColor = color;
        element.style.borderBottomColor = color;
    }

    private static void SetStableArmyRadius(
        VisualElement element,
        float radius)
    {
        if (element == null)
            return;

        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }
}
