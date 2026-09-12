namespace KingdomSurvival.Encounters
{
    // Стабильные ID, общие для Editor-seed скрипта (Encounters/Editor/
    // EncounterWarmSheepSeedData.cs) и runtime-интеграции
    // (PrototypeUIController.Encounters.cs) — чтобы не дублировать
    // строковый литерал пула в двух разных сборках.
    public static class RoadEncounterIds
    {
        public const string FirstRegionPoolId = "ROAD_POOL_01";
        public const string FirstRegionId = "road";
    }
}
