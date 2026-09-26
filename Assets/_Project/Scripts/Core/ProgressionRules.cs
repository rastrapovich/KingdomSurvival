using System;
using System.Collections.Generic;

// Данные прогрессии, которые настраиваются в «Базе развития»
// (Assets/_Project/ProgressionDatabase). Ядро не знает про Unity: база
// собирает ProgressionRules/ProgressionCatalog и назначает их в Current при
// запуске игры. Без базы (EditMode-тесты, редакторские окна) действуют
// значения по умолчанию — ровно те, что описаны в DEVELOPMENT_STATUS.md.
//
// Канон v1.48 §27: общий уровень 1–100, значимый выбор каждые 3 уровня,
// боевой банк 60/40, уровень сам не раздувает характеристики. База может
// отклониться от этих чисел — окно базы показывает такие отклонения.

// Один уровень карты развития типа персонажа.
public sealed class ProgressionLevel
{
    // Опыт от этого уровня до следующего (на 100-м — 0).
    public int ExperienceToNext;

    // На этом уровне человек получает значимый выбор развития.
    public bool Choice;

    // Прибавка к характеристикам при достижении уровня (накопительно).
    public StatModifier Bonus;

    // Сколько опыта даёт победа над существом этого типа и уровня;
    // 0 — считается формулой по его характеристикам.
    public int BattleExperience;

    public string Note = string.Empty;

    public ProgressionLevel Clone()
    {
        return new ProgressionLevel
        {
            ExperienceToNext = ExperienceToNext,
            Choice = Choice,
            Bonus = Bonus,
            BattleExperience = BattleExperience,
            Note = Note ?? string.Empty
        };
    }
}

public sealed class CompetencyRank
{
    public string CompetencyId = string.Empty;
    public int Rank;
}

// Профиль развития: Командир («hero») или тип персонажа из Базы существ
// (guard, archer, forest_beast…) — ключ совпадает с ID типа.
public sealed class ProgressionProfile
{
    public const int LevelCount = 100;

    public string Id = string.Empty;
    public string DisplayName = string.Empty;

    // Человек этого типа копит опыт и растёт (Командир, постоянные бойцы).
    // Существа имеют уровень как силу противника, но опыт не копят.
    public bool Progresses = true;

    // С каким уровнем приходит новый человек этого типа (§27.8).
    public int StartingLevel = 1;

    // Какую компетенцию развивает удар вблизи и издалека.
    public string MeleeCompetencyId = NarrativeCompetencyIds.ChoppingWeapons;
    public string RangedCompetencyId = NarrativeCompetencyIds.Shooting;

    // Что человек этого типа уже умеет, придя в отряд.
    public List<CompetencyRank> StartingCompetencies = new List<CompetencyRank>();

    // Из каких компетенций строятся варианты выбора; пусто — весь каталог
    // для Командира и «каталог бойца» для остальных.
    public List<string> ChoiceCompetencies = new List<string>();

    public ProgressionLevel[] Levels = CreateDefaultLevels();

    public ProgressionLevel GetLevel(int level)
    {
        int index = Math.Max(1, Math.Min(LevelCount, level)) - 1;
        if (Levels == null || index >= Levels.Length || Levels[index] == null)
            return DefaultLevel(index + 1);
        return Levels[index];
    }

    public int ExperienceToNextLevel(int level)
    {
        if (level >= LevelCount)
            return 0;
        return Math.Max(1, GetLevel(level).ExperienceToNext);
    }

    public int TotalExperienceForLevel(int level)
    {
        int clamped = Math.Max(1, Math.Min(LevelCount, level));
        long total = 0;
        for (int current = 1; current < clamped; current++)
            total += ExperienceToNextLevel(current);
        return (int)Math.Min(int.MaxValue, total);
    }

    public int LevelForExperience(int experience)
    {
        int level = 1;
        long total = 0;
        while (level < LevelCount)
        {
            total += ExperienceToNextLevel(level);
            if (experience < total)
                break;
            level++;
        }
        return level;
    }

    public bool IsChoiceLevel(int level)
    {
        return level >= 1 && level <= LevelCount && GetLevel(level).Choice;
    }

    // Сколько уровней выбора в промежутке (fromExclusive, toInclusive].
    public int CountChoiceLevels(int fromExclusive, int toInclusive)
    {
        int count = 0;
        for (int level = Math.Max(1, fromExclusive + 1); level <= Math.Min(LevelCount, toInclusive); level++)
        {
            if (IsChoiceLevel(level))
                count++;
        }
        return count;
    }

    public int NextChoiceLevel(int level)
    {
        for (int next = level + 1; next <= LevelCount; next++)
        {
            if (IsChoiceLevel(next))
                return next;
        }
        return 0;
    }

    // Накопленная прибавка характеристик на уровне (все уровни ≤ level).
    public StatModifier CumulativeBonus(int level)
    {
        StatModifier total = default;
        for (int current = 1; current <= Math.Min(LevelCount, level); current++)
        {
            StatModifier bonus = GetLevel(current).Bonus;
            total.MaxHitPoints += bonus.MaxHitPoints;
            total.Attack += bonus.Attack;
            total.Defense += bonus.Defense;
            total.Damage += bonus.Damage;
            total.Movement += bonus.Movement;
            total.Initiative += bonus.Initiative;
            total.AttackRange += bonus.AttackRange;
        }
        return total;
    }

    // Рабочая кривая по умолчанию: 100 опыта на 1-м уровне, +25 за каждый
    // следующий; выбор каждые 3 уровня; прибавок нет.
    public static ProgressionLevel DefaultLevel(int level)
    {
        return new ProgressionLevel
        {
            ExperienceToNext = level >= LevelCount ? 0 : 100 + 25 * (level - 1),
            Choice = level % 3 == 0
        };
    }

    public static ProgressionLevel[] CreateDefaultLevels()
    {
        ProgressionLevel[] levels = new ProgressionLevel[LevelCount];
        for (int i = 0; i < LevelCount; i++)
            levels[i] = DefaultLevel(i + 1);
        return levels;
    }
}

public sealed class ProgressionRules
{
    public const string HeroProfileId = "hero";

    private static ProgressionRules current;

    public static ProgressionRules Current
    {
        get => current ?? (current = CreateDefault());
        set => current = value;
    }

    // Боевой банк (§27.2): доля участия, остальное — реальный вклад.
    public int ParticipationPercent = 60;
    public int RetreatBankPercent = 50;
    // Повтор боя против того же состава: 1-й, 2-й, 3-й… раз.
    public int[] RepeatBattlePercents = { 100, 50, 25, 10, 5 };

    // «Цена» противника по характеристикам, если в карте уровня не задана.
    public int EnemyHitPointWeight = 5;
    public int EnemyStatWeight = 10;

    // Практика (§27.6).
    public int PracticePerUse = 2;
    public int[] RepeatPracticePoints = { 2, 2, 1, 1, 1, 1 };
    public int[] PracticeToNextRank = { 3, 6, 10, 15, 20 };
    public int PracticeCeiling = 3;

    // Владение оружием/щитом → +1 и +2 к атаке/защите.
    public int CombatBonusFirstRank = 3;
    public int CombatBonusSecondRank = 5;

    // Источники общего опыта вне боя.
    public int ExplorationExperience = 30;
    public int EncounterReactionExperience;
    public int EncounterMicroExperience;
    public int EncounterShortExperience = 20;
    public int EncounterStandardExperience = 40;
    public int EncounterComplexExperience = 80;
    public int EncounterQuestSeedExperience = 30;

    // Нейтральный вариант выбора «Крепость тела».
    public int ToughnessHitPoints = 2;
    public int MaxToughnessChoices = 3;

    private readonly Dictionary<string, ProgressionProfile> profiles = new Dictionary<string, ProgressionProfile>(StringComparer.Ordinal);

    public IEnumerable<ProgressionProfile> Profiles => profiles.Values;

    public void SetProfile(ProgressionProfile profile)
    {
        if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
            throw new ArgumentException("У профиля развития должен быть ID.", nameof(profile));
        profiles[profile.Id] = profile;
    }

    public bool HasProfile(string id)
    {
        return !string.IsNullOrEmpty(id) && profiles.ContainsKey(id);
    }

    // Профиль типа; для неизвестного типа — профиль по умолчанию (создаётся
    // один раз и дальше тот же).
    public ProgressionProfile GetProfile(string id)
    {
        string key = string.IsNullOrWhiteSpace(id) ? "default" : id;
        if (profiles.TryGetValue(key, out ProgressionProfile profile))
            return profile;
        profile = CreateDefaultProfile(key);
        profiles[key] = profile;
        return profile;
    }

    public static ProgressionRules CreateDefault()
    {
        ProgressionRules rules = new ProgressionRules();
        rules.SetProfile(CreateDefaultProfile(HeroProfileId));
        foreach (string id in new[] { "guard", "archer", "healer", "spearman", "scout", "militia" })
            rules.SetProfile(CreateDefaultProfile(id));
        return rules;
    }

    // Профиль по умолчанию: рабочая кривая и стартовое владение по роли
    // (боец умеет то, что требует его роль, — ступень 1, чисел не меняет).
    public static ProgressionProfile CreateDefaultProfile(string id)
    {
        ProgressionProfile profile = new ProgressionProfile { Id = id ?? string.Empty };
        switch (id)
        {
            case HeroProfileId:
                profile.DisplayName = "Командир";
                break;
            case "guard":
                Add(profile, NarrativeCompetencyIds.ShieldAndLine);
                Add(profile, NarrativeCompetencyIds.ChoppingWeapons);
                break;
            case "archer":
                Add(profile, NarrativeCompetencyIds.Shooting);
                break;
            case "healer":
                Add(profile, NarrativeCompetencyIds.Healing);
                break;
            case "spearman":
                profile.MeleeCompetencyId = NarrativeCompetencyIds.Spearcraft;
                Add(profile, NarrativeCompetencyIds.Spearcraft);
                break;
            case "scout":
                Add(profile, NarrativeCompetencyIds.Fieldcraft);
                Add(profile, NarrativeCompetencyIds.Stealth);
                break;
            default:
                Add(profile, NarrativeCompetencyIds.ChoppingWeapons);
                break;
        }
        return profile;
    }

    private static void Add(ProgressionProfile profile, string competencyId)
    {
        profile.StartingCompetencies.Add(new CompetencyRank { CompetencyId = competencyId, Rank = 1 });
    }

    public int PracticeToNext(int rank)
    {
        return PracticeToNextRank != null && rank >= 0 && rank < PracticeToNextRank.Length ? PracticeToNextRank[rank] : 0;
    }
}

// Перечни качеств, особенностей и компетенций с названиями и описаниями.
public sealed class QualityCatalogEntry
{
    public HeroQuality Quality;
    public string Name = string.Empty;
    public string Description = string.Empty;
}

public sealed class TraitCatalogEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
}

public sealed class CompetencyCatalogEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    // Входит в каталог постоянного бойца (бой и поход, §25.5).
    public bool FighterCatalog;
}

public sealed class ProgressionCatalog
{
    private static ProgressionCatalog current;

    public static ProgressionCatalog Current
    {
        get => current ?? (current = CreateDefault());
        set => current = value;
    }

    public readonly List<QualityCatalogEntry> Qualities = new List<QualityCatalogEntry>();
    public readonly List<TraitCatalogEntry> Traits = new List<TraitCatalogEntry>();
    public readonly List<CompetencyCatalogEntry> Competencies = new List<CompetencyCatalogEntry>();

    public QualityCatalogEntry FindQuality(HeroQuality quality)
    {
        foreach (QualityCatalogEntry entry in Qualities)
        {
            if (entry != null && entry.Quality == quality)
                return entry;
        }
        return null;
    }

    public TraitCatalogEntry FindTrait(string id)
    {
        foreach (TraitCatalogEntry entry in Traits)
        {
            if (entry != null && entry.Id == id)
                return entry;
        }
        return null;
    }

    public CompetencyCatalogEntry FindCompetency(string id)
    {
        foreach (CompetencyCatalogEntry entry in Competencies)
        {
            if (entry != null && entry.Id == id)
                return entry;
        }
        return null;
    }

    public static ProgressionCatalog CreateDefault()
    {
        ProgressionCatalog catalog = new ProgressionCatalog();
        AddQuality(catalog, HeroQuality.Strength, "Сила", "Физическое воздействие.");
        AddQuality(catalog, HeroQuality.Dexterity, "Сноровка", "Координация, точность и скорость.");
        AddQuality(catalog, HeroQuality.Fortitude, "Стойкость", "Здоровье и физические лишения.");
        AddQuality(catalog, HeroQuality.Instinct, "Чутьё", "Наблюдение, следы и опасность.");
        AddQuality(catalog, HeroQuality.Judgment, "Суждение", "Анализ, планирование и интерпретация.");
        AddQuality(catalog, HeroQuality.Character, "Характер", "Сила личности, влияние и сопротивление давлению.");

        catalog.Traits.Add(new TraitCatalogEntry
        {
            Id = NarrativeTraitIds.KnowsTheWay,
            Name = "Знающий дорогу",
            Description = "При успешном обнаружении дорожный Encounter начинается в подготовленном состоянии: герой замечает событие раньше, может наблюдать, обойти или занять выгодную позицию."
        });
        catalog.Traits.Add(new TraitCatalogEntry
        {
            Id = NarrativeTraitIds.Naturalist,
            Name = "Натуралист",
            Description = "Открывает авторские блоки и варианты, связанные с растениями, животными, погодой, болезнями, водой и природными изменениями."
        });

        // Первый базовый каталог компетенций — канон v1.49 §27.11.
        AddCompetency(catalog, NarrativeCompetencyIds.Fieldcraft, "Следопытство", "Читать следы и изменения местности.", true);
        AddCompetency(catalog, NarrativeCompetencyIds.Stealth, "Скрытное движение", "Незаметно двигаться, наблюдать и выбирать подход.", true);
        AddCompetency(catalog, NarrativeCompetencyIds.Hunting, "Охота", "Понимать зверя и добывать его.", true);
        AddCompetency(catalog, NarrativeCompetencyIds.Craft, "Ремесло", "Разобраться, как сделана вещь или механизм, починить или обойти проблему.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Healing, "Лечение", "Помощь раненым и больным.", true);
        AddCompetency(catalog, NarrativeCompetencyIds.Household, "Хозяйство", "Практическое знание жизни поселения.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Trade, "Торговое дело", "Понимать ценность, дефицит и движение вещей.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Negotiation, "Переговоры", "Добиваться решения через разговор.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.CustomAndLaw, "Обычай и право", "Понимать человеческие правила, обязательства и принятый порядок.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Lore, "Предания", "Работать с устной памятью людей.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Rites, "Обрядовое знание", "Понимать устройство обрядовых действий и табу.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.ChoppingWeapons, "Рубящее оружие", "Владение топорами, мечами и близкими по принципу видами оружия.", true);
        AddCompetency(catalog, NarrativeCompetencyIds.Shooting, "Стрельба", "Владение дистанционным оружием.", true);
        AddCompetency(catalog, NarrativeCompetencyIds.ShieldAndLine, "Щит и строй", "Защищаться не только самому, но и как часть группы.", true);
        AddCompetency(catalog, NarrativeCompetencyIds.Herbalism, "Травничество", "Понимать свойства растений, природных веществ и следы их воздействия.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Spearcraft, "Копейное дело", "Владение копьями и родственными видами оружия.", true);
        AddCompetency(catalog, NarrativeCompetencyIds.Observation, "Наблюдательность", "Замечать значимые детали в людях, предметах и обстановке.", true);
        AddCompetency(catalog, NarrativeCompetencyIds.Insight, "Проницательность", "Понимать скрытый смысл, причины, связи и несоответствия.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Fishing, "Рыбацкое дело", "Знать воду как источник промысла: рыбу, снасти, места лова, необычный улов.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Forestry, "Лесное дело", "Понимать лес как место труда, ресурсов и опасностей.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Livestock, "Скотное дело", "Понимать домашних животных, их состояние, поведение и роль в хозяйстве.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Farming, "Земельное дело", "Понимать поля, почву, урожай и последствия изменений земли или воды.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Building, "Строительное дело", "Разбираться в больших сооружениях, их повреждениях и восстановлении.", false);
        AddCompetency(catalog, NarrativeCompetencyIds.Investigation, "Расследование", "Связывать отдельные факты в последовательность событий и проверяемую версию.", false);
        return catalog;
    }

    private static void AddQuality(ProgressionCatalog catalog, HeroQuality quality, string name, string description)
    {
        catalog.Qualities.Add(new QualityCatalogEntry { Quality = quality, Name = name, Description = description });
    }

    private static void AddCompetency(ProgressionCatalog catalog, string id, string name, string description, bool fighter)
    {
        catalog.Competencies.Add(new CompetencyCatalogEntry { Id = id, Name = name, Description = description, FighterCatalog = fighter });
    }
}
