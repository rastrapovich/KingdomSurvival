using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private bool fighterDetailsUiInitialized;
    private VisualElement fighterDetailsWindow;
    private Label fighterDetailsName;
    private Label fighterDetailsRole;
    private Label fighterDetailsLevel;
    private Label fighterDetailsAttack;
    private Label fighterDetailsDefense;
    private Label fighterDetailsHealthText;
    private Label fighterDetailsState;
    private Label fighterDetailsLocation;
    private VisualElement fighterDetailsHealthFill;
    private string openedFighterDetailsId;
    private IVisualElementScheduledItem fighterDetailsUiPoll;
    private GameState fighterDetailsObservedState;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeFighterDetailsRuntime()
    {
        PrototypeUIController controller =
            UnityEngine.Object.FindAnyObjectByType<PrototypeUIController>();
        if (controller == null)
            return;

        UIDocument document = controller.GetComponent<UIDocument>();
        if (document == null)
            return;

        document.rootVisualElement.schedule
            .Execute(controller.TryInitializeFighterDetailsUi)
            .ExecuteLater(170);
    }

    private void TryInitializeFighterDetailsUi()
    {
        if (fighterDetailsUiInitialized)
            return;

        if (interfaceRoot == null || gameState == null)
        {
            UIDocument document = GetComponent<UIDocument>();
            if (document != null)
            {
                document.rootVisualElement.schedule
                    .Execute(TryInitializeFighterDetailsUi)
                    .ExecuteLater(60);
            }
            return;
        }

        CreateFighterDetailsWindow();
        fighterDetailsObservedState = gameState;
        fighterDetailsUiPoll = interfaceRoot.schedule
            .Execute(TickFighterDetailsUi)
            .Every(100);
        fighterDetailsUiInitialized = true;
        TickFighterDetailsUi();
    }

    private void TickFighterDetailsUi()
    {
        if (gameState == null)
            return;

        if (!ReferenceEquals(fighterDetailsObservedState, gameState))
        {
            fighterDetailsObservedState = gameState;
            CloseFighterDetails();
        }

        DecorateArmyFighterCards();

        if (!string.IsNullOrEmpty(openedFighterDetailsId))
            RefreshFighterDetailsWindow();
    }

    private void DecorateArmyFighterCards()
    {
        DecorateFighterCardsInList(commanderGarrisonList);
        DecorateFighterCardsInList(capitalGarrisonList);
    }

    private void DecorateFighterCardsInList(VisualElement list)
    {
        if (list == null)
            return;

        foreach (VisualElement child in list.Children())
        {
            Button card = child as Button;
            if (card == null || !card.ClassListContains("fighter-card"))
                continue;

            Label nameLabel = card.Q<Label>(className: "fighter-name");
            if (nameLabel == null)
                continue;

            FighterData fighter = FindFighterByName(nameLabel.text);
            if (fighter == null)
                continue;

            if (!card.ClassListContains("fighter-details-bound"))
            {
                string fighterId = fighter.Id;
                card.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 1 || isGameOver)
                        return;

                    OpenFighterDetails(fighterId);
                    evt.StopPropagation();
                });
                card.AddToClassList("fighter-details-bound");
            }

            // Во время экспедиции состав нельзя менять, но ПКМ должен оставаться
            // доступным для просмотра состояния бойца. Существующий обработчик
            // левой кнопки сам блокирует перенос при активной экспедиции.
            if (!isGameOver && !card.enabledSelf)
                card.SetEnabled(true);

            ApplyCompactFighterCardPresentation(card, fighter, nameLabel);
        }
    }

    private void ApplyCompactFighterCardPresentation(
        Button card,
        FighterData fighter,
        Label nameLabel)
    {
        FighterCombatState combat =
            BattleSystem.GetFighterCombatState(gameState, fighter.Id);
        if (combat == null)
            return;

        card.style.position = Position.Relative;
        card.tooltip = "ПКМ — сведения о бойце" +
            (gameState.HasActiveExpedition
                ? ". Состав зафиксирован до возвращения."
                : ". ЛКМ/перетаскивание — сменить гарнизон.");

        VisualElement image =
            card.Q<VisualElement>(className: "fighter-image-placeholder");
        if (image != null)
        {
            image.style.position = Position.Absolute;
            image.style.left = 4f;
            image.style.right = 4f;
            image.style.top = 4f;
            image.style.bottom = 23f;
            image.style.width = StyleKeyword.Auto;
            image.style.height = StyleKeyword.Auto;
            image.style.marginBottom = 0f;
        }

        nameLabel.style.display = DisplayStyle.Flex;
        nameLabel.style.position = Position.Absolute;
        nameLabel.style.left = 4f;
        nameLabel.style.right = 4f;
        nameLabel.style.bottom = 11f;
        nameLabel.style.height = 12f;
        nameLabel.style.fontSize = 9f;
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        nameLabel.style.color = new Color(0.9f, 0.87f, 0.78f, 1f);
        nameLabel.style.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 0.88f);

        Label roleLabel = card.Q<Label>(className: "fighter-role");
        Label infoLabel = card.Q<Label>(className: "fighter-info");
        Label assignmentLabel = card.Q<Label>(className: "fighter-assignment");
        if (roleLabel != null)
            roleLabel.style.display = DisplayStyle.None;
        if (infoLabel != null)
            infoLabel.style.display = DisplayStyle.None;
        if (assignmentLabel != null)
            assignmentLabel.style.display = DisplayStyle.None;

        VisualElement healthBar = card.Q<VisualElement>("fighter-health-bar");
        VisualElement healthFill;
        if (healthBar == null)
        {
            healthBar = new VisualElement { name = "fighter-health-bar" };
            healthBar.pickingMode = PickingMode.Ignore;
            healthBar.style.position = Position.Absolute;
            healthBar.style.left = 5f;
            healthBar.style.right = 5f;
            healthBar.style.bottom = 4f;
            healthBar.style.height = 6f;
            healthBar.style.backgroundColor = new Color(0.10f, 0.11f, 0.13f, 1f);
            healthBar.style.borderTopLeftRadius = 2f;
            healthBar.style.borderTopRightRadius = 2f;
            healthBar.style.borderBottomLeftRadius = 2f;
            healthBar.style.borderBottomRightRadius = 2f;

            healthFill = new VisualElement { name = "fighter-health-fill" };
            healthFill.pickingMode = PickingMode.Ignore;
            healthFill.style.height = Length.Percent(100f);
            healthFill.style.borderTopLeftRadius = 2f;
            healthFill.style.borderTopRightRadius = 2f;
            healthFill.style.borderBottomLeftRadius = 2f;
            healthFill.style.borderBottomRightRadius = 2f;
            healthBar.Add(healthFill);
            card.Add(healthBar);
        }
        else
        {
            healthFill = healthBar.Q<VisualElement>("fighter-health-fill");
        }

        UpdateHealthBar(healthFill, combat.HitPoints, combat.MaxHitPoints);
    }

    private FighterData FindFighterByName(string fighterName)
    {
        if (gameState == null || string.IsNullOrEmpty(fighterName))
            return null;

        foreach (FighterData fighter in gameState.Fighters)
        {
            if (fighter.Name == fighterName)
                return fighter;
        }

        return null;
    }

    private void CreateFighterDetailsWindow()
    {
        fighterDetailsWindow = new VisualElement();
        fighterDetailsWindow.name = "fighter-details-window";
        fighterDetailsWindow.style.display = DisplayStyle.None;
        fighterDetailsWindow.style.position = Position.Absolute;
        fighterDetailsWindow.style.right = 36f;
        fighterDetailsWindow.style.top = 86f;
        fighterDetailsWindow.style.width = 440f;
        fighterDetailsWindow.style.height = 360f;
        fighterDetailsWindow.style.paddingLeft = 16f;
        fighterDetailsWindow.style.paddingRight = 16f;
        fighterDetailsWindow.style.paddingTop = 14f;
        fighterDetailsWindow.style.paddingBottom = 14f;
        fighterDetailsWindow.style.backgroundColor =
            new Color(0.105f, 0.12f, 0.145f, 0.985f);
        SetBorder(fighterDetailsWindow, new Color(0.39f, 0.34f, 0.25f, 1f), 1f);
        SetRadius(fighterDetailsWindow, 6f);

        VisualElement header = new VisualElement();
        header.style.height = 36f;
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;

        fighterDetailsName = new Label("БОЕЦ");
        fighterDetailsName.style.fontSize = 19f;
        fighterDetailsName.style.unityFontStyleAndWeight = FontStyle.Bold;
        fighterDetailsName.style.color = new Color(0.88f, 0.72f, 0.39f, 1f);
        header.Add(fighterDetailsName);

        Button closeButton = new Button(CloseFighterDetails) { text = "×" };
        closeButton.style.width = 34f;
        closeButton.style.height = 30f;
        closeButton.style.fontSize = 18f;
        closeButton.style.backgroundColor = new Color(0.20f, 0.22f, 0.26f, 1f);
        closeButton.style.color = new Color(0.88f, 0.85f, 0.78f, 1f);
        header.Add(closeButton);
        fighterDetailsWindow.Add(header);

        VisualElement body = new VisualElement();
        body.style.flexGrow = 1f;
        body.style.flexDirection = FlexDirection.Row;
        body.style.marginTop = 10f;

        VisualElement portrait = new VisualElement();
        portrait.style.width = 180f;
        portrait.style.height = 260f;
        portrait.style.flexShrink = 0f;
        portrait.style.alignItems = Align.Center;
        portrait.style.justifyContent = Justify.Center;
        portrait.style.backgroundColor = new Color(0.075f, 0.085f, 0.105f, 1f);
        SetBorder(portrait, new Color(0.27f, 0.30f, 0.34f, 1f), 1f);
        SetRadius(portrait, 4f);
        Label portraitLabel = new Label("ИЗОБРАЖЕНИЕ\nБОЙЦА");
        portraitLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        portraitLabel.style.whiteSpace = WhiteSpace.Normal;
        portraitLabel.style.color = new Color(0.42f, 0.45f, 0.50f, 1f);
        portrait.Add(portraitLabel);
        body.Add(portrait);

        VisualElement info = new VisualElement();
        info.style.flexGrow = 1f;
        info.style.marginLeft = 18f;

        fighterDetailsRole = CreateDetailLine(info, "Роль: —", true);
        fighterDetailsLevel = CreateDetailLine(info, "Уровень: —", false);
        fighterDetailsAttack = CreateDetailLine(info, "Атака: —", false);
        fighterDetailsDefense = CreateDetailLine(info, "Защита: —", false);

        Label hpTitle = CreateDetailLine(info, "ЖИЗНИ", true);
        hpTitle.style.marginTop = 14f;

        VisualElement hpBar = new VisualElement();
        hpBar.style.height = 16f;
        hpBar.style.marginTop = 5f;
        hpBar.style.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 1f);
        SetRadius(hpBar, 3f);
        fighterDetailsHealthFill = new VisualElement();
        fighterDetailsHealthFill.style.height = Length.Percent(100f);
        SetRadius(fighterDetailsHealthFill, 3f);
        hpBar.Add(fighterDetailsHealthFill);
        info.Add(hpBar);

        fighterDetailsHealthText = CreateDetailLine(info, "100 / 100", false);
        fighterDetailsHealthText.style.unityTextAlign = TextAnchor.MiddleCenter;
        fighterDetailsState = CreateDetailLine(info, "Состояние: —", false);
        fighterDetailsLocation = CreateDetailLine(info, "Местонахождение: —", false);
        fighterDetailsLocation.style.marginTop = 12f;

        body.Add(info);
        fighterDetailsWindow.Add(body);

        Label hint = new Label("ПКМ по другой карточке — переключить бойца");
        hint.style.marginTop = 8f;
        hint.style.fontSize = 10f;
        hint.style.color = new Color(0.55f, 0.56f, 0.56f, 1f);
        fighterDetailsWindow.Add(hint);

        interfaceRoot.Add(fighterDetailsWindow);
    }

    private Label CreateDetailLine(
        VisualElement parent,
        string text,
        bool emphasized)
    {
        Label label = new Label(text);
        label.style.marginBottom = 7f;
        label.style.fontSize = emphasized ? 14f : 13f;
        label.style.color = emphasized
            ? new Color(0.86f, 0.82f, 0.72f, 1f)
            : new Color(0.74f, 0.75f, 0.74f, 1f);
        if (emphasized)
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
        parent.Add(label);
        return label;
    }

    private void OpenFighterDetails(string fighterId)
    {
        if (fighterDetailsWindow == null ||
            gameState == null ||
            gameState.FindFighter(fighterId) == null)
        {
            return;
        }

        openedFighterDetailsId = fighterId;
        RefreshFighterDetailsWindow();
        fighterDetailsWindow.style.display = DisplayStyle.Flex;
        fighterDetailsWindow.BringToFront();
    }

    private void CloseFighterDetails()
    {
        openedFighterDetailsId = null;
        if (fighterDetailsWindow != null)
            fighterDetailsWindow.style.display = DisplayStyle.None;
    }

    private void RefreshFighterDetailsWindow()
    {
        FighterData fighter = gameState.FindFighter(openedFighterDetailsId);
        if (fighter == null)
        {
            CloseFighterDetails();
            return;
        }

        FighterCombatState combat =
            BattleSystem.GetFighterCombatState(gameState, fighter.Id);
        if (combat == null)
            return;

        fighterDetailsName.text = fighter.Name.ToUpper();
        fighterDetailsRole.text =
            "Роль: " + BattleSystem.GetRoleLabel(combat.RoleCode);
        fighterDetailsLevel.text = "Уровень: " + fighter.Level;
        fighterDetailsAttack.text = "Атака: " + combat.AttackPower;
        fighterDetailsDefense.text = "Защита: " + combat.DefensePower;
        fighterDetailsHealthText.text =
            (int)Math.Ceiling(combat.HitPoints) + " / " + combat.MaxHitPoints;
        fighterDetailsState.text =
            "Состояние: " + BattleSystem.GetHealthLabel(combat.HealthState);
        fighterDetailsLocation.text =
            "Местонахождение: " +
            (gameState.IsFighterInActiveExpedition(fighter.Id)
                ? "экспедиция"
                : "столица");

        UpdateHealthBar(
            fighterDetailsHealthFill,
            combat.HitPoints,
            combat.MaxHitPoints);
    }

    private static void UpdateHealthBar(
        VisualElement fill,
        double hitPoints,
        int maxHitPoints)
    {
        if (fill == null)
            return;

        double safeMax = Math.Max(1, maxHitPoints);
        float fraction = Mathf.Clamp01((float)(hitPoints / safeMax));
        fill.style.width = Length.Percent(fraction * 100f);

        if (fraction > 0.60f)
            fill.style.backgroundColor = new Color(0.32f, 0.62f, 0.40f, 1f);
        else if (fraction > 0.30f)
            fill.style.backgroundColor = new Color(0.72f, 0.57f, 0.25f, 1f);
        else
            fill.style.backgroundColor = new Color(0.66f, 0.28f, 0.26f, 1f);
    }

    private static void SetBorder(
        VisualElement element,
        Color color,
        float width)
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
