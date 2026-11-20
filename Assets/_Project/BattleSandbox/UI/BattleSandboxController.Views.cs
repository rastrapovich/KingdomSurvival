using KingdomSurvival.MapLabBridge;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Вид карты в бою (20.11.2026, просьба пользователя): кнопка «Вид: …» в
    // панели скорости переключает «Обычный / Мрачный / Сепия» по кругу — тот
    // же выбор, что кнопка на глобальной карте (MapLabGameViews), он и
    // сохраняется. Арена перекрашивается сама (MapLabArenaDriver, клетки —
    // HexBoardElement), здесь — рисунки бойцов: поле, очередь хода, карточки.
    // Во время анимации удара или шага рисунки меняются после неё.
    public sealed partial class BattleSandboxController
    {
        private bool battleViewPending;

        private void AddBattleViewButton(VisualElement panel)
        {
            Button button = new Button { name = "battle-view-button" };
            StyleSecondaryButton(button);
            button.style.marginTop = 4f;
            button.style.height = 24f;
            button.style.fontSize = 11f;

            void Show()
            {
                button.text = "ВИД: " + MapLabGameViews.ActiveName.ToUpperInvariant();
                string note = MapLabGameViews.ActiveNote;
                button.tooltip = "Облик поля и бойцов — нажмите, чтобы сменить" + (string.IsNullOrEmpty(note) ? "." : ".\n" + note);
                button.style.display = MapLabGameViews.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            void OnChanged()
            {
                Show();
                OnBattleViewChanged();
            }

            button.clicked += () => MapLabGameViews.Cycle();
            Show();
            MapLabGameViews.Changed += OnChanged;
            button.RegisterCallback<DetachFromPanelEvent>(_ => MapLabGameViews.Changed -= OnChanged);
            panel.Add(button);
        }

        private void OnBattleViewChanged()
        {
            if (unitContent == null || !unitContent.ApplyView(MapLabGameViews.ActiveId))
                return;
            battleViewPending = true;
            if (!combatAnimationRunning)
                RefreshBattleScreen();
        }

        // Из RefreshBattleScreen: новые рисунки — доске (фигуры и анимации
        // строятся заново по рисункам вида), очередь и карточки — следом.
        private void ApplyPendingBattleView()
        {
            if (!battleViewPending || combatAnimationRunning || board == null || unitContent == null)
                return;
            battleViewPending = false;
            board.SetUnitVisuals(unitContent.Visuals, unitContent.AnimationDatabase);
        }
    }
}
