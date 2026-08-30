using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool armyLayoutRuntimeInitialized;
    private bool armyPointerCallbacksRegistered;

    private string armyDraggedFighterId;
    private int armyDraggedPointerId = -1;
    private Vector2 armyDragStartPosition;
    private bool armyDragStarted;
    private VisualElement armyDraggedCard;
    private VisualElement armyDragGhost;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeArmyLayoutRuntime()
    {
        PrototypeUIController controller =
            Object.FindFirstObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();

        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeArmyLayoutRuntime)
            .Every(100);
    }

    private void TryInitializeArmyLayoutRuntime()
    {
        if (armyLayoutRuntimeInitialized)
            return;

        if (interfaceRoot == null ||
            gameState == null ||
            armyScreen == null ||
            commanderGarrisonDropZone == null ||
            commanderGarrisonList == null ||
            capitalGarrisonDropZone == null ||
            capitalGarrisonList == null)
        {
            return;
        }

        RegisterArmyPointerCallbacks();
        ApplyArmyScreenLayout();
        RefreshArmyCardsRuntime();

        interfaceRoot.schedule
            .Execute(RefreshArmyScreenRuntime)
            .Every(100);

        armyLayoutRuntimeInitialized = true;
    }

    private void RegisterArmyPointerCallbacks()
    {
        if (armyPointerCallbacksRegistered || interfaceRoot == null)
            return;

        interfaceRoot.RegisterCallback<PointerDownEvent>(
            OnArmyRootPointerDown,
            TrickleDown.TrickleDown);
        interfaceRoot.RegisterCallback<PointerMoveEvent>(OnArmyRootPointerMove);
        interfaceRoot.RegisterCallback<PointerUpEvent>(OnArmyRootPointerUp);

        armyPointerCallbacksRegistered = true;
    }

    private void RefreshArmyScreenRuntime()
    {
        if (interfaceRoot == null || gameState == null)
            return;

        ApplyArmyScreenLayout();
        RefreshArmyCardsRuntime();
    }

    private void ApplyArmyScreenLayout()
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

        ScrollView armyScroll =
            armyScreen.Q<ScrollView>(className: "military-army-column");

        if (armyScroll != null)
        {
            armyScroll.style.width = Length.Percent(100);
            armyScroll.style.height = Length.Percent(100);
            armyScroll.style.minHeight = 0;
            armyScroll.contentContainer.style.flexGrow = 1;
            armyScroll.contentContainer.style.minHeight = 0;
        }

        armyPanel.style.width = Length.Percent(100);
        armyPanel.style.height = Length.Percent(100);
        armyPanel.style.minHeight = 0;
        armyPanel.style.marginBottom = 0;
        armyPanel.style.paddingLeft = 0;
        armyPanel.style.paddingRight = 0;
        armyPanel.style.paddingTop = 0;
        armyPanel.style.paddingBottom = 0;
        armyPanel.style.backgroundColor = ArmyRgb(18, 21, 26);
        SetArmyBorder(armyPanel, 0, Color.clear);

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

        topRow.style.width = Length.Percent(82);
        topRow.style.height = Length.Percent(60);
        topRow.style.minHeight = 285;
        topRow.style.maxHeight = 430;
        topRow.style.flexGrow = 0;
        topRow.style.flexShrink = 1;
        topRow.style.flexDirection = FlexDirection.Row;
        topRow.style.alignItems = Align.Stretch;
        topRow.style.justifyContent = Justify.FlexStart;
        topRow.style.alignSelf = Align.FlexStart;
        topRow.style.marginBottom = 4;

        ConfigureCommanderBlock(commanderProfile);
        ConfigureJourneyBlock();
        ConfigureSupplyBlock(supplyBlock);

        if (transferArrows != null)
            transferArrows.style.display = DisplayStyle.None;

        // По макету сначала гарнизон столицы, ниже — гарнизон командира.
        if (capitalGarrisonDropZone.parent == transferBoard &&
            commanderGarrisonDropZone.parent == transferBoard)
        {
            capitalGarrisonDropZone.RemoveFromHierarchy();
            commanderGarrisonDropZone.RemoveFromHierarchy();
            transferBoard.Add(capitalGarrisonDropZone);
            transferBoard.Add(commanderGarrisonDropZone);
        }

        transferBoard.style.width = Length.Percent(82);
        transferBoard.style.height = Length.Percent(40);
        transferBoard.style.minHeight = 220;
        transferBoard.style.flexGrow = 1;
        transferBoard.style.flexShrink = 1;
        transferBoard.style.flexDirection = FlexDirection.Column;
        transferBoard.style.alignItems = Align.Stretch;
        transferBoard.style.alignSelf = Align.FlexStart;
        transferBoard.style.marginTop = 0;
        transferBoard.style.marginBottom = 0;

        ConfigureGarrisonZone(capitalGarrisonDropZone, true);
        ConfigureGarrisonZone(commanderGarrisonDropZone, false);
        RefreshArmyDropZoneColors(false, false);
    }

    private void ConfigureCommanderBlock(VisualElement commanderProfile)
    {
        commanderProfile.style.width = Length.Percent(33);
        commanderProfile.style.minWidth = 210;
        commanderProfile.style.height = Length.Percent(100);
        commanderProfile.style.marginRight = 5;
        commanderProfile.style.paddingLeft = 10;
        commanderProfile.style.paddingRight = 10;
        commanderProfile.style.paddingTop = 10;
        commanderProfile.style.paddingBottom = 9;
        commanderProfile.style.backgroundColor = ArmyRgb(43, 47, 55);
        SetArmyBorder(commanderProfile, 1, ArmyRgb(91, 79, 61));
        SetArmyRadius(commanderProfile, 5);

        Label sectionTitle =
            commanderProfile.Q<Label>(className: "commander-section-title");

        if (sectionTitle != null)
        {
            sectionTitle.style.color = ArmyRgb(218, 181, 108);
            sectionTitle.style.fontSize = 13;
            sectionTitle.style.marginBottom = 6;
        }

        VisualElement image =
            commanderProfile.Q<VisualElement>(className: "commander-image-placeholder");

        if (image != null)
        {
            image.style.width = Length.Percent(100);
            image.style.height = Length.Percent(62);
            image.style.minHeight = 150;
            image.style.maxHeight = 260;
            image.style.marginBottom = 8;
            image.style.backgroundColor = ArmyRgb(32, 36, 43);
            SetArmyBorder(image, 1, ArmyRgb(73, 78, 88));
            SetArmyRadius(image, 4);
        }

        if (commanderDropdown != null)
        {
            commanderDropdown.style.width = Length.Percent(100);
            commanderDropdown.style.marginTop = 0;
            commanderDropdown.style.marginBottom = 4;
        }

        if (commanderDetailLabel != null)
        {
            commanderDetailLabel.style.color = ArmyRgb(185, 184, 177);
            commanderDetailLabel.style.fontSize = 10;
            commanderDetailLabel.style.whiteSpace = WhiteSpace.Normal;
        }
    }

    private void ConfigureJourneyBlock()
    {
        if (journeySummaryBlock == null)
            return;

        journeySummaryBlock.style.width = Length.Percent(33);
        journeySummaryBlock.style.minWidth = 210;
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
        journeySummaryBlock.style.backgroundColor = ArmyRgb(47, 50, 57);
        SetArmyBorder(journeySummaryBlock, 1, ArmyRgb(105, 87, 61));
        SetArmyRadius(journeySummaryBlock, 5);

        Label title = journeySummaryBlock.Q<Label>(className: "supply-title");

        if (title != null)
        {
            title.style.color = ArmyRgb(222, 185, 109);
            title.style.fontSize = 13;
        }

        if (journeySummaryScroll != null)
        {
            journeySummaryScroll.style.flexGrow = 1;
            journeySummaryScroll.style.minHeight = 0;
        }
    }

    private void ConfigureSupplyBlock(VisualElement supplyBlock)
    {
        supplyBlock.style.width = Length.Percent(31);
        supplyBlock.style.minWidth = 185;
        supplyBlock.style.height = 132;
        supplyBlock.style.minHeight = 132;
        supplyBlock.style.maxHeight = 132;
        supplyBlock.style.marginLeft = 0;
        supplyBlock.style.marginRight = 0;
        supplyBlock.style.marginTop = 0;
        supplyBlock.style.paddingLeft = 10;
        supplyBlock.style.paddingRight = 10;
        supplyBlock.style.paddingTop = 10;
        supplyBlock.style.paddingBottom = 8;
        supplyBlock.style.backgroundColor = ArmyRgb(51, 52, 55);
        SetArmyBorder(supplyBlock, 1, ArmyRgb(112, 94, 61));
        SetArmyRadius(supplyBlock, 5);

        Label title = supplyBlock.Q<Label>(className: "supply-title");

        if (title != null)
        {
            title.style.color = ArmyRgb(225, 190, 116);
            title.style.fontSize = 13;
        }
    }

    private void ConfigureGarrisonZone(VisualElement zone, bool capital)
    {
        if (zone == null)
            return;

        zone.style.position = Position.Relative;
        zone.style.width = Length.Percent(100);
        zone.style.height = Length.Percent(49);
        zone.style.minHeight = 104;
        zone.style.maxHeight = 150;
        zone.style.flexGrow = 1;
        zone.style.flexShrink = 1;
        zone.style.marginBottom = capital ? 5 : 0;
        zone.style.paddingLeft = 10;
        zone.style.paddingRight = 10;
        zone.style.paddingTop = 7;
        zone.style.paddingBottom = 7;
        SetArmyRadius(zone, 5);

        Label title = zone.Q<Label>(className: "army-roster-title");
        Label summary = zone.Q<Label>(className: "army-roster-summary");
        Label empty = zone.Q<Label>(className: "army-roster-empty-label");
        ScrollView scroll = zone.Q<ScrollView>(className: "army-roster-scroll");
        VisualElement list = zone.Q<VisualElement>(className: "army-roster-list");

        if (title != null)
        {
            title.style.marginBottom = 1;
            title.style.fontSize = 11;
            title.style.color = capital
                ? ArmyRgb(150, 190, 164)
                : ArmyRgb(220, 181, 105);
        }

        if (summary != null)
        {
            summary.style.marginBottom = 3;
            summary.style.fontSize = 9;
            summary.style.color = ArmyRgb(165, 167, 164);
        }

        if (scroll != null)
        {
            scroll.style.width = Length.Percent(100);
            scroll.style.height = 78;
            scroll.style.maxHeight = 78;
            scroll.style.minHeight = 68;
            scroll.style.flexGrow = 1;
        }

        if (list != null)
        {
            list.style.minWidth = Length.Percent(100);
            list.style.minHeight = 72;
            list.style.height = 72;
            list.style.flexDirection = FlexDirection.Row;
            list.style.flexWrap = Wrap.NoWrap;
            list.style.alignItems = Align.Center;
        }

        if (empty != null)
        {
            empty.style.left = 10;
            empty.style.right = 10;
            empty.style.top = 43;
            empty.style.bottom = 7;
            empty.style.fontSize = 9;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
        }
    }

    private void RefreshArmyCardsRuntime()
    {
        if (gameState == null)
            return;

        PrepareArmyCardsInList(capitalGarrisonList, true);
        PrepareArmyCardsInList(commanderGarrisonList, false);
    }

    private void PrepareArmyCardsInList(VisualElement list, bool capital)
    {
        if (list == null)
            return;

        list.Query<VisualElement>(className: "fighter-card")
            .ForEach(card => PrepareArmyFighterCard(card, capital));
    }

    private void PrepareArmyFighterCard(VisualElement card, bool capital)
    {
        if (card == null)
            return;

        FighterData fighter = ResolveFighterForCard(card);

        if (fighter == null)
            return;

        card.userData = fighter.Id;
        card.tooltip = gameState.HasActiveExpedition
            ? "Состав зафиксирован до возвращения экспедиции"
            : "Перетащите бойца в другой гарнизон";

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

        // В карточке остаётся только изображение-заглушка с типом бойца.
        card.Clear();
        card.Add(image);

        card.style.width = 92;
        card.style.minWidth = 92;
        card.style.maxWidth = 92;
        card.style.height = 74;
        card.style.minHeight = 74;
        card.style.maxHeight = 74;
        card.style.flexGrow = 0;
        card.style.flexShrink = 0;
        card.style.marginRight = 7;
        card.style.marginBottom = 0;
        card.style.paddingLeft = 5;
        card.style.paddingRight = 5;
        card.style.paddingTop = 5;
        card.style.paddingBottom = 5;
        card.style.backgroundColor = capital
            ? ArmyRgb(43, 58, 52)
            : ArmyRgb(59, 51, 38);
        SetArmyBorder(
            card,
            1,
            capital
                ? ArmyRgb(79, 119, 96)
                : ArmyRgb(139, 109, 65));
        SetArmyRadius(card, 4);

        image.style.width = Length.Percent(100);
        image.style.height = Length.Percent(100);
        image.style.marginBottom = 0;
        image.style.alignItems = Align.Center;
        image.style.justifyContent = Justify.Center;
        image.style.backgroundColor = ArmyRgb(30, 34, 40);
        SetArmyBorder(image, 1, ArmyRgb(70, 76, 86));
        SetArmyRadius(image, 3);

        typeLabel.style.color = capital
            ? ArmyRgb(167, 202, 179)
            : ArmyRgb(226, 191, 119);
        typeLabel.style.fontSize = 10;
        typeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        typeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        typeLabel.style.whiteSpace = WhiteSpace.Normal;
    }

    private FighterData ResolveFighterForCard(VisualElement card)
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

    private void OnArmyRootPointerDown(PointerDownEvent pointerEvent)
    {
        if (pointerEvent.button != 0 ||
            isGameOver ||
            gameState == null ||
            gameState.HasActiveExpedition)
        {
            return;
        }

        VisualElement target = pointerEvent.target as VisualElement;
        VisualElement card = FindArmyFighterCard(target);

        if (card == null)
            return;

        bool capital = IsCardInList(card, capitalGarrisonList);
        PrepareArmyFighterCard(card, capital);

        string fighterId = card.userData as string;

        if (string.IsNullOrEmpty(fighterId))
            return;

        CleanupArmyDragRuntime();

        armyDraggedFighterId = fighterId;
        armyDraggedPointerId = pointerEvent.pointerId;
        armyDragStartPosition = pointerEvent.position;
        armyDraggedCard = card;
        armyDragStarted = false;

        interfaceRoot.CapturePointer(pointerEvent.pointerId);
        pointerEvent.StopImmediatePropagation();
    }

    private void OnArmyRootPointerMove(PointerMoveEvent pointerEvent)
    {
        if (armyDraggedCard == null ||
            armyDraggedPointerId != pointerEvent.pointerId ||
            interfaceRoot == null ||
            !interfaceRoot.HasPointerCapture(pointerEvent.pointerId))
        {
            return;
        }

        if (!armyDragStarted &&
            Vector2.Distance(armyDragStartPosition, pointerEvent.position) >=
            FighterDragThreshold)
        {
            BeginArmyDragRuntime(pointerEvent.position);
        }

        if (armyDragStarted)
        {
            UpdateArmyDragGhost(pointerEvent.position);

            bool overCommander =
                commanderGarrisonDropZone.worldBound.Contains(pointerEvent.position);
            bool overCapital =
                capitalGarrisonDropZone.worldBound.Contains(pointerEvent.position);

            RefreshArmyDropZoneColors(overCapital, overCommander);
        }

        pointerEvent.StopImmediatePropagation();
    }

    private void OnArmyRootPointerUp(PointerUpEvent pointerEvent)
    {
        if (armyDraggedCard == null ||
            armyDraggedPointerId != pointerEvent.pointerId)
        {
            return;
        }

        string fighterId = armyDraggedFighterId;
        bool wasDragging = armyDragStarted;
        bool droppedToCommander =
            wasDragging &&
            commanderGarrisonDropZone.worldBound.Contains(pointerEvent.position);
        bool droppedToCapital =
            wasDragging &&
            capitalGarrisonDropZone.worldBound.Contains(pointerEvent.position);

        if (interfaceRoot != null &&
            interfaceRoot.HasPointerCapture(pointerEvent.pointerId))
        {
            interfaceRoot.ReleasePointer(pointerEvent.pointerId);
        }

        CleanupArmyDragRuntime();

        // Обычный клик намеренно ничего не делает.
        if (wasDragging && droppedToCommander)
            MoveFighterToCommander(fighterId, true);
        else if (wasDragging && droppedToCapital)
            MoveFighterToCommander(fighterId, false);

        pointerEvent.StopImmediatePropagation();
    }

    private void BeginArmyDragRuntime(Vector2 pointerPosition)
    {
        FighterData fighter = gameState.FindFighter(armyDraggedFighterId);

        if (fighter == null || interfaceRoot == null)
            return;

        armyDragStarted = true;
        armyDraggedCard.style.opacity = 0.32f;

        armyDragGhost = new VisualElement();
        armyDragGhost.pickingMode = PickingMode.Ignore;
        armyDragGhost.style.position = Position.Absolute;
        armyDragGhost.style.width = 92;
        armyDragGhost.style.height = 74;
        armyDragGhost.style.paddingLeft = 5;
        armyDragGhost.style.paddingRight = 5;
        armyDragGhost.style.paddingTop = 5;
        armyDragGhost.style.paddingBottom = 5;
        armyDragGhost.style.backgroundColor = ArmyRgb(54, 48, 38);
        armyDragGhost.style.opacity = 0.96f;
        SetArmyBorder(armyDragGhost, 2, ArmyRgb(218, 176, 96));
        SetArmyRadius(armyDragGhost, 4);

        VisualElement image = new VisualElement();
        image.style.width = Length.Percent(100);
        image.style.height = Length.Percent(100);
        image.style.alignItems = Align.Center;
        image.style.justifyContent = Justify.Center;
        image.style.backgroundColor = ArmyRgb(29, 33, 39);
        SetArmyBorder(image, 1, ArmyRgb(79, 83, 91));
        SetArmyRadius(image, 3);

        Label role = new Label(fighter.Role);
        role.style.color = ArmyRgb(231, 197, 127);
        role.style.fontSize = 10;
        role.style.unityFontStyleAndWeight = FontStyle.Bold;
        role.style.unityTextAlign = TextAnchor.MiddleCenter;
        role.style.whiteSpace = WhiteSpace.Normal;
        image.Add(role);
        armyDragGhost.Add(image);

        interfaceRoot.Add(armyDragGhost);
        armyDragGhost.BringToFront();
        UpdateArmyDragGhost(pointerPosition);
    }

    private void UpdateArmyDragGhost(Vector2 pointerPosition)
    {
        if (armyDragGhost == null)
            return;

        armyDragGhost.style.left = pointerPosition.x - 46f;
        armyDragGhost.style.top = pointerPosition.y - 37f;
    }

    private void CleanupArmyDragRuntime()
    {
        if (interfaceRoot != null &&
            armyDraggedPointerId >= 0 &&
            interfaceRoot.HasPointerCapture(armyDraggedPointerId))
        {
            interfaceRoot.ReleasePointer(armyDraggedPointerId);
        }

        if (armyDraggedCard != null)
            armyDraggedCard.style.opacity = 1f;

        if (armyDragGhost != null)
            armyDragGhost.RemoveFromHierarchy();

        RefreshArmyDropZoneColors(false, false);

        armyDraggedFighterId = null;
        armyDraggedPointerId = -1;
        armyDraggedCard = null;
        armyDragGhost = null;
        armyDragStarted = false;
    }

    private void RefreshArmyDropZoneColors(
        bool capitalHighlighted,
        bool commanderHighlighted)
    {
        if (capitalGarrisonDropZone != null)
        {
            bool empty =
                capitalGarrisonList != null &&
                capitalGarrisonList.childCount == 0;

            Color capitalBackground = empty
                ? ArmyRgb(61, 42, 43)
                : ArmyRgb(39, 55, 49);
            Color capitalBorder = empty
                ? ArmyRgb(130, 74, 70)
                : ArmyRgb(75, 112, 91);

            if (capitalHighlighted)
            {
                capitalBackground = ArmyRgb(48, 72, 60);
                capitalBorder = ArmyRgb(126, 174, 140);
            }

            capitalGarrisonDropZone.style.backgroundColor = capitalBackground;
            SetArmyBorder(capitalGarrisonDropZone, capitalHighlighted ? 2 : 1, capitalBorder);
        }

        if (commanderGarrisonDropZone != null)
        {
            Color commanderBackground = commanderHighlighted
                ? ArmyRgb(71, 61, 43)
                : ArmyRgb(51, 45, 35);
            Color commanderBorder = commanderHighlighted
                ? ArmyRgb(218, 177, 98)
                : ArmyRgb(132, 103, 62);

            commanderGarrisonDropZone.style.backgroundColor = commanderBackground;
            SetArmyBorder(
                commanderGarrisonDropZone,
                commanderHighlighted ? 2 : 1,
                commanderBorder);
        }
    }

    private VisualElement FindArmyFighterCard(VisualElement element)
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

    private static bool IsCardInList(VisualElement card, VisualElement list)
    {
        if (card == null || list == null)
            return false;

        VisualElement current = card.parent;

        while (current != null)
        {
            if (current == list)
                return true;

            current = current.parent;
        }

        return false;
    }

    private static Color ArmyRgb(byte r, byte g, byte b)
    {
        return new Color32(r, g, b, 255);
    }

    private static void SetArmyBorder(
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

    private static void SetArmyRadius(VisualElement element, float radius)
    {
        if (element == null)
            return;

        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }
}
