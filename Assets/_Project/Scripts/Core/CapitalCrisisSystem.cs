using System;

public static class CapitalCrisisSystem
{
    private static readonly Random Random = new Random();
    private static int nextOccurrenceId = 1;

    // Случайным остаётся только момент появления кризиса. Сам бой полностью
    // детерминирован и после выбора доктрины не перебрасывается.
    private const double CrisisChancePerDay = 0.25;

    public static void ResolveAtScheduledCheck(
        GameState state,
        int finishedDay,
        StrategicSimulationResult result)
    {
        if (state == null || result == null || BattleSystem.HasPendingBattle(state))
            return;

        if (Random.NextDouble() >= CrisisChancePerDay)
            return;

        string prepareMessage;
        if (!BattleSystem.TryPrepareCapitalBattle(state, out prepareMessage))
            return;

        string buildingDefenseMessage =
            BuildingBattleIntegration.ApplyCapitalDefenseToPendingBattle(state);

        PendingBattleData pending = BattleSystem.GetPendingBattle(state);
        if (pending == null || pending.Result == null)
            return;

        ExpeditionIncidentOccurrence occurrence =
            new ExpeditionIncidentOccurrence
            {
                // Отрицательные ID не пересекаются с ID походных происшествий.
                // DTO пока общий для уведомлений экспедиции и столицы.
                Id = -nextOccurrenceId++,
                Day = finishedDay,
                Title = "Нападение на городской амбар",
                Description =
                    pending.Context.Description + " " +
                    "Войска экспедиции в расчёт защиты столицы не входят." +
                    (string.IsNullOrWhiteSpace(buildingDefenseMessage)
                        ? string.Empty
                        : " " + buildingDefenseMessage),
                ConsequenceText = BattleSystem.BuildCompactPreview(pending.Result),
                Tone = ExpeditionIncidentTone.Negative
            };

        result.NewExpeditionIncidents.Add(occurrence);
        result.HadNotableOccurrence = true;
        result.Messages.Add(
            "Столица: " + occurrence.Title + ". " + prepareMessage + " " +
            occurrence.ConsequenceText);
    }
}
