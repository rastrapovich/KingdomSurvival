using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private sealed class QuickExpeditionCardView
    {
        public LocationData Location;
        public VisualElement Card;
        public Label NameLabel;
        public VisualElement ImageBox;
        public Label ImageLabel;
        public Label DistanceLabel;
        public Label ThreatLabel;
        public Button ActionButton;
    }

    private VisualElement quickExpeditionPopup;
    private Label quickExpeditionOrderLabel;
    private readonly Dictionary<string, QuickExpeditionCardView> quickExpeditionCards =
        new Dictionary<string, QuickExpeditionCardView>();
    private VisualTreeAsset expeditionLocationCardTemplate;

    private void InitializeExpeditionViewsUi()
    {
        BindQuickExpeditionPopup();

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

    private void BindQuickExpeditionPopup()
    {
        VisualElement screen = interfaceRoot.Q<VisualElement>("screen");
        if (screen == null)
            return;

        quickExpeditionPopup = screen.Q<VisualElement>("quick-expedition-popup");
        quickExpeditionOrderLabel = screen.Q<Label>("quick-expedition-order-label");
        if (quickExpeditionPopup == null || quickExpeditionOrderLabel == null)
            return;

        VisualTreeAsset template = LoadExpeditionLocationCardTemplate();
        if (template == null)
            return;

        foreach (LocationData location in gameState.Locations)
        {
            TemplateContainer instance = template.Instantiate();

            VisualElement card = instance.Q<VisualElement>("quick-expedition-card");
            Label name = instance.Q<Label>("quick-expedition-card-name");
            VisualElement image = instance.Q<VisualElement>("quick-expedition-card-image");
            Label imageLabel = instance.Q<Label>("quick-expedition-card-image-label");
            Label distance = instance.Q<Label>("quick-expedition-card-distance");
            Label threat = instance.Q<Label>("quick-expedition-card-threat");
            Button action = instance.Q<Button>("quick-expedition-card-action");

            string capturedId = location.Id;
            if (action != null)
                action.clicked += () => OnQuickLocationActionClicked(capturedId);

            quickExpeditionPopup.Add(instance);
            quickExpeditionCards[location.Id] = new QuickExpeditionCardView
            {
                Location = location,
                Card = card,
                NameLabel = name,
                ImageBox = image,
                ImageLabel = imageLabel,
                DistanceLabel = distance,
                ThreatLabel = threat,
                ActionButton = action
            };
        }
    }

    // ------------------------------------------------------------------
    // Шаблоны (Assets/_Project/UI/Templates) — раздел 19 UI_ARCHITECTURE.md.
    // ------------------------------------------------------------------

    private VisualTreeAsset LoadExpeditionLocationCardTemplate()
    {
        if (expeditionLocationCardTemplate == null)
            expeditionLocationCardTemplate = Resources.Load<VisualTreeAsset>("Templates/ExpeditionLocationCard");
        return expeditionLocationCardTemplate;
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

    private void RefreshExpeditionViewState()
    {
        if (gameState == null)
            return;
        RefreshPersistentCommanderExpeditionStatus();
        RefreshQuickExpeditionStatus();
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

        bool hasExpedition = gameState.HasActiveExpedition;
        bool blockedByDecision = gameState.HasPendingExpeditionDecision;
        bool blockedByResearch =
            hasExpedition && gameState.ActiveExpedition.IsLocationResearchInProgress;

        foreach (LocationData location in gameState.Locations)
        {
            QuickExpeditionCardView view;
            if (!quickExpeditionCards.TryGetValue(location.Id, out view))
                continue;

            if (view.Card != null)
                view.Card.style.display = location.IsVisibleOnMap
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;

            if (view.NameLabel != null)
                view.NameLabel.text = location.TravelTargetName;

            if (view.DistanceLabel != null)
                view.DistanceLabel.text = ContinuousExpeditionCommands.FormatHours(
                    location.TravelHoursFromCapital);

            if (view.ThreatLabel != null)
            {
                view.ThreatLabel.text = location.IsDiscovered
                    ? "Угроза: " + location.Threat
                    : "Угроза: неизвестна";
                view.ThreatLabel.style.color = location.IsDiscovered
                    ? ThreatColor(location.Threat)
                    : ExpeditionRgb(129, 136, 146);
            }

            ApplyExpeditionImageState(view);
            ApplyQuickLocationActionButtonState(
                view, hasExpedition, blockedByDecision, blockedByResearch);
        }
    }

    // Раньше 4 состояния картинки (доступно/выбрано-отменяемо/цель/
    // недоступно) выражались прямыми style.* поверх Button — теперь это
    // модификаторы класса из Expedition.uss на некликабельном VisualElement
    // (UI-M06: клик по картинке заменён отдельной кнопкой-действием, см.
    // ApplyQuickLocationActionButtonState). "Доступно" — базовый вид
    // .quick-expedition-card-image, отдельного модификатора не требует.
    private void ApplyExpeditionImageState(QuickExpeditionCardView view)
    {
        if (view.ImageBox == null)
            return;

        LocationData location = view.Location;
        bool hasExpedition = gameState.HasActiveExpedition;
        bool isTarget = hasExpedition &&
            gameState.ActiveExpedition.LocationId == location.Id;
        bool cancellable = isTarget && gameState.CanCancelPreparedExpedition;

        view.ImageBox.RemoveFromClassList("quick-expedition-card-image--selected-cancellable");
        view.ImageBox.RemoveFromClassList("quick-expedition-card-image--active-target");
        view.ImageBox.RemoveFromClassList("quick-expedition-card-image--unavailable");

        if (hasExpedition)
        {
            if (cancellable)
                view.ImageBox.AddToClassList("quick-expedition-card-image--selected-cancellable");
            else if (isTarget)
                view.ImageBox.AddToClassList("quick-expedition-card-image--active-target");
            else
                view.ImageBox.AddToClassList("quick-expedition-card-image--unavailable");
        }

        if (view.ImageLabel != null)
        {
            view.ImageLabel.text = location.IsDiscovered
                ? "ИЗОБРАЖЕНИЕ\nЛОКАЦИИ"
                : "НЕИЗВЕДАННАЯ\nОБЛАСТЬ";
        }
    }

    private void ApplyQuickLocationActionButtonState(
        QuickExpeditionCardView view,
        bool hasExpedition,
        bool blockedByDecision,
        bool blockedByResearch)
    {
        Button button = view.ActionButton;
        if (button == null)
            return;

        if (isGameOver)
        {
            button.text = "НЕДОСТУПНО";
            button.SetEnabled(false);
            return;
        }

        if (!hasExpedition)
        {
            button.text = "ОТПРАВИТЬ";
            button.tooltip = "Отправить героя (и выбранных бойцов) к этой локации.";
            button.SetEnabled(true);
            return;
        }

        if (blockedByDecision || blockedByResearch)
        {
            button.text = "НЕДОСТУПНО";
            button.tooltip = blockedByDecision
                ? "Сначала примите обязательное решение."
                : "Нельзя менять маршрут во время исследования.";
            button.SetEnabled(false);
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        bool locationIsCurrentTarget =
            expedition.Phase != CommanderState.ReturningToCastle &&
            !expedition.IsScoutingTarget &&
            expedition.LocationId == view.Location.Id;

        if (locationIsCurrentTarget)
        {
            button.text = "ТЕКУЩАЯ ЦЕЛЬ";
            button.tooltip = "Армия уже направляется сюда или находится здесь.";
            button.SetEnabled(false);
            return;
        }

        button.text = gameState.CanCancelPreparedExpedition
            ? "ИЗМЕНИТЬ ЦЕЛЬ"
            : "ИЗМЕНИТЬ МАРШРУТ";
        button.tooltip = "Построить новый прямой маршрут от текущей позиции армии.";
        button.SetEnabled(true);
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
        RefreshQuickExpeditionStatus();
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
}
