using System;
using System.Collections.Generic;

// 12Т (дополнение «Доска объявлений» §4): поручение — одно задание с общим
// состоянием во флагах NarrativeState. Принять его можно на доске, в корчме
// или в разговоре (эффект SetFlag того же AcceptedFlag) — все источники видят
// одно и то же. Здесь — только то, чего во флагах нет: когда принято (для
// срока), записи Хроники и «Дело» журнала. Шаги и награды делают диалоги;
// новой системы квестов нет.
public enum ErrandStatus
{
    NotTaken,
    Active,
    Completed,
    Failed
}

public static class ErrandService
{
    private static bool Has(GameState state, string flag) =>
        !string.IsNullOrEmpty(flag) && state?.Narrative != null && state.Narrative.HasFlag(flag);

    public static ErrandStatus Status(GameState state, ErrandDefinition errand)
    {
        if (errand == null || state == null)
            return ErrandStatus.NotTaken;
        if (Has(state, errand.CompletedFlag))
            return ErrandStatus.Completed;
        ErrandStateData record = Find(state, errand.Id);
        if (Has(state, errand.FailedFlag) || (record != null && record.Failed))
            return ErrandStatus.Failed;
        return Has(state, errand.AcceptedFlag) ? ErrandStatus.Active : ErrandStatus.NotTaken;
    }

    public static string StatusLabel(ErrandStatus status)
    {
        switch (status)
        {
            case ErrandStatus.Active: return "принято";
            case ErrandStatus.Completed: return "выполнено";
            case ErrandStatus.Failed: return "провалено";
            default: return "не взято";
        }
    }

    public static ErrandStateData Find(GameState state, string errandId)
    {
        if (state?.Settlements?.Errands == null)
            return null;
        return state.Settlements.Errands.Find(e => e != null && e.ErrandId == errandId);
    }

    // Срок словами — его показывают до принятия.
    public static string DescribeLimit(ErrandDefinition errand)
    {
        if (errand == null || errand.CompletionLimitHours <= 0)
            return string.Empty;
        double days = errand.CompletionLimitHours / 24.0;
        return days >= 1.0 && Math.Abs(days - Math.Round(days)) < 0.01
            ? "Срок выполнения: " + Math.Round(days) + " дн."
            : "Срок выполнения: " + Math.Round(errand.CompletionLimitHours) + " ч.";
    }

    // Принять поручение (доска, корчма). Повторно — ничего не меняет.
    public static bool TryAccept(GameState state, string errandId, string source, out string message)
    {
        ErrandDefinition errand = SettlementCatalog.Current.FindErrand(errandId);
        if (errand == null || string.IsNullOrEmpty(errand.AcceptedFlag))
        {
            message = "Такого поручения нет.";
            return false;
        }
        ErrandStatus status = Status(state, errand);
        if (status != ErrandStatus.NotTaken)
        {
            message = "Поручение «" + errand.Title + "» уже " + StatusLabel(status) + ".";
            return false;
        }
        state.Narrative ??= new NarrativeStateData();
        state.Narrative.SetFlag(errand.AcceptedFlag);
        RecordAccepted(state, errand, source);
        message = "Поручение принято: " + errand.Title + "." +
                  (errand.CompletionLimitHours > 0 ? " " + DescribeLimit(errand) : string.Empty);
        return true;
    }

    private static void RecordAccepted(GameState state, ErrandDefinition errand, string source)
    {
        SettlementService.EnsureState(state);
        if (Find(state, errand.Id) == null)
        {
            state.Settlements.Errands.Add(new ErrandStateData
            {
                ErrandId = errand.Id,
                AcceptedHour = ContinuousSimulationSystem.GetAbsoluteHours(state)
            });
        }
        string back = ErrandClaims.DescribeReturn(state, errand);
        Chronicle.Record(state, "errand." + errand.Id + ".accepted", "Поручение: " + errand.Title,
            (string.IsNullOrWhiteSpace(errand.Summary) ? errand.Title : errand.Summary) +
            (string.IsNullOrWhiteSpace(source) ? string.Empty : " (" + source + ")") +
            (string.IsNullOrEmpty(back) ? string.Empty : " " + back));
    }

    // Опрос: поручение, принятое в разговоре, получает отметку принятия;
    // вышедший срок — провал; выполнение — одна запись Хроники.
    public static List<string> Refresh(GameState state)
    {
        List<string> reports = new List<string>();
        if (state?.Narrative == null)
            return reports;
        double now = ContinuousSimulationSystem.GetAbsoluteHours(state);
        foreach (ErrandDefinition errand in SettlementCatalog.Current.Errands)
        {
            if (!Has(state, errand.AcceptedFlag) && !Has(state, errand.CompletedFlag))
                continue;
            if (Find(state, errand.Id) == null)
                RecordAccepted(state, errand, "из разговора");
            ErrandStateData record = Find(state, errand.Id);

            if (Has(state, errand.CompletedFlag))
            {
                if (!record.Finished)
                {
                    record.Finished = true;
                    string text = string.IsNullOrWhiteSpace(errand.CompletedText) ? "Поручение выполнено." : errand.CompletedText;
                    // Бриф исправлений п. 6: фактически полученная награда этого прохождения.
                    string reward = RewardFeed.DescribeErrandReward(state, errand.Id);
                    if (!string.IsNullOrEmpty(reward))
                        text += " " + reward;
                    if (Chronicle.Record(state, "errand." + errand.Id + ".done", "Выполнено: " + errand.Title, text, null,
                            "errand." + errand.Id + ".accepted"))
                        reports.Add(text);
                }
                continue;
            }

            if (!record.Failed && errand.CompletionLimitHours > 0 && now > record.AcceptedHour + errand.CompletionLimitHours)
            {
                record.Failed = true;
                if (!string.IsNullOrEmpty(errand.FailedFlag))
                    state.Narrative.SetFlag(errand.FailedFlag);
                string text = "Срок поручения «" + errand.Title + "» вышел.";
                Chronicle.Record(state, "errand." + errand.Id + ".failed", "Не успели: " + errand.Title, text, null,
                    "errand." + errand.Id + ".accepted");
                reports.Add(text);
            }
        }
        return reports;
    }

    // «Дела» журнала по принятым поручениям.
    public static List<JournalGoalViewData> BuildGoals(GameState state)
    {
        List<JournalGoalViewData> goals = new List<JournalGoalViewData>();
        if (state?.Narrative == null)
            return goals;
        double now = ContinuousSimulationSystem.GetAbsoluteHours(state);
        foreach (ErrandDefinition errand in SettlementCatalog.Current.Errands)
        {
            ErrandStatus status = Status(state, errand);
            if (status == ErrandStatus.NotTaken)
                continue;

            string step = string.IsNullOrWhiteSpace(errand.AcceptedStepText) ? errand.Summary : errand.AcceptedStepText;
            string stage = "accepted";
            if (errand.Stages != null)
            {
                for (int i = 0; i < errand.Stages.Count; i++)
                {
                    ErrandStageDefinition s = errand.Stages[i];
                    if (s != null && Has(state, s.Flag))
                    {
                        step = s.Text;
                        stage = "stage" + i;
                    }
                }
            }
            if (status == ErrandStatus.Completed)
            {
                step = string.IsNullOrWhiteSpace(errand.CompletedText) ? "Выполнено." : errand.CompletedText;
                string reward = RewardFeed.DescribeErrandReward(state, errand.Id);
                if (!string.IsNullOrEmpty(reward))
                    step += " " + reward;
                stage = "done";
            }
            else if (status == ErrandStatus.Failed)
            {
                step = "Срок вышел — поручение не выполнено.";
                stage = "failed";
            }
            else
            {
                // Бриф правок п. 21: к кому и куда вернуться.
                string back = ErrandClaims.DescribeReturn(state, errand);
                if (!string.IsNullOrEmpty(back))
                    step += " " + back;
            }
            if (status == ErrandStatus.Active && errand.CompletionLimitHours > 0)
            {
                ErrandStateData record = Find(state, errand.Id);
                if (record != null)
                {
                    double left = record.AcceptedHour + errand.CompletionLimitHours - now;
                    step += " Осталось: " + Math.Max(0, (int)Math.Ceiling(left / 24.0)) + " дн.";
                }
            }

            goals.Add(new JournalGoalViewData
            {
                Id = "errand." + errand.Id,
                Title = errand.Title,
                Description = errand.Summary + (string.IsNullOrWhiteSpace(errand.RewardText) ? string.Empty : " Награда: " + errand.RewardText + "."),
                CurrentStep = step,
                RevisionId = "errand." + errand.Id + "." + stage,
                Category = JournalGoalCategory.Optional,
                State = status == ErrandStatus.Completed ? JournalGoalState.Completed
                    : status == ErrandStatus.Failed ? JournalGoalState.Failed
                    : JournalGoalState.Active
            });
        }
        return goals;
    }
}
