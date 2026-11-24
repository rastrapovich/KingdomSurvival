using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Переключатель подсветки бойцов контуром (HexBoardElement.HighlightAll).
    // Выбор общий для всех боёв и запоминается между запусками; бойцы в
    // лесу подсвечены всегда, независимо от переключателя.
    public sealed partial class BattleSandboxController
    {
        private const string HighlightPrefKey = "KingdomSurvival.Battle.HighlightAllFighters";
        private static bool? highlightAllFighters;
        private Button highlightButton;

        internal static bool HighlightAllFighters
        {
            get
            {
                if (!highlightAllFighters.HasValue)
                    highlightAllFighters = PlayerPrefs.GetInt(HighlightPrefKey, 0) == 1;
                return highlightAllFighters.Value;
            }
            set
            {
                highlightAllFighters = value;
                PlayerPrefs.SetInt(HighlightPrefKey, value ? 1 : 0);
            }
        }

        private void ToggleFighterHighlight()
        {
            HighlightAllFighters = !HighlightAllFighters;
            if (board != null)
                board.HighlightAll = HighlightAllFighters;
            RefreshHighlightButton();
        }

        private void RefreshHighlightButton()
        {
            if (highlightButton != null)
                highlightButton.text = HighlightAllFighters ? "ПОДСВЕТКА: ВСЕ" : "ПОДСВЕТКА: ТОЛЬКО В ЛЕСУ";
        }
    }
}
