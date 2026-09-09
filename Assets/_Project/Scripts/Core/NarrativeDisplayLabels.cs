using System.Collections.Generic;
using System.Globalization;

// Русские подписи качеств/компетенций для механической строки диалога и
// будущего экрана героя (§14, §17). Чисто отображение — не влияет на расчёт.
public static class NarrativeQualityLabels
{
    public static string GetLabel(HeroQuality quality)
    {
        switch (quality)
        {
            case HeroQuality.Strength: return "Сила";
            case HeroQuality.Dexterity: return "Сноровка";
            case HeroQuality.Fortitude: return "Стойкость";
            case HeroQuality.Instinct: return "Чутьё";
            case HeroQuality.Judgment: return "Суждение";
            case HeroQuality.Character: return "Характер";
            default: return quality.ToString();
        }
    }
}

public static class NarrativeCompetencyLabels
{
    private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
    {
        { NarrativeCompetencyIds.Fieldcraft, "Следопытство" },
        { NarrativeCompetencyIds.Craft, "Ремесло" }
    };

    public static string GetLabel(string competencyId)
    {
        if (!string.IsNullOrWhiteSpace(competencyId) && Labels.TryGetValue(competencyId, out string label))
            return label;
        return competencyId ?? string.Empty;
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
