using System;
using System.Collections.Generic;

// WM-T01 (раздел 9.9 канона, задача "gameplay-география дорог"): отдельный
// слой gameplay-местности, независимый от WorldMapTerrainType (Plains/Hills/
// Mountains), который остаётся исключительно источником стоимости для
// WorldMapNavigation.FindPath (плотность маршрута, посчитанная один раз при
// прокладке пути) и этой правкой не трогается вообще.
//
// Этот слой — про другое: живой запрос "какая местность прямо под героем
// сейчас" (WorldMapGameplayTerrainQuery), используемый в
// ContinuousSimulationSystem.AdvanceExpeditionMovement, чтобы скорость
// менялась по ходу движения, не через плотность заранее посчитанного пути.
public enum WorldMapGameplayTerrainType
{
    OpenGround,
    Road,
    Field,
    Forest,
    Water
}

[Serializable]
public sealed class WorldMapGameplayTerrainSettings
{
    public WorldMapGameplayTerrainType Terrain;
    public bool Traversable = true;
    public float MovementMultiplier = 1f;
}

// WM-T02: дорога — упорядоченный путь точек (в тех же процентных координатах
// карты 0..100, что и всё остальное) плюс ширина gameplay-зоны в тех же
// единицах (не в пикселях PNG — раздел 6 задачи). Не GameObject сцены и не
// отдельная система координат — обычные сериализуемые данные
// WorldMapDefinitionData, как регионы/зоны местности/слоты.
[Serializable]
public sealed class WorldMapRoadDefinition
{
    public string Id = string.Empty;
    public string DisplayName = string.Empty;
    public bool Enabled = true;

    // Ширина gameplay-зоны дороги в координатах карты (проценты 0..100,
    // та же ось, что и Points) — не разрешение фоновой текстуры.
    public float Width = 1f;

    public List<MapPointData> Points = new List<MapPointData>();
}

// WM-T03/T04: чистая геометрия — расстояние от точки до отрезка, без сетки и
// без обращения к WorldMapNavigation.GetTerrainAtGridCell. Приоритет: Road
// побеждает базовую местность (раздел 12 задачи) — сейчас единственный
// реализованный Area Terrain, Field/Forest пока только данные без полигонов
// (раздел 11 задачи), поэтому запрос вне дороги всегда даёт OpenGround.
public static class WorldMapGameplayTerrainQuery
{
    private static readonly List<WorldMapGameplayTerrainSettings> DefaultSettings =
        new List<WorldMapGameplayTerrainSettings>
        {
            new WorldMapGameplayTerrainSettings { Terrain = WorldMapGameplayTerrainType.OpenGround, Traversable = true, MovementMultiplier = 1.00f },
            new WorldMapGameplayTerrainSettings { Terrain = WorldMapGameplayTerrainType.Road, Traversable = true, MovementMultiplier = 1.30f },
            new WorldMapGameplayTerrainSettings { Terrain = WorldMapGameplayTerrainType.Field, Traversable = true, MovementMultiplier = 0.90f },
            new WorldMapGameplayTerrainSettings { Terrain = WorldMapGameplayTerrainType.Forest, Traversable = true, MovementMultiplier = 0.70f },
            new WorldMapGameplayTerrainSettings { Terrain = WorldMapGameplayTerrainType.Water, Traversable = false, MovementMultiplier = 1.00f }
        };

    public static WorldMapGameplayTerrainType GetTerrainTypeAtPosition(
        WorldMapDefinitionData definition,
        float xPercent,
        float yPercent)
    {
        if (definition?.Roads != null)
        {
            foreach (WorldMapRoadDefinition road in definition.Roads)
            {
                if (road != null && road.Enabled && IsInsideRoad(road, xPercent, yPercent))
                    return WorldMapGameplayTerrainType.Road;
            }
        }

        return WorldMapGameplayTerrainType.OpenGround;
    }

    public static WorldMapGameplayTerrainSettings GetSettings(
        WorldMapDefinitionData definition,
        WorldMapGameplayTerrainType terrain)
    {
        if (definition?.GameplayTerrainSettings != null)
        {
            foreach (WorldMapGameplayTerrainSettings settings in definition.GameplayTerrainSettings)
            {
                if (settings != null && settings.Terrain == terrain)
                    return settings;
            }
        }

        foreach (WorldMapGameplayTerrainSettings settings in DefaultSettings)
        {
            if (settings.Terrain == terrain)
                return settings;
        }

        return DefaultSettings[0];
    }

    // Раздел 13 задачи: единая точка входа "какой множитель скорости
    // прямо в этой точке карты сейчас" — используется в живом запросе
    // движения, не в WorldMapNavigation.FindPath.
    public static float GetMovementMultiplier(
        WorldMapDefinitionData definition,
        float xPercent,
        float yPercent)
    {
        WorldMapGameplayTerrainType terrain =
            GetTerrainTypeAtPosition(definition, xPercent, yPercent);
        return GetSettings(definition, terrain).MovementMultiplier;
    }

    public static bool IsInsideRoad(
        WorldMapRoadDefinition road,
        float xPercent,
        float yPercent)
    {
        if (road?.Points == null || road.Points.Count < 2 || road.Width <= 0f)
            return false;

        double halfWidth = road.Width * 0.5;

        for (int i = 1; i < road.Points.Count; i++)
        {
            MapPointData a = road.Points[i - 1];
            MapPointData b = road.Points[i];
            if (a == null || b == null)
                continue;

            double distance = DistancePointToSegment(
                xPercent, yPercent,
                a.XPercent, a.YPercent,
                b.XPercent, b.YPercent);

            if (distance <= halfWidth)
                return true;
        }

        return false;
    }

    // Корректно обрабатывает вертикальные/горизонтальные/диагональные
    // сегменты и сегменты нулевой длины (совпадающие точки) — раздел 10
    // задачи. Стандартная проекция точки на отрезок с зажимом параметра t
    // в [0,1], без сеточных допущений.
    public static double DistancePointToSegment(
        double px, double py,
        double ax, double ay,
        double bx, double by)
    {
        double abx = bx - ax;
        double aby = by - ay;
        double lengthSquared = abx * abx + aby * aby;

        if (lengthSquared <= double.Epsilon)
        {
            double dx0 = px - ax;
            double dy0 = py - ay;
            return Math.Sqrt(dx0 * dx0 + dy0 * dy0);
        }

        double apx = px - ax;
        double apy = py - ay;
        double t = (apx * abx + apy * aby) / lengthSquared;
        t = Math.Max(0.0, Math.Min(1.0, t));

        double closestX = ax + t * abx;
        double closestY = ay + t * aby;
        double dx = px - closestX;
        double dy = py - closestY;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
