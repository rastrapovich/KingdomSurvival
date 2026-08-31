using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool worldMapLocationActionsInitialized;
    private string worldMapLocationCardLocationId;
    private VisualElement worldMapLocationCardAnchorNode;
    private Label worldMapLocationCardPresenceLabel;
    private Button worldMapLocationCardResearchButton;

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

        EnsureWorldMapLocationCard();

        if (worldMapLocationCard == null ||
            worldMapLocationCardCloseButton == null)
        {
            ScheduleWorldMapLocationActionsRetry();
            return;
        }

        EnsureWorldMapLocationActionElements();

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

    private void EnsureWorldMapLocationActionElements()
    {
        if (worldMapLocationCardPresenceLabel != null ||
            worldMapLocationCard == null)
        {
            return;
        }

        worldMapLocationCardPresenceLabel = new Label
        {
            text = "АРМИЯ НАХОДИТСЯ ЗДЕСЬ"
        };
        worldMapLocationCardPresenceLabel.style.color =
            (Color)new Color32(155, 207, 155, 255);
        worldMapLocationCardPresenceLabel.style.fontSize = 11f;
        worldMapLocationCardPresenceLabel.style.unityFontStyleAndWeight =
            FontStyle.Bold;
        worldMapLocationCardPresenceLabel.style.marginBottom = 8f;
        worldMapLocationCardPresenceLabel.style.display = DisplayStyle.None;

        worldMapLocationCardResearchButton =
            new Button(OnWorldMapLocationCardResearchClicked)
            {
                text = "ИССЛЕДОВАТЬ"
            };
        worldMapLocationCardResearchButton.style.height = 34f;
        worldMapLocationCardResearchButton.style.marginBottom = 6f;
        worldMapLocationCardResearchButton.style.unityFontStyleAndWeight =
            FontStyle.Bold;

        worldMapLocationCardCloseButton.RemoveFromHierarchy();
        worldMapLocationCard.Add(worldMapLocationCardPresenceLabel);
        worldMapLocationCard.Add(worldMapLocationCardResearchButton);
        worldMapLocationCard.Add(worldMapLocationCardCloseButton);

        worldMapLocationCard.style.right =
            new StyleLength(StyleKeyword.Auto);
        worldMapLocationCard.style.bottom =
            new StyleLength(StyleKeyword.Auto);
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
        if (worldMapLocationCardResearchButton == null ||
            worldMapLocationCardPresenceLabel == null)
        {
            return;
        }

        bool armyHere = IsArmyInsideWorldMapLocation(location);

        worldMapLocationCardPresenceLabel.style.display =
            armyHere ? DisplayStyle.Flex : DisplayStyle.None;

        if (!armyHere)
        {
            worldMapLocationCardResearchButton.text = "ИССЛЕДОВАТЬ";
            worldMapLocationCardResearchButton.SetEnabled(false);
            worldMapLocationCardResearchButton.tooltip =
                "Армия должна находиться внутри этой локации.";
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;

        if (location.IsExplored)
        {
            worldMapLocationCardResearchButton.text = "ИССЛЕДОВАНО";
            worldMapLocationCardResearchButton.SetEnabled(false);
            worldMapLocationCardResearchButton.tooltip =
                "Локация уже исследована.";
            return;
        }

        if (expedition.IsExplorationInProgress)
        {
            worldMapLocationCardResearchButton.text = "ИССЛЕДОВАНИЕ...";
            worldMapLocationCardResearchButton.SetEnabled(false);
            worldMapLocationCardResearchButton.tooltip =
                "Исследование уже идёт.";
            return;
        }

        if (location.ExplorationDays <= 0)
        {
            worldMapLocationCardResearchButton.text = "ИССЛЕДОВАТЬ";
            worldMapLocationCardResearchButton.SetEnabled(false);
            worldMapLocationCardResearchButton.tooltip =
                "Исследование этой локации пока не реализовано.";
            return;
        }

        if (gameState.HasPendingExpeditionDecision)
        {
            worldMapLocationCardResearchButton.text = "ИССЛЕДОВАТЬ";
            worldMapLocationCardResearchButton.SetEnabled(false);
            worldMapLocationCardResearchButton.tooltip =
                "Сначала примите обязательное решение.";
            return;
        }

        if (gameState.ArmySupply < gameState.ExpeditionSupplyConsumption)
        {
            worldMapLocationCardResearchButton.text = "ИССЛЕДОВАТЬ";
            worldMapLocationCardResearchButton.SetEnabled(false);
            worldMapLocationCardResearchButton.tooltip =
                "Не хватает снабжения для начала исследования.";
            return;
        }

        worldMapLocationCardResearchButton.text = "ИССЛЕДОВАТЬ";
        worldMapLocationCardResearchButton.SetEnabled(
            gameState.CanResearchActiveLocation);
        worldMapLocationCardResearchButton.tooltip =
            gameState.CanResearchActiveLocation
                ? "Начать исследование этой локации."
                : "Исследование сейчас недоступно.";
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

    private void OnWorldMapLocationCardResearchClicked()
    {
        if (gameState == null ||
            string.IsNullOrEmpty(worldMapLocationCardLocationId))
        {
            return;
        }

        LocationData location =
            gameState.FindLocation(worldMapLocationCardLocationId);

        if (location == null ||
            !IsArmyInsideWorldMapLocation(location) ||
            !gameState.CanResearchActiveLocation)
        {
            RefreshOpenWorldMapLocationCard();
            return;
        }

        OnContinuousResearchClicked();
        RefreshOpenWorldMapLocationCard();
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
