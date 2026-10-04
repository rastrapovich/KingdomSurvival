using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattleSandbox;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12К (канон v1.54 §28.3): геометрия исследуемого места.
    // Исследование: разметка местности места (как на глобальной карте) плюс
    // основания предметов художественной сборки — непроходимы.
    // Бой: кадр поля из Базы полей боя (16:9, ширина BattleFrameWidth) лежит
    // на рисунке места; его сетка — ровно настройки этого поля (масштаб и
    // сдвиг сетки). Клетки, центр которых попал на непроходимое, — стены боя.
    public sealed class LocalLocationGeometry
    {
        // Минимальное число клеток для отряда и противников в кадре боя.
        public const int PartyCapacity = 5;

        public LocalLocationDefinition Definition { get; }
        public BattlefieldDefinitionData Battlefield { get; }
        public WorldMapTerrainLayer Layer { get; }
        public WorldMapMovementRules Rules { get; }

        public LocalLocationGeometry(
            LocalLocationDefinition definition,
            BattlefieldDefinitionData battlefield,
            IEnumerable<Rect> blockedAreas = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Battlefield = battlefield;
            Rules = definition.MovementRules;
            Layer = definition.CreateTerrainLayer();
            if (blockedAreas != null)
            {
                foreach (Rect area in blockedAreas)
                    MarkBlocked(area);
            }
        }

        // Основание предмета (прямоугольник в пикселях рисунка) — непроходимо.
        private void MarkBlocked(Rect area)
        {
            WorldMapHexGrid grid = Layer.Grid;
            for (int index = 0; index < grid.CellCount; index++)
            {
                WorldMapHexCell cell = grid.CellAt(index);
                grid.CellCenter(cell, out double x, out double y);
                if (area.Contains(new Vector2((float)x, (float)y)))
                    Layer.Set(cell, WorldMapGameplayTerrainType.Cliffs);
            }
        }

        public float CanvasWidth => Definition.CanvasWidth;
        public float CanvasHeight => Definition.CanvasHeight;

        public bool IsPassable(double x, double y)
        {
            if (x < 0 || y < 0 || x > Layer.Grid.CanvasWidth || y > Layer.Grid.CanvasHeight)
                return false;
            return Rules.IsTraversable(Layer.GetAtPixel(x, y));
        }

        public bool IsPassable(LocalPointData point) => point != null && IsPassable(point.X, point.Y);

        public List<WorldMapPathfinder.PathPoint> FindPath(double fromX, double fromY, double toX, double toY, out bool reached)
        {
            return WorldMapPathfinder.FindPath(Layer, Rules, fromX, fromY, toX, toY, out reached);
        }

        // Длина пути по разметке (пиксели); бесконечность — не дойти.
        public double PathLength(double fromX, double fromY, double toX, double toY, out bool reached)
        {
            List<WorldMapPathfinder.PathPoint> path = FindPath(fromX, fromY, toX, toY, out reached);
            if (path.Count == 0)
                return double.PositiveInfinity;
            double length = 0;
            for (int i = 1; i < path.Count; i++)
                length += Math.Sqrt(Math.Pow(path[i].X - path[i - 1].X, 2) + Math.Pow(path[i].Y - path[i - 1].Y, 2));
            return length;
        }

        // Расстановка участников рядом с точкой (вход, точка отхода) без
        // наложения: ближайшие проходимые точки по кольцам вокруг.
        public List<LocalPointData> SpreadAround(LocalPointData center, int count, double spacing)
        {
            List<LocalPointData> result = new List<LocalPointData>();
            if (IsPassable(center))
                result.Add(new LocalPointData(center.X, center.Y));
            for (int ring = 1; ring < 12 && result.Count < count; ring++)
            {
                int steps = 6 * ring;
                for (int i = 0; i < steps && result.Count < count; i++)
                {
                    double angle = Math.PI * 2 * i / steps;
                    double x = center.X + Math.Cos(angle) * spacing * ring;
                    double y = center.Y + Math.Sin(angle) * spacing * ring;
                    if (IsPassable(x, y) && result.TrueForAll(p => p.DistanceTo(x, y) >= spacing * 0.8) &&
                        !double.IsPositiveInfinity(PathLength(center.X, center.Y, x, y, out bool reached)) && reached)
                        result.Add(new LocalPointData((float)x, (float)y));
                }
            }
            while (result.Count < count)
                result.Add(new LocalPointData(center.X, center.Y));
            return result;
        }

        // ------------------------------------------------------------------
        // Кадр боя на рисунке места
        // ------------------------------------------------------------------

        public float FrameWidth => Mathf.Max(64f, Definition.BattleFrameWidth);
        public float FrameHeight => FrameWidth / BattlefieldFrame.Aspect;

        public Rect FrameRect(Vector2 center)
        {
            return new Rect(center.x - FrameWidth / 2f, center.y - FrameHeight / 2f, FrameWidth, FrameHeight);
        }

        public BattlefieldGridLayout ArenaLayout(Vector2 center) =>
            BattlefieldFrame.ComputeLayout(FrameRect(center), BattlefieldFrame.GetGridArea(Battlefield));

        // Размер клетки боя в пикселях рисунка (одинаков для всех кадров
        // места) — от него размер фигур и в исследовании, и в бою.
        public float ArenaHexSize => ArenaLayout(Vector2.zero).Size;

        public Vector2 CellCenter(Vector2 arenaCenter, HexCoord cell) => ArenaLayout(arenaCenter).GetCenter(cell.Q, cell.R);

        // Стены боя: клетки арены, чей центр вне рисунка или на непроходимом.
        public HashSet<HexCoord> ArenaBlockedCells(Vector2 arenaCenter)
        {
            BattlefieldGridLayout layout = ArenaLayout(arenaCenter);
            HashSet<HexCoord> blocked = new HashSet<HexCoord>();
            foreach (HexCoord cell in SandboxArenaShape.Cells())
            {
                Vector2 point = layout.GetCenter(cell.Q, cell.R);
                if (!IsPassable(point.x, point.y))
                    blocked.Add(cell);
            }
            return blocked;
        }

        // Трудные клетки боя: центр на местности, замедляющей бег (осыпь,
        // кусты, топь), — то же правило, что в исследовании.
        public HashSet<HexCoord> ArenaDifficultCells(Vector2 arenaCenter)
        {
            BattlefieldGridLayout layout = ArenaLayout(arenaCenter);
            HashSet<HexCoord> difficult = new HashSet<HexCoord>();
            foreach (HexCoord cell in SandboxArenaShape.Cells())
            {
                Vector2 point = layout.GetCenter(cell.Q, cell.R);
                if (IsPassable(point.x, point.y) && Rules.RunSpeedMultiplier(Layer.GetAtPixel(point.x, point.y)) < DifficultRunMultiplier)
                    difficult.Add(cell);
            }
            return difficult;
        }

        public const double DifficultRunMultiplier = 0.9;

        // Бой на месте: кадр поля ложится на рисунок (центр — из редактора или
        // середина между отрядом и противниками), участники и противники
        // встают на ближайшие свободные клетки со своей стороны стены.
        public bool TryBuildEncounterRequest(
            GameState state,
            LocalEncounterDefinition encounter,
            IReadOnlyList<KeyValuePair<string, Vector2>> partyPoints,
            out CampaignBattleRequest request,
            out Vector2 arenaCenter,
            out string error)
        {
            request = null;
            error = string.Empty;
            List<KeyValuePair<string, Vector2>> enemies = LocalExplorationService
                .AliveEnemies(state, Definition, encounter?.Id)
                .Select(actor => new KeyValuePair<string, Vector2>(actor.ActorId, new Vector2(actor.X, actor.Y)))
                .ToList();
            List<LocalPointData> anchor = enemies.Select(item => new LocalPointData(item.Value.x, item.Value.y))
                .Concat(partyPoints.Select(item => new LocalPointData(item.Value.x, item.Value.y)))
                .ToList();
            arenaCenter = ArenaCenterFor(encounter, anchor);

            if (!TryAssignArenaCells(arenaCenter, enemies, null, out Dictionary<string, HexCoord> enemyCells) ||
                !TryAssignArenaCells(arenaCenter, partyPoints, new HashSet<HexCoord>(enemyCells.Values), out Dictionary<string, HexCoord> partyCells))
            {
                error = "В кадре боя не хватает свободных клеток.";
                return false;
            }

            request = LocalExplorationService.BuildEncounterRequest(
                state,
                Definition,
                encounter,
                partyCells.ToDictionary(item => item.Key, item => new LocalCellData(item.Value.Q, item.Value.R)),
                enemyCells.ToDictionary(item => item.Key, item => new LocalCellData(item.Value.Q, item.Value.R)),
                ArenaBlockedCells(arenaCenter).Select(cell => new LocalCellData(cell.Q, cell.R)),
                ArenaDifficultCells(arenaCenter).Select(cell => new LocalCellData(cell.Q, cell.R)));
            return true;
        }

        // Итог боя: клетки, где стояли выжившие и противники, — обратно в
        // точки рисунка (центры клеток кадра).
        public void WritePoints(CampaignBattleResult result, Vector2 arenaCenter)
        {
            if (result == null)
                return;
            BattlefieldGridLayout layout = ArenaLayout(arenaCenter);
            foreach (CampaignBattleSurvivor survivor in result.Survivors ?? new List<CampaignBattleSurvivor>())
            {
                if (survivor == null || !survivor.HasCell)
                    continue;
                Vector2 point = layout.GetCenter(survivor.CellQ, survivor.CellR);
                survivor.HasPoint = true;
                survivor.PointX = point.x;
                survivor.PointY = point.y;
            }
            foreach (CampaignBattleEnemyRecord record in result.Enemies ?? new List<CampaignBattleEnemyRecord>())
            {
                if (record == null || !record.HasCell)
                    continue;
                Vector2 point = layout.GetCenter(record.CellQ, record.CellR);
                record.HasPoint = true;
                record.PointX = point.x;
                record.PointY = point.y;
            }
        }

        // Кадр по умолчанию: середина между точками, не за краем рисунка.
        public Vector2 ClampFrameCenter(Vector2 center)
        {
            float halfW = Mathf.Min(FrameWidth / 2f, CanvasWidth / 2f);
            float halfH = Mathf.Min(FrameHeight / 2f, CanvasHeight / 2f);
            return new Vector2(Mathf.Clamp(center.x, halfW, CanvasWidth - halfW), Mathf.Clamp(center.y, halfH, CanvasHeight - halfH));
        }

        public Vector2 DefaultArenaCenter(IEnumerable<LocalPointData> points)
        {
            List<LocalPointData> list = points?.Where(p => p != null).ToList() ?? new List<LocalPointData>();
            if (list.Count == 0)
                return new Vector2(CanvasWidth / 2f, CanvasHeight / 2f);
            float minX = list.Min(p => p.X), maxX = list.Max(p => p.X);
            float minY = list.Min(p => p.Y), maxY = list.Max(p => p.Y);
            return ClampFrameCenter(new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f));
        }

        public Vector2 ArenaCenterFor(LocalEncounterDefinition encounter, IEnumerable<LocalPointData> fallbackPoints)
        {
            if (encounter != null && encounter.HasArenaCenter && encounter.ArenaCenter != null)
                return new Vector2(encounter.ArenaCenter.X, encounter.ArenaCenter.Y);
            return DefaultArenaCenter(fallbackPoints);
        }

        // Клетка арены под точкой рисунка (ближайшая клетка арены).
        public bool TryArenaCell(Vector2 arenaCenter, Vector2 point, out HexCoord cell)
        {
            cell = default;
            BattlefieldGridLayout layout = ArenaLayout(arenaCenter);
            HexCoord best = default;
            float bestDistance = float.MaxValue;
            foreach (HexCoord candidate in SandboxArenaShape.Cells())
            {
                float distance = (layout.GetCenter(candidate.Q, candidate.R) - point).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            cell = best;
            return bestDistance < float.MaxValue;
        }

        // Расстановка к бою: каждому — уникальная ближайшая свободная клетка
        // арены по проходимой области кадра (со своей стороны стены).
        public bool TryAssignArenaCells(
            Vector2 arenaCenter,
            IReadOnlyList<KeyValuePair<string, Vector2>> preferred,
            ISet<HexCoord> reserved,
            out Dictionary<string, HexCoord> assigned)
        {
            HashSet<HexCoord> blocked = ArenaBlockedCells(arenaCenter);
            bool Passable(HexCoord cell) => SandboxArenaShape.Contains(cell) && !blocked.Contains(cell);
            List<KeyValuePair<string, HexCoord>> start = new List<KeyValuePair<string, HexCoord>>();
            foreach (KeyValuePair<string, Vector2> entry in preferred)
            {
                TryArenaCell(arenaCenter, entry.Value, out HexCoord cell);
                if (!Passable(cell))
                    cell = NearestPassable(cell, Passable);
                start.Add(new KeyValuePair<string, HexCoord>(entry.Key, cell));
            }
            return SandboxLocalNavigation.TryAssignCells(start, Passable, reserved, out assigned);
        }

        private static HexCoord NearestPassable(HexCoord from, Func<HexCoord, bool> passable)
        {
            return SandboxArenaShape.Cells().Where(passable).OrderBy(cell => cell.DistanceTo(from)).ThenBy(cell => cell).FirstOrDefault();
        }
    }

    // ПР-12К: проверка конфигурации исследуемого места до того, как партия
    // окажется в неверном состоянии. Пустой список — место годится.
    public static class LocalLocationValidator
    {
        public static List<string> Validate(
            LocalLocationDefinition definition,
            BattlefieldDatabaseAsset battlefields,
            Func<string, bool> dialogueExists = null,
            Func<string, bool> creatureExists = null,
            IEnumerable<Rect> blockedAreas = null)
        {
            List<string> errors = new List<string>();
            if (definition == null)
            {
                errors.Add("Место не задано.");
                return errors;
            }
            string prefix = string.IsNullOrWhiteSpace(definition.Id) ? "Место без ID" : definition.Id;
            if (string.IsNullOrWhiteSpace(definition.Id))
                errors.Add(prefix + ": пустой ID.");
            if (string.IsNullOrWhiteSpace(definition.WorldLocationId))
                errors.Add(prefix + ": не задано место глобальной карты.");
            if (definition.CanvasWidth < 64 || definition.CanvasHeight < 64)
                errors.Add(prefix + ": размер рисунка места слишком мал.");

            BattlefieldDefinitionData field = battlefields != null ? battlefields.FindById(definition.BattlefieldId) : null;
            if (field == null)
            {
                errors.Add(prefix + ": нет поля '" + definition.BattlefieldId + "' в Базе полей боя.");
                return errors;
            }

            LocalLocationGeometry geometry = new LocalLocationGeometry(definition, field, blockedAreas);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            void CheckId(string kind, string id)
            {
                if (string.IsNullOrWhiteSpace(id))
                    errors.Add(prefix + ": " + kind + " без ID.");
                else if (!ids.Add(id))
                    errors.Add(prefix + ": повторный ID '" + id + "'.");
            }

            if (definition.Entrances.Count == 0)
                errors.Add(prefix + ": нет входа.");
            LocalPointData start = null;
            foreach (LocalEntranceDefinition entrance in definition.Entrances)
            {
                CheckId("вход", entrance?.Id);
                if (entrance == null)
                    continue;
                if (!geometry.IsPassable(entrance.Point))
                    errors.Add(prefix + ": вход '" + entrance.Id + "' стоит на непроходимом " + entrance.Point + ".");
                else if (start == null)
                    start = entrance.Point;
            }
            if (start == null)
                return errors;

            bool Reachable(LocalPointData point, double radius)
            {
                double length = geometry.PathLength(start.X, start.Y, point.X, point.Y, out bool reached);
                if (double.IsPositiveInfinity(length))
                    return false;
                if (reached)
                    return true;
                List<WorldMapPathfinder.PathPoint> path = geometry.FindPath(start.X, start.Y, point.X, point.Y, out _);
                WorldMapPathfinder.PathPoint end = path[path.Count - 1];
                return point.DistanceTo(end.X, end.Y) <= radius;
            }

            foreach (LocalObjectDefinition item in definition.Objects)
            {
                CheckId("объект", item?.Id);
                if (item == null)
                    continue;
                if (item.Point.X < 0 || item.Point.Y < 0 || item.Point.X > definition.CanvasWidth || item.Point.Y > definition.CanvasHeight)
                    errors.Add(prefix + ": объект '" + item.Id + "' за краем рисунка.");
                else if (!Reachable(item.Point, item.InteractRadius))
                    errors.Add(prefix + ": к объекту '" + item.Id + "' нельзя подойти от входа.");
                if (item.Kind == LocalObjectKind.Dialogue)
                {
                    if (string.IsNullOrWhiteSpace(item.DialogueId))
                        errors.Add(prefix + ": у объекта '" + item.Id + "' нет диалога.");
                    else if (dialogueExists != null && !dialogueExists(item.DialogueId))
                        errors.Add(prefix + ": диалога '" + item.DialogueId + "' нет в Базе диалогов.");
                }
                else if (string.IsNullOrWhiteSpace(item.Text))
                {
                    errors.Add(prefix + ": у объекта '" + item.Id + "' нет текста.");
                }
            }

            foreach (LocalEnemyDefinition enemy in definition.Enemies)
            {
                CheckId("противник", enemy?.InstanceId);
                if (enemy == null)
                    continue;
                if (!geometry.IsPassable(enemy.Point) || !Reachable(enemy.Point, 1))
                    errors.Add(prefix + ": противник '" + enemy.InstanceId + "' стоит на непроходимом или отрезан " + enemy.Point + ".");
                if (creatureExists != null && !creatureExists(enemy.UnitTypeId))
                    errors.Add(prefix + ": нет существа '" + enemy.UnitTypeId + "' в Базе существ.");
                if (definition.FindEncounter(enemy.EncounterId) == null)
                    errors.Add(prefix + ": противник '" + enemy.InstanceId + "' не входит ни в одно столкновение.");
            }

            foreach (LocalEncounterDefinition encounter in definition.Encounters)
            {
                CheckId("столкновение", encounter?.Id);
                if (encounter == null)
                    continue;
                if (string.IsNullOrWhiteSpace(encounter.BattleIdPrefix))
                    errors.Add(prefix + ": у столкновения '" + encounter.Id + "' нет префикса ID боя.");
                LocalAreaData area = encounter.TriggerArea;
                if (area == null || area.Width <= 0 || area.Height <= 0)
                    errors.Add(prefix + ": у столкновения '" + encounter.Id + "' нет зоны угрозы.");
                else if (!Reachable(new LocalPointData(area.X + area.Width / 2, area.Y + area.Height / 2), Math.Max(area.Width, area.Height) / 2))
                    errors.Add(prefix + ": в зону угрозы '" + encounter.Id + "' не пройти от входа.");
                if (!string.IsNullOrWhiteSpace(encounter.IntroDialogueId) && dialogueExists != null &&
                    !dialogueExists(encounter.IntroDialogueId))
                    errors.Add(prefix + ": диалога '" + encounter.IntroDialogueId + "' нет в Базе диалогов.");
                if (encounter.AllowRetreat)
                {
                    LocalPointData retreat = encounter.RetreatPoint;
                    if (!geometry.IsPassable(retreat) || !Reachable(retreat, 1))
                        errors.Add(prefix + ": безопасная точка отхода " + retreat + " недоступна.");
                    else if (area != null && area.Contains(retreat.X, retreat.Y))
                        errors.Add(prefix + ": безопасная точка отхода лежит в зоне угрозы — бой зациклится.");
                }

                List<LocalEnemyDefinition> enemies = definition.Enemies.Where(enemy => enemy != null && enemy.EncounterId == encounter.Id).ToList();
                if (enemies.Count == 0)
                {
                    errors.Add(prefix + ": в столкновении '" + encounter.Id + "' нет противников.");
                    continue;
                }
                List<LocalPointData> anchor = enemies.Select(enemy => enemy.Point).ToList();
                if (area != null)
                    anchor.Add(new LocalPointData(area.X + area.Width / 2, area.Y + area.Height / 2));
                Vector2 center = geometry.ArenaCenterFor(encounter, anchor);
                Rect frame = geometry.FrameRect(center);
                foreach (LocalEnemyDefinition enemy in enemies)
                {
                    if (!frame.Contains(new Vector2(enemy.Point.X, enemy.Point.Y)))
                        errors.Add(prefix + ": противник '" + enemy.InstanceId + "' вне кадра боя '" + encounter.Id + "'.");
                }
                int free = SandboxArenaShape.CellCount - geometry.ArenaBlockedCells(center).Count;
                if (free < LocalLocationGeometry.PartyCapacity + enemies.Count)
                    errors.Add(prefix + ": в кадре боя '" + encounter.Id + "' мало свободных клеток (" + free + ").");
            }

            return errors;
        }
    }
}
