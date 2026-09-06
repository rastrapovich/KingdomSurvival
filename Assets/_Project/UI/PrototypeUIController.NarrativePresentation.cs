using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private const float NarrativeReadHistoryOpacity = 0.42f;
    private int narrativePresentationHistoryCount = -1;

    private void LateUpdate()
    {
        if (!IsNarrativeDialogueActive ||
            narrativeDialogueSession == null ||
            narrativeHistoryContainer == null ||
            narrativeTextScroll == null)
        {
            narrativePresentationHistoryCount = -1;
            return;
        }

        int historyCount = narrativeDialogueSession.History.Count;
        int renderedCount = narrativeHistoryContainer.childCount;
        if (historyCount <= 0 || renderedCount <= 0)
            return;

        if (historyCount == narrativePresentationHistoryCount && renderedCount == historyCount)
            return;

        narrativePresentationHistoryCount = historyCount;
        RefreshNarrativeHistoryPresentation();
    }

    private void RefreshNarrativeHistoryPresentation()
    {
        if (narrativeHistoryContainer == null || narrativeTextScroll == null)
            return;

        int childCount = narrativeHistoryContainer.childCount;
        for (int i = 0; i < childCount; i++)
        {
            VisualElement entry = narrativeHistoryContainer.ElementAt(i);
            entry.style.opacity = i == childCount - 1 ? 1f : NarrativeReadHistoryOpacity;
        }

        ScheduleNarrativeScrollToBottom();
    }

    private void ScheduleNarrativeScrollToBottom()
    {
        if (narrativeTextScroll == null || narrativeHistoryContainer == null)
            return;

        narrativeTextScroll.schedule.Execute(() =>
        {
            ForceNarrativeScrollToBottom();
            narrativeTextScroll.schedule.Execute(ForceNarrativeScrollToBottom).StartingIn(1);
        });
    }

    private void ForceNarrativeScrollToBottom()
    {
        if (narrativeTextScroll == null ||
            narrativeHistoryContainer == null ||
            narrativeHistoryContainer.childCount == 0 ||
            narrativeTextScroll.panel == null)
        {
            return;
        }

        VisualElement latestEntry = narrativeHistoryContainer.ElementAt(narrativeHistoryContainer.childCount - 1);
        if (latestEntry != null && latestEntry.panel != null)
            narrativeTextScroll.ScrollTo(latestEntry);

        Scroller verticalScroller = narrativeTextScroll.verticalScroller;
        if (verticalScroller != null)
            verticalScroller.value = verticalScroller.highValue;
    }
}
