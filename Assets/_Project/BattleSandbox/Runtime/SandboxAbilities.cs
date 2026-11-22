using System;
using System.Collections.Generic;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-16 (бриф 19.11.2026): механика способности существа. Имя совпадает с
    // UnitAbilityKind Базы существ — адаптер переносит его по имени.
    public enum SandboxAbilityKind
    {
        None,

        // Активные: кнопка панели способностей, 1 действие.
        Pounce,
        Charge,
        BreakGuard,
        Shove,
        Attach,
        StunSpit,
        NumbBite,
        Engulf,
        VineGrab,
        ChimeMark,
        Burrow,
        Call,
        ConsumeCorpse,
        WeaveThread,
        TendrilGrab,
        PitCycle,
        LayEgg,

        // Пассивные правила и профили поведения ИИ — не кнопки.
        KiteAfterShot,
        KeepDistance,
        FeedOnNumbed,
        RegenOnWater,
        HiddenStart,
        Flood,

        // Бриф завершения (22.11.2026), этапы 3–4: активные, в конце списка —
        // числовые значения прежних видов в данных не сдвигаются.
        Heal,
        Spell,
        SpellArea,
        Dispel
    }

    // Способность как данные: числа из Базы существ, без кода под существо.
    // Поля общие; что значит Amount/ObjectHitPoints/Limit — по виду (см.
    // SandboxAbilityDefaults и ProjectDocs/BESTIARY_COMBAT_PASSPORTS.md).
    public sealed class SandboxAbilityDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public SandboxAbilityKind Kind { get; }
        public int Cooldown { get; }
        public int RangeMin { get; }
        public int RangeMax { get; }
        public int PathMin { get; }
        public int PathMax { get; }
        public decimal DamageMultiplier { get; }
        public int Amount { get; }
        public int ObjectHitPoints { get; }
        public int Duration { get; }
        public int Limit { get; }
        public int Push { get; }

        public SandboxAbilityDefinition(
            string id,
            string title,
            SandboxAbilityKind kind,
            int cooldown = 0,
            int rangeMin = 1,
            int rangeMax = 1,
            int pathMin = 0,
            int pathMax = 0,
            decimal damageMultiplier = 1m,
            int amount = 0,
            int objectHitPoints = 0,
            int duration = 0,
            int limit = 0,
            int push = 0,
            string description = null,
            string spawnTypeId = null,
            string statusPayloadId = null,
            bool statusPayloadOncePerBattle = false,
            int statusPayloadDuration = 0,
            int statusPayloadTick = 0,
            SandboxDamageType damageType = SandboxDamageType.Physical,
            bool isMagical = false)
        {
            Id = string.IsNullOrWhiteSpace(id) ? kind.ToString() : id;
            Title = string.IsNullOrWhiteSpace(title) ? Id : title;
            Description = description ?? string.Empty;
            Kind = kind;
            Cooldown = Math.Max(0, cooldown);
            RangeMin = Math.Max(0, rangeMin);
            RangeMax = Math.Max(RangeMin, rangeMax);
            PathMin = Math.Max(0, pathMin);
            PathMax = Math.Max(PathMin, pathMax);
            DamageMultiplier = Math.Max(0m, damageMultiplier);
            Amount = Math.Max(0, amount);
            ObjectHitPoints = Math.Max(0, objectHitPoints);
            Duration = Math.Max(0, duration);
            Limit = Math.Max(0, limit);
            Push = Math.Max(0, push);
            SpawnTypeId = spawnTypeId ?? string.Empty;
            StatusPayloadId = statusPayloadId ?? string.Empty;
            StatusPayloadOncePerBattle = statusPayloadOncePerBattle;
            StatusPayloadDuration = Math.Max(0, statusPayloadDuration);
            StatusPayloadTick = Math.Max(0, statusPayloadTick);
            DamageType = damageType;
            IsMagical = isMagical || kind == SandboxAbilityKind.Spell || kind == SandboxAbilityKind.SpellArea || kind == SandboxAbilityKind.Dispel;
        }

        // Тип повреждения и магическая доставка (способ атаки и тип урона — разные вещи:
        // огненная стрела может быть обычным снарядом).
        public SandboxDamageType DamageType { get; }
        public bool IsMagical { get; }

        // Пакет состояний: состояние удара способности (данные Базы существ).
        public string StatusPayloadId { get; }
        public bool StatusPayloadOncePerBattle { get; }
        public int StatusPayloadDuration { get; }
        public int StatusPayloadTick { get; }

        // Кого порождает способность (Гнездовая матка → blood_kleshchen).
        public string SpawnTypeId { get; }

        public bool IsActive => IsActiveKind(Kind);

        public static bool IsActiveKind(SandboxAbilityKind kind)
        {
            return (kind >= SandboxAbilityKind.Pounce && kind <= SandboxAbilityKind.LayEgg) ||
                   kind == SandboxAbilityKind.Heal || kind == SandboxAbilityKind.Spell ||
                   kind == SandboxAbilityKind.SpellArea || kind == SandboxAbilityKind.Dispel;
        }

        public static string DamageTypeLabel(SandboxDamageType type)
        {
            switch (type)
            {
                case SandboxDamageType.Fire: return "огонь";
                case SandboxDamageType.Cold: return "холод";
                case SandboxDamageType.Poison: return "яд";
                case SandboxDamageType.Supernatural: return "чары";
                default: return "физический";
            }
        }

        public string TypeLabel => IsActive
            ? "активная"
            : Kind == SandboxAbilityKind.KiteAfterShot || Kind == SandboxAbilityKind.KeepDistance ||
              Kind == SandboxAbilityKind.FeedOnNumbed
                ? "поведение ИИ"
                : "пассивная";
    }

    // Стартовые настройки брифа 19.11.2026 — рабочая первая версия баланса,
    // не канон. Источник истины в игре — База существ; эти значения
    // подставляет миграция, когда у записи способности ещё нет механики.
    public static class SandboxAbilityDefaults
    {
        public static SandboxAbilityDefinition Create(SandboxAbilityKind kind, string id = null, string title = null)
        {
            switch (kind)
            {
                case SandboxAbilityKind.Pounce:
                    return new SandboxAbilityDefinition(id, title ?? "Прыжок", kind, cooldown: 2, rangeMax: 3, pathMin: 2, pathMax: 3, damageMultiplier: 1.25m);
                case SandboxAbilityKind.Charge:
                    return new SandboxAbilityDefinition(id, title ?? "Короткий разгон", kind, cooldown: 2, pathMin: 2, pathMax: 3, damageMultiplier: 1.25m);
                case SandboxAbilityKind.BreakGuard:
                    return new SandboxAbilityDefinition(id, title ?? "Сбить стойку", kind, cooldown: 2, damageMultiplier: 1.15m);
                case SandboxAbilityKind.Shove:
                    return new SandboxAbilityDefinition(id, title ?? "Отбросить", kind, cooldown: 2, damageMultiplier: 1.10m, push: 1);
                case SandboxAbilityKind.Attach:
                    return new SandboxAbilityDefinition(id, title ?? "Цепляется", kind, cooldown: 2, damageMultiplier: 0.75m, amount: 2);
                case SandboxAbilityKind.StunSpit:
                    return new SandboxAbilityDefinition(id, title ?? "Ошеломляющий плевок", kind, cooldown: 3, rangeMin: 2, rangeMax: 3, damageMultiplier: 0.50m);
                case SandboxAbilityKind.NumbBite:
                    return new SandboxAbilityDefinition(id, title ?? "Оцепеняет", kind, cooldown: 3, damageMultiplier: 1.00m);
                case SandboxAbilityKind.Engulf:
                    return new SandboxAbilityDefinition(id, title ?? "Накрывает", kind, cooldown: 2, damageMultiplier: 0.75m, amount: 2);
                case SandboxAbilityKind.VineGrab:
                    return new SandboxAbilityDefinition(id, title ?? "Хватает и подтягивает", kind, cooldown: 2, rangeMin: 1, rangeMax: 3, damageMultiplier: 0.75m, objectHitPoints: 5, limit: 4, push: 1);
                case SandboxAbilityKind.ChimeMark:
                    return new SandboxAbilityDefinition(id, title ?? "Метит", kind, cooldown: 2, rangeMin: 0, rangeMax: 3, damageMultiplier: 1.25m, limit: 2);
                case SandboxAbilityKind.Burrow:
                    return new SandboxAbilityDefinition(id, title ?? "Подкоп", kind, cooldown: 3, rangeMin: 1, rangeMax: 3, damageMultiplier: 1.15m, duration: 2, push: 1);
                case SandboxAbilityKind.Call:
                    return new SandboxAbilityDefinition(id, title ?? "Зов", kind, cooldown: 3, rangeMin: 1, rangeMax: 4, push: 1);
                case SandboxAbilityKind.ConsumeCorpse:
                    return new SandboxAbilityDefinition(id, title ?? "Пустить корни", kind, cooldown: 2, amount: 25);
                case SandboxAbilityKind.WeaveThread:
                    return new SandboxAbilityDefinition(id, title ?? "Сплести нить", kind, cooldown: 2, rangeMax: 3, pathMin: 2, pathMax: 4, objectHitPoints: 4, duration: 3, limit: 2);
                case SandboxAbilityKind.TendrilGrab:
                    return new SandboxAbilityDefinition(id, title ?? "Захват жгутом", kind, rangeMin: 1, rangeMax: 3, damageMultiplier: 0.75m, objectHitPoints: 6, limit: 3, push: 1);
                case SandboxAbilityKind.PitCycle:
                    return new SandboxAbilityDefinition(id, title ?? "Провалы", kind, rangeMax: 4, damageMultiplier: 1.00m, duration: 2, limit: 4, amount: 2);
                case SandboxAbilityKind.LayEgg:
                    return new SandboxAbilityDefinition(id, title ?? "Отложить яйцо", kind, cooldown: 3, objectHitPoints: 5, duration: 2, limit: 3, amount: 2, spawnTypeId: "blood_kleshchen");
                case SandboxAbilityKind.KiteAfterShot:
                    return new SandboxAbilityDefinition(id, title ?? "Стреляет и отходит", kind, rangeMin: 3, rangeMax: 4, pathMin: 1, pathMax: 2);
                case SandboxAbilityKind.KeepDistance:
                    return new SandboxAbilityDefinition(id, title ?? "Держит дистанцию", kind, rangeMin: 4, rangeMax: 5);
                case SandboxAbilityKind.FeedOnNumbed:
                    return new SandboxAbilityDefinition(id, title ?? "Кормится", kind);
                case SandboxAbilityKind.RegenOnWater:
                    return new SandboxAbilityDefinition(id, title ?? "Восстанавливается", kind, amount: 10);
                case SandboxAbilityKind.HiddenStart:
                    return new SandboxAbilityDefinition(id, title ?? "Скрыт до броска", kind);
                case SandboxAbilityKind.Flood:
                    return new SandboxAbilityDefinition(id, title ?? "Разлив", kind, amount: 2, duration: 2, limit: 12);
                case SandboxAbilityKind.Heal:
                    return new SandboxAbilityDefinition(id, title ?? "Лечение", kind, rangeMin: 0, rangeMax: 1, amount: 4);
                case SandboxAbilityKind.Spell:
                    return new SandboxAbilityDefinition(id, title ?? "Заклинание", kind, cooldown: 2, rangeMin: 1, rangeMax: 3, damageMultiplier: 1.00m);
                case SandboxAbilityKind.SpellArea:
                    return new SandboxAbilityDefinition(id, title ?? "Заклинание по площади", kind, cooldown: 3, rangeMin: 1, rangeMax: 3, damageMultiplier: 1.00m, push: 1);
                case SandboxAbilityKind.Dispel:
                    return new SandboxAbilityDefinition(id, title ?? "Развеять чары", kind, cooldown: 2, rangeMin: 0, rangeMax: 3, amount: 1);
                default:
                    return new SandboxAbilityDefinition(id, title, kind);
            }
        }

        public static string Explain(SandboxAbilityDefinition a)
        {
            if (a == null)
                return string.Empty;
            string m = "×" + a.DamageMultiplier.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
            switch (a.Kind)
            {
                case SandboxAbilityKind.Pounce: return "Цель в пределах " + a.RangeMax + " гексов: прыжок на " + a.PathMin + "–" + a.PathMax + " гекса к свободной клетке рядом с ней (тратит столько же движения) и удар " + m + ".";
                case SandboxAbilityKind.Charge: return "Разбег по прямой на " + a.PathMin + "–" + a.PathMax + " гекса и удар " + m + " по цели на продолжении линии" + (a.Push > 0 ? ", затем отбрасывание на " + a.Push + " гекс." : ".");
                case SandboxAbilityKind.BreakGuard: return "Соседняя цель: удар " + m + " по текущей защите, затем снимает защитную стойку.";
                case SandboxAbilityKind.Shove: return "Соседняя цель: удар " + m + ", затем отбрасывание на " + a.Push + " гекс от себя.";
                case SandboxAbilityKind.Attach: return "Соседняя цель: укус " + m + "; если оба живы — садится на неё и в начале своих ходов пьёт " + a.Amount + " HP. Снять — 1 действие жертвы или соседнего союзника.";
                case SandboxAbilityKind.StunSpit: return "Одна цель в " + a.RangeMin + "–" + a.RangeMax + " гексах на линии видимости: " + m + " (минимум 1) и «Ошеломлён» — без ответных ударов и с −1 движения до конца следующего хода цели.";
                case SandboxAbilityKind.NumbBite: return "Соседняя цель: укус " + m + " и «Оцепенение» на следующий ход (без добровольного движения и ответов). Защитная стойка блокирует оцепенение, но не урон.";
                case SandboxAbilityKind.Engulf: return "Соседняя цель: " + m + " и «Захвачен». В начале ходов ползуна жертва теряет " + a.Amount + " HP. Вырваться/вытащить — 1 действие.";
                case SandboxAbilityKind.VineGrab: return "Цель до " + a.RangeMax + " гексов: " + m + ", лоза (" + a.ObjectHitPoints + " HP) и подтягивание на 1 гекс. Следующими ходами подтягивает ещё; рвётся дальше " + a.Limit + " гексов.";
                case SandboxAbilityKind.ChimeMark: return "До " + a.Limit + " врагов в " + a.RangeMax + " гексах получают метку. На следующем ходу «Удар звона» " + m + " по отмеченным, оставшимся в зоне радиуса " + a.RangeMax + ".";
                case SandboxAbilityKind.Burrow: return "Уходит под землю и объявляет выход до " + a.RangeMax + " гексов по грунту. На следующем ходу выходит и может ударить соседнего " + m + " и подтянуть к норе.";
                case SandboxAbilityKind.Call: return "Цель до " + a.RangeMax + " гексов без защитной стойки делает один вынужденный шаг ближе. Без урона.";
                case SandboxAbilityKind.ConsumeCorpse: return "Поглощает соседний труп и восстанавливает " + a.Amount + "% HP (минимум 1).";
                case SandboxAbilityKind.WeaveThread: return "Нить между двумя точками в " + a.RangeMax + " гексах (длина " + a.PathMin + "–" + a.PathMax + "), " + a.ObjectHitPoints + " HP, " + a.Duration + " раунда. Вход на гекс нити останавливает движение (кроме создателя).";
                case SandboxAbilityKind.TendrilGrab: return "Свободный жгут хватает цель до " + a.RangeMax + " гексов (" + m + "); удержанного можно подтянуть к телу. Жгут " + a.ObjectHitPoints + " HP.";
                case SandboxAbilityKind.PitCycle: return "Цикл: уходит под землю → объявляет " + a.Amount + " просадки и выход → выходит (просадки бьют " + m + ") и остаётся уязвим до следующего хода.";
                case SandboxAbilityKind.LayEgg: return "Яйцо (" + a.ObjectHitPoints + " HP) на соседней клетке; через " + a.Duration + " хода матки вылупляется клещень. Не больше " + a.Limit + " яиц и " + a.Limit + " живых клещней.";
                case SandboxAbilityKind.KiteAfterShot: return "ИИ: выстрел и отход на " + a.PathMin + "–" + a.PathMax + " гекса в пределах того же движения.";
                case SandboxAbilityKind.KeepDistance: return "ИИ: держит дистанцию " + a.RangeMin + "–" + a.RangeMax + " гексов, при угрозе сначала отходит.";
                case SandboxAbilityKind.FeedOnNumbed: return "ИИ: прежде всего бьёт оцепеневшую жертву.";
                case SandboxAbilityKind.RegenOnWater: return "В начале своего хода на мелкой воде восстанавливает " + a.Amount + "% HP (вверх, минимум 1).";
                case SandboxAbilityKind.HiddenStart: return "Если разведка не заметила — начинает бой скрытым (виден подозрительный пень). Раскрывается своей атакой, уроном или контактом.";
                case SandboxAbilityKind.Flood: return "Каждую " + a.Duration + "-ю свою активацию объявляет " + a.Amount + " клетки разлива у воды; они станут мелкой водой на следующем ходу. Не больше " + a.Limit + " клеток за бой.";
                case SandboxAbilityKind.Heal: return "Себя или союзника в " + a.RangeMax + " гексе: +" + a.Amount + " HP, не выше максимума. Яд и кровотечение лечение само не снимает. 1 действие вместо атаки.";
                case SandboxAbilityKind.Spell: return "Адресное заклинание: цель в " + a.RangeMin + "–" + a.RangeMax + " гексах по линии видимости, один удар " + m + " (" + SandboxAbilityDefinition.DamageTypeLabel(a.DamageType) + ")" +
                                                      (string.IsNullOrEmpty(a.StatusPayloadId) ? string.Empty : ", затем состояние") + ". Защита от чар отменяет.";
                case SandboxAbilityKind.SpellArea: return "Заклинание по площади: клетка в " + a.RangeMax + " гексах и соседние в радиусе " + Math.Max(1, a.Push) + ", удар " + m + " (" + SandboxAbilityDefinition.DamageTypeLabel(a.DamageType) + ") по всем, кто там стоит, включая своих.";
                case SandboxAbilityKind.Dispel: return "Снимает одно полезное состояние с врага в " + a.RangeMax + " гексах по линии видимости.";
                default: return a.Description;
            }
        }
    }

    // Команда способности: что выбрано игроком или ИИ. Одна и та же команда
    // проходит предпросмотр и исполнение.
    public sealed class SandboxAbilityCommand
    {
        public string AbilityId { get; set; }
        public string TargetUnitId { get; set; }
        public string SecondTargetUnitId { get; set; }
        public string TargetObjectId { get; set; }
        public HexCoord? TargetCell { get; set; }
        public HexCoord? SecondCell { get; set; }
        public HexCoord? ApproachCell { get; set; }

        // Ключевая клетка для выбора курсором: цель, приземление, выход и т.п.
        public HexCoord KeyCell { get; set; }

        public SandboxAbilityCommand Clone()
        {
            return (SandboxAbilityCommand)MemberwiseClone();
        }
    }

    public sealed class SandboxAbilityPreview
    {
        public bool IsValid { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int Damage { get; set; }
        public int NormalDamage { get; set; }
        public decimal Multiplier { get; set; } = 1m;
        public string Summary { get; set; } = string.Empty;
        public List<HexCoord> Path { get; } = new List<HexCoord>();
        public List<HexCoord> DangerCells { get; } = new List<HexCoord>();
        public HexCoord? LandingCell { get; set; }
        public HexCoord? ForcedCell { get; set; }
        public string ForcedBlockedReason { get; set; } = string.Empty;

        public static SandboxAbilityPreview Invalid(string reason)
        {
            return new SandboxAbilityPreview { IsValid = false, Reason = reason ?? string.Empty };
        }
    }

    // Состояние способности для панели: готова ли и почему нет.
    public sealed class SandboxAbilityState
    {
        public SandboxAbilityDefinition Ability { get; set; }
        public bool Available { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int CooldownLeft { get; set; }
        public string Label { get; set; } = string.Empty;
        public bool NeedsSecondCell { get; set; }
        public bool NoTarget { get; set; }
    }

    // Контекстное действие без способности: снять клещня, вырваться,
    // вытащить, разрезать нить, ударить по объекту.
    public enum SandboxContextActionKind
    {
        RemoveParasite,
        BreakFree,
        PullFree,
        CutThread,
        AttackObject,
        // Бриф завершения, этап 11: спасти соседа «При смерти» за 1 действие.
        Rescue,
        // Пакет состояний: очищение себя или соседнего союзника за 1 действие.
        CleanseStatus
    }

    public sealed class SandboxContextAction
    {
        public SandboxContextActionKind Kind { get; set; }
        public string Label { get; set; } = string.Empty;
        public string TargetUnitId { get; set; }
        public string ObjectId { get; set; }
        public string LinkId { get; set; }
        public string StatusId { get; set; }
    }

    public enum SandboxObjectKind
    {
        Vine,
        Thread,
        Egg,
        Tendril,
        // Бриф «Поверхности арены», этап 2: объекты местности (без владельца).
        Boulder,
        Trunk,
        FallenLog,
        Deadfall,
        Wattle,
        StoneWall,
        Bridge,
        RootNest,
        RitualStone,
        // Бриф завершения, этап 12: кокон выводка (не объект местности).
        Cocoon
    }

    // Разрушаемый объект боя: лоза, нить, яйцо, жгут. Не боец: нет
    // инициативы, опыта и трофеев.
    public sealed class SandboxBattleObject
    {
        public string Id { get; internal set; }
        public SandboxObjectKind Kind { get; internal set; }
        public string OwnerId { get; internal set; }
        public int HitPoints { get; internal set; }
        public int MaxHitPoints { get; internal set; }
        public List<HexCoord> Cells { get; } = new List<HexCoord>();
        public int ExpiresAtRound { get; internal set; }
        public int Counter { get; internal set; }
        public bool CreatedThisActivation { get; internal set; }
        public string HeldUnitId { get; internal set; }
        public bool IsDestroyed => HitPoints <= 0;
        public bool IsTerrain => Kind >= SandboxObjectKind.Boulder && Kind != SandboxObjectKind.Cocoon;
        // Кокон «Зародыша в теле»: сторона и источник фиксируются при создании.
        public SandboxTeam SnapshotTeam { get; internal set; }
        public string SourceId { get; internal set; }
        // Неразрушимый объект местности (валун, мост) — HitPoints не убывают.
        public bool Indestructible { get; internal set; }

        public string Label
        {
            get
            {
                switch (Kind)
                {
                    case SandboxObjectKind.Vine: return "Лоза";
                    case SandboxObjectKind.Thread: return "Нить";
                    case SandboxObjectKind.Egg: return "Яйцо";
                    case SandboxObjectKind.Cocoon: return "Кокон";
                    case SandboxObjectKind.Tendril: return "Жгут";
                    default: return SandboxTerrainObjects.Get(Kind).Label;
                }
            }
        }
    }

    public enum SandboxLinkKind
    {
        Attach,
        Engulf,
        Vine,
        Tendril
    }

    public sealed class SandboxLink
    {
        public string Id { get; internal set; }
        public SandboxLinkKind Kind { get; internal set; }
        public string SourceId { get; internal set; }
        public string TargetId { get; internal set; }
        public string ObjectId { get; internal set; }
        public int CreatedAtSourceActivation { get; internal set; }
    }

    public enum SandboxTelegraphKind
    {
        Toll,
        BurrowExit,
        Subsidence,
        Flood
    }

    // Объявленная угроза: видна на поле и в очереди до хода источника.
    public sealed class SandboxTelegraph
    {
        public string Id { get; internal set; }
        public SandboxTelegraphKind Kind { get; internal set; }
        public string SourceId { get; internal set; }
        public HexCoord Center { get; internal set; }
        public HexCoord Origin { get; internal set; }
        public List<HexCoord> Cells { get; } = new List<HexCoord>();
        public List<string> TargetIds { get; } = new List<string>();
        public string Note { get; internal set; } = string.Empty;
    }

    public static class SandboxHexLine
    {
        // Клетки прямой между центрами a и b, включая концы (кубическая
        // интерполяция со смещением, без зависимости от камеры).
        public static List<HexCoord> Line(HexCoord a, HexCoord b)
        {
            int n = a.DistanceTo(b);
            List<HexCoord> result = new List<HexCoord>();
            ToCube(a, out double ax, out double ay, out double az);
            ToCube(b, out double bx, out double by, out double bz);
            ax += 1e-6; ay += 2e-6; az -= 3e-6;
            for (int i = 0; i <= n; i++)
            {
                double t = n == 0 ? 0 : (double)i / n;
                HexCoord cell = FromCube(ax + (bx - ax) * t, ay + (by - ay) * t, az + (bz - az) * t);
                if (result.Count == 0 || result[result.Count - 1] != cell)
                    result.Add(cell);
            }
            return result;
        }

        public static HexCoord Step(HexCoord from, int direction)
        {
            int index = 0;
            foreach (HexCoord neighbor in from.Neighbors())
            {
                if (index++ == direction)
                    return neighbor;
            }
            return from;
        }

        private static void ToCube(HexCoord c, out double x, out double y, out double z)
        {
            int cx = c.Q - (c.R - (c.R & 1)) / 2;
            x = cx;
            z = c.R;
            y = -x - z;
        }

        private static HexCoord FromCube(double x, double y, double z)
        {
            double rx = Math.Round(x), ry = Math.Round(y), rz = Math.Round(z);
            double dx = Math.Abs(rx - x), dy = Math.Abs(ry - y), dz = Math.Abs(rz - z);
            if (dx > dy && dx > dz)
                rx = -ry - rz;
            else if (dy > dz)
                ry = -rx - rz;
            else
                rz = -rx - ry;
            int row = (int)rz;
            int col = (int)rx + (row - (row & 1)) / 2;
            return new HexCoord(col, row);
        }
    }
}
