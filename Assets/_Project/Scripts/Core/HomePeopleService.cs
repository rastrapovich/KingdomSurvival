using System;
using System.Collections.Generic;

// ПР-06А: единый вход к людям Дома. Все изменения человека — через этот
// сервис; FighterData/Fighters и Population остаются переходными
// представлениями и пересобираются отсюда, а не живут своей жизнью.
public static class HomePeopleService
{
    public const string CommanderId = "commander";
    public const string OstafiyId = "ostafiy";
    public const string LadaId = "lada";
    public const string MironId = "miron";
    public const string UlyanaId = "ulyana";
    public const string MartaId = "marta";
    public const string TorvinId = "torvin";
    // ПР-07Б: рыбак семьи Тихона (Chapter01FisherFamily) — исполнитель ловли.
    public const string TikhonId = "newcomer.fisher.tikhon";

    public const int RetinueSlots = 1;

    // ------------------------------------------------------------------
    // Начальный состав новой партии (спецификация §§4–5): 24 живых члена
    // Дома — Командир, 5 бойцов, 4 сюжетных жителя, 14 фоновых в 4 семьях.
    // Описания — рабочие тексты, утверждённые к реализации ПР-06.
    // ------------------------------------------------------------------

    public static HomePeopleState CreateDefaults(GameState state)
    {
        HomePeopleState people = new HomePeopleState();

        CommanderData commander = state.GetSelectedCommander();
        people.Residents.Add(new ResidentState
        {
            PersonId = commander != null ? commander.Id : CommanderId,
            DisplayName = commander != null ? commander.Name : "Командир",
            RoleLabel = "командир",
            ShortDescription = "Тот, на ком теперь решения Дома.",
            TravelRole = ResidentTravelRole.Commander,
            UnitTypeId = CampaignBattleBridge.HeroFallbackUnitTypeId
        });

        if (state.Fighters != null)
        {
            foreach (FighterData fighter in state.Fighters)
            {
                people.Residents.Add(new ResidentState
                {
                    PersonId = fighter.Id,
                    DisplayName = fighter.Name,
                    RoleLabel = fighter.Role.ToLowerInvariant(),
                    ShortDescription = FighterDescription(fighter.Id),
                    TravelRole = ResidentTravelRole.Combatant,
                    UnitTypeId = fighter.UnitTypeId
                });
            }
        }

        people.Residents.Add(Named(OstafiyId, "Остафий", "хранитель старых работ", ResidentAgeGroup.Elder, ResidentTravelRole.Retinue,
            "Помнит, как в Доме делали работы раньше. Может ошибаться в причинах, но не в порядке действий. В пути замечает следы прежних работ."));
        people.Residents.Add(Named(LadaId, "Лада", "мастер по дереву", ResidentAgeGroup.Adult, ResidentTravelRole.Retinue,
            "Работает с деревом, креплениями и настилами. Дома ведёт ремонт; в пути может укрепить опору."));
        people.Residents.Add(Named(MironId, "Мирон", "мельник", ResidentAgeGroup.Adult, ResidentTravelRole.None,
            "Слышит колесо раньше, чем видит. Первым замечает, когда вода ведёт себя иначе."));
        people.Residents.Add(Named(UlyanaId, "Ульяна", "держит быт Дома", ResidentAgeGroup.Adult, ResidentTravelRole.None,
            "Видит цену любого решения через тех, кого потом кормить. Если Марты нет, берёт уход на себя — как умеет."));

        AddBackgroundHousehold(people, "household.home.1", new[] { ResidentAgeGroup.Adult, ResidentAgeGroup.Adult, ResidentAgeGroup.Child, ResidentAgeGroup.Child });
        AddBackgroundHousehold(people, "household.home.2", new[] { ResidentAgeGroup.Adult, ResidentAgeGroup.Adult, ResidentAgeGroup.Elder, ResidentAgeGroup.Child });
        AddBackgroundHousehold(people, "household.home.3", new[] { ResidentAgeGroup.Adult, ResidentAgeGroup.Adult, ResidentAgeGroup.Child });
        AddBackgroundHousehold(people, "household.home.4", new[] { ResidentAgeGroup.Adult, ResidentAgeGroup.Adult, ResidentAgeGroup.Elder });

        foreach (ResidentState resident in people.Residents)
            EnsureCombatState(resident);

        return people;
    }

    private static ResidentState Named(string id, string name, string role, ResidentAgeGroup age, ResidentTravelRole travelRole, string description)
    {
        return new ResidentState
        {
            PersonId = id,
            DisplayName = name,
            RoleLabel = role,
            ShortDescription = description,
            DialogueSpeakerId = id,
            AgeGroup = age,
            TravelRole = travelRole
        };
    }

    private static void AddBackgroundHousehold(HomePeopleState people, string householdId, ResidentAgeGroup[] ages)
    {
        HouseholdState household = new HouseholdState { HouseholdId = householdId, DisplayName = "Другие семьи" };
        foreach (ResidentAgeGroup age in ages)
        {
            int number = CountBackground(people) + 1;
            string personId = "home.background." + number.ToString("00");
            people.Residents.Add(new ResidentState
            {
                PersonId = personId,
                DisplayName = "Житель Дома",
                RoleLabel = age == ResidentAgeGroup.Child ? "ребёнок" : "домочадец",
                HouseholdId = householdId,
                AgeGroup = age
            });
            household.MemberIds.Add(personId);
        }
        people.Households.Add(household);
    }

    private static int CountBackground(HomePeopleState people)
    {
        int count = 0;
        foreach (ResidentState resident in people.Residents)
        {
            if (resident.PersonId.StartsWith("home.background.", StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    // Рабочие характеры бойцов (спецификация §§4.7–4.11), кратко.
    private static string FighterDescription(string fighterId)
    {
        switch (fighterId)
        {
            case "garrick": return "Опытный защитник. Судит решение по тому, кто будет отвечать за его исполнение.";
            case "edric": return "Молодой лучник. Слишком быстро объявляет себя готовым — но в трудную минуту не бросает своих.";
            case "marta": return "Лекарь. Дома ведёт уход за ранеными; в походе — боевой лекарь отряда.";
            case "torvin": return "Копейщик. Не любит чужих решений о своём времени, но простой ремонт сделает по образцу.";
            case "agnessa": return "Разведчица. Сначала смотрит на следы, потом верит рассказам.";
            default: return string.Empty;
        }
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

    // Поход начался физически: отряд сдвинулся с точки Дома.
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

    public static bool IsHomePresent(GameState state, ResidentState resident)
    {
        return resident != null && resident.IsHomeMember && !IsInExpedition(state, resident.PersonId);
    }

    // Может работать дома: дома, жив, без длительного ранения.
    public static bool CanWorkAtHome(GameState state, ResidentState resident)
    {
        return IsHomePresent(state, resident) && resident.Injury == ResidentInjury.None;
    }

    public static int CountHomeMembers(GameState state)
    {
        int count = 0;
        foreach (ResidentState resident in All(state))
        {
            if (resident.IsHomeMember)
                count++;
        }
        return count;
    }

    public static int CountHomePresent(GameState state)
    {
        int count = 0;
        foreach (ResidentState resident in All(state))
        {
            if (IsHomePresent(state, resident))
                count++;
        }
        return count;
    }

    public static int CountExpeditionPresent(GameState state)
    {
        int count = 0;
        foreach (ResidentState resident in All(state))
        {
            if (resident.IsAlive && IsInExpedition(state, resident.PersonId))
                count++;
        }
        return count;
    }

    // Legacy-число «Население» — производное: живые члены Дома.
    public static void RecountPopulation(GameState state)
    {
        if (state?.People != null)
            state.Population = CountHomeMembers(state);
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
            reason = "такого человека нет в Доме";
            return false;
        }
        if (!resident.IsAlive)
        {
            reason = resident.DisplayName + " погиб(ла)";
            return false;
        }
        if (resident.Membership != ResidentMembership.HomeMember)
        {
            reason = resident.DisplayName + " больше не живёт в Доме";
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

    // Новый уровень HP; если человек ранен — начинается новый цикл ухода от
    // текущего значения (прежний прогресс не приписывается новому урону).
    public static void SetHitPoints(ResidentState resident, int hitPoints)
    {
        if (resident == null || !resident.HasCombatState)
            return;

        resident.CurrentHitPoints = Math.Max(0, Math.Min(resident.MaxHitPoints, hitPoints));
        resident.RecoveryBaseHitPoints = resident.CurrentHitPoints;
        resident.RecoveryProgress = 0.0;
    }

    // ПР-06Б: домохозяйство принимается целиком одной операцией. Сначала
    // проверяются все ID, затем добавляются все люди; операция отмечается в
    // NarrativeState и повторно ничего не создаёт (повторный клик,
    // повторное завершение сцены, загрузка). Бойцы семьи получают и
    // переходное представление FighterData — для выбора в поход.
    public static bool AdmitHousehold(
        GameState state,
        string operationId,
        HouseholdState household,
        IList<ResidentState> members,
        out string message)
    {
        message = string.Empty;
        if (state?.People == null || household == null || members == null || string.IsNullOrEmpty(operationId))
        {
            message = "Нельзя принять семью: нет данных.";
            return false;
        }

        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        if (state.Narrative.HasEffectApplied(operationId))
        {
            message = "Семья уже принята.";
            return false;
        }

        HashSet<string> ids = new HashSet<string>();
        foreach (ResidentState member in members)
        {
            if (member == null || string.IsNullOrEmpty(member.PersonId) || !ids.Add(member.PersonId) ||
                Find(state, member.PersonId) != null)
            {
                message = "Нельзя принять семью: противоречие в составе.";
                return false;
            }
        }

        household.MemberIds.Clear();
        foreach (ResidentState member in members)
        {
            member.HouseholdId = household.HouseholdId;
            member.Membership = ResidentMembership.HomeMember;
            member.LifeStatus = ResidentLifeStatus.Alive;
            EnsureCombatState(member);
            state.People.Residents.Add(member);
            household.MemberIds.Add(member.PersonId);

            if (member.TravelRole == ResidentTravelRole.Combatant && !string.IsNullOrEmpty(member.UnitTypeId))
            {
                if (state.Fighters == null)
                    state.Fighters = new List<FighterData>();
                string role = string.IsNullOrEmpty(member.RoleLabel) ? "Боец" : char.ToUpperInvariant(member.RoleLabel[0]) + member.RoleLabel.Substring(1);
                state.Fighters.Add(new FighterData(member.PersonId, member.DisplayName, role, 1, 2, member.UnitTypeId));
            }
        }

        state.People.Households.Add(household);
        state.Narrative.MarkEffectApplied(operationId);
        RecountPopulation(state);
        message = household.DisplayName + " теперь живёт в Доме: " + members.Count + " человек(а).";
        return true;
    }

    // Человек погиб: запись остаётся в реестре и семье; из состава и
    // переходных представлений он уходит.
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

        RecountPopulation(state);
    }
}
