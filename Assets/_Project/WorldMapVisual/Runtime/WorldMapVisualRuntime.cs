using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Кэширующий загрузчик активной темы карты, по образцу
    // DialogueDatabaseRuntime. Пока ассет KingdomSurvivalWorldMapDatabase не
    // создан в Resources, LoadActiveTheme возвращает null — вызывающий код
    // обязан аккуратно работать без темы (см. PrototypeUIController.WorldMap.cs).
    public static class WorldMapVisualRuntime
    {
        private static WorldMapDatabaseAsset cachedDatabase;
        private static bool attemptedLoad;

        public static WorldMapDatabaseAsset LoadDatabase()
        {
            if (!attemptedLoad)
            {
                cachedDatabase = Resources.Load<WorldMapDatabaseAsset>(
                    WorldMapDatabaseAsset.ResourcesPath);
                attemptedLoad = true;
            }

            return cachedDatabase;
        }

        public static WorldMapVisualTheme LoadActiveTheme()
        {
            WorldMapDatabaseAsset database = LoadDatabase();
            return database != null ? database.ActiveTheme : null;
        }

        public static WorldMapLocationDefinition FindLocation(string locationId)
        {
            WorldMapDatabaseAsset database = LoadDatabase();
            return database != null ? database.FindLocation(locationId) : null;
        }

        // 12И: настройки прямого управления героем (темп, местность, герой, камера).
        public static WorldMapMovementSettingsAsset LoadMovementSettings()
        {
            if (!attemptedMovementLoad)
            {
                cachedMovementSettings = Resources.Load<WorldMapMovementSettingsAsset>(
                    WorldMapMovementSettingsAsset.ResourcesPath);
                attemptedMovementLoad = true;
            }

            return cachedMovementSettings;
        }

        // Ядро получает правила из ассета; без ассета остаются значения ядра.
        public static void ApplyMovementRules()
        {
            WorldMapMovementSettingsAsset settings = LoadMovementSettings();
            if (settings != null)
                settings.ApplyToCore();
        }

        // Для EditMode-тестов/Editor-превью, где кэш между сценариями мешает.
        public static void ClearCache()
        {
            cachedDatabase = null;
            attemptedLoad = false;
            cachedMovementSettings = null;
            attemptedMovementLoad = false;
        }

        private static WorldMapMovementSettingsAsset cachedMovementSettings;
        private static bool attemptedMovementLoad;
    }
}
