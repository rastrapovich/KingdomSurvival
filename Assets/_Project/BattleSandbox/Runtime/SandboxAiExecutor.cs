using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    public sealed partial class SandboxBattle
    {
        // Шиповик: один выстрел и отход в одной последовательности. Выстрел и
        // отход тратят один бюджет движения; второго выстрела нет.
        public bool TryShootAndRetreat(string unitId, string targetId, out string message)
        {
            SandboxUnitState unit = GetUnit(unitId);
            int budget = unit != null ? unit.RemainingMovement : 0;
            if (!TryAttack(unitId, targetId, out message))
                return false;
            if (unit.IsDefeated || Phase != SandboxBattlePhase.InProgress || HasPendingRetaliation)
                return true;
            HexCoord? retreat = SandboxAiPlanner.FindKiteRetreat(this, unit, budget);
            if (retreat.HasValue)
            {
                HexCoord from = unit.Position;
                SetUnitPosition(unit, retreat.Value);
                unit.MovedThisActivation = true;
                message += " " + unit.DisplayLabel + " отходит " + from + " → " + retreat.Value + ".";
            }
            return true;
        }
    }

    // Синхронное исполнение решения ИИ (тесты, бой без показа). Показ боя в
    // UI исполняет то же решение по шагам с анимацией.
    public static class SandboxAiExecutor
    {
        public static List<string> TakeTurn(SandboxBattle battle)
        {
            List<string> events = new List<string>();
            SandboxUnitState unit = battle?.CurrentUnit;
            if (unit == null)
                return events;
            string unitId = unit.Id;
            for (int pass = 0; pass < 2; pass++)
            {
                if (battle.Phase != SandboxBattlePhase.InProgress || battle.CurrentUnit == null || battle.CurrentUnit.Id != unitId)
                    break;
                SandboxAiDecision decision = SandboxAiPlanner.Plan(battle, unit, pass == 0);
                if (decision.MoveTo.HasValue && decision.MoveTo.Value != unit.Position)
                {
                    if (battle.TryMove(unitId, decision.MoveTo.Value, out string moveMessage))
                        events.Add(moveMessage);
                    if (decision.Action == SandboxAiActionKind.None)
                        continue;
                    if (battle.CurrentUnit == null || battle.CurrentUnit.Id != unitId)
                        break;
                }
                if (Execute(battle, unit, decision, events))
                    break;
                if (decision.Action == SandboxAiActionKind.None)
                    break;
            }
            Finish(battle, unitId, events);
            return events;
        }

        // true — действие выполнено (или осознанно пропущено).
        public static bool Execute(SandboxBattle battle, SandboxUnitState unit, SandboxAiDecision decision, List<string> events)
        {
            string message;
            bool done;
            switch (decision.Action)
            {
                case SandboxAiActionKind.Ability:
                    done = battle.TryUseAbility(unit.Id, decision.Command, out message);
                    break;
                case SandboxAiActionKind.Attack:
                    done = battle.TryAttack(unit.Id, decision.TargetId, out message);
                    break;
                case SandboxAiActionKind.ShootAndRetreat:
                    done = battle.TryShootAndRetreat(unit.Id, decision.TargetId, out message);
                    break;
                case SandboxAiActionKind.Context:
                    done = battle.TryContextAction(unit.Id, decision.Context, out message);
                    break;
                case SandboxAiActionKind.Wait:
                    events.Add(unit.DisplayLabel + ": " + decision.Reason);
                    return true;
                default:
                    return false;
            }
            if (!done)
                return false;
            events.Add(message);
            if (battle.HasPendingRetaliation && battle.TryResolvePendingRetaliation(out string retaliation))
                events.Add(retaliation);
            return true;
        }

        public static void Finish(SandboxBattle battle, string unitId, List<string> events)
        {
            SandboxUnitState unit = battle.GetUnit(unitId);
            if (battle.Phase == SandboxBattlePhase.InProgress && battle.CurrentUnit != null && battle.CurrentUnit.Id == unitId &&
                unit.ActionPoints > 0 && !unit.IsGuarding && !unit.IsHidden && !unit.IsOffBoard)
            {
                if (battle.TryGuard(unitId, out string guard))
                    events.Add(guard);
            }
            events.AddRange(battle.TakeEventLog());
            if (battle.Phase == SandboxBattlePhase.InProgress && battle.CurrentUnit != null && battle.CurrentUnit.Id == unitId)
                battle.EndActivation();
            events.AddRange(battle.TakeEventLog());
        }
    }
}
