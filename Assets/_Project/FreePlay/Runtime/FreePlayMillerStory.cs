using System.Collections.Generic;

namespace KingdomSurvival.FreePlay
{
    // ПР-12Б, история И-3 «Мешок с клеймом» (ProjectDocs/PR12BV_FREE_PLAY_CONTENT_SPEC.md
    // §4; лор — LORE.md §17.1: дальняя мельница). [РАБОЧЕЕ].
    //
    // Продолжение утверждённой дорожной встречи ROAD_MILLER_SACK_01 (её текст
    // не меняется): что сделали с мешком, узнаём по её эффекту. Через пять
    // дней к Дому приходит сын дальнего мельника — мир сам приходит к игроку.
    // Мирон узнаёт клеймо. Исход разговора — дружба с дальней мельницей или
    // холодное прощание; и то и другое остаётся в Хронике.
    public static class FreePlayMillerStory
    {
        public const string EncounterId = "ROAD_MILLER_SACK_01";
        // Эффект ветки «взять крупу и оставить метку» во встрече.
        public const string TookGrainEffectId = "ROAD_MILLER_SACK_01.pr12.success.supplies";

        public const string GuestDialogueId = "freeplay_miller_guest";
        public const int GuestDelayDays = 5;

        public static class Flags
        {
            public const string Seen = "freeplay.miller.seen";
            public const string Friend = "freeplay.miller.friend";
            public const string Cool = "freeplay.miller.cool";
        }

        private const string ChronicleSack = "freeplay.miller.chronicle.sack";
        private const string ChronicleGuest = "freeplay.miller.chronicle.guest";

        private static bool Has(GameState state, string flag) => state?.Narrative != null && state.Narrative.HasFlag(flag);

        public static bool TookGrain(GameState state) =>
            state?.Narrative != null && state.Narrative.HasEffectApplied(TookGrainEffectId);

        public static bool EncounterSeen(GameState state)
        {
            EncounterRuntimeEntry entry = state?.Encounters?.FindEntry(EncounterId);
            return entry != null && entry.TimesStarted > 0;
        }

        public static bool Resolved(GameState state) => Has(state, Flags.Friend) || Has(state, Flags.Cool);

        // Гость у ворот: через пять дней после встречи, пока разговор не состоялся.
        public static bool IsGuestWaiting(GameState state)
        {
            if (!Has(state, Flags.Seen) || Resolved(state))
                return false;
            ChronicleEntryData sack = Chronicle.Find(state, ChronicleSack);
            return sack != null && state.Day >= sack.Day + GuestDelayDays;
        }

        public static List<string> Refresh(GameState state)
        {
            List<string> reports = new List<string>();
            if (state?.Narrative == null)
                return reports;

            if (!Has(state, Flags.Seen) && EncounterSeen(state))
            {
                state.Narrative.SetFlag(Flags.Seen);
                Chronicle.Record(state, ChronicleSack, "Мешок с клеймом",
                    TookGrain(state)
                        ? "На обочине подобрали крупу из чужого мешка с клеймом далёкой мельницы. Метку оставили на ветке, чтобы хозяин знал, где искать."
                        : "Чужой мешок с клеймом далёкой мельницы переложили выше от воды и оставили хозяину.");
            }

            if (Resolved(state) && Chronicle.Find(state, ChronicleGuest) == null)
            {
                string text = Has(state, Flags.Friend)
                    ? "Сын дальнего мельника ушёл с поклоном. Мирон говорит, что теперь на дальней мельнице Дом будут знать в лицо."
                    : "Сын дальнего мельника ушёл, не оглянувшись. Про Дом на дальней мельнице расскажут коротко.";
                Chronicle.Record(state, ChronicleGuest, "Гость с дальней мельницы", text, null, ChronicleSack);
                reports.Add(text);
            }
            return reports;
        }

        public static void AddHomeCares(GameState state, List<HomeCareView> cares)
        {
            if (!IsGuestWaiting(state))
                return;
            bool atHome = !HomePeopleService.HasDeparted(state);
            cares.Add(new HomeCareView
            {
                Id = "home.care.freeplay_miller_guest",
                Title = "Гость у ворот",
                Cause = "Пришёл парень с дальней мельницы. Спрашивает про мешок с клеймом.",
                Status = atHome ? "Ждёт, пока выйдут к нему." : "Ждёт, пока Командир вернётся.",
                Priority = HomeCares.PriorityStory,
                ActionLabel = "Выйти к гостю",
                Action = HomeCareAction.OpenDialogue,
                DialogueId = GuestDialogueId,
                ActionEnabled = atHome
            });
        }

        public static JournalGoalViewData BuildGoal(GameState state)
        {
            if (!IsGuestWaiting(state) && !Resolved(state))
                return null;
            bool done = Resolved(state);
            return new JournalGoalViewData
            {
                Id = "freeplay.goal.miller_guest",
                Title = "Гость с дальней мельницы",
                Description = "Мешок с клеймом, найденный на обочине, кто-то искал.",
                CurrentStep = done
                    ? Has(state, Flags.Friend) ? "Сын мельника ушёл с поклоном." : "Сын мельника ушёл, не оглянувшись."
                    : "У ворот ждёт сын дальнего мельника — выйти к нему на экране Дома.",
                RevisionId = "freeplay.goal.miller_guest." + (done ? "done" : "waiting"),
                Category = JournalGoalCategory.Optional,
                State = done ? JournalGoalState.Completed : JournalGoalState.Active
            };
        }
    }
}
