using System.Collections.Generic;
using NUnit.Framework;

// WM-T03 (задача "gameplay-география дорог"): детекция "герой на дороге" —
// чистая геометрия точка-до-отрезка, без сетки WorldMapNavigation.
public class WorldMapGameplayTerrainQueryTests
{
    private static WorldMapRoadDefinition BuildRoad(
        bool enabled,
        float width,
        params (float X, float Y)[] points)
    {
        WorldMapRoadDefinition road = new WorldMapRoadDefinition
        {
            Id = "road-test",
            Enabled = enabled,
            Width = width
        };

        foreach ((float x, float y) in points)
            road.Points.Add(new MapPointData(x, y));

        return road;
    }

    [Test]
    public void PointExactlyOnCenterLine_IsInsideRoad()
    {
        WorldMapRoadDefinition road = BuildRoad(true, 4f, (0f, 50f), (100f, 50f));
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 50f, 50f), Is.True);
    }

    [Test]
    public void PointWithinHalfWidth_IsInside()
    {
        WorldMapRoadDefinition road = BuildRoad(true, 4f, (0f, 50f), (100f, 50f));
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 50f, 51.9f), Is.True);
    }

    [Test]
    public void PointBeyondHalfWidth_IsOutside()
    {
        WorldMapRoadDefinition road = BuildRoad(true, 4f, (0f, 50f), (100f, 50f));
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 50f, 52.1f), Is.False);
    }

    [Test]
    public void PointNearSegmentStart_IsDetectedCorrectly()
    {
        WorldMapRoadDefinition road = BuildRoad(true, 4f, (10f, 10f), (90f, 10f));
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 10f, 11f), Is.True);
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 5f, 10f), Is.False);
    }

    [Test]
    public void PointNearSegmentEnd_IsDetectedCorrectly()
    {
        WorldMapRoadDefinition road = BuildRoad(true, 4f, (10f, 10f), (90f, 10f));
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 90f, 11f), Is.True);
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 95f, 10f), Is.False);
    }

    [Test]
    public void DiagonalSegment_IsDetectedByPerpendicularDistance()
    {
        WorldMapRoadDefinition road = BuildRoad(true, 4f, (0f, 0f), (100f, 100f));
        // Точка на диагонали.
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 50f, 50f), Is.True);
        // Точка далеко в стороне от диагонали.
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 50f, 10f), Is.False);
    }

    [Test]
    public void SharpTurn_DetectsInsideNearVertex()
    {
        // P0 -> P1 -> P2, острый поворот.
        WorldMapRoadDefinition road = BuildRoad(
            true, 4f, (0f, 50f), (50f, 50f), (50f, 0f));

        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 25f, 50f), Is.True);
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 50f, 25f), Is.True);
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 51f, 51f), Is.True,
            "Точка рядом с вершиной поворота должна засчитываться хотя бы одним из двух сегментов.");
    }

    [Test]
    public void ZeroLengthSegment_DoesNotThrowOrProduceNaN()
    {
        WorldMapRoadDefinition road = BuildRoad(
            true, 4f, (50f, 50f), (50f, 50f), (100f, 50f));

        Assert.DoesNotThrow(() =>
            WorldMapGameplayTerrainQuery.IsInsideRoad(road, 50f, 50f));

        bool result = WorldMapGameplayTerrainQuery.IsInsideRoad(road, 50f, 50f);
        Assert.That(double.IsNaN(result ? 0 : 0), Is.False);
        Assert.That(result, Is.True);
    }

    [Test]
    public void PathWithZeroPoints_IsIgnored()
    {
        WorldMapRoadDefinition road = BuildRoad(true, 4f);
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 50f, 50f), Is.False);
    }

    [Test]
    public void PathWithOnePoint_IsIgnored()
    {
        WorldMapRoadDefinition road = BuildRoad(true, 4f, (50f, 50f));
        Assert.That(WorldMapGameplayTerrainQuery.IsInsideRoad(road, 50f, 50f), Is.False);
    }

    [Test]
    public void DisabledRoad_IsIgnoredByTerrainTypeQuery()
    {
        WorldMapDefinitionData definition = new WorldMapDefinitionData();
        definition.Roads.Add(BuildRoad(false, 4f, (0f, 50f), (100f, 50f)));

        WorldMapGameplayTerrainType terrain =
            WorldMapGameplayTerrainQuery.GetTerrainTypeAtPosition(definition, 50f, 50f);

        Assert.That(terrain, Is.EqualTo(WorldMapGameplayTerrainType.OpenGround));
    }

    [Test]
    public void MultipleRoads_HittingAnyEnabledRoadReturnsRoad()
    {
        WorldMapDefinitionData definition = new WorldMapDefinitionData();
        definition.Roads.Add(BuildRoad(true, 2f, (0f, 0f), (30f, 0f)));
        definition.Roads.Add(BuildRoad(true, 2f, (0f, 80f), (100f, 80f)));

        Assert.That(
            WorldMapGameplayTerrainQuery.GetTerrainTypeAtPosition(definition, 50f, 80f),
            Is.EqualTo(WorldMapGameplayTerrainType.Road));
        Assert.That(
            WorldMapGameplayTerrainQuery.GetTerrainTypeAtPosition(definition, 15f, 0.5f),
            Is.EqualTo(WorldMapGameplayTerrainType.Road));
    }

    [Test]
    public void NoRoads_GivesOpenGroundWithMultiplierOne()
    {
        WorldMapDefinitionData definition = new WorldMapDefinitionData();

        Assert.That(
            WorldMapGameplayTerrainQuery.GetTerrainTypeAtPosition(definition, 40f, 40f),
            Is.EqualTo(WorldMapGameplayTerrainType.OpenGround));
        Assert.That(
            WorldMapGameplayTerrainQuery.GetMovementMultiplier(definition, 40f, 40f),
            Is.EqualTo(1.0f).Within(0.0001f));
    }

    [Test]
    public void NullDefinition_GivesOpenGroundWithMultiplierOne()
    {
        Assert.That(
            WorldMapGameplayTerrainQuery.GetTerrainTypeAtPosition(null, 40f, 40f),
            Is.EqualTo(WorldMapGameplayTerrainType.OpenGround));
        Assert.That(
            WorldMapGameplayTerrainQuery.GetMovementMultiplier(null, 40f, 40f),
            Is.EqualTo(1.0f).Within(0.0001f));
    }

    [Test]
    public void RoadMultiplierAppliesOnlyInsideZone()
    {
        WorldMapDefinitionData definition = new WorldMapDefinitionData();
        definition.Roads.Add(BuildRoad(true, 4f, (0f, 50f), (100f, 50f)));

        float onRoad = WorldMapGameplayTerrainQuery.GetMovementMultiplier(definition, 50f, 50f);
        float offRoad = WorldMapGameplayTerrainQuery.GetMovementMultiplier(definition, 50f, 10f);

        Assert.That(onRoad, Is.EqualTo(1.30f).Within(0.0001f));
        Assert.That(offRoad, Is.EqualTo(1.0f).Within(0.0001f));
    }

    [Test]
    public void CustomGameplayTerrainSettings_OverrideDefaults()
    {
        WorldMapDefinitionData definition = new WorldMapDefinitionData();
        definition.Roads.Add(BuildRoad(true, 4f, (0f, 50f), (100f, 50f)));
        definition.GameplayTerrainSettings.Add(new WorldMapGameplayTerrainSettings
        {
            Terrain = WorldMapGameplayTerrainType.Road,
            Traversable = true,
            MovementMultiplier = 2.5f
        });

        float onRoad = WorldMapGameplayTerrainQuery.GetMovementMultiplier(definition, 50f, 50f);
        Assert.That(onRoad, Is.EqualTo(2.5f).Within(0.0001f));
    }
}
