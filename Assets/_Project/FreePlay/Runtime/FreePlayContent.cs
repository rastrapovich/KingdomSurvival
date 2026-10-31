using System.Collections.Generic;

namespace KingdomSurvival.FreePlay
{
    // ПР-12А (канон v1.59 §6.0) [РАБОЧЕЕ]: содержание «Свободной игры» —
    // единственного режима. «Дела» — из источников историй (разговор, доска,
    // слух, место), Хроника — выходы и возвращения отряда. Исходного Дома нет.
    public static class FreePlayContent
    {
        public const string ModeId = CampaignStartOptions.FreePlayId;

        // «Затопленные руины» убраны из новой игры (решение пользователя
        // 30.10.2026): место из авторских шаблонов снимается при создании.
        public const string RemovedRuinsLocationId = "ruins";

        public const string DeparturePrefix = "freeplay.departure.";
        public const string ReturnPrefix = "freeplay.return.";

        public static void Register()
        {
            CampaignContent.Register(new CampaignContentProvider
            {
                Goals = BuildGoals,
                OnNewCampaign = InitializeNewCampaign,
                // ПР-12Б: истории свободной игры.
                LocationEntry = LocationEntry,
                BattleAfterDialogue = BattleAfterDialogue,
                BattleApplied = BattleApplied,
                Refresh = RefreshWithReports
            });
            // ПР-12К: исследуемые места (пилот — Старая шахта).
            LocalLocationCatalog.RegisterDefault(FreePlayMineLocal.Create());
            // 12Т: область поиска тайника у старого русла.
            WorldMapSearchAreas.Register(FreePlayCacheStory.SearchAreas);
        }

        public static LocationEntryView LocationEntry(GameState state, string locationId)
        {
            return FreePlayMineStory.LocationEntry(state, locationId) ?? FreePlayCacheStory.LocationEntry(state, locationId);
        }

        public static CampaignBattleRequest BattleAfterDialogue(GameState state, string dialogueId)
        {
            return FreePlayMineStory.BattleAfterDialogue(state, dialogueId);
        }

        public static void BattleApplied(GameState state, CampaignBattleResult result, List<string> reports)
        {
            FreePlayMineStory.BattleApplied(state, result, reports);
        }

        // Опрос: история походов и истории; донесения — игроку.
        public static List<string> RefreshWithReports(GameState state)
        {
            Refresh(state);
            if (state == null)
                return new List<string>();
            List<string> reports = FreePlayMineStory.Refresh(state);
            reports.AddRange(FreePlayCaveStory.Refresh(state));
            // 12Т: поручения поселений и тайник у старого русла.
            reports.AddRange(FreePlayCacheStory.Refresh(state));
            reports.AddRange(ErrandService.Refresh(state));
            return reports;
        }

        public static void InitializeNewCampaign(GameState state)
        {
            if (state == null)
                return;
            // Бриф правок п. 19: партия начинается на стоянке у дороги — в
            // лагере, а не в пустом поселении. Бриф правок (19 пунктов)
            // пп. 5, 17: 20 припасов на старте — один раз при создании
            // партии; свита пуста.
            state.ArmySupply = StartingSupply;
            ExpeditionPreparation.Get(state);
            state.Preparation.RetinueId = string.Empty;
            if (Chronicle.Find(state, StartCampChronicleId) == null)
                Chronicle.Record(state, StartCampChronicleId, "Стоянка у дороги",
                    "Отряд встал лагерем у развилки дорог: шалаш, кострище, вещи в мешках. Отсюда можно идти куда угодно.");
            state.Locations?.RemoveAll(location => location != null && location.Id == RemovedRuinsLocationId);
        }

        public const string StartCampChronicleId = "start.camp";

        // Рабочее значение (бриф правок, 19 пунктов, п. 5).
        public const int StartingSupply = 20;

        // ------------------------------------------------------------------
        // История: выход и возвращение каждого похода — по одной записи.
        // Опрашивается каждый кадр (UI) и в тестах; запись идемпотентна.
        // ------------------------------------------------------------------

        public static void Refresh(GameState state)
        {
            if (state == null)
                return;

            int departures = CountEntries(state, DeparturePrefix);
            int returns = CountEntries(state, ReturnPrefix);

            if (state.HasActiveExpedition && departures == returns)
            {
                ExpeditionData expedition = state.ActiveExpedition;
                LocationData target = state.FindLocation(expedition.LocationId);
                List<string> names = new List<string>();
                foreach (string id in CampRest.PartyIds(state))
                {
                    ResidentState person = PeopleService.Find(state, id);
                    if (person != null)
                        names.Add(person.DisplayName);
                }
                Chronicle.Record(state, DeparturePrefix + (departures + 1), "Выход в поход",
                    "Отряд снялся со стоянки" + (target != null ? " — к месту «" + target.TravelTargetName + "»" : string.Empty) +
                    (names.Count > 0 ? ". В походе: " + string.Join(", ", names) : string.Empty) + ".",
                    target != null && target.IsVisibleOnMap ? target.Id : null);
            }
            else if (!state.HasActiveExpedition && departures > returns)
            {
                Chronicle.Record(state, ReturnPrefix + departures, "Возвращение", "Отряд вернулся на стоянку.",
                    null, DeparturePrefix + departures);
            }
        }

        public static int CountEntries(GameState state, string prefix)
        {
            int count = 0;
            foreach (ChronicleEntryData entry in Chronicle.Get(state).Entries)
            {
                if (entry.Id.StartsWith(prefix, System.StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        // ------------------------------------------------------------------
        // «Дела»: появляются из своих источников (разговор, доска, слух,
        // история), а не из списка мест (бриф исправлений п. 6). Главной
        // цели у свободной игры нет.
        // ------------------------------------------------------------------

        public static IReadOnlyList<JournalGoalViewData> BuildGoals(GameState state)
        {
            List<JournalGoalViewData> goals = new List<JournalGoalViewData>();
            if (state == null)
                return goals;

            JournalGoalViewData mine = FreePlayMineStory.BuildGoal(state);
            if (mine != null)
                goals.Add(mine);
            JournalGoalViewData cave = FreePlayCaveStory.BuildGoal(state);
            if (cave != null)
                goals.Add(cave);
            // 12Т: принятые поручения поселений — одно «Дело» на поручение,
            // откуда бы его ни взяли (доска, корчма, разговор).
            goals.AddRange(ErrandService.BuildGoals(state));
            return goals;
        }
    }
}
