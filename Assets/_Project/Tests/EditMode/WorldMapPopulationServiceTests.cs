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
        WorldMapNavigation.ConfigureDefaultTerrain();
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
        WorldMapNavigation.ConfigureDefaultTerrain();
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

    [Test]
    public void Populate_AnchoredLocationWithRequiredTagsOnlyUsesCompatibleSlots()
    {
        List<WorldMapSpawnSlotDefinition> slots = new List<WorldMapSpawnSlotDefinition>
        {
            new WorldMapSpawnSlotDefinition
            {
                Id = "slot-plain",
                Tags = new List<string>(),
                MinXPercent = 0f, MaxXPercent = 20f, MinYPercent = 0f, MaxYPercent = 20f
            },
            new WorldMapSpawnSlotDefinition
            {
                Id = "slot-forest",
                Tags = new List<string> { "Forest" },
                MinXPercent = 80f, MaxXPercent = 100f, MinYPercent = 80f, MaxYPercent = 100f
            }
        };

        List<WorldMapLocationTemplateData> templates = new List<WorldMapLocationTemplateData>
        {
            new WorldMapLocationTemplateData
            {
                Id = "hermit-camp",
                Name = "Стоянка отшельника",
                RequiredSlotTags = new List<string> { "Forest" }
            }
        };

        for (int seed = 0; seed < 20; seed++)
        {
            LocationData location =
                WorldMapPopulationService.Populate(seed, templates, slots).Single();

            Assert.That(location.MapXPercent, Is.InRange(80f, 100f),
                "Локация с требованием тега 'Forest' никогда не должна оказаться в слоте без него.");
            Assert.That(location.MapYPercent, Is.InRange(80f, 100f));
        }
    }

    [Test]
    public void Populate_NamedSpawnSlotIdWinsOverRequiredTags()
    {
        List<WorldMapSpawnSlotDefinition> slots = new List<WorldMapSpawnSlotDefinition>
        {
            new WorldMapSpawnSlotDefinition
            {
                Id = "slot-named",
                Tags = new List<string>(),
                MinXPercent = 30f, MaxXPercent = 40f, MinYPercent = 30f, MaxYPercent = 40f
            },
            new WorldMapSpawnSlotDefinition
            {
                Id = "slot-forest",
                Tags = new List<string> { "Forest" },
                MinXPercent = 80f, MaxXPercent = 100f, MinYPercent = 80f, MaxYPercent = 100f
            }
        };

        List<WorldMapLocationTemplateData> templates = new List<WorldMapLocationTemplateData>
        {
            new WorldMapLocationTemplateData
            {
                Id = "named",
                Name = "Named",
                SpawnSlotId = "slot-named",
                RequiredSlotTags = new List<string> { "Forest" }
            }
        };

        LocationData location = WorldMapPopulationService.Populate(1, templates, slots).Single();

        Assert.That(location.MapXPercent, Is.InRange(30f, 40f));
        Assert.That(location.MapYPercent, Is.InRange(30f, 40f));
    }
}
