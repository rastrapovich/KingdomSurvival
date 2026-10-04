using System;
using System.Collections.Generic;

// ПР-12К (канон v1.53 §28.3): исследуемое место — команды и правила без
// Unity. Геометрия (проходимость, путь, расстановка) считается слоем поля
// боя; здесь — что изменилось в месте и как бой на месте возвращает итог.
//
// Рабочее правило противников после отхода [РАБОЧЕЕ]: выжившие звери
// возвращаются в логово (свою авторскую клетку), раны остаются; убитые не
// появляются снова. Исцеление — только по отдельному правилу сценария.
public static class LocalExplorationService
{
    public static LocalExplorationStateData Data(GameState state)
    {
        if (state == null)
            return null;
        if (state.LocalExploration == null)
            state.LocalExploration = new LocalExplorationStateData();
        return state.LocalExploration;
    }

    public static bool IsActive(GameState state)
    {
        LocalExplorationStateData data = state?.LocalExploration;
        return data != null && data.IsActive && !string.IsNullOrEmpty(data.ActiveLocalLocationId);
    }

    public static LocalLocationDefinition ActiveDefinition(GameState state)
    {
        return IsActive(state) ? LocalLocationCatalog.Find(state.LocalExploration.ActiveLocalLocationId) : null;
    }

    public static LocalLocationStateData GetOrCreate(GameState state, string localLocationId)
    {
        LocalExplorationStateData data = Data(state);
        if (data == null || string.IsNullOrEmpty(localLocationId))
            return null;
        LocalLocationStateData location = data.Locations.Find(item => item != null && item.LocalLocationId == localLocationId);
        if (location == null)
        {
            location = new LocalLocationStateData { LocalLocationId = localLocationId };
            data.Locations.Add(location);
        }
        return location;
    }

    public static LocalLocationStateData Find(GameState state, string localLocationId)
    {
        return state?.LocalExploration?.Locations?.Find(item => item != null && item.LocalLocationId == localLocationId);
    }

    // ------------------------------------------------------------------
    // Вход и выход
    // ------------------------------------------------------------------

    // Можно ли сейчас войти: отряд стоит у этого места, ничем не занят.
    public static bool CanEnter(GameState state, LocalLocationDefinition definition, out string reason)
    {
        reason = string.Empty;
        if (state == null || definition == null)
        {
            reason = "Места нет.";
            return false;
        }
        if (!state.HasActiveExpedition)
        {
            reason = "Войти можно только в походе.";
            return false;
        }
        ExpeditionData expedition = state.ActiveExpedition;
        if (expedition.Phase != CommanderState.AtLocation || expedition.LocationId != definition.WorldLocationId)
        {
            reason = "Сначала дойдите до входа.";
            return false;
        }
        if (expedition.HasTimedActivity || state.HasPendingExpeditionDecision)
        {
            reason = "Отряд занят.";
            return false;
        }
        if (definition.Entrances.Count == 0)
        {
            reason = "У места нет входа.";
            return false;
        }
        return true;
    }

    public static bool Enter(GameState state, LocalLocationDefinition definition, string entranceId, out string reason)
    {
        if (!CanEnter(state, definition, out reason))
            return false;

        LocalExplorationStateData data = Data(state);
        LocalEntranceDefinition entrance = definition.FindEntrance(entranceId);
        data.IsActive = true;
        data.ActiveLocalLocationId = definition.Id;
        data.EntranceId = entrance != null ? entrance.Id : string.Empty;
        data.Party.Clear();
        data.PendingNotice = string.Empty;

        LocalLocationStateData location = GetOrCreate(state, definition.Id);
        location.Visited = true;
        EnsureEnemies(state, definition);

        // Отряд входит у входа; точные клетки без наложения расставляет слой
        // поля (NormalizeParty), здесь — только точка сбора.
        foreach (string personId in PartyPresence.PresentIds(state))
        {
            data.Party.Add(new LocalActorStateData
            {
                ActorId = personId,
                Q = entrance != null ? entrance.Cell.Q : 0,
                R = entrance != null ? entrance.Cell.R : 0
            });
        }
        return true;
    }

    public static void Exit(GameState state)
    {
        LocalExplorationStateData data = Data(state);
        if (data == null)
            return;
        data.IsActive = false;
        data.ActiveLocalLocationId = string.Empty;
        data.EntranceId = string.Empty;
        data.Party.Clear();
    }

    // После загрузки: место, которого больше нет, или отряд не у его входа —
    // возвращаемся на глобальную карту у входа, кампания и итоги целы.
    public static void NormalizeAfterLoad(GameState state)
    {
        LocalExplorationStateData data = Data(state);
        if (data == null || !data.IsActive)
            return;
        LocalLocationDefinition definition = LocalLocationCatalog.Find(data.ActiveLocalLocationId);
        bool atEntrance = definition != null && state.HasActiveExpedition &&
                          state.ActiveExpedition.Phase == CommanderState.AtLocation &&
                          state.ActiveExpedition.LocationId == definition.WorldLocationId;
        if (definition == null || !atEntrance)
        {
            string name = definition != null ? definition.DisplayName : "исследуемое место";
            Exit(state);
            data.PendingNotice = "Место «" + name + "» сейчас недоступно — отряд снаружи, у входа.";
            return;
        }

        // Погибших и ушедших из похода в месте нет.
        List<string> present = PartyPresence.PresentIds(state);
        data.Party.RemoveAll(actor => actor == null || !present.Contains(actor.ActorId));
        foreach (string personId in present)
        {
            if (!data.Party.Exists(actor => actor.ActorId == personId))
            {
                LocalActorStateData hero = data.Party.Count > 0 ? data.Party[0] : null;
                data.Party.Add(new LocalActorStateData { ActorId = personId, Q = hero?.Q ?? 0, R = hero?.R ?? 0 });
            }
        }
        EnsureEnemies(state, definition);
    }

    public static void StorePartyPosition(GameState state, string personId, int q, int r, int facing)
    {
        LocalExplorationStateData data = Data(state);
        if (data == null || !data.IsActive || string.IsNullOrEmpty(personId))
            return;
        LocalActorStateData actor = data.Party.Find(item => item.ActorId == personId);
        if (actor == null)
        {
            actor = new LocalActorStateData { ActorId = personId };
            data.Party.Add(actor);
        }
        actor.Q = q;
        actor.R = r;
        actor.Facing = facing;
    }

    // ------------------------------------------------------------------
    // Противники, объекты, столкновения
    // ------------------------------------------------------------------

    // Противники места заводятся при первом входе по авторским клеткам.
    // Исчерпанное историей столкновение (флаг) — его противников нет:
    // старая партия, очистившая логово до локальной карты, не встретит их.
    public static void EnsureEnemies(GameState state, LocalLocationDefinition definition)
    {
        LocalLocationStateData location = GetOrCreate(state, definition?.Id);
        if (location == null)
            return;
        foreach (LocalEnemyDefinition enemy in definition.Enemies)
        {
            if (enemy == null || string.IsNullOrEmpty(enemy.InstanceId))
                continue;
            LocalActorStateData actor = location.Enemies.Find(item => item.ActorId == enemy.InstanceId);
            if (actor == null)
            {
                actor = new LocalActorStateData { ActorId = enemy.InstanceId, Q = enemy.Cell.Q, R = enemy.Cell.R };
                location.Enemies.Add(actor);
            }
            LocalEncounterDefinition encounter = definition.FindEncounter(enemy.EncounterId);
            if (encounter != null && HasFlag(state, encounter.ResolvedFlag) && !actor.Defeated)
                actor.Defeated = true;
        }
    }

    public static List<LocalActorStateData> AliveEnemies(GameState state, LocalLocationDefinition definition, string encounterId = null)
    {
        List<LocalActorStateData> alive = new List<LocalActorStateData>();
        LocalLocationStateData location = Find(state, definition?.Id);
        if (location == null)
            return alive;
        foreach (LocalActorStateData actor in location.Enemies)
        {
            LocalEnemyDefinition enemy = definition.FindEnemy(actor.ActorId);
            if (actor.Defeated || enemy == null)
                continue;
            if (!string.IsNullOrEmpty(encounterId) && enemy.EncounterId != encounterId)
                continue;
            alive.Add(actor);
        }
        return alive;
    }

    public static bool IsEncounterActive(GameState state, LocalLocationDefinition definition, LocalEncounterDefinition encounter)
    {
        if (state == null || definition == null || encounter == null)
            return false;
        if (HasFlag(state, encounter.ResolvedFlag))
            return false;
        LocalLocationStateData location = Find(state, definition.Id);
        if (location != null && location.ResolvedEncounterIds.Contains(encounter.Id))
            return false;
        return AliveEnemies(state, definition, encounter.Id).Count > 0;
    }

    // Столкновение, которое начинает шаг командира в эту клетку; null — нет.
    public static LocalEncounterDefinition EncounterAt(GameState state, LocalLocationDefinition definition, int q, int r)
    {
        if (definition == null)
            return null;
        foreach (LocalEncounterDefinition encounter in definition.Encounters)
        {
            if (encounter != null && encounter.TriggerCells.Exists(cell => cell.Is(q, r)) &&
                IsEncounterActive(state, definition, encounter))
                return encounter;
        }
        return null;
    }

    public static bool IsObjectVisible(GameState state, LocalObjectDefinition item)
    {
        if (item == null)
            return false;
        if (!string.IsNullOrEmpty(item.RequiresFlag) && !HasFlag(state, item.RequiresFlag))
            return false;
        return string.IsNullOrEmpty(item.HiddenWhenFlag) || !HasFlag(state, item.HiddenWhenFlag);
    }

    public static bool IsObjectDone(GameState state, LocalLocationDefinition definition, LocalObjectDefinition item)
    {
        LocalLocationStateData location = Find(state, definition?.Id);
        return item != null && item.OnceOnly && location != null && location.DoneInteractionIds.Contains(item.Id);
    }

    public static bool IsObjectAvailable(GameState state, LocalLocationDefinition definition, LocalObjectDefinition item)
    {
        return IsObjectVisible(state, item) && !IsObjectDone(state, definition, item);
    }

    // Действие объекта состоялось: однократный отмечается сразу, до показа
    // сцены, — повторный клик и загрузка его не повторят.
    public static bool MarkInteraction(GameState state, LocalLocationDefinition definition, LocalObjectDefinition item)
    {
        LocalLocationStateData location = GetOrCreate(state, definition?.Id);
        if (location == null || item == null || !IsObjectAvailable(state, definition, item))
            return false;
        if (item.OnceOnly)
            location.DoneInteractionIds.Add(item.Id);
        return true;
    }

    // ------------------------------------------------------------------
    // Бой на месте
    // ------------------------------------------------------------------

    public static string NextBattleId(GameState state, LocalEncounterDefinition encounter)
    {
        int attempt = 1;
        while (CampaignBattleBridge.IsApplied(state, encounter.BattleIdPrefix + attempt))
            attempt++;
        return encounter.BattleIdPrefix + attempt;
    }

    // Запрос боя на месте. partyCells — клетки присутствующих (расставлены
    // слоем поля без наложения); в бой идут только кандидаты присутствия.
    public static CampaignBattleRequest BuildEncounterRequest(
        GameState state,
        LocalLocationDefinition definition,
        LocalEncounterDefinition encounter,
        IReadOnlyDictionary<string, LocalCellData> partyCells)
    {
        if (state == null || definition == null || encounter == null)
            throw new ArgumentNullException(nameof(encounter));

        List<string> candidates = PartyPresence.BattleCandidateIds(state);
        CampaignBattleRequest request = CampaignBattleBridge.CreateRequestFor(
            state, NextBattleId(state, encounter), candidates, CampaignBattleSourceKind.Local);
        request.SourceId = encounter.Id;
        request.EncounterId = encounter.Id;
        request.LocalLocationId = definition.Id;
        request.BattlefieldId = definition.BattlefieldId;
        request.AllowRetreat = encounter.AllowRetreat;
        foreach (LocalCellData cell in definition.DifficultCells)
            request.DifficultCells.Add(new CampaignBattleCell { Q = cell.Q, R = cell.R });
        foreach (LocalObjectDefinition item in definition.Objects)
        {
            if (item?.Cell != null)
                request.BlockedCells.Add(new CampaignBattleCell { Q = item.Cell.Q, R = item.Cell.R });
        }

        foreach (CampaignBattleParticipant participant in request.Participants)
        {
            if (partyCells != null && partyCells.TryGetValue(participant.PersonId, out LocalCellData cell) && cell != null)
            {
                participant.HasCell = true;
                participant.CellQ = cell.Q;
                participant.CellR = cell.R;
            }
        }

        foreach (LocalActorStateData actor in AliveEnemies(state, definition, encounter.Id))
        {
            LocalEnemyDefinition enemy = definition.FindEnemy(actor.ActorId);
            request.Enemies.Add(new CampaignBattleEnemy
            {
                UnitTypeId = enemy.UnitTypeId,
                Level = Math.Max(1, enemy.Level),
                Count = 1,
                InstanceId = enemy.InstanceId,
                CurrentHitPoints = actor.HitPoints,
                HasCell = true,
                CellQ = actor.Q,
                CellR = actor.R
            });
        }

        // Подготовленное начало: спутник, способный заметить угрозу заранее,
        // должен быть здесь, а не числиться в походе.
        request.PreparedStart = !string.IsNullOrEmpty(encounter.PreparedStartCompanionId) &&
                                PartyPresence.IsPresent(state, encounter.PreparedStartCompanionId);
        FeatureCombatBatch.ApplyPreparedStart(state, request);
        return request;
    }

    // Итог боя на месте — к месту, один раз на бой. Вызывается из
    // CampaignBattleBridge.ApplyResult внутри того же однократного применения.
    public static void ApplyBattle(GameState state, CampaignBattleResult result)
    {
        if (state == null || result == null || result.SourceKind != CampaignBattleSourceKind.Local ||
            string.IsNullOrEmpty(result.LocalLocationId))
            return;
        LocalLocationDefinition definition = LocalLocationCatalog.Find(result.LocalLocationId);
        LocalLocationStateData location = GetOrCreate(state, result.LocalLocationId);
        if (location == null || location.AppliedBattleIds.Contains(result.BattleId))
            return;
        location.AppliedBattleIds.Add(result.BattleId);

        bool retreat = result.Outcome == CampaignBattleOutcome.Retreat;
        foreach (CampaignBattleEnemyRecord record in result.Enemies ?? new List<CampaignBattleEnemyRecord>())
        {
            if (record == null || string.IsNullOrEmpty(record.InstanceId))
                continue;
            LocalActorStateData actor = location.Enemies.Find(item => item.ActorId == record.InstanceId);
            if (actor == null)
            {
                actor = new LocalActorStateData { ActorId = record.InstanceId };
                location.Enemies.Add(actor);
            }
            actor.Defeated = record.Defeated;
            actor.HitPoints = record.Defeated ? 0 : Math.Max(1, record.HitPoints);
            LocalEnemyDefinition enemy = definition?.FindEnemy(record.InstanceId);
            if (retreat && enemy != null)
            {
                actor.Q = enemy.Cell.Q;
                actor.R = enemy.Cell.R;
            }
            else if (record.HasCell)
            {
                actor.Q = record.CellQ;
                actor.R = record.CellR;
            }
        }

        if (result.Outcome == CampaignBattleOutcome.Victory && !string.IsNullOrEmpty(result.EncounterId) &&
            !location.ResolvedEncounterIds.Contains(result.EncounterId))
            location.ResolvedEncounterIds.Add(result.EncounterId);

        // Отряд: выжившие — где стояли; при отходе — к безопасной точке
        // (точные клетки без наложения расставит слой поля).
        LocalExplorationStateData data = Data(state);
        if (!data.IsActive || data.ActiveLocalLocationId != result.LocalLocationId)
            return;
        LocalEncounterDefinition encounter = definition?.FindEncounter(result.EncounterId);
        data.Party.RemoveAll(actor => result.FallenPersonIds != null && result.FallenPersonIds.Contains(actor.ActorId));
        foreach (CampaignBattleSurvivor survivor in result.Survivors ?? new List<CampaignBattleSurvivor>())
        {
            LocalActorStateData actor = data.Party.Find(item => item.ActorId == survivor.PersonId);
            if (actor == null)
                continue;
            if (retreat && encounter != null)
            {
                actor.Q = encounter.RetreatCell.Q;
                actor.R = encounter.RetreatCell.R;
            }
            else if (survivor.HasCell)
            {
                actor.Q = survivor.CellQ;
                actor.R = survivor.CellR;
            }
        }
        // Не сражавшиеся (свита, раненые) при отходе уходят вместе со всеми.
        if (retreat && encounter != null)
        {
            foreach (LocalActorStateData actor in data.Party)
            {
                if (result.Survivors == null || !result.Survivors.Exists(survivor => survivor.PersonId == actor.ActorId))
                {
                    actor.Q = encounter.RetreatCell.Q;
                    actor.R = encounter.RetreatCell.R;
                }
            }
        }
    }

    private static bool HasFlag(GameState state, string flag)
    {
        return !string.IsNullOrEmpty(flag) && state?.Narrative != null && state.Narrative.HasFlag(flag);
    }
}
