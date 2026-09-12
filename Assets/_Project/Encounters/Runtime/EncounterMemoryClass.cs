namespace KingdomSurvival.Encounters
{
    // Три уровня памяти Encounter (§115): не каждая мелочь обязана оставлять
    // постоянный флаг — иначе флаговый мусор (§114).
    public enum EncounterMemoryClass
    {
        // Мир не хранит исход после завершения — нормальный World Texture
        // Encounter, не ошибка (§113, §153).
        None,

        // Небольшое временное/локальное состояние (например, "пёс идёт за
        // отрядом"), не обязательно постоянный NarrativeState-флаг.
        Local,

        // Решение может быть прочитано намного позже — future hook,
        // Flag Registry со статусом Reserved.
        Persistent
    }
}
