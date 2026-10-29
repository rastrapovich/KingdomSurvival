using UnityEngine;

namespace KingdomSurvival.SettlementDatabase
{
    // Перед загрузкой первой сцены переносит «Базу поселений» в ядро
    // (SettlementCatalog.Current). Нет базы — поселения открываются
    // нейтральным экраном без услуг (лагерь и вход работают как прежде).
    public static class SettlementDatabaseRuntime
    {
        private static SettlementDatabaseAsset loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplyFromResources()
        {
            SettlementDatabaseAsset database = Load();
            if (database == null)
            {
                Debug.LogWarning("База поселений не найдена (Resources/" + SettlementDatabaseAsset.ResourcesPath +
                                 ") — поселения без услуг. Откройте Kingdom Survival → База поселений.");
                SettlementCatalog.Reset();
                return;
            }
            Apply(database);
        }

        public static void Apply(SettlementDatabaseAsset database)
        {
            if (database == null)
                return;
            loaded = database;
            SettlementCatalog.Current = database.ToCatalog();
        }

        public static SettlementDatabaseAsset Load()
        {
            if (loaded == null)
                loaded = Resources.Load<SettlementDatabaseAsset>(SettlementDatabaseAsset.ResourcesPath);
            return loaded;
        }
    }
}
