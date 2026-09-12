using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.Encounters
{
    [CreateAssetMenu(
        fileName = "KingdomSurvivalEncounters",
        menuName = "Kingdom Survival/База энкаунтеров")]
    public sealed class EncounterDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "Encounters/KingdomSurvivalEncounters";

        [SerializeField] private List<EncounterPoolDefinition> pools = new List<EncounterPoolDefinition>();
        [SerializeField] private List<EncounterDefinition> encounters = new List<EncounterDefinition>();

        public IReadOnlyList<EncounterPoolDefinition> Pools => pools;
        public IReadOnlyList<EncounterDefinition> Encounters => encounters;

        public EncounterDefinition FindById(string encounterId)
        {
            if (string.IsNullOrWhiteSpace(encounterId) || encounters == null)
                return null;

            for (int i = 0; i < encounters.Count; i++)
            {
                EncounterDefinition encounter = encounters[i];
                if (encounter != null && string.Equals(encounter.EncounterId, encounterId, StringComparison.Ordinal))
                    return encounter;
            }

            return null;
        }

        public EncounterPoolDefinition FindPool(string poolId)
        {
            if (string.IsNullOrWhiteSpace(poolId) || pools == null)
                return null;

            for (int i = 0; i < pools.Count; i++)
            {
                EncounterPoolDefinition pool = pools[i];
                if (pool != null && string.Equals(pool.PoolId, poolId, StringComparison.Ordinal))
                    return pool;
            }

            return null;
        }

        public List<EncounterDefinition> GetByPool(string poolId)
        {
            List<EncounterDefinition> result = new List<EncounterDefinition>();
            if (string.IsNullOrWhiteSpace(poolId) || encounters == null)
                return result;

            for (int i = 0; i < encounters.Count; i++)
            {
                EncounterDefinition encounter = encounters[i];
                if (encounter != null &&
                    encounter.SelectionMode == EncounterSelectionMode.Pool &&
                    string.Equals(encounter.PoolId, poolId, StringComparison.Ordinal))
                {
                    result.Add(encounter);
                }
            }

            return result;
        }

        public void CollectValidationIssues(List<string> issues)
        {
            if (issues == null)
                throw new ArgumentNullException(nameof(issues));

            issues.Clear();

            HashSet<string> poolIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < pools.Count; i++)
            {
                EncounterPoolDefinition pool = pools[i];
                if (pool == null || string.IsNullOrWhiteSpace(pool.PoolId))
                {
                    issues.Add("Пул #" + (i + 1) + ": отсутствует PoolId.");
                    continue;
                }

                if (!poolIds.Add(pool.PoolId))
                    issues.Add("Повторяющийся PoolId: " + pool.PoolId + ".");
            }

            HashSet<string> encounterIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < encounters.Count; i++)
            {
                EncounterDefinition encounter = encounters[i];
                if (encounter == null || string.IsNullOrWhiteSpace(encounter.EncounterId))
                {
                    issues.Add("Encounter #" + (i + 1) + ": отсутствует EncounterId.");
                    continue;
                }

                if (!encounterIds.Add(encounter.EncounterId))
                    issues.Add("Повторяющийся EncounterId: " + encounter.EncounterId + ".");

                bool isProduction = encounter.Status == EncounterStatus.Production;

                if (encounter.SelectionMode == EncounterSelectionMode.Pool)
                {
                    if (string.IsNullOrWhiteSpace(encounter.PoolId))
                        issues.Add(encounter.EncounterId + ": режим Pool без PoolId.");
                    else if (isProduction && !poolIds.Contains(encounter.PoolId))
                        issues.Add(encounter.EncounterId + ": ссылается на несуществующий пул " + encounter.PoolId + ".");
                }

                if (isProduction &&
                    encounter.ResolutionMode == EncounterResolutionMode.DialogueDriven &&
                    string.IsNullOrWhiteSpace(encounter.DialogueId))
                {
                    issues.Add(encounter.EncounterId + ": Production Encounter без DialogueId.");
                }

                if (isProduction &&
                    encounter.SelectionMode == EncounterSelectionMode.Pool &&
                    encounter.SelectionWeight <= 0)
                {
                    issues.Add(encounter.EncounterId + ": SelectionWeight должен быть больше 0 для Pool-энкаунтера.");
                }

                if (!encounter.UnlimitedOccurrences && encounter.MaxOccurrencesPerGame < 1)
                    issues.Add(encounter.EncounterId + ": MaxOccurrencesPerGame < 1 при UnlimitedOccurrences = false.");

                foreach (string flagId in encounter.RequiredFlagsAll)
                {
                    if (encounter.ForbiddenFlags.Contains(flagId))
                        issues.Add(encounter.EncounterId + ": флаг " + flagId + " одновременно в Required и Forbidden.");
                }

                foreach (string flagId in encounter.RequiredFlagsAny)
                {
                    if (encounter.ForbiddenFlags.Contains(flagId))
                        issues.Add(encounter.EncounterId + ": флаг " + flagId + " одновременно в Required и Forbidden.");
                }
            }
        }
    }
}
