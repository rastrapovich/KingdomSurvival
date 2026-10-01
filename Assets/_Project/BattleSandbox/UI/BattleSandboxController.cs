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
        private readonly List<string> battleLog = new List<string>();

        private SandboxUnitContent unitContent;
        private VisualElement root;
        private Button startBattleButton;
        private SandboxFighterDetailsView fighterDetailsView;

        private SandboxBattle battle;
        private HexBoardElement board;
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
                campaignBattle.PlayerFirstRoundInitiativeBonus);

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
        private VisualElement playerColumn;
        private VisualElement enemyColumn;

        private void BuildSetupScreen()
        {
            ResetCombatFlow();
            selectedTargetId = null;
            battle = null;
            root.Clear();
            fighterDetailsView = new SandboxFighterDetailsView(root);

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
                "Слева — ваш отряд (1–" + MaxPlayerUnits + "), справа — противник (1–" + SandboxRoster.MaxEnemies + "). " +
                "В обеих колонках доступны все бойцы и существа базы, один тип можно взять несколько раз. " +
                "Щелчок по имени открывает карточку. Полигон не изменяет состояние основной игры.",
                13,
                new Color(0.72f, 0.72f, 0.69f, 1f));
            description.style.marginTop = 8f;
            description.style.marginBottom = 18f;
            description.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(description);

            VisualElement columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.alignItems = Align.FlexStart;
            playerColumn = CreatePanel();
            playerColumn.style.flexGrow = 1f;
            playerColumn.style.flexBasis = 0f;
            playerColumn.style.marginRight = 7f;
            enemyColumn = CreatePanel();
            enemyColumn.style.flexGrow = 1f;
            enemyColumn.style.flexBasis = 0f;
            enemyColumn.style.marginLeft = 7f;
            columns.Add(playerColumn);
            columns.Add(enemyColumn);
            scroll.Add(columns);

            startBattleButton = new Button(StartBattle) { text = "НАЧАТЬ БОЙ" };
            StylePrimaryButton(startBattleButton);
            startBattleButton.style.width = 320f;
            startBattleButton.style.height = 52f;
            startBattleButton.style.marginTop = 20f;
            startBattleButton.style.alignSelf = Align.Center;
            scroll.Add(startBattleButton);

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

        private void RefreshSetupColumns()
        {
            if (playerColumn == null || enemyColumn == null)
                return;
            BuildSideColumn(playerColumn, false);
            BuildSideColumn(enemyColumn, true);
            startBattleButton.SetEnabled(SideTotal(false) > 0 && SideTotal(true) > 0);
        }

        private void BuildSideColumn(VisualElement column, bool enemySide)
        {
            column.Clear();
            int total = SideTotal(enemySide);
            int limit = SideLimit(enemySide);

            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            Label title = CreateSectionTitle((enemySide ? "ПРОТИВНИК" : "ВАШ ОТРЯД") + " · " + total + " / " + limit);
            title.style.flexGrow = 1f;
            header.Add(title);
            Button clear = new Button(() => ClearSide(enemySide)) { text = "ОЧИСТИТЬ" };
            StyleSecondaryButton(clear);
            clear.style.height = 24f;
            clear.SetEnabled(total > 0);
            header.Add(clear);
            column.Add(header);

            Label hint = CreateLabel(
                total == 0
                    ? (enemySide ? "Добавьте хотя бы одного противника." : "Добавьте хотя бы одного бойца.")
                    : string.Join(", ", PickedUnits(enemySide).GroupBy(unit => unit.RoleLabel)
                        .Select(group => group.Count() > 1 ? group.Key + " ×" + group.Count() : group.Key)),
                11,
                total == 0 ? new Color(0.90f, 0.62f, 0.45f, 1f) : new Color(0.78f, 0.74f, 0.66f, 1f));
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginTop = 4f;
            hint.style.marginBottom = 6f;
            column.Add(hint);

            string currentGroup = null;
            foreach (SandboxUnitOption option in unitContent.AllUnits)
            {
                if (option.Group != currentGroup)
                {
                    currentGroup = option.Group;
                    Label groupLabel = CreateLabel(currentGroup.ToUpperInvariant(), 10, new Color(0.62f, 0.57f, 0.47f, 1f));
                    groupLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    groupLabel.style.marginTop = 8f;
                    groupLabel.style.marginBottom = 2f;
                    column.Add(groupLabel);
                }
                column.Add(BuildUnitRow(option.Definition, enemySide, total < limit));
            }
        }

        private VisualElement BuildUnitRow(SandboxUnitDefinition definition, bool enemySide, bool canAdd)
        {
            CountsFor(enemySide).TryGetValue(definition.Id, out int count);
            SandboxUnitVisual visual = unitContent.GetVisual(definition.Id);

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 30f;
            row.style.marginBottom = 2f;
            row.style.paddingLeft = 4f;
            row.style.paddingRight = 4f;
            row.style.backgroundColor = count > 0
                ? new Color(0.20f, 0.17f, 0.10f, 0.9f)
                : new Color(0f, 0f, 0f, 0f);
            SetRadius(row, 3f);

            Image portrait = new Image
            {
                sprite = visual.Portrait != null ? visual.Portrait : visual.BattlefieldSprite,
                scaleMode = ScaleMode.ScaleAndCrop,
                pickingMode = PickingMode.Ignore
            };
            portrait.style.width = 22f;
            portrait.style.height = 28f;
            portrait.style.marginRight = 6f;
            portrait.style.flexShrink = 0f;
            row.Add(portrait);

            Button name = new Button(() => fighterDetailsView.Open(definition, portrait: visual.Portrait))
            {
                text = definition.RoleLabel + "   " + definition.MaxHitPoints + "/" + definition.Attack + "/" +
                       definition.Defense + "/" + definition.Damage,
                tooltip = "Карточка. HP / Атака / Защита / Урон · Ход " + definition.Movement +
                          " · Инициатива " + definition.Initiative + " · Дальность " + definition.AttackRange
            };
            name.style.flexGrow = 1f;
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            name.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            SetBorder(name, new Color(0f, 0f, 0f, 0f), 0f);
            name.style.color = count > 0 ? new Color(0.95f, 0.84f, 0.60f, 1f) : new Color(0.78f, 0.77f, 0.72f, 1f);
            name.style.fontSize = 12f;
            row.Add(name);

            Button minus = new Button(() => ChangeCount(enemySide, definition.Id, -1)) { text = "−" };
            Label value = CreateLabel(count.ToString(), 12, new Color(0.95f, 0.84f, 0.60f, 1f));
            value.style.width = 20f;
            value.style.unityTextAlign = TextAnchor.MiddleCenter;
            Button plus = new Button(() => ChangeCount(enemySide, definition.Id, 1)) { text = "+" };
            foreach (Button button in new[] { minus, plus })
            {
                StyleSecondaryButton(button);
                button.style.height = 22f;
                button.style.width = 26f;
            }
            minus.SetEnabled(count > 0);
            plus.SetEnabled(canAdd);
            row.Add(minus);
            row.Add(value);
            row.Add(plus);
            return row;
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
            battle = SandboxRoster.CreateBattle(fighters, enemies);
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
            board.style.marginRight = 14f;
            board.HexClicked += OnBoardHexClicked;
            board.UnitDetailsRequested += OnBoardUnitDetailsRequested;
            board.AttackRequested += OnBoardAttackRequested;
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
