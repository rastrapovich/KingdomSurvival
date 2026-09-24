using System;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;

// Сохранения кампании в Unity-слое. Файлы и слоты — CampaignSaveStore (Core,
// чистый C#), проверка совместимости — CampaignSaveService.IsLoadable; здесь
// только persistentDataPath, JsonUtility и применение загруженной кампании.
//
// ПР-04: один автосейв и три ручных слота (первый — прежний единственный
// файл, старые партии на месте). Автосохранение тихое: перед боем, после
// применения итога боя и после завершения сюжетной сцены — в устойчивых
// точках, не посреди сцены или обязательного выбора. Ручное сохранение
// доступно из меню паузы, которое не открывается поверх диалога, решения
// или боя.
public partial class PrototypeUIController
{
    private CampaignSaveStore campaignSaveStore;

    private CampaignSaveStore SaveStore
    {
        get
        {
            if (campaignSaveStore == null)
            {
                campaignSaveStore = new CampaignSaveStore(
                    CampaignSaveStore.DirectoryOverride ?? Application.persistentDataPath,
                    data => JsonUtility.ToJson(data, true),
                    json => JsonUtility.FromJson<CampaignSaveData>(json));
            }
            return campaignSaveStore;
        }
    }

    private static void GetActiveWorld(out string worldId, out int geographyVersion)
    {
        WorldMapDatabaseAsset mapDatabase = WorldMapVisualRuntime.LoadDatabase();
        bool hasWorld = mapDatabase != null && mapDatabase.ActiveWorld != null;
        worldId = hasWorld ? mapDatabase.ActiveWorld.WorldDefinitionId : string.Empty;
        geographyVersion = hasWorld ? mapDatabase.ActiveWorld.GeographyVersion : 0;
    }

    // Сводка слота для меню: есть ли, можно ли загрузить, что показать.
    private readonly struct SaveSlotSummary
    {
        public readonly string SlotId;
        public readonly bool Exists;
        public readonly bool IsLoadable;
        public readonly string Description;
        public readonly DateTime WrittenAt;

        public SaveSlotSummary(string slotId, bool exists, bool isLoadable, string description, DateTime writtenAt)
        {
            SlotId = slotId;
            Exists = exists;
            IsLoadable = isLoadable;
            Description = description;
            WrittenAt = writtenAt;
        }
    }

    private SaveSlotSummary ReadSlotSummary(string slotId)
    {
        CampaignSaveReadResult read = SaveStore.Read(slotId);
        if (!read.Exists)
            return new SaveSlotSummary(slotId, false, false, "пусто", DateTime.MinValue);
        if (!read.IsReadable)
            return new SaveSlotSummary(slotId, true, false, "файл повреждён", read.WrittenAt);

        GetActiveWorld(out string worldId, out int geographyVersion);
        string when = read.WrittenAt.ToString("dd.MM HH:mm");
        if (!CampaignSaveService.IsLoadable(read.Data, worldId, geographyVersion, out string reason))
            return new SaveSlotSummary(slotId, true, false, "нельзя загрузить: " + reason, read.WrittenAt);

        string description = "день " + read.Data.State.Day + " · " + when;
        if (read.FromBackup)
            description += " · из резервной копии";
        return new SaveSlotSummary(slotId, true, true, description, read.WrittenAt);
    }

    // Самое свежее загружаемое сохранение — для «Продолжить».
    private SaveSlotSummary? FindMostRecentLoadableSlot()
    {
        SaveSlotSummary? best = null;
        foreach (string slotId in CampaignSaveStore.AllSlotIds)
        {
            SaveSlotSummary summary = ReadSlotSummary(slotId);
            if (summary.IsLoadable && (!best.HasValue || summary.WrittenAt > best.Value.WrittenAt))
                best = summary;
        }
        return best;
    }

    private bool SaveCampaign(string slotId)
    {
        if (gameState == null)
        {
            ReportCampaignIo("[Сохранение] Нет активной партии.");
            return false;
        }

        try
        {
            GetActiveWorld(out string worldId, out int geographyVersion);
            SaveStore.Write(slotId, CampaignSaveService.ExportCampaign(gameState, worldId, geographyVersion));
            ReportCampaignIo("[Сохранение] Партия сохранена: " + CampaignSaveStore.GetSlotTitle(slotId) + ".");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError("Kingdom Survival: не удалось сохранить партию — " + exception);
            ReportCampaignIo("[Сохранение] Не удалось сохранить партию: " + exception.Message);
            return false;
        }
    }

    // Тихое автосохранение в устойчивой точке. Ошибка не прерывает игру.
    private void Autosave()
    {
        if (gameState == null || isGameOver || !CampaignSession.HasActive)
            return;

        try
        {
            GetActiveWorld(out string worldId, out int geographyVersion);
            SaveStore.Write(CampaignSaveStore.AutosaveSlotId,
                CampaignSaveService.ExportCampaign(gameState, worldId, geographyVersion));
        }
        catch (Exception exception)
        {
            Debug.LogError("Kingdom Survival: автосохранение не удалось — " + exception);
        }
    }

    private bool LoadCampaign(string slotId)
    {
        CampaignSaveReadResult read = SaveStore.Read(slotId);
        if (!read.Exists)
        {
            ReportCampaignIo("[Загрузка] " + CampaignSaveStore.GetSlotTitle(slotId) + ": сохранения нет.");
            return false;
        }

        if (!read.IsReadable)
        {
            ReportCampaignIo("[Загрузка] Файл сохранения повреждён, партия не тронута.");
            return false;
        }

        GetActiveWorld(out string worldId, out int geographyVersion);
        if (!CampaignSaveService.IsLoadable(read.Data, worldId, geographyVersion, out string reason))
        {
            ReportCampaignIo("[Загрузка] Сохранение нельзя загрузить: " + reason + ". Файл не тронут.");
            return false;
        }

        GameState restored;
        try
        {
            restored = CampaignSaveService.RestoreCampaign(read.Data);
        }
        catch (Exception exception)
        {
            Debug.LogError("Kingdom Survival: не удалось восстановить партию — " + exception);
            ReportCampaignIo("[Загрузка] Не удалось восстановить партию: " + exception.Message);
            return false;
        }

        // Авторская география не хранится в файле сохранения (это
        // статический WorldMapNavigation, не поле GameState) — переприменяем
        // тот же мир до того, как экраны прочитают рельеф/маршрут.
        EnsureWorldMapGeographyConfigured();

        CampaignSession.Begin(restored);

        gameState = restored;
        isGameOver = false;
        lastNavigationClickTime = -NavigationClickCooldownSeconds;
        unreadIncidents.Clear();
        reportRequiresAcknowledgement.Clear();
        reportReadStates.Clear();
        selectedFighterIds.Clear();
        selectedJournalGoalId = null;
        ClearQueuedModals();
        ResetWorldMapSelection();

        HideIncidentModal();
        HideGameOver();
        CloseMainScreen();

        ReportCampaignIo(read.FromBackup
            ? "[Загрузка] Основной файл повреждён — партия загружена из резервной копии."
            : "[Загрузка] Партия загружена: " + CampaignSaveStore.GetSlotTitle(slotId) + ".");
        RefreshInterface();
        return true;
    }

    private bool LoadMostRecentCampaign()
    {
        SaveSlotSummary? recent = FindMostRecentLoadableSlot();
        if (!recent.HasValue)
        {
            ReportCampaignIo("[Загрузка] Нет сохранений, которые можно загрузить.");
            return false;
        }

        return LoadCampaign(recent.Value.SlotId);
    }
}
