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
        { NarrativeCompetencyIds.Stealth, "Скрытное движение" },
        { NarrativeCompetencyIds.Hunting, "Охота" },
        { NarrativeCompetencyIds.Craft, "Ремесло" },
        { NarrativeCompetencyIds.Healing, "Лечение" },
        { NarrativeCompetencyIds.Household, "Хозяйство" },
        { NarrativeCompetencyIds.Trade, "Торговое дело" },
        { NarrativeCompetencyIds.Negotiation, "Переговоры" },
        { NarrativeCompetencyIds.CustomAndLaw, "Обычай и право" },
        { NarrativeCompetencyIds.Lore, "Предания" },
        { NarrativeCompetencyIds.Rites, "Обрядовое знание" },
        { NarrativeCompetencyIds.ChoppingWeapons, "Рубящее оружие" },
        { NarrativeCompetencyIds.Shooting, "Стрельба" },
        { NarrativeCompetencyIds.ShieldAndLine, "Щит и строй" },
        { NarrativeCompetencyIds.Herbalism, "Травничество" },
        { NarrativeCompetencyIds.Spearcraft, "Копейное дело" },
        { NarrativeCompetencyIds.Observation, "Наблюдательность" },
        { NarrativeCompetencyIds.Insight, "Проницательность" },
        { NarrativeCompetencyIds.Fishing, "Рыбацкое дело" },
        { NarrativeCompetencyIds.Forestry, "Лесное дело" },
        { NarrativeCompetencyIds.Livestock, "Скотное дело" },
        { NarrativeCompetencyIds.Farming, "Земельное дело" },
        { NarrativeCompetencyIds.Building, "Строительное дело" },
        { NarrativeCompetencyIds.Investigation, "Расследование" }
    };

    // Базовая функция компетенции — таблица канона v1.49 §27.11.
    private static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        { NarrativeCompetencyIds.Fieldcraft, "Читать следы и изменения местности." },
        { NarrativeCompetencyIds.Stealth, "Незаметно двигаться, наблюдать и выбирать подход." },
        { NarrativeCompetencyIds.Hunting, "Понимать зверя и добывать его." },
        { NarrativeCompetencyIds.Craft, "Разобраться, как сделана вещь или механизм, починить или обойти проблему." },
        { NarrativeCompetencyIds.Healing, "Помощь раненым и больным." },
        { NarrativeCompetencyIds.Household, "Практическое знание жизни поселения." },
        { NarrativeCompetencyIds.Trade, "Понимать ценность, дефицит и движение вещей." },
        { NarrativeCompetencyIds.Negotiation, "Добиваться решения через разговор." },
        { NarrativeCompetencyIds.CustomAndLaw, "Понимать человеческие правила, обязательства и принятый порядок." },
        { NarrativeCompetencyIds.Lore, "Работать с устной памятью людей." },
        { NarrativeCompetencyIds.Rites, "Понимать устройство обрядовых действий и табу." },
        { NarrativeCompetencyIds.ChoppingWeapons, "Владение топорами, мечами и близкими по принципу видами оружия." },
        { NarrativeCompetencyIds.Shooting, "Владение дистанционным оружием." },
        { NarrativeCompetencyIds.ShieldAndLine, "Защищаться не только самому, но и как часть группы." },
        { NarrativeCompetencyIds.Herbalism, "Понимать свойства растений, природных веществ и следы их воздействия." },
        { NarrativeCompetencyIds.Spearcraft, "Владение копьями и родственными видами оружия." },
        { NarrativeCompetencyIds.Observation, "Замечать значимые детали в людях, предметах и обстановке." },
        { NarrativeCompetencyIds.Insight, "Понимать скрытый смысл, причины, связи и несоответствия." },
        { NarrativeCompetencyIds.Fishing, "Знать воду как источник промысла: рыбу, снасти, места лова, необычный улов." },
        { NarrativeCompetencyIds.Forestry, "Понимать лес как место труда, ресурсов и опасностей." },
        { NarrativeCompetencyIds.Livestock, "Понимать домашних животных, их состояние, поведение и роль в хозяйстве." },
        { NarrativeCompetencyIds.Farming, "Понимать поля, почву, урожай и последствия изменений земли или воды." },
        { NarrativeCompetencyIds.Building, "Разбираться в больших сооружениях, их повреждениях и восстановлении." },
        { NarrativeCompetencyIds.Investigation, "Связывать отдельные факты в последовательность событий и проверяемую версию." }
    };

    public static string GetLabel(string competencyId)
    {
        if (!string.IsNullOrWhiteSpace(competencyId) && Labels.TryGetValue(competencyId, out string label))
            return label;
        return competencyId ?? string.Empty;
    }

    public static string GetDescription(string competencyId)
    {
        if (!string.IsNullOrWhiteSpace(competencyId) && Descriptions.TryGetValue(competencyId, out string description))
            return description;
        return string.Empty;
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
