using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Переходы в другие редакторские окна идут через ExecuteMenuItem, а не
    // через прямые ссылки на чужие Editor-сборки (раздел 20.1) — так
    // DevelopmentTracker.Editor не создаёт циклических зависимостей с
    // Dialogue Database, Unit Database или UI Конструктором.
    public static class DevelopmentTrackerMenuCommands
    {
        public static void OpenDialogueDatabase()
        {
            TryExecute("Kingdom Survival/База диалогов");
        }

        public static void OpenUnitDatabase()
        {
            TryExecute("Kingdom Survival/База существ");
        }

        public static void OpenBattlefieldDatabase()
        {
            TryExecute("Kingdom Survival/База полей боя");
        }

        public static void OpenUILayoutConstructor()
        {
            TryExecute("Kingdom Survival/UI Конструктор");
        }

        public static void ResetPlanToInstructionSeed()
        {
            if (!EditorUtility.DisplayDialog(
                    "Пересобрать план по инструкции",
                    "Это заменит содержимое плана начальными этапами P00–P16 из инструкции. " +
                    "Ручные правки статусов, галочек и заметок будут потеряны для задач, " +
                    "чьи ID совпадают с seed-данными. Продолжить?",
                    "Пересобрать", "Отмена"))
            {
                return;
            }

            DevelopmentPlanBootstrap.CreateAndSeedPlan();
        }

        private static void TryExecute(string menuPath)
        {
            if (!EditorApplication.ExecuteMenuItem(menuPath))
                Debug.LogWarning("Не удалось открыть окно: пункт меню \"" + menuPath + "\" недоступен.");
        }
    }
}
