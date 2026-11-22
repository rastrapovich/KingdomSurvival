using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф завершения, этапы 7–12: «Окружён» (вычисляемое положение),
    // «Смятение» и «Подчинение» (детерминированная политика хода без смены
    // исходной стороны), «Под защитой союзника» (перехват до расчёта урона),
    // «При смерти» и спасение (только с явной возможностью у бойца), кокон и
    // выводок «Зародыша в теле». Плюс теги гибели/призыва этих механизмов.
    public sealed partial class SandboxBattle
    {
        public const string SurroundedId = "status.054";
        public const string ConfusionId = "status.059";
        public const string DominationId = "status.061";
        public const string GuardedId = "status.107";
        public const string DyingId = "status.150";
        public const string EmbryoId = "status.203";

        // Временная боевая заготовка выводка (не вид бестиария).
        public static SandboxUnitDefinition BroodDefinition(int hitPoints = 3) =>
            new SandboxUnitDefinition("brood.parasite", "Малый паразит", SandboxUnitRole.Beast, hitPoints, 1, 0, 1, 2, 0, 1, new[] { SandboxCombatTagRules.Beast });

        // ---------- Окружён ----------

        private List<SandboxUnitState> Encirclers(SandboxUnitState unit, HexCoord at)
        {
            return units.Where(u => u != unit && !u.IsDefeated && !u.IsOffBoard && u.Team != unit.Team && u.Position.DistanceTo(at) == 1).ToList();
        }

        private bool IsSurroundedAt(SandboxUnitState unit, HexCoord at)
        {
            if (unit.IsDefeated || unit.IsOffBoard)
                return false;
            bool ally = units.Any(u => u != unit && !u.IsDefeated && !u.IsOffBoard && u.Team == unit.Team && u.Position.DistanceTo(at) == 1);
            return !ally && Encirclers(unit, at).Count >= 2;
        }

        // Пересчёт после шага/сдвига/появления/гибели: состояние появляется и
        // исчезает от расстановки, у него один позиционный источник.
        internal void RecomputeSurrounded()
        {
            if (Phase != SandboxBattlePhase.InProgress)
                return;
            foreach (SandboxUnitState unit in units)
            {
                bool should = IsSurroundedAt(unit, unit.Position);
                bool has = unit.HasStatus(SurroundedId);
                if (should && !has)
                    AddComputedStatus(unit, SurroundedId);
                else if (!should && has)
                    RemoveStatus(unit, SurroundedId, "окружение разорвано", SandboxStatusEndReason.BrokenByDistance);
            }
        }

        // Состояние-факт: без реакций наложения и без отказа «уже действует».
        private void AddComputedStatus(SandboxUnitState unit, string statusId)
        {
            SandboxStatusDefinition def = SandboxStatusCatalog.Get(statusId);
            SandboxStatusInstance instance = new SandboxStatusInstance
            {
                InstanceId = "st:" + nextStatusSequence, Sequence = nextStatusSequence++, Definition = def, TargetUnitId = unit.Id,
                AppliedRound = Math.Max(1, Round), AppliedAtActivation = unit.ActivationCount
            };
            instance.charges.Add(new SandboxStatusCharge { AppliedAtActivation = unit.ActivationCount });
            unit.statuses.Add(instance);
            StatusRevision++;
            Emit(SandboxCombatEventKind.StatusApplied, null, unit, e => { e.StatusId = statusId; e.StatusInstanceId = instance.InstanceId; });
            Log(unit.DisplayLabel + ": «" + def.Name + "».");
        }

        // +1 цена первого добровольного выхода из соседства окружающих в эту активацию.
        private int SurroundExitCost(SandboxUnitState unit, HexCoord from, HexCoord to)
        {
            if (from != unit.Position || !unit.HasStatus(SurroundedId) || unit.passive.Counter("surround.paid") == unit.ActivationCount)
                return 0;
            if (!Encirclers(unit, from).Any(enemy => enemy.Position.DistanceTo(to) > 1))
                return 0;
            // «Разгон разрывает кольцо»: заряд темпа вместо доплаты.
            SandboxStatusInstance momentum = FindStatus(unit, SandboxStatusCatalog.Momentum);
            if (momentum != null && momentum.ChargeCount > 0 && HasPassive(unit, SandboxPassiveKind.MomentumBreaksRing))
                return 0;
            return 1;
        }

        private void AfterSurroundExit(SandboxUnitState unit, HexCoord origin)
        {
            if (!IsSurroundedAt(unit, origin) || unit.passive.Counter("surround.paid") == unit.ActivationCount)
                return;
            if (!Encirclers(unit, origin).Any(enemy => enemy.Position.DistanceTo(unit.Position) > 1))
                return;
            unit.passive.Counters["surround.paid"] = unit.ActivationCount;
            SandboxStatusInstance momentum = FindStatus(unit, SandboxStatusCatalog.Momentum);
            SandboxPassiveRule ring = ReadyRule(unit, SandboxPassiveKind.MomentumBreaksRing);
            if (momentum != null && ring != null)
            {
                MarkUsed(unit, ring, null);
                if (momentum.charges.Count > 1)
                {
                    momentum.charges.RemoveAt(0);
                    StatusRevision++;
                }
                else
                    RemoveStatusInstance(unit, momentum, RuleName(ring) + " — заряд темпа вместо доплаты", endReason: SandboxStatusEndReason.Consumed);
            }
        }

        // ---------- Смятение и Подчинение ----------

        private bool PerceivedBy(SandboxUnitState viewer, SandboxUnitState other)
        {
            if (other == viewer || other.IsDefeated || other.IsOffBoard)
                return false;
            if (other.Team != viewer.Team && (other.IsHidden || other.HasStatus(SandboxStatusCatalog.Invisible)))
                return false;
            return true;
        }

        // Принудительная активация: 1 обычная атака по выбранной цели, движение
        // не по известной опасности; без предметов и способностей. true — ход занят.
        private bool ProcessForcedActivation(SandboxUnitState unit)
        {
            int activation = unit.ActivationCount;
            SandboxStatusInstance confusion = unit.statuses.FirstOrDefault(s => s.StatusId == ConfusionId && s.AppliedAtActivation < activation);
            SandboxStatusInstance domination = unit.statuses.FirstOrDefault(s => s.StatusId == DominationId && s.AppliedAtActivation < activation);
            if (confusion == null && domination == null)
                return false;

            SandboxUnitState target;
            string reason;
            if (confusion != null)
            {
                reason = "«Смятение»";
                // Ближайшая живая воспринимаемая цель любой стороны; равенство — исходный враг, затем ID.
                target = units.Where(u => PerceivedBy(unit, u))
                    .OrderBy(u => u.Position.DistanceTo(unit.Position))
                    .ThenBy(u => u.Team == unit.Team ? 1 : 0)
                    .ThenBy(u => u.Id, StringComparer.Ordinal).FirstOrDefault();
            }
            else
            {
                reason = "«Подчинение»";
                SandboxUnitState master = GetUnit(domination.SourceUnitId);
                SandboxTeam side = master != null ? master.Team : unit.Team;
                // Цель — противник стороны источника; не сам носитель.
                target = units.Where(u => PerceivedBy(unit, u) && u.Team != side)
                    .OrderBy(u => u.Position.DistanceTo(unit.Position)).ThenBy(u => u.HitPoints).ThenBy(u => u.Id, StringComparer.Ordinal).FirstOrDefault();
            }
            if (target == null || !TryForcedStrike(unit, target, reason))
            {
                Log(unit.DisplayLabel + ": " + reason + " — законной цели нет, держит оборону.");
                if (unit.ActionPoints > 0)
                {
                    unit.IsGuarding = true;
                    unit.ActionPoints = 0;
                }
            }
            unit.ActionPoints = 0;
            unit.RemainingMovement = 0;
            return true;
        }

        private bool TryForcedStrike(SandboxUnitState unit, SandboxUnitState target, string reason)
        {
            int range = Math.Max(1, unit.AttackRange);
            bool CanHitFrom(HexCoord cell)
            {
                int d = cell.DistanceTo(target.Position);
                return d >= 1 && d <= range && (d == 1 || CanShootFrom(cell, target.Position));
            }
            HexCoord from = unit.Position;
            if (!CanHitFrom(from))
            {
                Dictionary<HexCoord, int> reach = BuildReachable(unit, Math.Max(0, unit.RemainingMovement));
                FilterStatusReachable(unit, reach);
                HexCoord? best = reach.Where(pair => CanHitFrom(pair.Key) && HazardAt(unit, pair.Key) == 0)
                    .OrderBy(pair => pair.Value).ThenBy(pair => pair.Key).Select(pair => (HexCoord?)pair.Key).FirstOrDefault();
                if (!best.HasValue)
                    return false;
                SetUnitPosition(unit, best.Value);
                unit.MovedThisActivation = true;
                from = best.Value;
            }
            int damage = BuildAttackPreview(unit, target, from).Damage;
            int before = target.HitPoints;
            RecordHit(unit, target, damage, from);
            ApplyHit(SandboxHitKind.Attack, unit, target, damage);
            unit.HasAttacked = true;
            Log(unit.DisplayLabel + ": " + reason + " — бьёт " + target.DisplayLabel + " на " + (before - target.HitPoints) + " урона.");
            EvaluateBattleOutcome();
            return true;
        }

        // ---------- Под защитой союзника ----------

        private bool CanGuardNow(SandboxUnitState guardian, SandboxUnitState ward)
        {
            return guardian != null && !guardian.IsDefeated && !guardian.IsOffBoard && guardian.Position.DistanceTo(ward.Position) == 1 &&
                   !StrongControlled(guardian) && !IsHeld(guardian);
        }

        // Направленная обычная атака по подопечному уходит защитнику до расчёта урона.
        internal SandboxUnitState InterceptTarget(SandboxUnitState attacker, SandboxUnitState target)
        {
            if (attacker == null || target == null || attacker.Team == target.Team)
                return target;
            SandboxStatusInstance cover = target.statuses.FirstOrDefault(s => s.StatusId == GuardedId);
            if (cover == null)
                return target;
            SandboxUnitState guardian = GetUnit(cover.SourceUnitId);
            if (!CanGuardNow(guardian, target))
            {
                RemoveStatusInstance(target, cover, "защитник не может прикрыть", endReason: SandboxStatusEndReason.BrokenByDistance);
                return target;
            }
            RemoveStatusInstance(target, cover, guardian.DisplayLabel + " принимает удар на себя", endReason: SandboxStatusEndReason.Consumed, actor: guardian);
            Emit(SandboxCombatEventKind.Reaction, guardian, guardian, e => { e.OriginalTargetId = target.Id; e.StatusId = GuardedId; e.Note = "intercept"; });
            return guardian;
        }

        // ---------- При смерти и спасение ----------

        // Падение в 0 HP участника с явной возможностью спасения: ещё не окончательная смерть.
        private bool TryEnterDying(SandboxUnitState unit)
        {
            if (!unit.Definition.CanBeRescuedInBattle || unit.passive.RescueUsed || unit.IsDying || unit.passive.ForceFinalDeath)
                return false;
            unit.IsDying = true;
            unit.passive.DyingSinceActivation = unit.ActivationCount;
            // Связи и ауры, которым нужен действующий участник, теряют силу сразу.
            foreach (SandboxLink link in links.Where(item => item.SourceId == unit.Id || item.TargetId == unit.Id).ToList())
                RemoveLink(link, unit.DisplayLabel + " при смерти — связь исчезла.");
            foreach (SandboxStatusInstance control in unit.statuses.Where(s => s.Definition.StrongControl || !s.Definition.IsHarmful).ToList())
                unit.statuses.Remove(control);
            unit.IsGuarding = false;
            AddComputedStatus(unit, DyingId);
            Emit(SandboxCombatEventKind.Downed, null, unit);
            Log(unit.DisplayLabel + " при смерти — соседний союзник может спасти до конца его следующего хода.");
            return true;
        }

        // Окончательная смерть: добивание, истёкшее окно, тики.
        private void FinalizeDeath(SandboxUnitState unit, string why)
        {
            if (!unit.IsDying)
                return;
            unit.IsDying = false;
            unit.passive.ForceFinalDeath = true;
            unit.HitPoints = 0;
            SandboxStatusInstance dying = FindStatus(unit, DyingId);
            if (dying != null)
                unit.statuses.Remove(dying);
            Log(unit.DisplayLabel + ": " + why + " — окончательная смерть.");
            OnUnitDefeated(unit);
        }

        public bool CanRescue(SandboxUnitState rescuer, SandboxUnitState dying)
        {
            return rescuer != null && dying != null && dying.IsDying && !dying.passive.RescueUsed && rescuer != dying &&
                   rescuer.Team == dying.Team && !rescuer.IsDefeated && !rescuer.IsOffBoard && rescuer.Position.DistanceTo(dying.Position) == 1;
        }

        private string PerformRescue(SandboxUnitState rescuer, SandboxUnitState dying)
        {
            dying.IsDying = false;
            dying.passive.RescueUsed = true;
            dying.HitPoints = 1;
            SandboxStatusInstance mark = FindStatus(dying, DyingId);
            if (mark != null)
                RemoveStatusInstance(dying, mark, null, endReason: SandboxStatusEndReason.CleansedByAction, actor: rescuer);
            Emit(SandboxCombatEventKind.RescuePerformed, rescuer, dying);
            string note = rescuer.DisplayLabel + " спасает " + dying.DisplayLabel + " — 1 HP.";
            // «Спасённый держится крепче».
            SandboxPassiveRule firm = ReadyRule(dying, SandboxPassiveKind.RescuedTemporaryPool);
            if (firm != null)
            {
                MarkUsed(dying, firm, null);
                ApplyStatus("status.148", dying, dying, firm.Profile);
            }
            RecomputeSurrounded();
            return note;
        }

        // Пропущенная активация лежащего: тики по данным, в конце — истечение окна.
        private void ProcessDownedActivation(SandboxUnitState unit)
        {
            unit.ActivationCount++;
            foreach (SandboxStatusInstance tick in unit.statuses.Where(s => s.Definition.Has(SandboxStatusOpKind.TickDamage)).ToList())
            {
                if (!unit.IsDying)
                    break;
                FinalizeDeath(unit, "«" + tick.Definition.Name + "» добивает лежащего");
            }
            if (unit.IsDying && unit.ActivationCount > unit.passive.DyingSinceActivation)
                FinalizeDeath(unit, "окно спасения истекло");
        }

        // ---------- Кокон и выводок ----------

        private void TryCreateCocoon(SandboxUnitState host)
        {
            SandboxStatusInstance infection = FindStatus(host, EmbryoId);
            if (infection == null)
                return;
            string key = host.Id + "+" + infection.InstanceId;
            if (!cocoonKeys.Add(key))
                return;
            string sourceId = infection.SourceUnitId ?? string.Empty;
            cocoonsBySource.TryGetValue(sourceId, out int made);
            if (made >= 2)
            {
                Log("Зародыш: предел 2 кокона от источника за бой — кокон не создан.");
                return;
            }
            cocoonsBySource[sourceId] = made + 1;
            SandboxUnitState source = GetUnit(sourceId);
            SandboxBattleObject cocoon = CreateObject(SandboxObjectKind.Cocoon, null, 3);
            cocoon.Cells.Add(host.Position);
            cocoon.SnapshotTeam = source != null ? source.Team : (host.Team == SandboxTeam.Player ? SandboxTeam.Enemy : SandboxTeam.Player);
            cocoon.SourceId = sourceId;
            // Полный раунд предупреждения: создан во время раунда R — вылупление в начале R+2.
            cocoon.ExpiresAtRound = CurrentUnit != null ? Round + 2 : Round + 1;
            Log("На месте " + host.DisplayLabel + " появляется кокон (3 HP) — вылупится в начале раунда " + cocoon.ExpiresAtRound + ".");
        }

        private readonly HashSet<string> cocoonKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> cocoonsBySource = new Dictionary<string, int>(StringComparer.Ordinal);

        private void ProcessCocoonsAtRoundStart()
        {
            foreach (SandboxBattleObject cocoon in objects.Where(o => o.Kind == SandboxObjectKind.Cocoon && !o.IsDestroyed && o.ExpiresAtRound <= Round).ToList())
            {
                HexCoord center = cocoon.Cells[0];
                HexCoord? cell = new[] { center }.Concat(center.Neighbors()).Where(IsFreeCell).Cast<HexCoord?>().FirstOrDefault();
                if (!cell.HasValue)
                {
                    Log("Кокону некуда вылупиться — ждёт следующего раунда.");
                    continue;
                }
                SpawnUnit(BroodDefinition(), cocoon.SnapshotTeam, cell.Value, cocoon.SourceId, "из кокона выходит");
                cocoon.HitPoints = 0;
            }
        }

        // Общий сервис появления: первая активация — со следующего раунда, без награды.
        internal SandboxUnitState SpawnUnit(SandboxUnitDefinition definition, SandboxTeam team, HexCoord cell, string sourceId, string verb)
        {
            int index = units.Count(u => u.SpawnedBySourceId == sourceId) + 1;
            SandboxUnitState spawn = new SandboxUnitState((string.IsNullOrEmpty(sourceId) ? "spawn" : sourceId) + ":brood:" + index + ":" + nextObjectId++, definition, team, cell)
            {
                SpawnedBySourceId = sourceId,
                ActsFromRound = Round + 1
            };
            units.Add(spawn);
            SandboxTerrainRules.RegisterBattle(new[] { spawn }, terrain);
            Log(definition.RoleLabel + " " + verb + " в " + cell + " — вступит в бой со следующего раунда.");
            // «Растёт от призыва»: первое появление союзного призыва в раунде.
            foreach (SandboxUnitState grower in units.Where(u => !u.IsDefeated && u.Team == team && u != spawn).ToList())
            {
                SandboxPassiveRule grow = ReadyRule(grower, SandboxPassiveKind.GrowOnAllySummon);
                if (grow == null || grower.passive.Stack("attack.summon") >= grow.Value2)
                    continue;
                MarkUsed(grower, grow, null);
                grower.passive.Stacks["attack.summon"] = grower.passive.Stack("attack.summon") + grow.Value;
                grower.passive.Stacks["attack.permanent"] = grower.passive.Stack("attack.permanent") + grow.Value;
                Log(grower.DisplayLabel + ": " + RuleName(grow) + " — +" + grow.Value + " Атака.");
            }
            RecomputeSurrounded();
            return spawn;
        }

        // ---------- Гибель: выводок, взрыв с предупреждением, останки ----------

        private readonly List<(List<HexCoord> Cells, int Damage, string SourceId, string Label)> pendingBlasts = new List<(List<HexCoord>, int, string, string)>();

        private void OnFinalDeathPassives(SandboxUnitState dead)
        {
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(dead, SandboxPassiveKind.SplitIntoBrood).ToList())
            {
                List<HexCoord> cells = new[] { dead.Position }.Concat(dead.Position.Neighbors().OrderBy(c => c))
                    .Where(c => IsInside(c) && IsEnterable(c) && units.All(u => u == dead || u.IsDefeated || u.IsOffBoard || u.Position != c)).Take(rule.Value).ToList();
                foreach (HexCoord cell in cells)
                    SpawnUnit(BroodDefinition(1), dead.Team, cell, dead.Id, "(«" + def.Name + "») появляется");
            }
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(dead, SandboxPassiveKind.DelayedDeathBlast))
            {
                pendingBlasts.Add((dead.Position.Neighbors().Where(IsInside).ToList(), rule.Value, dead.Id, "«" + def.Name + "»"));
                Log(dead.DisplayLabel + ": «" + def.Name + "» — соседние гексы под угрозой взрыва в конце раунда.");
            }
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(dead, SandboxPassiveKind.LeaveRemains))
            {
                AddEffect(dead.Position, SandboxEffectKind.Remains, 0, rule.Value, dead.Id);
                Log(dead.DisplayLabel + ": «" + def.Name + "» — на гексе остаются опасные останки.");
            }
        }

        private void ProcessDeathBlastsAtRoundStart()
        {
            foreach ((List<HexCoord> cells, int damage, string sourceId, string label) in pendingBlasts.ToList())
            {
                pendingBlasts.Remove((cells, damage, sourceId, label));
                foreach (SandboxUnitState unit in units.Where(u => !u.IsDefeated && !u.IsOffBoard && cells.Contains(u.Position)).OrderBy(u => u.Id, StringComparer.Ordinal).ToList())
                    DealStatusDamage(unit, damage, null, label + " взрывается", SandboxDamageType.Fire);
            }
        }

        // Конец своего хода на останках: «Опасные останки» ранят, «Поглощает останки» лечит.
        private void ProcessRemainsAtActivationEnd(SandboxUnitState unit)
        {
            if (unit.IsDefeated || unit.IsOffBoard)
                return;
            SandboxCellEffect remains = EffectsAt(unit.Position).FirstOrDefault(e => e.Kind == SandboxEffectKind.Remains);
            SandboxUnitState corpse = units.FirstOrDefault(u => u.IsDefeated && !u.IsDying && !u.CorpseConsumed && u.Position == unit.Position);
            SandboxPassiveRule eater = ReadyRule(unit, SandboxPassiveKind.ConsumeRemains);
            if (eater != null && (remains != null || corpse != null))
            {
                MarkUsed(unit, eater, null);
                if (remains != null)
                    RemoveEffect(unit.Position, SandboxEffectKind.Remains);
                else
                    corpse.CorpseConsumed = true;
                HealLog(unit, eater.Value, RuleName(eater).Trim('«', '»'));
                return;
            }
            if (remains != null)
                DealStatusDamage(unit, Math.Max(1, remains.Damage), null, "опасные останки", SandboxDamageType.Poison);
        }

        // ---------- Начало боя ----------

        // «Делит связь»: до боя связывается с ближайшим живым союзником в радиусе 3.
        private void InitializeStatusPassives()
        {
            foreach (SandboxUnitState unit in units.Where(u => !u.IsDefeated).OrderBy(u => u.Id, StringComparer.Ordinal).ToList())
            {
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.SharedFateLink))
                {
                    SandboxUnitState partner = units.Where(u => u != unit && !u.IsDefeated && !u.IsOffBoard && u.Team == unit.Team &&
                                                                u.Position.DistanceTo(unit.Position) <= SandboxStatusRules.LinkBreakDistance)
                        .OrderBy(u => u.Position.DistanceTo(unit.Position)).ThenBy(u => u.Id, StringComparer.Ordinal).FirstOrDefault();
                    if (partner == null)
                    {
                        Log(unit.DisplayLabel + ": «" + def.Name + "» — союзника в радиусе 3 нет, связи нет.");
                        continue;
                    }
                    ApplyStatus(SandboxStatusCatalog.SharedWounds, unit, partner, new SandboxStatusProfile { ProfileId = "shared_fate", UntilBattleEnd = true });
                }
            }
            RecomputeSurrounded();
        }
    }
}
