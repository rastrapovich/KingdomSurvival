using System;
using System.IO;

// Сохранения PlayMode-тестов пишутся во временную папку, а не в
// persistentDataPath игрока: автосохранение перед боем и после сцены
// иначе перезаписало бы настоящие партии.
public static class PlayModeSaveIsolation
{
    public static string Begin()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ks-playmode-saves-" + Guid.NewGuid().ToString("N"));
        CampaignSaveStore.DirectoryOverride = directory;
        return directory;
    }

    public static void End()
    {
        string directory = CampaignSaveStore.DirectoryOverride;
        CampaignSaveStore.DirectoryOverride = null;
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}
