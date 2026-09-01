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
    private VisualElement battleLayout;
    private VisualElement battlePlayerColumn;
    private VisualElement battleEnemyColumn;
    private VisualElement battleDoctrineRow;
    private Label battleOutcomeLabel;
    private Label battleStrategicConsequenceLabel;
    private VisualElement battleModalWindow;
    private VisualElement battleModalBody;
    private VisualElement battleIncidentImage;
    private PendingBattleData openedBattle;
    private IVisualElementScheduledItem battleUiPoll;
    private GameState battleObservedState;
    private readonly HashSet<string> completedBattleLocationIds =
        new HashSet<string>();

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

        battleModalWindow =
            interfaceRoot.Q<VisualElement>(className: "incident-modal-window");
        battleModalBody =
            interfaceRoot.Q<VisualElement>(className: "incident-modal-body");
        battleIncidentImage =
            interfaceRoot.Q<VisualElement>(className: "incident-image-placeholder");

        CreateBattleCompositionUi();
        battleObservedState = gameState;
        battleUiPoll = interfaceRoot.schedule
            .Execute(TickBattleUi)
            .Every(100);
        battleUiInitialized = true;
        TickBattleUi();
    }

    private void CreateBattleCompositionUi()
    {
        battleLayout = new VisualElement();
        battleLayout.style.display = DisplayStyle.None;
        battleLayout.style.width = Length.Percent(100f);
        battleLayout.style.flexGrow = 1f;
        battleLayout.style.flexDirection = FlexDirection.Row;
        battleLayout.style.alignItems = Align.Stretch;
        battleLayout.style.marginTop = 8f;
        battleLayout.style.marginBottom = 12f;

        battlePlayerColumn = new VisualElement();
        battlePlayerColumn.style.width = Length.Percent(38f);
        battlePlayerColumn.style.flexShrink = 0f;
        battlePlayerColumn.style.paddingRight = 12f;

        VisualElement center = new VisualElement();
        center.style.width = Length.Percent(24f);
        center.style.flexShrink = 0f;
        center.style.alignItems = Align.Center;
        center.style.justifyContent = Justify.Center;
        center.style.paddingLeft = 12f;
        center.style.paddingRight = 12f;

        Label versusLabel = new Label("VS");
        versusLabel.style.fontSize = 34f;
        versusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        versusLabel.style.color = new Color(0.58f, 0.49f, 0.36f, 1f);
        versusLabel.style.marginBottom = 16f;
        center.Add(versusLabel);

        battleOutcomeLabel = new Label("ПРОГНОЗ");
        battleOutcomeLabel.style.fontSize = 15f;
        battleOutcomeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        battleOutcomeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        battleOutcomeLabel.style.whiteSpace = WhiteSpace.Normal;
        battleOutcomeLabel.style.color = new Color(0.89f, 0.83f, 0.70f, 1f);
        center.Add(battleOutcomeLabel);

        battleStrategicConsequenceLabel = new Label();
        battleStrategicConsequenceLabel.style.marginTop = 12f;
        battleStrategicConsequenceLabel.style.fontSize = 11f;
        battleStrategicConsequenceLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        battleStrategicConsequenceLabel.style.whiteSpace = WhiteSpace.Normal;
        battleStrategicConsequenceLabel.style.color =
            new Color(0.64f, 0.65f, 0.64f, 1f);
        center.Add(battleStrategicConsequenceLabel);

        battleEnemyColumn = new VisualElement();
        battleEnemyColumn.style.width = Length.Percent(38f);
        battleEnemyColumn.style.flexShrink = 0f;
        battleEnemyColumn.style.paddingLeft = 12f;

        battleLayout.Add(battlePlayerColumn);
        battleLayout.Add(center);
        battleLayout.Add(battleEnemyColumn);
        incidentModalTextColumn.Add(battleLayout);

        battleDoctrineRow = new VisualElement();
        battleDoctrineRow.style.display = DisplayStyle.None;
        battleDoctrineRow.style.width = Length.Percent(100f);
        battleDoctrineRow.style.flexDirection = FlexDirection.Row;
        battleDoctrineRow.style.justifyContent = Justify.Center;
        battleDoctrineRow.style.marginBottom = 10f;

        battleCautiousButton = CreateDoctrineButton(
            "ОСТОРОЖНАЯ",
            () => SelectBattleDoctrine(BattleDoctrine.Cautious));
        battleBalancedButton = CreateDoctrineButton(
            "СБАЛАНСИРОВАННАЯ",
            () => SelectBattleDoctrine(BattleDoctrine.Balanced));
        battleAssaultButton = CreateDoctrineButton(
            "НАТИСК",
            () => SelectBattleDoctrine(BattleDoctrine.Assault));

        battleDoctrineRow.Add(battleCautiousButton);
        battleDoctrineRow.Add(battleBalancedButton);
        battleDoctrineRow.Add(battleAssaultButton);
        incidentModalTextColumn.Add(battleDoctrineRow);
    }

    private Button CreateDoctrineButton(string text, Action action)
    {
        Button button = new Button(action) { text = text };
        button.AddToClassList("incident-understood-button");
        button.style.width = Length.Percent(31f);
        button.style.height = 42f;
        button.style.marginLeft = 4f;
        button.style.marginRight = 4f;
        button.style.fontSize = 11f;
        return button;
    }

    private void TickBattleUi()
    {
        if (gameState == null || isGameOver)
            return;

        if (!ReferenceEquals(battleObservedState, gameState))
        {
            battleObservedState = gameState;
            completedBattleLocationIds.Clear();
            openedBattle = null;
        }

        if (openedBattle == null &&
            !BattleSystem.HasPendingBattle(gameState) &&
            !gameState.HasPendingExpeditionDecision &&
            gameState.HasActiveExpedition &&
            !gameState.ActiveExpedition.HasTimedActivity &&
            gameState.ActiveExpedition.Phase == CommanderState.AtLocation &&
            !completedBattleLocationIds.Contains(gameState.ActiveExpedition.LocationId) &&
            BattleSystem.HasUnresolvedLocationEncounter(
                gameState,
                gameState.ActiveExpedition.LocationId))
        {
            string ignoredMessage;
            BattleSystem.TryPrepareCurrentLocationBattle(gameState, out ignoredMessage);
        }

        EnsurePendingBattleModalShown();
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
        incidentModalDescription.text = string.Empty;
        incidentModalConsequence.text = string.Empty;
        incidentModalDescription.style.display = DisplayStyle.None;
        incidentModalConsequence.style.display = DisplayStyle.None;

        if (battleIncidentImage != null)
            battleIncidentImage.style.display = DisplayStyle.None;
        if (battleModalWindow != null)
        {
            battleModalWindow.style.width = 960f;
            battleModalWindow.style.maxWidth = Length.Percent(94f);
            battleModalWindow.style.minHeight = 600f;
        }
        if (battleModalBody != null)
            battleModalBody.style.width = Length.Percent(100f);
        incidentModalTextColumn.style.width = Length.Percent(100f);
        incidentModalTextColumn.style.flexGrow = 1f;

        decisionOptionAButton.style.display = DisplayStyle.None;
        decisionOptionBButton.style.display = DisplayStyle.None;
        SetBattleCompositionVisible(true);

        incidentUnderstoodButton.clicked -= OnIncidentUnderstoodClicked;
        incidentUnderstoodButton.clicked -= OnBattleConfirmClicked;
        incidentUnderstoodButton.clicked += OnBattleConfirmClicked;
        incidentUnderstoodButton.text = "НАЧАТЬ БОЙ";
        incidentUnderstoodButton.style.display = DisplayStyle.Flex;

        RefreshDoctrineButtonSelection();
        RefreshBattleComposition();
        incidentModalOverlay.style.display = DisplayStyle.Flex;
        RefreshTimeControlAvailability();
    }

    private void SelectBattleDoctrine(BattleDoctrine doctrine)
    {
        if (openedBattle == null || !BattleSystem.HasPendingBattle(gameState))
            return;

        BattleResult result = BattleSystem.SelectPendingDoctrine(gameState, doctrine);
        if (result == null)
            return;

        openedBattle = BattleSystem.GetPendingBattle(gameState);
        RefreshDoctrineButtonSelection();
        RefreshBattleComposition();
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

    private void RefreshBattleComposition()
    {
        if (openedBattle == null || openedBattle.Result == null)
            return;

        battlePlayerColumn.Clear();
        battleEnemyColumn.Clear();
        battlePlayerColumn.Add(CreateBattleColumnTitle("ОТРЯД"));

        if (openedBattle.Context.FighterIds.Count == 0)
        {
            Label empty = new Label("Защитников нет");
            empty.style.marginTop = 20f;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            empty.style.color = new Color(0.68f, 0.35f, 0.34f, 1f);
            battlePlayerColumn.Add(empty);
        }
        else
        {
            foreach (string fighterId in openedBattle.Context.FighterIds)
            {
                FighterData fighter = gameState.FindFighter(fighterId);
                FighterCombatState combat =
                    BattleSystem.GetFighterCombatState(gameState, fighterId);
                if (fighter == null || combat == null)
                    continue;

                battlePlayerColumn.Add(CreateBattleFighterCard(
                    fighter,
                    combat,
                    FindFighterConsequence(openedBattle.Result, fighterId)));
            }
        }

        battleEnemyColumn.Add(CreateBattleColumnTitle("ПРОТИВНИК"));
        battleEnemyColumn.Add(CreateBattleEnemyCard(openedBattle.Context));
        battleOutcomeLabel.text =
            "ПРОГНОЗ\n" + GetBattleOutcomeLabel(openedBattle.Result.Outcome).ToUpper();
        battleStrategicConsequenceLabel.text =
            BuildShortStrategicConsequence(openedBattle.Result);
    }

    private Label CreateBattleColumnTitle(string text)
    {
        Label label = new Label(text);
        label.style.height = 24f;
        label.style.marginBottom = 8f;
        label.style.fontSize = 12f;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.color = new Color(0.69f, 0.66f, 0.58f, 1f);
        return label;
    }

    private VisualElement CreateBattleFighterCard(
        FighterData fighter,
        FighterCombatState combat,
        FighterBattleConsequence consequence)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("battle-fighter-card");
        card.style.height = 68f;
        card.style.marginBottom = 7f;
        card.style.paddingLeft = 8f;
        card.style.paddingRight = 8f;
        card.style.paddingTop = 6f;
        card.style.paddingBottom = 6f;
        card.style.flexDirection = FlexDirection.Row;
        card.style.backgroundColor = new Color(0.15f, 0.17f, 0.20f, 1f);
        SetBorder(card, new Color(0.29f, 0.31f, 0.34f, 1f), 1f);
        SetRadius(card, 4f);

        VisualElement portrait = new VisualElement();
        portrait.style.width = 52f;
        portrait.style.height = 52f;
        portrait.style.flexShrink = 0f;
        portrait.style.alignItems = Align.Center;
        portrait.style.justifyContent = Justify.Center;
        portrait.style.backgroundColor = new Color(0.09f, 0.10f, 0.12f, 1f);
        SetRadius(portrait, 3f);
        Label portraitText = new Label("Боец");
        portraitText.style.fontSize = 8f;
        portraitText.style.color = new Color(0.43f, 0.45f, 0.48f, 1f);
        portrait.Add(portraitText);
        card.Add(portrait);

        VisualElement info = new VisualElement();
        info.style.flexGrow = 1f;
        info.style.marginLeft = 9f;
        info.style.justifyContent = Justify.Center;

        Label name = new Label(fighter.Name);
        name.style.fontSize = 12f;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.color = new Color(0.88f, 0.84f, 0.75f, 1f);
        info.Add(name);

        int beforeHp = (int)Math.Ceiling(combat.HitPoints);
        int afterHp = consequence != null ? consequence.AfterHitPoints : beforeHp;
        Label hpText = new Label(
            consequence != null && afterHp != beforeHp
                ? beforeHp + " → " + afterHp + " HP"
                : beforeHp + " HP");
        hpText.style.marginTop = 2f;
        hpText.style.fontSize = 10f;
        hpText.style.color = new Color(0.67f, 0.68f, 0.66f, 1f);
        info.Add(hpText);

        VisualElement hpBar = new VisualElement();
        hpBar.style.height = 8f;
        hpBar.style.marginTop = 4f;
        hpBar.style.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 1f);
        SetRadius(hpBar, 2f);
        VisualElement hpFill = new VisualElement();
        hpFill.style.height = Length.Percent(100f);
        SetRadius(hpFill, 2f);
        hpBar.Add(hpFill);
        UpdateHealthBar(hpFill, afterHp, combat.MaxHitPoints);
        info.Add(hpBar);
        card.Add(info);

        string fighterId = fighter.Id;
        card.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 1)
                return;
            OpenFighterDetails(fighterId);
            evt.StopPropagation();
        });
        card.tooltip = "ПКМ — открыть сведения о бойце";
        return card;
    }

    private VisualElement CreateBattleEnemyCard(BattleContext context)
    {
        VisualElement card = new VisualElement();
        card.style.minHeight = 250f;
        card.style.paddingLeft = 14f;
        card.style.paddingRight = 14f;
        card.style.paddingTop = 14f;
        card.style.paddingBottom = 14f;
        card.style.alignItems = Align.Center;
        card.style.backgroundColor = new Color(0.20f, 0.13f, 0.14f, 1f);
        SetBorder(card, new Color(0.43f, 0.24f, 0.23f, 1f), 1f);
        SetRadius(card, 5f);

        VisualElement portrait = new VisualElement();
        portrait.style.width = 180f;
        portrait.style.height = 150f;
        portrait.style.alignItems = Align.Center;
        portrait.style.justifyContent = Justify.Center;
        portrait.style.backgroundColor = new Color(0.11f, 0.08f, 0.09f, 1f);
        SetRadius(portrait, 4f);
        Label portraitText = new Label("ИЗОБРАЖЕНИЕ\nПРОТИВНИКА");
        portraitText.style.unityTextAlign = TextAnchor.MiddleCenter;
        portraitText.style.whiteSpace = WhiteSpace.Normal;
        portraitText.style.color = new Color(0.49f, 0.37f, 0.37f, 1f);
        portrait.Add(portraitText);
        card.Add(portrait);

        Label enemyName = new Label(context.EnemyName.ToUpper());
        enemyName.style.marginTop = 12f;
        enemyName.style.fontSize = 15f;
        enemyName.style.unityFontStyleAndWeight = FontStyle.Bold;
        enemyName.style.unityTextAlign = TextAnchor.MiddleCenter;
        enemyName.style.color = new Color(0.88f, 0.70f, 0.66f, 1f);
        card.Add(enemyName);

        Label enemyPower = new Label("Сила: " + context.EnemyPower);
        enemyPower.style.marginTop = 7f;
        enemyPower.style.fontSize = 11f;
        enemyPower.style.color = new Color(0.68f, 0.57f, 0.55f, 1f);
        card.Add(enemyPower);
        return card;
    }

    private FighterBattleConsequence FindFighterConsequence(
        BattleResult result,
        string fighterId)
    {
        foreach (FighterBattleConsequence consequence in result.FighterConsequences)
        {
            if (consequence.FighterId == fighterId)
                return consequence;
        }
        return null;
    }

    private string BuildShortStrategicConsequence(BattleResult result)
    {
        string text = string.Empty;
        if (result.FoodDelta != 0)
            text += "Пища " + FormatSigned(result.FoodDelta);
        if (result.MoodDelta != 0)
            text += (text.Length > 0 ? " · " : "") +
                "Настроение " + FormatSigned(result.MoodDelta);
        return text.Length > 0 ? text : "Стратегических потерь нет";
    }

    private static string FormatSigned(int value)
    {
        return value > 0 ? "+" + value : value.ToString();
    }

    private string GetBattleOutcomeLabel(BattleOutcome outcome)
    {
        switch (outcome)
        {
            case BattleOutcome.Victory:
                return "Победа";
            case BattleOutcome.CostlyVictory:
                return "Тяжёлая победа";
            case BattleOutcome.Withdrawal:
                return "Отход";
            default:
                return "Поражение";
        }
    }

    private void OnBattleConfirmClicked()
    {
        if (openedBattle == null || openedBattle.Result == null || isGameOver)
            return;

        BattleContext completedContext = openedBattle.Context;

        // С версии 1.10 исход боя никогда сам не отдаёт приказ возвращаться.
        // BattleSystem пока хранит совместимое поле ForceRetreat, поэтому перед
        // применением явно отключаем старое поведение сохранённого результата.
        if (openedBattle.Result.Kind == BattleKind.ExpeditionLocation)
            openedBattle.Result.ForceRetreat = false;

        BattleResult appliedResult;
        string reportText;
        if (!BattleSystem.TryApplyPendingBattle(
                gameState,
                out appliedResult,
                out reportText))
        {
            battleOutcomeLabel.text = reportText;
            return;
        }

        if (appliedResult.Kind == BattleKind.ExpeditionLocation)
        {
            completedBattleLocationIds.Add(completedContext.SourceId);
            StopExpeditionAfterBattle();
        }

        selectedFighterIds.RemoveWhere(
            fighterId => gameState.FindFighter(fighterId) == null);

        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(gameState);
        AddReport(
            "[" + ContinuousSimulationSystem.FormatClock(clock.HourOfDay) + "]\n" +
            reportText);

        // openedBattle остаётся непустым до закрытия отдельного окна результата.
        // Это удерживает общую блокирующую систему на паузе без промежуточного кадра.
        RestoreStandardIncidentPresentation();
        HideIncidentModal();
        RefreshInterface();
        if (stableUiInitialized)
            RefreshStableUiAfterStateChange();
        RefreshContinuousTimeUi(true);
        CheckForDefeat();
        ShowBattleResult(completedContext, appliedResult);
        RefreshTimeControlAvailability();
    }

    private void StopExpeditionAfterBattle()
    {
        if (gameState == null || !gameState.HasActiveExpedition)
            return;

        ExpeditionData expedition = gameState.ActiveExpedition;
        CommanderData commander = gameState.FindCommander(expedition.CommanderId);

        expedition.Phase = CommanderState.AtLocation;
        expedition.RemainingRouteCells = 0;
        expedition.RouteLengthCells = 0;
        expedition.RouteIndex = 0;
        expedition.RouteDelayHoursRemaining = 0.0;
        expedition.ActiveActivity = null;
        expedition.PendingDecision = null;
        expedition.HasInterruptedRoute = false;
        expedition.LastTravelPoints.Clear();
        expedition.TargetMapXPercent = expedition.CurrentMapXPercent;
        expedition.TargetMapYPercent = expedition.CurrentMapYPercent;
        expedition.Route = new List<MapPointData>
        {
            new MapPointData(
                expedition.CurrentMapXPercent,
                expedition.CurrentMapYPercent)
        };

        if (commander != null)
            commander.State = CommanderState.AtLocation;

        ContinuousSimulationSystem.NotifyRouteChanged(gameState);
    }

    private void RestoreStandardIncidentPresentation()
    {
        SetBattleCompositionVisible(false);

        if (incidentUnderstoodButton != null)
        {
            incidentUnderstoodButton.clicked -= OnBattleConfirmClicked;
            incidentUnderstoodButton.clicked -= OnIncidentUnderstoodClicked;
            incidentUnderstoodButton.clicked += OnIncidentUnderstoodClicked;
            incidentUnderstoodButton.text = "ПОНЯТНО";
        }

        incidentModalDescription.style.display = DisplayStyle.Flex;
        incidentModalConsequence.style.display = DisplayStyle.Flex;
        if (battleIncidentImage != null)
            battleIncidentImage.style.display = DisplayStyle.Flex;

        if (battleModalWindow != null)
        {
            battleModalWindow.style.width = StyleKeyword.Null;
            battleModalWindow.style.maxWidth = StyleKeyword.Null;
            battleModalWindow.style.minHeight = StyleKeyword.Null;
        }
        if (battleModalBody != null)
            battleModalBody.style.width = StyleKeyword.Null;
        incidentModalTextColumn.style.width = StyleKeyword.Null;
        incidentModalTextColumn.style.flexGrow = StyleKeyword.Null;
    }

    private void SetBattleCompositionVisible(bool visible)
    {
        DisplayStyle style = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (battleLayout != null)
            battleLayout.style.display = style;
        if (battleDoctrineRow != null)
            battleDoctrineRow.style.display = style;
    }
}
