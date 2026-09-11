using System;
using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
    // Формирует контекст спутников, предметов и размера отряда для Главы 01
    // (раздел 6.2 инструкции). Не блокирует обязательный путь отсутствием
    // конкретного спутника или предмета — только сообщает, что присутствует.
    public static class Chapter01ContextBuilder
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
                GetPartySize(gameState));
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
            List<string> items = new List<string>();
            if (gameState?.Narrative?.Items != null)
                items.AddRange(gameState.Narrative.Items);
            return items;
        }

        // Размер экспедиции = герой + бойцы, 1..5 (P10-T04: PartySize —
        // отдельное понятие от списка спутников PresentCompanionIds; герой
        // всегда входит в отряд, даже когда идёт один). Не постоянный флаг
        // party_size_N — контекстное значение, пересчитываемое каждый раз.
        public static int GetPartySize(GameState gameState)
        {
            if (gameState == null || !gameState.HasActiveExpedition || gameState.ActiveExpedition.FighterIds == null)
                return 1;
            return gameState.ActiveExpedition.FighterIds.Count + 1;
        }
    }
}
