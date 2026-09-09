using System.Collections.Generic;

namespace KingdomSurvival.DialogueDatabase
{
    // Дополнение к инструкции "новое отображение пассивных наблюдений и
    // проверок" — "новый текст всегда появляется цельным": один вызов
    // DisplayNarrativeView() (один NarrativeDialogueView + опциональный
    // результат активной проверки) есть одна неделимая порция чтения.
    //
    // Эта группировка — чистая презентационная логика без зависимости от
    // UI Toolkit, поэтому она вынесена сюда (не в PrototypeUIController) и
    // может проверяться EditMode-тестами напрямую. UI (игровой рантайм и
    // Editor Preview) строит из результата BuildSegments один визуальный
    // контейнер на группу; сама группировка не решает, где рисовать scroll
    // или opacity — только что с чем логически слито.
    public enum NarrativeUiSegmentKind
    {
        // Один говорящий, один или несколько абзацев подряд без пассивной
        // проверки (обычные MainLine/CompanionLine реплики).
        SpeakerParagraphs,

        // Observation/Memory/HeroThought/Narration с пассивной проверкой —
        // отображается инлайн-строкой без подписи говорящего.
        CheckedObservation,

        // Результат активной проверки (ведёт группу, если проверка была
        // разрешена выбором игрока) либо MainLine/CompanionLine с
        // прикреплённой пассивной проверкой — заголовок проверки без
        // склеенного текста.
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

        // Строит сегменты одной группы. leadingActiveCheck — результат
        // активной проверки, которая привела к этому view (null, если
        // переход был обычным); он всегда идёт первым отдельным сегментом,
        // если задан. blocks — NarrativeDialogueView.VisibleTextBlocks
        // одного и того же view: все они принадлежат одной группе по
        // определению (§1 инструкции), поэтому у BuildSegments нет
        // параметра "новая группа" — вызывающий код сам решает, когда
        // начинать новый вызов (один раз на DisplayNarrativeView).
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

            int i = 0;
            while (i < blocks.Count)
            {
                NarrativeDialogueVisibleBlock block = blocks[i];
                if (block == null)
                {
                    i++;
                    continue;
                }

                bool isCheckedObservation = block.CheckPresentation != null && IsObservationLikeKind(block.Kind);
                if (isCheckedObservation)
                {
                    NarrativeUiHistorySegment observation = new NarrativeUiHistorySegment
                    {
                        Kind = NarrativeUiSegmentKind.CheckedObservation,
                        SpeakerDisplayName = block.SpeakerDisplayName,
                        SpeakerRole = block.SpeakerRole,
                        CheckPresentation = block.CheckPresentation
                    };
                    observation.Paragraphs.Add(block.Text);
                    segments.Add(observation);
                    i++;
                    continue;
                }

                // Обычная реплика — либо самостоятельный CheckResult-заголовок
                // (редкий случай MainLine/CompanionLine с пассивной проверкой,
                // текст которой по-прежнему показывается отдельно — §3/§17
                // инструкции presentation пассивных проверок), либо начало
                // серии абзацев одного говорящего без проверки.
                if (block.CheckPresentation != null)
                {
                    NarrativeUiHistorySegment checkedMainLine = new NarrativeUiHistorySegment
                    {
                        Kind = NarrativeUiSegmentKind.SpeakerParagraphs,
                        SpeakerDisplayName = block.SpeakerDisplayName,
                        SpeakerRole = block.SpeakerRole,
                        CheckPresentation = block.CheckPresentation
                    };
                    checkedMainLine.Paragraphs.Add(block.Text);
                    segments.Add(checkedMainLine);
                    i++;
                    continue;
                }

                NarrativeUiHistorySegment segment = new NarrativeUiHistorySegment
                {
                    Kind = NarrativeUiSegmentKind.SpeakerParagraphs,
                    SpeakerDisplayName = block.SpeakerDisplayName,
                    SpeakerRole = block.SpeakerRole
                };
                segment.Paragraphs.Add(block.Text);
                i++;

                // Сливаем подряд идущие блоки того же говорящего без своей
                // проверки в один сегмент — один заголовок, несколько
                // абзацев (§5 дополнения к инструкции).
                while (i < blocks.Count)
                {
                    NarrativeDialogueVisibleBlock next = blocks[i];
                    if (next == null)
                        break;

                    bool nextIsCheckedObservation = next.CheckPresentation != null && IsObservationLikeKind(next.Kind);
                    if (nextIsCheckedObservation || next.CheckPresentation != null)
                        break;
                    if (next.SpeakerDisplayName != segment.SpeakerDisplayName || next.SpeakerRole != segment.SpeakerRole)
                        break;

                    segment.Paragraphs.Add(next.Text);
                    i++;
                }

                segments.Add(segment);
            }

            return segments;
        }
    }
}
