using System;
using System.Collections.Generic;

// Персистентная история энкаунтеров (сколько раз стартовал/завершился,
// когда в последний раз) — сколько раз сцена появилась, а не что произошло
// внутри неё (это остаётся в NarrativeStateData.Flags). Лежит в Core (а не
// в модуле Encounters), т.к. является полем GameState, а Core ни на что не
// ссылается (§18, §64 инструкции по Encounter-системе).
[Serializable]
public sealed class EncounterRuntimeEntry
{
    public string EncounterId = string.Empty;
    public int TimesStarted;
    public int TimesCompleted;

    // -1 = ещё ни разу не происходило. Часы считаются от начала мира
    // (WorldHour = Day * 24), см. допущение в EncounterOpportunity.
    public double LastStartedWorldHour = -1;
    public double LastCompletedWorldHour = -1;
}

[Serializable]
public sealed class EncounterPoolCooldownEntry
{
    public string PoolId = string.Empty;
    public double LastTriggeredWorldHour;
}

[Serializable]
public sealed class EncounterRuntimeStateData
{
    public List<EncounterRuntimeEntry> Entries = new List<EncounterRuntimeEntry>();

    // Защита от повторного разрешения одной и той же Opportunity (§22, §56) —
    // например, если игрок сохранился прямо перед проверкой и перезагрузился.
    public List<string> ProcessedOpportunities = new List<string>();

    // Пер-пуловый кулдаун (EncounterPoolDefinition.MinimumHoursBetweenEncounters).
    // Список, а не Dictionary — как Relations в NarrativeStateData: в проекте
    // пока нет системы сохранений, но остальной Core сознательно избегает
    // типов, не сериализуемых стандартным Unity JsonUtility.
    public List<EncounterPoolCooldownEntry> PoolCooldowns = new List<EncounterPoolCooldownEntry>();

    // Защита совместимости старых сохранений (§65, §66): после десериализации
    // старого save без этого поля коллекции будут null.
    public void EnsureInitialized()
    {
        if (Entries == null)
            Entries = new List<EncounterRuntimeEntry>();
        if (ProcessedOpportunities == null)
            ProcessedOpportunities = new List<string>();
        if (PoolCooldowns == null)
            PoolCooldowns = new List<EncounterPoolCooldownEntry>();
    }

    public EncounterRuntimeEntry FindEntry(string encounterId)
    {
        if (string.IsNullOrWhiteSpace(encounterId) || Entries == null)
            return null;

        for (int i = 0; i < Entries.Count; i++)
        {
            EncounterRuntimeEntry entry = Entries[i];
            if (entry != null && string.Equals(entry.EncounterId, encounterId, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    public EncounterRuntimeEntry FindOrCreateEntry(string encounterId)
    {
        if (string.IsNullOrWhiteSpace(encounterId))
            throw new ArgumentException("EncounterId must not be empty.", nameof(encounterId));

        EncounterRuntimeEntry entry = FindEntry(encounterId);
        if (entry != null)
            return entry;

        if (Entries == null)
            Entries = new List<EncounterRuntimeEntry>();

        entry = new EncounterRuntimeEntry { EncounterId = encounterId };
        Entries.Add(entry);
        return entry;
    }

    public bool WasOpportunityProcessed(string opportunityId)
    {
        return !string.IsNullOrWhiteSpace(opportunityId) &&
               ProcessedOpportunities != null &&
               ProcessedOpportunities.Contains(opportunityId);
    }

    public void MarkOpportunityProcessed(string opportunityId)
    {
        if (string.IsNullOrWhiteSpace(opportunityId))
            return;
        if (ProcessedOpportunities == null)
            ProcessedOpportunities = new List<string>();
        if (!ProcessedOpportunities.Contains(opportunityId))
            ProcessedOpportunities.Add(opportunityId);
    }

    public double GetPoolLastTriggeredWorldHour(string poolId)
    {
        if (string.IsNullOrWhiteSpace(poolId) || PoolCooldowns == null)
            return -1;

        for (int i = 0; i < PoolCooldowns.Count; i++)
        {
            EncounterPoolCooldownEntry entry = PoolCooldowns[i];
            if (entry != null && string.Equals(entry.PoolId, poolId, StringComparison.Ordinal))
                return entry.LastTriggeredWorldHour;
        }

        return -1;
    }

    public void MarkPoolTriggered(string poolId, double worldHour)
    {
        if (string.IsNullOrWhiteSpace(poolId))
            return;
        if (PoolCooldowns == null)
            PoolCooldowns = new List<EncounterPoolCooldownEntry>();

        for (int i = 0; i < PoolCooldowns.Count; i++)
        {
            EncounterPoolCooldownEntry entry = PoolCooldowns[i];
            if (entry != null && string.Equals(entry.PoolId, poolId, StringComparison.Ordinal))
            {
                entry.LastTriggeredWorldHour = worldHour;
                return;
            }
        }

        PoolCooldowns.Add(new EncounterPoolCooldownEntry { PoolId = poolId, LastTriggeredWorldHour = worldHour });
    }
}
