using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool expeditionViewsInitialized;
    private IVisualElementScheduledItem expeditionViewsInitItem;
    private IVisualElementScheduledItem expeditionViewsMaintenanceItem;

    private VisualElement quickExpeditionPopup;
    private Label quickExpeditionOrderLabel;
    private Label persistentCommanderStateLabel;
    private Label persistentCommanderTargetLabel;
    private VisualElement bigExpeditionLocationGrid;

    private readonly Dictionary<string, Button> quickExpeditionImageButtons =
        new Dictionary<string, Button>();
    private readonly Dictionary<string, Label> quickExpeditionImageLabels =
        new Dictionary<string, Label>();
    private readonly Dictionary<string, VisualElement> bigExpeditionImages =
        new Dictionary<string, VisualElement>();
    private readonly Dictionary<string, Label> bigExpeditionImageLabels =
        new Dictionary<string, Label>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeExpeditionViewsRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();

        if (document == null)
            return;

        controller.expeditionViewsInitItem = document.rootVisualElement.schedule
            .Execute(controller.TryInitializeExpeditionViews)
            .Every(100);
    }

    private void TryInitializeExpeditionViews()
    {
        if (expeditionViewsInitialized)
            return;

        if (!persistentCommanderShellInitialized ||
            interfaceRoot == null ||
            persistentCommanderPanel == null ||
            persistentCommanderExpeditionButton == null ||
            persistentCommanderGarrisonHost == null ||
            expeditionsScreen == null ||
            gameState == null)
        {
            return;
        }

        CreatePersistentCommanderStatus();
        CreateQuickExpeditionPopup();
        ConfigureLargeExpeditionScreen();
        RebindPersistentExpeditionButton();

        interfaceRoot.RegisterCallback<PointerDownEvent>(
            OnExpeditionQuickOutsidePointerDown,
            TrickleDown.TrickleDown);
        navExpeditionsButton.clicked += HideQuickExpeditionPopup;

        RefreshExpeditionViewState();

        expeditionViewsMaintenanceItem = interfaceRoot.schedule
            .Execute(MaintainExpeditionViews)
            .Every(200);

        expeditionViewsInitialized = true;
        expeditionViewsInitItem?.Pause();
    }

    private void CreatePersistentCommanderStatus()
    {
        VisualElement existing =
            persistentCommanderPanel.Q<VisualElement>("persistent-commander-status");

        if (existing != null)
        {
            persistentCommanderStateLabel =
                existing.Q<Label>("persistent-commander-state");
            persistentCommanderTargetLabel =
                existing.Q<Label>("persistent-commander-target");
            return;
        }

        VisualElement status = new VisualElement();
        status.name = "persistent-commander-status";
        status.style.width = Length.Percent(100);
        status.style.height = 38;
        status.style.minHeight = 38;
        status.style.maxHeight = 38;
        status.style.flexShrink = 0;
        status.style.marginBottom = 6;
        status.style.paddingLeft = 7;
        status.style.paddingRight = 7;
        status.style.paddingTop = 3;
        status.style.paddingBottom = 3;
        status.style.backgroundColor = ExpeditionRgb(27, 31, 37);
        SetExpeditionBorder(status, 1, ExpeditionRgb(62, 69, 78));
        SetExpeditionRadius(status, 3);

        persistentCommanderStateLabel = new Label("В ЗАМКЕ");
        persistentCommanderStateLabel.name = "persistent-commander-state";
        persistentCommanderStateLabel.style.height = 15;
        persistentCommanderStateLabel.style.fontSize = 10;
        persistentCommanderStateLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        persistentCommanderStateLabel.style.color = ExpeditionRgb(171, 199, 177);

        persistentCommanderTargetLabel = new Label("Цель: —");
        persistentCommanderTargetLabel.name = "persistent-commander-target";
        persistentCommanderTargetLabel.style.height = 15;
        persistentCommanderTargetLabel.style.fontSize = 9;
        persistentCommanderTargetLabel.style.color = ExpeditionRgb(177, 178, 174);
        persistentCommanderTargetLabel.style.whiteSpace = WhiteSpace.NoWrap;

        status.Add(persistentCommanderStateLabel);
        status.Add(persistentCommanderTargetLabel);

        int insertIndex = persistentCommanderPanel.childCount > 1 ? 1 :
            persistentCommanderPanel.childCount;
        persistentCommanderPanel.Insert(insertIndex, status);
    }

    private void CreateQuickExpeditionPopup()
    {
        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");

        if (screen == null)
            return;

        quickExpeditionPopup =
            screen.Q<VisualElement>("quick-expedition-popup");

        if (quickExpeditionPopup != null)
            return;

        quickExpeditionPopup = new VisualElement();
        quickExpeditionPopup.name = "quick-expedition-popup";
        quickExpeditionPopup.style.position = Position.Absolute;
        quickExpeditionPopup.style.width = 330;
        quickExpeditionPopup.style.height = 322;
        quickExpeditionPopup.style.minHeight = 322;
        quickExpeditionPopup.style.maxHeight = 322;
        quickExpeditionPopup.style.paddingLeft = 10;
        quickExpeditionPopup.style.paddingRight = 10;
        quickExpeditionPopup.style.paddingTop = 9;
        quickExpeditionPopup.style.paddingBottom = 9;
        quickExpeditionPopup.style.backgroundColor = ExpeditionRgb(29, 33, 39);
        quickExpeditionPopup.style.display = DisplayStyle.None;
        SetExpeditionBorder(
            quickExpeditionPopup,
            1,
            ExpeditionRgb(105, 88, 61));
        SetExpeditionRadius(quickExpeditionPopup, 5);

        Label title = new Label("ЭКСПЕДИЦИИ");
        title.style.height = 19;
        title.style.marginBottom = 4;
        title.style.fontSize = 12;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = ExpeditionRgb(222, 184, 107);
        quickExpeditionPopup.Add(title);

        quickExpeditionOrderLabel = new Label("ПРИКАЗ НА СЕГОДНЯ: нет");
        quickExpeditionOrderLabel.style.height = 17;
        quickExpeditionOrderLabel.style.marginBottom = 5;
        quickExpeditionOrderLabel.style.fontSize = 9;
        quickExpeditionOrderLabel.style.color = ExpeditionRgb(187, 186, 178);
        quickExpeditionPopup.Add(quickExpeditionOrderLabel);

        foreach (LocationData location in gameState.Locations)
            quickExpeditionPopup.Add(CreateQuickLocationCard(location));

        screen.Add(quickExpeditionPopup);
        quickExpeditionPopup.BringToFront();
    }

    private VisualElement CreateQuickLocationCard(LocationData location)
    {
        VisualElement card = new VisualElement();
        card.style.width = Length.Percent(100);
        card.style.height = 82;
        card.style.minHeight = 82;
        card.style.maxHeight = 82;
        card.style.marginBottom = 5;
        card.style.paddingLeft = 7;
        card.style.paddingRight = 7;
        card.style.paddingTop = 5;
        card.style.paddingBottom = 5;
        card.style.backgroundColor = ExpeditionRgb(35, 40, 47);
        SetExpeditionBorder(card, 1, ExpeditionRgb(65, 72, 82));
        SetExpeditionRadius(card, 4);

        Label name = new Label(location.Name);
        name.style.height = 16;
        name.style.fontSize = 10;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.color = ExpeditionRgb(215, 210, 197);
        card.Add(name);

        VisualElement row = new VisualElement();
        row.style.flexGrow = 1;
        row.style.minHeight = 0;
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Stretch;
        card.Add(row);

        Button image = new Button(() => OnExpeditionLocationImageClicked(location.Id));
        image.style.width = 142;
        image.style.minWidth = 142;
        image.style.maxWidth = 142;
        image.style.height = Length.Percent(100);
        image.style.marginRight = 8;
        image.style.paddingLeft = 4;
        image.style.paddingRight = 4;
        image.style.paddingTop = 4;
        image.style.paddingBottom = 4;
        image.style.backgroundColor = ExpeditionRgb(27, 31, 37);
        SetExpeditionBorder(image, 1, ExpeditionRgb(69, 77, 87));
        SetExpeditionRadius(image, 3);

        Label imageLabel = new Label("ИЗОБРАЖЕНИЕ\nНАЖАТЬ: ОТПРАВИТЬ");
        imageLabel.style.fontSize = 8;
        imageLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        imageLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        imageLabel.style.whiteSpace = WhiteSpace.Normal;
        imageLabel.style.color = ExpeditionRgb(129, 136, 146);
        image.Add(imageLabel);

        quickExpeditionImageButtons[location.Id] = image;
        quickExpeditionImageLabels[location.Id] = imageLabel;
        row.Add(image);

        VisualElement info = new VisualElement();
        info.style.flexGrow = 1;
        info.style.minWidth = 0;
        info.style.justifyContent = Justify.Center;

        Label distance = new Label(location.DistanceDays + " дня");
        distance.style.height = 16;
        distance.style.fontSize = 9;
        distance.style.color = ExpeditionRgb(180, 178, 169);
        info.Add(distance);

        Label threat = new Label("Угроза: " + location.Threat);
        threat.style.height = 16;
        threat.style.fontSize = 9;
        threat.style.color = ThreatColor(location.Threat);
        info.Add(threat);

        row.Add(info);
        return card;
    }

    private void ConfigureLargeExpeditionScreen()
    {
        ScrollView scroll =
            expeditionsScreen.Q<ScrollView>(className: "military-expedition-column");
        VisualElement panel =
            expeditionsScreen.Q<VisualElement>(className: "expedition-panel");

        if (scroll == null || panel == null)
            return;

        expeditionsScreen.style.width = Length.Percent(100);
        expeditionsScreen.style.height = Length.Percent(100);
        expeditionsScreen.style.minWidth = 0;
        expeditionsScreen.style.minHeight = 0;

        scroll.style.width = Length.Percent(100);
        scroll.style.height = Length.Percent(100);
        scroll.style.minWidth = 0;
        scroll.style.minHeight = 0;
        scroll.contentContainer.style.width = Length.Percent(100);
        scroll.contentContainer.style.minWidth = 0;

        panel.style.width = Length.Percent(100);
        panel.style.minWidth = 0;
        panel.style.marginLeft = 0;
        panel.style.marginRight = 0;
        panel.style.marginTop = 0;
        panel.style.marginBottom = 0;
        panel.style.paddingLeft = 14;
        panel.style.paddingRight = 14;
        panel.style.paddingTop = 12;
        panel.style.paddingBottom = 14;
        panel.style.backgroundColor = ExpeditionRgb(29, 33, 39);
        SetExpeditionBorder(panel, 1, ExpeditionRgb(68, 74, 83));
        SetExpeditionRadius(panel, 4);

        List<VisualElement> cards = new List<VisualElement>();
        panel.Query<VisualElement>(className: "location-card")
            .ForEach(card => cards.Add(card));

        bigExpeditionLocationGrid =
            panel.Q<VisualElement>("big-expedition-location-grid");

        if (bigExpeditionLocationGrid == null)
        {
            bigExpeditionLocationGrid = new VisualElement();
            bigExpeditionLocationGrid.name = "big-expedition-location-grid";
            bigExpeditionLocationGrid.style.width = Length.Percent(100);
            bigExpeditionLocationGrid.style.flexDirection = FlexDirection.Row;
            bigExpeditionLocationGrid.style.flexWrap = Wrap.Wrap;
            bigExpeditionLocationGrid.style.alignItems = Align.FlexStart;
            bigExpeditionLocationGrid.style.justifyContent = Justify.FlexStart;

            foreach (VisualElement card in cards)
            {
                card.RemoveFromHierarchy();
                bigExpeditionLocationGrid.Add(card);
            }

            panel.Add(bigExpeditionLocationGrid);
        }

        foreach (VisualElement card in cards)
            ConfigureLargeLocationCard(card);
    }

    private void ConfigureLargeLocationCard(VisualElement card)
    {
        Label nameLabel = card.Q<Label>(className: "location-name");

        if (nameLabel == null)
            return;

        LocationData location = FindLocationByName(nameLabel.text);

        if (location == null)
            return;

        card.style.width = Length.Percent(31.5f);
        card.style.minWidth = 280;
        card.style.flexGrow = 1;
        card.style.flexShrink = 1;
        card.style.marginLeft = 4;
        card.style.marginRight = 4;
        card.style.marginTop = 4;
        card.style.marginBottom = 4;
        card.style.paddingLeft = 10;
        card.style.paddingRight = 10;
        card.style.paddingTop = 9;
        card.style.paddingBottom = 9;
        card.style.backgroundColor = ExpeditionRgb(37, 42, 49);
        SetExpeditionBorder(card, 1, ExpeditionRgb(68, 75, 85));
        SetExpeditionRadius(card, 4);

        VisualElement image =
            card.Q<VisualElement>(className: "location-image-placeholder");
        Label imageLabel =
            card.Q<Label>(className: "location-image-placeholder-text");
        Button sendButton = card.Q<Button>(className: "send-button");

        if (sendButton != null)
            sendButton.style.display = DisplayStyle.None;

        if (image == null || imageLabel == null)
            return;

        image.style.width = Length.Percent(100);
        image.style.height = 150;
        image.style.minHeight = 150;
        image.style.maxHeight = 150;
        image.style.marginTop = 7;
        image.style.marginBottom = 8;
        image.style.alignItems = Align.Center;
        image.style.justifyContent = Justify.Center;
        image.style.backgroundColor = ExpeditionRgb(28, 32, 38);
        SetExpeditionBorder(image, 1, ExpeditionRgb(72, 80, 90));
        SetExpeditionRadius(image, 3);
        image.pickingMode = PickingMode.Position;

        string capturedLocationId = location.Id;
        image.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            OnExpeditionLocationImageClicked(capturedLocationId);
            evt.StopPropagation();
        });

        bigExpeditionImages[location.Id] = image;
        bigExpeditionImageLabels[location.Id] = imageLabel;
    }

    private void RebindPersistentExpeditionButton()
    {
        persistentCommanderExpeditionButton.clicked -=
            OnPersistentCommanderExpeditionClicked;
        persistentCommanderExpeditionButton.clicked +=
            ToggleQuickExpeditionPopup;
        persistentCommanderExpeditionButton.tooltip =
            "Быстрый список экспедиций";
    }

    private void ToggleQuickExpeditionPopup()
    {
        if (quickExpeditionPopup == null)
            return;

        bool opening = quickExpeditionPopup.style.display == DisplayStyle.None;

        if (!opening)
        {
            HideQuickExpeditionPopup();
            return;
        }

        RefreshExpeditionViewState();
        quickExpeditionPopup.style.display = DisplayStyle.Flex;
        quickExpeditionPopup.BringToFront();
        PositionQuickExpeditionPopup();
        quickExpeditionPopup.schedule
            .Execute(PositionQuickExpeditionPopup)
            .ExecuteLater(1);
    }

    private void HideQuickExpeditionPopup()
    {
        if (quickExpeditionPopup != null)
            quickExpeditionPopup.style.display = DisplayStyle.None;
    }

    private void PositionQuickExpeditionPopup()
    {
        if (quickExpeditionPopup == null ||
            persistentCommanderExpeditionButton == null ||
            interfaceRoot == null)
        {
            return;
        }

        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");

        if (screen == null)
            return;

        Rect screenBounds = screen.worldBound;
        Rect buttonBounds = persistentCommanderExpeditionButton.worldBound;

        float left = buttonBounds.xMax - screenBounds.x + 10f;
        float popupHeight = 322f;
        float bottomLimit = screenBounds.height - 90f;

        if (persistentCommanderGarrisonHost != null)
        {
            Rect garrisonBounds = persistentCommanderGarrisonHost.worldBound;
            bottomLimit = garrisonBounds.yMin - screenBounds.y - 7f;
        }

        float top = bottomLimit - popupHeight;
        top = Mathf.Clamp(top, 14f, Mathf.Max(14f, screenBounds.height - popupHeight - 86f));

        quickExpeditionPopup.style.left = left;
        quickExpeditionPopup.style.top = top;
    }

    private void OnExpeditionQuickOutsidePointerDown(PointerDownEvent evt)
    {
        if (quickExpeditionPopup == null ||
            quickExpeditionPopup.style.display == DisplayStyle.None)
        {
            return;
        }

        VisualElement target = evt.target as VisualElement;

        if (IsInsideElement(target, quickExpeditionPopup) ||
            IsInsideElement(target, persistentCommanderExpeditionButton))
        {
            return;
        }

        HideQuickExpeditionPopup();
    }

    private static bool IsInsideElement(
        VisualElement element,
        VisualElement ancestor)
    {
        if (element == null || ancestor == null)
            return false;

        VisualElement current = element;

        while (current != null)
        {
            if (current == ancestor)
                return true;

            current = current.parent;
        }

        return false;
    }

    private void OnExpeditionLocationImageClicked(string locationId)
    {
        if (isGameOver || gameState == null)
            return;

        if (!gameState.HasActiveExpedition)
        {
            TrySendExpedition(locationId);
            RefreshExpeditionViewState();
            return;
        }

        bool sameTarget =
            gameState.ActiveExpedition != null &&
            gameState.ActiveExpedition.LocationId == locationId;

        if (sameTarget && gameState.CanCancelExpeditionBeforeDayEnd)
        {
            string resultMessage;

            if (gameState.TryCancelExpeditionBeforeDayEnd(out resultMessage))
            {
                selectedFighterIds.Clear();
                AddReport(resultMessage);
                RefreshInterface();
            }

            RefreshExpeditionViewState();
        }
    }

    private void MaintainExpeditionViews()
    {
        if (!expeditionViewsInitialized)
            return;

        if (openedScreen.HasValue &&
            openedScreen.Value == MainScreen.Expeditions)
        {
            HideQuickExpeditionPopup();
        }

        RefreshExpeditionViewState();

        if (quickExpeditionPopup != null &&
            quickExpeditionPopup.style.display == DisplayStyle.Flex)
        {
            PositionQuickExpeditionPopup();
        }
    }

    private void RefreshExpeditionViewState()
    {
        if (gameState == null)
            return;

        RefreshPersistentCommanderExpeditionStatus();
        RefreshQuickExpeditionStatus();
        RefreshLargeExpeditionImages();
    }

    private void RefreshPersistentCommanderExpeditionStatus()
    {
        if (persistentCommanderStateLabel == null ||
            persistentCommanderTargetLabel == null)
        {
            return;
        }

        string stateText;
        string targetText;
        Color stateColor;
        LocationData location = null;

        if (!gameState.HasActiveExpedition)
        {
            stateText = "В ЗАМКЕ";
            targetText = "Цель: —";
            stateColor = ExpeditionRgb(163, 197, 174);
        }
        else
        {
            location = gameState.FindLocation(gameState.ActiveExpedition.LocationId);

            if (gameState.CanCancelExpeditionBeforeDayEnd)
            {
                stateText = "В ЗАМКЕ";
                targetText = "Приказ: " + (location != null ? location.Name : "—");
                stateColor = ExpeditionRgb(221, 181, 103);
            }
            else
            {
                switch (gameState.ActiveExpedition.Phase)
                {
                    case CommanderState.TravellingToLocation:
                        stateText = "В ПУТИ";
                        targetText = "Цель: " + (location != null ? location.Name : "—");
                        stateColor = ExpeditionRgb(205, 184, 117);
                        break;

                    case CommanderState.AtLocation:
                        stateText = "ДЕЙСТВУЕТ В ЛОКАЦИИ";
                        targetText = "Цель: " + (location != null ? location.Name : "—");
                        stateColor = ExpeditionRgb(150, 193, 164);
                        break;

                    case CommanderState.ReturningToCastle:
                        stateText = "ВОЗВРАЩАЕТСЯ";
                        targetText = "Цель: столица";
                        stateColor = ExpeditionRgb(185, 178, 149);
                        break;

                    default:
                        stateText = "В ЗАМКЕ";
                        targetText = "Цель: —";
                        stateColor = ExpeditionRgb(163, 197, 174);
                        break;
                }
            }
        }

        persistentCommanderStateLabel.text = stateText;
        persistentCommanderStateLabel.style.color = stateColor;
        persistentCommanderTargetLabel.text = targetText;
    }

    private void RefreshQuickExpeditionStatus()
    {
        if (quickExpeditionOrderLabel == null)
            return;

        if (!gameState.HasActiveExpedition)
        {
            quickExpeditionOrderLabel.text = "ПРИКАЗ НА СЕГОДНЯ: нет";
        }
        else
        {
            LocationData location =
                gameState.FindLocation(gameState.ActiveExpedition.LocationId);
            string locationName = location != null ? location.Name : "неизвестно";

            if (gameState.CanCancelExpeditionBeforeDayEnd)
                quickExpeditionOrderLabel.text = "ПРИКАЗ НА СЕГОДНЯ: " + locationName;
            else
                quickExpeditionOrderLabel.text =
                    "ЭКСПЕДИЦИЯ: " + GetShortExpeditionState() + " → " + locationName;
        }

        foreach (LocationData location in gameState.Locations)
        {
            Button button;
            Label label;

            if (!quickExpeditionImageButtons.TryGetValue(location.Id, out button) ||
                !quickExpeditionImageLabels.TryGetValue(location.Id, out label))
            {
                continue;
            }

            ApplyExpeditionImageState(location.Id, button, label);
        }
    }

    private void RefreshLargeExpeditionImages()
    {
        foreach (LocationData location in gameState.Locations)
        {
            VisualElement image;
            Label label;

            if (!bigExpeditionImages.TryGetValue(location.Id, out image) ||
                !bigExpeditionImageLabels.TryGetValue(location.Id, out label))
            {
                continue;
            }

            ApplyExpeditionImageState(location.Id, image, label);
        }
    }

    private void ApplyExpeditionImageState(
        string locationId,
        VisualElement image,
        Label label)
    {
        bool hasExpedition = gameState.HasActiveExpedition;
        bool isTarget = hasExpedition &&
            gameState.ActiveExpedition.LocationId == locationId;
        bool cancellable = isTarget && gameState.CanCancelExpeditionBeforeDayEnd;

        if (!hasExpedition)
        {
            image.SetEnabled(true);
            image.style.backgroundColor = ExpeditionRgb(28, 32, 38);
            SetExpeditionBorder(image, 1, ExpeditionRgb(73, 81, 91));
            label.text = "ИЗОБРАЖЕНИЕ ЛОКАЦИИ\nНАЖАТЬ: ОТПРАВИТЬ";
            label.style.color = ExpeditionRgb(134, 141, 151);
            return;
        }

        if (cancellable)
        {
            image.SetEnabled(true);
            image.style.backgroundColor = ExpeditionRgb(58, 50, 36);
            SetExpeditionBorder(image, 2, ExpeditionRgb(205, 163, 83));
            label.text = "ВЫБРАНО\nНАЖАТЬ ЕЩЁ РАЗ: ОТМЕНИТЬ";
            label.style.color = ExpeditionRgb(230, 194, 120);
            return;
        }

        if (isTarget)
        {
            image.SetEnabled(false);
            image.style.backgroundColor = ExpeditionRgb(38, 55, 47);
            SetExpeditionBorder(image, 2, ExpeditionRgb(92, 137, 108));
            label.text = GetTargetImageStateText();
            label.style.color = ExpeditionRgb(171, 205, 181);
            return;
        }

        image.SetEnabled(false);
        image.style.backgroundColor = ExpeditionRgb(31, 34, 39);
        SetExpeditionBorder(image, 1, ExpeditionRgb(55, 60, 68));
        label.text = "НЕДОСТУПНО\nИДЁТ ДРУГАЯ ЭКСПЕДИЦИЯ";
        label.style.color = ExpeditionRgb(102, 107, 114);
    }

    private string GetTargetImageStateText()
    {
        if (!gameState.HasActiveExpedition)
            return "ИЗОБРАЖЕНИЕ ЛОКАЦИИ";

        switch (gameState.ActiveExpedition.Phase)
        {
            case CommanderState.TravellingToLocation:
                return "В ПУТИ";
            case CommanderState.AtLocation:
                return "В ЛОКАЦИИ";
            case CommanderState.ReturningToCastle:
                return "ВОЗВРАЩАЕТСЯ";
            default:
                return "ЭКСПЕДИЦИЯ";
        }
    }

    private string GetShortExpeditionState()
    {
        if (!gameState.HasActiveExpedition)
            return "нет";

        switch (gameState.ActiveExpedition.Phase)
        {
            case CommanderState.TravellingToLocation:
                return "в пути";
            case CommanderState.AtLocation:
                return "в локации";
            case CommanderState.ReturningToCastle:
                return "возвращается";
            default:
                return "активна";
        }
    }

    private LocationData FindLocationByName(string locationName)
    {
        if (gameState == null || string.IsNullOrEmpty(locationName))
            return null;

        foreach (LocationData location in gameState.Locations)
        {
            if (location.Name == locationName)
                return location;
        }

        return null;
    }

    private static Color ThreatColor(string threat)
    {
        if (threat == "низкая")
            return ExpeditionRgb(133, 185, 147);

        if (threat == "средняя")
            return ExpeditionRgb(212, 178, 98);

        if (threat == "высокая")
            return ExpeditionRgb(205, 112, 102);

        return ExpeditionRgb(180, 178, 169);
    }

    private static Color ExpeditionRgb(byte r, byte g, byte b)
    {
        return new Color32(r, g, b, 255);
    }

    private static void SetExpeditionBorder(
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

    private static void SetExpeditionRadius(
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
