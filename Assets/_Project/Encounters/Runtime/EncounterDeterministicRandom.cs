using System;
using System.Collections.Generic;

namespace KingdomSurvival.Encounters
{
    // Детерминированный random (§54, §55): один и тот же
    // (WorldSeed, OpportunityId, EncounterId, RollPurpose) всегда даёт один
    // и тот же результат — реролл через save/load невозможен. Намеренно НЕ
    // использует UnityEngine.Random (его состояние глобально и меняется от
    // посторонних вызовов) и НЕ использует string.GetHashCode() (рандомизирован
    // между процессами в .NET) — вместо этого стабильный FNV-1a хеш строк.
    public static class EncounterDeterministicRandom
    {
        public static int BuildSeed(int worldSeed, string opportunityId, string encounterId, string rollPurpose)
        {
            unchecked
            {
                uint hash = 2166136261;
                hash = MixString(hash, worldSeed.ToString());
                hash = MixString(hash, opportunityId ?? string.Empty);
                hash = MixString(hash, encounterId ?? string.Empty);
                hash = MixString(hash, rollPurpose ?? string.Empty);
                return (int)hash;
            }
        }

        // Возвращает [0, 100). PASS если результат < percentChance.
        public static bool RollPercent(int worldSeed, string opportunityId, string encounterId, string rollPurpose, int percentChance)
        {
            if (percentChance <= 0)
                return false;
            if (percentChance >= 100)
                return true;

            int seed = BuildSeed(worldSeed, opportunityId, encounterId, rollPurpose);
            System.Random random = new System.Random(seed);
            return random.Next(0, 100) < percentChance;
        }

        public static T WeightedPick<T>(
            int worldSeed,
            string opportunityId,
            string rollPurpose,
            IReadOnlyList<T> items,
            Func<T, int> weightSelector)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException("Items must not be empty.", nameof(items));
            if (weightSelector == null)
                throw new ArgumentNullException(nameof(weightSelector));

            if (items.Count == 1)
                return items[0];

            int totalWeight = 0;
            for (int i = 0; i < items.Count; i++)
                totalWeight += Math.Max(0, weightSelector(items[i]));

            if (totalWeight <= 0)
                return items[0];

            int seed = BuildSeed(worldSeed, opportunityId, "weighted_pick", rollPurpose);
            System.Random random = new System.Random(seed);
            int roll = random.Next(0, totalWeight);

            int cumulative = 0;
            for (int i = 0; i < items.Count; i++)
            {
                cumulative += Math.Max(0, weightSelector(items[i]));
                if (roll < cumulative)
                    return items[i];
            }

            return items[items.Count - 1];
        }

        private static uint MixString(uint hash, string value)
        {
            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619;
                }
            }

            return hash;
        }
    }
}
