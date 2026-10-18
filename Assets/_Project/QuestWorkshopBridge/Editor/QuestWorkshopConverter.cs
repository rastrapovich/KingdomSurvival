using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.Encounters;
using UnityEngine;

namespace KingdomSurvival.QuestWorkshopBridge.Editor
{
    // Зеркало DialogueDefinitionData с теми же именами полей: JsonUtility
    // переносит его в настоящую запись через FromJsonOverwrite, а поля,
    // которых здесь нет (иллюстрация сцены и её кадрировка), остаются как были.
    [Serializable]
    internal sealed class DialogueMirror
    {
        public string id = string.Empty;
        public string title = string.Empty;
        public DialogueCategory category = DialogueCategory.SideQuest;
        public DialogueProductionStatus status = DialogueProductionStatus.Working;
        public string developerComment = string.Empty;
        public string startNodeId = string.Empty;
        public List<string> tags = new List<string>();
        public List<NodeMirror> nodes = new List<NodeMirror>();
        public int schemaVersion = DialogueDefinitionData.CurrentSchemaVersion;
    }

    [Serializable]
    internal sealed class NodeMirror
    {
        public string id = string.Empty;
        public string speakerId = string.Empty;
        public string text = string.Empty;
        public List<BlockMirror> textBlocks = new List<BlockMirror>();
        public List<ChoiceMirror> choices = new List<ChoiceMirror>();
        public Vector2 editorPosition = Vector2.zero;
        public bool hasEditorPosition;
    }

    [Serializable]
    internal sealed class BlockMirror
    {
        public string blockId = string.Empty;
        public DialogueTextBlockKind kind = DialogueTextBlockKind.MainLine;
        public string speakerIdOverride = string.Empty;
        public string text = string.Empty;
        public NarrativeConditionGroup conditions = new NarrativeConditionGroup();
        public bool hasPassiveCheck;
        public NarrativeCheckSpec passiveCheck = new NarrativeCheckSpec { Kind = NarrativeCheckKind.Passive };
        public List<NarrativeEffect> onRevealEffects = new List<NarrativeEffect>();
    }

    [Serializable]
    internal sealed class ChoiceMirror
    {
        public string text = string.Empty;
        public string nextNodeId = string.Empty;
        public bool endsDialogue;
        public string choiceId = string.Empty;
        public DialogueChoiceKind kind = DialogueChoiceKind.Normal;
        public NarrativeConditionGroup conditions = new NarrativeConditionGroup();
        public DialogueChoiceUnavailablePresentation unavailablePresentation = DialogueChoiceUnavailablePresentation.Hidden;
        public NarrativeCheckSpec check = new NarrativeCheckSpec();
        public string successNodeId = string.Empty;
        public string failureNodeId = string.Empty;
        public List<NarrativeEffect> successEffects = new List<NarrativeEffect>();
        public List<NarrativeEffect> failureEffects = new List<NarrativeEffect>();
    }

    public enum WorkshopIssueSeverity
    {
        Error,
        Warning,
        Info
    }

    public sealed class WorkshopIssue
    {
        public WorkshopIssueSeverity Severity;
        public string Message = string.Empty;

        public override string ToString()
        {
            string prefix = Severity == WorkshopIssueSeverity.Error ? "Ошибка" : Severity == WorkshopIssueSeverity.Warning ? "Предупреждение" : "Заметка";
            return prefix + ": " + Message;
        }
    }

    // Преобразование сцен мастерской в записи Unity. Правила ID — обратные
    // правилам мастерской (ext/10-unity-convert.js): префиксы снимаются,
    // собственные ID мастерской переносятся как есть.
    public static class QuestWorkshopConverter
    {
        private static readonly Regex NodePrefix = new Regex("^ud:[^:]*:", RegexOptions.Compiled);
        private static readonly Regex UnityIdPattern = new Regex("^[A-Za-z0-9_.\\-]+$", RegexOptions.Compiled);

        public static string StripNode(string id)
        {
            return string.IsNullOrEmpty(id) ? string.Empty : NodePrefix.Replace(id, string.Empty);
        }

        public static string Strip(string prefix, string id)
        {
            if (string.IsNullOrEmpty(id))
                return string.Empty;
            return id.StartsWith(prefix, StringComparison.Ordinal) ? id.Substring(prefix.Length) : id;
        }

        public static string SpeakerId(WorkshopProject project, string id)
        {
            if (string.IsNullOrEmpty(id))
                return string.Empty;
            if (id.StartsWith("unity_person_", StringComparison.Ordinal))
                return id.Substring("unity_person_".Length);
            if (project.People.TryGetValue(id, out WorkshopPerson person) && person.UnityId.Length > 0)
                return person.UnityId;
            return id;
        }

        private static string FactId(WorkshopProject project, string prefix, string id)
        {
            if (string.IsNullOrEmpty(id))
                return string.Empty;
            if (id.StartsWith(prefix, StringComparison.Ordinal))
                return id.Substring(prefix.Length);
            if (project.Facts.TryGetValue(id, out WorkshopFact fact) && fact.UnityId.Length > 0)
                return fact.UnityId;
            return id;
        }

        public static string FlagId(WorkshopProject project, string id)
        {
            return FactId(project, "unity_flag_", id);
        }

        public static string KnowledgeId(WorkshopProject project, string id)
        {
            return FactId(project, "unity_knowledge_", id);
        }

        private static bool TryEnum<T>(string value, out T result) where T : struct
        {
            return Enum.TryParse(value ?? string.Empty, false, out result) && Enum.IsDefined(typeof(T), result);
        }

        public static DialogueCategory DefaultCategory(WorkshopScene scene)
        {
            if (scene.Encounter != null && scene.Encounter.Enabled)
                return DialogueCategory.RandomEncounter;
            WorkshopQuest quest = scene.Quest;
            if (quest != null && quest.Mode == "Кризис")
                return DialogueCategory.MainStory;
            if (quest != null && quest.Role == "Дорожная история")
                return DialogueCategory.RandomEncounter;
            return DialogueCategory.SideQuest;
        }

        // ── Условия, эффекты, проверки ─────────────────────────────────────

        private static NarrativeConditionGroup Conditions(WorkshopProject project, WorkshopConditionGroup group, List<WorkshopIssue> issues)
        {
            NarrativeConditionGroup result = new NarrativeConditionGroup
            {
                Combinator = group.Mode == "Any" ? NarrativeConditionCombinator.Any : NarrativeConditionCombinator.All
            };
            foreach (WorkshopCondition item in group.Items)
            {
                if (!TryEnum(item.Type, out NarrativeConditionType type))
                {
                    issues?.Add(Issue(WorkshopIssueSeverity.Error, "Условие неизвестного типа «" + item.Type + "»."));
                    continue;
                }
                NarrativeCondition condition = new NarrativeCondition { Type = type, IntParam = item.Value, Negate = item.Negate };
                switch (type)
                {
                    case NarrativeConditionType.FlagSet:
                        condition.StringParam = FlagId(project, item.Ref);
                        break;
                    case NarrativeConditionType.KnowledgeKnown:
                        condition.StringParam = KnowledgeId(project, item.Ref);
                        break;
                    case NarrativeConditionType.CompanionPresent:
                    case NarrativeConditionType.RelationAtLeast:
                    case NarrativeConditionType.RelationAtMost:
                        condition.StringParam = SpeakerId(project, item.Ref);
                        break;
                    case NarrativeConditionType.CheckSucceeded:
                    case NarrativeConditionType.CheckFailed:
                    case NarrativeConditionType.CheckNotAttempted:
                        condition.StringParam = Strip("unity_check_", item.Ref);
                        break;
                    case NarrativeConditionType.EffectApplied:
                        condition.StringParam = Strip("unity_effect_", item.Ref);
                        break;
                    case NarrativeConditionType.ItemPresent:
                        condition.StringParam = Strip("unity_item_", item.Ref);
                        break;
                    case NarrativeConditionType.QualityAtLeast:
                        if (TryEnum(item.Ref, out HeroQuality quality))
                            condition.QualityParam = quality;
                        else
                            issues?.Add(Issue(WorkshopIssueSeverity.Error, "Условие качества без качества («" + item.Ref + "»)."));
                        break;
                    default:
                        condition.StringParam = item.Ref ?? string.Empty;
                        break;
                }
                bool needsRef = type != NarrativeConditionType.PartySizeAtLeast && type != NarrativeConditionType.PartySizeAtMost &&
                                type != NarrativeConditionType.QualityAtLeast;
                if (needsRef && string.IsNullOrEmpty(condition.StringParam))
                    issues?.Add(Issue(WorkshopIssueSeverity.Error, "В условии «" + item.Type + "» не выбран объект."));
                result.Conditions.Add(condition);
            }
            return result;
        }

        private static List<NarrativeEffect> Effects(WorkshopProject project, List<WorkshopEffect> effects, List<WorkshopIssue> issues)
        {
            List<NarrativeEffect> result = new List<NarrativeEffect>();
            foreach (WorkshopEffect effect in effects)
            {
                if (effect.Type == "Planned")
                {
                    issues?.Add(Issue(WorkshopIssueSeverity.Warning,
                        "Авторский замысел «" + (effect.Note.Length > 0 ? effect.Note : "без описания") + "» не переносится — для него нужен код."));
                    continue;
                }
                if (!TryEnum(effect.Type, out NarrativeEffectType type))
                {
                    issues?.Add(Issue(WorkshopIssueSeverity.Error, "Эффект неизвестного типа «" + effect.Type + "»."));
                    continue;
                }
                NarrativeEffect result1 = new NarrativeEffect
                {
                    EffectExecutionId = Strip("unity_effect_", effect.Id),
                    Type = type,
                    IntParam = effect.Amount
                };
                switch (type)
                {
                    case NarrativeEffectType.SetFlag:
                    case NarrativeEffectType.ClearFlag:
                        result1.StringParam = FlagId(project, effect.Ref);
                        break;
                    case NarrativeEffectType.AddKnowledge:
                        result1.StringParam = KnowledgeId(project, effect.Ref);
                        break;
                    case NarrativeEffectType.ChangeRelation:
                        result1.StringParam = SpeakerId(project, effect.Ref);
                        break;
                    case NarrativeEffectType.UnlockCheck:
                        result1.StringParam = Strip("unity_check_", effect.Ref);
                        break;
                    case NarrativeEffectType.GrantItem:
                    case NarrativeEffectType.RemoveItem:
                        result1.StringParam = Strip("unity_item_", effect.Ref);
                        break;
                    default:
                        result1.StringParam = effect.Ref ?? string.Empty;
                        break;
                }
                if (string.IsNullOrEmpty(result1.EffectExecutionId))
                    issues?.Add(Issue(WorkshopIssueSeverity.Error, "У эффекта «" + effect.Type + "» нет ID однократного применения."));
                result.Add(result1);
            }
            return result;
        }

        private static NarrativeCheckSpec Check(WorkshopProject project, WorkshopCheck check, NarrativeCheckKind kind, List<WorkshopIssue> issues)
        {
            NarrativeCheckSpec spec = new NarrativeCheckSpec
            {
                CheckId = Strip("unity_check_", check.Id),
                Kind = kind,
                CompetencyId = check.CompetencyId ?? string.Empty,
                Difficulty = Mathf.Clamp(check.Difficulty, 1, 40)
            };
            if (TryEnum(check.Quality, out HeroQuality quality))
                spec.Quality = quality;
            else
                issues?.Add(Issue(WorkshopIssueSeverity.Error, "У проверки неизвестное качество «" + check.Quality + "»."));
            if (string.IsNullOrEmpty(spec.CheckId))
                issues?.Add(Issue(WorkshopIssueSeverity.Error, "У проверки нет стабильного ID."));
            foreach (WorkshopModifier modifier in check.Modifiers)
            {
                spec.ModifierRules.Add(new NarrativeContextModifierRule
                {
                    SourceId = Strip("unity_mod_", modifier.Id),
                    Label = modifier.Label ?? string.Empty,
                    Value = modifier.Amount,
                    Condition = Conditions(project, modifier.Conditions, issues)
                });
            }
            return spec;
        }

        private static WorkshopIssue Issue(WorkshopIssueSeverity severity, string message)
        {
            return new WorkshopIssue { Severity = severity, Message = message };
        }

        // ── Сборка диалога ──────────────────────────────────────────────────

        // Строит полную запись диалога для переноса. Если диалог уже есть в базе,
        // поля, которые мастерская не задаёт (комментарий разработчика, а также
        // категория, статус и теги, если в сцене они пусты), берутся из базы,
        // а позиции узлов в графе — из одноимённых узлов.
        internal static DialogueMirror BuildDialogue(WorkshopProject project, WorkshopScene scene, DialogueDefinitionData existing, List<WorkshopIssue> issues)
        {
            DialogueMirror mirror = new DialogueMirror
            {
                id = scene.UnityId,
                title = scene.Title,
                startNodeId = StripNode(scene.StartNodeId)
            };

            if (TryEnum(scene.UnityCategory, out DialogueCategory category))
                mirror.category = category;
            else
                mirror.category = existing != null ? existing.Category : DefaultCategory(scene);

            if (TryEnum(scene.UnityStatus, out DialogueProductionStatus status))
                mirror.status = status;
            else
                mirror.status = existing != null ? existing.Status : DialogueProductionStatus.Working;

            if (scene.UnityTags.Count > 0)
                mirror.tags = new List<string>(scene.UnityTags);
            else if (existing != null)
                mirror.tags = new List<string>(existing.Tags);

            mirror.developerComment = existing != null ? existing.DeveloperComment ?? string.Empty : scene.Summary ?? string.Empty;

            Dictionary<string, DialogueNodeData> oldNodes = new Dictionary<string, DialogueNodeData>(StringComparer.Ordinal);
            if (existing != null)
            {
                foreach (DialogueNodeData node in existing.Nodes)
                    if (node != null && !string.IsNullOrEmpty(node.Id))
                        oldNodes[node.Id] = node;
            }

            HashSet<string> nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorkshopNode node in scene.Nodes)
                nodeIds.Add(StripNode(node.Id));

            foreach (WorkshopNode node in scene.Nodes)
            {
                NodeMirror nodeMirror = new NodeMirror
                {
                    id = StripNode(node.Id),
                    speakerId = SpeakerId(project, node.SpeakerId)
                };
                if (oldNodes.TryGetValue(nodeMirror.id, out DialogueNodeData oldNode))
                {
                    nodeMirror.editorPosition = oldNode.EditorPosition;
                    nodeMirror.hasEditorPosition = oldNode.HasEditorPosition;
                }

                int blockIndex = 0;
                foreach (WorkshopBlock block in node.Blocks)
                {
                    BlockMirror blockMirror = new BlockMirror
                    {
                        blockId = StripNode(block.Id),
                        speakerIdOverride = SpeakerId(project, block.SpeakerId),
                        text = block.Text ?? string.Empty,
                        conditions = Conditions(project, block.Conditions, issues),
                        onRevealEffects = Effects(project, block.Effects, issues)
                    };
                    if (string.IsNullOrEmpty(blockMirror.blockId))
                        blockMirror.blockId = nodeMirror.id + "_b" + blockIndex;
                    if (TryEnum(block.Kind, out DialogueTextBlockKind kind))
                        blockMirror.kind = kind;
                    else
                        issues?.Add(Issue(WorkshopIssueSeverity.Error, "Реплика неизвестного вида «" + block.Kind + "»."));
                    if (block.Check != null)
                    {
                        if (block.Check.Kind != "Passive")
                            issues?.Add(Issue(WorkshopIssueSeverity.Error, "У реплики может быть только пассивная проверка; активная — у ответа."));
                        blockMirror.hasPassiveCheck = true;
                        blockMirror.passiveCheck = Check(project, block.Check, NarrativeCheckKind.Passive, issues);
                    }
                    if (string.IsNullOrWhiteSpace(blockMirror.text))
                        issues?.Add(Issue(WorkshopIssueSeverity.Warning, "Пустая реплика в узле «" + nodeMirror.id + "»."));
                    nodeMirror.textBlocks.Add(blockMirror);
                    blockIndex++;
                }

                int choiceIndex = 0;
                foreach (WorkshopChoice choice in node.Choices)
                {
                    ChoiceMirror choiceMirror = new ChoiceMirror
                    {
                        text = choice.Text ?? string.Empty,
                        choiceId = StripNode(choice.Id),
                        conditions = Conditions(project, choice.Conditions, issues),
                        unavailablePresentation = choice.Unavailable == "DisabledWithHint"
                            ? DialogueChoiceUnavailablePresentation.DisabledWithHint
                            : DialogueChoiceUnavailablePresentation.Hidden,
                        successEffects = Effects(project, choice.Effects, issues),
                        failureEffects = Effects(project, choice.FailureEffects, issues)
                    };
                    if (string.IsNullOrEmpty(choiceMirror.choiceId))
                        choiceMirror.choiceId = nodeMirror.id + "#c" + choiceIndex;
                    if (TryEnum(choice.Kind, out DialogueChoiceKind choiceKind))
                        choiceMirror.kind = choiceKind;
                    else
                        issues?.Add(Issue(WorkshopIssueSeverity.Error, "Ответ неизвестного вида «" + choice.Kind + "»."));

                    if (choice.IsActive)
                    {
                        choiceMirror.successNodeId = StripNode(choice.SuccessNodeId);
                        choiceMirror.failureNodeId = StripNode(choice.FailureNodeId);
                        if (choice.Check == null)
                            issues?.Add(Issue(WorkshopIssueSeverity.Error, "Для проверки ответа «" + choice.Text + "» не заданы параметры."));
                        else
                            choiceMirror.check = Check(project, choice.Check,
                                choice.Kind == "ActiveDecisive" ? NarrativeCheckKind.ActiveDecisive : NarrativeCheckKind.ActiveReturnable, issues);
                        RequireTarget(choiceMirror.successNodeId, nodeIds, choice, issues);
                        RequireTarget(choiceMirror.failureNodeId, nodeIds, choice, issues);
                    }
                    else if (choiceMirror.kind != DialogueChoiceKind.Exit)
                    {
                        choiceMirror.nextNodeId = StripNode(choice.NextNodeId);
                        RequireTarget(choiceMirror.nextNodeId, nodeIds, choice, issues);
                    }

                    if (choice.Hint.Length > 0)
                        issues?.Add(Issue(WorkshopIssueSeverity.Info, "Подсказка недоступного ответа в Unity пока не хранится."));
                    nodeMirror.choices.Add(choiceMirror);
                    choiceIndex++;
                }

                if (nodeMirror.choices.Count == 0)
                    issues?.Add(Issue(WorkshopIssueSeverity.Warning, "Узел «" + nodeMirror.id + "» без ответа: разговор на нём остановится."));
                mirror.nodes.Add(nodeMirror);
            }

            if (!nodeIds.Contains(mirror.startNodeId))
                issues?.Add(Issue(WorkshopIssueSeverity.Error, "Не выбрана первая реплика сцены."));
            return mirror;
        }

        private static void RequireTarget(string target, HashSet<string> nodeIds, WorkshopChoice choice, List<WorkshopIssue> issues)
        {
            if (string.IsNullOrEmpty(target))
                issues?.Add(Issue(WorkshopIssueSeverity.Error, "У ответа «" + choice.Text + "» не задано продолжение."));
            else if (!nodeIds.Contains(target))
                issues?.Add(Issue(WorkshopIssueSeverity.Error,
                    "Ответ «" + choice.Text + "» ведёт в реплику другой сцены (" + target + ") — в Unity переход между диалогами так не работает."));
        }

        // Проверки сцены без обращения к базам (ID, структура, встреча).
        public static List<WorkshopIssue> ValidateScene(WorkshopProject project, WorkshopScene scene, EncounterDatabaseAsset encounters)
        {
            List<WorkshopIssue> issues = new List<WorkshopIssue>();
            string unityId = scene.UnityId;
            if (unityId.Length == 0)
                issues.Add(Issue(WorkshopIssueSeverity.Error, scene.IsMerged
                    ? "Не задан ID диалога квеста (мастерская: вкладка «Реализация» → «ID диалога квеста в Unity»)."
                    : "Не задан ID диалога Unity (поле сцены «ID диалога в Unity»)."));
            else if (!UnityIdPattern.IsMatch(unityId))
                issues.Add(Issue(WorkshopIssueSeverity.Error, "ID диалога Unity — только латиница, цифры, «_», «.», «-»."));
            if (scene.Nodes.Count == 0)
            {
                issues.Add(Issue(WorkshopIssueSeverity.Error, "В сцене нет реплик — переносить нечего."));
                return issues;
            }

            BuildDialogue(project, scene, null, issues);

            if (scene.Encounter != null && scene.Encounter.Enabled)
            {
                if (scene.Encounter.EncounterId.Length == 0)
                    issues.Add(Issue(WorkshopIssueSeverity.Error, "Встреча включена, но не задан ID встречи (EncounterId)."));
                if (scene.Encounter.SelectionMode == "Pool")
                {
                    if (scene.Encounter.PoolId.Length == 0)
                        issues.Add(Issue(WorkshopIssueSeverity.Error, "Встреча из пула, но пул не выбран."));
                    else if (encounters != null && encounters.FindPool(scene.Encounter.PoolId) == null)
                        issues.Add(Issue(WorkshopIssueSeverity.Error, "Пул «" + scene.Encounter.PoolId + "» не найден в базе встреч."));
                }
            }
            if (scene.NextSceneId.Length > 0)
                issues.Add(Issue(WorkshopIssueSeverity.Info, "Переход «после разговора» к другой сцене задаёт сюжетный код Unity, а не база диалогов."));

            // Повторы одной и той же заметки (например, о подсказке) не нужны.
            List<WorkshopIssue> unique = new List<WorkshopIssue>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorkshopIssue issue in issues)
                if (seen.Add(issue.Severity + issue.Message))
                    unique.Add(issue);
            return unique;
        }

        // ── Сравнение с базой ───────────────────────────────────────────────

        internal static DialogueMirror MirrorOf(DialogueDefinitionData dialogue)
        {
            return JsonUtility.FromJson<DialogueMirror>(JsonUtility.ToJson(dialogue));
        }

        // Приводит запись к виду, в котором её видит мастерская: без позиций графа,
        // без остаточных данных неактивных проверок, legacy-текст — как блок.
        internal static string Comparable(DialogueMirror source)
        {
            DialogueMirror mirror = JsonUtility.FromJson<DialogueMirror>(JsonUtility.ToJson(source));
            mirror.developerComment = string.Empty;
            foreach (NodeMirror node in mirror.nodes)
            {
                node.editorPosition = Vector2.zero;
                node.hasEditorPosition = false;
                if (node.textBlocks.Count == 0 && !string.IsNullOrEmpty(node.text))
                    node.textBlocks.Add(new BlockMirror { blockId = node.id + "_legacy", text = node.text });
                node.text = string.Empty;
                for (int i = 0; i < node.textBlocks.Count; i++)
                {
                    BlockMirror block = node.textBlocks[i];
                    if (string.IsNullOrEmpty(block.blockId))
                        block.blockId = node.id + "_b" + i;
                    if (!block.hasPassiveCheck)
                        block.passiveCheck = new NarrativeCheckSpec { Kind = NarrativeCheckKind.Passive };
                    NormalizeConditions(block.conditions);
                    if (block.hasPassiveCheck)
                        NormalizeCheck(block.passiveCheck);
                }
                for (int i = 0; i < node.choices.Count; i++)
                {
                    ChoiceMirror choice = node.choices[i];
                    if (string.IsNullOrEmpty(choice.choiceId))
                        choice.choiceId = node.id + "#c" + i;
                    if (choice.kind == DialogueChoiceKind.Normal && choice.endsDialogue)
                        choice.kind = DialogueChoiceKind.Exit;
                    choice.endsDialogue = false;
                    bool active = choice.kind == DialogueChoiceKind.ActiveReturnable || choice.kind == DialogueChoiceKind.ActiveDecisive;
                    if (active)
                    {
                        choice.nextNodeId = string.Empty;
                        NormalizeCheck(choice.check);
                    }
                    else
                    {
                        choice.check = new NarrativeCheckSpec();
                        choice.successNodeId = string.Empty;
                        choice.failureNodeId = string.Empty;
                        if (choice.kind == DialogueChoiceKind.Exit)
                            choice.nextNodeId = string.Empty;
                    }
                    NormalizeConditions(choice.conditions);
                }
            }
            return JsonUtility.ToJson(mirror);
        }

        private static void NormalizeCheck(NarrativeCheckSpec check)
        {
            if (check == null)
                return;
            foreach (NarrativeContextModifierRule rule in check.ModifierRules)
                NormalizeConditions(rule.Condition);
        }

        private static void NormalizeConditions(NarrativeConditionGroup group)
        {
            if (group == null || group.Conditions == null)
                return;
            foreach (NarrativeCondition condition in group.Conditions)
            {
                if (condition.Type == NarrativeConditionType.QualityAtLeast)
                    condition.StringParam = string.Empty;
                else
                    condition.QualityParam = default;
            }
        }

        public static bool SameAsDatabase(WorkshopProject project, WorkshopScene scene, DialogueDefinitionData existing)
        {
            if (existing == null)
                return false;
            DialogueMirror built = BuildDialogue(project, scene, existing, null);
            return Comparable(built) == Comparable(MirrorOf(existing));
        }

        // Короткая сводка различий для окна: что поменяется в Unity.
        public static List<string> DescribeDifferences(WorkshopProject project, WorkshopScene scene, DialogueDefinitionData existing)
        {
            List<string> lines = new List<string>();
            DialogueMirror built = BuildDialogue(project, scene, existing, null);
            if (existing == null)
            {
                lines.Add("Новый диалог: узлов " + built.nodes.Count + ", реплик " + CountBlocks(built) + ".");
                return lines;
            }

            DialogueMirror current = JsonUtility.FromJson<DialogueMirror>(Comparable(MirrorOf(existing)));
            DialogueMirror next = JsonUtility.FromJson<DialogueMirror>(Comparable(built));
            if (current.title != next.title)
                lines.Add("Название: «" + current.title + "» → «" + next.title + "».");
            if (current.category != next.category)
                lines.Add("Категория: " + current.category + " → " + next.category + ".");
            if (current.status != next.status)
                lines.Add("Статус: " + current.status + " → " + next.status + ".");
            if (string.Join(",", current.tags) != string.Join(",", next.tags))
                lines.Add("Теги: " + string.Join(", ", current.tags) + " → " + string.Join(", ", next.tags) + ".");
            if (current.startNodeId != next.startNodeId)
                lines.Add("Первая реплика: " + current.startNodeId + " → " + next.startNodeId + ".");

            Dictionary<string, string> oldNodes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (NodeMirror node in current.nodes)
                oldNodes[node.id] = JsonUtility.ToJson(node);
            int added = 0, changed = 0, textChanged = 0;
            HashSet<string> nextIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (NodeMirror node in next.nodes)
            {
                nextIds.Add(node.id);
                if (!oldNodes.TryGetValue(node.id, out string oldJson))
                {
                    added++;
                    continue;
                }
                if (oldJson == JsonUtility.ToJson(node))
                    continue;
                changed++;
                NodeMirror oldNode = JsonUtility.FromJson<NodeMirror>(oldJson);
                if (TextOf(oldNode) != TextOf(node))
                    textChanged++;
            }
            int removed = 0;
            foreach (string id in oldNodes.Keys)
                if (!nextIds.Contains(id))
                    removed++;
            if (added > 0)
                lines.Add("Новых узлов: " + added + ".");
            if (removed > 0)
                lines.Add("Удаляется узлов: " + removed + ".");
            if (changed > 0)
                lines.Add("Изменено узлов: " + changed + (textChanged > 0 ? " (из них с другим текстом: " + textChanged + ")" : " (только логика или ответы)") + ".");
            if (lines.Count == 0)
                lines.Add("Отличий нет.");
            return lines;
        }

        private static int CountBlocks(DialogueMirror mirror)
        {
            int count = 0;
            foreach (NodeMirror node in mirror.nodes)
                count += node.textBlocks.Count;
            return count;
        }

        private static string TextOf(NodeMirror node)
        {
            List<string> parts = new List<string>();
            foreach (BlockMirror block in node.textBlocks)
                parts.Add(block.text);
            foreach (ChoiceMirror choice in node.choices)
                parts.Add(choice.text);
            return string.Join("\n", parts);
        }

        // ── Встреча ─────────────────────────────────────────────────────────

        public static EncounterDefinition BuildEncounter(WorkshopProject project, WorkshopScene scene, EncounterDefinition existing)
        {
            WorkshopEncounter source = scene.Encounter;
            EncounterDefinition encounter = existing != null
                ? JsonUtility.FromJson<EncounterDefinition>(JsonUtility.ToJson(existing))
                : new EncounterDefinition();
            encounter.EncounterId = source.EncounterId;
            encounter.DisplayName = source.DisplayName.Length > 0 ? source.DisplayName : scene.Title;
            if (source.Description.Length > 0)
                encounter.Description = source.Description;
            else if (existing == null)
                encounter.Description = scene.Summary ?? string.Empty;
            encounter.Status = TryEnum(source.Status, out EncounterStatus status) ? status : EncounterStatus.Draft;
            encounter.Category = TryEnum(source.Category, out EncounterCategory category) ? category : EncounterCategory.Road;
            encounter.Tags = new List<string>(source.Tags);
            encounter.DurationClass = TryEnum(source.DurationClass, out EncounterDurationClass duration) ? duration : EncounterDurationClass.Standard;
            encounter.MemoryClass = TryEnum(source.MemoryClass, out EncounterMemoryClass memory) ? memory : EncounterMemoryClass.None;
            encounter.Functions = new List<EncounterFunction>();
            foreach (string function in source.Functions)
                if (TryEnum(function, out EncounterFunction parsed))
                    encounter.Functions.Add(parsed);
            encounter.DialogueId = scene.UnityId;
            encounter.ResolutionMode = EncounterResolutionMode.DialogueDriven;
            encounter.SelectionMode = TryEnum(source.SelectionMode, out EncounterSelectionMode selection) ? selection : EncounterSelectionMode.Pool;
            encounter.PoolId = source.PoolId;
            encounter.DiscoveryChancePercent = Mathf.Clamp(source.DiscoveryChance, 0, 100);
            encounter.SelectionWeight = Mathf.Max(0, source.Weight);
            encounter.UnlimitedOccurrences = source.Unlimited;
            encounter.MaxOccurrencesPerGame = Mathf.Max(0, source.MaxOccurrences);
            encounter.CooldownHours = Mathf.Max(0, source.CooldownHours);
            encounter.AllowedRegionIds = new List<string>(source.Regions);
            encounter.RequiredLocationTags = new List<string>(source.RequiredLocationTags);
            encounter.ForbiddenLocationTags = new List<string>(source.ForbiddenLocationTags);
            encounter.RequiredConditions = Conditions(project, scene.Conditions, null);
            encounter.RequiredFlagsAll = new List<string>(source.RequiredFlagsAll);
            encounter.RequiredFlagsAny = new List<string>(source.RequiredFlagsAny);
            encounter.ForbiddenFlags = new List<string>(source.ForbiddenFlags);
            encounter.FlagsSetOnStart = new List<string>(source.FlagsOnStart);
            encounter.FlagsSetOnComplete = new List<string>(source.FlagsOnComplete);
            encounter.ClearFlagsOnComplete = new List<string>(source.ClearFlagsOnComplete);
            encounter.DesignerNotes = source.DesignerNotes;
            encounter.FutureHooksNotes = source.FutureHooks;
            return encounter;
        }

        public static bool SameEncounter(EncounterDefinition a, EncounterDefinition b)
        {
            return a != null && b != null && JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        }

        // Флаги, которые встреча читает или ставит: их документирует реестр флагов.
        public static List<string> EncounterFlags(WorkshopEncounter encounter)
        {
            List<string> flags = new List<string>();
            if (encounter == null || !encounter.Enabled)
                return flags;
            foreach (List<string> list in new[]
                     {
                         encounter.RequiredFlagsAll, encounter.RequiredFlagsAny, encounter.ForbiddenFlags,
                         encounter.FlagsOnStart, encounter.FlagsOnComplete, encounter.ClearFlagsOnComplete
                     })
            {
                foreach (string flag in list)
                    if (!flags.Contains(flag))
                        flags.Add(flag);
            }
            return flags;
        }
    }
}
