using System;
using System.Collections.Generic;

[Serializable]
public class MapPointData
{
    public float XPercent;
    public float YPercent;

    public MapPointData(float xPercent, float yPercent)
    {
        XPercent = xPercent;
        YPercent = yPercent;
    }
}

public enum WorldMapTerrainType
{
    Plains,
    Hills,
    Mountains
}

public static class WorldMapNavigation
{
    public const int GridWidth = 104;
    public const int GridHeight = 64;
    public const int DiscoveryRadiusCells = 1;
    public const float CapitalXPercent = 50f;
    public const float CapitalYPercent = 81f;

    private static bool terrainConfigured;
    private static WorldMapTerrainType[,] terrainGrid;
    private static WorldMapDefinitionData activeDefinition;

    // AM-07.5 (канон v1.35, §9.9): постоянная география (рельеф, лес, поля,
    // река — всё) больше не порождается кодом ни в каком виде — ни
    // процедурно по WorldSeed, ни авторской геометрией, которую код
    // отрисовывает. Без подключённого авторского мира сетка — сплошная
    // Plains: безопасное пустое значение, а не скрытый откат к старому
    // генератору. Единственный официальный вход конфигурации теперь
    // ConfigureFromDefinition; ConfigureDefaultTerrain существует только
    // как явный, честный запасной вариант для кода/тестов без мира.
    public static void ConfigureDefaultTerrain()
    {
        if (terrainConfigured && activeDefinition == null && terrainGrid != null)
            return;

        activeDefinition = null;
        terrainConfigured = true;
        terrainGrid = new WorldMapTerrainType[GridWidth, GridHeight];
    }

    // AM-01/AM-07.5 (канон v1.35, §9.9): авторская постоянная география —
    // единственный источник рельефа. Переходный статический адаптер —
    // допустим до переноса всех потребителей на явный контекст кампании
    // (раздел 16 исходной инструкции по миграции), затем должен быть удалён.
    public static void ConfigureFromDefinition(WorldMapDefinitionData definition)
    {
        if (definition == null || !definition.IsValid)
        {
            ConfigureDefaultTerrain();
            return;
        }

        if (terrainConfigured && ReferenceEquals(activeDefinition, definition))
            return;

        activeDefinition = definition;
        terrainConfigured = true;
        terrainGrid = BuildAuthoredTerrain(definition);
    }

    public static bool HasActiveDefinition => activeDefinition != null;

    // Задача "gameplay-география дорог" (WM-T04): единственная точка, откуда
    // ContinuousSimulationSystem может прочитать активные Roads/настройки
    // gameplay-местности для живого запроса скорости, не проходя через
    // Unity-слой. Null, если авторский мир не подключён — запрос множителя
    // тогда безопасно даёт OpenGround/1.0 (WorldMapGameplayTerrainQuery).
    public static WorldMapDefinitionData ActiveDefinition => activeDefinition;

    // WM-T05 ("автоматический выбор быстрейшего маршрута"): единственная
    // точка входа для игры не изменилась, но теперь делегирует в
    // WorldMapRoutePlanner — тот сравнивает прямой путь с путём через
    // авторскую дорожную сеть по игровому времени и возвращает лучший,
    // уже ресэмплированный в нормальный movement route (Hills/Mountains
    // под-точки сохранены). Старая прямая логика не удалена — она живёт в
    // WorldMapRoutePlanner.BuildDirectPath (fallback без дорог и off-road
    // участки) и WorldMapRoutePlanner.BuildMovementRouteAlongPolyline
    // (ресэмплинг, идентичный прежнему циклу FindPath).
    public static List<MapPointData> FindPath(
        float startXPercent,
        float startYPercent,
        float targetXPercent,
        float targetYPercent)
    {
        EnsureTerrainConfigured();
        return WorldMapRoutePlanner.FindFastestRoute(
            startXPercent, startYPercent, targetXPercent, targetYPercent, activeDefinition);
    }

    // В существующей симуляции один сегмент маршрута занимает одну базовую
    // единицу движения. На холмах/горах FindPath добавляет 2/3 под-сегмента.
    public static int CalculateRouteCells(List<MapPointData> path, int routeIndex = 0)
    {
        if (path == null || path.Count <= 1)
            return 0;

        return Math.Max(0, path.Count - 1 - routeIndex);
    }

    // Раздел 9 задачи "автоматический выбор быстрейшего маршрута": один
    // общий helper (WorldMapRoutePlanner.DistanceCellsBetween) для метрики
    // расстояния — раньше эта формула была продублирована здесь и в FindPath.
    public static double CalculateGeometricDistanceCells(List<MapPointData> path)
    {
        if (path == null || path.Count <= 1)
            return 0.0;

        double total = 0.0;
        for (int i = 1; i < path.Count; i++)
        {
            total += WorldMapRoutePlanner.DistanceCellsBetween(
                path[i - 1].XPercent, path[i - 1].YPercent,
                path[i].XPercent, path[i].YPercent);
        }

        return total;
    }

    public static int GetTerrainTravelCost(WorldMapTerrainType terrain)
    {
        switch (terrain)
        {
            case WorldMapTerrainType.Hills:
                return 2;
            case WorldMapTerrainType.Mountains:
                return 3;
            default:
                return 1;
        }
    }

    public static float GetTerrainSpeedMultiplier(WorldMapTerrainType terrain)
    {
        switch (terrain)
        {
            case WorldMapTerrainType.Hills:
                return 0.5f;
            case WorldMapTerrainType.Mountains:
                return 1f / 3f;
            default:
                return 1f;
        }
    }

    public static WorldMapTerrainType GetTerrainAtPercent(
        float xPercent,
        float yPercent)
    {
        EnsureTerrainConfigured();
        return GetTerrainAtGridCell(
            PercentToGridX(xPercent),
            PercentToGridY(yPercent));
    }

    public static WorldMapTerrainType GetTerrainAtGridCell(int x, int y)
    {
        EnsureTerrainConfigured();
        if (!IsInside(x, y))
            return WorldMapTerrainType.Plains;
        return terrainGrid[x, y];
    }

    // Оставлены для совместимости со старым UI/тестами. Непроходимых клеток
    // больше нет: холмы и горы замедляют, но не блокируют движение.
    public static bool IsBlockedPercent(float xPercent, float yPercent) => false;
    public static bool IsBlockedGridCell(int x, int y) => false;

    public static int GridXFromPercent(float value) => PercentToGridX(value);
    public static int GridYFromPercent(float value) => PercentToGridY(value);

    public static void AdvanceRouteByCells(ExpeditionData expedition, int cells)
    {
        if (expedition == null || cells <= 0)
            return;

        if (expedition.LastTravelPoints.Count == 0)
        {
            expedition.LastTravelStartedPhase = expedition.Phase;
            expedition.LastTravelTargetLocationId = expedition.LocationId;
            expedition.LastTravelTargetXPercent = expedition.TargetMapXPercent;
            expedition.LastTravelTargetYPercent = expedition.TargetMapYPercent;
        }

        if (expedition.Route != null && expedition.Route.Count > 0)
        {
            for (int i = 0; i < cells; i++)
            {
                if (expedition.RouteIndex >= expedition.Route.Count - 1)
                    break;

                expedition.RouteIndex++;
                MapPointData position = expedition.Route[expedition.RouteIndex];
                expedition.CurrentMapXPercent = position.XPercent;
                expedition.CurrentMapYPercent = position.YPercent;
                expedition.LastTravelPoints.Add(
                    new MapPointData(position.XPercent, position.YPercent));
            }
        }

        expedition.RemainingRouteCells =
            CalculateRouteCells(expedition.Route, expedition.RouteIndex);
    }

    public static void AddRouteDelayHours(ExpeditionData expedition, double hours)
    {
        if (expedition == null || hours <= 0.0)
            return;
        expedition.RouteDelayHoursRemaining += hours;
    }

    public static bool IsWithinDiscoveryRadius(
        float firstXPercent,
        float firstYPercent,
        float secondXPercent,
        float secondYPercent)
    {
        int firstX = PercentToGridX(firstXPercent);
        int firstY = PercentToGridY(firstYPercent);
        int secondX = PercentToGridX(secondXPercent);
        int secondY = PercentToGridY(secondYPercent);

        int gridDistance = Math.Max(
            Math.Abs(firstX - secondX),
            Math.Abs(firstY - secondY));
        return gridDistance <= DiscoveryRadiusCells;
    }

    public static float ClampMapX(float value) =>
        Math.Max(2f, Math.Min(98f, value));

    public static float ClampMapY(float value) =>
        Math.Max(2f, Math.Min(96f, value));

    private static void EnsureTerrainConfigured()
    {
        if (!terrainConfigured || terrainGrid == null)
            ConfigureDefaultTerrain();
    }

    private static bool IsInside(int x, int y) =>
        x >= 0 && x < GridWidth && y >= 0 && y < GridHeight;

    private static int PercentToGridX(float value) =>
        WorldMapCoordinates.PercentToGridX(value, GridWidth);

    private static int PercentToGridY(float value) =>
        WorldMapCoordinates.PercentToGridY(value, GridHeight);

    // AM-01: рельеф из авторских прямоугольных областей вместо случайных
    // кластеров. Области применяются по возрастанию Priority — совпадающие
    // по приоритету решает порядок в списке (последняя побеждает); строгая
    // проверка конфликтов равных приоритетов — задача редактора (AM-03).
    private static WorldMapTerrainType[,] BuildAuthoredTerrain(WorldMapDefinitionData definition)
    {
        WorldMapTerrainType[,] result = new WorldMapTerrainType[GridWidth, GridHeight];

        if (definition.TerrainAreas == null || definition.TerrainAreas.Count == 0)
            return result;

        List<WorldMapTerrainAreaData> ordered =
            new List<WorldMapTerrainAreaData>(definition.TerrainAreas);
        ordered.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        for (int y = 0; y < GridHeight; y++)
        {
            float yPercent = WorldMapCoordinates.GridYToPercent(y, GridHeight);
            for (int x = 0; x < GridWidth; x++)
            {
                float xPercent = WorldMapCoordinates.GridXToPercent(x, GridWidth);
                foreach (WorldMapTerrainAreaData area in ordered)
                {
                    if (area != null && area.Contains(xPercent, yPercent))
                        result[x, y] = area.Terrain;
                }
            }
        }

        return result;
    }
}
