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
        // ПР-12К (канон v1.53 §28.3): дорожный бой — арена по местности, где
        // стоит отряд; подходящего рисунка нет — общее поле полигона.
        if (!request.IsLocal && string.IsNullOrEmpty(request.BattlefieldId))
            request.BattlefieldId = SelectRoadBattlefieldId();
        CampaignSession.EnterBattle(request);
        ContinuousSimulationSystem.SetPaused(gameState, true);
        SceneManager.LoadScene(BattleSceneName);
        return true;
    }

    private string SelectRoadBattlefieldId()
    {
        KingdomSurvival.BattlefieldDatabase.BattlefieldDatabaseAsset database =
            UnityEngine.Resources.Load<KingdomSurvival.BattlefieldDatabase.BattlefieldDatabaseAsset>(
                KingdomSurvival.BattlefieldDatabase.BattlefieldDatabaseAsset.ResourcesPath);
        if (database == null || gameState == null || !gameState.HasActiveExpedition)
            return string.Empty;
        WorldMapGameplayTerrainType terrain = WorldMapNavigation.GetTerrainAtPercent(
            gameState.ActiveExpedition.CurrentMapXPercent,
            gameState.ActiveExpedition.CurrentMapYPercent);
        KingdomSurvival.BattlefieldDatabase.BattlefieldDefinitionData field =
            database.FindForTags(RoadBattlefieldTags.For(terrain));
        if (field == null)
        {
            // Отмеченный временный вариант: рисунка для этой местности нет.
            UnityEngine.Debug.Log("Дорожный бой: для местности «" + terrain +
                                  "» нет своего поля — временно общее поле полигона.");
            return string.Empty;
        }
        return field.Id;
    }

    // Опрос каждого кадра: вступление к сюжетному бою и запуск боя после него.
    private void RefreshStoryBattle()
    {
        if (gameState == null || isGameOver)
            return;

        // Бой после сцены — и главы, и свободной игры (ПР-12Б).
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

        if (!Chapter01Crisis.IsActive(gameState))
            return;

        string dialogueId = Chapter01CampBattle.GetPendingDialogueId(gameState);
        if (!string.IsNullOrEmpty(dialogueId))
            TryOpenNarrativeDialogueById(dialogueId);
    }

    private void OnStoryDialogueCompleted(string dialogueId)
    {
        if (gameState == null)
            return;
        if (dialogueId == Chapter01Ids.Dialogues.CampBeasts)
        {
            pendingStoryBattle = Chapter01CampBattle.CreateRequest(gameState);
            return;
        }

        // ПР-12Б: сцены свободной игры — реакция режима и, если нужно, бой.
        CampaignContent.OnDialogueCompleted(gameState, dialogueId);
        // ПР-12К: в исследуемом месте бой после сцены начинается на месте —
        // отдельная арена здесь не грузится.
        if (IsLocalScreenOpen)
        {
            OnLocalDialogueCompleted(dialogueId);
            return;
        }
        pendingStoryBattle = CampaignContent.BattleAfterDialogue(gameState, dialogueId);
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

        // ПР-12Б: исход боя истории свободной игры.
        if (status == CampaignBattleApplyStatus.SquadSurvived)
        {
            List<string> storyReports = new List<string>();
            CampaignContent.OnBattleApplied(gameState, result, storyReports);
            foreach (string report in storyReports)
                AddReport(report);
        }

        if (status == CampaignBattleApplyStatus.SquadSurvived)
        {
            string outcome = result.Outcome == CampaignBattleOutcome.Victory ? "Победа."
                : result.Outcome == CampaignBattleOutcome.Retreat ? "Отряд отступил." : "Бой проигран.";
            string losses = fallenNames.Count == 0
                ? "Все вернулись из боя."
                : "Погибли: " + string.Join(", ", fallenNames) + ".";
            // 12Е-7/12Е-8: сработавшие особенности и выросшие следы — отдельными
            // строками под итогом.
            System.Predicate<string> ownLine = note => FeaturePresentation.IsFeatureLine(note) || ProgressionTraces.IsTraceLine(note);
            List<string> plain = notes.FindAll(note => !ownLine(note));
            List<string> features = notes.FindAll(ownLine);
            AddReport("[БОЙ] " + outcome + " " + losses + (plain.Count > 0 ? " " + string.Join(" ", plain) : string.Empty) +
                      (features.Count > 0 ? "\n" + string.Join("\n", features) : string.Empty));
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
