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

        int childCount = narrativeHistoryContainer.childCount;
        for (int i = 0; i < childCount; i++)
        {
            VisualElement entry = narrativeHistoryContainer.ElementAt(i);
            entry.style.opacity = i == childCount - 1 ? 1f : NarrativeReadHistoryOpacity;
        }

        ScheduleNarrativeScrollToLatestGroup();
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
                if (entry.ClassListContains("narrative-dialogue-history-player") ||
                    entry.ClassListContains("narrative-dialogue-history-check-result"))
                {
                    UILayoutRuntimeApplier.ApplyTextStyle(entry, choicesDefinition, reference, actual);
                    continue;
                }

                // Одна группа (§ дополнения "новый текст всегда появляется
                // цельным") может содержать несколько абзацев и несколько
                // говорящих — стилизовать нужно каждый найденный текстовый
                // Label, а не только первый.
                entry.Query<Label>(className: "narrative-dialogue-history-text")
                    .ForEach(textLabel => UILayoutRuntimeApplier.ApplyTextStyle(textLabel, textDefinition, reference, actual));
            }
        }

        if (narrativeChoicesContainer != null)
        {
            for (int i = 0; i < narrativeChoicesContainer.childCount; i++)
            {
                VisualElement choice = narrativeChoicesContainer.ElementAt(i);

                // Вторичные строки (механика/подсказка недоступности) не
                // являются кнопками ответа — у них собственный, более
                // мелкий стиль из USS, который не должен перебиваться
                // общим текстовым стилем варианта (§14).
                if (choice.ClassListContains("narrative-dialogue-choice-mechanic") ||
                    choice.ClassListContains("narrative-dialogue-choice-hint"))
                {
                    continue;
                }

                UILayoutRuntimeApplier.ApplyTextStyle(choice, choicesDefinition, reference, actual);
            }
        }
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
