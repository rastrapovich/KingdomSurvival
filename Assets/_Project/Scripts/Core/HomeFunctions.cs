using System;
using System.Collections.Generic;

// ПР-06А: две доказательные функции Дома (спецификация §7). Функция
// появляется из присутствия и состояния людей, без ручных назначений:
//   - home.maintenance — Лада; Остафий или Торвин замещают частично;
//   - home.care — Марта; Ульяна замещает частично; затяжной голод
//     останавливает уход, даже если Марта дома.
// Полный исполнитель — «работает», только частичный — «ограничено»,
// никого — «остановлено». Два частичных не складываются в мастера.

public enum HomeFunctionStatus
{
    Working,
    Limited,
    Stopped
}

public readonly struct HomeFunctionReport
{
    public readonly string FunctionId;
    public readonly HomeFunctionStatus Status;
    public readonly string ExecutorId;
    public readonly string ExecutorName;
    public readonly string Reason;

    public HomeFunctionReport(string functionId, HomeFunctionStatus status, string executorId, string executorName, string reason)
    {
        FunctionId = functionId;
        Status = status;
        ExecutorId = executorId;
        ExecutorName = executorName;
        Reason = reason;
    }

    // Доля полной скорости: работает — 1, ограничено — 0.5, остановлено — 0.
    public double Rate => Status == HomeFunctionStatus.Working ? 1.0
        : Status == HomeFunctionStatus.Limited ? 0.5
        : 0.0;
}

public static class HomeFunctionResolver
{
    public const string MaintenanceId = "home.maintenance";
    public const string CareId = "home.care";
    // ПР-07Б: рыбная ловля Тихона — только «работает» или «остановлена»;
    // частичных заместителей нет.
    public const string FishingId = "home.fishing";

    // С какой полуночи подряд без полного дневного расхода уход встаёт.
    public const int CareStopsAfterShortageDays = 2;

    private static readonly string[] MaintenancePartial = { HomePeopleService.OstafiyId, HomePeopleService.TorvinId };
    private static readonly string[] CarePartial = { HomePeopleService.UlyanaId };

    // Прогноз для подготовки похода: как будет работать функция, если
    // перечисленные люди уйдут (сам выбор ещё никого не перемещает).
    public static HomeFunctionReport Forecast(GameState state, string functionId, ICollection<string> leavingIds)
    {
        assumedAway = leavingIds;
        try
        {
            return Resolve(state, functionId);
        }
        finally
        {
            assumedAway = null;
        }
    }

    [ThreadStatic]
    private static ICollection<string> assumedAway;

    private static bool CanWork(GameState state, ResidentState resident)
    {
        return HomePeopleService.CanWorkAtHome(state, resident) &&
               (assumedAway == null || !assumedAway.Contains(resident.PersonId));
    }

    public static HomeFunctionReport Resolve(GameState state, string functionId)
    {
        switch (functionId)
        {
            case MaintenanceId:
                return ResolveBy(state, functionId, HomePeopleService.LadaId, MaintenancePartial,
                    "в Доме не осталось человека, способного вести ремонт");
            case FishingId:
                return ResolveFishing(state, functionId);
            case CareId:
                if (state != null && state.ConsecutiveFoodShortageDays >= CareStopsAfterShortageDays)
                {
                    return new HomeFunctionReport(functionId, HomeFunctionStatus.Stopped, string.Empty, string.Empty,
                        "голод: уход остановлен до появления еды");
                }
                return ResolveBy(state, functionId, HomePeopleService.MartaId, CarePartial,
                    "в Доме некому ухаживать за ранеными");
            default:
                throw new ArgumentException("Неизвестная функция Дома: " + functionId, nameof(functionId));
        }
    }

    // Ловля открыта, когда семья Тихона действительно принята в Дом.
    public static bool IsFishingOpen(GameState state)
    {
        ResidentState fisher = HomePeopleService.Find(state, HomePeopleService.TikhonId);
        return fisher != null && !string.IsNullOrEmpty(fisher.HouseholdId);
    }

    private static HomeFunctionReport ResolveFishing(GameState state, string functionId)
    {
        ResidentState fisher = HomePeopleService.Find(state, HomePeopleService.TikhonId);
        if (fisher == null)
            return new HomeFunctionReport(functionId, HomeFunctionStatus.Stopped, string.Empty, string.Empty, "рыбака в Доме нет");

        if (CanWork(state, fisher) && (!fisher.HasCombatState || fisher.CurrentHitPoints > 0))
            return new HomeFunctionReport(functionId, HomeFunctionStatus.Working, fisher.PersonId, fisher.DisplayName, string.Empty);

        string reason = AbsenceReason(state, fisher);
        if (string.IsNullOrEmpty(reason))
            reason = fisher.DisplayName + " не может работать";
        return new HomeFunctionReport(functionId, HomeFunctionStatus.Stopped, string.Empty, string.Empty, reason);
    }

    private static HomeFunctionReport ResolveBy(GameState state, string functionId, string masterId, string[] partialIds, string stoppedReason)
    {
        ResidentState master = HomePeopleService.Find(state, masterId);
        if (CanWork(state, master))
            return new HomeFunctionReport(functionId, HomeFunctionStatus.Working, master.PersonId, master.DisplayName, string.Empty);

        string masterAbsence = master == null ? string.Empty : AbsenceReason(state, master);
        foreach (string partialId in partialIds)
        {
            ResidentState partial = HomePeopleService.Find(state, partialId);
            if (CanWork(state, partial))
            {
                return new HomeFunctionReport(functionId, HomeFunctionStatus.Limited, partial.PersonId, partial.DisplayName,
                    masterAbsence + " — продолжает " + partial.DisplayName);
            }
        }

        return new HomeFunctionReport(functionId, HomeFunctionStatus.Stopped, string.Empty, string.Empty,
            string.IsNullOrEmpty(masterAbsence) ? stoppedReason : masterAbsence + "; " + stoppedReason);
    }

    public static string AbsenceReason(GameState state, ResidentState resident)
    {
        if (!resident.IsAlive)
            return resident.DisplayName + " погиб(ла)";
        if (resident.Membership != ResidentMembership.HomeMember)
            return resident.DisplayName + " ушёл(ла) из Дома";
        if (HomePeopleService.IsInExpedition(state, resident.PersonId))
            return resident.DisplayName + " в походе";
        if (assumedAway != null && assumedAway.Contains(resident.PersonId))
            return resident.DisplayName + " уйдёт в поход";
        if (resident.Injury == ResidentInjury.Recovering)
            return resident.DisplayName + " ранен(а)";
        return string.Empty;
    }
}

// ПР-06А: то, что в Доме идёт само по игровым часам — домашняя работа и
// уход за ранеными. Вызывается владельцем времени (ContinuousSimulationSystem)
// ровно на прошедшие часы; закрытие UI и сохранение на ход не влияют.
public static class HomeLife
{
    public const string YardDeckWorkId = "home.work.yard_deck";
    public const int YardDeckGoldCost = 6;
    public const double YardDeckRequiredWork = 12.0;

    public const double FullCareCycleHours = 24.0;

    // ПР-07Б (PR07_HOME_SPEC §10): 8 пищи за 24 часа фактической работы.
    public const int FishingFoodPerFullDay = 8;
    public const double FishingFoodPerHour = FishingFoodPerFullDay / 24.0;
    private const double CatchEpsilon = 0.000001;

    public static HomeWorkState FindWork(GameState state, string workId)
    {
        if (state?.People?.Works == null)
            return null;
        foreach (HomeWorkState work in state.People.Works)
        {
            if (work.WorkId == workId)
                return work;
        }
        return null;
    }

    public static bool IsWorkInProgress(HomeWorkState work)
    {
        return work != null && work.Started && !work.Completed;
    }

    // Запуск один раз: повторный вызов золото не списывает.
    public static bool TryStartYardDeck(GameState state, out string message)
    {
        message = string.Empty;
        if (state?.People == null)
        {
            message = "Нет людей Дома.";
            return false;
        }

        // ПР-07А-1: домашние работы начинает только тот, кто дома.
        if (HomePeopleService.HasDeparted(state))
        {
            message = "Нужно вернуться в Дом.";
            return false;
        }

        HomeWorkState existing = FindWork(state, YardDeckWorkId);
        if (existing != null)
        {
            message = existing.Completed ? "Настил уже восстановлен." : "Ремонт настила уже идёт.";
            return false;
        }

        if (state.Gold < YardDeckGoldCost)
        {
            message = "Не хватает золота: нужно " + YardDeckGoldCost + ".";
            return false;
        }

        state.Gold -= YardDeckGoldCost;
        state.People.Works.Add(new HomeWorkState
        {
            WorkId = YardDeckWorkId,
            Started = true,
            RequiredWork = YardDeckRequiredWork
        });
        message = "Начат ремонт хозяйственного настила.";
        return true;
    }

    public static double RemainingWork(HomeWorkState work)
    {
        return work == null ? 0.0 : Math.Max(0.0, work.RequiredWork - work.DoneWork);
    }

    // Есть ли в Доме процесс, ради которого время должно идти само.
    public static bool HasPendingProgress(GameState state)
    {
        if (state?.People == null)
            return false;

        HomeWorkState deck = FindWork(state, YardDeckWorkId);
        if (IsWorkInProgress(deck) &&
            HomeFunctionResolver.Resolve(state, HomeFunctionResolver.MaintenanceId).Status != HomeFunctionStatus.Stopped)
            return true;

        return AnyoneCanBeCaredFor(state);
    }

    private static bool AnyoneCanBeCaredFor(GameState state)
    {
        if (HomeFunctionResolver.Resolve(state, HomeFunctionResolver.CareId).Status == HomeFunctionStatus.Stopped)
            return false;

        foreach (ResidentState resident in HomePeopleService.All(state))
        {
            if (resident.NeedsCare && HomePeopleService.IsHomePresent(state, resident))
                return true;
        }
        return false;
    }

    // Прошло hours игровых часов. Сообщения — в донесения.
    public static void Advance(GameState state, double hours, List<string> messages)
    {
        if (state?.People == null || hours <= 0.0)
            return;

        AdvanceWork(state, hours, messages);
        AdvanceCare(state, hours, messages);
        AdvanceFishing(state, hours);
    }

    // Улов копится за фактически отработанные часы, а не за присутствие
    // рыбака в точке полуночи. Ловля не держит время запущенным
    // (не входит в HasPendingProgress): она идёт вместе с уже идущим временем.
    private static void AdvanceFishing(GameState state, double hours)
    {
        if (!HomeFunctionResolver.IsFishingOpen(state))
            return;
        if (HomeFunctionResolver.Resolve(state, HomeFunctionResolver.FishingId).Status != HomeFunctionStatus.Working)
            return;
        state.People.FishingEarned += hours * FishingFoodPerHour;
    }

    // Полночь: целая часть заработанного улова переходит в запасы Дома,
    // дробный остаток остаётся. Уже заработанное принадлежит Дому, даже если
    // рыбак ушёл или погиб.
    public static int TakeFishingCatch(GameState state)
    {
        if (state?.People == null || state.People.FishingEarned <= 0.0)
            return 0;

        int whole = (int)Math.Floor(state.People.FishingEarned + CatchEpsilon);
        state.People.FishingEarned = Math.Max(0.0, state.People.FishingEarned - whole);
        if (state.People.FishingEarned < CatchEpsilon)
            state.People.FishingEarned = 0.0;
        return whole;
    }

    private static void AdvanceWork(GameState state, double hours, List<string> messages)
    {
        HomeWorkState deck = FindWork(state, YardDeckWorkId);
        if (!IsWorkInProgress(deck))
            return;

        double rate = HomeFunctionResolver.Resolve(state, HomeFunctionResolver.MaintenanceId).Rate;
        if (rate <= 0.0)
            return;

        deck.DoneWork = Math.Min(deck.RequiredWork, deck.DoneWork + hours * rate);
        if (deck.DoneWork >= deck.RequiredWork - 0.000001)
        {
            deck.DoneWork = deck.RequiredWork;
            deck.Completed = true;
            HomeKnowledge.Report(state, messages, "Хозяйственный настил восстановлен: по двору снова ходят напрямую, а не в обход луж.");
            // ПР-08 (§8.1 ТЗ): из остатков настила Лада плетёт верёвку с крючьями.
            if (ItemService.GrantOnce(state, "pr08.grant.rope", ItemCatalog.RopeWithHooks, string.Empty) != null)
                HomeKnowledge.Report(state, messages, "Лада сплела из остатков настила верёвку с крючьями — она в кладовой.");
        }
    }

    private static void AdvanceCare(GameState state, double hours, List<string> messages)
    {
        double rate = HomeFunctionResolver.Resolve(state, HomeFunctionResolver.CareId).Rate;
        if (rate <= 0.0)
            return;

        double progressPerHour = rate / FullCareCycleHours;
        foreach (ResidentState resident in HomePeopleService.All(state))
        {
            if (!resident.NeedsCare || !HomePeopleService.IsHomePresent(state, resident))
                continue;

            resident.RecoveryProgress = Math.Min(1.0, resident.RecoveryProgress + hours * progressPerHour);
            if (resident.HasCombatState)
            {
                int missing = resident.MaxHitPoints - resident.RecoveryBaseHitPoints;
                resident.CurrentHitPoints = resident.RecoveryBaseHitPoints +
                                            (int)Math.Floor(missing * resident.RecoveryProgress);
            }

            if (resident.RecoveryProgress >= 1.0)
            {
                if (resident.HasCombatState)
                    resident.CurrentHitPoints = resident.MaxHitPoints;
                resident.RecoveryBaseHitPoints = resident.CurrentHitPoints;
                resident.RecoveryProgress = 0.0;
                bool wasRecovering = resident.Injury == ResidentInjury.Recovering;
                resident.Injury = ResidentInjury.None;
                HomeKnowledge.Report(state, messages, resident.DisplayName + (wasRecovering ? ": рана зажила, снова может работать." : ": силы восстановлены."));
            }
        }
    }
}
