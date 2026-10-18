using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.Encounters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomSurvival.QuestWorkshopBridge.Editor
{
    public enum WorkshopSceneState
    {
        Plan,
        NoUnityId,
        Errors,
        New,
        Same,
        Changed
    }

    // Состояние одной сцены мастерской относительно баз Unity.
    public sealed class WorkshopSceneReport
    {
        public WorkshopScene Scene;
        public WorkshopSceneState State;
        public readonly List<WorkshopIssue> Issues = new List<WorkshopIssue>();
        public readonly List<string> Differences = new List<string>();
        public string EncounterState = string.Empty;
        public bool HasErrors => Issues.Exists(i => i.Severity == WorkshopIssueSeverity.Error);
        public bool Transferable => !HasErrors && (State == WorkshopSceneState.New || State == WorkshopSceneState.Changed || EncounterState == "новая" || EncounterState == "изменена");
    }

    public sealed class WorkshopImportResult
    {
        public readonly List<string> AddedDialogues = new List<string>();
        public readonly List<string> UpdatedDialogues = new List<string>();
        public readonly List<string> AddedSpeakers = new List<string>();
        public readonly List<string> AddedFlags = new List<string>();
        public readonly List<string> AddedEncounters = new List<string>();
        public readonly List<string> UpdatedEncounters = new List<string>();
        public readonly List<string> Skipped = new List<string>();
        public readonly List<string> ValidationIssues = new List<string>();

        public bool Changed =>
            AddedDialogues.Count + UpdatedDialogues.Count + AddedSpeakers.Count + AddedFlags.Count +
            AddedEncounters.Count + UpdatedEncounters.Count > 0;

        public string Summary()
        {
            StringBuilder text = new StringBuilder();
            Append(text, "Новые диалоги", AddedDialogues);
            Append(text, "Обновлены диалоги", UpdatedDialogues);
            Append(text, "Новые говорящие", AddedSpeakers);
            Append(text, "Новые флаги реестра", AddedFlags);
            Append(text, "Новые встречи", AddedEncounters);
            Append(text, "Обновлены встречи", UpdatedEncounters);
            Append(text, "Пропущены", Skipped);
            Append(text, "Проверка базы диалогов", ValidationIssues);
            return text.Length == 0 ? "Изменений нет." : text.ToString().TrimEnd();
        }

        private static void Append(StringBuilder text, string title, List<string> items)
        {
            if (items.Count == 0)
                return;
            text.Append(title).Append(" (").Append(items.Count).Append("): ").Append(string.Join(", ", items)).Append('\n');
        }
    }

    // Анализ и перенос сцен мастерской в базы Unity. Работает с переданными
    // экземплярами баз — окно передаёт ассеты проекта, тесты — временные копии.
    public static class QuestWorkshopImporter
    {
        public static List<WorkshopSceneReport> Analyze(
            WorkshopProject project,
            DialogueDatabaseAsset dialogues,
            EncounterDatabaseAsset encounters)
        {
            List<WorkshopSceneReport> reports = new List<WorkshopSceneReport>();
            Dictionary<string, WorkshopScene> owners = new Dictionary<string, WorkshopScene>(StringComparer.Ordinal);
            foreach (WorkshopQuest quest in project.Quests)
            {
                foreach (WorkshopScene scene in quest.TransferUnits())
                {
                    WorkshopSceneReport report = new WorkshopSceneReport { Scene = scene };
                    reports.Add(report);
                    string unityId = scene.UnityId;
                    if (scene.Nodes.Count == 0 && unityId.Length == 0)
                    {
                        report.State = WorkshopSceneState.Plan;
                        continue;
                    }

                    report.Issues.AddRange(QuestWorkshopConverter.ValidateScene(project, scene, encounters));
                    if (unityId.Length > 0)
                    {
                        if (owners.TryGetValue(unityId, out WorkshopScene other))
                            report.Issues.Add(new WorkshopIssue
                            {
                                Severity = WorkshopIssueSeverity.Error,
                                Message = "ID «" + unityId + "» уже у сцены «" + other.Title + "» (" + other.Quest.Title + ")."
                            });
                        else
                            owners[unityId] = scene;
                    }

                    if (unityId.Length == 0)
                    {
                        report.State = WorkshopSceneState.NoUnityId;
                        continue;
                    }

                    DialogueDefinitionData existing = dialogues.FindDialogue(unityId);
                    if (scene.Nodes.Count > 0 && !report.HasErrors)
                    {
                        foreach (string issue in DatabaseIssues(project, scene, dialogues, existing))
                            report.Issues.Add(new WorkshopIssue { Severity = WorkshopIssueSeverity.Warning, Message = "Проверка базы диалогов: " + issue });
                    }
                    if (scene.Nodes.Count > 0)
                    {
                        report.Differences.AddRange(QuestWorkshopConverter.DescribeDifferences(project, scene, existing));
                        report.State = existing == null
                            ? WorkshopSceneState.New
                            : QuestWorkshopConverter.SameAsDatabase(project, scene, existing) ? WorkshopSceneState.Same : WorkshopSceneState.Changed;
                    }
                    else
                    {
                        report.State = WorkshopSceneState.Plan;
                    }

                    if (scene.Encounter != null && scene.Encounter.Enabled && scene.Encounter.EncounterId.Length > 0 && encounters != null)
                    {
                        EncounterDefinition current = encounters.FindById(scene.Encounter.EncounterId);
                        EncounterDefinition next = QuestWorkshopConverter.BuildEncounter(project, scene, current);
                        report.EncounterState = current == null ? "новая" : QuestWorkshopConverter.SameEncounter(current, next) ? "совпадает" : "изменена";
                        if (report.EncounterState != "совпадает")
                            report.Differences.Add("Встреча " + scene.Encounter.EncounterId + ": " + report.EncounterState + ".");
                    }

                    if (report.HasErrors && report.State != WorkshopSceneState.Plan)
                        report.State = WorkshopSceneState.Errors;
                }
            }
            return reports;
        }

        public static WorkshopImportResult Apply(
            WorkshopProject project,
            IEnumerable<WorkshopScene> scenes,
            DialogueDatabaseAsset dialogues,
            EncounterDatabaseAsset encounters,
            EncounterFlagRegistryAsset flags)
        {
            WorkshopImportResult result = new WorkshopImportResult();
            List<DialogueDefinitionData> dialogueList = PrivateList<DialogueDatabaseAsset, DialogueDefinitionData>(dialogues, "dialogues");
            List<DialogueSpeakerData> speakerList = PrivateList<DialogueDatabaseAsset, DialogueSpeakerData>(dialogues, "speakers");

            foreach (WorkshopScene scene in scenes)
            {
                List<WorkshopIssue> issues = QuestWorkshopConverter.ValidateScene(project, scene, encounters);
                if (issues.Exists(i => i.Severity == WorkshopIssueSeverity.Error))
                {
                    result.Skipped.Add(scene.Title + " — " + issues.Find(i => i.Severity == WorkshopIssueSeverity.Error).Message);
                    continue;
                }

                string unityId = scene.UnityId;
                DialogueDefinitionData existing = dialogues.FindDialogue(unityId);
                if (!QuestWorkshopConverter.SameAsDatabase(project, scene, existing))
                {
                    DialogueMirror mirror = QuestWorkshopConverter.BuildDialogue(project, scene, existing, null);
                    string json = JsonUtility.ToJson(mirror);
                    if (existing != null)
                    {
                        JsonUtility.FromJsonOverwrite(json, existing);
                        result.UpdatedDialogues.Add(unityId);
                    }
                    else
                    {
                        DialogueDefinitionData created = new DialogueDefinitionData();
                        JsonUtility.FromJsonOverwrite(json, created);
                        dialogueList.Add(created);
                        result.AddedDialogues.Add(unityId);
                    }

                    foreach (NodeMirror node in mirror.nodes)
                    {
                        AddSpeaker(project, dialogues, speakerList, node.speakerId, result);
                        foreach (BlockMirror block in node.textBlocks)
                            AddSpeaker(project, dialogues, speakerList, block.speakerIdOverride, result);
                    }
                }

                if (scene.Encounter != null && scene.Encounter.Enabled && encounters != null)
                {
                    List<EncounterDefinition> encounterList = PrivateList<EncounterDatabaseAsset, EncounterDefinition>(encounters, "encounters");
                    EncounterDefinition current = encounters.FindById(scene.Encounter.EncounterId);
                    EncounterDefinition next = QuestWorkshopConverter.BuildEncounter(project, scene, current);
                    if (current == null)
                    {
                        encounterList.Add(next);
                        result.AddedEncounters.Add(next.EncounterId);
                    }
                    else if (!QuestWorkshopConverter.SameEncounter(current, next))
                    {
                        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(next), current);
                        result.UpdatedEncounters.Add(next.EncounterId);
                    }

                    if (flags != null)
                    {
                        List<EncounterFlagDefinition> flagList = PrivateList<EncounterFlagRegistryAsset, EncounterFlagDefinition>(flags, "flags");
                        foreach (string flagId in QuestWorkshopConverter.EncounterFlags(scene.Encounter))
                        {
                            if (flags.FindById(flagId) != null)
                                continue;
                            WorkshopFact fact = FindFact(project, "unity_flag_", flagId);
                            flagList.Add(new EncounterFlagDefinition
                            {
                                FlagId = flagId,
                                DisplayName = fact != null && fact.Title.Length > 0 ? fact.Title : flagId,
                                Description = fact != null ? fact.Text : string.Empty,
                                Category = fact != null && fact.Category.Length > 0 ? fact.Category : scene.Quest.Line,
                                Status = fact != null && Enum.TryParse(fact.Status, out EncounterFlagStatus status) ? status : EncounterFlagStatus.Active,
                                FutureUseNotes = "Заведён Мастерской квестов: встреча " + scene.Encounter.EncounterId + "."
                            });
                            result.AddedFlags.Add(flagId);
                        }
                    }
                }

                List<string> validation = new List<string>();
                dialogues.CollectValidationIssuesForDialogue(unityId, validation);
                foreach (string issue in validation)
                    result.ValidationIssues.Add(issue);
            }
            return result;
        }

        // Прогоняет собранный диалог через проверку самой базы диалогов на
        // временной копии: окно показывает те же замечания, что и Unity.
        public static List<string> DatabaseIssues(WorkshopProject project, WorkshopScene scene, DialogueDatabaseAsset dialogues, DialogueDefinitionData existing)
        {
            List<string> issues = new List<string>();
            DialogueDatabaseAsset temp = ScriptableObject.CreateInstance<DialogueDatabaseAsset>();
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                List<DialogueSpeakerData> speakers = PrivateList<DialogueDatabaseAsset, DialogueSpeakerData>(temp, "speakers");
                speakers.AddRange(dialogues.Speakers);
                DialogueMirror mirror = QuestWorkshopConverter.BuildDialogue(project, scene, existing, null);
                WorkshopImportResult ignored = new WorkshopImportResult();
                foreach (NodeMirror node in mirror.nodes)
                {
                    AddSpeaker(project, temp, speakers, node.speakerId, ignored);
                    foreach (BlockMirror block in node.textBlocks)
                        AddSpeaker(project, temp, speakers, block.speakerIdOverride, ignored);
                }
                DialogueDefinitionData built = new DialogueDefinitionData();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(mirror), built);
                PrivateList<DialogueDatabaseAsset, DialogueDefinitionData>(temp, "dialogues").Add(built);
                temp.CollectValidationIssuesForDialogue(mirror.id, issues);
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
            string prefix = "Диалог '" + scene.UnityId + "'";
            for (int i = 0; i < issues.Count; i++)
            {
                string text = issues[i];
                if (text.StartsWith(prefix, StringComparison.Ordinal))
                    text = text.Substring(prefix.Length).TrimStart(' ', ':', '/').Trim();
                issues[i] = text;
            }
            return issues;
        }

        private static WorkshopFact FindFact(WorkshopProject project, string prefix, string unityId)
        {
            if (project.Facts.TryGetValue(prefix + unityId, out WorkshopFact fact))
                return fact;
            foreach (WorkshopFact candidate in project.Facts.Values)
                if (candidate.UnityId == unityId || candidate.Id == unityId)
                    return candidate;
            return null;
        }

        private static void AddSpeaker(
            WorkshopProject project,
            DialogueDatabaseAsset database,
            List<DialogueSpeakerData> speakers,
            string speakerId,
            WorkshopImportResult result)
        {
            if (string.IsNullOrEmpty(speakerId) || database.FindSpeaker(speakerId) != null)
                return;
            WorkshopPerson person = null;
            if (!project.People.TryGetValue("unity_person_" + speakerId, out person))
            {
                foreach (WorkshopPerson candidate in project.People.Values)
                    if (candidate.UnityId == speakerId || candidate.Id == speakerId)
                        person = candidate;
            }

            Dictionary<string, object> json = new Dictionary<string, object>
            {
                ["id"] = speakerId,
                ["displayName"] = person != null && person.Name.Length > 0 ? person.Name : speakerId,
                ["role"] = person != null ? person.Role : string.Empty
            };
            speakers.Add(JsonUtility.FromJson<DialogueSpeakerData>(QuestWorkshopJson.Serialize(json)));
            result.AddedSpeakers.Add(speakerId);
        }

        private static List<T> PrivateList<TOwner, T>(TOwner owner, string fieldName)
        {
            FieldInfo field = typeof(TOwner).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(typeof(TOwner).Name, fieldName);
            List<T> list = (List<T>)field.GetValue(owner);
            if (list == null)
            {
                list = new List<T>();
                field.SetValue(owner, list);
            }
            return list;
        }

        // ── Снимок для мастерской ───────────────────────────────────────────

        public static string BuildSnapshotJson(
            DialogueDatabaseAsset dialogues,
            EncounterDatabaseAsset encounters,
            EncounterFlagRegistryAsset flags,
            string commit)
        {
            StringBuilder json = new StringBuilder();
            json.Append("{\"format\":\"ks-unity-snapshot\",\"version\":1");
            json.Append(",\"createdAt\":").Append(QuestWorkshopJson.Serialize(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
            json.Append(",\"commit\":").Append(QuestWorkshopJson.Serialize(commit ?? string.Empty));
            AppendArray(json, "speakers", dialogues != null ? (IEnumerable<object>)dialogues.Speakers : Array.Empty<object>());
            AppendArray(json, "dialogues", dialogues != null ? (IEnumerable<object>)dialogues.Dialogues : Array.Empty<object>());
            AppendArray(json, "flags", flags != null ? (IEnumerable<object>)flags.Flags : Array.Empty<object>());
            AppendArray(json, "encounters", encounters != null ? (IEnumerable<object>)encounters.Encounters : Array.Empty<object>());
            AppendArray(json, "pools", encounters != null ? (IEnumerable<object>)encounters.Pools : Array.Empty<object>());
            json.Append('}');
            return json.ToString();
        }

        private static void AppendArray(StringBuilder json, string name, IEnumerable<object> items)
        {
            json.Append(",\"").Append(name).Append("\":[");
            bool first = true;
            foreach (object item in items)
            {
                if (item == null)
                    continue;
                if (!first)
                    json.Append(',');
                first = false;
                json.Append(JsonUtility.ToJson(item));
            }
            json.Append(']');
        }

        // Короткий хеш текущего коммита (для подписи снимка), без запуска git.
        public static string ReadGitCommit(string projectRoot)
        {
            try
            {
                string gitDir = Path.Combine(projectRoot, ".git");
                string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
                if (!head.StartsWith("ref: ", StringComparison.Ordinal))
                    return head.Length >= 7 ? head.Substring(0, 7) : head;
                string reference = head.Substring(5).Trim();
                string refPath = Path.Combine(gitDir, reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(refPath))
                    return File.ReadAllText(refPath).Trim().Substring(0, 7);
                string packed = Path.Combine(gitDir, "packed-refs");
                if (File.Exists(packed))
                {
                    foreach (string line in File.ReadAllLines(packed))
                        if (line.EndsWith(" " + reference, StringComparison.Ordinal))
                            return line.Substring(0, 7);
                }
            }
            catch (Exception)
            {
            }
            return string.Empty;
        }
    }
}
