using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.Encounters
{
    [CreateAssetMenu(
        fileName = "KingdomSurvivalEncounterFlags",
        menuName = "Kingdom Survival/Реестр флагов энкаунтеров")]
    public sealed class EncounterFlagRegistryAsset : ScriptableObject
    {
        public const string ResourcesPath = "Encounters/KingdomSurvivalEncounterFlags";

        [SerializeField] private List<EncounterFlagDefinition> flags = new List<EncounterFlagDefinition>();

        public IReadOnlyList<EncounterFlagDefinition> Flags => flags;

        public EncounterFlagDefinition FindById(string flagId)
        {
            if (string.IsNullOrWhiteSpace(flagId) || flags == null)
                return null;

            for (int i = 0; i < flags.Count; i++)
            {
                EncounterFlagDefinition flag = flags[i];
                if (flag != null && string.Equals(flag.FlagId, flagId, StringComparison.Ordinal))
                    return flag;
            }

            return null;
        }

        public bool IsKnownFlag(string flagId)
        {
            return FindById(flagId) != null;
        }

        public void CollectValidationIssues(List<string> issues)
        {
            if (issues == null)
                throw new ArgumentNullException(nameof(issues));

            issues.Clear();

            HashSet<string> flagIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < flags.Count; i++)
            {
                EncounterFlagDefinition flag = flags[i];
                if (flag == null || string.IsNullOrWhiteSpace(flag.FlagId))
                {
                    issues.Add("Флаг #" + (i + 1) + ": отсутствует FlagId.");
                    continue;
                }

                if (!flagIds.Add(flag.FlagId))
                    issues.Add("Повторяющийся FlagId: " + flag.FlagId + ".");

                // Active + Unused — предупреждение (§31), но проверка "нет
                // потребителя" требует знания Encounter Database и потому
                // выполняется отдельным кросс-валидатором, не здесь.
                if (flag.Status == EncounterFlagStatus.Active && string.IsNullOrWhiteSpace(flag.DisplayName))
                    issues.Add(flag.FlagId + ": Active-флаг без DisplayName.");
            }
        }
    }
}
