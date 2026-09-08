using System;
using System.Collections.Generic;
using System.Linq;

// Шесть финальных качеств героя (1-10). См. производственную инструкцию
// "качества, проверки и реактивный текст" §2.
public enum HeroQuality
{
    Strength,
    Dexterity,
    Fortitude,
    Instinct,
    Judgment,
    Character
}

// Стабильные строковые ID компетенций и особенностей, зарегистрированных
// на сегодня. Список расширяется по мере появления реального контента —
// см. §3/§4 инструкции.
public static class NarrativeCompetencyIds
{
    public const string Fieldcraft = "fieldcraft";

    public static readonly IReadOnlyList<string> Known = new List<string> { Fieldcraft };

    public static bool IsKnown(string competencyId)
    {
        return !string.IsNullOrWhiteSpace(competencyId) && Known.Contains(competencyId);
    }
}

public static class NarrativeTraitIds
{
    public const string KnowsTheWay = "knows_the_way";
    public const string Naturalist = "naturalist";

    public static readonly IReadOnlyList<string> Known = new List<string> { KnowsTheWay, Naturalist };
}

[Serializable]
public sealed class HeroCompetencyData
{
    public string CompetencyId;
    public int Value;

    public HeroCompetencyData()
    {
        CompetencyId = string.Empty;
        Value = 0;
    }

    public HeroCompetencyData(string competencyId, int value)
    {
        CompetencyId = competencyId ?? string.Empty;
        Value = NarrativeCheckMath.ClampCompetency(value);
    }
}

// Хранит шесть качеств отдельными полями (не словарём), компетенции и
// особенности как сериализуемые списки со стабильными строковыми ID.
// См. §2-§4, §8 инструкции.
[Serializable]
public sealed class HeroProfileData
{
    public const int MinQualityValue = 1;
    public const int MaxQualityValue = 10;
    public const int DefaultQualityValue = 5;

    public int Strength = DefaultQualityValue;
    public int Dexterity = DefaultQualityValue;
    public int Fortitude = DefaultQualityValue;
    public int Instinct = DefaultQualityValue;
    public int Judgment = DefaultQualityValue;
    public int Character = DefaultQualityValue;

    public List<HeroCompetencyData> Competencies = new List<HeroCompetencyData>();
    public List<string> Traits = new List<string>();

    public int GetQuality(HeroQuality quality)
    {
        switch (quality)
        {
            case HeroQuality.Strength: return Strength;
            case HeroQuality.Dexterity: return Dexterity;
            case HeroQuality.Fortitude: return Fortitude;
            case HeroQuality.Instinct: return Instinct;
            case HeroQuality.Judgment: return Judgment;
            case HeroQuality.Character: return Character;
            default: throw new ArgumentOutOfRangeException(nameof(quality), quality, null);
        }
    }

    public void SetQuality(HeroQuality quality, int value)
    {
        int clamped = NarrativeCheckMath.ClampQuality(value);
        switch (quality)
        {
            case HeroQuality.Strength: Strength = clamped; break;
            case HeroQuality.Dexterity: Dexterity = clamped; break;
            case HeroQuality.Fortitude: Fortitude = clamped; break;
            case HeroQuality.Instinct: Instinct = clamped; break;
            case HeroQuality.Judgment: Judgment = clamped; break;
            case HeroQuality.Character: Character = clamped; break;
            default: throw new ArgumentOutOfRangeException(nameof(quality), quality, null);
        }
    }

    public int GetCompetency(string competencyId)
    {
        if (string.IsNullOrWhiteSpace(competencyId) || Competencies == null)
            return 0;

        for (int i = 0; i < Competencies.Count; i++)
        {
            HeroCompetencyData entry = Competencies[i];
            if (entry != null && string.Equals(entry.CompetencyId, competencyId, StringComparison.Ordinal))
                return NarrativeCheckMath.ClampCompetency(entry.Value);
        }

        return 0;
    }

    public void SetCompetency(string competencyId, int value)
    {
        if (string.IsNullOrWhiteSpace(competencyId))
            throw new ArgumentException("Competency id cannot be empty.", nameof(competencyId));

        if (Competencies == null)
            Competencies = new List<HeroCompetencyData>();

        int clamped = NarrativeCheckMath.ClampCompetency(value);
        for (int i = 0; i < Competencies.Count; i++)
        {
            HeroCompetencyData entry = Competencies[i];
            if (entry != null && string.Equals(entry.CompetencyId, competencyId, StringComparison.Ordinal))
            {
                entry.Value = clamped;
                return;
            }
        }

        Competencies.Add(new HeroCompetencyData(competencyId, clamped));
    }

    public bool HasTrait(string traitId)
    {
        if (string.IsNullOrWhiteSpace(traitId) || Traits == null)
            return false;

        for (int i = 0; i < Traits.Count; i++)
        {
            if (string.Equals(Traits[i], traitId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public void GrantTrait(string traitId)
    {
        if (string.IsNullOrWhiteSpace(traitId))
            throw new ArgumentException("Trait id cannot be empty.", nameof(traitId));

        if (Traits == null)
            Traits = new List<string>();

        if (!HasTrait(traitId))
            Traits.Add(traitId);
    }

    public void RemoveTrait(string traitId)
    {
        if (string.IsNullOrWhiteSpace(traitId) || Traits == null)
            return;

        Traits.RemoveAll(id => string.Equals(id, traitId, StringComparison.Ordinal));
    }
}
