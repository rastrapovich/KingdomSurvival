using UnityEngine;

namespace KingdomSurvival.ProgressionDatabase
{
    // Перед загрузкой первой сцены переносит «Базу развития» в ядро: правила
    // и карты развития — в ProgressionRules.Current, перечни — в
    // ProgressionCatalog.Current. В редакторе вне Play Mode ядро работает на
    // значениях по умолчанию, чтобы EditMode-тесты не зависели от правок базы.
    public static class ProgressionDatabaseRuntime
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplyFromResources()
        {
            ProgressionDatabaseAsset database = Resources.Load<ProgressionDatabaseAsset>(ProgressionDatabaseAsset.ResourcesPath);
            if (database == null)
            {
                Debug.LogWarning("База развития не найдена (Resources/" + ProgressionDatabaseAsset.ResourcesPath +
                                 ") — действуют значения по умолчанию.");
                return;
            }
            Apply(database);
        }

        public static void Apply(ProgressionDatabaseAsset database)
        {
            if (database == null)
                return;
            ProgressionRules.Current = database.ToRules();
            ProgressionCatalog.Current = database.ToCatalog();
        }

        public static void ResetToDefaults()
        {
            ProgressionRules.Current = ProgressionRules.CreateDefault();
            ProgressionCatalog.Current = ProgressionCatalog.CreateDefault();
        }
    }
}
