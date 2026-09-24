using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
    // Одно дело в Доме: сцена, которую игрок открывает сам, выбрав её
    // на экране Дома. Where — кто и где («Мирон · мельница»); Urgent —
    // пометка «срочно» (осмотр плотины после паводка).
    public readonly struct Chapter01HomeActivity
    {
        public readonly string DialogueId;
        public readonly string NodeId;
        public readonly string Title;
        public readonly string Where;
        public readonly bool Urgent;

        public Chapter01HomeActivity(string dialogueId, string nodeId, string title, string where, bool urgent = false)
        {
            DialogueId = dialogueId;
            NodeId = nodeId;
            Title = title;
            Where = where;
            Urgent = urgent;
        }
    }

    // ПР-02: домашняя часть главы N01–N10 через Дом, людей и место
    // (таблица утверждена пользователем 24.09.2026). Три вида узлов:
    //   - дела-карточки, которые игрок выбирает сам (N02, N03, N05, N07A/B/C,
    //     N08, N09);
    //   - сцены, открывающиеся сами (N01 в новой партии, N10 сразу после N09);
    //   - ночные события (N04 паводок, N06 удар на мельнице) — открываются
    //     сами, когда в игре наступает ночь; пока их ждут, время в Доме идёт.
    // Расследования N07 — в любом порядке; N08 открывается, как только
    // собранного хватает для Совета перед уходом (мельница, или скот и река),
    // недоделанные N07 остаются делами до выхода в поход.
    // Всё выводится из NarrativeState — отдельного хранимого состояния нет.
    public static class Chapter01HomeActivities
    {
        // Рабочие границы ночи (не канон): с 21:00 до 05:00.
        public const double NightStartHour = 21.0;
        public const double NightEndHour = 5.0;

        public static bool IsNight(double hourOfDay)
        {
            return hourOfDay >= NightStartHour || hourOfDay < NightEndHour;
        }

        // Домашняя часть идёт, пока герой дома и поход главы не начат.
        public static bool IsHomePhase(NarrativeStateData state, bool heroAtHome)
        {
            return state != null &&
                   heroAtHome &&
                   !state.HasFlag(Chapter01Ids.Flags.ExpeditionStarted) &&
                   !state.HasFlag(Chapter01Ids.Flags.Completed);
        }

        // Вариант (б): N08 доступен, как только одна из комбинаций знаний
        // для N09 станет собираемой после N08 (N08 всегда даёт старый обычай):
        // мельница (седьмой рукав) или два природных признака — скот и река.
        public static bool CanOpenSevenTeeth(NarrativeStateData state)
        {
            if (state == null)
                return false;

            return state.HasFlag(Chapter01Ids.Flags.InvestigatedMill) ||
                   (state.HasFlag(Chapter01Ids.Flags.InvestigatedCattle) &&
                    state.HasFlag(Chapter01Ids.Flags.InvestigatedRiver));
        }

        public static IReadOnlyList<Chapter01HomeActivity> GetAvailable(NarrativeStateData state, bool heroAtHome)
        {
            List<Chapter01HomeActivity> activities = new List<Chapter01HomeActivity>();
            if (!IsHomePhase(state, heroAtHome))
                return activities;

            if (state.HasFlag(Chapter01Ids.Flags.HomeIntroSeen) &&
                !state.HasFlag(Chapter01Ids.Flags.HousePeopleMet))
            {
                activities.Add(new Chapter01HomeActivity(
                    Chapter01Ids.Dialogues.D02, Chapter01Ids.Nodes.N02,
                    "Спор у настила", "Остафий и Лада · плотина"));
            }

            if (state.HasFlag(Chapter01Ids.Flags.HousePeopleMet) &&
                !state.HasFlag(Chapter01Ids.Flags.FirstPressureSeen))
            {
                activities.Add(new Chapter01HomeActivity(
                    Chapter01Ids.Dialogues.D03, Chapter01Ids.Nodes.N03,
                    "Колесо сбилось", "Мирон · мельница"));
            }

            if (state.HasFlag(Chapter01Ids.Flags.FloodHappened) &&
                Chapter01StoryDirector.GetRepairChoice(state) == Chapter01RepairChoice.None)
            {
                activities.Add(new Chapter01HomeActivity(
                    Chapter01Ids.Dialogues.D05, Chapter01Ids.Nodes.N05,
                    "Осмотр плотины", "Все четверо · плотина", urgent: true));
            }

            if (state.HasFlag(Chapter01Ids.Flags.WaterWrongActive))
            {
                if (!state.HasFlag(Chapter01Ids.Flags.InvestigatedMill))
                {
                    activities.Add(new Chapter01HomeActivity(
                        Chapter01Ids.Dialogues.D07A, Chapter01Ids.Nodes.N07A,
                        "Колесо, которое не спит", "Мирон · мельница"));
                }

                if (!state.HasFlag(Chapter01Ids.Flags.InvestigatedCattle))
                {
                    activities.Add(new Chapter01HomeActivity(
                        Chapter01Ids.Dialogues.D07B, Chapter01Ids.Nodes.N07B,
                        "Скот у старого водопоя", "Ульяна · водопой"));
                }

                if (!state.HasFlag(Chapter01Ids.Flags.InvestigatedRiver))
                {
                    activities.Add(new Chapter01HomeActivity(
                        Chapter01Ids.Dialogues.D07C, Chapter01Ids.Nodes.N07C,
                        "Река и рыба", "Берег ниже плотины"));
                }

                if (!state.HasFlag(Chapter01Ids.Flags.OldTraceFound) && CanOpenSevenTeeth(state))
                {
                    activities.Add(new Chapter01HomeActivity(
                        Chapter01Ids.Dialogues.D08, Chapter01Ids.Nodes.N08,
                        "Старый рукав", "Старый рукав"));
                }
            }

            if (state.HasFlag(Chapter01Ids.Flags.OldTraceFound) &&
                !state.HasFlag(Chapter01Ids.Flags.FarRouteUnlocked) &&
                Chapter01StoryDirector.CanOpenDepartureCouncil(state))
            {
                activities.Add(new Chapter01HomeActivity(
                    Chapter01Ids.Dialogues.D09, Chapter01Ids.Nodes.N09,
                    "Собрать людей", "Дом · вечерний совет"));
            }

            return activities;
        }

        // Ночное событие, которого сейчас ждёт глава (или null).
        public static string GetPendingNightScene(NarrativeStateData state, bool heroAtHome)
        {
            if (!IsHomePhase(state, heroAtHome))
                return null;

            if (state.HasFlag(Chapter01Ids.Flags.FirstPressureSeen) &&
                !state.HasFlag(Chapter01Ids.Flags.FloodHappened))
            {
                return Chapter01Ids.Dialogues.D04;
            }

            if (Chapter01StoryDirector.GetRepairChoice(state) != Chapter01RepairChoice.None &&
                !state.HasFlag(Chapter01Ids.Flags.WaterWrongActive))
            {
                return Chapter01Ids.Dialogues.D06;
            }

            return null;
        }

        // Сцена, которая откроется сама прямо сейчас (или null).
        public static string GetAutoScene(NarrativeStateData state, bool heroAtHome, double hourOfDay)
        {
            if (!IsHomePhase(state, heroAtHome))
                return null;

            if (!state.HasFlag(Chapter01Ids.Flags.HomeIntroSeen))
                return Chapter01Ids.Dialogues.D01;

            string night = GetPendingNightScene(state, heroAtHome);
            if (night != null)
                return IsNight(hourOfDay) ? night : null;

            if (state.HasFlag(Chapter01Ids.Flags.FarRouteUnlocked) &&
                !state.HasFlag(Chapter01Ids.Flags.PartyGatheringSeen))
            {
                return Chapter01Ids.Dialogues.D10;
            }

            return null;
        }

        // ПР-06А: необязательная домашняя работа — восстановить хозяйственный
        // настил во дворе, повреждённый паводком. Появляется после решения о
        // ремонте плотины (N05) и не является вторым выбором ремонта.
        public static bool IsYardDeckOffered(GameState gameState)
        {
            if (!Chapter01Crisis.IsActive(gameState) || gameState.Narrative == null)
                return false;

            return gameState.Narrative.HasFlag(Chapter01Ids.Flags.FloodHappened) &&
                   Chapter01StoryDirector.GetRepairChoice(gameState.Narrative) != Chapter01RepairChoice.None;
        }

        // Глава ждёт ночи: время в Доме должно идти, даже если никто не в пути.
        public static bool IsWaitingForNight(NarrativeStateData state, bool heroAtHome, double hourOfDay)
        {
            return GetPendingNightScene(state, heroAtHome) != null && !IsNight(hourOfDay);
        }

        public static IReadOnlyList<Chapter01HomeActivity> GetAvailable(GameState gameState)
        {
            if (!Chapter01Crisis.IsActive(gameState))
                return new List<Chapter01HomeActivity>();

            List<Chapter01HomeActivity> activities =
                new List<Chapter01HomeActivity>(GetAvailable(gameState.Narrative, !gameState.HasActiveExpedition));

            // ПР-06Б: предложение семьи Тихона — отдельный провайдер вне
            // IsHomePhase: ждёт дома и после похода главы, без срока.
            if (Chapter01FisherFamily.IsOffered(gameState))
            {
                activities.Add(new Chapter01HomeActivity(
                    Chapter01Ids.Dialogues.GateFamily, null,
                    "Люди у ворот", "Тихон и его семья · ворота"));
            }

            return activities;
        }

        public static string GetAutoScene(GameState gameState)
        {
            if (!Chapter01Crisis.IsActive(gameState))
                return null;
            return GetAutoScene(gameState.Narrative, !gameState.HasActiveExpedition, HourOf(gameState));
        }

        public static bool IsWaitingForNight(GameState gameState)
        {
            if (!Chapter01Crisis.IsActive(gameState))
                return false;
            return IsWaitingForNight(gameState.Narrative, !gameState.HasActiveExpedition, HourOf(gameState));
        }

        private static double HourOf(GameState gameState)
        {
            return ContinuousSimulationSystem.GetClock(gameState).HourOfDay;
        }
    }
}
