using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф «Поверхности и окружение боевой арены», этап 4: отладочные
    // команды полигона (поставить основание/объект, очистить временное).
    // Это режим проверки, а не способность героя: в бою кампании не
    // вызываются. Пути, атака и ИИ читают изменённую местность сразу.
    public sealed partial class SandboxBattle
    {
        public void DebugSetGround(HexCoord cell, SandboxGround value)
        {
            if (!IsInside(cell))
                return;
            SetGround(cell, value);
            SetCellFlag(cell, SandboxCellState.ShallowWater, value == SandboxGround.ShallowWater);
        }

        public void DebugSetHill(HexCoord cell, bool hill)
        {
            if (!IsInside(cell) || GetTerrain(cell) == SandboxTerrain.Impassable)
                return;
            if (hill)
                terrain[cell] = SandboxTerrain.Difficult;
            else
                terrain.Remove(cell);
            // Защита от высоты читает снимок местности — обновить его.
            SandboxTerrainRules.RegisterBattle(units, terrain);
            SandboxTerrainRules.AttachBattle(units, this);
        }

        // Объект местности на свободную от фигур клетку (прежний объект
        // клетки убирается).
        public bool DebugPlaceObject(HexCoord cell, SandboxObjectKind kind)
        {
            if (!IsInside(cell))
                return false;
            if (SandboxTerrainObjects.Get(kind).BlocksMovement && GetUnitAt(cell) != null)
                return false;
            DebugRemoveObject(cell);
            PlaceTerrainObject(kind, new[] { cell });
            return true;
        }

        public bool DebugRemoveObject(HexCoord cell)
        {
            SandboxBattleObject item = TerrainObjectAt(cell);
            if (item == null)
                return false;
            item.Cells.Remove(cell);
            if (item.Cells.Count == 0)
            {
                RemoveEffectsOfSource(item.Id);
                objects.Remove(item);
            }
            return true;
        }

        // Снять все временные области (огонь, смолу, лёд…); основания и
        // объекты остаются.
        public int DebugClearEffects()
        {
            int count = cellEffects.Values.Sum(list => list.Count);
            cellEffects.Clear();
            return count;
        }

        // Логическая категория клетки для показа в полигоне.
        public string DebugCellCategory(HexCoord cell)
        {
            if (!IsInside(cell) || GetTerrain(cell) == SandboxTerrain.Impassable || IsGroundBlocking(cell) || BlockedByTerrainObject(cell))
                return "closed";
            if (EffectsAt(cell).Any(e => SandboxHazardRules.IsHazard(e.Kind)))
                return "hazard";
            if (GetStepCost(null, cell) > 1 || GetVegetation(cell) != SandboxVegetation.None || GetTerrain(cell) == SandboxTerrain.Difficult)
                return "special";
            return "free";
        }

        public IEnumerable<HexCoord> DebugBoardCells()
        {
            for (int r = 0; r < Height; r++)
                for (int q = 0; q < Width; q++)
                {
                    HexCoord cell = new HexCoord(q, r);
                    if (IsInside(cell))
                        yield return cell;
                }
        }
    }
}
