namespace KingdomSurvival.Encounters
{
    // Минимальный набор под MVP-срез (§9): достаточно категорий, чтобы
    // отличать дорожные энкаунтеры от прочих. Расширять по мере появления
    // контента, не заранее.
    public enum EncounterCategory
    {
        Road,
        Camp,
        Location
    }
}
