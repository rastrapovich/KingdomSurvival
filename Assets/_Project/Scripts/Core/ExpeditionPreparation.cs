using System;
using System.Collections.Generic;

// ПР-07А-1: подготовленный состав похода — одно сохраняемое состояние,
// которое читают Дом, экран героя и карта (спецификация PR07 §8.1).
// Упорядоченные PersonId бойцов (0..4 места, порядок задаёт игрок) и
// необязательный специалист свиты. Подготовка ничего не перемещает: люди
// дома и работают, пока отряд не тронулся.
[Serializable]
public sealed class ExpeditionPreparationData
{
    public List<string> FighterIds = new List<string>();
    public string RetinueId = string.Empty;
}

// Все изменения подготовки — атомарные команды: либо действие применено
// целиком, либо возвращена причина и состав не тронут. Проверки здесь, а
// не в кнопках: UI лишь вызывает команды.
public static class ExpeditionPreparation
{
    public static int FighterSlots => GameState.ExpeditionFighterSlots;

    public static ExpeditionPreparationData Get(GameState state)
    {
        if (state.Preparation == null)
            state.Preparation = new ExpeditionPreparationData();
        if (state.Preparation.FighterIds == null)
            state.Preparation.FighterIds = new List<string>();
        if (state.Preparation.RetinueId == null)
            state.Preparation.RetinueId = string.Empty;
        return state.Preparation;
    }

    // Бойцы в порядке мест. Пока поход существует, источник — сам поход
    // (в том числе у старых сохранений без подготовки); без похода —
    // последний подготовленный состав. Погибшие и ушедшие из Дома выпадают
    // сами — «Погибшие не воскресают» (§14 A14); раненые остаются с причиной.
    public static IReadOnlyList<string> GetFighterIds(GameState state)
    {
        if (state == null)
            return new List<string>();
        if (state.HasActiveExpedition)
            return state.ActiveExpedition.FighterIds;
        Prune(state);
        return Get(state).FighterIds;
    }

    public static string GetRetinueId(GameState state)
    {
        if (state == null)
            return null;
        if (state.HasActiveExpedition)
        {
            List<string> retinue = state.ActiveExpedition.RetinueIds;
            return retinue != null && retinue.Count > 0 ? retinue[0] : null;
        }
        Prune(state);
        string id = Get(state).RetinueId;
        return string.IsNullOrEmpty(id) ? null : id;
    }

    // Поход создан или изменён в обход команд подготовки — подготовка
    // запоминает его состав как основу следующего выхода.
    public static void RememberExpeditionRoster(GameState state)
    {
        if (state == null || !state.HasActiveExpedition)
            return;
        ExpeditionPreparationData data = Get(state);
        data.FighterIds.Clear();
        data.FighterIds.AddRange(state.ActiveExpedition.FighterIds);
        List<string> retinue = state.ActiveExpedition.RetinueIds;
        data.RetinueId = retinue != null && retinue.Count > 0 ? retinue[0] : string.Empty;
    }

    public static bool IsPrepared(GameState state, string personId)
    {
        if (state == null || string.IsNullOrEmpty(personId))
            return false;
        return new List<string>(GetFighterIds(state)).Contains(personId) || GetRetinueId(state) == personId;
    }

    // Состав можно менять, пока отряд не тронулся: герой дома (похода нет)
    // или поход создан, но движение не началось. После первого шага —
    // только просмотр, в том числе на остановке и на обратном пути.
    public static bool CanEdit(GameState state, out string reason)
    {
        reason = string.Empty;
        if (state == null)
        {
            reason = "Нет кампании.";
            return false;
        }

        if (!state.HasActiveExpedition)
            return true;

        if (ContinuousPreparationCommands.CanEditPreparedRoster(state))
            return true;

        reason = "Состав закреплён: поход начался.";
        return false;
    }

    public static bool CanEdit(GameState state)
    {
        return CanEdit(state, out _);
    }

    // ------------------------------------------------------------------
    // Команды
    // ------------------------------------------------------------------

    // Взять бойца: в пустое место (slotIndex < 0 — первое свободное) или
    // атомарно заменить занятого — прежний снимается и остаётся дома.
    public static bool TryPlaceFighter(GameState state, string personId, int slotIndex, out string message)
    {
        if (!CanEdit(state, out message))
            return false;

        List<string> fighters = new List<string>(GetFighterIds(state));
        string retinue = GetRetinueId(state);

        if (fighters.Contains(personId))
        {
            int current = fighters.IndexOf(personId);
            if (slotIndex < 0 || slotIndex == current)
            {
                message = string.Empty;
                return true;
            }
            return TryMoveFighter(state, current, slotIndex, out message);
        }

        if (!CanBeFighter(state, personId, out message))
            return false;

        if (personId == retinue)
        {
            message = "Один человек не может быть и бойцом, и специалистом.";
            return false;
        }

        if (slotIndex < 0 || slotIndex >= fighters.Count)
        {
            if (fighters.Count >= FighterSlots)
            {
                message = "В отряде уже " + FighterSlots + " бойца — выберите, кого заменить.";
                return false;
            }
            fighters.Add(personId);
        }
        else
        {
            fighters[slotIndex] = personId;
        }

        return Commit(state, fighters, retinue, out message);
    }

    public static bool TryAddFighter(GameState state, string personId, out string message)
    {
        return TryPlaceFighter(state, personId, -1, out message);
    }

    // Перестановка: перенести в другое место (или поменять местами).
    // Порядок — только порядок карточек, без боевых правил расстановки.
    public static bool TryMoveFighter(GameState state, int fromIndex, int toIndex, out string message)
    {
        if (!CanEdit(state, out message))
            return false;

        List<string> fighters = new List<string>(GetFighterIds(state));
        if (fromIndex < 0 || fromIndex >= fighters.Count)
        {
            message = "В этом месте никого нет.";
            return false;
        }

        if (toIndex < 0)
            toIndex = 0;
        if (toIndex >= fighters.Count)
            toIndex = fighters.Count - 1;
        if (toIndex == fromIndex)
        {
            message = string.Empty;
            return true;
        }

        string moved = fighters[fromIndex];
        fighters[fromIndex] = fighters[toIndex];
        fighters[toIndex] = moved;
        return Commit(state, fighters, GetRetinueId(state), out message);
    }

    public static bool TrySetRetinue(GameState state, string personId, out string message)
    {
        if (!CanEdit(state, out message))
            return false;

        List<string> fighters = new List<string>(GetFighterIds(state));
        if (string.IsNullOrEmpty(personId))
            return Commit(state, fighters, null, out message);

        ResidentState candidate = HomePeopleService.Find(state, personId);
        if (candidate != null && candidate.TravelRole == ResidentTravelRole.Combatant)
        {
            message = "Это место для специалиста.";
            return false;
        }

        if (fighters.Contains(personId))
        {
            message = "Один человек не может быть и бойцом, и специалистом.";
            return false;
        }

        if (!HomePeopleService.CanJoinAsRetinue(state, personId, out string reason))
        {
            ResidentState resident = HomePeopleService.Find(state, personId);
            message = resident != null && resident.TravelRole == ResidentTravelRole.Combatant
                ? "Это место для специалиста."
                : Capitalize(reason) + ".";
            return false;
        }

        return Commit(state, fighters, personId, out message);
    }

    // «Оставить дома»: снять с подготовки (бойца или специалиста).
    public static bool TryRemove(GameState state, string personId, out string message)
    {
        if (!CanEdit(state, out message))
            return false;

        if (IsCommander(state, personId))
        {
            message = "Командир ведёт этот поход.";
            return false;
        }

        List<string> fighters = new List<string>(GetFighterIds(state));
        string retinue = GetRetinueId(state);
        bool removed = fighters.Remove(personId);
        if (retinue == personId)
        {
            retinue = null;
            removed = true;
        }

        if (!removed)
        {
            message = string.Empty;
            return true;
        }

        return Commit(state, fighters, retinue, out message);
    }

    public static void Clear(GameState state)
    {
        if (state == null)
            return;
        ExpeditionPreparationData data = Get(state);
        data.FighterIds.Clear();
        data.RetinueId = string.Empty;
    }

    // Проверка всего состава перед выходом — одной операцией (§8.4):
    // дубликаты, погибшие, ушедшие, раненые, превышение мест, чужая роль.
    public static bool Validate(GameState state, IList<string> fighterIds, string retinueId, out string message)
    {
        message = string.Empty;
        if (fighterIds == null)
        {
            message = "Не удалось определить состав похода.";
            return false;
        }

        if (fighterIds.Count > FighterSlots)
        {
            message = "В поход можно взять не больше " + FighterSlots +
                      " бойцов. Командир входит в отряд автоматически и занимает отдельное место.";
            return false;
        }

        HashSet<string> seen = new HashSet<string>();
        foreach (string id in fighterIds)
        {
            if (!seen.Add(id ?? string.Empty))
            {
                message = "В составе экспедиции один боец указан несколько раз.";
                return false;
            }
            if (!CanBeFighter(state, id, out message))
                return false;
        }

        if (!string.IsNullOrEmpty(retinueId))
        {
            if (seen.Contains(retinueId))
            {
                message = "Один человек не может быть и бойцом, и специалистом.";
                return false;
            }
            if (!HomePeopleService.CanJoinAsRetinue(state, retinueId, out string reason))
            {
                message = "Нельзя взять в свиту: " + reason + ".";
                return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------------

    private static bool CanBeFighter(GameState state, string personId, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrEmpty(personId))
        {
            message = "Не выбран человек.";
            return false;
        }

        if (IsCommander(state, personId))
        {
            message = "Командир ведёт этот поход.";
            return false;
        }

        ResidentState resident = HomePeopleService.Find(state, personId);
        if (state.FindFighter(personId) == null)
        {
            if (resident == null)
                message = "В составе экспедиции найден неизвестный боец: " + personId + ".";
            else if (!resident.IsAlive)
                message = resident.DisplayName + " погиб(ла).";
            else if (resident.AgeGroup == ResidentAgeGroup.Child)
                message = resident.DisplayName + " — ребёнок, в поход не берут.";
            else if (resident.TravelRole == ResidentTravelRole.Retinue)
                message = "Этот человек не участвует в бою.";
            else
                message = resident.DisplayName + " не идёт в поход бойцом.";
            return false;
        }

        if (resident != null)
        {
            if (!resident.IsHomeMember)
            {
                message = resident.DisplayName + " больше не живёт в Доме.";
                return false;
            }
            if (resident.Injury == ResidentInjury.Recovering)
            {
                message = resident.DisplayName + " ранен(а) и восстанавливается — в поход не пойдёт.";
                return false;
            }
        }

        return true;
    }

    private static bool IsCommander(GameState state, string personId)
    {
        CommanderData commander = state.GetSelectedCommander();
        return commander != null && commander.Id == personId;
    }

    // Применить новый состав. Если поход уже создан, но не тронулся, — тот
    // же состав сразу получает и экспедиция (одно место правды для
    // Дома, героя и карты).
    private static bool Commit(GameState state, List<string> fighters, string retinueId, out string message)
    {
        if (state.HasActiveExpedition)
        {
            if (!Validate(state, fighters, retinueId, out message))
                return false;
            state.ActiveExpedition.FighterIds.Clear();
            state.ActiveExpedition.FighterIds.AddRange(fighters);
            if (state.ActiveExpedition.RetinueIds == null)
                state.ActiveExpedition.RetinueIds = new List<string>();
            state.ActiveExpedition.RetinueIds.Clear();
            if (!string.IsNullOrEmpty(retinueId))
                state.ActiveExpedition.RetinueIds.Add(retinueId);
        }

        ExpeditionPreparationData data = Get(state);
        data.FighterIds.Clear();
        data.FighterIds.AddRange(fighters);
        data.RetinueId = retinueId ?? string.Empty;

        message = "Состав похода: командир" +
                  (fighters.Count > 0 ? " + " + fighters.Count + " " + FighterWord(fighters.Count) : "") +
                  (string.IsNullOrEmpty(retinueId) ? "" : " и специалист") + ".";
        return true;
    }

    // Погибшие и ушедшие из Дома выпадают из подготовки.
    private static void Prune(GameState state)
    {
        ExpeditionPreparationData data = Get(state);
        data.FighterIds.RemoveAll(id =>
        {
            if (state.FindFighter(id) == null)
                return true;
            ResidentState resident = HomePeopleService.Find(state, id);
            return resident != null && !resident.IsHomeMember;
        });

        if (!string.IsNullOrEmpty(data.RetinueId))
        {
            ResidentState resident = HomePeopleService.Find(state, data.RetinueId);
            if (resident == null || !resident.IsHomeMember)
                data.RetinueId = string.Empty;
        }
    }

    private static string FighterWord(int count)
    {
        int lastTwo = count % 100;
        if (lastTwo >= 11 && lastTwo <= 14)
            return "бойцов";
        switch (count % 10)
        {
            case 1: return "боец";
            case 2:
            case 3:
            case 4: return "бойца";
            default: return "бойцов";
        }
    }

    private static string Capitalize(string text)
    {
        return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
