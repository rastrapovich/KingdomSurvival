using KingdomSurvival.WorldMapVisual;
using NUnit.Framework;
using UnityEngine;

// Размер карты новой партии (просьба пользователя 01.11.2026): маленькая —
// 50 клеток по ширине, средняя — 100; генератор берёт остальное из ассета.
public sealed class NewGameMapSizeTests
{
    [Test]
    public void Setup_DefaultsToMedium_AndRejectsUnknownSize()
    {
        CampaignSetup setup = new CampaignSetup();
        Assert.AreEqual(CampaignStartOptions.MediumMapHexesAcross, setup.MapHexesAcross);
        Assert.IsTrue(setup.Validate(out string reason), reason);
        Assert.AreEqual(2, CampaignStartOptions.MapSizes.Count);
        Assert.AreEqual(50, CampaignStartOptions.SmallMapHexesAcross);
        Assert.AreEqual(100, CampaignStartOptions.MediumMapHexesAcross);

        setup.MapHexesAcross = 77;
        Assert.IsFalse(setup.Validate(out reason));
        StringAssert.Contains("размер карты", reason);
    }

    [TestCase(50)]
    [TestCase(100)]
    public void Generator_BuildsMapOfChosenWidth_WithVillages(int hexesAcross)
    {
        WorldMapGeneratorAsset generator = WorldMapVisualRuntime.LoadGenerator();
        Assert.IsNotNull(generator);
        GeneratedWorldMap map = generator.Generate(20261101, hexesAcross);
        Assert.IsTrue(map.IsValid);
        Assert.AreEqual(hexesAcross, map.HexesAcross);
        Assert.Greater(map.Villages.Count, 0, "На карте есть поселения.");

        GameState state = new CampaignSetup { WorldSeed = map.Seed, MapHexesAcross = hexesAcross }.CreateCampaign(generatedMap: map);
        Assert.AreEqual(hexesAcross, state.GeneratedMap.HexesAcross, "Размер хранится в партии.");
    }
}
