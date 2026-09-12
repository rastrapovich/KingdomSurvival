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

        public static WorldMapVisualTheme LoadActiveTheme()
        {
            if (!attemptedLoad)
            {
                cachedDatabase = Resources.Load<WorldMapDatabaseAsset>(
                    WorldMapDatabaseAsset.ResourcesPath);
                attemptedLoad = true;
            }

            return cachedDatabase != null ? cachedDatabase.ActiveTheme : null;
        }

        // Для EditMode-тестов/Editor-превью, где кэш между сценариями мешает.
        public static void ClearCache()
        {
            cachedDatabase = null;
            attemptedLoad = false;
        }
    }
}
