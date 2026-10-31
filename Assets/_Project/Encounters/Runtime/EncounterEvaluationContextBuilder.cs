using System;
using System.Collections.Generic;

namespace KingdomSurvival.Encounters
{
    // Контекст встреч: присутствующие бойцы, вещи рядом и размер отряда.
    // Encounters — общий модуль, не зависящий от конкретного содержания
    // (правило модульности CLAUDE.md).
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

        // ПР-12К (канон v1.53 §28.9): бойцы, физически присутствующие рядом.
        public static List<string> GetPresentCompanionIds(GameState gameState)
        {
            return PartyPresence.PresentFighterIds(gameState);
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
            return PartyPresence.PresentPartySize(gameState);
        }
    }
}
