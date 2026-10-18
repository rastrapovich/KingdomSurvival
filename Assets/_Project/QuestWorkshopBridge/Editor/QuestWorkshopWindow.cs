using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.Encounters;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.QuestWorkshopBridge.Editor
{
    // Пути Мастерской квестов. Папка QuestWorkshop лежит в корне проекта игры
    // и не хранится в git (решение пользователя 18.10.2026).
    public static class QuestWorkshopPaths
    {
        public const string DialogueDatabasePath = "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset";
        public const string EncounterDatabasePath = "Assets/_Project/Encounters/Resources/Encounters/KingdomSurvivalEncounters.asset";
        public const string EncounterFlagsPath = "Assets/_Project/Encounters/Resources/Encounters/KingdomSurvivalEncounterFlags.asset";

        public static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
        public static string Folder => Path.Combine(ProjectRoot, "QuestWorkshop");
        public static string Workspace => Path.Combine(Folder, "workspace.json");
        public static string Snapshot => Path.Combine(Folder, "unity-snapshot.json");
        public static string Html => Path.Combine(Folder, "Мастерская квестов.html");
        public static string SyncState => Path.Combine(Folder, "unity-sync.json");

        public static DialogueDatabaseAsset LoadDialogues()
        {
            return AssetDatabase.LoadAssetAtPath<DialogueDatabaseAsset>(DialogueDatabasePath);
        }

        public static EncounterDatabaseAsset LoadEncounters()
        {
            return AssetDatabase.LoadAssetAtPath<EncounterDatabaseAsset>(EncounterDatabasePath);
        }

        public static EncounterFlagRegistryAsset LoadFlags()
        {
            return AssetDatabase.LoadAssetAtPath<EncounterFlagRegistryAsset>(EncounterFlagsPath);
        }

        // Пишет снимок баз Unity для мастерской. Возвращает текст для журнала.
        public static string WriteSnapshot()
        {
            if (!Directory.Exists(Folder))
                return "Папка мастерской не найдена: " + Folder;
            DialogueDatabaseAsset dialogues = LoadDialogues();
            Dictionary<string, string> media = QuestWorkshopMedia.Export(Folder, dialogues);
            string json = QuestWorkshopImporter.BuildSnapshotJson(dialogues, LoadEncounters(), LoadFlags(), QuestWorkshopImporter.ReadGitCommit(ProjectRoot), media);
            File.WriteAllText(Snapshot, json, new System.Text.UTF8Encoding(false));
            return "Снимок для мастерской обновлён: " + Snapshot;
        }
    }

    public sealed class QuestWorkshopWindow : EditorWindow
    {
        private static readonly Color PlanColor = new Color(0.5f, 0.5f, 0.5f, 0.25f);
        private static readonly Color ErrorColor = new Color(0.85f, 0.3f, 0.25f, 0.35f);
        private static readonly Color NewColor = new Color(0.85f, 0.65f, 0.2f, 0.35f);
        private static readonly Color SameColor = new Color(0.3f, 0.6f, 0.4f, 0.3f);
        private static readonly Color ChangedColor = new Color(0.95f, 0.55f, 0.15f, 0.4f);

        private WorkshopProject project;
        private List<WorkshopSceneReport> reports = new List<WorkshopSceneReport>();
        private readonly HashSet<WorkshopScene> selected = new HashSet<WorkshopScene>();
        private List<WorkshopSpeakerReport> speakerReports = new List<WorkshopSpeakerReport>();
        private readonly HashSet<string> selectedSpeakers = new HashSet<string>(StringComparer.Ordinal);
        private QuestWorkshopSyncState sync = new QuestWorkshopSyncState();
        private WorkshopSceneReport current;
        private DateTime loadedWriteTime;
        private string loadError = string.Empty;

        private Label statusLabel;
        private Label resultLabel;
        private ScrollView listView;
        private ScrollView detailView;
        private TextField searchField;
        private Toggle onlyPendingToggle;
        private Toggle showPlansToggle;
        private Button transferButton;

        [MenuItem("Kingdom Survival/Мастерская квестов")]
        public static void Open()
        {
            QuestWorkshopWindow window = GetWindow<QuestWorkshopWindow>();
            window.titleContent = new GUIContent("Мастерская квестов");
            window.minSize = new Vector2(760, 420);
            window.Show();
        }

        // Для -executeMethod: обновить снимок без окна.
        public static void ExportSnapshotBatch()
        {
            Debug.Log(QuestWorkshopPaths.WriteSnapshot());
        }

        // Для -executeMethod: сверить мастерскую с базами и вывести итог в журнал.
        public static void VerifyBatch()
        {
            if (!File.Exists(QuestWorkshopPaths.Workspace))
            {
                Debug.LogError("Нет файла мастерской: " + QuestWorkshopPaths.Workspace);
                return;
            }
            WorkshopProject loaded = WorkshopProject.Load(QuestWorkshopPaths.Workspace);
            QuestWorkshopSyncState state = QuestWorkshopSyncState.Load(QuestWorkshopPaths.SyncState);
            List<WorkshopSceneReport> list = QuestWorkshopImporter.Analyze(loaded, QuestWorkshopPaths.LoadDialogues(), QuestWorkshopPaths.LoadEncounters(), state);
            foreach (WorkshopSpeakerReport speaker in QuestWorkshopImporter.AnalyzeSpeakers(loaded, QuestWorkshopPaths.LoadDialogues(), state))
                Debug.Log("Мастерская квестов: говорящий " + speaker.SpeakerId + " — " + speaker.Describe());
            state.Save(QuestWorkshopPaths.SyncState);
            foreach (IGrouping<WorkshopSceneState, WorkshopSceneReport> group in list.GroupBy(r => r.State))
                Debug.Log("Мастерская квестов: " + group.Key + " — " + group.Count());
            foreach (WorkshopSceneReport report in list.Where(r => r.State == WorkshopSceneState.Changed || r.State == WorkshopSceneState.Errors || r.EncounterState == "изменена"))
                Debug.Log("Мастерская квестов: " + report.Scene.UnityId + " [" + report.State + ", встреча: " + report.EncounterState + "]\n  " +
                          string.Join("\n  ", report.Differences.Concat(report.Issues.Select(i => i.ToString()))));
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;

            Toolbar toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(OpenWorkshop) { text = "Открыть мастерскую" });
            toolbar.Add(new ToolbarButton(Reload) { text = "Перечитать" });
            toolbar.Add(new ToolbarButton(UpdateSnapshot) { text = "Обновить снимок для мастерской", tooltip = "Записать QuestWorkshop/unity-snapshot.json — его показывает «Из базы Unity»" });
            ToolbarToggle autoSync = new ToolbarToggle
            {
                text = "Автосинхронизация",
                value = QuestWorkshopAutoSync.Enabled,
                tooltip = "Сразу переносить в Unity новые сцены и сцены, которые в Unity с прошлой синхронизации не правили. Конфликты и правки из Unity не трогаются."
            };
            autoSync.RegisterValueChangedCallback(evt => QuestWorkshopAutoSync.Enabled = evt.newValue);
            toolbar.Add(autoSync);
            toolbar.Add(new ToolbarSpacer { flex = true });
            toolbar.Add(new ToolbarButton(() => EditorUtility.RevealInFinder(QuestWorkshopPaths.Folder)) { text = "Папка" });
            root.Add(toolbar);

            statusLabel = new Label { style = { paddingLeft = 6, paddingTop = 4, paddingBottom = 4, whiteSpace = WhiteSpace.Normal } };
            root.Add(statusLabel);

            VisualElement filters = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingRight = 4, alignItems = Align.Center } };
            searchField = new TextField { style = { flexGrow = 1 } };
            searchField.RegisterValueChangedCallback(_ => RebuildList());
            searchField.tooltip = "Поиск по квесту, сцене или ID";
            filters.Add(new Label("Поиск:") { style = { marginRight = 4 } });
            filters.Add(searchField);
            onlyPendingToggle = new Toggle("Только к переносу") { value = true, style = { marginLeft = 10 } };
            onlyPendingToggle.RegisterValueChangedCallback(_ => RebuildList());
            filters.Add(onlyPendingToggle);
            showPlansToggle = new Toggle("Сцены-планы") { value = false, style = { marginLeft = 10 } };
            showPlansToggle.RegisterValueChangedCallback(_ => RebuildList());
            filters.Add(showPlansToggle);
            root.Add(filters);

            TwoPaneSplitView split = new TwoPaneSplitView(0, 470, TwoPaneSplitViewOrientation.Horizontal) { style = { flexGrow = 1 } };
            listView = new ScrollView(ScrollViewMode.Vertical);
            detailView = new ScrollView(ScrollViewMode.Vertical) { style = { paddingLeft = 8, paddingRight = 8 } };
            split.Add(listView);
            split.Add(detailView);
            root.Add(split);

            VisualElement bottom = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingRight = 4, paddingTop = 4, paddingBottom = 4 } };
            bottom.Add(new Button(SelectPending) { text = "Отметить всё к переносу" });
            bottom.Add(new Button(() => { selected.Clear(); RebuildList(); }) { text = "Снять отметки" });
            bottom.Add(new VisualElement { style = { flexGrow = 1 } });
            transferButton = new Button(Transfer) { text = "Перенести в Unity" };
            transferButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            bottom.Add(transferButton);
            root.Add(bottom);

            resultLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 6, paddingBottom = 6 } };
            root.Add(resultLabel);

            Reload();
        }

        private void OnEnable()
        {
            QuestWorkshopAutoSync.Synced += OnAutoSynced;
        }

        private void OnDisable()
        {
            QuestWorkshopAutoSync.Synced -= OnAutoSynced;
        }

        private void OnAutoSynced(WorkshopImportResult result)
        {
            if (statusLabel == null)
                return;
            Reload();
            if (result != null && result.Changed)
                resultLabel.text = "Автосинхронизация: " + result.Summary();
        }

        private void OnFocus()
        {
            if (statusLabel == null || !File.Exists(QuestWorkshopPaths.Workspace))
                return;
            if (File.GetLastWriteTimeUtc(QuestWorkshopPaths.Workspace) != loadedWriteTime)
                Reload();
        }

        private void OpenWorkshop()
        {
            if (!File.Exists(QuestWorkshopPaths.Html))
            {
                EditorUtility.DisplayDialog("Мастерская квестов", "Не найден файл мастерской:\n" + QuestWorkshopPaths.Html +
                    "\n\nСоберите её: node QuestWorkshop/src/build.js", "Понятно");
                return;
            }
            Application.OpenURL(new Uri(QuestWorkshopPaths.Html).AbsoluteUri);
        }

        private void UpdateSnapshot()
        {
            resultLabel.text = QuestWorkshopPaths.WriteSnapshot();
        }

        private void Reload()
        {
            project = null;
            reports = new List<WorkshopSceneReport>();
            loadError = string.Empty;
            string path = QuestWorkshopPaths.Workspace;
            if (!Directory.Exists(QuestWorkshopPaths.Folder))
                loadError = "Нет папки мастерской: " + QuestWorkshopPaths.Folder + ". Мастерская квестов живёт в корне проекта и не хранится в git.";
            else if (!File.Exists(path))
                loadError = "Нет файла проекта мастерской: " + path + ". Откройте мастерскую и подключите папку QuestWorkshop.";
            else
            {
                try
                {
                    project = WorkshopProject.Load(path);
                    loadedWriteTime = File.GetLastWriteTimeUtc(path);
                    DialogueDatabaseAsset dialogues = QuestWorkshopPaths.LoadDialogues();
                    if (dialogues == null)
                        loadError = "База диалогов не найдена: " + QuestWorkshopPaths.DialogueDatabasePath;
                    else
                    {
                        sync = QuestWorkshopSyncState.Load(QuestWorkshopPaths.SyncState);
                        reports = QuestWorkshopImporter.Analyze(project, dialogues, QuestWorkshopPaths.LoadEncounters(), sync);
                        speakerReports = QuestWorkshopImporter.AnalyzeSpeakers(project, dialogues, sync);
                        sync.Save(QuestWorkshopPaths.SyncState);
                    }
                }
                catch (Exception error)
                {
                    loadError = "Не удалось прочитать workspace.json: " + error.Message;
                    project = null;
                }
            }

            HashSet<string> keep = new HashSet<string>(selected.Select(s => s.UnityId));
            selected.Clear();
            foreach (WorkshopSceneReport report in reports)
                if (keep.Contains(report.Scene.UnityId) && report.Transferable)
                    selected.Add(report.Scene);
            HashSet<string> keepSpeakers = new HashSet<string>(selectedSpeakers);
            selectedSpeakers.Clear();
            foreach (WorkshopSpeakerReport speaker in speakerReports)
                if (keepSpeakers.Contains(speaker.SpeakerId) && speaker.Transferable)
                    selectedSpeakers.Add(speaker.SpeakerId);
            current = current != null ? reports.FirstOrDefault(r => r.Scene.UnityId == current.Scene.UnityId && r.Scene.Id == current.Scene.Id) : null;
            UpdateStatus();
            RebuildList();
            ShowDetails(current);
        }

        private void UpdateStatus()
        {
            if (statusLabel == null)
                return;
            if (loadError.Length > 0)
            {
                statusLabel.text = loadError;
                return;
            }
            int pending = reports.Count(r => r.Transferable && r.State != WorkshopSceneState.Conflict);
            int unityNewer = reports.Count(r => r.State == WorkshopSceneState.UnityNewer);
            int conflicts = reports.Count(r => r.State == WorkshopSceneState.Conflict);
            int same = reports.Count(r => r.State == WorkshopSceneState.Same);
            int errors = reports.Count(r => r.State == WorkshopSceneState.Errors || r.State == WorkshopSceneState.NoUnityId);
            int plans = reports.Count(r => r.State == WorkshopSceneState.Plan);
            statusLabel.text = $"Проект «{project.Name}» · сохранён {FormatTime(project.SavedAt)} · версия {project.Revision} · историй {project.Quests.Count}\n" +
                               $"К переносу: {pending} · правили в Unity: {unityNewer} · конфликтов: {conflicts} · совпадает: {same} · с ошибками: {errors} · план: {plans}" +
                               (speakerReports.Count > 0 ? $" · говорящих к сверке: {speakerReports.Count}" : string.Empty) +
                               (QuestWorkshopAutoSync.Enabled ? "\nАвтосинхронизация включена: безопасные изменения мастерской переносятся сами." : string.Empty);
        }

        private static string FormatTime(string iso)
        {
            return DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime time)
                ? time.ToLocalTime().ToString("dd.MM HH:mm")
                : "—";
        }

        private bool Visible(WorkshopSceneReport report)
        {
            if (report.State == WorkshopSceneState.Plan && !showPlansToggle.value)
                return false;
            if (onlyPendingToggle.value && report.State == WorkshopSceneState.Same && report.EncounterState != "изменена" && report.EncounterState != "новая")
                return false;
            if (report.Scene.Quest.Archived && report.State == WorkshopSceneState.Same)
                return false;
            string query = (searchField.value ?? string.Empty).Trim();
            if (query.Length == 0)
                return true;
            return (report.Scene.Title + " " + report.Scene.Quest.Title + " " + report.Scene.UnityId + " " + report.Scene.Quest.Line)
                .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RebuildList()
        {
            if (listView == null)
                return;
            listView.Clear();
            if (project == null)
                return;

            IEnumerable<IGrouping<string, WorkshopSceneReport>> lines = reports
                .Where(Visible)
                .GroupBy(r => string.IsNullOrEmpty(r.Scene.Quest.Line) ? "Без сюжетной линии" : r.Scene.Quest.Line)
                .OrderBy(g => g.Key, StringComparer.CurrentCulture);
            int shown = 0;
            if (speakerReports.Count > 0)
            {
                Foldout speakers = new Foldout { text = "Говорящие (" + speakerReports.Count + ")", value = true };
                speakers.style.unityFontStyleAndWeight = FontStyle.Bold;
                foreach (WorkshopSpeakerReport speaker in speakerReports)
                {
                    VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 4, unityFontStyleAndWeight = FontStyle.Normal } };
                    Toggle toggle = new Toggle { value = selectedSpeakers.Contains(speaker.SpeakerId) };
                    toggle.SetEnabled(speaker.Transferable);
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue)
                            selectedSpeakers.Add(speaker.SpeakerId);
                        else
                            selectedSpeakers.Remove(speaker.SpeakerId);
                        RebuildList();
                    });
                    row.Add(toggle);
                    row.Add(new Label(speaker.SpeakerId + ": " + speaker.Describe()) { style = { whiteSpace = WhiteSpace.Normal, flexShrink = 1 } });
                    speakers.Add(row);
                    shown++;
                }
                listView.Add(speakers);
            }
            foreach (IGrouping<string, WorkshopSceneReport> line in lines)
            {
                Foldout lineFoldout = new Foldout { text = line.Key + " (" + line.Count() + ")", value = true };
                lineFoldout.style.unityFontStyleAndWeight = FontStyle.Bold;
                foreach (IGrouping<WorkshopQuest, WorkshopSceneReport> quest in line.GroupBy(r => r.Scene.Quest))
                {
                    Label questLabel = new Label(quest.Key.Title + (quest.Key.Archived ? " · архив" : string.Empty))
                    {
                        style = { unityFontStyleAndWeight = FontStyle.Normal, marginTop = 4, marginLeft = 2, color = new Color(0.6f, 0.6f, 0.6f) }
                    };
                    lineFoldout.Add(questLabel);
                    foreach (WorkshopSceneReport report in quest)
                    {
                        lineFoldout.Add(BuildRow(report));
                        shown++;
                    }
                }
                listView.Add(lineFoldout);
            }
            if (shown == 0)
                listView.Add(new Label(onlyPendingToggle.value ? "Всё совпадает с Unity — переносить нечего." : "Ничего не найдено.") { style = { paddingLeft = 8, paddingTop = 8 } });

            int count = selected.Count + selectedSpeakers.Count;
            transferButton.text = count > 0 ? $"Перенести в Unity ({count})" : "Перенести в Unity";
            transferButton.SetEnabled(count > 0);
        }

        private VisualElement BuildRow(WorkshopSceneReport report)
        {
            VisualElement row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 4, paddingTop = 1, paddingBottom = 1,
                    unityFontStyleAndWeight = FontStyle.Normal
                }
            };
            if (current == report)
                row.style.backgroundColor = new Color(0.3f, 0.5f, 0.9f, 0.18f);

            Toggle toggle = new Toggle { value = selected.Contains(report.Scene) };
            toggle.SetEnabled(report.Transferable);
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                    selected.Add(report.Scene);
                else
                    selected.Remove(report.Scene);
                RebuildList();
            });
            row.Add(toggle);

            Label state = new Label(StateText(report))
            {
                style =
                {
                    backgroundColor = StateColor(report), paddingLeft = 4, paddingRight = 4, marginRight = 6, minWidth = 88,
                    unityTextAlign = TextAnchor.MiddleCenter, borderTopLeftRadius = 3, borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3, borderBottomRightRadius = 3
                }
            };
            row.Add(state);
            Label title = new Label(report.Scene.IsMerged ? "Весь квест одним диалогом" : report.Scene.Title) { style = { flexGrow = 1, overflow = Overflow.Hidden } };
            row.Add(title);
            Label id = new Label(report.Scene.UnityId) { style = { color = new Color(0.55f, 0.55f, 0.55f), marginLeft = 6 } };
            row.Add(id);
            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.target is Toggle || (evt.target as VisualElement)?.parent is Toggle)
                    return;
                current = report;
                RebuildList();
                ShowDetails(report);
            });
            return row;
        }

        private static string StateText(WorkshopSceneReport report)
        {
            string text = report.State switch
            {
                WorkshopSceneState.Plan => "план",
                WorkshopSceneState.NoUnityId => "нет ID",
                WorkshopSceneState.Errors => "ошибки",
                WorkshopSceneState.New => "новая",
                WorkshopSceneState.Same => "совпадает",
                WorkshopSceneState.Changed => report.Direction == WorkshopSyncDirection.WorkshopNewer ? "из мастерской" : "отличается",
                WorkshopSceneState.UnityNewer => "правили в Unity",
                WorkshopSceneState.Conflict => "конфликт",
                _ => report.State.ToString()
            };
            if (report.State == WorkshopSceneState.Same && (report.EncounterState == "новая" || report.EncounterState == "изменена"))
                text = "встреча";
            return text;
        }

        private static Color StateColor(WorkshopSceneReport report)
        {
            switch (report.State)
            {
                case WorkshopSceneState.Plan: return PlanColor;
                case WorkshopSceneState.NoUnityId:
                case WorkshopSceneState.Errors: return ErrorColor;
                case WorkshopSceneState.New: return NewColor;
                case WorkshopSceneState.Changed: return ChangedColor;
                case WorkshopSceneState.Conflict: return ErrorColor;
                case WorkshopSceneState.UnityNewer: return PlanColor;
                default: return report.Transferable ? ChangedColor : SameColor;
            }
        }

        private void ShowDetails(WorkshopSceneReport report)
        {
            if (detailView == null)
                return;
            detailView.Clear();
            if (report == null)
            {
                detailView.Add(new Label("Выберите сцену слева.\n\nСцена мастерской = диалог в базе Unity (ID диалога задаётся в редакторе сцены). " +
                                         "Сцена-встреча дополнительно создаёт или обновляет запись в базе встреч.\n\n" +
                                         "Перенос обновляет текст, ответы, проверки и эффекты; иллюстрации, портреты и раскладка графа в Unity сохраняются. " +
                                         "Отменить перенос можно через Edit → Undo.") { style = { whiteSpace = WhiteSpace.Normal, paddingTop = 8 } });
                return;
            }

            WorkshopScene scene = report.Scene;
            detailView.Add(new Label(scene.Title) { style = { fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold, paddingTop = 6, whiteSpace = WhiteSpace.Normal } });
            if (scene.IsMerged)
                detailView.Add(new Label("Весь квест уходит в Unity одним диалогом: сцены связаны переходами или так задано в мастерской.") { style = { whiteSpace = WhiteSpace.Normal, marginTop = 2 } });
            detailView.Add(new Label(scene.Quest.Title + " · " + (scene.Quest.Line.Length > 0 ? scene.Quest.Line : "без линии")) { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.6f, 0.6f, 0.6f) } });
            detailView.Add(new Label("ID диалога: " + (scene.UnityId.Length > 0 ? scene.UnityId : "—") + " · состояние: " + StateText(report) +
                                     (report.EncounterState.Length > 0 ? " · встреча " + scene.Encounter.EncounterId + ": " + report.EncounterState : string.Empty))
            { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } });

            if (report.Differences.Count > 0)
            {
                detailView.Add(Header("Что изменится в Unity"));
                foreach (string line in report.Differences)
                    detailView.Add(Bullet(line));
            }

            if (report.Issues.Count > 0)
            {
                detailView.Add(Header("Замечания"));
                foreach (WorkshopIssue issue in report.Issues)
                {
                    Label label = Bullet(issue.ToString());
                    if (issue.Severity == WorkshopIssueSeverity.Error)
                        label.style.color = new Color(0.95f, 0.45f, 0.4f);
                    else if (issue.Severity == WorkshopIssueSeverity.Warning)
                        label.style.color = new Color(0.95f, 0.7f, 0.3f);
                    detailView.Add(label);
                }
            }

            if (report.State == WorkshopSceneState.Same && report.EncounterState != "новая" && report.EncounterState != "изменена")
                detailView.Add(new Label("Сцена совпадает с диалогом в Unity.") { style = { marginTop = 8 } });

            if (scene.UnityId.Length > 0 && QuestWorkshopPaths.LoadDialogues()?.FindDialogue(scene.UnityId) != null)
                detailView.Add(new Button(() => EditorApplication.ExecuteMenuItem("Kingdom Survival/База диалогов")) { text = "Открыть Базу диалогов", style = { marginTop = 10, alignSelf = Align.FlexStart } });
        }

        private static Label Header(string text)
        {
            return new Label(text) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10, marginBottom = 2 } };
        }

        private static Label Bullet(string text)
        {
            return new Label("• " + text) { style = { whiteSpace = WhiteSpace.Normal, marginLeft = 4 } };
        }

        private void SelectPending()
        {
            selected.Clear();
            foreach (WorkshopSceneReport report in reports)
                if (report.Transferable && report.State != WorkshopSceneState.Conflict && Visible(report))
                    selected.Add(report.Scene);
            selectedSpeakers.Clear();
            foreach (WorkshopSpeakerReport speaker in speakerReports)
                if (speaker.Transferable && speaker.Direction != WorkshopSyncDirection.Conflict)
                    selectedSpeakers.Add(speaker.SpeakerId);
            RebuildList();
        }

        private void Transfer()
        {
            if (project == null || selected.Count + selectedSpeakers.Count == 0)
                return;
            DialogueDatabaseAsset dialogues = QuestWorkshopPaths.LoadDialogues();
            EncounterDatabaseAsset encounters = QuestWorkshopPaths.LoadEncounters();
            EncounterFlagRegistryAsset flags = QuestWorkshopPaths.LoadFlags();
            if (dialogues == null)
            {
                resultLabel.text = "База диалогов не найдена.";
                return;
            }

            List<WorkshopScene> scenes = reports.Where(r => selected.Contains(r.Scene) && r.Transferable).Select(r => r.Scene).ToList();
            int updates = reports.Count(r => selected.Contains(r.Scene) && (r.State == WorkshopSceneState.Changed || r.State == WorkshopSceneState.Conflict));
            int conflicts = reports.Count(r => selected.Contains(r.Scene) && r.State == WorkshopSceneState.Conflict);
            List<WorkshopSpeakerReport> speakers = speakerReports.Where(s => selectedSpeakers.Contains(s.SpeakerId) && s.Transferable).ToList();
            if (!EditorUtility.DisplayDialog("Перенести в Unity",
                    $"Сцен к переносу: {scenes.Count} (новых: {scenes.Count - updates}, обновлений: {updates}" +
                    (conflicts > 0 ? $", из них конфликтов: {conflicts} — правки Unity в них будут заменены" : string.Empty) + ")" +
                    (speakers.Count > 0 ? $"; говорящих: {speakers.Count}" : string.Empty) + ".\n\n" +
                    "Текст, ответы, проверки и эффекты в базе Unity будут заменены версией мастерской. " +
                    "Иллюстрации, портреты и раскладка графа сохранятся. Отменить можно через Edit → Undo.",
                    "Перенести", "Отмена"))
                return;

            List<UnityEngine.Object> targets = new List<UnityEngine.Object> { dialogues };
            if (encounters != null)
                targets.Add(encounters);
            if (flags != null)
                targets.Add(flags);
            Undo.RecordObjects(targets.ToArray(), "Мастерская квестов: перенос в Unity");

            WorkshopImportResult result = QuestWorkshopImporter.Apply(project, scenes, dialogues, encounters, flags, sync);
            QuestWorkshopImporter.ApplySpeakers(project, speakers, dialogues, result, sync);
            sync.Save(QuestWorkshopPaths.SyncState);
            if (result.Changed)
            {
                foreach (UnityEngine.Object target in targets)
                    EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
                QuestWorkshopPaths.WriteSnapshot();
            }

            selected.Clear();
            selectedSpeakers.Clear();
            Reload();
            resultLabel.text = result.Summary() + (result.Changed ? "\nСнимок для мастерской обновлён." : string.Empty);
            Debug.Log("Мастерская квестов: перенос в Unity.\n" + result.Summary());
        }
    }
}
