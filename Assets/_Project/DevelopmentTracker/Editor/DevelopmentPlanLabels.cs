namespace KingdomSurvival.DevelopmentTracker.Editor
{
    public static class DevelopmentPlanLabels
    {
        public static readonly DevelopmentTaskStatus[] AllStatuses =
        {
            DevelopmentTaskStatus.NotStarted,
            DevelopmentTaskStatus.InProgress,
            DevelopmentTaskStatus.NeedsUnityCheck,
            DevelopmentTaskStatus.Completed,
            DevelopmentTaskStatus.Blocked,
            DevelopmentTaskStatus.Deferred
        };

        public static readonly DevelopmentTaskCategory[] AllCategories =
        {
            DevelopmentTaskCategory.Decision,
            DevelopmentTaskCategory.Narrative,
            DevelopmentTaskCategory.Content,
            DevelopmentTaskCategory.Code,
            DevelopmentTaskCategory.Data,
            DevelopmentTaskCategory.UI,
            DevelopmentTaskCategory.Art,
            DevelopmentTaskCategory.Integration,
            DevelopmentTaskCategory.Test,
            DevelopmentTaskCategory.Documentation
        };

        public static readonly DevelopmentTaskPriority[] AllPriorities =
        {
            DevelopmentTaskPriority.Now,
            DevelopmentTaskPriority.Next,
            DevelopmentTaskPriority.Later
        };

        public static readonly DevelopmentVerificationType[] AllVerificationTypes =
        {
            DevelopmentVerificationType.Auto,
            DevelopmentVerificationType.EditMode,
            DevelopmentVerificationType.PlayMode,
            DevelopmentVerificationType.Manual
        };

        public static readonly DevelopmentBlockerReason[] AllBlockerReasons =
        {
            DevelopmentBlockerReason.Decision,
            DevelopmentBlockerReason.Code,
            DevelopmentBlockerReason.Content,
            DevelopmentBlockerReason.Art,
            DevelopmentBlockerReason.Dependency,
            DevelopmentBlockerReason.UnityCheck
        };

        public static string StatusLabel(DevelopmentTaskStatus status)
        {
            switch (status)
            {
                case DevelopmentTaskStatus.NotStarted: return "Не начато";
                case DevelopmentTaskStatus.InProgress: return "В работе";
                case DevelopmentTaskStatus.NeedsUnityCheck: return "Нужна проверка в Unity";
                case DevelopmentTaskStatus.Completed: return "Выполнено";
                case DevelopmentTaskStatus.Blocked: return "Заблокировано";
                case DevelopmentTaskStatus.Deferred: return "Отложено";
                default: return status.ToString();
            }
        }

        public static string CategoryLabel(DevelopmentTaskCategory category)
        {
            switch (category)
            {
                case DevelopmentTaskCategory.Decision: return "Решение";
                case DevelopmentTaskCategory.Narrative: return "Нарратив";
                case DevelopmentTaskCategory.Content: return "Контент";
                case DevelopmentTaskCategory.Code: return "Код";
                case DevelopmentTaskCategory.Data: return "Данные";
                case DevelopmentTaskCategory.UI: return "UI";
                case DevelopmentTaskCategory.Art: return "Арт";
                case DevelopmentTaskCategory.Integration: return "Интеграция";
                case DevelopmentTaskCategory.Test: return "Тест";
                case DevelopmentTaskCategory.Documentation: return "Документация";
                default: return category.ToString();
            }
        }

        public static string PriorityLabel(DevelopmentTaskPriority priority)
        {
            switch (priority)
            {
                case DevelopmentTaskPriority.Now: return "Сейчас";
                case DevelopmentTaskPriority.Next: return "Далее";
                case DevelopmentTaskPriority.Later: return "Позже";
                default: return priority.ToString();
            }
        }

        public static string VerificationTypeLabel(DevelopmentVerificationType type)
        {
            switch (type)
            {
                case DevelopmentVerificationType.Auto: return "Авто";
                case DevelopmentVerificationType.EditMode: return "EditMode";
                case DevelopmentVerificationType.PlayMode: return "PlayMode";
                case DevelopmentVerificationType.Manual: return "Ручная";
                default: return type.ToString();
            }
        }

        public static string BlockerReasonLabel(DevelopmentBlockerReason reason)
        {
            switch (reason)
            {
                case DevelopmentBlockerReason.Decision: return "Решение";
                case DevelopmentBlockerReason.Code: return "Код";
                case DevelopmentBlockerReason.Content: return "Контент";
                case DevelopmentBlockerReason.Art: return "Арт";
                case DevelopmentBlockerReason.Dependency: return "Зависимость";
                case DevelopmentBlockerReason.UnityCheck: return "Unity-проверка";
                default: return reason.ToString();
            }
        }
    }
}
