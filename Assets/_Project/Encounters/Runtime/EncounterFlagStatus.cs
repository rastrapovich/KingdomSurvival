namespace KingdomSurvival.Encounters
{
    // Reserved — намеренная сюжетная закладка (Future Hook), создаётся ДО
    // того как появится сцена, которая её читает. Validator не должен
    // считать это мусором (§30). Deprecated — вручную помечен как больше не
    // актуальный, но не удалён физически, т.к. мог быть Producer'ом в старых
    // сохранениях.
    public enum EncounterFlagStatus
    {
        Active,
        Reserved,
        Deprecated
    }
}
