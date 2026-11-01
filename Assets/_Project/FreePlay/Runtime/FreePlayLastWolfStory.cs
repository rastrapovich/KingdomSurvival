using System;
using System.Collections.Generic;

namespace KingdomSurvival.FreePlay
{
    // «Последний волк» [РАБОЧЕЕ] — квест мастерской quest_free_i1_coal,
    // заменяет удалённую И-1 «Уголь для Лады» (решение пользователя
    // 31.10.2026; аудит A-04). Лор — LORE.md §17.1: хутор углежогов у кромки
    // Чёрного леса, звери леса. Савва и подробности найма — рабочая
    // художественная разработка, не факт LORE.
    //
    // Сцены — один диалог freeplay_last_wolf, перенесённый из мастерской; начало
    // каждой сцены — точка входа (ссылка «диалог#узел»). Здесь — то, что пока
    // делает код: какую сцену открыть у Чёрного леса и в избе у дороги, время
    // пути и переноски, однократные еда и серебро, отложенное спасение
    // местными, срок за добычей до утра, весть по поселениям, Хроника, «Дело».
    // Числа — рабочие (мастерская: «Стоимость», «Возможные итоги»).
    public static class FreePlayLastWolfStory
    {
        public const string DialogueId = "freeplay_last_wolf";
        // Встреча и хутор — у кромки Чёрного леса (место «forest» генератора).
        public const string LocationId = "forest";

        public static class Nodes
        {
            public const string Arrival = "lw_arrival";
            public const string ReturnCorpse = "lw_return_corpse";
            public const string ReturnEmpty = "lw_return_empty";
            public const string HutAgain = "lw_hut_again";
            public const string InnBefore = "lw_inn_before";
            public const string InnReport = "lw_inn_report";
            public const string InnAfter = "lw_inn_after";
        }

        public static class Flags
        {
            public const string Seen = "last_wolf.seen";
            public const string DeadSeen = "last_wolf.dead_seen";
            public const string RumorHeard = "last_wolf.rumor_heard";
            public const string DirectionKnown = "last_wolf.direction_known";
            public const string FeeRefused = "last_wolf.fee_refused";
            public const string Withdrawn = "last_wolf.withdrawn";
            public const string Closed = "last_wolf.closed";
            public const string HunterSafe = "last_wolf.hunter_safe";
            public const string ProofLeft = "last_wolf.proof_left";
            public const string ProofDelivered = "last_wolf.proof_delivered";
            public const string ProofRetrieved = "last_wolf.proof_retrieved";
            public const string HelpSent = "last_wolf.help_sent";
            public const string DeathReported = "last_wolf.death_reported";
            public const string MessageEnding = "last_wolf.message_ending";
            public const string WaitedHelpers = "last_wolf.waited_helpers";
            public const string ReturnAvailable = "last_wolf.return_available";
            public const string ReturnEmptySeen = "last_wolf.return_empty_seen";
            public const string RescueComplete = "last_wolf.rescue_complete";
            public const string LocalsCollect = "last_wolf.locals_collect";
            public const string FeePaid = "last_wolf.fee_paid";
            public const string FeeWaived = "last_wolf.fee_waived";
            public const string RumorShared = "last_wolf.rumor_shared";
            public const string HutAgainSeen = "last_wolf.hut_again_seen";
            // Ставит код.
            public const string FoodPaid = "last_wolf.food_paid";
            public const string RewardPaid = "last_wolf.reward_paid";
            public const string NewsLocal = "last_wolf.news_local";
        }

        // Однократные отметки «время за этот отрезок уже прошло».
        private const string TimePersonFirst = "last_wolf.time.person_first";
        private const string TimeBoth = "last_wolf.time.both";
        private const string TimeMessage = "last_wolf.time.message";
        private const string TimeWait = "last_wolf.time.wait";
        private const string TimeReturn = "last_wolf.time.return";

        public const double PersonFirstHours = 1.0;
        public const double BothHoursWithHelper = 2.0;
        public const double BothHoursAlone = 3.0;
        public const double MessageHours = 0.5;
        public const double WaitHours = 2.0;
        public const double ReturnHours = 2.0;
        public const int FoodReward = 2;
        public const int SilverShare = 6;
        // «Утро» для сроков истории: 6 часов следующего дня.
        public const double MorningHour = 6.0;
        // Сообщение углежогов доходит до поселений (через торг) за столько дней.
        public const int NewsDays = 2;

        public const string ChronicleDeadSeen = "lw_ch_dead_seen";
        public const string ChronicleHunterSafe = "lw_ch_hunter_safe";
        public const string ChronicleHelpSent = "lw_ch_help_sent";
        public const string ChronicleProofRetrieved = "lw_ch_proof_retrieved";
        public const string ChronicleLocalsCollect = "lw_ch_locals_collect";
        public const string ChronicleWithdrawn = "lw_ch_withdrawn";

        private static bool Has(GameState state, string flag) => state?.Narrative != null && state.Narrative.HasFlag(flag);

        private static void Set(GameState state, string flag) => state.Narrative.SetFlag(flag);

        public static string Ref(string nodeId) => DialogueEntryRef.Make(DialogueId, nodeId);

        // Абсолютные игровые часы записи Хроники (её день и час).
        private static double HoursOf(GameState state, string chronicleId)
        {
            ChronicleEntryData entry = Chronicle.Find(state, chronicleId);
            return entry != null ? Math.Max(0, entry.Day - 1) * 24.0 + entry.Hour : double.MaxValue;
        }

        // Утро следующего дня после момента absoluteHours.
        public static double NextMorning(double absoluteHours)
        {
            return Math.Floor(absoluteHours / 24.0) * 24.0 + 24.0 + MorningHour;
        }

        private static bool MorningPassed(GameState state, string chronicleId)
        {
            double since = HoursOf(state, chronicleId);
            return since < double.MaxValue && ContinuousSimulationSystem.GetAbsoluteHours(state) >= NextMorning(since);
        }

        // Срок за оставленной добычей ещё не вышел.
        public static bool ReturnOpen(GameState state)
        {
            return Has(state, Flags.ReturnAvailable) && !Has(state, Flags.ProofDelivered) && !Has(state, Flags.LocalsCollect);
        }

        // ------------------------------------------------------------------
        // Точки входа
        // ------------------------------------------------------------------

        public static LocationEntryView LocationEntry(GameState state, string locationId)
        {
            if (locationId != LocationId || state?.Narrative == null)
                return null;
            if (!Has(state, Flags.Seen))
            {
                return new LocationEntryView
                {
                    DialogueId = Ref(Nodes.Arrival),
                    ButtonText = "ПОДОЙТИ К ВЫВОРОТНЮ",
                    Hint = "У вывороченной ели лежит что-то серое. Рядом сидит человек и не встаёт."
                };
            }
            if (ReturnOpen(state))
            {
                return new LocationEntryView
                {
                    DialogueId = Ref(Nodes.ReturnCorpse),
                    ButtonText = "ЗАБРАТЬ ДОБЫЧУ У ВЫВОРОТНЯ",
                    Hint = "Туша седого волка ждёт до утра. Углежог одолжил жерди и верёвку; весь круг — около двух часов."
                };
            }
            if (Has(state, Flags.ReturnAvailable) && Has(state, Flags.LocalsCollect) && !Has(state, Flags.ProofDelivered) &&
                !Has(state, Flags.ReturnEmptySeen))
            {
                return new LocationEntryView
                {
                    DialogueId = Ref(Nodes.ReturnEmpty),
                    ButtonText = "ЗАГЛЯНУТЬ К ВЫВОРОТНЮ",
                    Hint = "Срок вышел — за добычей собирались сходить углежоги."
                };
            }
            if (Has(state, Flags.HunterSafe) && !Has(state, Flags.Withdrawn) && !Has(state, Flags.HutAgainSeen))
            {
                return new LocationEntryView
                {
                    DialogueId = Ref(Nodes.HutAgain),
                    ButtonText = "ЗАЙТИ НА ХУТОР УГЛЕЖОГОВ",
                    Hint = "Хутор у кромки леса. Савва должен быть там."
                };
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Время, прошедшее за сцену: путь, волокуша, ожидание, второй круг.
        // Каждый отрезок — один раз; UI прокручивает часы обычным ходом.
        // ------------------------------------------------------------------

        public static double HoursAfterDialogue(GameState state, string dialogueId)
        {
            if (dialogueId != DialogueId || state?.Narrative == null)
                return 0.0;
            double hours = 0.0;
            if (Has(state, Flags.ProofLeft) && Pay(state, TimePersonFirst))
                hours += PersonFirstHours;
            bool both = Has(state, Flags.HunterSafe) && Has(state, Flags.ProofDelivered) && !Has(state, Flags.ProofLeft) &&
                        !Has(state, Flags.HelpSent) && !Has(state, Flags.ProofRetrieved);
            if (both && Pay(state, TimeBoth))
                hours += PartyPresence.PresentPartySize(state) >= 2 ? BothHoursWithHelper : BothHoursAlone;
            if (Has(state, Flags.HelpSent) && Pay(state, TimeMessage))
                hours += MessageHours;
            if (Has(state, Flags.WaitedHelpers) && Pay(state, TimeWait))
                hours += WaitHours;
            if (Has(state, Flags.ProofRetrieved) && Pay(state, TimeReturn))
                hours += ReturnHours;
            return hours;
        }

        private static bool Pay(GameState state, string marker)
        {
            if (Has(state, marker))
                return false;
            Set(state, marker);
            return true;
        }

        // ------------------------------------------------------------------
        // Опрос: Хроника, выплаты, отложенные исходы, весть, карта.
        // ------------------------------------------------------------------

        public static List<string> Refresh(GameState state)
        {
            List<string> reports = new List<string>();
            if (state?.Narrative == null)
                return reports;

            // Хозяйка указала тропу — кромка Чёрного леса открыта на карте.
            if (Has(state, Flags.DirectionKnown) || Has(state, Flags.Seen))
            {
                LocationData forest = state.FindLocation(LocationId);
                if (forest != null && (!forest.IsVisibleOnMap || !forest.IsDiscovered))
                {
                    forest.IsVisibleOnMap = true;
                    forest.IsDiscovered = true;
                    if (!Has(state, Flags.Seen))
                        reports.Add("Тропа к углежогам — через ольшаник, к кромке Чёрного леса. Место отмечено на карте.");
                }
            }

            if (Has(state, Flags.DeadSeen))
                Chronicle.Record(state, ChronicleDeadSeen, "Седой волк",
                    "Седой волк мёртв. Савва убил его, но повредил ногу между корней и не может сам вернуться на хутор.", LocationId);
            if (Has(state, Flags.Withdrawn))
                Chronicle.Record(state, ChronicleWithdrawn, "Мимо выворотня",
                    "Мы ушли, не обещая помощи. Савва остался ждать углежогов, которые собирались искать его к вечеру.",
                    LocationId, ChronicleDeadSeen);
            if (Has(state, Flags.HelpSent))
                Chronicle.Record(state, ChronicleHelpSent, "За Саввой пошли",
                    "Мы указали углежогам точное место. Они отправились за Саввой с жердями и верёвкой.", LocationId, ChronicleDeadSeen);

            RefreshDelayed(state, reports);

            // Под крышей — то, что отряд видел сам (после сообщения — дождался
            // людей или застал Савву на хуторе позже).
            if (Has(state, Flags.HunterSafe) && !Has(state, Flags.Withdrawn) &&
                (!Has(state, Flags.HelpSent) || Has(state, Flags.WaitedHelpers) || Has(state, Flags.HutAgainSeen)))
                Chronicle.Record(state, ChronicleHunterSafe, "Савва на хуторе",
                    Has(state, Flags.HelpSent)
                        ? "Углежоги вынесли Савву из леса по нашему слову. Охотник под крышей."
                        : "Охотник добрался под крышу: мы довели его до хутора углежогов.",
                    LocationId, ChronicleDeadSeen);
            if (Has(state, Flags.ProofRetrieved))
                Chronicle.Record(state, ChronicleProofRetrieved, "Добыча доставлена",
                    "После спасения Саввы мы отдельно вернулись за волком. Углежог проверил добычу и рассчитался.", LocationId,
                    ChronicleHunterSafe);
            if (Has(state, Flags.LocalsCollect) && !Has(state, Flags.Withdrawn))
                Chronicle.Record(state, ChronicleLocalsCollect, "За добычей сходили местные",
                    "Тушу седого волка вынесли углежоги. Плата за охоту — у Саввы; помощь человеку от этого не стала меньше.",
                    LocationId, ChronicleHunterSafe);

            PayRewards(state, reports);
            RefreshNews(state);
            return reports;
        }

        // Еда — один раз за помощь (любой путь, где углежог её отдаёт), серебро —
        // доля за вынесенную добычу, если от неё не отказались.
        private static void PayRewards(GameState state, List<string> reports)
        {
            bool fed = Has(state, Flags.MessageEnding) || Has(state, Flags.ReturnAvailable) || Has(state, Flags.RescueComplete) ||
                       Has(state, Flags.FeePaid) || Has(state, Flags.FeeWaived);
            if (fed && !Has(state, Flags.FoodPaid))
            {
                Set(state, Flags.FoodPaid);
                state.ArmySupply += FoodReward;
                reports.Add("Хлеб и солёная рыба от углежогов: +" + FoodReward + " припаса.");
            }
            if (Has(state, Flags.FeePaid) && !Has(state, Flags.FeeRefused) && !Has(state, Flags.RewardPaid))
            {
                Set(state, Flags.RewardPaid);
                state.ArmyGold += SilverShare;
                reports.Add("Доля за вынесенного волка — рубленое серебро: +" + SilverShare + " зол.");
            }
        }

        // Отложенные исходы: местные выносят Савву к утру после сообщения или
        // ухода героя; срок за добычей истекает утром — тушу забирают углежоги.
        private static void RefreshDelayed(GameState state, List<string> reports)
        {
            if (Has(state, Flags.HelpSent) && !Has(state, Flags.HunterSafe) && MorningPassed(state, ChronicleHelpSent))
            {
                Set(state, Flags.HunterSafe);
                Set(state, Flags.ProofDelivered);
            }
            if (Has(state, Flags.Withdrawn) && !Has(state, Flags.HunterSafe) && MorningPassed(state, ChronicleWithdrawn))
            {
                // Вечерний поиск углежогов: отряд об этом не знает и не узнаёт
                // без свидетеля — ни донесения, ни записи.
                Set(state, Flags.HunterSafe);
                Set(state, Flags.LocalsCollect);
                Set(state, Flags.DeathReported);
                Set(state, Flags.Closed);
            }
            if (ReturnOpen(state) && MorningPassed(state, ChronicleHunterSafe))
            {
                Set(state, Flags.LocalsCollect);
                Set(state, Flags.Closed);
                reports.Add("Утро: углежоги сами сходили за тушей седого волка. Плата осталась Савве — уговор был до утра.");
            }
        }

        // A-04: весть о гибели волка доходит до поселений только по каналу —
        // рассказ героя хозяйке избы или сообщение углежогов через NewsDays дней
        // после того, как они узнали о мёртвом звере.
        private static void RefreshNews(GameState state)
        {
            if (Has(state, Flags.NewsLocal))
                return;
            bool told = Has(state, Flags.RumorShared);
            ChronicleEntryData met = Chronicle.Find(state, ChronicleDeadSeen);
            bool carried = Has(state, Flags.DeathReported) && Has(state, Flags.HunterSafe) && met != null && state.Day >= met.Day + NewsDays;
            if (told || carried)
                Set(state, Flags.NewsLocal);
        }

        // ------------------------------------------------------------------
        // «Дело»
        // ------------------------------------------------------------------

        public static JournalGoalViewData BuildGoal(GameState state)
        {
            if (!Has(state, Flags.Seen))
                return null;
            string step;
            JournalGoalState goalState = JournalGoalState.Active;
            if (ReturnOpen(state))
            {
                step = "Савва под крышей. При желании — забрать тушу у выворотня до утра (около двух часов).";
            }
            else if (Has(state, Flags.Withdrawn))
            {
                step = "Мы ушли, ничего не обещая. Савва ждёт углежогов.";
                goalState = JournalGoalState.Completed;
            }
            else if (Has(state, Flags.HelpSent) && !Has(state, Flags.HunterSafe))
            {
                step = "Углежоги пошли за Саввой по нашему слову.";
                goalState = Has(state, Flags.Closed) ? JournalGoalState.Completed : JournalGoalState.Active;
            }
            else if (Has(state, Flags.HunterSafe))
            {
                step = Has(state, Flags.ProofDelivered)
                    ? "Савва и добыча на хуторе. Дело закончено."
                    : Has(state, Flags.LocalsCollect)
                        ? "Савва на хуторе, за добычей сходили местные. Дело закончено."
                        : "Савва на хуторе. Дело закончено.";
                goalState = JournalGoalState.Completed;
            }
            else
            {
                step = "Решить, как помочь раненому охотнику у выворотня.";
            }
            return new JournalGoalViewData
            {
                Id = "freeplay.goal.last_wolf",
                Title = "Последний волк",
                Description = "У выворотня на кромке Чёрного леса — мёртвый седой волк и старый охотник Савва с повреждённой ногой. " +
                              "До хутора углежогов — полчаса налегке.",
                CurrentStep = step,
                RevisionId = "freeplay.goal.last_wolf." + (goalState == JournalGoalState.Completed ? "done" : "active") +
                             (ReturnOpen(state) ? ".return" : string.Empty),
                Category = JournalGoalCategory.Optional,
                State = goalState
            };
        }
    }
}
