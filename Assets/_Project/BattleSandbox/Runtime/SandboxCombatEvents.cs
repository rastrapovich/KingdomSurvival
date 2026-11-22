using System;
using System.Collections.Generic;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф завершения (22.11.2026), этап 1: единый контракт результата.
    // Каждое попадание, тик, лечение и переход состояния — одно событие с
    // корневым действием, исполнителем и фактическим результатом. Реакции
    // тегов, журнал, предпросмотр и тесты читают одно и то же; UI не хранит
    // второй источник истины.
    public enum SandboxCombatEventKind
    {
        HitResolved,
        StatusTick,
        HealingResolved,
        StatusApplicationAttempt,
        StatusApplied,
        StatusRefreshed,
        StatusChargeChanged,
        StatusRejected,
        StatusEnded,
        Downed,
        RescuePerformed,
        FinalDeath,
        ObjectDestroyed,
        Reaction
    }

    public enum SandboxStatusEndReason
    {
        Expired,
        CleansedByAction,
        CleansedByPassive,
        Consumed,
        BrokenByDamage,
        BrokenByDistance,
        SourceFinalDeath,
        OwnerFinalDeath,
        Replaced,
        BattleEnded
    }

    public enum SandboxDeliveryKind
    {
        Melee,
        Ranged,
        Spell,
        Area,
        Surface,
        Tick,
        Transfer,
        Retort,
        Heal
    }

    public sealed class SandboxCombatEvent
    {
        public int EventId { get; internal set; }
        public int RootActionId { get; internal set; }
        public int ParentEventId { get; internal set; }
        public SandboxCombatEventKind Kind { get; internal set; }
        public string SourceUnitId { get; internal set; }
        public string ActorUnitId { get; internal set; }
        public string TargetUnitId { get; internal set; }
        public string OriginalTargetId { get; internal set; }
        public SandboxDeliveryKind Delivery { get; internal set; }
        public SandboxDamageType DamageType { get; internal set; }
        public bool IsMagical { get; internal set; }
        public bool IsReaction { get; internal set; }
        public bool IsRetaliation { get; internal set; }
        public int HitIndex { get; internal set; }
        public int HitCount { get; internal set; } = 1;
        public int Requested { get; internal set; }
        public int AfterMitigation { get; internal set; }
        public int AbsorbedTemporary { get; internal set; }
        public int ActualHp { get; internal set; }
        public int ActualHeal { get; internal set; }
        public int Overheal { get; internal set; }
        public string StatusId { get; internal set; }
        public string StatusInstanceId { get; internal set; }
        public string ProfileId { get; internal set; }
        public SandboxStatusEndReason EndReason { get; internal set; }
        public string Note { get; internal set; } = string.Empty;
        public int Round { get; internal set; }
        public int ActivationId { get; internal set; }
    }

    public sealed partial class SandboxBattle
    {
        public const int MaxCombatEventLog = 4000;
        private readonly List<SandboxCombatEvent> combatEvents = new List<SandboxCombatEvent>();
        private int nextEventId = 1;
        private int nextRootActionId = 1;
        private int currentRootAction;

        public IReadOnlyList<SandboxCombatEvent> CombatEvents => combatEvents;
        public int CurrentRootActionId => currentRootAction;

        // Начало команды игрока/ИИ или начала хода: новое корневое действие.
        internal int BeginRootAction()
        {
            currentRootAction = nextRootActionId++;
            return currentRootAction;
        }

        internal SandboxCombatEvent Emit(SandboxCombatEventKind kind, SandboxUnitState source, SandboxUnitState target, Action<SandboxCombatEvent> fill = null)
        {
            SandboxCombatEvent e = new SandboxCombatEvent
            {
                EventId = nextEventId++,
                RootActionId = currentRootAction,
                Kind = kind,
                SourceUnitId = source?.Id,
                ActorUnitId = source?.Id,
                TargetUnitId = target?.Id,
                OriginalTargetId = target?.Id,
                Round = Round,
                ActivationId = CurrentUnit != null ? CurrentUnit.ActivationCount : 0
            };
            fill?.Invoke(e);
            combatEvents.Add(e);
            if (combatEvents.Count > MaxCombatEventLog)
                combatEvents.RemoveRange(0, combatEvents.Count - MaxCombatEventLog);
            return e;
        }
    }
}
