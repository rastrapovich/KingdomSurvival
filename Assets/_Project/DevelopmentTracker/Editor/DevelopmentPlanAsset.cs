using System.Collections.Generic;
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
        public const int CurrentSchemaVersion = 1;

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

        // Первая версия схемы: миграция сегодня — это только защита от
        // null-полей в asset, сохранённом более ранней/повреждённой версией,
        // и подъём schemaVersion до текущей. Реальные преобразования полей
        // появятся здесь, когда появится вторая версия схемы.
        public void MigrateIfNeeded()
        {
            if (phases == null)
                phases = new List<DevelopmentPhaseData>();

            for (int i = 0; i < phases.Count; i++)
            {
                DevelopmentPhaseData phase = phases[i];
                if (phase == null)
                    continue;
                if (phase.dependencies == null)
                    phase.dependencies = new List<string>();
                if (phase.tasks == null)
                    phase.tasks = new List<DevelopmentTaskData>();

                for (int j = 0; j < phase.tasks.Count; j++)
                {
                    DevelopmentTaskData task = phase.tasks[j];
                    if (task == null)
                        continue;
                    if (task.dependencies == null) task.dependencies = new List<string>();
                    if (task.acceptanceCriteria == null) task.acceptanceCriteria = new List<AcceptanceCriterionData>();
                    if (task.manualChecks == null) task.manualChecks = new List<string>();
                    if (task.fileReferences == null) task.fileReferences = new List<string>();
                    if (task.relatedDialogueIds == null) task.relatedDialogueIds = new List<string>();
                    if (task.relatedFlagIds == null) task.relatedFlagIds = new List<string>();
                    if (task.relatedKnowledgeIds == null) task.relatedKnowledgeIds = new List<string>();
                }
            }

            if (schemaVersion < CurrentSchemaVersion)
                schemaVersion = CurrentSchemaVersion;
        }
    }
}
