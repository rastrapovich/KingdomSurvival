using System;
using System.Collections.Generic;
using System.Linq;

// Бриф «Живое население глобальной карты» (19.11.2026), [РАБОЧЕЕ — ПРОТОТИП].
// Определения слоя населения: пресеты групп (состав по UnitTypeId Базы
// существ, биотоп, поведение, радиусы) и общие настройки (бюджеты, отступы,
// интервалы, резервы). Это данные генератора карты (WorldMapGenerationSettings
// .Population) — правятся в окне «Генератор карты», хранятся в его ассете.
// Составы, биотопы и числа — рабочие настройки прототипа, не канон и не
// факты мира; UnitTypeId не задаёт ни мораль, ни профессию, ни сторону.

// Поведение группы на карте (строки — устойчивые ID данных).
public static class WorldPopulationBehavior
{
    // Мирное движение: обоз, странствующий мастер.
    public const string Peaceful = "peaceful";
    // Дозор: обходит свой отрезок дороги, вытесняет банду.
    public const string Patrol = "patrol";
    // Банда: ищет слабую цель у своей дороги, избегает сильного дозора.
    public const string Bandit = "bandit";
    // Хищник (стая): наблюдает; нападает только на слабый отряд.
    public const string Predator = "predator";
    // Территориальный зверь/существо: предупреждает, нападает при сближении.
    public const string Territorial = "territorial";
    // Скрытный зверь: уходит от отряда; сам не нападает.
    public const string Shy = "shy";

    public static bool IsHostileCapable(string behavior) =>
        behavior == Bandit || behavior == Predator || behavior == Territorial;

    public static readonly string[] All = { Peaceful, Patrol, Bandit, Predator, Territorial, Shy };
}

// Рабочие биотопы — контекст поверх уже созданной географии карты.
public static class WorldPopulationBiome
{
    // Человеческие группы на дорогах (маршруты групп мира).
    public const string Road = "road";
    // Лес у края: лесная клетка рядом с открытой проходимой землёй.
    public const string ForestEdge = "forest-edge";
    // Подлесок: опушка или открытая земля у леса.
    public const string Underbrush = "underbrush";
    // Глубина леса: не меньше трёх клеток леса до опушки.
    public const string DeepForest = "deep-forest";
    // Берег: проходимая суша у воды.
    public const string Shore = "shore";
    // Топь: болото рядом с водой.
    public const string Swamp = "swamp";
    // Каменистая доступная суша у скал и гор (не сама скала).
    public const string Rocky = "rocky";
    // У заброшенного/старого места без собственных обитателей.
    public const string Ruins = "ruins";

    public static readonly string[] All = { Road, ForestEdge, Underbrush, DeepForest, Shore, Swamp, Rocky, Ruins };
}

[Serializable]
public sealed class WorldPopulationMember
{
    public string UnitTypeId = string.Empty;
    public int Min = 1;
    public int Max = 1;

    public WorldPopulationMember Clone() => (WorldPopulationMember)MemberwiseClone();
}

// Пресет группы: кто, где, как себя ведёт. Роль — роль группы мира
// (WorldMapGroupRule.*) для скорости и показа.
[Serializable]
public sealed class WorldPopulationPreset
{
    // Класс бюджета: мирное движение, обычная угроза, сильная угроза,
    // необычный обитатель (существо каталога).
    public const string Peaceful = "peaceful";
    public const string Ordinary = "ordinary";
    public const string Strong = "strong";
    public const string Creature = "creature";

    public string Id = string.Empty;
    public string Name = string.Empty;
    public bool Enabled = true;
    public string Role = WorldMapGroupRule.Beast;
    public string Behavior = WorldPopulationBehavior.Territorial;
    public string Budget = Ordinary;
    public string Biome = WorldPopulationBiome.ForestEdge;
    public List<WorldPopulationMember> Members = new List<WorldPopulationMember>();
    public int Weight = 1;
    public int MaxPerMap = 2;
    // Радиусы, клетки общей gameplay-сетки.
    public float TerritoryRadiusHexes = 2.5f;
    public float DetectHexes = 3f;
    public float WarnHexes = 1.8f;
    public float ChaseHexes = 3f;
    // Стоянка на точке распорядка, часов.
    public float RestHours = 4f;
    // Сколько новых групп источник может дать после гибели первой (0 — не
    // восстанавливается).
    public int Reserve = 1;
    // Отряд может откупиться припасами вместо боя (хищник).
    public int AppeaseSupply;
    // Отход при контакте стоит припасов (опасный территориальный зверь).
    public int RetreatSupplyCost;
    // Набор шести ракурсов фигуры MapLab; пусто — рисунок первого участника
    // из Базы существ (одиночный спрайт без выдуманных ракурсов).
    public string FigureSet = string.Empty;
    // Короткий прототипный контекст: признаки, предупреждение, нападение.
    public string Signs = string.Empty;
    public string WarningText = string.Empty;
    public string AttackText = string.Empty;
    public string Description = string.Empty;
    public string Status = "[РАБОЧЕЕ — ПРОТОТИП]";

    public WorldPopulationPreset Clone()
    {
        WorldPopulationPreset copy = (WorldPopulationPreset)MemberwiseClone();
        copy.Members = (Members ?? new List<WorldPopulationMember>()).Where(m => m != null).Select(m => m.Clone()).ToList();
        return copy;
    }

    public bool IsHostileCapable => WorldPopulationBehavior.IsHostileCapable(Behavior);
}

// Общие настройки слоя. Дистанции — клетки gameplay-сетки, время — игровые
// часы (одна мера на мир, без процентов холста и пикселей экрана).
[Serializable]
public sealed class WorldPopulationSettings
{
    public bool Enabled = true;
    // Бюджеты: верхние ориентиры, а не требование заполнить все места.
    public int OrdinaryThreatBudget = 6;
    public int StrongThreatBudget = 1;
    public int CreatureBudget = 3;
    // Обычных угроз — по одной на столько клеток пригодной суши.
    public int LandHexesPerOrdinaryThreat = 900;
    public int LandHexesPerCreature = 1600;
    // Множитель враждебной составляющей (настройка «Враги» новой партии);
    // мирное движение он не трогает и статы не усиливает.
    public float ThreatScale = 1f;
    // Отступы: старт и входы услуг (деревни) — свободны от обычных угроз.
    public int StartSafeRadiusHexes = 3;
    public int ServiceSafeRadiusHexes = 2;
    public int MinSourceDistanceHexes = 6;
    // Сильная угроза — не ближе этого к старту (не в обязательном начале).
    public int StrongMinStartDistanceHexes = 18;
    // Контакт и признаки.
    public float ContactHexes = 0.6f;
    public float TraceHexes = 4f;
    public float CampApproachHexes = 0.9f;
    public float TalkHexes = 2f;
    // Ритм.
    public float RespiteHours = 2f;
    public float RepeatContactHours = 6f;
    public float ChaseMaxHours = 2.5f;
    public float ConflictWindowHours = 1.5f;
    public float PredatorObserveHours = 0.5f;
    // Восстановление источников.
    public int BandReserve = 2;
    public float BandRefillHours = 72f;
    public float BeastRefillHours = 144f;
    public float ReplacementHours = 48f;
    // Столкновения групп мира без героя.
    public bool NpcConflicts = true;
    // Плата банде: доля серебра отряда, не меньше минимума.
    public int DemandMinimum = 5;
    public float DemandShare = 0.2f;
    public List<WorldPopulationPreset> Presets = WorldPopulationCatalog.DefaultPresets();

    // Временные необязательные встречи (просьба пользователя 19.11.2026).
    public bool TemporaryEnabled = true;
    // Как часто (игровые часы) проверяется появление и с какой вероятностью.
    public float TemporaryCheckHours = 2f;
    public float TemporaryChance = 0.3f;
    public int TemporaryMaxActive = 2;
    // Срок занятия, часов.
    public float TemporaryMinHours = 4f;
    public float TemporaryMaxHours = 9f;
    // Появление — вне обзора отряда: дальше радиуса обзора на столько клеток.
    public float TemporarySpawnMinExtraHexes = 1.5f;
    public float TemporarySpawnMaxExtraHexes = 5f;
    // Тот же вид встречи — не чаще; зачищенный участок — не наполняется.
    public float TemporaryRepeatKindHours = 12f;
    public float TemporaryClearedHours = 48f;
    public float TemporaryClearedHexes = 8f;
    // Враждебных групп рядом (10 клеток) не больше — общий бюджет угроз.
    public int TemporaryNearbyThreatLimit = 2;
    // «Недавняя находка» — исследованное место не старше, часов.
    public float TemporaryFindHours = 48f;
    public List<WorldTemporaryEncounterKind> TemporaryKinds = WorldPopulationCatalog.DefaultTemporaryKinds();

    public static WorldPopulationSettings CreateDefault() => new WorldPopulationSettings();

    public WorldPopulationSettings Clone()
    {
        WorldPopulationSettings copy = (WorldPopulationSettings)MemberwiseClone();
        copy.Presets = (Presets ?? new List<WorldPopulationPreset>()).Where(p => p != null).Select(p => p.Clone()).ToList();
        copy.TemporaryKinds = (TemporaryKinds ?? new List<WorldTemporaryEncounterKind>()).Where(k => k != null).Select(k => k.Clone()).ToList();
        return copy;
    }

    public void Sanitize()
    {
        Presets ??= WorldPopulationCatalog.DefaultPresets();
        Presets.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Id));
        foreach (WorldPopulationPreset p in Presets)
        {
            p.Members ??= new List<WorldPopulationMember>();
            p.Members.RemoveAll(m => m == null || string.IsNullOrWhiteSpace(m.UnitTypeId));
            foreach (WorldPopulationMember m in p.Members)
            {
                m.Min = Math.Max(0, m.Min);
                m.Max = Math.Max(m.Min, m.Max);
            }
            p.Weight = Math.Max(0, p.Weight);
            p.MaxPerMap = Math.Max(0, p.MaxPerMap);
            p.TerritoryRadiusHexes = Math.Max(1f, p.TerritoryRadiusHexes);
            p.DetectHexes = Math.Max(0.5f, p.DetectHexes);
            p.WarnHexes = Math.Max(ContactHexes, p.WarnHexes);
            p.ChaseHexes = Math.Max(0f, p.ChaseHexes);
            p.RestHours = Math.Max(0f, p.RestHours);
            p.Reserve = Math.Max(0, p.Reserve);
        }
        OrdinaryThreatBudget = Math.Max(0, OrdinaryThreatBudget);
        StrongThreatBudget = Math.Max(0, StrongThreatBudget);
        CreatureBudget = Math.Max(0, CreatureBudget);
        LandHexesPerOrdinaryThreat = Math.Max(50, LandHexesPerOrdinaryThreat);
        LandHexesPerCreature = Math.Max(50, LandHexesPerCreature);
        ThreatScale = Math.Max(0f, ThreatScale);
        ContactHexes = Math.Max(0.2f, ContactHexes);
        StartSafeRadiusHexes = Math.Max(0, StartSafeRadiusHexes);
        ServiceSafeRadiusHexes = Math.Max(0, ServiceSafeRadiusHexes);
        MinSourceDistanceHexes = Math.Max(0, MinSourceDistanceHexes);
        ChaseMaxHours = Math.Max(0.25f, ChaseMaxHours);
        TemporaryKinds ??= WorldPopulationCatalog.DefaultTemporaryKinds();
        TemporaryKinds.RemoveAll(k => k == null || string.IsNullOrWhiteSpace(k.Id));
        TemporaryCheckHours = Math.Max(0.25f, TemporaryCheckHours);
        TemporaryChance = Math.Max(0f, Math.Min(1f, TemporaryChance));
        TemporaryMaxActive = Math.Max(0, TemporaryMaxActive);
        TemporaryMinHours = Math.Max(0.5f, TemporaryMinHours);
        TemporaryMaxHours = Math.Max(TemporaryMinHours, TemporaryMaxHours);
        TemporarySpawnMinExtraHexes = Math.Max(0.5f, TemporarySpawnMinExtraHexes);
        TemporarySpawnMaxExtraHexes = Math.Max(TemporarySpawnMinExtraHexes + 0.5f, TemporarySpawnMaxExtraHexes);
        ConflictWindowHours = Math.Max(0.1f, ConflictWindowHours);
        BandReserve = Math.Max(0, BandReserve);
    }

    public WorldPopulationPreset Find(string id) => Presets?.FirstOrDefault(p => p != null && p.Id == id);
}

// Проверка существа Базы существ без ссылки ядра на модуль UnitDatabase:
// Unity-слой назначает поставщика (UnitDatabaseStatsProvider). Без него
// проверка пропускается и это отмечается в отчёте генерации.
public interface IUnitCatalogAvailability
{
    // false — существо не годится для новых групп (нет записи, исключено
    // из новых встреч, нет рисунка); reason — причина для отчёта.
    bool IsAvailableForNewGroups(string unitTypeId, out string reason);
}

// Вид временной встречи. Поведение и состав берутся из пресета BasePresetId;
// Kind задаёт занятие (как появляется и что делает до ухода).
[Serializable]
public sealed class WorldTemporaryEncounterKind
{
    // Проходит через округу: входит с одного края обзора, уходит за другой.
    public const string Passing = "passing";
    // Охотится: петляет вокруг места появления.
    public const string Hunting = "hunting";
    // Стоит у дороги: держится места появления.
    public const string Roadside = "roadside";
    // Чужая стычка: две группы уже в схватке.
    public const string Skirmish = "skirmish";
    // Существа у недавней находки отряда.
    public const string AtFind = "at-find";

    public string Id = string.Empty;
    public string Name = string.Empty;
    public bool Enabled = true;
    public string Kind = Passing;
    public string BasePresetId = string.Empty;
    // Для стычки — вторая сторона.
    public string SecondPresetId = string.Empty;
    public string SecondName = string.Empty;
    public int Weight = 1;
    // Множитель веса ночью (20:00–05:00); 0 — только днём.
    public float NightWeight = 1f;
    // Допустимая местность места появления (пусто — любая проходимая суша).
    public List<WorldMapGameplayTerrainType> Terrains = new List<WorldMapGameplayTerrainType>();
    // Причина присутствия (подпись наведения) и признак при появлении рядом.
    public string Reason = string.Empty;
    public string Hint = string.Empty;
    public string Status = "[РАБОЧЕЕ — ПРОТОТИП]";

    public WorldTemporaryEncounterKind Clone()
    {
        WorldTemporaryEncounterKind copy = (WorldTemporaryEncounterKind)MemberwiseClone();
        copy.Terrains = new List<WorldMapGameplayTerrainType>(Terrains ?? new List<WorldMapGameplayTerrainType>());
        return copy;
    }
}

public static class WorldPopulationCatalog
{
    public static IUnitCatalogAvailability Availability { get; set; }

    public static List<WorldTemporaryEncounterKind> DefaultTemporaryKinds()
    {
        List<WorldMapGameplayTerrainType> T(params WorldMapGameplayTerrainType[] t) => t.ToList();
        return new List<WorldTemporaryEncounterKind>
        {
            new WorldTemporaryEncounterKind
            {
                Id = "temp-passing-band", Name = "Проходящая ватага", Kind = WorldTemporaryEncounterKind.Passing, BasePresetId = "band-small",
                Weight = 3, NightWeight = 0.5f, Reason = "идут через округу",
                Terrains = T(WorldMapGameplayTerrainType.Road, WorldMapGameplayTerrainType.Trail, WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Field),
                Hint = "По дороге прошли люди с оружием — следы свежие, идут не спеша."
            },
            new WorldTemporaryEncounterKind
            {
                Id = "temp-hunting-pack", Name = "Охотящаяся стая", Kind = WorldTemporaryEncounterKind.Hunting, BasePresetId = "wolf-pack",
                Weight = 2, NightWeight = 2f, Reason = "охотятся",
                Terrains = T(WorldMapGameplayTerrainType.Forest, WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Field),
                Hint = "Где-то за деревьями перекликаются волки — стая вышла на охоту."
            },
            new WorldTemporaryEncounterKind
            {
                Id = "temp-roadside-boar", Name = "Кабан у дороги", Kind = WorldTemporaryEncounterKind.Roadside, BasePresetId = "boar",
                Weight = 2, NightWeight = 1f, Reason = "роет землю у обочины",
                Terrains = T(WorldMapGameplayTerrainType.OpenGround, WorldMapGameplayTerrainType.Forest, WorldMapGameplayTerrainType.Field),
                Hint = "Обочина впереди перерыта — кабан кормится у самой дороги."
            },
            new WorldTemporaryEncounterKind
            {
                Id = "temp-skirmish", Name = "Чужая стычка", Kind = WorldTemporaryEncounterKind.Skirmish, BasePresetId = "band-small",
                SecondPresetId = "caravan-common", SecondName = "Путники", Weight = 1, NightWeight = 0f, Reason = "схватились с путниками",
                Terrains = T(WorldMapGameplayTerrainType.Road, WorldMapGameplayTerrainType.Trail, WorldMapGameplayTerrainType.OpenGround),
                Hint = "Со стороны дороги доносятся крики и звон железа."
            },
            new WorldTemporaryEncounterKind
            {
                Id = "temp-at-find", Name = "Падальщики у находки", Kind = WorldTemporaryEncounterKind.AtFind, BasePresetId = "padalny-mnogonog",
                Weight = 2, NightWeight = 1.5f, Reason = "копошатся у места, где вы были",
                Hint = "К месту недавней находки потянулись падальщики."
            },
        };
    }

    // Стартовый рабочий набор брифа §7 (составы — реальные ID Базы существ;
    // характеристики берутся из базы, а не отсюда).
    public static List<WorldPopulationPreset> DefaultPresets()
    {
        WorldPopulationMember M(string id, int min = 1, int max = 1) => new WorldPopulationMember { UnitTypeId = id, Min = min, Max = max };
        WorldPopulationPreset P(string id, string name, string role, string behavior, string budget, string biome, params WorldPopulationMember[] members) =>
            new WorldPopulationPreset { Id = id, Name = name, Role = role, Behavior = behavior, Budget = budget, Biome = biome, Members = members.ToList() };

        return new List<WorldPopulationPreset>
        {
            // Люди: составы для групп мира (роли патруль/обоз/банда).
            With(P("patrol-small", "Дозор", WorldMapGroupRule.Patrol, WorldPopulationBehavior.Patrol, WorldPopulationPreset.Peaceful, WorldPopulationBiome.Road,
                    M("spearman"), M("militia"), M("archer")),
                p => { p.Weight = 3; p.DetectHexes = 3.5f; p.ChaseHexes = 4f; p.Description = "Несколько пеших с копьями обходят дорогу."; }),
            With(P("patrol-strong", "Усиленный дозор", WorldMapGroupRule.Patrol, WorldPopulationBehavior.Patrol, WorldPopulationPreset.Peaceful, WorldPopulationBiome.Road,
                    M("guard"), M("spearman"), M("archer")),
                p => { p.Weight = 1; p.MaxPerMap = 1; p.DetectHexes = 3.5f; p.ChaseHexes = 4f; p.Description = "Дозор под началом гвардейца — так охраняют важный путь."; }),
            With(P("caravan-common", "Обоз", WorldMapGroupRule.Caravan, WorldPopulationBehavior.Peaceful, WorldPopulationPreset.Peaceful, WorldPopulationBiome.Road,
                    M("militia"), M("spearman")),
                p => { p.DetectHexes = 2.5f; p.Description = "Телеги с поклажей, возчики идут рядом с лошадьми, двое с оружием."; }),
            With(P("band-small", "Ватага", WorldMapGroupRule.Band, WorldPopulationBehavior.Bandit, WorldPopulationPreset.Ordinary, WorldPopulationBiome.Road,
                    M("militia", 2, 2), M("scout")),
                p =>
                {
                    p.Weight = 3; p.DetectHexes = 3f; p.ChaseHexes = 3f; p.Reserve = 2;
                    p.Signs = "У дороги — свежее кострище, присыпанное землёй, и следы нескольких пар сапог в сторону леса.";
                    p.AttackText = "Из-за деревьев выходят люди с оружием и перегораживают дорогу. Старший требует платы за проход.";
                    p.Description = "Вооружённые люди в серых плащах держатся своей тропы.";
                }),
            With(P("band-prepared", "Подготовленная ватага", WorldMapGroupRule.Band, WorldPopulationBehavior.Bandit, WorldPopulationPreset.Strong, WorldPopulationBiome.Road,
                    M("militia", 2, 2), M("archer"), M("scout")),
                p =>
                {
                    p.Weight = 1; p.MaxPerMap = 1; p.DetectHexes = 3.5f; p.ChaseHexes = 3.5f; p.Reserve = 2;
                    p.Signs = "На обочине — сломанная стрела и брошенный мешок. Кто-то здесь уже останавливал путников.";
                    p.AttackText = "Лучник на пригорке, остальные — на дороге. Требуют платы и не прячут оружия.";
                    p.Description = "Ватага с лучником держит свой участок дороги.";
                }),
            // Звери (обычные угрозы).
            With(P("wolf-pack", "Волчья стая", WorldMapGroupRule.Beast, WorldPopulationBehavior.Predator, WorldPopulationPreset.Ordinary, WorldPopulationBiome.ForestEdge,
                    M("wolf", 2, 3)),
                p =>
                {
                    p.Weight = 3; p.MaxPerMap = 3; p.TerritoryRadiusHexes = 3f; p.DetectHexes = 3f; p.ChaseHexes = 2.5f; p.RestHours = 5f; p.AppeaseSupply = 2;
                    p.Signs = "На опушке — волчьи следы цепочкой и обглоданная кость.";
                    p.WarningText = "Серые тени держатся на краю опушки и идут вровень с отрядом.";
                    p.AttackText = "Стая выходит из подлеска и берёт отряд в полукольцо.";
                    p.Description = "Стая кормится у опушки; здоровый вооружённый отряд обычно только провожает взглядом.";
                }),
            With(P("boar", "Кабан", WorldMapGroupRule.Beast, WorldPopulationBehavior.Territorial, WorldPopulationPreset.Ordinary, WorldPopulationBiome.Underbrush,
                    M("boar")),
                p =>
                {
                    p.Weight = 2; p.MaxPerMap = 2; p.TerritoryRadiusHexes = 2f; p.WarnHexes = 1.6f; p.ChaseHexes = 1.5f; p.RestHours = 4f; p.RetreatSupplyCost = 1;
                    p.Signs = "Земля под дубами изрыта, на стволах — грязь на высоте колена.";
                    p.WarningText = "В подлеске хрустит и фыркает — кабан не уходит со своего места.";
                    p.AttackText = "Кабан срывается с места и несётся прямо на отряд.";
                    p.Description = "Кабан держится своего подлеска; опасен, если подойти близко.";
                }),
            With(P("lynx", "Рысь", WorldMapGroupRule.Beast, WorldPopulationBehavior.Shy, WorldPopulationPreset.Ordinary, WorldPopulationBiome.DeepForest,
                    M("lynx")),
                p =>
                {
                    p.Weight = 1; p.MaxPerMap = 1; p.TerritoryRadiusHexes = 3f; p.DetectHexes = 2.5f; p.ChaseHexes = 0f; p.RestHours = 6f;
                    p.Signs = "На стволе — глубокие следы когтей, на снегу у валежника — крупные круглые отпечатки.";
                    p.WarningText = "На поваленном стволе мелькнули кисточки ушей — и пропали.";
                    p.AttackText = "Рысь прижата к валежнику и шипит.";
                    p.Description = "Скрытный зверь: чаще след, чем встреча.";
                }),
            With(P("elk-bull", "Лось-секач", WorldMapGroupRule.Beast, WorldPopulationBehavior.Territorial, WorldPopulationPreset.Ordinary, WorldPopulationBiome.ForestEdge,
                    M("elk_bull")),
                p =>
                {
                    p.Weight = 1; p.MaxPerMap = 1; p.TerritoryRadiusHexes = 2.5f; p.WarnHexes = 1.6f; p.ChaseHexes = 1f; p.RestHours = 5f;
                    p.Signs = "Обглоданные осинки и глубокие раздвоенные следы у просеки.";
                    p.WarningText = "Лось поднял голову и смотрит на отряд, не уступая просеку.";
                    p.AttackText = "Лось опускает рога и идёт на отряд.";
                    p.Description = "Нейтральный зверь; бьётся, если на него идут.";
                }),
            // Сильная обозначенная угроза.
            With(P("bear", "Медведь", WorldMapGroupRule.Beast, WorldPopulationBehavior.Territorial, WorldPopulationPreset.Strong, WorldPopulationBiome.DeepForest,
                    M("bear")),
                p =>
                {
                    p.MaxPerMap = 1; p.TerritoryRadiusHexes = 3f; p.WarnHexes = 2f; p.ChaseHexes = 1.5f; p.RestHours = 6f; p.RetreatSupplyCost = 2; p.Reserve = 0;
                    p.Signs = "Содранная кора на высоте человеческого роста и разваленный муравейник.";
                    p.WarningText = "Из чащи доносится тяжёлое сопение. Медведь встал и смотрит в вашу сторону.";
                    p.AttackText = "Медведь идёт на отряд, ломая подлесок.";
                    p.Description = "Сильная местная угроза; участок можно обойти.";
                }),
            // Необычные обитатели (прототипный каталог, не факт мира).
            With(P("beregovik", "Береговик", WorldMapGroupRule.Creature, WorldPopulationBehavior.Territorial, WorldPopulationPreset.Creature, WorldPopulationBiome.Shore,
                    M("beregovik")),
                p =>
                {
                    // Решение пользователя 19.11.2026: Береговик исключён из
                    // новых встреч («убрать, пока не нужен») — пресет выключен.
                    p.Enabled = false;
                    p.Status = "[РАБОЧЕЕ — ПРОТОТИП] Выключен: Береговик исключён из новых встреч решением 19.11.2026.";
                    p.MaxPerMap = 1; p.TerritoryRadiusHexes = 1.5f; p.WarnHexes = 1.5f; p.ChaseHexes = 1f; p.Reserve = 0;
                    p.Signs = "У воды — мокрая полоса, будто что-то тяжёлое волокли с берега и обратно.";
                    p.WarningText = "Тина у берега шевелится против течения.";
                    p.AttackText = "Из воды у самого берега поднимается тёмная туша.";
                    p.Description = "Держит свой участок берега и дальше воды не преследует.";
                }),
            With(P("tinny-polzun", "Тинный ползун", WorldMapGroupRule.Creature, WorldPopulationBehavior.Territorial, WorldPopulationPreset.Creature, WorldPopulationBiome.Swamp,
                    M("tinny_polzun")),
                p =>
                {
                    p.MaxPerMap = 1; p.TerritoryRadiusHexes = 2f; p.WarnHexes = 1.5f; p.ChaseHexes = 0.8f; p.Reserve = 0;
                    p.Signs = "По кочкам тянется слизистый след, осока полегла широкой полосой.";
                    p.WarningText = "В топи что-то медленно ползёт к сухому краю.";
                    p.AttackText = "Ползун выбирается из тины на тропу.";
                    p.Description = "Медленная местная угроза топкого берега.";
                }),
            With(P("kamnespin", "Камнеспин", WorldMapGroupRule.Creature, WorldPopulationBehavior.Territorial, WorldPopulationPreset.Creature, WorldPopulationBiome.Rocky,
                    M("kamnespin")),
                p =>
                {
                    p.MaxPerMap = 1; p.TerritoryRadiusHexes = 2f; p.WarnHexes = 1.5f; p.ChaseHexes = 1f; p.Reserve = 0;
                    p.Signs = "Под склоном — свежая осыпь, камни сдвинуты, будто их ворочали.";
                    p.WarningText = "Один из валунов у подножия медленно поворачивается.";
                    p.AttackText = "Камнеспин сползает со склона навстречу отряду.";
                    p.Description = "Держится камней у подножия скал.";
                }),
            With(P("padalny-mnogonog", "Падальщики", WorldMapGroupRule.Creature, WorldPopulationBehavior.Territorial, WorldPopulationPreset.Creature, WorldPopulationBiome.Ruins,
                    M("padalny_mnogonog", 1, 2)),
                p =>
                {
                    p.MaxPerMap = 1; p.TerritoryRadiusHexes = 1.5f; p.WarnHexes = 1.4f; p.ChaseHexes = 0.8f; p.Reserve = 0;
                    p.Signs = "Тяжёлый запах падали тянет от старых развалин.";
                    p.WarningText = "В развалинах шуршит и щёлкает множество ног.";
                    p.AttackText = "Многоноги выползают из-под брёвен к отряду.";
                    p.Description = "Кормятся у старого места; без провокации не уходят от него.";
                }),
        };
    }

    private static WorldPopulationPreset With(WorldPopulationPreset preset, Action<WorldPopulationPreset> setup)
    {
        setup(preset);
        return preset;
    }

    // Пресет человеческой группы мира по её роли (первый подходящий обычный;
    // сильный выбирается генератором отдельно).
    public static WorldPopulationPreset DefaultForRole(WorldPopulationSettings settings, string role)
    {
        if (settings?.Presets == null)
            return null;
        return settings.Presets.Where(p => p.Enabled && p.Role == role && p.Budget != WorldPopulationPreset.Strong)
            .OrderBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    public static bool CheckUnits(WorldPopulationPreset preset, out string reason)
    {
        reason = string.Empty;
        if (preset?.Members == null || preset.Members.Count == 0)
        {
            reason = "нет состава";
            return false;
        }
        if (Availability == null)
            return true;
        foreach (WorldPopulationMember member in preset.Members)
        {
            if (!Availability.IsAvailableForNewGroups(member.UnitTypeId, out string why))
            {
                reason = "существо «" + member.UnitTypeId + "»: " + why;
                return false;
            }
        }
        return true;
    }
}
