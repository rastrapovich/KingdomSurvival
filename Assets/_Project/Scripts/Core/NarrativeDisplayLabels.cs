using System.Collections.Generic;

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
        { NarrativeCompetencyIds.Fieldcraft, "Следопытство" }
    };

    public static string GetLabel(string competencyId)
    {
        if (!string.IsNullOrWhiteSpace(competencyId) && Labels.TryGetValue(competencyId, out string label))
            return label;
        return competencyId ?? string.Empty;
    }
}
