using System.Collections.Generic;
using KingdomSurvival.Chapter01;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// ПР-03: черновой переход кампания → BattleSandbox → кампания. Кампания
// остаётся в CampaignSession, сцена боя получает запрос с людьми похода и
// возвращает итог; эта сцена при подхвате кампании применяет его один раз
// (CampaignBattleBridge). Временное правило: павшие бойцы погибают насовсем;
// пал герой — «Отряд разбит», кампания заканчивается, игрок в главном меню
// загружает сохранение или начинает заново.
public partial class PrototypeUIController
{
    private const string BattleSceneName = "BattleSandbox";

    // ПР-10: сюжетный бой ждёт, пока закроется сцена-вступление.
    private CampaignBattleRequest pendingStoryBattle;

    private bool TryStartCampaignBattle(string battleId, out string message)
    {
        return TryStartPreparedCampaignBattle(battleId, null, out message);
    }

    private bool TryStartPreparedCampaignBattle(string battleId, CampaignBattleRequest prepared, out string message)
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

        CampaignBattleRequest request = prepared ?? CampaignBattleBridge.CreateRequest(gameState, battleId);
        // ПР-10: бой главы отмечается начатым уже после предбоевого сохранения —
        // загрузка этого сохранения снова откроет вступление и бой.
        if (request.BattleId == Chapter01CampBattle.BattleId && gameState.Narrative != null)
            gameState.Narrative.SetFlag(Chapter01Ids.Flags.CampBeastsTriggered);
        CampaignSession.EnterBattle(request);
        ContinuousSimulationSystem.SetPaused(gameState, true);
        SceneManager.LoadScene(BattleSceneName);
        return true;
    }

    // Опрос каждого кадра: вступление к сюжетному бою и запуск боя после него.
    private void RefreshStoryBattle()
    {
        if (gameState == null || isGameOver || !Chapter01Crisis.IsActive(gameState))
            return;

        if (pendingStoryBattle != null)
        {
            if (IsNarrativeDialogueActive || HasBlockingModalWork())
                return;
            CampaignBattleRequest request = pendingStoryBattle;
            pendingStoryBattle = null;
            if (!TryStartPreparedCampaignBattle(request.BattleId, request, out string message))
                AddReport(message);
            return;
        }

        string dialogueId = Chapter01CampBattle.GetPendingDialogueId(gameState);
        if (!string.IsNullOrEmpty(dialogueId))
            TryOpenNarrativeDialogueById(dialogueId);
    }

    private void OnStoryDialogueCompleted(string dialogueId)
    {
        if (dialogueId == Chapter01Ids.Dialogues.CampBeasts && gameState != null)
            pendingStoryBattle = Chapter01CampBattle.CreateRequest(gameState);
    }

    // Вызывается при подхвате кампании: если она вернулась из боя —
    // применить итог. True — кампания продолжается.
    private bool ApplyReturnedCampaignBattle()
    {
        CampaignBattleResult result = CampaignSession.TakeCompletedBattle();
        if (result == null || gameState == null)
            return true;

        List<string> fallenNames = new List<string>();
        List<string> notes = new List<string>();
        CampaignBattleApplyStatus status = CampaignBattleBridge.ApplyResult(gameState, result, fallenNames, notes);

        if (status == CampaignBattleApplyStatus.HeroFell)
        {
            CampaignSession.End();
            gameState = null;
            CloseMainScreen();
            ShowMainMenu();
            if (mainMenuMessage != null)
            {
                mainMenuMessage.text =
                    "Отряд разбит: герой пал в бою. Можно вернуться к сохранению перед боем.";
            }
            // ПР-10: поражение — загрузка предбоевого сохранения одним нажатием.
            ShowPreBattleLoadButton(true);
            return false;
        }

        // ПР-10: исход сюжетного боя главы.
        if (status == CampaignBattleApplyStatus.SquadSurvived &&
            result.BattleId == Chapter01CampBattle.BattleId && gameState.Narrative != null)
        {
            gameState.Narrative.SetFlag(Chapter01Ids.Flags.CampBeastsResolved);
            AddReport(Chapter01CampBattle.DescribeOutcome(result.Outcome));
        }

        if (status == CampaignBattleApplyStatus.SquadSurvived)
        {
            string outcome = result.Outcome == CampaignBattleOutcome.Victory ? "Победа."
                : result.Outcome == CampaignBattleOutcome.Retreat ? "Отряд отступил." : "Бой проигран.";
            string losses = fallenNames.Count == 0
                ? "Все вернулись из боя."
                : "Погибли: " + string.Join(", ", fallenNames) + ".";
            AddReport("[БОЙ] " + outcome + " " + losses + (notes.Count > 0 ? " " + string.Join(" ", notes) : string.Empty));
            RefreshInterface();
            // ПР-04: итог применён — устойчивая точка.
            Autosave();
        }

        return true;
    }

    private Button mainMenuPreBattleButton;

    private void ShowPreBattleLoadButton(bool show)
    {
        if (interfaceRoot == null)
            return;
        if (mainMenuPreBattleButton == null)
        {
            mainMenuPreBattleButton = interfaceRoot.Q<Button>("main-menu-load-prebattle-button");
            if (mainMenuPreBattleButton == null)
                return;
            mainMenuPreBattleButton.clicked += OnLoadPreBattleClicked;
        }
        mainMenuPreBattleButton.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnLoadPreBattleClicked()
    {
        ShowPreBattleLoadButton(false);
        if (!LoadCampaign(CampaignSaveStore.AutosaveSlotId) && mainMenuMessage != null)
            mainMenuMessage.text = "Сохранение перед боем не загрузилось. Загрузите другое сохранение.";
    }
}
