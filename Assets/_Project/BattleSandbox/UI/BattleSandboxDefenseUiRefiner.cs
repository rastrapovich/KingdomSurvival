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
        private static readonly FieldInfo DetailsTooltipTitleField =
            typeof(SandboxFighterDetailsView).GetField("tooltipTitle", InstanceFlags);
        private static readonly FieldInfo DetailsTooltipTextField =
            typeof(SandboxFighterDetailsView).GetField("tooltipText", InstanceFlags);

        private BattleSandboxController controller;

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
        }

        private void RefreshGuardButton()
        {
            Button button = GuardButtonField != null
                ? GuardButtonField.GetValue(controller) as Button
                : null;
            if (button == null)
                return;

            button.text =
                "ЗАЩИТНАЯ СТОЙКА\n" +
                "+50% к защите после всех модификаторов.\n" +
                "Округление вниз, минимум +1.";
            button.tooltip = string.Empty;
            button.style.height = 72f;
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.fontSize = 10f;
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
                rangeValue.tooltip = range.BuildBreakdown();
            }

            RefreshSharedTooltip(details, attack, defense, range);
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
                    "Боец с тегом «Дальний бой» получает +1 к дальности, пока стоит на холме.\n\n" +
                    range.BuildBreakdown();
            }
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
    }
}
