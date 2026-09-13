using System;
using System.IO;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;

// AM-05 (канон v1.33, §9.9 / раздел 15 инструкции по миграции): минимальный
// действующий вход Save/Load. Единственный слот (без менеджера профилей —
// "не нужен для первого рабочего результата"). Фактический JsonUtility и
// File I/O живут здесь (Unity-слой), а не в KingdomSurvival.Core — Core
// собирает/восстанавливает CampaignSaveData чистым C#, эта часть только
// сериализует его в файл и обратно.
//
// Известное ограничение первой версии: сохранение во время активного
// обязательного модального окна (диалог/происшествие/решение) не
// запрещается явно — если это окажется проблемой на практике, следующий шаг
// (раздел 15 инструкции допускает такое ограничение как временное) —
// заблокировать SaveCampaign, пока queuedModals непусто.
public partial class PrototypeUIController
{
    private const string CampaignSaveFileName = "campaign.save.json";

    private static string CampaignSavePath =>
        Path.Combine(Application.persistentDataPath, CampaignSaveFileName);

    public bool HasSavedCampaign => File.Exists(CampaignSavePath);

    private void SaveCampaign()
    {
        if (gameState == null)
        {
            AddReport("[Сохранение] Нет активной партии.");
            return;
        }

        try
        {
            WorldMapDatabaseAsset mapDatabase = WorldMapVisualRuntime.LoadDatabase();
            string worldId = mapDatabase != null && mapDatabase.ActiveWorld != null
                ? mapDatabase.ActiveWorld.WorldDefinitionId
                : string.Empty;
            int geographyVersion = mapDatabase != null && mapDatabase.ActiveWorld != null
                ? mapDatabase.ActiveWorld.GeographyVersion
                : 0;

            CampaignSaveData data =
                CampaignSaveService.ExportCampaign(gameState, worldId, geographyVersion);
            string json = JsonUtility.ToJson(data, true);

            WriteFileAtomically(CampaignSavePath, json);
            AddReport("[Сохранение] Партия сохранена.");
        }
        catch (Exception exception)
        {
            Debug.LogError("Kingdom Survival: не удалось сохранить партию — " + exception);
            AddReport("[Сохранение] Не удалось сохранить партию: " + exception.Message);
        }
    }

    private void LoadCampaign()
    {
        string path = CampaignSavePath;
        if (!File.Exists(path))
        {
            AddReport("[Загрузка] Файл сохранения не найден.");
            return;
        }

        CampaignSaveData data;
        try
        {
            string json = File.ReadAllText(path);
            data = JsonUtility.FromJson<CampaignSaveData>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError("Kingdom Survival: не удалось прочитать сохранение — " + exception);
            AddReport("[Загрузка] Файл сохранения повреждён, партия не тронута.");
            return;
        }

        if (data == null || data.State == null)
        {
            AddReport("[Загрузка] Файл сохранения повреждён, партия не тронута.");
            return;
        }

        if (data.SaveFormatVersion != CampaignSaveService.CurrentSaveFormatVersion)
        {
            AddReport(
                "[Загрузка] Формат сохранения (" + data.SaveFormatVersion +
                ") не совпадает с текущим (" + CampaignSaveService.CurrentSaveFormatVersion +
                ") — загрузка отменена, партия не тронута.");
            return;
        }

        WorldMapDatabaseAsset mapDatabase = WorldMapVisualRuntime.LoadDatabase();
        string activeWorldId = mapDatabase != null && mapDatabase.ActiveWorld != null
            ? mapDatabase.ActiveWorld.WorldDefinitionId
            : string.Empty;

        if (!string.IsNullOrEmpty(data.WorldDefinitionId) && data.WorldDefinitionId != activeWorldId)
        {
            AddReport(
                "[Загрузка] Это сохранение использует другую авторскую карту ('" +
                data.WorldDefinitionId + "'), а сейчас активна '" + activeWorldId +
                "' — загрузка отменена, чтобы не перенести героя на чужую географию.");
            return;
        }

        GameState restored;
        try
        {
            restored = CampaignSaveService.RestoreCampaign(data);
        }
        catch (Exception exception)
        {
            Debug.LogError("Kingdom Survival: не удалось восстановить партию — " + exception);
            AddReport("[Загрузка] Не удалось восстановить партию: " + exception.Message);
            return;
        }

        // Авторская география не хранится в файле сохранения (это
        // статический WorldMapNavigation, не поле GameState) — переприменяем
        // тот же мир до того, как экраны прочитают рельеф/маршрут.
        EnsureWorldMapGeographyConfigured();

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

        AddReport("[Загрузка] Партия загружена.");
        RefreshInterface();
    }

    // Запись во временный файл с последующей заменой (раздел 15 инструкции):
    // ошибка посреди записи не должна повредить уже существующее сохранение.
    private static void WriteFileAtomically(string path, string content)
    {
        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);

        if (File.Exists(path))
        {
            string backupPath = path + ".bak";
            File.Replace(tempPath, path, backupPath);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}
