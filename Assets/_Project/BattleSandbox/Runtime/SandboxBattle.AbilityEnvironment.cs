using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-16: среда и объекты — подкоп, провалы, разлив, яйца.
    public sealed partial class SandboxBattle
    {
        // Выход из-под земли: объявленная клетка → ближайшая свободная в
        // радиусе 1 → исходная точка (без атаки) → «Зажат в тоннеле».
        private void ResolveEmergence(SandboxUnitState unit, HexCoord exit, HexCoord origin, List<HexCoord> route)
        {
            unit.IsUnderground = false;
            HexCoord? cell = null;
            bool atOrigin = false;
            if (IsFreeCell(exit))
            {
                cell = exit;
            }
            else
            {
                cell = exit.Neighbors().Where(IsFreeCell).OrderBy(item => item.DistanceTo(exit)).ThenBy(item => item)
                    .Cast<HexCoord?>().FirstOrDefault();
                if (!cell.HasValue && IsFreeCell(origin))
                {
                    cell = origin;
                    atOrigin = true;
                }
            }

            bool pitBoss = unit.Definition.HasAbility(SandboxAbilityKind.PitCycle);
            SandboxAbilityDefinition burrow = unit.Definition.FindAbility(SandboxAbilityKind.Burrow);
            if (!cell.HasValue)
            {
                unit.IsStuckInTunnel = true;
                unit.Position = exit;
                Log(unit.DisplayLabel + " зажат в тоннеле под " + exit + ": выйти некуда. По маркеру выхода его можно бить (реальные HP).");
                if (pitBoss)
                    unit.PitPhase = 0;
                return;
            }

            unit.IsStuckInTunnel = false;
            unit.Position = cell.Value;
            unit.RemainingMovement = 0;
            unit.ActsWithoutMovement = true;
            if (route != null && burrow != null)
            {
                foreach (HexCoord step in route.Concat(new[] { cell.Value }))
                {
                    if (step == origin || !IsInside(step) || GetTerrain(step) == SandboxTerrain.Impassable)
                        continue;
                    SetCellFlag(step, SandboxCellState.LooseEarth, true);
                    looseEarthUntilRound[step] = Round + Math.Max(1, burrow.Duration);
                }
            }

            if (pitBoss)
            {
                unit.PitPhase = 0;
                justEmerged[unit.Id] = true;
                Log(unit.DisplayLabel + " выходит из-под земли в " + cell.Value + " — открыт до своего следующего хода.");
                return;
            }

            if (burrow != null)
                unit.AbilityReadyAt[burrow.Id] = unit.ActivationCount + burrow.Cooldown + 1;
            if (atOrigin)
            {
                Log(unit.DisplayLabel + ": выход занят — возвращается в нору " + cell.Value + " без удара.");
                return;
            }
            unit.CanStrikeAfterExit = true;
            Log(unit.DisplayLabel + " выходит из-под земли в " + cell.Value + (cell.Value != exit ? " (объявленная клетка занята)" : string.Empty) + ".");
        }

        private void TryLeaveTunnel(SandboxUnitState unit)
        {
            HexCoord exit = unit.Position;
            HexCoord? cell = new[] { unit.BurrowOrigin, exit }.Concat(exit.Neighbors().OrderBy(item => item))
                .Where(IsFreeCell).Cast<HexCoord?>().FirstOrDefault();
            if (!cell.HasValue)
            {
                Log(unit.DisplayLabel + " всё ещё зажат в тоннеле.");
                return;
            }
            unit.IsStuckInTunnel = false;
            unit.Position = cell.Value;
            unit.RemainingMovement = 0;
            unit.ActsWithoutMovement = true;
            Log(unit.DisplayLabel + " выбирается из тоннеля в " + cell.Value + ".");
        }

        // ---------- Хозяин тёмной ямы ----------

        private bool PlanSubsidence(SandboxUnitState unit, SandboxAbilityDefinition ability, out List<HexCoord> pits, out HexCoord exit)
        {
            pits = new List<HexCoord>();
            exit = unit.BurrowOrigin;
            HexCoord origin = unit.BurrowOrigin;
            List<SandboxUnitState> foes = Opponents(unit).ToList();
            int activePits = pitUntilRound.Count;
            List<HexCoord> candidates = CellsWithin(origin, Math.Max(1, ability.RangeMax))
                .Where(cell => IsDiggable(cell) && !HasCellState(cell, SandboxCellState.Pit) && FindEggAt(cell) == null)
                .OrderBy(cell => foes.Any(foe => foe.Position == cell) ? 0 : 1)
                .ThenBy(cell => foes.Count == 0 ? 0 : foes.Min(foe => foe.Position.DistanceTo(cell)))
                .ThenBy(cell => cell)
                .ToList();
            foreach (HexCoord cell in candidates)
            {
                if (pits.Count >= Math.Max(1, ability.Amount))
                    break;
                bool becomesPit = GetUnitAt(cell) == null;
                if (becomesPit && activePits + pits.Count(IsFreeCell) >= Math.Max(1, ability.Limit))
                    continue;
                List<HexCoord> trial = new List<HexCoord>(pits) { cell };
                if (!KeepsBattleConnected(trial.Where(IsFreeCell)))
                    continue;
                pits.Add(cell);
            }
            if (pits.Count == 0)
                return false;

            List<HexCoord> chosen = pits;
            SandboxUnitState nearest = foes.OrderBy(foe => foe.Position.DistanceTo(origin)).ThenBy(foe => foe.Id, StringComparer.Ordinal).FirstOrDefault();
            HexCoord? best = nearest == null
                ? (HexCoord?)null
                : nearest.Position.Neighbors()
                    .Where(cell => IsDiggable(cell) && IsFreeCell(cell) && !chosen.Contains(cell) && cell.DistanceTo(origin) <= ability.RangeMax + 1)
                    .OrderBy(cell => cell.DistanceTo(origin)).ThenBy(cell => cell)
                    .Cast<HexCoord?>().FirstOrDefault();
            exit = best ?? origin;
            return true;
        }

        // Новые ямы не должны навсегда разобщить живых участников.
        private bool KeepsBattleConnected(IEnumerable<HexCoord> newPits)
        {
            HashSet<HexCoord> blocked = new HashSet<HexCoord>(newPits);
            List<HexCoord> standing = units.Where(unit => !unit.IsDefeated && !unit.IsOffBoard).Select(unit => unit.Position).ToList();
            if (standing.Count < 2)
                return true;
            HashSet<HexCoord> seen = new HashSet<HexCoord> { standing[0] };
            Queue<HexCoord> queue = new Queue<HexCoord>();
            queue.Enqueue(standing[0]);
            while (queue.Count > 0)
            {
                HexCoord current = queue.Dequeue();
                foreach (HexCoord next in current.Neighbors())
                {
                    if (seen.Contains(next) || blocked.Contains(next) || !IsInside(next) ||
                        GetTerrain(next) == SandboxTerrain.Impassable || HasCellState(next, SandboxCellState.Pit))
                        continue;
                    seen.Add(next);
                    queue.Enqueue(next);
                }
            }
            return standing.All(seen.Contains);
        }

        private void ResolveSubsidence(SandboxUnitState boss, SandboxTelegraph telegraph)
        {
            SandboxAbilityDefinition ability = boss.Definition.FindAbility(SandboxAbilityKind.PitCycle);
            int duration = ability != null ? Math.Max(1, ability.Duration) : 2;
            foreach (HexCoord cell in telegraph.Cells.OrderBy(item => item))
            {
                SandboxUnitState victim = GetUnitAt(cell);
                if (victim != null && victim != boss)
                {
                    int damage = BuildAttackPreview(boss, victim, cell, false, ability != null ? ability.DamageMultiplier : 1m).Damage;
                    RecordHit(boss, victim, damage, cell);
                    ApplyHit(SandboxHitKind.Ability, boss, victim, damage);
                    string note = "Просадка в " + cell + ": " + victim.DisplayLabel + " −" + damage;
                    if (!victim.IsDefeated)
                    {
                        HexCoord? escape = cell.Neighbors().Where(IsFreeCell).OrderBy(item => item).Cast<HexCoord?>().FirstOrDefault();
                        if (escape.HasValue)
                        {
                            SetUnitPosition(victim, escape.Value);
                            note += ", сдвинут на " + escape.Value;
                            MakePit(cell, duration);
                        }
                        else
                        {
                            note += ", места нет — клетка осела, но держит";
                            SetCellFlag(cell, SandboxCellState.Subsided, true);
                        }
                    }
                    else
                    {
                        MakePit(cell, duration);
                    }
                    Log(note + ".");
                }
                else if (GetUnitAt(cell) == null)
                {
                    MakePit(cell, duration);
                    Log("Просадка в " + cell + ": яма на " + duration + " раунда.");
                }
            }
        }

        private void MakePit(HexCoord cell, int duration)
        {
            if (GetUnitAt(cell) != null)
            {
                SetCellFlag(cell, SandboxCellState.Subsided, true);
                return;
            }
            SetCellFlag(cell, SandboxCellState.Pit, true);
            pitUntilRound[cell] = Round + duration;
        }

        // ---------- Речной узел: разлив ----------

        private void DeclareFlood(SandboxUnitState unit, SandboxAbilityDefinition flood)
        {
            int limit = Math.Max(0, flood.Limit);
            int pending = telegraphs.Where(item => item.Kind == SandboxTelegraphKind.Flood).Sum(item => item.Cells.Count);
            int left = limit - floodCellsCreated - pending;
            if (left <= 0)
                return;
            HashSet<HexCoord> water = new HashSet<HexCoord>(cellStates.Where(pair => (pair.Value & SandboxCellState.ShallowWater) != 0).Select(pair => pair.Key));
            if (water.Count == 0)
                return;
            List<SandboxUnitState> foes = Opponents(unit).ToList();
            List<HexCoord> cells = water.SelectMany(cell => cell.Neighbors())
                .Distinct()
                .Where(cell => IsInside(cell) && GetTerrain(cell) != SandboxTerrain.Impassable && !water.Contains(cell) &&
                               !HasCellState(cell, SandboxCellState.Pit) && FindEggAt(cell) == null)
                .OrderBy(cell => foes.Count == 0 ? 0 : foes.Min(foe => foe.Position.DistanceTo(cell)))
                .ThenBy(cell => cell)
                .Take(Math.Min(Math.Max(1, flood.Amount), left))
                .ToList();
            if (cells.Count == 0)
                return;
            SandboxTelegraph telegraph = new SandboxTelegraph
            {
                Id = "flood:" + nextObjectId++, Kind = SandboxTelegraphKind.Flood, SourceId = unit.Id,
                Center = cells[0], Origin = unit.Position, Note = "Разлив на следующем ходу " + unit.DisplayLabel
            };
            telegraph.Cells.AddRange(cells);
            telegraphs.Add(telegraph);
            Log(unit.DisplayLabel + " объявляет разлив: " + string.Join(", ", cells) + " станут водой на его следующем ходу.");
        }

        // ---------- Гнездовая матка ----------

        private void AdvanceEggs(SandboxUnitState mother)
        {
            SandboxAbilityDefinition ability = mother.Definition.FindAbility(SandboxAbilityKind.LayEgg);
            List<SandboxBattleObject> eggs = objects.Where(item => item.Kind == SandboxObjectKind.Egg && !item.IsDestroyed && item.OwnerId == mother.Id)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
            foreach (SandboxBattleObject egg in eggs)
                egg.Counter = Math.Max(0, egg.Counter - 1);

            SandboxBattleObject ready = eggs.FirstOrDefault(item => item.Counter <= 0);
            if (ready == null)
                return;
            int limit = ability != null ? Math.Max(1, ability.Limit) : 3;
            int spawned = units.Count(item => !item.IsDefeated && item.SpawnedBySourceId == mother.Id);
            int living = units.Count(item => !item.IsDefeated && item.Team == mother.Team);
            if (spawned >= limit || living >= MaxLivingPerSide)
            {
                Log("Яйцо готово, но лимит живых существ заполнен — вылупление ждёт.");
                return;
            }
            string typeId = ability != null && !string.IsNullOrEmpty(ability.SpawnTypeId) ? ability.SpawnTypeId : "blood_kleshchen";
            SandboxUnitDefinition definition = ResolveUnitType?.Invoke(typeId);
            if (definition == null)
            {
                Log("Яйцо не вылупилось: нет существа «" + typeId + "» в базе.");
                return;
            }
            HexCoord cell = ready.Cells[0];
            ready.HitPoints = 0;
            if (GetUnitAt(cell) != null)
            {
                ready.HitPoints = 1;
                return;
            }
            SandboxUnitState spawn = new SandboxUnitState(mother.Id + ":spawn:" + (units.Count(item => item.SpawnedBySourceId == mother.Id) + 1), definition, mother.Team, cell)
            {
                SpawnedBySourceId = mother.Id
            };
            units.Add(spawn);
            SandboxTerrainRules.RegisterBattle(new[] { spawn }, terrain);
            Log("Из яйца в " + cell + " вылупляется " + spawn.DisplayLabel + " — вступит в бой со следующего раунда.");
        }
    }
}
