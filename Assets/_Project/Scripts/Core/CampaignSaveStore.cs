using System;
using System.Collections.Generic;
using System.IO;

// ПР-04: слоты сохранений кампании — один автосейв и три ручных слота.
// Чистый C# (System.IO): каталог и JSON-сериализация передаются снаружи
// (Unity-слой даёт persistentDataPath и JsonUtility), поэтому хранилище
// проверяется EditMode-тестами на временной папке.
//
// Запись атомарна: сначала временный файл, затем замена с резервной копией
// .bak. Чтение: если основной файл повреждён, берётся резервная копия —
// о чём сообщается, — но несовместимая версия никогда не «чинится» копией.
public sealed class CampaignSaveStore
{
    public const string AutosaveSlotId = "autosave";

    // Тесты подменяют каталог сохранений, чтобы не трогать настоящие
    // сохранения игрока. В игре всегда null.
    public static string DirectoryOverride { get; set; }
    public static readonly IReadOnlyList<string> ManualSlotIds = new[] { "slot1", "slot2", "slot3" };

    // Первый ручной слот — прежний единственный файл сохранения, чтобы
    // старые партии остались на месте без миграции.
    private const string LegacySlotFileName = "campaign.save.json";

    private readonly string directory;
    private readonly Func<CampaignSaveData, string> serialize;
    private readonly Func<string, CampaignSaveData> deserialize;

    public CampaignSaveStore(
        string directory,
        Func<CampaignSaveData, string> serialize,
        Func<string, CampaignSaveData> deserialize)
    {
        this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
        this.serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
        this.deserialize = deserialize ?? throw new ArgumentNullException(nameof(deserialize));
    }

    public static IReadOnlyList<string> AllSlotIds
    {
        get
        {
            List<string> ids = new List<string> { AutosaveSlotId };
            ids.AddRange(ManualSlotIds);
            return ids;
        }
    }

    public static string GetSlotTitle(string slotId)
    {
        if (slotId == AutosaveSlotId)
            return "Автосохранение";

        for (int i = 0; i < ManualSlotIds.Count; i++)
        {
            if (ManualSlotIds[i] == slotId)
                return "Слот " + (i + 1);
        }

        return slotId;
    }

    public string GetPath(string slotId)
    {
        if (slotId == AutosaveSlotId)
            return Path.Combine(directory, "campaign.autosave.json");
        if (slotId == ManualSlotIds[0])
            return Path.Combine(directory, LegacySlotFileName);
        if (IsKnownSlot(slotId))
            return Path.Combine(directory, "campaign." + slotId + ".json");

        throw new ArgumentException("Неизвестный слот сохранения: " + slotId, nameof(slotId));
    }

    public static bool IsKnownSlot(string slotId)
    {
        if (slotId == AutosaveSlotId)
            return true;
        foreach (string id in ManualSlotIds)
        {
            if (id == slotId)
                return true;
        }
        return false;
    }

    public void Write(string slotId, CampaignSaveData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        string path = GetPath(slotId);
        Directory.CreateDirectory(directory);

        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, serialize(data));

        if (File.Exists(path))
            File.Replace(tempPath, path, path + ".bak");
        else
            File.Move(tempPath, path);
    }

    public CampaignSaveReadResult Read(string slotId)
    {
        string path = GetPath(slotId);
        if (!File.Exists(path))
            return CampaignSaveReadResult.Missing(slotId);

        DateTime writtenAt = File.GetLastWriteTime(path);
        if (TryDeserialize(path, out CampaignSaveData data))
            return CampaignSaveReadResult.Loaded(slotId, data, writtenAt, false);

        string backupPath = path + ".bak";
        if (File.Exists(backupPath) && TryDeserialize(backupPath, out CampaignSaveData backup))
            return CampaignSaveReadResult.Loaded(slotId, backup, File.GetLastWriteTime(backupPath), true);

        return CampaignSaveReadResult.Corrupted(slotId, writtenAt);
    }

    private bool TryDeserialize(string path, out CampaignSaveData data)
    {
        data = null;
        try
        {
            data = deserialize(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return false;
        }

        return data != null && data.State != null;
    }
}

public sealed class CampaignSaveReadResult
{
    public string SlotId { get; private set; }
    public bool Exists { get; private set; }
    public bool IsReadable => Data != null;
    public bool FromBackup { get; private set; }
    public CampaignSaveData Data { get; private set; }
    public DateTime WrittenAt { get; private set; }

    public static CampaignSaveReadResult Missing(string slotId)
    {
        return new CampaignSaveReadResult { SlotId = slotId };
    }

    public static CampaignSaveReadResult Corrupted(string slotId, DateTime writtenAt)
    {
        return new CampaignSaveReadResult { SlotId = slotId, Exists = true, WrittenAt = writtenAt };
    }

    public static CampaignSaveReadResult Loaded(string slotId, CampaignSaveData data, DateTime writtenAt, bool fromBackup)
    {
        return new CampaignSaveReadResult
        {
            SlotId = slotId,
            Exists = true,
            Data = data,
            WrittenAt = writtenAt,
            FromBackup = fromBackup
        };
    }
}
