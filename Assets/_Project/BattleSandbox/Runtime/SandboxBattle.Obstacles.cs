using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф «Поверхности и окружение боевой арены», этап 2: объекты местности
    // на общей модели объектов боя (те же ID, HP, разрушение и журнал, что у
    // лозы и нити) и общая линия огня для обычных выстрелов, способностей,
    // предпросмотра и ИИ.
    //   • высокий объект (валун, ствол, обрядовый камень) и обрыв закрывают
    //     прямой выстрел через промежуточную клетку;
    //   • низкий (плетень, стенка, завал) даёт мягкое укрытие цели, если
    //     линия проходит через него; укрытия не складываются;
    //   • глубокая вода мешает идти, но не стреле;
    //   • прежняя безымянная непроходимая клетка закрывает линию, как раньше.
    public enum SandboxObstacleHeight
    {
        None,
        Low,
        High
    }

    public readonly struct SandboxTerrainObjectInfo
    {
        public readonly string Label;
        public readonly SandboxObstacleHeight Height;
        public readonly bool BlocksMovement;
        public readonly int StepCost;
        public readonly int HitPoints;
        public readonly bool Indestructible;

        public SandboxTerrainObjectInfo(string label, SandboxObstacleHeight height, bool blocks, int stepCost, int hitPoints, bool indestructible)
        {
            Label = label;
            Height = height;
            BlocksMovement = blocks;
            StepCost = stepCost;
            HitPoints = hitPoints;
            Indestructible = indestructible;
        }
    }

    // Свойства объектов местности. Числа — стартовые настройки прототипа.
    public static class SandboxTerrainObjects
    {
        public static int WattleHitPoints = 8;
        public static int RootNestHitPoints = 10;

        public static SandboxTerrainObjectInfo Get(SandboxObjectKind kind)
        {
            switch (kind)
            {
                case SandboxObjectKind.Boulder: return new SandboxTerrainObjectInfo("Валун", SandboxObstacleHeight.High, true, 0, 1, true);
                case SandboxObjectKind.Trunk: return new SandboxTerrainObjectInfo("Толстый ствол", SandboxObstacleHeight.High, true, 0, 1, true);
                case SandboxObjectKind.FallenLog: return new SandboxTerrainObjectInfo("Поваленное дерево", SandboxObstacleHeight.None, false, 2, 1, true);
                case SandboxObjectKind.Deadfall: return new SandboxTerrainObjectInfo("Завал", SandboxObstacleHeight.Low, true, 0, 1, true);
                case SandboxObjectKind.Wattle: return new SandboxTerrainObjectInfo("Плетень", SandboxObstacleHeight.Low, true, 0, WattleHitPoints, false);
                case SandboxObjectKind.StoneWall: return new SandboxTerrainObjectInfo("Каменная стенка", SandboxObstacleHeight.Low, true, 0, 1, true);
                case SandboxObjectKind.Bridge: return new SandboxTerrainObjectInfo("Мост", SandboxObstacleHeight.None, false, 1, 1, true);
                case SandboxObjectKind.RootNest: return new SandboxTerrainObjectInfo("Корневое гнездо", SandboxObstacleHeight.Low, true, 0, RootNestHitPoints, false);
                case SandboxObjectKind.RitualStone: return new SandboxTerrainObjectInfo("Обрядовый камень", SandboxObstacleHeight.High, true, 0, 1, true);
                default: return new SandboxTerrainObjectInfo(kind.ToString(), SandboxObstacleHeight.None, false, 0, 1, false);
            }
        }
    }

    // Итог проверки линии огня: свободна ли, чем закрыта (первое препятствие
    // от стрелка), даёт ли низкий объект укрытие цели.
    public readonly struct SandboxLineOfFire
    {
        public readonly bool Clear;
        public readonly bool HasBlocker;
        public readonly HexCoord Blocker;
        public readonly string Reason;
        public readonly bool LowCover;

        public SandboxLineOfFire(bool clear, bool hasBlocker, HexCoord blocker, string reason, bool lowCover)
        {
            Clear = clear;
            HasBlocker = hasBlocker;
            Blocker = blocker;
            Reason = reason ?? string.Empty;
            LowCover = lowCover;
        }
    }

    public sealed partial class SandboxBattle
    {
        // Объект местности: занимает клетки; HP — только у разрушаемых.
        public SandboxBattleObject PlaceTerrainObject(SandboxObjectKind kind, IEnumerable<HexCoord> cells, int hitPoints = 0)
        {
            SandboxTerrainObjectInfo info = SandboxTerrainObjects.Get(kind);
            SandboxBattleObject item = CreateObject(kind, null, hitPoints > 0 ? hitPoints : info.HitPoints);
            item.Indestructible = info.Indestructible;
            if (cells != null)
                item.Cells.AddRange(cells.Where(IsInside).Distinct());
            return item;
        }

        public IEnumerable<SandboxBattleObject> TerrainObjects =>
            objects.Where(item => item.IsTerrain && !item.IsDestroyed);

        public SandboxBattleObject TerrainObjectAt(HexCoord cell) =>
            objects.FirstOrDefault(item => item.IsTerrain && !item.IsDestroyed && item.Cells.Contains(cell));

        public bool HasBridgeAt(HexCoord cell)
        {
            SandboxBattleObject item = TerrainObjectAt(cell);
            return item != null && item.Kind == SandboxObjectKind.Bridge;
        }

        private bool BlockedByTerrainObject(HexCoord cell)
        {
            SandboxBattleObject item = TerrainObjectAt(cell);
            return item != null && SandboxTerrainObjects.Get(item.Kind).BlocksMovement;
        }

        // Линия — от «меньшего» конца к «большему»: результат не зависит от
        // того, кто стреляет, на границе гексов — детерминированно.
        public static List<HexCoord> SymmetricLine(HexCoord a, HexCoord b)
        {
            bool swap = b.CompareTo(a) < 0;
            List<HexCoord> line = SandboxHexLine.Line(swap ? b : a, swap ? a : b);
            if (swap)
                line.Reverse();
            return line;
        }

        public SandboxLineOfFire GetLineOfFire(HexCoord from, HexCoord to)
        {
            List<HexCoord> line = SymmetricLine(from, to);
            bool lowCover = false;
            for (int i = 1; i < line.Count - 1; i++)
            {
                HexCoord cell = line[i];
                if (!IsInside(cell))
                    return new SandboxLineOfFire(false, true, cell, "за краем поля", false);
                if (GetTerrain(cell) == SandboxTerrain.Impassable)
                    return new SandboxLineOfFire(false, true, cell, "закрыто препятствием", false);
                if (GetGround(cell) == SandboxGround.Cliff)
                    return new SandboxLineOfFire(false, true, cell, "закрыто обрывом", false);
                SandboxBattleObject item = TerrainObjectAt(cell);
                if (item != null)
                {
                    SandboxObstacleHeight height = SandboxTerrainObjects.Get(item.Kind).Height;
                    if (height == SandboxObstacleHeight.High)
                        return new SandboxLineOfFire(false, true, cell, "закрывает " + item.Label.ToLowerInvariant(), false);
                    if (height == SandboxObstacleHeight.Low)
                        lowCover = true;
                }
                if (BlocksLineBySurface(cell, from, to))
                    return new SandboxLineOfFire(false, true, cell, "в дыму не видно цели", false);
            }
            // Дым в клетке стрелка или цели закрывает дальний выстрел.
            if (from.DistanceTo(to) > 1 && (BlocksLineBySurface(from, from, to) || BlocksLineBySurface(to, from, to)))
                return new SandboxLineOfFire(false, true, BlocksLineBySurface(to, from, to) ? to : from, "в дыму не видно цели", false);
            return new SandboxLineOfFire(true, false, default, string.Empty, lowCover);
        }

        // Временные области, закрывающие дальнюю линию (дым) — этап 3.
        private bool BlocksLineBySurface(HexCoord cell, HexCoord from, HexCoord to) =>
            from.DistanceTo(to) > 1 && HasEffect(cell, SandboxEffectKind.Smoke);

        // Дальний выстрел из позиции: соседняя цель — без проверки линии.
        public bool CanShootFrom(HexCoord from, HexCoord to) =>
            from.DistanceTo(to) <= 1 || GetLineOfFire(from, to).Clear;
    }
}
