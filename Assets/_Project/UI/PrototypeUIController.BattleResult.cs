using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool battleResultUiInitialized;
    private VisualElement battleResultOverlay;
    private VisualElement battleResultWindow;
    private Label battleResultOutcome;
    private Label battleResultSummary;
    private VisualElement battleResultFighters;
    private Label battleResultEnemyName;
    private Label battleResultEnemyStatus;
    private Label battleResultConsequences;
    private Button battleResultUnderstoodButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeBattleResultRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeBattleResultUi)
            .ExecuteLater(190);
    }

    private void TryInitializeBattleResultUi()
    {
        if (battleResultUiInitialized)
            return;

        if (interfaceRoot == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInitializeBattleResultUi)
                    .ExecuteLater(60);
            }
            return;
        }

        CreateBattleResultUi();
        battleResultUiInitialized = true;
    }

    private void CreateBattleResultUi()
    {
        battleResultOverlay = new VisualElement();
        battleResultOverlay.name = "battle-result-overlay";
        battleResultOverlay.style.display = DisplayStyle.None;
        battleResultOverlay.style.position = Position.Absolute;
        battleResultOverlay.style.left = 0f;
        battleResultOverlay.style.right = 0f;
        battleResultOverlay.style.top = 0f;
        battleResultOverlay.style.bottom = 0f;
        battleResultOverlay.style.alignItems = Align.Center;
        battleResultOverlay.style.justifyContent = Justify.Center;
        battleResultOverlay.style.backgroundColor =
            new Color(0.01f, 0.015f, 0.02f, 0.78f);

        battleResultWindow = new VisualElement();
        battleResultWindow.style.width = 900f;
        battleResultWindow.style.maxWidth = Length.Percent(94f);
        battleResultWindow.style.minHeight = 560f;
        battleResultWindow.style.paddingLeft = 22f;
        battleResultWindow.style.paddingRight = 22f;
        battleResultWindow.style.paddingTop = 18f;
        battleResultWindow.style.paddingBottom = 18f;
        battleResultWindow.style.backgroundColor =
            new Color(0.10f, 0.115f, 0.135f, 1f);
        SetBorder(battleResultWindow, new Color(0.43f, 0.36f, 0.25f, 1f), 1f);
        SetRadius(battleResultWindow, 7f);

        Label title = new Label("РЕЗУЛЬТАТ БОЯ");
        title.style.fontSize = 14f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.unityTextAlign = TextAnchor.MiddleCenter;
        title.style.color = new Color(0.67f, 0.63f, 0.55f, 1f);
        battleResultWindow.Add(title);

        battleResultOutcome = new Label("ПОБЕДА");
        battleResultOutcome.style.marginTop = 8f;
        battleResultOutcome.style.fontSize = 30f;
        battleResultOutcome.style.unityFontStyleAndWeight = FontStyle.Bold;
        battleResultOutcome.style.unityTextAlign = TextAnchor.MiddleCenter;
        battleResultOutcome.style.color = new Color(0.87f, 0.72f, 0.39f, 1f);
        battleResultWindow.Add(battleResultOutcome);

        battleResultSummary = new Label();
        battleResultSummary.style.marginTop = 5f;
        battleResultSummary.style.marginBottom = 16f;
        battleResultSummary.style.fontSize = 12f;
        battleResultSummary.style.unityTextAlign = TextAnchor.MiddleCenter;
        battleResultSummary.style.whiteSpace = WhiteSpace.Normal;
        battleResultSummary.style.color = new Color(0.73f, 0.72f, 0.68f, 1f);
        battleResultWindow.Add(battleResultSummary);

        VisualElement columns = new VisualElement();
        columns.style.flexGrow = 1f;
        columns.style.flexDirection = FlexDirection.Row;
        columns.style.alignItems = Align.Stretch;

        VisualElement fightersColumn = new VisualElement();
        fightersColumn.style.width = Length.Percent(62f);
        fightersColumn.style.paddingRight = 16f;
        fightersColumn.Add(CreateBattleColumnTitle("ОТРЯД ПОСЛЕ БОЯ"));
        battleResultFighters = new VisualElement();
        fightersColumn.Add(battleResultFighters);
        columns.Add(fightersColumn);

        VisualElement enemyColumn = new VisualElement();
        enemyColumn.style.width = Length.Percent(38f);
        enemyColumn.style.paddingLeft = 16f;
        enemyColumn.Add(CreateBattleColumnTitle("ПРОТИВНИК"));

        VisualElement enemyCard = new VisualElement();
        enemyCard.style.minHeight = 210f;
        enemyCard.style.alignItems = Align.Center;
        enemyCard.style.justifyContent = Justify.Center;
        enemyCard.style.paddingLeft = 14f;
        enemyCard.style.paddingRight = 14f;
        enemyCard.style.backgroundColor = new Color(0.18f, 0.12f, 0.13f, 1f);
        SetBorder(enemyCard, new Color(0.39f, 0.23f, 0.23f, 1f), 1f);
        SetRadius(enemyCard, 5f);

        Label enemyImage = new Label("ИЗОБРАЖЕНИЕ\nПРОТИВНИКА");
        enemyImage.style.width = 180f;
        enemyImage.style.height = 120f;
        enemyImage.style.unityTextAlign = TextAnchor.MiddleCenter;
        enemyImage.style.whiteSpace = WhiteSpace.Normal;
        enemyImage.style.color = new Color(0.44f, 0.34f, 0.35f, 1f);
        enemyCard.Add(enemyImage);

        battleResultEnemyName = new Label();
        battleResultEnemyName.style.marginTop = 10f;
        battleResultEnemyName.style.fontSize = 15f;
        battleResultEnemyName.style.unityFontStyleAndWeight = FontStyle.Bold;
        battleResultEnemyName.style.unityTextAlign = TextAnchor.MiddleCenter;
        battleResultEnemyName.style.color = new Color(0.87f, 0.69f, 0.65f, 1f);
        enemyCard.Add(battleResultEnemyName);

        battleResultEnemyStatus = new Label();
        battleResultEnemyStatus.style.marginTop = 6f;
        battleResultEnemyStatus.style.fontSize = 12f;
        battleResultEnemyStatus.style.unityTextAlign = TextAnchor.MiddleCenter;
        battleResultEnemyStatus.style.color = new Color(0.69f, 0.60f, 0.57f, 1f);
        enemyCard.Add(battleResultEnemyStatus);
        enemyColumn.Add(enemyCard);
        columns.Add(enemyColumn);
        battleResultWindow.Add(columns);

        battleResultConsequences = new Label();
        battleResultConsequences.style.marginTop = 14f;
        battleResultConsequences.style.marginBottom = 12f;
        battleResultConsequences.style.fontSize = 12f;
        battleResultConsequences.style.unityTextAlign = TextAnchor.MiddleCenter;
        battleResultConsequences.style.whiteSpace = WhiteSpace.Normal;
        battleResultConsequences.style.color = new Color(0.73f, 0.72f, 0.67f, 1f);
        battleResultWindow.Add(battleResultConsequences);

        battleResultUnderstoodButton = new Button(CloseBattleResult)
        {
            text = "ПОНЯТНО"
        };
        battleResultUnderstoodButton.style.width = 260f;
        battleResultUnderstoodButton.style.height = 48f;
        battleResultUnderstoodButton.style.alignSelf = Align.Center;
        battleResultUnderstoodButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        battleResultUnderstoodButton.style.backgroundColor =
            new Color(0.30f, 0.25f, 0.17f, 1f);
        battleResultUnderstoodButton.style.color =
            new Color(0.91f, 0.84f, 0.69f, 1f);
        battleResultWindow.Add(battleResultUnderstoodButton);

        battleResultOverlay.Add(battleResultWindow);
        interfaceRoot.Add(battleResultOverlay);
    }

    private void ShowBattleResult(BattleContext context, BattleResult result)
    {
        if (!battleResultUiInitialized)
            TryInitializeBattleResultUi();
        if (battleResultOverlay == null || context == null || result == null)
            return;

        PauseForBlockingModal();
        battleResultOutcome.text = GetBattleOutcomeLabel(result.Outcome).ToUpper();
        battleResultSummary.text = BuildBattleResultSummary(result);
        battleResultEnemyName.text = context.EnemyName.ToUpper();
        battleResultEnemyStatus.text = GetEnemyResultStatus(result.Outcome);
        battleResultConsequences.text = BuildBattleResultConsequences(result);

        battleResultFighters.Clear();
        foreach (FighterBattleConsequence consequence in result.FighterConsequences)
            battleResultFighters.Add(CreateBattleResultFighterCard(consequence));

        battleResultOverlay.style.display = DisplayStyle.Flex;
        battleResultOverlay.BringToFront();
    }

    private VisualElement CreateBattleResultFighterCard(
        FighterBattleConsequence consequence)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("battle-result-fighter-card");
        card.style.height = 62f;
        card.style.marginBottom = 7f;
        card.style.paddingLeft = 10f;
        card.style.paddingRight = 10f;
        card.style.paddingTop = 7f;
        card.style.paddingBottom = 7f;
        card.style.backgroundColor = consequence.AfterState == FighterHealthState.Dead
            ? new Color(0.24f, 0.12f, 0.13f, 1f)
            : new Color(0.15f, 0.17f, 0.20f, 1f);
        SetBorder(
            card,
            consequence.AfterState == FighterHealthState.Dead
                ? new Color(0.52f, 0.25f, 0.24f, 1f)
                : new Color(0.29f, 0.31f, 0.34f, 1f),
            1f);
        SetRadius(card, 4f);

        Label name = new Label(consequence.FighterName);
        name.style.fontSize = 12f;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.color = new Color(0.88f, 0.84f, 0.75f, 1f);
        card.Add(name);

        Label state = new Label(
            consequence.BeforeHitPoints + " → " + consequence.AfterHitPoints +
            " HP · " + BattleSystem.GetHealthLabel(consequence.AfterState).ToUpper());
        state.style.marginTop = 3f;
        state.style.fontSize = 10f;
        state.style.color = consequence.AfterState == FighterHealthState.Dead
            ? new Color(0.83f, 0.40f, 0.37f, 1f)
            : new Color(0.67f, 0.68f, 0.66f, 1f);
        card.Add(state);

        VisualElement bar = new VisualElement();
        bar.style.height = 7f;
        bar.style.marginTop = 5f;
        bar.style.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 1f);
        SetRadius(bar, 2f);
        VisualElement fill = new VisualElement();
        fill.style.height = Length.Percent(100f);
        SetRadius(fill, 2f);
        bar.Add(fill);
        UpdateHealthBar(fill, consequence.AfterHitPoints, 100);
        card.Add(bar);

        string fighterId = consequence.FighterId;
        if (gameState != null && gameState.FindFighter(fighterId) != null)
        {
            card.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1)
                    return;
                OpenFighterDetails(fighterId);
                evt.StopPropagation();
            });
            card.tooltip = "ПКМ — сведения о бойце";
        }

        return card;
    }

    private string BuildBattleResultSummary(BattleResult result)
    {
        int wounded = 0;
        int dead = 0;
        foreach (FighterBattleConsequence consequence in result.FighterConsequences)
        {
            if (consequence.AfterState == FighterHealthState.Dead)
                dead++;
            else if (consequence.AfterHitPoints < consequence.BeforeHitPoints)
                wounded++;
        }

        string losses = dead > 0
            ? "Погибло: " + dead + ", ранено: " + wounded + "."
            : wounded > 0
                ? "Ранено бойцов: " + wounded + "."
                : "Отряд не получил новых ранений.";

        if (result.Kind == BattleKind.ExpeditionLocation)
            return losses + " Отряд остаётся на позиции и ждёт нового приказа.";
        return losses + " Защитники остаются в столице.";
    }

    private string BuildBattleResultConsequences(BattleResult result)
    {
        string text = string.Empty;
        if (result.FoodDelta != 0)
            text += "Пища " + FormatSigned(result.FoodDelta);
        if (result.MoodDelta != 0)
            text += (text.Length > 0 ? " · " : "") +
                "Настроение " + FormatSigned(result.MoodDelta);
        if (result.ArmyGoldDelta != 0)
            text += (text.Length > 0 ? " · " : "") +
                "Золото отряда " + FormatSigned(result.ArmyGoldDelta);
        if (result.ArmySupplyDelta != 0)
            text += (text.Length > 0 ? " · " : "") +
                "Снабжение " + FormatSigned(result.ArmySupplyDelta);

        return text.Length > 0
            ? "Стратегические последствия: " + text
            : "Стратегических потерь ресурсов нет.";
    }

    private string GetEnemyResultStatus(BattleOutcome outcome)
    {
        switch (outcome)
        {
            case BattleOutcome.Victory:
                return "РАЗБИТ";
            case BattleOutcome.CostlyVictory:
                return "ОТБРОШЕН";
            case BattleOutcome.Withdrawal:
                return "ОТРЯД ОТОШЁЛ";
            default:
                return "ПОЛЕ ОСТАЛОСЬ ЗА ПРОТИВНИКОМ";
        }
    }

    private void CloseBattleResult()
    {
        if (battleResultOverlay != null)
            battleResultOverlay.style.display = DisplayStyle.None;

        openedBattle = null;
        RefreshInterface();
        if (stableUiInitialized)
            RefreshStableUiAfterStateChange();
        RefreshContinuousTimeUi(true);
        RefreshTimeControlAvailability();
        ResumeAfterBlockingModalIfReady();
    }
}
