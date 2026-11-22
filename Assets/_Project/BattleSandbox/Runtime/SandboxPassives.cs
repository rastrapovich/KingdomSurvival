using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Пакет «Пассивные теги» (20.11.2026): постоянный тег бойца — правило
    // «условие → событие → эффект → лимит», никогда не кнопка. Определение —
    // данные; исполнение — SandboxBattle.Passives. Тег назначается обычным
    // ID в tagIds Базы существ.
    public enum SandboxPassiveKind
    {
        // Постоянные расчёты (предпросмотр и удар считают одинаково).
        AttackMod,
        DefenseMod,
        IncomingAttackMod,
        OutgoingFlat,
        IncomingFlat,
        IncomingPercent,
        IgnoreDefense,
        InitiativeMod,
        AuraAllyAttack,
        AuraEnemyAttack,
        AuraEnemyDefense,
        AuraEnemyInitiative,
        AuraAllyMovementStart,
        AuraAllyGuardStrongControlled,
        Retaliations,
        NoProvokedRetaliation,
        NoMeleeRetaliationAfterMove,
        NoMeleeRetaliationAfterShot,
        // Состояния.
        StatusImmunity,
        StatusMaxDuration,
        StatusDurationDelta,
        StatusCancelOnce,
        // События.
        OnHitApplyStatus,
        OnHitTakenApplyStatusToAttacker,
        OnHitSplashAdjacentToTarget,
        OnHitSplashBehindTarget,
        OnHitSplashFlank,
        OnHitHealSelf,
        OnDeathDamage,
        OnDeathHealAllies,
        OnDeathApplyStatus,
        OnDeathDefenseToAlly,
        OnDeathAttackToAlly,
        OnKillApplyStatusNearestEnemy,
        OnKillMaxHitPoints,
        OnKillOutgoingNextTurn,
        OnKillStepIntoCell,
        CheatDeathOnce,
        BelowHalfOnce,
        ActivationStartHeal,
        ActivationStartHealAlly,
        ActivationEndHeal,
        ActivationEndHealPercent,
        ActivationEndRestHeal,
        ActivationEndRestSleep,
        ActivationEndRestCleanse,
        ActivationStartStatusDecay,
        MovementBonusAfterNoAttack,
        EnterAdjacentDamage,
        LeaveAdjacentCost,
        PushReduction,
        // Накопления (счётчик на бойце).
        StackDefensePerHitTaken,
        StackReductionPerDamageTaken,
        StackOutgoingPerAttack,
        StackAttackAfterMiss,
        ChargeAttackAfterDamageTaken,
        StackAttackAfterAlliesFell,
        FirstMeleeHitPerRoundDefense,
        // Реакция на событие состояния (этап 2 брифа завершения).
        StatusReaction,
        // Правила в конкретной точке расчёта (числа — в Value/StatusIds).
        StatusExtraDamageReduceOnce,
        BasePenaltyIgnoreOnce,
        MarkConsumeOncePerAction,
        DelayedTriggerReduceOnce,
        PainEchoReduceOnce,
        DrainLinkShortRange,
        SharedWoundsSurviveOnce,
        ShortenTargetBuffOnHit,
        ReflectImmunityOnce,
        RetortReduceOncePerRound,
        ShieldExtendIfWaited,
        TempPoolExhaustedRegen,
        NoVampirismWhileStatus,
        WoundStrengthExtend,
        SupportRefundMovement,
        RetaliationIgnoresStatus,
        VowReduceWhileStatus,
        GuardStartShortenStatus,
        AllyGuardExtinguish,
        WakeAdjacentSleeper,
        StartRemoveMagicalStatus,
        GrappleBreakOnce,
        HolderImmobilizedView,
        RootsApplyStatusOnce,
        FearBreaksGrapple,
        // Этапы 3–4: лечение, магия, типы урона.
        HealOtherBonus,
        HealReceivedBonus,
        HealReceivedPenaltyWhileStatus,
        OnHealOther,
        HealOnDispelMagical,
        SpellCastSelfStatus,
        AreaSpellSparesAllies,
        OnKillHealSelf,
        SparkCharge,
        MagicRestoreOnHit,
        MagicStealAtStart,
        PierceFlatReduction,
        ColdHitRimeCell,
        ColdHitMarkNext,
        ElectricWaterImmune,
        // Этапы 5–6: реакции на движение и несколько попаданий.
        LeaveAdjacencyStrike,
        HitAndRun,
        FollowRetreatingEnemy,
        AvengeAdjacentAlly,
        BlockRetort,
        RetaliateFirst,
        StrikeAndReturn,
        MoveAfterKill,
        SecondShot,
        SecondMeleeHit,
        // Этапы 7–12: окружение, спасение, призывы, гибель.
        MomentumBreaksRing,
        RescuedTemporaryPool,
        GrowOnAllySummon,
        SplitIntoBrood,
        DelayedDeathBlast,
        LeaveRemains,
        ConsumeRemains,
        SharedFateLink,
        // Этап 13: факты способностей (тело, оружие, местность, свет, снаряды).
        OutgoingPercent,
        AuraAllyDefense,
        AuraAllyRegen,
        RetortWeakenOnHit,
        FlyOverLowObstacles,
        ObstacleDamageMultiplier,
        FlankBonusReduction,
        IgnoreDarkness,
        StatusAttackPenaltyIgnore,
        DodgeDefense,
        DodgeReduction,
        ForestStride,
        MaxHitPointsPercent,
        BreakWeapon,
        IgnoreFatigue,
        DestroyAmmo,
        RootBindOnHit,
        ReflectEvenRounds,
        AmbushFirstActivation,
        ForceShell,
        StickyTrailAfterShift,
        QuietPhase,
        // Прежнее правило под другим ID (Стая, ходьба по местности).
        Alias,
        // Политика без собственного эффекта (совпадает с общим правилом).
        Policy
    }

    public enum SandboxPassiveCondition
    {
        None,
        Melee,
        Ranged,
        TargetIsolated,
        TargetHpHalf,
        TargetHp30,
        NoLivingAllies,
        AlliesWithin2AtLeast3,
        NoAlliesWithin2,
        TargetMaxHpHalfOfMine,
        TargetTag,
        SelfHpHalf,
        SelfHpQuarter,
        SelfHp20,
        SelfFullHp,
        FirstRound,
        TwoEnemiesAdjacentToSelf,
        TwoOtherAlliesNearTarget,
        AllyOppositeTarget,
        TargetHasStatus,
        TargetArmored,
        SelfOnHill,
        SelfOnShallowWater,
        TargetNotActedThisRound,
        TargetLiving,
        NotAttackedSinceMove,
        TargetHeldBySelfVine,
        AttackerTag,
        DamageAtLeastFifthOfTargetMax,
        NotArmoredSelf,
        RememberedAttacker,
        AvengedKiller,
        FirstAttackOnTarget,
        SelfHasStatus,
        SelfIsLinkPartner,
        Retaliation,
        FireDamage,
        ColdDamage,
        MagicalHit,
        NonMagical,
        // Этап 13.
        SelfInForest,
        SelfInThicket,
        BeyondHalfRange,
        EvenRound,
        FirstOwnActivation,
        SelfHpBelowHalf,
        Dark,
        // Свой ход, не меньше трёх шагов по прямой.
        StraightRun
    }

    // Событие состояния для реакции тега.
    public enum SandboxReactionEvent
    {
        Attempt,
        Applied,
        ChargeAdded,
        Tick,
        Ended
    }

    // Чьё событие слушает носитель.
    public enum SandboxReactionScope
    {
        Self,
        // Союзник носителя в радиусе Value2 (не сам носитель).
        AllyNear,
        // Любой боец, на которого состояние наложил сам носитель.
        AppliedByOwner,
        // Состояние, источник которого — носитель (метка носителя на ком угодно).
        OwnerIsSource,
        // Любой боец (условие отбирает нужное).
        Anyone
    }

    public enum SandboxReactionEffect
    {
        Cancel,
        Replace,
        ApplyToSelf,
        ApplyToEventTarget,
        ApplyToEventActor,
        ApplyToNearestAlly,
        RemoveFromSelf,
        RemoveFromEventTarget,
        SetDuration,
        AddCharge,
        HealSelf,
        DamageEventActor,
        ImmuneSameStatusSameSource,
        ImmuneSameStatus,
        ShortenSelfStatus,
        RemoveOneChargeFromSelf,
        // Заряд +Value Атаки к следующему удару, до конца следующего своего хода.
        GrantAttackCharge
    }

    public enum SandboxPassiveLimit
    {
        None,
        PerOwnTurn,
        PerRound,
        PerBattle,
        PerTargetPerBattle,
        PerTargetPerRound,
        EveryNth
    }

    public sealed class SandboxPassiveRule
    {
        public SandboxPassiveKind Kind { get; internal set; }
        public int Value { get; internal set; }
        public int Value2 { get; internal set; }
        public SandboxPassiveCondition Condition { get; internal set; }
        public string ConditionParam { get; internal set; } = string.Empty;
        public SandboxPassiveCondition Condition2 { get; internal set; }
        public SandboxPassiveLimit Limit { get; internal set; }
        public int LimitN { get; internal set; }
        public string[] StatusIds { get; internal set; } = Array.Empty<string>();
        public SandboxStatusProfile Profile { get; internal set; }
        public string AliasOf { get; internal set; } = string.Empty;
        // Реакции на состояния.
        public SandboxReactionEvent ReactEvent { get; internal set; }
        public SandboxReactionScope Scope { get; internal set; }
        public SandboxReactionEffect Effect { get; internal set; }
        // Причины окончания, на которые реагирует правило (пусто — любые).
        public SandboxStatusEndReason[] EndReasons { get; internal set; } = Array.Empty<SandboxStatusEndReason>();
        // Состояние-результат эффекта (наложить/снять/заменить на).
        public string EffectStatus { get; internal set; }
        public SandboxStatusProfile EffectProfile { get; internal set; }
        // Носитель должен иметь / не иметь состояние; цель события должна иметь.
        public string OwnerRequires { get; internal set; }
        public string OwnerLacks { get; internal set; }
        public string[] EventTargetRequiresAny { get; internal set; } = Array.Empty<string>();
        public string Key { get; internal set; } = string.Empty;
    }

    public sealed class SandboxPassiveDefinition
    {
        private readonly List<SandboxPassiveRule> rules = new List<SandboxPassiveRule>();
        public string TagId { get; }
        public string Name { get; }
        // Семья: одинаковые семьи не суммируются с собой (ауры, alias).
        public string Family { get; internal set; }
        public IReadOnlyList<SandboxPassiveRule> Rules => rules;
        public string Summary { get; internal set; } = string.Empty;

        public SandboxPassiveDefinition(string tagId, string name)
        {
            TagId = tagId;
            Name = name;
            Family = tagId;
        }

        internal void Add(SandboxPassiveRule rule)
        {
            rule.Key = TagId + "#" + rules.Count;
            rules.Add(rule);
        }
    }

    // Счётчики конкретного бойца: определение неизменяемо, у каждого бойца свои.
    public sealed class SandboxPassiveRuntime
    {
        internal readonly Dictionary<string, int> LastTurn = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> LastRound = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly HashSet<string> UsedInBattle = new HashSet<string>(StringComparer.Ordinal);
        internal readonly Dictionary<string, HashSet<string>> UsedOnTargets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> Counters = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> Stacks = new Dictionary<string, int>(StringComparer.Ordinal);
        public string RememberedAttackerId { get; internal set; }
        public HashSet<string> KillersOfAllies { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> AttackedTargets { get; } = new HashSet<string>(StringComparer.Ordinal);
        internal int GrantedDefense;
        internal int GrantedAttackUntilActivation = -1;
        internal int GrantedAttack;
        internal int BonusMaxHitPoints;
        internal int OutgoingNextTurnActivation = -1;
        internal int OutgoingNextTurnValue;
        internal int ChargedAttack;
        internal int ChargedAttackExpires = -1;
        internal bool TookDamageThisTurn;
        internal bool AttackedThisTurn;
        internal bool ShotThisTurn;
        internal bool AttackedSinceMove = true;
        internal bool BelowHalfDone;
        // Невосприимчивость, полученная реакциями: statusId или statusId@sourceId.
        internal readonly HashSet<string> GrantedImmunities = new HashSet<string>(StringComparer.Ordinal);
        // Чародейский запас (если задан в Базе существ), искры, метка холода.
        public int MagicSpent { get; internal set; }
        public int Sparks { get; internal set; }
        internal string ColdMarkTargetId;
        internal int ColdMarkExpires = -1;
        internal int ColdMarkValue;
        // Реакции и последовательности: цели удара в этот ход, корень второго попадания,
        // позиция начала хода, движение после убийства.
        internal readonly HashSet<string> HitAndRunTargets = new HashSet<string>(StringComparer.Ordinal);
        internal int FollowUpRoot = -1;
        internal HexCoord ActivationStartPosition;
        public int MoveAfterActionBudget { get; internal set; }
        // «При смерти»: одно спасение за бой, начало окна, окончательная смерть.
        public bool RescueUsed { get; internal set; }
        internal int DyingSinceActivation;
        internal bool ForceFinalDeath;
        // Этап 13: ослабленный ответ, связь корней, силовой покров, снаряды.
        internal int RetortPenalty;
        internal int RetortPenaltyUntilActivation = -1;
        internal string RootBindTargetId;
        public int ShellCharges { get; internal set; }
        public int StraightSteps { get; internal set; }
        internal int LastStepDirection = -1;
        public int Counter(string key) => Counters.TryGetValue(key, out int value) ? value : 0;
        public int Stack(string key) => Stacks.TryGetValue(key, out int value) ? value : 0;
    }

    public static partial class SandboxPassiveCatalog
    {
        private static readonly Dictionary<string, SandboxPassiveDefinition> byId = new Dictionary<string, SandboxPassiveDefinition>(StringComparer.Ordinal);
        private static readonly List<SandboxPassiveDefinition> all = new List<SandboxPassiveDefinition>();

        public static IReadOnlyList<SandboxPassiveDefinition> All => all;

        public static SandboxPassiveDefinition Get(string tagId)
        {
            return tagId != null && byId.TryGetValue(tagId, out SandboxPassiveDefinition def) ? def : null;
        }

        // Есть ли у набора тегов alias, ведущий к runtimeId (Стая, ходьба по местности).
        public static bool IsAliased(IEnumerable<string> tagIds, string runtimeId)
        {
            foreach (string tagId in tagIds)
            {
                SandboxPassiveDefinition def = Get(tagId);
                if (def == null)
                    continue;
                foreach (SandboxPassiveRule rule in def.Rules)
                {
                    if (rule.Kind == SandboxPassiveKind.Alias && rule.AliasOf == runtimeId)
                        return true;
                }
            }
            return false;
        }

        // Нормализованные теги бойца: legacy alias и новый ID ведут к одному
        // правилу и исполняются один раз (семья без повторов).
        public static IEnumerable<SandboxPassiveDefinition> Of(SandboxUnitState unit)
        {
            if (unit == null)
                yield break;
            HashSet<string> families = null;
            foreach (string tagId in unit.Definition.TagIds)
            {
                SandboxPassiveDefinition def = Get(tagId);
                if (def == null)
                    continue;
                families ??= new HashSet<string>(StringComparer.Ordinal);
                if (families.Add(def.Family))
                    yield return def;
            }
        }

        private sealed class B
        {
            public SandboxPassiveDefinition Def;

            public B R(SandboxPassiveKind kind, int value = 0, SandboxPassiveCondition when = SandboxPassiveCondition.None, string param = null,
                SandboxPassiveLimit limit = SandboxPassiveLimit.None, int n = 0, string status = null, SandboxStatusProfile profile = null,
                int value2 = 0, SandboxPassiveCondition when2 = SandboxPassiveCondition.None, string[] statuses = null)
            {
                Def.Add(new SandboxPassiveRule
                {
                    Kind = kind, Value = value, Value2 = value2, Condition = when, Condition2 = when2, ConditionParam = param ?? string.Empty,
                    Limit = limit, LimitN = n, Profile = profile,
                    StatusIds = statuses ?? (status != null ? new[] { status } : Array.Empty<string>())
                });
                return this;
            }

            public B Heal(SandboxPassiveKind kind, SandboxReactionEffect effect, string effectStatus = null, SandboxPassiveLimit limit = SandboxPassiveLimit.None,
                int value = 0, int range = 0, SandboxStatusProfile profile = null)
            {
                Def.Add(new SandboxPassiveRule
                {
                    Kind = kind, Effect = effect, EffectStatus = effectStatus, Limit = limit, Value = value, Value2 = range, EffectProfile = profile
                });
                return this;
            }

            public B React(SandboxReactionEvent on, string[] statuses, SandboxReactionEffect effect, SandboxReactionScope scope = SandboxReactionScope.Self,
                SandboxPassiveLimit limit = SandboxPassiveLimit.None, string effectStatus = null, SandboxStatusProfile effectProfile = null,
                int value = 0, int range = 0, string ownerRequires = null, string ownerLacks = null, string[] targetRequiresAny = null,
                SandboxStatusEndReason[] reasons = null)
            {
                Def.Add(new SandboxPassiveRule
                {
                    Kind = SandboxPassiveKind.StatusReaction, ReactEvent = on, StatusIds = statuses, Effect = effect, Scope = scope, Limit = limit,
                    EffectStatus = effectStatus, EffectProfile = effectProfile, Value = value, Value2 = range, OwnerRequires = ownerRequires,
                    OwnerLacks = ownerLacks, EventTargetRequiresAny = targetRequiresAny ?? Array.Empty<string>(),
                    EndReasons = reasons ?? Array.Empty<SandboxStatusEndReason>()
                });
                return this;
            }

            public B Alias(string runtimeId)
            {
                Def.Family = runtimeId;
                Def.Add(new SandboxPassiveRule { Kind = SandboxPassiveKind.Alias, AliasOf = runtimeId });
                return this;
            }

            public B Family(string family)
            {
                Def.Family = family;
                return this;
            }
        }

        private static B T(string tagId, string name)
        {
            if (byId.ContainsKey(tagId))
                throw new InvalidOperationException("Повтор тега " + tagId);
            SandboxPassiveDefinition def = new SandboxPassiveDefinition(tagId, name);
            byId[tagId] = def;
            all.Add(def);
            return new B { Def = def };
        }

        private static SandboxStatusProfile P(string id, int duration = 0, int tick = 0, int value = 0, bool refresh = false,
            bool battle = false, int maxCharges = 0, string mode = null)
        {
            return new SandboxStatusProfile
            {
                ProfileId = id, Duration = duration, Tick = tick, Value = value, RefreshSameSource = refresh,
                UntilBattleEnd = battle, MaxCharges = maxCharges, Mode = mode ?? string.Empty
            };
        }
    }
}
