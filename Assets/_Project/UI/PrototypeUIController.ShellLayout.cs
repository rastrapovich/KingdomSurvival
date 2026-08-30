using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private int shellReportHistoryHash = int.MinValue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeShellRuntimeHelpers()
    {
        PrototypeUIController controller =
            Object.FindFirstObjectByType<PrototypeUIController>();

        if (controller == null || controller.interfaceRoot == null)
            return;

        controller.EnsureShellDebugHost();

        controller.interfaceRoot.schedule
            .Execute(controller.RefreshRoyalReportsNewestFirst)
            .Every(100);
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
        debugHost.style.borderLeftWidth = 0;
        debugHost.style.borderRightWidth = 0;
        debugHost.style.borderTopWidth = 0;
        debugHost.style.borderBottomWidth = 0;
        debugHost.style.backgroundColor = Color.clear;
        debugHost.style.alignItems = Align.Center;
        debugHost.style.justifyContent = Justify.FlexEnd;
        screen.Add(debugHost);
    }

    private void RefreshRoyalReportsNewestFirst()
    {
        if (reportHistoryLabel == null || reportHistory == null)
            return;

        int currentHash = 17;

        foreach (string entry in reportHistory)
            currentHash = currentHash * 31 +
                (entry != null ? entry.GetHashCode() : 0);

        if (currentHash == shellReportHistoryHash)
            return;

        shellReportHistoryHash = currentHash;

        List<string> newestFirst = new List<string>(reportHistory.Count);

        for (int i = reportHistory.Count - 1; i >= 0; i--)
        {
            string entry = reportHistory[i] ?? string.Empty;
            entry = entry.Replace(
                "Откройте нужный экран круглой кнопкой слева сверху.",
                "Выберите нужный раздел в нижнем меню.");
            newestFirst.Add(entry);
        }

        reportHistoryLabel.text = string.Join("\n\n", newestFirst);

        if (reportHistoryScroll != null)
            reportHistoryScroll.scrollOffset = Vector2.zero;
    }
}
