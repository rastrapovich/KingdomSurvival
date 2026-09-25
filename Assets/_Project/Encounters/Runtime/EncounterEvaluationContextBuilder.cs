using System;
using System.Collections.Generic;

namespace KingdomSurvival.Encounters
{
    // Аналог Chapter01ContextBuilder (Chapter01/Runtime/Chapter01ContextBuilder.cs),
    // сознательно продублированный здесь вместо ссылки на сборку Chapter01 —
    // Encounters должен оставаться общим модулем, не зависящим от конкретной
    // главы (правило модульности CLAUDE.md). TODO: если в будущем понадобится
    // третий потребитель этой же логики, вынести общий билдер в Scripts/Core.
    public static class EncounterEvaluationContextBuilder
    {
        public static NarrativeEvaluationContext Build(GameState gameState, HeroProfileData hero)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));
            if (hero == null)
                throw new ArgumentNullException(nameof(hero));
            if (gameState.Narrative == null)
                gameState.Narrative = new NarrativeStateData();

            return new NarrativeEvaluationContext(
                hero,
                gameState.Narrative,
                GetPresentCompanionIds(gameState),
                GetPresentItemIds(gameState),
                gameState.WorldSeed,
                GetPartySize(gameState),
                gameState);
        }

        public static List<string> GetPresentCompanionIds(GameState gameState)
        {
            List<string> companions = new List<string>();
            if (gameState != null && gameState.HasActiveExpedition && gameState.ActiveExpedition.FighterIds != null)
                companions.AddRange(gameState.ActiveExpedition.FighterIds);
            return companions;
        }

        public static List<string> GetPresentItemIds(GameState gameState)
        {
            // ПР-08: вещи рядом — у отряда в пути, дома у жителей и в кладовой.
            return gameState != null ? ItemService.GetPresentItemIds(gameState) : new List<string>();
        }

        public static int GetPartySize(GameState gameState)
        {
            if (gameState == null || !gameState.HasActiveExpedition || gameState.ActiveExpedition.FighterIds == null)
                return 1;
            return gameState.ActiveExpedition.FighterIds.Count + 1;
        }
    }
}
