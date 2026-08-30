using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private int shellReportHistoryHash = int.MinValue;
    private bool shellRuntimeInitialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeShellRuntimeHelpers()
    {
        PrototypeUIController controller =
            Object.FindFirstObjectByType<PrototypeUIController>();

        if (controller == null)
            return;

        controller.TryInitializeShellRuntime();
    }

    private void FixedUpdate()
    {
        // RuntimeInitialize может выполниться раньше OnEnable. FixedUpdate
        // гарантирует повторную попытку уже после создания UI.
        TryInitializeShellRuntime();
    }

    private void TryInitializeShellRuntime()
    {
        if (shellRuntimeInitialized || interfaceRoot == null)
            return;

        EnsureShellDebugHost();
        ApplyShellLayoutFallback();

        // Повторяем после первого layout-pass UI Toolkit. Критические размеры
        // остаются inline и больше не зависят от порядка импорта USS.
        interfaceRoot.schedule
            .Execute(ApplyShellLayoutFallback)
            .ExecuteLater(1);

        interfaceRoot.schedule
            .Execute(RefreshRoyalReportsNewestFirst)
            .Every(100);

        shellRuntimeInitialized = true;
    }

    private void EnsureShellDebugHost()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
            return;

        VisualElement screen =
            document.rootVisualElement.Q<VisualElement>("screen");

        if (screen == null ||
            screen.Q<VisualElement>(className: "top-bar") != null)
        {
            return;
        }

        VisualElement debugHost = new VisualElement();
        debugHost.AddToClassList("top-bar");
        debugHost.AddToClassList("shell-debug-host");
        screen.Add(debugHost);
    }

    private void ApplyShellLayoutFallback()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
            return;

        VisualElement root = document.rootVisualElement;
        VisualElement screen = root.Q<VisualElement>("screen");

        if (screen == null)
            return;

        // Вся оболочка всегда занимает viewport.
        screen.style.position = Position.Absolute;
        screen.style.left = 0;
        screen.style.right = 0;
        screen.style.top = 0;
        screen.style.bottom = 0;
        screen.style.flexDirection = FlexDirection.Column;
        screen.style.paddingLeft = 14;
        screen.style.paddingRight = 14;
        screen.style.paddingTop = 14;
        screen.style.paddingBottom = 14;
        screen.style.backgroundColor = Rgb(18, 21, 26);

        VisualElement mainRow =
            root.Q<VisualElement>(className: "shell-main-row");

        if (mainRow != null)
        {
            mainRow.style.flexGrow = 1;
            mainRow.style.flexShrink = 1;
            mainRow.style.minHeight = 0;
            mainRow.style.flexDirection = FlexDirection.Row;
            mainRow.style.marginBottom = 10;
        }

        ConfigureSidebar(root);
        ConfigureWorkspace(root);
        ConfigureBottomBar(root);
        ConfigureDebugHost(root);
        ConfigureIncidentStack(root);
    }

    private void ConfigureSidebar(VisualElement root)
    {
        VisualElement sidebar =
            root.Q<VisualElement>(className: "shell-sidebar");

        if (sidebar == null)
            return;

        sidebar.style.width = 300;
        sidebar.style.minWidth = 300;
        sidebar.style.maxWidth = 300;
        sidebar.style.minHeight = 0;
        sidebar.style.flexShrink = 0;
        sidebar.style.marginRight = 12;
        sidebar.style.paddingLeft = 8;
        sidebar.style.paddingRight = 8;
        sidebar.style.paddingTop = 10;
        sidebar.style.paddingBottom = 8;
        sidebar.style.backgroundColor = Rgb(30, 33, 39);
        SetBorder(sidebar, 1, Rgb(79, 72, 63));

        Label title = root.Q<Label>(className: "shell-game-title");

        if (title != null)
        {
            title.style.height = 48;
            title.style.minHeight = 48;
            title.style.flexShrink = 0;
            title.style.color = Rgb(221, 181, 99);
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        VisualElement reportsDock =
            root.Q<VisualElement>(className: "shell-reports-dock");

        if (reportsDock != null)
        {
            // Перебиваем старый reports-dock: absolute / 430x270 / bottom 22.
            reportsDock.style.position = Position.Relative;
            reportsDock.style.left = 0;
            reportsDock.style.right = 0;
            reportsDock.style.top = 0;
            reportsDock.style.bottom = 0;
            reportsDock.style.width = Length.Percent(100);
            reportsDock.style.height = 0;
            reportsDock.style.flexGrow = 1;
            reportsDock.style.flexShrink = 1;
            reportsDock.style.minHeight = 0;
        }

        VisualElement reportsPanel =
            root.Q<VisualElement>(className: "shell-reports-panel");

        if (reportsPanel != null)
        {
            reportsPanel.style.flexGrow = 1;
            reportsPanel.style.flexShrink = 1;
            reportsPanel.style.minHeight = 0;
            reportsPanel.style.marginBottom = 0;
            reportsPanel.style.paddingLeft = 12;
            reportsPanel.style.paddingRight = 12;
            reportsPanel.style.paddingTop = 12;
            reportsPanel.style.paddingBottom = 12;
            reportsPanel.style.backgroundColor = Rgb(34, 38, 45);
            SetBorder(reportsPanel, 1, Rgb(70, 75, 84));
        }

        Label reportsTitle =
            root.Q<Label>(className: "shell-reports-title");

        if (reportsTitle != null)
        {
            reportsTitle.style.marginBottom = 10;
            reportsTitle.style.color = Rgb(221, 181, 99);
            reportsTitle.style.fontSize = 15;
        }

        if (reportHistoryScroll != null)
        {
            reportHistoryScroll.style.flexGrow = 1;
            reportHistoryScroll.style.flexShrink = 1;
            reportHistoryScroll.style.minHeight = 0;
            reportHistoryScroll.style.maxHeight = 10000;
        }

        if (reportHistoryLabel != null)
        {
            reportHistoryLabel.style.color = Rgb(205, 200, 188);
            reportHistoryLabel.style.fontSize = 12;
            reportHistoryLabel.style.whiteSpace = WhiteSpace.Normal;
        }
    }

    private void ConfigureWorkspace(VisualElement root)
    {
        VisualElement workspace =
            root.Q<VisualElement>(className: "shell-workspace");

        if (workspace != null)
        {
            workspace.style.flexGrow = 1;
            workspace.style.flexShrink = 1;
            workspace.style.minWidth = 0;
            workspace.style.minHeight = 0;
            workspace.style.backgroundColor = Rgb(18, 21, 26);
        }

        VisualElement content =
            root.Q<VisualElement>("main-screen-scroll");

        if (content != null)
        {
            // Старый main-screen-scroll резервировал снизу 300 px.
            content.style.flexGrow = 1;
            content.style.flexShrink = 1;
            content.style.minWidth = 0;
            content.style.minHeight = 0;
            content.style.marginTop = 0;
            content.style.marginBottom = 0;
        }

        ConfigureContentScreen(capitalScreen);
        ConfigureContentScreen(armyScreen);
        ConfigureContentScreen(expeditionsScreen);
    }

    private static void ConfigureContentScreen(VisualElement screen)
    {
        if (screen == null)
            return;

        screen.style.width = Length.Percent(100);
        screen.style.height = Length.Percent(100);
        screen.style.minHeight = 0;
    }

    private void ConfigureBottomBar(VisualElement root)
    {
        VisualElement bottomBar =
            root.Q<VisualElement>(className: "shell-bottom-bar");

        if (bottomBar == null)
            return;

        bottomBar.style.height = 82;
        bottomBar.style.minHeight = 82;
        bottomBar.style.maxHeight = 82;
        bottomBar.style.flexShrink = 0;
        bottomBar.style.flexDirection = FlexDirection.Row;
        bottomBar.style.backgroundColor = Rgb(33, 37, 44);
        SetBorder(bottomBar, 1, Rgb(78, 69, 55));

        VisualElement navigation =
            root.Q<VisualElement>(className: "shell-navigation-bar");

        if (navigation != null)
        {
            navigation.style.width = 300;
            navigation.style.minWidth = 300;
            navigation.style.maxWidth = 300;
            navigation.style.height = Length.Percent(100);
            navigation.style.flexShrink = 0;
            navigation.style.flexDirection = FlexDirection.Row;
            navigation.style.alignItems = Align.Stretch;
            navigation.style.marginTop = 0;
            navigation.style.paddingLeft = 4;
            navigation.style.paddingRight = 4;
        }

        ConfigureNavButton(
            navCapitalButton,
            94,
            Rgb(96, 82, 53),
            Rgb(148, 126, 78));

        ConfigureNavButton(
            navArmyButton,
            94,
            Rgb(91, 59, 61),
            Rgb(137, 86, 88));

        ConfigureNavButton(
            navExpeditionsButton,
            98,
            Rgb(55, 82, 70),
            Rgb(82, 124, 102));

        VisualElement resourceBar =
            root.Q<VisualElement>(className: "shell-resource-bar");

        if (resourceBar != null)
        {
            resourceBar.style.flexGrow = 1;
            resourceBar.style.flexShrink = 1;
            resourceBar.style.minWidth = 0;
            resourceBar.style.height = Length.Percent(100);
            resourceBar.style.flexDirection = FlexDirection.Row;
            resourceBar.style.alignItems = Align.Center;
            resourceBar.style.justifyContent = Justify.FlexEnd;
            resourceBar.style.paddingLeft = 12;
            resourceBar.style.paddingRight = 10;
        }

        VisualElement resourceStrip =
            root.Q<VisualElement>(className: "shell-resource-strip");

        if (resourceStrip != null)
        {
            resourceStrip.style.flexGrow = 0;
            resourceStrip.style.flexShrink = 0;
            resourceStrip.style.flexDirection = FlexDirection.Row;
            resourceStrip.style.flexWrap = Wrap.NoWrap;
            resourceStrip.style.alignItems = Align.Center;
            resourceStrip.style.justifyContent = Justify.FlexEnd;
        }

        root.Query<VisualElement>(className: "shell-resource-box")
            .ForEach(ConfigureResourceBox);

        VisualElement moodBox =
            root.Q<VisualElement>(className: "shell-mood-box");

        if (moodBox != null)
        {
            moodBox.style.width = 142;
            moodBox.style.minWidth = 142;
        }

        VisualElement foodBox =
            root.Q<VisualElement>(className: "food-box");

        if (foodBox != null)
        {
            foodBox.style.width = 108;
            foodBox.style.minWidth = 108;
        }

        if (endDayButton != null)
        {
            // Перебиваем старую круглую absolute-кнопку ДЕНЬ.
            endDayButton.style.position = Position.Relative;
            endDayButton.style.right = 0;
            endDayButton.style.bottom = 0;
            endDayButton.style.width = 92;
            endDayButton.style.minWidth = 92;
            endDayButton.style.maxWidth = 92;
            endDayButton.style.height = 54;
            endDayButton.style.marginLeft = 8;
            endDayButton.style.backgroundColor = Rgb(188, 136, 39);
            endDayButton.style.color = Rgb(28, 24, 17);
            endDayButton.style.fontSize = 15;
            endDayButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            SetBorder(endDayButton, 1, Rgb(224, 174, 68));
            SetRadius(endDayButton, 8);
        }
    }

    private static void ConfigureNavButton(
        Button button,
        float width,
        Color background,
        Color border)
    {
        if (button == null)
            return;

        // Перебиваем старый nav-button: 58x58 и border-radius 29.
        button.style.width = width;
        button.style.minWidth = width;
        button.style.maxWidth = width;
        button.style.height = Length.Percent(100);
        button.style.minHeight = 0;
        button.style.flexGrow = 0;
        button.style.flexShrink = 0;
        button.style.marginRight = 3;
        button.style.paddingLeft = 5;
        button.style.paddingRight = 5;
        button.style.backgroundColor = background;
        button.style.color = Rgb(235, 226, 205);
        button.style.fontSize = button.name == "nav-expeditions-button" ? 11 : 13;
        button.style.unityFontStyleAndWeight = FontStyle.Normal;
        SetBorder(button, 1, border);
        SetRadius(button, 3);
    }

    private static void ConfigureResourceBox(VisualElement box)
    {
        box.style.width = 100;
        box.style.minWidth = 100;
        box.style.height = 54;
        box.style.minHeight = 54;
        box.style.maxHeight = 54;
        box.style.flexGrow = 0;
        box.style.flexShrink = 0;
        box.style.marginRight = 6;
        box.style.marginBottom = 0;
        box.style.paddingLeft = 9;
        box.style.paddingRight = 9;
        box.style.paddingTop = 7;
        box.style.paddingBottom = 6;
        box.style.justifyContent = Justify.Center;
        box.style.backgroundColor = Rgb(45, 49, 57);
        SetBorder(box, 1, Rgb(59, 64, 73));
        SetRadius(box, 4);
    }

    private void ConfigureDebugHost(VisualElement root)
    {
        VisualElement debugHost =
            root.Q<VisualElement>(className: "shell-debug-host");

        if (debugHost == null)
            return;

        debugHost.style.position = Position.Absolute;
        debugHost.style.right = 14;
        debugHost.style.top = 14;
        debugHost.style.width = 84;
        debugHost.style.minWidth = 84;
        debugHost.style.maxWidth = 84;
        debugHost.style.height = 38;
        debugHost.style.minHeight = 38;
        debugHost.style.paddingLeft = 0;
        debugHost.style.paddingRight = 0;
        debugHost.style.paddingTop = 0;
        debugHost.style.paddingBottom = 0;
        debugHost.style.backgroundColor = Color.clear;
        debugHost.style.alignItems = Align.Center;
        debugHost.style.justifyContent = Justify.FlexEnd;
        SetBorder(debugHost, 0, Color.clear);
    }

    private void ConfigureIncidentStack(VisualElement root)
    {
        VisualElement stack =
            root.Q<VisualElement>("incident-notification-stack");

        if (stack == null)
            return;

        stack.style.right = 30;
        stack.style.bottom = 108;
    }

    private void RefreshRoyalReportsNewestFirst()
    {
        if (reportHistoryLabel == null || reportHistory == null)
            return;

        int currentHash = 17;

        foreach (string entry in reportHistory)
        {
            currentHash = currentHash * 31 +
                (entry != null ? entry.GetHashCode() : 0);
        }

        if (currentHash == shellReportHistoryHash)
            return;

        shellReportHistoryHash = currentHash;

        List<string> newestFirst =
            new List<string>(reportHistory.Count);

        for (int i = reportHistory.Count - 1; i >= 0; i--)
        {
            string entry = reportHistory[i] ?? string.Empty;
            entry = entry.Replace(
                "Откройте нужный экран круглой кнопкой слева сверху.",
                "Выберите нужный раздел в нижнем меню.");
            newestFirst.Add(entry);
        }

        reportHistoryLabel.text =
            string.Join("\n\n", newestFirst);

        if (reportHistoryScroll != null)
            reportHistoryScroll.scrollOffset = Vector2.zero;
    }

    private static Color Rgb(byte r, byte g, byte b)
    {
        return new Color32(r, g, b, 255);
    }

    private static void SetBorder(
        VisualElement element,
        float width,
        Color color)
    {
        element.style.borderLeftWidth = width;
        element.style.borderRightWidth = width;
        element.style.borderTopWidth = width;
        element.style.borderBottomWidth = width;
        element.style.borderLeftColor = color;
        element.style.borderRightColor = color;
        element.style.borderTopColor = color;
        element.style.borderBottomColor = color;
    }

    private static void SetRadius(
        VisualElement element,
        float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }
}
