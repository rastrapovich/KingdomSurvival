// AM-07: чистая логика поиска — сама не хранит состояние (оно уже есть в
// NarrativeStateData.Flags, часть GameState, сохраняется/восстанавливается
// обычным Save/Load без дополнительного снимка). Один resolver для UI,
// tooltip и списка локаций — раздел 12 инструкции требует, чтобы все
// потребители смотрели в один источник видимости, а не дублировали логику.
public static class WorldMapKnowledgeService
{
    public static WorldMapSearchStageDefinition GetActiveStage(
        WorldMapSearchAreaDefinition search,
        NarrativeStateData narrative)
    {
        if (search == null || search.Stages == null || narrative == null)
            return null;

        WorldMapSearchStageDefinition active = null;
        foreach (WorldMapSearchStageDefinition stage in search.Stages)
        {
            if (stage != null && AllFlagsKnown(stage.RequiredKnowledgeFlags, narrative))
                active = stage;
        }

        return active;
    }

    private static bool AllFlagsKnown(
        System.Collections.Generic.List<string> flags,
        NarrativeStateData narrative)
    {
        if (flags == null)
            return true;

        foreach (string flag in flags)
        {
            if (!narrative.HasFlag(flag))
                return false;
        }

        return true;
    }
}
