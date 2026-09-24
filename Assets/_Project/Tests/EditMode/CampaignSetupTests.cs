using NUnit.Framework;
using UnityEngine;

// ПР-05: конфигурация кампании фиксируется при старте и переживает
// сохранение; неверный выбор не создаёт полусозданную кампанию.
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
        Assert.AreEqual(CampaignStartOptions.HomeOnForeignWaterCrisisId, configuration.CrisisId);
        Assert.AreEqual(CampaignStartOptions.PlaceholderCommanderId, configuration.CommanderProfileId);
        Assert.AreEqual(CampaignStartOptions.BaseHomeStartId, configuration.StartingConditionId);
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
        CampaignSetup setup = new CampaignSetup { CrisisId = "crisis.unknown" };
        Assert.IsFalse(setup.Validate(out string reason));
        StringAssert.Contains("кризис", reason);
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
        foreach (CampaignOptionDefinition option in CampaignStartOptions.Crises)
            Assert.IsNotEmpty(option.Summary, option.Id);
        foreach (CampaignOptionDefinition option in CampaignStartOptions.Commanders)
            Assert.IsNotEmpty(option.Summary, option.Id);
        foreach (CampaignOptionDefinition option in CampaignStartOptions.StartingConditions)
            Assert.IsNotEmpty(option.Summary, option.Id);
    }
}
