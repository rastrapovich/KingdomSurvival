namespace KingdomSurvival.Encounters
{
    // MVP поддерживает только DialogueDriven — Encounter всегда открывает
    // существующий Narrative Dialogue (единый канал показа, см. итоговое
    // UI-правило спецификации). Automatic/PassiveCheck/ChoiceWithCheck —
    // Reactive Encounter Layer, Phase 2. Значения добавлять строго в конец
    // enum, если Encounter Database уже содержит сериализованные данные.
    public enum EncounterResolutionMode
    {
        DialogueDriven
    }
}
