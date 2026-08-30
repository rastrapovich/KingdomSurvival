using System.Collections.Generic;
using UnityEngine;

public partial class PrototypeUIController
{
    private int shellReportHistoryHash = int.MinValue;

    private void LateUpdate()
    {
        RefreshRoyalReportsNewestFirst();
    }

    private void RefreshRoyalReportsNewestFirst()
    {
        if (reportHistoryLabel == null || reportHistory == null)
            return;

        int currentHash = 17;

        foreach (string entry in reportHistory)
            currentHash = currentHash * 31 + (entry != null ? entry.GetHashCode() : 0);

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
