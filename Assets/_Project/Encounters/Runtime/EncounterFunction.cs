namespace KingdomSurvival.Encounters
{
    // Дизайнерская маркировка "зачем этот Encounter существует" (§111-112) —
    // не условие появления, только классификация для Content Balance
    // Diagnostics (§129) в Monte Carlo. Отдельно от свободных строковых Tags:
    // Tags описывают характеристику сцены (forest/animal/mundane), Function —
    // её функцию в общем темпе игры.
    public enum EncounterFunction
    {
        Atmosphere,
        WorldTexture,
        ResourcePressure,
        Risk,
        CompanionCharacterization,
        Relationship,
        Knowledge,
        Foreshadowing,
        WorldState,
        QuestSeed,
        MapDiscovery
    }
}
