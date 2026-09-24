using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

// ПР-04: слоты сохранений и совместимость. Хранилище — чистый C#, проверяется
// на временной папке с тем же JsonUtility, что и игра.
public sealed class CampaignSaveStoreTests
{
    private string directory;
    private CampaignSaveStore store;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "ks-save-tests-" + Guid.NewGuid().ToString("N"));
        store = new CampaignSaveStore(
            directory,
            data => JsonUtility.ToJson(data, true),
            json => JsonUtility.FromJson<CampaignSaveData>(json));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private static CampaignSaveData NewSave(int day, string world = "", int geography = 0)
    {
        GameState state = new GameState();
        state.CreateNewGame(20260924);
        state.Day = day;
        return CampaignSaveService.ExportCampaign(state, world, geography);
    }

    [Test]
    public void Slots_AreAutosaveAndThreeManual_FirstManualIsLegacyFile()
    {
        CollectionAssert.AreEqual(
            new[] { CampaignSaveStore.AutosaveSlotId, "slot1", "slot2", "slot3" },
            CampaignSaveStore.AllSlotIds);
        Assert.AreEqual("campaign.save.json", Path.GetFileName(store.GetPath("slot1")),
            "Прежний единственный файл остаётся первым слотом — старые партии на месте.");
        Assert.AreNotEqual(store.GetPath("slot2"), store.GetPath(CampaignSaveStore.AutosaveSlotId));
    }

    [Test]
    public void Write_ThenRead_RoundTrips_PerSlot()
    {
        store.Write("slot2", NewSave(5));
        store.Write(CampaignSaveStore.AutosaveSlotId, NewSave(7));

        Assert.AreEqual(5, store.Read("slot2").Data.State.Day);
        Assert.AreEqual(7, store.Read(CampaignSaveStore.AutosaveSlotId).Data.State.Day);
        Assert.IsFalse(store.Read("slot3").Exists);
    }

    [Test]
    public void CorruptedMainFile_FallsBackToBackup_AndSaysSo()
    {
        store.Write("slot1", NewSave(3));
        store.Write("slot1", NewSave(4));
        File.WriteAllText(store.GetPath("slot1"), "{ not json");

        CampaignSaveReadResult read = store.Read("slot1");
        Assert.IsTrue(read.IsReadable);
        Assert.IsTrue(read.FromBackup);
        Assert.AreEqual(3, read.Data.State.Day, "Резервная копия — предыдущая запись.");
    }

    [Test]
    public void CorruptedWithoutBackup_IsReportedAsCorrupted_FileUntouched()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(store.GetPath("slot3"), "garbage");

        CampaignSaveReadResult read = store.Read("slot3");
        Assert.IsTrue(read.Exists);
        Assert.IsFalse(read.IsReadable);
        Assert.AreEqual("garbage", File.ReadAllText(store.GetPath("slot3")));
    }

    [Test]
    public void IsLoadable_RejectsOtherFormat_OtherWorld_ChangedGeography()
    {
        CampaignSaveData ok = NewSave(1, "world.a", 2);
        Assert.IsTrue(CampaignSaveService.IsLoadable(ok, "world.a", 2, out _));

        CampaignSaveData oldFormat = NewSave(1);
        oldFormat.SaveFormatVersion = 0;
        Assert.IsFalse(CampaignSaveService.IsLoadable(oldFormat, string.Empty, 0, out string formatReason));
        StringAssert.Contains("формат", formatReason);

        Assert.IsFalse(CampaignSaveService.IsLoadable(ok, "world.b", 2, out string worldReason));
        StringAssert.Contains("карта", worldReason);

        Assert.IsFalse(CampaignSaveService.IsLoadable(ok, "world.a", 3, out string geographyReason),
            "ПР-04: версия географии теперь проверяется при загрузке.");
        StringAssert.Contains("география", geographyReason);
    }

    [Test]
    public void IsLoadable_UnknownGeographyVersion_IsNotRejected()
    {
        CampaignSaveData legacy = NewSave(1, "world.a", 0);
        Assert.IsTrue(CampaignSaveService.IsLoadable(legacy, "world.a", 3, out _));
    }
}
