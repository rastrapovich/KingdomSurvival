using System;
using System.Collections.Generic;

namespace KingdomSurvival.FreePlay
{
    // ПР-12Б, история И-4 «Разрытый курган» (ProjectDocs/PR12BV_FREE_PLAY_CONTENT_SPEC.md
    // §4; лор — LORE.md §17.1: курган у старой дороги возле руин, поверье
    // «из кургана не берут» — верование, кто лежит в кургане — открыто).
    // [РАБОЧЕЕ]; люди семьи и их имена — рабочие.
    //
    // Ход: курган находят после осмотра руин или по встрече «Свежая земля» →
    // у кургана следы (Агнесса читает сама, иначе Следопытство), поверье от
    // Остафия → голодная семья у костра: позвать в Дом (с возвратом находки
    // или без), вернуть и отпустить, отобрать узел, отпустить с находкой; или
    // засыпать курган самим. Оставили курган разрытым — через пять дней у
    // Дома «кто-то ходит»: двусмысленно, разгадки нет, есть выбор, что делать.
    public static class FreePlayKurganStory
    {
        public const string LocationId = "freeplay.kurgan";
        public const string LocationName = "Курган у старой дороги";
        public const string RuinsLocationId = "ruins";

        public const string MoundDialogueId = "freeplay_kurgan_mound";
        public const string WalkingDialogueId = "freeplay_kurgan_walking";
        public const string FreshEarthDialogueId = "freeplay_kurgan_fresh_earth";

        public const string HouseholdId = "household.freeplay.refugees";
        public const string FamilyJoinOperationId = "freeplay.kurgan.family_join";
        public const string GordeyId = "freeplay.refugee.gordey";
        public const string ZlataId = "freeplay.refugee.zlata";
        public const string MishkaId = "freeplay.refugee.mishka";

        public const int WalkingDelayDays = 5;
        public const int FindGold = 15;

        public static class Flags
        {
            public const string Known = "freeplay.kurgan.known";
            // Исходы у кургана.
            public const string Filled = "freeplay.kurgan.filled";
            public const string FamilyJoined = "freeplay.kurgan.family_joined";
            public const string FamilyLeft = "freeplay.kurgan.family_left";
            public const string TookFind = "freeplay.kurgan.took_find";
            public const string TrackLost = "freeplay.kurgan.track_lost";
            // Отложенное: кто-то ходит у Дома; ответ Дома.
            public const string Walking = "freeplay.kurgan.walking";
            public const string WalkingFilled = "freeplay.kurgan.walking_filled";
            public const string WalkingWatch = "freeplay.kurgan.walking_watch";
            public const string WalkingIgnored = "freeplay.kurgan.walking_ignored";
        }

        private const string ChronicleFound = "freeplay.kurgan.chronicle.found";
        private const string ChronicleOutcome = "freeplay.kurgan.chronicle.outcome";
        private const string ChronicleFamily = "freeplay.kurgan.chronicle.family";
        private const string ChronicleFind = "freeplay.kurgan.chronicle.find";
        private const string ChronicleWalking = "freeplay.kurgan.chronicle.walking";
        private const string ChronicleWalkingAnswer = "freeplay.kurgan.chronicle.walking_answer";

        private static bool Has(GameState state, string flag) => state?.Narrative != null && state.Narrative.HasFlag(flag);

        // У кургана решили: семью встретили (любой исход), след потеряли или засыпали.
        public static bool Decided(GameState state) =>
            Has(state, Flags.FamilyJoined) || Has(state, Flags.FamilyLeft) || Has(state, Flags.Filled) || Has(state, Flags.TrackLost);

        public static bool IsMoundFilled(GameState state) => Has(state, Flags.Filled) || Has(state, Flags.WalkingFilled);

        public static bool WalkingAnswered(GameState state) =>
            Has(state, Flags.WalkingFilled) || Has(state, Flags.WalkingWatch) || Has(state, Flags.WalkingIgnored);

        public static LocationData FindKurgan(GameState state) => state?.FindLocation(LocationId);

        public static List<string> Refresh(GameState state)
        {
            List<string> reports = new List<string>();
            if (state?.Narrative == null)
                return reports;

            LocationData ruins = state.FindLocation(RuinsLocationId);
            if (!Has(state, Flags.Known) && ruins != null && ruins.IsExplored)
                state.Narrative.SetFlag(Flags.Known);

            if (Has(state, Flags.Known) && FindKurgan(state) == null)
            {
                EnsureKurgan(state);
                string text = "У старой дороги возле руин — разрытый курган: земля ещё сырая, на отвале черепок и детский след.";
                Chronicle.Record(state, ChronicleFound, "Разрытый курган", text, LocationId);
                reports.Add(text);
            }

            if (Has(state, Flags.FamilyJoined) && AdmitFamily(state, out string admitted))
            {
                string text = "Семья беженцев пришла в Дом: Гордей, Злата и маленький Мишка. " + admitted + " Еды теперь нужно больше.";
                Chronicle.Record(state, ChronicleFamily, "Семья с кургана в Доме", text, null, ChronicleFound);
                reports.Add(text);
            }

            if (Has(state, Flags.TookFind) && Chronicle.Find(state, ChronicleFind) == null)
            {
                state.Gold += FindGold;
                string text = "Медную пряжку и бусины из кургана выменяли на " + FindGold + " золота. Остафий молчит об этом весь вечер.";
                Chronicle.Record(state, ChronicleFind, "Находка из кургана", text, LocationId, ChronicleFound);
                reports.Add(text);
            }

            if (Decided(state) && Chronicle.Find(state, ChronicleOutcome) == null)
            {
                Chronicle.Record(state, ChronicleOutcome, "Курган у старой дороги",
                    Has(state, Flags.Filled)
                        ? "Курган снова засыпан; взятое вернули в землю."
                        : "Курган так и остался разрытым.",
                    LocationId, ChronicleFound);
            }

            if (Decided(state) && !IsMoundFilled(state) && !Has(state, Flags.Walking) &&
                state.Day >= Chronicle.Find(state, ChronicleOutcome).Day + WalkingDelayDays)
            {
                state.Narrative.SetFlag(Flags.Walking);
                string text = "Третью ночь собаки лают в одну сторону — к старой дороге. Утром у тына следы: босые, большие, и не видно, откуда пришли.";
                Chronicle.Record(state, ChronicleWalking, "Кто-то ходит у Дома", text, null, ChronicleOutcome);
                reports.Add(text);
            }

            if (WalkingAnswered(state) && Chronicle.Find(state, ChronicleWalkingAnswer) == null)
            {
                string text = Has(state, Flags.WalkingFilled)
                    ? "К кургану отнесли хлеб и засыпали его. Собаки больше не лают по ночам."
                    : Has(state, Flags.WalkingWatch)
                        ? "Дозор у тына никого не видел. Под утро следы всё равно были."
                        : "На хождение у тына решили не обращать внимания. Собаки привыкают первыми.";
                Chronicle.Record(state, ChronicleWalkingAnswer, "Кто ходит у Дома", text, null, ChronicleWalking);
                reports.Add(text);
            }
            return reports;
        }

        // Курган у старой дороги: между руинами и Домом, ближе к руинам, там,
        // куда есть путь.
        public static LocationData EnsureKurgan(GameState state)
        {
            LocationData existing = FindKurgan(state);
            if (existing != null)
                return existing;
            LocationData ruins = state.FindLocation(RuinsLocationId);
            float fromX = ruins != null ? ruins.MapXPercent : WorldMapNavigation.CapitalXPercent + 10f;
            float fromY = ruins != null ? ruins.MapYPercent : WorldMapNavigation.CapitalYPercent;
            float candidateX = fromX + (WorldMapNavigation.CapitalXPercent - fromX) * 0.25f;
            float candidateY = fromY + (WorldMapNavigation.CapitalYPercent - fromY) * 0.25f;
            List<MapPointData> route = WorldMapNavigation.FindPath(
                WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent, candidateX, candidateY);
            float finalX = route.Count > 0 ? route[route.Count - 1].XPercent : candidateX;
            float finalY = route.Count > 0 ? route[route.Count - 1].YPercent : candidateY;

            LocationData kurgan = new LocationData(LocationId, LocationName,
                ContinuousSimulationSystem.CalculateTravelHours(route), "неизвестна", explorationHours: 1.0)
            {
                ResearchResultText = "Курган как курган: трава, камень у подножия, тропа мимо. Люди Дома обходят его стороной.",
                RegionId = "freeplay-kurgan",
                RegionName = GameState.GetRegionName(finalX, finalY),
                MapSlotIndex = state.Locations.Count,
                MapXPercent = finalX,
                MapYPercent = finalY,
                IsDiscovered = true,
                IsVisibleOnMap = true,
                InteractionDescription = "Старый курган у дороги. С южного бока земля свежая, рыжая."
            };
            state.Locations.Add(kurgan);
            return kurgan;
        }

        private static bool AdmitFamily(GameState state, out string message)
        {
            message = string.Empty;
            if (state.People == null || state.Narrative.HasEffectApplied(FamilyJoinOperationId))
                return false;
            HouseholdState household = new HouseholdState { HouseholdId = HouseholdId, DisplayName = "Семья Гордея" };
            List<ResidentState> members = new List<ResidentState>
            {
                new ResidentState
                {
                    PersonId = GordeyId,
                    DisplayName = "Гордей",
                    RoleLabel = "беженец",
                    ShortDescription = "Пришёл издалека, откуда — не говорит. Копал курган от голода и сам это помнит. Копьё держать умеет.",
                    DialogueSpeakerId = "refugee",
                    AgeGroup = ResidentAgeGroup.Adult,
                    TravelRole = ResidentTravelRole.Combatant,
                    UnitTypeId = "militia"
                },
                new ResidentState
                {
                    PersonId = ZlataId,
                    DisplayName = "Злата",
                    RoleLabel = "жена Гордея",
                    ShortDescription = "Считает хлеб по кускам и детей по головам. Первой сказала, что красть грех, и первой взялась за палку.",
                    DialogueSpeakerId = "refugee_woman",
                    AgeGroup = ResidentAgeGroup.Adult
                },
                new ResidentState
                {
                    PersonId = MishkaId,
                    DisplayName = "Мишка",
                    RoleLabel = "сын Гордея",
                    AgeGroup = ResidentAgeGroup.Child
                }
            };
            return HomePeopleService.AdmitHousehold(state, FamilyJoinOperationId, household, members, out message);
        }

        // ------------------------------------------------------------------
        // Сцены
        // ------------------------------------------------------------------

        public static LocationEntryView LocationEntry(GameState state, string locationId)
        {
            if (locationId != LocationId || !Has(state, Flags.Known) || Decided(state))
                return null;
            return new LocationEntryView
            {
                DialogueId = MoundDialogueId,
                ButtonText = "ПОДОЙТИ К КУРГАНУ",
                Hint = "Кто-то разрыл курган. Земля ещё сырая."
            };
        }

        public static void AddHomeCares(GameState state, List<HomeCareView> cares)
        {
            if (!Has(state, Flags.Walking) || WalkingAnswered(state))
                return;
            bool atHome = !HomePeopleService.HasDeparted(state);
            cares.Add(new HomeCareView
            {
                Id = "home.care.freeplay_kurgan_walking",
                Title = "Кто-то ходит ночами",
                Cause = "Собаки лают к старой дороге, у тына — чужие следы.",
                Status = atHome ? "Дом ждёт решения." : "Дом ждёт, пока Командир вернётся.",
                Priority = HomeCares.PriorityStory,
                ActionLabel = "Решить, что делать",
                Action = HomeCareAction.OpenDialogue,
                DialogueId = WalkingDialogueId,
                ActionEnabled = atHome
            });
        }

        public static JournalGoalViewData BuildGoal(GameState state)
        {
            if (!Has(state, Flags.Known))
                return null;
            bool walkingOpen = Has(state, Flags.Walking) && !WalkingAnswered(state);
            bool done = Decided(state) && !walkingOpen;
            string step;
            if (!Decided(state))
                step = "Дойти до кургана у старой дороги и узнать, кто его разрыл.";
            else if (walkingOpen)
                step = "Ночами у Дома кто-то ходит. Решить, что с этим делать, — на экране Дома.";
            else
                step = (Has(state, Flags.FamilyJoined) ? "Семья с кургана живёт в Доме. " : string.Empty) +
                       (IsMoundFilled(state) ? "Курган засыпан." : "Курган остался разрытым.");
            return new JournalGoalViewData
            {
                Id = "freeplay.goal.kurgan",
                Title = "Разрытый курган",
                Description = "Из кургана не берут — так говорят. Кто лежит в кургане у старой дороги, в Доме не знает никто.",
                CurrentStep = step,
                RevisionId = "freeplay.goal.kurgan." + (done ? "done" : walkingOpen ? "walking" : Decided(state) ? "decided" : "known"),
                Category = JournalGoalCategory.Optional,
                State = done ? JournalGoalState.Completed : JournalGoalState.Active
            };
        }
    }
}
