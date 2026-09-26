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

// Стабильные строковые ID компетенций и особенностей. Компетенции — первый
// базовый каталог канона v1.49 §27.11 (24 штуки, порядок канона). Новая
// компетенция добавляется только при нескольких разных применениях.
public static class NarrativeCompetencyIds
{
    // Следопытство (исторический ID «полевого дела» сохранён: он уже
    // записан в диалогах и сохранениях).
    public const string Fieldcraft = "fieldcraft";
    public const string Stealth = "stealth";
    public const string Hunting = "hunting";

    // Зарегистрирована по решению DEC-08 (см. ProjectDocs/DEVELOPMENT_STATUS.md §2):
    // осознанное отступление от рекомендации сводной инструкции по Главе 01
    // не добавлять новую компетенцию ради одной сцены. Используется впервые
    // в N08 «Семь зубцов» (chapter01.node.08).
    public const string Craft = "craft";
    public const string Healing = "healing";
    public const string Household = "household";
    public const string Trade = "trade";
    public const string Negotiation = "negotiation";
    public const string CustomAndLaw = "custom_and_law";
    public const string Lore = "lore";
    public const string Rites = "rites";
    public const string ChoppingWeapons = "chopping_weapons";
    public const string Shooting = "shooting";
    public const string ShieldAndLine = "shield_and_line";
    public const string Herbalism = "herbalism";
    public const string Spearcraft = "spearcraft";
    public const string Observation = "observation";
    public const string Insight = "insight";
    public const string Fishing = "fishing";
    public const string Forestry = "forestry";
    public const string Livestock = "livestock";
    public const string Farming = "farming";
    public const string Building = "building";
    public const string Investigation = "investigation";

    public static readonly IReadOnlyList<string> Known = new List<string>
    {
        Fieldcraft, Stealth, Hunting, Craft, Healing, Household, Trade, Negotiation,
        CustomAndLaw, Lore, Rites, ChoppingWeapons, Shooting, ShieldAndLine, Herbalism,
        Spearcraft, Observation, Insight, Fishing, Forestry, Livestock, Farming, Building,
        Investigation
    };

    // Каталог постоянного бойца уже каталога Командира: бой и поход (§25.5).
    public static readonly IReadOnlyList<string> FighterCatalog = new List<string>
    {
        Fieldcraft, Stealth, Hunting, Healing, ChoppingWeapons, Shooting, ShieldAndLine,
        Spearcraft, Observation
    };

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
