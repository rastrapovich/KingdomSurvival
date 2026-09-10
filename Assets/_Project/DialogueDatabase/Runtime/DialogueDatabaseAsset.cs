using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase
{
    public enum DialogueCategory
    {
        Test,
        MainStory,
        SideQuest,
        RandomEncounter,
        AmbientNpc,
        Service
    }

    public enum DialogueProductionStatus
    {
        Test,
        Working,
        Approved,
        Disabled
    }

    // Виды текстового блока узла — §11 производственной инструкции.
    public enum DialogueTextBlockKind
    {
        MainLine,
        Observation,
        Memory,
        HeroThought,
        CompanionLine,
        Narration
    }

    // Виды варианта ответа — §11 инструкции.
    public enum DialogueChoiceKind
    {
        Normal,
        ActiveReturnable,
        ActiveDecisive,
        Exit
    }

    public enum DialogueChoiceUnavailablePresentation
    {
        Hidden,
        DisabledWithHint
    }

    [Serializable]
    public sealed class DialogueSpeakerData
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string role = string.Empty;
        [SerializeField] private Sprite portrait;

        // Индивидуальная кадрировка — второй слой поверх общего portrait
        // из UI Конструктора. Геометрия рамки, режим изображения, общий zoom,
        // общий pan, tint и opacity остаются собственностью UILayout.
        [SerializeField] private bool overridePortraitFraming;
        [SerializeField, Min(0.05f)] private float portraitScale = 1f;
        [SerializeField] private Vector2 portraitOffsetNormalized = Vector2.zero;
        [SerializeField] private bool portraitFlipX;

        public string Id => id;
        public string DisplayName => displayName;
        public string Role => role;
        public Sprite Portrait => portrait;
        public bool OverridePortraitFraming => overridePortraitFraming;
        public float PortraitScale => portraitScale > 0f ? Mathf.Max(0.05f, portraitScale) : 1f;
        public Vector2 PortraitOffsetNormalized => portraitOffsetNormalized;
        public bool PortraitFlipX => portraitFlipX;
    }

    // Один текстовый блок узла: основная реплика, наблюдение, память, мысль
    // героя, реплика спутника либо повествование. Показывается только если
    // Conditions выполнены; необязательная пассивная проверка добавляет
    // ещё один слой видимости поверх условий. См. §11.
    [Serializable]
    public sealed class DialogueTextBlockData
    {
        [SerializeField] private string blockId = string.Empty;
        [SerializeField] private DialogueTextBlockKind kind = DialogueTextBlockKind.MainLine;
        [SerializeField] private string speakerIdOverride = string.Empty;
        [SerializeField, TextArea(2, 6)] private string text = string.Empty;
        [SerializeField] private NarrativeConditionGroup conditions = new NarrativeConditionGroup();
        [SerializeField] private bool hasPassiveCheck;
        [SerializeField] private NarrativeCheckSpec passiveCheck = new NarrativeCheckSpec { Kind = NarrativeCheckKind.Passive };
        [SerializeField] private List<NarrativeEffect> onRevealEffects = new List<NarrativeEffect>();

        public string BlockId => blockId;
        public DialogueTextBlockKind Kind => kind;
        public string SpeakerIdOverride => speakerIdOverride;
        public string Text => text;
        public NarrativeConditionGroup Conditions => conditions ?? (conditions = new NarrativeConditionGroup());
        public bool HasPassiveCheck => hasPassiveCheck;
        public NarrativeCheckSpec PassiveCheck => passiveCheck;

        public IReadOnlyList<NarrativeEffect> OnRevealEffects =>
            onRevealEffects ?? (IReadOnlyList<NarrativeEffect>)Array.Empty<NarrativeEffect>();

        public DialogueTextBlockData()
        {
        }

        private DialogueTextBlockData(string blockId, DialogueTextBlockKind kind, string text)
        {
            this.blockId = blockId ?? string.Empty;
            this.kind = kind;
            this.text = text ?? string.Empty;
        }

        // Обратная совместимость (§11): если у узла нет textBlocks, старое
        // поле DialogueNodeData.Text превращается в единственный runtime-блок.
        public static DialogueTextBlockData CreateLegacyMainLine(string text)
        {
            return new DialogueTextBlockData("__legacy_main_line", DialogueTextBlockKind.MainLine, text);
        }
    }

    [Serializable]
    public sealed class DialogueChoiceData
    {
        [SerializeField, TextArea(1, 3)] private string text = string.Empty;
        [SerializeField] private string nextNodeId = string.Empty;
        [SerializeField] private bool endsDialogue;

        [SerializeField] private string choiceId = string.Empty;
        [SerializeField] private DialogueChoiceKind kind = DialogueChoiceKind.Normal;
        [SerializeField] private NarrativeConditionGroup conditions = new NarrativeConditionGroup();
        [SerializeField] private DialogueChoiceUnavailablePresentation unavailablePresentation = DialogueChoiceUnavailablePresentation.Hidden;
        [SerializeField] private NarrativeCheckSpec check = new NarrativeCheckSpec();
        [SerializeField] private string successNodeId = string.Empty;
        [SerializeField] private string failureNodeId = string.Empty;
        [SerializeField] private List<NarrativeEffect> successEffects = new List<NarrativeEffect>();
        [SerializeField] private List<NarrativeEffect> failureEffects = new List<NarrativeEffect>();

        // Legacy/Normal-переход. Спецификация называет его "NormalNodeId" —
        // здесь это то же самое поле, что и прежде, без переименования, чтобы
        // не ломать уже сохранённые данные prototype_miller.
        public string NextNodeId => nextNodeId;
        public bool EndsDialogue => endsDialogue;

        public string Text => text;
        public string ChoiceId => choiceId;
        public DialogueChoiceKind Kind => kind;
        public NarrativeConditionGroup Conditions => conditions ?? (conditions = new NarrativeConditionGroup());
        public DialogueChoiceUnavailablePresentation UnavailablePresentation => unavailablePresentation;
        public NarrativeCheckSpec Check => check;
        public string SuccessNodeId => successNodeId;
        public string FailureNodeId => failureNodeId;

        public IReadOnlyList<NarrativeEffect> SuccessEffects =>
            successEffects ?? (IReadOnlyList<NarrativeEffect>)Array.Empty<NarrativeEffect>();

        public IReadOnlyList<NarrativeEffect> FailureEffects =>
            failureEffects ?? (IReadOnlyList<NarrativeEffect>)Array.Empty<NarrativeEffect>();

        public bool IsActiveCheck =>
            kind == DialogueChoiceKind.ActiveReturnable || kind == DialogueChoiceKind.ActiveDecisive;

        // Старые данные (schemaVersion 0) не различают Kind — они всегда
        // обычные с endsDialogue как единственным признаком выхода. Новый
        // Kind.Exit — то же самое явно поименованное намерение.
        public bool IsExit => kind == DialogueChoiceKind.Exit || endsDialogue;

        // Стабильный ChoiceId для выбора по значению, а не по позиции
        // (§12). У старых данных ChoiceId не сериализован — синтезируем
        // по позиции в узле; это стабильно, пока не меняется порядок ответов.
        public string GetStableChoiceId(string nodeId, int indexInNode)
        {
            return string.IsNullOrWhiteSpace(choiceId)
                ? (nodeId ?? string.Empty) + "#c" + indexInNode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : choiceId;
        }
    }

    [Serializable]
    public sealed class DialogueNodeData
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string speakerId = string.Empty;
        [SerializeField, TextArea(3, 8)] private string text = string.Empty;
        [SerializeField] private List<DialogueTextBlockData> textBlocks = new List<DialogueTextBlockData>();
        [SerializeField] private List<DialogueChoiceData> choices = new List<DialogueChoiceData>();
        [SerializeField, HideInInspector] private Vector2 editorPosition = Vector2.zero;
        [SerializeField, HideInInspector] private bool hasEditorPosition;

        public string Id => id;
        public string SpeakerId => speakerId;
        public string Text => text;

        public IReadOnlyList<DialogueTextBlockData> TextBlocks =>
            textBlocks ?? (IReadOnlyList<DialogueTextBlockData>)Array.Empty<DialogueTextBlockData>();

        public IReadOnlyList<DialogueChoiceData> Choices => choices ?? (IReadOnlyList<DialogueChoiceData>)Array.Empty<DialogueChoiceData>();
        public Vector2 EditorPosition => editorPosition;
        public bool HasEditorPosition => hasEditorPosition;

        // §11: "если textBlocks пуст — старое поле node.text преобразуется
        // в основной runtime-блок". Старые сохранённые данные не переписываются.
        public IReadOnlyList<DialogueTextBlockData> GetEffectiveTextBlocks()
        {
            if (textBlocks != null && textBlocks.Count > 0)
                return textBlocks;

            if (string.IsNullOrEmpty(text))
                return Array.Empty<DialogueTextBlockData>();

            return new List<DialogueTextBlockData> { DialogueTextBlockData.CreateLegacyMainLine(text) };
        }
    }

    [Serializable]
    public sealed class DialogueDefinitionData
    {
        public const int CurrentSchemaVersion = 1;

        [SerializeField] private string id = string.Empty;
        [SerializeField] private string title = string.Empty;
        [SerializeField] private DialogueCategory category = DialogueCategory.Test;
        [SerializeField] private DialogueProductionStatus status = DialogueProductionStatus.Working;
        [SerializeField, TextArea(2, 5)] private string developerComment = string.Empty;
        [SerializeField] private string startNodeId = string.Empty;
        [SerializeField] private List<string> tags = new List<string>();
        [SerializeField] private List<DialogueNodeData> nodes = new List<DialogueNodeData>();
        [SerializeField] private int schemaVersion;

        public string Id => id;
        public string Title => title;
        public DialogueCategory Category => category;
        public DialogueProductionStatus Status => status;
        public string DeveloperComment => developerComment;
        public string StartNodeId => startNodeId;
        public IReadOnlyList<string> Tags => tags ?? (IReadOnlyList<string>)Array.Empty<string>();
        public IReadOnlyList<DialogueNodeData> Nodes => nodes ?? (IReadOnlyList<DialogueNodeData>)Array.Empty<DialogueNodeData>();
        public int SchemaVersion => schemaVersion;
    }

    [CreateAssetMenu(
        fileName = "KingdomSurvivalDialogues",
        menuName = "Kingdom Survival/База диалогов")]
    public sealed class DialogueDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "DialogueDatabase/KingdomSurvivalDialogues";

        [SerializeField] private List<DialogueSpeakerData> speakers = new List<DialogueSpeakerData>();
        [SerializeField] private List<DialogueDefinitionData> dialogues = new List<DialogueDefinitionData>();

        public IReadOnlyList<DialogueSpeakerData> Speakers => speakers ?? (IReadOnlyList<DialogueSpeakerData>)Array.Empty<DialogueSpeakerData>();
        public IReadOnlyList<DialogueDefinitionData> Dialogues => dialogues ?? (IReadOnlyList<DialogueDefinitionData>)Array.Empty<DialogueDefinitionData>();

        public DialogueSpeakerData FindSpeaker(string speakerId)
        {
            if (string.IsNullOrWhiteSpace(speakerId) || speakers == null)
                return null;

            for (int i = 0; i < speakers.Count; i++)
            {
                DialogueSpeakerData speaker = speakers[i];
                if (speaker != null && string.Equals(speaker.Id, speakerId, StringComparison.Ordinal))
                    return speaker;
            }

            return null;
        }

        public DialogueDefinitionData FindDialogue(string dialogueId)
        {
            if (string.IsNullOrWhiteSpace(dialogueId) || dialogues == null)
                return null;

            for (int i = 0; i < dialogues.Count; i++)
            {
                DialogueDefinitionData dialogue = dialogues[i];
                if (dialogue != null && string.Equals(dialogue.Id, dialogueId, StringComparison.Ordinal))
                    return dialogue;
            }

            return null;
        }

        // Легаси-путь: строит старый NarrativeDialogueDefinition (один
        // переход на выбор, без проверок). Диалоги с активными проверками
        // читаются через NarrativeDialogueRuntimeSession, а не отсюда.
        public bool TryBuildRuntime(
            string dialogueId,
            out NarrativeDialogueDefinition definition,
            out string error)
        {
            definition = null;
            error = string.Empty;

            DialogueDefinitionData dialogue = FindDialogue(dialogueId);
            if (dialogue == null)
            {
                error = "Диалог не найден: " + dialogueId;
                return false;
            }

            List<string> issues = new List<string>();
            CollectDialogueValidationIssues(dialogue, issues, includeReachability: true);
            if (issues.Count > 0)
            {
                error = string.Join("\n", issues);
                return false;
            }

            for (int nodeIndex = 0; nodeIndex < dialogue.Nodes.Count; nodeIndex++)
            {
                DialogueNodeData node = dialogue.Nodes[nodeIndex];
                for (int choiceIndex = 0; choiceIndex < node.Choices.Count; choiceIndex++)
                {
                    if (node.Choices[choiceIndex].IsActiveCheck)
                    {
                        error =
                            "Диалог '" + dialogueId + "' использует активные проверки (узел '" + node.Id +
                            "'). Соберите его через NarrativeDialogueRuntimeSession, а не через TryBuildRuntime.";
                        return false;
                    }
                }
            }

            NarrativeDialogueNode[] runtimeNodes = new NarrativeDialogueNode[dialogue.Nodes.Count];
            for (int nodeIndex = 0; nodeIndex < dialogue.Nodes.Count; nodeIndex++)
            {
                DialogueNodeData node = dialogue.Nodes[nodeIndex];
                DialogueSpeakerData speaker = FindSpeaker(node.SpeakerId);
                NarrativeDialogueChoice[] runtimeChoices = new NarrativeDialogueChoice[node.Choices.Count];

                for (int choiceIndex = 0; choiceIndex < node.Choices.Count; choiceIndex++)
                {
                    DialogueChoiceData choice = node.Choices[choiceIndex];
                    runtimeChoices[choiceIndex] = choice.IsExit
                        ? NarrativeDialogueChoice.Exit(choice.Text)
                        : new NarrativeDialogueChoice(choice.Text, choice.NextNodeId);
                }

                runtimeNodes[nodeIndex] = new NarrativeDialogueNode(
                    node.Id,
                    speaker.Id,
                    speaker.DisplayName,
                    speaker.Role,
                    node.Text,
                    runtimeChoices);
            }

            definition = new NarrativeDialogueDefinition(
                dialogue.Id,
                dialogue.StartNodeId,
                runtimeNodes);
            return true;
        }

        public void CollectValidationIssues(List<string> issues)
        {
            if (issues == null)
                throw new ArgumentNullException(nameof(issues));

            issues.Clear();

            HashSet<string> speakerIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < speakers.Count; i++)
            {
                DialogueSpeakerData speaker = speakers[i];
                if (speaker == null)
                {
                    issues.Add("Говорящий #" + (i + 1) + ": пустая запись.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(speaker.Id))
                    issues.Add("Говорящий #" + (i + 1) + ": отсутствует ID.");
                else if (!speakerIds.Add(speaker.Id))
                    issues.Add("Повторяющийся ID говорящего: " + speaker.Id + ".");

                if (string.IsNullOrWhiteSpace(speaker.DisplayName))
                    issues.Add("Говорящий '" + speaker.Id + "': отсутствует отображаемое имя.");
            }

            HashSet<string> dialogueIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < dialogues.Count; i++)
            {
                DialogueDefinitionData dialogue = dialogues[i];
                if (dialogue == null)
                {
                    issues.Add("Диалог #" + (i + 1) + ": пустая запись.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(dialogue.Id))
                    issues.Add("Диалог #" + (i + 1) + ": отсутствует ID.");
                else if (!dialogueIds.Add(dialogue.Id))
                    issues.Add("Повторяющийся ID диалога: " + dialogue.Id + ".");

                CollectDialogueValidationIssues(dialogue, issues, includeReachability: true);
            }

            CollectDatabaseWideNarrativeIssues(issues);
        }

        public void CollectValidationIssuesForDialogue(string dialogueId, List<string> issues)
        {
            if (issues == null)
                throw new ArgumentNullException(nameof(issues));

            issues.Clear();
            DialogueDefinitionData dialogue = FindDialogue(dialogueId);
            if (dialogue == null)
            {
                issues.Add("Диалог не найден: " + dialogueId + ".");
                return;
            }

            CollectDialogueValidationIssues(dialogue, issues, includeReachability: true);
        }

        // Проверяет, что проверка в принципе достижима при максимально
        // благоприятных допустимых значениях героя (§19: "невозможную при
        // любых допустимых значениях проверку").
        private static bool IsCheckPossible(NarrativeCheckSpec spec)
        {
            if (spec == null)
                return true;

            int maxCompetency = string.IsNullOrWhiteSpace(spec.CompetencyId) ? 0 : 5;
            int maxDieContribution = spec.Kind == NarrativeCheckKind.Passive
                ? NarrativeCheckMath.PassiveBase
                : NarrativeCheckMath.MaxDieSum;
            int maxTotal = maxDieContribution + HeroProfileData.MaxQualityValue + maxCompetency + NarrativeCheckMath.MaxContextModifier;
            return maxTotal >= spec.Difficulty;
        }

        private static IEnumerable<string> GetChoiceTargets(DialogueChoiceData choice)
        {
            if (choice == null || choice.IsExit)
                yield break;

            if (choice.IsActiveCheck)
            {
                if (!string.IsNullOrWhiteSpace(choice.SuccessNodeId))
                    yield return choice.SuccessNodeId;
                if (!string.IsNullOrWhiteSpace(choice.FailureNodeId))
                    yield return choice.FailureNodeId;
            }
            else if (!string.IsNullOrWhiteSpace(choice.NextNodeId))
            {
                yield return choice.NextNodeId;
            }
        }

        private void CollectDialogueValidationIssues(
            DialogueDefinitionData dialogue,
            List<string> issues,
            bool includeReachability)
        {
            string prefix = string.IsNullOrWhiteSpace(dialogue.Id)
                ? "Диалог без ID"
                : "Диалог '" + dialogue.Id + "'";

            if (string.IsNullOrWhiteSpace(dialogue.Title))
                issues.Add(prefix + ": отсутствует название.");
            if (string.IsNullOrWhiteSpace(dialogue.StartNodeId))
                issues.Add(prefix + ": не назначен стартовый узел.");
            if (dialogue.Nodes == null || dialogue.Nodes.Count == 0)
            {
                issues.Add(prefix + ": нет узлов.");
                return;
            }

            HashSet<string> nodeIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, DialogueNodeData> nodesById = new Dictionary<string, DialogueNodeData>(StringComparer.Ordinal);
            HashSet<string> checkIdsInDialogue = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> effectIdsInDialogue = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> returnableChecksNeedingUnlock = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> unlockedCheckIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> requiredKnowledgeIds = new HashSet<string>(StringComparer.Ordinal);
            List<NarrativeKnowledgeGrantSource> knowledgeGrants = new List<NarrativeKnowledgeGrantSource>();

            for (int i = 0; i < dialogue.Nodes.Count; i++)
            {
                DialogueNodeData node = dialogue.Nodes[i];
                if (node == null)
                {
                    issues.Add(prefix + ": узел #" + (i + 1) + " пуст.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    issues.Add(prefix + ": узел #" + (i + 1) + " без ID.");
                    continue;
                }

                if (!nodeIds.Add(node.Id))
                    issues.Add(prefix + ": повторяющийся Node ID '" + node.Id + "'.");
                else
                    nodesById.Add(node.Id, node);

                if (string.IsNullOrWhiteSpace(node.SpeakerId))
                    issues.Add(prefix + " / " + node.Id + ": не назначен говорящий.");
                else if (FindSpeaker(node.SpeakerId) == null)
                    issues.Add(prefix + " / " + node.Id + ": неизвестный говорящий '" + node.SpeakerId + "'.");

                if (string.IsNullOrWhiteSpace(node.Text) && node.TextBlocks.Count == 0)
                    issues.Add(prefix + " / " + node.Id + ": пустая реплика.");
                if (node.Choices == null || node.Choices.Count == 0)
                    issues.Add(prefix + " / " + node.Id + ": нет ни одного варианта ответа.");
            }

            for (int i = 0; i < dialogue.Nodes.Count; i++)
            {
                DialogueNodeData node = dialogue.Nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.Id))
                    continue;

                string nodePrefix = prefix + " / " + node.Id;
                HashSet<string> blockIdsInNode = new HashSet<string>(StringComparer.Ordinal);
                for (int blockIndex = 0; blockIndex < node.TextBlocks.Count; blockIndex++)
                {
                    DialogueTextBlockData block = node.TextBlocks[blockIndex];
                    string blockPrefix = nodePrefix + " / блок #" + (blockIndex + 1);
                    if (block == null)
                    {
                        issues.Add(blockPrefix + ": пустая запись.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(block.BlockId) && !blockIdsInNode.Add(block.BlockId))
                        issues.Add(blockPrefix + ": повторяющийся BlockId '" + block.BlockId + "' в узле.");

                    if (string.IsNullOrWhiteSpace(block.Text))
                        issues.Add(blockPrefix + ": пустой текст блока.");

                    CollectRequiredKnowledge(block.Conditions, requiredKnowledgeIds);
                    CollectKnowledgeGrants(block.OnRevealEffects, blockPrefix, false, null, knowledgeGrants);

                    if (block.HasPassiveCheck)
                    {
                        ValidateCheckSpec(
                            block.PassiveCheck,
                            NarrativeCheckKind.Passive,
                            blockPrefix,
                            issues,
                            checkIdsInDialogue);
                    }

                    CollectEffectIds(block.OnRevealEffects, blockPrefix, issues, effectIdsInDialogue);
                }

                HashSet<string> choiceIdsInNode = new HashSet<string>(StringComparer.Ordinal);
                for (int choiceIndex = 0; choiceIndex < node.Choices.Count; choiceIndex++)
                {
                    DialogueChoiceData choice = node.Choices[choiceIndex];
                    string choicePrefix = nodePrefix + " / ответ #" + (choiceIndex + 1);
                    if (choice == null)
                    {
                        issues.Add(choicePrefix + ": пустая запись.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(choice.ChoiceId) && !choiceIdsInNode.Add(choice.ChoiceId))
                        issues.Add(choicePrefix + ": повторяющийся ChoiceId '" + choice.ChoiceId + "' в узле.");

                    if (string.IsNullOrWhiteSpace(choice.Text))
                        issues.Add(choicePrefix + ": пустой текст ответа.");

                    ValidateChoiceTransitions(choice, choicePrefix, nodesById, issues);
                    CollectRequiredKnowledge(choice.Conditions, requiredKnowledgeIds);

                    bool isDecisiveGrant = choice.Kind == DialogueChoiceKind.ActiveDecisive;
                    string returnableCheckIdForGrant = choice.Kind == DialogueChoiceKind.ActiveReturnable
                        ? choice.Check.CheckId
                        : null;
                    CollectKnowledgeGrants(choice.SuccessEffects, choicePrefix + " / успех", isDecisiveGrant, returnableCheckIdForGrant, knowledgeGrants);
                    CollectKnowledgeGrants(choice.FailureEffects, choicePrefix + " / провал", isDecisiveGrant, returnableCheckIdForGrant, knowledgeGrants);

                    if (choice.IsActiveCheck)
                    {
                        NarrativeCheckKind expectedCheckKind = choice.Kind == DialogueChoiceKind.ActiveDecisive
                            ? NarrativeCheckKind.ActiveDecisive
                            : NarrativeCheckKind.ActiveReturnable;
                        ValidateCheckSpec(choice.Check, expectedCheckKind, choicePrefix, issues, checkIdsInDialogue);

                        if (choice.Kind == DialogueChoiceKind.ActiveReturnable &&
                            choice.Check != null &&
                            !string.IsNullOrWhiteSpace(choice.Check.CheckId))
                        {
                            returnableChecksNeedingUnlock.Add(choice.Check.CheckId);
                        }

                        CollectEffectIds(choice.SuccessEffects, choicePrefix + " / успех", issues, effectIdsInDialogue);
                        CollectEffectIds(choice.FailureEffects, choicePrefix + " / провал", issues, effectIdsInDialogue);
                        CollectUnlockTargets(choice.SuccessEffects, unlockedCheckIds);
                        CollectUnlockTargets(choice.FailureEffects, unlockedCheckIds);
                    }
                }

                CollectUnlockTargetsFromNodeBlocks(node, unlockedCheckIds);
            }

            foreach (string checkId in returnableChecksNeedingUnlock)
            {
                if (!unlockedCheckIds.Contains(checkId))
                {
                    issues.Add(
                        prefix + ": возвратная проверка '" + checkId +
                        "' не имеет ни одного доступного эффекта UnlockCheck в этом диалоге.");
                }
            }

            // §19: "единственная обязательная улика, закрытая одной
            // проверкой". Если знание требуется условием где-то в диалоге и
            // единственный способ его получить — решающая проверка (без
            // повтора) или возвратная без доступной разблокировки, автор
            // рискует необратимо запереть прогресс.
            foreach (string knowledgeId in requiredKnowledgeIds)
            {
                NarrativeKnowledgeGrantSource onlySource = null;
                int sourceCount = 0;
                for (int i = 0; i < knowledgeGrants.Count; i++)
                {
                    if (knowledgeGrants[i].KnowledgeId != knowledgeId)
                        continue;
                    sourceCount++;
                    onlySource = knowledgeGrants[i];
                }

                if (sourceCount != 1 || onlySource == null)
                    continue;

                bool isFragile = onlySource.IsDecisiveGrant ||
                    (onlySource.ReturnableCheckId != null && !unlockedCheckIds.Contains(onlySource.ReturnableCheckId));

                if (isFragile)
                {
                    issues.Add(
                        prefix + ": знание '" + knowledgeId +
                        "' требуется условием, но единственный способ получить его — " + onlySource.Location +
                        " — непереигрываемая или не имеющая разблокировки проверка.");
                }
            }

            if (!string.IsNullOrWhiteSpace(dialogue.StartNodeId) && !nodesById.ContainsKey(dialogue.StartNodeId))
                issues.Add(prefix + ": стартовый узел '" + dialogue.StartNodeId + "' не существует.");

            if (!includeReachability || !nodesById.ContainsKey(dialogue.StartNodeId))
                return;

            HashSet<string> reachable = CollectReachableNodes(dialogue.StartNodeId, nodesById);
            foreach (string nodeId in nodesById.Keys)
            {
                if (!reachable.Contains(nodeId))
                    issues.Add(prefix + ": узел '" + nodeId + "' недостижим из старта.");
            }

            Dictionary<string, bool> memo = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (!CanReachExit(dialogue.StartNodeId, nodesById, new HashSet<string>(StringComparer.Ordinal), memo))
                issues.Add(prefix + ": из стартового узла невозможно завершить разговор.");
        }

        private static void ValidateCheckSpec(
            NarrativeCheckSpec spec,
            NarrativeCheckKind expectedKind,
            string prefix,
            List<string> issues,
            HashSet<string> checkIdsInDialogue)
        {
            if (spec == null)
            {
                issues.Add(prefix + ": не задана проверка.");
                return;
            }

            if (spec.Kind != expectedKind)
            {
                issues.Add(
                    prefix + ": тип проверки (" + spec.Kind + ") не совпадает с ожидаемым (" + expectedKind + ").");
            }

            if (string.IsNullOrWhiteSpace(spec.CheckId))
            {
                issues.Add(prefix + ": у проверки не задан CheckId.");
            }
            else
            {
                string signature = spec.CheckId + "::" + expectedKind;
                bool alreadyKnown = false;
                foreach (string known in checkIdsInDialogue)
                {
                    if (known.StartsWith(spec.CheckId + "::", StringComparison.Ordinal) && known != signature)
                    {
                        issues.Add(
                            prefix + ": CheckId '" + spec.CheckId +
                            "' уже используется в этом диалоге с другим типом проверки.");
                        alreadyKnown = true;
                        break;
                    }
                }

                if (!alreadyKnown)
                    checkIdsInDialogue.Add(signature);
            }

            if (!NarrativeDifficulty.IsInValidRange(spec.Difficulty))
            {
                issues.Add(
                    prefix + ": сложность " + spec.Difficulty + " вне диапазона [" +
                    NarrativeDifficulty.MinDifficulty + ".." + NarrativeDifficulty.MaxDifficulty + "].");
            }

            if (!string.IsNullOrWhiteSpace(spec.CompetencyId) && !NarrativeCompetencyIds.IsKnown(spec.CompetencyId))
                issues.Add(prefix + ": неизвестная компетенция '" + spec.CompetencyId + "'.");

            if (!IsCheckPossible(spec))
                issues.Add(prefix + ": проверка невозможна ни при каких допустимых значениях героя.");
        }

        private static void ValidateChoiceTransitions(
            DialogueChoiceData choice,
            string choicePrefix,
            Dictionary<string, DialogueNodeData> nodesById,
            List<string> issues)
        {
            switch (choice.Kind)
            {
                case DialogueChoiceKind.Exit:
                    if (!string.IsNullOrWhiteSpace(choice.NextNodeId) ||
                        !string.IsNullOrWhiteSpace(choice.SuccessNodeId) ||
                        !string.IsNullOrWhiteSpace(choice.FailureNodeId))
                    {
                        issues.Add(choicePrefix + ": EXIT-ответ не должен одновременно содержать переход.");
                    }
                    break;

                case DialogueChoiceKind.ActiveReturnable:
                case DialogueChoiceKind.ActiveDecisive:
                    if (choice.EndsDialogue || !string.IsNullOrWhiteSpace(choice.NextNodeId))
                        issues.Add(choicePrefix + ": активная проверка не должна использовать обычный переход/EXIT.");

                    if (string.IsNullOrWhiteSpace(choice.SuccessNodeId))
                        issues.Add(choicePrefix + ": у активной проверки нет ветки успеха.");
                    else if (!nodesById.ContainsKey(choice.SuccessNodeId))
                        issues.Add(choicePrefix + ": ветка успеха ведёт в отсутствующий узел '" + choice.SuccessNodeId + "'.");

                    if (string.IsNullOrWhiteSpace(choice.FailureNodeId))
                        issues.Add(choicePrefix + ": у активной проверки нет ветки провала.");
                    else if (!nodesById.ContainsKey(choice.FailureNodeId))
                        issues.Add(choicePrefix + ": ветка провала ведёт в отсутствующий узел '" + choice.FailureNodeId + "'.");
                    break;

                default:
                    if (choice.EndsDialogue)
                    {
                        if (!string.IsNullOrWhiteSpace(choice.NextNodeId))
                            issues.Add(choicePrefix + ": EXIT-ответ не должен иметь переход.");
                    }
                    else if (string.IsNullOrWhiteSpace(choice.NextNodeId))
                    {
                        issues.Add(choicePrefix + ": не указан следующий узел.");
                    }
                    else if (!nodesById.ContainsKey(choice.NextNodeId))
                    {
                        issues.Add(choicePrefix + ": переход ведёт в отсутствующий узел '" + choice.NextNodeId + "'.");
                    }
                    break;
            }
        }

        private static void CollectEffectIds(
            IReadOnlyList<NarrativeEffect> effects,
            string prefix,
            List<string> issues,
            HashSet<string> effectIdsInDialogue)
        {
            if (effects == null)
                return;

            for (int i = 0; i < effects.Count; i++)
            {
                NarrativeEffect effect = effects[i];
                if (effect == null)
                    continue;

                if (string.IsNullOrWhiteSpace(effect.EffectExecutionId))
                {
                    issues.Add(prefix + ": эффект #" + (i + 1) + " без EffectExecutionId.");
                    continue;
                }

                if (!effectIdsInDialogue.Add(effect.EffectExecutionId))
                {
                    issues.Add(
                        prefix + ": повторное использование EffectExecutionId '" +
                        effect.EffectExecutionId + "' в этом диалоге.");
                }
            }
        }

        // Один источник знания, отслеживаемый для §19-эвристики ниже.
        private sealed class NarrativeKnowledgeGrantSource
        {
            public string KnowledgeId;
            public bool IsDecisiveGrant;
            public string ReturnableCheckId;
            public string Location;
        }

        private static void CollectRequiredKnowledge(NarrativeConditionGroup conditions, HashSet<string> requiredKnowledgeIds)
        {
            if (conditions == null || conditions.Conditions == null)
                return;

            for (int i = 0; i < conditions.Conditions.Count; i++)
            {
                NarrativeCondition condition = conditions.Conditions[i];
                if (condition != null &&
                    condition.Type == NarrativeConditionType.KnowledgeKnown &&
                    !string.IsNullOrWhiteSpace(condition.StringParam))
                {
                    requiredKnowledgeIds.Add(condition.StringParam);
                }
            }
        }

        private static void CollectKnowledgeGrants(
            IReadOnlyList<NarrativeEffect> effects,
            string location,
            bool isDecisiveGrant,
            string returnableCheckId,
            List<NarrativeKnowledgeGrantSource> grants)
        {
            if (effects == null)
                return;

            for (int i = 0; i < effects.Count; i++)
            {
                NarrativeEffect effect = effects[i];
                if (effect == null ||
                    effect.Type != NarrativeEffectType.AddKnowledge ||
                    string.IsNullOrWhiteSpace(effect.StringParam))
                {
                    continue;
                }

                grants.Add(new NarrativeKnowledgeGrantSource
                {
                    KnowledgeId = effect.StringParam,
                    IsDecisiveGrant = isDecisiveGrant,
                    ReturnableCheckId = returnableCheckId,
                    Location = location
                });
            }
        }

        private static void CollectUnlockTargets(IReadOnlyList<NarrativeEffect> effects, HashSet<string> unlockedCheckIds)
        {
            if (effects == null)
                return;

            for (int i = 0; i < effects.Count; i++)
            {
                NarrativeEffect effect = effects[i];
                if (effect != null &&
                    effect.Type == NarrativeEffectType.UnlockCheck &&
                    !string.IsNullOrWhiteSpace(effect.StringParam))
                {
                    unlockedCheckIds.Add(effect.StringParam);
                }
            }
        }

        private static void CollectUnlockTargetsFromNodeBlocks(DialogueNodeData node, HashSet<string> unlockedCheckIds)
        {
            for (int i = 0; i < node.TextBlocks.Count; i++)
            {
                DialogueTextBlockData block = node.TextBlocks[i];
                if (block != null)
                    CollectUnlockTargets(block.OnRevealEffects, unlockedCheckIds);
            }
        }

        private void CollectDatabaseWideNarrativeIssues(List<string> issues)
        {
            Dictionary<string, string> checkKindById = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> effectIdsAcrossDatabase = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < dialogues.Count; i++)
            {
                DialogueDefinitionData dialogue = dialogues[i];
                if (dialogue == null)
                    continue;

                for (int nodeIndex = 0; nodeIndex < dialogue.Nodes.Count; nodeIndex++)
                {
                    DialogueNodeData node = dialogue.Nodes[nodeIndex];
                    if (node == null)
                        continue;

                    for (int blockIndex = 0; blockIndex < node.TextBlocks.Count; blockIndex++)
                    {
                        DialogueTextBlockData block = node.TextBlocks[blockIndex];
                        if (block != null && block.HasPassiveCheck)
                            RegisterCheckSignature(block.PassiveCheck, dialogue.Id, checkKindById, issues);

                        RegisterEffectIds(block?.OnRevealEffects, dialogue.Id, effectIdsAcrossDatabase, issues);
                    }

                    for (int choiceIndex = 0; choiceIndex < node.Choices.Count; choiceIndex++)
                    {
                        DialogueChoiceData choice = node.Choices[choiceIndex];
                        if (choice == null)
                            continue;

                        if (choice.IsActiveCheck)
                            RegisterCheckSignature(choice.Check, dialogue.Id, checkKindById, issues);

                        RegisterEffectIds(choice.SuccessEffects, dialogue.Id, effectIdsAcrossDatabase, issues);
                        RegisterEffectIds(choice.FailureEffects, dialogue.Id, effectIdsAcrossDatabase, issues);
                    }
                }
            }
        }

        private static void RegisterCheckSignature(
            NarrativeCheckSpec spec,
            string dialogueId,
            Dictionary<string, string> checkKindById,
            List<string> issues)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.CheckId))
                return;

            string kindLabel = spec.Kind.ToString();
            if (checkKindById.TryGetValue(spec.CheckId, out string existingKind))
            {
                if (existingKind != kindLabel)
                {
                    issues.Add(
                        "CheckId '" + spec.CheckId + "' (диалог '" + dialogueId +
                        "') используется в базе с разными типами проверки: " + existingKind + " и " + kindLabel + ".");
                }
            }
            else
            {
                checkKindById.Add(spec.CheckId, kindLabel);
            }
        }

        private static void RegisterEffectIds(
            IReadOnlyList<NarrativeEffect> effects,
            string dialogueId,
            HashSet<string> effectIdsAcrossDatabase,
            List<string> issues)
        {
            if (effects == null)
                return;

            for (int i = 0; i < effects.Count; i++)
            {
                NarrativeEffect effect = effects[i];
                if (effect == null || string.IsNullOrWhiteSpace(effect.EffectExecutionId))
                    continue;

                if (!effectIdsAcrossDatabase.Add(effect.EffectExecutionId))
                {
                    issues.Add(
                        "EffectExecutionId '" + effect.EffectExecutionId +
                        "' повторно используется в базе (диалог '" + dialogueId + "').");
                }
            }
        }

        private static HashSet<string> CollectReachableNodes(
            string startNodeId,
            Dictionary<string, DialogueNodeData> nodesById)
        {
            HashSet<string> reachable = new HashSet<string>(StringComparer.Ordinal);
            Queue<string> queue = new Queue<string>();
            queue.Enqueue(startNodeId);

            while (queue.Count > 0)
            {
                string nodeId = queue.Dequeue();
                if (!reachable.Add(nodeId))
                    continue;

                DialogueNodeData node;
                if (!nodesById.TryGetValue(nodeId, out node) || node == null)
                    continue;

                for (int i = 0; i < node.Choices.Count; i++)
                {
                    foreach (string target in GetChoiceTargets(node.Choices[i]))
                    {
                        if (nodesById.ContainsKey(target))
                            queue.Enqueue(target);
                    }
                }
            }

            return reachable;
        }

        private static bool CanReachExit(
            string nodeId,
            Dictionary<string, DialogueNodeData> nodesById,
            HashSet<string> visiting,
            Dictionary<string, bool> memo)
        {
            bool cached;
            if (memo.TryGetValue(nodeId, out cached))
                return cached;
            if (!visiting.Add(nodeId))
                return false;

            DialogueNodeData node;
            if (!nodesById.TryGetValue(nodeId, out node) || node == null)
            {
                visiting.Remove(nodeId);
                memo[nodeId] = false;
                return false;
            }

            for (int i = 0; i < node.Choices.Count; i++)
            {
                DialogueChoiceData choice = node.Choices[i];
                if (choice == null)
                    continue;

                if (choice.IsExit)
                {
                    visiting.Remove(nodeId);
                    memo[nodeId] = true;
                    return true;
                }

                bool anyTargetReachesExit = false;
                foreach (string target in GetChoiceTargets(choice))
                {
                    if (nodesById.ContainsKey(target) && CanReachExit(target, nodesById, visiting, memo))
                    {
                        anyTargetReachesExit = true;
                        break;
                    }
                }

                if (anyTargetReachesExit)
                {
                    visiting.Remove(nodeId);
                    memo[nodeId] = true;
                    return true;
                }
            }

            visiting.Remove(nodeId);
            memo[nodeId] = false;
            return false;
        }
    }
}
