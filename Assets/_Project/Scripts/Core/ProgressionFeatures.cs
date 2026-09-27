using System;
using System.Collections.Generic;

// Карточка особенности, приёма или приказа — рабочий каталог
// ProjectDocs/FEATURES_REFERENCES_ADAPTATION.md и правила его §0.1
// (решения автора 27.09.2026). Каталог [РАБОЧЕЕ]: названия, условия и числа
// не утверждены. Значения по умолчанию — ProgressionFeatureDefaults; в игре
// каталог приходит из «Базы развития».

// Слой: особенность меняет правило; приём — конкретное действие бойца;
// приказ — команда Командира. Особенность может открывать приём или приказ.
public enum FeatureLayer
{
    Feature,
    Technique,
    Order
}

// Кому доступна: боевые стили — обоим; приказы — только Командиру;
// «личная» — черта конкретного человека (биография, итог истории).
public enum FeatureOwner
{
    Both,
    Commander,
    Fighter,
    Personal
}

public enum FeatureKind
{
    Permanent,      // П — постоянное правило
    Reaction,       // Р — реакция на событие
    Limited,        // Л — «раз за ход / бой / встречу / поход»
    TwoSided,       // Д — выгода и цена
    OpensTechnique, // Пр — открывает приём
    Option,         // О — открывает вариант действия в сцене
    Mastery,        // М — мастерская вершина
    Flaw,           // П (минус) — недостаток биографии
    Action          // приём или приказ
}

// Кандидат — есть в данных, кода нет, в выбор не попадает. Заглушка —
// зарегистрирована в движке, ждёт события, которого ещё нет. Активна —
// работает и может предлагаться.
public enum FeatureStatus
{
    Candidate,
    Stub,
    Active
}

// Класс реализации (пометки чистового каталога): С — системное правило,
// Т — стандартный тег сцены, А — авторский контент, Ф — будущая механика,
// П — действие (приём или приказ).
[Flags]
public enum FeatureImplementation
{
    None = 0,
    System = 1,
    Tag = 2,
    Authored = 4,
    Future = 8,
    Action = 16
}

// Откуда берётся (§0.1: разрешены все источники).
[Flags]
public enum FeatureSource
{
    None = 0,
    LevelChoice = 1,
    Biography = 2,
    Story = 4,
    Trace = 8,
    Teacher = 16,
    Practice = 32,
    PersonalOutcome = 64,
    Presence = 128,
    Basic = 256,
    OpenedByFeature = 512
}

// Предел срабатывания вместо отдельного ресурса (§1 п. 8 каталога).
public enum FeatureLimit
{
    None,
    Turn,
    Round,
    Battle,
    Encounter,
    Scene,
    Conversation,
    Deal,
    Night,
    Expedition,
    UntilCamp
}

public enum FeatureRequirementKind
{
    Competency,
    Quality,
    Feature
}

// Требование: компетенция ≥ ступени, качество ≥ значения или другая
// особенность ≥ ранга (для качества Id — имя HeroQuality).
public sealed class FeatureRequirement
{
    public FeatureRequirementKind Kind;
    public string Id = string.Empty;
    public int Value;

    public FeatureRequirement Clone()
    {
        return new FeatureRequirement { Kind = Kind, Id = Id ?? string.Empty, Value = Value };
    }
}

// Ранг 1–3. Ранг с иным способом действия может носить своё название.
public sealed class FeatureRank
{
    public string Name = string.Empty;
    public string Effect = string.Empty;

    public FeatureRank Clone()
    {
        return new FeatureRank { Name = Name ?? string.Empty, Effect = Effect ?? string.Empty };
    }
}

public sealed class TraitCatalogEntry
{
    public const int MaxRanks = 3;

    public string Id = string.Empty;
    public string Name = string.Empty;

    // Эффект целиком (включая варианты).
    public string Description = string.Empty;

    // Номер в каталоге: Б-01, Н-47, П-05, ПК-01.
    public string Code = string.Empty;
    public FeatureLayer Layer;
    public string Group = string.Empty;
    public FeatureOwner Owner;
    public FeatureKind Kind;
    public bool Combat;
    public FeatureStatus Status;
    public FeatureImplementation Implementation;
    public FeatureSource Sources;
    public FeatureLimit Limit;

    // «Как открывается» словами — полный текст, включая ранговые условия.
    public string UnlockText = string.Empty;
    public readonly List<FeatureRequirement> Requirements = new List<FeatureRequirement>();
    // Достаточно одного требования (в каталоге — «или»).
    public bool RequirementsAnyOf;

    public readonly List<FeatureRank> Ranks = new List<FeatureRank>();

    // Какой ещё не существующей механики ждёт («нужно: …»).
    public string Dependency = string.Empty;
    // На что опирается в текущей игре.
    public string Support = string.Empty;
    // Способ отображения (§0.1).
    public string Display = string.Empty;

    public readonly List<string> ExcludesIds = new List<string>();
    // Приём или приказ, который открывает особенность.
    public readonly List<string> OpensIds = new List<string>();
    public string MergedFrom = string.Empty;
    public string Note = string.Empty;

    public int RankCount => Math.Max(1, Math.Min(MaxRanks, Ranks.Count));

    public string RankName(int rank)
    {
        if (rank < 1 || rank > Ranks.Count)
            return Name;
        string name = Ranks[rank - 1].Name;
        return string.IsNullOrWhiteSpace(name) ? Name + " " + RomanRank(rank) : name;
    }

    public static string RomanRank(int rank)
    {
        switch (rank)
        {
            case 1: return "I";
            case 2: return "II";
            case 3: return "III";
            default: return rank.ToString();
        }
    }

    public TraitCatalogEntry Clone()
    {
        TraitCatalogEntry copy = new TraitCatalogEntry
        {
            Id = Id,
            Name = Name,
            Description = Description,
            Code = Code,
            Layer = Layer,
            Group = Group,
            Owner = Owner,
            Kind = Kind,
            Combat = Combat,
            Status = Status,
            Implementation = Implementation,
            Sources = Sources,
            Limit = Limit,
            UnlockText = UnlockText,
            RequirementsAnyOf = RequirementsAnyOf,
            Dependency = Dependency,
            Support = Support,
            Display = Display,
            MergedFrom = MergedFrom,
            Note = Note
        };
        foreach (FeatureRequirement requirement in Requirements)
            copy.Requirements.Add(requirement.Clone());
        foreach (FeatureRank rank in Ranks)
            copy.Ranks.Add(rank.Clone());
        copy.ExcludesIds.AddRange(ExcludesIds);
        copy.OpensIds.AddRange(OpensIds);
        return copy;
    }
}

public static class FeatureLabels
{
    public static string Layer(FeatureLayer layer)
    {
        switch (layer)
        {
            case FeatureLayer.Technique: return "Приём";
            case FeatureLayer.Order: return "Приказ";
            default: return "Особенность";
        }
    }

    public static string Owner(FeatureOwner owner)
    {
        switch (owner)
        {
            case FeatureOwner.Commander: return "Командир";
            case FeatureOwner.Fighter: return "Боец";
            case FeatureOwner.Personal: return "Личная";
            default: return "Оба";
        }
    }

    public static string OwnerShort(FeatureOwner owner)
    {
        switch (owner)
        {
            case FeatureOwner.Commander: return "К";
            case FeatureOwner.Fighter: return "Б";
            case FeatureOwner.Personal: return "Л";
            default: return "О";
        }
    }

    public static string Kind(FeatureKind kind)
    {
        switch (kind)
        {
            case FeatureKind.Reaction: return "Реакция";
            case FeatureKind.Limited: return "Ограниченная";
            case FeatureKind.TwoSided: return "Двусторонняя";
            case FeatureKind.OpensTechnique: return "Открывает приём";
            case FeatureKind.Option: return "Открывает вариант";
            case FeatureKind.Mastery: return "Мастерская";
            case FeatureKind.Flaw: return "Недостаток";
            case FeatureKind.Action: return "Действие";
            default: return "Постоянная";
        }
    }

    public static string Status(FeatureStatus status)
    {
        switch (status)
        {
            case FeatureStatus.Active: return "Активна";
            case FeatureStatus.Stub: return "Заглушка";
            default: return "Кандидат";
        }
    }

    public static string Implementation(FeatureImplementation implementation)
    {
        List<string> parts = new List<string>();
        if ((implementation & FeatureImplementation.System) != 0) parts.Add("С");
        if ((implementation & FeatureImplementation.Tag) != 0) parts.Add("Т");
        if ((implementation & FeatureImplementation.Authored) != 0) parts.Add("А");
        if ((implementation & FeatureImplementation.Future) != 0) parts.Add("Ф");
        if ((implementation & FeatureImplementation.Action) != 0) parts.Add("П");
        return parts.Count > 0 ? string.Join("/", parts) : "—";
    }

    public static string Limit(FeatureLimit limit)
    {
        switch (limit)
        {
            case FeatureLimit.Turn: return "1/ход";
            case FeatureLimit.Round: return "1/раунд";
            case FeatureLimit.Battle: return "1/бой";
            case FeatureLimit.Encounter: return "1/встречу";
            case FeatureLimit.Scene: return "1/сцену";
            case FeatureLimit.Conversation: return "1/разговор";
            case FeatureLimit.Deal: return "1/сделку";
            case FeatureLimit.Night: return "1/ночёвку";
            case FeatureLimit.Expedition: return "1/поход";
            case FeatureLimit.UntilCamp: return "до лагеря";
            default: return "—";
        }
    }

    public static string Sources(FeatureSource sources)
    {
        List<string> parts = new List<string>();
        if ((sources & FeatureSource.LevelChoice) != 0) parts.Add("выбор уровня");
        if ((sources & FeatureSource.Biography) != 0) parts.Add("биография");
        if ((sources & FeatureSource.Story) != 0) parts.Add("история");
        if ((sources & FeatureSource.Trace) != 0) parts.Add("след пережитого");
        if ((sources & FeatureSource.Teacher) != 0) parts.Add("учитель");
        if ((sources & FeatureSource.Practice) != 0) parts.Add("практика");
        if ((sources & FeatureSource.PersonalOutcome) != 0) parts.Add("итог личной истории");
        if ((sources & FeatureSource.Presence) != 0) parts.Add("присутствие человека");
        if ((sources & FeatureSource.Basic) != 0) parts.Add("базовое");
        if ((sources & FeatureSource.OpenedByFeature) != 0) parts.Add("открывает особенность");
        return parts.Count > 0 ? string.Join(", ", parts) : "—";
    }
}

// Особенности, у которых в игре уже есть код. Только они могут быть
// «Активна» и попадать в выбор развития; окно базы сверяет статус с этим
// списком. Новая реализация добавляет сюда свой ID.
public static class ProgressionFeatureImplementations
{
    private static readonly HashSet<string> Implemented = new HashSet<string>(StringComparer.Ordinal)
    {
        NarrativeTraitIds.KnowsTheWay,
        NarrativeTraitIds.Naturalist,
        "toughness"
    };

    public static bool IsImplemented(string id)
    {
        return !string.IsNullOrEmpty(id) && Implemented.Contains(id);
    }

    public static IEnumerable<string> All => Implemented;

    // Регистрация кода особенности (реализации первой партии и тесты).
    public static void Register(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            Implemented.Add(id);
    }

    public static void Unregister(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            Implemented.Remove(id);
    }
}
