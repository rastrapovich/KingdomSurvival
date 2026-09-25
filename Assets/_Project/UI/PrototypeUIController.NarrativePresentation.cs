using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private int narrativePresentationHistoryCount = -1;

    private void RefreshNarrativePresentationFrame()
    {
        if (!IsNarrativeDialogueActive ||
            narrativeHistoryContainer == null ||
            narrativeTextScroll == null)
        {
            narrativePresentationHistoryCount = -1;
            return;
        }

        int historyCount = narrativeHistory.Count;
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
        NarrativeDialogueRendering.ApplyReadHistoryOpacity(narrativeHistoryContainer);

        ScheduleNarrativeScrollToLatestGroup();
    }

    private void ApplyNarrativeLayoutPresentation()
    {
        NarrativeDialogueRendering.ApplyLayoutPresentation(
            narrativeDialogueOverlay, narrativeSpeakerLabel, narrativeRoleLabel,
            narrativeHistoryContainer, narrativeChoicesContainer, GetNarrativeScreenSize());
    }

    // Дополнение к инструкции "новое отображение пассивных наблюдений и
    // проверок" — "новый текст всегда появляется цельным": каждый top-level
    // child narrativeHistoryContainer теперь ровно одна группа истории
    // (PrototypeUIController.Narrative.cs — RenderNarrativeDialogueHistory/
    // BuildNarrativeHistoryGroupElement), поэтому последний child — это
    // всегда целиком новая группа, а не последний абзац внутри неё.
    //
    // Раньше здесь принудительно выставлялся verticalScroller.value =
    // highValue — то есть каждое обновление истории жёстко прокручивало в
    // самый низ, независимо от того, помещается ли новая группа в область
    // просмотра целиком. Это и было основной причиной того, что игрок видел
    // конец только что появившегося текста раньше начала (§3/§10 дополнения
    // к инструкции). ScrollTo сам по себе просто подводит видимую область к
    // target: если группа помещается — она становится видна целиком; если
    // не помещается — видна её верхняя граница, а не нижняя.
    private void ScheduleNarrativeScrollToLatestGroup()
    {
        if (narrativeTextScroll == null || narrativeHistoryContainer == null)
            return;

        narrativeTextScroll.schedule.Execute(() =>
        {
            ScrollNarrativeHistoryToLatestGroupStart();
            narrativeTextScroll.schedule.Execute(ScrollNarrativeHistoryToLatestGroupStart).StartingIn(1);
        });
    }

    private void ScrollNarrativeHistoryToLatestGroupStart()
    {
        if (narrativeTextScroll == null ||
            narrativeHistoryContainer == null ||
            narrativeHistoryContainer.childCount == 0 ||
            narrativeTextScroll.panel == null)
        {
            return;
        }

        VisualElement latestGroup = narrativeHistoryContainer.ElementAt(narrativeHistoryContainer.childCount - 1);
        if (latestGroup != null && latestGroup.panel != null)
            narrativeTextScroll.ScrollTo(latestGroup);
    }
}
