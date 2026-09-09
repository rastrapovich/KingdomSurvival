using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

// Единый Narrative Check Presentation Layer поверх NarrativeCheckPresentationData:
// заголовок "КАЧЕСТВО [+ КОМПЕТЕНЦИЯ]   УСПЕХ/ПРОВАЛ" с подробным tooltip по
// наведению/фокусу на слово результата. Используется и пассивными блоками,
// и активными проверками (§12/§13/§14 инструкции по визуализации проверок).
// Ничего здесь не пересчитывает проверку заново — только читает уже готовый
// NarrativeCheckPresentationData (§15: "hover только читает готовый результат").
public partial class PrototypeUIController
{
    private VisualElement narrativeCheckTooltip;

    private VisualElement BuildNarrativeCheckHeaderElement(NarrativeCheckPresentationData data)
    {
        VisualElement header = new VisualElement();
        header.AddToClassList("narrative-check-header");

        Label source = new Label(NarrativeCheckPresentationBuilder.BuildSourceLabel(data).ToUpperInvariant());
        source.AddToClassList("narrative-check-source");
        header.Add(source);

        Label result = new Label(data.Success ? "УСПЕХ" : "ПРОВАЛ") { focusable = true };
        result.AddToClassList("narrative-check-result");
        result.AddToClassList(data.Success ? "narrative-check-result--success" : "narrative-check-result--failure");
        result.pickingMode = PickingMode.Position;
        result.RegisterCallback<PointerEnterEvent>(_ => ShowNarrativeCheckTooltip(result, data));
        result.RegisterCallback<PointerLeaveEvent>(_ => HideNarrativeCheckTooltip());
        result.RegisterCallback<FocusInEvent>(_ => ShowNarrativeCheckTooltip(result, data));
        result.RegisterCallback<FocusOutEvent>(_ => HideNarrativeCheckTooltip());
        header.Add(result);

        return header;
    }

    private void EnsureNarrativeCheckTooltip()
    {
        if (narrativeCheckTooltip != null && narrativeCheckTooltip.parent != null)
            return;

        narrativeCheckTooltip = new VisualElement { name = "narrative-check-tooltip", pickingMode = PickingMode.Ignore };
        narrativeCheckTooltip.AddToClassList("narrative-check-tooltip");
        narrativeCheckTooltip.style.display = DisplayStyle.None;

        // Поверх narrative overlay, а не внутри ScrollView текста — иначе
        // tooltip будет обрезан скроллом (§14).
        narrativeDialogueOverlay.Add(narrativeCheckTooltip);
    }

    private void ShowNarrativeCheckTooltip(VisualElement anchor, NarrativeCheckPresentationData data)
    {
        if (narrativeDialogueOverlay == null || data == null)
            return;

        EnsureNarrativeCheckTooltip();
        narrativeCheckTooltip.Clear();
        BuildNarrativeCheckTooltipContent(narrativeCheckTooltip, data);
        narrativeCheckTooltip.style.display = DisplayStyle.Flex;
        narrativeCheckTooltip.schedule.Execute(() => PositionNarrativeCheckTooltip(anchor));
    }

    private void HideNarrativeCheckTooltip()
    {
        if (narrativeCheckTooltip != null)
            narrativeCheckTooltip.style.display = DisplayStyle.None;
    }

    // Позиция привязана к слову УСПЕХ/ПРОВАЛ и ограничена границами overlay
    // (§14): справа не помещается — слева; снизу не помещается — сдвиг вверх.
    private void PositionNarrativeCheckTooltip(VisualElement anchor)
    {
        if (narrativeCheckTooltip == null || narrativeDialogueOverlay == null || anchor.panel == null)
            return;

        Rect anchorWorld = anchor.worldBound;
        Rect overlayWorld = narrativeDialogueOverlay.worldBound;
        if (overlayWorld.width <= 0f || overlayWorld.height <= 0f)
            return;

        float tooltipWidth = narrativeCheckTooltip.resolvedStyle.width;
        float tooltipHeight = narrativeCheckTooltip.resolvedStyle.height;
        if (tooltipWidth <= 0f)
            tooltipWidth = 320f;
        if (tooltipHeight <= 0f)
            tooltipHeight = 160f;

        float anchorLeftLocal = anchorWorld.x - overlayWorld.x;
        float anchorRightLocal = anchorWorld.xMax - overlayWorld.x;
        float anchorTopLocal = anchorWorld.y - overlayWorld.y;

        float left = anchorRightLocal + 8f;
        if (left + tooltipWidth > overlayWorld.width)
            left = anchorLeftLocal - tooltipWidth - 8f;
        if (left < 4f)
            left = 4f;

        float top = anchorTopLocal;
        if (top + tooltipHeight > overlayWorld.height)
            top = overlayWorld.height - tooltipHeight - 4f;
        if (top < 4f)
            top = 4f;

        narrativeCheckTooltip.style.left = left;
        narrativeCheckTooltip.style.top = top;
    }

    // Показывает только то, что реально участвовало в расчёте (§5/§15):
    // модификаторы без выполненного условия сюда не попадают, потому что
    // их вообще нет в data.AppliedModifiers — это уже сделано в Core при
    // построении NarrativeCheckResult.
    private static void BuildNarrativeCheckTooltipContent(VisualElement container, NarrativeCheckPresentationData data)
    {
        Label title = new Label(data.Kind == NarrativeCheckKind.Passive ? "ПАССИВНАЯ ПРОВЕРКА" : "АКТИВНАЯ ПРОВЕРКА");
        title.AddToClassList("narrative-check-tooltip-title");
        container.Add(title);

        AddTooltipTextRow(container, "Сложность: " + NarrativeDifficultyLabels.Describe(data.Difficulty));
        AddTooltipDivider(container);

        if (data.Kind == NarrativeCheckKind.Passive)
            AddTooltipValueRow(container, "База пассивной проверки", data.PassiveBase, forceSign: false);
        else
            AddTooltipTextRow(container, "Кубики: " + data.DieOne + " + " + data.DieTwo + " = " + (data.DieOne + data.DieTwo));

        AddTooltipValueRow(container, data.QualityLabel, data.QualityValue, forceSign: false);
        if (data.HasCompetency)
            AddTooltipValueRow(container, data.CompetencyLabel, data.CompetencyValue, forceSign: false);

        if (data.AppliedModifiers != null && data.AppliedModifiers.Count > 0)
        {
            AddTooltipDivider(container);
            AddTooltipTextRow(container, "Контекст:");
            for (int i = 0; i < data.AppliedModifiers.Count; i++)
            {
                NarrativeAppliedModifierSnapshot modifier = data.AppliedModifiers[i];
                if (modifier == null)
                    continue;
                AddTooltipValueRow(container, "  " + modifier.Label, modifier.Value, forceSign: true);
            }

            if (data.RawContextModifier != data.AppliedContextModifier)
            {
                AddTooltipValueRow(container, "Сумма модификаторов", data.RawContextModifier, forceSign: true);
                AddTooltipValueRow(container, "Применено", data.AppliedContextModifier, forceSign: true);
                AddTooltipTextRow(container, "Лимит контекста: ±" + NarrativeCheckMath.MaxContextModifier.ToString(CultureInfo.InvariantCulture));
            }
        }

        AddTooltipDivider(container);

        AddTooltipTotalRow(container, "Итого: " + data.Total.ToString(CultureInfo.InvariantCulture));
        AddTooltipTotalRow(container, "Нужно: " + data.Difficulty.ToString(CultureInfo.InvariantCulture));

        string comparator = data.Total >= data.Difficulty ? " ≥ " : " < ";
        Label verdict = new Label(
            data.Total.ToString(CultureInfo.InvariantCulture) + comparator + data.Difficulty.ToString(CultureInfo.InvariantCulture) +
            " — " + (data.Success ? "УСПЕХ" : "ПРОВАЛ"));
        verdict.AddToClassList("narrative-check-tooltip-total");
        verdict.AddToClassList(data.Success ? "narrative-check-result--success" : "narrative-check-result--failure");
        container.Add(verdict);
    }

    private static void AddTooltipTextRow(VisualElement container, string text)
    {
        Label row = new Label(text);
        row.AddToClassList("narrative-check-tooltip-row");
        container.Add(row);
    }

    private static void AddTooltipValueRow(VisualElement container, string label, int value, bool forceSign)
    {
        string valueText = forceSign
            ? (value >= 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture))
            : value.ToString(CultureInfo.InvariantCulture);
        AddTooltipTextRow(container, label + ": " + valueText);
    }

    private static void AddTooltipTotalRow(VisualElement container, string text)
    {
        Label row = new Label(text);
        row.AddToClassList("narrative-check-tooltip-total");
        container.Add(row);
    }

    private static void AddTooltipDivider(VisualElement container)
    {
        VisualElement divider = new VisualElement();
        divider.AddToClassList("narrative-check-tooltip-divider");
        container.Add(divider);
    }
}
