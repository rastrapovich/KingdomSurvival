namespace KingdomSurvival.Encounters
{
    // Production ID считается immutable (§6 инструкции по Encounter-системе).
    // Deprecated используется вместо физического удаления, чтобы не сломать
    // старые сохранения, которые могли зафиксировать этот EncounterId.
    public enum EncounterStatus
    {
        Draft,
        Production,
        Disabled,
        Deprecated
    }
}
