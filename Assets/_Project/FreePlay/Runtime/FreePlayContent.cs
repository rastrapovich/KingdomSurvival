using System.Collections.Generic;

namespace KingdomSurvival.FreePlay
{
    // ПР-12А (канон v1.45 §6.0) [РАБОЧЕЕ]: содержание режима «Свободная игра».
    // Никакой линии N01–N17 и тайны воды: «Дела» — из состояния партии
    // (походы и известные места), Дом — в обычном состоянии базового старта,
    // заботы — общие (еда, раненые). Авторские нити появятся в ПР-12Б.
    public static class FreePlayContent
    {
        public const string ModeId = CampaignStartOptions.FreePlayId;

        // Место, известное Дому с самого начала: ближняя цель первого выхода.
        public const string StartingKnownLocationId = "ruins";

        public const string RegionGoalId = "freeplay.goal.region";
        public const string DeparturePrefix = "freeplay.departure.";
        public const string ReturnPrefix = "freeplay.return.";

        public static void Register()
        {
            CampaignContent.Register(ModeId, new CampaignContentProvider
            {
                Goals = BuildGoals,
                HomeObjects = DescribeHomeObjects,
                HomeCares = HomeCares.DescribeCommon,
                OnNewCampaign = InitializeNewCampaign
            });
        }

        public static void InitializeNewCampaign(GameState state)
        {
            LocationData known = state?.FindLocation(StartingKnownLocationId);
            if (known == null)
                return;
            known.IsDiscovered = true;
            known.IsVisibleOnMap = true;
        }

        // ------------------------------------------------------------------
        // История: выход и возвращение каждого похода — по одной записи.
        // Опрашивается каждый кадр (UI) и в тестах; запись идемпотентна.
        // ------------------------------------------------------------------

        public static void Refresh(GameState state)
        {
            if (state == null || !CampaignContent.IsFreePlay(state))
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
                    ResidentState person = HomePeopleService.Find(state, id);
                    if (person != null)
                        names.Add(person.DisplayName);
                }
                Chronicle.Record(state, DeparturePrefix + (departures + 1), "Выход в поход",
                    "Отряд ушёл из Дома" + (target != null ? " — к месту «" + target.TravelTargetName + "»" : string.Empty) +
                    (names.Count > 0 ? ". В походе: " + string.Join(", ", names) : string.Empty) + ".",
                    target != null && target.IsVisibleOnMap ? target.Id : null);
            }
            else if (!state.HasActiveExpedition && departures > returns)
            {
                Chronicle.Record(state, ReturnPrefix + departures, "Возвращение", "Отряд вернулся в Дом.",
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
        // «Дела»
        // ------------------------------------------------------------------

        public static IReadOnlyList<JournalGoalViewData> BuildGoals(GameState state)
        {
            List<JournalGoalViewData> goals = new List<JournalGoalViewData>();
            if (state == null)
                return goals;

            goals.Add(BuildRegionGoal(state));
            foreach (LocationData location in state.Locations)
            {
                if (location == null || location.IsWaypoint || !location.IsVisibleOnMap || !location.IsDiscovered)
                    continue;
                goals.Add(BuildPlaceGoal(location));
            }
            return goals;
        }

        private static JournalGoalViewData BuildRegionGoal(GameState state)
        {
            int returns = CountEntries(state, ReturnPrefix);
            string step;
            string stage;
            if (state.HasActiveExpedition)
            {
                LocationData target = state.FindLocation(state.ActiveExpedition.LocationId);
                step = target != null
                    ? "Отряд в пути — к месту «" + target.TravelTargetName + "». Дом ждёт вестей."
                    : "Отряд в пути. Дом ждёт вестей.";
                stage = "away";
            }
            else if (returns == 0)
            {
                step = "Соберите людей на экране Дома и выберите цель на карте. Кто уйдёт в поход — того не будет дома.";
                stage = "home";
            }
            else
            {
                step = "Отряд вернулся (походов: " + returns + "). Посмотрите, что изменилось дома, и решите, куда идти дальше.";
                stage = "back";
            }

            return new JournalGoalViewData
            {
                Id = RegionGoalId,
                Title = "Округа Дома",
                Description = "Дом живёт обычной жизнью, но за привычными дорогами он знает немного. " +
                              "Что там — места, люди, опасности — можно узнать только самим.",
                CurrentStep = step,
                RevisionId = RegionGoalId + "." + stage + "." + returns,
                Category = JournalGoalCategory.Main,
                State = JournalGoalState.Active
            };
        }

        private static JournalGoalViewData BuildPlaceGoal(LocationData location)
        {
            bool explored = location.IsExplored;
            return new JournalGoalViewData
            {
                Id = "freeplay.goal.place." + location.Id,
                Title = location.Name,
                Description = explored && !string.IsNullOrEmpty(location.ResearchResultText)
                    ? location.ResearchResultText
                    : "Известное место в округе. Опасность: " + (string.IsNullOrEmpty(location.Threat) ? "неизвестна" : location.Threat) + ".",
                CurrentStep = explored ? "Место осмотрено." : "Дойти и осмотреть.",
                RevisionId = "freeplay.goal.place." + location.Id + (explored ? ".explored" : ".known"),
                Category = JournalGoalCategory.Optional,
                State = explored ? JournalGoalState.Completed : JournalGoalState.Active
            };
        }

        // ------------------------------------------------------------------
        // Дом — обычное состояние базового старта («Обычная жизнь»). Ключи
        // объектов и состояний совпадают со слоями образа Дома.
        // ------------------------------------------------------------------

        public static IReadOnlyList<HomeObjectView> DescribeHomeObjects(GameState state)
        {
            List<HomeObjectView> objects = new List<HomeObjectView>();
            if (state == null)
                return objects;

            objects.Add(Home("home.object.water", "Вода", "Normal", "спокойна", "Вода идёт как обычно."));
            objects.Add(Home("home.object.mill", "Мельница", "RunningNormally", "работает", "Мельница работает, колесо слышно с утра."));
            objects.Add(Home("home.object.dam", "Плотина", "Intact", "цела", "Плотина держит воду."));
            objects.Add(Home("home.object.mill_walkway", "Настил у мельницы", "OldIntact", "цел", "Настил у мельницы цел."));
            objects.Add(Home("home.object.livestock", "Скот и звук Дома", "Calm", "спокоен", "Скот спокоен, Дом звучит как обычно."));
            return objects;
        }

        private static HomeObjectView Home(string id, string title, string stateKey, string shortLabel, string text)
        {
            return new HomeObjectView
            {
                Id = id,
                Title = title,
                StateKey = stateKey,
                Short = shortLabel,
                State = text,
                Source = "Видно каждый день."
            };
        }
    }
}
