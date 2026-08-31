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
        RegisterMapLayoutRefreshButton("nav-army-button");
        RegisterMapLayoutRefreshButton("nav-expeditions-button");
        RegisterMapLayoutRefreshButton("persistent-commander-army-button");
        RegisterMapLayoutRefreshButton("persistent-commander-expedition-button");

        EnsureWorldMapLocationCard();
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
        if (persistentCommanderGarrisonHost == null)
        {
            persistentCommanderGarrisonHost =
                interfaceRoot.Q<VisualElement>("persistent-commander-garrison-host");
        }

        bool expeditionMapOpen =
            openedScreen.HasValue &&
            openedScreen.Value == MainScreen.Expeditions;

        if (persistentCommanderGarrisonHost != null)
        {
            persistentCommanderGarrisonHost.style.display =
                expeditionMapOpen
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
        }

        if (expeditionMapOpen)
        {
            ConfigureWorldMapFullscreenLayout();
            worldMap.style.flexGrow = 1f;
            worldMap.style.flexShrink = 1f;
            worldMap.style.width = Length.Percent(100);
            worldMap.style.height = Length.Percent(100);
            worldMap.style.marginBottom = 0f;
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

    private void EnsureWorldMapLocationCard()
    {
        if (worldMapLocationCard != null || worldMap == null)
            return;

        worldMapLocationCard = new VisualElement
        {
            name = "world-map-location-inspection-card"
        };
        worldMapLocationCard.style.position = Position.Absolute;
        worldMapLocationCard.style.right = 14f;
        worldMapLocationCard.style.bottom = 14f;
        worldMapLocationCard.style.width = 300f;
        worldMapLocationCard.style.paddingLeft = 14f;
        worldMapLocationCard.style.paddingRight = 14f;
        worldMapLocationCard.style.paddingTop = 12f;
        worldMapLocationCard.style.paddingBottom = 12f;
        worldMapLocationCard.style.backgroundColor =
            (Color)new Color32(35, 39, 46, 248);
        worldMapLocationCard.style.borderLeftWidth = 1f;
        worldMapLocationCard.style.borderRightWidth = 1f;
        worldMapLocationCard.style.borderTopWidth = 1f;
        worldMapLocationCard.style.borderBottomWidth = 1f;
        worldMapLocationCard.style.borderLeftColor =
            (Color)new Color32(177, 139, 73, 255);
        worldMapLocationCard.style.borderRightColor =
            (Color)new Color32(93, 79, 56, 255);
        worldMapLocationCard.style.borderTopColor =
            (Color)new Color32(177, 139, 73, 255);
        worldMapLocationCard.style.borderBottomColor =
            (Color)new Color32(93, 79, 56, 255);
        worldMapLocationCard.style.borderTopLeftRadius = 5f;
        worldMapLocationCard.style.borderTopRightRadius = 5f;
        worldMapLocationCard.style.borderBottomLeftRadius = 5f;
        worldMapLocationCard.style.borderBottomRightRadius = 5f;

        worldMapLocationCardTitle = new Label();
        worldMapLocationCardTitle.style.color =
            (Color)new Color32(235, 194, 104, 255);
        worldMapLocationCardTitle.style.fontSize = 14f;
        worldMapLocationCardTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        worldMapLocationCardTitle.style.whiteSpace = WhiteSpace.Normal;
        worldMapLocationCardTitle.style.marginBottom = 7f;

        worldMapLocationCardDetails = new Label();
        worldMapLocationCardDetails.style.color =
            (Color)new Color32(207, 201, 184, 255);
        worldMapLocationCardDetails.style.fontSize = 11f;
        worldMapLocationCardDetails.style.whiteSpace = WhiteSpace.Normal;
        worldMapLocationCardDetails.style.marginBottom = 10f;

        worldMapLocationCardCloseButton = new Button(HideWorldMapLocationCard)
        {
            text = "ЗАКРЫТЬ"
        };
        worldMapLocationCardCloseButton.style.height = 32f;
        worldMapLocationCardCloseButton.style.unityFontStyleAndWeight =
            FontStyle.Bold;

        worldMapLocationCard.Add(worldMapLocationCardTitle);
        worldMapLocationCard.Add(worldMapLocationCardDetails);
        worldMapLocationCard.Add(worldMapLocationCardCloseButton);
        worldMap.Add(worldMapLocationCard);
        HideWorldMapLocationCard();
    }

    private void ShowWorldMapLocationCard(LocationData location)
    {
        EnsureWorldMapLocationCard();
        if (worldMapLocationCard == null)
            return;

        string researchText = location.ExplorationDays > 0
            ? "Исследование: " + location.ExplorationDays + " дн."
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
