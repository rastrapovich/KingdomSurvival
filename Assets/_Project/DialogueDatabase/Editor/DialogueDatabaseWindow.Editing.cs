using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    public sealed partial class DialogueDatabaseWindow : EditorWindow
    {
        private void AddNode(SerializedProperty dialogue)
        {
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            Vector2 position = new Vector2(80f + nodes.arraySize * 36f, 80f + nodes.arraySize * 36f);
            AddNodeAtPosition(dialogue, position);
        }

        private string AddNodeAtPosition(SerializedProperty dialogue, Vector2 position)
        {
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            string id = MakeUniqueNodeId(nodes, "node");
            int index = nodes.arraySize;
            nodes.arraySize++;
            SerializedProperty node = nodes.GetArrayElementAtIndex(index);
            node.FindPropertyRelative("id").stringValue = id;
            node.FindPropertyRelative("speakerId").stringValue = database.Speakers.Count > 0 ? database.Speakers[0].Id : string.Empty;
            node.FindPropertyRelative("text").stringValue = "Новая реплика.";
            node.FindPropertyRelative("textBlocks").arraySize = 0;
            SerializedProperty choices = node.FindPropertyRelative("choices");
            choices.arraySize = 1;
            ResetChoiceToDefaults(choices.GetArrayElementAtIndex(0));
            SerializedProperty choice = choices.GetArrayElementAtIndex(0);
            choice.FindPropertyRelative("text").stringValue = "Завершить разговор.";
            choice.FindPropertyRelative("nextNodeId").stringValue = string.Empty;
            choice.FindPropertyRelative("endsDialogue").boolValue = true;
            choice.FindPropertyRelative("kind").enumValueIndex = (int)DialogueChoiceKind.Exit;
            SetNodeEditorPosition(node, position);
            node.isExpanded = true;
            return id;
        }

        // Полностью сбрасывает вариант ответа перед заполнением нужного вида.
        // Обязательно: SerializedProperty.arraySize++ на непустом списке
        // дублирует данные ПРЕДЫДУЩЕГО элемента, а не создаёт чистый объект.
        private static void ResetChoiceToDefaults(SerializedProperty choice)
        {
            choice.FindPropertyRelative("text").stringValue = string.Empty;
            choice.FindPropertyRelative("nextNodeId").stringValue = string.Empty;
            choice.FindPropertyRelative("endsDialogue").boolValue = false;
            choice.FindPropertyRelative("choiceId").stringValue = string.Empty;
            choice.FindPropertyRelative("kind").enumValueIndex = (int)DialogueChoiceKind.Normal;
            choice.FindPropertyRelative("unavailablePresentation").enumValueIndex = (int)DialogueChoiceUnavailablePresentation.Hidden;
            choice.FindPropertyRelative("successNodeId").stringValue = string.Empty;
            choice.FindPropertyRelative("failureNodeId").stringValue = string.Empty;

            SerializedProperty conditions = choice.FindPropertyRelative("conditions").FindPropertyRelative("Conditions");
            if (conditions != null)
                conditions.arraySize = 0;

            SerializedProperty check = choice.FindPropertyRelative("check");
            check.FindPropertyRelative("CheckId").stringValue = string.Empty;
            check.FindPropertyRelative("Kind").enumValueIndex = (int)NarrativeCheckKind.ActiveReturnable;
            check.FindPropertyRelative("Difficulty").intValue = NarrativeDifficulty.Ordinary;
            check.FindPropertyRelative("CompetencyId").stringValue = string.Empty;
            SerializedProperty modifierRules = check.FindPropertyRelative("ModifierRules");
            if (modifierRules != null)
                modifierRules.arraySize = 0;

            choice.FindPropertyRelative("successEffects").arraySize = 0;
            choice.FindPropertyRelative("failureEffects").arraySize = 0;
        }

        private static void AddChoice(SerializedProperty choices)
        {
            int index = choices.arraySize;
            choices.arraySize++;
            SerializedProperty choice = choices.GetArrayElementAtIndex(index);
            ResetChoiceToDefaults(choice);
            choice.FindPropertyRelative("text").stringValue = "Новый ответ";
            choice.FindPropertyRelative("nextNodeId").stringValue = string.Empty;
            choice.FindPropertyRelative("endsDialogue").boolValue = true;
            choice.FindPropertyRelative("kind").enumValueIndex = (int)DialogueChoiceKind.Normal;
        }

        private static void AddExitChoice(SerializedProperty choices)
        {
            int index = choices.arraySize;
            choices.arraySize++;
            SerializedProperty choice = choices.GetArrayElementAtIndex(index);
            ResetChoiceToDefaults(choice);
            choice.FindPropertyRelative("text").stringValue = "Завершить разговор.";
            choice.FindPropertyRelative("endsDialogue").boolValue = true;
            choice.FindPropertyRelative("kind").enumValueIndex = (int)DialogueChoiceKind.Exit;
        }

        private static void AddActiveCheckChoice(SerializedProperty choices, DialogueChoiceKind kind)
        {
            int index = choices.arraySize;
            choices.arraySize++;
            SerializedProperty choice = choices.GetArrayElementAtIndex(index);
            ResetChoiceToDefaults(choice);
            choice.FindPropertyRelative("text").stringValue =
                kind == DialogueChoiceKind.ActiveDecisive ? "Новая решающая проверка" : "Новая возвратная проверка";
            choice.FindPropertyRelative("kind").enumValueIndex = (int)kind;
            choice.FindPropertyRelative("choiceId").stringValue = Guid.NewGuid().ToString("N");

            SerializedProperty check = choice.FindPropertyRelative("check");
            check.FindPropertyRelative("CheckId").stringValue = Guid.NewGuid().ToString("N");
            check.FindPropertyRelative("Kind").enumValueIndex = (int)kind;
            check.FindPropertyRelative("Difficulty").intValue = NarrativeDifficulty.Ordinary;
        }

        // Полностью сбрасывает текстовый блок перед заполнением — см.
        // причину в комментарии к ResetChoiceToDefaults.
        private static void ResetTextBlockToDefaults(SerializedProperty block)
        {
            block.FindPropertyRelative("blockId").stringValue = string.Empty;
            block.FindPropertyRelative("kind").enumValueIndex = (int)DialogueTextBlockKind.MainLine;
            block.FindPropertyRelative("speakerIdOverride").stringValue = string.Empty;
            block.FindPropertyRelative("text").stringValue = string.Empty;
            block.FindPropertyRelative("hasPassiveCheck").boolValue = false;

            SerializedProperty conditions = block.FindPropertyRelative("conditions").FindPropertyRelative("Conditions");
            if (conditions != null)
                conditions.arraySize = 0;

            SerializedProperty passiveCheck = block.FindPropertyRelative("passiveCheck");
            passiveCheck.FindPropertyRelative("CheckId").stringValue = string.Empty;
            passiveCheck.FindPropertyRelative("Kind").enumValueIndex = (int)NarrativeCheckKind.Passive;
            passiveCheck.FindPropertyRelative("Difficulty").intValue = NarrativeDifficulty.Ordinary;
            passiveCheck.FindPropertyRelative("CompetencyId").stringValue = string.Empty;
            SerializedProperty modifierRules = passiveCheck.FindPropertyRelative("ModifierRules");
            if (modifierRules != null)
                modifierRules.arraySize = 0;

            block.FindPropertyRelative("onRevealEffects").arraySize = 0;
        }

        private static void AddTextBlock(SerializedProperty textBlocks, DialogueTextBlockKind kind)
        {
            int index = textBlocks.arraySize;
            textBlocks.arraySize++;
            SerializedProperty block = textBlocks.GetArrayElementAtIndex(index);
            ResetTextBlockToDefaults(block);
            block.FindPropertyRelative("blockId").stringValue = Guid.NewGuid().ToString("N");
            block.FindPropertyRelative("kind").enumValueIndex = (int)kind;
            block.FindPropertyRelative("text").stringValue = "Новый текст.";
            block.isExpanded = true;
        }

        private static void SetNodeEditorPosition(SerializedProperty node, Vector2 position)
        {
            SerializedProperty editorPosition = node.FindPropertyRelative("editorPosition");
            SerializedProperty hasEditorPosition = node.FindPropertyRelative("hasEditorPosition");
            if (editorPosition != null)
                editorPosition.vector2Value = position;
            if (hasEditorPosition != null)
                hasEditorPosition.boolValue = true;
        }

        private void InitializeDialogue(SerializedProperty dialogue, string id)
        {
            dialogue.FindPropertyRelative("id").stringValue = id;
            dialogue.FindPropertyRelative("title").stringValue = "Новый диалог";
            dialogue.FindPropertyRelative("category").enumValueIndex = (int)DialogueCategory.Test;
            dialogue.FindPropertyRelative("status").enumValueIndex = (int)DialogueProductionStatus.Working;
            dialogue.FindPropertyRelative("developerComment").stringValue = string.Empty;
            dialogue.FindPropertyRelative("startNodeId").stringValue = "start";
            dialogue.FindPropertyRelative("tags").arraySize = 0;
            dialogue.FindPropertyRelative("schemaVersion").intValue = DialogueDefinitionData.CurrentSchemaVersion;

            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            nodes.arraySize = 1;
            SerializedProperty node = nodes.GetArrayElementAtIndex(0);
            node.FindPropertyRelative("id").stringValue = "start";
            node.FindPropertyRelative("speakerId").stringValue = database.Speakers.Count > 0 ? database.Speakers[0].Id : string.Empty;
            node.FindPropertyRelative("text").stringValue = "Новая реплика.";
            node.FindPropertyRelative("textBlocks").arraySize = 0;
            SerializedProperty choices = node.FindPropertyRelative("choices");
            choices.arraySize = 1;
            SerializedProperty choice = choices.GetArrayElementAtIndex(0);
            ResetChoiceToDefaults(choice);
            choice.FindPropertyRelative("text").stringValue = "Завершить разговор.";
            choice.FindPropertyRelative("nextNodeId").stringValue = string.Empty;
            choice.FindPropertyRelative("endsDialogue").boolValue = true;
            choice.FindPropertyRelative("kind").enumValueIndex = (int)DialogueChoiceKind.Exit;
            SetNodeEditorPosition(node, new Vector2(80f, 80f));
        }

        // §11: миграция помечает диалог как переведённый на новую схему.
        // Данные не удаляются и не переписываются — только добавляется по
        // одному основному текстовому блоку на узел без textBlocks и
        // стабильные ChoiceId там, где их ещё нет.
        private void MigrateDialogueSchema(SerializedProperty dialogue)
        {
            Undo.RecordObject(database, "Migrate Dialogue Schema");

            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");
            for (int nodeIndex = 0; nodeIndex < nodes.arraySize; nodeIndex++)
            {
                SerializedProperty node = nodes.GetArrayElementAtIndex(nodeIndex);
                string nodeId = node.FindPropertyRelative("id").stringValue;
                SerializedProperty textBlocks = node.FindPropertyRelative("textBlocks");
                string legacyText = node.FindPropertyRelative("text").stringValue;

                if (textBlocks.arraySize == 0 && !string.IsNullOrEmpty(legacyText))
                {
                    textBlocks.arraySize = 1;
                    SerializedProperty block = textBlocks.GetArrayElementAtIndex(0);
                    ResetTextBlockToDefaults(block);
                    block.FindPropertyRelative("blockId").stringValue = Guid.NewGuid().ToString("N");
                    block.FindPropertyRelative("kind").enumValueIndex = (int)DialogueTextBlockKind.MainLine;
                    block.FindPropertyRelative("text").stringValue = legacyText;
                }

                SerializedProperty choices = node.FindPropertyRelative("choices");
                for (int choiceIndex = 0; choiceIndex < choices.arraySize; choiceIndex++)
                {
                    SerializedProperty choiceId = choices.GetArrayElementAtIndex(choiceIndex).FindPropertyRelative("choiceId");
                    if (string.IsNullOrWhiteSpace(choiceId.stringValue))
                        choiceId.stringValue = (nodeId ?? string.Empty) + "#c" + choiceIndex;
                }
            }

            dialogue.FindPropertyRelative("schemaVersion").intValue = DialogueDefinitionData.CurrentSchemaVersion;
            dialogue.serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
        }

        // §13: "При дублировании обязательно генерировать новые BlockId,
        // ChoiceId, CheckId и EffectExecutionId". CheckId переименовывается
        // согласованно: если эффект UnlockCheck внутри того же диалога
        // ссылался на старый CheckId, ссылка обновляется на новый.
        private static void RegenerateNarrativeIdentifiers(SerializedProperty dialogue)
        {
            Dictionary<string, string> checkIdRemap = new Dictionary<string, string>(StringComparer.Ordinal);
            SerializedProperty nodes = dialogue.FindPropertyRelative("nodes");

            for (int nodeIndex = 0; nodeIndex < nodes.arraySize; nodeIndex++)
            {
                SerializedProperty node = nodes.GetArrayElementAtIndex(nodeIndex);
                SerializedProperty textBlocks = node.FindPropertyRelative("textBlocks");
                for (int i = 0; i < textBlocks.arraySize; i++)
                {
                    RegisterCheckIdRemap(
                        textBlocks.GetArrayElementAtIndex(i).FindPropertyRelative("passiveCheck").FindPropertyRelative("CheckId"),
                        checkIdRemap);
                }

                SerializedProperty choices = node.FindPropertyRelative("choices");
                for (int i = 0; i < choices.arraySize; i++)
                {
                    RegisterCheckIdRemap(
                        choices.GetArrayElementAtIndex(i).FindPropertyRelative("check").FindPropertyRelative("CheckId"),
                        checkIdRemap);
                }
            }

            for (int nodeIndex = 0; nodeIndex < nodes.arraySize; nodeIndex++)
            {
                SerializedProperty node = nodes.GetArrayElementAtIndex(nodeIndex);

                SerializedProperty textBlocks = node.FindPropertyRelative("textBlocks");
                for (int i = 0; i < textBlocks.arraySize; i++)
                {
                    SerializedProperty block = textBlocks.GetArrayElementAtIndex(i);
                    SerializedProperty blockId = block.FindPropertyRelative("blockId");
                    if (!string.IsNullOrWhiteSpace(blockId.stringValue))
                        blockId.stringValue = Guid.NewGuid().ToString("N");

                    ApplyCheckIdRemap(block.FindPropertyRelative("passiveCheck").FindPropertyRelative("CheckId"), checkIdRemap);
                    RegenerateEffectIds(block.FindPropertyRelative("onRevealEffects"), checkIdRemap);
                }

                SerializedProperty choices = node.FindPropertyRelative("choices");
                for (int i = 0; i < choices.arraySize; i++)
                {
                    SerializedProperty choice = choices.GetArrayElementAtIndex(i);
                    SerializedProperty choiceId = choice.FindPropertyRelative("choiceId");
                    if (!string.IsNullOrWhiteSpace(choiceId.stringValue))
                        choiceId.stringValue = Guid.NewGuid().ToString("N");

                    ApplyCheckIdRemap(choice.FindPropertyRelative("check").FindPropertyRelative("CheckId"), checkIdRemap);
                    RegenerateEffectIds(choice.FindPropertyRelative("successEffects"), checkIdRemap);
                    RegenerateEffectIds(choice.FindPropertyRelative("failureEffects"), checkIdRemap);
                }
            }
        }

        private static void RegisterCheckIdRemap(SerializedProperty checkIdProperty, Dictionary<string, string> remap)
        {
            if (checkIdProperty == null)
                return;

            string oldId = checkIdProperty.stringValue;
            if (string.IsNullOrWhiteSpace(oldId) || remap.ContainsKey(oldId))
                return;

            remap[oldId] = Guid.NewGuid().ToString("N");
        }

        private static void ApplyCheckIdRemap(SerializedProperty checkIdProperty, Dictionary<string, string> remap)
        {
            if (checkIdProperty == null)
                return;

            string oldId = checkIdProperty.stringValue;
            if (!string.IsNullOrWhiteSpace(oldId) && remap.TryGetValue(oldId, out string newId))
                checkIdProperty.stringValue = newId;
        }

        private static void RegenerateEffectIds(SerializedProperty effects, Dictionary<string, string> checkIdRemap)
        {
            if (effects == null)
                return;

            for (int i = 0; i < effects.arraySize; i++)
            {
                SerializedProperty effect = effects.GetArrayElementAtIndex(i);
                SerializedProperty executionId = effect.FindPropertyRelative("EffectExecutionId");
                if (executionId != null && !string.IsNullOrWhiteSpace(executionId.stringValue))
                    executionId.stringValue = Guid.NewGuid().ToString("N");

                SerializedProperty type = effect.FindPropertyRelative("Type");
                SerializedProperty stringParam = effect.FindPropertyRelative("StringParam");
                if (type != null && stringParam != null &&
                    (NarrativeEffectType)type.enumValueIndex == NarrativeEffectType.UnlockCheck &&
                    checkIdRemap.TryGetValue(stringParam.stringValue ?? string.Empty, out string remapped))
                {
                    stringParam.stringValue = remapped;
                }
            }
        }

        private void StartPreview(string dialogueId)
        {
            validationIssues.Clear();

            NarrativeDialogueRuntimeSession session = new NarrativeDialogueRuntimeSession();
            BuildPreviewContext(out HeroProfileData hero, out NarrativeStateData state, out List<string> companions, out List<string> items);

            bool started = session.Start(
                database,
                dialogueId,
                hero,
                state,
                out NarrativeDialogueView view,
                out string error,
                companions,
                items,
                previewWorldSeed);

            if (!started)
            {
                previewSession = null;
                previewView = null;
                previewDialogueId = string.Empty;
                previewMessage = error;
                previewLastCheckPresentation = null;
                if (!string.IsNullOrWhiteSpace(error))
                    validationIssues.Add(error);
                return;
            }

            // §15: Preview всегда строится тем же путём, что и production —
            // revealHiddenTextForAuthor только добавляет авторский debug-текст,
            // не меняет набор видимых блоков.
            view = session.BuildViewPreview(previewRevealHiddenTextForAuthor);

            previewSession = session;
            previewView = view;
            previewDialogueId = dialogueId;
            previewMessage = string.Empty;
            previewLastCheckPresentation = null;
        }

        private void BuildPreviewContext(
            out HeroProfileData hero,
            out NarrativeStateData state,
            out List<string> companions,
            out List<string> items)
        {
            hero = previewHero;
            state = previewState;

            // "Запустить / с начала" обязан давать чистый прогресс: история
            // проверок и применённых эффектов не хранится в CSV-полях автора,
            // поэтому обнуляется явно при каждом запуске.
            state.CheckHistory.Clear();
            state.AppliedEffectExecutionIds.Clear();

            state.Flags.Clear();
            foreach (string flag in SplitCsv(previewFlagsCsv))
                state.SetFlag(flag);

            state.Knowledge.Clear();
            foreach (string knowledge in SplitCsv(previewKnowledgeCsv))
                state.AddKnowledge(knowledge);

            state.Relations.Clear();
            foreach (string pair in SplitCsv(previewRelationsCsv))
            {
                string[] parts = pair.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int value))
                    state.ChangeRelation(parts[0].Trim(), value);
            }

            companions = SplitCsv(previewCompanionsCsv);
            items = SplitCsv(previewItemsCsv);
        }

        private static List<string> SplitCsv(string csv)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(csv))
                return result;

            string[] parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    result.Add(trimmed);
            }
            return result;
        }

        private void ValidateSelected(string dialogueId)
        {
            database.CollectValidationIssuesForDialogue(dialogueId, validationIssues);
            if (validationIssues.Count == 0)
                previewMessage = "Выбранный диалог прошёл проверку.";
        }

        private void ValidateAll()
        {
            database.CollectValidationIssues(validationIssues);
            previewMessage = validationIssues.Count == 0
                ? "База диалогов прошла проверку: ошибок не найдено."
                : "Найдено ошибок: " + validationIssues.Count + ".";
        }

        private void ResetPreview()
        {
            if (previewSession != null && previewSession.IsActive)
                previewSession.End();
            previewSession = null;
            previewView = null;
            previewDialogueId = string.Empty;
            previewMessage = string.Empty;
            previewLastCheckPresentation = null;
            validationIssues.Clear();
        }

        private string MakeUniqueDialogueId(string baseId)
        {
            string clean = string.IsNullOrWhiteSpace(baseId) ? "dialogue" : baseId.Trim();
            string candidate = clean;
            int suffix = 2;
            while (database.FindDialogue(candidate) != null)
                candidate = clean + "_" + suffix++;
            return candidate;
        }

        private string MakeUniqueSpeakerId(string baseId)
        {
            string clean = string.IsNullOrWhiteSpace(baseId) ? "speaker" : baseId.Trim();
            string candidate = clean;
            int suffix = 2;
            while (database.FindSpeaker(candidate) != null)
                candidate = clean + "_" + suffix++;
            return candidate;
        }

        private static string MakeUniqueNodeId(SerializedProperty nodes, string baseId)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.arraySize; i++)
                ids.Add(nodes.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue);

            string candidate = baseId;
            int suffix = 2;
            while (ids.Contains(candidate))
                candidate = baseId + "_" + suffix++;
            return candidate;
        }

        private static string[] GetNodeIds(SerializedProperty nodes)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < nodes.arraySize; i++)
            {
                string id = nodes.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }
            return ids.ToArray();
        }

        private static string CategoryLabel(DialogueCategory category)
        {
            switch (category)
            {
                case DialogueCategory.Test: return "Тестовые";
                case DialogueCategory.MainStory: return "Главный сюжет";
                case DialogueCategory.SideQuest: return "Побочные квесты";
                case DialogueCategory.RandomEncounter: return "Случайные встречи";
                case DialogueCategory.AmbientNpc: return "Обычные NPC";
                case DialogueCategory.Service: return "Служебные";
                default: return category.ToString();
            }
        }

        private static string StatusLabel(DialogueProductionStatus status)
        {
            switch (status)
            {
                case DialogueProductionStatus.Test: return "тестовый";
                case DialogueProductionStatus.Working: return "рабочий";
                case DialogueProductionStatus.Approved: return "утверждённый";
                case DialogueProductionStatus.Disabled: return "отключённый";
                default: return status.ToString();
            }
        }
    }
}
