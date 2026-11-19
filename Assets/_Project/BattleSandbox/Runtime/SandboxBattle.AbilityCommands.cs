using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-16: единый исполнитель способностей. Для каждой команды один и тот
    // же расчёт даёт предпросмотр и исполнение; до подтверждения ничего не
    // тратится. Игрок и ИИ видят одни и те же кандидаты.
    public sealed partial class SandboxBattle
    {
        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        public sealed class AbilityCandidate
        {
            public SandboxAbilityCommand Command { get; set; }
            public SandboxAbilityPreview Preview { get; set; }
        }

        // ---------- Панель способностей ----------

        public List<SandboxAbilityState> GetAbilityStates(string unitId)
        {
            List<SandboxAbilityState> result = new List<SandboxAbilityState>();
            SandboxUnitState unit = GetUnit(unitId);
            if (unit == null)
                return result;
            foreach (SandboxAbilityDefinition ability in unit.Definition.Abilities.Where(item => item.IsActive))
            {
                SandboxAbilityState state = new SandboxAbilityState
                {
                    Ability = ability,
                    Label = AbilityLabel(unit, ability),
                    CooldownLeft = CooldownLeft(unit, ability),
                    NeedsSecondCell = ability.Kind == SandboxAbilityKind.WeaveThread,
                    NoTarget = IsNoTargetMode(unit, ability)
                };
                string gate = AbilityGate(unit, ability);
                if (string.IsNullOrEmpty(gate) && GetAbilityCandidates(unitId, ability.Id).Count == 0)
                    gate = NoCandidateReason(unit, ability);
                state.Available = string.IsNullOrEmpty(gate);
                state.Reason = gate ?? string.Empty;
                result.Add(state);
            }
            return result;
        }

        public int CooldownLeft(SandboxUnitState unit, SandboxAbilityDefinition ability)
        {
            if (unit == null || ability == null || !unit.AbilityReadyAt.TryGetValue(ability.Id, out int readyAt))
                return 0;
            int current = CurrentUnit == unit ? unit.ActivationCount : unit.ActivationCount + 1;
            return Math.Max(0, readyAt - current);
        }

        private bool IsLinkCooldownPending(SandboxUnitState unit, SandboxAbilityDefinition ability)
        {
            switch (ability.Kind)
            {
                case SandboxAbilityKind.Attach: return links.Any(item => item.SourceId == unit.Id && item.Kind == SandboxLinkKind.Attach);
                case SandboxAbilityKind.Engulf: return links.Any(item => item.SourceId == unit.Id && item.Kind == SandboxLinkKind.Engulf);
                default: return false;
            }
        }

        private string AbilityLabel(SandboxUnitState unit, SandboxAbilityDefinition ability)
        {
            switch (ability.Kind)
            {
                case SandboxAbilityKind.ChimeMark:
                    return HasTollReady(unit) ? "Удар звона" : ability.Title;
                case SandboxAbilityKind.VineGrab:
                    return links.Any(item => item.SourceId == unit.Id && item.Kind == SandboxLinkKind.Vine) ? "Подтянуть лозой" : ability.Title;
                case SandboxAbilityKind.Burrow:
                    return unit.CanStrikeAfterExit ? "Удар из-под земли" : ability.Title;
                case SandboxAbilityKind.PitCycle:
                    return unit.PitPhase == 1 ? "Подготовить провалы" : "Уйти под землю";
                default:
                    return ability.Title;
            }
        }

        private bool IsNoTargetMode(SandboxUnitState unit, SandboxAbilityDefinition ability)
        {
            return (ability.Kind == SandboxAbilityKind.ChimeMark && HasTollReady(unit)) ||
                   ability.Kind == SandboxAbilityKind.PitCycle;
        }

        private bool HasTollReady(SandboxUnitState unit)
        {
            return tollPreparedAt.TryGetValue(unit.Id, out int at) && at < unit.ActivationCount &&
                   telegraphs.Any(item => item.SourceId == unit.Id && item.Kind == SandboxTelegraphKind.Toll);
        }

        // Причина, по которой способность сейчас недоступна; пусто — можно.
        private string AbilityGate(SandboxUnitState unit, SandboxAbilityDefinition ability)
        {
            if (Phase != SandboxBattlePhase.InProgress)
                return "Бой завершён.";
            if (CurrentUnit != unit)
                return "Сейчас ход другого участника.";
            if (HasPendingRetaliation)
                return "Сначала должен завершиться ответный удар.";
            if (unit.ActionPoints <= 0 || unit.HasAttacked)
                return "Действие уже потрачено.";
            if (unit.IsAttached)
                return "Сидит на жертве — отдельно не действует.";
            if (unit.AwaitingPlacement)
                return "Ждёт места на поле.";
            if (unit.IsStuckInTunnel)
                return "Зажат в тоннеле.";
            if (unit.IsUnderground && ability.Kind != SandboxAbilityKind.PitCycle)
                return "Под землёй.";
            bool special = (ability.Kind == SandboxAbilityKind.ChimeMark && HasTollReady(unit)) ||
                           (ability.Kind == SandboxAbilityKind.Burrow && unit.CanStrikeAfterExit) ||
                           (ability.Kind == SandboxAbilityKind.VineGrab && links.Any(item => item.SourceId == unit.Id && item.Kind == SandboxLinkKind.Vine)) ||
                           ability.Kind == SandboxAbilityKind.PitCycle || ability.Kind == SandboxAbilityKind.TendrilGrab;
            if (!special)
            {
                if (IsLinkCooldownPending(unit, ability))
                    return "Сейчас удерживает жертву.";
                int left = CooldownLeft(unit, ability);
                if (left > 0)
                    return "Перезарядка: ещё " + left + " " + Activations(left) + ".";
            }
            if (ability.Kind == SandboxAbilityKind.PitCycle && justEmerged.ContainsKey(unit.Id))
                return "Только что вышел — до следующего хода остаётся на поверхности.";
            if (ability.Kind == SandboxAbilityKind.ConsumeCorpse && unit.HitPoints >= unit.MaxHitPoints)
                return "Здоровье полное — тело не нужно.";
            if (ability.Kind == SandboxAbilityKind.WeaveThread &&
                objects.Count(item => item.Kind == SandboxObjectKind.Thread && !item.IsDestroyed && item.OwnerId == unit.Id) >= Math.Max(1, ability.Limit))
                return "Уже натянуто " + ability.Limit + " нити.";
            if (ability.Kind == SandboxAbilityKind.LayEgg &&
                objects.Count(item => item.Kind == SandboxObjectKind.Egg && !item.IsDestroyed && item.OwnerId == unit.Id) >= Math.Max(1, ability.Limit))
                return "Уже " + ability.Limit + " невылупившихся яйца.";
            return string.Empty;
        }

        private static string Activations(int n)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return "ход";
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return "хода";
            return "ходов";
        }

        private string NoCandidateReason(SandboxUnitState unit, SandboxAbilityDefinition ability)
        {
            switch (ability.Kind)
            {
                case SandboxAbilityKind.Pounce: return "Нет цели с свободным приземлением в " + ability.PathMin + "–" + ability.PathMax + " гексах прыжка.";
                case SandboxAbilityKind.Charge: return "Нет свободной прямой для разгона перед целью.";
                case SandboxAbilityKind.StunSpit:
                case SandboxAbilityKind.Call:
                case SandboxAbilityKind.VineGrab:
                case SandboxAbilityKind.TendrilGrab:
                case SandboxAbilityKind.ChimeMark: return "Нет видимой цели в дальности.";
                case SandboxAbilityKind.Burrow: return "Нет копаемого грунта для выхода.";
                case SandboxAbilityKind.ConsumeCorpse: return "Рядом нет тела.";
                case SandboxAbilityKind.WeaveThread: return "Нет опор для нити.";
                case SandboxAbilityKind.LayEgg: return "Рядом нет свободной клетки или достигнут лимит.";
                default: return "Нет подходящей цели рядом.";
            }
        }

        // ---------- Кандидаты ----------

        public List<AbilityCandidate> GetAbilityCandidates(string unitId, string abilityId)
        {
            List<AbilityCandidate> result = new List<AbilityCandidate>();
            SandboxUnitState unit = GetUnit(unitId);
            SandboxAbilityDefinition ability = unit?.Definition.Abilities.FirstOrDefault(item => item.Id == abilityId);
            if (unit == null || ability == null || !ability.IsActive || !string.IsNullOrEmpty(AbilityGate(unit, ability)))
                return result;
            foreach (SandboxAbilityCommand command in EnumerateCommands(unit, ability))
            {
                SandboxAbilityPreview preview = Evaluate(unit, ability, command);
                if (preview.IsValid)
                    result.Add(new AbilityCandidate { Command = command, Preview = preview });
            }
            return result;
        }

        public SandboxAbilityPreview PreviewAbility(string unitId, SandboxAbilityCommand command)
        {
            SandboxUnitState unit = GetUnit(unitId);
            SandboxAbilityDefinition ability = unit?.Definition.Abilities.FirstOrDefault(item => command != null && item.Id == command.AbilityId);
            if (unit == null || ability == null)
                return SandboxAbilityPreview.Invalid("Способность недоступна.");
            string gate = AbilityGate(unit, ability);
            if (!string.IsNullOrEmpty(gate))
                return SandboxAbilityPreview.Invalid(gate);
            return Evaluate(unit, ability, command);
        }

        private IEnumerable<SandboxUnitState> Opponents(SandboxUnitState unit)
        {
            return units.Where(other => !other.IsDefeated && other.Team != unit.Team && !other.IsOffBoard)
                .OrderBy(other => other.Id, StringComparer.Ordinal);
        }

        private IEnumerable<SandboxAbilityCommand> EnumerateCommands(SandboxUnitState unit, SandboxAbilityDefinition ability)
        {
            string id = ability.Id;
            switch (ability.Kind)
            {
                case SandboxAbilityKind.Pounce:
                    foreach (SandboxUnitState target in Opponents(unit))
                    {
                        HexCoord? landing = FindPounceLanding(unit, ability, target);
                        if (landing.HasValue)
                            yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = target.Id, TargetCell = landing, KeyCell = target.Position };
                    }
                    break;
                case SandboxAbilityKind.Charge:
                    foreach (SandboxUnitState target in Opponents(unit))
                    {
                        if (TryFindChargeLine(unit, ability, target, out List<HexCoord> path, out _))
                            yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = target.Id, TargetCell = path[path.Count - 1], KeyCell = target.Position };
                    }
                    break;
                case SandboxAbilityKind.BreakGuard:
                case SandboxAbilityKind.Shove:
                case SandboxAbilityKind.Attach:
                case SandboxAbilityKind.NumbBite:
                case SandboxAbilityKind.Engulf:
                    foreach (SandboxUnitState target in Opponents(unit))
                    {
                        HexCoord? approach = FindApproach(unit, target);
                        if (approach.HasValue)
                            yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = target.Id, ApproachCell = approach, KeyCell = target.Position };
                    }
                    break;
                case SandboxAbilityKind.StunSpit:
                case SandboxAbilityKind.Call:
                    foreach (SandboxUnitState target in Opponents(unit))
                        yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = target.Id, KeyCell = target.Position };
                    break;
                case SandboxAbilityKind.VineGrab:
                    SandboxLink vine = links.FirstOrDefault(item => item.SourceId == unit.Id && item.Kind == SandboxLinkKind.Vine);
                    if (vine != null)
                    {
                        SandboxUnitState held = GetUnit(vine.TargetId);
                        if (held != null)
                            yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = held.Id, KeyCell = held.Position, TargetObjectId = "pull" };
                    }
                    else
                    {
                        foreach (SandboxUnitState target in Opponents(unit))
                            yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = target.Id, KeyCell = target.Position };
                    }
                    break;
                case SandboxAbilityKind.TendrilGrab:
                    foreach (SandboxLink held in links.Where(item => item.SourceId == unit.Id && item.Kind == SandboxLinkKind.Tendril).OrderBy(item => item.Id))
                    {
                        SandboxUnitState victim = GetUnit(held.TargetId);
                        if (victim != null)
                            yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = victim.Id, KeyCell = victim.Position, TargetObjectId = "pull" };
                    }
                    foreach (SandboxUnitState target in Opponents(unit))
                    {
                        if (!links.Any(item => item.TargetId == target.Id && item.Kind != SandboxLinkKind.Attach))
                            yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = target.Id, KeyCell = target.Position };
                    }
                    break;
                case SandboxAbilityKind.ChimeMark:
                    if (HasTollReady(unit))
                    {
                        yield return new SandboxAbilityCommand { AbilityId = id, KeyCell = unit.Position, TargetObjectId = "toll" };
                        break;
                    }
                    foreach (SandboxUnitState target in Opponents(unit))
                    {
                        SandboxUnitState second = Opponents(unit)
                            .Where(other => other != target && CanMark(unit, ability, other))
                            .OrderBy(other => other.Position.DistanceTo(target.Position))
                            .ThenBy(other => other.Id, StringComparer.Ordinal)
                            .FirstOrDefault();
                        yield return new SandboxAbilityCommand
                        {
                            AbilityId = id, TargetUnitId = target.Id,
                            SecondTargetUnitId = ability.Limit >= 2 ? second?.Id : null, KeyCell = target.Position
                        };
                    }
                    break;
                case SandboxAbilityKind.Burrow:
                    if (unit.CanStrikeAfterExit)
                    {
                        foreach (SandboxUnitState target in Opponents(unit).Where(other => other.Position.DistanceTo(unit.Position) == 1))
                            yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = target.Id, KeyCell = target.Position, TargetObjectId = "strike" };
                        break;
                    }
                    foreach (KeyValuePair<HexCoord, List<HexCoord>> exit in BurrowExits(unit, ability.RangeMax))
                        yield return new SandboxAbilityCommand { AbilityId = id, TargetCell = exit.Key, KeyCell = exit.Key };
                    break;
                case SandboxAbilityKind.ConsumeCorpse:
                    foreach (SandboxUnitState corpse in Corpses.Where(item => item.Position.DistanceTo(unit.Position) <= 1).OrderBy(item => item.Id, StringComparer.Ordinal))
                        yield return new SandboxAbilityCommand { AbilityId = id, TargetUnitId = corpse.Id, KeyCell = corpse.Position };
                    break;
                case SandboxAbilityKind.WeaveThread:
                    foreach (SandboxAbilityCommand thread in ThreadCommands(unit, ability))
                        yield return thread;
                    break;
                case SandboxAbilityKind.PitCycle:
                    yield return new SandboxAbilityCommand { AbilityId = id, KeyCell = unit.Position };
                    break;
                case SandboxAbilityKind.LayEgg:
                    foreach (HexCoord cell in unit.Position.Neighbors().Where(IsFreeCell).OrderBy(item => item))
                        yield return new SandboxAbilityCommand { AbilityId = id, TargetCell = cell, KeyCell = cell };
                    break;
            }
        }

        // ---------- Оценка ----------

        private SandboxAbilityPreview Evaluate(SandboxUnitState unit, SandboxAbilityDefinition ability, SandboxAbilityCommand command)
        {
            SandboxUnitState target = !string.IsNullOrEmpty(command.TargetUnitId) ? GetUnit(command.TargetUnitId) : null;
            SandboxAbilityPreview preview = new SandboxAbilityPreview { IsValid = true, Multiplier = ability.DamageMultiplier };
            switch (ability.Kind)
            {
                case SandboxAbilityKind.Pounce:
                {
                    if (!IsLiveOpponent(unit, target)) return SandboxAbilityPreview.Invalid("Цель недоступна.");
                    HexCoord? landing = command.TargetCell;
                    string reason = CheckPounceLanding(unit, ability, target, landing);
                    if (!string.IsNullOrEmpty(reason)) return SandboxAbilityPreview.Invalid(reason);
                    preview.LandingCell = landing;
                    preview.Path.AddRange(SandboxHexLine.Line(unit.Position, landing.Value));
                    FillStrike(preview, unit, target, landing.Value, ability.DamageMultiplier);
                    preview.Summary = "Прыжок → " + landing.Value + " (" + unit.Position.DistanceTo(landing.Value) + " движения), удар " + DamageFormula(preview);
                    return preview;
                }
                case SandboxAbilityKind.Charge:
                {
                    if (!IsLiveOpponent(unit, target)) return SandboxAbilityPreview.Invalid("Цель недоступна.");
                    if (!TryFindChargeLine(unit, ability, target, out List<HexCoord> path, out string reason))
                        return SandboxAbilityPreview.Invalid(reason);
                    preview.Path.Add(unit.Position);
                    preview.Path.AddRange(path);
                    HexCoord strikeFrom = path[path.Count - 1];
                    FillStrike(preview, unit, target, strikeFrom, ability.DamageMultiplier);
                    preview.Summary = "Разгон " + path.Count + " гекса → удар " + DamageFormula(preview);
                    if (ability.Push > 0)
                        FillPush(preview, target, strikeFrom.GetNeighborIndex(target.Position));
                    return preview;
                }
                case SandboxAbilityKind.BreakGuard:
                case SandboxAbilityKind.Shove:
                case SandboxAbilityKind.Attach:
                case SandboxAbilityKind.NumbBite:
                case SandboxAbilityKind.Engulf:
                {
                    if (!IsLiveOpponent(unit, target)) return SandboxAbilityPreview.Invalid("Цель недоступна.");
                    HexCoord approach = command.ApproachCell ?? unit.Position;
                    string reason = CheckApproach(unit, target, approach);
                    if (!string.IsNullOrEmpty(reason)) return SandboxAbilityPreview.Invalid(reason);
                    if (ability.Kind == SandboxAbilityKind.Attach && GetAttachedParasite(target) != null)
                        return SandboxAbilityPreview.Invalid("На этой цели уже сидит паразит.");
                    if (ability.Kind == SandboxAbilityKind.Engulf && IsHeld(target))
                        return SandboxAbilityPreview.Invalid("Цель уже удерживают.");
                    if (approach != unit.Position)
                        preview.Path.AddRange(SandboxMovementPathFor(unit, approach));
                    FillStrike(preview, unit, target, approach, ability.DamageMultiplier);
                    switch (ability.Kind)
                    {
                        case SandboxAbilityKind.BreakGuard:
                            preview.Summary = "Удар " + DamageFormula(preview) + (target.IsGuarding ? ", затем стойка снята" : " (цель не в стойке)");
                            break;
                        case SandboxAbilityKind.Shove:
                            preview.Summary = "Удар " + DamageFormula(preview);
                            FillPush(preview, target, approach.GetNeighborIndex(target.Position));
                            break;
                        case SandboxAbilityKind.Attach:
                            preview.Summary = "Укус " + DamageFormula(preview) + ", затем прикрепление (−" + Math.Max(1, ability.Amount) + " HP в начале ходов клещня)";
                            break;
                        case SandboxAbilityKind.NumbBite:
                            preview.Summary = "Укус " + DamageFormula(preview) + (target.IsGuarding
                                ? " — стойка блокирует оцепенение"
                                : target.IsNumbed || target.NumbImmunity > 0 ? " — у цели иммунитет к оцепенению" : ", затем «Оцепенение»");
                            break;
                        default:
                            preview.Summary = "Накрывает " + DamageFormula(preview) + ", затем «Захвачен» (−" + Math.Max(1, ability.Amount) + " HP в начале ходов ползуна)";
                            break;
                    }
                    return preview;
                }
                case SandboxAbilityKind.StunSpit:
                {
                    if (!IsLiveOpponent(unit, target)) return SandboxAbilityPreview.Invalid("Цель недоступна.");
                    int distance = unit.Position.DistanceTo(target.Position);
                    if (distance < ability.RangeMin || distance > ability.RangeMax)
                        return SandboxAbilityPreview.Invalid("Цель вне дальности " + ability.RangeMin + "–" + ability.RangeMax + ".");
                    if (!HasLineOfSight(unit.Position, target.Position))
                        return SandboxAbilityPreview.Invalid("Нет линии видимости.");
                    FillStrike(preview, unit, target, unit.Position, ability.DamageMultiplier);
                    preview.Summary = "Плевок " + DamageFormula(preview) + (target.IsStunned ? " — уже ошеломлён, срок не продлевается" : ", затем «Ошеломлён»");
                    return preview;
                }
                case SandboxAbilityKind.VineGrab:
                case SandboxAbilityKind.TendrilGrab:
                {
                    if (target == null || target.IsDefeated) return SandboxAbilityPreview.Invalid("Цель недоступна.");
                    if (command.TargetObjectId == "pull")
                    {
                        HexCoord? pull = FindPullCell(target, unit.Position, unit.Position);
                        if (target.Position.DistanceTo(unit.Position) <= 1)
                            return SandboxAbilityPreview.Invalid("Цель уже рядом — бейте обычной атакой.");
                        if (!pull.HasValue) return SandboxAbilityPreview.Invalid("Подтянуть некуда: клетки заняты.");
                        preview.ForcedCell = pull;
                        preview.Summary = "Подтянуть " + target.DisplayLabel + " на 1 гекс → " + pull.Value;
                        return preview;
                    }
                    if (!IsLiveOpponent(unit, target)) return SandboxAbilityPreview.Invalid("Цель недоступна.");
                    int distance = unit.Position.DistanceTo(target.Position);
                    if (distance < ability.RangeMin || distance > ability.RangeMax)
                        return SandboxAbilityPreview.Invalid("Цель вне дальности " + ability.RangeMax + ".");
                    if (!HasLineOfSight(unit.Position, target.Position))
                        return SandboxAbilityPreview.Invalid("Нет линии видимости.");
                    if (IsHeld(target))
                        return SandboxAbilityPreview.Invalid("Цель уже удерживают.");
                    if (ability.Kind == SandboxAbilityKind.TendrilGrab && FreeTendril(unit) == null)
                        return SandboxAbilityPreview.Invalid("Все жгуты заняты или разрушены.");
                    FillStrike(preview, unit, target, unit.Position, ability.DamageMultiplier);
                    preview.Path.AddRange(SandboxHexLine.Line(unit.Position, target.Position));
                    HexCoord? first = FindPullCell(target, unit.Position, unit.Position);
                    preview.ForcedCell = ability.Kind == SandboxAbilityKind.VineGrab ? first : null;
                    preview.Summary = (ability.Kind == SandboxAbilityKind.VineGrab ? "Лоза " : "Жгут ") + DamageFormula(preview) + ", захват" +
                                      (ability.Kind == SandboxAbilityKind.VineGrab
                                          ? (first.HasValue ? " и подтягивание → " + first.Value : "; подтянуть некуда")
                                          : string.Empty);
                    return preview;
                }
                case SandboxAbilityKind.ChimeMark:
                {
                    if (command.TargetObjectId == "toll")
                    {
                        SandboxTelegraph toll = telegraphs.FirstOrDefault(item => item.SourceId == unit.Id && item.Kind == SandboxTelegraphKind.Toll);
                        if (toll == null) return SandboxAbilityPreview.Invalid("Нет подготовленного звона.");
                        List<SandboxUnitState> hit = TollTargets(toll).ToList();
                        if (hit.Count == 0) return SandboxAbilityPreview.Invalid("В зоне не осталось отмеченных.");
                        preview.DangerCells.AddRange(toll.Cells);
                        preview.Summary = "Удар звона " + FormatMultiplier(ability.DamageMultiplier) + " по " +
                                          string.Join(", ", hit.Select(item => item.DisplayLabel + " (" + BuildAttackPreview(unit, item, unit.Position, false, ability.DamageMultiplier).Damage + ")"));
                        return preview;
                    }
                    if (!IsLiveOpponent(unit, target) || !CanMark(unit, ability, target))
                        return SandboxAbilityPreview.Invalid("Цель вне дальности " + ability.RangeMax + " или не видна.");
                    SandboxUnitState second = !string.IsNullOrEmpty(command.SecondTargetUnitId) ? GetUnit(command.SecondTargetUnitId) : null;
                    preview.DangerCells.AddRange(CellsWithin(unit.Position, ability.RangeMax));
                    preview.Summary = "Метка: " + target.DisplayLabel + (second != null ? " и " + second.DisplayLabel : string.Empty) +
                                      ". На следующем ходу — удар " + FormatMultiplier(ability.DamageMultiplier) + " по отмеченным в зоне радиуса " + ability.RangeMax + ".";
                    return preview;
                }
                case SandboxAbilityKind.Burrow:
                {
                    if (command.TargetObjectId == "strike")
                    {
                        if (!IsLiveOpponent(unit, target) || target.Position.DistanceTo(unit.Position) != 1)
                            return SandboxAbilityPreview.Invalid("Цель не рядом с выходом.");
                        FillStrike(preview, unit, target, unit.Position, ability.DamageMultiplier);
                        HexCoord? pull = FindPullCell(target, unit.BurrowOrigin, unit.Position);
                        preview.ForcedCell = pull;
                        preview.Summary = "Удар из-под земли " + DamageFormula(preview) + (pull.HasValue ? ", затем подтянуть к норе → " + pull.Value : "; подтянуть к норе некуда");
                        return preview;
                    }
                    if (!command.TargetCell.HasValue) return SandboxAbilityPreview.Invalid("Выберите клетку выхода.");
                    Dictionary<HexCoord, List<HexCoord>> exits = BurrowExits(unit, ability.RangeMax);
                    if (!exits.TryGetValue(command.TargetCell.Value, out List<HexCoord> route))
                        return SandboxAbilityPreview.Invalid("Сюда не прокопать: нужен связный грунт до " + ability.RangeMax + " гексов.");
                    preview.Path.AddRange(route);
                    preview.DangerCells.Add(command.TargetCell.Value);
                    preview.Summary = "Уходит под землю; на следующем ходу выйдет в " + command.TargetCell.Value + " и сможет ударить соседнего " + FormatMultiplier(ability.DamageMultiplier) + ".";
                    return preview;
                }
                case SandboxAbilityKind.Call:
                {
                    if (!IsLiveOpponent(unit, target)) return SandboxAbilityPreview.Invalid("Цель недоступна.");
                    int distance = unit.Position.DistanceTo(target.Position);
                    if (distance < ability.RangeMin || distance > ability.RangeMax)
                        return SandboxAbilityPreview.Invalid("Цель вне дальности " + ability.RangeMax + ".");
                    if (!HasLineOfSight(unit.Position, target.Position))
                        return SandboxAbilityPreview.Invalid("Нет линии видимости.");
                    if (target.IsGuarding)
                    {
                        preview.Summary = "Защитная стойка — зов не подействует.";
                        return preview;
                    }
                    HexCoord? step = FindPullCell(target, unit.Position, unit.Position);
                    preview.ForcedCell = step;
                    preview.Summary = step.HasValue ? "Зов: " + target.DisplayLabel + " шагнёт → " + step.Value : "Зов: цель устоит — нет пути ближе.";
                    return preview;
                }
                case SandboxAbilityKind.ConsumeCorpse:
                {
                    if (target == null || !target.IsDefeated || target.CorpseConsumed || target.Position.DistanceTo(unit.Position) > 1)
                        return SandboxAbilityPreview.Invalid("Рядом нет доступного тела.");
                    int heal = Math.Max(1, (int)Math.Ceiling(unit.MaxHitPoints * Math.Max(1, ability.Amount) / 100m));
                    preview.Summary = "Поглотить тело: +" + Math.Min(heal, unit.MaxHitPoints - unit.HitPoints) + " HP.";
                    return preview;
                }
                case SandboxAbilityKind.WeaveThread:
                {
                    if (!command.TargetCell.HasValue || !command.SecondCell.HasValue)
                        return SandboxAbilityPreview.Invalid("Выберите две опоры.");
                    string reason = CheckThread(unit, ability, command.TargetCell.Value, command.SecondCell.Value, out List<HexCoord> cells);
                    if (!string.IsNullOrEmpty(reason)) return SandboxAbilityPreview.Invalid(reason);
                    preview.DangerCells.AddRange(cells);
                    preview.Summary = "Нить " + command.TargetCell.Value + " — " + command.SecondCell.Value + ": " + cells.Count + " гекса, " + ability.ObjectHitPoints + " HP, " + ability.Duration + " раунда.";
                    return preview;
                }
                case SandboxAbilityKind.PitCycle:
                {
                    if (unit.PitPhase == 1)
                    {
                        if (!PlanSubsidence(unit, ability, out List<HexCoord> pits, out HexCoord exit))
                            return SandboxAbilityPreview.Invalid("Нет допустимых клеток для просадок.");
                        preview.DangerCells.AddRange(pits);
                        preview.LandingCell = exit;
                        preview.Summary = "Просадки: " + string.Join(", ", pits) + "; выход → " + exit + " на следующем ходу.";
                        return preview;
                    }
                    preview.Summary = "Уйти под землю. Следующим ходом объявит провалы и выход.";
                    return preview;
                }
                case SandboxAbilityKind.LayEgg:
                {
                    if (!command.TargetCell.HasValue || !IsFreeCell(command.TargetCell.Value) || command.TargetCell.Value.DistanceTo(unit.Position) != 1)
                        return SandboxAbilityPreview.Invalid("Яйцо — только на соседнюю свободную клетку.");
                    preview.Summary = "Яйцо (" + ability.ObjectHitPoints + " HP) в " + command.TargetCell.Value + ", вылупится через " + ability.Duration + " хода матки.";
                    return preview;
                }
            }
            return SandboxAbilityPreview.Invalid("Механика ещё не подключена.");
        }

        private bool IsLiveOpponent(SandboxUnitState unit, SandboxUnitState target)
        {
            return target != null && !target.IsDefeated && target.Team != unit.Team && !target.IsOffBoard;
        }

        private void FillStrike(SandboxAbilityPreview preview, SandboxUnitState unit, SandboxUnitState target, HexCoord from, decimal multiplier)
        {
            HexCoord saved = unit.Position;
            unit.Position = from;
            preview.NormalDamage = BuildAttackPreview(unit, target, from).Damage;
            preview.Damage = BuildAttackPreview(unit, target, from, false, multiplier).Damage;
            unit.Position = saved;
            preview.Multiplier = multiplier;
        }

        private static string FormatMultiplier(decimal multiplier)
        {
            return "×" + multiplier.ToString("0.##", Ru);
        }

        private static string DamageFormula(SandboxAbilityPreview preview)
        {
            return preview.NormalDamage + " " + FormatMultiplier(preview.Multiplier) + " = " + preview.Damage;
        }

        private void FillPush(SandboxAbilityPreview preview, SandboxUnitState target, int direction)
        {
            if (direction < 0)
                return;
            HexCoord cell = SandboxHexLine.Step(target.Position, direction);
            string reason = ForcedMoveBlockReason(target, cell, null);
            if (string.IsNullOrEmpty(reason))
            {
                preview.ForcedCell = cell;
                preview.Summary += ", отбрасывание → " + cell;
            }
            else
            {
                preview.ForcedBlockedReason = reason;
                preview.Summary += "; отбрасывание невозможно: " + reason;
            }
        }

        // ---------- Геометрия ----------

        private HexCoord? FindPounceLanding(SandboxUnitState unit, SandboxAbilityDefinition ability, SandboxUnitState target)
        {
            return target.Position.Neighbors()
                .Where(cell => string.IsNullOrEmpty(CheckPounceLanding(unit, ability, target, cell)))
                .OrderBy(cell => unit.Position.DistanceTo(cell))
                .ThenBy(cell => cell)
                .Cast<HexCoord?>()
                .FirstOrDefault();
        }

        private string CheckPounceLanding(SandboxUnitState unit, SandboxAbilityDefinition ability, SandboxUnitState target, HexCoord? landing)
        {
            if (!landing.HasValue)
                return "Не выбрано приземление.";
            HexCoord cell = landing.Value;
            if (unit.Position.DistanceTo(target.Position) > ability.RangeMax)
                return "Цель дальше " + ability.RangeMax + " гексов.";
            if (cell.DistanceTo(target.Position) != 1)
                return "Приземление должно быть рядом с целью.";
            int length = unit.Position.DistanceTo(cell);
            if (length < ability.PathMin || length > ability.PathMax)
                return "Прыжок — " + ability.PathMin + "–" + ability.PathMax + " гекса.";
            if (length > unit.RemainingMovement)
                return "Не хватает движения на прыжок (" + length + ").";
            if (!IsFreeCell(cell))
                return "Клетка приземления занята или непроходима.";
            List<HexCoord> line = SandboxHexLine.Line(unit.Position, cell);
            for (int i = 1; i < line.Count - 1; i++)
            {
                // Высоты препятствий в данных нет — перелёт через занятую или
                // непроходимую клетку консервативно запрещён.
                if (!IsFreeCell(line[i]))
                    return "На линии прыжка препятствие " + line[i] + ".";
            }
            return string.Empty;
        }

        private bool TryFindChargeLine(SandboxUnitState unit, SandboxAbilityDefinition ability, SandboxUnitState target, out List<HexCoord> best, out string reason)
        {
            best = null;
            reason = "Нет свободной прямой " + ability.PathMin + "–" + ability.PathMax + " гекса перед целью.";
            int bestCost = int.MaxValue;
            for (int direction = 0; direction < 6; direction++)
            {
                List<HexCoord> path = new List<HexCoord>();
                HexCoord cell = unit.Position;
                int cost = 0;
                for (int step = 1; step <= ability.PathMax; step++)
                {
                    cell = SandboxHexLine.Step(cell, direction);
                    if (!IsFreeCell(cell) || IsMovementStopCell(unit, cell))
                        break;
                    cost += GetStepCost(unit, cell);
                    if (cost > unit.RemainingMovement)
                        break;
                    path.Add(cell);
                    if (step < ability.PathMin)
                        continue;
                    HexCoord strike = SandboxHexLine.Step(cell, direction);
                    if (strike == target.Position && (best == null || cost < bestCost ||
                                                      (cost == bestCost && cell.CompareTo(best[best.Count - 1]) < 0)))
                    {
                        best = new List<HexCoord>(path);
                        bestCost = cost;
                    }
                }
            }
            return best != null;
        }

        private HexCoord? FindApproach(SandboxUnitState unit, SandboxUnitState target)
        {
            if (unit.Position.DistanceTo(target.Position) == 1)
                return unit.Position;
            if (unit.ActsWithoutMovement || unit.RemainingMovement <= 0)
                return null;
            IReadOnlyDictionary<HexCoord, int> reachable = GetReachable(unit.Id);
            HexCoord? best = null;
            int bestCost = int.MaxValue;
            foreach (HexCoord cell in target.Position.Neighbors().OrderBy(item => item))
            {
                if (!reachable.TryGetValue(cell, out int cost) || cost >= unit.RemainingMovement)
                    continue;
                if (cost < bestCost)
                {
                    best = cell;
                    bestCost = cost;
                }
            }
            return best;
        }

        private string CheckApproach(SandboxUnitState unit, SandboxUnitState target, HexCoord approach)
        {
            if (approach.DistanceTo(target.Position) != 1)
                return "Цель должна быть рядом.";
            if (approach == unit.Position)
                return string.Empty;
            IReadOnlyDictionary<HexCoord, int> reachable = GetReachable(unit.Id);
            if (!reachable.TryGetValue(approach, out int cost) || cost >= unit.RemainingMovement)
                return "Подойти и ударить в пределах движения нельзя.";
            return string.Empty;
        }

        private List<HexCoord> SandboxMovementPathFor(SandboxUnitState unit, HexCoord destination)
        {
            return SandboxMovementPath.TryBuild(this, unit.Id, destination, out IReadOnlyList<HexCoord> path, out _)
                ? path.ToList()
                : new List<HexCoord> { unit.Position, destination };
        }

        // Ближайшая к точке притяжения соседняя клетка, уменьшающая расстояние.
        private HexCoord? FindPullCell(SandboxUnitState target, HexCoord toward, HexCoord forbidden)
        {
            int current = target.Position.DistanceTo(toward);
            return target.Position.Neighbors()
                .Where(cell => cell != forbidden && cell.DistanceTo(toward) < current &&
                               string.IsNullOrEmpty(ForcedMoveBlockReason(target, cell, null)))
                .OrderBy(cell => cell.DistanceTo(toward))
                .ThenBy(cell => cell)
                .Cast<HexCoord?>()
                .FirstOrDefault();
        }

        private string ForcedMoveBlockReason(SandboxUnitState unit, HexCoord cell, string ignoredUnitId)
        {
            if (!IsInside(cell))
                return "край поля";
            if (GetTerrain(cell) == SandboxTerrain.Impassable)
                return "стена";
            if (HasCellState(cell, SandboxCellState.Pit))
                return "провал";
            if (FindEggAt(cell) != null)
                return "клетка занята яйцом";
            SandboxUnitState occupant = GetUnitAt(cell);
            if (occupant != null && occupant != unit && occupant.Id != ignoredUnitId)
                return "клетка занята";
            return string.Empty;
        }

        // Принудительное перемещение: не тратит движение жертвы, не вызывает
        // ответов за проход, нить останавливает на своём гексе.
        private string ForceMove(SandboxUnitState unit, HexCoord cell, string verb)
        {
            string reason = ForcedMoveBlockReason(unit, cell, null);
            if (!string.IsNullOrEmpty(reason))
                return " " + verb + " невозможно: " + reason + ".";
            SetUnitPosition(unit, cell);
            string note = " " + unit.DisplayLabel + " " + verb + " → " + cell + ".";
            if (IsMovementStopCell(unit, cell))
            {
                if (CurrentUnit == unit)
                {
                    unit.RemainingMovement = 0;
                    unit.ActsWithoutMovement = true;
                }
                note += " Нить останавливает.";
            }
            return note;
        }

        private bool CanMark(SandboxUnitState unit, SandboxAbilityDefinition ability, SandboxUnitState target)
        {
            return target.Position.DistanceTo(unit.Position) <= ability.RangeMax && HasLineOfSight(unit.Position, target.Position);
        }

        private IEnumerable<SandboxUnitState> TollTargets(SandboxTelegraph toll)
        {
            return toll.TargetIds.Select(GetUnit)
                .Where(item => item != null && !item.IsDefeated && !item.IsOffBoard && toll.Cells.Contains(item.Position));
        }

        private List<HexCoord> CellsWithin(HexCoord center, int radius)
        {
            List<HexCoord> cells = new List<HexCoord>();
            for (int r = 0; r < Height; r++)
            {
                for (int q = 0; q < Width; q++)
                {
                    HexCoord cell = new HexCoord(q, r);
                    if (IsInside(cell) && cell.DistanceTo(center) <= radius)
                        cells.Add(cell);
                }
            }
            return cells;
        }

        private bool IsDiggable(HexCoord cell)
        {
            // Земля и холм копаются; камень/стены, вода и провалы — нет.
            return IsInside(cell) && GetTerrain(cell) != SandboxTerrain.Impassable &&
                   !HasCellState(cell, SandboxCellState.ShallowWater) && !HasCellState(cell, SandboxCellState.Pit);
        }

        // Выходы подкопа: связный путь по копаемому грунту до range гексов.
        private Dictionary<HexCoord, List<HexCoord>> BurrowExits(SandboxUnitState unit, int range)
        {
            Dictionary<HexCoord, List<HexCoord>> result = new Dictionary<HexCoord, List<HexCoord>>();
            Dictionary<HexCoord, HexCoord> from = new Dictionary<HexCoord, HexCoord>();
            Dictionary<HexCoord, int> depth = new Dictionary<HexCoord, int> { { unit.Position, 0 } };
            Queue<HexCoord> queue = new Queue<HexCoord>();
            queue.Enqueue(unit.Position);
            while (queue.Count > 0)
            {
                HexCoord current = queue.Dequeue();
                if (depth[current] >= range)
                    continue;
                foreach (HexCoord next in current.Neighbors().OrderBy(item => item))
                {
                    if (depth.ContainsKey(next) || !IsDiggable(next))
                        continue;
                    depth[next] = depth[current] + 1;
                    from[next] = current;
                    queue.Enqueue(next);
                }
            }
            foreach (HexCoord cell in depth.Keys.Where(cell => cell != unit.Position && IsFreeCell(cell)).OrderBy(cell => cell))
            {
                List<HexCoord> route = new List<HexCoord> { cell };
                HexCoord step = cell;
                while (from.TryGetValue(step, out HexCoord previous))
                {
                    route.Add(previous);
                    step = previous;
                }
                route.Reverse();
                result[cell] = route;
            }
            return result;
        }

        private IEnumerable<SandboxAbilityCommand> ThreadCommands(SandboxUnitState unit, SandboxAbilityDefinition ability)
        {
            List<HexCoord> near = CellsWithin(unit.Position, ability.RangeMax).OrderBy(cell => cell).ToList();
            for (int i = 0; i < near.Count; i++)
            {
                for (int j = i + 1; j < near.Count; j++)
                {
                    if (string.IsNullOrEmpty(CheckThread(unit, ability, near[i], near[j], out _)))
                        yield return new SandboxAbilityCommand { AbilityId = ability.Id, TargetCell = near[i], SecondCell = near[j], KeyCell = near[i] };
                }
            }
        }

        private string CheckThread(SandboxUnitState unit, SandboxAbilityDefinition ability, HexCoord a, HexCoord b, out List<HexCoord> cells)
        {
            cells = new List<HexCoord>();
            if (!IsInside(a) || !IsInside(b))
                return "Опора вне поля.";
            if (a.DistanceTo(unit.Position) > ability.RangeMax || b.DistanceTo(unit.Position) > ability.RangeMax)
                return "Опоры — не дальше " + ability.RangeMax + " гексов от нитника.";
            int length = a.DistanceTo(b);
            if (length < ability.PathMin || length > ability.PathMax)
                return "Длина нити — " + ability.PathMin + "–" + ability.PathMax + " гекса.";
            List<HexCoord> line = SandboxHexLine.Line(a, b);
            for (int i = 0; i < line.Count; i++)
            {
                bool anchor = i == 0 || i == line.Count - 1;
                if (GetTerrain(line[i]) == SandboxTerrain.Impassable)
                {
                    if (!anchor)
                        return "Нить не проходит сквозь стену.";
                    continue; // камень или дерево — естественная опора
                }
                if (HasCellState(line[i], SandboxCellState.Pit))
                    return "Нить не натянуть над провалом.";
                cells.Add(line[i]);
            }
            return cells.Count == 0 ? "Нить не перекрывает ни одного гекса." : string.Empty;
        }

        private SandboxBattleObject FreeTendril(SandboxUnitState unit)
        {
            return objects.Where(item => item.Kind == SandboxObjectKind.Tendril && item.OwnerId == unit.Id && !item.IsDestroyed && string.IsNullOrEmpty(item.HeldUnitId))
                .OrderBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
        }

        // ---------- Исполнение ----------

        public bool TryUseAbility(string unitId, SandboxAbilityCommand command, out string message)
        {
            SandboxUnitState unit = GetUnit(unitId);
            SandboxAbilityDefinition ability = unit?.Definition.Abilities.FirstOrDefault(item => command != null && item.Id == command.AbilityId);
            SandboxAbilityPreview preview = PreviewAbility(unitId, command);
            if (!preview.IsValid || ability == null)
            {
                message = preview.Reason;
                return false;
            }

            ClearPendingRetaliation();
            string reveal = RevealAttacker(unit);
            SandboxUnitState target = !string.IsNullOrEmpty(command.TargetUnitId) ? GetUnit(command.TargetUnitId) : null;
            string head = reveal + unit.DisplayLabel + " — " + AbilityLabel(unit, ability);
            bool contact = false;
            bool setCooldown = true;
            string body;

            switch (ability.Kind)
            {
                case SandboxAbilityKind.Pounce:
                {
                    HexCoord landing = command.TargetCell.Value;
                    unit.RemainingMovement -= unit.Position.DistanceTo(landing);
                    SetUnitPosition(unit, landing);
                    unit.MovedThisActivation = true;
                    body = " → " + landing + ": " + Strike(unit, target, ability.DamageMultiplier, true, preview);
                    contact = true;
                    break;
                }
                case SandboxAbilityKind.Charge:
                {
                    TryFindChargeLine(unit, ability, target, out List<HexCoord> path, out _);
                    HexCoord end = path[path.Count - 1];
                    int direction = end.GetNeighborIndex(target.Position);
                    SetUnitPosition(unit, end);
                    unit.MovedThisActivation = true;
                    body = " (разбег " + path.Count + "): " + Strike(unit, target, ability.DamageMultiplier, true, preview);
                    if (!unit.IsDefeated && !target.IsDefeated && ability.Push > 0)
                        body += ForceMove(target, SandboxHexLine.Step(target.Position, direction), "отброшен");
                    contact = true;
                    break;
                }
                case SandboxAbilityKind.BreakGuard:
                case SandboxAbilityKind.Shove:
                case SandboxAbilityKind.Attach:
                case SandboxAbilityKind.NumbBite:
                case SandboxAbilityKind.Engulf:
                {
                    HexCoord approach = command.ApproachCell ?? unit.Position;
                    if (approach != unit.Position)
                    {
                        IReadOnlyDictionary<HexCoord, int> reachable = GetReachable(unit.Id);
                        unit.RemainingMovement -= reachable[approach];
                        SetUnitPosition(unit, approach);
                        unit.MovedThisActivation = true;
                        head = AfterVoluntaryMove(unit).Trim() + (string.IsNullOrEmpty(reveal) ? string.Empty : " ") + head;
                    }
                    bool wasGuarding = target.IsGuarding;
                    body = ": " + Strike(unit, target, ability.DamageMultiplier, true, preview);
                    contact = true;
                    if (!unit.IsDefeated && !target.IsDefeated)
                    {
                        switch (ability.Kind)
                        {
                            case SandboxAbilityKind.BreakGuard:
                                if (target.IsGuarding)
                                {
                                    target.IsGuarding = false;
                                    body += " Стойка сбита.";
                                }
                                break;
                            case SandboxAbilityKind.Shove:
                                body += ForceMove(target, SandboxHexLine.Step(target.Position, unit.Position.GetNeighborIndex(target.Position)), "отброшен");
                                break;
                            case SandboxAbilityKind.NumbBite:
                                if (wasGuarding)
                                    body += " Стойка удержала от оцепенения.";
                                else if (target.IsNumbed || target.NumbImmunity > 0)
                                    body += " Иммунитет: оцепенение не повторяется.";
                                else
                                    body += ApplyNumb(target);
                                break;
                            case SandboxAbilityKind.Attach:
                            case SandboxAbilityKind.Engulf:
                                afterReaction.Add(new PendingAfterReaction
                                {
                                    Kind = ability.Kind == SandboxAbilityKind.Attach ? SandboxLinkKind.Attach : SandboxLinkKind.Engulf,
                                    SourceId = unit.Id, TargetId = target.Id, AbilityId = ability.Id
                                });
                                setCooldown = false;
                                break;
                        }
                    }
                    break;
                }
                case SandboxAbilityKind.StunSpit:
                    body = " → " + target.DisplayLabel + ": " + Strike(unit, target, ability.DamageMultiplier, false, preview);
                    if (!target.IsDefeated)
                        body += target.IsStunned ? " Уже ошеломлён — срок не продлевается." : ApplyStun(target);
                    break;
                case SandboxAbilityKind.VineGrab:
                case SandboxAbilityKind.TendrilGrab:
                {
                    if (command.TargetObjectId == "pull")
                    {
                        body = ":" + ForceMove(target, preview.ForcedCell.Value, "подтянут");
                        setCooldown = ability.Kind != SandboxAbilityKind.VineGrab ? true : false;
                        break;
                    }
                    body = " → " + target.DisplayLabel + ": " + Strike(unit, target, ability.DamageMultiplier, false, preview);
                    if (!target.IsDefeated && !unit.IsDefeated)
                    {
                        SandboxBattleObject holder = ability.Kind == SandboxAbilityKind.VineGrab
                            ? CreateObject(SandboxObjectKind.Vine, unit.Id, Math.Max(1, ability.ObjectHitPoints))
                            : FreeTendril(unit);
                        holder.HeldUnitId = target.Id;
                        SandboxLink link = new SandboxLink
                        {
                            Id = "link:" + nextObjectId++,
                            Kind = ability.Kind == SandboxAbilityKind.VineGrab ? SandboxLinkKind.Vine : SandboxLinkKind.Tendril,
                            SourceId = unit.Id, TargetId = target.Id, ObjectId = holder.Id,
                            CreatedAtSourceActivation = unit.ActivationCount
                        };
                        links.Add(link);
                        UpdateLinkObjectCells(link);
                        body += " Захвачен (" + holder.Label.ToLowerInvariant() + " " + holder.HitPoints + " HP).";
                        if (ability.Kind == SandboxAbilityKind.VineGrab)
                        {
                            HexCoord? pull = FindPullCell(target, unit.Position, unit.Position);
                            body += pull.HasValue ? ForceMove(target, pull.Value, "подтянут") : " Подтянуть некуда.";
                            setCooldown = false;
                        }
                    }
                    break;
                }
                case SandboxAbilityKind.ChimeMark:
                {
                    if (command.TargetObjectId == "toll")
                    {
                        SandboxTelegraph toll = telegraphs.First(item => item.SourceId == unit.Id && item.Kind == SandboxTelegraphKind.Toll);
                        List<string> parts = new List<string>();
                        foreach (SandboxUnitState victim in TollTargets(toll).ToList())
                        {
                            int damage = BuildAttackPreview(unit, victim, unit.Position, false, ability.DamageMultiplier).Damage;
                            RecordHit(unit, victim, damage, unit.Position);
                            ApplyHit(SandboxHitKind.Ability, unit, victim, damage);
                            parts.Add(victim.DisplayLabel + " −" + damage + (victim.IsDefeated ? " (выведен из строя)" : string.Empty));
                        }
                        CancelTelegraphs(unit.Id, SandboxTelegraphKind.Toll);
                        body = ": " + string.Join(", ", parts) + ".";
                        setCooldown = false;
                        break;
                    }
                    SandboxTelegraph prepared = new SandboxTelegraph
                    {
                        Id = "toll:" + nextObjectId++, Kind = SandboxTelegraphKind.Toll, SourceId = unit.Id,
                        Center = unit.Position, Origin = unit.Position,
                        Note = "Удар звона на следующем ходу " + unit.DisplayLabel
                    };
                    prepared.Cells.AddRange(CellsWithin(unit.Position, ability.RangeMax));
                    prepared.TargetIds.Add(target.Id);
                    if (!string.IsNullOrEmpty(command.SecondTargetUnitId))
                        prepared.TargetIds.Add(command.SecondTargetUnitId);
                    CancelTelegraphs(unit.Id, SandboxTelegraphKind.Toll);
                    telegraphs.Add(prepared);
                    tollPreparedAt[unit.Id] = unit.ActivationCount;
                    body = ": отмечены " + string.Join(", ", prepared.TargetIds.Select(GetUnit).Where(item => item != null).Select(item => item.DisplayLabel)) +
                           ". На следующем ходу — удар по отмеченным в радиусе " + ability.RangeMax + " от " + unit.Position + ".";
                    break;
                }
                case SandboxAbilityKind.Burrow:
                {
                    if (command.TargetObjectId == "strike")
                    {
                        body = ": " + Strike(unit, target, ability.DamageMultiplier, true, preview);
                        contact = true;
                        if (!target.IsDefeated && !unit.IsDefeated)
                        {
                            HexCoord? pull = FindPullCell(target, unit.BurrowOrigin, unit.Position);
                            if (pull.HasValue && ability.Push > 0)
                                body += ForceMove(target, pull.Value, "подтянут к норе");
                        }
                        setCooldown = false;
                        break;
                    }
                    List<HexCoord> route = BurrowExits(unit, ability.RangeMax)[command.TargetCell.Value];
                    SandboxTelegraph exit = new SandboxTelegraph
                    {
                        Id = "exit:" + nextObjectId++, Kind = SandboxTelegraphKind.BurrowExit, SourceId = unit.Id,
                        Center = command.TargetCell.Value, Origin = unit.Position,
                        Note = "Здесь выйдет " + unit.DisplayLabel + " на следующем ходу"
                    };
                    exit.Cells.AddRange(route);
                    telegraphs.Add(exit);
                    unit.BurrowOrigin = unit.Position;
                    unit.IsUnderground = true;
                    body = ": уходит под землю. Выход объявлен в " + command.TargetCell.Value + " на следующем ходу.";
                    setCooldown = false;
                    break;
                }
                case SandboxAbilityKind.Call:
                    if (target.IsGuarding)
                        body = ": " + target.DisplayLabel + " устоял — защитная стойка.";
                    else if (!preview.ForcedCell.HasValue)
                        body = ": " + target.DisplayLabel + " устоял — нет пути ближе.";
                    else
                        body = ":" + ForceMove(target, preview.ForcedCell.Value, "шагает на зов");
                    break;
                case SandboxAbilityKind.ConsumeCorpse:
                {
                    int heal = Math.Max(1, (int)Math.Ceiling(unit.MaxHitPoints * Math.Max(1, ability.Amount) / 100m));
                    int before = unit.HitPoints;
                    unit.Heal(heal);
                    target.CorpseConsumed = true;
                    body = ": поглощает тело " + target.Definition.RoleLabel + ", +" + (unit.HitPoints - before) + " HP.";
                    break;
                }
                case SandboxAbilityKind.WeaveThread:
                {
                    CheckThread(unit, ability, command.TargetCell.Value, command.SecondCell.Value, out List<HexCoord> cells);
                    SandboxBattleObject thread = CreateObject(SandboxObjectKind.Thread, unit.Id, Math.Max(1, ability.ObjectHitPoints));
                    thread.Cells.AddRange(cells);
                    thread.ExpiresAtRound = Round + Math.Max(1, ability.Duration);
                    body = ": нить " + command.TargetCell.Value + " — " + command.SecondCell.Value + " (" + cells.Count + " гекса, до раунда " + thread.ExpiresAtRound + ").";
                    break;
                }
                case SandboxAbilityKind.PitCycle:
                {
                    if (unit.PitPhase == 1)
                    {
                        PlanSubsidence(unit, ability, out List<HexCoord> pits, out HexCoord exitCell);
                        SandboxTelegraph subsidence = new SandboxTelegraph
                        {
                            Id = "pits:" + nextObjectId++, Kind = SandboxTelegraphKind.Subsidence, SourceId = unit.Id,
                            Center = exitCell, Origin = unit.BurrowOrigin, Note = "Просадки на следующем ходу"
                        };
                        subsidence.Cells.AddRange(pits);
                        telegraphs.Add(subsidence);
                        SandboxTelegraph exit = new SandboxTelegraph
                        {
                            Id = "exit:" + nextObjectId++, Kind = SandboxTelegraphKind.BurrowExit, SourceId = unit.Id,
                            Center = exitCell, Origin = unit.BurrowOrigin, Note = "Здесь выйдет " + unit.DisplayLabel + " на следующем ходу"
                        };
                        telegraphs.Add(exit);
                        unit.PitPhase = 2;
                        body = ": просадки в " + string.Join(", ", pits) + ", выход в " + exitCell + " — на следующем ходу.";
                    }
                    else
                    {
                        unit.BurrowOrigin = unit.Position;
                        unit.IsUnderground = true;
                        unit.PitPhase = 1;
                        body = ": уходит под землю.";
                    }
                    setCooldown = false;
                    break;
                }
                case SandboxAbilityKind.LayEgg:
                {
                    SandboxBattleObject egg = CreateObject(SandboxObjectKind.Egg, unit.Id, Math.Max(1, ability.ObjectHitPoints));
                    egg.Cells.Add(command.TargetCell.Value);
                    egg.Counter = Math.Max(1, ability.Duration);
                    body = ": яйцо в " + command.TargetCell.Value + ".";
                    break;
                }
                default:
                    message = "Механика ещё не подключена.";
                    return false;
            }

            unit.ActionPoints = 0;
            unit.RemainingMovement = 0;
            unit.HasAttacked = true;
            if (setCooldown && ability.Cooldown > 0)
                unit.AbilityReadyAt[ability.Id] = unit.ActivationCount + ability.Cooldown + 1;
            if (setCooldown && ability.Cooldown > 0)
                body += " КД " + ability.Cooldown + ".";

            EvaluateBattleOutcome();
            if (Phase == SandboxBattlePhase.InProgress && contact && target != null && !unit.IsDefeated &&
                !target.IsDefeated && target.CanRetaliate && unit.Position.DistanceTo(target.Position) == 1)
            {
                pendingRetaliationDefenderId = target.Id;
                pendingRetaliationAttackerId = unit.Id;
            }
            else
            {
                body += ResolveAfterReaction();
            }
            message = head + body;
            return true;
        }

        // Удар способности: «Упреждающий удар» уважается, урон считается один
        // раз общим расчётом с множителем.
        private string Strike(SandboxUnitState attacker, SandboxUnitState target, decimal multiplier, bool contact, SandboxAbilityPreview preview)
        {
            string prefix = string.Empty;
            if (contact && target.IsGuarding && target.HasPerk(SandboxPerks.FirstStrike) && target.CanRetaliate &&
                attacker.Position.DistanceTo(target.Position) == 1)
            {
                int counter = BuildAttackPreview(target, attacker, target.Position, retaliation: true).Damage;
                target.RetaliationsThisRound++;
                target.RetaliationsMade++;
                RecordHit(target, attacker, counter, target.Position);
                ApplyHit(SandboxHitKind.FirstStrike, target, attacker, counter);
                prefix = target.DisplayLabel + " бьёт первым и наносит " + counter + " урона. ";
                if (attacker.IsDefeated)
                    return prefix + attacker.DisplayLabel + " выведен из строя — способность сорвана.";
            }
            int normal = BuildAttackPreview(attacker, target, attacker.Position).Damage;
            int damage = BuildAttackPreview(attacker, target, attacker.Position, false, multiplier).Damage;
            string targetLabel = target.DisplayLabel;
            string pack = PackNote(attacker, target, attacker.Position);
            RecordHit(attacker, target, damage, attacker.Position);
            if (attacker.Position.DistanceTo(target.Position) > 1)
                attacker.UsedRangedAttack = true;
            else
                attacker.UsedMeleeAttack = true;
            ApplyHit(SandboxHitKind.Ability, attacker, target, damage);
            target.TimesAttackedThisRound++;
            target.TimesAttacked++;
            return prefix + targetLabel + " — урон " + normal + " " + FormatMultiplier(multiplier) + " = " + damage + pack +
                   (target.IsDefeated ? ", выведен из строя." : ".") + RevealTarget(target);
        }

        private string ApplyStun(SandboxUnitState target)
        {
            target.IsStunned = true;
            target.StunCounted = false;
            CancelPreparedOnControl(target);
            return " «Ошеломлён»: без ответных ударов, −1 движения до конца следующего хода.";
        }

        private string ApplyNumb(SandboxUnitState target)
        {
            target.IsNumbed = true;
            target.NumbCounted = false;
            CancelPreparedOnControl(target);
            return " «Оцепенение»: на следующий ход без движения и ответов.";
        }

        private void CancelPreparedOnControl(SandboxUnitState unit)
        {
            if (telegraphs.Any(item => item.SourceId == unit.Id && item.Kind == SandboxTelegraphKind.Toll))
            {
                CancelTelegraphs(unit.Id, SandboxTelegraphKind.Toll);
                Log(unit.DisplayLabel + " потерял звон — подготовка сорвана, метки сняты.");
            }
        }

        // ---------- Контекстные действия ----------

        public List<SandboxContextAction> GetContextActions(string unitId)
        {
            List<SandboxContextAction> result = new List<SandboxContextAction>();
            SandboxUnitState unit = GetUnit(unitId);
            if (unit == null || CurrentUnit != unit || Phase != SandboxBattlePhase.InProgress || HasPendingRetaliation ||
                unit.ActionPoints <= 0 || unit.HasAttacked || unit.IsOffBoard)
                return result;

            SandboxUnitState own = GetAttachedParasite(unit);
            if (own != null && own.Team != unit.Team)
                result.Add(new SandboxContextAction { Kind = SandboxContextActionKind.RemoveParasite, TargetUnitId = unit.Id, Label = "Снять " + own.DisplayLabel.ToLowerInvariant() });
            foreach (SandboxUnitState ally in units.Where(other => other != unit && other.Team == unit.Team && !other.IsDefeated && !other.IsOffBoard &&
                                                                 other.Position.DistanceTo(unit.Position) == 1).OrderBy(other => other.Id, StringComparer.Ordinal))
            {
                SandboxUnitState parasite = GetAttachedParasite(ally);
                if (parasite != null && parasite.Team != unit.Team)
                    result.Add(new SandboxContextAction { Kind = SandboxContextActionKind.RemoveParasite, TargetUnitId = ally.Id, Label = "Снять паразита с " + ally.DisplayLabel });
                SandboxLink allyHold = FindHoldingLink(ally);
                if (allyHold != null && allyHold.Kind == SandboxLinkKind.Engulf)
                    result.Add(new SandboxContextAction { Kind = SandboxContextActionKind.PullFree, TargetUnitId = ally.Id, LinkId = allyHold.Id, Label = "Вытащить " + ally.DisplayLabel });
            }
            SandboxLink hold = FindHoldingLink(unit);
            if (hold != null)
                result.Add(new SandboxContextAction { Kind = SandboxContextActionKind.BreakFree, TargetUnitId = unit.Id, LinkId = hold.Id, Label = "Вырваться" });

            // Бриф «Поверхности арены»: разрушаемые объекты местности (плетень,
            // корневое гнездо) — та же команда «Ударить»; дальним — по линии огня.
            foreach (SandboxBattleObject item in objects.Where(item => !item.IsDestroyed && (item.OwnerId != null || (item.IsTerrain && !item.Indestructible)))
                         .OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                SandboxUnitState owner = item.OwnerId != null ? GetUnit(item.OwnerId) : null;
                if (owner != null && owner.Team == unit.Team)
                    continue;
                int distance = item.Cells.Count == 0 ? int.MaxValue : item.Cells.Min(cell => cell.DistanceTo(unit.Position));
                if (item.IsTerrain && distance > 1 && !item.Cells.Any(cell => CanShootFrom(unit.Position, cell)))
                    continue;
                if (item.Kind == SandboxObjectKind.Thread && distance <= 1)
                    result.Add(new SandboxContextAction { Kind = SandboxContextActionKind.CutThread, ObjectId = item.Id, Label = "Разрезать нить" });
                if (distance <= unit.AttackRange && distance >= (unit.AttackRange > 1 ? 0 : 0))
                    result.Add(new SandboxContextAction { Kind = SandboxContextActionKind.AttackObject, ObjectId = item.Id, Label = "Ударить: " + item.Label.ToLowerInvariant() + " (" + item.HitPoints + "/" + item.MaxHitPoints + " HP)" });
            }
            return result;
        }

        public bool TryContextAction(string unitId, SandboxContextAction action, out string message)
        {
            message = "Действие недоступно.";
            if (action == null)
                return false;
            SandboxContextAction match = GetContextActions(unitId).FirstOrDefault(item =>
                item.Kind == action.Kind && item.TargetUnitId == action.TargetUnitId && item.ObjectId == action.ObjectId);
            if (match == null)
                return false;
            SandboxUnitState unit = GetUnit(unitId);
            switch (match.Kind)
            {
                case SandboxContextActionKind.RemoveParasite:
                {
                    SandboxUnitState host = GetUnit(match.TargetUnitId);
                    SandboxLink link = links.First(item => item.Kind == SandboxLinkKind.Attach && item.TargetId == host.Id);
                    message = unit.DisplayLabel + " срывает паразита" + (host == unit ? " с себя." : " с " + host.DisplayLabel + ".");
                    RemoveLink(link, "сорван действием.");
                    break;
                }
                case SandboxContextActionKind.BreakFree:
                case SandboxContextActionKind.PullFree:
                {
                    SandboxLink link = links.First(item => item.Id == match.LinkId);
                    message = match.Kind == SandboxContextActionKind.BreakFree
                        ? unit.DisplayLabel + " вырывается."
                        : unit.DisplayLabel + " вытаскивает " + GetUnit(match.TargetUnitId).DisplayLabel + ".";
                    RemoveLink(link, "освобождён действием.");
                    break;
                }
                case SandboxContextActionKind.CutThread:
                {
                    SandboxBattleObject thread = GetObject(match.ObjectId);
                    thread.HitPoints = 0;
                    message = unit.DisplayLabel + " разрезает нить.";
                    break;
                }
                default:
                {
                    SandboxBattleObject item = GetObject(match.ObjectId);
                    int damage = Math.Max(1, unit.Damage);
                    item.HitPoints = Math.Max(0, item.HitPoints - damage);
                    message = unit.DisplayLabel + " бьёт " + item.Label.ToLowerInvariant() + ": −" + damage + " HP" +
                              (item.IsDestroyed ? " — разрушено." : " (осталось " + item.HitPoints + ").");
                    if (item.IsDestroyed)
                        OnObjectDestroyed(item);
                    break;
                }
            }
            unit.ActionPoints = 0;
            unit.RemainingMovement = 0;
            unit.HasAttacked = true;
            string done = message;
            message += string.Join(string.Empty, TakeEventLog().Where(entry => !done.Contains(entry)).Select(entry => " " + entry));
            EvaluateBattleOutcome();
            return true;
        }

        private void OnObjectDestroyed(SandboxBattleObject item)
        {
            item.HitPoints = 0;
            foreach (SandboxLink link in links.Where(link => link.ObjectId == item.Id).ToList())
                RemoveLink(link, item.Label.ToLowerInvariant() + " перерублена.");
        }
    }
}
