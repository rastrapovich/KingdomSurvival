using System;
using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
    public enum Chapter01RepairChoice
    {
        None,
        Old,
        New
    }

    // Узкий контроллер позвоночника N01-N17 (раздел 6.1/6.2 инструкции).
    // Не универсальный QuestManager: читает NarrativeState, не хранит вторую
    // копию прогресса, не перемещает отряд по карте — только определяет,
    // какой диалог открыть следующим.
    //
    // Текущая последовательность — черновой линейный каркас P03 ("Готово
    // когда" раздела 8: граф проходим от N01 до N17 в обеих ветках ремонта
    // без тупика). Свободный порядок N07A/B/C и осмысленные комбинации
    // знаний для перехода к N09 (раздел 14) — отдельные задачи P07-T05 и
    // P08-T02, которые заменят линейную часть этой последовательности, не
    // меняя публичный контракт GetNextDialogueId/TryAdvance.
    public static class Chapter01StoryDirector
    {
        private readonly struct NodeStep
        {
            public readonly string NodeId;
            public readonly string DialogueId;
            public readonly string[] CompletionFlags;

            public NodeStep(string nodeId, string dialogueId, params string[] completionFlags)
            {
                NodeId = nodeId;
                DialogueId = dialogueId;
                CompletionFlags = completionFlags ?? Array.Empty<string>();
            }
        }

        private static readonly NodeStep[] Sequence =
        {
            new NodeStep(Chapter01Ids.Nodes.N01, Chapter01Ids.Dialogues.D01, Chapter01Ids.Flags.HomeIntroSeen),
            new NodeStep(Chapter01Ids.Nodes.N02, Chapter01Ids.Dialogues.D02, Chapter01Ids.Flags.HousePeopleMet),
            new NodeStep(Chapter01Ids.Nodes.N03, Chapter01Ids.Dialogues.D03, Chapter01Ids.Flags.FirstPressureSeen),
            new NodeStep(Chapter01Ids.Nodes.N04, Chapter01Ids.Dialogues.D04, Chapter01Ids.Flags.FloodHappened),
            new NodeStep(Chapter01Ids.Nodes.N05, Chapter01Ids.Dialogues.D05, Chapter01Ids.Flags.RepairOld, Chapter01Ids.Flags.RepairNew),
            new NodeStep(Chapter01Ids.Nodes.N06, Chapter01Ids.Dialogues.D06, Chapter01Ids.Flags.WaterWrongActive),
            new NodeStep(Chapter01Ids.Nodes.N07A, Chapter01Ids.Dialogues.D07A, Chapter01Ids.Flags.InvestigatedMill),
            new NodeStep(Chapter01Ids.Nodes.N07B, Chapter01Ids.Dialogues.D07B, Chapter01Ids.Flags.InvestigatedCattle),
            new NodeStep(Chapter01Ids.Nodes.N07C, Chapter01Ids.Dialogues.D07C, Chapter01Ids.Flags.InvestigatedRiver),
            new NodeStep(Chapter01Ids.Nodes.N08, Chapter01Ids.Dialogues.D08, Chapter01Ids.Flags.OldTraceFound),
            new NodeStep(Chapter01Ids.Nodes.N09, Chapter01Ids.Dialogues.D09, Chapter01Ids.Flags.FarRouteUnlocked),
            new NodeStep(Chapter01Ids.Nodes.N10, Chapter01Ids.Dialogues.D10, Chapter01Ids.Flags.ExpeditionStarted),
            new NodeStep(Chapter01Ids.Nodes.N11, Chapter01Ids.Dialogues.D11, Chapter01Ids.Flags.LongRoadStarted),
            new NodeStep(Chapter01Ids.Nodes.N12, Chapter01Ids.Dialogues.D12, Chapter01Ids.Flags.OldFordFound),
            new NodeStep(Chapter01Ids.Nodes.N13, Chapter01Ids.Dialogues.D13, Chapter01Ids.Flags.DownstreamContact),
            new NodeStep(Chapter01Ids.Nodes.N14, Chapter01Ids.Dialogues.D14, Chapter01Ids.Flags.AgreementRevealed),
            new NodeStep(Chapter01Ids.Nodes.N14Half, Chapter01Ids.Dialogues.D14Half, Chapter01Ids.Flags.ReturnStarted),
            new NodeStep(Chapter01Ids.Nodes.N15, Chapter01Ids.Dialogues.D15, Chapter01Ids.Flags.ReturnRoadTraveled),
            new NodeStep(Chapter01Ids.Nodes.N16, Chapter01Ids.Dialogues.D16, Chapter01Ids.Flags.ReturnedHome),
            new NodeStep(Chapter01Ids.Nodes.N17, Chapter01Ids.Dialogues.D17, Chapter01Ids.Flags.CouncilCompleted)
        };

        // Первый незавершённый узел последовательности, либо null, если
        // глава завершена или state ещё не создан. Идемпотентно: повторный
        // вызов без изменения флагов между вызовами возвращает тот же узел,
        // а не открывает его заново с нуля — решение "не запускать заново
        // завершённый узел" целиком определяется тем, что флаг завершения
        // уже выставлен и узел пропускается.
        public static string GetNextDialogueId(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (state.HasFlag(Chapter01Ids.Flags.Completed))
                return null;

            for (int i = 0; i < Sequence.Length; i++)
            {
                if (!IsStepCompleted(state, Sequence[i]))
                    return Sequence[i].DialogueId;
            }

            return null;
        }

        public static string GetNextNodeId(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (state.HasFlag(Chapter01Ids.Flags.Completed))
                return null;

            for (int i = 0; i < Sequence.Length; i++)
            {
                if (!IsStepCompleted(state, Sequence[i]))
                    return Sequence[i].NodeId;
            }

            return null;
        }

        public static bool IsChapterComplete(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            return state.HasFlag(Chapter01Ids.Flags.Completed);
        }

        public static Chapter01RepairChoice GetRepairChoice(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            bool old = state.HasFlag(Chapter01Ids.Flags.RepairOld);
            bool @new = state.HasFlag(Chapter01Ids.Flags.RepairNew);

            if (old && @new)
                throw new InvalidOperationException("repair_old и repair_new не могут быть установлены одновременно.");

            if (old)
                return Chapter01RepairChoice.Old;
            if (@new)
                return Chapter01RepairChoice.New;
            return Chapter01RepairChoice.None;
        }

        // P07-T05: свободный порядок N07A/N07B/N07C без счётчика улик —
        // возвращает конкретные ещё не пройденные расследования, а не
        // число. Пустой список означает либо "фаза расследования ещё не
        // началась" (WaterWrongActive == false), либо "все три уже
        // завершены" (пора в N08) — вызывающая сторона различает эти
        // случаи через сами InvestigatedMill/Cattle/River, если нужно.
        // GetNextDialogueId ниже продолжает возвращать один линейный ID
        // (первый ещё не пройденный, в фиксированном порядке A/B/C) —
        // этого достаточно для TryAdvance и не меняет его публичный
        // контракт; реальный свободный выбор получает список отсюда.
        public static IReadOnlyList<string> GetAvailableInvestigationDialogueIds(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            List<string> available = new List<string>();
            if (!state.HasFlag(Chapter01Ids.Flags.WaterWrongActive))
                return available;

            if (!state.HasFlag(Chapter01Ids.Flags.InvestigatedMill))
                available.Add(Chapter01Ids.Dialogues.D07A);
            if (!state.HasFlag(Chapter01Ids.Flags.InvestigatedCattle))
                available.Add(Chapter01Ids.Dialogues.D07B);
            if (!state.HasFlag(Chapter01Ids.Flags.InvestigatedRiver))
                available.Add(Chapter01Ids.Dialogues.D07C);

            return available;
        }

        // Тонкая точка интеграции с UI (раздел 6.2: "запускает диалог через
        // существующий TryOpenNarrativeDialogueById"), без прямой ссылки на
        // PrototypeUIController — вызывающий код передаёт открыватель
        // диалога делегатом. Возвращает false, если открывать нечего или
        // открыватель отказал (например, уже идёт другой диалог).
        public static bool TryAdvance(GameState gameState, Func<string, bool> openDialogueById)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));
            if (openDialogueById == null)
                throw new ArgumentNullException(nameof(openDialogueById));

            if (gameState.Narrative == null)
                gameState.Narrative = new NarrativeStateData();

            string nextDialogueId = GetNextDialogueId(gameState.Narrative);
            if (string.IsNullOrEmpty(nextDialogueId))
                return false;

            return openDialogueById(nextDialogueId);
        }

        // P05-T03 (раздел 17 инструкции): узкая точка завершения сцены,
        // отдельная от TryAdvance/GetNextDialogueId. Читает завершившийся
        // dialogueId и применяет внешние системные последствия конкретной
        // главы, не расширяя универсальный NarrativeEffectType новыми
        // типами ради одной сцены. UI вызывает это перед закрытием
        // завершившегося диалога (PrototypeUIController.Narrative.cs).
        public static void HandleDialogueCompleted(GameState gameState, string dialogueId)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));

            if (string.Equals(dialogueId, Chapter01Ids.Dialogues.D04, StringComparison.Ordinal))
                Chapter01OutcomeApplier.ApplyFloodConsequences(gameState);
            else if (string.Equals(dialogueId, Chapter01Ids.Dialogues.D08, StringComparison.Ordinal))
                Chapter01OutcomeApplier.ApplySevenTeethInvestigationConsequences(gameState);
        }

        private static bool IsStepCompleted(NarrativeStateData state, NodeStep step)
        {
            for (int i = 0; i < step.CompletionFlags.Length; i++)
            {
                if (state.HasFlag(step.CompletionFlags[i]))
                    return true;
            }
            return false;
        }
    }
}
