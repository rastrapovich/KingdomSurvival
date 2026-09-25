using UnityEngine.UIElements;

// Единый Narrative Check Presentation Layer поверх NarrativeCheckPresentationData:
// строка "КАЧЕСТВО [+ КОМПЕТЕНЦИЯ]: УСПЕХ/ПРОВАЛ [— текст]" с подробным
// tooltip по наведению/фокусу на слово результата. Построение элементов —
// общее с превью в редакторе (NarrativeDialogueRendering); здесь только
// привязка к постоянным узлам окна диалога игры. Ничего не пересчитывает
// проверку заново — только читает готовый NarrativeCheckPresentationData.
public partial class PrototypeUIController
{
    private VisualElement narrativeCheckTooltip;
    private NarrativeDialogueRendering.CheckTooltip narrativeCheckTooltipHost;

    // narrative-check-tooltip — постоянный узел Prototype_Main.uxml, привязан
    // в InitializeNarrativeDialogueUi; здесь он только наполняется.
    private NarrativeDialogueRendering.CheckTooltip NarrativeCheckTooltipHost =>
        narrativeCheckTooltipHost ??= new NarrativeDialogueRendering.CheckTooltip(narrativeCheckTooltip, narrativeDialogueOverlay);

    // Заголовок без прикреплённого текста — активные проверки и заголовок
    // над обычной репликой с пассивной проверкой.
    private VisualElement BuildNarrativeCheckHeaderElement(NarrativeCheckPresentationData data)
    {
        return BuildNarrativePassiveCheckLine(data, null);
    }

    // "ИСТОЧНИК: РЕЗУЛЬТАТ [— текст]": при провале третьего элемента нет —
    // провал показывает упущенную возможность, а не её содержание.
    private VisualElement BuildNarrativePassiveCheckLine(NarrativeCheckPresentationData data, string revealedText)
    {
        return NarrativeDialogueRendering.BuildCheckLine(data, revealedText, NarrativeCheckTooltipHost);
    }

    private void HideNarrativeCheckTooltip()
    {
        NarrativeCheckTooltipHost.Hide();
    }
}
