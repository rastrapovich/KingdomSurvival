using System.Collections.Generic;
using System.Text;

namespace KingdomSurvival.FreePlay
{
    // ПР-12Б: контрольный итог свободной игры без завершения партии —
    // «что изменилось в Доме и какие решения приняты». Всегда под рукой в
    // «Делах» («Итог Дома»), а каждые CheckpointReturns походов — запись
    // Хроники и донесение. Итог только читает состояние партии и Хронику.
    public static class FreePlaySummary
    {
        public const string GoalId = "freeplay.goal.summary";
        public const string CheckpointPrefix = "freeplay.summary.";
        public const int CheckpointReturns = 3;

        public static int Returns(GameState state) => FreePlayContent.CountEntries(state, FreePlayContent.ReturnPrefix);

        public static JournalGoalViewData BuildGoal(GameState state)
        {
            int returns = Returns(state);
            if (returns == 0)
                return null;
            return new JournalGoalViewData
            {
                Id = GoalId,
                Title = "Итог Дома",
                Description = Build(state),
                CurrentStep = "Походов: " + returns + ". Партия продолжается — итог меняется вместе с Домом.",
                RevisionId = GoalId + "." + returns,
                Category = JournalGoalCategory.Main,
                State = JournalGoalState.Active
            };
        }

        // Контрольная запись каждые CheckpointReturns возвращений.
        public static List<string> Refresh(GameState state)
        {
            List<string> reports = new List<string>();
            int returns = Returns(state);
            if (returns == 0 || returns % CheckpointReturns != 0 || state.HasActiveExpedition)
                return reports;
            string id = CheckpointPrefix + returns;
            if (Chronicle.Find(state, id) != null)
                return reports;

            string text = Build(state);
            Chronicle.Record(state, id, "Итог Дома: походов " + returns, text);
            reports.Add("Контрольный итог после " + returns + " походов — в «Делах», «Итог Дома».\n" + text);
            return reports;
        }

        public static string Build(GameState state)
        {
            StringBuilder text = new StringBuilder();
            text.Append("День ").Append(state.Day).Append(". Походов: ").Append(Returns(state)).Append('.');

            // Люди.
            int alive = 0;
            List<string> dead = new List<string>();
            List<string> wounded = new List<string>();
            foreach (ResidentState resident in HomePeopleService.All(state))
            {
                if (resident.LifeStatus == ResidentLifeStatus.Dead)
                {
                    dead.Add(resident.DisplayName);
                    continue;
                }
                alive++;
                if (resident.NeedsCare)
                    wounded.Add(resident.DisplayName);
            }
            text.Append("\nЛюди: живых ").Append(alive).Append('.');
            if (wounded.Count > 0)
                text.Append(" Раненых: ").Append(string.Join(", ", wounded)).Append('.');
            if (dead.Count > 0)
                text.Append(" Погибли: ").Append(string.Join(", ", dead)).Append('.');

            // Хозяйство.
            text.Append("\nЗапасы: еда ").Append(state.Food).Append(", припасы ").Append(state.ArmySupply)
                .Append(", золото ").Append(state.Gold).Append('.');
            HomeWorkState deck = HomeLife.FindWork(state, HomeLife.YardDeckWorkId);
            text.Append(" Настил во дворе: ")
                .Append(deck == null ? "не начат" : deck.Completed ? "восстановлен" : "в работе")
                .Append(". Уголь у Лады: ").Append(FreePlayCoalStory.HasCoal(state) ? "есть" : "на исходе").Append('.');

            // Решения и их след — из Хроники (истории режима, без выходов и возвращений).
            List<string> decisions = new List<string>();
            foreach (ChronicleEntryData entry in Chronicle.Get(state).Entries)
            {
                if (entry == null || !entry.Id.StartsWith("freeplay.", System.StringComparison.Ordinal) ||
                    entry.Id.StartsWith(FreePlayContent.DeparturePrefix, System.StringComparison.Ordinal) ||
                    entry.Id.StartsWith(FreePlayContent.ReturnPrefix, System.StringComparison.Ordinal) ||
                    entry.Id.StartsWith(CheckpointPrefix, System.StringComparison.Ordinal))
                    continue;
                decisions.Add("День " + entry.Day + " — " + entry.Title + ": " + entry.Text);
            }
            text.Append("\n\nЧто было:");
            if (decisions.Count == 0)
                text.Append("\nПока ничего такого, о чём в Доме стали бы рассказывать.");
            else
                foreach (string line in decisions)
                    text.Append("\n• ").Append(line);

            // Открытые нити.
            List<string> open = new List<string>();
            foreach (JournalGoalViewData goal in FreePlayContent.BuildGoals(state, includeSummary: false))
            {
                if (goal.State == JournalGoalState.Active && goal.Id != FreePlayContent.RegionGoalId)
                    open.Add(goal.Title + " — " + goal.CurrentStep);
            }
            text.Append("\n\nЧто ещё ждёт:");
            if (open.Count == 0)
                text.Append("\nЯвных дел нет — можно идти туда, где ещё не были.");
            else
                foreach (string line in open)
                    text.Append("\n• ").Append(line);
            return text.ToString();
        }
    }
}
