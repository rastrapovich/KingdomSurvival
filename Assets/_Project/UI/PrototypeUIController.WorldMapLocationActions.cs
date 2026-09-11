using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool worldMapLocationActionsInitialized;
    private string worldMapLocationCardLocationId;
    private VisualElement worldMapLocationCardAnchorNode;
    private Label worldMapLocationCardPresenceLabel;

    // P10-LocInt: старая кнопка "ИССЛЕДОВАТЬ" карточки карты переиспользована
    // как "ВОЙТИ В ЛОКАЦИЮ" (раздел "Новая кнопка ВОЙТИ В ЛОКАЦИЮ" инструкции)
    // — прямой запуск исследования из карточки убран, чтобы не оставлять два
    // разных интерфейсных пути для одной и той же механики. Element name в
    // UXML не переименован (world-map-location-inspection-research-button),
    // чтобы не задевать существующие UI-тесты, проверяющие его наличие.
    private Button worldMapLocationCardEnterButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeWorldMapLocationActionsRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeWorldMapLocationActions)
            .ExecuteLater(70);
    }

    private void TryInitializeWorldMapLocationActions()
    {
        if (worldMapLocationActionsInitialized)
            return;

        if (interfaceRoot == null || gameState == null || worldMap == null)
        {
            ScheduleWorldMapLocationActionsRetry();
            return;
        }

        VisualElement markers =
            interfaceRoot.Q<VisualElement>("world-map-markers");

        if (markers == null)
        {
            ScheduleWorldMapLocationActionsRetry();
            return;
        }

        if (worldMapLocationCard == null ||
            worldMapLocationCardCloseButton == null)
        {
            ScheduleWorldMapLocationActionsRetry();
            return;
        }

        BindWorldMapLocationActionElements();

        markers.RegisterCallback<PointerUpEvent>(
            OnWorldMapLocationActionPointerUp,
            TrickleDown.TrickleDown);
        worldMap.RegisterCallback<PointerDownEvent>(
            OnWorldMapLocationCardDismissPointerDown,
            TrickleDown.TrickleDown);
        interfaceRoot.RegisterCallback<KeyDownEvent>(
            OnWorldMapLocationCardKeyDown);

        worldMap.schedule
            .Execute(RefreshOpenWorldMapLocationCard)
            .Every(150);

        worldMapLocationActionsInitialized = true;
    }

    private void ScheduleWorldMapLocationActionsRetry()
    {
        UIDocument document = GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(TryInitializeWorldMapLocationActions)
            .ExecuteLater(40);
    }

    private void BindWorldMapLocationActionElements()
    {
        if (worldMapLocationCard == null)
            return;

        worldMapLocationCardPresenceLabel =
            worldMapLocationCard.Q<Label>("world-map-location-inspection-presence");
        worldMapLocationCardEnterButton =
            worldMapLocationCard.Q<Button>("world-map-location-inspection-research-button");

        if (worldMapLocationCardEnterButton != null)
        {
            worldMapLocationCardEnterButton.text = "ВОЙТИ В ЛОКАЦИЮ";
            worldMapLocationCardEnterButton.clicked += OnWorldMapLocationCardEnterClicked;
        }
    }

    private void OnWorldMapLocationActionPointerUp(PointerUpEvent evt)
    {
        if (evt.button != 1 || gameState == null)
            return;

        VisualElement node =
            FindWorldMapLocationNode(evt.target as VisualElement);

        if (node == null || string.IsNullOrEmpty(node.name))
            return;

        const string prefix = "world-map-node-";
        if (!node.name.StartsWith(prefix))
            return;

        string locationId = node.name.Substring(prefix.Length);
        LocationData location = gameState.FindLocation(locationId);

        if (location == null ||
            location.IsWaypoint ||
            !location.IsVisibleOnMap)
        {
            return;
        }

        worldMapLocationCardLocationId = location.Id;
        worldMapLocationCardAnchorNode = node;

        RefreshOpenWorldMapLocationCard();

        worldMap.schedule
            .Execute(RefreshOpenWorldMapLocationCard)
            .ExecuteLater(1);
    }

    private void OnWorldMapLocationCardDismissPointerDown(
        PointerDownEvent evt)
    {
        if (worldMapLocationCard == null ||
            worldMapLocationCard.resolvedStyle.display != DisplayStyle.Flex)
        {
            return;
        }

        VisualElement target = evt.target as VisualElement;
        if (IsElementInsideWorldMapLocationCard(target))
            return;

        HideAnchoredWorldMapLocationCard();
    }

    private void OnWorldMapLocationCardKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Escape)
            return;

        if (worldMapLocationCard == null ||
            worldMapLocationCard.resolvedStyle.display != DisplayStyle.Flex)
        {
            return;
        }

        HideAnchoredWorldMapLocationCard();
        evt.StopPropagation();
    }

    private bool IsElementInsideWorldMapLocationCard(VisualElement element)
    {
        VisualElement current = element;

        while (current != null)
        {
            if (current == worldMapLocationCard)
                return true;

            current = current.parent;
        }

        return false;
    }

    private void HideAnchoredWorldMapLocationCard()
    {
        worldMapLocationCardLocationId = null;
        worldMapLocationCardAnchorNode = null;
        HideWorldMapLocationCard();
    }

    private void RefreshOpenWorldMapLocationCard()
    {
        if (gameState == null ||
            worldMapLocationCard == null ||
            worldMapLocationCard.resolvedStyle.display != DisplayStyle.Flex ||
            string.IsNullOrEmpty(worldMapLocationCardLocationId))
        {
            return;
        }

        LocationData location =
            gameState.FindLocation(worldMapLocationCardLocationId);

        if (location == null ||
            location.IsWaypoint ||
            !location.IsVisibleOnMap)
        {
            HideAnchoredWorldMapLocationCard();
            return;
        }

        RefreshWorldMapLocationCardActionState(location);
        PositionWorldMapLocationCardAboveAnchor();
    }

    private void RefreshWorldMapLocationCardActionState(
        LocationData location)
    {
        if (worldMapLocationCardEnterButton == null ||
            worldMapLocationCardPresenceLabel == null)
        {
            return;
        }

        bool armyHere = IsArmyInsideWorldMapLocation(location);

        worldMapLocationCardPresenceLabel.style.display =
            armyHere ? DisplayStyle.Flex : DisplayStyle.None;

        worldMapLocationCardEnterButton.text = "ВОЙТИ В ЛОКАЦИЮ";

        if (!armyHere)
        {
            worldMapLocationCardEnterButton.SetEnabled(false);
            worldMapLocationCardEnterButton.tooltip =
                "Отряд должен находиться внутри этой локации.";
            return;
        }

        if (IsNarrativeDialogueActive || IsLocationInteractionActive)
        {
            worldMapLocationCardEnterButton.SetEnabled(false);
            worldMapLocationCardEnterButton.tooltip =
                "Окно уже открыто.";
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        if (expedition.HasTimedActivity)
        {
            worldMapLocationCardEnterButton.SetEnabled(false);
            worldMapLocationCardEnterButton.tooltip =
                "Сначала завершите текущее исследование.";
            return;
        }

        if (gameState.HasPendingExpeditionDecision)
        {
            worldMapLocationCardEnterButton.SetEnabled(false);
            worldMapLocationCardEnterButton.tooltip =
                "Сначала примите обязательное решение.";
            return;
        }

        worldMapLocationCardEnterButton.SetEnabled(true);
        worldMapLocationCardEnterButton.tooltip =
            "Открыть окно локации.";
    }

    private bool IsArmyInsideWorldMapLocation(LocationData location)
    {
        return
            location != null &&
            gameState != null &&
            gameState.HasActiveExpedition &&
            gameState.ActiveExpedition.Phase == CommanderState.AtLocation &&
            gameState.ActiveExpedition.LocationId == location.Id;
    }

    private void OnWorldMapLocationCardEnterClicked()
    {
        if (gameState == null ||
            string.IsNullOrEmpty(worldMapLocationCardLocationId))
        {
            return;
        }

        LocationData location =
            gameState.FindLocation(worldMapLocationCardLocationId);

        if (location == null || !IsArmyInsideWorldMapLocation(location))
        {
            RefreshOpenWorldMapLocationCard();
            return;
        }

        // Тонкий обработчик: вся содержательная логика открытия — в
        // TryOpenLocationInteraction (PrototypeUIController.LocationInteraction.cs),
        // тот же метод, что и автоматическое открытие при прибытии.
        HideAnchoredWorldMapLocationCard();
        TryOpenLocationInteraction(location.Id);
    }

    private void PositionWorldMapLocationCardAboveAnchor()
    {
        if (worldMapLocationCard == null ||
            worldMapLocationCardAnchorNode == null ||
            worldMap == null)
        {
            return;
        }

        float mapWidth = worldMap.resolvedStyle.width;
        float mapHeight = worldMap.resolvedStyle.height;
        float cardWidth = worldMapLocationCard.resolvedStyle.width;
        float cardHeight = worldMapLocationCard.resolvedStyle.height;

        if (float.IsNaN(mapWidth) || mapWidth <= 0f ||
            float.IsNaN(mapHeight) || mapHeight <= 0f)
        {
            return;
        }

        if (float.IsNaN(cardWidth) || cardWidth <= 0f)
            cardWidth = 300f;
        if (float.IsNaN(cardHeight) || cardHeight <= 0f)
            cardHeight = 180f;

        Vector2 anchorWorld =
            worldMapLocationCardAnchorNode.worldBound.center;
        Vector2 anchorLocal = worldMap.WorldToLocal(anchorWorld);

        float markerHalfHeight =
            worldMapLocationCardAnchorNode.worldBound.height * 0.5f;
        const float edgePadding = 8f;
        const float markerGap = 10f;

        float left = anchorLocal.x - cardWidth * 0.5f;
        float top =
            anchorLocal.y - markerHalfHeight - cardHeight - markerGap;

        float maxLeft = Math.Max(edgePadding, mapWidth - cardWidth - edgePadding);
        float maxTop = Math.Max(edgePadding, mapHeight - cardHeight - edgePadding);

        left = Mathf.Clamp(left, edgePadding, maxLeft);
        top = Mathf.Clamp(top, edgePadding, maxTop);

        worldMapLocationCard.style.left = left;
        worldMapLocationCard.style.top = top;
        worldMapLocationCard.style.right =
            new StyleLength(StyleKeyword.Auto);
        worldMapLocationCard.style.bottom =
            new StyleLength(StyleKeyword.Auto);
        worldMapLocationCard.BringToFront();
    }
}
