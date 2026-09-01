using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool battleUiInitialized;
    private Button battleCautiousButton;
    private Button battleBalancedButton;
    private Button battleAssaultButton;
    private Label battleRosterSummaryLabel;
    private PendingBattleData openedBattle;
    private IVisualElementScheduledItem battleUiPoll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeBattleUiRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeBattleUi)
            .ExecuteLater(140);
    }

    private void TryInitializeBattleUi()
    {
        if (battleUiInitialized)
            return;

        if (interfaceRoot == null ||
            incidentModalTextColumn == null ||
            incidentModalOverlay == null ||
            incidentUnderstoodButton == null ||
            decisionOptionAButton == null ||
            decisionOptionBButton == null ||
            fighterSelectionHintLabel == null ||
            gameState == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInitializeBattleUi)
                    .ExecuteLater(60);
            }
            return;
        }

        battleCautiousButton = CreateDoctrineButton(
            "ОСТОРОЖНАЯ",
            () => SelectBattleDoctrine(BattleDoctrine.Cautious));
        battleBalancedButton = CreateDoctrineButton(
            "СБАЛАНСИРОВАННАЯ",
            () => SelectBattleDoctrine(BattleDoctrine.Balanced));
        battleAssaultButton = CreateDoctrineButton(
            "НАТИСК",
            () => SelectBattleDoctrine(BattleDoctrine.Assault));

        incidentModalTextColumn.Add(battleCautiousButton);
        incidentModalTextColumn.Add(battleBalancedButton);
        incidentModalTextColumn.Add(battleAssaultButton);
        SetDoctrineButtonsVisible(false);

        battleRosterSummaryLabel = new Label();
        battleRosterSummaryLabel.style.whiteSpace = WhiteSpace.Normal;
        battleRosterSummaryLabel.style.fontSize = 12f;
        battleRosterSummaryLabel.style.marginTop = 6f;
        battleRosterSummaryLabel.style.marginBottom = 6f;
        battleRosterSummaryLabel.tooltip =
            "Боевые характеристики прототипа: атака, защита, HP и состояние.";

        VisualElement rosterParent = fighterSelectionHintLabel.parent;
        if (rosterParent != null)
            rosterParent.Add(battleRosterSummaryLabel);

        battleUiPoll = interfaceRoot.schedule
            .Execute(TickBattleUi)
            .Every(100);
        battleUiInitialized = true;
        TickBattleUi();
    }

    private Button CreateDoctrineButton(string text, Action action)
    {
        Button button = new Button(action)
        {
            text = text
        };
        button.AddToClassList("incident-understood-button");
        button.style.width = 360f;
        button.style.height = 46f;
        button.style.alignSelf = Align.FlexStart;
        button.style.marginBottom = 5f;
        return button;
    }

    private void TickBattleUi()
    {
        if (gameState == null || isGameOver)
            return;

        // Скрытая опасная локация сначала создаёт обычное решение
        // «исследовать/продолжить». Если игрок остаётся и исследует её,
        // после закрытия решения подготавливается обязательный бой.
        if (!BattleSystem.HasPendingBattle(gameState) &&
            !gameState.HasPendingExpeditionDecision &&
            gameState.HasActiveExpedition &&
            !gameState.ActiveExpedition.HasTimedActivity &&
            gameState.ActiveExpedition.Phase == CommanderState.AtLocation &&
            BattleSystem.HasUnresolvedLocationEncounter(
                gameState,
                gameState.ActiveExpedition.LocationId))
        {
            string ignoredMessage;
            BattleSystem.TryPrepareCurrentLocationBattle(
                gameState,
                out ignoredMessage);
        }

        EnsurePendingBattleModalShown();
        RefreshBattleRosterSummary();
    }

    private void EnsurePendingBattleModalShown()
    {
        if (openedBattle != null ||
            !BattleSystem.HasPendingBattle(gameState) ||
            activeQueuedModal != null ||
            openedDecision != null ||
            openedIncident != null)
        {
            return;
        }

        OpenPendingBattle();
    }

    private void OpenPendingBattle()
    {
        PendingBattleData pending = BattleSystem.GetPendingBattle(gameState);
        if (pending == null || pending.Result == null)
            return;

        PauseForBlockingModal();
        openedBattle = pending;
        openedIncident = null;
        openedDecision = null;

        incidentModalTitle.text = "БОЙ · " + pending.Context.Title.ToUpper();
        incidentModalDescription.text =
            pending.Context.Description + "\n\n" +
            "Выберите доктрину. Прогноз полностью детерминирован: " +
            "до подтверждения можно переключать доктрины без применения потерь.";

        decisionOptionAButton.style.display = DisplayStyle.None;
        decisionOptionBButton.style.display = DisplayStyle.None;
        SetDoctrineButtonsVisible(true);

        incidentUnderstoodButton.clicked -= OnIncidentUnderstoodClicked;
        incidentUnderstoodButton.clicked -= OnBattleConfirmClicked;
        incidentUnderstoodButton.clicked += OnBattleConfirmClicked;
        incidentUnderstoodButton.text = "ПОДТВЕРДИТЬ БОЙ";
        incidentUnderstoodButton.style.display = DisplayStyle.Flex;

        RefreshDoctrineButtonSelection();
        RefreshBattlePreviewText();
        incidentModalOverlay.style.display = DisplayStyle.Flex;
        RefreshTimeControlAvailability();
    }

    private void SelectBattleDoctrine(BattleDoctrine doctrine)
    {
        if (openedBattle == null || !BattleSystem.HasPendingBattle(gameState))
            return;

        BattleResult result =
            BattleSystem.SelectPendingDoctrine(gameState, doctrine);
        if (result == null)
            return;

        openedBattle = BattleSystem.GetPendingBattle(gameState);
        RefreshDoctrineButtonSelection();
        RefreshBattlePreviewText();
    }

    private void RefreshDoctrineButtonSelection()
    {
        if (openedBattle == null || openedBattle.Result == null)
            return;

        BattleDoctrine selected = openedBattle.Result.Doctrine;
        battleCautiousButton.text =
            (selected == BattleDoctrine.Cautious ? "● " : "") + "ОСТОРОЖНАЯ";
        battleBalancedButton.text =
            (selected == BattleDoctrine.Balanced ? "● " : "") + "СБАЛАНСИРОВАННАЯ";
        battleAssaultButton.text =
            (selected == BattleDoctrine.Assault ? "● " : "") + "НАТИСК";
    }

    private void RefreshBattlePreviewText()
    {
        if (openedBattle == null)
            return;

        incidentModalConsequence.text =
            BattleSystem.BuildBattlePreview(openedBattle);
    }

    private void OnBattleConfirmClicked()
    {
        if (openedBattle == null || isGameOver)
            return;

        BattleResult appliedResult;
        string reportText;
        if (!BattleSystem.TryApplyPendingBattle(
                gameState,
                out appliedResult,
                out reportText))
        {
            incidentModalConsequence.text = reportText;
            return;
        }

        selectedFighterIds.RemoveWhere(
            fighterId => gameState.FindFighter(fighterId) == null);

        ContinuousClockSnapshot clock =
            ContinuousSimulationSystem.GetClock(gameState);
        AddReport(
            "[" + ContinuousSimulationSystem.FormatClock(clock.HourOfDay) + "]\n" +
            reportText);

        openedBattle = null;
        RestoreStandardIncidentConfirmButton();
        SetDoctrineButtonsVisible(false);
        HideIncidentModal();
        RefreshInterface();
        if (stableUiInitialized)
            RefreshStableUiAfterStateChange();
        RefreshContinuousTimeUi(true);
        RefreshBattleRosterSummary();
        CheckForDefeat();
        ResumeAfterBlockingModalIfReady();
    }

    private void RestoreStandardIncidentConfirmButton()
    {
        if (incidentUnderstoodButton == null)
            return;

        incidentUnderstoodButton.clicked -= OnBattleConfirmClicked;
        incidentUnderstoodButton.clicked -= OnIncidentUnderstoodClicked;
        incidentUnderstoodButton.clicked += OnIncidentUnderstoodClicked;
        incidentUnderstoodButton.text = "ПОНЯТНО";
    }

    private void SetDoctrineButtonsVisible(bool visible)
    {
        DisplayStyle style = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (battleCautiousButton != null)
            battleCautiousButton.style.display = style;
        if (battleBalancedButton != null)
            battleBalancedButton.style.display = style;
        if (battleAssaultButton != null)
            battleAssaultButton.style.display = style;
    }

    private void RefreshBattleRosterSummary()
    {
        if (battleRosterSummaryLabel == null || gameState == null)
            return;

        List<string> lines = new List<string>();
        foreach (FighterData fighter in gameState.Fighters)
        {
            FighterCombatState combat =
                BattleSystem.GetFighterCombatState(gameState, fighter.Id);
            if (combat == null)
                continue;

            string place = gameState.IsFighterInActiveExpedition(fighter.Id)
                ? "экспедиция"
                : "столица";
            lines.Add(
                fighter.Name + " · " + BattleSystem.GetRoleLabel(combat.RoleCode) +
                " · АТК " + combat.AttackPower +
                " / ЗАЩ " + combat.DefensePower +
                " · HP " + (int)Math.Ceiling(combat.HitPoints) +
                "/" + combat.MaxHitPoints +
                " · " + BattleSystem.GetHealthLabel(combat.HealthState) +
                " · " + place);
        }

        battleRosterSummaryLabel.text =
            lines.Count > 0
                ? "БОЕВОЕ СОСТОЯНИЕ\n" + string.Join("\n", lines)
                : "БОЕВОЕ СОСТОЯНИЕ\nЖивых бойцов не осталось.";
    }
}
