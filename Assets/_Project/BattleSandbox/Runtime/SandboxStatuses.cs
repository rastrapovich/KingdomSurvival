using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Пакет «Боевые состояния и пассивные теги» (20.11.2026): единые временные
    // состояния бойца. Определение неизменяемо и хранит только данные
    // (параметризованные операции общих семейств); исполнение — в
    // SandboxBattle.Statuses. Тексты каталога не разбираются в runtime.
    public enum SandboxStatusPolarity
    {
        Harmful,
        Beneficial,
        Mixed
    }

    public enum SandboxStatusClock
    {
        // N собственных активаций цели, начавшихся после наложения; снятие в конце последней.
        OwnActivations,
        // До начала следующей собственной активации (Краткая неуязвимость).
        UntilOwnActivationStart,
        // N активаций источника (Мучительная связь, Метка добычи, Вызов).
        SourceActivations,
        // Срабатывание в начале раунда R+N (Отложенный взрыв, Приговор).
        RoundTrigger,
        // До снятия, гибели или конца боя (Повреждена нога).
        UntilRemoved,
        // Состояние-представление существующей связи/флага (паразит, корни,
        // захват, стойка): срок и снятие — у прежнего механизма.
        LegacyFacade
    }

    public enum SandboxStatusStacking
    {
        // Один экземпляр: повтор не усиливает и не продлевает.
        Single,
        // Заряды с отдельными сроками до MaxCharges; новый заряд не продлевает старые.
        Charges
    }

    [Flags]
    public enum SandboxStatusCleanse
    {
        None = 0,
        Self = 1,
        Ally = 2
    }

    public enum SandboxDamageType
    {
        Physical,
        Fire,
        Cold,
        Poison,
        Supernatural
    }

    public enum SandboxStatusOpKind
    {
        // Модификаторы характеристик (сумма от состояний — в пределах SandboxStatusRules).
        AttackMod,
        DefenseMod,
        MovementMod,
        InitiativeMod,
        MaxHitPointsMod,
        BaseDamageMod,
        // Периодические.
        TickDamage,
        TickHeal,
        // Ограничения действий.
        NoVoluntaryMove,
        SkipActivation,
        NoRetaliation,
        NoOffense,
        NoRangedAttack,
        RangedAdjacentOnly,
        CannotTargetSource,
        CannotApproachSource,
        MustTargetSource,
        FleeFromSource,
        NoPositiveAttack,
        CannotBeTargeted,
        // Урон и защита.
        AbsorbHit,
        TemporaryHitPoints,
        Invulnerable,
        IncomingFlat,
        OutgoingFlat,
        IncomingAttackMod,
        Vampirism,
        ContactRetort,
        // Реакции на события.
        RemoveOnHpDamage,
        RemoveOnDirectHpHit,
        RemoveOnDamageFromSourceSide,
        // Именованный исполнитель (см. SandboxStatusExecutors).
        Special
    }

    public enum SandboxStatusFilter
    {
        Any,
        Melee,
        Ranged,
        VsSource,
        VsOthers,
        Physical,
        Fire,
        // Только пока источник видим в пределах 3 гексов (Страх).
        WhileSourceNear,
        // Пока HP ≤ половины / ≤ четверти исходного максимума (Сила от ран).
        WoundThreshold,
        // Первое попадание источника в каждой его активации (Метка добычи).
        SourceOncePerSourceTurn,
        // Первое попадание врага между двумя собственными ходами (Рваная рана).
        FirstBetweenOwnTurns
    }

    public sealed class SandboxStatusOp
    {
        public SandboxStatusOpKind Kind { get; }
        public int Value { get; }
        public SandboxStatusFilter Filter { get; }
        // Операция действует, только когда зарядов не меньше (Иней: −1 движения при 2).
        public int MinCharges { get; }
        // Срабатывание расходует одно использование экземпляра.
        public bool ConsumesUse { get; }
        public SandboxDamageType DamageType { get; }
        public string Executor { get; }

        public SandboxStatusOp(SandboxStatusOpKind kind, int value = 0, SandboxStatusFilter filter = SandboxStatusFilter.Any,
            int minCharges = 0, bool consumesUse = false, SandboxDamageType damageType = SandboxDamageType.Physical, string executor = null)
        {
            Kind = kind;
            Value = value;
            Filter = filter;
            MinCharges = Math.Max(0, minCharges);
            ConsumesUse = consumesUse;
            DamageType = damageType;
            Executor = executor ?? string.Empty;
        }
    }

    public sealed class SandboxStatusDefinition
    {
        private readonly List<SandboxStatusOp> ops = new List<SandboxStatusOp>();
        private readonly List<string> immuneTo = new List<string>();

        public string Id { get; }
        public string Name { get; }
        public string Category { get; }
        public SandboxStatusPolarity Polarity { get; }
        public SandboxStatusClock Clock { get; internal set; } = SandboxStatusClock.OwnActivations;
        public int Duration { get; internal set; } = 1;
        public SandboxStatusStacking Stacking { get; internal set; } = SandboxStatusStacking.Single;
        public int MaxCharges { get; internal set; } = 1;
        // Расходуемые использования одного экземпляра (Роковая метка — 2 попадания).
        public int Uses { get; internal set; }
        public bool StrongControl { get; internal set; }
        public SandboxStatusCleanse Cleanse { get; internal set; }
        public string CleanseVerb { get; internal set; } = "снять";
        public bool RemovedOnSourceDeath { get; internal set; }
        public bool Supernatural { get; internal set; }
        public string IconId => Id;
        public IReadOnlyList<SandboxStatusOp> Ops => ops;
        public IReadOnlyCollection<string> ImmuneTo => immuneTo;
        // Не подключённый исполнитель: причина в отчёте покрытия, а не тихий no-op.
        public string PendingReason { get; internal set; } = string.Empty;
        public bool IsExecutable => string.IsNullOrEmpty(PendingReason);

        public SandboxStatusDefinition(string id, string name, string category, SandboxStatusPolarity polarity)
        {
            Id = id;
            Name = name;
            Category = category;
            Polarity = polarity;
        }

        internal SandboxStatusDefinition Add(SandboxStatusOp op)
        {
            ops.Add(op);
            return this;
        }

        internal void AddImmunity(string statusId) => immuneTo.Add(statusId);

        public bool Has(SandboxStatusOpKind kind) => ops.Any(op => op.Kind == kind);
        public bool HasSpecial(string executor) => ops.Any(op => op.Kind == SandboxStatusOpKind.Special && op.Executor == executor);
        // Вредно ли для носителя (очищение союзником, приоритет показа). Задаётся
        // каталогом явно: «Смешанное» в источнике бывает и усилением, и порчей.
        public bool IsHarmful { get; internal set; }
    }

    // Один заряд со своим сроком (Кровотечение, Иней, Набранный темп).
    public sealed class SandboxStatusCharge
    {
        public int AppliedAtActivation { get; internal set; }
        public int Elapsed { get; internal set; }
    }

    public sealed class SandboxStatusInstance
    {
        internal readonly List<SandboxStatusCharge> charges = new List<SandboxStatusCharge>();

        public string InstanceId { get; internal set; }
        public SandboxStatusDefinition Definition { get; internal set; }
        public string StatusId => Definition.Id;
        public string SourceUnitId { get; internal set; }
        public string TargetUnitId { get; internal set; }
        public int Sequence { get; internal set; }
        public int AppliedRound { get; internal set; }
        // Активация цели в момент наложения: она сама срок не сокращает.
        public int AppliedAtActivation { get; internal set; }
        public int AppliedAtSourceActivation { get; internal set; }
        public int Elapsed { get; internal set; }
        public int UsesLeft { get; internal set; }
        // Накопленные значения (ступени Ужаса, запомненное попадание Эха).
        public int Stacks { get; internal set; }
        public int StoredValue { get; internal set; }
        public int TemporaryHitPoints { get; internal set; }
        public int FirstTickBonus { get; internal set; }
        public int LastTriggerActivation { get; internal set; } = -1;
        public int TriggerRound { get; internal set; }
        public string ProfileId { get; internal set; } = string.Empty;
        // Параметры профиля источника (явно разрешённые отличия), 0 — по определению.
        public int ProfileDuration { get; internal set; }
        public int ProfileTick { get; internal set; }
        public IReadOnlyList<SandboxStatusCharge> Charges => charges;
        public int ChargeCount => Definition.Stacking == SandboxStatusStacking.Charges ? charges.Count : 1;
        public int EffectiveDuration => ProfileDuration > 0 ? ProfileDuration : Definition.Duration;
    }

    // Профиль конкретного источника: один statusId и одна иконка, другие числа.
    public sealed class SandboxStatusProfile
    {
        public string ProfileId { get; set; } = string.Empty;
        public int Duration { get; set; }
        public int Tick { get; set; }
        public bool Supernatural { get; set; }
    }

    // Общие правила каталога (§17 пакета) — редактируемые настройки баланса.
    public static class SandboxStatusRules
    {
        public static int MaxTickDamagePerActivationStart = 3;
        public static int AttackModCap = 2;
        public static int DefenseModCap = 2;
        public static int MovementModCap = 2;
        public static int InitiativeModCap = 2;
        public static int MaxFlatReduction = 1;
        public static int MaxFlatBonus = 2;
        public static int FearRadius = 3;
        public static int LinkBreakDistance = 3;
        public static int DrainLinkBreakDistance = 4;
        public static int PanicRetreat = 2;
        public static int MaxEventChain = 64;

        public static int Clamp(int value, int cap) => Math.Max(-cap, Math.Min(cap, value));
    }

    // Наблюдаемое представление: UI берёт числа отсюда, отдельной симуляции нет.
    public sealed class SandboxStatusView
    {
        public string StatusId { get; set; }
        public string Name { get; set; }
        public string IconId { get; set; }
        public SandboxStatusPolarity Polarity { get; set; }
        public bool StrongControl { get; set; }
        public int Priority { get; set; }
        public int Sequence { get; set; }
        // Оставшиеся собственные ходы (−1 — зависит от связи/источника).
        public int TurnsLeft { get; set; }
        public int Charges { get; set; }
        public int TriggerRound { get; set; }
        public bool Dependent { get; set; }
        public string SourceLabel { get; set; } = string.Empty;
        public string Effect { get; set; } = string.Empty;
        public string Removal { get; set; } = string.Empty;
        public List<int> ChargeTurnsLeft { get; } = new List<int>();
        public string Tooltip
        {
            get
            {
                string time = TriggerRound > 0 ? "Срабатывает в начале раунда " + TriggerRound + "."
                    : Dependent ? "Пока действует связь."
                    : TurnsLeft > 0 ? "Осталось собственных ходов: " + TurnsLeft + "." : string.Empty;
                string charges = ChargeTurnsLeft.Count > 1
                    ? " Заряды: " + string.Join(", ", ChargeTurnsLeft.Select(left => left + " х.")) + "."
                    : Charges > 1 ? " Заряды: " + Charges + "." : string.Empty;
                return Name + ". " + Effect + " " + time + charges +
                       (string.IsNullOrEmpty(SourceLabel) ? string.Empty : " Источник: " + SourceLabel + ".") +
                       (string.IsNullOrEmpty(Removal) ? string.Empty : " " + Removal);
            }
        }
    }
}
