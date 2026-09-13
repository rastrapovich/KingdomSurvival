using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

// AM-04 (канон v1.33, §9.9): расстановка новой партии вынесена в отдельный
// сервис с явными режимами Fixed/Anchored/Temporary.
public class WorldMapPopulationServiceTests
{
    [Test]
    public void Populate_PlacesFixedLocationAtExactCoordinatesRegardlessOfSeed()
    {
        WorldMapNavigation.ConfigureTerrain(0);
        List<WorldMapLocationTemplateData> templates = new List<WorldMapLocationTemplateData>
        {
            new WorldMapLocationTemplateData
            {
                Id = "mill",
                Name = "Мельница",
                Mode = WorldMapPlacementMode.Fixed,
                FixedXPercent = 40f,
                FixedYPercent = 60f,
                InitiallyVisibleOnMap = true
            }
        };

        LocationData first = WorldMapPopulationService.Populate(1, templates).Single();
        LocationData second = WorldMapPopulationService.Populate(999999, templates).Single();

        Assert.That(first.MapXPercent, Is.EqualTo(40f).Within(0.001f));
        Assert.That(first.MapYPercent, Is.EqualTo(60f).Within(0.001f));
        Assert.That(second.MapXPercent, Is.EqualTo(first.MapXPercent).Within(0.001f));
        Assert.That(second.MapYPercent, Is.EqualTo(first.MapYPercent).Within(0.001f));
    }

    [Test]
    public void Populate_AssignsRegionIdMatchingRegistryNotSectorIndex()
    {
        WorldMapNavigation.ConfigureTerrain(0);
        List<WorldMapLocationTemplateData> templates = new List<WorldMapLocationTemplateData>
        {
            new WorldMapLocationTemplateData
            {
                Id = "east-outpost",
                Name = "Восточный форпост",
                Mode = WorldMapPlacementMode.Fixed,
                FixedXPercent = 90f,
                FixedYPercent = 20f
            }
        };

        LocationData location = WorldMapPopulationService.Populate(1, templates).Single();
        WorldMapRegionDefinition expectedRegion =
            WorldMapRegionRegistry.FindRegion(location.MapXPercent, location.MapYPercent);

        Assert.That(location.RegionId, Is.EqualTo(expectedRegion.Id));
        Assert.That(location.RegionId, Does.Not.StartWith("sector-"));
    }

    [Test]
    public void Populate_ExcludesTemporaryLocationsFromInitialPool()
    {
        List<WorldMapLocationTemplateData> templates = new List<WorldMapLocationTemplateData>
        {
            new WorldMapLocationTemplateData
            {
                Id = "camp",
                Name = "Временный лагерь",
                Mode = WorldMapPlacementMode.Temporary
            },
            new WorldMapLocationTemplateData
            {
                Id = "ruins",
                Name = "Руины",
                Mode = WorldMapPlacementMode.Anchored
            }
        };

        List<LocationData> result = WorldMapPopulationService.Populate(1, templates);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo("ruins"));
    }

    [Test]
    public void Populate_AnchoredPlacementIsDeterministicAndIndependentOfInputOrder()
    {
        List<WorldMapLocationTemplateData> forward = new List<WorldMapLocationTemplateData>
        {
            new WorldMapLocationTemplateData { Id = "a", Name = "A" },
            new WorldMapLocationTemplateData { Id = "b", Name = "B" }
        };
        List<WorldMapLocationTemplateData> reversed = new List<WorldMapLocationTemplateData>
        {
            new WorldMapLocationTemplateData { Id = "b", Name = "B" },
            new WorldMapLocationTemplateData { Id = "a", Name = "A" }
        };

        List<LocationData> fromForward = WorldMapPopulationService.Populate(42, forward);
        List<LocationData> fromReversed = WorldMapPopulationService.Populate(42, reversed);

        LocationData aFromForward = fromForward.Single(l => l.Id == "a");
        LocationData aFromReversed = fromReversed.Single(l => l.Id == "a");

        Assert.That(aFromReversed.MapXPercent, Is.EqualTo(aFromForward.MapXPercent).Within(0.001f));
        Assert.That(aFromReversed.MapYPercent, Is.EqualTo(aFromForward.MapYPercent).Within(0.001f));
    }
}
