using System;
using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
    // Единый реестр стабильных ID Главы 01 (раздел 7 сводной инструкции).
    // Один префикс chapter01.* для всех новых данных — не смешивать с
    // устаревшими a1_/k_a1_/water_ префиксами. Уже выданные ID не
    // переименовывать без миграции сохранений.
    //
    // Списки флагов/знаний ниже включают "минимальный" набор из раздела 7
    // инструкции плюс несколько технических флагов завершения узла,
    // которых не было в минимальном списке (отмечены отдельно) — они нужны
    // Chapter01StoryDirector для последовательности N02/N06/N11/N15, у
    // которых в разделе 7 нет собственного явного флага. Список "уточняется
    // по мере производства" (раздел 7, последний абзац), поэтому это не
    // нарушение уже принятых решений.
    public static class Chapter01Ids
    {
        public static class Nodes
        {
            public const string N01 = "chapter01.node.01";
            public const string N02 = "chapter01.node.02";
            public const string N03 = "chapter01.node.03";
            public const string N04 = "chapter01.node.04";
            public const string N05 = "chapter01.node.05";
            public const string N06 = "chapter01.node.06";
            public const string N07A = "chapter01.node.07a";
            public const string N07B = "chapter01.node.07b";
            public const string N07C = "chapter01.node.07c";
            public const string N08 = "chapter01.node.08";
            public const string N09 = "chapter01.node.09";
            public const string N10 = "chapter01.node.10";
            public const string N11 = "chapter01.node.11";
            public const string N12 = "chapter01.node.12";
            public const string N13 = "chapter01.node.13";
            public const string N14 = "chapter01.node.14";
            public const string N14Half = "chapter01.node.14_5";
            public const string N15 = "chapter01.node.15";
            public const string N16 = "chapter01.node.16";
            public const string N17 = "chapter01.node.17";

            public static readonly IReadOnlyList<string> All = new[]
            {
                N01, N02, N03, N04, N05, N06, N07A, N07B, N07C, N08,
                N09, N10, N11, N12, N13, N14, N14Half, N15, N16, N17
            };
        }

        public static class Dialogues
        {
            public const string D01 = "chapter01_dialogue_01_ordinary_morning";
            public const string D02 = "chapter01_dialogue_02_people_of_the_house";
            public const string D03 = "chapter01_dialogue_03_first_pressure";
            public const string D04 = "chapter01_dialogue_04_flood";
            public const string D05 = "chapter01_dialogue_05_wet_plan";
            public const string D06 = "chapter01_dialogue_06_wrong_water";
            public const string D07A = "chapter01_dialogue_07a_mill";
            public const string D07B = "chapter01_dialogue_07b_cattle";
            public const string D07C = "chapter01_dialogue_07c_river";
            public const string D08 = "chapter01_dialogue_08_seven_teeth";
            public const string D09 = "chapter01_dialogue_09_council_departure";
            public const string D10 = "chapter01_dialogue_10_gather_party";
            public const string D11 = "chapter01_dialogue_11_first_long_road";

            // P09-T02/T03: две дорожные встречи первого дальнего похода —
            // не часть линейной N01-N17 последовательности
            // Chapter01StoryDirector.Sequence (у них нет своего NodeStep),
            // а side-контент, триггеруемый физическим прогрессом маршрута
            // (см. Chapter01StoryDirector.GetPendingRoadEventDialogueId).
            public const string D11B = "chapter01_dialogue_11b_three_under_cart";
            public const string D11C = "chapter01_dialogue_11c_camp_after_cart";

            public const string D12 = "chapter01_dialogue_12_old_ford";
            public const string D13 = "chapter01_dialogue_13_other_people";
            public const string D14 = "chapter01_dialogue_14_agreement_revealed";
            public const string D14Half = "chapter01_dialogue_14b_continue_or_return";
            public const string D15 = "chapter01_dialogue_15_changed_return_road";
            public const string D16 = "chapter01_dialogue_16_home_again";
            public const string D17 = "chapter01_dialogue_17_council_of_the_house";

            public static readonly IReadOnlyList<string> All = new[]
            {
                D01, D02, D03, D04, D05, D06, D07A, D07B, D07C, D08,
                D09, D10, D11, D11B, D11C, D12, D13, D14, D14Half, D15, D16, D17
            };
        }

        public static class Flags
        {
            // --- Минимальный список раздела 7 ---
            public const string Started = "chapter01.flag.started";
            public const string HomeIntroSeen = "chapter01.flag.home_intro_seen";
            public const string FirstPressureSeen = "chapter01.flag.first_pressure_seen";
            public const string FloodHappened = "chapter01.flag.flood_happened";
            public const string FloodWorkersSaved = "chapter01.flag.flood_workers_saved";
            public const string FloodLivestockLost = "chapter01.flag.flood_livestock_lost";
            public const string FloodMillDeckDestroyed = "chapter01.flag.flood_mill_deck_destroyed";
            public const string HeroInjuredByFlood = "chapter01.flag.hero_injured_by_flood";
            public const string DamInspected = "chapter01.flag.dam_inspected";
            public const string RepairOld = "chapter01.flag.repair_old";
            public const string RepairNew = "chapter01.flag.repair_new";
            public const string RepairCompleted = "chapter01.flag.repair_completed";
            public const string WaterWrongActive = "chapter01.flag.water_wrong_active";
            public const string InvestigatedMill = "chapter01.flag.investigated_mill";
            public const string InvestigatedCattle = "chapter01.flag.investigated_cattle";
            public const string InvestigatedRiver = "chapter01.flag.investigated_river";
            public const string OldTraceFound = "chapter01.flag.old_trace_found";
            public const string FarRouteUnlocked = "chapter01.flag.far_route_unlocked";
            public const string ExpeditionStarted = "chapter01.flag.expedition_started";
            public const string OldFordFound = "chapter01.flag.old_ford_found";
            public const string DownstreamContact = "chapter01.flag.downstream_contact";
            public const string AgreementRevealed = "chapter01.flag.agreement_revealed";
            public const string ReturnStarted = "chapter01.flag.return_started";
            public const string ReturnedHome = "chapter01.flag.returned_home";
            public const string CouncilCompleted = "chapter01.flag.council_completed";
            public const string Completed = "chapter01.flag.completed";

            // --- Дополнительные технические флаги завершения узла ---
            // У N02/N11/N15 в разделе 7 инструкции нет собственного флага
            // завершения (только косвенные state-флаги соседних узлов).
            // Chapter01StoryDirector требует ровно одного флага на узел для
            // последовательности — добавлены три минимальных технических.
            public const string HousePeopleMet = "chapter01.flag.house_people_met";
            public const string LongRoadStarted = "chapter01.flag.long_road_started";
            public const string ReturnRoadTraveled = "chapter01.flag.return_road_traveled";

            // P09-T01/T02/T03: первая дальняя дорога и лагерь ("движение =
            // течение времени" + "Трое под телегой" производственная
            // инструкция). RoadDestinationReached — физическое прибытие в
            // OldWaterSearch (не то же самое, что открытие области поиска в
            // P08 — там раскрывается только точка на карте, здесь — реальный
            // приход туда). FollowedOldRoad/CrossedOldRoadBoundary —
            // взаимоисключающий выбор маршрута в N11, не может быть оба
            // сразу. OldRoadDetourInProgress — чисто технический маркер
            // "герой физически идёт к временной точке крюка, после которой
            // нужно самостоятельно продолжить к настоящей цели" (см.
            // Chapter01StoryDirector.TryContinueOldRoadDetourIfArrived), не
            // часть производственного содержания. CartOutcome* — ровно один
            // из четырёх взаимоисключающих исходов "Троих под телегой".
            public const string RoadDestinationReached = "chapter01.flag.road_destination_reached";
            public const string FollowedOldRoad = "chapter01.flag.followed_old_road";
            public const string CrossedOldRoadBoundary = "chapter01.flag.crossed_old_road_boundary";
            public const string OldRoadDetourInProgress = "chapter01.flag.old_road_detour_in_progress";
            public const string CartResolved = "chapter01.flag.cart_resolved";
            public const string CartOutcomeSavedBoth = "chapter01.flag.cart_outcome_saved_both";
            public const string CartOutcomeSavedMan = "chapter01.flag.cart_outcome_saved_man";
            public const string CartOutcomeSavedSeed = "chapter01.flag.cart_outcome_saved_seed";
            public const string CartOutcomePassedBy = "chapter01.flag.cart_outcome_passed_by";
            public const string CartCampEchoSeen = "chapter01.flag.cart_camp_echo_seen";

            // Лагерь — постоянная игровая возможность интерфейса, а не
            // сюжетный факт мира (раздел "Где хранить разблокировку" P09M-
            // инструкции про лагерь): намеренно не chapter01.knowledge.*.
            public const string CampUnlocked = "chapter01.flag.camp_unlocked";

            // P04-T04: маркер того, что игрок действительно увидел исходную
            // норму Дома в N01 (Chapter01HomeState.Baseline) — не само
            // состояние воды/мельницы/скота/настила, а подтверждение показа.
            // N16 не должно строить рифму с состоянием, которого игрок не видел.
            public const string HomeBaselineCaptured = "chapter01.flag.home_baseline_captured";

            public static readonly IReadOnlyList<string> All = new[]
            {
                Started, HomeIntroSeen, FirstPressureSeen, FloodHappened, FloodWorkersSaved,
                FloodLivestockLost, FloodMillDeckDestroyed, HeroInjuredByFlood, DamInspected,
                RepairOld, RepairNew, RepairCompleted, WaterWrongActive, InvestigatedMill,
                InvestigatedCattle, InvestigatedRiver, OldTraceFound, FarRouteUnlocked,
                ExpeditionStarted, OldFordFound, DownstreamContact, AgreementRevealed,
                ReturnStarted, ReturnedHome, CouncilCompleted, Completed,
                HousePeopleMet, LongRoadStarted, ReturnRoadTraveled, HomeBaselineCaptured,
                RoadDestinationReached, FollowedOldRoad, CrossedOldRoadBoundary,
                OldRoadDetourInProgress, CartResolved, CartOutcomeSavedBoth,
                CartOutcomeSavedMan, CartOutcomeSavedSeed, CartOutcomePassedBy,
                CartCampEchoSeen, CampUnlocked
            };
        }

        public static class Knowledge
        {
            public const string SecondLoafIsRation = "chapter01.knowledge.second_loaf_is_ration";
            public const string WaterFlowIsWrong = "chapter01.knowledge.water_flow_is_wrong";
            public const string MillMovesAtWrongTime = "chapter01.knowledge.mill_moves_at_wrong_time";
            public const string CattleAvoidOldBranch = "chapter01.knowledge.cattle_avoid_old_branch";
            public const string FishPatternChanged = "chapter01.knowledge.fish_pattern_changed";
            public const string OldSeventhChannel = "chapter01.knowledge.old_seventh_channel";
            public const string OldFord = "chapter01.knowledge.old_ford";
            public const string OldCustom = "chapter01.knowledge.old_custom";
            public const string DrownedWomanStory = "chapter01.knowledge.drowned_woman_story";
            public const string SevenToothObject = "chapter01.knowledge.seven_tooth_object";
            public const string DownstreamPeople = "chapter01.knowledge.downstream_people";
            public const string OldAgreement = "chapter01.knowledge.old_agreement";
            public const string SharedWaterSystem = "chapter01.knowledge.shared_water_system";
            public const string HomeWasNotSelfSufficient = "chapter01.knowledge.home_was_not_self_sufficient";

            // P09-T04: успех пассивной проверки RoadReading в N11 — правдоподобная
            // неполная версия провала не даёт знания (раздел "При провале"
            // производственной инструкции про дорогу).
            public const string OldRoadAvoidedLowland = "chapter01.knowledge.old_road_avoided_lowland";

            public static readonly IReadOnlyList<string> All = new[]
            {
                SecondLoafIsRation, WaterFlowIsWrong, MillMovesAtWrongTime, CattleAvoidOldBranch,
                FishPatternChanged, OldSeventhChannel, OldFord, OldCustom, DrownedWomanStory,
                SevenToothObject, DownstreamPeople, OldAgreement, SharedWaterSystem,
                HomeWasNotSelfSufficient, OldRoadAvoidedLowland
            };
        }

        public static class Checks
        {
            public const string SecondLoafJudgment = "chapter01.check.second_loaf_judgment";
            public const string FloodResponse = "chapter01.check.flood_response";
            public const string MillConversation = "chapter01.check.mill_conversation";
            public const string SevenToothObject = "chapter01.check.seven_tooth_object";
            public const string RoadReading = "chapter01.check.road_reading";
            public const string FirstContact = "chapter01.check.first_contact";

            public static readonly IReadOnlyList<string> All = new[]
            {
                SecondLoafJudgment, FloodResponse, MillConversation, SevenToothObject,
                RoadReading, FirstContact
            };
        }

        public static class Items
        {
            public const string SevenToothGauge = "chapter01.item.seven_tooth_gauge";

            public static readonly IReadOnlyList<string> All = new[] { SevenToothGauge };
        }

        // P08-T03: единственная production-координата первой дальней цели —
        // "область поиска", не точная локация (раздел про N09/карту: точный
        // "Старый брод" игрок должен найти позже, в P09). GameState.Locations
        // не знает о главах — эту запись создаёт/раскрывает
        // Chapter01OutcomeApplier по решению идти дальше (N09), не лор.
        public static class Locations
        {
            public const string OldWaterSearch = "chapter01.location.old_water_search";

            public static readonly IReadOnlyList<string> All = new[] { OldWaterSearch };
        }

        // P08J-T01: стабильные ID записей Журнала целей — отдельная сущность
        // от Knowledge/Flags. chapter01.knowledge.seven_tooth_object (факт,
        // который герой узнал) и chapter01.journal.goal.seven_tooth_gauge
        // (запись в журнале про поход) не одно и то же и не должны совпадать
        // по ID, даже если сейчас связаны условием в Chapter01JournalProvider.
        public static class JournalGoals
        {
            public const string OldWaterTrail = "chapter01.journal.goal.old_water_trail";
            public const string SecondLoaf = "chapter01.journal.goal.second_loaf";
            public const string SevenToothGauge = "chapter01.journal.goal.seven_tooth_gauge";

            public static readonly IReadOnlyList<string> All = new[] { OldWaterTrail, SecondLoaf, SevenToothGauge };
        }

        // Стабильные execution ID для Chapter01OutcomeApplier (раздел 6.4).
        // Каждый применяется не более одного раза за прохождение.
        public static class Effects
        {
            public const string FloodResourceLoss = "chapter01.effect.flood_resource_loss";
            public const string FloodTimeAdvance = "chapter01.effect.flood_time_advance";
            public const string FloodMillRepairCost = "chapter01.effect.flood_mill_repair_cost";
            public const string LongRoadTimeAdvance = "chapter01.effect.long_road_time_advance";
            public const string SevenToothGaugeGrant = "chapter01.effect.seven_tooth_gauge_grant";
            public const string DepartureLocationReveal = "chapter01.effect.departure_location_reveal";

            // P09-T01/T03: OldRoadDetourStart строит реальный waypoint-крюк
            // (не просто списывает часы — раздел "Решение у старой дороги"
            // инструкции про дорогу). CartActivityStart запускает
            // GameState.TryStartRoadActivity с ценой в часах, зависящей от
            // выбранного исхода "Троих под телегой" — сам NarrativeEffect не
            // умеет стартовать времязатратные действия, поэтому это внешний
            // системный эффект, как и остальные в этом классе.
            public const string OldRoadDetourStart = "chapter01.effect.old_road_detour_start";
            public const string CartActivityStart = "chapter01.effect.cart_activity_start";

            public static readonly IReadOnlyList<string> All = new[]
            {
                FloodResourceLoss, FloodTimeAdvance, FloodMillRepairCost,
                LongRoadTimeAdvance, SevenToothGaugeGrant, DepartureLocationReveal,
                OldRoadDetourStart, CartActivityStart
            };
        }

        // Запрещает пустые и дублирующиеся ID во всём реестре (раздел 7,
        // последний абзац). Пустая коллекция результата = реестр корректен.
        public static IReadOnlyList<string> ValidateRegistry()
        {
            List<string> issues = new List<string>();
            Dictionary<string, int> occurrences = new Dictionary<string, int>();

            void Check(string category, IReadOnlyList<string> ids)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    string id = ids[i];
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        issues.Add(category + "[" + i + "] пуст.");
                        continue;
                    }

                    occurrences.TryGetValue(id, out int count);
                    occurrences[id] = count + 1;
                }
            }

            Check("Nodes", Nodes.All);
            Check("Dialogues", Dialogues.All);
            Check("Flags", Flags.All);
            Check("Knowledge", Knowledge.All);
            Check("Checks", Checks.All);
            Check("Items", Items.All);
            Check("Effects", Effects.All);
            Check("Locations", Locations.All);
            Check("JournalGoals", JournalGoals.All);

            foreach (KeyValuePair<string, int> entry in occurrences)
            {
                if (entry.Value > 1)
                    issues.Add("Дублирующийся ID \"" + entry.Key + "\" (" + entry.Value + " раз(а)).");
            }

            return issues;
        }
    }
}
