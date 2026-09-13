using System.Collections.Generic;
using KingdomSurvival.WorldMapVisual;
using NUnit.Framework;
using UnityEngine;

public sealed class WorldMapDatabaseTests
{
    [SetUp]
    public void ClearRuntimeCache()
    {
        WorldMapVisualRuntime.ClearCache();
    }

    [Test]
    public void Database_ContainsStartingLocationsAndBuildsRuntimeTemplates()
    {
        WorldMapDatabaseAsset database = Resources.Load<WorldMapDatabaseAsset>(
            WorldMapDatabaseAsset.ResourcesPath);

        Assert.IsNotNull(database);
        Assert.That(database.Locations.Count, Is.EqualTo(3));

        IReadOnlyList<WorldMapLocationTemplateData> templates =
            database.BuildRuntimeLocationTemplates();
        Assert.That(templates.Count, Is.EqualTo(3));
        Assert.That(templates[0].Id, Is.EqualTo("ruins"));
        Assert.That(templates[1].Id, Is.EqualTo("mine"));
        Assert.That(templates[2].Id, Is.EqualTo("forest"));
    }

    [Test]
    public void GameState_UsesInjectedLocationTemplatesInsteadOfHardcodedList()
    {
        List<WorldMapLocationTemplateData> templates =
            new List<WorldMapLocationTemplateData>
            {
                new WorldMapLocationTemplateData
                {
                    Id = "custom-place",
                    Name = "Новое место",
                    Threat = "средняя",
                    ExplorationHours = 3.5,
                    InitiallyDiscovered = true,
                    InitiallyVisibleOnMap = true,
                    SpawnSlotId = "slot-west"
                }
            };

        GameState state = new GameState();
        state.CreateNewGame(42, templates);

        Assert.That(state.Locations.Count, Is.EqualTo(1));
        LocationData location = state.Locations[0];
        Assert.That(location.Id, Is.EqualTo("custom-place"));
        Assert.That(location.Name, Is.EqualTo("Новое место"));
        Assert.That(location.ExplorationHours, Is.EqualTo(3.5));
        Assert.That(location.IsDiscovered, Is.True);
        Assert.That(location.MapSlotIndex, Is.EqualTo(0));
    }
}
