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
        public const string TikhonId = "newcomer.fisher.tikhon";
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

        // Что видно до принятия (§10.3).
        public static string BuildOfferSummary(GameState gameState)
        {
            int presentNow = gameState != null ? HomePeopleService.CountHomePresent(gameState) : 0;
            string summary =
                "Семья Тихона — 4 человека: Тихон (рыбак, может идти с отрядом), его жена Варвара и дети Аня и Федя. " +
                "Дома станет на четыре жителя больше; расход запасов при всех дома вырастет на 4 в сутки (" +
                presentNow + " → " + (presentNow + MemberCount) + ").";

            if (gameState != null)
            {
                int food = gameState.Food;
                int dailyAfter = presentNow + MemberCount;
                int income = BuildingSystem.GetDailyFoodIncome(gameState);
                if (dailyAfter > income)
                {
                    int shortfall = dailyAfter - income;
                    summary += " Сейчас доход пищи " + income + " в сутки: запасов (" + food + ") хватит примерно на " +
                               (food / shortfall) + " сут. нехватки по " + shortfall + ".";
                }
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

            return HomePeopleService.AdmitHousehold(
                gameState, Chapter01Ids.Effects.FisherFamilyJoin, household, members, out message);
        }
    }
}
