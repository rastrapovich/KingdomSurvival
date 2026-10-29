using UnityEngine.UIElements;

// Бриф правок (22 пункта) п. 12: левой колонки с пустой плашкой больше нет —
// её место отдано карте. Донесения — архив сообщений под небольшой кнопкой
// в левом нижнем углу: нажатие разворачивает список поверх карты, повторное
// сворачивает. Свёрнутый список места не занимает и не раскладывается;
// история при этом не теряется. За пределами раскрытого списка карта
// принимает клики как обычно.
public partial class PrototypeUIController
{
    private const string ReportsCollapsedClass = "shell-sidebar--collapsed";

    private VisualElement reportsSidebar;
    private Button reportsToggleButton;
    private bool reportsExpanded;
    private int reportsSeenCount;

    private bool IsReportsDockExpanded => reportsExpanded;

    private void EnsureReportsDock()
    {
        if (reportsSidebar != null || interfaceRoot == null)
        {
            RefreshReportsToggle();
            return;
        }
        reportsSidebar = interfaceRoot.Q(className: "shell-sidebar");
        if (reportsSidebar == null)
            return;
        reportsSidebar.AddToClassList("shell-sidebar--floating");
        reportsSidebar.AddToClassList(ReportsCollapsedClass);
        reportsSidebar.pickingMode = PickingMode.Ignore;
        reportsToggleButton = new Button(ToggleReportsDock) { name = "reports-toggle-button" };
        reportsToggleButton.AddToClassList("reports-toggle-button");
        reportsSidebar.Add(reportsToggleButton);
        // Поверх рабочей области (карты), но под экранами и окнами.
        reportsSidebar.BringToFront();
        reportsSeenCount = reportHistory.Count;
        RefreshReportsToggle();
    }

    private void ToggleReportsDock()
    {
        SetReportsDockExpanded(!reportsExpanded);
    }

    private void SetReportsDockExpanded(bool expanded)
    {
        if (reportsSidebar == null)
            return;
        reportsExpanded = expanded;
        reportsSidebar.EnableInClassList(ReportsCollapsedClass, !expanded);
        if (expanded)
        {
            reportsSeenCount = reportHistory.Count;
            ScheduleRoyalReportsRefresh();
        }
        RefreshReportsToggle();
    }

    private void RefreshReportsToggle()
    {
        if (reportsToggleButton == null)
            return;
        int fresh = System.Math.Max(0, reportHistory.Count - reportsSeenCount);
        if (reportsExpanded)
            reportsSeenCount = reportHistory.Count;
        string text = reportsExpanded ? "ДОНЕСЕНИЯ ▾" : fresh > 0 ? "ДОНЕСЕНИЯ ▴ · новых " + fresh : "ДОНЕСЕНИЯ ▴";
        if (reportsToggleButton.text != text)
            reportsToggleButton.text = text;
        reportsToggleButton.EnableInClassList("reports-toggle-button--fresh", !reportsExpanded && fresh > 0);
    }
}
