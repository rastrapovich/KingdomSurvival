using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool persistentCommanderShellInitialized;
    private IVisualElementScheduledItem persistentCommanderShellInitItem;
    private IVisualElementScheduledItem persistentCommanderShellStateItem;

    private VisualElement persistentCommanderPanel;
    private VisualElement persistentCommanderGarrisonHost;
    private Button persistentCommanderArmyButton;
    private Button persistentCommanderExpeditionButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializePersistentCommanderShellRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();

        if (document == null)
            return;

        controller.persistentCommanderShellInitItem =
            document.rootVisualElement.schedule
                .Execute(controller.TryInitializePersistentCommanderShell)
                .Every(100);
    }

    private void TryInitializePersistentCommanderShell()
    {
        if (persistentCommanderShellInitialized)
            return;

        if (!shellRuntimeInitialized ||
            !stableArmyLayoutInitialized ||
            !armyBottomPolishInitialized ||
            interfaceRoot == null ||
            armyScreen == null ||
            commanderGarrisonDropZone == null ||
            commanderGarrisonList == null ||
            capitalGarrisonDropZone == null ||
            capitalGarrisonList == null)
        {
            return;
        }

        VisualElement sidebar =
            interfaceRoot.Q<VisualElement>(className: "shell-sidebar");
        VisualElement workspace =
            interfaceRoot.Q<VisualElement>(className: "shell-workspace");
        VisualElement content =
            interfaceRoot.Q<VisualElement>("main-screen-scroll");
        VisualElement transferBoard =
            armyScreen.Q<VisualElement>(className: "army-transfer-board");

        if (sidebar == null ||
            workspace == null ||
            content == null ||
            transferBoard == null)
        {
            return;
        }

        CreatePersistentCommanderPanel(sidebar);
        MoveCommanderGarrisonToPersistentWorkspace(workspace, content);
        ConfigureArmyCapitalGarrisonAfterMove(transferBoard);
        RefreshPersistentCommanderNavigationState();

        persistentCommanderShellStateItem = interfaceRoot.schedule
            .Execute(RefreshPersistentCommanderNavigationState)
            .Every(250);

        persistentCommanderShellInitialized = true;
        persistentCommanderShellInitItem?.Pause();
    }

    private void CreatePersistentCommanderPanel(VisualElement sidebar)
    {
        persistentCommanderPanel =
            sidebar.Q<VisualElement>("persistent-commander-panel");

        if (persistentCommanderPanel != null)
            return;

        persistentCommanderPanel = new VisualElement();
        persistentCommanderPanel.name = "persistent-commander-panel";
        persistentCommanderPanel.style.width = Length.Percent(100);
        persistentCommanderPanel.style.height = 231;
        persistentCommanderPanel.style.minHeight = 231;
        persistentCommanderPanel.style.maxHeight = 231;
        persistentCommanderPanel.style.flexGrow = 0;
        persistentCommanderPanel.style.flexShrink = 0;
        persistentCommanderPanel.style.marginTop = 7;
        persistentCommanderPanel.style.paddingLeft = 10;
        persistentCommanderPanel.style.paddingRight = 10;
        persistentCommanderPanel.style.paddingTop = 9;
        persistentCommanderPanel.style.paddingBottom = 9;
        persistentCommanderPanel.style.backgroundColor = PersistentCommanderRgb(31, 35, 41);
        SetPersistentCommanderBorder(
            persistentCommanderPanel,
            1,
            PersistentCommanderRgb(104, 84, 57));
        SetPersistentCommanderRadius(persistentCommanderPanel, 4);

        Label title = new Label("КОМАНДИР");
        title.style.height = 18;
        title.style.minHeight = 18;
        title.style.maxHeight = 18;
        title.style.flexShrink = 0;
        title.style.marginBottom = 7;
        title.style.color = PersistentCommanderRgb(220, 181, 103);
        title.style.fontSize = 12;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.unityTextAlign = TextAnchor.MiddleLeft;
        persistentCommanderPanel.Add(title);

        VisualElement contentRow = new VisualElement();
        contentRow.style.width = Length.Percent(100);
        contentRow.style.flexGrow = 1;
        contentRow.style.minHeight = 0;
        contentRow.style.flexDirection = FlexDirection.Row;
        contentRow.style.alignItems = Align.Stretch;
        persistentCommanderPanel.Add(contentRow);

        VisualElement portrait = new VisualElement();
        portrait.style.width = Length.Percent(44);
        portrait.style.minWidth = 0;
        portrait.style.height = Length.Percent(100);
        portrait.style.marginRight = 8;
        portrait.style.alignItems = Align.Center;
        portrait.style.justifyContent = Justify.Center;
        portrait.style.backgroundColor = PersistentCommanderRgb(27, 31, 37);
        SetPersistentCommanderBorder(
            portrait,
            1,
            PersistentCommanderRgb(69, 75, 85));
        SetPersistentCommanderRadius(portrait, 3);

        Label portraitLabel = new Label("ИЗОБРАЖЕНИЕ\nКОМАНДИРА");
        portraitLabel.style.color = PersistentCommanderRgb(112, 118, 128);
        portraitLabel.style.fontSize = 9;
        portraitLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        portraitLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        portraitLabel.style.whiteSpace = WhiteSpace.Normal;
        portrait.Add(portraitLabel);
        contentRow.Add(portrait);

        VisualElement buttonsGrid = new VisualElement();
        buttonsGrid.style.flexGrow = 1;
        buttonsGrid.style.minWidth = 0;
        buttonsGrid.style.height = Length.Percent(100);
        buttonsGrid.style.flexDirection = FlexDirection.Column;
        contentRow.Add(buttonsGrid);

        VisualElement topButtons = CreatePersistentCommanderButtonRow();
        VisualElement bottomButtons = CreatePersistentCommanderButtonRow();
        buttonsGrid.Add(topButtons);
        buttonsGrid.Add(bottomButtons);

        Button emptyA = CreatePersistentCommanderButton(string.Empty);
        Button emptyB = CreatePersistentCommanderButton(string.Empty);
        emptyA.pickingMode = PickingMode.Ignore;
        emptyB.pickingMode = PickingMode.Ignore;
        StylePersistentCommanderEmptyButton(emptyA);
        StylePersistentCommanderEmptyButton(emptyB);
        topButtons.Add(emptyA);
        topButtons.Add(emptyB);

        persistentCommanderArmyButton =
            CreatePersistentCommanderButton("АРМИЯ");
        persistentCommanderArmyButton.name = "persistent-commander-army-button";
        persistentCommanderArmyButton.tooltip = "Открыть экран армии";
        persistentCommanderArmyButton.clicked += OnPersistentCommanderArmyClicked;
        bottomButtons.Add(persistentCommanderArmyButton);

        persistentCommanderExpeditionButton =
            CreatePersistentCommanderButton("ЭКСПЕДИЦИЯ");
        persistentCommanderExpeditionButton.name =
            "persistent-commander-expedition-button";
        persistentCommanderExpeditionButton.tooltip =
            "Открыть экран экспедиций";
        persistentCommanderExpeditionButton.clicked +=
            OnPersistentCommanderExpeditionClicked;
        bottomButtons.Add(persistentCommanderExpeditionButton);
    }

    private VisualElement CreatePersistentCommanderButtonRow()
    {
        VisualElement row = new VisualElement();
        row.style.width = Length.Percent(100);
        row.style.height = Length.Percent(50);
        row.style.flexGrow = 1;
        row.style.minHeight = 0;
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Stretch;
        return row;
    }

    private Button CreatePersistentCommanderButton(string text)
    {
        Button button = new Button();
        button.text = text;
        button.style.width = Length.Percent(50);
        button.style.height = Length.Percent(100);
        button.style.flexGrow = 1;
        button.style.minWidth = 0;
        button.style.minHeight = 0;
        button.style.marginLeft = 2;
        button.style.marginRight = 2;
        button.style.marginTop = 2;
        button.style.marginBottom = 2;
        button.style.paddingLeft = 3;
        button.style.paddingRight = 3;
        button.style.paddingTop = 2;
        button.style.paddingBottom = 2;
        button.style.fontSize = 9;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        SetPersistentCommanderRadius(button, 3);
        return button;
    }

    private void StylePersistentCommanderEmptyButton(Button button)
    {
        button.style.backgroundColor = PersistentCommanderRgb(41, 46, 53);
        button.style.color = PersistentCommanderRgb(118, 122, 128);
        SetPersistentCommanderBorder(
            button,
            1,
            PersistentCommanderRgb(62, 68, 77));
    }

    private void OnPersistentCommanderArmyClicked()
    {
        if (!CanProcessNavigationClick())
            return;

        ToggleScreen(MainScreen.Army);
        RefreshPersistentCommanderNavigationState();
    }

    private void OnPersistentCommanderExpeditionClicked()
    {
        if (!CanProcessNavigationClick())
            return;

        ToggleScreen(MainScreen.Expeditions);
        RefreshPersistentCommanderNavigationState();
    }

    private void RefreshPersistentCommanderNavigationState()
    {
        if (persistentCommanderArmyButton == null ||
            persistentCommanderExpeditionButton == null)
        {
            return;
        }

        bool armyActive =
            openedScreen.HasValue && openedScreen.Value == MainScreen.Army;
        bool expeditionActive =
            openedScreen.HasValue && openedScreen.Value == MainScreen.Expeditions;

        StylePersistentCommanderNavigationButton(
            persistentCommanderArmyButton,
            armyActive,
            true);
        StylePersistentCommanderNavigationButton(
            persistentCommanderExpeditionButton,
            expeditionActive,
            false);
    }

    private void StylePersistentCommanderNavigationButton(
        Button button,
        bool active,
        bool army)
    {
        Color background;
        Color border;
        Color text;

        if (army)
        {
            background = active
                ? PersistentCommanderRgb(112, 67, 68)
                : PersistentCommanderRgb(76, 51, 52);
            border = active
                ? PersistentCommanderRgb(166, 104, 101)
                : PersistentCommanderRgb(111, 73, 72);
            text = PersistentCommanderRgb(233, 211, 205);
        }
        else
        {
            background = active
                ? PersistentCommanderRgb(66, 104, 81)
                : PersistentCommanderRgb(49, 76, 61);
            border = active
                ? PersistentCommanderRgb(103, 151, 119)
                : PersistentCommanderRgb(72, 108, 84);
            text = PersistentCommanderRgb(214, 231, 219);
        }

        button.style.backgroundColor = background;
        button.style.color = text;
        SetPersistentCommanderBorder(button, 1, border);
    }

    private void MoveCommanderGarrisonToPersistentWorkspace(
        VisualElement workspace,
        VisualElement content)
    {
        workspace.style.flexDirection = FlexDirection.Column;
        workspace.style.alignItems = Align.Stretch;

        content.style.flexGrow = 1;
        content.style.flexShrink = 1;
        content.style.minHeight = 0;

        persistentCommanderGarrisonHost =
            workspace.Q<VisualElement>("persistent-commander-garrison-host");

        if (persistentCommanderGarrisonHost == null)
        {
            persistentCommanderGarrisonHost = new VisualElement();
            persistentCommanderGarrisonHost.name =
                "persistent-commander-garrison-host";
            persistentCommanderGarrisonHost.style.width = Length.Percent(100);
            persistentCommanderGarrisonHost.style.height = 113;
            persistentCommanderGarrisonHost.style.minHeight = 113;
            persistentCommanderGarrisonHost.style.maxHeight = 113;
            persistentCommanderGarrisonHost.style.flexGrow = 0;
            persistentCommanderGarrisonHost.style.flexShrink = 0;
            persistentCommanderGarrisonHost.style.flexDirection = FlexDirection.Row;
            persistentCommanderGarrisonHost.style.alignItems = Align.FlexStart;
            persistentCommanderGarrisonHost.style.justifyContent =
                Justify.FlexStart;
            persistentCommanderGarrisonHost.style.marginTop = 5;
            persistentCommanderGarrisonHost.style.marginBottom = 0;
            workspace.Add(persistentCommanderGarrisonHost);
        }

        if (commanderGarrisonDropZone.parent != persistentCommanderGarrisonHost)
        {
            commanderGarrisonDropZone.RemoveFromHierarchy();
            persistentCommanderGarrisonHost.Add(commanderGarrisonDropZone);
        }

        commanderGarrisonDropZone.style.width = Length.Percent(82);
        commanderGarrisonDropZone.style.height = 113;
        commanderGarrisonDropZone.style.minHeight = 113;
        commanderGarrisonDropZone.style.maxHeight = 113;
        commanderGarrisonDropZone.style.flexGrow = 0;
        commanderGarrisonDropZone.style.flexShrink = 0;
        commanderGarrisonDropZone.style.alignSelf = Align.FlexStart;
        commanderGarrisonDropZone.style.marginBottom = 0;

        KeepPersistentCommanderGarrisonTextClean();
        AlignArmyCards(commanderGarrisonList);
    }

    private void ConfigureArmyCapitalGarrisonAfterMove(
        VisualElement transferBoard)
    {
        transferBoard.style.width = Length.Percent(82);
        transferBoard.style.height = 113;
        transferBoard.style.minHeight = 113;
        transferBoard.style.maxHeight = 113;
        transferBoard.style.flexGrow = 0;
        transferBoard.style.flexShrink = 0;
        transferBoard.style.flexDirection = FlexDirection.Column;
        transferBoard.style.alignItems = Align.Stretch;
        transferBoard.style.alignSelf = Align.FlexStart;
        transferBoard.style.marginTop = 0;
        transferBoard.style.marginBottom = 0;

        capitalGarrisonDropZone.style.width = Length.Percent(100);
        capitalGarrisonDropZone.style.height = 113;
        capitalGarrisonDropZone.style.minHeight = 113;
        capitalGarrisonDropZone.style.maxHeight = 113;
        capitalGarrisonDropZone.style.flexGrow = 0;
        capitalGarrisonDropZone.style.flexShrink = 0;
        capitalGarrisonDropZone.style.marginBottom = 0;

        Label capitalSummary =
            capitalGarrisonDropZone.Q<Label>(className: "army-roster-summary");
        Label capitalEmpty =
            capitalGarrisonDropZone.Q<Label>(className: "army-roster-empty-label");
        KeepArmyAuxiliaryLabelHidden(capitalSummary);
        KeepArmyAuxiliaryLabelHidden(capitalEmpty);
        AlignArmyCards(capitalGarrisonList);
    }

    private void KeepPersistentCommanderGarrisonTextClean()
    {
        Label summary =
            commanderGarrisonDropZone.Q<Label>(className: "army-roster-summary");
        Label empty =
            commanderGarrisonDropZone.Q<Label>(className: "army-roster-empty-label");

        KeepArmyAuxiliaryLabelHidden(summary);
        KeepArmyAuxiliaryLabelHidden(empty);
    }

    private static Color PersistentCommanderRgb(byte r, byte g, byte b)
    {
        return new Color32(r, g, b, 255);
    }

    private static void SetPersistentCommanderBorder(
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

    private static void SetPersistentCommanderRadius(
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
