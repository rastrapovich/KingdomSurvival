namespace KingdomSurvival.Encounters
{
    // Pool — выбирается EncounterSelector из общего пула (§8).
    // Direct — запускается конкретным сюжетным кодом по ID, минуя Selector
    // (обязательные сцены вроде N11/D11B остаются на этом режиме).
    public enum EncounterSelectionMode
    {
        Pool,
        Direct
    }
}
