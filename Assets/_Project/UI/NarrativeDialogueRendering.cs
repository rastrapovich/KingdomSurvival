using System.Collections.Generic;
using System.Globalization;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.UILayout;
using UnityEngine;
using UnityEngine.UIElements;

// Общая отрисовка окна диалога — одна для игры (PrototypeUIController.
// Narrative*.cs) и для превью в редакторе базы диалогов
// (UI/Editor/DialogueGamePreviewWindow.cs): группа истории, строка проверки
// «ИСТОЧНИК: УСПЕХ/ПРОВАЛ [— текст]», подсказка проверки, стили текста из
// UI Конструктора и портрет/иллюстрация. Здесь только построение элементов
// по готовым данным — состояние разговора и выбор ответа остаются у
// вызывающего кода. Постоянная структура окна живёт в Prototype_Main.uxml.
public static class NarrativeDialogueRendering
{
    public const float ReadHistoryOpacity = 0.42f;

    // Подсказка проверки: постоянный узел narrative-check-tooltip поверх
    // overlay (не внутри ScrollView — иначе обрежется прокруткой).
    public sealed class CheckTooltip
    {
        private readonly VisualElement tooltip;
        private readonly VisualElement overlay;

        public CheckTooltip(VisualElement tooltip, VisualElement overlay)
        {
            this.tooltip = tooltip;
            this.overlay = overlay;
        }

        public void Show(VisualElement anchor, NarrativeCheckPresentationData data)
        {
            if (tooltip == null || data == null)
                return;

            tooltip.Clear();
            BuildCheckTooltipContent(tooltip, data);
            tooltip.style.display = DisplayStyle.Flex;
            tooltip.schedule.Execute(() => Position(anchor));
        }

        public void Hide()
        {
            if (tooltip != null)
                tooltip.style.display = DisplayStyle.None;
        }

        // Позиция привязана к слову УСПЕХ/ПРОВАЛ и ограничена границами overlay:
        // справа не помещается — слева; снизу не помещается — сдвиг вверх.
        private void Position(VisualElement anchor)
        {
            if (tooltip == null || overlay == null || anchor.panel == null)
                return;

            Rect anchorWorld = anchor.worldBound;
            Rect overlayWorld = overlay.worldBound;
            if (overlayWorld.width <= 0f || overlayWorld.height <= 0f)
                return;

            float tooltipWidth = tooltip.resolvedStyle.width;
            float tooltipHeight = tooltip.resolvedStyle.height;
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

            tooltip.style.left = left;
            tooltip.style.top = top;
        }
    }

    // ------------------------------------------------------------------
    // История
    // ------------------------------------------------------------------

    // Один presentation-шаг: сегменты считает NarrativeUiHistoryGrouping
    // (чистая логика), здесь — только раскладка по VisualElement.
    public static VisualElement BuildHistoryGroup(
        NarrativeCheckPresentationData leadingActiveCheck,
        IReadOnlyList<NarrativeDialogueVisibleBlock> blocks,
        CheckTooltip tooltip)
    {
        VisualElement group = new VisualElement();
        group.AddToClassList("narrative-dialogue-history-group");

        List<NarrativeUiHistorySegment> segments = NarrativeUiHistoryGrouping.BuildSegments(leadingActiveCheck, blocks);
        for (int i = 0; i < segments.Count; i++)
        {
            NarrativeUiHistorySegment segment = segments[i];

            if (segment.Kind == NarrativeUiSegmentKind.CheckResult)
            {
                VisualElement checkLine = new VisualElement();
                checkLine.AddToClassList("narrative-dialogue-history-check-result");
                checkLine.Add(BuildCheckLine(segment.CheckPresentation, null, tooltip));
                group.Add(checkLine);
                continue;
            }

            // Наблюдение с проверкой — одна строка «ИСТОЧНИК: РЕЗУЛЬТАТ — текст»
            // без подписи говорящего: это наблюдение героя, а не реплика NPC.
            if (segment.Kind == NarrativeUiSegmentKind.CheckedObservation)
            {
                VisualElement observationBlock = new VisualElement();
                observationBlock.AddToClassList("narrative-dialogue-history-entry");
                string revealedText = segment.Paragraphs.Count > 0 ? segment.Paragraphs[0] : string.Empty;
                observationBlock.Add(BuildCheckLine(segment.CheckPresentation, revealedText, tooltip));
                group.Add(observationBlock);
                continue;
            }

            // Обычная реплика — подпись говорящего один раз, затем абзацы.
            VisualElement block = new VisualElement();
            block.AddToClassList("narrative-dialogue-history-entry");

            if (segment.CheckPresentation != null)
                block.Add(BuildCheckLine(segment.CheckPresentation, null, tooltip));

            Label speaker = new Label(segment.SpeakerDisplayName);
            speaker.AddToClassList("narrative-dialogue-history-speaker");
            block.Add(speaker);

            for (int p = 0; p < segment.Paragraphs.Count; p++)
            {
                Label text = new Label(segment.Paragraphs[p]);
                text.AddToClassList("narrative-dialogue-history-text");
                block.Add(text);
            }

            group.Add(block);
        }

        return group;
    }

    // «ИСТОЧНИК: РЕЗУЛЬТАТ [— текст]». Тире и текст — только при раскрытом
    // тексте; при провале третьего элемента нет вовсе.
    public static VisualElement BuildCheckLine(NarrativeCheckPresentationData data, string revealedText, CheckTooltip tooltip)
    {
        VisualElement line = new VisualElement();
        line.AddToClassList("narrative-check-header");
        line.AddToClassList("narrative-check-inline");

        Label source = new Label(NarrativeCheckPresentationBuilder.BuildSourceLabel(data).ToUpperInvariant() + ":");
        source.AddToClassList("narrative-check-source");
        line.Add(source);

        Label result = new Label(data.Success ? "УСПЕХ" : "ПРОВАЛ") { focusable = true };
        result.AddToClassList("narrative-check-result");
        result.AddToClassList(data.Success ? "narrative-check-result--success" : "narrative-check-result--failure");
        result.pickingMode = PickingMode.Position;
        if (tooltip != null)
        {
            result.RegisterCallback<PointerEnterEvent>(_ => tooltip.Show(result, data));
            result.RegisterCallback<PointerLeaveEvent>(_ => tooltip.Hide());
            result.RegisterCallback<FocusInEvent>(_ => tooltip.Show(result, data));
            result.RegisterCallback<FocusOutEvent>(_ => tooltip.Hide());
        }
        line.Add(result);

        if (!string.IsNullOrEmpty(revealedText))
        {
            Label body = new Label("— " + revealedText);
            body.AddToClassList("narrative-dialogue-history-text");
            body.AddToClassList("narrative-check-inline-body");
            line.Add(body);
        }

        return line;
    }

    // Прочитанные шаги приглушены, последний — во всю яркость.
    public static void ApplyReadHistoryOpacity(VisualElement historyContainer)
    {
        if (historyContainer == null)
            return;
        int childCount = historyContainer.childCount;
        for (int i = 0; i < childCount; i++)
            historyContainer.ElementAt(i).style.opacity = i == childCount - 1 ? 1f : ReadHistoryOpacity;
    }

    // ------------------------------------------------------------------
    // Подсказка проверки: только то, что реально участвовало в расчёте.
    // ------------------------------------------------------------------

    public static void BuildCheckTooltipContent(VisualElement container, NarrativeCheckPresentationData data)
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

    // ------------------------------------------------------------------
    // Стили текста и затемнение из UI Конструктора (экран "narrative-dialogue").
    // ------------------------------------------------------------------

    public static void ApplyLayoutPresentation(
        VisualElement overlay,
        Label speakerLabel,
        Label roleLabel,
        VisualElement historyContainer,
        VisualElement choicesContainer,
        Vector2 actualSize)
    {
        UILayoutDatabaseAsset database = UILayoutRuntimeApplier.LoadDefaultDatabase();
        UILayoutScreenDefinition layout = database != null ? database.FindScreen(UILayoutDatabaseAsset.NarrativeDialogueScreenId) : null;
        if (database == null || layout == null)
            return;

        Vector2 reference = database.ReferenceResolution;
        Vector2 actual = actualSize.x > 0f && actualSize.y > 0f ? actualSize : reference;
        UILayoutElementDefinition speakerDefinition = layout.FindElement("speaker");
        UILayoutElementDefinition roleDefinition = layout.FindElement("role");
        UILayoutElementDefinition textDefinition = layout.FindElement("text");
        UILayoutElementDefinition choicesDefinition = layout.FindElement("choices");

        UILayoutRuntimeApplier.ApplyDimming(overlay, layout);
        UILayoutRuntimeApplier.ApplyTextStyle(speakerLabel, speakerDefinition, reference, actual);
        UILayoutRuntimeApplier.ApplyTextStyle(roleLabel, roleDefinition, reference, actual);

        if (historyContainer != null)
        {
            for (int i = 0; i < historyContainer.childCount; i++)
            {
                VisualElement entry = historyContainer.ElementAt(i);
                if (entry.ClassListContains("narrative-dialogue-history-player") ||
                    entry.ClassListContains("narrative-dialogue-history-check-result"))
                {
                    UILayoutRuntimeApplier.ApplyTextStyle(entry, choicesDefinition, reference, actual);
                    continue;
                }

                // Группа может содержать несколько абзацев и говорящих —
                // стилизуется каждый текстовый Label.
                entry.Query<Label>(className: "narrative-dialogue-history-text")
                    .ForEach(textLabel => UILayoutRuntimeApplier.ApplyTextStyle(textLabel, textDefinition, reference, actual));
            }
        }

        if (choicesContainer != null)
        {
            for (int i = 0; i < choicesContainer.childCount; i++)
            {
                VisualElement choice = choicesContainer.ElementAt(i);

                // Вторичные строки (механика/подсказка недоступности) — не
                // кнопки ответа: у них собственный мелкий стиль из USS.
                if (choice.ClassListContains("narrative-dialogue-choice-mechanic") ||
                    choice.ClassListContains("narrative-dialogue-choice-hint"))
                {
                    continue;
                }

                UILayoutRuntimeApplier.ApplyTextStyle(choice, choicesDefinition, reference, actual);
            }
        }
    }

    // ------------------------------------------------------------------
    // Портрет говорящего или иллюстрация события на всю сцену.
    // ------------------------------------------------------------------

    public static void ApplySpeakerPortrait(
        VisualElement portraitElement,
        Label placeholder,
        DialogueDatabaseAsset database,
        string dialogueId,
        string speakerId,
        Vector2 actualSize)
    {
        if (portraitElement == null)
            return;

        DialogueDefinitionData dialogue = database != null ? database.FindDialogue(dialogueId) : null;
        bool showSceneIllustration = dialogue != null && dialogue.SceneIllustration != null;
        DialogueSpeakerData speaker = !showSceneIllustration && database != null
            ? database.FindSpeaker(speakerId)
            : null;
        Sprite portrait = showSceneIllustration ? dialogue.SceneIllustration
            : speaker != null ? speaker.Portrait : null;
        if (portrait == null)
        {
            UILayoutRuntimeApplier.ClearDynamicImage(portraitElement);
            if (placeholder != null)
                placeholder.style.display = DisplayStyle.Flex;
            return;
        }

        UILayoutDatabaseAsset layoutDatabase = UILayoutRuntimeApplier.LoadDefaultDatabase();
        UILayoutScreenDefinition dialogueLayout = layoutDatabase != null
            ? layoutDatabase.FindScreen(UILayoutDatabaseAsset.NarrativeDialogueScreenId)
            : null;
        UILayoutElementDefinition portraitDefinition = dialogueLayout != null
            ? dialogueLayout.FindElement("portrait")
            : null;

        Vector2 reference = layoutDatabase != null
            ? (Vector2)layoutDatabase.ReferenceResolution
            : new Vector2(1920f, 1080f);
        Vector2 actual = actualSize.x > 0f && actualSize.y > 0f ? actualSize : reference;

        bool useIndividualFraming = speaker != null && speaker.OverridePortraitFraming;
        float imageScale = showSceneIllustration ? dialogue.SceneIllustrationScale
            : useIndividualFraming ? speaker.PortraitScale : 1f;
        Vector2 imageOffset = showSceneIllustration ? dialogue.SceneIllustrationOffsetNormalized
            : useIndividualFraming ? speaker.PortraitOffsetNormalized : Vector2.zero;
        bool flipX = !showSceneIllustration && useIndividualFraming && speaker.PortraitFlipX;
        UILayoutImageMode? illustrationMode = showSceneIllustration
            ? (dialogue.SceneIllustrationFillFrame ? UILayoutImageMode.Cover : UILayoutImageMode.Contain)
            : (UILayoutImageMode?)null;

        UILayoutRuntimeApplier.ApplyDynamicImage(
            portraitElement,
            portrait,
            portraitDefinition,
            reference,
            actual,
            imageScale,
            imageOffset,
            flipX,
            illustrationMode);

        if (placeholder != null)
            placeholder.style.display = DisplayStyle.None;
    }
}
