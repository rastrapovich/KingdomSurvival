using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    public sealed class DevelopmentTrackerWindow : EditorWindow
    {
        private const string PrefsPrefix = "KingdomSurvival.DevTracker.";
        private const string PrefsSearch = PrefsPrefix + "Search";
        private const string PrefsStatusFilter = PrefsPrefix + "StatusFilter";
        private const string PrefsCategoryFilter = PrefsPrefix + "CategoryFilter";
        private const string PrefsOnlyIncomplete = PrefsPrefix + "OnlyIncomplete";
        private const string PrefsOnlyRequired = PrefsPrefix + "OnlyRequired";
        private const string PrefsOnlyBlocked = PrefsPrefix + "OnlyBlocked";
        private const string PrefsExpandedPhases = PrefsPrefix + "ExpandedPhases";
        private const string PrefsExpandedTasks = PrefsPrefix + "ExpandedTasks";

        private DevelopmentPlanAsset plan;

        private VisualElement contentContainer;
        private Label milestoneLabel;
        private ProgressBar overallProgressBar;
        private Label overallProgressLabel;
        private Label nextTaskLabel;
        private Label counterLabel;
        private VisualElement diagnosticsContainer;
        private Label diagnosticsSummaryLabel;

        private TextField searchField;
        private PopupField<string> statusFilterField;
        private PopupField<string> categoryFilterField;
        private Toggle onlyIncompleteToggle;
        private Toggle onlyRequiredToggle;
        private Toggle onlyBlockedToggle;

        private readonly HashSet<string> expandedPhaseIds = new HashSet<string>();
        private readonly HashSet<string> expandedTaskIds = new HashSet<string>();

        [MenuItem("Kingdom Survival/Этапы разработки")]
        public static void OpenWindow()
        {
            DevelopmentTrackerWindow window = GetWindow<DevelopmentTrackerWindow>();
            window.titleContent = new GUIContent("Этапы разработки");
            window.minSize = new Vector2(980f, 640f);
        }

        public void CreateGUI()
        {
            LoadPrefs();

            plan = AssetDatabase.LoadAssetAtPath<DevelopmentPlanAsset>(DevelopmentPlanBootstrap.AssetPath);

            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1f;

            if (plan == null)
            {
                BuildMissingPlanView();
                return;
            }

            plan.MigrateIfNeeded();

            BuildTopPanel();
            BuildFilterPanel();

            contentContainer = new ScrollView(ScrollViewMode.Vertical);
            contentContainer.style.flexGrow = 1f;
            contentContainer.style.paddingLeft = 6f;
            contentContainer.style.paddingRight = 6f;
            rootVisualElement.Add(contentContainer);

            BuildDiagnosticsPanel();

            RefreshAll();
        }

        private void BuildMissingPlanView()
        {
            VisualElement box = new VisualElement();
            box.style.flexGrow = 1f;
            box.style.justifyContent = Justify.Center;
            box.style.alignItems = Align.Center;

            Label label = new Label(
                "План разработки ещё не создан по пути:\n" + DevelopmentPlanBootstrap.AssetPath);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 12f;
            box.Add(label);

            Button create = new Button(() =>
            {
                DevelopmentPlanBootstrap.CreateAndSeedPlan();
                CreateGUI();
            })
            { text = "СОЗДАТЬ ПЛАН ПО ИНСТРУКЦИИ" };
            create.style.height = 28f;
            create.style.width = 260f;
            box.Add(create);

            rootVisualElement.Add(box);
        }

        // ------------------------------------------------------------
        // Верхняя панель
        // ------------------------------------------------------------

        private void BuildTopPanel()
        {
            VisualElement panel = new VisualElement();
            panel.style.paddingLeft = 8f;
            panel.style.paddingRight = 8f;
            panel.style.paddingTop = 6f;
            panel.style.paddingBottom = 6f;
            panel.style.borderBottomWidth = 1f;
            panel.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f, 1f);

            milestoneLabel = new Label();
            milestoneLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            milestoneLabel.style.fontSize = 14f;
            panel.Add(milestoneLabel);

            VisualElement progressRow = new VisualElement();
            progressRow.style.flexDirection = FlexDirection.Row;
            progressRow.style.alignItems = Align.Center;
            progressRow.style.marginTop = 4f;

            overallProgressBar = new ProgressBar { title = string.Empty };
            overallProgressBar.style.flexGrow = 1f;
            overallProgressBar.style.height = 20f;
            progressRow.Add(overallProgressBar);

            overallProgressLabel = new Label();
            overallProgressLabel.style.marginLeft = 8f;
            overallProgressLabel.style.minWidth = 220f;
            progressRow.Add(overallProgressLabel);
            panel.Add(progressRow);

            nextTaskLabel = new Label();
            nextTaskLabel.style.marginTop = 4f;
            nextTaskLabel.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(nextTaskLabel);

            counterLabel = new Label();
            counterLabel.style.marginTop = 2f;
            counterLabel.style.color = new Color(0.65f, 0.65f, 0.65f, 1f);
            counterLabel.style.fontSize = 10f;
            panel.Add(counterLabel);

            VisualElement buttonsRow = new VisualElement();
            buttonsRow.style.flexDirection = FlexDirection.Row;
            buttonsRow.style.marginTop = 6f;

            AddButton(buttonsRow, "ПРОВЕРИТЬ ПЛАН", RefreshAll);
            AddButton(buttonsRow, "СОХРАНИТЬ", SavePlan);
            AddButton(buttonsRow, "РАЗВЕРНУТЬ ВСЁ", () => SetAllPhasesExpanded(true));
            AddButton(buttonsRow, "СВЕРНУТЬ ВСЁ", () => SetAllPhasesExpanded(false));
            AddButton(buttonsRow, "ПЕРЕСОБРАТЬ ПО ИНСТРУКЦИИ", () =>
            {
                DevelopmentTrackerMenuCommands.ResetPlanToInstructionSeed();
                plan = AssetDatabase.LoadAssetAtPath<DevelopmentPlanAsset>(DevelopmentPlanBootstrap.AssetPath);
                RefreshAll();
            });

            Label spacer = new Label();
            spacer.style.flexGrow = 1f;
            buttonsRow.Add(spacer);

            AddButton(buttonsRow, "БАЗА ДИАЛОГОВ", DevelopmentTrackerMenuCommands.OpenDialogueDatabase);
            AddButton(buttonsRow, "БАЗА СУЩЕСТВ", DevelopmentTrackerMenuCommands.OpenUnitDatabase);
            AddButton(buttonsRow, "UI КОНСТРУКТОР", DevelopmentTrackerMenuCommands.OpenUILayoutConstructor);

            panel.Add(buttonsRow);
            rootVisualElement.Add(panel);
        }

        private static void AddButton(VisualElement parent, string text, Action onClick)
        {
            Button button = new Button(onClick) { text = text };
            button.style.height = 24f;
            button.style.marginRight = 6f;
            parent.Add(button);
        }

        // ------------------------------------------------------------
        // Панель фильтров
        // ------------------------------------------------------------

        private void BuildFilterPanel()
        {
            VisualElement panel = new VisualElement();
            panel.style.flexDirection = FlexDirection.Row;
            panel.style.alignItems = Align.Center;
            panel.style.paddingLeft = 8f;
            panel.style.paddingRight = 8f;
            panel.style.paddingTop = 6f;
            panel.style.paddingBottom = 6f;
            panel.style.borderBottomWidth = 1f;
            panel.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f, 1f);

            searchField = new TextField { value = searchField?.value ?? EditorPrefs.GetString(PrefsSearch, string.Empty) };
            searchField.style.width = 220f;
            searchField.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetString(PrefsSearch, evt.newValue);
                RefreshContentOnly();
            });
            panel.Add(searchField);

            List<string> statusChoices = new List<string> { "Все статусы" };
            statusChoices.AddRange(DevelopmentPlanLabels.AllStatuses.Select(DevelopmentPlanLabels.StatusLabel));
            statusFilterField = new PopupField<string>(statusChoices, ClampIndex(EditorPrefs.GetInt(PrefsStatusFilter, 0), statusChoices.Count));
            statusFilterField.style.marginLeft = 8f;
            statusFilterField.style.width = 200f;
            statusFilterField.RegisterValueChangedCallback(_ =>
            {
                EditorPrefs.SetInt(PrefsStatusFilter, statusFilterField.index);
                RefreshContentOnly();
            });
            panel.Add(statusFilterField);

            List<string> categoryChoices = new List<string> { "Все категории" };
            categoryChoices.AddRange(DevelopmentPlanLabels.AllCategories.Select(DevelopmentPlanLabels.CategoryLabel));
            categoryFilterField = new PopupField<string>(categoryChoices, ClampIndex(EditorPrefs.GetInt(PrefsCategoryFilter, 0), categoryChoices.Count));
            categoryFilterField.style.marginLeft = 8f;
            categoryFilterField.style.width = 180f;
            categoryFilterField.RegisterValueChangedCallback(_ =>
            {
                EditorPrefs.SetInt(PrefsCategoryFilter, categoryFilterField.index);
                RefreshContentOnly();
            });
            panel.Add(categoryFilterField);

            onlyIncompleteToggle = BuildFilterToggle(panel, "Только незавершённые", PrefsOnlyIncomplete);
            onlyRequiredToggle = BuildFilterToggle(panel, "Только обязательные", PrefsOnlyRequired);
            onlyBlockedToggle = BuildFilterToggle(panel, "Только заблокированные", PrefsOnlyBlocked);

            rootVisualElement.Add(panel);
        }

        private Toggle BuildFilterToggle(VisualElement parent, string label, string prefsKey)
        {
            Toggle toggle = new Toggle(label) { value = EditorPrefs.GetBool(prefsKey, false) };
            toggle.style.marginLeft = 10f;
            toggle.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool(prefsKey, evt.newValue);
                RefreshContentOnly();
            });
            parent.Add(toggle);
            return toggle;
        }

        private static int ClampIndex(int index, int count)
        {
            if (count <= 0) return 0;
            return Mathf.Clamp(index, 0, count - 1);
        }

        // ------------------------------------------------------------
        // EditorPrefs — раскрытые foldout (раздел 4.8)
        // ------------------------------------------------------------

        private void LoadPrefs()
        {
            expandedPhaseIds.Clear();
            foreach (string id in EditorPrefs.GetString(PrefsExpandedPhases, string.Empty).Split(
                         new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                expandedPhaseIds.Add(id);

            expandedTaskIds.Clear();
            foreach (string id in EditorPrefs.GetString(PrefsExpandedTasks, string.Empty).Split(
                         new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                expandedTaskIds.Add(id);
        }

        private void SavePhaseExpandedPrefs()
        {
            EditorPrefs.SetString(PrefsExpandedPhases, string.Join("|", expandedPhaseIds));
        }

        private void SaveTaskExpandedPrefs()
        {
            EditorPrefs.SetString(PrefsExpandedTasks, string.Join("|", expandedTaskIds));
        }

        private void SetAllPhasesExpanded(bool expanded)
        {
            expandedPhaseIds.Clear();
            if (expanded && plan?.phases != null)
            {
                foreach (DevelopmentPhaseData phase in plan.phases)
                    if (phase != null) expandedPhaseIds.Add(phase.id);
            }
            SavePhaseExpandedPrefs();
            RefreshContentOnly();
        }

        // ------------------------------------------------------------
        // Пересборка содержимого
        // ------------------------------------------------------------

        private void RefreshAll()
        {
            if (plan == null)
                return;

            RefreshTopPanel();
            RefreshContentOnly();
            RefreshDiagnostics();
        }

        private void RefreshTopPanel()
        {
            DevelopmentPhaseData milestone = plan.FindPhase(plan.currentMilestoneId);
            milestoneLabel.text = "Текущая веха: " +
                (milestone != null ? milestone.title + " (" + milestone.id + ")" : plan.currentMilestoneId);

            DevelopmentPlanProgress.Summary overall = DevelopmentPlanProgress.ComputeOverall(plan);
            overallProgressBar.value = overall.Ratio * 100f;
            overallProgressBar.title = Mathf.RoundToInt(overall.Ratio * 100f) + "%";
            overallProgressLabel.text = overall.completedRequired + "/" + overall.totalRequired +
                " обязательных задач · опционально " + overall.optionalCompleted + "/" + overall.optionalTotal;

            counterLabel.text = "Заблокировано: " + overall.blockedCount +
                " · Нужна проверка в Unity: " + overall.needsUnityCheckCount;

            DevelopmentTaskData next = DevelopmentPlanProgress.FindNextRecommendedTask(plan, plan.currentMilestoneId);
            nextTaskLabel.text = next != null
                ? "Следующая рекомендуемая задача: [" + next.id + "] " + next.title
                : "Следующая рекомендуемая задача: нет доступных (всё выполнено или заблокировано).";
        }

        private void RefreshContentOnly()
        {
            if (contentContainer == null || plan == null)
                return;

            contentContainer.Clear();

            List<DevelopmentPhaseData> phases = new List<DevelopmentPhaseData>(plan.phases ?? new List<DevelopmentPhaseData>());
            phases.Sort((a, b) => a.order.CompareTo(b.order));

            foreach (DevelopmentPhaseData phase in phases)
            {
                if (phase == null)
                    continue;

                List<DevelopmentTaskData> visibleTasks = GetVisibleTasks(phase);
                bool noFiltersActive = string.IsNullOrEmpty(CurrentSearch())
                    && CurrentStatusFilterIndex() == 0
                    && CurrentCategoryFilterIndex() == 0
                    && !onlyIncompleteToggle.value && !onlyRequiredToggle.value && !onlyBlockedToggle.value;

                // Пустой этап без фильтров всё равно показывается (чтобы было видно,
                // что этап существует и ещё не наполнен); под активным фильтром
                // этап без совпадений скрывается.
                if (!noFiltersActive && visibleTasks.Count == 0)
                    continue;

                contentContainer.Add(BuildPhaseFoldout(phase, visibleTasks));
            }

            RefreshTopPanel();
        }

        private string CurrentSearch()
        {
            return (searchField?.value ?? string.Empty).Trim();
        }

        private int CurrentStatusFilterIndex()
        {
            return statusFilterField?.index ?? 0;
        }

        private int CurrentCategoryFilterIndex()
        {
            return categoryFilterField?.index ?? 0;
        }

        private List<DevelopmentTaskData> GetVisibleTasks(DevelopmentPhaseData phase)
        {
            List<DevelopmentTaskData> result = new List<DevelopmentTaskData>();
            if (phase.tasks == null)
                return result;

            string query = CurrentSearch();
            int statusIndex = CurrentStatusFilterIndex();
            int categoryIndex = CurrentCategoryFilterIndex();

            foreach (DevelopmentTaskData task in phase.tasks)
            {
                if (task == null)
                    continue;
                if (!MatchesSearch(task, query))
                    continue;
                if (statusIndex > 0 && task.status != DevelopmentPlanLabels.AllStatuses[statusIndex - 1])
                    continue;
                if (categoryIndex > 0 && task.category != DevelopmentPlanLabels.AllCategories[categoryIndex - 1])
                    continue;
                if (onlyIncompleteToggle.value && task.status == DevelopmentTaskStatus.Completed)
                    continue;
                if (onlyRequiredToggle.value && !task.required)
                    continue;
                if (onlyBlockedToggle.value && task.status != DevelopmentTaskStatus.Blocked)
                    continue;

                result.Add(task);
            }

            return result;
        }

        private static bool MatchesSearch(DevelopmentTaskData task, string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;

            return (task.id != null && task.id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                || (task.title != null && task.title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                || (task.details != null && task.details.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ------------------------------------------------------------
        // Этап (foldout) и строки задач
        // ------------------------------------------------------------

        private VisualElement BuildPhaseFoldout(DevelopmentPhaseData phase, List<DevelopmentTaskData> tasksToShow)
        {
            DevelopmentPlanProgress.Summary phaseProgress = DevelopmentPlanProgress.ComputePhase(phase);
            DevelopmentTaskStatus phaseStatus = phase.ComputeStatus();

            Foldout foldout = new Foldout
            {
                text = "[" + phase.id + "] " + phase.title +
                       "  ·  " + DevelopmentPlanLabels.StatusLabel(phaseStatus) +
                       "  ·  " + phaseProgress.completedRequired + "/" + phaseProgress.totalRequired,
                value = expandedPhaseIds.Contains(phase.id)
            };
            foldout.style.marginTop = 4f;
            foldout.style.marginBottom = 4f;
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) expandedPhaseIds.Add(phase.id);
                else expandedPhaseIds.Remove(phase.id);
                SavePhaseExpandedPrefs();
            });

            if (!string.IsNullOrEmpty(phase.purpose))
            {
                Label purpose = new Label(phase.purpose);
                purpose.style.whiteSpace = WhiteSpace.Normal;
                purpose.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                purpose.style.fontSize = 11f;
                purpose.style.marginBottom = 6f;
                foldout.Add(purpose);
            }

            List<DevelopmentTaskData> ordered = new List<DevelopmentTaskData>(tasksToShow);
            ordered.Sort((a, b) => a.order.CompareTo(b.order));

            foreach (DevelopmentTaskData task in ordered)
                foldout.Add(BuildTaskRow(phase, task));

            return foldout;
        }

        private VisualElement BuildTaskRow(DevelopmentPhaseData phase, DevelopmentTaskData task)
        {
            VisualElement container = new VisualElement();
            container.style.borderBottomWidth = 1f;
            container.style.borderBottomColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            container.style.paddingTop = 3f;
            container.style.paddingBottom = 3f;

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            Toggle completeToggle = new Toggle { value = task.IsCompleted };
            completeToggle.style.marginRight = 4f;
            completeToggle.RegisterValueChangedCallback(evt => OnToggleCompleted(task, evt.newValue));
            row.Add(completeToggle);

            List<string> statusChoices = DevelopmentPlanLabels.AllStatuses.Select(DevelopmentPlanLabels.StatusLabel).ToList();
            PopupField<string> statusPopup = new PopupField<string>(statusChoices, (int)task.status);
            statusPopup.style.width = 170f;
            statusPopup.RegisterValueChangedCallback(evt =>
            {
                DevelopmentTaskStatus newStatus = DevelopmentPlanLabels.AllStatuses[statusPopup.index];
                SetTaskStatus(task, newStatus);
                completeToggle.SetValueWithoutNotify(task.IsCompleted);
            });
            row.Add(statusPopup);

            Label categoryLabel = new Label(DevelopmentPlanLabels.CategoryLabel(task.category));
            categoryLabel.style.width = 90f;
            categoryLabel.style.fontSize = 10f;
            categoryLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            row.Add(categoryLabel);

            Label idLabel = new Label(task.id);
            idLabel.style.width = 80f;
            idLabel.style.fontSize = 10f;
            idLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(idLabel);

            Label titleLabel = new Label((task.required ? "" : "(опц.) ") + task.title);
            titleLabel.style.flexGrow = 1f;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            row.Add(titleLabel);

            if (!AreRequiredDependenciesSatisfied(task))
            {
                Label depWarning = new Label("⛓ зависимость");
                depWarning.tooltip = "Не все обязательные зависимости этой задачи завершены.";
                StyleIndicator(depWarning, new Color(0.85f, 0.6f, 0.2f, 1f));
                row.Add(depWarning);
            }

            if (task.status == DevelopmentTaskStatus.Blocked)
            {
                Label blockedWarning = new Label("⛔ блок");
                blockedWarning.tooltip = string.IsNullOrEmpty(task.blockerNote) ? "Нет blockerNote." : task.blockerNote;
                StyleIndicator(blockedWarning, new Color(0.8f, 0.3f, 0.3f, 1f));
                row.Add(blockedWarning);
            }

            if (!string.IsNullOrEmpty(task.optionalAutoCheckId))
            {
                Label autoIndicator = new Label("⚙ авто");
                autoIndicator.tooltip = "У задачи есть автопроверка: " + task.optionalAutoCheckId;
                StyleIndicator(autoIndicator, new Color(0.4f, 0.6f, 0.85f, 1f));
                row.Add(autoIndicator);
            }

            Button expandButton = new Button { text = expandedTaskIds.Contains(task.id) ? "▲" : "▼" };
            expandButton.style.width = 24f;
            expandButton.style.marginLeft = 4f;
            container.Add(row);

            VisualElement detail = BuildTaskDetail(phase, task);
            detail.style.display = expandedTaskIds.Contains(task.id) ? DisplayStyle.Flex : DisplayStyle.None;

            expandButton.clicked += () =>
            {
                bool nowExpanded = detail.style.display == DisplayStyle.None;
                detail.style.display = nowExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                expandButton.text = nowExpanded ? "▲" : "▼";
                if (nowExpanded) expandedTaskIds.Add(task.id);
                else expandedTaskIds.Remove(task.id);
                SaveTaskExpandedPrefs();
            };
            row.Add(expandButton);

            container.Add(detail);
            return container;
        }

        private static void StyleIndicator(Label label, Color color)
        {
            label.style.fontSize = 10f;
            label.style.color = color;
            label.style.marginLeft = 6f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        private bool AreRequiredDependenciesSatisfied(DevelopmentTaskData task)
        {
            if (task.dependencies == null || task.dependencies.Count == 0)
                return true;

            foreach (string depId in task.dependencies)
            {
                if (string.IsNullOrEmpty(depId))
                    continue;

                DevelopmentTaskData depTask = plan.FindTask(depId, out _);
                if (depTask != null && depTask.required && depTask.status != DevelopmentTaskStatus.Completed)
                    return false;

                if (depTask == null)
                {
                    DevelopmentPhaseData depPhase = plan.FindPhase(depId);
                    if (depPhase != null && depPhase.required && depPhase.ComputeStatus() != DevelopmentTaskStatus.Completed)
                        return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------
        // Галочка и статус (раздел 4.4)
        // ------------------------------------------------------------

        private void OnToggleCompleted(DevelopmentTaskData task, bool completed)
        {
            Undo.RecordObject(plan, "Изменить статус задачи");

            if (completed)
            {
                if (!AreRequiredDependenciesSatisfied(task))
                {
                    bool confirmed = EditorUtility.DisplayDialog(
                        "Незавершённая обязательная зависимость",
                        "У задачи \"" + task.id + "\" есть обязательная зависимость, которая ещё не завершена. " +
                        "Всё равно отметить задачу выполненной?",
                        "Отметить выполненной", "Отмена");
                    if (!confirmed)
                    {
                        RefreshContentOnly();
                        return;
                    }
                }

                SetTaskStatus(task, DevelopmentTaskStatus.Completed);
            }
            else
            {
                // Снятие галочки переводит задачу в «В работе», а не в «Не начато» (раздел 4.4).
                SetTaskStatus(task, DevelopmentTaskStatus.InProgress);
            }
        }

        private void SetTaskStatus(DevelopmentTaskData task, DevelopmentTaskStatus status)
        {
            Undo.RecordObject(plan, "Изменить статус задачи");

            task.status = status;
            if (status == DevelopmentTaskStatus.Completed && string.IsNullOrEmpty(task.completedAt))
                task.completedAt = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (status != DevelopmentTaskStatus.Completed)
                task.completedAt = string.Empty;

            EditorUtility.SetDirty(plan);
            RefreshContentOnly();
        }

        // ------------------------------------------------------------
        // Карточка задачи
        // ------------------------------------------------------------

        private VisualElement BuildTaskDetail(DevelopmentPhaseData phase, DevelopmentTaskData task)
        {
            VisualElement detail = new VisualElement();
            detail.style.paddingLeft = 24f;
            detail.style.paddingRight = 8f;
            detail.style.paddingTop = 4f;
            detail.style.paddingBottom = 8f;
            detail.style.backgroundColor = new Color(1f, 1f, 1f, 0.02f);

            TextField titleField = BuildTextField("Название", task.title, v => { task.title = v; MarkDirty(); });
            detail.Add(titleField);

            TextField detailsField = BuildTextField("Действия", task.details, v => { task.details = v; MarkDirty(); }, multiline: true);
            detail.Add(detailsField);

            VisualElement metaRow = new VisualElement();
            metaRow.style.flexDirection = FlexDirection.Row;
            metaRow.style.marginTop = 4f;

            PopupField<string> categoryPopup = new PopupField<string>(
                "Категория",
                DevelopmentPlanLabels.AllCategories.Select(DevelopmentPlanLabels.CategoryLabel).ToList(),
                (int)task.category);
            categoryPopup.style.width = 220f;
            categoryPopup.RegisterValueChangedCallback(evt =>
            {
                task.category = DevelopmentPlanLabels.AllCategories[categoryPopup.index];
                MarkDirty();
                RefreshContentOnly();
            });
            metaRow.Add(categoryPopup);

            Toggle requiredToggle = new Toggle("Обязательная") { value = task.required };
            requiredToggle.style.marginLeft = 12f;
            requiredToggle.RegisterValueChangedCallback(evt => { task.required = evt.newValue; MarkDirty(); RefreshContentOnly(); });
            metaRow.Add(requiredToggle);

            IntegerField orderField = new IntegerField("Порядок") { value = task.order };
            orderField.style.width = 140f;
            orderField.style.marginLeft = 12f;
            orderField.RegisterValueChangedCallback(evt => { task.order = evt.newValue; MarkDirty(); });
            metaRow.Add(orderField);

            detail.Add(metaRow);

            VisualElement metaRow2 = new VisualElement();
            metaRow2.style.flexDirection = FlexDirection.Row;
            metaRow2.style.marginTop = 4f;

            PopupField<string> priorityPopup = new PopupField<string>(
                "Приоритет",
                DevelopmentPlanLabels.AllPriorities.Select(DevelopmentPlanLabels.PriorityLabel).ToList(),
                (int)task.priority);
            priorityPopup.style.width = 200f;
            priorityPopup.RegisterValueChangedCallback(evt =>
            {
                task.priority = DevelopmentPlanLabels.AllPriorities[priorityPopup.index];
                MarkDirty();
            });
            metaRow2.Add(priorityPopup);

            PopupField<string> verificationPopup = new PopupField<string>(
                "Тип проверки",
                DevelopmentPlanLabels.AllVerificationTypes.Select(DevelopmentPlanLabels.VerificationTypeLabel).ToList(),
                (int)task.verificationType);
            verificationPopup.style.width = 200f;
            verificationPopup.style.marginLeft = 12f;
            verificationPopup.RegisterValueChangedCallback(evt =>
            {
                task.verificationType = DevelopmentPlanLabels.AllVerificationTypes[verificationPopup.index];
                MarkDirty();
            });
            metaRow2.Add(verificationPopup);

            PopupField<string> blockerReasonPopup = new PopupField<string>(
                "Причина блокировки",
                DevelopmentPlanLabels.AllBlockerReasons.Select(DevelopmentPlanLabels.BlockerReasonLabel).ToList(),
                (int)task.blockerReason);
            blockerReasonPopup.style.width = 220f;
            blockerReasonPopup.style.marginLeft = 12f;
            blockerReasonPopup.RegisterValueChangedCallback(evt =>
            {
                task.blockerReason = DevelopmentPlanLabels.AllBlockerReasons[blockerReasonPopup.index];
                MarkDirty();
            });
            metaRow2.Add(blockerReasonPopup);

            detail.Add(metaRow2);

            detail.Add(BuildSectionLabel("Критерии приёмки"));
            detail.Add(BuildAcceptanceCriteriaList(task));

            detail.Add(BuildListTextArea("Зависимости (ID через строку)", task.dependencies, MarkDirty));
            detail.Add(BuildListTextArea("Ручные проверки", task.manualChecks, MarkDirty));
            detail.Add(BuildListTextArea("Ссылки на файлы", task.fileReferences, MarkDirty));
            detail.Add(BuildListTextArea("ID диалогов", task.relatedDialogueIds, MarkDirty));
            detail.Add(BuildListTextArea("ID флагов", task.relatedFlagIds, MarkDirty));
            detail.Add(BuildListTextArea("ID знаний", task.relatedKnowledgeIds, MarkDirty));

            detail.Add(BuildTextField("Заметка блокировки (blockerNote)", task.blockerNote, v => { task.blockerNote = v; MarkDirty(); }, multiline: true));
            detail.Add(BuildTextField("Заметка реализации (implementationNote)", task.implementationNote, v => { task.implementationNote = v; MarkDirty(); }, multiline: true));

            VisualElement completionRow = new VisualElement();
            completionRow.style.flexDirection = FlexDirection.Row;
            completionRow.style.marginTop = 4f;
            completionRow.Add(BuildTextField("Дата выполнения", task.completedAt, v => { task.completedAt = v; MarkDirty(); }, width: 160f));
            completionRow.Add(BuildTextField("Коммит", task.completedCommit, v => { task.completedCommit = v; MarkDirty(); }, width: 260f));
            detail.Add(completionRow);

            VisualElement autoCheckRow = new VisualElement();
            autoCheckRow.style.flexDirection = FlexDirection.Row;
            autoCheckRow.style.alignItems = Align.Center;
            autoCheckRow.style.marginTop = 4f;

            TextField autoCheckField = BuildTextField(
                "Автопроверка (file:/asset:)", task.optionalAutoCheckId,
                v => { task.optionalAutoCheckId = v; MarkDirty(); }, width: 360f);
            autoCheckRow.Add(autoCheckField);

            Label autoCheckResultLabel = new Label();
            autoCheckResultLabel.style.marginLeft = 8f;
            autoCheckResultLabel.style.whiteSpace = WhiteSpace.Normal;

            Button runAutoCheck = new Button(() =>
            {
                AutoCheckResult result = DevelopmentPlanAutoChecks.Run(task.optionalAutoCheckId);
                autoCheckResultLabel.text = result.HasResult
                    ? (result.Passed ? "✓ " : "✗ ") + result.Message
                    : "Автопроверка не задана.";
                autoCheckResultLabel.style.color = result.HasResult
                    ? (result.Passed ? new Color(0.4f, 0.8f, 0.4f, 1f) : new Color(0.85f, 0.4f, 0.4f, 1f))
                    : new Color(0.6f, 0.6f, 0.6f, 1f);
            })
            { text = "ПРОГНАТЬ АВТОПРОВЕРКУ" };
            runAutoCheck.style.marginLeft = 8f;
            autoCheckRow.Add(runAutoCheck);
            autoCheckRow.Add(autoCheckResultLabel);

            detail.Add(autoCheckRow);

            return detail;
        }

        private void MarkDirty()
        {
            if (plan != null)
                EditorUtility.SetDirty(plan);
        }

        private static Label BuildSectionLabel(string text)
        {
            Label label = new Label(text);
            label.style.marginTop = 8f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 11f;
            return label;
        }

        private static TextField BuildTextField(string label, string value, Action<string> onChanged, bool multiline = false, float width = 0f)
        {
            TextField field = new TextField(label) { value = value ?? string.Empty, multiline = multiline };
            if (multiline)
            {
                field.style.minHeight = 40f;
                field.style.whiteSpace = WhiteSpace.Normal;
            }
            if (width > 0f)
                field.style.width = width;
            field.style.marginTop = 4f;
            field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            return field;
        }

        private VisualElement BuildListTextArea(string label, List<string> backingList, Action onChanged)
        {
            TextField field = new TextField(label)
            {
                value = string.Join("\n", backingList ?? new List<string>()),
                multiline = true
            };
            field.style.minHeight = 36f;
            field.style.marginTop = 4f;
            field.RegisterValueChangedCallback(evt =>
            {
                backingList.Clear();
                foreach (string line in evt.newValue.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        backingList.Add(trimmed);
                }
                onChanged();
            });
            return field;
        }

        private VisualElement BuildAcceptanceCriteriaList(DevelopmentTaskData task)
        {
            VisualElement container = new VisualElement();

            void Rebuild()
            {
                container.Clear();

                for (int i = 0; i < task.acceptanceCriteria.Count; i++)
                {
                    AcceptanceCriterionData criterion = task.acceptanceCriteria[i];
                    VisualElement row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginTop = 2f;

                    Toggle done = new Toggle { value = criterion.done };
                    done.RegisterValueChangedCallback(evt => { criterion.done = evt.newValue; MarkDirty(); });
                    row.Add(done);

                    TextField text = new TextField { value = criterion.text };
                    text.style.flexGrow = 1f;
                    text.style.marginLeft = 4f;
                    text.RegisterValueChangedCallback(evt => { criterion.text = evt.newValue; MarkDirty(); });
                    row.Add(text);

                    int indexCaptured = i;
                    Button remove = new Button(() =>
                    {
                        task.acceptanceCriteria.RemoveAt(indexCaptured);
                        MarkDirty();
                        Rebuild();
                    })
                    { text = "✕" };
                    remove.style.width = 22f;
                    remove.style.marginLeft = 4f;
                    row.Add(remove);

                    container.Add(row);
                }

                Button add = new Button(() =>
                {
                    task.acceptanceCriteria.Add(new AcceptanceCriterionData());
                    MarkDirty();
                    Rebuild();
                })
                { text = "+ КРИТЕРИЙ" };
                add.style.width = 110f;
                add.style.marginTop = 4f;
                container.Add(add);
            }

            Rebuild();
            return container;
        }

        private void SavePlan()
        {
            if (plan == null)
                return;

            EditorUtility.SetDirty(plan);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------
        // Диагностика
        // ------------------------------------------------------------

        private void BuildDiagnosticsPanel()
        {
            diagnosticsContainer = new VisualElement();
            diagnosticsContainer.style.borderTopWidth = 1f;
            diagnosticsContainer.style.borderTopColor = new Color(0.22f, 0.22f, 0.22f, 1f);
            diagnosticsContainer.style.maxHeight = 160f;
            diagnosticsContainer.style.paddingLeft = 8f;
            diagnosticsContainer.style.paddingRight = 8f;
            diagnosticsContainer.style.paddingTop = 4f;
            diagnosticsContainer.style.paddingBottom = 4f;

            diagnosticsSummaryLabel = new Label();
            diagnosticsSummaryLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            diagnosticsContainer.Add(diagnosticsSummaryLabel);

            ScrollView issuesScroll = new ScrollView(ScrollViewMode.Vertical) { name = "issues-scroll" };
            issuesScroll.style.flexGrow = 1f;
            diagnosticsContainer.Add(issuesScroll);

            rootVisualElement.Add(diagnosticsContainer);
        }

        private void RefreshDiagnostics()
        {
            if (plan == null || diagnosticsContainer == null)
                return;

            List<ValidationIssue> issues = DevelopmentPlanValidator.Validate(plan);
            int errorCount = issues.Count(i => i.Severity == ValidationSeverity.Error);
            int warningCount = issues.Count(i => i.Severity == ValidationSeverity.Warning);

            List<DevelopmentTaskData> needsUnity = new List<DevelopmentTaskData>();
            foreach (DevelopmentPhaseData phase in plan.phases ?? new List<DevelopmentPhaseData>())
            {
                if (phase?.tasks == null) continue;
                foreach (DevelopmentTaskData task in phase.tasks)
                    if (task != null && task.status == DevelopmentTaskStatus.NeedsUnityCheck)
                        needsUnity.Add(task);
            }

            diagnosticsSummaryLabel.text = "Диагностика: ошибок " + errorCount + ", предупреждений " + warningCount +
                " · задач «Нужна проверка в Unity»: " + needsUnity.Count;

            ScrollView issuesScroll = diagnosticsContainer.Q<ScrollView>("issues-scroll");
            issuesScroll.Clear();

            foreach (ValidationIssue issue in issues)
            {
                Label line = new Label(
                    (issue.Severity == ValidationSeverity.Error ? "ОШИБКА" : "ПРЕДУПРЕЖДЕНИЕ") +
                    (string.IsNullOrEmpty(issue.EntityId) ? "" : " [" + issue.EntityId + "]") +
                    ": " + issue.Message);
                line.style.whiteSpace = WhiteSpace.Normal;
                line.style.fontSize = 10f;
                line.style.color = issue.Severity == ValidationSeverity.Error
                    ? new Color(0.9f, 0.45f, 0.45f, 1f)
                    : new Color(0.85f, 0.75f, 0.4f, 1f);
                issuesScroll.Add(line);
            }

            if (needsUnity.Count > 0)
            {
                Label header = new Label("Нужна проверка в Unity:");
                header.style.marginTop = 6f;
                header.style.unityFontStyleAndWeight = FontStyle.Bold;
                header.style.fontSize = 10f;
                issuesScroll.Add(header);

                foreach (DevelopmentTaskData task in needsUnity)
                {
                    Label line = new Label("• [" + task.id + "] " + task.title);
                    line.style.fontSize = 10f;
                    line.style.whiteSpace = WhiteSpace.Normal;
                    issuesScroll.Add(line);
                }
            }
        }
    }
}
