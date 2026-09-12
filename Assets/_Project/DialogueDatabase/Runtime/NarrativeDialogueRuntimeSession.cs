using System;
using System.Collections.Generic;

namespace KingdomSurvival.DialogueDatabase
{
    // Runtime v2: вычисляет NarrativeDialogueView и разрешает выбор по
    // стабильному ChoiceId вместо позиции в списке. См. §12 инструкции.
    //
    // Игровой код обязан вызывать только Start/BuildView/SelectChoice.
    // SelectChoicePreview существует только для авторского Preview в
    // редакторе (принудительный исход проверки) — см. §13/§20.
    public sealed class NarrativeDialogueRuntimeSession
    {
        // Зарезервированный runtime-переход между двумя последовательно
        // показываемыми TextBlock одного авторского узла. Его нет в asset:
        // сессия создаёт кнопку сама и не позволяет авторским выборам
        // использовать тот же ID.
        public const string SequentialContinueChoiceId = "__runtime.dialogue.next_text_block";

        private DialogueDatabaseAsset database;
        private DialogueDefinitionData dialogue;
        private NarrativeEvaluationContext context;
        private int worldSeed;

        // Один авторский узел может хранить несколько модульных/условных
        // TextBlock, но production-представление всегда показывает только
        // один из них за шаг. Состояние ниже фиксирует текущую реплику и
        // результат её пассивной проверки, чтобы повторный BuildView не
        // перебрасывал проверку и не применял OnRevealEffects повторно.
        private string presentationNodeId = string.Empty;
        private int presentationBlockIndex = -1;
        private NarrativeCheckResult presentationPassiveResult;
        private bool presentationTextRevealed = true;
        private bool presentationPrepared;

        public bool IsActive { get; private set; }
        public string DialogueId => dialogue?.Id ?? string.Empty;
        public string CurrentNodeId { get; private set; } = string.Empty;

        public bool Start(
            DialogueDatabaseAsset dialogueDatabase,
            string dialogueId,
            HeroProfileData hero,
            NarrativeStateData state,
            out NarrativeDialogueView view,
            out string error,
            IReadOnlyCollection<string> presentCompanionIds = null,
            IReadOnlyCollection<string> presentItemIds = null,
            int worldSeedValue = 0,
            int? partySize = null)
        {
            view = null;
            error = string.Empty;

            if (dialogueDatabase == null)
            {
                error = "База диалогов не назначена.";
                return false;
            }

            DialogueDefinitionData dialogueData = dialogueDatabase.FindDialogue(dialogueId);
            if (dialogueData == null)
            {
                error = "Диалог не найден: " + dialogueId;
                return false;
            }

            if (string.IsNullOrWhiteSpace(dialogueData.StartNodeId) || FindNode(dialogueData, dialogueData.StartNodeId) == null)
            {
                error = "У диалога '" + dialogueId + "' не задан существующий стартовый узел.";
                return false;
            }

            database = dialogueDatabase;
            dialogue = dialogueData;
            context = new NarrativeEvaluationContext(hero, state, presentCompanionIds, presentItemIds, worldSeedValue, partySize);
            worldSeed = worldSeedValue;
            CurrentNodeId = dialogueData.StartNodeId;
            ResetPresentationState();
            IsActive = true;

            view = BuildView();
            return true;
        }

        public void End()
        {
            IsActive = false;
            database = null;
            dialogue = null;
            context = null;
            CurrentNodeId = string.Empty;
            ResetPresentationState();
        }

        // Единственный производственный путь построения представления узла.
        // Провалившийся пассивный блок остаётся в представлении — игрок
        // видит, что здесь была возможность что-то заметить, но не видит,
        // что именно (см. класс-каммент NarrativeDialogueVisibleBlock и §4/§18
        // инструкции "новое отображение пассивных наблюдений и проверок").
        public NarrativeDialogueView BuildView()
        {
            return BuildViewCore(revealHiddenTextForAuthor: false);
        }

        // Только для авторского Preview в редакторе (§16: переключатель
        // "Показать упущенный текст пассивных проверок"). Заполняет
        // PreviewOnlyHiddenText фактическим текстом провалившегося блока —
        // сам блок при этом остаётся нераскрытым (IsTextRevealed остаётся
        // false, эффекты не применяются). Игровой runtime обязан вызывать
        // только BuildView() — см. класс-каммент.
        public NarrativeDialogueView BuildViewPreview(bool revealHiddenTextForAuthor)
        {
            return BuildViewCore(revealHiddenTextForAuthor);
        }

        private NarrativeDialogueView BuildViewCore(bool revealHiddenTextForAuthor)
        {
            RequireActiveSession();
            DialogueNodeData node = RequireNode(CurrentNodeId);
            EnsurePresentationPrepared(node);

            List<NarrativeDialogueVisibleBlock> visibleBlocks = new List<NarrativeDialogueVisibleBlock>();
            if (presentationBlockIndex >= 0)
            {
                DialogueTextBlockData block = node.GetEffectiveTextBlocks()[presentationBlockIndex];
                string speakerId = string.IsNullOrWhiteSpace(block.SpeakerIdOverride)
                    ? node.SpeakerId
                    : block.SpeakerIdOverride;
                DialogueSpeakerData speaker = database.FindSpeaker(speakerId);
                visibleBlocks.Add(new NarrativeDialogueVisibleBlock
                {
                    BlockId = block.BlockId,
                    Kind = block.Kind,
                    SpeakerId = speakerId ?? string.Empty,
                    SpeakerDisplayName = speaker != null ? speaker.DisplayName : (speakerId ?? string.Empty),
                    SpeakerRole = speaker != null ? speaker.Role : string.Empty,
                    Text = presentationTextRevealed ? block.Text : string.Empty,
                    CheckPresentation = presentationPassiveResult != null
                        ? NarrativeCheckPresentationBuilder.Build(block.PassiveCheck, presentationPassiveResult)
                        : null,
                    IsTextRevealed = presentationTextRevealed,
                    PreviewOnlyHiddenText = (!presentationTextRevealed && revealHiddenTextForAuthor) ? block.Text : null
                });
            }

            List<NarrativeDialogueChoiceView> available = new List<NarrativeDialogueChoiceView>();
            List<NarrativeDialogueChoiceView> disabled = new List<NarrativeDialogueChoiceView>();

            // Пока внутри узла остаётся хотя бы одна подходящая реплика,
            // реальные ответы скрыты. Игрок получает только нейтральное
            // «…», которое не считается репликой героя и переводит на
            // следующий TextBlock того же узла.
            if (FindNextEligibleBlockIndex(node, presentationBlockIndex + 1) >= 0)
            {
                available.Add(new NarrativeDialogueChoiceView
                {
                    ChoiceId = SequentialContinueChoiceId,
                    Text = "…",
                    Kind = DialogueChoiceKind.Continue,
                    IsAvailable = true
                });
            }
            else
            {
                BuildAuthoredChoiceViews(node, available, disabled);
            }

            return new NarrativeDialogueView
            {
                DialogueId = dialogue.Id,
                NodeId = node.Id,
                VisibleTextBlocks = visibleBlocks,
                AvailableChoices = available,
                DisabledChoices = disabled
            };
        }

        // Единственный производственный путь выбора. Бросок всегда честный.
        public NarrativeDialogueSelectionResult SelectChoice(string choiceId)
        {
            return SelectChoiceInternal(choiceId, NarrativeCheckForcedOutcome.None);
        }

        // Только для авторского Preview в редакторе — см. класс-каммент.
        public NarrativeDialogueSelectionResult SelectChoicePreview(string choiceId, NarrativeCheckForcedOutcome forcedOutcome)
        {
            return SelectChoiceInternal(choiceId, forcedOutcome);
        }

        private NarrativeDialogueSelectionResult SelectChoiceInternal(string choiceId, NarrativeCheckForcedOutcome forcedOutcome)
        {
            RequireActiveSession();
            DialogueNodeData node = RequireNode(CurrentNodeId);
            EnsurePresentationPrepared(node);

            if (string.Equals(choiceId, SequentialContinueChoiceId, StringComparison.Ordinal))
                return AdvanceToNextTextBlock(node);

            if (FindNextEligibleBlockIndex(node, presentationBlockIndex + 1) >= 0)
            {
                throw new InvalidOperationException(
                    "Сначала должна быть показана следующая реплика текущего узла.");
            }

            DialogueChoiceData choice = FindChoice(node, choiceId);
            if (choice == null)
                throw new InvalidOperationException("Неизвестный ChoiceId в узле '" + node.Id + "': " + choiceId);

            if (!choice.Conditions.Evaluate(context))
                throw new InvalidOperationException("Выбор '" + choiceId + "' недоступен: условия не выполнены.");

            if (choice.IsActiveCheck)
            {
                NarrativeCheckAttempt attempt = forcedOutcome == NarrativeCheckForcedOutcome.None
                    ? NarrativeCheckResolver.TryResolveActive(choice.Check, context, worldSeed)
                    : NarrativeCheckResolver.TryResolveActiveForPreview(choice.Check, context, worldSeed, forcedOutcome);

                if (attempt.Outcome == NarrativeCheckAttemptOutcome.Blocked)
                    throw new InvalidOperationException(attempt.BlockReason ?? "Проверка сейчас недоступна.");

                bool success = attempt.Result.Success;
                NarrativeEffectApplier.ApplyAll(success ? choice.SuccessEffects : choice.FailureEffects, context);
                string targetNodeId = success ? choice.SuccessNodeId : choice.FailureNodeId;
                // Активная проверка использует тот же presentation-компонент,
                // что и пассивная (§12): и успех, и провал видны игроку.
                NarrativeCheckPresentationData presentation = NarrativeCheckPresentationBuilder.Build(choice.Check, attempt.Result);
                return TransitionTo(targetNodeId, attempt.Result, presentation);
            }

            if (choice.IsExit)
            {
                IsActive = false;
                return new NarrativeDialogueSelectionResult { DialogueEnded = true };
            }

            return TransitionTo(choice.NextNodeId, null, null);
        }

        private NarrativeDialogueSelectionResult TransitionTo(
            string nodeId,
            NarrativeCheckResult checkResult,
            NarrativeCheckPresentationData checkPresentation)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || FindNode(dialogue, nodeId) == null)
                throw new InvalidOperationException("Переход ведёт в отсутствующий узел: " + nodeId);

            CurrentNodeId = nodeId;
            ResetPresentationState();
            return new NarrativeDialogueSelectionResult
            {
                DialogueEnded = false,
                CheckResult = checkResult,
                CheckPresentation = checkPresentation,
                View = BuildView()
            };
        }

        private void BuildAuthoredChoiceViews(
            DialogueNodeData node,
            List<NarrativeDialogueChoiceView> available,
            List<NarrativeDialogueChoiceView> disabled)
        {
            IReadOnlyList<DialogueChoiceData> choices = node.Choices;
            for (int i = 0; i < choices.Count; i++)
            {
                DialogueChoiceData choice = choices[i];
                if (choice == null)
                    continue;

                string choiceId = choice.GetStableChoiceId(node.Id, i);
                bool conditionsMet = choice.Conditions.Evaluate(context);

                if (!conditionsMet)
                {
                    AddDisabledIfHinted(disabled, choice, choiceId, "Пока недоступно.");
                    continue;
                }

                if (choice.IsActiveCheck)
                {
                    NarrativeCheckAttemptOutcome peek = NarrativeCheckResolver.PeekAvailability(
                        choice.Check, context.State, out _, out string blockReason);

                    if (peek == NarrativeCheckAttemptOutcome.Blocked)
                    {
                        AddDisabledIfHinted(disabled, choice, choiceId, blockReason);
                        continue;
                    }

                    double probability = NarrativeCheckResolver.GetActiveSuccessProbability(choice.Check, context);
                    available.Add(new NarrativeDialogueChoiceView
                    {
                        ChoiceId = choiceId,
                        Text = choice.Text,
                        Kind = choice.Kind,
                        IsAvailable = true,
                        SuccessProbability = probability,
                        MechanicalSummary = BuildMechanicalSummary(choice.Check, probability, choice.Kind)
                    });
                    continue;
                }

                available.Add(new NarrativeDialogueChoiceView
                {
                    ChoiceId = choiceId,
                    Text = choice.Text,
                    Kind = choice.Kind,
                    IsAvailable = true
                });
            }
        }

        private NarrativeDialogueSelectionResult AdvanceToNextTextBlock(DialogueNodeData node)
        {
            int nextIndex = FindNextEligibleBlockIndex(node, presentationBlockIndex + 1);
            if (nextIndex < 0)
                throw new InvalidOperationException("В текущем узле больше нет реплик для показа.");

            presentationBlockIndex = nextIndex;
            PrepareCurrentPresentationBlock(node);
            return new NarrativeDialogueSelectionResult
            {
                DialogueEnded = false,
                View = BuildView()
            };
        }

        private void EnsurePresentationPrepared(DialogueNodeData node)
        {
            if (presentationPrepared && string.Equals(presentationNodeId, node.Id, StringComparison.Ordinal))
                return;

            ResetPresentationState();
            presentationNodeId = node.Id;
            presentationBlockIndex = FindNextEligibleBlockIndex(node, 0);
            presentationPrepared = true;
            PrepareCurrentPresentationBlock(node);
        }

        private void PrepareCurrentPresentationBlock(DialogueNodeData node)
        {
            presentationPassiveResult = null;
            presentationTextRevealed = true;

            if (presentationBlockIndex < 0)
                return;

            DialogueTextBlockData block = node.GetEffectiveTextBlocks()[presentationBlockIndex];
            if (block.HasPassiveCheck)
            {
                presentationPassiveResult = NarrativeCheckResolver.ResolvePassive(block.PassiveCheck, context);
                presentationTextRevealed = presentationPassiveResult.Success;
            }

            // Эффект раскрытия принадлежит конкретной реплике и срабатывает
            // только в тот момент, когда до неё дошла очередь. Провал
            // пассивной проверки не считается раскрытием знания.
            if (presentationTextRevealed)
                NarrativeEffectApplier.ApplyAll(block.OnRevealEffects, context);
        }

        private int FindNextEligibleBlockIndex(DialogueNodeData node, int startIndex)
        {
            IReadOnlyList<DialogueTextBlockData> blocks = node.GetEffectiveTextBlocks();
            int firstIndex = Math.Max(0, startIndex);
            for (int i = firstIndex; i < blocks.Count; i++)
            {
                DialogueTextBlockData block = blocks[i];
                if (block != null && block.Conditions.Evaluate(context))
                    return i;
            }

            return -1;
        }

        private void ResetPresentationState()
        {
            presentationNodeId = string.Empty;
            presentationBlockIndex = -1;
            presentationPassiveResult = null;
            presentationTextRevealed = true;
            presentationPrepared = false;
        }

        private static void AddDisabledIfHinted(
            List<NarrativeDialogueChoiceView> disabled,
            DialogueChoiceData choice,
            string choiceId,
            string hint)
        {
            if (choice.UnavailablePresentation != DialogueChoiceUnavailablePresentation.DisabledWithHint)
                return;

            disabled.Add(new NarrativeDialogueChoiceView
            {
                ChoiceId = choiceId,
                Text = choice.Text,
                Kind = choice.Kind,
                IsAvailable = false,
                IsDisabledWithHint = true,
                DisabledHint = hint
            });
        }

        private static string BuildMechanicalSummary(NarrativeCheckSpec spec, double probability, DialogueChoiceKind kind)
        {
            string qualityLabel = NarrativeQualityLabels.GetLabel(spec.Quality);
            string competencyPart = string.IsNullOrWhiteSpace(spec.CompetencyId)
                ? string.Empty
                : " + " + NarrativeCompetencyLabels.GetLabel(spec.CompetencyId);
            string kindLabel = kind == DialogueChoiceKind.ActiveDecisive ? "решающая" : "возвратная";
            int percent = (int)Math.Round(probability * 100.0, MidpointRounding.AwayFromZero);
            return qualityLabel + competencyPart + " • " + percent + "% • " + kindLabel;
        }

        private static DialogueNodeData FindNode(DialogueDefinitionData dialogueData, string nodeId)
        {
            if (dialogueData == null || string.IsNullOrWhiteSpace(nodeId))
                return null;

            for (int i = 0; i < dialogueData.Nodes.Count; i++)
            {
                DialogueNodeData node = dialogueData.Nodes[i];
                if (node != null && string.Equals(node.Id, nodeId, StringComparison.Ordinal))
                    return node;
            }
            return null;
        }

        private DialogueNodeData RequireNode(string nodeId)
        {
            DialogueNodeData node = FindNode(dialogue, nodeId);
            if (node == null)
                throw new InvalidOperationException("Узел не найден: " + nodeId);
            return node;
        }

        private static DialogueChoiceData FindChoice(DialogueNodeData node, string choiceId)
        {
            IReadOnlyList<DialogueChoiceData> choices = node.Choices;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i] != null && string.Equals(choices[i].GetStableChoiceId(node.Id, i), choiceId, StringComparison.Ordinal))
                    return choices[i];
            }
            return null;
        }

        private void RequireActiveSession()
        {
            if (!IsActive || dialogue == null || context == null)
                throw new InvalidOperationException("Нет активной сессии диалога.");
        }
    }
}
