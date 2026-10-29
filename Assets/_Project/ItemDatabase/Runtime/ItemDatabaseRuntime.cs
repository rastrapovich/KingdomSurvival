using UnityEngine;

namespace KingdomSurvival.ItemDatabase
{
    // Перед загрузкой первой сцены переносит «Базу предметов» в ядро
    // (ItemCatalog.Current). В редакторе вне Play Mode ядро работает на
    // исходных определениях — EditMode-тесты не зависят от правок базы.
    // Нет базы — громкая ошибка в консоли, а игра остаётся на исходных
    // определениях: вещи сохранения не теряются и не подменяются.
    public static class ItemDatabaseRuntime
    {
        private static ItemDatabaseAsset loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplyFromResources()
        {
            ItemDatabaseAsset database = Load();
            if (database == null)
            {
                Debug.LogError("База предметов не найдена (Resources/" + ItemDatabaseAsset.ResourcesPath +
                               "). Действуют исходные определения вещей; откройте Kingdom Survival → База предметов.");
                ItemCatalog.ResetToDefaults();
                return;
            }
            Apply(database);
        }

        public static void Apply(ItemDatabaseAsset database)
        {
            if (database == null)
                return;
            loaded = database;
            ItemCatalog.Current = database.ToCatalog();
        }

        public static ItemDatabaseAsset Load()
        {
            if (loaded == null)
                loaded = Resources.Load<ItemDatabaseAsset>(ItemDatabaseAsset.ResourcesPath);
            return loaded;
        }

        // Изображение вещи для экранов; null — нейтральная заглушка в UI.
        public static Sprite Icon(string itemId)
        {
            ItemDatabaseAsset database = Load();
            return database != null ? database.IconOf(itemId) : null;
        }
    }
}
