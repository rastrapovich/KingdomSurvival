using System;

namespace KingdomSurvival.Chapter01
{
    // Одноразовые внешние эффекты Главы 01, которых пока нет среди
    // стандартных NarrativeEffect (раздел 6.4 инструкции): ресурсы, время,
    // предметы. Каждое применение защищено стабильным execution ID через
    // тот же механизм, что уже используют NarrativeEffect
    // (NarrativeStateData.AppliedEffectExecutionIds) — повторный вызов с тем
    // же ID не повторяет эффект.
    public static class Chapter01OutcomeApplier
    {
        public static bool ApplyResourceDelta(
            GameState gameState,
            string executionId,
            int foodDelta = 0,
            int goldDelta = 0,
            int armySupplyDelta = 0)
        {
            return Apply(gameState, executionId, state =>
            {
                gameState.Food += foodDelta;
                gameState.Gold += goldDelta;
                gameState.ArmySupply += armySupplyDelta;
            });
        }

        public static bool ApplyTimeAdvance(GameState gameState, string executionId, int days)
        {
            if (days <= 0)
                throw new ArgumentOutOfRangeException(nameof(days), days, "Продвижение времени должно быть положительным.");

            return Apply(gameState, executionId, state => gameState.Day += days);
        }

        public static bool GrantItem(GameState gameState, string executionId, string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Item id cannot be empty.", nameof(itemId));

            return Apply(gameState, executionId, state => state.GrantItem(itemId));
        }

        // До появления постоянной системы HP/травм (раздел 6.4) травма
        // героя — нарративный флаг с видимым последствием, а не отдельное
        // механическое поле.
        public static bool MarkHeroInjured(GameState gameState, string executionId, string injuryFlagId)
        {
            if (string.IsNullOrWhiteSpace(injuryFlagId))
                throw new ArgumentException("Injury flag id cannot be empty.", nameof(injuryFlagId));

            return Apply(gameState, executionId, state => state.SetFlag(injuryFlagId));
        }

        private static bool Apply(GameState gameState, string executionId, Action<NarrativeStateData> mutation)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));
            if (string.IsNullOrWhiteSpace(executionId))
                throw new ArgumentException("Execution id cannot be empty.", nameof(executionId));
            if (mutation == null)
                throw new ArgumentNullException(nameof(mutation));

            if (gameState.Narrative == null)
                gameState.Narrative = new NarrativeStateData();

            if (gameState.Narrative.HasEffectApplied(executionId))
                return false;

            mutation(gameState.Narrative);
            gameState.Narrative.MarkEffectApplied(executionId);
            return true;
        }
    }
}
