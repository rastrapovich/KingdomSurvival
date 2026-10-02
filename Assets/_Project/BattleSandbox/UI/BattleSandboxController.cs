using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattleSandboxController : MonoBehaviour
    {
        private readonly List<string> battleLog = new List<string>();

        private SandboxUnitContent unitContent;
        private VisualElement root;
        private Button startBattleButton;
        private SandboxFighterDetailsView fighterDetailsView;

        private SandboxBattle battle;
        private HexBoardElement board;
        private BattlefieldDatabaseAsset battlefieldDatabase;
        private Label roundLabel;
        private VisualElement initiativeRow;
        private Label currentUnitLabel;
        private Label currentStatsLabel;
        private Label targetLabel;
        private Label instructionLabel;
        private Label logLabel;
        private Button returnToSetupButton;
        private Button guardButton;
        private Button endActivationButton;
        private VisualElement resultBanner;
        private Label resultLabel;
        private string selectedTargetId;
        private bool initialized;
        private bool enemyStepScheduled;
        private bool combatAnimationRunning;
        // Номер боя: колбэки анимаций и отложенные ходы прежнего боя (после
        // «Повторить бой» или «Новый состав») игнорируются.
        private int battleGeneration;

        // Новый бой или экран состава: незавершённый показ прежнего боя не
        // должен оставить флаги взведёнными — иначе в новом бою никто не ходит.
        private void ResetCombatFlow()
        {
            battleGeneration++;
            enemyStepScheduled = false;
            combatAnimationRunning = false;
        }

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
            unitContent = SandboxUnitDatabaseAdapter.Load();
            battlefieldDatabase = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);

            root.style.flexGrow = 1f;
            root.style.backgroundColor = new Color(0.035f, 0.043f, 0.050f, 1f);
            root.style.color = new Color(0.88f, 0.84f, 0.76f, 1f);

            // ПР-03: кампания пришла в бой со своим отрядом — полигонный
            // выбор состава пропускается.
            campaignBattle = CampaignSession.PendingBattle;
            if (campaignBattle != null && StartCampaignBattle())
                return;

            campaignBattle = null;
            BuildSetupScreen();
        }

        // ------------------------------------------------------------------
        // ПР-03: бой кампании. Юнит i — участник запроса i (герой и бойцы
        // похода) со своим именем и боевой основой из UnitDatabase по
        // UnitTypeId. По итогу — «Вернуться в кампанию» с павшими по ID.
        // ------------------------------------------------------------------

        private CampaignBattleRequest campaignBattle;
        private readonly List<CampaignBattleParticipant> campaignParticipants = new List<CampaignBattleParticipant>();
        private readonly List<string> campaignUnitIds = new List<string>();

        private bool StartCampaignBattle()
        {
            campaignParticipants.Clear();
            List<SandboxUnitDefinition> fighters = new List<SandboxUnitDefinition>();
            foreach (CampaignBattleParticipant participant in campaignBattle.Participants)
            {
                SandboxUnitDefinition baseDefinition = unitContent.PlayerRoster
                    .FirstOrDefault(definition => definition.Id == participant.UnitTypeId);
                if (baseDefinition == null)
                {
                    Debug.LogWarning("Бой кампании: нет боевой основы '" + participant.UnitTypeId +
                                     "' для " + participant.DisplayName + " — участник пропущен.");
                    continue;
                }

                // ПР-08: числа, собранные кампанией (шаблон + качества + вещи +
                // состояния), — те же, что на экране героя.
                bool assembled = participant.HasAssembledStats;
                fighters.Add(new SandboxUnitDefinition(
                    baseDefinition.Id,
                    participant.DisplayName,
                    baseDefinition.Role,
                    assembled ? participant.MaxHitPoints : baseDefinition.MaxHitPoints,
                    assembled ? participant.Attack : baseDefinition.Attack,
                    assembled ? participant.Defense : baseDefinition.Defense,
                    assembled ? participant.Damage : baseDefinition.Damage,
                    assembled ? participant.Movement : baseDefinition.Movement,
                    assembled ? participant.Initiative : baseDefinition.Initiative,
                    assembled ? participant.AttackRange : baseDefinition.AttackRange,
                    baseDefinition.TagIds,
                    // 12Е-6: боевые правила особенностей человека.
                    participant.PerkIds));
                campaignParticipants.Add(participant);
            }

            if (fighters.Count == 0)
            {
                Debug.LogError("Бой кампании: в запросе нет ни одного участника с боевой основой.");
                return false;
            }

            // Тот же формат ID, что задаёт SandboxRoster.CreateBattle.
            campaignUnitIds.Clear();
            for (int i = 0; i < fighters.Count; i++)
                campaignUnitIds.Add("player:" + fighters[i].Id + ":" + (i + 1));

            battle = SandboxRoster.CreateBattle(fighters, BuildCampaignEnemies(), campaignBattle.Seed,
                campaignBattle.PlayerFirstRoundInitiativeBonus, BattlefieldFrame.DisabledCells(ActiveBattlefield()));

            // ПР-10: пал герой — бой проигран.
            for (int i = 0; i < campaignParticipants.Count; i++)
            {
                if (campaignParticipants[i].IsHero)
                    battle.LeaderUnitId = campaignUnitIds[i];
            }
            campaignRetreated = false;

            // ПР-06А: текущие HP человека из кампании.
            for (int i = 0; i < campaignParticipants.Count; i++)
            {
                int hitPoints = campaignParticipants[i].CurrentHitPoints;
                if (hitPoints <= 0)
                    continue;
                string unitId = campaignUnitIds[i];
                SandboxUnitState unit = battle.Units.FirstOrDefault(candidate => candidate.Id == unitId);
                unit?.SetStartingHitPoints(hitPoints);
            }

            battleLog.Clear();
            battleLog.Add("Бой начался. Отряд встречает засаду.");
            // 12Е-6: что особенности отряда дали ещё до первого удара.
            if (campaignBattle.Notes != null)
                battleLog.AddRange(campaignBattle.Notes);
            selectedTargetId = null;
            BuildBattleScreen();
            RefreshBattleScreen();
            return true;
        }

        // ПР-10: враги из запроса (шаблоны UnitDatabase × количество); без
        // списка — стандартная засада. Не больше SandboxRoster.MaxEnemies существ.
        private List<SandboxUnitDefinition> BuildCampaignEnemies()
        {
            List<SandboxUnitDefinition> enemies = new List<SandboxUnitDefinition>();
            campaignEnemyLevels.Clear();
            if (campaignBattle.Enemies != null)
            {
                foreach (CampaignBattleEnemy enemy in campaignBattle.Enemies)
                {
                    if (enemy == null || string.IsNullOrWhiteSpace(enemy.UnitTypeId) ||
                        !unitContent.CreaturesById.TryGetValue(enemy.UnitTypeId, out SandboxUnitDefinition definition))
                    {
                        Debug.LogWarning("Бой кампании: нет существа '" + (enemy != null ? enemy.UnitTypeId : "?") + "'.");
                        continue;
                    }
                    int level = Mathf.Clamp(enemy.Level, 1, ProgressionProfile.LevelCount);
                    SandboxUnitDefinition leveled = ApplyEnemyLevel(definition, level);
                    for (int i = 0; i < Mathf.Max(1, enemy.Count) && enemies.Count < SandboxRoster.MaxEnemies; i++)
                    {
                        enemies.Add(leveled);
                        campaignEnemyLevels.Add(level);
                    }
                }
            }
            // Запрос без известных существ — ошибка данных; бой всё же
            // начинается против первого существа базы, а не падает.
            if (enemies.Count == 0)
            {
                SandboxUnitDefinition fallback = unitContent.CreaturesById.Values.FirstOrDefault();
                if (fallback != null)
                {
                    Debug.LogWarning("Бой кампании без известных противников: подставлено «" + fallback.RoleLabel + "».");
                    enemies.Add(fallback);
                }
                campaignEnemyLevels.Clear();
            }
            return enemies;
        }

        // Уровень противника: накопленные прибавки из карты развития его типа
        // («База развития»). На 1-м уровне по умолчанию прибавок нет.
        private static SandboxUnitDefinition ApplyEnemyLevel(SandboxUnitDefinition definition, int level)
        {
            StatModifier bonus = ProgressionRules.Current.GetProfile(definition.Id).CumulativeBonus(level);
            if (bonus.IsZero)
                return definition;
            return new SandboxUnitDefinition(
                definition.Id,
                definition.RoleLabel,
                definition.Role,
                Mathf.Max(1, definition.MaxHitPoints + bonus.MaxHitPoints),
                Mathf.Max(0, definition.Attack + bonus.Attack),
                Mathf.Max(0, definition.Defense + bonus.Defense),
                Mathf.Max(1, definition.Damage + bonus.Damage),
                Mathf.Max(1, definition.Movement + bonus.Movement),
                Mathf.Max(0, definition.Initiative + bonus.Initiative),
                Mathf.Max(1, definition.AttackRange + bonus.AttackRange),
                definition.TagIds);
        }

        private readonly List<int> campaignEnemyLevels = new List<int>();

        private bool campaignRetreated;

        // ПР-10: отход — только в написанных боях, где он разрешён.
        private void RetreatFromCampaignBattle()
        {
            if (campaignBattle == null || battle == null || !campaignBattle.AllowRetreat ||
                battle.Phase != SandboxBattlePhase.InProgress)
                return;
            campaignRetreated = true;
            CampaignSession.CompleteBattle(BuildCampaignBattleResult());
            campaignBattle = null;
            UnityEngine.SceneManagement.SceneManager.LoadScene(CampaignSceneName);
        }

        private CampaignBattleResult BuildCampaignBattleResult()
        {
            CampaignBattleResult result = new CampaignBattleResult
            {
                BattleId = campaignBattle.BattleId,
                Outcome = campaignRetreated
                    ? CampaignBattleOutcome.Retreat
                    : battle.Phase == SandboxBattlePhase.PlayerVictory
                        ? CampaignBattleOutcome.Victory
                        : CampaignBattleOutcome.Defeat,
                Rounds = battle.Round
            };

            for (int i = 0; i < campaignParticipants.Count; i++)
            {
                string unitId = campaignUnitIds[i];
                SandboxUnitState unit = battle.Units.FirstOrDefault(candidate => candidate.Id == unitId);
                if (unit != null && unit.IsDefeated)
                    result.FallenPersonIds.Add(campaignParticipants[i].PersonId);
                else if (unit != null)
                {
                    result.Survivors.Add(new CampaignBattleSurvivor
                    {
                        PersonId = campaignParticipants[i].PersonId,
                        HitPoints = unit.HitPoints
                    });
                    // 12Е-6, «Ещё на ногах»: устоял на 1 здоровья — после боя
                    // обязательная тяжёлая рана.
                    if (unit.StillStandingUsed)
                        result.ForcedHeavyWoundIds.Add(campaignParticipants[i].PersonId);
                }

                // Канон v1.48 §27.3: реальный вклад участника в банк опыта.
                if (unit != null)
                {
                    result.Contributions.Add(new CampaignBattleContribution
                    {
                        PersonId = campaignParticipants[i].PersonId,
                        DamageDealt = unit.DamageDealt,
                        DamagePrevented = unit.DamagePrevented,
                        UsedRangedAttack = unit.UsedRangedAttack,
                        UsedMeleeAttack = unit.UsedMeleeAttack,
                        // 12Е-8: для следов развития.
                        TimesAttacked = unit.TimesAttacked,
                        Retaliations = unit.RetaliationsMade,
                        ShotFromPlace = unit.ShotFromPlace
                    });
                }
            }

            // Противники боя — их «цена» составляет банк опыта (§27.2).
            int enemyIndex = 0;
            foreach (SandboxUnitState enemy in battle.Units.Where(candidate => candidate.Team == SandboxTeam.Enemy))
            {
                int level = enemyIndex < campaignEnemyLevels.Count ? campaignEnemyLevels[enemyIndex] : 1;
                enemyIndex++;
                result.Enemies.Add(new CampaignBattleEnemyRecord
                {
                    UnitTypeId = enemy.TypeId,
                    Level = level,
                    MaxHitPoints = enemy.MaxHitPoints,
                    Attack = enemy.Attack,
                    Defense = enemy.Defense,
                    Damage = enemy.Damage,
                    // ПР-12Ж: дальность — по основе, без бонуса холма.
                    Movement = enemy.Movement,
                    Initiative = enemy.Initiative,
                    AttackRange = enemy.Definition.AttackRange,
                    TagIds = enemy.Definition.TagIds.ToList(),
                    Defeated = enemy.IsDefeated
                });
            }

            return result;
        }

        private void ReturnToCampaign()
        {
            if (campaignBattle == null || battle == null || battle.Phase == SandboxBattlePhase.InProgress)
                return;

            CampaignSession.CompleteBattle(BuildCampaignBattleResult());
            campaignBattle = null;
            UnityEngine.SceneManagement.SceneManager.LoadScene(CampaignSceneName);
        }

        private const string CampaignSceneName = "Prototype_Main";

        // ------------------------------------------------------------------
        // Полигон: две колонки. Слева — свой отряд (1–6), справа — противник
        // (1–MaxEnemies). В обеих доступны все типы Базы существ, один тип
        // можно взять несколько раз. Автовыбора нет: колонки начинаются пустыми.
        // ------------------------------------------------------------------

        public const int MaxPlayerUnits = 6;

        private readonly Dictionary<string, int> playerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> enemyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Color PlayerSideAccent = new Color(0.88f, 0.71f, 0.38f, 1f);
        private static readonly Color EnemySideAccent = new Color(0.88f, 0.40f, 0.32f, 1f);
        // Три карточки в ряд: 3 × (150 + 10) + поля и полоса прокрутки.
        private const float SideColumnWidth = 524f;

        private readonly Dictionary<string, SandboxPickCard> playerCards = new Dictionary<string, SandboxPickCard>(StringComparer.Ordinal);
        private readonly Dictionary<string, SandboxPickCard> enemyCards = new Dictionary<string, SandboxPickCard>(StringComparer.Ordinal);
        private Label playerCounterLabel;
        private Label enemyCounterLabel;
        private Label playerSummaryLabel;
        private Label enemySummaryLabel;
        private Button playerClearButton;
        private Button enemyClearButton;
        private Label startHintLabel;

        private void BuildSetupScreen()
        {
            ResetCombatFlow();
            selectedTargetId = null;
            battle = null;
            root.Clear();
            fighterDetailsView = new SandboxFighterDetailsView(root);
            playerCards.Clear();
            enemyCards.Clear();

            VisualElement screen = new VisualElement { name = "sandbox-setup-screen" };
            screen.style.flexGrow = 1f;
            screen.style.flexDirection = FlexDirection.Column;
            screen.style.paddingLeft = 28f;
            screen.style.paddingRight = 28f;
            screen.style.paddingTop = 18f;
            screen.style.paddingBottom = 24f;
            root.Add(screen);

            // Шапка: только название полигона по центру.
            VisualElement header = new VisualElement();
            header.style.alignItems = Align.Center;
            header.style.flexShrink = 0f;
            Label title = CreateLabel("ИЗОЛИРОВАННЫЙ БОЕВОЙ ПОЛИГОН", 16, new Color(0.93f, 0.80f, 0.55f, 1f));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 4f;
            header.Add(title);
            VisualElement rule = new VisualElement();
            rule.style.width = 420f;
            rule.style.height = 1f;
            rule.style.marginTop = 10f;
            rule.style.backgroundColor = new Color(0.88f, 0.71f, 0.38f, 0.35f);
            header.Add(rule);
            screen.Add(header);

            // Две колонки у краёв, между ними — кнопка боя.
            VisualElement body = new VisualElement();
            body.style.flexGrow = 1f;
            body.style.flexShrink = 1f;
            body.style.minHeight = 0f;
            body.style.flexDirection = FlexDirection.Row;
            body.style.marginTop = 18f;
            body.Add(BuildSideColumn(false));
            body.Add(BuildSetupCenter());
            body.Add(BuildSideColumn(true));
            screen.Add(body);

            RefreshSetupColumns();
        }

        private Dictionary<string, int> CountsFor(bool enemySide) => enemySide ? enemyCounts : playerCounts;
        private int SideLimit(bool enemySide) => enemySide ? SandboxRoster.MaxEnemies : MaxPlayerUnits;
        private int SideTotal(bool enemySide) => CountsFor(enemySide).Values.Sum();

        private void ChangeCount(bool enemySide, string typeId, int delta)
        {
            Dictionary<string, int> counts = CountsFor(enemySide);
            counts.TryGetValue(typeId, out int count);
            if (delta > 0 && SideTotal(enemySide) >= SideLimit(enemySide))
                return;
            int next = Mathf.Max(0, count + delta);
            if (next == 0)
                counts.Remove(typeId);
            else
                counts[typeId] = next;
            RefreshSetupColumns();
        }

        private void ClearSide(bool enemySide)
        {
            CountsFor(enemySide).Clear();
            RefreshSetupColumns();
        }

        // Состав стороны в порядке Базы существ: тип × число.
        private List<SandboxUnitDefinition> PickedUnits(bool enemySide)
        {
            Dictionary<string, int> counts = CountsFor(enemySide);
            List<SandboxUnitDefinition> units = new List<SandboxUnitDefinition>();
            foreach (SandboxUnitOption option in unitContent.AllUnits)
            {
                counts.TryGetValue(option.Definition.Id, out int count);
                for (int i = 0; i < count; i++)
                    units.Add(option.Definition);
            }
            return units;
        }

        // Карточки обновляются на месте: колонка не перестраивается и не
        // теряет прокрутку при каждом щелчке.
        private void RefreshSetupColumns()
        {
            if (startBattleButton == null)
                return;
            RefreshSide(false);
            RefreshSide(true);
            int players = SideTotal(false);
            int enemies = SideTotal(true);
            bool ready = players > 0 && enemies > 0;
            startBattleButton.SetEnabled(ready);
            if (startHintLabel != null)
            {
                startHintLabel.text = ready
                    ? "Отряд: " + players + " · Противник: " + enemies
                    : players == 0 && enemies == 0
                        ? "Наберите отряд слева и противника справа"
                        : players == 0 ? "Добавьте хотя бы одного бойца в отряд" : "Добавьте хотя бы одного противника";
            }
        }

        private void RefreshSide(bool enemySide)
        {
            int total = SideTotal(enemySide);
            int limit = SideLimit(enemySide);
            Label counter = enemySide ? enemyCounterLabel : playerCounterLabel;
            Label summary = enemySide ? enemySummaryLabel : playerSummaryLabel;
            Button clear = enemySide ? enemyClearButton : playerClearButton;
            if (counter != null)
                counter.text = total + " / " + limit;
            clear?.SetEnabled(total > 0);
            if (summary != null)
            {
                summary.text = total == 0
                    ? "Пока никого — щёлкните по карточке, чтобы добавить."
                    : string.Join(", ", PickedUnits(enemySide).GroupBy(unit => unit.RoleLabel)
                        .Select(picked => picked.Count() > 1 ? picked.Key + " ×" + picked.Count() : picked.Key));
            }
            Dictionary<string, int> counts = CountsFor(enemySide);
            foreach (KeyValuePair<string, SandboxPickCard> pair in enemySide ? enemyCards : playerCards)
            {
                counts.TryGetValue(pair.Key, out int count);
                pair.Value.SetState(count, total < limit);
            }
        }

        private VisualElement BuildSideColumn(bool enemySide)
        {
            Color accent = enemySide ? EnemySideAccent : PlayerSideAccent;
            VisualElement column = new VisualElement { name = enemySide ? "sandbox-enemy-column" : "sandbox-player-column" };
            column.style.width = SideColumnWidth;
            column.style.maxWidth = Length.Percent(40f);
            column.style.flexShrink = 1f;
            column.style.minHeight = 0f;
            column.style.flexDirection = FlexDirection.Column;
            column.style.paddingLeft = 14f;
            column.style.paddingRight = 6f;
            column.style.paddingTop = 14f;
            column.style.paddingBottom = 8f;
            column.style.backgroundColor = new Color(0.06f, 0.07f, 0.08f, 0.92f);
            SetBorder(column, new Color(accent.r, accent.g, accent.b, 0.30f));
            SetRadius(column, 8f);

            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.flexShrink = 0f;
            header.style.paddingRight = 8f;
            Label title = CreateLabel(enemySide ? "ПРОТИВНИК" : "ВАШ ОТРЯД", 17, accent);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 2f;
            title.style.flexGrow = 1f;
            header.Add(title);
            Label counter = CreateLabel(string.Empty, 17, new Color(0.92f, 0.88f, 0.80f, 1f));
            counter.style.unityFontStyleAndWeight = FontStyle.Bold;
            counter.style.marginRight = 10f;
            header.Add(counter);
            Button clear = new Button(() => ClearSide(enemySide)) { text = "ОЧИСТИТЬ" };
            StyleSecondaryButton(clear);
            clear.style.height = 26f;
            clear.style.fontSize = 10f;
            header.Add(clear);
            column.Add(header);

            Label summary = CreateLabel(string.Empty, 11, new Color(0.70f, 0.68f, 0.62f, 1f));
            summary.style.whiteSpace = WhiteSpace.Normal;
            summary.style.minHeight = 30f;
            summary.style.marginTop = 6f;
            summary.style.paddingRight = 8f;
            summary.style.flexShrink = 0f;
            column.Add(summary);

            VisualElement rule = new VisualElement();
            rule.style.height = 1f;
            rule.style.marginTop = 6f;
            rule.style.marginRight = 8f;
            rule.style.marginBottom = 8f;
            rule.style.flexShrink = 0f;
            rule.style.backgroundColor = new Color(accent.r, accent.g, accent.b, 0.25f);
            column.Add(rule);

            // Своя прокрутка у каждой колонки: колесом по 90 пикселей, без «резинки».
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = enemySide ? "sandbox-enemy-scroll" : "sandbox-player-scroll",
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto,
                mouseWheelScrollSize = 90f,
                touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped
            };
            scroll.style.flexGrow = 1f;
            scroll.style.flexShrink = 1f;
            scroll.style.minHeight = 0f;
            StyleDarkScroller(scroll.verticalScroller, accent);
            column.Add(scroll);

            Dictionary<string, SandboxPickCard> cards = enemySide ? enemyCards : playerCards;
            VisualElement grid = null;
            string currentGroup = null;
            foreach (SandboxUnitOption option in unitContent.AllUnits)
            {
                if (option.Group != currentGroup || grid == null)
                {
                    currentGroup = option.Group;
                    Label groupLabel = CreateLabel(currentGroup.ToUpperInvariant(), 11, new Color(0.62f, 0.57f, 0.47f, 1f));
                    groupLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    groupLabel.style.letterSpacing = 2f;
                    groupLabel.style.marginTop = cards.Count == 0 ? 0f : 8f;
                    groupLabel.style.marginBottom = 8f;
                    scroll.Add(groupLabel);
                    grid = new VisualElement();
                    grid.style.flexDirection = FlexDirection.Row;
                    grid.style.flexWrap = Wrap.Wrap;
                    scroll.Add(grid);
                }

                SandboxUnitDefinition definition = option.Definition;
                SandboxUnitVisual visual = unitContent.GetVisual(definition.Id);
                SandboxPickCard card = new SandboxPickCard(
                    definition,
                    visual,
                    accent,
                    () => ChangeCount(enemySide, definition.Id, 1),
                    () => ChangeCount(enemySide, definition.Id, -1),
                    () => fighterDetailsView.Open(definition, portrait: visual.Portrait));
                cards[definition.Id] = card;
                grid.Add(card.Root);
            }

            if (enemySide)
            {
                enemyCounterLabel = counter;
                enemySummaryLabel = summary;
                enemyClearButton = clear;
            }
            else
            {
                playerCounterLabel = counter;
                playerSummaryLabel = summary;
                playerClearButton = clear;
            }
            return column;
        }

        // Тонкая тёмная полоса прокрутки без стрелок, ползунок — цвета стороны.
        private static void StyleDarkScroller(Scroller scroller, Color accent)
        {
            if (scroller == null)
                return;
            scroller.style.width = 8f;
            scroller.style.minWidth = 8f;
            scroller.style.marginLeft = 4f;
            scroller.style.borderLeftWidth = 0f;
            scroller.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            scroller.lowButton.style.display = DisplayStyle.None;
            scroller.highButton.style.display = DisplayStyle.None;
            scroller.slider.style.marginTop = 0f;
            scroller.slider.style.marginBottom = 0f;
            VisualElement tracker = scroller.slider.Q("unity-tracker");
            if (tracker != null)
            {
                tracker.style.backgroundColor = new Color(1f, 1f, 1f, 0.05f);
                SetBorder(tracker, new Color(0f, 0f, 0f, 0f), 0f);
                SetRadius(tracker, 4f);
            }
            VisualElement dragger = scroller.slider.Q("unity-dragger");
            if (dragger != null)
            {
                dragger.style.backgroundColor = new Color(accent.r, accent.g, accent.b, 0.55f);
                SetBorder(dragger, new Color(0f, 0f, 0f, 0f), 0f);
                SetRadius(dragger, 4f);
                dragger.style.width = 8f;
                dragger.style.left = 0f;
            }
        }

        private VisualElement BuildSetupCenter()
        {
            VisualElement center = new VisualElement { name = "sandbox-setup-center" };
            center.style.flexGrow = 1f;
            center.style.minWidth = 220f;
            center.style.alignItems = Align.Center;
            center.style.justifyContent = Justify.Center;
            center.style.paddingLeft = 20f;
            center.style.paddingRight = 20f;

            startBattleButton = new Button(StartBattle) { text = "НАЧАТЬ БОЙ", name = "sandbox-start-battle" };
            StylePrimaryButton(startBattleButton);
            startBattleButton.style.width = 260f;
            startBattleButton.style.height = 64f;
            startBattleButton.style.fontSize = 18f;
            startBattleButton.style.letterSpacing = 3f;
            center.Add(startBattleButton);

            startHintLabel = CreateLabel(string.Empty, 12, new Color(0.70f, 0.68f, 0.62f, 1f));
            startHintLabel.style.marginTop = 12f;
            startHintLabel.style.maxWidth = 260f;
            startHintLabel.style.whiteSpace = WhiteSpace.Normal;
            startHintLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            center.Add(startHintLabel);

            Label help = CreateLabel("ЛКМ — добавить · ПКМ — карточка · «−» — убрать", 10, new Color(0.48f, 0.48f, 0.46f, 1f));
            help.style.marginTop = 6f;
            help.style.unityTextAlign = TextAnchor.MiddleCenter;
            center.Add(help);
            return center;
        }

        private BattlefieldDefinitionData ActiveBattlefield()
        {
            return battlefieldDatabase != null ? battlefieldDatabase.GetSandboxBattlefield() : null;
        }

        // Поле из Базы полей боя: фон и основной вид гексов в кадре 16:9,
        // поверх — интерактивное поле. Оба считают раскладку одинаково.
        private BattlefieldView CreateBattlefieldSurface(HexBoardElement boardElement)
        {
            BattlefieldDefinitionData field = ActiveBattlefield();
            BattlefieldHexStyle style = battlefieldDatabase != null ? battlefieldDatabase.GetHexStyle(field) : null;

            BattlefieldView surface = new BattlefieldView(true) { name = "battlefield-surface" };
            surface.style.flexGrow = 1f;
            surface.style.flexShrink = 1f;
            surface.style.minWidth = 620f;
            surface.style.minHeight = 520f;
            surface.style.position = Position.Relative;
            surface.style.backgroundColor = new Color(0.035f, 0.043f, 0.050f, 1f);
            surface.Show(field, style);
            // В редакторе правки окна базы видны в идущем бою.
            if (Application.isEditor)
            {
                surface.EnableLiveRefresh(500);
                surface.LayoutChanged += boardElement.MarkDirtyRepaint;
            }

            boardElement.style.position = Position.Absolute;
            boardElement.style.left = 0f;
            boardElement.style.right = 0f;
            boardElement.style.top = 0f;
            boardElement.style.bottom = 0f;
            boardElement.style.minWidth = 0f;
            boardElement.style.minHeight = 0f;
            boardElement.style.marginRight = 0f;
            boardElement.style.backgroundColor = Color.clear;
            boardElement.style.borderLeftWidth = 0f;
            boardElement.style.borderRightWidth = 0f;
            boardElement.style.borderTopWidth = 0f;
            boardElement.style.borderBottomWidth = 0f;
            boardElement.SetBattlefield(field, style, true);
            surface.Add(boardElement);
            return surface;
        }

        private void StartBattle()
        {
            List<SandboxUnitDefinition> fighters = PickedUnits(false);
            List<SandboxUnitDefinition> enemies = PickedUnits(true);
            if (fighters.Count == 0 || enemies.Count == 0)
                return;

            // «Повторить бой» во время незавершённого показа прежнего боя не
            // должен унаследовать его флаги.
            ResetCombatFlow();
            battle = SandboxRoster.CreateBattle(fighters, enemies,
                disabledCells: BattlefieldFrame.DisabledCells(ActiveBattlefield()));
            battleLog.Clear();
            battleLog.Add("Бой начался. Противник: " + string.Join(", ", enemies.GroupBy(enemy => enemy.RoleLabel)
                .Select(group => group.Count() > 1 ? group.Key + " ×" + group.Count() : group.Key)) + ".");
            selectedTargetId = null;
            BuildBattleScreen();
            RefreshBattleScreen();
        }

        private void BuildBattleScreen()
        {
            root.Clear();
            fighterDetailsView = new SandboxFighterDetailsView(root);

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

            returnToSetupButton = new Button(BuildSetupScreen) { text = "НОВЫЙ СОСТАВ" };
            StyleSecondaryButton(returnToSetupButton);
            returnToSetupButton.style.width = 170f;
            header.Add(returnToSetupButton);
            screen.Add(header);

            initiativeRow = new VisualElement();
            initiativeRow.style.height = 98f;
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
            board.SetUnitVisuals(unitContent.Visuals, unitContent.AnimationDatabase);
            board.HexClicked += OnBoardHexClicked;
            board.UnitDetailsRequested += OnBoardUnitDetailsRequested;
            board.AttackRequested += OnBoardAttackRequested;
            body.Add(CreateBattlefieldSurface(board));

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
                "Синие гексы расходуют запас движения. Наведение показывает маршрут. Меч выбирает грань удара, а выстрел не зависит от стороны гекса.",
                11,
                new Color(0.58f, 0.65f, 0.67f, 1f));
            instructionLabel.style.marginTop = 13f;
            instructionLabel.style.whiteSpace = WhiteSpace.Normal;
            sidebar.Add(instructionLabel);

            targetLabel = CreateLabel("Цель не выбрана", 12, new Color(0.82f, 0.66f, 0.62f, 1f));
            targetLabel.style.marginTop = 14f;
            targetLabel.style.whiteSpace = WhiteSpace.Normal;
            sidebar.Add(targetLabel);

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

            if (campaignBattle != null)
            {
                // Бой кампании нельзя переиграть или пересобрать: итог
                // возвращается в ту же кампанию.
                returnToSetupButton.style.display = DisplayStyle.None;
                Button returnButton = new Button(ReturnToCampaign) { text = "ВЕРНУТЬСЯ В КАМПАНИЮ" };
                returnButton.name = "campaign-return-button";
                StylePrimaryButton(returnButton);
                returnButton.style.marginTop = 9f;
                resultBanner.Add(returnButton);

                if (campaignBattle.AllowRetreat)
                {
                    Button retreatButton = new Button(RetreatFromCampaignBattle) { text = "ОТСТУПИТЬ" };
                    retreatButton.name = "campaign-retreat-button";
                    StylePrimaryButton(retreatButton);
                    retreatButton.style.marginTop = 12f;
                    sidebar.Add(retreatButton);
                }
            }
            else
            {
                Button repeatButton = new Button(StartBattle) { text = "ПОВТОРИТЬ БОЙ" };
                StylePrimaryButton(repeatButton);
                repeatButton.style.marginTop = 9f;
                resultBanner.Add(repeatButton);
            }
            sidebar.Add(resultBanner);

            body.Add(sidebar);
            screen.Add(body);
            screen.Add(BuildSpeedControl());
        }

        // ПР-12З: общая скорость показа боя. Добавляется последним ребёнком
        // экрана: оформление поля опирается на первые два.
        private static VisualElement BuildSpeedControl()
        {
            VisualElement panel = new VisualElement { name = "battle-speed-control" };
            panel.style.position = Position.Absolute;
            panel.style.right = 16f;
            panel.style.top = 104f;
            panel.style.width = 220f;
            panel.style.paddingLeft = 10f;
            panel.style.paddingRight = 10f;
            panel.style.paddingTop = 6f;
            panel.style.paddingBottom = 4f;
            panel.style.backgroundColor = new Color(0.045f, 0.052f, 0.055f, 0.78f);
            SetBorder(panel, new Color(0.52f, 0.47f, 0.35f, 0.42f));
            SetRadius(panel, 4f);

            Label title = CreateLabel(string.Empty, 11, new Color(0.92f, 0.84f, 0.68f, 1f));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            panel.Add(title);

            SliderInt slider = new SliderInt(0, BattlePresentationSpeed.Steps.Length - 1)
            {
                name = "battle-speed-slider",
                value = BattlePresentationSpeed.Index,
                tooltip = "Скорость ходьбы, ударов и анимаций в бою. На правила боя не влияет."
            };
            void Refresh(int index) => title.text = "СКОРОСТЬ БОЯ · " + BattlePresentationSpeed.Label(index);
            Refresh(slider.value);
            slider.RegisterValueChangedCallback(evt =>
            {
                BattlePresentationSpeed.Index = evt.newValue;
                Refresh(evt.newValue);
            });
            panel.Add(slider);
            return panel;
        }

        private void OnBoardHexClicked(HexCoord coord)
        {
            if (combatAnimationRunning)
                return;

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
                {
                    selectedTargetId = occupant.Id;
                }
                else
                {
                    selectedTargetId = null;
                    fighterDetailsView.Open(occupant.Definition, occupant);
                }
                RefreshBattleScreen();
                return;
            }

            selectedTargetId = null;
            BeginMoveAnimation(
                current.Id,
                coord,
                moved =>
                {
                    if (moved)
                        FinishActivationIfEmpty();
                });
        }

        private void OnBoardAttackRequested(string targetId, HexCoord? requestedPosition)
        {
            if (combatAnimationRunning || battle == null ||
                battle.Phase != SandboxBattlePhase.InProgress)
            {
                return;
            }

            SandboxUnitState current = battle.CurrentUnit;
            SandboxUnitState target = battle.GetUnit(targetId);
            if (current == null || target == null ||
                current.Team != SandboxTeam.Player || target.Team == current.Team)
            {
                return;
            }

            if (!TryBeginDirectAttack(current, target, requestedPosition))
            {
                selectedTargetId = target.Id;
                RefreshBattleScreen();
            }
        }

        private void OnBoardUnitDetailsRequested(string unitId, Vector2 panelPosition)
        {
            if (combatAnimationRunning || battle == null || fighterDetailsView == null || root == null)
                return;

            SandboxUnitState unit = battle.GetUnit(unitId);
            if (unit == null)
                return;

            Vector2 rootPosition = root.WorldToLocal(panelPosition);
            fighterDetailsView.Open(
                unit.Definition,
                unit,
                rootPosition,
                unitContent.GetVisual(unit.TypeId).Portrait);
        }

        private bool TryBeginDirectAttack(
            SandboxUnitState attacker,
            SandboxUnitState target,
            HexCoord? requestedPosition)
        {
            if (battle == null || attacker == null || target == null)
                return false;

            HexCoord attackPosition;
            int movementCost;
            if (attacker.AttackRange <= 1)
            {
                if (!requestedPosition.HasValue ||
                    !battle.TryGetMeleeAttackPosition(
                        attacker.Id,
                        target.Id,
                        requestedPosition.Value,
                        out movementCost))
                {
                    return false;
                }

                attackPosition = requestedPosition.Value;
            }
            else if (!battle.TryFindAttackPosition(
                         attacker.Id,
                         target.Id,
                         out attackPosition,
                         out movementCost))
            {
                return false;
            }

            selectedTargetId = target.Id;
            if (movementCost > 0 && attackPosition != attacker.Position)
            {
                string attackerId = attacker.Id;
                string targetId = target.Id;
                return BeginMoveAnimation(
                    attackerId,
                    attackPosition,
                    moved =>
                    {
                        if (moved)
                            BeginPreparedAttack(attackerId, targetId);
                    });
            }

            return BeginPreparedAttack(attacker.Id, target.Id);
        }

        private bool BeginPreparedAttack(string attackerId, string targetId)
        {
            if (battle == null || battle.CurrentUnit == null ||
                battle.CurrentUnit.Id != attackerId)
            {
                return false;
            }

            SandboxAttackPreview preview = battle.PreviewAttack(attackerId, targetId);
            if (!preview.IsValid)
                return false;

            selectedTargetId = targetId;
            BeginAttackAnimation(
                attackerId,
                targetId,
                preview.Damage,
                applied =>
                {
                    selectedTargetId = null;
                    if (applied)
                        FinishActivationIfEmpty();
                });
            return true;
        }

        private bool BeginMoveAnimation(
            string unitId,
            HexCoord destination,
            Action<bool> onComplete)
        {
            if (battle == null || board == null || combatAnimationRunning)
                return false;

            IReadOnlyList<HexCoord> path;
            int movementCost;
            if (!SandboxMovementPath.TryBuild(
                    battle,
                    unitId,
                    destination,
                    out path,
                    out movementCost) ||
                path.Count < 2 || movementCost <= 0)
            {
                return false;
            }

            combatAnimationRunning = true;
            RefreshBattleScreen();

            int generation = battleGeneration;
            bool started = board.PlayMoveAnimation(
                unitId,
                path,
                () =>
                {
                    // Поле прежнего боя ничего не меняет в новом.
                    if (generation != battleGeneration)
                        return;
                    bool moved = false;
                    string message = string.Empty;
                    if (battle != null)
                        moved = battle.TryMove(unitId, destination, out message);
                    if (moved)
                        AddBattleLog(message);

                    combatAnimationRunning = false;
                    onComplete?.Invoke(moved);
                    RefreshBattleScreen();
                });

            if (started)
                return true;

            string fallbackMessage;
            bool fallbackMoved = battle.TryMove(unitId, destination, out fallbackMessage);
            if (fallbackMoved)
                AddBattleLog(fallbackMessage);
            combatAnimationRunning = false;
            onComplete?.Invoke(fallbackMoved);
            RefreshBattleScreen();
            return true;
        }

        private void PerformGuard()
        {
            if (combatAnimationRunning)
                return;

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
            if (combatAnimationRunning || battle == null || battle.CurrentUnit == null ||
                battle.CurrentUnit.Team != SandboxTeam.Player)
            {
                return;
            }

            AddBattleLog(battle.CurrentUnit.DisplayLabel + " завершает активацию.");
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
            fighterDetailsView.Refresh(battle);
            roundLabel.text = "Раунд " + battle.Round + " · движение + одно боевое действие";
            RefreshInitiativeRow();

            SandboxUnitState current = battle.CurrentUnit;
            bool playerTurn = battle.Phase == SandboxBattlePhase.InProgress &&
                              current != null && current.Team == SandboxTeam.Player;
            bool playerInteractionAvailable = playerTurn && !combatAnimationRunning;

            if (current != null)
            {
                currentUnitLabel.text =
                    (current.Team == SandboxTeam.Player ? "ВАШ ХОД · " : "ХОД ВРАГА · ") +
                    current.DisplayLabel.ToUpper();
                currentStatsLabel.text =
                    current.Definition.RoleLabel + "\n" +
                    "HP " + current.HitPoints + "/" + current.MaxHitPoints +
                    "  ·  ОД " + current.ActionPoints + "/" + SandboxUnitState.ActionsPerActivation + "\n" +
                    "АТК " + current.Attack + "  ·  ЗАЩ " + current.Defense +
                    "  ·  УРОН " + current.Damage + "\n" +
                    "ДВИЖ " + current.RemainingMovement + "/" + current.Movement +
                    "  ·  ИНИЦ " + current.Initiative + "\n" +
                    "ОТВЕТНЫЙ УДАР: " + (current.CanRetaliate ? "ГОТОВ" : "ПОТРАЧЕН");
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
            }
            else
            {
                HexCoord attackPosition;
                int movementCost = 0;
                bool reachableForAttack = current != null && battle.TryFindAttackPosition(
                    current.Id,
                    target.Id,
                    out attackPosition,
                    out movementCost);
                SandboxAttackPreview reachablePreview = reachableForAttack && current != null
                    ? battle.PreviewReachableAttack(current.Id, target.Id)
                    : null;
                SandboxAttackPreview displayedPreview = preview != null && preview.IsValid
                    ? preview
                    : reachablePreview;
                string hoverHint = current != null && current.AttackRange > 1
                    ? "\nНаведите курсор на цель для выстрела. Сторона гекса не выбирается."
                    : "\nНаведите курсор на цель и выберите грань атаки.";
                targetLabel.text =
                    "Цель: " + target.DisplayLabel + " · HP " + target.HitPoints + "/" + target.MaxHitPoints +
                    (displayedPreview != null && displayedPreview.IsValid
                        ? "\nПрогноз: " + displayedPreview.Damage + " урона · останется " +
                          displayedPreview.TargetHitPointsAfter + " HP" +
                          (movementCost > 0 ? " · сближение " + movementCost : string.Empty)
                        : reachableForAttack
                            ? hoverHint
                            : "\nЦель находится вне доступной зоны атаки.") +
                    "\nОтветный удар: " + (target.CanRetaliate ? "готов" : "потрачен");
            }

            guardButton.SetEnabled(
                playerInteractionAvailable && current != null &&
                current.ActionPoints > 0 && !current.IsGuarding);
            endActivationButton.SetEnabled(playerInteractionAvailable);
            if (returnToSetupButton != null)
                returnToSetupButton.SetEnabled(!combatAnimationRunning);
            instructionLabel.text = combatAnimationRunning
                ? "Выполняется перемещение или атака… управление возобновится после завершения анимации."
                : playerTurn
                ? "Синие гексы — оставшееся движение. Меч выбирает грань удара; выжившая цель может ответить один раз за раунд. Выстрел ответ не вызывает. ПКМ открывает карточку."
                : battle.Phase == SandboxBattlePhase.InProgress
                    ? "Противник выполняет свою активацию…"
                    : campaignBattle != null
                        ? "Бой окончен. Вернитесь в кампанию — итог боя перейдёт в отряд."
                        : "Можно повторить бой тем же составом или вернуться к выбору бойцов.";

            logLabel.text = string.Join("\n", battleLog.Skip(Math.Max(0, battleLog.Count - 9)));
            RefreshResultBanner();
            QueueEnemyStepIfNeeded();
        }

        private void RefreshInitiativeRow()
        {
            initiativeRow.Clear();

            foreach (string unitId in battle.TurnOrderIds)
            {
                SandboxUnitState unit = battle.GetUnit(unitId);
                if (unit == null || unit.IsDefeated)
                    continue;

                bool active = battle.CurrentUnit != null && battle.CurrentUnit.Id == unit.Id;
                SandboxUnitState captured = unit;
                SandboxUnitVisual visual = unitContent.GetVisual(captured.TypeId);
                Button card = SandboxFighterCardFactory.CreateInitiativeCard(
                    captured,
                    visual.Portrait,
                    active,
                    () => fighterDetailsView.Open(
                        captured.Definition,
                        captured,
                        portrait: visual.Portrait));
                card.SetEnabled(!combatAnimationRunning);
                initiativeRow.Add(card);
            }
        }

        private void RefreshResultBanner()
        {
            // Итог показывается, когда доиграл показ последнего удара: иначе
            // «Повторить бой» можно нажать посреди анимации смерти.
            if (battle.Phase == SandboxBattlePhase.InProgress || combatAnimationRunning)
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
            if (enemyStepScheduled || combatAnimationRunning || battle == null ||
                battle.Phase != SandboxBattlePhase.InProgress ||
                battle.CurrentUnit == null || battle.CurrentUnit.Team != SandboxTeam.Enemy)
            {
                return;
            }

            enemyStepScheduled = true;
            int generation = battleGeneration;
            root.schedule.Execute(() =>
            {
                // Отложенный ход прежнего боя в новом не выполняется.
                if (generation != battleGeneration)
                    return;
                if (battle == null || battle.Phase != SandboxBattlePhase.InProgress ||
                    battle.CurrentUnit == null || battle.CurrentUnit.Team != SandboxTeam.Enemy)
                {
                    enemyStepScheduled = false;
                    return;
                }

                SandboxUnitState enemy = battle.CurrentUnit;
                SandboxUnitState target = SelectEnemyAttackTarget(enemy);
                if (target != null)
                {
                    BeginEnemyAttack(enemy, target);
                    return;
                }

                SandboxUnitState closest = battle.FindClosestOpponent(enemy);
                if (closest != null)
                {
                    HexCoord destination = battle.FindBestMoveToward(enemy, closest.Position);
                    if (destination != enemy.Position)
                    {
                        string enemyId = enemy.Id;
                        if (BeginMoveAnimation(
                                enemyId,
                                destination,
                                _ => ContinueEnemyAfterMovement(enemyId)))
                        {
                            return;
                        }
                    }
                }

                CompleteEnemyActivation(enemy.Id);
                RefreshBattleScreen();
            }).ExecuteLater((long)(420f / BattlePresentationSpeed.Multiplier));
        }

        private void ContinueEnemyAfterMovement(string enemyId)
        {
            if (battle == null || battle.Phase != SandboxBattlePhase.InProgress ||
                battle.CurrentUnit == null || battle.CurrentUnit.Id != enemyId)
            {
                CompleteEnemyActivation(enemyId);
                return;
            }

            SandboxUnitState enemy = battle.CurrentUnit;
            SandboxUnitState target = SelectEnemyAttackTarget(enemy);
            if (target != null)
            {
                BeginEnemyAttack(enemy, target);
                return;
            }

            CompleteEnemyActivation(enemyId);
        }

        private void BeginEnemyAttack(SandboxUnitState enemy, SandboxUnitState target)
        {
            if (enemy == null || target == null)
            {
                if (enemy != null)
                    CompleteEnemyActivation(enemy.Id);
                return;
            }

            SandboxAttackPreview preview = battle.PreviewAttack(enemy.Id, target.Id);
            if (!preview.IsValid)
            {
                CompleteEnemyActivation(enemy.Id);
                RefreshBattleScreen();
                return;
            }

            BeginAttackAnimation(
                enemy.Id,
                target.Id,
                preview.Damage,
                _ => CompleteEnemyActivation(enemy.Id));
        }

        private void BeginAttackAnimation(
            string attackerId,
            string targetId,
            int damage,
            Action<bool> onComplete)
        {
            if (battle == null || board == null || combatAnimationRunning)
                return;

            combatAnimationRunning = true;
            RefreshBattleScreen();

            // ПР-12З: сначала исход — модель считает удар и ответный удар один
            // раз, — затем поле показывает записи ударов по порядку. Маркеры
            // анимации ничего не пересчитывают; без анимации исход тот же.
            int firstRecord = battle.HitRecords.Count;
            string message;
            bool attackApplied = battle.TryAttack(attackerId, targetId, out message);
            if (attackApplied)
            {
                AddBattleLog(message);
                string retaliationMessage;
                if (battle.HasPendingRetaliation && battle.TryResolvePendingRetaliation(out retaliationMessage))
                    AddBattleLog(retaliationMessage);
            }

            List<SandboxHitRecord> records = battle.GetHitRecordsSince(firstRecord);
            int generation = battleGeneration;
            if (attackApplied && board.PlayHitSequence(records, () =>
                {
                    if (generation == battleGeneration)
                        FinishAttackSequence(true, onComplete);
                }))
            {
                return;
            }

            FinishAttackSequence(attackApplied, onComplete);
        }

        private void FinishAttackSequence(bool attackApplied, Action<bool> onComplete)
        {
            combatAnimationRunning = false;
            onComplete?.Invoke(attackApplied);
            RefreshBattleScreen();
        }

        private SandboxUnitState SelectEnemyAttackTarget(SandboxUnitState attacker)
        {
            if (battle == null || attacker == null)
                return null;

            return battle.Units
                .Where(unit => !unit.IsDefeated && unit.Team != attacker.Team)
                .Select(unit => new
                {
                    Unit = unit,
                    Preview = battle.PreviewAttack(attacker.Id, unit.Id)
                })
                .Where(candidate => candidate.Preview.IsValid)
                .OrderBy(candidate => candidate.Unit.HitPoints)
                .ThenByDescending(candidate => candidate.Preview.Damage)
                .ThenBy(candidate => candidate.Unit.Id, StringComparer.Ordinal)
                .Select(candidate => candidate.Unit)
                .FirstOrDefault();
        }

        private void CompleteEnemyActivation(string enemyId)
        {
            if (battle != null && battle.Phase == SandboxBattlePhase.InProgress &&
                battle.CurrentUnit != null && battle.CurrentUnit.Id == enemyId)
            {
                SandboxUnitState enemy = battle.CurrentUnit;
                if (enemy.ActionPoints > 0 && !enemy.IsGuarding)
                {
                    string guardMessage;
                    if (battle.TryGuard(enemy.Id, out guardMessage))
                        AddBattleLog(guardMessage);
                }

                if (battle.Phase == SandboxBattlePhase.InProgress &&
                    battle.CurrentUnit != null && battle.CurrentUnit.Id == enemyId)
                {
                    battle.EndActivation();
                }
            }

            enemyStepScheduled = false;
            selectedTargetId = null;
        }

        private void AddBattleLog(string entry)
        {
            if (!string.IsNullOrWhiteSpace(entry))
                battleLog.Add("• " + entry);
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
