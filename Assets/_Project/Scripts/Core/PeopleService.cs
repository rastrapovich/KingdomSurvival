using System;
using System.Collections.Generic;

// ПР-06А: единый вход к людям партии. Все изменения человека — через этот
// сервис; FighterData/Fighters остаются переходным представлением бойцов.
// Канон v1.59: исходного Дома нет — новая партия начинается с одного
// Командира; бойцы приходят наймом, прежние люди Дома — заготовки
// (CharacterTemplates).
public static class PeopleService
{
    public const string CommanderId = "commander";

    public const int RetinueSlots = 1;

    public static PeopleState CreateDefaults(GameState state)
    {
        PeopleState people = new PeopleState();

        CommanderData commander = state.GetSelectedCommander();
        people.Residents.Add(new ResidentState
        {
            PersonId = commander != null ? commander.Id : CommanderId,
            DisplayName = commander != null ? commander.Name : "Командир",
            RoleLabel = "командир",
            ShortDescription = "Ведёт отряд и отвечает за его решения.",
            TravelRole = ResidentTravelRole.Commander,
            UnitTypeId = CampaignBattleBridge.HeroFallbackUnitTypeId
        });

        foreach (ResidentState resident in people.Residents)
            EnsureCombatState(resident);

        return people;
    }

    // ------------------------------------------------------------------
    // Запросы
    // ------------------------------------------------------------------

    public static ResidentState Find(GameState state, string personId)
    {
        if (state?.People?.Residents == null || string.IsNullOrEmpty(personId))
            return null;

        foreach (ResidentState resident in state.People.Residents)
        {
            if (resident.PersonId == personId)
                return resident;
        }
        return null;
    }

    public static IReadOnlyList<ResidentState> All(GameState state)
    {
        return state?.People?.Residents ?? (IReadOnlyList<ResidentState>)Array.Empty<ResidentState>();
    }

    // Поход начался физически: отряд сдвинулся с точки стоянки.
    public static bool HasDeparted(GameState state)
    {
        return state != null && state.HasActiveExpedition &&
               ContinuousSimulationSystem.HasExpeditionStartedMoving(state);
    }

    public static bool IsInExpeditionParty(GameState state, string personId)
    {
        if (state == null || !state.HasActiveExpedition || string.IsNullOrEmpty(personId))
            return false;

        ExpeditionData expedition = state.ActiveExpedition;
        return personId == expedition.CommanderId ||
               (expedition.FighterIds != null && expedition.FighterIds.Contains(personId)) ||
               (expedition.RetinueIds != null && expedition.RetinueIds.Contains(personId));
    }

    // Физически в походе — в составе и поход уже начался.
    public static bool IsInExpedition(GameState state, string personId)
    {
        return HasDeparted(state) && IsInExpeditionParty(state, personId);
    }

    // ------------------------------------------------------------------
    // Свита
    // ------------------------------------------------------------------

    public static bool CanJoinAsRetinue(GameState state, string personId, out string reason)
    {
        reason = string.Empty;
        ResidentState resident = Find(state, personId);
        if (resident == null)
        {
            reason = "такого человека нет в отряде";
            return false;
        }
        if (!resident.IsAlive)
        {
            reason = resident.DisplayName + " погиб(ла)";
            return false;
        }
        if (resident.Membership != ResidentMembership.Member)
        {
            reason = resident.DisplayName + " больше не с отрядом";
            return false;
        }
        if (resident.TravelRole != ResidentTravelRole.Retinue)
        {
            reason = resident.AgeGroup == ResidentAgeGroup.Child
                ? resident.DisplayName + " — ребёнок"
                : resident.DisplayName + " не идёт в поход специалистом";
            return false;
        }
        if (resident.Injury == ResidentInjury.Recovering)
        {
            reason = resident.DisplayName + " ранен(а) и восстанавливается";
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------
    // Бой и потери
    // ------------------------------------------------------------------

    // Индивидуальные HP из реального шаблона UnitDatabase. Без провайдера
    // (часть тестов) боевое состояние остаётся ненастроенным до боя.
    public static void EnsureCombatState(ResidentState resident)
    {
        if (resident == null || resident.HasCombatState || string.IsNullOrEmpty(resident.UnitTypeId))
            return;

        IUnitStatsProvider provider = GameState.UnitStatsProvider;
        if (provider == null || !provider.TryGetCombatStats(resident.UnitTypeId, out UnitCombatStats stats))
            return;

        resident.MaxHitPoints = Math.Max(1, stats.MaxHitPoints);
        resident.CurrentHitPoints = resident.MaxHitPoints;
        resident.HasCombatState = true;
    }

    public static void SetHitPoints(ResidentState resident, int hitPoints)
    {
        if (resident == null || !resident.HasCombatState)
            return;

        resident.CurrentHitPoints = Math.Max(0, Math.Min(resident.MaxHitPoints, hitPoints));
    }

    // Принять в отряд персонажа-заготовку (CharacterTemplates) как своего
    // человека. Канон v1.59: новая партия этого не делает — вход нужен
    // будущему содержанию (знакомство через реальную встречу) и тестам.
    // Повторный вызов ничего не создаёт.
    public static ResidentState AdmitFromTemplate(GameState state, string templateId)
    {
        CharacterTemplate template = CharacterTemplates.Find(templateId);
        if (state?.People == null || template == null)
            return null;
        ResidentState existing = Find(state, template.Id);
        if (existing != null)
            return existing;

        ResidentState resident = new ResidentState
        {
            PersonId = template.Id,
            DisplayName = template.DisplayName,
            RoleLabel = template.RoleLabel,
            ShortDescription = template.ShortDescription,
            DialogueSpeakerId = template.DialogueSpeakerId ?? string.Empty,
            AgeGroup = template.AgeGroup,
            TravelRole = template.TravelRole,
            UnitTypeId = template.UnitTypeId ?? string.Empty
        };
        EnsureCombatState(resident);
        state.People.Residents.Add(resident);

        if (resident.TravelRole == ResidentTravelRole.Combatant && !string.IsNullOrEmpty(resident.UnitTypeId))
        {
            if (state.Fighters == null)
                state.Fighters = new List<FighterData>();
            string role = string.IsNullOrEmpty(resident.RoleLabel)
                ? "Боец"
                : char.ToUpperInvariant(resident.RoleLabel[0]) + resident.RoleLabel.Substring(1);
            state.Fighters.Add(new FighterData(resident.PersonId, resident.DisplayName, role, 1, 2, resident.UnitTypeId));
        }

        ItemService.GrantKit(state, resident.PersonId, template.KitItemIds);
        CharacterProgressionService.EnsureState(state);
        return resident;
    }

    // Человек погиб: запись остаётся в реестре; из состава и переходных
    // представлений он уходит.
    public static void MarkDead(GameState state, string personId, string reasonId)
    {
        ResidentState resident = Find(state, personId);
        if (resident == null || !resident.IsAlive)
            return;

        resident.LifeStatus = ResidentLifeStatus.Dead;
        resident.DeathReasonId = reasonId ?? string.Empty;
        resident.CurrentHitPoints = 0;
        resident.Exhausted = false;
        ItemService.OnPersonDied(state, personId);

        // ПР-11: гибель вне боя — отдельная запись истории (бой пишет свою).
        if (reasonId == null || !reasonId.StartsWith("battle.", StringComparison.Ordinal))
            Chronicle.Record(state, "death." + personId, "Потеря", resident.DisplayName + " больше нет с нами.");

        if (state.Fighters != null)
            state.Fighters.RemoveAll(fighter => fighter.Id == personId);
        if (state.HasActiveExpedition)
        {
            state.ActiveExpedition.FighterIds?.Remove(personId);
            state.ActiveExpedition.RetinueIds?.Remove(personId);
        }
    }
}
