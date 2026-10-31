using System;
using System.Collections.Generic;

namespace KingdomSurvival.FreePlay
{
    // ПР-12Б, история И-2 «Сухая шахта» (ProjectDocs/PR12BV_FREE_PLAY_CONTENT_SPEC.md
    // §4; лор — LORE.md §17.1: шахта старше Дома, кем вырыта — открыто;
    // звери Чёрного леса). [РАБОЧЕЕ].
    //
    // Ход: на месте отвалы с железом (Наблюдательность), клеймо старых работ
    // (Расследование), дальняя штольня занята зверями (бой «Логово в
    // штольне», можно не спускаться, после отхода — вернуться) → железо.
    // Канон v1.59: зацепка через Остафия, разговор о клейме и топор из угля
    // Лады удалены вместе с исходным Домом. Шахта открывается на карте и
    // исследуется локальной картой (FreePlayMineLocal); сцены входа ниже
    // работают для партий, где шахта уже была известна (флаг Known).
    public static class FreePlayMineStory
    {
        public const string LocationId = "mine";

        public const string EntryDialogueId = "freeplay_mine_entry";
        public const string DeepDialogueId = "freeplay_mine_deep";
        public const string LairBattlePrefix = "freeplay.battle.mine_lair.";

        public static class Flags
        {
            public const string Known = "freeplay.mine.known";
            public const string Entered = "freeplay.mine.entered";
            public const string IronDumps = "freeplay.mine.iron_dumps";
            public const string Mark = "freeplay.mine.mark";
            public const string Deep = "freeplay.mine.deep";
            public const string LairCleared = "freeplay.mine.lair_cleared";
        }

        private const string ChronicleIron = "freeplay.mine.chronicle.iron";
        private const string ChronicleMark = "freeplay.mine.chronicle.mark";
        private const string ChronicleLair = "freeplay.mine.chronicle.lair";

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
                    "На старом железе шахты — одно клеймо: два угла, вписанные один в другой. " +
                    "Кто-то метил своё железо, чтобы его узнавали.", LocationId);
            }

            return reports;
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
            request.PreparedStart = PartyPresence.IsPresent(state, CampRest.AgnessaId);
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
                reports?.Add("Звери остались в дальней штольне. Туда можно вернуться.");
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
                step = "Железо найдено, дальняя штольня чиста." + (Has(state, Flags.Mark) ? " Клеймо старых работ прочитано." : string.Empty);
            else
                step = (HasIron(state) ? "Железо найдено. " : "Железа пока нет. ") +
                       (Has(state, Flags.LairCleared) ? "Дальняя штольня чиста." : "В дальней штольне — звери.");
            return new JournalGoalViewData
            {
                Id = "freeplay.goal.mine",
                Title = "Сухая шахта",
                Description = "Шахту вырыли давно. Кто — не знает никто. " +
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
