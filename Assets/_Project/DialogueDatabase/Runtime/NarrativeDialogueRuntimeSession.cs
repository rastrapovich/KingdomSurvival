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
        private DialogueDatabaseAsset database;
        private DialogueDefinitionData dialogue;
        private NarrativeEvaluationContext context;
        private int worldSeed;

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
            int worldSeedValue = 0)
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
            context = new NarrativeEvaluationContext(hero, state, presentCompanionIds, presentItemIds, worldSeedValue);
            worldSeed = worldSeedValue;
            CurrentNodeId = dialogueData.StartNodeId;
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
        }

        // Единственный производственный путь построения представления узла.
        // Провалившийся пассивный блок в него никогда не попадает — §3
        // инструкции по визуализации проверок ("пассивный провал игроку не
        // показывать вообще, иначе это метагейм").
        public NarrativeDialogueView BuildView()
        {
            return BuildViewCore(includeFailedPassiveChecks: false);
        }

        // Только для авторского Preview в редакторе (§17: переключатель
        // "Показывать проваленные пассивные проверки"). Игровой runtime
        // обязан вызывать только BuildView() — см. класс-каммент.
        public NarrativeDialogueView BuildViewPreview(bool includeFailedPassiveChecks)
        {
            return BuildViewCore(includeFailedPassiveChecks);
        }

        private NarrativeDialogueView BuildViewCore(bool includeFailedPassiveChecks)
        {
            RequireActiveSession();
            DialogueNodeData node = RequireNode(CurrentNodeId);

            List<NarrativeDialogueVisibleBlock> visibleBlocks = new List<NarrativeDialogueVisibleBlock>();
            IReadOnlyList<DialogueTextBlockData> blocks = node.GetEffectiveTextBlocks();
            for (int i = 0; i < blocks.Count; i++)
            {
                DialogueTextBlockData block = blocks[i];
                if (block == null || !block.Conditions.Evaluate(context))
                    continue;

                NarrativeCheckResult passiveResult = null;
                bool included = true;
                if (block.HasPassiveCheck)
                {
                    passiveResult = NarrativeCheckResolver.ResolvePassive(block.PassiveCheck, context);
                    included = passiveResult.Success;
                }

                bool isPreviewOnlyFailure = block.HasPassiveCheck && !included;
                if (!included && !(includeFailedPassiveChecks && isPreviewOnlyFailure))
                    continue;

                // Провалившийся пассивный блок в Preview не должен запускать
                // игровые эффекты — в production-runtime он никогда не
                // показывается (§3/§17).
                if (!isPreviewOnlyFailure)
                    NarrativeEffectApplier.ApplyAll(block.OnRevealEffects, context);

                string speakerId = string.IsNullOrWhiteSpace(block.SpeakerIdOverride) ? node.SpeakerId : block.SpeakerIdOverride;
                DialogueSpeakerData speaker = database.FindSpeaker(speakerId);
                visibleBlocks.Add(new NarrativeDialogueVisibleBlock
                {
                    BlockId = block.BlockId,
                    Kind = block.Kind,
                    SpeakerId = speakerId ?? string.Empty,
                    SpeakerDisplayName = speaker != null ? speaker.DisplayName : (speakerId ?? string.Empty),
                    SpeakerRole = speaker != null ? speaker.Role : string.Empty,
                    Text = block.Text,
                    CheckPresentation = passiveResult != null
                        ? NarrativeCheckPresentationBuilder.Build(block.PassiveCheck, passiveResult)
                        : null,
                    IsPassiveFailurePreviewOnly = isPreviewOnlyFailure
                });
            }

            List<NarrativeDialogueChoiceView> available = new List<NarrativeDialogueChoiceView>();
            List<NarrativeDialogueChoiceView> disabled = new List<NarrativeDialogueChoiceView>();
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
            return new NarrativeDialogueSelectionResult
            {
                DialogueEnded = false,
                CheckResult = checkResult,
                CheckPresentation = checkPresentation,
                View = BuildView()
            };
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
