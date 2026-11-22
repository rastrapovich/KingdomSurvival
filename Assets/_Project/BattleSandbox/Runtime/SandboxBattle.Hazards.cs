using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф «Поверхности и окружение боевой арены», этап 3: временные
    // поверхности и области с явным сроком в раундах (созданная в раунде R со
    // сроком D снимается в начале R+D+1; 0 — до снятия), опасность один раз
    // на вид за цикл активации бойца и ограниченный набор взаимодействий
    // (огонь, холод, вода, ток) одной командой в стабильном порядке, без
    // цепных реакций. Числа — стартовые, для проверки на актуальных HP.
    public enum SandboxEffectKind
    {
        Fire,
        Tar,
        Web,
        Roots,
        Thorns,
        Poison,
        Smoke,
        Ice,
        Electric,
        // Иней на земле от холодного удара: шаг на клетку +1 для всех, без урона.
        Rime,
        // Опасные останки: стоящий в конце хода теряет HP.
        Remains
    }

    public enum SandboxImpulse
    {
        Fire,
        Cold,
        Water,
        Electric
    }

    public sealed class SandboxCellEffect
    {
        public SandboxEffectKind Kind { get; internal set; }
        public int CreatedRound { get; internal set; }
        // Снимается в начале этого раунда; 0 — пока не снимут взаимодействием.
        public int ExpiresAtRound { get; internal set; }
        public int Damage { get; internal set; }
        // Источник (корневое гнездо и т. п.): его уничтожение снимает только своё.
        public string SourceId { get; internal set; } = string.Empty;

        public int RoundsLeft(int round) => ExpiresAtRound <= 0 ? -1 : Math.Max(0, ExpiresAtRound - round);
    }

    public static class SandboxHazardRules
    {
        public static int FireDamage = 2;
        public static int ThornsDamage = 1;
        public static int PoisonDamage = 2;
        public static int ElectricDamage = 2;
        public static int ElectricMaxCells = 12;
        public static int DefaultDurationRounds = 2;

        public static string Label(SandboxEffectKind kind)
        {
            switch (kind)
            {
                case SandboxEffectKind.Fire: return "огонь";
                case SandboxEffectKind.Tar: return "смола";
                case SandboxEffectKind.Web: return "паутина";
                case SandboxEffectKind.Roots: return "живые корни";
                case SandboxEffectKind.Thorns: return "колючки";
                case SandboxEffectKind.Poison: return "ядовитая жижа";
                case SandboxEffectKind.Smoke: return "дым";
                case SandboxEffectKind.Ice: return "лёд";
                case SandboxEffectKind.Rime: return "иней на земле";
                case SandboxEffectKind.Remains: return "опасные останки";
                default: return "ток в воде";
            }
        }

        public static bool IsHazard(SandboxEffectKind kind) =>
            kind == SandboxEffectKind.Fire || kind == SandboxEffectKind.Thorns || kind == SandboxEffectKind.Poison || kind == SandboxEffectKind.Electric;

        public static int DefaultDamage(SandboxEffectKind kind)
        {
            switch (kind)
            {
                case SandboxEffectKind.Fire: return FireDamage;
                case SandboxEffectKind.Thorns: return ThornsDamage;
                case SandboxEffectKind.Poison: return PoisonDamage;
                case SandboxEffectKind.Electric: return ElectricDamage;
                default: return 0;
            }
        }
    }

    public sealed partial class SandboxBattle
    {
        private readonly Dictionary<HexCoord, List<SandboxCellEffect>> cellEffects = new Dictionary<HexCoord, List<SandboxCellEffect>>();
        // Виды опасности, уже сработавшие у бойца в текущем цикле активации.
        private readonly Dictionary<string, HashSet<SandboxEffectKind>> hazardCycle = new Dictionary<string, HashSet<SandboxEffectKind>>();

        public IReadOnlyDictionary<HexCoord, List<SandboxCellEffect>> CellEffects => cellEffects;

        public IReadOnlyList<SandboxCellEffect> EffectsAt(HexCoord cell) =>
            cellEffects.TryGetValue(cell, out List<SandboxCellEffect> list) ? list : (IReadOnlyList<SandboxCellEffect>)Array.Empty<SandboxCellEffect>();

        public bool HasEffect(HexCoord cell, SandboxEffectKind kind) => EffectsAt(cell).Any(e => e.Kind == kind);

        // Вода, на которой лёд: водная опасность и вход воды не действуют.
        private bool IsFrozen(HexCoord cell) => HasEffect(cell, SandboxEffectKind.Ice);

        // Разрешённые сочетания: смола и огонь не ложатся на воду, ток — только
        // на воду, лёд — только поверх мелководья.
        public bool CanPlaceEffect(HexCoord cell, SandboxEffectKind kind)
        {
            if (!IsInside(cell) || GetTerrain(cell) == SandboxTerrain.Impassable || GetGround(cell) == SandboxGround.Cliff)
                return false;
            bool water = (HasShallowWater(cell) || GetGround(cell) == SandboxGround.DeepWater) && !IsFrozen(cell);
            switch (kind)
            {
                case SandboxEffectKind.Tar:
                case SandboxEffectKind.Fire:
                    return !water && !IsFrozen(cell);
                case SandboxEffectKind.Electric:
                    return water && !HasBridgeAt(cell);
                case SandboxEffectKind.Ice:
                    return HasShallowWater(cell) && !HasBridgeAt(cell);
                default:
                    return true;
            }
        }

        // Добавить поверхность со сроком в раундах (0 — постоянная).
        public SandboxCellEffect AddEffect(HexCoord cell, SandboxEffectKind kind, int durationRounds, int damage = 0, string sourceId = null)
        {
            if (!CanPlaceEffect(cell, kind))
                return null;
            if (!cellEffects.TryGetValue(cell, out List<SandboxCellEffect> list))
                cellEffects[cell] = list = new List<SandboxCellEffect>();
            int round = Math.Max(1, Round);
            SandboxCellEffect effect = list.FirstOrDefault(e => e.Kind == kind && e.SourceId == (sourceId ?? string.Empty));
            if (effect == null)
                list.Add(effect = new SandboxCellEffect { Kind = kind, SourceId = sourceId ?? string.Empty });
            effect.CreatedRound = round;
            effect.ExpiresAtRound = durationRounds > 0 ? Math.Max(effect.ExpiresAtRound, round + durationRounds + 1) : 0;
            effect.Damage = Math.Max(effect.Damage, damage > 0 ? damage : SandboxHazardRules.DefaultDamage(kind));
            return effect;
        }

        public void RemoveEffect(HexCoord cell, SandboxEffectKind kind)
        {
            if (!cellEffects.TryGetValue(cell, out List<SandboxCellEffect> list))
                return;
            list.RemoveAll(e => e.Kind == kind);
            if (list.Count == 0)
                cellEffects.Remove(cell);
        }

        private void RemoveEffectsOfSource(string sourceId)
        {
            foreach (HexCoord cell in cellEffects.Keys.ToList())
            {
                cellEffects[cell].RemoveAll(e => e.SourceId == sourceId);
                if (cellEffects[cell].Count == 0)
                    cellEffects.Remove(cell);
            }
        }

        // Начало раунда: снять истёкшие поверхности (лёд — вода под ним уже есть).
        private void ProcessSurfaceRoundStart()
        {
            foreach (HexCoord cell in cellEffects.Keys.ToList())
            {
                List<SandboxCellEffect> expired = cellEffects[cell].Where(e => e.ExpiresAtRound > 0 && e.ExpiresAtRound <= Round).ToList();
                foreach (SandboxCellEffect effect in expired)
                {
                    cellEffects[cell].Remove(effect);
                    if (effect.Kind == SandboxEffectKind.Ice)
                        Log("Лёд на " + cell + " растаял — снова вода.");
                }
                if (cellEffects[cell].Count == 0)
                    cellEffects.Remove(cell);
            }
        }

        // Компоненты стоимости временных поверхностей (максимум с остальными).
        internal int EffectStepCost(SandboxUnitState unit, HexCoord cell)
        {
            int cost = 1;
            bool Has(string tag) => unit != null && unit.HasTag(tag);
            foreach (SandboxCellEffect effect in EffectsAt(cell))
            {
                switch (effect.Kind)
                {
                    case SandboxEffectKind.Tar:
                    case SandboxEffectKind.Roots:
                    case SandboxEffectKind.Thorns:
                    case SandboxEffectKind.Rime:
                        cost = Math.Max(cost, 2);
                        break;
                    case SandboxEffectKind.Web:
                        cost = Math.Max(cost, Has(SandboxTerrainTags.SpiderStep) ? 1 : 2);
                        break;
                    case SandboxEffectKind.Ice:
                        cost = Math.Max(cost, Has(SandboxTerrainTags.SureFooted) ? 1 : 2);
                        break;
                }
            }
            return cost;
        }

        // ---------- Опасность: один раз на вид за цикл активации ----------

        private HashSet<SandboxEffectKind> Cycle(SandboxUnitState unit)
        {
            if (!hazardCycle.TryGetValue(unit.Id, out HashSet<SandboxEffectKind> set))
                hazardCycle[unit.Id] = set = new HashSet<SandboxEffectKind>();
            return set;
        }

        // Начало собственной активации: новый цикл, затем поверхности под ним.
        private void ProcessHazardsAtActivation(SandboxUnitState unit)
        {
            if (unit == null || unit.IsDefeated)
                return;
            Cycle(unit).Clear();
            RememberActivationCell(unit);
            ApplyHazardsAt(unit, unit.Position);
        }

        // Вход в клетку (свой шаг или принудительное перемещение — тот же цикл).
        internal void ApplyHazardsAt(SandboxUnitState unit, HexCoord cell)
        {
            if (unit == null || unit.IsDefeated || unit.IsOffBoard)
                return;
            HashSet<SandboxEffectKind> done = Cycle(unit);
            // Сильнейший источник вида, без удвоения; порядок видов стабилен.
            foreach (IGrouping<SandboxEffectKind, SandboxCellEffect> group in EffectsAt(cell)
                         .Where(e => SandboxHazardRules.IsHazard(e.Kind)).GroupBy(e => e.Kind).OrderBy(g => g.Key))
            {
                if (done.Contains(group.Key))
                    continue;
                if (group.Key == SandboxEffectKind.Electric && (HasBridgeAt(cell) || IsFrozen(cell)))
                    continue;
                // «Не бьёт током от воды»: контактный ток наэлектризованной воды не действует.
                if (group.Key == SandboxEffectKind.Electric && HasPassive(unit, SandboxPassiveKind.ElectricWaterImmune))
                    continue;
                done.Add(group.Key);
                int damage = group.Max(e => e.Damage);
                if (damage <= 0)
                    continue;
                ApplyHit(SandboxHitKind.Environment, unit, unit, damage);
                Log(unit.DisplayLabel + ": " + SandboxHazardRules.Label(group.Key) + " — −" + damage + " HP" + (unit.IsDefeated ? ", падает." : "."));
                if (unit.IsDefeated)
                    return;
            }
        }

        // Ожидаемый урон опасностей по пути (для предпросмотра; ничего не меняет).
        public int PreviewHazardDamage(SandboxUnitState unit, IReadOnlyList<HexCoord> path)
        {
            if (unit == null || path == null)
                return 0;
            HashSet<SandboxEffectKind> done = new HashSet<SandboxEffectKind>(Cycle(unit));
            int total = 0;
            for (int i = 1; i < path.Count; i++)
            {
                foreach (IGrouping<SandboxEffectKind, SandboxCellEffect> group in EffectsAt(path[i])
                             .Where(e => SandboxHazardRules.IsHazard(e.Kind)).GroupBy(e => e.Kind))
                {
                    if (!done.Add(group.Key))
                        continue;
                    if (group.Key == SandboxEffectKind.Electric && (HasBridgeAt(path[i]) || IsFrozen(path[i])))
                        continue;
                    total += group.Max(e => e.Damage);
                }
            }
            return total;
        }

        // ---------- Взаимодействия ----------

        // Воздействие на клетки одной командой: допустимость → переходы
        // поверхностей → итоговые области. Ток — по связной воде от первой
        // клетки (по расстоянию, затем координатам), не больше предела.
        public List<string> ApplyImpulse(SandboxImpulse impulse, IEnumerable<HexCoord> cells, int durationRounds = 0, int damage = 0)
        {
            List<string> notes = new List<string>();
            int duration = durationRounds > 0 ? durationRounds : SandboxHazardRules.DefaultDurationRounds;
            List<HexCoord> targets = (cells ?? Enumerable.Empty<HexCoord>()).Where(IsInside).Distinct().OrderBy(c => c).ToList();
            if (impulse == SandboxImpulse.Electric)
            {
                if (targets.Count == 0)
                    return notes;
                List<HexCoord> region = WaterRegion(targets[0], SandboxHazardRules.ElectricMaxCells);
                foreach (HexCoord cell in region)
                    AddEffect(cell, SandboxEffectKind.Electric, 1, damage);
                notes.Add(region.Count > 0 ? "Ток идёт по воде: " + region.Count + " кл." : "Здесь нет воды — разряд уходит в землю.");
                return notes;
            }
            foreach (HexCoord cell in targets)
            {
                switch (impulse)
                {
                    case SandboxImpulse.Fire:
                        if (IsFrozen(cell))
                        {
                            RemoveEffect(cell, SandboxEffectKind.Ice);
                            notes.Add(cell + ": лёд растаял.");
                            break;
                        }
                        if (GetGround(cell) == SandboxGround.Ice)
                        {
                            SetGround(cell, SandboxGround.WetGround);
                            notes.Add(cell + ": лёд растаял в мокрую землю.");
                            break;
                        }
                        if (HasShallowWater(cell) || GetGround(cell) == SandboxGround.DeepWater)
                        {
                            notes.Add(cell + ": вода шипит паром.");
                            break;
                        }
                        bool fuel = HasEffect(cell, SandboxEffectKind.Tar) || HasEffect(cell, SandboxEffectKind.Web) ||
                                    HasEffect(cell, SandboxEffectKind.Thorns) || GetVegetation(cell) == SandboxVegetation.Brush;
                        RemoveEffect(cell, SandboxEffectKind.Tar);
                        RemoveEffect(cell, SandboxEffectKind.Web);
                        RemoveEffect(cell, SandboxEffectKind.Thorns);
                        // Выгорает только нижний горючий слой; лес и стволы стоят.
                        if (GetVegetation(cell) == SandboxVegetation.Brush)
                            SetVegetation(cell, SandboxVegetation.None);
                        AddEffect(cell, SandboxEffectKind.Fire, fuel ? Math.Max(duration, 2) : duration, damage);
                        notes.Add(cell + ": " + (fuel ? "вспыхивает" : "горит") + ".");
                        break;
                    case SandboxImpulse.Cold:
                        RemoveEffect(cell, SandboxEffectKind.Fire);
                        if (AddEffect(cell, SandboxEffectKind.Ice, duration) != null)
                            notes.Add(cell + ": вода схватилась льдом.");
                        break;
                    case SandboxImpulse.Water:
                        if (HasEffect(cell, SandboxEffectKind.Fire))
                        {
                            RemoveEffect(cell, SandboxEffectKind.Fire);
                            notes.Add(cell + ": огонь погашен.");
                        }
                        if (!HasBridgeAt(cell) && !IsGroundBlocking(cell) && GetGround(cell) != SandboxGround.Ice)
                            SetCellFlag(cell, SandboxCellState.ShallowWater, true);
                        break;
                }
            }
            return notes;
        }

        private List<HexCoord> WaterRegion(HexCoord start, int limit)
        {
            List<HexCoord> result = new List<HexCoord>();
            bool Conducts(HexCoord c) => IsInside(c) && (HasShallowWater(c) || GetGround(c) == SandboxGround.DeepWater) && !IsFrozen(c) && !HasBridgeAt(c);
            if (!Conducts(start))
                return result;
            HashSet<HexCoord> seen = new HashSet<HexCoord> { start };
            List<HexCoord> frontier = new List<HexCoord> { start };
            while (frontier.Count > 0 && result.Count < limit)
            {
                frontier = frontier.OrderBy(c => c.DistanceTo(start)).ThenBy(c => c).ToList();
                HexCoord current = frontier[0];
                frontier.RemoveAt(0);
                result.Add(current);
                foreach (HexCoord next in current.Neighbors())
                    if (seen.Add(next) && Conducts(next))
                        frontier.Add(next);
            }
            return result;
        }

        // Скольжение по льду после толчка: ещё одна клетка в том же
        // направлении, не больше одного шага, без урона столкновения.
        private string SlideOnIce(SandboxUnitState unit, HexCoord from, HexCoord landed)
        {
            bool ice = IsFrozen(landed) || GetGround(landed) == SandboxGround.Ice;
            if (!ice || unit.HasTag(SandboxTerrainTags.SureFooted) || from.DistanceTo(landed) != 1)
                return string.Empty;
            HexCoord next = SandboxHexLine.Step(landed, from.GetNeighborIndex(landed));
            if (!string.IsNullOrEmpty(ForcedMoveBlockReason(unit, next, null)))
                return " Скользит по льду, но упирается.";
            SetUnitPosition(unit, next);
            ApplyHazardsAt(unit, next);
            return " Скользит по льду → " + next + ".";
        }
    }
}
