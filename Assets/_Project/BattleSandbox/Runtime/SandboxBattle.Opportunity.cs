using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф завершения, этапы 5–6: реакции на уход из соседства и несколько
    // попаданий одного действия. Реакция есть только у носителя тега; она
    // разрешается до пересечения ребра, не вызывает ответа на ответ и тратит
    // общий бюджет реакций носителя (ёмкость — его ответные удары за раунд).
    public sealed partial class SandboxBattle
    {
        // Может ли боец сейчас ответить реакцией (не контроль, не вне поля).
        private bool CanReact(SandboxUnitState unit)
        {
            return unit != null && !unit.IsDefeated && !unit.IsOffBoard && !unit.IsHidden &&
                   !StatusHas(unit, SandboxStatusOpKind.NoRetaliation) && !StatusHas(unit, SandboxStatusOpKind.SkipActivation) &&
                   !unit.HasRetaliatedThisRound;
        }

        private void SpendReaction(SandboxUnitState unit)
        {
            unit.RetaliationsThisRound++;
            unit.RetaliationsMade++;
        }

        // Удар-реакция: обычный ближний удар, isReaction, без ответа на него.
        private int ReactionStrike(SandboxUnitState striker, SandboxUnitState target, string label)
        {
            int damage = BuildAttackPreview(striker, target, striker.Position, retaliation: true).Damage;
            int before = target.HitPoints;
            RecordHit(striker, target, damage, striker.Position);
            ApplyHit(SandboxHitKind.Reaction, striker, target, damage);
            Log(striker.DisplayLabel + ": " + label + " — " + (before - target.HitPoints) + " урона " + target.DisplayLabel +
                (target.IsDefeated ? ", выведен из строя." : "."));
            return before - target.HitPoints;
        }

        // Перед шагом from → to: реакции врагов, из соседства которых уходит боец.
        // false — движение прервано (погиб, обездвижен реакцией).
        private bool ResolveLeaveReactions(SandboxUnitState mover, HexCoord from, HexCoord to)
        {
            foreach (SandboxUnitState reactor in units.Where(u => u.Team != mover.Team && u.Position.DistanceTo(from) == 1 && u.Position.DistanceTo(to) > 1)
                         .OrderBy(u => u.Id, StringComparer.Ordinal).ToList())
            {
                if (mover.IsDefeated)
                    return false;
                if (!CanReact(reactor))
                    continue;
                // «Бьёт и отступает»: атакованная в этот ход цель не бьёт за выход.
                if (mover.passive.HitAndRunTargets.Contains(reactor.Id) && HasPassive(mover, SandboxPassiveKind.HitAndRun))
                    continue;
                SandboxPassiveRule rule = ReadyRule(reactor, SandboxPassiveKind.LeaveAdjacencyStrike);
                if (rule == null)
                    continue;
                MarkUsed(reactor, rule, mover.Id);
                SpendReaction(reactor);
                int taken = ReactionStrike(reactor, mover, RuleName(rule) + " (удар за выход)");
                if (rule.Value > 0 && taken > 0 && !mover.IsDefeated)
                {
                    // «Бьёт уходящего по ногам»: оставшееся движение обнуляется.
                    mover.RemainingMovement = 0;
                    Log(mover.DisplayLabel + ": движение обнулено.");
                    return false;
                }
            }
            return !mover.IsDefeated;
        }

        // После добровольного перемещения: «Идёт следом» — шаг за ушедшим врагом.
        private void ResolvePursuers(SandboxUnitState mover, HexCoord origin)
        {
            foreach (SandboxUnitState pursuer in units.Where(u => !u.IsDefeated && !u.IsOffBoard && u.Team != mover.Team &&
                                                                  u.Position.DistanceTo(origin) == 1 && u.Position.DistanceTo(mover.Position) > 1)
                         .OrderBy(u => u.Id, StringComparer.Ordinal).ToList())
            {
                SandboxPassiveRule rule = ReadyRule(pursuer, SandboxPassiveKind.FollowRetreatingEnemy);
                if (rule == null || StrongControlled(pursuer) || StatusHas(pursuer, SandboxStatusOpKind.NoVoluntaryMove) || IsHeld(pursuer))
                    continue;
                int current = pursuer.Position.DistanceTo(mover.Position);
                HexCoord? step = pursuer.Position.Neighbors()
                    .Where(cell => IsFreeCell(cell) && cell.DistanceTo(mover.Position) < current && IsEnterableFor(pursuer, cell))
                    .OrderBy(cell => cell.DistanceTo(mover.Position)).ThenBy(cell => cell).Cast<HexCoord?>().FirstOrDefault();
                if (!step.HasValue)
                    continue;
                MarkUsed(pursuer, rule, mover.Id);
                SetUnitPosition(pursuer, step.Value);
                ApplyHazardsAt(pursuer, step.Value);
                Log(pursuer.DisplayLabel + ": " + RuleName(rule) + " — шаг следом.");
            }
        }

        // «Мстит за соседнего»: удар по врагу, ударившему соседнего союзника.
        private void ResolveAvengers(SandboxUnitState attacker, SandboxUnitState victim)
        {
            if (attacker == null || attacker.IsDefeated || victim == null)
                return;
            foreach (SandboxUnitState avenger in units.Where(u => u != victim && u.Team == victim.Team && u.Position.DistanceTo(attacker.Position) == 1)
                         .OrderBy(u => u.Id, StringComparer.Ordinal).ToList())
            {
                if (attacker.IsDefeated || !CanReact(avenger))
                    continue;
                SandboxPassiveRule rule = ReadyRule(avenger, SandboxPassiveKind.AvengeAdjacentAlly);
                if (rule == null)
                    continue;
                MarkUsed(avenger, rule, null);
                SpendReaction(avenger);
                ReactionStrike(avenger, attacker, RuleName(rule));
            }
        }

        // ---------- Несколько попаданий одного действия ----------

        private bool halfAttackHit;

        // Второе попадание обычной атаки («Два выстрела», «Два ближних удара»).
        // Одно действие: цена не повторяется, лимиты «за ход» общие.
        private string ResolveFollowUpHit(SandboxUnitState attacker, SandboxUnitState target, bool ranged)
        {
            if (attacker == null || target == null || attacker.IsDefeated || target.IsDefeated || Phase != SandboxBattlePhase.InProgress)
                return string.Empty;
            SandboxPassiveKind kind = ranged ? SandboxPassiveKind.SecondShot : SandboxPassiveKind.SecondMeleeHit;
            if (!HasPassive(attacker, kind) || attacker.passive.FollowUpRoot == currentRootAction)
                return string.Empty;
            int distance = attacker.Position.DistanceTo(target.Position);
            if ((ranged && (distance <= 1 || distance > attacker.AttackRange)) || (!ranged && distance != 1))
                return " Второе попадание сорвано: цель вне досягаемости.";
            attacker.passive.FollowUpRoot = currentRootAction;
            halfAttackHit = !ranged;
            int damage;
            try
            {
                damage = BuildAttackPreview(attacker, target, attacker.Position).Damage;
            }
            finally
            {
                halfAttackHit = false;
            }
            int before = target.HitPoints;
            RecordHit(attacker, target, damage, attacker.Position);
            ApplyHit(SandboxHitKind.Attack, attacker, target, damage);
            CombatEvents.Last().HitIndex = 1;
            return " Второе попадание" + (ranged ? "" : " (половина Атаки)") + ": " + (before - target.HitPoints) + " урона" +
                   (target.IsDefeated ? " — " + target.DisplayLabel + " выведен из строя." : ".");
        }
    }
}
