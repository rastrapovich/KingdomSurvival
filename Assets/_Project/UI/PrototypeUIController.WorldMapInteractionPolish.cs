using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool worldMapInteractionPolishInitialized;
    private VisualElement worldMapLocationCard;
    private Label worldMapLocationCardTitle;
    private Label worldMapLocationCardDetails;
    private Button worldMapLocationCardCloseButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeWorldMapInteractionPolishRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeWorldMapInteractionPolish)
            .ExecuteLater(30);
    }

    private void TryInitializeWorldMapInteractionPolish()
    {
        if (worldMapInteractionPolishInitialized)
            return;

        if (interfaceRoot == null || gameState == null || worldMap == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInitializeWorldMapInteractionPolish)
                    .ExecuteLater(30);
            }
            return;
        }

        VisualElement markers =
            interfaceRoot.Q<VisualElement>("world-map-markers");

        if (markers == null)
            return;

        markers.RegisterCallback<PointerDownEvent>(
            OnWorldMapMarkerPointerDown,
            TrickleDown.TrickleDown);

        RegisterMapLayoutRefreshButton("nav-capital-button");
        RegisterMapLayoutRefreshButton("nav-expeditions-button");
        RegisterMapLayoutRefreshButton("world-map-locations-button");

        BindWorldMapLocationCard();
        worldMapInteractionPolishInitialized = true;
        RefreshWorldMapPresentation();
    }

    private void RegisterMapLayoutRefreshButton(string buttonName)
    {
        Button button = interfaceRoot.Q<Button>(buttonName);
        if (button != null)
            button.clicked += ScheduleWorldMapPresentationRefresh;
    }

    private void ScheduleWorldMapPresentationRefresh()
    {
        if (interfaceRoot == null)
            return;

        interfaceRoot.schedule
            .Execute(RefreshWorldMapPresentation)
            .ExecuteLater(1);
    }

    private void RefreshWorldMapPresentation()
    {
        bool expeditionMapOpen =
            openedScreen.HasValue &&
            openedScreen.Value == MainScreen.Expeditions;

        if (expeditionMapOpen)
        {
            // WM-03: world-map теперь position:absolute внутри world-map-viewport
            // и всегда заполняет его на 100% через статический USS — отдельная
            // подгонка flexGrow/width/height самого world-map здесь больше не
            // нужна, ConfigureWorldMapFullscreenLayout уже отвечает за viewport.
            ConfigureWorldMapFullscreenLayout();
        }
        else
        {
            HideWorldMapLocationCard();
        }
    }

    private void OnWorldMapMarkerPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 1 || gameState == null)
            return;

        VisualElement node = FindWorldMapLocationNode(evt.target as VisualElement);
        if (node == null || string.IsNullOrEmpty(node.name))
            return;

        const string prefix = "world-map-node-";
        if (!node.name.StartsWith(prefix))
            return;

        string locationId = node.name.Substring(prefix.Length);
        LocationData location = gameState.FindLocation(locationId);

        if (location == null || location.IsWaypoint || !location.IsVisibleOnMap)
            return;

        ShowWorldMapLocationCard(location);
        evt.StopImmediatePropagation();
    }

    private static VisualElement FindWorldMapLocationNode(VisualElement element)
    {
        VisualElement current = element;

        while (current != null)
        {
            if (current.ClassListContains("world-map-node"))
                return current;
            current = current.parent;
        }

        return null;
    }

    private void BindWorldMapLocationCard()
    {
        if (worldMap == null)
            return;

        worldMapLocationCard = worldMap.Q<VisualElement>("world-map-location-inspection-card");
        worldMapLocationCardTitle = worldMap.Q<Label>("world-map-location-inspection-title");
        worldMapLocationCardDetails = worldMap.Q<Label>("world-map-location-inspection-details");
        worldMapLocationCardCloseButton = worldMap.Q<Button>("world-map-location-inspection-close-button");

        if (worldMapLocationCardCloseButton != null)
            worldMapLocationCardCloseButton.clicked += HideWorldMapLocationCard;
    }

    private void ShowWorldMapLocationCard(LocationData location)
    {
        if (worldMapLocationCard == null ||
            worldMapLocationCardTitle == null ||
            worldMapLocationCardDetails == null)
        {
            return;
        }

        string researchText = location.ExplorationHours > 0
            ? "Исследование: " +
              ContinuousExpeditionCommands.FormatHours(location.ExplorationHours)
            : "Исследование: пока не реализовано";

        worldMapLocationCardTitle.text = location.Name.ToUpper();
        worldMapLocationCardDetails.text =
            "Регион: " + location.RegionName + "\n" +
            "Угроза: " + location.Threat + "\n" +
            GetWorldMapLocationStatus(location) + "\n" +
            researchText + "\n\n" +
            "ЛКМ по маркеру — отдать приказ двигаться сюда.";

        worldMapLocationCard.style.display = DisplayStyle.Flex;
        worldMapLocationCard.BringToFront();
    }

    private void HideWorldMapLocationCard()
    {
        if (worldMapLocationCard != null)
            worldMapLocationCard.style.display = DisplayStyle.None;
    }
}
