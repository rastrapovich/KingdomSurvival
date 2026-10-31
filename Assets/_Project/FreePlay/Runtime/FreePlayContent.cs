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
                HomeCares = DescribeHomeCares,
                OnNewCampaign = InitializeNewCampaign,
                // ПР-12Б: истории свободной игры.
                LocationEntry = LocationEntry,
                DialogueCompleted = DialogueCompleted,
                BattleAfterDialogue = BattleAfterDialogue,
                BattleApplied = BattleApplied,
                HomeWorkRate = FreePlayCoalStory.HomeWorkRate,
                Refresh = RefreshWithReports,
                HomeAction = HomeAction
            });
            // ПР-12К: исследуемые места режима (пилот — Старая шахта).
            LocalLocationCatalog.RegisterDefault(FreePlayMineLocal.Create());
            // 12Т: область поиска тайника у старого русла.
            WorldMapSearchAreas.Register(FreePlayCacheStory.SearchAreas);
        }

        public static LocationEntryView LocationEntry(GameState state, string locationId)
        {
            return FreePlayCoalStory.LocationEntry(state, locationId) ?? FreePlayMineStory.LocationEntry(state, locationId) ??
                   FreePlayKurganStory.LocationEntry(state, locationId) ?? FreePlayCacheStory.LocationEntry(state, locationId);
        }

        public static string HomeAction(GameState state, string actionId)
        {
            return actionId == FreePlayCoalStory.TradeActionId ? FreePlayCoalStory.RunTrade(state) : string.Empty;
        }

        public static void DialogueCompleted(GameState state, string dialogueId)
        {
            FreePlayMineStory.DialogueCompleted(state, dialogueId);
        }

        public static CampaignBattleRequest BattleAfterDialogue(GameState state, string dialogueId)
        {
            return FreePlayCoalStory.BattleAfterDialogue(state, dialogueId) ?? FreePlayMineStory.BattleAfterDialogue(state, dialogueId);
        }

        public static void BattleApplied(GameState state, CampaignBattleResult result, List<string> reports)
        {
            FreePlayCoalStory.BattleApplied(state, result, reports);
            FreePlayMineStory.BattleApplied(state, result, reports);
        }

        public static List<HomeCareView> DescribeHomeCares(GameState state)
        {
            List<HomeCareView> cares = HomeCares.DescribeCommon(state);
            if (state != null)
            {
                FreePlayMillerStory.AddHomeCares(state, cares);
                FreePlayCoalStory.AddHomeCares(state, cares);
                FreePlayMineStory.AddHomeCares(state, cares);
                FreePlayKurganStory.AddHomeCares(state, cares);
            }
            return cares;
        }

        // Опрос режима: история походов и истории режима; донесения — игроку.
        public static List<string> RefreshWithReports(GameState state)
        {
            Refresh(state);
            if (!CampaignContent.IsFreePlay(state))
                return new List<string>();
            List<string> reports = FreePlayCoalStory.Refresh(state);
            reports.AddRange(FreePlayMillerStory.Refresh(state));
            reports.AddRange(FreePlayMineStory.Refresh(state));
            reports.AddRange(FreePlayKurganStory.Refresh(state));
            reports.AddRange(FreePlayCaveStory.Refresh(state));
            // 12Т: поручения поселений и тайник у старого русла.
            reports.AddRange(FreePlayCacheStory.Refresh(state));
            reports.AddRange(ErrandService.Refresh(state));
            reports.AddRange(FreePlaySummary.Refresh(state));
            return reports;
        }

        public static void InitializeNewCampaign(GameState state)
        {
            // Бриф правок п. 19: партия начинается на стоянке у дороги — в
            // лагере, а не в пустом поселении. Это не Дом и не город.
            // Бриф правок (19 пунктов) пп. 5, 17: 20 припасов на старте — один
            // раз при создании партии; свита пуста (жителей стартового сюжета
            // отряд ещё не знает).
            if (state != null)
            {
                state.ArmySupply = StartingSupply;
                ExpeditionPreparation.Get(state);
                state.Preparation.RetinueId = string.Empty;
            }
            if (state != null && Chronicle.Find(state, StartCampChronicleId) == null)
                Chronicle.Record(state, StartCampChronicleId, "Стоянка у дороги",
                    "Отряд встал лагерем у развилки дорог: шалаш, кострище, вещи в мешках. Отсюда можно идти куда угодно.");
            // Бриф правок п. 15 (решение пользователя 30.10.2026): «Затопленные
            // руины» убраны из новой игры вместе с историей кургана — без руин
            // курган не открывается. Код истории остаётся для старых партий,
            // где она уже началась.
            if (state != null && !KurganStoryEnabled)
            {
                state.Locations?.RemoveAll(location => location != null && location.Id == StartingKnownLocationId);
                return;
            }
            LocationData known = state?.FindLocation(StartingKnownLocationId);
            if (known == null)
                return;
            known.IsDiscovered = true;
            known.IsVisibleOnMap = true;
        }

        public const string StartCampChronicleId = "start.camp";

        // Рабочее значение (бриф правок, 19 пунктов, п. 5).
        public const int StartingSupply = 20;

        // Руины и курган в новой партии (выключено, см. InitializeNewCampaign).
        public static bool KurganStoryEnabled = false;

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
            return BuildGoals(state, includeSummary: true);
        }

        public static List<JournalGoalViewData> BuildGoals(GameState state, bool includeSummary)
        {
            List<JournalGoalViewData> goals = new List<JournalGoalViewData>();
            if (state == null)
                return goals;

            // Бриф правок п. 11: «Основная цель» («Округа Дома») и «Итог Дома»
            // пока не показываются — цель свободной игры будет определена
            // отдельно. Данные и записи Хроники итога остаются.
            if (ShowHomeGoals)
            {
                goals.Add(BuildRegionGoal(state));
                // ПР-12Б: контрольный итог — после первого возвращения.
                JournalGoalViewData summary = includeSummary ? FreePlaySummary.BuildGoal(state) : null;
                if (summary != null)
                    goals.Add(summary);
            }
            JournalGoalViewData coal = FreePlayCoalStory.BuildGoal(state);
            if (coal != null)
                goals.Add(coal);
            JournalGoalViewData leader = FreePlayCoalStory.BuildLeaderGoal(state);
            if (leader != null)
                goals.Add(leader);
            JournalGoalViewData trade = FreePlayCoalStory.BuildTradeGoal(state);
            if (trade != null)
                goals.Add(trade);
            JournalGoalViewData guest = FreePlayMillerStory.BuildGoal(state);
            if (guest != null)
                goals.Add(guest);
            JournalGoalViewData mine = FreePlayMineStory.BuildGoal(state);
            if (mine != null)
                goals.Add(mine);
            JournalGoalViewData kurgan = FreePlayKurganStory.BuildGoal(state);
            if (kurgan != null)
                goals.Add(kurgan);
            JournalGoalViewData cave = FreePlayCaveStory.BuildGoal(state);
            if (cave != null)
                goals.Add(cave);
            // 12Т: принятые поручения поселений — одно «Дело» на поручение,
            // откуда бы его ни взяли (доска, корчма, разговор).
            goals.AddRange(ErrandService.BuildGoals(state));
            // Бриф исправлений п. 6: известное на карте место — ещё не задание.
            // Дополнительные «Дела» появляются из своих источников (разговор,
            // доска, слух, история), а не из списка мест.
            return goals;
        }

        public static bool ShowHomeGoals = false;

        public static JournalGoalViewData BuildRegionGoal(GameState state)
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
