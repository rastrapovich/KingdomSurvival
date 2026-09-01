using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool enemyBattleRosterUiInitialized;
    private VisualElement battleResultEnemyRosterColumn;
    private IVisualElementScheduledItem enemyBattleRosterPoll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeEnemyBattleRosterRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeEnemyBattleRosterUi)
            .ExecuteLater(230);
    }

    private void TryInitializeEnemyBattleRosterUi()
    {
        if (enemyBattleRosterUiInitialized)
            return;

        if (interfaceRoot == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInitializeEnemyBattleRosterUi)
                    .ExecuteLater(60);
            }
            return;
        }

        enemyBattleRosterPoll = interfaceRoot.schedule
            .Execute(TickEnemyBattleRosterUi)
            .Every(30);
        enemyBattleRosterUiInitialized = true;
    }

    private void TickEnemyBattleRosterUi()
    {
        if (openedBattle == null ||
            openedBattle.Context == null ||
            openedBattle.Result == null ||
            openedBattle.Context.Enemies == null ||
            openedBattle.Context.Enemies.Count == 0)
        {
            return;
        }

        if (battleLayout != null &&
            battleLayout.resolvedStyle.display != DisplayStyle.None)
        {
            RenderEnemyRosterInPreview(openedBattle.Context);
            if (battleStrategicConsequenceLabel != null)
            {
                battleStrategicConsequenceLabel.text =
                    BuildBattleRewardAndConsequenceText(openedBattle.Result, true);
            }
        }

        if (battleResultOverlay != null &&
            battleResultOverlay.resolvedStyle.display != DisplayStyle.None)
        {
            RenderEnemyRosterInResult(openedBattle.Context, openedBattle.Result);
            if (battleResultConsequences != null)
            {
                battleResultConsequences.text =
                    BuildBattleRewardAndConsequenceText(openedBattle.Result, false);
            }
        }
    }

    private void RenderEnemyRosterInPreview(BattleContext context)
    {
        if (battleEnemyColumn == null)
            return;

        string signature = "preview:" + context.Id + ":" + context.Enemies.Count;
        bool alreadyRendered =
            object.Equals(battleEnemyColumn.userData, signature) &&
            battleEnemyColumn.childCount == context.Enemies.Count + 1 &&
            battleEnemyColumn.Q<VisualElement>(className: "battle-enemy-unit-card") != null;
        if (alreadyRendered)
            return;

        battleEnemyColumn.Clear();
        battleEnemyColumn.userData = signature;
        battleEnemyColumn.Add(CreateBattleColumnTitle("ПРОТИВНИК"));

        foreach (BattleEnemyUnit enemy in context.Enemies)
            battleEnemyColumn.Add(CreateEnemyRosterCard(enemy, false, openedBattle.Result.Outcome));
    }

    private void RenderEnemyRosterInResult(BattleContext context, BattleResult result)
    {
        if (battleResultEnemyRosterColumn == null && battleResultEnemyName != null)
        {
            VisualElement oldEnemyCard = battleResultEnemyName.parent;
            if (oldEnemyCard != null)
                battleResultEnemyRosterColumn = oldEnemyCard.parent;
        }

        if (battleResultEnemyRosterColumn == null)
            return;

        string signature = "result:" + context.Id + ":" + context.Enemies.Count + ":" + result.Outcome;
        bool alreadyRendered =
            object.Equals(battleResultEnemyRosterColumn.userData, signature) &&
            battleResultEnemyRosterColumn.childCount == context.Enemies.Count + 1 &&
            battleResultEnemyRosterColumn.Q<VisualElement>(className: "battle-result-enemy-unit-card") != null;
        if (alreadyRendered)
            return;

        battleResultEnemyRosterColumn.Clear();
        battleResultEnemyRosterColumn.userData = signature;
        battleResultEnemyRosterColumn.Add(CreateBattleColumnTitle("ПРОТИВНИК ПОСЛЕ БОЯ"));

        foreach (BattleEnemyUnit enemy in context.Enemies)
            battleResultEnemyRosterColumn.Add(CreateEnemyRosterCard(enemy, true, result.Outcome));
    }

    private VisualElement CreateEnemyRosterCard(
        BattleEnemyUnit enemy,
        bool resultMode,
        BattleOutcome outcome)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList(
            resultMode ? "battle-result-enemy-unit-card" : "battle-enemy-unit-card");
        card.style.minHeight = resultMode ? 58f : 68f;
        card.style.marginBottom = 7f;
        card.style.paddingLeft = 10f;
        card.style.paddingRight = 10f;
        card.style.paddingTop = 7f;
        card.style.paddingBottom = 7f;
        card.style.backgroundColor = new Color(0.20f, 0.13f, 0.14f, 1f);
        SetBorder(card, new Color(0.43f, 0.24f, 0.23f, 1f), 1f);
        SetRadius(card, 4f);

        Label name = new Label(enemy.Name.ToUpper());
        name.style.fontSize = 12f;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.color = new Color(0.88f, 0.70f, 0.66f, 1f);
        card.Add(name);

        Label type = new Label(enemy.TypeLabel);
        type.style.marginTop = 1f;
        type.style.fontSize = 9f;
        type.style.color = new Color(0.60f, 0.50f, 0.49f, 1f);
        card.Add(type);

        if (!resultMode)
        {
            Label stats = new Label(
                enemy.MaxHitPoints + " HP · атака " + enemy.AttackPower +
                " · защита " + enemy.DefensePower);
            stats.style.marginTop = 3f;
            stats.style.fontSize = 10f;
            stats.style.color = new Color(0.72f, 0.62f, 0.59f, 1f);
            card.Add(stats);

            VisualElement bar = new VisualElement();
            bar.style.height = 6f;
            bar.style.marginTop = 5f;
            bar.style.backgroundColor = new Color(0.09f, 0.07f, 0.08f, 1f);
            SetRadius(bar, 2f);
            VisualElement fill = new VisualElement();
            fill.style.width = Length.Percent(100f);
            fill.style.height = Length.Percent(100f);
            fill.style.backgroundColor = new Color(0.61f, 0.27f, 0.25f, 1f);
            SetRadius(fill, 2f);
            bar.Add(fill);
            card.Add(bar);
        }
        else
        {
            Label status = new Label(GetEnemyUnitResultStatus(outcome));
            status.style.marginTop = 4f;
            status.style.fontSize = 10f;
            status.style.unityFontStyleAndWeight = FontStyle.Bold;
            status.style.color = new Color(0.75f, 0.58f, 0.54f, 1f);
            card.Add(status);
        }

        return card;
    }

    private string BuildBattleRewardAndConsequenceText(
        BattleResult result,
        bool previewMode)
    {
        string reward = string.Empty;
        if (result.Kind == BattleKind.ExpeditionLocation)
        {
            if (result.ArmyGoldDelta != 0)
                reward = "золото отряда " + FormatSigned(result.ArmyGoldDelta);
            if (result.ArmySupplyDelta != 0)
            {
                reward += (reward.Length > 0 ? " · " : "") +
                    "снабжение " + FormatSigned(result.ArmySupplyDelta);
            }
        }
        else if (result.GoldDelta != 0)
        {
            reward = "золото " + FormatSigned(result.GoldDelta);
        }

        string consequences = string.Empty;
        if (result.FoodDelta != 0)
            consequences = "пища " + FormatSigned(result.FoodDelta);
        if (result.MoodDelta != 0)
        {
            consequences += (consequences.Length > 0 ? " · " : "") +
                "настроение " + FormatSigned(result.MoodDelta);
        }

        string text = string.Empty;
        if (reward.Length > 0)
            text = (previewMode ? "Ожидаемая добыча: " : "Добыча: ") + reward;
        if (consequences.Length > 0)
        {
            text += (text.Length > 0 ? "\n" : "") +
                "Последствия: " + consequences;
        }

        if (text.Length == 0)
            return previewMode ? "Дополнительных ресурсов не ожидается" : "Новых изменений ресурсов нет";
        return text;
    }

    private string GetEnemyUnitResultStatus(BattleOutcome outcome)
    {
        switch (outcome)
        {
            case BattleOutcome.Victory:
                return "РАЗБИТ";
            case BattleOutcome.CostlyVictory:
                return "ОТБРОШЕН";
            case BattleOutcome.Withdrawal:
                return "ОСТАЛСЯ НА ПОЛЕ";
            default:
                return "УДЕРЖАЛ ПОЛЕ";
        }
    }
}
