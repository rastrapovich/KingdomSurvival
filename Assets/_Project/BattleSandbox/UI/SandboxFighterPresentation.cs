using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    internal static class SandboxFighterCardFactory
    {
        private static readonly Color PlayerAccent = new Color(0.78f, 0.62f, 0.27f, 1f);
        private static readonly Color EnemyAccent = new Color(0.68f, 0.27f, 0.23f, 1f);
        private static readonly Color CardBackground = new Color(0.10f, 0.115f, 0.135f, 1f);

        public static Button CreateRosterCard(
            SandboxUnitDefinition definition,
            Action onSelect,
            Action onDetails)
        {
            Button card = CreatePortraitCard(definition, SandboxTeam.Player, onSelect, onDetails);
            card.name = "sandbox-roster-card-" + definition.Id;
            card.style.width = 142f;
            card.style.height = 202f;
            card.style.flexShrink = 0f;
            card.style.marginRight = 10f;
            card.style.marginBottom = 10f;

            Label hint = new Label("ЛКМ: выбрать · ПКМ: сведения");
            hint.style.fontSize = 8f;
            hint.style.color = new Color(0.48f, 0.49f, 0.48f, 1f);
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.pickingMode = PickingMode.Ignore;
            card.Add(hint);
            return card;
        }

        public static Button CreateEnemyPreviewCard(
            SandboxUnitDefinition definition,
            Action onDetails)
        {
            Button card = CreatePortraitCard(definition, SandboxTeam.Enemy, onDetails, onDetails);
            card.name = "sandbox-enemy-card-" + definition.Id;
            card.style.width = 142f;
            card.style.height = 184f;
            card.style.flexShrink = 0f;
            card.style.marginRight = 10f;
            card.style.marginBottom = 10f;
            return card;
        }

        public static Button CreateInitiativeCard(
            SandboxUnitState unit,
            bool active,
            Action onDetails)
        {
            bool damaged = unit.IsDamaged;
            Button card = new Button(onDetails);
            card.name = "sandbox-initiative-card-" + unit.Id;
            card.userData = unit.Id;
            card.style.width = active ? 64f : 58f;
            card.style.height = active ? 86f : 78f;
            card.style.flexShrink = 0f;
            card.style.marginRight = 6f;
            card.style.paddingLeft = 4f;
            card.style.paddingRight = 4f;
            card.style.paddingTop = 4f;
            card.style.paddingBottom = 4f;
            card.style.alignItems = Align.Stretch;
            card.style.backgroundColor = active
                ? damaged
                    ? new Color(0.29f, 0.18f, 0.14f, 1f)
                    : new Color(0.25f, 0.25f, 0.21f, 1f)
                : damaged
                    ? new Color(0.18f, 0.095f, 0.105f, 1f)
                    : CardBackground;
            card.style.color = unit.Team == SandboxTeam.Player ? PlayerAccent : EnemyAccent;
            SetBorder(
                card,
                active
                    ? new Color(0.94f, 0.80f, 0.45f, 1f)
                    : damaged
                        ? new Color(0.78f, 0.30f, 0.25f, 1f)
                        : unit.Team == SandboxTeam.Player
                            ? new Color(0.45f, 0.38f, 0.23f, 1f)
                            : new Color(0.45f, 0.24f, 0.23f, 1f),
                active ? 3f : damaged ? 2f : 1f);
            SetRadius(card, 4f);

            SandboxInitiativePortrait portrait = new SandboxInitiativePortrait(
                unit.Role,
                unit.Team,
                damaged);
            portrait.style.flexGrow = 1f;
            portrait.style.minHeight = 0f;
            portrait.style.backgroundColor = unit.Team == SandboxTeam.Player
                ? new Color(0.08f, 0.11f, 0.14f, 1f)
                : new Color(0.16f, 0.075f, 0.08f, 1f);
            SetRadius(portrait, 2f);
            card.Add(portrait);

            VisualElement healthBar = CreateHealthBar(unit.HitPoints, unit.MaxHitPoints, 5f);
            healthBar.style.marginTop = 3f;
            card.Add(healthBar);
            card.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1)
                    return;
                onDetails?.Invoke();
                evt.StopPropagation();
            });
            card.tooltip = unit.DisplayLabel + " · инициатива " + unit.Initiative +
                           " · HP " + unit.HitPoints + "/" + unit.MaxHitPoints +
                           " · ЛКМ или ПКМ — открыть карточку";
            return card;
        }

        public static void SetRosterSelected(Button card, bool selected)
        {
            if (card == null)
                return;

            card.style.backgroundColor = selected
                ? new Color(0.25f, 0.22f, 0.14f, 1f)
                : CardBackground;
            SetBorder(
                card,
                selected
                    ? new Color(0.84f, 0.68f, 0.32f, 1f)
                    : new Color(0.31f, 0.32f, 0.32f, 1f),
                selected ? 2f : 1f);

            Label badge = card.Q<Label>("sandbox-selection-badge");
            if (badge != null)
            {
                badge.text = selected ? "✓" : string.Empty;
                badge.style.backgroundColor = selected
                    ? new Color(0.69f, 0.53f, 0.22f, 1f)
                    : new Color(0f, 0f, 0f, 0f);
            }
        }

        private static Button CreatePortraitCard(
            SandboxUnitDefinition definition,
            SandboxTeam team,
            Action onClick,
            Action onDetails)
        {
            Button card = new Button(onClick);
            card.userData = definition.Id;
            card.style.paddingLeft = 7f;
            card.style.paddingRight = 7f;
            card.style.paddingTop = 7f;
            card.style.paddingBottom = 7f;
            card.style.backgroundColor = CardBackground;
            card.style.color = new Color(0.84f, 0.82f, 0.75f, 1f);
            card.style.position = Position.Relative;
            card.style.alignItems = Align.Stretch;
            SetBorder(
                card,
                team == SandboxTeam.Player
                    ? new Color(0.36f, 0.34f, 0.29f, 1f)
                    : new Color(0.43f, 0.24f, 0.23f, 1f));
            SetRadius(card, 4f);

            VisualElement portrait = new VisualElement();
            portrait.style.height = 106f;
            portrait.style.alignItems = Align.Center;
            portrait.style.justifyContent = Justify.Center;
            portrait.style.backgroundColor = team == SandboxTeam.Player
                ? new Color(0.11f, 0.13f, 0.16f, 1f)
                : new Color(0.17f, 0.095f, 0.10f, 1f);
            SetBorder(
                portrait,
                team == SandboxTeam.Player
                    ? new Color(0.29f, 0.32f, 0.36f, 1f)
                    : new Color(0.43f, 0.22f, 0.21f, 1f));
            SetRadius(portrait, 3f);
            portrait.pickingMode = PickingMode.Ignore;

            Label portraitRole = new Label(
                "ИЗОБРАЖЕНИЕ\n" + definition.RoleLabel.ToUpper());
            portraitRole.style.fontSize = 10f;
            portraitRole.style.unityFontStyleAndWeight = FontStyle.Bold;
            portraitRole.style.unityTextAlign = TextAnchor.MiddleCenter;
            portraitRole.style.color = team == SandboxTeam.Player ? PlayerAccent : EnemyAccent;
            portraitRole.pickingMode = PickingMode.Ignore;
            portrait.Add(portraitRole);
            card.Add(portrait);

            Label type = new Label(definition.RoleLabel.ToUpper());
            type.style.marginTop = 6f;
            type.style.fontSize = 11f;
            type.style.unityFontStyleAndWeight = FontStyle.Bold;
            type.style.unityTextAlign = TextAnchor.MiddleCenter;
            type.pickingMode = PickingMode.Ignore;
            card.Add(type);

            Label stats = new Label(
                "HP " + definition.MaxHitPoints + "  ·  УРОН " + definition.Damage + "\n" +
                "АТК " + definition.Attack + "  ·  ЗАЩ " + definition.Defense);
            stats.style.marginTop = 3f;
            stats.style.fontSize = 8f;
            stats.style.color = new Color(0.61f, 0.62f, 0.60f, 1f);
            stats.style.unityTextAlign = TextAnchor.MiddleCenter;
            stats.style.whiteSpace = WhiteSpace.Normal;
            stats.pickingMode = PickingMode.Ignore;
            card.Add(stats);

            VisualElement healthBar = CreateHealthBar(
                definition.MaxHitPoints,
                definition.MaxHitPoints,
                5f);
            healthBar.style.marginTop = 5f;
            card.Add(healthBar);

            Label badge = new Label();
            badge.name = "sandbox-selection-badge";
            badge.style.position = Position.Absolute;
            badge.style.right = 5f;
            badge.style.top = 5f;
            badge.style.width = 22f;
            badge.style.height = 22f;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = Color.white;
            SetRadius(badge, 11f);
            badge.pickingMode = PickingMode.Ignore;
            card.Add(badge);

            card.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1)
                    return;
                onDetails?.Invoke();
                evt.StopPropagation();
            });
            card.tooltip = team == SandboxTeam.Player
                ? "ЛКМ — выбрать. ПКМ — открыть карточку бойца."
                : "Открыть карточку противника.";
            return card;
        }

        private static VisualElement CreateHealthBar(int hitPoints, int maxHitPoints, float height)
        {
            VisualElement bar = new VisualElement();
            bar.style.height = height;
            bar.style.backgroundColor = new Color(0.055f, 0.06f, 0.07f, 1f);
            SetRadius(bar, 2f);
            bar.pickingMode = PickingMode.Ignore;

            VisualElement fill = new VisualElement();
            fill.style.height = Length.Percent(100f);
            fill.style.width = Length.Percent(
                Mathf.Clamp01((float)hitPoints / Mathf.Max(1, maxHitPoints)) * 100f);
            fill.style.backgroundColor = GetHealthColor(hitPoints, maxHitPoints);
            SetRadius(fill, 2f);
            fill.pickingMode = PickingMode.Ignore;
            bar.Add(fill);
            return bar;
        }

        private static Color GetHealthColor(int hitPoints, int maxHitPoints)
        {
            float fraction = Mathf.Clamp01((float)hitPoints / Mathf.Max(1, maxHitPoints));
            if (fraction > 0.60f)
                return new Color(0.32f, 0.62f, 0.40f, 1f);
            if (fraction > 0.30f)
                return new Color(0.72f, 0.57f, 0.25f, 1f);
            return new Color(0.66f, 0.28f, 0.26f, 1f);
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

    internal sealed class SandboxInitiativePortrait : VisualElement
    {
        private readonly SandboxUnitRole role;
        private readonly SandboxTeam team;
        private readonly bool damaged;

        public SandboxInitiativePortrait(
            SandboxUnitRole role,
            SandboxTeam team,
            bool damaged)
        {
            this.role = role;
            this.team = team;
            this.damaged = damaged;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += DrawPortrait;
        }

        private void DrawPortrait(MeshGenerationContext context)
        {
            if (contentRect.width <= 1f || contentRect.height <= 1f)
                return;

            Painter2D painter = context.painter2D;
            float width = contentRect.width;
            float height = contentRect.height;
            Color silhouette = team == SandboxTeam.Player
                ? new Color(0.78f, 0.62f, 0.27f, damaged ? 0.68f : 0.95f)
                : new Color(0.72f, 0.27f, 0.23f, damaged ? 0.68f : 0.95f);

            Vector2 headCenter = new Vector2(width * 0.5f, height * 0.29f);
            float headRadius = Mathf.Min(width, height) * 0.12f;
            DrawCircle(painter, headCenter, headRadius, silhouette);

            painter.fillColor = silhouette;
            painter.BeginPath();
            painter.MoveTo(new Vector2(width * 0.20f, height * 0.78f));
            painter.LineTo(new Vector2(width * 0.27f, height * 0.52f));
            painter.LineTo(new Vector2(width * 0.42f, height * 0.43f));
            painter.LineTo(new Vector2(width * 0.58f, height * 0.43f));
            painter.LineTo(new Vector2(width * 0.73f, height * 0.52f));
            painter.LineTo(new Vector2(width * 0.80f, height * 0.78f));
            painter.ClosePath();
            painter.Fill();

            DrawRoleMark(
                painter,
                new Vector2(width * 0.5f, height * 0.63f),
                Mathf.Min(width, height) * 0.16f);
        }

        private void DrawRoleMark(Painter2D painter, Vector2 center, float half)
        {
            painter.strokeColor = new Color(0.055f, 0.05f, 0.045f, 0.92f);
            painter.lineWidth = 2f;
            painter.BeginPath();

            if (role == SandboxUnitRole.Archer)
            {
                painter.MoveTo(center + new Vector2(-half, half));
                painter.LineTo(center + new Vector2(half, -half));
                painter.LineTo(center + new Vector2(half * 0.25f, -half));
                painter.MoveTo(center + new Vector2(half, -half));
                painter.LineTo(center + new Vector2(half, -half * 0.25f));
            }
            else if (role == SandboxUnitRole.Guard)
            {
                painter.MoveTo(center + new Vector2(-half, -half));
                painter.LineTo(center + new Vector2(-half, half * 0.35f));
                painter.LineTo(center + new Vector2(0f, half));
                painter.LineTo(center + new Vector2(half, half * 0.35f));
                painter.LineTo(center + new Vector2(half, -half));
                painter.ClosePath();
            }
            else if (role == SandboxUnitRole.Healer)
            {
                painter.MoveTo(center + new Vector2(-half, 0f));
                painter.LineTo(center + new Vector2(half, 0f));
                painter.MoveTo(center + new Vector2(0f, -half));
                painter.LineTo(center + new Vector2(0f, half));
            }
            else if (role == SandboxUnitRole.Spearman)
            {
                painter.MoveTo(center + new Vector2(-half, half));
                painter.LineTo(center + new Vector2(half, -half));
            }
            else if (role == SandboxUnitRole.Scout)
            {
                painter.MoveTo(center + new Vector2(0f, -half));
                painter.LineTo(center + new Vector2(half, 0f));
                painter.LineTo(center + new Vector2(0f, half));
                painter.LineTo(center + new Vector2(-half, 0f));
                painter.ClosePath();
            }
            else if (role == SandboxUnitRole.Beast)
            {
                painter.MoveTo(center + new Vector2(-half, -half));
                painter.LineTo(center + new Vector2(0f, half));
                painter.LineTo(center + new Vector2(half, -half));
            }
            else
            {
                painter.MoveTo(center + new Vector2(-half, 0f));
                painter.LineTo(center + new Vector2(half, 0f));
            }

            painter.Stroke();
        }

        private static void DrawCircle(
            Painter2D painter,
            Vector2 center,
            float radius,
            Color fill)
        {
            const int segments = 16;
            painter.fillColor = fill;
            painter.BeginPath();
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }

            painter.ClosePath();
            painter.Fill();
        }
    }

    internal sealed class SandboxFighterDetailsView
    {
        private const float WindowWidth = 650f;
        private const float WindowHeight = 500f;

        private readonly VisualElement root;
        private readonly VisualElement dimmer;
        private readonly VisualElement window;
        private readonly VisualElement statTooltip;
        private readonly Label tooltipTitle;
        private readonly Label tooltipText;
        private readonly Label fighterTitle;
        private readonly Label damageBadge;
        private readonly Label fighterRole;
        private readonly VisualElement portraitPanel;
        private readonly Label portraitLabel;
        private readonly Label teamLabel;
        private readonly VisualElement healthFill;
        private readonly Label healthText;
        private readonly Label attackValue;
        private readonly Label defenseValue;
        private readonly Label damageValue;
        private readonly Label movementValue;
        private readonly Label initiativeValue;
        private readonly Label rangeValue;
        private readonly Label actionsValue;
        private readonly Label stateValue;

        private SandboxUnitDefinition openedDefinition;
        private SandboxUnitState openedState;

        public SandboxFighterDetailsView(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));

            dimmer = new VisualElement { focusable = true };
            dimmer.name = "sandbox-fighter-details-dimmer";
            dimmer.style.display = DisplayStyle.None;
            dimmer.style.position = Position.Absolute;
            dimmer.style.left = 0f;
            dimmer.style.right = 0f;
            dimmer.style.top = 0f;
            dimmer.style.bottom = 0f;
            dimmer.style.backgroundColor = new Color(0.01f, 0.015f, 0.02f, 0.74f);
            dimmer.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.target != dimmer)
                    return;
                Close();
                evt.StopPropagation();
            });
            dimmer.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                    return;
                Close();
                evt.StopPropagation();
            });
            root.Add(dimmer);

            window = new VisualElement();
            window.name = "sandbox-fighter-details-window";
            window.style.display = DisplayStyle.None;
            window.style.position = Position.Absolute;
            window.style.width = WindowWidth;
            window.style.height = WindowHeight;
            window.style.paddingLeft = 18f;
            window.style.paddingRight = 18f;
            window.style.paddingTop = 15f;
            window.style.paddingBottom = 15f;
            window.style.backgroundColor = new Color(0.105f, 0.12f, 0.145f, 0.995f);
            SetBorder(window, new Color(0.45f, 0.38f, 0.25f, 1f), 1f);
            SetRadius(window, 6f);
            root.Add(window);

            VisualElement header = new VisualElement();
            header.style.height = 40f;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;

            VisualElement identity = new VisualElement();
            identity.style.flexGrow = 1f;
            identity.style.flexDirection = FlexDirection.Row;
            identity.style.alignItems = Align.Center;

            fighterTitle = CreateLabel("ТИП БОЙЦА", 20, new Color(0.90f, 0.74f, 0.40f, 1f));
            fighterTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            identity.Add(fighterTitle);

            damageBadge = CreateLabel(string.Empty, 10, Color.white);
            damageBadge.style.display = DisplayStyle.None;
            damageBadge.style.marginLeft = 12f;
            damageBadge.style.paddingLeft = 9f;
            damageBadge.style.paddingRight = 9f;
            damageBadge.style.paddingTop = 4f;
            damageBadge.style.paddingBottom = 4f;
            damageBadge.style.backgroundColor = new Color(0.62f, 0.19f, 0.17f, 1f);
            damageBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            SetRadius(damageBadge, 3f);
            identity.Add(damageBadge);
            header.Add(identity);

            Button closeButton = new Button(Close) { text = "×" };
            closeButton.style.width = 36f;
            closeButton.style.height = 32f;
            closeButton.style.fontSize = 19f;
            closeButton.style.backgroundColor = new Color(0.20f, 0.22f, 0.26f, 1f);
            closeButton.style.color = new Color(0.88f, 0.85f, 0.78f, 1f);
            SetBorder(closeButton, new Color(0.34f, 0.35f, 0.36f, 1f));
            SetRadius(closeButton, 3f);
            closeButton.RegisterCallback<PointerEnterEvent>(_ =>
            {
                closeButton.style.backgroundColor = new Color(0.62f, 0.20f, 0.17f, 1f);
                closeButton.style.color = Color.white;
                SetBorder(closeButton, new Color(0.90f, 0.46f, 0.34f, 1f), 2f);
            });
            closeButton.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                closeButton.style.backgroundColor = new Color(0.20f, 0.22f, 0.26f, 1f);
                closeButton.style.color = new Color(0.88f, 0.85f, 0.78f, 1f);
                SetBorder(closeButton, new Color(0.34f, 0.35f, 0.36f, 1f));
            });
            header.Add(closeButton);
            window.Add(header);

            VisualElement body = new VisualElement();
            body.style.flexGrow = 1f;
            body.style.flexDirection = FlexDirection.Row;
            body.style.marginTop = 10f;

            portraitPanel = new VisualElement();
            portraitPanel.style.width = 210f;
            portraitPanel.style.height = 390f;
            portraitPanel.style.flexShrink = 0f;
            portraitPanel.style.alignItems = Align.Center;
            portraitPanel.style.justifyContent = Justify.Center;
            portraitPanel.style.backgroundColor = new Color(0.075f, 0.085f, 0.105f, 1f);
            SetBorder(portraitPanel, new Color(0.27f, 0.30f, 0.34f, 1f));
            SetRadius(portraitPanel, 4f);

            portraitLabel = CreateLabel("ИЗОБРАЖЕНИЕ\nБОЙЦА", 15, new Color(0.50f, 0.48f, 0.42f, 1f));
            portraitLabel.style.whiteSpace = WhiteSpace.Normal;
            portraitLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            portraitLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            portraitPanel.Add(portraitLabel);

            fighterRole = CreateLabel("Роль", 13, new Color(0.78f, 0.65f, 0.39f, 1f));
            fighterRole.style.position = Position.Absolute;
            fighterRole.style.left = 8f;
            fighterRole.style.right = 8f;
            fighterRole.style.bottom = 54f;
            fighterRole.style.unityTextAlign = TextAnchor.MiddleCenter;
            fighterRole.style.unityFontStyleAndWeight = FontStyle.Bold;
            portraitPanel.Add(fighterRole);

            teamLabel = CreateLabel("ОТРЯД", 9, new Color(0.52f, 0.54f, 0.54f, 1f));
            teamLabel.style.position = Position.Absolute;
            teamLabel.style.left = 8f;
            teamLabel.style.right = 8f;
            teamLabel.style.bottom = 35f;
            teamLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            portraitPanel.Add(teamLabel);

            VisualElement hpBar = new VisualElement();
            hpBar.style.position = Position.Absolute;
            hpBar.style.left = 12f;
            hpBar.style.right = 12f;
            hpBar.style.bottom = 13f;
            hpBar.style.height = 12f;
            hpBar.style.backgroundColor = new Color(0.055f, 0.06f, 0.07f, 1f);
            SetRadius(hpBar, 3f);
            healthFill = new VisualElement();
            healthFill.style.height = Length.Percent(100f);
            SetRadius(healthFill, 3f);
            hpBar.Add(healthFill);
            portraitPanel.Add(hpBar);
            body.Add(portraitPanel);

            VisualElement info = new VisualElement();
            info.style.flexGrow = 1f;
            info.style.marginLeft = 20f;

            Label section = CreateLabel("БОЕВЫЕ ХАРАКТЕРИСТИКИ", 12, new Color(0.72f, 0.67f, 0.56f, 1f));
            section.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.style.marginBottom = 8f;
            info.Add(section);

            healthText = CreateStatRow(
                info,
                "ЖИЗНИ",
                "—",
                "Запас здоровья участника. При 0 HP боец выбывает из текущего боя.");
            attackValue = CreateStatRow(
                info,
                "АТАКА",
                "—",
                "Преодолевает Защиту цели. Каждое очко разницы между Атакой и эффективной Защитой изменяет базовый Урон на 5.");
            defenseValue = CreateStatRow(
                info,
                "ЗАЩИТА",
                "—",
                "Снижает входящий урон на 5 за каждое очко относительно Атаки врага. Стойка временно добавляет +2.");
            damageValue = CreateStatRow(
                info,
                "УРОН",
                "—",
                "Базовая сила обычного удара или выстрела. Формула: max(5, Урон + (Атака − эффективная Защита) × 5).");
            movementValue = CreateStatRow(
                info,
                "ХОД",
                "—",
                "Общий запас движения на активацию. Его можно расходовать частями: обычный гекс стоит 1, сложный — 2. Последнее очко автоматически завершает активацию и сжигает неиспользованное ОД.");
            initiativeValue = CreateStatRow(
                info,
                "ИНИЦИАТИВА",
                "—",
                "Определяет порядок активаций в каждом раунде. Чем выше значение, тем раньше ходит участник.");
            rangeValue = CreateStatRow(
                info,
                "ДАЛЬНОСТЬ",
                "—",
                "Максимальная гексовая дистанция обычной атаки. Для ближнего удара гекс противника обязан входить в остаток движения.");
            actionsValue = CreateStatRow(
                info,
                "ОЧКИ ДЕЙСТВИЯ",
                "—",
                "В начале активации выдаётся 1 ОД на атаку или защитную стойку. Атака или стойка расходует ОД и завершает движение; нулевое движение завершает активацию даже с неиспользованным ОД.");

            stateValue = CreateLabel("Состояние: —", 11, new Color(0.66f, 0.67f, 0.65f, 1f));
            stateValue.style.marginTop = 10f;
            info.Add(stateValue);

            Label hoverHint = CreateLabel(
                "Наведите курсор на характеристику, чтобы увидеть точное объяснение.",
                9,
                new Color(0.49f, 0.56f, 0.58f, 1f));
            hoverHint.style.marginTop = 10f;
            hoverHint.style.whiteSpace = WhiteSpace.Normal;
            info.Add(hoverHint);

            body.Add(info);
            window.Add(body);

            statTooltip = new VisualElement();
            statTooltip.name = "sandbox-stat-tooltip";
            statTooltip.pickingMode = PickingMode.Ignore;
            statTooltip.style.display = DisplayStyle.None;
            statTooltip.style.position = Position.Absolute;
            statTooltip.style.width = 330f;
            statTooltip.style.paddingLeft = 12f;
            statTooltip.style.paddingRight = 12f;
            statTooltip.style.paddingTop = 10f;
            statTooltip.style.paddingBottom = 10f;
            statTooltip.style.backgroundColor = new Color(0.055f, 0.065f, 0.075f, 0.995f);
            SetBorder(statTooltip, new Color(0.58f, 0.47f, 0.26f, 1f));
            SetRadius(statTooltip, 4f);
            tooltipTitle = CreateLabel(string.Empty, 11, new Color(0.91f, 0.76f, 0.43f, 1f));
            tooltipTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            statTooltip.Add(tooltipTitle);
            tooltipText = CreateLabel(string.Empty, 10, new Color(0.76f, 0.76f, 0.72f, 1f));
            tooltipText.style.marginTop = 5f;
            tooltipText.style.whiteSpace = WhiteSpace.Normal;
            statTooltip.Add(tooltipText);
            root.Add(statTooltip);
        }

        public void Open(
            SandboxUnitDefinition definition,
            SandboxUnitState state = null,
            Vector2? anchorPosition = null)
        {
            if (definition == null)
                return;

            openedDefinition = definition;
            openedState = state;
            RefreshValues();
            HideTooltip();
            dimmer.style.display = DisplayStyle.Flex;
            window.style.display = DisplayStyle.Flex;
            PositionWindow(anchorPosition);
            dimmer.BringToFront();
            window.BringToFront();
            dimmer.Focus();
        }

        public void Refresh(SandboxBattle battle)
        {
            if (openedDefinition == null || window.resolvedStyle.display == DisplayStyle.None)
                return;

            openedState = battle != null ? battle.GetUnit(openedDefinition.Id) : null;
            RefreshValues();
        }

        public void Close()
        {
            openedDefinition = null;
            openedState = null;
            HideTooltip();
            window.style.display = DisplayStyle.None;
            dimmer.style.display = DisplayStyle.None;
        }

        private Label CreateStatRow(
            VisualElement parent,
            string title,
            string value,
            string explanation)
        {
            VisualElement row = new VisualElement();
            row.style.height = 34f;
            row.style.marginBottom = 4f;
            row.style.paddingLeft = 10f;
            row.style.paddingRight = 10f;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.backgroundColor = new Color(0.13f, 0.145f, 0.165f, 1f);
            SetBorder(row, new Color(0.25f, 0.27f, 0.29f, 1f));
            SetRadius(row, 3f);

            Label titleLabel = CreateLabel(title, 10, new Color(0.64f, 0.63f, 0.58f, 1f));
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.pickingMode = PickingMode.Ignore;
            row.Add(titleLabel);

            Label valueLabel = CreateLabel(value, 13, new Color(0.91f, 0.79f, 0.54f, 1f));
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            valueLabel.pickingMode = PickingMode.Ignore;
            row.Add(valueLabel);

            row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                row.style.backgroundColor = new Color(0.20f, 0.19f, 0.14f, 1f);
                ShowTooltip(row, title, explanation);
            });
            row.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                row.style.backgroundColor = new Color(0.13f, 0.145f, 0.165f, 1f);
                HideTooltip();
            });
            parent.Add(row);
            return valueLabel;
        }

        private void RefreshValues()
        {
            int hitPoints = openedState != null
                ? openedState.HitPoints
                : openedDefinition.MaxHitPoints;
            int actions = openedState != null
                ? openedState.ActionPoints
                : SandboxUnitState.ActionsPerActivation;
            int remainingMovement = openedState != null
                ? openedState.RemainingMovement
                : openedDefinition.Movement;
            int damageTaken = openedState != null ? openedState.DamageTaken : 0;
            bool damaged = damageTaken > 0;

            fighterTitle.text = openedDefinition.RoleLabel.ToUpper();
            fighterRole.text = openedDefinition.RoleLabel.ToUpper();
            portraitLabel.text = openedDefinition.RoleLabel.ToUpper() + "\n\nИЗОБРАЖЕНИЕ\nБОЙЦА";
            teamLabel.text = openedState == null || openedState.Team == SandboxTeam.Player
                ? "ОТРЯД КОРОЛЕВСТВА"
                : "ПРОТИВНИК";
            healthText.text = hitPoints + " / " + openedDefinition.MaxHitPoints;
            attackValue.text = openedDefinition.Attack.ToString();
            defenseValue.text = openedDefinition.Defense +
                (openedState != null && openedState.IsGuarding ? " + 2" : string.Empty);
            damageValue.text = openedDefinition.Damage.ToString();
            movementValue.text = remainingMovement + " / " + openedDefinition.Movement;
            initiativeValue.text = openedDefinition.Initiative.ToString();
            rangeValue.text = openedDefinition.AttackRange.ToString();
            actionsValue.text = actions + " / " + SandboxUnitState.ActionsPerActivation;

            damageBadge.style.display = damaged ? DisplayStyle.Flex : DisplayStyle.None;
            damageBadge.text = openedState != null && openedState.IsDefeated
                ? "ВЫБЫЛ"
                : "РАНЕН · −" + damageTaken + " HP";
            SetBorder(
                window,
                damaged
                    ? new Color(0.69f, 0.28f, 0.23f, 1f)
                    : new Color(0.45f, 0.38f, 0.25f, 1f),
                damaged ? 2f : 1f);
            portraitPanel.style.backgroundColor = damaged
                ? new Color(0.15f, 0.075f, 0.085f, 1f)
                : new Color(0.075f, 0.085f, 0.105f, 1f);

            stateValue.text = openedState == null
                ? "Состояние: готов к бою"
                : openedState.IsDefeated
                    ? "Состояние: выведен из строя"
                    : damaged && openedState.IsGuarding
                        ? "Состояние: ранен · защитная стойка"
                        : damaged
                            ? "Состояние: ранен · потеряно " + damageTaken + " HP"
                            : openedState.IsGuarding
                                ? "Состояние: защитная стойка"
                                : "Состояние: в бою";

            float fraction = Mathf.Clamp01(
                (float)hitPoints / Mathf.Max(1, openedDefinition.MaxHitPoints));
            healthFill.style.width = Length.Percent(fraction * 100f);
            healthFill.style.backgroundColor = fraction > 0.60f
                ? new Color(0.32f, 0.62f, 0.40f, 1f)
                : fraction > 0.30f
                    ? new Color(0.72f, 0.57f, 0.25f, 1f)
                    : new Color(0.66f, 0.28f, 0.26f, 1f);
        }

        private void PositionWindow(Vector2? anchorPosition)
        {
            float rootWidth = root.resolvedStyle.width;
            float rootHeight = root.resolvedStyle.height;
            if (float.IsNaN(rootWidth) || rootWidth < WindowWidth)
                rootWidth = 1280f;
            if (float.IsNaN(rootHeight) || rootHeight < WindowHeight)
                rootHeight = 720f;

            float left = (rootWidth - WindowWidth) * 0.5f;
            float top = (rootHeight - WindowHeight) * 0.5f;
            if (anchorPosition.HasValue)
            {
                Vector2 anchor = anchorPosition.Value;
                left = anchor.x + 22f;
                if (left + WindowWidth > rootWidth - 12f)
                    left = anchor.x - WindowWidth - 22f;
                top = anchor.y - 90f;
            }

            window.style.left = Mathf.Clamp(
                left,
                12f,
                Mathf.Max(12f, rootWidth - WindowWidth - 12f));
            window.style.top = Mathf.Clamp(
                top,
                12f,
                Mathf.Max(12f, rootHeight - WindowHeight - 12f));
        }

        private void ShowTooltip(VisualElement anchor, string title, string explanation)
        {
            tooltipTitle.text = title;
            tooltipText.text = explanation;
            statTooltip.style.display = DisplayStyle.Flex;
            statTooltip.BringToFront();

            Rect bounds = anchor.worldBound;
            Vector2 topRight = root.WorldToLocal(new Vector2(bounds.xMax, bounds.yMin));
            Vector2 topLeft = root.WorldToLocal(new Vector2(bounds.xMin, bounds.yMin));
            float rootWidth = root.resolvedStyle.width;
            float rootHeight = root.resolvedStyle.height;
            if (float.IsNaN(rootWidth) || rootWidth < 400f)
                rootWidth = 1280f;
            if (float.IsNaN(rootHeight) || rootHeight < 300f)
                rootHeight = 720f;

            float left = topRight.x + 10f;
            if (left + 330f > rootWidth - 12f)
                left = topLeft.x - 340f;
            statTooltip.style.left = Mathf.Clamp(left, 12f, Mathf.Max(12f, rootWidth - 342f));
            statTooltip.style.top = Mathf.Clamp(
                topRight.y,
                12f,
                Mathf.Max(12f, rootHeight - 120f));
        }

        private void HideTooltip()
        {
            statTooltip.style.display = DisplayStyle.None;
        }

        private static Label CreateLabel(string text, int size, Color color)
        {
            Label label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            return label;
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
