using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattleSandboxController : MonoBehaviour
    {
        private readonly HashSet<string> selectedFighterIds = new HashSet<string>
        {
            "garrick",
            "edric",
            "torvin",
            "agnessa"
        };
        private readonly Dictionary<string, Button> rosterButtons = new Dictionary<string, Button>();
        private readonly List<string> battleLog = new List<string>();

        private VisualElement root;
        private Label selectedCountLabel;
        private Button startBattleButton;

        private SandboxBattle battle;
        private HexBoardElement board;
        private Label roundLabel;
        private VisualElement initiativeRow;
        private Label currentUnitLabel;
        private Label currentStatsLabel;
        private Label targetLabel;
        private Label instructionLabel;
        private Label logLabel;
        private Button attackButton;
        private Button guardButton;
        private Button endActivationButton;
        private VisualElement resultBanner;
        private Label resultLabel;
        private string selectedTargetId;
        private bool initialized;
        private bool enemyStepScheduled;

        private void OnEnable()
        {
            UIDocument document = GetComponent<UIDocument>();
            root = document != null ? document.rootVisualElement : null;
            if (root == null)
                return;

            root.schedule.Execute(Initialize).ExecuteLater(1);
        }

        private void Initialize()
        {
            if (initialized || root == null)
                return;

            initialized = true;
            root.style.flexGrow = 1f;
            root.style.backgroundColor = new Color(0.035f, 0.043f, 0.050f, 1f);
            root.style.color = new Color(0.88f, 0.84f, 0.76f, 1f);
            BuildSetupScreen();
        }

        private void BuildSetupScreen()
        {
            enemyStepScheduled = false;
            selectedTargetId = null;
            battle = null;
            rosterButtons.Clear();
            root.Clear();

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.paddingLeft = 36f;
            scroll.style.paddingRight = 36f;
            scroll.style.paddingTop = 24f;
            scroll.style.paddingBottom = 28f;
            root.Add(scroll);

            Label eyebrow = CreateLabel("ИЗОЛИРОВАННЫЙ БОЕВОЙ ПОЛИГОН", 12, new Color(0.62f, 0.57f, 0.47f, 1f));
            eyebrow.style.unityFontStyleAndWeight = FontStyle.Bold;
            scroll.Add(eyebrow);

            Label title = CreateLabel("ГЕКСОВЫЙ БОЙ · ЧЁРНЫЙ ЛЕС", 30, new Color(0.95f, 0.84f, 0.60f, 1f));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginTop = 5f;
            scroll.Add(title);

            Label description = CreateLabel(
                "Выберите от одного до шести бойцов. Полигон не читает и не изменяет состояние основной игры.",
                13,
                new Color(0.72f, 0.72f, 0.69f, 1f));
            description.style.marginTop = 8f;
            description.style.marginBottom = 22f;
            description.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(description);

            VisualElement rosterPanel = CreatePanel();
            rosterPanel.Add(CreateSectionTitle("ВАШ ОТРЯД"));

            VisualElement rosterGrid = new VisualElement();
            rosterGrid.style.flexDirection = FlexDirection.Row;
            rosterGrid.style.flexWrap = Wrap.Wrap;
            rosterGrid.style.marginTop = 10f;
            foreach (SandboxUnitDefinition definition in SandboxRoster.PlayerRoster)
            {
                SandboxUnitDefinition captured = definition;
                Button card = new Button(() => ToggleFighter(captured.Id));
                card.text = BuildRosterCardText(definition);
                card.style.width = 190f;
                card.style.height = 126f;
                card.style.marginRight = 10f;
                card.style.marginBottom = 10f;
                card.style.paddingLeft = 12f;
                card.style.paddingRight = 12f;
                card.style.paddingTop = 10f;
                card.style.paddingBottom = 10f;
                card.style.whiteSpace = WhiteSpace.Normal;
                card.style.unityTextAlign = TextAnchor.MiddleLeft;
                card.style.unityFontStyleAndWeight = FontStyle.Bold;
                card.style.fontSize = 12f;
                SetRadius(card, 5f);
                rosterButtons[definition.Id] = card;
                rosterGrid.Add(card);
            }
            rosterPanel.Add(rosterGrid);

            selectedCountLabel = CreateLabel(string.Empty, 12, new Color(0.78f, 0.74f, 0.66f, 1f));
            selectedCountLabel.style.marginTop = 3f;
            rosterPanel.Add(selectedCountLabel);
            scroll.Add(rosterPanel);

            VisualElement enemyPanel = CreatePanel();
            enemyPanel.style.marginTop = 14f;
            enemyPanel.Add(CreateSectionTitle("ПРОТИВНИК · ЗАСАДА В ЧЁРНОМ ЛЕСУ"));

            VisualElement enemyGrid = new VisualElement();
            enemyGrid.style.flexDirection = FlexDirection.Row;
            enemyGrid.style.flexWrap = Wrap.Wrap;
            enemyGrid.style.marginTop = 10f;
            foreach (SandboxUnitDefinition enemy in SandboxRoster.EnemyRoster)
            {
                Label enemyCard = CreateLabel(
                    enemy.Name.ToUpper() + "\n" +
                    enemy.MaxHitPoints + " HP  ·  АТК " + enemy.Attack + "  ·  ЗАЩ " + enemy.Defense,
                    11,
                    new Color(0.88f, 0.71f, 0.68f, 1f));
                enemyCard.style.width = 190f;
                enemyCard.style.height = 72f;
                enemyCard.style.marginRight = 10f;
                enemyCard.style.marginBottom = 8f;
                enemyCard.style.paddingLeft = 11f;
                enemyCard.style.paddingRight = 11f;
                enemyCard.style.paddingTop = 10f;
                enemyCard.style.backgroundColor = new Color(0.20f, 0.10f, 0.11f, 1f);
                enemyCard.style.whiteSpace = WhiteSpace.Normal;
                SetBorder(enemyCard, new Color(0.39f, 0.20f, 0.20f, 1f));
                SetRadius(enemyCard, 4f);
                enemyGrid.Add(enemyCard);
            }
            enemyPanel.Add(enemyGrid);
            scroll.Add(enemyPanel);

            startBattleButton = new Button(StartBattle) { text = "НАЧАТЬ БОЙ" };
            StylePrimaryButton(startBattleButton);
            startBattleButton.style.width = 320f;
            startBattleButton.style.height = 52f;
            startBattleButton.style.marginTop = 20f;
            startBattleButton.style.alignSelf = Align.Center;
            scroll.Add(startBattleButton);

            RefreshRosterSelection();
        }

        private void ToggleFighter(string fighterId)
        {
            if (selectedFighterIds.Contains(fighterId))
                selectedFighterIds.Remove(fighterId);
            else if (selectedFighterIds.Count < 6)
                selectedFighterIds.Add(fighterId);

            RefreshRosterSelection();
        }

        private void RefreshRosterSelection()
        {
            foreach (KeyValuePair<string, Button> pair in rosterButtons)
            {
                bool selected = selectedFighterIds.Contains(pair.Key);
                pair.Value.style.backgroundColor = selected
                    ? new Color(0.28f, 0.25f, 0.16f, 1f)
                    : new Color(0.12f, 0.14f, 0.16f, 1f);
                SetBorder(
                    pair.Value,
                    selected
                        ? new Color(0.80f, 0.65f, 0.31f, 1f)
                        : new Color(0.28f, 0.30f, 0.31f, 1f),
                    selected ? 2f : 1f);
                pair.Value.style.color = selected
                    ? new Color(0.96f, 0.87f, 0.67f, 1f)
                    : new Color(0.70f, 0.70f, 0.68f, 1f);
            }

            selectedCountLabel.text =
                "Выбрано: " + selectedFighterIds.Count + " / 6" +
                (selectedFighterIds.Count == 0 ? " · выберите хотя бы одного бойца" : string.Empty);
            startBattleButton.SetEnabled(selectedFighterIds.Count > 0);
        }

        private void StartBattle()
        {
            if (selectedFighterIds.Count == 0)
                return;

            battle = SandboxRoster.CreateDefaultBattle(selectedFighterIds);
            battleLog.Clear();
            battleLog.Add("Бой начался. Враг перекрывает дорогу через Чёрный лес.");
            selectedTargetId = null;
            BuildBattleScreen();
            RefreshBattleScreen();
        }

        private void BuildBattleScreen()
        {
            root.Clear();

            VisualElement screen = new VisualElement();
            screen.style.flexGrow = 1f;
            screen.style.paddingLeft = 22f;
            screen.style.paddingRight = 22f;
            screen.style.paddingTop = 16f;
            screen.style.paddingBottom = 18f;
            root.Add(screen);

            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;

            VisualElement heading = new VisualElement();
            Label title = CreateLabel("БОЕВОЙ ПОЛИГОН · ЧЁРНЫЙ ЛЕС", 20, new Color(0.94f, 0.81f, 0.55f, 1f));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.Add(title);
            roundLabel = CreateLabel(string.Empty, 12, new Color(0.68f, 0.66f, 0.61f, 1f));
            roundLabel.style.marginTop = 3f;
            heading.Add(roundLabel);
            header.Add(heading);

            Button returnButton = new Button(BuildSetupScreen) { text = "НОВЫЙ СОСТАВ" };
            StyleSecondaryButton(returnButton);
            returnButton.style.width = 170f;
            header.Add(returnButton);
            screen.Add(header);

            initiativeRow = new VisualElement();
            initiativeRow.style.height = 54f;
            initiativeRow.style.marginTop = 12f;
            initiativeRow.style.marginBottom = 12f;
            initiativeRow.style.paddingLeft = 8f;
            initiativeRow.style.paddingRight = 8f;
            initiativeRow.style.flexDirection = FlexDirection.Row;
            initiativeRow.style.alignItems = Align.Center;
            initiativeRow.style.backgroundColor = new Color(0.08f, 0.095f, 0.105f, 1f);
            SetBorder(initiativeRow, new Color(0.24f, 0.26f, 0.27f, 1f));
            SetRadius(initiativeRow, 4f);
            screen.Add(initiativeRow);

            VisualElement body = new VisualElement();
            body.style.flexGrow = 1f;
            body.style.flexDirection = FlexDirection.Row;
            body.style.alignItems = Align.Stretch;

            board = new HexBoardElement();
            board.style.marginRight = 14f;
            board.HexClicked += OnBoardHexClicked;
            body.Add(board);

            VisualElement sidebar = CreatePanel();
            sidebar.style.width = 330f;
            sidebar.style.marginTop = 0f;
            sidebar.style.flexShrink = 0f;

            currentUnitLabel = CreateLabel(string.Empty, 17, new Color(0.95f, 0.83f, 0.59f, 1f));
            currentUnitLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            sidebar.Add(currentUnitLabel);

            currentStatsLabel = CreateLabel(string.Empty, 12, new Color(0.75f, 0.74f, 0.69f, 1f));
            currentStatsLabel.style.marginTop = 5f;
            currentStatsLabel.style.whiteSpace = WhiteSpace.Normal;
            sidebar.Add(currentStatsLabel);

            instructionLabel = CreateLabel(
                "Синие гексы — перемещение. Красные — доступные цели. Сначала выберите врага, затем подтвердите атаку.",
                11,
                new Color(0.58f, 0.65f, 0.67f, 1f));
            instructionLabel.style.marginTop = 13f;
            instructionLabel.style.whiteSpace = WhiteSpace.Normal;
            sidebar.Add(instructionLabel);

            targetLabel = CreateLabel("Цель не выбрана", 12, new Color(0.82f, 0.66f, 0.62f, 1f));
            targetLabel.style.marginTop = 14f;
            targetLabel.style.whiteSpace = WhiteSpace.Normal;
            sidebar.Add(targetLabel);

            attackButton = new Button(PerformAttack) { text = "АТАКОВАТЬ" };
            StylePrimaryButton(attackButton);
            attackButton.style.marginTop = 9f;
            sidebar.Add(attackButton);

            guardButton = new Button(PerformGuard) { text = "ЗАЩИТНАЯ СТОЙКА" };
            StyleSecondaryButton(guardButton);
            guardButton.style.marginTop = 8f;
            sidebar.Add(guardButton);

            endActivationButton = new Button(EndPlayerActivation) { text = "ЗАКОНЧИТЬ ХОД" };
            StyleSecondaryButton(endActivationButton);
            endActivationButton.style.marginTop = 8f;
            sidebar.Add(endActivationButton);

            Label logTitle = CreateSectionTitle("ХОД БОЯ");
            logTitle.style.marginTop = 18f;
            sidebar.Add(logTitle);
            logLabel = CreateLabel(string.Empty, 10, new Color(0.66f, 0.67f, 0.65f, 1f));
            logLabel.style.marginTop = 7f;
            logLabel.style.whiteSpace = WhiteSpace.Normal;
            logLabel.style.flexGrow = 1f;
            sidebar.Add(logLabel);

            resultBanner = new VisualElement();
            resultBanner.style.display = DisplayStyle.None;
            resultBanner.style.marginTop = 12f;
            resultBanner.style.paddingLeft = 12f;
            resultBanner.style.paddingRight = 12f;
            resultBanner.style.paddingTop = 12f;
            resultBanner.style.paddingBottom = 12f;
            resultBanner.style.backgroundColor = new Color(0.22f, 0.18f, 0.10f, 1f);
            SetBorder(resultBanner, new Color(0.70f, 0.54f, 0.24f, 1f));
            SetRadius(resultBanner, 4f);

            resultLabel = CreateLabel(string.Empty, 16, new Color(0.96f, 0.84f, 0.57f, 1f));
            resultLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            resultLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            resultBanner.Add(resultLabel);

            Button repeatButton = new Button(StartBattle) { text = "ПОВТОРИТЬ БОЙ" };
            StylePrimaryButton(repeatButton);
            repeatButton.style.marginTop = 9f;
            resultBanner.Add(repeatButton);
            sidebar.Add(resultBanner);

            body.Add(sidebar);
            screen.Add(body);
        }

        private void OnBoardHexClicked(HexCoord coord)
        {
            SandboxUnitState current = battle != null ? battle.CurrentUnit : null;
            if (current == null || current.Team != SandboxTeam.Player ||
                battle.Phase != SandboxBattlePhase.InProgress)
            {
                return;
            }

            SandboxUnitState occupant = battle.GetUnitAt(coord);
            if (occupant != null)
            {
                if (occupant.Team == SandboxTeam.Enemy)
                    selectedTargetId = occupant.Id;
                else
                    selectedTargetId = null;
                RefreshBattleScreen();
                return;
            }

            string message;
            if (battle.TryMove(current.Id, coord, out message))
            {
                AddBattleLog(message);
                selectedTargetId = null;
                FinishActivationIfEmpty();
                RefreshBattleScreen();
            }
        }

        private void PerformAttack()
        {
            SandboxUnitState current = battle != null ? battle.CurrentUnit : null;
            if (current == null || string.IsNullOrEmpty(selectedTargetId))
                return;

            string message;
            if (battle.TryAttack(current.Id, selectedTargetId, out message))
            {
                AddBattleLog(message);
                selectedTargetId = null;
                FinishActivationIfEmpty();
                RefreshBattleScreen();
            }
        }

        private void PerformGuard()
        {
            SandboxUnitState current = battle != null ? battle.CurrentUnit : null;
            if (current == null)
                return;

            string message;
            if (battle.TryGuard(current.Id, out message))
            {
                AddBattleLog(message);
                FinishActivationIfEmpty();
                RefreshBattleScreen();
            }
        }

        private void EndPlayerActivation()
        {
            if (battle == null || battle.CurrentUnit == null ||
                battle.CurrentUnit.Team != SandboxTeam.Player)
            {
                return;
            }

            AddBattleLog(battle.CurrentUnit.Name + " завершает активацию.");
            battle.EndActivation();
            selectedTargetId = null;
            RefreshBattleScreen();
        }

        private void FinishActivationIfEmpty()
        {
            if (battle.Phase == SandboxBattlePhase.InProgress &&
                battle.CurrentUnit != null && battle.CurrentUnit.ActionPoints <= 0)
            {
                battle.EndActivation();
            }
        }

        private void RefreshBattleScreen()
        {
            if (battle == null || board == null)
                return;

            board.SetBattle(battle, selectedTargetId);
            roundLabel.text = "Раунд " + battle.Round + " · два действия на активацию";
            RefreshInitiativeRow();

            SandboxUnitState current = battle.CurrentUnit;
            bool playerTurn = battle.Phase == SandboxBattlePhase.InProgress &&
                              current != null && current.Team == SandboxTeam.Player;

            if (current != null)
            {
                currentUnitLabel.text =
                    (current.Team == SandboxTeam.Player ? "ВАШ ХОД · " : "ХОД ВРАГА · ") +
                    current.Name.ToUpper();
                currentStatsLabel.text =
                    current.Definition.RoleLabel + "\n" +
                    "HP " + current.HitPoints + "/" + current.MaxHitPoints +
                    "  ·  действия " + current.ActionPoints + "/2\n" +
                    "АТК " + current.Attack + "  ·  ЗАЩ " + current.Defense +
                    "  ·  ХОД " + current.Movement + "  ·  ИНИЦ " + current.Initiative;
            }
            else
            {
                currentUnitLabel.text = "БОЙ ЗАВЕРШЁН";
                currentStatsLabel.text = string.Empty;
            }

            SandboxAttackPreview preview = null;
            SandboxUnitState target = !string.IsNullOrEmpty(selectedTargetId)
                ? battle.GetUnit(selectedTargetId)
                : null;
            if (current != null && target != null)
                preview = battle.PreviewAttack(current.Id, target.Id);

            if (target == null || target.IsDefeated)
            {
                targetLabel.text = "Цель не выбрана";
                attackButton.text = "АТАКОВАТЬ";
                attackButton.SetEnabled(false);
            }
            else
            {
                targetLabel.text =
                    "Цель: " + target.Name + " · HP " + target.HitPoints + "/" + target.MaxHitPoints +
                    (preview != null && preview.IsValid
                        ? "\nПрогноз: " + preview.Damage + " урона · останется " + preview.TargetHitPointsAfter + " HP"
                        : "\n" + (preview != null ? preview.Reason : "Недоступно"));
                attackButton.text = preview != null && preview.IsValid
                    ? "АТАКОВАТЬ · " + preview.Damage + " УРОНА"
                    : "АТАКА НЕДОСТУПНА";
                attackButton.SetEnabled(playerTurn && preview != null && preview.IsValid);
            }

            guardButton.SetEnabled(playerTurn && current.ActionPoints > 0 && !current.IsGuarding);
            endActivationButton.SetEnabled(playerTurn);
            instructionLabel.text = playerTurn
                ? "Синие гексы — перемещение. Красные — доступные цели. Сначала выберите врага, затем подтвердите атаку."
                : battle.Phase == SandboxBattlePhase.InProgress
                    ? "Противник выполняет свою активацию…"
                    : "Можно повторить бой тем же составом или вернуться к выбору бойцов.";

            logLabel.text = string.Join("\n", battleLog.Skip(Math.Max(0, battleLog.Count - 9)));
            RefreshResultBanner();
            QueueEnemyStepIfNeeded();
        }

        private void RefreshInitiativeRow()
        {
            initiativeRow.Clear();
            Label caption = CreateLabel("ОЧЕРЕДЬ", 10, new Color(0.55f, 0.55f, 0.52f, 1f));
            caption.style.width = 70f;
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            initiativeRow.Add(caption);

            foreach (string unitId in battle.TurnOrderIds)
            {
                SandboxUnitState unit = battle.GetUnit(unitId);
                if (unit == null || unit.IsDefeated)
                    continue;

                bool active = battle.CurrentUnit != null && battle.CurrentUnit.Id == unit.Id;
                Label chip = CreateLabel(
                    unit.Name + " · " + unit.HitPoints,
                    10,
                    unit.Team == SandboxTeam.Player
                        ? new Color(0.93f, 0.80f, 0.54f, 1f)
                        : new Color(0.90f, 0.58f, 0.55f, 1f));
                chip.style.height = 32f;
                chip.style.paddingLeft = 9f;
                chip.style.paddingRight = 9f;
                chip.style.marginRight = 5f;
                chip.style.unityTextAlign = TextAnchor.MiddleCenter;
                chip.style.backgroundColor = active
                    ? new Color(0.29f, 0.30f, 0.25f, 1f)
                    : new Color(0.12f, 0.14f, 0.15f, 1f);
                SetBorder(
                    chip,
                    active
                        ? new Color(0.90f, 0.77f, 0.42f, 1f)
                        : new Color(0.25f, 0.26f, 0.26f, 1f),
                    active ? 2f : 1f);
                SetRadius(chip, 4f);
                initiativeRow.Add(chip);
            }
        }

        private void RefreshResultBanner()
        {
            if (battle.Phase == SandboxBattlePhase.InProgress)
            {
                resultBanner.style.display = DisplayStyle.None;
                return;
            }

            resultBanner.style.display = DisplayStyle.Flex;
            int survivors = battle.Units.Count(unit => unit.Team == SandboxTeam.Player && !unit.IsDefeated);
            resultLabel.text = battle.Phase == SandboxBattlePhase.PlayerVictory
                ? "ПОБЕДА\nВыжило бойцов: " + survivors
                : "ПОРАЖЕНИЕ\nОтряд выведен из строя";
        }

        private void QueueEnemyStepIfNeeded()
        {
            if (enemyStepScheduled || battle == null ||
                battle.Phase != SandboxBattlePhase.InProgress ||
                battle.CurrentUnit == null || battle.CurrentUnit.Team != SandboxTeam.Enemy)
            {
                return;
            }

            enemyStepScheduled = true;
            root.schedule.Execute(() =>
            {
                enemyStepScheduled = false;
                if (battle == null || battle.Phase != SandboxBattlePhase.InProgress ||
                    battle.CurrentUnit == null || battle.CurrentUnit.Team != SandboxTeam.Enemy)
                {
                    return;
                }

                IReadOnlyList<string> events = SandboxEnemyPlanner.TakeCurrentTurn(battle);
                foreach (string entry in events)
                    AddBattleLog(entry);
                selectedTargetId = null;
                RefreshBattleScreen();
            }).ExecuteLater(420);
        }

        private void AddBattleLog(string entry)
        {
            if (!string.IsNullOrWhiteSpace(entry))
                battleLog.Add("• " + entry);
        }

        private static string BuildRosterCardText(SandboxUnitDefinition definition)
        {
            return definition.Name.ToUpper() + "\n" + definition.RoleLabel + "\n" +
                   "HP " + definition.MaxHitPoints + "   АТК " + definition.Attack +
                   "   ЗАЩ " + definition.Defense + "\n" +
                   "ХОД " + definition.Movement + "   ИНИЦ " + definition.Initiative +
                   "   ДАЛЬН " + definition.AttackRange;
        }

        private static VisualElement CreatePanel()
        {
            VisualElement panel = new VisualElement();
            panel.style.paddingLeft = 16f;
            panel.style.paddingRight = 16f;
            panel.style.paddingTop = 14f;
            panel.style.paddingBottom = 14f;
            panel.style.backgroundColor = new Color(0.085f, 0.10f, 0.11f, 1f);
            SetBorder(panel, new Color(0.25f, 0.27f, 0.28f, 1f));
            SetRadius(panel, 5f);
            return panel;
        }

        private static Label CreateSectionTitle(string text)
        {
            Label label = CreateLabel(text, 12, new Color(0.72f, 0.67f, 0.56f, 1f));
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static Label CreateLabel(string text, int size, Color color)
        {
            Label label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            return label;
        }

        private static void StylePrimaryButton(Button button)
        {
            button.style.height = 44f;
            button.style.backgroundColor = new Color(0.36f, 0.29f, 0.15f, 1f);
            button.style.color = new Color(0.97f, 0.87f, 0.65f, 1f);
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            SetBorder(button, new Color(0.70f, 0.56f, 0.27f, 1f));
            SetRadius(button, 4f);
        }

        private static void StyleSecondaryButton(Button button)
        {
            button.style.height = 40f;
            button.style.backgroundColor = new Color(0.14f, 0.16f, 0.17f, 1f);
            button.style.color = new Color(0.78f, 0.77f, 0.72f, 1f);
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            SetBorder(button, new Color(0.31f, 0.33f, 0.33f, 1f));
            SetRadius(button, 4f);
        }

        private static void SetBorder(VisualElement element, Color color, float width = 1f)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
