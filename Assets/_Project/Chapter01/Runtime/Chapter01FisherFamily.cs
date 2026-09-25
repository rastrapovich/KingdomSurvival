using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
    // ПР-06Б [РАБОЧЕЕ][KINGDOM SURVIVAL + THE WITCHER 3]: причинное пополнение
    // вместо платного найма. Паводок сделал жильё рыбака Тихона непригодным;
    // семья просится в Дом вся — четверо или никто. Предложение без срока:
    // закрытое окно оставляет его открытым, отказ и принятие — устойчивые
    // взаимоисключающие исходы (флаги сцены chapter01_dialogue_gate_family).
    public static class Chapter01FisherFamily
    {
        public const string HouseholdId = "household.fisher_tikhon";
        public const string TikhonId = HomePeopleService.TikhonId;
        public const string VarvaraId = "newcomer.fisher.varvara";
        public const string AnyaId = "newcomer.fisher.anya";
        public const string FedyaId = "newcomer.fisher.fedya";
        public const int MemberCount = 4;

        public static bool IsResolved(NarrativeStateData state)
        {
            return state != null &&
                   (state.HasFlag(Chapter01Ids.Flags.FisherFamilyAccepted) ||
                    state.HasFlag(Chapter01Ids.Flags.FisherFamilyDeclined));
        }

        // Триггер (спецификация §10.2): кризис главы, паводок случился, N05
        // решён, предложение не разрешено, герой дома. Не привязано к
        // IsHomePhase — ждёт и после похода главы.
        public static bool IsOffered(GameState gameState)
        {
            if (!Chapter01Crisis.IsActive(gameState) || gameState.Narrative == null || gameState.HasActiveExpedition)
                return false;

            NarrativeStateData state = gameState.Narrative;
            return state.HasFlag(Chapter01Ids.Flags.FloodHappened) &&
                   Chapter01StoryDirector.GetRepairChoice(state) != Chapter01RepairChoice.None &&
                   !IsResolved(state);
        }

        // Что видно до принятия (§10.3, PR07_HOME_SPEC §11): польза и
        // расход — до решения, а не после.
        public static string BuildOfferSummary(GameState gameState)
        {
            string summary =
                "Тихон, Варвара и двое детей — четыре новых жителя. Пока Тихон дома и здоров, ловля приносит до " +
                HomeLife.FishingFoodPerFullDay + " пищи за сутки работы. Семья расходует " + MemberCount +
                " пищи в сутки, когда все дома. Тихона можно взять бойцом; тогда ловля остановится, а трое его домочадцев останутся дома.";

            if (gameState != null)
            {
                int consumptionAfter = gameState.DailyFoodConsumption + MemberCount;
                HomeFoodForecast forecast = HomeOverview.ForecastFood(
                    gameState.Food, BuildingSystem.GetDailyFoodIncome(gameState), consumptionAfter,
                    gameState.ConsecutiveFoodShortageDays > 0, 0.0, HomeLife.FishingFoodPerHour, 24.0);
                summary += " Если принять: дома едят " + consumptionAfter + " в сутки. " + HomeOverview.DescribeFood(forecast);
            }

            return summary;
        }

        // Принять всех четверых одной операцией. Идемпотентно: повторное
        // завершение сцены или загрузка ничего не добавляют.
        public static bool TryAccept(GameState gameState, out string message)
        {
            message = string.Empty;
            if (gameState?.People == null)
                return false;

            HouseholdState household = new HouseholdState
            {
                HouseholdId = HouseholdId,
                DisplayName = "Семья Тихона"
            };

            List<ResidentState> members = new List<ResidentState>
            {
                new ResidentState
                {
                    PersonId = TikhonId,
                    DisplayName = "Тихон",
                    RoleLabel = "рыбак",
                    ShortDescription = "Читает течение и дно, знает лодку. Защищаться умеет как ополченец. Пришёл, потому что паводок оставил семье стены без жилья.",
                    DialogueSpeakerId = "tikhon",
                    AgeGroup = ResidentAgeGroup.Adult,
                    TravelRole = ResidentTravelRole.Combatant,
                    UnitTypeId = CampaignBattleBridge.HeroFallbackUnitTypeId
                },
                new ResidentState
                {
                    PersonId = VarvaraId,
                    DisplayName = "Варвара",
                    RoleLabel = "жена Тихона",
                    ShortDescription = "Решала переселение наравне с мужем. Семья пришла вся — или не пришла бы вовсе.",
                    DialogueSpeakerId = "varvara",
                    AgeGroup = ResidentAgeGroup.Adult
                },
                new ResidentState
                {
                    PersonId = AnyaId,
                    DisplayName = "Аня",
                    RoleLabel = "дочь Тихона",
                    AgeGroup = ResidentAgeGroup.Child
                },
                new ResidentState
                {
                    PersonId = FedyaId,
                    DisplayName = "Федя",
                    RoleLabel = "сын Тихона",
                    AgeGroup = ResidentAgeGroup.Child
                }
            };

            bool admitted = HomePeopleService.AdmitHousehold(
                gameState, Chapter01Ids.Effects.FisherFamilyJoin, household, members, out message);
            if (admitted)
            {
                // ПР-08: у Тихона свой топор и кожух.
                ItemService.EnsureInventory(gameState);
                // ПР-07Б: принятие открывает ловлю — одно короткое сообщение.
                HomeKnowledge.Report(gameState, null,
                    "Семья Тихона теперь живёт в Доме. Открыта рыбная ловля: пока Тихон дома и здоров — до " +
                    HomeLife.FishingFoodPerFullDay + " пищи за полные сутки работы.");
            }
            return admitted;
        }
    }
}
