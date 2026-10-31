using System;
using System.Collections.Generic;

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
    // 3 — канон v1.59: кампания «Дом на чужой воде» и модель исходного Дома
    // удалены. Свободная игра формата 2 преобразуется при загрузке
    // (ConvertFromFormat2); сохранение удалённой кампании не загружается.
    public const int CurrentSaveFormatVersion = 3;
    public const int FreePlayHomeSaveFormatVersion = 2;

    // ID удалённой кампании — только чтобы узнать её старые сохранения.
    public const string RemovedCrisisId = "crisis.home_on_foreign_water";

    public const string RemovedCampaignReason =
        "это сохранение удалённой сюжетной кампании «Дом на чужой воде» — начните новую свободную игру";

    public static CampaignSaveData ExportCampaign(
        GameState state,
        string worldDefinitionId = null,
        int geographyVersion = 0)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        // Канон v1.58 §9.9: сгенерированная карта едет в самом сохранении и
        // не сверяется с авторским миром базы.
        if (state.HasGeneratedMap)
        {
            worldDefinitionId = GeneratedWorldMap.WorldDefinitionId;
            geographyVersion = state.GeneratedMap.Version;
        }

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
            HasSettlementCamp = hasExpedition && SettlementCampService.HasVisit(state),
            ClockSnapshot = ContinuousSimulationSystem.ExportSnapshot(state)
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

        if (data.SaveFormatVersion > CurrentSaveFormatVersion)
        {
            reason = "формат сохранения " + data.SaveFormatVersion + " новее этой версии игры";
            return false;
        }

        if (data.SaveFormatVersion < FreePlayHomeSaveFormatVersion)
        {
            reason = "сохранение сделано до появления людей отряда (формат " + data.SaveFormatVersion + ") — начните новую игру";
            return false;
        }

        if (IsRemovedCampaign(data))
        {
            reason = RemovedCampaignReason;
            return false;
        }

        if (data.State.People == null)
        {
            reason = "в сохранении нет людей отряда";
            return false;
        }

        if (data.WorldDefinitionId == GeneratedWorldMap.WorldDefinitionId)
        {
            if (!data.State.HasGeneratedMap)
            {
                reason = "в сохранении нет сгенерированной карты";
                return false;
            }
            return true;
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

        // ПР-12К: лагерь у поселения — только если он был; исследуемые места
        // у сохранений до ПР-12К пусты (отряд на глобальной карте).
        if (!data.HasSettlementCamp || state.ActiveExpedition == null)
            state.SettlementCamp = null;
        if (state.LocalExploration == null)
            state.LocalExploration = new LocalExplorationStateData();
        if (state.ActiveExpedition == null)
            LocalExplorationService.Exit(state);

        ContinuousSimulationSystem.RestoreSnapshot(state, data.ClockSnapshot);

        // Канон v1.59: свободная игра формата 2 — снять остатки исходного Дома.
        if (data.SaveFormatVersion < CurrentSaveFormatVersion)
            ConvertFromFormat2(state);

        // ПР-08: вещи — у старых сохранений выдаётся стартовый набор один раз.
        ItemService.EnsureInventory(state);

        // 12Т: поселения — у сохранений до 12Т состояние заводится пустым,
        // деревни генератора становятся посещаемыми, профили назначаются
        // один раз (детерминированно по seed и ID места). Кошельки и остатки
        // уже начатых поселений не трогаются.
        SettlementService.EnsureState(state);
        SettlementService.SyncLocations(state);

        // ПР-07А-1: у сохранений до подготовки состава её нет.
        if (state.Preparation == null)
            state.Preparation = new ExpeditionPreparationData();

        // Канон v1.48 §27: у сохранений до прогрессии она заводится с нуля —
        // уровни берутся из прежних записей бойцов.
        CharacterProgressionService.EnsureState(state);

        // Канон v1.58 §9.9: поддельная карта на месте null — авторский мир.
        if (!state.HasGeneratedMap)
            state.GeneratedMap = null;

        // Бриф правок (19 пунктов) п. 1: у сохранений до тумана открыта
        // округа текущей точки героя; известные места остаются известными.
        WorldMapFog.EnsureInitialized(state);

        return state;
    }

    // Сохранение удалённой кампании: режим кризиса или партия до ПР-05 без
    // конфигурации (тогда она могла быть только кампанией кризиса).
    public static bool IsRemovedCampaign(CampaignSaveData data)
    {
        CampaignConfiguration configuration = data?.State?.Configuration;
        return configuration == null || string.IsNullOrEmpty(configuration.CrisisId) ||
               configuration.CrisisId == RemovedCrisisId;
    }

    // Явное преобразование свободной игры формата 2. Поля Дома (казна, еда,
    // население, постройки, семьи, работы, улов, новости Дома) JsonUtility
    // уже отбросил при чтении. Здесь — то, что нужно решить по смыслу:
    //   • в отряде остаются Командир, наёмники и люди, которые сейчас в
    //     походе; остальные жители Дома не переносятся (стартовые знакомства
    //     и члены Дома не оживают) — их персонажи есть в CharacterTemplates;
    //   • вещи ушедших жителей снимаются вместе с ними; вещи без владельца
    //     (кладовая Дома, в свободной игре невидимая) переходят герою;
    //     вещи удалённой кампании снимаются;
    //   • подготовленный состав чистится от тех, кого больше нет.
    // Карта, туман, поселения, поручения, флаги, Хроника и прогресс
    // остаются как есть. Идемпотентно.
    public static void ConvertFromFormat2(GameState state)
    {
        if (state?.People?.Residents == null)
            return;

        CommanderData commander = state.GetSelectedCommander();
        string commanderId = commander != null ? commander.Id : PeopleService.CommanderId;
        HashSet<string> keep = new HashSet<string> { commanderId };
        if (state.ActiveExpedition != null)
        {
            foreach (string id in state.ActiveExpedition.FighterIds ?? new List<string>())
                keep.Add(id);
            foreach (string id in state.ActiveExpedition.RetinueIds ?? new List<string>())
                keep.Add(id);
        }
        foreach (ResidentState resident in state.People.Residents)
        {
            if (resident != null && resident.Membership == ResidentMembership.External)
                keep.Add(resident.PersonId);
        }

        state.People.Residents.RemoveAll(resident => resident == null || !keep.Contains(resident.PersonId));
        state.Fighters?.RemoveAll(fighter => fighter == null || !keep.Contains(fighter.Id));

        if (state.Inventory?.Items != null)
        {
            state.Inventory.Items.RemoveAll(item =>
                item == null ||
                Array.IndexOf(ItemCatalog.RemovedItemIds, item.ItemId) >= 0 ||
                (!string.IsNullOrEmpty(item.OwnerPersonId) && !keep.Contains(item.OwnerPersonId)));
            foreach (ItemInstanceData item in state.Inventory.Items)
            {
                if (!string.IsNullOrEmpty(item.OwnerPersonId))
                    continue;
                item.OwnerPersonId = commanderId;
                item.Slot = ItemSlot.None;
            }
        }
        state.Narrative?.Items?.RemoveAll(itemId => Array.IndexOf(ItemCatalog.RemovedItemIds, itemId) >= 0);

        if (state.Preparation != null)
        {
            state.Preparation.FighterIds?.RemoveAll(id => !keep.Contains(id));
            if (!string.IsNullOrEmpty(state.Preparation.RetinueId) && !keep.Contains(state.Preparation.RetinueId))
                state.Preparation.RetinueId = string.Empty;
        }
    }
}
