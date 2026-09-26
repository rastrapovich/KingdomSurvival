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

// Рабочие числа [РАБОЧЕЕ][KINGDOM SURVIVAL]: канон v1.48 §27.10 оставляет
// кривую XP, ступени компетенций, число вариантов и коэффициенты открытыми
// для настройки при реализации. Утверждены только потолок 100, выбор
// каждые 3 уровня и деление боевого банка 60/40.
public static class CharacterProgression
{
    public const int MaxLevel = 100;
    public const int ChoiceEveryLevels = 3;

    public const int MaxCompetencyRank = 5;
    // «Условная средняя степень», до которой хватает собственной практики.
    public const int PracticeCeiling = 3;

    // Боевой банк: 60% участие / 40% реальный вклад (§27.2).
    public const int ParticipationPercent = 60;

    // Отход из написанного боя — половина банка.
    public const int RetreatBankPercent = 50;

    // Повтор боя против того же состава: банк 100% → 50% → 25% → 10% → 5%.
    private static readonly int[] RepeatBattlePercents = { 100, 50, 25, 10, 5 };

    // Практика за одно содержательное применение и её угасание при повторе.
    public const int PracticePerUse = 2;
    private static readonly int[] RepeatPracticePoints = { 2, 2, 1, 1, 1, 1 };

    // Практика до следующей ступени: 0→1, 1→2, 2→3, 3→4, 4→5.
    private static readonly int[] PracticeToNextRankTable = { 3, 6, 10, 15, 20 };

    // Опыт за впервые исследованное место.
    public const int ExplorationExperience = 30;

    // Крепость тела: +2 HP, не больше трёх раз за жизнь (HP растут очень
    // ограниченно, §27.5).
    public const int ToughnessHitPoints = 2;
    public const int MaxToughnessChoices = 3;

    // Боевые числа растут от владения (§27.5): ступень 3 — +1, ступень 5 — +2.
    public const int CombatBonusFirstRank = 3;
    public const int CombatBonusSecondRank = 5;

    // Опыт до следующего уровня: 100 на первом, +25 за каждый следующий.
    // До 100-го уровня — 131 175 опыта: практически недостижимо в обычном
    // прохождении, но реально при целенаправленной долгой игре.
    public static int ExperienceToNextLevel(int level)
    {
        if (level >= MaxLevel)
            return 0;
        return 100 + 25 * (Math.Max(1, level) - 1);
    }

    public static int TotalExperienceForLevel(int level)
    {
        int clamped = Math.Max(1, Math.Min(MaxLevel, level));
        int steps = clamped - 1;
        return 100 * steps + 25 * steps * (steps - 1) / 2;
    }

    public static int LevelForExperience(int experience)
    {
        int level = 1;
        while (level < MaxLevel && experience >= TotalExperienceForLevel(level + 1))
            level++;
        return level;
    }

    public static int PracticeToNextRank(int rank)
    {
        if (rank < 0 || rank >= PracticeToNextRankTable.Length)
            return 0;
        return PracticeToNextRankTable[rank];
    }

    public static int RepeatBattlePercent(int previousCount)
    {
        int index = Math.Max(0, Math.Min(RepeatBattlePercents.Length - 1, previousCount));
        return RepeatBattlePercents[index];
    }

    // Сотый безопасный удар почти ничему не учит (§27.6).
    public static int RepeatPractice(int previousCount)
    {
        if (previousCount < 0)
            previousCount = 0;
        return previousCount < RepeatPracticePoints.Length ? RepeatPracticePoints[previousCount] : 0;
    }

    // Сколько «стоит» противник в банке боя: здоровье и боевые числа.
    public static int EnemyExperience(int maxHitPoints, int attack, int defense, int damage)
    {
        return Math.Max(0, 5 * maxHitPoints + 10 * (attack + defense + damage));
    }
}
