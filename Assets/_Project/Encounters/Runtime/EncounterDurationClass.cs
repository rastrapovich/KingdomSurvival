namespace KingdomSurvival.Encounters
{
    // Предполагаемая продолжительность взаимодействия игрока, не сюжетная
    // важность (§94-95, обновлённая версия §152). Presentation всегда одна и
    // та же (Narrative Dialogue) — эта классификация нужна для авторского
    // темпа (pacing-лимиты пула, Monte Carlo распределение), не для UI.
    public enum EncounterDurationClass
    {
        Reaction,
        Micro,
        Short,
        Standard,
        Complex,
        QuestSeed
    }
}
