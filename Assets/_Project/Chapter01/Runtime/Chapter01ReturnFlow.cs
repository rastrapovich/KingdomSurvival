using System;

namespace KingdomSurvival.Chapter01
{
    public enum Chapter01ReturnBranch
    {
        Unknown,
        ReturnNow,
        FollowFreshTrace
    }

    public enum Chapter01ReturnEchoKind
    {
        Fallback,
        OldRepairReturnNow,
        OldRepairFollowTrace,
        NewRepairReturnNow,
        NewRepairFollowTrace,
        FloodDamageReturnNow,
        FloodDamageFollowTrace
    }

    public readonly struct Chapter01ReturnEcho
    {
        public Chapter01ReturnEchoKind Kind { get; }
        public double RoadDelayHours { get; }
        public string RoadText { get; }
        public string HomeText { get; }

        public Chapter01ReturnEcho(
            Chapter01ReturnEchoKind kind,
            double roadDelayHours,
            string roadText,
            string homeText)
        {
            Kind = kind;
            RoadDelayHours = Math.Max(0.0, roadDelayHours);
            RoadText = roadText ?? string.Empty;
            HomeText = homeText ?? string.Empty;
        }
    }

    // P11/P12: узкая orchestration-модель только для первого возвращения.
    // Не QuestManager и не новая система последствий: читает уже существующие
    // NarrativeState/GameState и использует штатные TryOrderReturn/
    // TryStartRoadActivity. Ветка N14½ хранится существующими execution ID
    // диалога, поэтому переживает Save/Load без новых постоянных флагов.
    public static class Chapter01ReturnFlow
    {
        public const string ContinueBranchEffectId = "chapter01.effect.n14_5_continue_flag1";
        public const string ReturnNowBranchEffectId = "chapter01.effect.n14_5_return_flag1";

        private const string FollowTraceDelayEffectId = "chapter01.system.return.follow_trace_delay";
        private const string ReturnRoadConsequenceEffectId = "chapter01.system.return.road_consequence";
        private const string ReturnRoadReportEffectId = "chapter01.system.return.road_echo_reported";
        private const string HomeReportEffectId = "chapter01.system.return.home_echo_reported";

        private const string FollowTraceActivityId = "chapter01.activity.follow_fresh_trace";
        private const string ReturnRoadActivityId = "chapter01.activity.changed_return_road";
        private const double FollowTraceHours = 4.0;
        private const double ReturnRoadSceneProgress = 0.25;

        public static Chapter01ReturnBranch GetBranch(NarrativeStateData state)
        {
            if (state == null)
                return Chapter01ReturnBranch.Unknown;

            if (state.HasEffectApplied(ContinueBranchEffectId))
                return Chapter01ReturnBranch.FollowFreshTrace;
            if (state.HasEffectApplied(ReturnNowBranchEffectId))
                return Chapter01ReturnBranch.ReturnNow;

            // Старое сохранение/Debug может иметь ReturnStarted без execution ID.
            // Безопасный fallback — не добавлять скрытую задержку.
            return state.HasFlag(Chapter01Ids.Flags.ReturnStarted)
                ? Chapter01ReturnBranch.ReturnNow
                : Chapter01ReturnBranch.Unknown;
        }

        public static void EnsureAgreementKnowledge(NarrativeStateData state)
        {
            if (state == null || !state.HasFlag(Chapter01Ids.Flags.AgreementRevealed))
                return;

            // N14 обязан быть непробиваемым источником причинной истины.
            // Это также чинит старые/отладочные состояния, где флаг сцены
            // сохранился, а один из эффектов knowledge — нет.
            state.AddKnowledge(Chapter01Ids.Knowledge.SharedWaterSystem);
            state.AddKnowledge(Chapter01Ids.Knowledge.HomeWasNotSelfSufficient);
            state.AddKnowledge(Chapter01Ids.Knowledge.OldAgreement);
        }

        public static bool EnsurePhysicalReturn(GameState gameState, out string message)
        {
            message = string.Empty;
            if (gameState?.Narrative == null ||
                !gameState.Narrative.HasFlag(Chapter01Ids.Flags.ReturnStarted) ||
                !gameState.HasActiveExpedition)
            {
                return false;
            }

            ExpeditionData expedition = gameState.ActiveExpedition;
            if (expedition.Phase != CommanderState.ReturningToCastle)
            {
                if (!gameState.TryOrderReturn(out message))
                    return false;
            }

            TryApplyFollowTraceDelay(gameState);
            return true;
        }

        private static void TryApplyFollowTraceDelay(GameState gameState)
        {
            NarrativeStateData state = gameState?.Narrative;
            if (state == null || GetBranch(state) != Chapter01ReturnBranch.FollowFreshTrace)
                return;
            if (state.HasEffectApplied(FollowTraceDelayEffectId))
                return;
            if (!gameState.HasActiveExpedition ||
                gameState.ActiveExpedition.Phase != CommanderState.ReturningToCastle ||
                gameState.ActiveExpedition.HasTimedActivity)
            {
                return;
            }

            if (gameState.TryStartRoadActivity(
                    FollowTraceActivityId,
                    "ПРОВЕРКА СВЕЖЕГО СЛЕДА",
                    FollowTraceHours,
                    0,
                    0,
                    out _))
            {
                state.MarkEffectApplied(FollowTraceDelayEffectId);
            }
        }

        public static bool IsReturnRoadSceneReady(GameState gameState)
        {
            if (gameState?.Narrative == null || !gameState.HasActiveExpedition)
                return false;

            NarrativeStateData state = gameState.Narrative;
            ExpeditionData expedition = gameState.ActiveExpedition;
            if (!state.HasFlag(Chapter01Ids.Flags.ReturnStarted) ||
                state.HasFlag(Chapter01Ids.Flags.ReturnRoadTraveled) ||
                expedition.Phase != CommanderState.ReturningToCastle ||
                expedition.HasTimedActivity ||
                expedition.RouteLengthCells <= 0)
            {
                return false;
            }

            int travelled = expedition.RouteLengthCells - expedition.RemainingRouteCells;
            double progress = travelled / (double)expedition.RouteLengthCells;
            return progress >= ReturnRoadSceneProgress;
        }

        public static bool IsHomecomingReady(GameState gameState)
        {
            if (gameState?.Narrative == null || gameState.HasActiveExpedition)
                return false;

            NarrativeStateData state = gameState.Narrative;
            if (!state.HasFlag(Chapter01Ids.Flags.ReturnRoadTraveled) ||
                state.HasFlag(Chapter01Ids.Flags.ReturnedHome))
            {
                return false;
            }

            CommanderData commander = gameState.GetSelectedCommander();
            return commander != null && commander.State == CommanderState.InCastle;
        }

        public static bool TryApplyReturnRoadConsequence(GameState gameState)
        {
            if (gameState?.Narrative == null ||
                !gameState.Narrative.HasFlag(Chapter01Ids.Flags.ReturnRoadTraveled) ||
                gameState.Narrative.HasEffectApplied(ReturnRoadConsequenceEffectId))
            {
                return false;
            }

            if (!gameState.HasActiveExpedition ||
                gameState.ActiveExpedition.Phase != CommanderState.ReturningToCastle ||
                gameState.ActiveExpedition.HasTimedActivity)
            {
                return false;
            }

            Chapter01ReturnEcho echo = ResolveEcho(gameState.Narrative);
            if (echo.RoadDelayHours <= 0.0)
            {
                gameState.Narrative.MarkEffectApplied(ReturnRoadConsequenceEffectId);
                return true;
            }

            if (!gameState.TryStartRoadActivity(
                    ReturnRoadActivityId,
                    "ИЗМЕНИВШАЯСЯ ОБРАТНАЯ ДОРОГА",
                    echo.RoadDelayHours,
                    0,
                    0,
                    out _))
            {
                return false;
            }

            gameState.Narrative.MarkEffectApplied(ReturnRoadConsequenceEffectId);
            return true;
        }

        public static Chapter01ReturnEcho ResolveEcho(NarrativeStateData state)
        {
            if (state == null)
                return FallbackEcho();

            Chapter01ReturnBranch branch = GetBranch(state);
            bool followed = branch == Chapter01ReturnBranch.FollowFreshTrace;
            bool floodDamage =
                state.HasFlag(Chapter01Ids.Flags.FloodMillDeckDestroyed) ||
                state.HasFlag(Chapter01Ids.Flags.FloodLivestockLost);

            string humanEcho = state.HasFlag(Chapter01Ids.Flags.FloodWorkersSaved)
                ? " Те, кого удалось вытащить из воды, уже снова заняты делом."
                : string.Empty;

            if (floodDamage)
            {
                return new Chapter01ReturnEcho(
                    followed ? Chapter01ReturnEchoKind.FloodDamageFollowTrace : Chapter01ReturnEchoKind.FloodDamageReturnNow,
                    followed ? 3.0 : 2.0,
                    "Вода успела изменить знакомый путь: размокший участок приходится обходить, и обратная дорога отнимает больше времени.",
                    "След паводка никуда не исчез: у Дома по-прежнему видны повреждения и потери." + humanEcho);
            }

            if (state.HasFlag(Chapter01Ids.Flags.RepairOld))
            {
                return new Chapter01ReturnEcho(
                    followed ? Chapter01ReturnEchoKind.OldRepairFollowTrace : Chapter01ReturnEchoKind.OldRepairReturnNow,
                    followed ? 2.0 : 1.0,
                    "На обратном пути старые отводы читаются иначе: теперь ясно, что они были частью одной системы, а не случайными канавами.",
                    "Старый ремонт удержал привычный рисунок воды, но после похода он уже не выглядит самодостаточным порядком Дома." + humanEcho);
            }

            if (state.HasFlag(Chapter01Ids.Flags.RepairNew))
            {
                return new Chapter01ReturnEcho(
                    followed ? Chapter01ReturnEchoKind.NewRepairFollowTrace : Chapter01ReturnEchoKind.NewRepairReturnNow,
                    followed ? 2.5 : 1.5,
                    "Там, где новый ремонт изменил ход воды, знакомая дорога стала другой: одни места подсохли, в других появились свежие промоины.",
                    "Новый ремонт сделал жизнь Дома устойчивее здесь и сейчас, но последствия ниже по воде уже нельзя считать чужими." + humanEcho);
            }

            return FallbackEcho();
        }

        public static bool TryMarkRoadEchoReported(NarrativeStateData state)
        {
            return TryMarkOnce(state, ReturnRoadReportEffectId);
        }

        public static bool TryMarkHomeEchoReported(NarrativeStateData state)
        {
            return TryMarkOnce(state, HomeReportEffectId);
        }

        private static bool TryMarkOnce(NarrativeStateData state, string id)
        {
            if (state == null || state.HasEffectApplied(id))
                return false;
            state.MarkEffectApplied(id);
            return true;
        }

        private static Chapter01ReturnEcho FallbackEcho()
        {
            return new Chapter01ReturnEcho(
                Chapter01ReturnEchoKind.Fallback,
                1.0,
                "Обратная дорога знакома, но после услышанного те же места читаются иначе.",
                "Дом жил всё время отсутствия героя и встречает его уже изменившимся.");
        }
    }
}
