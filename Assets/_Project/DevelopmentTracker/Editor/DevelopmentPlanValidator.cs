using System.Collections.Generic;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    public enum ValidationSeverity
    {
        Error,
        Warning
    }

    public sealed class ValidationIssue
    {
        public readonly ValidationSeverity Severity;
        public readonly string EntityId;
        public readonly string Message;

        public ValidationIssue(ValidationSeverity severity, string entityId, string message)
        {
            Severity = severity;
            EntityId = entityId ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    // Проверяет только структуру плана (раздел 4.10). Не оценивает качество
    // текста, художественную выразительность или баланс — это остаётся
    // ручной работой автора.
    public static class DevelopmentPlanValidator
    {
        public static List<ValidationIssue> Validate(DevelopmentPlanAsset plan)
        {
            List<ValidationIssue> issues = new List<ValidationIssue>();

            if (plan == null)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, string.Empty, "План не задан."));
                return issues;
            }

            List<DevelopmentPhaseData> phases = plan.phases ?? new List<DevelopmentPhaseData>();

            Dictionary<string, DevelopmentPhaseData> phaseById = new Dictionary<string, DevelopmentPhaseData>();
            Dictionary<string, DevelopmentTaskData> taskById = new Dictionary<string, DevelopmentTaskData>();
            Dictionary<string, int> idCounts = new Dictionary<string, int>();
            Dictionary<string, List<string>> depGraph = new Dictionary<string, List<string>>();

            foreach (DevelopmentPhaseData phase in phases)
            {
                if (phase == null)
                    continue;

                RegisterId(idCounts, phase.id);
                if (!string.IsNullOrEmpty(phase.id) && !phaseById.ContainsKey(phase.id))
                    phaseById[phase.id] = phase;

                List<DevelopmentTaskData> tasks = phase.tasks ?? new List<DevelopmentTaskData>();
                foreach (DevelopmentTaskData task in tasks)
                {
                    if (task == null)
                        continue;

                    RegisterId(idCounts, task.id);
                    if (!string.IsNullOrEmpty(task.id) && !taskById.ContainsKey(task.id))
                        taskById[task.id] = task;
                }
            }

            foreach (DevelopmentPhaseData phase in phases)
            {
                if (phase == null || string.IsNullOrEmpty(phase.id))
                    continue;
                depGraph[phase.id] = phase.dependencies ?? new List<string>();

                List<DevelopmentTaskData> tasks = phase.tasks ?? new List<DevelopmentTaskData>();
                foreach (DevelopmentTaskData task in tasks)
                {
                    if (task == null || string.IsNullOrEmpty(task.id))
                        continue;
                    depGraph[task.id] = task.dependencies ?? new List<string>();
                }
            }

            // 1. Уникальность ID.
            foreach (KeyValuePair<string, int> kv in idCounts)
            {
                if (kv.Value > 1)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        kv.Key,
                        "Дублирующийся ID: \"" + kv.Key + "\" встречается " + kv.Value + " раз(а)."));
                }
            }

            bool NodeExists(string id)
            {
                return phaseById.ContainsKey(id) || taskById.ContainsKey(id);
            }

            // 2. Существование зависимостей, 3. отсутствие self-dependency.
            foreach (KeyValuePair<string, List<string>> kv in depGraph)
            {
                string ownerId = kv.Key;
                foreach (string dep in kv.Value)
                {
                    if (string.IsNullOrEmpty(dep))
                        continue;
                    if (dep == ownerId)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error,
                            ownerId,
                            "Задача/этап зависит от самого себя."));
                    }
                    else if (!NodeExists(dep))
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error,
                            ownerId,
                            "Зависимость \"" + dep + "\" не найдена в плане."));
                    }
                }
            }

            // 4. Циклические зависимости (прямые и косвенные).
            HashSet<string> visited = new HashSet<string>();
            HashSet<string> inStack = new HashSet<string>();
            HashSet<string> reportedCycleNodes = new HashSet<string>();

            void Visit(string node)
            {
                if (inStack.Contains(node))
                {
                    if (reportedCycleNodes.Add(node))
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error,
                            node,
                            "Циклическая зависимость обнаружена на \"" + node + "\"."));
                    }
                    return;
                }

                if (visited.Contains(node))
                    return;

                visited.Add(node);
                inStack.Add(node);

                if (depGraph.TryGetValue(node, out List<string> deps))
                {
                    foreach (string dep in deps)
                    {
                        if (!string.IsNullOrEmpty(dep) && dep != node && NodeExists(dep))
                            Visit(dep);
                    }
                }

                inStack.Remove(node);
            }

            foreach (string node in depGraph.Keys)
                Visit(node);

            // 5. У обязательной задачи должен быть хотя бы один критерий приёмки.
            foreach (DevelopmentPhaseData phase in phases)
            {
                if (phase?.tasks == null)
                    continue;

                foreach (DevelopmentTaskData task in phase.tasks)
                {
                    if (task == null)
                        continue;
                    if (task.required && (task.acceptanceCriteria == null || task.acceptanceCriteria.Count == 0))
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error,
                            task.id,
                            "У обязательной задачи нет ни одного критерия приёмки."));
                    }
                }
            }

            // 6. Корректность порядка этапов и задач (уникальность order).
            Dictionary<int, int> phaseOrderCounts = new Dictionary<int, int>();
            foreach (DevelopmentPhaseData phase in phases)
            {
                if (phase == null)
                    continue;
                if (phase.order < 0)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning, phase.id, "Отрицательный порядок этапа."));
                }
                phaseOrderCounts.TryGetValue(phase.order, out int count);
                phaseOrderCounts[phase.order] = count + 1;
            }
            foreach (KeyValuePair<int, int> kv in phaseOrderCounts)
            {
                if (kv.Value > 1)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        string.Empty,
                        "Несколько этапов имеют одинаковый порядок: " + kv.Key + "."));
                }
            }

            foreach (DevelopmentPhaseData phase in phases)
            {
                if (phase?.tasks == null)
                    continue;

                Dictionary<int, int> taskOrderCounts = new Dictionary<int, int>();
                foreach (DevelopmentTaskData task in phase.tasks)
                {
                    if (task == null)
                        continue;
                    if (task.order < 0)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Warning, task.id, "Отрицательный порядок задачи."));
                    }
                    taskOrderCounts.TryGetValue(task.order, out int count);
                    taskOrderCounts[task.order] = count + 1;
                }
                foreach (KeyValuePair<int, int> kv in taskOrderCounts)
                {
                    if (kv.Value > 1)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Warning,
                            phase.id,
                            "Несколько задач этапа \"" + phase.id + "\" имеют одинаковый порядок: " + kv.Key + "."));
                    }
                }
            }

            // 7. Нельзя быть «Выполнено» при незавершённой обязательной зависимости.
            foreach (DevelopmentPhaseData phase in phases)
            {
                if (phase?.tasks == null)
                    continue;

                foreach (DevelopmentTaskData task in phase.tasks)
                {
                    if (task == null || task.status != DevelopmentTaskStatus.Completed || task.dependencies == null)
                        continue;

                    foreach (string depId in task.dependencies)
                    {
                        if (taskById.TryGetValue(depId, out DevelopmentTaskData depTask)
                            && depTask.required
                            && depTask.status != DevelopmentTaskStatus.Completed)
                        {
                            issues.Add(new ValidationIssue(
                                ValidationSeverity.Error,
                                task.id,
                                "Задача отмечена выполненной, но обязательная зависимость \"" + depId + "\" не завершена."));
                        }
                    }
                }
            }

            // 8. blockerNote обязателен при статусе «Заблокировано».
            foreach (DevelopmentPhaseData phase in phases)
            {
                if (phase?.tasks == null)
                    continue;

                foreach (DevelopmentTaskData task in phase.tasks)
                {
                    if (task != null
                        && task.status == DevelopmentTaskStatus.Blocked
                        && string.IsNullOrWhiteSpace(task.blockerNote))
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error,
                            task.id,
                            "Статус «Заблокировано» требует заполненного blockerNote."));
                    }
                }
            }

            // 9. Задачи, меняющие игровой runtime (категория «Код»), должны
            // иметь хотя бы одну ручную проверку — это предупреждение, а не
            // жёсткий запрет, чтобы не блокировать раннюю стадию работы.
            foreach (DevelopmentPhaseData phase in phases)
            {
                if (phase?.tasks == null)
                    continue;

                foreach (DevelopmentTaskData task in phase.tasks)
                {
                    if (task != null
                        && task.category == DevelopmentTaskCategory.Code
                        && (task.manualChecks == null || task.manualChecks.Count == 0))
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Warning,
                            task.id,
                            "Задача меняет игровой код, но не указана ни одна ручная проверка."));
                    }
                }
            }

            // 10. Совместимость schemaVersion.
            if (plan.schemaVersion > DevelopmentPlanAsset.CurrentSchemaVersion)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    string.Empty,
                    "schemaVersion плана (" + plan.schemaVersion + ") новее поддерживаемой (" +
                    DevelopmentPlanAsset.CurrentSchemaVersion + ")."));
            }
            else if (plan.schemaVersion < DevelopmentPlanAsset.CurrentSchemaVersion)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    string.Empty,
                    "План сохранён более старой схемой и требует миграции (DevelopmentPlanAsset.MigrateIfNeeded)."));
            }

            // 11. Выполненные задачи с незакрытыми критериями приёмки.
            foreach (DevelopmentPhaseData phase in phases)
            {
                if (phase?.tasks == null)
                    continue;

                foreach (DevelopmentTaskData task in phase.tasks)
                {
                    if (task == null || task.status != DevelopmentTaskStatus.Completed || task.acceptanceCriteria == null)
                        continue;

                    foreach (AcceptanceCriterionData criterion in task.acceptanceCriteria)
                    {
                        if (criterion != null && !criterion.done)
                        {
                            issues.Add(new ValidationIssue(
                                ValidationSeverity.Warning,
                                task.id,
                                "Задача выполнена, но остались незакрытые критерии приёмки."));
                            break;
                        }
                    }
                }
            }

            return issues;
        }

        private static void RegisterId(Dictionary<string, int> counts, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;
            counts.TryGetValue(id, out int count);
            counts[id] = count + 1;
        }
    }
}
