using NUnit.Framework;
using UnityEngine;

// ПР-05: конфигурация кампании фиксируется при старте и переживает
// сохранение; неверный выбор не создаёт полусозданную кампанию. Канон v1.59:
// режим один — свободная игра, без выбора кризиса и начального состояния.
public sealed class CampaignSetupTests
{
    [Test]
    public void DefaultSetup_IsValid_AndRecordsConfiguration()
    {
        CampaignSetup setup = new CampaignSetup { WorldSeed = 20260924 };
        Assert.IsTrue(setup.Validate(out string reason), reason);

        GameState state = setup.CreateCampaign();
        CampaignConfiguration configuration = state.Configuration;

        Assert.IsNotNull(configuration);
        Assert.IsNotEmpty(configuration.CampaignId);
        Assert.AreEqual(CampaignStartOptions.FreePlayId, configuration.CrisisId);
        Assert.AreEqual(CampaignStartOptions.PlaceholderCommanderId, configuration.CommanderProfileId);
        Assert.AreEqual(CampaignStartOptions.RoadCampStartId, configuration.StartingConditionId);
        Assert.AreEqual(CampaignConfiguration.CurrentContentVersion, configuration.ContentVersion);
        Assert.AreEqual(20260924, configuration.WorldSeed);
    }

    [Test]
    public void EachCampaign_GetsItsOwnId()
    {
        GameState first = new CampaignSetup().CreateCampaign();
        GameState second = new CampaignSetup().CreateCampaign();
        Assert.AreNotEqual(first.Configuration.CampaignId, second.Configuration.CampaignId);
    }

    [Test]
    public void UnknownChoice_IsRejected_BeforeAnythingIsCreated()
    {
        CampaignSetup setup = new CampaignSetup { CommanderProfileId = "commander.unknown" };
        Assert.IsFalse(setup.Validate(out string reason));
        StringAssert.Contains("командир", reason);
        Assert.Throws<System.InvalidOperationException>(() => setup.CreateCampaign());
    }

    [Test]
    public void Configuration_SurvivesSaveLoad_Unchanged()
    {
        GameState state = new CampaignSetup { WorldSeed = 7 }.CreateCampaign();
        CampaignConfiguration before = state.Configuration;

        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state));
        GameState restored = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));

        Assert.AreEqual(before.CampaignId, restored.Configuration.CampaignId);
        Assert.AreEqual(before.CrisisId, restored.Configuration.CrisisId);
        Assert.AreEqual(before.StartingConditionId, restored.Configuration.StartingConditionId);
        Assert.AreEqual(before.WorldSeed, restored.Configuration.WorldSeed);
        Assert.AreEqual(state.WorldSeed, restored.WorldSeed, "Загрузка не перебрасывает мир.");
    }

    [Test]
    public void OptionTexts_ArePresent_ForEveryChoice()
    {
        foreach (CampaignOptionDefinition option in CampaignStartOptions.Commanders)
            Assert.IsNotEmpty(option.Summary, option.Id);
    }

    // 12У-H: основные параметры карты новой партии.
    [Test]
    public void MapOptions_FourSizes_ApplyToCopy_AndSurviveSave()
    {
        foreach (int size in new[] { 50, 100, 140, 180 })
            Assert.IsNotNull(CampaignStartOptions.FindMapSize(size), "Размер " + size);

        WorldMapGenerationSettings asset = WorldMapGenerationSettings.CreateDefault();
        int bands = asset.Groups.Find(g => g.Role == WorldMapGroupRule.Band).Count;
        int lairs = asset.Landmarks.Find(r => r.Id == "lair").Count;

        WorldMapGenerationSettings many = asset.Clone();
        new CampaignMapOptions { Enemies = CampaignMapOptions.High, Water = CampaignMapOptions.High, Relief = CampaignMapOptions.Low, RegionKind = WorldMapRegionKind.RiverValley }.ApplyTo(many, 180);
        Assert.AreEqual(180, many.HexesAcross);
        Assert.AreEqual(WorldMapRegionKind.RiverValley, many.RegionKind);
        Assert.Greater(many.Groups.Find(g => g.Role == WorldMapGroupRule.Band).Count, bands, "Врагов много — ватаг больше.");
        Assert.Greater(many.Landmarks.Find(r => r.Id == "lair").Count, lairs, "Врагов много — логов больше.");
        Assert.Greater(many.LakesShare, asset.LakesShare);
        Assert.Less(many.HillsShare, asset.HillsShare);
        Assert.Greater(many.VillageCount, asset.VillageCount, "Большая карта — больше деревень.");

        WorldMapGenerationSettings few = asset.Clone();
        new CampaignMapOptions { Enemies = CampaignMapOptions.Low }.ApplyTo(few, 100);
        Assert.Less(few.Landmarks.Find(r => r.Id == "lair").Count, lairs + 0, "Врагов мало — логов меньше.");
        Assert.AreEqual(bands, asset.Groups.Find(g => g.Role == WorldMapGroupRule.Band).Count, "Исходные настройки не меняются.");
        Assert.AreEqual(1.0, new CampaignMapOptions().RoadEncounterShare);
        Assert.Less(new CampaignMapOptions { Enemies = CampaignMapOptions.Low }.RoadEncounterShare, 1.0);

        GeneratedWorldMap map = WorldMapGenerator.Generate(many, 7);
        Assert.IsTrue(map.IsValid, "Очень большая карта генерируется.");
        Assert.That(map.Report.Issues.FindAll(x => x.Error), Is.Empty);

        CampaignSetup setup = new CampaignSetup { WorldSeed = 5, MapHexesAcross = 140, MapOptions = new CampaignMapOptions { Enemies = CampaignMapOptions.Low, Forest = CampaignMapOptions.High } };
        Assert.IsTrue(setup.Validate(out string reason), reason);
        GameState state = setup.CreateCampaign();
        GameState loaded = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        Assert.AreEqual(CampaignMapOptions.Low, loaded.Configuration.MapOptions.Enemies, "Выбор карты сохраняется в партии.");
        Assert.AreEqual(CampaignMapOptions.High, loaded.Configuration.MapOptions.Forest);
    }
}
