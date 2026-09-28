using System;
using System.Collections.Generic;

namespace KingdomSurvival.FreePlay
{
    // ПР-12Б, история И-2 «Сухая шахта» (ProjectDocs/PR12BV_FREE_PLAY_CONTENT_SPEC.md
    // §4; лор — LORE.md §17.1: шахта старше Дома, кем вырыта — открыто;
    // звери Чёрного леса). [РАБОЧЕЕ].
    //
    // Ход: после первого возвращения Остафий вспоминает о шахте → на месте
    // отвалы с железом (Наблюдательность), клеймо старых работ (Остафий в
    // отряде или Расследование), дальняя штольня занята зверями (бой
    // «Логово в штольне», можно не спускаться, после отхода — вернуться) →
    // железо; с углём Лады из него отковывают топор. Прочитанное клеймо
    // открывает у Остафия разговор о прежних работах.
    public static class FreePlayMineStory
    {
        public const string LocationId = "mine";

        public const string OstafiyDialogueId = "freeplay_mine_ostafiy";
        public const string EntryDialogueId = "freeplay_mine_entry";
        public const string DeepDialogueId = "freeplay_mine_deep";
        public const string MarkTalkDialogueId = "freeplay_mine_mark_talk";
        public const string LairBattlePrefix = "freeplay.battle.mine_lair.";
        public const string AxeGrantId = "freeplay.mine.axe";

        public static class Flags
        {
            public const string Known = "freeplay.mine.known";
            public const string Entered = "freeplay.mine.entered";
            public const string IronDumps = "freeplay.mine.iron_dumps";
            public const string Mark = "freeplay.mine.mark";
            public const string Deep = "freeplay.mine.deep";
            public const string LairCleared = "freeplay.mine.lair_cleared";
            public const string Axe = "freeplay.mine.axe";
            public const string MarkTalked = "freeplay.mine.mark_talked";
        }

        private const string ChronicleIron = "freeplay.mine.chronicle.iron";
        private const string ChronicleMark = "freeplay.mine.chronicle.mark";
        private const string ChronicleLair = "freeplay.mine.chronicle.lair";
        private const string ChronicleAxe = "freeplay.mine.chronicle.axe";
        private const string ChronicleMarkTalk = "freeplay.mine.chronicle.mark_talk";

        private static bool Has(GameState state, string flag) => state?.Narrative != null && state.Narrative.HasFlag(flag);

        public static bool HasIron(GameState state) => Has(state, Flags.IronDumps) || Has(state, Flags.LairCleared);

        public static List<string> Refresh(GameState state)
        {
            List<string> reports = new List<string>();
            if (state?.Narrative == null)
                return reports;

            if (Has(state, Flags.Known))
            {
                LocationData mine = state.FindLocation(LocationId);
                if (mine != null && (!mine.IsVisibleOnMap || !mine.IsDiscovered))
                {
                    mine.IsVisibleOnMap = true;
                    mine.IsDiscovered = true;
                }
            }

            if (HasIron(state) && Chronicle.Find(state, ChronicleIron) == null)
            {
                Chronicle.Record(state, ChronicleIron, "Железо из старой шахты",
                    "В отвалах старой шахты нашлось железо на несколько поковок.", LocationId);
            }

            if (Has(state, Flags.Mark) && Chronicle.Find(state, ChronicleMark) == null)
            {
                Chronicle.Record(state, ChronicleMark, "Клеймо старых работ",
                    "На старом железе шахты — одно клеймо: два угла, вписанные один в другой. Не Дома и не дальней мельницы: " +
                    "кто-то метил своё железо, чтобы его узнавали.", LocationId);
            }

            if (HasIron(state) && FreePlayCoalStory.HasCoal(state) && !Has(state, Flags.Axe))
            {
                state.Narrative.SetFlag(Flags.Axe);
                ItemService.GrantOnce(state, AxeGrantId, ItemCatalog.Axe, string.Empty);
                string text = "Лада и Торвин отковали из старого железа топор — он в кладовой.";
                Chronicle.Record(state, ChronicleAxe, "Топор из старого железа", text, null, ChronicleIron);
                reports.Add(text);
            }
            return reports;
        }

        // ------------------------------------------------------------------
        // Дом: Остафий вспоминает о шахте; Остафий о клейме
        // ------------------------------------------------------------------

        public static void AddHomeCares(GameState state, List<HomeCareView> cares)
        {
            bool atHome = !HomePeopleService.HasDeparted(state);
            ResidentState ostafiy = HomePeopleService.Find(state, HomePeopleService.OstafiyId);
            if (ostafiy == null || !ostafiy.IsAlive)
                return;

            if (!Has(state, Flags.Known) && FreePlayContent.CountEntries(state, FreePlayContent.ReturnPrefix) > 0)
            {
                cares.Add(new HomeCareView
                {
                    Id = "home.care.freeplay_mine_hint",
                    Title = "Остафий вспоминает",
                    Cause = "Остафий слышал, о чём рассказывал вернувшийся отряд, и что-то припомнил.",
                    Status = atHome ? "Ждёт разговора." : "Расскажет, когда Командир вернётся.",
                    Priority = HomeCares.PriorityOptional,
                    ActionLabel = "Поговорить с Остафием",
                    Action = HomeCareAction.OpenDialogue,
                    DialogueId = OstafiyDialogueId,
                    ActionEnabled = atHome
                });
            }

            if (Has(state, Flags.Mark) && !Has(state, Flags.MarkTalked))
            {
                cares.Add(new HomeCareView
                {
                    Id = "home.care.freeplay_mine_mark",
                    Title = "Остафий о клейме",
                    Cause = "Остафий хочет посмотреть на клеймо со старого железа.",
                    Status = atHome ? "Ждёт разговора." : "Посмотрит, когда Командир вернётся.",
                    Priority = HomeCares.PriorityOptional,
                    ActionLabel = "Показать клеймо Остафию",
                    Action = HomeCareAction.OpenDialogue,
                    DialogueId = MarkTalkDialogueId,
                    ActionEnabled = atHome
                });
            }
        }

        public static void DialogueCompleted(GameState state, string dialogueId)
        {
            if (dialogueId == MarkTalkDialogueId && Has(state, Flags.MarkTalked) && Chronicle.Find(state, ChronicleMarkTalk) == null)
            {
                Chronicle.Record(state, ChronicleMarkTalk, "Остафий о клейме",
                    "Остафий думает, что видел такой знак на камне — может быть, в кладке затопленных руин. Сам говорит: старость любит сходства.",
                    null, ChronicleMark);
            }
        }

        // ------------------------------------------------------------------
        // Шахта: сцена при входе и логово в дальней штольне
        // ------------------------------------------------------------------

        public static LocationEntryView LocationEntry(GameState state, string locationId)
        {
            if (locationId != LocationId || !Has(state, Flags.Known))
                return null;
            if (!Has(state, Flags.Entered))
            {
                return new LocationEntryView
                {
                    DialogueId = EntryDialogueId,
                    ButtonText = "ОСМОТРЕТЬ ОТВАЛЫ И ШТОЛЬНИ",
                    Hint = "Старое железо, клейма и дальняя штольня, откуда тянет зверем."
                };
            }
            if (Has(state, Flags.LairCleared))
                return null;
            return new LocationEntryView
            {
                DialogueId = DeepDialogueId,
                ButtonText = "СПУСТИТЬСЯ В ДАЛЬНЮЮ ШТОЛЬНЮ",
                Hint = "В дальней штольне живут звери. Можно и не спускаться."
            };
        }

        public static CampaignBattleRequest BattleAfterDialogue(GameState state, string dialogueId)
        {
            if (!state.HasActiveExpedition || Has(state, Flags.LairCleared))
                return null;
            bool deep = dialogueId == DeepDialogueId || (dialogueId == EntryDialogueId && Has(state, Flags.Deep) &&
                                                         !CampaignBattleBridge.IsApplied(state, LairBattlePrefix + 1));
            return deep ? CreateLairRequest(state) : null;
        }

        public static CampaignBattleRequest CreateLairRequest(GameState state)
        {
            int attempt = 1;
            while (CampaignBattleBridge.IsApplied(state, LairBattlePrefix + attempt))
                attempt++;
            CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, LairBattlePrefix + attempt);
            request.SourceId = DeepDialogueId;
            request.AllowRetreat = true;
            request.Enemies.Add(new CampaignBattleEnemy { UnitTypeId = "forest_beast", Count = 2 });
            request.PreparedStart = CampRest.PartyIds(state).Contains(CampRest.AgnessaId);
            FeatureCombatBatch.ApplyPreparedStart(state, request);
            return request;
        }

        public static void BattleApplied(GameState state, CampaignBattleResult result, List<string> reports)
        {
            if (state?.Narrative == null || result?.BattleId == null ||
                !result.BattleId.StartsWith(LairBattlePrefix, StringComparison.Ordinal))
                return;
            if (result.Outcome != CampaignBattleOutcome.Victory)
            {
                reports?.Add("Звери остались в дальней штольне. Туда можно вернуться — Лада поставит крепь у входа, если придётся отходить.");
                return;
            }
            state.Narrative.SetFlag(Flags.LairCleared);
            string text = "Дальняя штольня очищена от зверей. В глубине — сухой забой и железо, сложенное аккуратно, будто его оставили до завтра.";
            Chronicle.Record(state, ChronicleLair, "Логово в штольне", text, LocationId);
            reports?.Add(text);
        }

        // ------------------------------------------------------------------
        // «Дело»
        // ------------------------------------------------------------------

        public static JournalGoalViewData BuildGoal(GameState state)
        {
            if (!Has(state, Flags.Known))
                return null;
            bool done = HasIron(state) && Has(state, Flags.LairCleared);
            string step;
            if (!Has(state, Flags.Entered))
                step = "Дойти до старой шахты и осмотреть отвалы.";
            else if (done)
                step = "Железо у Дома, дальняя штольня чиста." + (Has(state, Flags.Mark) ? " Клеймо старых работ прочитано." : string.Empty);
            else
                step = (HasIron(state) ? "Железо у Дома. " : "Железа пока нет. ") +
                       (Has(state, Flags.LairCleared) ? "Дальняя штольня чиста." : "В дальней штольне — звери.");
            return new JournalGoalViewData
            {
                Id = "freeplay.goal.mine",
                Title = "Сухая шахта",
                Description = "Шахту вырыли до того, как пришли основатели Дома. Кто — в Доме не знает никто. " +
                              "Штольни сухие, и железо в отвалах лежит, будто его ждут.",
                CurrentStep = step,
                RevisionId = "freeplay.goal.mine." + (Has(state, Flags.Entered) ? "entered" : "known") + (HasIron(state) ? ".iron" : "") +
                             (Has(state, Flags.LairCleared) ? ".lair" : ""),
                Category = JournalGoalCategory.Optional,
                State = done ? JournalGoalState.Completed : JournalGoalState.Active
            };
        }
    }
}
