using System.Collections.Generic;

namespace KingdomSurvival.DialogueDatabase
{
    // Одна видимая реплика узла, уже прошедшая условия. Runtime выдаёт
    // такие блоки строго по одному presentation-шагу.
    //
    // Presentation-правило пассивных проверок (инструкция "новое отображение
    // пассивных наблюдений и проверок", §4/§18): провал пассивной проверки
    // больше не удаляет блок из production view — блок остаётся видимым
    // (игрок видит, что здесь можно было что-то заметить), но само тело
    // текста не раскрывается. Провал показывает наличие упущенной
    // возможности, а не её содержание.
    public sealed class NarrativeDialogueVisibleBlock
    {
        public string BlockId;
        public DialogueTextBlockKind Kind;
        public string SpeakerId;
        public string SpeakerDisplayName;
        public string SpeakerRole;

        // Пусто, если IsTextRevealed == false — production UI никогда не
        // использует скрытый body text (§5 инструкции).
        public string Text;

        // Заполнено, только если у блока есть пассивная проверка (§10
        // инструкции по визуализации проверок). Null — проверки не было.
        public NarrativeCheckPresentationData CheckPresentation;

        // true, если у блока нет пассивной проверки, либо проверка
        // пройдена — Text содержит настоящий текст и OnRevealEffects уже
        // применены. false — проверка провалена: Text пуст, эффекты не
        // применялись. Это единственный источник истины о том, раскрыт ли
        // текст; отдельного "только Preview"-состояния больше нет — провал
        // виден и в production, и в Preview одинаково (§15).
        public bool IsTextRevealed = true;

        // Заполняется только BuildViewPreview(revealHiddenTextForAuthor: true)
        // — авторский просмотр упущенного текста (§16 инструкции). Production
        // BuildView() никогда не заполняет это поле. Null, если текст и так
        // раскрыт или автор не запросил показ.
        public string PreviewOnlyHiddenText;
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

    // Вычисленное представление текущего шага узла: максимум одна реплика.
    // Если в авторском узле есть следующая подходящая реплика, список
    // AvailableChoices содержит только синтетический Continue; настоящие
    // ответы появляются после последней реплики.
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
