using System.Collections.Generic;

namespace KingdomSurvival.FreePlay
{
    // Квест-локация «Стук за завалом» [РАБОЧЕЕ] — первая история, собранная
    // по гайду Мастерской квестов (QuestWorkshop/KingdomSurvival_Quest_Guide_RU.md,
    // квест quest_free_knocking_cave). Место, объекты, волки, столкновение и
    // сцены — данные, перенесённые из мастерской (База карты, генератор,
    // База локаций, База диалогов); победа в среднем зале ставит флаг сама
    // (LocalExplorationService). Здесь — только то, что пока делает код:
    // Хроника и «Дело» по флагам истории.
    //
    // Точильщик Нежата, пещера и старая кладка с клеймом «два угла» —
    // предложения в лор (П-1…П-3), не утверждены. Кто клал кладку и что за
    // ней — [ОТКРЫТО].
    public static class FreePlayCaveStory
    {
        public const string LocationId = "knocking_cave";
        public const string LocalLocationId = "local.knocking_cave";

        public static class Flags
        {
            public const string Entered = "freeplay.cave.entered";
            public const string CutterKnown = "freeplay.cave.cutter_known";
            public const string CutterGone = "freeplay.cave.cutter_gone";
            public const string FreedByRuse = "freeplay.cave.cutter_freed_by_ruse";
            public const string CutterMet = "freeplay.cave.cutter_met";
            public const string DenCleared = "freeplay.cave.den_cleared";
            public const string MarkMatched = "freeplay.cave.mark_matched";
        }

        public const string OldMarkKnowledge = "freeplay.cave.knowledge.old_mark_seen";

        public const string ChronicleFreed = "freeplay.cave.chronicle.freed";
        public const string ChronicleMet = "freeplay.cave.chronicle.met";
        public const string ChronicleDen = "freeplay.cave.chronicle.den";
        public const string ChronicleMark = "freeplay.cave.chronicle.mark";
        private const string MineChronicleMark = "freeplay.mine.chronicle.mark";

        private static bool Has(GameState state, string flag) => state?.Narrative != null && state.Narrative.HasFlag(flag);

        public static List<string> Refresh(GameState state)
        {
            List<string> reports = new List<string>();
            if (state?.Narrative == null)
                return reports;

            if (Has(state, Flags.FreedByRuse) && Chronicle.Find(state, ChronicleFreed) == null)
            {
                Chronicle.Record(state, ChronicleFreed, "Точильщик ушёл из пещеры",
                    "Нежата, запертый волками в пещере за завалом, ушёл на рассвете — пока отряд уводил зверей от лаза.", LocationId);
            }

            if (Has(state, Flags.CutterMet) && Chronicle.Find(state, ChronicleMet) == null)
            {
                Chronicle.Record(state, ChronicleMet, "Точильщик в дальнем зале",
                    "Три дня в дальнем зале пещеры сидел точильщик Нежата и стучал молотком, чтобы волки знали: человек не спит.", LocationId);
            }

            if (Has(state, Flags.DenCleared) && Chronicle.Find(state, ChronicleDen) == null)
            {
                string text = "Средний зал пещеры за завалом очищен от волков. В глубине дальнего зала — старая кладка со знаком.";
                Chronicle.Record(state, ChronicleDen, "Волки в пещере", text, LocationId);
                reports.Add(text);
            }

            if (Has(state, Flags.MarkMatched) && Chronicle.Find(state, ChronicleMark) == null)
            {
                string text = "На старой кладке в глубине пещеры — тот же знак, что на железе Старой шахты: два угла, вписанные один в другой. Кто клал — неизвестно.";
                Chronicle.Record(state, ChronicleMark, "Клеймо на старой кладке", text, LocationId,
                    Chronicle.Find(state, MineChronicleMark) != null ? MineChronicleMark : null);
                reports.Add(text);
            }
            return reports;
        }

        // «Дело» появляется, когда отряд вошёл в пещеру.
        public static JournalGoalViewData BuildGoal(GameState state)
        {
            if (!Has(state, Flags.Entered))
                return null;
            bool gone = Has(state, Flags.CutterGone);
            bool cleared = Has(state, Flags.DenCleared);
            bool matched = Has(state, Flags.MarkMatched);
            string step;
            string stage;
            if (matched)
            {
                step = "Знак на старой кладке — то же клеймо, что на железе шахты. Кто клал и что за стеной — неизвестно.";
                stage = "matched";
            }
            else if (cleared)
            {
                step = (gone ? "Нежата ушёл. " : "Нежата ждёт в дальнем зале. ") +
                       "За дальним залом — старая кладка со знаком «два угла». Где такой знак встречался ещё?";
                stage = gone ? "cleared.gone" : "cleared";
            }
            else if (gone)
            {
                step = "Нежата ушёл живым. Волки остались в среднем зале — дальний зал за ними.";
                stage = "gone";
            }
            else if (Has(state, Flags.CutterKnown))
            {
                step = "Точильщик Нежата заперт волками в дальнем зале.";
                stage = "known";
            }
            else
            {
                step = "Найти того, кто стучит за завалом.";
                stage = "entered";
            }
            return new JournalGoalViewData
            {
                Id = "freeplay.goal.cave",
                Title = "Стук за завалом",
                Description = "Устье пещеры заложено изнутри, а из глубины мерно стучат.",
                CurrentStep = step,
                RevisionId = "freeplay.goal.cave." + stage,
                Category = JournalGoalCategory.Optional,
                State = matched ? JournalGoalState.Completed : JournalGoalState.Active
            };
        }
    }
}
