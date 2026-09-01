using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private VisualElement quickExpeditionPopup;
    private Label quickExpeditionOrderLabel;

    private readonly Dictionary<string, Button> quickExpeditionImageButtons =
        new Dictionary<string, Button>();
    private readonly Dictionary<string, VisualElement> quickExpeditionCards =
        new Dictionary<string, VisualElement>();
    private readonly Dictionary<string, Label> quickExpeditionImageLabels =
        new Dictionary<string, Label>();
    private readonly Dictionary<string, Label> quickExpeditionNameLabels =
        new Dictionary<string, Label>();
    private readonly Dictionary<string, Label> quickExpeditionDistanceLabels =
        new Dictionary<string, Label>();
    private readonly Dictionary<string, Label> quickExpeditionThreatLabels =
        new Dictionary<string, Label>();
    private readonly Dictionary<string, VisualElement> bigExpeditionImages =
        new Dictionary<string, VisualElement>();
    private readonly Dictionary<string, Label> bigExpeditionImageLabels =
        new Dictionary<string, Label>();

    private void InitializeExpeditionViewsUi()
    {
        CreateQuickExpeditionPopup();
        BindLargeExpeditionImages();

        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");
        if (screen != null)
        {
            screen.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (quickExpeditionPopup != null &&
                    quickExpeditionPopup.style.display == DisplayStyle.Flex)
                    PositionQuickExpeditionPopup();
            });
        }

        interfaceRoot.RegisterCallback<PointerDownEvent>(
            OnExpeditionQuickOutsidePointerDown,
            TrickleDown.TrickleDown);
        RefreshExpeditionViewState();
    }

    private void CreateQuickExpeditionPopup()
    {
        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");
        if (screen == null)
            return;

        quickExpeditionPopup = screen.Q<VisualElement>("quick-expedition-popup");
        if (quickExpeditionPopup != null)
            return;

        quickExpeditionPopup = new VisualElement();
        quickExpeditionPopup.name = "quick-expedition-popup";
        quickExpeditionPopup.AddToClassList("quick-expedition-popup");
        quickExpeditionPopup.style.display = DisplayStyle.None;

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
        quickExpeditionCards[location.Id] = card;
        card.style.display = location.IsVisibleOnMap
            ? DisplayStyle.Flex
            : DisplayStyle.None;
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

        Label name = new Label(location.TravelTargetName);
        name.style.height = 16;
        name.style.fontSize = 10;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.color = ExpeditionRgb(215, 210, 197);
        card.Add(name);
        quickExpeditionNameLabels[location.Id] = name;

        VisualElement row = new VisualElement();
        row.style.flexGrow = 1;
        row.style.minHeight = 0;
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Stretch;
        card.Add(row);

        Button image =
            new Button(() => OnExpeditionLocationImageClicked(location.Id));
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

        Label distance = new Label(
            ContinuousExpeditionCommands.FormatHours(
                location.TravelHoursFromCapital));
        distance.style.height = 16;
        distance.style.fontSize = 9;
        distance.style.color = ExpeditionRgb(180, 178, 169);
        info.Add(distance);
        quickExpeditionDistanceLabels[location.Id] = distance;

        Label threat = new Label(
            location.IsDiscovered
                ? "Угроза: " + location.Threat
                : "Угроза: неизвестна");
        threat.style.height = 16;
        threat.style.fontSize = 9;
        threat.style.color = location.IsDiscovered
            ? ThreatColor(location.Threat)
            : ExpeditionRgb(129, 136, 146);
        info.Add(threat);
        quickExpeditionThreatLabels[location.Id] = threat;
        row.Add(info);
        return card;
    }

    private void BindLargeExpeditionImages()
    {
        bigExpeditionImages.Clear();
        bigExpeditionImageLabels.Clear();
        List<VisualElement> cards = new List<VisualElement>();
        expeditionsScreen.Query<VisualElement>(className: "location-card")
            .ForEach(card => cards.Add(card));

        foreach (VisualElement card in cards)
        {
            Label nameLabel = card.Q<Label>(className: "location-name");
            if (nameLabel == null)
                continue;

            LocationData location = FindLocationByName(nameLabel.text);
            if (location == null)
                continue;

            VisualElement image =
                card.Q<VisualElement>(className: "location-image-placeholder");
            Label imageLabel =
                card.Q<Label>(className: "location-image-placeholder-text");
            if (image == null || imageLabel == null)
                continue;

            string capturedId = location.Id;
            image.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                OnExpeditionLocationImageClicked(capturedId);
                evt.StopPropagation();
            });

            bigExpeditionImages[location.Id] = image;
            bigExpeditionImageLabels[location.Id] = imageLabel;
        }
    }

    private void ToggleQuickExpeditionPopup()
    {
        if (quickExpeditionPopup == null || isGameOver)
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
            return;

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
        top = Mathf.Clamp(
            top,
            14f,
            Mathf.Max(14f, screenBounds.height - popupHeight - 86f));
        quickExpeditionPopup.style.left = left;
        quickExpeditionPopup.style.top = top;
    }

    private void OnExpeditionQuickOutsidePointerDown(PointerDownEvent evt)
    {
        if (quickExpeditionPopup == null ||
            quickExpeditionPopup.style.display == DisplayStyle.None)
            return;

        VisualElement target = evt.target as VisualElement;
        if (IsInsideElement(target, quickExpeditionPopup) ||
            IsInsideElement(target, persistentCommanderExpeditionButton))
            return;

        HideQuickExpeditionPopup();
    }

    private static bool IsInsideElement(
        VisualElement element,
        VisualElement ancestor)
    {
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
            TrySendExpeditionFromStableUi(locationId);
            return;
        }

        bool sameTarget =
            gameState.ActiveExpedition != null &&
            gameState.ActiveExpedition.LocationId == locationId;

        if (sameTarget && gameState.CanCancelPreparedExpedition)
            OnStableExpeditionActionClicked();
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
            return;

        if (!gameState.HasActiveExpedition)
        {
            persistentCommanderStateLabel.text = "В ЗАМКЕ";
            persistentCommanderStateLabel.style.color = ExpeditionRgb(163, 197, 174);
            persistentCommanderTargetLabel.text = "Цель: —";
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        LocationData location = gameState.FindLocation(expedition.LocationId);
        string locationName = location != null
            ? location.TravelTargetName
            : expedition.IsScoutingTarget ? "точка разведки" : "—";

        if (gameState.CanCancelPreparedExpedition)
        {
            persistentCommanderStateLabel.text = "В ЗАМКЕ";
            persistentCommanderStateLabel.style.color = ExpeditionRgb(221, 181, 103);
            persistentCommanderTargetLabel.text = "Приказ: " + locationName;
            return;
        }

        switch (expedition.Phase)
        {
            case CommanderState.TravellingToLocation:
                persistentCommanderStateLabel.text = "В ПУТИ";
                persistentCommanderStateLabel.style.color = ExpeditionRgb(205, 184, 117);
                persistentCommanderTargetLabel.text = "Цель: " + locationName;
                break;
            case CommanderState.AtLocation:
                persistentCommanderStateLabel.text = "ДЕЙСТВУЕТ В ЛОКАЦИИ";
                persistentCommanderStateLabel.style.color = ExpeditionRgb(150, 193, 164);
                persistentCommanderTargetLabel.text = "Цель: " + locationName;
                break;
            case CommanderState.ReturningToCastle:
                persistentCommanderStateLabel.text = "ВОЗВРАЩАЕТСЯ";
                persistentCommanderStateLabel.style.color = ExpeditionRgb(185, 178, 149);
                persistentCommanderTargetLabel.text = "Цель: столица";
                break;
            default:
                persistentCommanderStateLabel.text = "В ЗАМКЕ";
                persistentCommanderTargetLabel.text = "Цель: —";
                break;
        }
    }

    private void RefreshQuickExpeditionStatus()
    {
        if (quickExpeditionOrderLabel == null)
            return;

        if (!gameState.HasActiveExpedition)
            quickExpeditionOrderLabel.text = "ПРИКАЗ НА СЕГОДНЯ: нет";
        else
        {
            LocationData location =
                gameState.FindLocation(gameState.ActiveExpedition.LocationId);
            string locationName = location != null
                ? location.TravelTargetName
                : gameState.ActiveExpedition.IsScoutingTarget
                    ? "точка разведки"
                    : "неизвестно";
            quickExpeditionOrderLabel.text = gameState.CanCancelPreparedExpedition
                ? "ПРИКАЗ НА СЕГОДНЯ: " + locationName
                : "ЭКСПЕДИЦИЯ: " + GetShortExpeditionState() + " → " + locationName;
        }

        foreach (LocationData location in gameState.Locations)
        {
            Button button;
            Label label;
            Label name;
            Label distance;
            Label threat;
            VisualElement card;

            if (quickExpeditionCards.TryGetValue(location.Id, out card))
                card.style.display = location.IsVisibleOnMap
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;

            if (quickExpeditionNameLabels.TryGetValue(location.Id, out name))
                name.text = location.TravelTargetName;

            if (quickExpeditionDistanceLabels.TryGetValue(
                    location.Id,
                    out distance))
            {
                distance.text = ContinuousExpeditionCommands.FormatHours(
                    location.TravelHoursFromCapital);
            }

            if (quickExpeditionThreatLabels.TryGetValue(
                    location.Id,
                    out threat))
            {
                threat.text = location.IsDiscovered
                    ? "Угроза: " + location.Threat
                    : "Угроза: неизвестна";
                threat.style.color = location.IsDiscovered
                    ? ThreatColor(location.Threat)
                    : ExpeditionRgb(129, 136, 146);
            }

            if (quickExpeditionImageButtons.TryGetValue(location.Id, out button) &&
                quickExpeditionImageLabels.TryGetValue(location.Id, out label))
                ApplyExpeditionImageState(location, button, label);
        }
    }

    private void RefreshLargeExpeditionImages()
    {
        foreach (LocationData location in gameState.Locations)
        {
            VisualElement image;
            Label label;
            if (bigExpeditionImages.TryGetValue(location.Id, out image) &&
                bigExpeditionImageLabels.TryGetValue(location.Id, out label))
                ApplyExpeditionImageState(location, image, label);
        }
    }

    private void ApplyExpeditionImageState(
        LocationData location,
        VisualElement image,
        Label label)
    {
        string locationId = location.Id;
        bool hasExpedition = gameState.HasActiveExpedition;
        bool isTarget = hasExpedition &&
            gameState.ActiveExpedition.LocationId == locationId;
        bool cancellable = isTarget && gameState.CanCancelPreparedExpedition;

        if (!hasExpedition)
        {
            image.SetEnabled(true);
            image.style.backgroundColor = ExpeditionRgb(28, 32, 38);
            SetExpeditionBorder(image, 1, ExpeditionRgb(73, 81, 91));
            label.text = location.IsDiscovered
                ? "ИЗОБРАЖЕНИЕ ЛОКАЦИИ\nНАЖАТЬ: ОТПРАВИТЬ"
                : "НЕИЗВЕДАННАЯ ОБЛАСТЬ\nНАЖАТЬ: ОТПРАВИТЬ";
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
            case CommanderState.TravellingToLocation: return "В ПУТИ";
            case CommanderState.AtLocation: return "В ЛОКАЦИИ";
            case CommanderState.ReturningToCastle: return "ВОЗВРАЩАЕТСЯ";
            default: return "ЭКСПЕДИЦИЯ";
        }
    }

    private string GetShortExpeditionState()
    {
        if (!gameState.HasActiveExpedition)
            return "нет";
        switch (gameState.ActiveExpedition.Phase)
        {
            case CommanderState.TravellingToLocation: return "в пути";
            case CommanderState.AtLocation: return "в локации";
            case CommanderState.ReturningToCastle: return "возвращается";
            default: return "активна";
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
        if (threat == "низкая") return ExpeditionRgb(133, 185, 147);
        if (threat == "средняя") return ExpeditionRgb(212, 178, 98);
        if (threat == "высокая") return ExpeditionRgb(205, 112, 102);
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
