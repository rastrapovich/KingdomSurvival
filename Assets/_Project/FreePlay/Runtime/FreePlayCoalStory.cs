using System;
using System.Collections.Generic;

namespace KingdomSurvival.FreePlay
{
    // ПР-12Б, история И-1 «Уголь для Лады» (ProjectDocs/PR12BV_FREE_PLAY_CONTENT_SPEC.md
    // §4; лор — LORE.md §17.1: хутор углежогов, звери Чёрного леса).
    // [РАБОЧЕЕ]: утверждено к реализации, не канон мира.
    //
    // Ход: Лада — угля на две правки, без него ремонт двора вдвое медленнее →
    // разговор открывает хутор у кромки Чёрного леса → на хуторе у ям ходит
    // старый вожак: отогнать (бой), поставить огневую изгородь (Лада в
    // отряде) или обменять хлеб Дома на уголь → уголь возвращает ремонту
    // полную скорость. Отход или поражение — хутор уходит к дальним ямам,
    // обмен вдвое дороже.
    public static class FreePlayCoalStory
    {
        public const string LocationId = "freeplay.coalburners";
        public const string LocationName = "Хутор углежогов";
        public const string ForestLocationId = "forest";

        public const string LadaDialogueId = "freeplay_coal_lada";
        public const string HutorDialogueId = "freeplay_coal_hutor";
        public const string HutorAfterDialogueId = "freeplay_coal_hutor_after";
        public const string BattleId = "freeplay.battle.coal_pits_leader";

        public static class Flags
        {
            // Лада рассказала о хуторе — он на карте.
            public const string Asked = "freeplay.coal.asked";
            // На хуторе: выбран путь.
            public const string Hunt = "freeplay.coal.hunt";
            public const string Trail = "freeplay.coal.trail";
            public const string Fence = "freeplay.coal.fence";
            public const string Trade = "freeplay.coal.trade";
            public const string TradeDouble = "freeplay.coal.trade_double";
            // Итог боя.
            public const string LeaderDriven = "freeplay.coal.leader_driven";
            public const string HutorMoved = "freeplay.coal.hutor_moved";
            // Уголь в Доме.
            public const string Coal = "freeplay.coal.coal";
            // Отложенные последствия: углежоги пришли сами; вожак вышел к
            // дороге; вожак убит на своей тропе.
            public const string Visited = "freeplay.coal.visited";
            public const string LeaderOnRoad = "freeplay.coal.leader_on_road";
            public const string LeaderKilled = "freeplay.coal.leader_killed";
        }

        // Партия свободной игры (для встреч общего пула, которые бывают
        // только здесь, — «Дым над лесом»).
        public const string ModeFlag = "freeplay.mode";

        public const string TrailDialogueId = "freeplay_coal_leader_trail";
        public const string SmokeDialogueId = "freeplay_coal_smoke";
        public const string TrailBattlePrefix = "freeplay.battle.leader_trail.";

        // Через сколько дней после угля приходят последствия.
        public const int AftermathDays = 7;
        public const int VisitSupplies = 4;

        // Без угля Лада гнёт скобы руками: работы Дома вдвое медленнее.
        public const double WorkRateWithoutCoal = 0.5;
        public const int TradeFood = 6;
        public const int TradeFoodAfterRetreat = 12;
        public const int FenceSupplies = 2;

        private const string ChronicleCoal = "freeplay.coal.chronicle.coal";
        private const string ChronicleMoved = "freeplay.coal.chronicle.moved";
        private const string ChronicleVisit = "freeplay.coal.chronicle.visit";
        private const string ChronicleLeaderRoad = "freeplay.coal.chronicle.leader_road";
        private const string ChronicleLeaderKilled = "freeplay.coal.chronicle.leader_killed";

        // Хутору помогли: вожак отогнан или убит, или ямы обнесены огнём.
        public static bool Helped(GameState state)
        {
            return Has(state, Flags.LeaderDriven) || Has(state, Flags.Fence) || Has(state, Flags.LeaderKilled);
        }

        // Вожак жив и его можно искать на тропе в Чёрном лесу.
        public static bool IsLeaderTrailOpen(GameState state)
        {
            return !Has(state, Flags.LeaderDriven) && !Has(state, Flags.LeaderKilled) &&
                   (Has(state, Flags.HutorMoved) || Has(state, Flags.LeaderOnRoad));
        }

        private static int DayOf(GameState state, string chronicleId)
        {
            ChronicleEntryData entry = Chronicle.Find(state, chronicleId);
            return entry != null ? entry.Day : int.MaxValue / 2;
        }

        private static bool Has(GameState state, string flag)
        {
            return state?.Narrative != null && state.Narrative.HasFlag(flag);
        }

        public static bool HasCoal(GameState state) => Has(state, Flags.Coal);

        public static LocationData FindHutor(GameState state) => state?.FindLocation(LocationId);

        // ------------------------------------------------------------------
        // Опрос: хутор появляется на карте после разговора с Ладой; обмен и
        // изгородь приносят уголь. Возвращает донесения.
        // ------------------------------------------------------------------

        public static List<string> Refresh(GameState state)
        {
            List<string> reports = new List<string>();
            if (state?.Narrative == null)
                return reports;

            if (!Has(state, ModeFlag))
                state.Narrative.SetFlag(ModeFlag);
            if (Has(state, Flags.Asked))
                EnsureHutor(state);
            RefreshAftermath(state, reports);

            bool paid = Has(state, Flags.Fence) || Has(state, Flags.Trade) || Has(state, Flags.TradeDouble) ||
                        Has(state, Flags.LeaderDriven) || Has(state, Flags.LeaderKilled);
            if (paid && !HasCoal(state))
            {
                state.Narrative.SetFlag(Flags.Coal);
                string how = Has(state, Flags.LeaderKilled)
                    ? "за убитого вожака"
                    : Has(state, Flags.LeaderDriven)
                    ? "за отогнанного вожака"
                    : Has(state, Flags.Fence)
                        ? "за огневую изгородь вокруг ям"
                        : "в обмен на хлеб Дома";
                string text = "Углежоги отдали уголь " + how + ". Лада снова правит скобы в огне — ремонт пойдёт с прежней скоростью.";
                Chronicle.Record(state, ChronicleCoal, "Уголь для Лады", text, LocationId);
                reports.Add(text);
            }
            return reports;
        }

        // Отложенные последствия (ТЗ §4, И-1): через неделю после того, как
        // хутору помогли, углежоги приходят сами; если уголь просто купили,
        // вожак через неделю выходит к дороге. Отход у ям сразу оставляет
        // вожака на его тропе.
        private static void RefreshAftermath(GameState state, List<string> reports)
        {
            if (Helped(state) && !Has(state, Flags.Visited))
            {
                int since = Has(state, Flags.LeaderKilled) ? DayOf(state, ChronicleLeaderKilled) : DayOf(state, ChronicleCoal);
                if (state.Day >= since + AftermathDays)
                {
                    state.Narrative.SetFlag(Flags.Visited);
                    state.ArmySupply += VisitSupplies;
                    string text = "Углежоги пришли в Дом сами — с мешком угля и связкой вяленого мяса в дорогу (+" + VisitSupplies +
                                  " припаса). Лада забирает мешок к себе раньше, чем его успевают поставить на землю.";
                    Chronicle.Record(state, ChronicleVisit, "Углежоги пришли сами", text, LocationId, ChronicleCoal);
                    reports.Add(text);
                }
            }

            bool boughtOnly = HasCoal(state) && !Helped(state) && !Has(state, Flags.HutorMoved);
            if (boughtOnly && !Has(state, Flags.LeaderOnRoad) && state.Day >= DayOf(state, ChronicleCoal) + AftermathDays)
            {
                state.Narrative.SetFlag(Flags.LeaderOnRoad);
                string text = "Старый вожак ушёл от угольных ям к дороге: у кромки Чёрного леса нашли задранную овцу. Его тропу можно искать в лесу.";
                Chronicle.Record(state, ChronicleLeaderRoad, "Вожак вышел к дороге", text, ForestLocationId, ChronicleCoal);
                reports.Add(text);
            }

            if (IsLeaderTrailOpen(state))
            {
                LocationData forest = state.FindLocation(ForestLocationId);
                if (forest != null && (!forest.IsVisibleOnMap || !forest.IsDiscovered))
                {
                    forest.IsVisibleOnMap = true;
                    forest.IsDiscovered = true;
                }
            }
        }

        // Хутор у кромки Чёрного леса: чуть ближе к Дому, чем сам лес, и
        // там, куда есть путь.
        public static LocationData EnsureHutor(GameState state)
        {
            LocationData existing = FindHutor(state);
            if (existing != null)
                return existing;

            LocationData forest = state.FindLocation(ForestLocationId);
            float targetX = forest != null ? forest.MapXPercent : WorldMapNavigation.CapitalXPercent + 20f;
            float targetY = forest != null ? forest.MapYPercent : WorldMapNavigation.CapitalYPercent;
            float candidateX = targetX + (WorldMapNavigation.CapitalXPercent - targetX) * 0.2f;
            float candidateY = targetY + (WorldMapNavigation.CapitalYPercent - targetY) * 0.2f;

            List<MapPointData> route = WorldMapNavigation.FindPath(
                WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent, candidateX, candidateY);
            float finalX = candidateX;
            float finalY = candidateY;
            if (route.Count > 0)
            {
                finalX = route[route.Count - 1].XPercent;
                finalY = route[route.Count - 1].YPercent;
            }

            LocationData hutor = new LocationData(
                LocationId,
                LocationName,
                ContinuousSimulationSystem.CalculateTravelHours(route),
                "средняя",
                explorationHours: 1.0)
            {
                ResearchResultText = "Ямы дымят, в корытах стынет дёготь. Люди хутора работают молча и поглядывают на ельник.",
                RegionId = "freeplay-coalburners",
                RegionName = GameState.GetRegionName(finalX, finalY),
                MapSlotIndex = state.Locations.Count,
                MapXPercent = finalX,
                MapYPercent = finalY,
                IsDiscovered = true,
                IsVisibleOnMap = true,
                InteractionDescription =
                    "Дёгтем пахнет раньше, чем хутор показывается из-за ольхи. Над ямами стоит дым, у дальней ямы — никого."
            };
            state.Locations.Add(hutor);
            return hutor;
        }

        // ------------------------------------------------------------------
        // Сцена при входе на хутор
        // ------------------------------------------------------------------

        public static LocationEntryView LocationEntry(GameState state, string locationId)
        {
            // Тропа вожака в Чёрном лесу — пока он жив и туда стоит идти.
            if (locationId == ForestLocationId)
            {
                return IsLeaderTrailOpen(state)
                    ? new LocationEntryView
                    {
                        DialogueId = TrailDialogueId,
                        ButtonText = "ИДТИ ПО ТРОПЕ ВОЖАКА",
                        Hint = "Найти старого вожака на его тропе и покончить с этим."
                    }
                    : null;
            }

            if (locationId != LocationId || HasCoal(state) || Has(state, Flags.Hunt) && !Has(state, Flags.HutorMoved))
                return null;
            return new LocationEntryView
            {
                DialogueId = Has(state, Flags.HutorMoved) ? HutorAfterDialogueId : HutorDialogueId,
                ButtonText = "ПОДОЙТИ К УГЛЕЖОГАМ",
                Hint = Has(state, Flags.HutorMoved)
                    ? "Хутор перебрался к дальним ямам. Уголь ещё можно выменять."
                    : "Поговорить с углежогами об угле для Лады."
            };
        }

        // ------------------------------------------------------------------
        // Бой «Вожак у угольных ям»
        // ------------------------------------------------------------------

        public static CampaignBattleRequest BattleAfterDialogue(GameState state, string dialogueId)
        {
            if (dialogueId == TrailDialogueId)
                return IsLeaderTrailOpen(state) && state.HasActiveExpedition ? CreateTrailRequest(state) : null;

            if (dialogueId != HutorDialogueId || !Has(state, Flags.Hunt) || HasCoal(state) || Has(state, Flags.HutorMoved) ||
                CampaignBattleBridge.IsApplied(state, BattleId) || !state.HasActiveExpedition)
                return null;
            return CreateRequest(state);
        }

        public static CampaignBattleRequest CreateRequest(GameState state)
        {
            CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, BattleId);
            request.SourceId = HutorDialogueId;
            request.AllowRetreat = true;
            request.Enemies.Add(new CampaignBattleEnemy { UnitTypeId = "forest_beast_alpha", Count = 1 });
            request.Enemies.Add(new CampaignBattleEnemy { UnitTypeId = "forest_beast", Count = 1 });
            // Агнесса в отряде или найденная тропа — зверя ждут там, где он
            // пойдёт: подготовленное начало («Засада»).
            request.PreparedStart = Has(state, Flags.Trail) || CampRest.PartyIds(state).Contains(CampRest.AgnessaId);
            FeatureCombatBatch.ApplyPreparedStart(state, request);
            return request;
        }

        // «Тропа вожака»: вожак и крупный зверь; после отхода можно прийти
        // снова — у каждой попытки свой ID (итог применяется один раз).
        public static CampaignBattleRequest CreateTrailRequest(GameState state)
        {
            int attempt = 1;
            while (CampaignBattleBridge.IsApplied(state, TrailBattlePrefix + attempt))
                attempt++;
            CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, TrailBattlePrefix + attempt);
            request.SourceId = TrailDialogueId;
            request.AllowRetreat = true;
            request.Enemies.Add(new CampaignBattleEnemy { UnitTypeId = "forest_beast_alpha", Count = 1 });
            request.Enemies.Add(new CampaignBattleEnemy { UnitTypeId = "forest_beast_strong", Count = 1 });
            // Агнесса в отряде — вожака ждут там, где он пойдёт.
            request.PreparedStart = CampRest.PartyIds(state).Contains(CampRest.AgnessaId);
            FeatureCombatBatch.ApplyPreparedStart(state, request);
            return request;
        }

        public static void BattleApplied(GameState state, CampaignBattleResult result, List<string> reports)
        {
            if (state?.Narrative == null || result == null)
                return;

            if (result.BattleId != null && result.BattleId.StartsWith(TrailBattlePrefix, StringComparison.Ordinal))
            {
                if (result.Outcome != CampaignBattleOutcome.Victory)
                {
                    reports?.Add("Вожак ушёл в ельник. Тропа никуда не делась — можно вернуться с другими людьми.");
                    return;
                }
                state.Narrative.SetFlag(Flags.LeaderKilled);
                string killed = "Старый вожак убит на своей тропе. У угольных ям и на дороге станет тише.";
                Chronicle.Record(state, ChronicleLeaderKilled, "Вожак убит", killed, ForestLocationId,
                    Has(state, Flags.LeaderOnRoad) ? ChronicleLeaderRoad : ChronicleMoved);
                reports?.Add(killed);
                return;
            }

            if (result.BattleId != BattleId)
                return;

            if (result.Outcome == CampaignBattleOutcome.Victory)
            {
                state.Narrative.SetFlag(Flags.LeaderDriven);
                reports?.Add("Вожак ушёл в ельник и не вернулся к ямам. Углежоги выносят мешки угля.");
                return;
            }

            state.Narrative.SetFlag(Flags.HutorMoved);
            string text = "Вожак остался у ям. Углежоги перебираются к дальним ямам — уголь теперь достанется вдвое дороже.";
            Chronicle.Record(state, ChronicleMoved, "Хутор ушёл к дальним ямам", text, LocationId);
            reports?.Add(text);
        }

        // ------------------------------------------------------------------
        // Дом: скорость работ и заботы
        // ------------------------------------------------------------------

        public static double HomeWorkRate(GameState state)
        {
            return HasCoal(state) ? 1.0 : WorkRateWithoutCoal;
        }

        public static void AddHomeCares(GameState state, List<HomeCareView> cares)
        {
            AddYardDeck(state, cares);
            if (HasCoal(state))
                return;

            bool asked = Has(state, Flags.Asked);
            cares.Add(new HomeCareView
            {
                Id = "home.care.freeplay_coal",
                Title = "Уголь у Лады",
                Cause = "Угля на две правки. Без него Лада гнёт скобы руками.",
                Status = "Работы Дома идут вдвое медленнее.",
                Detail = asked
                    ? Has(state, Flags.HutorMoved)
                        ? "Хутор углежогов перебрался к дальним ямам: уголь достанется вдвое дороже."
                        : "Хутор углежогов — у кромки Чёрного леса, он на карте."
                    : "Лада знает, где взять уголь.",
                Priority = HomeCares.PriorityOptional,
                ActionLabel = asked ? string.Empty : "Поговорить с Ладой",
                Action = asked ? HomeCareAction.None : HomeCareAction.OpenDialogue,
                DialogueId = asked ? string.Empty : LadaDialogueId,
                ActionEnabled = !asked
            });
        }

        // Хозяйственный настил во дворе: в свободной игре он просто прогнил.
        private static void AddYardDeck(GameState state, List<HomeCareView> cares)
        {
            HomeWorkState deck = HomeLife.FindWork(state, HomeLife.YardDeckWorkId);
            HomeFunctionReport maintenance = HomeFunctionResolver.Resolve(state, HomeFunctionResolver.MaintenanceId);
            double rate = maintenance.Rate * HomeWorkRate(state);
            string slow = HasCoal(state) ? string.Empty : " Без угля — вдвое медленнее.";

            if (deck == null)
            {
                bool atHome = !HomePeopleService.HasDeparted(state);
                double hours = rate > 0.0 ? HomeLife.YardDeckRequiredWork / rate : 0.0;
                cares.Add(new HomeCareView
                {
                    Id = "home.care.yard_deck",
                    Title = "Хозяйственный настил во дворе",
                    Cause = "Настил прогнил: по двору обходят лужи.",
                    Status = rate > 0.0
                        ? "Сделает " + maintenance.ExecutorName + " — около " + (int)Math.Ceiling(hours) + " ч." + slow
                        : "Сейчас некому: " + maintenance.Reason + ".",
                    Detail = "Цена: " + HomeLife.YardDeckGoldCost + " золота один раз.",
                    Priority = HomeCares.PriorityOptional,
                    ActionLabel = "Восстановить · " + HomeLife.YardDeckGoldCost + " золота",
                    Action = HomeCareAction.StartYardDeck,
                    ActionEnabled = atHome && state.Gold >= HomeLife.YardDeckGoldCost
                });
                return;
            }

            if (deck.Completed)
                return;

            double remaining = HomeLife.RemainingWork(deck);
            cares.Add(new HomeCareView
            {
                Id = "home.care.yard_deck",
                Title = "Хозяйственный настил во дворе",
                Cause = "Ремонт начат.",
                Status = rate > 0.0
                    ? "Работает " + maintenance.ExecutorName + " — осталось около " + (int)Math.Ceiling(remaining / rate) + " ч." + slow
                    : "Приостановлен: " + maintenance.Reason + ". Сделанное не пропадёт.",
                Priority = rate > 0.0 ? HomeCares.PriorityRunning : HomeCares.PriorityNeed,
                Urgent = rate <= 0.0
            });
        }

        // ------------------------------------------------------------------
        // «Дело»
        // ------------------------------------------------------------------

        // «Старый вожак» — пока вожак жив и ходит у леса, и после его смерти.
        public static JournalGoalViewData BuildLeaderGoal(GameState state)
        {
            bool killed = Has(state, Flags.LeaderKilled);
            if (!killed && !IsLeaderTrailOpen(state))
                return null;
            return new JournalGoalViewData
            {
                Id = "freeplay.goal.coal_leader",
                Title = "Старый вожак",
                Description = "Седой по хребту, огня не боится и научился брать у людей. " +
                              (Has(state, Flags.HutorMoved) ? "Из-за него углежоги ушли к дальним ямам." : "Теперь он ходит к дороге."),
                CurrentStep = killed
                    ? "Вожак убит на своей тропе."
                    : "Идти в Чёрный лес по его тропе. С Агнессой его ждут там, где он пойдёт.",
                RevisionId = "freeplay.goal.coal_leader." + (killed ? "done" : "open"),
                Category = JournalGoalCategory.Optional,
                State = killed ? JournalGoalState.Completed : JournalGoalState.Active
            };
        }

        public static JournalGoalViewData BuildGoal(GameState state)
        {
            string step;
            string stage;
            JournalGoalState goalState = JournalGoalState.Active;
            if (HasCoal(state))
            {
                step = "Уголь в Доме — Лада правит скобы, ремонт идёт с прежней скоростью.";
                stage = "done";
                goalState = JournalGoalState.Completed;
            }
            else if (Has(state, Flags.HutorMoved))
            {
                step = "Вожак остался у ям, хутор перебрался к дальним. Уголь можно выменять — вдвое дороже.";
                stage = "moved";
            }
            else if (Has(state, Flags.Asked))
            {
                step = "Дойти до хутора углежогов у кромки Чёрного леса и договориться об угле.";
                stage = "asked";
            }
            else
            {
                step = "Поговорить с Ладой на экране Дома: где взять уголь.";
                stage = "home";
            }

            return new JournalGoalViewData
            {
                Id = "freeplay.goal.coal",
                Title = "Уголь для Лады",
                Description = "Лада правит скобы для ремонта в огне, а угля осталось на две правки. " +
                              "Без него работы Дома идут вдвое медленнее.",
                CurrentStep = step,
                RevisionId = "freeplay.goal.coal." + stage,
                Category = JournalGoalCategory.Optional,
                State = goalState
            };
        }
    }
}
