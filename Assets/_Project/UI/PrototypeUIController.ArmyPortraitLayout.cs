using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private const float CompactBottomBarHeight = 42f;
    private const float PortraitRosterHeight = 128f;
    private const float PortraitFighterWidth = 76f;
    private const float PortraitFighterHeight = 112f;

    private bool armyPortraitLayoutInitialized;
    private IVisualElementScheduledItem armyPortraitLayoutPoll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeArmyPortraitLayoutRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeArmyPortraitLayout)
            .ExecuteLater(220);
    }

    private void TryInitializeArmyPortraitLayout()
    {
        if (armyPortraitLayoutInitialized)
            return;

        if (interfaceRoot == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInitializeArmyPortraitLayout)
                    .ExecuteLater(60);
            }
            return;
        }

        ApplyArmyPortraitLayout();
        armyPortraitLayoutPoll = interfaceRoot.schedule
            .Execute(ApplyArmyPortraitLayout)
            .Every(100);
        armyPortraitLayoutInitialized = true;
    }

    private void ApplyArmyPortraitLayout()
    {
        if (interfaceRoot == null)
            return;

        VisualElement mainRow =
            interfaceRoot.Q<VisualElement>(className: "shell-main-row");
        if (mainRow != null)
            mainRow.style.marginBottom = 5f;

        VisualElement bottomBar =
            interfaceRoot.Q<VisualElement>(className: "shell-bottom-bar");
        SetFixedLayoutHeight(bottomBar, CompactBottomBarHeight);

        VisualElement navigationBar =
            interfaceRoot.Q<VisualElement>(className: "shell-navigation-bar");
        if (navigationBar != null)
        {
            navigationBar.style.paddingTop = 2f;
            navigationBar.style.paddingBottom = 2f;
        }

        interfaceRoot.Query<Button>(className: "shell-nav-button").ForEach(button =>
        {
            button.style.fontSize = 11f;
            button.style.paddingTop = 1f;
            button.style.paddingBottom = 1f;
        });

        VisualElement resourceBar =
            interfaceRoot.Q<VisualElement>(className: "shell-resource-bar");
        if (resourceBar != null)
        {
            resourceBar.style.paddingLeft = 8f;
            resourceBar.style.paddingRight = 6f;
        }

        interfaceRoot.Query<VisualElement>(className: "shell-resource-box").ForEach(box =>
        {
            SetFixedLayoutHeight(box, 32f);
            box.style.paddingTop = 2f;
            box.style.paddingBottom = 2f;
            box.style.paddingLeft = 8f;
            box.style.paddingRight = 8f;
            box.style.marginRight = 5f;
        });

        interfaceRoot.Query<Label>(className: "resource-income").ForEach(label =>
        {
            label.style.marginTop = 0f;
            label.style.fontSize = 8f;
        });

        VisualElement foodPopup =
            interfaceRoot.Q<VisualElement>(className: "food-expense-popup");
        if (foodPopup != null)
            foodPopup.style.bottom = 36f;

        Button timeButton = interfaceRoot.Q<Button>("time-toggle-button");
        if (timeButton != null)
        {
            SetFixedLayoutHeight(timeButton, 32f);
            timeButton.style.marginLeft = 6f;
            timeButton.style.fontSize = 12f;
            timeButton.style.borderTopLeftRadius = 5f;
            timeButton.style.borderTopRightRadius = 5f;
            timeButton.style.borderBottomLeftRadius = 5f;
            timeButton.style.borderBottomRightRadius = 5f;
        }

        VisualElement incidentStack =
            interfaceRoot.Q<VisualElement>(className: "incident-notification-stack");
        if (incidentStack != null)
            incidentStack.style.bottom = 68f;

        VisualElement commanderHost =
            interfaceRoot.Q<VisualElement>("persistent-commander-garrison-host");
        SetFixedLayoutHeight(commanderHost, PortraitRosterHeight);
        SetFixedLayoutHeight(
            interfaceRoot.Q<VisualElement>(className: "army-transfer-board"),
            PortraitRosterHeight);

        ConfigurePortraitDropZone(commanderGarrisonDropZone);
        ConfigurePortraitDropZone(capitalGarrisonDropZone);
        ConfigurePortraitRosterList(commanderGarrisonList);
        ConfigurePortraitRosterList(capitalGarrisonList);

        interfaceRoot.Query<Label>(className: "army-roster-title").ForEach(title =>
        {
            title.style.display = DisplayStyle.None;
        });

        ConfigureEmptyRosterLabel(commanderGarrisonEmptyLabel);
        ConfigureEmptyRosterLabel(capitalGarrisonEmptyLabel);

        interfaceRoot.Query<Button>(className: "fighter-card").ForEach(
            ConfigurePortraitFighterCard);
    }

    private void ConfigurePortraitDropZone(VisualElement dropZone)
    {
        if (dropZone == null)
            return;

        SetFixedLayoutHeight(dropZone, PortraitRosterHeight);
        dropZone.style.paddingLeft = 8f;
        dropZone.style.paddingRight = 8f;
        dropZone.style.paddingTop = 8f;
        dropZone.style.paddingBottom = 8f;
    }

    private void ConfigurePortraitRosterList(VisualElement list)
    {
        if (list == null)
            return;

        SetFixedLayoutHeight(list, PortraitFighterHeight);
        list.style.alignItems = Align.FlexStart;
    }

    private void ConfigurePortraitFighterCard(Button card)
    {
        if (card == null)
            return;

        card.style.width = PortraitFighterWidth;
        card.style.minWidth = PortraitFighterWidth;
        card.style.maxWidth = PortraitFighterWidth;
        card.style.height = PortraitFighterHeight;
        card.style.minHeight = PortraitFighterHeight;
        card.style.maxHeight = PortraitFighterHeight;
        card.style.marginRight = 8f;
        card.style.paddingLeft = 4f;
        card.style.paddingRight = 4f;
        card.style.paddingTop = 4f;
        card.style.paddingBottom = 4f;
        card.style.position = Position.Relative;

        Label nameLabel = card.Q<Label>(className: "fighter-name");
        if (nameLabel != null)
            nameLabel.style.display = DisplayStyle.None;

        Label roleLabel = card.Q<Label>(className: "fighter-role");
        if (roleLabel != null)
            roleLabel.style.display = DisplayStyle.None;

        Label infoLabel = card.Q<Label>(className: "fighter-info");
        if (infoLabel != null)
            infoLabel.style.display = DisplayStyle.None;

        Label assignmentLabel = card.Q<Label>(className: "fighter-assignment");
        if (assignmentLabel != null)
            assignmentLabel.style.display = DisplayStyle.None;

        VisualElement image =
            card.Q<VisualElement>(className: "fighter-image-placeholder");
        if (image != null)
        {
            image.style.position = Position.Absolute;
            image.style.left = 4f;
            image.style.right = 4f;
            image.style.top = 4f;
            image.style.bottom = 14f;
            image.style.width = StyleKeyword.Auto;
            image.style.height = StyleKeyword.Auto;
            image.style.marginBottom = 0f;
        }

        VisualElement healthBar = card.Q<VisualElement>("fighter-health-bar");
        if (healthBar != null)
        {
            healthBar.style.left = 5f;
            healthBar.style.right = 5f;
            healthBar.style.bottom = 4f;
            healthBar.style.height = 6f;
        }
    }

    private void ConfigureEmptyRosterLabel(Label label)
    {
        if (label == null)
            return;

        label.pickingMode = PickingMode.Ignore;
        label.style.position = Position.Absolute;
        label.style.left = 8f;
        label.style.right = 8f;
        label.style.top = 8f;
        label.style.bottom = 8f;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
    }

    private static void SetFixedLayoutHeight(VisualElement element, float height)
    {
        if (element == null)
            return;

        element.style.height = height;
        element.style.minHeight = height;
        element.style.maxHeight = height;
    }
}
