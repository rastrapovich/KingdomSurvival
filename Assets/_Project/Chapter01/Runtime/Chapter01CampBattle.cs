using System;

namespace KingdomSurvival.Chapter01
{
    // ПР-10 (ProjectDocs/PR10_CAMPAIGN_BATTLE_SPEC.md §2, вариант 1)
    // [РАБОЧЕЕ][KINGDOM SURVIVAL + §23.3]: «Звери у стоянки». Паводок согнал
    // зверьё с привычных мест; в первую ночёвку похода главы у дороги или в
    // поле на стоянку выходят звери. Бытовая угроза: тайну воды не раскрывает.
    // Один раз за главу; отход разрешён.
    public static class Chapter01CampBattle
    {
        public const string BattleId = "chapter01.battle.beasts_at_camp";
        public const double HoursIntoRest = 2.0;

        // Сцена-вступление открывается сама, когда первый ночлег похода главы
        // (не у места, не на обратном пути) идёт уже два часа.
        public static string GetPendingDialogueId(GameState gameState)
        {
            if (gameState?.Narrative == null || !gameState.HasActiveExpedition)
                return null;

            NarrativeStateData state = gameState.Narrative;
            if (!state.HasFlag(Chapter01Ids.Flags.ExpeditionStarted) ||
                state.HasFlag(Chapter01Ids.Flags.CampBeastsTriggered) ||
                state.HasFlag(Chapter01Ids.Flags.ReturnStarted) ||
                !CampRest.IsResting(gameState) ||
                CampRest.GetPlace(gameState) == CampPlaceKind.AtLocation)
            {
                return null;
            }

            ExpeditionActivityData rest = gameState.ActiveExpedition.ActiveActivity;
            if (rest.TotalHours - rest.RemainingHours < HoursIntoRest)
                return null;

            return Chapter01Ids.Dialogues.CampBeasts;
        }

        public static CampaignBattleRequest CreateRequest(GameState gameState)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));

            CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(gameState, BattleId);
            request.SourceId = Chapter01Ids.Dialogues.CampBeasts;
            request.AllowRetreat = true;
            request.Enemies.Add(new CampaignBattleEnemy { UnitTypeId = "forest_beast", Count = 2 });
            request.Enemies.Add(new CampaignBattleEnemy { UnitTypeId = "forest_beast_alpha", Count = 1 });
            return request;
        }

        // Итог боя для донесения (после применения CampaignBattleBridge).
        public static string DescribeOutcome(CampaignBattleOutcome outcome)
        {
            switch (outcome)
            {
                case CampaignBattleOutcome.Victory:
                    return "Звери отступили в темноту. До рассвета у костра больше никто не спал спокойно.";
                case CampaignBattleOutcome.Retreat:
                    return "Отряд бросил стоянку и ушёл в темноту, пока звери делили оставленное.";
                default:
                    return "Бой у стоянки проигран.";
            }
        }
    }
}
