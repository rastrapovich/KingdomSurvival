using System;

// AM-05: сборка/восстановление CampaignSaveData — чистая C#-логика без
// файлового I/O и без JsonUtility (это Unity-зависимость, не допускается в
// KingdomSurvival.Core). Фактическое чтение/запись файла — в Unity-слое
// (см. PrototypeUIController.CampaignSave.cs), который вызывает эти методы
// до/после сериализации в JSON.
public static class CampaignSaveService
{
    // 2 — ПР-06А: люди Дома, домохозяйства, работы, свита, HP. Формат 1 не
    // мигрируется (решение пользователя 24.09.2026): такие сохранения честно
    // отклоняются, файл не удаляется.
    public const int CurrentSaveFormatVersion = 2;

    public static CampaignSaveData ExportCampaign(
        GameState state,
        string worldDefinitionId = null,
        int geographyVersion = 0)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        bool hasExpedition = state.ActiveExpedition != null;
        return new CampaignSaveData
        {
            SaveFormatVersion = CurrentSaveFormatVersion,
            WorldDefinitionId = worldDefinitionId ?? string.Empty,
            GeographyVersion = geographyVersion,
            State = state,
            HasActiveExpedition = hasExpedition,
            HasActiveActivity = hasExpedition && state.ActiveExpedition.ActiveActivity != null,
            HasPendingDecision = hasExpedition && state.ActiveExpedition.PendingDecision != null,
            ClockSnapshot = ContinuousSimulationSystem.ExportSnapshot(state),
            BuildingSnapshot = BuildingSystem.ExportSnapshot(state)
        };
    }

    // ПР-04: можно ли загрузить сохранение в текущую сборку. Несовместимое
    // не «чинится» — файл остаётся нетронутым, игрок видит причину.
    // Сохранение без авторского мира (WorldDefinitionId пуст) и версия
    // географии 0 (неизвестна) не проверяются — так было до авторской карты.
    public static bool IsLoadable(
        CampaignSaveData data,
        string activeWorldDefinitionId,
        int activeGeographyVersion,
        out string reason)
    {
        reason = string.Empty;
        if (data == null || data.State == null)
        {
            reason = "файл повреждён";
            return false;
        }

        if (data.SaveFormatVersion != CurrentSaveFormatVersion)
        {
            reason = data.SaveFormatVersion < CurrentSaveFormatVersion
                ? "сохранение сделано до появления людей Дома (формат " + data.SaveFormatVersion + ") — начните новую игру"
                : "формат сохранения " + data.SaveFormatVersion + " новее этой версии игры";
            return false;
        }

        if (data.State.People == null)
        {
            reason = "в сохранении нет людей Дома";
            return false;
        }

        if (!string.IsNullOrEmpty(data.WorldDefinitionId) &&
            data.WorldDefinitionId != (activeWorldDefinitionId ?? string.Empty))
        {
            reason = "другая авторская карта ('" + data.WorldDefinitionId + "')";
            return false;
        }

        if (!string.IsNullOrEmpty(data.WorldDefinitionId) &&
            data.GeographyVersion > 0 && activeGeographyVersion > 0 &&
            data.GeographyVersion != activeGeographyVersion)
        {
            reason = "карта изменилась (география " + data.GeographyVersion + ", сейчас " + activeGeographyVersion + ")";
            return false;
        }

        return true;
    }

    // Возвращает State из data после исправления null-ности и восстановления
    // скрытых RuntimeState часов/построек. Не создаёт новую партию и не
    // вызывает WorldMapPopulationService — Populate запускается только при
    // CreateNewGame (раздел 15 инструкции: "CreateNewGame и PopulationService
    // при этом не вызываются").
    public static GameState RestoreCampaign(CampaignSaveData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (data.State == null)
            throw new ArgumentException("В сохранении отсутствует State.", nameof(data));

        GameState state = data.State;

        if (!data.HasActiveExpedition)
        {
            state.ActiveExpedition = null;
        }
        else if (state.ActiveExpedition != null)
        {
            if (!data.HasActiveActivity)
                state.ActiveExpedition.ActiveActivity = null;
            if (!data.HasPendingDecision)
                state.ActiveExpedition.PendingDecision = null;
        }

        ContinuousSimulationSystem.RestoreSnapshot(state, data.ClockSnapshot);
        BuildingSystem.RestoreSnapshot(state, data.BuildingSnapshot);

        // ПР-06А: население — производное, пересчитывается из людей.
        HomePeopleService.RecountPopulation(state);

        return state;
    }
}
