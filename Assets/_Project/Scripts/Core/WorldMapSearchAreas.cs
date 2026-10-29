using System;
using System.Collections.Generic;

// 12Т (SETTLEMENTS_TRADE_TAVERN_SPEC.md §12): области поиска, которые сейчас
// нужно показать на карте. Истории режима регистрируют свои области;
// активная стадия — по-прежнему WorldMapKnowledgeService (один источник).
// Найденное место (видимое на карте) области больше не показывает: точный
// маркер появляется только у самой находки.
public sealed class ActiveSearchArea
{
    public WorldMapSearchAreaDefinition Search;
    public WorldMapSearchStageDefinition Stage;
}

public static class WorldMapSearchAreas
{
    private static readonly List<Func<GameState, IEnumerable<WorldMapSearchAreaDefinition>>> Providers =
        new List<Func<GameState, IEnumerable<WorldMapSearchAreaDefinition>>>();

    public static void Register(Func<GameState, IEnumerable<WorldMapSearchAreaDefinition>> provider)
    {
        if (provider != null && !Providers.Contains(provider))
            Providers.Add(provider);
    }

    public static List<ActiveSearchArea> Active(GameState state)
    {
        List<ActiveSearchArea> result = new List<ActiveSearchArea>();
        if (state?.Narrative == null)
            return result;
        foreach (Func<GameState, IEnumerable<WorldMapSearchAreaDefinition>> provider in Providers)
        {
            IEnumerable<WorldMapSearchAreaDefinition> searches = provider(state);
            if (searches == null)
                continue;
            foreach (WorldMapSearchAreaDefinition search in searches)
            {
                if (search == null)
                    continue;
                // Найдено (видно на карте) или уже обыскано — области нет.
                LocationData target = state.FindLocation(search.TargetLocationId);
                if (target != null && (target.IsVisibleOnMap || target.IsExplored))
                    continue;
                WorldMapSearchStageDefinition stage = WorldMapKnowledgeService.GetActiveStage(search, state.Narrative);
                if (stage != null)
                    result.Add(new ActiveSearchArea { Search = search, Stage = stage });
            }
        }
        return result;
    }
}
