using System;
using System.Collections.Generic;

// 12Е-2: особенности у каждого человека, который развивается (Командир и
// постоянные бойцы). Особенность хранится с рангом и источником: выбор
// уровня, биография, история, след, учитель, итог личной истории (§0.1
// каталога — разрешены все). Свита и жители прогрессию не получают.
//
// У Командира прежние особенности живут и в HeroProfile.Traits: их читают
// условия диалогов и дорожные встречи. Выдача Командиру пишет в оба места;
// особенность, выданная диалогом прямо в профиль, видна как ранг 1 из
// истории.

[Serializable]
public sealed class PersonFeatureData
{
    public string FeatureId = string.Empty;
    public int Rank = 1;
    public FeatureSource Source = FeatureSource.LevelChoice;
    // Кто или что дало: ID истории, учителя, узла диалога.
    public string SourceId = string.Empty;
    // Уровень человека в момент получения.
    public int AcquiredLevel = 1;
}

// Особенность человека для показа: запись каталога, ранг и откуда.
public sealed class OwnedFeature
{
    public string FeatureId;
    public int Rank;
    public FeatureSource Source;
    public string SourceId;
    public TraitCatalogEntry Entry;

    public string Title
    {
        get
        {
            if (Entry == null)
                return FeatureId;
            if (Entry.Ranks.Count <= 1)
                return Entry.Name;
            string rankName = Entry.RankName(Rank);
            return rankName == Entry.Name + " " + TraitCatalogEntry.RomanRank(Rank) || rankName == Entry.Name
                ? Entry.Name + " " + TraitCatalogEntry.RomanRank(Rank)
                : Entry.Name + " — " + rankName;
        }
    }
}

public static class CharacterFeatureService
{
    public const string ToughnessId = "toughness";
    private const string SourcePrefix = "feature:";

    // Все особенности человека: записи прогрессии, прежние особенности
    // профиля Командира и «Крепость тела» (её ранг — число таких выборов).
    public static List<OwnedFeature> GetFeatures(GameState state, string personId)
    {
        List<OwnedFeature> result = new List<OwnedFeature>();
        PersonProgressionData record = CharacterProgressionService.Get(state, personId);
        if (record == null)
            return result;

        foreach (PersonFeatureData data in Records(record))
        {
            if (data == null || string.IsNullOrWhiteSpace(data.FeatureId) || data.Rank <= 0)
                continue;
            result.Add(Describe(data.FeatureId, data.Rank, data.Source, data.SourceId));
        }

        HeroProfileData hero = HeroProfileOf(state, personId);
        if (hero?.Traits != null)
        {
            foreach (string traitId in hero.Traits)
            {
                if (!string.IsNullOrWhiteSpace(traitId) && result.TrueForAll(owned => owned.FeatureId != traitId))
                    result.Add(Describe(traitId, 1, FeatureSource.Story, string.Empty));
            }
        }

        if (record.ToughnessChoices > 0 && result.TrueForAll(owned => owned.FeatureId != ToughnessId))
            result.Add(Describe(ToughnessId, Math.Min(TraitCatalogEntry.MaxRanks, record.ToughnessChoices), FeatureSource.LevelChoice, string.Empty));

        return result;
    }

    public static int GetRank(GameState state, string personId, string featureId)
    {
        if (string.IsNullOrWhiteSpace(featureId))
            return 0;
        foreach (OwnedFeature owned in GetFeatures(state, personId))
        {
            if (owned.FeatureId == featureId)
                return owned.Rank;
        }
        return 0;
    }

    public static bool Has(GameState state, string personId, string featureId)
    {
        return GetRank(state, personId, featureId) > 0;
    }

    // Может ли этот человек владеть записью: приёмы и приказы — не
    // особенности; «только Командир» — Командиру, «только боец» — бойцам.
    public static bool CanOwn(GameState state, string personId, TraitCatalogEntry entry, out string reason)
    {
        reason = string.Empty;
        if (entry == null)
        {
            reason = "Такой особенности нет в каталоге.";
            return false;
        }
        if (!CharacterProgressionService.IsProgressing(state, personId))
        {
            reason = "Особенности есть у Командира и постоянных бойцов.";
            return false;
        }
        if (entry.Layer != FeatureLayer.Feature)
        {
            reason = "«" + entry.Name + "» — " + FeatureLabels.Layer(entry.Layer).ToLowerInvariant() + ", а не особенность.";
            return false;
        }
        bool commander = IsCommander(state, personId);
        if (entry.Owner == FeatureOwner.Commander && !commander)
        {
            reason = "«" + entry.Name + "» — только у Командира.";
            return false;
        }
        if (entry.Owner == FeatureOwner.Fighter && commander)
        {
            reason = "«" + entry.Name + "» — только у постоянного бойца.";
            return false;
        }
        return true;
    }

    // Выдать особенность вне выбора уровня (история, учитель, след, биография)
    // или по выбору. Один источник выдаёт один раз: сохранение, загрузка и
    // повтор текста не выдают снова. Ранг не понижается и не выше 3.
    public static bool Grant(GameState state, string personId, string featureId, int rank, FeatureSource source,
        string sourceId, out string message, bool chronicle = true)
    {
        message = string.Empty;
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        TraitCatalogEntry entry = ProgressionCatalog.Current.FindTrait(featureId);
        if (!CanOwn(state, personId, entry, out message))
            return false;

        ProgressionStateData progression = CharacterProgressionService.EnsureState(state);
        string appliedKey = string.IsNullOrWhiteSpace(sourceId) ? null : SourcePrefix + sourceId + "|" + personId;
        if (appliedKey != null && progression.AppliedSources.Contains(appliedKey))
        {
            message = "Уже получено.";
            return false;
        }

        PersonProgressionData record = CharacterProgressionService.Get(state, personId);
        int target = Math.Max(1, Math.Min(entry.RankCount, rank));
        int current = GetRank(state, personId, featureId);
        if (current >= target)
        {
            message = CharacterProgressionService.DisplayName(state, personId) + " уже владеет: " + entry.RankName(current) + ".";
            if (appliedKey != null)
                progression.AppliedSources.Add(appliedKey);
            return false;
        }

        if (featureId == ToughnessId)
        {
            record.ToughnessChoices = Math.Max(record.ToughnessChoices, target);
            ItemService.RefreshMaxHitPoints(state, personId);
        }
        else
        {
            PersonFeatureData data = Find(record, featureId);
            if (data == null)
            {
                data = new PersonFeatureData { FeatureId = featureId };
                Records(record).Add(data);
            }
            data.Rank = target;
            data.Source = source;
            data.SourceId = sourceId ?? string.Empty;
            data.AcquiredLevel = record.Level;
            HeroProfileOf(state, personId)?.GrantTrait(featureId);
        }

        if (appliedKey != null)
            progression.AppliedSources.Add(appliedKey);

        OwnedFeature owned = Describe(featureId, target, source, sourceId);
        message = CharacterProgressionService.DisplayName(state, personId) + ": " +
                  (current > 0 ? "особенность усилилась — " : "новая особенность — ") + owned.Title + ".";
        if (chronicle)
            Chronicle.Record(state, "feature." + personId + "." + featureId + "." + target, "Развитие", message);
        return true;
    }

    // Снять особенность (история может отнять черту). У Командира — и из профиля.
    public static bool Remove(GameState state, string personId, string featureId)
    {
        PersonProgressionData record = CharacterProgressionService.Get(state, personId);
        if (record == null || string.IsNullOrWhiteSpace(featureId))
            return false;
        bool removed = Records(record).RemoveAll(data => data != null && data.FeatureId == featureId) > 0;
        HeroProfileData hero = HeroProfileOf(state, personId);
        if (hero != null && hero.HasTrait(featureId))
        {
            hero.RemoveTrait(featureId);
            removed = true;
        }
        return removed;
    }

    private static OwnedFeature Describe(string featureId, int rank, FeatureSource source, string sourceId)
    {
        return new OwnedFeature
        {
            FeatureId = featureId,
            Rank = rank,
            Source = source,
            SourceId = sourceId ?? string.Empty,
            Entry = ProgressionCatalog.Current.FindTrait(featureId)
        };
    }

    private static List<PersonFeatureData> Records(PersonProgressionData record)
    {
        if (record.Features == null)
            record.Features = new List<PersonFeatureData>();
        return record.Features;
    }

    private static PersonFeatureData Find(PersonProgressionData record, string featureId)
    {
        foreach (PersonFeatureData data in Records(record))
        {
            if (data != null && data.FeatureId == featureId)
                return data;
        }
        return null;
    }

    private static bool IsCommander(GameState state, string personId)
    {
        CommanderData commander = state?.GetSelectedCommander();
        return commander != null && commander.Id == personId;
    }

    private static HeroProfileData HeroProfileOf(GameState state, string personId)
    {
        CommanderData commander = state?.GetSelectedCommander();
        return commander != null && commander.Id == personId ? commander.HeroProfile : null;
    }
}
