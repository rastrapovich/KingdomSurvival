namespace KingdomSurvival.Chapter01
{
    // ПР-05: первая глава — сюжетный модуль кризиса «Дом на чужой воде», а не
    // безусловный сценарий любой кампании. Глава ведёт кампанию только если
    // та начата с этим кризисом. Кампании без конфигурации (старше ПР-05,
    // тестовые) — это кампании единственного кризиса.
    public static class Chapter01Crisis
    {
        public const string CrisisId = CampaignStartOptions.HomeOnForeignWaterCrisisId;

        public static bool IsActive(GameState gameState)
        {
            if (gameState == null)
                return false;

            CampaignConfiguration configuration = gameState.Configuration;
            return configuration == null ||
                   string.IsNullOrEmpty(configuration.CrisisId) ||
                   configuration.CrisisId == CrisisId;
        }
    }
}
