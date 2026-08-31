using System.Collections.Generic;
using NUnit.Framework;

public class WorldMapOneCellMovementTests
{
    [Test]
    public void CellsPerDay_IsExactlyOne()
    {
        Assert.That(WorldMapNavigation.CellsPerDay, Is.EqualTo(1));
    }

    [Test]
    public void AdvanceRoute_OneDayMovesExactlyOneCell()
    {
        List<MapPointData> route = WorldMapNavigation.FindPath(
            WorldMapNavigation.CapitalXPercent,
            WorldMapNavigation.CapitalYPercent,
            90f,
            20f);

        Assert.That(route.Count, Is.GreaterThan(2));

        ExpeditionData expedition = new ExpeditionData
        {
            IsActive = true,
            Phase = CommanderState.TravellingToLocation,
            LocationId = "target",
            Route = route,
            RouteIndex = 0,
            DaysRemaining = WorldMapNavigation.CalculateDays(route),
            CurrentMapXPercent = route[0].XPercent,
            CurrentMapYPercent = route[0].YPercent,
            TargetMapXPercent = route[route.Count - 1].XPercent,
            TargetMapYPercent = route[route.Count - 1].YPercent
        };

        WorldMapNavigation.AdvanceRoute(expedition, 1);

        Assert.That(expedition.RouteIndex, Is.EqualTo(1));
        Assert.That(expedition.LastTravelPoints.Count, Is.EqualTo(1));
        Assert.That(
            expedition.CurrentMapXPercent,
            Is.EqualTo(route[1].XPercent).Within(0.001f));
        Assert.That(
            expedition.CurrentMapYPercent,
            Is.EqualTo(route[1].YPercent).Within(0.001f));
    }
}
