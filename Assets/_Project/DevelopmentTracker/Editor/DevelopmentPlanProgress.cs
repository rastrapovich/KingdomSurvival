using System.Collections.Generic;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    public static class DevelopmentPlanProgress
    {
        public struct Summary
        {
            public int completedRequired;
            public int totalRequired;
            public int blockedCount;
            public int needsUnityCheckCount;
            public int optionalTotal;
            public int optionalCompleted;

            public float Ratio
            {
                get { return totalRequired <= 0 ? 1f : (float)completedRequired / totalRequired; }
            }
        }

        public static Summary ComputeForTasks(IEnumerable<DevelopmentTaskData> tasks)
        {
            Summary summary = default;
            if (tasks == null)
                return summary;

            foreach (DevelopmentTaskData task in tasks)
            {
                if (task == null)
                    continue;

                if (task.status == DevelopmentTaskStatus.Blocked)
                    summary.blockedCount++;
                if (task.status == DevelopmentTaskStatus.NeedsUnityCheck)
                    summary.needsUnityCheckCount++;

                // Отложенные задачи полностью исключены из обоих счётчиков —
                // они сознательно вынесены из текущей вехи (раздел 4.5).
                if (task.status == DevelopmentTaskStatus.Deferred)
                    continue;

                if (task.required)
                {
                    summary.totalRequired++;
                    if (task.status == DevelopmentTaskStatus.Completed)
                        summary.completedRequired++;
                }
                else
                {
                    summary.optionalTotal++;
                    if (task.status == DevelopmentTaskStatus.Completed)
                        summary.optionalCompleted++;
                }
            }

            return summary;
        }

        public static Summary ComputePhase(DevelopmentPhaseData phase)
        {
            if (phase == null)
                return default;
            return ComputeForTasks(phase.tasks);
        }

        public static Summary ComputeOverall(DevelopmentPlanAsset plan)
        {
            List<DevelopmentTaskData> all = new List<DevelopmentTaskData>();
            if (plan != null && plan.phases != null)
            {
                foreach (DevelopmentPhaseData phase in plan.phases)
                {
                    if (phase != null && phase.tasks != null)
                        all.AddRange(phase.tasks);
                }
            }

            return ComputeForTasks(all);
        }

        // Первая незавершённая обязательная задача текущей вехи, все обязательные
        // зависимости которой уже выполнены — используется для «Следующей
        // рекомендуемой задачи» в верхней панели окна.
        public static DevelopmentTaskData FindNextRecommendedTask(DevelopmentPlanAsset plan, string milestonePhaseId)
        {
            if (plan == null || plan.phases == null)
                return null;

            DevelopmentPhaseData milestone = plan.FindPhase(milestonePhaseId);
            List<DevelopmentPhaseData> searchOrder = new List<DevelopmentPhaseData>();
            if (milestone != null)
                searchOrder.Add(milestone);
            foreach (DevelopmentPhaseData phase in plan.phases)
            {
                if (phase != milestone)
                    searchOrder.Add(phase);
            }

            DevelopmentTaskData best = null;
            foreach (DevelopmentPhaseData phase in searchOrder)
            {
                if (phase == null || phase.tasks == null)
                    continue;

                List<DevelopmentTaskData> ordered = new List<DevelopmentTaskData>(phase.tasks);
                ordered.Sort((a, b) => a.order.CompareTo(b.order));

                foreach (DevelopmentTaskData task in ordered)
                {
                    if (task == null || !task.required)
                        continue;
                    if (task.status == DevelopmentTaskStatus.Completed || task.status == DevelopmentTaskStatus.Deferred)
                        continue;
                    if (task.status == DevelopmentTaskStatus.Blocked)
                        continue;
                    if (!AreDependenciesSatisfied(plan, task))
                        continue;

                    if (best == null || task.order < best.order)
                        best = task;
                }

                if (best != null)
                    return best;
            }

            return best;
        }

        private static bool AreDependenciesSatisfied(DevelopmentPlanAsset plan, DevelopmentTaskData task)
        {
            if (task.dependencies == null || task.dependencies.Count == 0)
                return true;

            foreach (string depId in task.dependencies)
            {
                if (string.IsNullOrEmpty(depId))
                    continue;

                DevelopmentTaskData depTask = plan.FindTask(depId, out _);
                if (depTask != null)
                {
                    if (depTask.status != DevelopmentTaskStatus.Completed)
                        return false;
                    continue;
                }

                DevelopmentPhaseData depPhase = plan.FindPhase(depId);
                if (depPhase != null && depPhase.ComputeStatus() != DevelopmentTaskStatus.Completed)
                    return false;
            }

            return true;
        }
    }
}
