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

        private static readonly FieldInfo BattleField =
            typeof(BattleSandboxController).GetField("battle", InstanceFlags);
        private static readonly FieldInfo CurrentStatsLabelField =
            typeof(BattleSandboxController).GetField("currentStatsLabel", InstanceFlags);
        private static readonly FieldInfo FighterDetailsViewField =
            typeof(BattleSandboxController).GetField("fighterDetailsView", InstanceFlags);

        private static readonly FieldInfo DetailsOpenedStateField =
            typeof(SandboxFighterDetailsView).GetField("openedState", InstanceFlags);
        private static readonly FieldInfo DetailsDefenseValueField =
            typeof(SandboxFighterDetailsView).GetField("defenseValue", InstanceFlags);
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

            RefreshCurrentUnitStats(battle);
            RefreshOpenedFighterDetails(battle);
        }

        private void RefreshCurrentUnitStats(SandboxBattle battle)
        {
            if (CurrentStatsLabelField == null)
                return;

            Label label = CurrentStatsLabelField.GetValue(controller) as Label;
            SandboxUnitState current = battle.CurrentUnit;
            if (label == null || current == null)
                return;

            SandboxDefenseSnapshot defense =
                SandboxDefensePresentation.GetSnapshot(battle, current);
            label.text =
                current.Definition.RoleLabel + "\n" +
                "HP " + current.HitPoints + "/" + current.MaxHitPoints +
                "  ·  ОД " + current.ActionPoints + "/" + SandboxUnitState.ActionsPerActivation + "\n" +
                "АТК " + current.Attack + "  ·  ЗАЩ " + defense.EffectiveDefense +
                "  ·  УРОН " + current.Damage + "\n" +
                "ЗАЩИТА: " + defense.BuildCompactBreakdown() + "\n" +
                "ДВИЖ " + current.RemainingMovement + "/" + current.Movement +
                "  ·  ИНИЦ " + current.Initiative + "\n" +
                "ОТВЕТНЫЙ УДАР: " + (current.CanRetaliate ? "ГОТОВ" : "ПОТРАЧЕН");
        }

        private void RefreshOpenedFighterDetails(SandboxBattle battle)
        {
            if (FighterDetailsViewField == null ||
                DetailsOpenedStateField == null ||
                DetailsDefenseValueField == null)
            {
                return;
            }

            SandboxFighterDetailsView details =
                FighterDetailsViewField.GetValue(controller) as SandboxFighterDetailsView;
            if (details == null)
                return;

            SandboxUnitState state =
                DetailsOpenedStateField.GetValue(details) as SandboxUnitState;
            Label defenseValue =
                DetailsDefenseValueField.GetValue(details) as Label;
            if (state == null || defenseValue == null)
                return;

            SandboxDefenseSnapshot defense =
                SandboxDefensePresentation.GetSnapshot(battle, state);
            defenseValue.text = defense.EffectiveDefense.ToString();
            defenseValue.tooltip = defense.BuildCompactBreakdown();

            Label tooltipTitle = DetailsTooltipTitleField != null
                ? DetailsTooltipTitleField.GetValue(details) as Label
                : null;
            Label tooltipText = DetailsTooltipTextField != null
                ? DetailsTooltipTextField.GetValue(details) as Label
                : null;
            if (tooltipTitle != null && tooltipText != null && tooltipTitle.text == "ЗАЩИТА")
            {
                tooltipText.text =
                    "Эффективная защита используется в расчёте входящего урона. " +
                    defense.BuildCompactBreakdown() + ". Защитная стойка добавляет 50% от защиты после брони и холма, " +
                    "бонус округляется вниз и не может быть меньше +1.";
            }
        }
    }
}
