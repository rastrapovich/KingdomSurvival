using System.Collections.Generic;

namespace KingdomSurvival.DialogueDatabase
{
    // Один видимый текстовый блок узла, уже прошедший условия и (если есть)
    // пассивную проверку. См. §12 инструкции: "NarrativeDialogueView.VisibleTextBlocks".
    public sealed class NarrativeDialogueVisibleBlock
    {
        public string BlockId;
        public DialogueTextBlockKind Kind;
        public string SpeakerId;
        public string SpeakerDisplayName;
        public string SpeakerRole;
        public string Text;

        // Заполнено, только если у блока есть пассивная проверка (§10
        // инструкции по визуализации проверок). Null — проверки не было.
        // В production-runtime блок с провалившейся проверкой сюда вообще
        // не попадает — см. IsPassiveFailurePreviewOnly.
        public NarrativeCheckPresentationData CheckPresentation;

        // true только для Preview (§17): проверка провалилась и такой блок
        // никогда не появился бы в BuildView() игрового runtime.
        public bool IsPassiveFailurePreviewOnly;
    }

    public sealed class NarrativeDialogueChoiceView
    {
        public string ChoiceId;
        public string Text;
        public DialogueChoiceKind Kind;

        // Доступен для нажатия прямо сейчас.
        public bool IsAvailable;

        // Показан отключённым с причиной (DisabledWithHint), а не скрыт.
        public bool IsDisabledWithHint;
        public string DisabledHint;

        // Заполнено только для ActiveReturnable/ActiveDecisive.
        public double? SuccessProbability;

        // Готовая вторая строка вида "Чутьё + Следопытство • 72% • возвратная" (§14).
        // Пусто для Normal/Exit — у них нет механической строки.
        public string MechanicalSummary;
    }

    // Вычисленное представление текущего узла: что видно игроку и какие
    // ответы доступны/заблокированы. См. §12.
    public sealed class NarrativeDialogueView
    {
        public string DialogueId;
        public string NodeId;
        public IReadOnlyList<NarrativeDialogueVisibleBlock> VisibleTextBlocks;
        public IReadOnlyList<NarrativeDialogueChoiceView> AvailableChoices;
        public IReadOnlyList<NarrativeDialogueChoiceView> DisabledChoices;
    }

    public sealed class NarrativeDialogueSelectionResult
    {
        public bool DialogueEnded;

        // Заполнено только при выборе активной проверки (Normal/Exit — null).
        public NarrativeCheckResult CheckResult;

        // Presentation-снимок того же результата (§12: активные проверки
        // используют тот же компонент отображения, что и пассивные).
        public NarrativeCheckPresentationData CheckPresentation;

        // Null, если разговор завершился этим выбором.
        public NarrativeDialogueView View;
    }
}
