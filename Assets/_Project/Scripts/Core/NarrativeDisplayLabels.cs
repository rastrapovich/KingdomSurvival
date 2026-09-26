using System.Collections.Generic;
using System.Globalization;

// Русские подписи качеств, компетенций и особенностей для механической строки
// диалога и экрана героя (§14, §17). Названия и описания — из «Базы развития»
// (ProgressionCatalog). Чисто отображение — не влияет на расчёт.
public static class NarrativeQualityLabels
{
    public static string GetLabel(HeroQuality quality)
    {
        QualityCatalogEntry entry = ProgressionCatalog.Current.FindQuality(quality);
        return entry != null && !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name : quality.ToString();
    }

    public static string GetDescription(HeroQuality quality)
    {
        QualityCatalogEntry entry = ProgressionCatalog.Current.FindQuality(quality);
        return entry != null ? entry.Description ?? string.Empty : string.Empty;
    }
}

public static class NarrativeCompetencyLabels
{
    public static string GetLabel(string competencyId)
    {
        CompetencyCatalogEntry entry = ProgressionCatalog.Current.FindCompetency(competencyId);
        if (entry != null && !string.IsNullOrWhiteSpace(entry.Name))
            return entry.Name;
        return competencyId ?? string.Empty;
    }

    public static string GetDescription(string competencyId)
    {
        CompetencyCatalogEntry entry = ProgressionCatalog.Current.FindCompetency(competencyId);
        return entry != null ? entry.Description ?? string.Empty : string.Empty;
    }
}

public static class NarrativeTraitLabels
{
    public static string GetLabel(string traitId)
    {
        TraitCatalogEntry entry = ProgressionCatalog.Current.FindTrait(traitId);
        if (entry != null && !string.IsNullOrWhiteSpace(entry.Name))
            return entry.Name;
        return traitId ?? string.Empty;
    }

    public static string GetDescription(string traitId)
    {
        TraitCatalogEntry entry = ProgressionCatalog.Current.FindTrait(traitId);
        return entry != null ? entry.Description ?? string.Empty : string.Empty;
    }
}

// Человекочитаемые названия именованных значений сложности (§7 инструкции
// по визуализации проверок). Промежуточные значения (напр. 14) названия не
// получают — GetLabel возвращает пустую строку, вызывающий код показывает
// голое число.
public static class NarrativeDifficultyLabels
{
    public static string GetLabel(int difficulty)
    {
        switch (difficulty)
        {
            case NarrativeDifficulty.Obvious: return "Очевидная";
            case NarrativeDifficulty.Simple: return "Простая";
            case NarrativeDifficulty.Ordinary: return "Обычная";
            case NarrativeDifficulty.Demanding: return "Требовательная";
            case NarrativeDifficulty.Hard: return "Сложная";
            case NarrativeDifficulty.VeryHard: return "Очень сложная";
            case NarrativeDifficulty.Exceptional: return "Исключительная";
            case NarrativeDifficulty.Legendary: return "Легендарная";
            default: return string.Empty;
        }
    }

    // "Обычная (13)" для именованных значений, просто "14" для промежуточных.
    public static string Describe(int difficulty)
    {
        string label = GetLabel(difficulty);
        string numberText = difficulty.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(label) ? numberText : label + " (" + numberText + ")";
    }
}
