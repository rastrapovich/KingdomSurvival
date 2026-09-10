using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    public enum DevelopmentTaskStatus
    {
        NotStarted,
        InProgress,
        NeedsUnityCheck,
        Completed,
        Blocked,
        Deferred
    }

    public enum DevelopmentTaskCategory
    {
        Decision,
        Narrative,
        Content,
        Code,
        Data,
        UI,
        Art,
        Integration,
        Test,
        Documentation
    }

    public enum DevelopmentTaskPriority
    {
        Now,
        Next,
        Later
    }

    public enum DevelopmentVerificationType
    {
        Auto,
        EditMode,
        PlayMode,
        Manual
    }

    public enum DevelopmentBlockerReason
    {
        Decision,
        Code,
        Content,
        Art,
        Dependency,
        UnityCheck
    }

    [System.Serializable]
    public sealed class AcceptanceCriterionData
    {
        public string text = string.Empty;
        public bool done;
    }

    [System.Serializable]
    public sealed class DevelopmentTaskData
    {
        public string id = string.Empty;
        public string title = string.Empty;
        [TextArea] public string details = string.Empty;
        public DevelopmentTaskCategory category = DevelopmentTaskCategory.Code;
        public DevelopmentTaskStatus status = DevelopmentTaskStatus.NotStarted;
        public bool required = true;
        public int order;
        public DevelopmentTaskPriority priority = DevelopmentTaskPriority.Next;
        public DevelopmentVerificationType verificationType = DevelopmentVerificationType.Manual;
        public DevelopmentBlockerReason blockerReason = DevelopmentBlockerReason.Dependency;

        public List<string> dependencies = new List<string>();
        public List<AcceptanceCriterionData> acceptanceCriteria = new List<AcceptanceCriterionData>();
        public List<string> manualChecks = new List<string>();
        public List<string> fileReferences = new List<string>();
        public List<string> relatedDialogueIds = new List<string>();
        public List<string> relatedFlagIds = new List<string>();
        public List<string> relatedKnowledgeIds = new List<string>();

        [TextArea] public string blockerNote = string.Empty;
        [TextArea] public string implementationNote = string.Empty;
        public string completedAt = string.Empty;
        public string completedCommit = string.Empty;

        // Формат: "file:<путь>" или "asset:<путь>" — см. DevelopmentPlanAutoChecks.
        public string optionalAutoCheckId = string.Empty;

        public bool IsCompleted
        {
            get { return status == DevelopmentTaskStatus.Completed; }
        }
    }

    [System.Serializable]
    public sealed class DevelopmentPhaseData
    {
        public string id = string.Empty;
        public string title = string.Empty;
        [TextArea] public string purpose = string.Empty;
        public int order;
        public bool required = true;
        public List<string> dependencies = new List<string>();
        [TextArea] public string acceptanceSummary = string.Empty;
        public List<DevelopmentTaskData> tasks = new List<DevelopmentTaskData>();

        // Статус этапа не хранится как отдельное поле: он всегда выводится
        // из статусов задач, чтобы галочка на этапе не могла спрятать
        // незавершённые дочерние задачи (требование раздела 4.4).
        public DevelopmentTaskStatus ComputeStatus()
        {
            if (tasks == null || tasks.Count == 0)
                return DevelopmentTaskStatus.NotStarted;

            List<DevelopmentTaskData> relevant = new List<DevelopmentTaskData>();
            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i] != null && tasks[i].status != DevelopmentTaskStatus.Deferred)
                    relevant.Add(tasks[i]);
            }

            if (relevant.Count == 0)
                return DevelopmentTaskStatus.Deferred;

            List<DevelopmentTaskData> required = new List<DevelopmentTaskData>();
            for (int i = 0; i < relevant.Count; i++)
            {
                if (relevant[i].required)
                    required.Add(relevant[i]);
            }

            List<DevelopmentTaskData> scope = required.Count > 0 ? required : relevant;

            bool allCompleted = true;
            bool anyBlocked = false;
            bool anyNeedsUnityCheck = false;
            bool anyInProgressOrCompleted = false;

            for (int i = 0; i < scope.Count; i++)
            {
                DevelopmentTaskStatus s = scope[i].status;
                if (s != DevelopmentTaskStatus.Completed)
                    allCompleted = false;
                if (s == DevelopmentTaskStatus.Blocked)
                    anyBlocked = true;
                if (s == DevelopmentTaskStatus.NeedsUnityCheck)
                    anyNeedsUnityCheck = true;
                if (s == DevelopmentTaskStatus.InProgress || s == DevelopmentTaskStatus.Completed)
                    anyInProgressOrCompleted = true;
            }

            if (allCompleted)
                return DevelopmentTaskStatus.Completed;
            if (anyBlocked)
                return DevelopmentTaskStatus.Blocked;
            if (anyNeedsUnityCheck)
                return DevelopmentTaskStatus.NeedsUnityCheck;
            if (anyInProgressOrCompleted)
                return DevelopmentTaskStatus.InProgress;
            return DevelopmentTaskStatus.NotStarted;
        }
    }

    public sealed class DevelopmentPlanAsset : ScriptableObject
    {
        public const int CurrentSchemaVersion = 2;

        private static readonly HashSet<string> Version2SeedOwnedPhaseIds = new HashSet<string>
        {
            "P08J_JOURNAL",
            "P08M_MAP_TIME",
            "P09_ROAD"
        };

        public int schemaVersion = CurrentSchemaVersion;
        public string projectTitle = "Kingdom Survival";
        public string currentMilestoneId = string.Empty;
        public string planUpdatedAt = string.Empty;
        public List<DevelopmentPhaseData> phases = new List<DevelopmentPhaseData>();
        [TextArea] public string projectNotes = string.Empty;

        public DevelopmentPhaseData FindPhase(string phaseId)
        {
            if (string.IsNullOrEmpty(phaseId) || phases == null)
                return null;

            for (int i = 0; i < phases.Count; i++)
            {
                if (phases[i] != null && phases[i].id == phaseId)
                    return phases[i];
            }
            return null;
        }

        public DevelopmentTaskData FindTask(string taskId, out DevelopmentPhaseData owner)
        {
            owner = null;
            if (string.IsNullOrEmpty(taskId) || phases == null)
                return null;

            for (int i = 0; i < phases.Count; i++)
            {
                DevelopmentPhaseData phase = phases[i];
                if (phase == null || phase.tasks == null)
                    continue;

                for (int j = 0; j < phase.tasks.Count; j++)
                {
                    if (phase.tasks[j] != null && phase.tasks[j].id == taskId)
                    {
                        owner = phase;
                        return phase.tasks[j];
                    }
                }
            }
            return null;
        }

        // Возвращает true, если объект реально был изменён и его нужно сохранить.
        // v2 синхронизирует новые P08J/P08M/P09 определения из seed без
        // разрушительной кнопки «Пересобрать по инструкции»: уже отмеченный
        // прогресс сохраняется только для задач, у которых совпадают и ID,
        // и название. Это защищает от переноса старого статуса на новую задачу,
        // если стабильный ID был переиспользован после переработки P09.
        public bool MigrateIfNeeded()
        {
            bool changed = EnsureCollections();

            if (schemaVersion < 2)
            {
                changed |= MigrateToVersion2();
                schemaVersion = 2;
                changed = true;
            }

            if (schemaVersion < CurrentSchemaVersion)
            {
                schemaVersion = CurrentSchemaVersion;
                changed = true;
            }

            if (changed)
                EditorUtility.SetDirty(this);

            return changed;
        }

        private bool EnsureCollections()
        {
            bool changed = false;
            if (phases == null)
            {
                phases = new List<DevelopmentPhaseData>();
                changed = true;
            }

            for (int i = 0; i < phases.Count; i++)
            {
                DevelopmentPhaseData phase = phases[i];
                if (phase == null)
                    continue;
                if (phase.dependencies == null)
                {
                    phase.dependencies = new List<string>();
                    changed = true;
                }
                if (phase.tasks == null)
                {
                    phase.tasks = new List<DevelopmentTaskData>();
                    changed = true;
                }

                for (int j = 0; j < phase.tasks.Count; j++)
                {
                    DevelopmentTaskData task = phase.tasks[j];
                    if (task == null)
                        continue;
                    if (task.dependencies == null) { task.dependencies = new List<string>(); changed = true; }
                    if (task.acceptanceCriteria == null) { task.acceptanceCriteria = new List<AcceptanceCriterionData>(); changed = true; }
                    if (task.manualChecks == null) { task.manualChecks = new List<string>(); changed = true; }
                    if (task.fileReferences == null) { task.fileReferences = new List<string>(); changed = true; }
                    if (task.relatedDialogueIds == null) { task.relatedDialogueIds = new List<string>(); changed = true; }
                    if (task.relatedFlagIds == null) { task.relatedFlagIds = new List<string>(); changed = true; }
                    if (task.relatedKnowledgeIds == null) { task.relatedKnowledgeIds = new List<string>(); changed = true; }
                }
            }

            return changed;
        }

        private bool MigrateToVersion2()
        {
            DevelopmentPlanAsset seed = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
            try
            {
                DevelopmentPlanSeedData.Populate(seed);
                bool changed = false;

                for (int i = 0; i < seed.phases.Count; i++)
                {
                    DevelopmentPhaseData seedPhase = seed.phases[i];
                    if (seedPhase == null || string.IsNullOrEmpty(seedPhase.id))
                        continue;

                    DevelopmentPhaseData existing = FindPhase(seedPhase.id);
                    if (existing == null)
                    {
                        phases.Add(ClonePhase(seedPhase));
                        changed = true;
                        continue;
                    }

                    if (Version2SeedOwnedPhaseIds.Contains(seedPhase.id))
                    {
                        DevelopmentPhaseData replacement = ClonePhase(seedPhase);
                        PreserveMatchingTaskProgress(existing, replacement);
                        int existingIndex = phases.IndexOf(existing);
                        phases[existingIndex] = replacement;
                        changed = true;
                        continue;
                    }

                    if (existing.order != seedPhase.order)
                    {
                        existing.order = seedPhase.order;
                        changed = true;
                    }

                    if (!StringListsEqual(existing.dependencies, seedPhase.dependencies))
                    {
                        existing.dependencies = CloneStrings(seedPhase.dependencies);
                        changed = true;
                    }
                }

                if (planUpdatedAt != seed.planUpdatedAt)
                {
                    planUpdatedAt = seed.planUpdatedAt;
                    changed = true;
                }

                return changed;
            }
            finally
            {
                Object.DestroyImmediate(seed);
            }
        }

        private static void PreserveMatchingTaskProgress(DevelopmentPhaseData previous, DevelopmentPhaseData replacement)
        {
            if (previous?.tasks == null || replacement?.tasks == null)
                return;

            for (int i = 0; i < replacement.tasks.Count; i++)
            {
                DevelopmentTaskData currentTask = replacement.tasks[i];
                if (currentTask == null)
                    continue;

                DevelopmentTaskData previousTask = FindTaskByIdAndTitle(previous.tasks, currentTask.id, currentTask.title);
                if (previousTask == null)
                    continue;

                currentTask.status = previousTask.status;
                currentTask.priority = previousTask.priority;
                currentTask.blockerNote = previousTask.blockerNote ?? string.Empty;
                currentTask.completedAt = previousTask.completedAt ?? string.Empty;
                currentTask.completedCommit = previousTask.completedCommit ?? string.Empty;
                PreserveMatchingAcceptanceProgress(previousTask, currentTask);
            }
        }

        private static DevelopmentTaskData FindTaskByIdAndTitle(List<DevelopmentTaskData> tasks, string id, string title)
        {
            if (tasks == null)
                return null;

            for (int i = 0; i < tasks.Count; i++)
            {
                DevelopmentTaskData task = tasks[i];
                if (task != null && task.id == id && task.title == title)
                    return task;
            }

            return null;
        }

        private static void PreserveMatchingAcceptanceProgress(DevelopmentTaskData previous, DevelopmentTaskData current)
        {
            if (previous?.acceptanceCriteria == null || current?.acceptanceCriteria == null)
                return;

            for (int i = 0; i < current.acceptanceCriteria.Count; i++)
            {
                AcceptanceCriterionData currentCriterion = current.acceptanceCriteria[i];
                if (currentCriterion == null)
                    continue;

                for (int j = 0; j < previous.acceptanceCriteria.Count; j++)
                {
                    AcceptanceCriterionData previousCriterion = previous.acceptanceCriteria[j];
                    if (previousCriterion != null && previousCriterion.text == currentCriterion.text)
                    {
                        currentCriterion.done = previousCriterion.done;
                        break;
                    }
                }
            }
        }

        private static DevelopmentPhaseData ClonePhase(DevelopmentPhaseData source)
        {
            DevelopmentPhaseData clone = new DevelopmentPhaseData
            {
                id = source.id ?? string.Empty,
                title = source.title ?? string.Empty,
                purpose = source.purpose ?? string.Empty,
                order = source.order,
                required = source.required,
                dependencies = CloneStrings(source.dependencies),
                acceptanceSummary = source.acceptanceSummary ?? string.Empty,
                tasks = new List<DevelopmentTaskData>()
            };

            if (source.tasks != null)
            {
                for (int i = 0; i < source.tasks.Count; i++)
                {
                    if (source.tasks[i] != null)
                        clone.tasks.Add(CloneTask(source.tasks[i]));
                }
            }

            return clone;
        }

        private static DevelopmentTaskData CloneTask(DevelopmentTaskData source)
        {
            return new DevelopmentTaskData
            {
                id = source.id ?? string.Empty,
                title = source.title ?? string.Empty,
                details = source.details ?? string.Empty,
                category = source.category,
                status = source.status,
                required = source.required,
                order = source.order,
                priority = source.priority,
                verificationType = source.verificationType,
                blockerReason = source.blockerReason,
                dependencies = CloneStrings(source.dependencies),
                acceptanceCriteria = CloneAcceptanceCriteria(source.acceptanceCriteria),
                manualChecks = CloneStrings(source.manualChecks),
                fileReferences = CloneStrings(source.fileReferences),
                relatedDialogueIds = CloneStrings(source.relatedDialogueIds),
                relatedFlagIds = CloneStrings(source.relatedFlagIds),
                relatedKnowledgeIds = CloneStrings(source.relatedKnowledgeIds),
                blockerNote = source.blockerNote ?? string.Empty,
                implementationNote = source.implementationNote ?? string.Empty,
                completedAt = source.completedAt ?? string.Empty,
                completedCommit = source.completedCommit ?? string.Empty,
                optionalAutoCheckId = source.optionalAutoCheckId ?? string.Empty
            };
        }

        private static List<AcceptanceCriterionData> CloneAcceptanceCriteria(List<AcceptanceCriterionData> source)
        {
            List<AcceptanceCriterionData> clone = new List<AcceptanceCriterionData>();
            if (source == null)
                return clone;

            for (int i = 0; i < source.Count; i++)
            {
                AcceptanceCriterionData criterion = source[i];
                if (criterion == null)
                    continue;
                clone.Add(new AcceptanceCriterionData
                {
                    text = criterion.text ?? string.Empty,
                    done = criterion.done
                });
            }

            return clone;
        }

        private static List<string> CloneStrings(List<string> source)
        {
            return source == null ? new List<string>() : new List<string>(source);
        }

        private static bool StringListsEqual(List<string> a, List<string> b)
        {
            int aCount = a?.Count ?? 0;
            int bCount = b?.Count ?? 0;
            if (aCount != bCount)
                return false;

            for (int i = 0; i < aCount; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }
    }
}
