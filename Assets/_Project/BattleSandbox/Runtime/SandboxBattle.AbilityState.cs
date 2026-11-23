using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    [Flags]
    public enum SandboxCellState
    {
        None = 0,
        ShallowWater = 1,
        LooseEarth = 2,
        Pit = 4,
        Subsided = 8
    }

    // ПР-16: состояние способностей боя — клетки, объекты, связи, объявленные
    // угрозы. Один владелец фактического состояния: UI получает снимок и
    // посылает команды, ИИ проверяет те же правила.
    public sealed partial class SandboxBattle
    {
        public const int PackBonusDefault = 1;

        private readonly Dictionary<HexCoord, SandboxCellState> cellStates = new Dictionary<HexCoord, SandboxCellState>();
        private readonly Dictionary<HexCoord, int> looseEarthUntilRound = new Dictionary<HexCoord, int>();
        private readonly Dictionary<HexCoord, int> pitUntilRound = new Dictionary<HexCoord, int>();
        private readonly List<SandboxBattleObject> objects = new List<SandboxBattleObject>();
        private readonly List<SandboxLink> links = new List<SandboxLink>();
        private readonly List<SandboxTelegraph> telegraphs = new List<SandboxTelegraph>();
        private readonly List<string> eventLog = new List<string>();
        private readonly List<PendingAfterReaction> afterReaction = new List<PendingAfterReaction>();
        private readonly Dictionary<string, int> tollPreparedAt = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> floodActivations = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> justEmerged = new Dictionary<string, bool>(StringComparer.Ordinal);
        private int floodCellsCreated;
        private int nextObjectId = 1;

        private sealed class PendingAfterReaction
        {
            public SandboxLinkKind Kind;
            public string SourceId;
            public string TargetId;
            public string AbilityId;
        }

        public IReadOnlyList<SandboxBattleObject> Objects => objects;
        public IReadOnlyList<SandboxLink> Links => links;
        public IReadOnlyList<SandboxTelegraph> Telegraphs => telegraphs;
        public IReadOnlyDictionary<HexCoord, SandboxCellState> CellStates => cellStates;
        public int FloodCellsCreated => floodCellsCreated;

        // Разведка заметила скрытых (Срубник): явный входной параметр встречи.
        public bool HiddenUnitsScouted { get; set; }

        // Общий лимит живых противников одной стороны для появления существ.
        public int MaxLivingPerSide { get; set; } = 8;

        // Тип существа по ID — для вылупления (Гнездовая матка).
        public Func<string, SandboxUnitDefinition> ResolveUnitType { get; set; }

        // События, случившиеся сами (тики, выходы, разливы): UI забирает их в журнал.
        public List<string> TakeEventLog()
        {
            List<string> result = new List<string>(eventLog);
            eventLog.Clear();
            return result;
        }

        private void Log(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                eventLog.Add(message.Trim());
        }

        // ---------- Клетки ----------

        public SandboxCellState GetCellState(HexCoord cell)
        {
            return cellStates.TryGetValue(cell, out SandboxCellState state) ? state : SandboxCellState.None;
        }

        public bool HasCellState(HexCoord cell, SandboxCellState flag)
        {
            return (GetCellState(cell) & flag) != 0;
        }

        private void SetCellFlag(HexCoord cell, SandboxCellState flag, bool on)
        {
            SandboxCellState state = GetCellState(cell);
            state = on ? state | flag : state & ~flag;
            if (state == SandboxCellState.None)
                cellStates.Remove(cell);
            else
                cellStates[cell] = state;
        }

        // Стартовая вода раскладки встречи (Речной узел, проверки Гнильца).
        public void SetInitialWater(IEnumerable<HexCoord> cells)
        {
            if (cells == null)
                return;
            foreach (HexCoord cell in cells)
            {
                if (IsInside(cell) && GetTerrain(cell) != SandboxTerrain.Impassable)
                    SetCellFlag(cell, SandboxCellState.ShallowWater, true);
            }
        }

        // Можно ли встать на клетку (без учёта бойцов).
        public bool IsEnterable(HexCoord cell)
        {
            return IsInside(cell) &&
                   GetTerrain(cell) != SandboxTerrain.Impassable &&
                   !IsGroundBlocking(cell) &&
                   !BlockedByTerrainObject(cell) &&
                   !HasCellState(cell, SandboxCellState.Pit) &&
                   FindEggAt(cell) == null;
        }

        // Цена входа без особенностей бойца: холм, мелкая вода, разрыхлённая
        // земля, просадка — 2; поверхности арены — SandboxBattle.Surfaces.
        public int GetStepCost(HexCoord cell) => GetStepCost(null, cell);

        public bool IsFreeCell(HexCoord cell)
        {
            return IsEnterable(cell) && GetUnitAt(cell) == null;
        }

        // Гекс чужой нити: вход на него останавливает движение.
        public bool IsMovementStopCell(SandboxUnitState unit, HexCoord cell)
        {
            foreach (SandboxBattleObject thread in objects)
            {
                if (thread.Kind == SandboxObjectKind.Thread && !thread.IsDestroyed &&
                    (unit == null || thread.OwnerId != unit.Id) && thread.Cells.Contains(cell))
                    return true;
            }
            return false;
        }

        public SandboxBattleObject FindEggAt(HexCoord cell)
        {
            return objects.FirstOrDefault(item => item.Kind == SandboxObjectKind.Egg && !item.IsDestroyed && item.Cells.Count > 0 && item.Cells[0] == cell);
        }

        public SandboxBattleObject GetObject(string objectId)
        {
            return objects.FirstOrDefault(item => item.Id == objectId);
        }

        // Линия видимости способностей: между концами нет непроходимых клеток.
        public bool HasLineOfSight(HexCoord from, HexCoord to)
        {
            // Бриф «Поверхности арены»: общая линия огня (высокие объекты,
            // обрыв, прежние непроходимые клетки; симметрична).
            return GetLineOfFire(from, to).Clear;
        }

        // ---------- Бойцы ----------

        // Мёртвые, не съеденные тела: доступны Костяному садовнику.
        public IEnumerable<SandboxUnitState> Corpses =>
            units.Where(unit => unit.IsDefeated && !unit.IsDying && !unit.CorpseConsumed);

        public SandboxUnitState GetAttachedParasite(SandboxUnitState host)
        {
            if (host == null)
                return null;
            SandboxLink link = links.FirstOrDefault(item => item.Kind == SandboxLinkKind.Attach && item.TargetId == host.Id);
            return link != null ? GetUnit(link.SourceId) : null;
        }

        public SandboxLink FindHoldingLink(SandboxUnitState victim)
        {
            return victim == null
                ? null
                : links.FirstOrDefault(item => item.TargetId == victim.Id && item.Kind != SandboxLinkKind.Attach);
        }

        public bool IsHeld(SandboxUnitState unit)
        {
            return FindHoldingLink(unit) != null;
        }

        // Цель для выбора курсором на клетке: боец на поле, затем сидящий на
        // нём паразит другой стороны, затем зажатый в тоннеле.
        public SandboxUnitState GetSelectableAt(HexCoord cell, SandboxTeam viewer)
        {
            SandboxUnitState onBoard = GetUnitAt(cell);
            if (onBoard != null && onBoard.Team != viewer)
                return onBoard;
            if (onBoard != null)
            {
                SandboxUnitState parasite = GetAttachedParasite(onBoard);
                if (parasite != null && !parasite.IsDefeated && parasite.Team != viewer)
                    return parasite;
            }
            SandboxUnitState stuck = units.FirstOrDefault(unit => !unit.IsDefeated && unit.IsStuckInTunnel && unit.Position == cell && unit.Team != viewer);
            return stuck ?? onBoard;
        }

        internal void SetUnitPosition(SandboxUnitState unit, HexCoord cell)
        {
            HexCoord before = unit.Position;
            unit.Position = cell;
            foreach (SandboxLink link in links.Where(item => item.Kind == SandboxLinkKind.Attach && item.TargetId == unit.Id).ToList())
            {
                SandboxUnitState parasite = GetUnit(link.SourceId);
                if (parasite != null)
                    parasite.Position = cell;
            }
            if (before != cell && telegraphs.Any(item => item.Kind == SandboxTelegraphKind.Toll && item.SourceId == unit.Id))
            {
                CancelTelegraphs(unit.Id, SandboxTelegraphKind.Toll);
                Log(unit.DisplayLabel + " сдвинут с места подготовки — удар звона сорван, метки сняты.");
            }
            CheckLinkDistances();
        }

        private void CheckLinkDistances()
        {
            foreach (SandboxLink link in links.ToList())
            {
                SandboxUnitState source = GetUnit(link.SourceId);
                SandboxUnitState target = GetUnit(link.TargetId);
                if (source == null || target == null)
                    continue;
                int distance = source.Position.DistanceTo(target.Position);
                if (link.Kind == SandboxLinkKind.Engulf && distance > 1)
                {
                    RemoveLink(link, "захват разорван — бойцы больше не рядом.");
                }
                else if (link.Kind == SandboxLinkKind.Vine || link.Kind == SandboxLinkKind.Tendril)
                {
                    SandboxAbilityDefinition ability = source.Definition.FindAbility(
                        link.Kind == SandboxLinkKind.Vine ? SandboxAbilityKind.VineGrab : SandboxAbilityKind.TendrilGrab);
                    int maxLength = link.Kind == SandboxLinkKind.Vine
                        ? (ability != null && ability.Limit > 0 ? ability.Limit : 4)
                        : (ability != null ? ability.RangeMax + 1 : 4);
                    if (distance > maxLength)
                        RemoveLink(link, (link.Kind == SandboxLinkKind.Vine ? "лоза" : "жгут") + " не дотягивается и отпускает.");
                    else
                        UpdateLinkObjectCells(link);
                }
            }
            CheckStatusLinkDistances();
            RecomputeSurrounded();
        }

        private void UpdateLinkObjectCells(SandboxLink link)
        {
            SandboxBattleObject item = GetObject(link.ObjectId);
            SandboxUnitState source = GetUnit(link.SourceId);
            SandboxUnitState target = GetUnit(link.TargetId);
            if (item == null || source == null || target == null)
                return;
            item.Cells.Clear();
            item.Cells.AddRange(SandboxHexLine.Line(source.Position, target.Position));
        }

        // ---------- Начало боя ----------

        // Правило раскладки встречи ПР-16: если у Речного узла нет воды, у
        // него явно ставится стартовый локус (до 3 клеток рядом). Это видно
        // на поле и в журнале — не скрытое затопление случайной арены.
        private void PrepareStartingWater()
        {
            if (cellStates.Any(pair => (pair.Value & SandboxCellState.ShallowWater) != 0))
                return;
            foreach (SandboxUnitState unit in units.Where(item => item.Definition.HasAbility(SandboxAbilityKind.Flood)).OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                List<HexCoord> cells = new[] { unit.Position }.Concat(unit.Position.Neighbors().OrderBy(cell => cell))
                    .Where(cell => IsInside(cell) && GetTerrain(cell) != SandboxTerrain.Impassable &&
                                   (cell == unit.Position || GetUnitAt(cell) == null))
                    .Take(3).ToList();
                if (cells.Count == 0)
                    continue;
                SetInitialWater(cells);
                Log("Раскладка встречи: стартовая вода у " + unit.DisplayLabel + " — " + string.Join(", ", cells) + ".");
                return;
            }
        }

        private void InitializeAbilityState()
        {
            foreach (SandboxUnitState unit in units.ToList())
            {
                if (unit.Definition.HasAbility(SandboxAbilityKind.HiddenStart) && !HiddenUnitsScouted)
                    unit.IsHidden = true;

                SandboxAbilityDefinition tendrils = unit.Definition.FindAbility(SandboxAbilityKind.TendrilGrab);
                if (tendrils != null)
                {
                    for (int i = 0; i < Math.Max(1, tendrils.Limit); i++)
                    {
                        SandboxBattleObject tendril = CreateObject(SandboxObjectKind.Tendril, unit.Id, Math.Max(1, tendrils.ObjectHitPoints));
                        tendril.Cells.Add(unit.Position);
                    }
                }

                SandboxAbilityDefinition eggs = unit.Definition.FindAbility(SandboxAbilityKind.LayEgg);
                if (eggs != null)
                {
                    int placed = 0;
                    foreach (HexCoord cell in unit.Position.Neighbors().OrderBy(cell => cell))
                    {
                        if (placed >= Math.Max(0, eggs.Amount))
                            break;
                        if (!IsFreeCell(cell))
                            continue;
                        SandboxBattleObject egg = CreateObject(SandboxObjectKind.Egg, unit.Id, Math.Max(1, eggs.ObjectHitPoints));
                        egg.Cells.Add(cell);
                        egg.Counter = Math.Max(1, eggs.Duration);
                        placed++;
                    }
                    if (placed < eggs.Amount)
                        Log("Гнездовая матка: нет места для всех стартовых яиц — размещено " + placed + " из " + eggs.Amount + ".");
                }

                if (unit.Definition.HasAbility(SandboxAbilityKind.Flood) &&
                    !cellStates.Any(pair => (pair.Value & SandboxCellState.ShallowWater) != 0))
                {
                    Log("Ошибка настройки встречи: у Речного узла нет стартовой воды — разлив не начнётся.");
                }
            }
        }

        public static bool HasFloodWithoutWater(SandboxBattle battle)
        {
            return battle.units.Any(unit => unit.Definition.HasAbility(SandboxAbilityKind.Flood)) &&
                   !battle.cellStates.Any(pair => (pair.Value & SandboxCellState.ShallowWater) != 0);
        }

        private SandboxBattleObject CreateObject(SandboxObjectKind kind, string ownerId, int hitPoints)
        {
            SandboxBattleObject item = new SandboxBattleObject
            {
                Id = kind.ToString().ToLowerInvariant() + ":" + nextObjectId++,
                Kind = kind,
                OwnerId = ownerId,
                HitPoints = hitPoints,
                MaxHitPoints = hitPoints
            };
            objects.Add(item);
            return item;
        }

        // ---------- Раунд ----------

        private void ProcessRoundStart()
        {
            ProcessSurfaceRoundStart();
            foreach (SandboxBattleObject thread in objects.Where(item => item.Kind == SandboxObjectKind.Thread && !item.IsDestroyed && item.ExpiresAtRound <= Round).ToList())
            {
                thread.HitPoints = 0;
                Log("Нить истлела и опала.");
            }
            foreach (KeyValuePair<HexCoord, int> pair in looseEarthUntilRound.ToList())
            {
                if (pair.Value <= Round)
                {
                    looseEarthUntilRound.Remove(pair.Key);
                    SetCellFlag(pair.Key, SandboxCellState.LooseEarth, false);
                }
            }
            foreach (KeyValuePair<HexCoord, int> pair in pitUntilRound.ToList())
            {
                if (pair.Value <= Round)
                {
                    pitUntilRound.Remove(pair.Key);
                    SetCellFlag(pair.Key, SandboxCellState.Pit, false);
                    SetCellFlag(pair.Key, SandboxCellState.Subsided, true);
                }
            }
            objects.RemoveAll(item => item.IsDestroyed && item.Kind != SandboxObjectKind.Tendril);
            // Пакет состояний: Отложенный взрыв и Приговор (раунд R+2).
            ProcessStatusRoundStart();
            ProcessDeathBlastsAtRoundStart();
            ProcessCocoonsAtRoundStart();
        }

        // ---------- Начало активации ----------

        // false — активация закончилась сама (клещень после тика).
        private bool ProcessActivationStart(SandboxUnitState unit)
        {
            bool keepsTurn = true;
            // Бриф «Поверхности арены»: новый цикл опасности и поверхность под бойцом.
            ProcessHazardsAtActivation(unit);
            if (unit.IsDefeated)
                return false;

            // 1. Отложенные события этого источника.
            foreach (SandboxTelegraph telegraph in telegraphs.Where(item => item.SourceId == unit.Id).ToList())
            {
                switch (telegraph.Kind)
                {
                    case SandboxTelegraphKind.Flood:
                        telegraphs.Remove(telegraph);
                        foreach (HexCoord cell in telegraph.Cells)
                        {
                            SetCellFlag(cell, SandboxCellState.ShallowWater, true);
                            floodCellsCreated++;
                        }
                        Log(unit.DisplayLabel + ": разлив — " + telegraph.Cells.Count + " клетки стали мелкой водой.");
                        break;
                    case SandboxTelegraphKind.Subsidence:
                        telegraphs.Remove(telegraph);
                        ResolveSubsidence(unit, telegraph);
                        break;
                }
            }
            SandboxTelegraph exit = telegraphs.FirstOrDefault(item => item.SourceId == unit.Id && item.Kind == SandboxTelegraphKind.BurrowExit);
            if (exit != null)
            {
                telegraphs.Remove(exit);
                ResolveEmergence(unit, exit.Center, exit.Origin, exit.Cells);
            }
            else if (unit.IsStuckInTunnel)
            {
                TryLeaveTunnel(unit);
            }

            // Сорванный и ждущий места паразит пробует встать.
            if (unit.AwaitingPlacement)
                TryPlaceDetached(unit);

            // Матка: счётчики яиц и не больше одного вылупления за ход.
            if (unit.Definition.HasAbility(SandboxAbilityKind.LayEgg))
                AdvanceEggs(unit);

            // 2. Тики связей.
            foreach (SandboxLink link in links.Where(item => item.SourceId == unit.Id).ToList())
            {
                SandboxUnitState target = GetUnit(link.TargetId);
                if (target == null || target.IsDefeated)
                    continue;
                if (link.Kind == SandboxLinkKind.Attach || link.Kind == SandboxLinkKind.Engulf)
                {
                    if (link.CreatedAtSourceActivation >= unit.ActivationCount)
                        continue;
                    SandboxAbilityDefinition ability = unit.Definition.FindAbility(
                        link.Kind == SandboxLinkKind.Attach ? SandboxAbilityKind.Attach : SandboxAbilityKind.Engulf);
                    int amount = ability != null && ability.Amount > 0 ? ability.Amount : 2;
                    int before = target.HitPoints;
                    target.ReceiveDamage(amount);
                    int taken = before - target.HitPoints;
                    unit.DamageDealt += taken;
                    string note = unit.DisplayLabel + (link.Kind == SandboxLinkKind.Attach ? " пьёт кровь: " : " давит тиной: ") +
                                  target.DisplayLabel + " −" + taken + " HP (фиксированный урон)";
                    if (link.Kind == SandboxLinkKind.Attach)
                    {
                        int healBefore = unit.HitPoints;
                        unit.Heal(taken);
                        note += ", " + unit.DisplayLabel + " +" + (unit.HitPoints - healBefore) + " HP";
                    }
                    Log(note + (target.IsDefeated ? " — " + target.DisplayLabel + " выведен из строя." : "."));
                    if (target.IsDefeated)
                        OnUnitDefeated(target);
                }
            }
            if (unit.IsAttached)
            {
                keepsTurn = false;
            }

            // 3. Восстановление на мокрой клетке.
            SandboxAbilityDefinition regen = unit.Definition.FindAbility(SandboxAbilityKind.RegenOnWater);
            if (regen != null && !unit.IsOffBoard && HasCellState(unit.Position, SandboxCellState.ShallowWater) && unit.HitPoints < unit.MaxHitPoints)
            {
                int heal = Math.Max(1, (int)Math.Ceiling(unit.MaxHitPoints * Math.Max(1, regen.Amount) / 100m));
                int before = unit.HitPoints;
                unit.Heal(heal);
                Log(unit.DisplayLabel + " на мелкой воде восстанавливается: +" + (unit.HitPoints - before) + " HP.");
            }

            // 3а. Пакет состояний: восстановление → тики (≤3 HP) → смерть →
            // паника. Отдельный ход паразита выше не повторяется.
            ProcessPassiveActivationStart(unit);
            ProcessStatusSourceActivationStart(unit);
            bool controlled = ProcessStatusActivationStart(unit);
            if (unit.IsDefeated)
                return false;
            // Этапы 8–9: «Смятение» и «Подчинение» — детерминированный ход политикой.
            if (!controlled && ProcessForcedActivation(unit))
                return false;
            if (controlled)
            {
                // Сильный контроль: активация имеет Start и End, но без
                // движения и действия; срок расходуется в конце.
                unit.ActionPoints = 0;
                unit.RemainingMovement = 0;
                // Сдерживающее состояние могло истечь в этом же начале хода — имя берётся, если оно ещё есть.
                SandboxStatusInstance skip = unit.statuses.FirstOrDefault(item => item.Definition.Has(SandboxStatusOpKind.SkipActivation));
                Log(unit.DisplayLabel + " пропускает ход" + (skip != null ? ": «" + skip.Definition.Name + "»." : " (контроль)."));
                return false;
            }

            // 4. Ограничения состояний — к штатному бюджету хода.
            unit.RemainingMovement += PassiveMovementStart(unit);
            int statusMovement = StatusMovementMod(unit);
            if (statusMovement != 0)
                unit.RemainingMovement = Math.Max(0, unit.RemainingMovement + statusMovement);
            ApplyHalfSpeed(unit);
            if (StatusHas(unit, SandboxStatusOpKind.NoVoluntaryMove))
                unit.RemainingMovement = 0;
            if (!unit.IsNumbed && unit.NumbImmunity > 0)
            {
                unit.NumbImmunityCounted = true;
            }
            if (IsHeld(unit) || links.Any(item => item.SourceId == unit.Id && item.Kind == SandboxLinkKind.Engulf))
                unit.RemainingMovement = 0;
            if (unit.IsUnderground || unit.IsStuckInTunnel || unit.CanStrikeAfterExit)
                unit.RemainingMovement = 0;
            if (justEmerged.ContainsKey(unit.Id))
                unit.RemainingMovement = 0;
            if (unit.RemainingMovement <= 0)
            {
                unit.RemainingMovement = 0;
                unit.ActsWithoutMovement = true;
            }

            EvaluateBattleOutcome();
            return keepsTurn;
        }

        private void ProcessActivationEnd(SandboxUnitState unit)
        {
            unit.passive.MoveAfterActionBudget = 0;
            // Окно иммунитета многонога закрывается в конце хода без оцепенения.
            if (unit.NumbImmunityCounted && !unit.IsNumbed)
            {
                unit.NumbImmunity = 0;
                unit.NumbImmunityCounted = false;
            }
            // Пакет состояний: истечение по собственным ходам и ходам источника;
            // окончание оцепенения открывает прежнее окно NumbImmunity.
            ProcessPassiveActivationEnd(unit);
            ProcessRemainsAtActivationEnd(unit);
            ProcessStatusActivationEnd(unit);

            if (tollPreparedAt.TryGetValue(unit.Id, out int preparedAt) && preparedAt < unit.ActivationCount &&
                telegraphs.Any(item => item.SourceId == unit.Id && item.Kind == SandboxTelegraphKind.Toll))
            {
                CancelTelegraphs(unit.Id, SandboxTelegraphKind.Toll);
                Log(unit.DisplayLabel + ": звон рассеялся без удара — метки сняты.");
            }

            SandboxAbilityDefinition flood = unit.Definition.FindAbility(SandboxAbilityKind.Flood);
            if (flood != null && !unit.IsDefeated)
            {
                floodActivations.TryGetValue(unit.Id, out int count);
                count++;
                floodActivations[unit.Id] = count;
                if (count % Math.Max(1, flood.Duration) == 0)
                    DeclareFlood(unit, flood);
            }

            justEmerged.Remove(unit.Id);
            unit.CanStrikeAfterExit = false;
            unit.ActsWithoutMovement = false;
        }

        // ---------- Смерть ----------

        private readonly HashSet<string> defeatProcessed = new HashSet<string>(StringComparer.Ordinal);

        private void OnUnitDefeated(SandboxUnitState dead)
        {
            // Этап 11: «При смерти» — не окончательная смерть (нет наград, кокона, трупа).
            if (dead.IsDying || (!defeatProcessed.Contains(dead.Id) && TryEnterDying(dead)))
            {
                RecomputeSurrounded();
                return;
            }
            if (!defeatProcessed.Add(dead.Id))
                return;
            Emit(SandboxCombatEventKind.FinalDeath, null, dead);
            TryCreateCocoon(dead);
            OnFinalDeathPassives(dead);
            OnPassiveUnitDefeated(dead);
            OnStatusUnitDefeated(dead);
            foreach (SandboxLink link in links.Where(item => item.SourceId == dead.Id || item.TargetId == dead.Id).ToList())
                RemoveLink(link, link.SourceId == dead.Id ? "источник пал — связь исчезла." : "жертва пала — связь исчезла.");

            foreach (SandboxBattleObject item in objects.Where(item => item.OwnerId == dead.Id && !item.IsDestroyed).ToList())
            {
                if (item.Kind == SandboxObjectKind.Thread)
                    continue; // натянутое волокно доживает свой срок
                item.HitPoints = 0;
                item.HeldUnitId = null;
            }

            if (telegraphs.Any(item => item.SourceId == dead.Id))
            {
                telegraphs.RemoveAll(item => item.SourceId == dead.Id);
                Log(dead.DisplayLabel + " пал — объявленные им угрозы отменены.");
            }
            tollPreparedAt.Remove(dead.Id);
            dead.IsUnderground = false;
            dead.IsStuckInTunnel = false;
            dead.AwaitingPlacement = false;
            dead.AttachedToId = null;
            if (dead.IsHidden)
                dead.IsHidden = false;
            // Гибель соседа сразу пересчитывает «Окружён» у остальных бойцов.
            RecomputeSurrounded();
        }

        private void RemoveLink(SandboxLink link, string reason)
        {
            if (!links.Remove(link))
                return;
            SandboxUnitState source = GetUnit(link.SourceId);
            SandboxUnitState target = GetUnit(link.TargetId);
            string sourceLabel = source != null ? source.DisplayLabel : "?";
            string targetLabel = target != null ? target.DisplayLabel : "?";
            switch (link.Kind)
            {
                case SandboxLinkKind.Attach:
                    if (source != null)
                    {
                        source.AttachedToId = null;
                        SetLinkCooldown(source, SandboxAbilityKind.Attach);
                        if (!source.IsDefeated && target != null)
                            PlaceNear(source, target.Position);
                    }
                    Log(sourceLabel + " отделяется от " + targetLabel + ": " + reason);
                    break;
                case SandboxLinkKind.Engulf:
                    if (source != null)
                        SetLinkCooldown(source, SandboxAbilityKind.Engulf);
                    Log(targetLabel + " свободен от " + sourceLabel + ": " + reason);
                    break;
                case SandboxLinkKind.Vine:
                    SandboxBattleObject vine = GetObject(link.ObjectId);
                    if (vine != null)
                        vine.HitPoints = 0;
                    if (source != null)
                        SetLinkCooldown(source, SandboxAbilityKind.VineGrab);
                    Log(targetLabel + " освобождён от лозы: " + reason);
                    break;
                case SandboxLinkKind.Tendril:
                    SandboxBattleObject tendril = GetObject(link.ObjectId);
                    if (tendril != null)
                    {
                        tendril.HeldUnitId = null;
                        tendril.Cells.Clear();
                        if (source != null)
                            tendril.Cells.Add(source.Position);
                    }
                    Log(targetLabel + " выскальзывает из жгута: " + reason);
                    break;
            }
        }

        private static void SetLinkCooldown(SandboxUnitState source, SandboxAbilityKind kind)
        {
            SandboxAbilityDefinition ability = source.Definition.FindAbility(kind);
            if (ability != null)
                source.AbilityReadyAt[ability.Id] = source.ActivationCount + ability.Cooldown + 1;
        }

        // Ставит живого отделившегося на ближайшую свободную соседнюю клетку
        // по стабильному порядку; без места — «сорван, ожидает размещения».
        private void PlaceNear(SandboxUnitState unit, HexCoord around)
        {
            unit.AttachedToId = null;
            HexCoord? cell = around.Neighbors().Where(IsFreeCell).OrderBy(item => item).Cast<HexCoord?>().FirstOrDefault();
            if (cell.HasValue)
            {
                unit.AwaitingPlacement = false;
                unit.Position = cell.Value;
            }
            else
            {
                unit.AwaitingPlacement = true;
                unit.Position = around;
                Log(unit.DisplayLabel + " сорван, но рядом нет места — ждёт размещения.");
            }
        }

        private void TryPlaceDetached(SandboxUnitState unit)
        {
            HexCoord? cell = unit.Position.Neighbors().Where(IsFreeCell).OrderBy(item => item).Cast<HexCoord?>().FirstOrDefault();
            if (IsFreeCell(unit.Position))
                cell = unit.Position;
            if (!cell.HasValue)
                return;
            unit.AwaitingPlacement = false;
            unit.Position = cell.Value;
            Log(unit.DisplayLabel + " нашёл место на поле " + cell.Value + ".");
        }

        private void CancelTelegraphs(string sourceId, SandboxTelegraphKind kind)
        {
            telegraphs.RemoveAll(item => item.SourceId == sourceId && item.Kind == kind);
            if (kind == SandboxTelegraphKind.Toll)
                tollPreparedAt.Remove(sourceId);
        }

        // ---------- Скрытность ----------

        private string RevealAttacker(SandboxUnitState attacker)
        {
            if (attacker == null || !attacker.IsHidden)
                return string.Empty;
            attacker.IsHidden = false;
            return "Пень оживает — это " + attacker.DisplayLabel + "! ";
        }

        private string RevealTarget(SandboxUnitState target)
        {
            if (target == null || !target.IsHidden)
                return string.Empty;
            target.IsHidden = false;
            return " Подозрительный пень раскрыт: это " + target.DisplayLabel + ".";
        }

        // Контакт: подошедший вплотную раскрывает скрытого.
        private string AfterVoluntaryMove(SandboxUnitState mover)
        {
            string note = string.Empty;
            foreach (SandboxUnitState hidden in units.Where(unit => unit.IsHidden && !unit.IsDefeated && unit.Team != mover.Team &&
                                                                   unit.Position.DistanceTo(mover.Position) == 1).ToList())
            {
                hidden.IsHidden = false;
                note += " Вплотную видно: пень — это " + hidden.DisplayLabel + "!";
            }
            return note;
        }

        // ---------- Стая ----------

        public bool HasPackBonus(SandboxUnitState attacker, SandboxUnitState target, HexCoord attackPosition)
        {
            if (attacker == null || target == null || !attacker.HasTag(SandboxCombatTagRules.Pack) ||
                attackPosition.DistanceTo(target.Position) != 1)
                return false;
            return units.Any(other => other != attacker && other.Team == attacker.Team && !other.IsDefeated &&
                                      !other.IsOffBoard && other.HasTag(SandboxCombatTagRules.Pack) &&
                                      other.Position != attackPosition &&
                                      other.Position.DistanceTo(target.Position) == 1);
        }

        private string PackNote(SandboxUnitState attacker, SandboxUnitState target, HexCoord attackPosition)
        {
            return HasPackBonus(attacker, target, attackPosition)
                ? " (Стая: +" + SandboxCombatTagRules.PackAttackBonus + " Атака)"
                : string.Empty;
        }

        // ---------- После реакций ----------

        private string ResolveAfterReaction()
        {
            if (afterReaction.Count == 0)
                return string.Empty;
            string note = string.Empty;
            foreach (PendingAfterReaction pending in afterReaction.ToList())
            {
                afterReaction.Remove(pending);
                SandboxUnitState source = GetUnit(pending.SourceId);
                SandboxUnitState target = GetUnit(pending.TargetId);
                SandboxAbilityDefinition ability = source?.Definition.Abilities.FirstOrDefault(item => item.Id == pending.AbilityId);
                if (source == null || target == null || ability == null)
                    continue;
                if (source.IsDefeated || target.IsDefeated || source.Position.DistanceTo(target.Position) > 1)
                {
                    source.AbilityReadyAt[ability.Id] = source.ActivationCount + ability.Cooldown + 1;
                    continue;
                }
                if (pending.Kind == SandboxLinkKind.Attach)
                {
                    if (GetAttachedParasite(target) != null)
                        continue;
                    links.Add(new SandboxLink
                    {
                        Id = "link:" + nextObjectId++, Kind = SandboxLinkKind.Attach, SourceId = source.Id,
                        TargetId = target.Id, CreatedAtSourceActivation = source.ActivationCount
                    });
                    source.AttachedToId = target.Id;
                    source.Position = target.Position;
                    note += " " + source.DisplayLabel + " цепляется за " + target.DisplayLabel + ".";
                }
                else if (ReadyRule(target, SandboxPassiveKind.GrappleBreakOnce) is SandboxPassiveRule slip)
                {
                    // «Скользкая хватка»: первый Захват за бой срывается сразу.
                    MarkUsed(target, slip, null);
                    source.AbilityReadyAt[pending.AbilityId] = source.ActivationCount + ability.Cooldown + 1;
                    note += " " + target.DisplayLabel + " выскальзывает из захвата (" + RuleName(slip) + ").";
                }
                else
                {
                    links.Add(new SandboxLink
                    {
                        Id = "link:" + nextObjectId++, Kind = SandboxLinkKind.Engulf, SourceId = source.Id,
                        TargetId = target.Id, CreatedAtSourceActivation = source.ActivationCount
                    });
                    note += " " + source.DisplayLabel + " накрывает " + target.DisplayLabel + " — захвачен.";
                }
            }
            return note;
        }
    }
}
