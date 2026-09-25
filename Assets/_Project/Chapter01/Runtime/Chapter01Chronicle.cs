using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
    public enum KnowledgeCertainty
    {
        Rumor,
        Observation,
        Established
    }

    public sealed class KnowledgeEntry
    {
        public string Id;
        public string Title;
        public KnowledgeCertainty Certainty;
        public string ConfirmedBy;
        public string LocationId;
    }

    // ПР-11 (ProjectDocs/PR11_CHRONICLE_SPEC.md §3) [РАБОЧЕЕ]: сведения главы —
    // короткое название, степень уверенности, подтверждение слуха и место.
    // Сам текст сведения берётся из реплики, которая его дала (база диалогов).
    public static class Chapter01KnowledgeCatalog
    {
        private static readonly List<KnowledgeEntry> Entries = new List<KnowledgeEntry>
        {
            Rumor(Chapter01Ids.Knowledge.SecondLoafIsRation, "Второй хлеб — это паёк", Chapter01Ids.Knowledge.OldAgreement),
            Rumor(Chapter01Ids.Knowledge.OldCustom, "Старый обычай", Chapter01Ids.Knowledge.OldAgreement),
            Rumor(Chapter01Ids.Knowledge.DrownedWomanStory, "История утонувшей", null),
            Rumor(Chapter01Ids.Knowledge.OldRoadAvoidedLowland, "Старая дорога обходила низину", null),
            Seen(Chapter01Ids.Knowledge.WaterFlowIsWrong, "Вода ведёт себя неправильно", null),
            Seen(Chapter01Ids.Knowledge.MillMovesAtWrongTime, "Колесо движется не в своё время", null),
            Seen(Chapter01Ids.Knowledge.CattleAvoidOldBranch, "Скот обходит старый рукав", null),
            Seen(Chapter01Ids.Knowledge.FishPatternChanged, "Рыба ведёт себя иначе", null),
            Seen(Chapter01Ids.Knowledge.OldSeventhChannel, "Седьмой рукав", null),
            Seen(Chapter01Ids.Knowledge.OldFord, "Старый брод", Chapter01Ids.Locations.OldWaterSearch),
            Seen(Chapter01Ids.Knowledge.SevenToothObject, "Мерка семи зубьев", null),
            Seen(Chapter01Ids.Knowledge.DownstreamPeople, "Люди ниже по течению", Chapter01Ids.Locations.DownstreamSettlement),
            Known(Chapter01Ids.Knowledge.OldAgreement, "Старое соглашение"),
            Known(Chapter01Ids.Knowledge.SharedWaterSystem, "Общая вода"),
            Known(Chapter01Ids.Knowledge.HomeWasNotSelfSufficient, "Дом не был самодостаточным")
        };

        private static KnowledgeEntry Rumor(string id, string title, string confirmedBy) =>
            new KnowledgeEntry { Id = id, Title = title, Certainty = KnowledgeCertainty.Rumor, ConfirmedBy = confirmedBy };

        private static KnowledgeEntry Seen(string id, string title, string locationId) =>
            new KnowledgeEntry { Id = id, Title = title, Certainty = KnowledgeCertainty.Observation, LocationId = locationId };

        private static KnowledgeEntry Known(string id, string title) =>
            new KnowledgeEntry { Id = id, Title = title, Certainty = KnowledgeCertainty.Established };

        public static IReadOnlyList<KnowledgeEntry> All => Entries;

        public static KnowledgeEntry Find(string id)
        {
            foreach (KnowledgeEntry entry in Entries)
            {
                if (entry.Id == id)
                    return entry;
            }
            return null;
        }

        // Открытые сведения в порядке каталога.
        public static List<KnowledgeEntry> Known(NarrativeStateData state)
        {
            List<KnowledgeEntry> known = new List<KnowledgeEntry>();
            if (state == null)
                return known;
            foreach (KnowledgeEntry entry in Entries)
            {
                if (state.HasKnowledge(entry.Id))
                    known.Add(entry);
            }
            return known;
        }

        public static bool IsConfirmed(NarrativeStateData state, KnowledgeEntry entry)
        {
            return entry != null && !string.IsNullOrEmpty(entry.ConfirmedBy) &&
                   state != null && state.HasKnowledge(entry.ConfirmedBy);
        }

        public static string CertaintyLabel(NarrativeStateData state, KnowledgeEntry entry)
        {
            switch (entry.Certainty)
            {
                case KnowledgeCertainty.Rumor:
                    return IsConfirmed(state, entry) ? "слух — подтвердилось" : "слух";
                case KnowledgeCertainty.Observation:
                    return "наблюдение";
                default:
                    return "установлено";
            }
        }
    }

    // ПР-11 (ТЗ §4, §6) [РАБОЧЕЕ]: история главы. Записи выводятся из уже
    // случившегося (флаги главы) и пишутся один раз — опрос каждый кадр
    // (UI) и на каждом шаге прогона главы; время записи — момент события.
    public static class Chapter01Chronicle
    {
        public const string FordAccessId = "ch01.ford_access";

        public static void Refresh(GameState gameState)
        {
            NarrativeStateData state = gameState?.Narrative;
            if (state == null || !Chapter01Crisis.IsActive(gameState))
                return;

            if (state.HasFlag(Chapter01Ids.Flags.FloodHappened))
            {
                string losses = state.HasFlag(Chapter01Ids.Flags.FloodLivestockLost) ? " и унесла часть скота" : string.Empty;
                Chronicle.Record(gameState, "ch01.flood", "Паводок",
                    "Ночью пришла большая вода: сорвала настил у мельницы" + losses + ".");
            }

            Chapter01RepairChoice repair = Chapter01StoryDirector.GetRepairChoice(state);
            if (repair != Chapter01RepairChoice.None)
            {
                Chronicle.Record(gameState, "ch01.repair", "Решение о плотине",
                    repair == Chapter01RepairChoice.Old
                        ? "Плотину решили чинить по-старому, как делали прежде."
                        : "Плотину решили чинить по-новому.");
            }

            if (state.HasFlag(Chapter01Ids.Flags.FisherFamilyAccepted))
                Chronicle.Record(gameState, "ch01.family", "Люди у ворот",
                    "Семью Тихона приняли в Дом: Тихон, Варвара, Аня и Федя.");
            else if (state.HasFlag(Chapter01Ids.Flags.FisherFamilyDeclined))
                Chronicle.Record(gameState, "ch01.family", "Люди у ворот", "Семье Тихона отказали: они ушли искать другой кров.");

            HomeWorkState deck = HomeLife.FindWork(gameState, HomeLife.YardDeckWorkId);
            if (deck != null && deck.Completed)
                Chronicle.Record(gameState, "ch01.yard_deck", "Хозяйственный настил", "Настил во дворе восстановлен.");

            if (state.HasFlag(Chapter01Ids.Flags.ExpeditionStarted) && !Chronicle.Has(gameState, "ch01.departure"))
            {
                List<string> names = new List<string>();
                foreach (string id in CampRest.PartyIds(gameState))
                {
                    ResidentState person = HomePeopleService.Find(gameState, id);
                    if (person != null)
                        names.Add(person.DisplayName);
                }
                Chronicle.Record(gameState, "ch01.departure", "Выход в поход",
                    "Отряд ушёл по следу старого русла" + (names.Count > 0 ? ": " + string.Join(", ", names) : string.Empty) + ".",
                    Chapter01Ids.Locations.OldWaterSearch);
            }

            if (state.HasFlag(Chapter01Ids.Flags.FordAccessResolved))
                Chronicle.Record(gameState, FordAccessId, "Выход у старого брода", DescribeFordAccess(state),
                    Chapter01Ids.Locations.OldWaterSearch);

            if (state.HasFlag(Chapter01Ids.Flags.DownstreamContact))
                Chronicle.Record(gameState, "ch01.downstream", "Люди ниже по течению",
                    "Ниже по течению живут люди — отряд встретился с ними.",
                    Chapter01Ids.Locations.DownstreamSettlement);

            if (state.HasFlag(Chapter01Ids.Flags.AgreementRevealed))
                Chronicle.Record(gameState, "ch01.agreement", "Старое соглашение",
                    "Дом стоит на воде, которую когда-то устроили сообща с чужими людьми.",
                    Chapter01Ids.Locations.DownstreamSettlement);

            if (state.HasFlag(Chapter01Ids.Flags.ReturnStarted))
                Chronicle.Record(gameState, "ch01.return_decision", "Домой", "Отряд повернул домой.");

            if (state.HasFlag(Chapter01Ids.Flags.FordReturnHandled) && Chronicle.Has(gameState, FordAccessId))
            {
                Chronicle.Record(gameState, "ch01.ford_return", "Обратно через брод",
                    Chapter01StoryDirector.IsFordCrossingKnown(gameState)
                        ? "Брод прошли без остановки — потому что переход уже был знаком."
                        : "У брода снова пришлось обходить к пологому берегу: край так никто и не укрепил.",
                    Chapter01Ids.Locations.OldWaterSearch, FordAccessId);
            }

            if (state.HasFlag(Chapter01Ids.Flags.ReturnedHome))
                Chronicle.Record(gameState, "ch01.returned", "Возвращение", "Отряд вернулся в Дом.");

            string growth = Chapter01OutcomeApplier.DescribeRoadGrowth(state);
            if (growth != null)
                Chronicle.Record(gameState, "ch01.road_growth", "Путь героя", growth);

            if (state.HasFlag(Chapter01Ids.Flags.CouncilCompleted))
                Chronicle.Record(gameState, "ch01.council", "Совет Дома", DescribeCouncil(state));
        }

        private static string DescribeFordAccess(NarrativeStateData state)
        {
            if (state.HasFlag(Chapter01Ids.Flags.FordAccessBracedSupport))
                return "Лада укрепила подмытый край — по нему поднялись.";
            if (state.HasFlag(Chapter01Ids.Flags.FordAccessOldDescent))
                return "Остафий нашёл старый боковой сход — по нему поднялись.";
            if (state.HasFlag(Chapter01Ids.Flags.FordAccessShallowLine))
                return "Тихон провёл по каменному переходу.";
            return "Подмытый край обошли к пологому берегу.";
        }

        private static string DescribeCouncil(NarrativeStateData state)
        {
            if (state.HasFlag(Chapter01Ids.Flags.CouncilOldOrderRestored))
                return "Совет решил вернуть старый порядок воды.";
            if (state.HasFlag(Chapter01Ids.Flags.CouncilNewOrderCreated))
                return "Совет решил договориться о новом порядке воды.";
            if (state.HasFlag(Chapter01Ids.Flags.CouncilWaterKeptForHome))
                return "Совет решил оставить воду Дому.";
            return "Совет Дома завершён.";
        }
    }
}

namespace KingdomSurvival.Chapter01
{
    // ПР-11: единый вход «Дел» журнала — провайдер выбирается по кризису
    // кампании. Первая глава — первый зарегистрированный провайдер; другой
    // кризис регистрирует свой, UI журнала не меняется.
    public static class CrisisJournal
    {
        private static readonly System.Collections.Generic.Dictionary<string, System.Func<GameState, IReadOnlyList<JournalGoalViewData>>> Providers =
            new System.Collections.Generic.Dictionary<string, System.Func<GameState, IReadOnlyList<JournalGoalViewData>>>
            {
                { Chapter01Crisis.CrisisId, Chapter01JournalProvider.Build }
            };

        public static void Register(string crisisId, System.Func<GameState, IReadOnlyList<JournalGoalViewData>> provider)
        {
            if (!string.IsNullOrEmpty(crisisId) && provider != null)
                Providers[crisisId] = provider;
        }

        public static IReadOnlyList<JournalGoalViewData> BuildGoals(GameState gameState)
        {
            if (gameState == null)
                return new List<JournalGoalViewData>();
            string crisisId = gameState.Configuration != null && !string.IsNullOrEmpty(gameState.Configuration.CrisisId)
                ? gameState.Configuration.CrisisId
                : Chapter01Crisis.CrisisId;
            return Providers.TryGetValue(crisisId, out System.Func<GameState, IReadOnlyList<JournalGoalViewData>> provider)
                ? provider(gameState)
                : new List<JournalGoalViewData>();
        }
    }
}
