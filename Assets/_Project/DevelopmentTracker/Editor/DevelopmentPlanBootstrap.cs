using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    // Создаёт и заполняет план автоматически при первом открытии проекта в
    // Unity после pull. Существующий план не пересобирается разрушительно:
    // MigrateIfNeeded добавляет новые определения схемы с сохранением прогресса.
    [InitializeOnLoad]
    public static class DevelopmentPlanBootstrap
    {
        public const string AssetFolder = "Assets/_Project/DevelopmentTracker/Editor/Data";
        public const string AssetPath = AssetFolder + "/KingdomSurvivalDevelopmentPlan.asset";

        static DevelopmentPlanBootstrap()
        {
            EditorApplication.delayCall += EnsurePlanExists;
        }

        private static void EnsurePlanExists()
        {
            DevelopmentPlanAsset plan = AssetDatabase.LoadAssetAtPath<DevelopmentPlanAsset>(AssetPath);
            if (plan == null)
            {
                CreateAndSeedPlan();
                return;
            }

            bool migrated = plan.MigrateIfNeeded();
            if (!migrated && !EditorUtility.IsDirty(plan))
                return;

            AssetDatabase.SaveAssets();
            if (migrated)
            {
                Debug.Log("Kingdom Survival: план разработки безопасно обновлён до схемы v" +
                          DevelopmentPlanAsset.CurrentSchemaVersion + " (прогресс сохранён).");
            }
        }

        public static DevelopmentPlanAsset CreateAndSeedPlan()
        {
            EnsureFolderExists();

            DevelopmentPlanAsset plan = AssetDatabase.LoadAssetAtPath<DevelopmentPlanAsset>(AssetPath);
            if (plan == null)
            {
                plan = ScriptableObject.CreateInstance<DevelopmentPlanAsset>();
                DevelopmentPlanSeedData.Populate(plan);
                AssetDatabase.CreateAsset(plan, AssetPath);
            }
            else
            {
                DevelopmentPlanSeedData.Populate(plan);
                EditorUtility.SetDirty(plan);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Kingdom Survival: план разработки заполнен по инструкции (" + AssetPath + ").");
            return plan;
        }

        private static void EnsureFolderExists()
        {
            if (AssetDatabase.IsValidFolder(AssetFolder))
                return;

            string parent = "Assets/_Project/DevelopmentTracker/Editor";
            if (!AssetDatabase.IsValidFolder(parent))
            {
                // Оба родителя уже существуют в проекте — это защита на случай
                // ручного удаления папки, а не ожидаемый путь выполнения.
                AssetDatabase.CreateFolder("Assets/_Project/DevelopmentTracker", "Editor");
            }

            AssetDatabase.CreateFolder(parent, "Data");
        }
    }
}
