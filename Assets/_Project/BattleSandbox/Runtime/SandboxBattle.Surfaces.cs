using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф «Поверхности и окружение боевой арены», этап 1: основание и
    // растительность клетки поверх прежней высоты. Один владелец состояния —
    // бой; путь, предпросмотр, исполнение, ИИ и подсказка читают эти правила.
    //   • стоимость входа — максимум компонентов (лесной холм = 2, не 4);
    //   • пассивная особенность снимает только свой компонент;
    //   • непроходимость (обрыв, глубокая вода без «Пловца») важнее любой цены;
    //   • укрытие леса/зарослей — только от дальнего снаряда с расстояния > 1,
    //     вместе с холмом не больше предела; топь — −1 после бонусов.
    public sealed partial class SandboxBattle
    {
        private readonly Dictionary<HexCoord, SandboxGround> ground = new Dictionary<HexCoord, SandboxGround>();
        private readonly Dictionary<HexCoord, SandboxVegetation> vegetation = new Dictionary<HexCoord, SandboxVegetation>();

        public SandboxGround GetGround(HexCoord cell) =>
            ground.TryGetValue(cell, out SandboxGround value) ? value : SandboxGround.Default;

        public SandboxVegetation GetVegetation(HexCoord cell) =>
            vegetation.TryGetValue(cell, out SandboxVegetation value) ? value : SandboxVegetation.None;

        public IReadOnlyDictionary<HexCoord, SandboxGround> GroundCells => ground;
        public IReadOnlyDictionary<HexCoord, SandboxVegetation> VegetationCells => vegetation;

        public void SetGround(HexCoord cell, SandboxGround value)
        {
            if (value == SandboxGround.Default)
                ground.Remove(cell);
            else
                ground[cell] = value;
        }

        public void SetVegetation(HexCoord cell, SandboxVegetation value)
        {
            if (value == SandboxVegetation.None)
                vegetation.Remove(cell);
            else
                vegetation[cell] = value;
        }

        // Основание, по которому обычная наземная пехота не ходит.
        public bool IsGroundBlocking(HexCoord cell)
        {
            SandboxGround value = GetGround(cell);
            if (value == SandboxGround.DeepWater && HasBridgeAt(cell))
                return false;
            return value == SandboxGround.Cliff || value == SandboxGround.DeepWater;
        }

        // Можно ли встать на клетку этому бойцу: глубокая вода — для «Пловца».
        public bool IsEnterableFor(SandboxUnitState unit, HexCoord cell)
        {
            if (IsEnterable(cell))
                return true;
            return unit != null && unit.HasTag(SandboxTerrainTags.Swimmer) && GetGround(cell) == SandboxGround.DeepWater &&
                   IsInside(cell) && GetTerrain(cell) != SandboxTerrain.Impassable &&
                   !HasCellState(cell, SandboxCellState.Pit) && FindEggAt(cell) == null;
        }

        // Мелкая вода на клетке — основанием арены или состоянием способности.
        public bool HasShallowWater(HexCoord cell) =>
            GetGround(cell) == SandboxGround.ShallowWater || HasCellState(cell, SandboxCellState.ShallowWater);

        // Цена входа для конкретного бойца: максимум компонентов, пассивные
        // особенности снимают только свой компонент. unit == null — без
        // особенностей (прежний общий расчёт).
        public int GetStepCost(SandboxUnitState unit, HexCoord cell)
        {
            SandboxSurfaceRules r = SandboxSurfaceRules.Current;
            int cost = 1;
            void Take(int value) => cost = Math.Max(cost, r.SafeCost(value));
            bool Has(string tag) => unit != null && unit.HasTag(tag);

            if (GetTerrain(cell) == SandboxTerrain.Difficult)
                Take(Has(SandboxTerrainTags.Highlander) ? 1 : r.HillCost);
            // Целый мост перекрывает основание под собой (вода, топь, грязь).
            bool bridge = HasBridgeAt(cell);
            SandboxBattleObject placed = TerrainObjectAt(cell);
            if (placed != null && placed.Kind == SandboxObjectKind.FallenLog)
                Take(SandboxTerrainObjects.Get(placed.Kind).StepCost);
            switch (bridge ? SandboxGround.Default : GetGround(cell))
            {
                case SandboxGround.Mud: Take(Has(SandboxTerrainTags.SwampWalker) ? 1 : r.MudCost); break;
                case SandboxGround.Bog: Take(Has(SandboxTerrainTags.SwampWalker) ? 1 : r.BogCost); break;
                case SandboxGround.ShallowWater: Take(Has(SandboxTerrainTags.WaterWalker) ? 1 : r.ShallowWaterCost); break;
                case SandboxGround.DeepWater: Take(r.ShallowWaterCost); break;
                case SandboxGround.Rubble: Take(r.RubbleCost); break;
                case SandboxGround.DeepSnow: Take(Has(SandboxTerrainTags.Snowshoe) ? 1 : r.DeepSnowCost); break;
                case SandboxGround.Ice: Take(Has(SandboxTerrainTags.SureFooted) ? 1 : r.IceCost); break;
                case SandboxGround.Sand: Take(r.SandCost); break;
                case SandboxGround.WetGround: Take(r.WetGroundCost); break;
                case SandboxGround.Road: Take(r.RoadCost); break;
            }
            SandboxCellState state = GetCellState(cell);
            if ((state & SandboxCellState.ShallowWater) != 0 && !bridge)
                Take(Has(SandboxTerrainTags.WaterWalker) ? 1 : 2);
            if ((state & (SandboxCellState.LooseEarth | SandboxCellState.Subsided)) != 0)
                Take(2);
            switch (GetVegetation(cell))
            {
                case SandboxVegetation.Forest: Take(Has(SandboxTerrainTags.ForestWalker) ? 1 : r.ForestCost); break;
                case SandboxVegetation.Brush: Take(Has(SandboxTerrainTags.ForestWalker) ? 1 : r.BrushCost); break;
            }
            return cost;
        }

        // Укрытие цели от обычного дальнего снаряда: лес/заросли (наибольшее,
        // не сумма); вместе с бонусом холма — не выше общего предела.
        public int GetCoverBonus(SandboxUnitState target, HexCoord attackPosition, bool projectile)
        {
            if (target == null || !projectile || attackPosition.DistanceTo(target.Position) <= 1)
                return 0;
            SandboxSurfaceRules r = SandboxSurfaceRules.Current;
            int cover = 0;
            switch (GetVegetation(target.Position))
            {
                case SandboxVegetation.Forest: cover = Math.Max(cover, r.ForestCover); break;
                case SandboxVegetation.Brush: cover = Math.Max(cover, r.BrushCover); break;
            }
            // Низкий объект на линии выстрела (плетень, стенка, завал) — мягкое
            // укрытие; с лесом и зарослями не складывается.
            if (GetLineOfFire(attackPosition, target.Position).LowCover)
                cover = Math.Max(cover, r.LowObjectCover);
            int hill = SandboxTerrainRules.GetDefenseBonus(target);
            return Math.Max(0, Math.Min(cover, r.TerrainDefenseCap - hill));
        }

        // Топь: −1 Защиты, пока боец в ней (снимает «Болотный ходок»).
        public int GetSurfaceDefensePenalty(SandboxUnitState unit)
        {
            if (unit == null || unit.IsOffBoard || GetGround(unit.Position) != SandboxGround.Bog || HasBridgeAt(unit.Position) ||
                unit.HasTag(SandboxTerrainTags.SwampWalker))
                return 0;
            return Math.Max(0, SandboxSurfaceRules.Current.BogDefensePenalty);
        }

        // Подсказка клетки для выбранного бойца: основание, высота, вход,
        // бонусы и штрафы, почему нельзя пройти.
        public string DescribeCell(SandboxUnitState unit, HexCoord cell)
        {
            if (!IsInside(cell))
                return "Вне поля.";
            SandboxSurfaceRules r = SandboxSurfaceRules.Current;
            SandboxGround g = GetGround(cell);
            SandboxVegetation v = GetVegetation(cell);
            bool hill = GetTerrain(cell) == SandboxTerrain.Difficult;
            string name = (v == SandboxVegetation.Forest ? "Лесной " : v == SandboxVegetation.Brush ? "Заросший " : string.Empty);
            string baseName = hill ? "холм" : SandboxSurfaceLabels.Ground(g).ToLowerInvariant();
            name = name.Length > 0 ? name + baseName : char.ToUpperInvariant(baseName[0]) + baseName.Substring(1);
            if (hill && g != SandboxGround.Default && g != SandboxGround.Ground)
                name += " (" + SandboxSurfaceLabels.Ground(g).ToLowerInvariant() + ")";

            List<string> parts = new List<string>();
            if (GetTerrain(cell) == SandboxTerrain.Impassable)
                return name + ": непроходимо.";
            if (!IsEnterableFor(unit, cell))
                return name + ": " + (g == SandboxGround.DeepWater ? "глубоко — нужен «Пловец»" : g == SandboxGround.Cliff ? "обрыв, не пройти" : "не пройти") + ".";
            parts.Add("вход " + GetStepCost(unit, cell));
            if (hill)
            {
                parts.Add("+" + r.HillDefense + " защиты от высоты");
                if (unit != null && unit.HasTag(SandboxCombatTagRules.Ranged))
                    parts.Add("дальность стрелка +" + SandboxTerrainRules.HillRangedAttackRangeBonus);
            }
            if (v != SandboxVegetation.None)
            {
                int cover = v == SandboxVegetation.Forest ? r.ForestCover : r.BrushCover;
                parts.Add((hill ? "ещё +" : "+") + Math.Min(cover, Math.Max(0, r.TerrainDefenseCap - (hill ? r.HillDefense : 0))) + " от дальних снарядов");
            }
            if (g == SandboxGround.Bog)
                parts.Add(unit != null && unit.HasTag(SandboxTerrainTags.SwampWalker)
                    ? "штраф защиты снят — " + SandboxSurfaceLabels.Tag(SandboxTerrainTags.SwampWalker)
                    : "−" + r.BogDefensePenalty + " защиты в топи");
            List<string> exempt = ExemptionsAt(unit, cell, hill, g, v);
            if (exempt.Count > 0)
                parts.Add("облегчает: " + string.Join(", ", exempt));
            return name + ": " + string.Join("; ", parts) + ".";
        }

        private static List<string> ExemptionsAt(SandboxUnitState unit, HexCoord cell, bool hill, SandboxGround g, SandboxVegetation v)
        {
            List<string> result = new List<string>();
            if (unit == null)
                return result;
            void Check(bool applies, string tag)
            {
                if (applies && unit.HasTag(tag))
                    result.Add(SandboxSurfaceLabels.Tag(tag));
            }
            Check(hill, SandboxTerrainTags.Highlander);
            Check(v != SandboxVegetation.None, SandboxTerrainTags.ForestWalker);
            Check(g == SandboxGround.Mud || g == SandboxGround.Bog, SandboxTerrainTags.SwampWalker);
            Check(g == SandboxGround.DeepSnow, SandboxTerrainTags.Snowshoe);
            Check(g == SandboxGround.Ice, SandboxTerrainTags.SureFooted);
            Check(g == SandboxGround.ShallowWater, SandboxTerrainTags.WaterWalker);
            return result.Distinct().ToList();
        }

        // Основание мелкой воды арены — то же водное состояние, что у
        // способностей (разлив, Речной узел видят воду одинаково).
        private void SyncGroundWater()
        {
            foreach (KeyValuePair<HexCoord, SandboxGround> pair in ground)
                if (pair.Value == SandboxGround.ShallowWater && IsInside(pair.Key))
                    SetCellFlag(pair.Key, SandboxCellState.ShallowWater, true);
        }
    }
}
