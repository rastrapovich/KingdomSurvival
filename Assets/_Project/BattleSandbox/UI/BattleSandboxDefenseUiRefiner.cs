using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    [DefaultExecutionOrder(13000)]
    internal sealed class BattleSandboxDefenseUiRefiner : MonoBehaviour
    {
        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly Color NormalStatColor =
            new Color(0.91f, 0.79f, 0.54f, 1f);
        private static readonly Color BonusStatColor =
            new Color(0.42f, 0.82f, 0.47f, 1f);
        private const string BonusColorHex = "#6BD178";

        private static readonly FieldInfo RootField =
            typeof(BattleSandboxController).GetField("root", InstanceFlags);
        private static readonly FieldInfo BattleField =
            typeof(BattleSandboxController).GetField("battle", InstanceFlags);
        private static readonly FieldInfo CurrentStatsLabelField =
            typeof(BattleSandboxController).GetField("currentStatsLabel", InstanceFlags);
        private static readonly FieldInfo FighterDetailsViewField =
            typeof(BattleSandboxController).GetField("fighterDetailsView", InstanceFlags);
        private static readonly FieldInfo GuardButtonField =
            typeof(BattleSandboxController).GetField("guardButton", InstanceFlags);
        private static readonly FieldInfo SelectedTargetIdField =
            typeof(BattleSandboxController).GetField("selectedTargetId", InstanceFlags);

        private static readonly FieldInfo DetailsOpenedStateField =
            typeof(SandboxFighterDetailsView).GetField("openedState", InstanceFlags);
        private static readonly FieldInfo DetailsAttackValueField =
            typeof(SandboxFighterDetailsView).GetField("attackValue", InstanceFlags);
        private static readonly FieldInfo DetailsDefenseValueField =
            typeof(SandboxFighterDetailsView).GetField("defenseValue", InstanceFlags);
        private static readonly FieldInfo DetailsRangeValueField =
            typeof(SandboxFighterDetailsView).GetField("rangeValue", InstanceFlags);
        private static readonly FieldInfo DetailsStatTooltipField =
            typeof(SandboxFighterDetailsView).GetField("statTooltip", InstanceFlags);
        private static readonly FieldInfo DetailsTooltipTitleField =
            typeof(SandboxFighterDetailsView).GetField("tooltipTitle", InstanceFlags);
        private static readonly FieldInfo DetailsTooltipTextField =
            typeof(SandboxFighterDetailsView).GetField("tooltipText", InstanceFlags);

        private BattleSandboxController controller;
        private Button registeredGuardButton;
        private VisualElement registeredRangeRow;
        private VisualElement hoverPopup;
        private Label hoverPopupTitle;
        private Label hoverPopupText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != "BattleSandbox")
                return;

            if (FindFirstObjectByType<BattleSandboxDefenseUiRefiner>() != null)
                return;

            GameObject host = new GameObject("BattleSandboxDefenseUiRefiner");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<BattleSandboxDefenseUiRefiner>();
        }

        private void LateUpdate()
        {
            if (SceneManager.GetActiveScene().name != "BattleSandbox")
            {
                Destroy(gameObject);
                return;
            }

            if (controller == null)
                controller = FindFirstObjectByType<BattleSandboxController>();
            if (controller == null || BattleField == null)
                return;

            SandboxBattle battle = BattleField.GetValue(controller) as SandboxBattle;
            if (battle == null)
                return;

            RefreshGuardButton();
            RefreshCurrentUnitStats(battle);
            RefreshOpenedFighterDetails(battle);
            EnsureRangeHover();
        }

        private void OnDisable()
        {
            HideHoverPopup();
        }

        private void RefreshGuardButton()
        {
            Button button = GuardButtonField != null
                ? GuardButtonField.GetValue(controller) as Button
                : null;
            if (button == null)
                return;

            button.text = "ЗАЩИТНАЯ СТОЙКА";
            button.tooltip = string.Empty;
            button.style.height = 36f;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.fontSize = 11f;

            if (registeredGuardButton == button)
                return;

            registeredGuardButton = button;
            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                ShowHoverPopup(
                    button,
                    "ЗАЩИТНАЯ СТОЙКА",
                    "+50% к защите после всех модификаторов.\n" +
                    "Округление вниз, минимум +1.");
            });
            button.RegisterCallback<PointerLeaveEvent>(_ => HideHoverPopup());
        }

        private void RefreshCurrentUnitStats(SandboxBattle battle)
        {
            if (CurrentStatsLabelField == null)
                return;

            Label label = CurrentStatsLabelField.GetValue(controller) as Label;
            SandboxUnitState current = battle.CurrentUnit;
            if (label == null || current == null)
                return;

            SandboxUnitState selectedTarget = GetSelectedTarget(battle);
            SandboxAttackSnapshot attack =
                SandboxCombatStatPresentation.GetAttackSnapshot(current, selectedTarget);
            SandboxDefenseSnapshot defense =
                SandboxDefensePresentation.GetSnapshot(battle, current);
            SandboxRangeSnapshot range =
                SandboxCombatStatPresentation.GetRangeSnapshot(current);

            label.enableRichText = true;
            label.text =
                current.Definition.RoleLabel + "\n" +
                "HP " + current.HitPoints + "/" + current.MaxHitPoints +
                "  ·  ОД " + current.ActionPoints + "/" + SandboxUnitState.ActionsPerActivation + "\n" +
                "АТК " + FormatStat(attack.EffectiveAttack.ToString("0.##"), attack.HasPositiveBonus) +
                "  ·  ЗАЩ " + FormatStat(defense.EffectiveDefense.ToString(), defense.EffectiveDefense > defense.BaseDefense) +
                "  ·  УРОН " + current.Damage + "\n" +
                "ДАЛ " + FormatStat(range.EffectiveRange.ToString(), range.HasBonus) +
                "  ·  ДВИЖ " + current.RemainingMovement + "/" + current.Movement +
                "  ·  ИНИЦ " + current.Initiative + "\n" +
                "ОТВЕТНЫЙ УДАР: " + (current.CanRetaliate ? "ГОТОВ" : "ПОТРАЧЕН");
        }

        private void RefreshOpenedFighterDetails(SandboxBattle battle)
        {
            if (FighterDetailsViewField == null || DetailsOpenedStateField == null)
                return;

            SandboxFighterDetailsView details =
                FighterDetailsViewField.GetValue(controller) as SandboxFighterDetailsView;
            if (details == null)
                return;

            SandboxUnitState state =
                DetailsOpenedStateField.GetValue(details) as SandboxUnitState;
            if (state == null)
                return;

            SandboxUnitState target = state == battle.CurrentUnit
                ? GetSelectedTarget(battle)
                : null;
            SandboxAttackSnapshot attack =
                SandboxCombatStatPresentation.GetAttackSnapshot(state, target);
            SandboxDefenseSnapshot defense =
                SandboxDefensePresentation.GetSnapshot(battle, state);
            SandboxRangeSnapshot range =
                SandboxCombatStatPresentation.GetRangeSnapshot(state);

            Label attackValue = GetDetailsLabel(details, DetailsAttackValueField);
            Label defenseValue = GetDetailsLabel(details, DetailsDefenseValueField);
            Label rangeValue = GetDetailsLabel(details, DetailsRangeValueField);

            if (attackValue != null)
            {
                attackValue.text = attack.EffectiveAttack.ToString("0.##");
                attackValue.style.color = attack.HasPositiveBonus ? BonusStatColor : NormalStatColor;
                attackValue.tooltip = attack.BuildBreakdown();
            }

            if (defenseValue != null)
            {
                defenseValue.text = defense.EffectiveDefense.ToString();
                defenseValue.style.color = defense.EffectiveDefense > defense.BaseDefense
                    ? BonusStatColor
                    : NormalStatColor;
                defenseValue.tooltip = BuildDefenseBreakdown(defense);
            }

            if (rangeValue != null)
            {
                rangeValue.text = range.EffectiveRange.ToString();
                rangeValue.style.color = range.HasBonus ? BonusStatColor : NormalStatColor;
                rangeValue.tooltip = string.Empty;
            }

            RefreshSharedTooltip(details, attack, defense, range);
        }

        private void EnsureRangeHover()
        {
            if (FighterDetailsViewField == null ||
                DetailsOpenedStateField == null ||
                DetailsRangeValueField == null)
            {
                return;
            }

            SandboxFighterDetailsView details =
                FighterDetailsViewField.GetValue(controller) as SandboxFighterDetailsView;
            Label rangeValue = details != null
                ? GetDetailsLabel(details, DetailsRangeValueField)
                : null;
            VisualElement row = rangeValue != null ? rangeValue.parent : null;
            if (row == null || registeredRangeRow == row)
                return;

            registeredRangeRow = row;
            row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                SandboxFighterDetailsView currentDetails =
                    FighterDetailsViewField.GetValue(controller) as SandboxFighterDetailsView;
                SandboxUnitState state = currentDetails != null
                    ? DetailsOpenedStateField.GetValue(currentDetails) as SandboxUnitState
                    : null;
                if (state == null)
                    return;

                VisualElement builtInTooltip = DetailsStatTooltipField != null
                    ? DetailsStatTooltipField.GetValue(currentDetails) as VisualElement
                    : null;
                if (builtInTooltip != null)
                    builtInTooltip.style.display = DisplayStyle.None;

                SandboxRangeSnapshot range =
                    SandboxCombatStatPresentation.GetRangeSnapshot(state);
                ShowHoverPopup(
                    row,
                    "ДАЛЬНОСТЬ",
                    "Максимальная гексовая дистанция обычной атаки.\n" +
                    "Для бойца с тегом «Дальний бой» холм даёт +1 к дальности.\n\n" +
                    range.BuildBreakdown());
            });
            row.RegisterCallback<PointerLeaveEvent>(_ => HideHoverPopup());
        }

        private void RefreshSharedTooltip(
            SandboxFighterDetailsView details,
            SandboxAttackSnapshot attack,
            SandboxDefenseSnapshot defense,
            SandboxRangeSnapshot range)
        {
            Label tooltipTitle = GetDetailsLabel(details, DetailsTooltipTitleField);
            Label tooltipText = GetDetailsLabel(details, DetailsTooltipTextField);
            if (tooltipTitle == null || tooltipText == null)
                return;

            if (tooltipTitle.text == "АТАКА")
            {
                tooltipText.text =
                    "Каждый пункт Атаки выше Защиты врага увеличивает базовый урон на 25%.\n" +
                    "Максимальный множитель урона — ×5.\n\n" +
                    attack.BuildBreakdown();
            }
            else if (tooltipTitle.text == "ЗАЩИТА")
            {
                tooltipText.text =
                    "Каждый пункт Защиты выше Атаки врага уменьшает базовый урон на 12,5%.\n" +
                    "Максимальное снижение урона — 70%.\n\n" +
                    BuildDefenseBreakdown(defense);
            }
            else if (tooltipTitle.text == "ДАЛЬНОСТЬ")
            {
                tooltipText.text =
                    "Максимальная гексовая дистанция обычной атаки.\n" +
                    "Для бойца с тегом «Дальний бой» холм даёт +1 к дальности.\n\n" +
                    range.BuildBreakdown();
            }
        }

        private void ShowHoverPopup(VisualElement anchor, string title, string text)
        {
            VisualElement root = RootField != null
                ? RootField.GetValue(controller) as VisualElement
                : null;
            if (root == null || anchor == null)
                return;

            EnsureHoverPopup(root);
            hoverPopupTitle.text = title;
            hoverPopupText.text = text;
            hoverPopup.style.display = DisplayStyle.Flex;
            hoverPopup.BringToFront();

            Rect bounds = anchor.worldBound;
            Vector2 topRight = root.WorldToLocal(new Vector2(bounds.xMax, bounds.yMin));
            Vector2 topLeft = root.WorldToLocal(new Vector2(bounds.xMin, bounds.yMin));
            float rootWidth = root.resolvedStyle.width;
            float rootHeight = root.resolvedStyle.height;
            if (float.IsNaN(rootWidth) || rootWidth < 400f)
                rootWidth = 1280f;
            if (float.IsNaN(rootHeight) || rootHeight < 300f)
                rootHeight = 720f;

            const float popupWidth = 330f;
            float left = topRight.x + 10f;
            if (left + popupWidth > rootWidth - 12f)
                left = topLeft.x - popupWidth - 10f;

            hoverPopup.style.left = Mathf.Clamp(
                left,
                12f,
                Mathf.Max(12f, rootWidth - popupWidth - 12f));
            hoverPopup.style.top = Mathf.Clamp(
                topRight.y,
                12f,
                Mathf.Max(12f, rootHeight - 170f));
        }

        private void EnsureHoverPopup(VisualElement root)
        {
            if (hoverPopup != null && hoverPopup.parent == root)
                return;

            hoverPopup = new VisualElement
            {
                name = "battle-sandbox-hover-help",
                pickingMode = PickingMode.Ignore
            };
            hoverPopup.style.display = DisplayStyle.None;
            hoverPopup.style.position = Position.Absolute;
            hoverPopup.style.width = 330f;
            hoverPopup.style.paddingLeft = 12f;
            hoverPopup.style.paddingRight = 12f;
            hoverPopup.style.paddingTop = 10f;
            hoverPopup.style.paddingBottom = 10f;
            hoverPopup.style.backgroundColor = new Color(0.055f, 0.065f, 0.075f, 0.995f);
            SetBorder(hoverPopup, new Color(0.58f, 0.47f, 0.26f, 1f));
            SetRadius(hoverPopup, 4f);

            hoverPopupTitle = new Label();
            hoverPopupTitle.style.fontSize = 11f;
            hoverPopupTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            hoverPopupTitle.style.color = new Color(0.91f, 0.76f, 0.43f, 1f);
            hoverPopup.Add(hoverPopupTitle);

            hoverPopupText = new Label();
            hoverPopupText.style.marginTop = 5f;
            hoverPopupText.style.fontSize = 10f;
            hoverPopupText.style.whiteSpace = WhiteSpace.Normal;
            hoverPopupText.style.color = new Color(0.76f, 0.76f, 0.72f, 1f);
            hoverPopup.Add(hoverPopupText);

            root.Add(hoverPopup);
        }

        private void HideHoverPopup()
        {
            if (hoverPopup != null)
                hoverPopup.style.display = DisplayStyle.None;
        }

        private SandboxUnitState GetSelectedTarget(SandboxBattle battle)
        {
            if (SelectedTargetIdField == null)
                return null;

            string targetId = SelectedTargetIdField.GetValue(controller) as string;
            return string.IsNullOrEmpty(targetId) ? null : battle.GetUnit(targetId);
        }

        private static Label GetDetailsLabel(
            SandboxFighterDetailsView details,
            FieldInfo field)
        {
            return field != null ? field.GetValue(details) as Label : null;
        }

        private static string BuildDefenseBreakdown(SandboxDefenseSnapshot defense)
        {
            string result = defense.BaseDefense + "  база";
            if (defense.ArmorBonus > 0)
                result += "\n+" + defense.ArmorBonus + "  броня";
            if (defense.HillBonus > 0)
                result += "\n+" + defense.HillBonus + "  расположение: холм";
            if (defense.GuardBonus > 0)
                result += "\n+" + defense.GuardBonus + "  защитная стойка";
            result += "\n=" + defense.EffectiveDefense + "  итог";
            return result;
        }

        private static string FormatStat(string value, bool boosted)
        {
            return boosted
                ? "<color=" + BonusColorHex + ">" + value + "</color>"
                : value;
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
