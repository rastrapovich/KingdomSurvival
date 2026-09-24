using System.Collections.Generic;
using UnityEngine.SceneManagement;

// ПР-03: черновой переход кампания → BattleSandbox → кампания. Кампания
// остаётся в CampaignSession, сцена боя получает запрос с людьми похода и
// возвращает итог; эта сцена при подхвате кампании применяет его один раз
// (CampaignBattleBridge). Временное правило: павшие бойцы погибают насовсем;
// пал герой — «Отряд разбит», кампания заканчивается, игрок в главном меню
// загружает сохранение или начинает заново.
public partial class PrototypeUIController
{
    private const string BattleSceneName = "BattleSandbox";

    private bool TryStartCampaignBattle(string battleId, out string message)
    {
        message = string.Empty;
        if (gameState == null || !CampaignSession.HasActive)
        {
            message = "Нет активной партии.";
            return false;
        }

        if (isGameOver || HasBlockingModalWork())
        {
            message = "Сейчас нельзя начать бой: закройте открытые окна.";
            return false;
        }

        if (!gameState.HasActiveExpedition)
        {
            message = "Бой возможен только в походе.";
            return false;
        }

        // ПР-04: автосохранение перед боем — предбоевая точка возврата.
        Autosave();

        CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(gameState, battleId);
        CampaignSession.EnterBattle(request);
        ContinuousSimulationSystem.SetPaused(gameState, true);
        SceneManager.LoadScene(BattleSceneName);
        return true;
    }

    // Вызывается при подхвате кампании: если она вернулась из боя —
    // применить итог. True — кампания продолжается.
    private bool ApplyReturnedCampaignBattle()
    {
        CampaignBattleResult result = CampaignSession.TakeCompletedBattle();
        if (result == null || gameState == null)
            return true;

        List<string> fallenNames = new List<string>();
        CampaignBattleApplyStatus status = CampaignBattleBridge.ApplyResult(gameState, result, fallenNames);

        if (status == CampaignBattleApplyStatus.HeroFell)
        {
            CampaignSession.End();
            gameState = null;
            CloseMainScreen();
            ShowMainMenu();
            if (mainMenuMessage != null)
            {
                mainMenuMessage.text =
                    "Отряд разбит: герой пал в бою. Загрузите последнее сохранение или начните новую игру.";
            }
            return false;
        }

        if (status == CampaignBattleApplyStatus.SquadSurvived)
        {
            string outcome = result.Outcome == CampaignBattleOutcome.Victory ? "Засада отбита." : "Бой проигран.";
            string losses = fallenNames.Count == 0
                ? "Все вернулись из боя."
                : "Погибли: " + string.Join(", ", fallenNames) + ".";
            AddReport("[БОЙ] " + outcome + " " + losses);
            RefreshInterface();
            // ПР-04: итог применён — устойчивая точка.
            Autosave();
        }

        return true;
    }
}
