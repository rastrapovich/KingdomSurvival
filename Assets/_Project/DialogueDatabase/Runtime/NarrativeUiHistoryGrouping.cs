using System;
using System.Collections.Generic;

namespace KingdomSurvival.DialogueDatabase
{
    // Каноническая presentation-граница: один NarrativeDialogueView — одна
    // реплика (не больше одного VisibleTextBlock) плюс опциональный результат
    // активной проверки, который к ней привёл. Несколько говорящих или
    // несколько фраз в одном view считаются ошибкой runtime-контракта.
    //
    // Эта группировка — чистая презентационная логика без зависимости от
    // UI Toolkit, поэтому она вынесена сюда (не в PrototypeUIController) и
    // может проверяться EditMode-тестами напрямую. UI (игровой рантайм и
    // Editor Preview) строит из результата BuildSegments один визуальный
    // контейнер на группу; сама группировка не решает, где рисовать scroll
    // или opacity — только что с чем логически слито.
    public enum NarrativeUiSegmentKind
    {
        // Одна реплика без пассивной проверки (MainLine/CompanionLine).
        SpeakerParagraphs,

        // Observation/Memory/HeroThought/Narration с пассивной проверкой —
        // отображается инлайн-строкой без подписи говорящего.
        CheckedObservation,

        // Результат активной проверки, который ведёт шаг после выбора
        // игрока. Пассивная проверка остаётся частью собственной реплики.
        CheckResult
    }

    public sealed class NarrativeUiHistorySegment
    {
        public NarrativeUiSegmentKind Kind;
        public string SpeakerDisplayName = string.Empty;
        public string SpeakerRole = string.Empty;
        public readonly List<string> Paragraphs = new List<string>();
        public NarrativeCheckPresentationData CheckPresentation;
    }

    public static class NarrativeUiHistoryGrouping
    {
        public static bool IsObservationLikeKind(DialogueTextBlockKind kind)
        {
            return kind == DialogueTextBlockKind.Observation ||
                   kind == DialogueTextBlockKind.Memory ||
                   kind == DialogueTextBlockKind.HeroThought ||
                   kind == DialogueTextBlockKind.Narration;
        }

        // Строит сегменты одного шага. leadingActiveCheck — результат
        // активной проверки, которая привела к этому view (null при обычном
        // переходе); он всегда идёт первым отдельным сегментом. blocks —
        // NarrativeDialogueView.VisibleTextBlocks, где канонически допустим
        // максимум один элемент.
        public static List<NarrativeUiHistorySegment> BuildSegments(
            NarrativeCheckPresentationData leadingActiveCheck,
            IReadOnlyList<NarrativeDialogueVisibleBlock> blocks)
        {
            List<NarrativeUiHistorySegment> segments = new List<NarrativeUiHistorySegment>();

            if (leadingActiveCheck != null)
            {
                segments.Add(new NarrativeUiHistorySegment
                {
                    Kind = NarrativeUiSegmentKind.CheckResult,
                    CheckPresentation = leadingActiveCheck
                });
            }

            if (blocks == null)
                return segments;

            if (blocks.Count > 1)
            {
                throw new InvalidOperationException(
                    "Канонический шаг диалога не может содержать больше одной реплики.");
            }

            if (blocks.Count == 0 || blocks[0] == null)
                return segments;

            NarrativeDialogueVisibleBlock block = blocks[0];
            bool isCheckedObservation = block.CheckPresentation != null && IsObservationLikeKind(block.Kind);
            NarrativeUiHistorySegment segment = new NarrativeUiHistorySegment
            {
                Kind = isCheckedObservation
                    ? NarrativeUiSegmentKind.CheckedObservation
                    : NarrativeUiSegmentKind.SpeakerParagraphs,
                SpeakerDisplayName = block.SpeakerDisplayName,
                SpeakerRole = block.SpeakerRole,
                CheckPresentation = block.CheckPresentation
            };
            segment.Paragraphs.Add(block.Text);
            segments.Add(segment);

            return segments;
        }
    }
}
