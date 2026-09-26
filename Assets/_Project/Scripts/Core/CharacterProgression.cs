using System;
using System.Collections.Generic;

// Канон v1.48 §27 (с §§25.4–25.5, 26.1 и каталогом v1.49 §27.11): единая
// прогрессия Командира и постоянных бойцов. Общий опыт (уровень 1–100)
// отмечает уникально пережитое; практика — реальные применения конкретной
// компетенции. Это разные прогрессии. Каждые 3 уровня — значимый выбор.
//
// Каталог особенностей (перков) в каноне открыт, поэтому выбор пока
// составляется из того, что уже работает: углубить практикуемую
// компетенцию (персональный вариант), начать новое дело и крепость тела
// (нейтральные варианты).

[Serializable]
public sealed class CompetencyProgressData
{
    public string CompetencyId = string.Empty;

    // Ступень 0–5 бойца. Ступень Командира живёт в HeroProfile (её читают
    // проверки), здесь у него — только практика и потолок.
    public int Rank;

    // Практика к следующей ступени; обнуляется при росте.
    public int Practice;

    // Практика с последнего значимого выбора — из неё растут персональные
    // варианты развития (§27.1.1).
    public int PracticeSinceChoice;

    // До этой ступени компетенция растёт собственной практикой; выше —
    // только после наставника или нового знания (§27.6).
    public int Ceiling = CharacterProgression.PracticeCeiling;
}

[Serializable]
public sealed class PersonProgressionData
{
    public string PersonId = string.Empty;
    public int Level = 1;

    // С каким уровнем человек пришёл (§27.8): выборы до него — часть
    // биографии, а не долг перед игроком.
    public int StartingLevel = 1;

    // Весь накопленный общий опыт.
    public int Experience;

    public int ChoicesTaken;
    public int ToughnessChoices;
    public List<string> ChosenOptionIds = new List<string>();
    public List<CompetencyProgressData> Competencies = new List<CompetencyProgressData>();

    public CompetencyProgressData FindCompetency(string competencyId)
    {
        if (Competencies == null)
            return null;
        foreach (CompetencyProgressData entry in Competencies)
        {
            if (entry != null && entry.CompetencyId == competencyId)
                return entry;
        }
        return null;
    }

    public CompetencyProgressData GetOrCreateCompetency(string competencyId)
    {
        CompetencyProgressData entry = FindCompetency(competencyId);
        if (entry != null)
            return entry;
        if (Competencies == null)
            Competencies = new List<CompetencyProgressData>();
        entry = new CompetencyProgressData { CompetencyId = competencyId };
        Competencies.Add(entry);
        return entry;
    }
}

// Счётчик повторов одного и того же содержания (одинаковый состав врагов)
// — для антифарма §27.4.
[Serializable]
public sealed class ProgressionRepeatData
{
    public string Key = string.Empty;
    public int Count;
}

[Serializable]
public sealed class ProgressionStateData
{
    public List<PersonProgressionData> People = new List<PersonProgressionData>();

    // Применённые источники опыта «источник|человек»: сохранение, загрузка
    // и повтор текста не дублируют опыт (§25.4).
    public List<string> AppliedSources = new List<string>();

    public List<ProgressionRepeatData> Repeats = new List<ProgressionRepeatData>();
}

public sealed class ExperienceGain
{
    public string PersonId;
    public string DisplayName;
    public int Amount;
    public int OldLevel;
    public int NewLevel;
    public bool NewChoiceAvailable;
}

public sealed class PracticeGain
{
    public string PersonId;
    public string DisplayName;
    public string CompetencyId;
    public int Points;
    public int OldRank;
    public int NewRank;
    public bool ReachedCeiling;
}

public enum DevelopmentOptionKind
{
    // Персональный: углубить компетенцию, которую человек реально применял.
    Deepen,
    // Нейтральный: начать осваивать новое дело.
    Learn,
    // Нейтральный: крепость тела, немного здоровья (строго ограничено).
    Toughness
}

public sealed class DevelopmentOption
{
    public string Id;
    public DevelopmentOptionKind Kind;
    public string CompetencyId;
    public string Title;
    public string Description;
    public bool IsPersonal;
}

// Числа прогрессии берутся из действующих правил «Базы развития»
// (ProgressionRules.Current; без базы — рабочие значения по умолчанию
// [РАБОЧЕЕ]). Канон v1.48 §27.10 оставляет кривую XP, ступени компетенций,
// число вариантов и коэффициенты открытыми для настройки; утверждены потолок
// 100, выбор каждые 3 уровня и деление боевого банка 60/40.
public static class CharacterProgression
{
    public const int MaxLevel = ProgressionProfile.LevelCount;
    public const int MaxCompetencyRank = 5;

    private static ProgressionRules Rules => ProgressionRules.Current;

    public static int ParticipationPercent => Rules.ParticipationPercent;
    public static int RetreatBankPercent => Rules.RetreatBankPercent;
    public static int PracticePerUse => Rules.PracticePerUse;
    public static int PracticeCeiling => Math.Max(1, Math.Min(MaxCompetencyRank, Rules.PracticeCeiling));
    public static int ExplorationExperience => Rules.ExplorationExperience;
    public static int ToughnessHitPoints => Rules.ToughnessHitPoints;
    public static int MaxToughnessChoices => Rules.MaxToughnessChoices;
    public static int CombatBonusFirstRank => Rules.CombatBonusFirstRank;
    public static int CombatBonusSecondRank => Rules.CombatBonusSecondRank;

    // Кривая Командира (профиль «hero»). У каждого типа персонажа — своя
    // карта развития: ProgressionRules.Current.GetProfile(id).
    public static int ExperienceToNextLevel(int level)
    {
        return Rules.GetProfile(ProgressionRules.HeroProfileId).ExperienceToNextLevel(level);
    }

    public static int TotalExperienceForLevel(int level)
    {
        return Rules.GetProfile(ProgressionRules.HeroProfileId).TotalExperienceForLevel(level);
    }

    public static int LevelForExperience(int experience)
    {
        return Rules.GetProfile(ProgressionRules.HeroProfileId).LevelForExperience(experience);
    }

    public static int PracticeToNextRank(int rank)
    {
        return Rules.PracticeToNext(rank);
    }

    public static int RepeatBattlePercent(int previousCount)
    {
        int[] percents = Rules.RepeatBattlePercents;
        if (percents == null || percents.Length == 0)
            return 100;
        int index = Math.Max(0, Math.Min(percents.Length - 1, previousCount));
        return percents[index];
    }

    // Сотый безопасный удар почти ничему не учит (§27.6).
    public static int RepeatPractice(int previousCount)
    {
        int[] points = Rules.RepeatPracticePoints;
        if (previousCount < 0)
            previousCount = 0;
        return points != null && previousCount < points.Length ? points[previousCount] : 0;
    }

    // «Цена» противника по характеристикам, если карта уровня её не задаёт.
    public static int EnemyExperience(int maxHitPoints, int attack, int defense, int damage)
    {
        return Math.Max(0, Rules.EnemyHitPointWeight * maxHitPoints + Rules.EnemyStatWeight * (attack + defense + damage));
    }
}
