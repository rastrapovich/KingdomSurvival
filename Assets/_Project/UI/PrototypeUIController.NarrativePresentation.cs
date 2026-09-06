using KingdomSurvival.UILayout;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private const float NarrativeReadHistoryOpacity = 0.42f;
    private int narrativePresentationHistoryCount = -1;

    private void RefreshNarrativePresentationFrame()
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

        ApplyNarrativeLayoutPresentation();

        int childCount = narrativeHistoryContainer.childCount;
        for (int i = 0; i < childCount; i++)
        {
            VisualElement entry = narrativeHistoryContainer.ElementAt(i);
            entry.style.opacity = i == childCount - 1 ? 1f : NarrativeReadHistoryOpacity;
        }

        ScheduleNarrativeScrollToBottom();
    }

    private void ApplyNarrativeLayoutPresentation()
    {
        UILayoutDatabaseAsset database = UILayoutRuntimeApplier.LoadDefaultDatabase();
        UILayoutScreenDefinition layout = database != null ? database.FindScreen("narrative-dialogue") : null;
        if (database == null || layout == null)
            return;

        VisualElement screen = interfaceRoot != null
            ? interfaceRoot.Q<VisualElement>("screen")
            : null;
        float actualWidth = screen != null ? screen.resolvedStyle.width : 0f;
        float actualHeight = screen != null ? screen.resolvedStyle.height : 0f;
        if (actualWidth <= 0f || actualHeight <= 0f)
        {
            actualWidth = database.ReferenceResolution.x;
            actualHeight = database.ReferenceResolution.y;
        }

        Vector2 reference = database.ReferenceResolution;
        Vector2 actual = new Vector2(actualWidth, actualHeight);
        UILayoutElementDefinition speakerDefinition = layout.FindElement("speaker");
        UILayoutElementDefinition roleDefinition = layout.FindElement("role");
        UILayoutElementDefinition textDefinition = layout.FindElement("text");
        UILayoutElementDefinition choicesDefinition = layout.FindElement("choices");

        UILayoutRuntimeApplier.ApplyDimming(narrativeDialogueOverlay, layout);
        UILayoutRuntimeApplier.ApplyTextStyle(narrativeSpeakerLabel, speakerDefinition, reference, actual);
        UILayoutRuntimeApplier.ApplyTextStyle(narrativeRoleLabel, roleDefinition, reference, actual);

        if (narrativeHistoryContainer != null)
        {
            for (int i = 0; i < narrativeHistoryContainer.childCount; i++)
            {
                VisualElement entry = narrativeHistoryContainer.ElementAt(i);
                if (entry.ClassListContains("narrative-dialogue-history-player"))
                {
                    UILayoutRuntimeApplier.ApplyTextStyle(entry, choicesDefinition, reference, actual);
                    continue;
                }

                Label textLabel = entry.Q<Label>(className: "narrative-dialogue-history-text");
                if (textLabel != null)
                    UILayoutRuntimeApplier.ApplyTextStyle(textLabel, textDefinition, reference, actual);
            }
        }

        if (narrativeChoicesContainer != null)
        {
            for (int i = 0; i < narrativeChoicesContainer.childCount; i++)
            {
                VisualElement choice = narrativeChoicesContainer.ElementAt(i);
                UILayoutRuntimeApplier.ApplyTextStyle(choice, choicesDefinition, reference, actual);
            }
        }
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
