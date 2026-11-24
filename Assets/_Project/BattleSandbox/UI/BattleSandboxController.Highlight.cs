using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Маленькие галочки вида поля под строкой «Раунд N»:
    //   «Подсветка» — еле заметный контур у всех бойцов (свои — жёлтый,
    //   противники — красный); в лесу боец подсвечен всегда.
    //   «Сетка» — все клетки поля выделены контуром, особые — цветом, как
    //   «Показать логические клетки» в отладке местности.
    // Выбор общий для всех боёв и запоминается между запусками.
    public sealed partial class BattleSandboxController
    {
        private const string HighlightPrefKey = "KingdomSurvival.Battle.HighlightAllFighters";
        private const string GridPrefKey = "KingdomSurvival.Battle.ShowAllCells";

        internal static bool HighlightAllFighters
        {
            get => PlayerPrefs.GetInt(HighlightPrefKey, 0) == 1;
            set => PlayerPrefs.SetInt(HighlightPrefKey, value ? 1 : 0);
        }

        internal static bool ShowAllCells
        {
            get => PlayerPrefs.GetInt(GridPrefKey, 0) == 1;
            set => PlayerPrefs.SetInt(GridPrefKey, value ? 1 : 0);
        }

        private VisualElement BuildBoardViewToggles()
        {
            VisualElement row = new VisualElement { name = "sandbox-board-view-toggles" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3f;
            row.Add(SmallToggle("sandbox-highlight-toggle", "Подсветка", HighlightAllFighters,
                "Еле заметный контур у всех бойцов: свои — жёлтым, противники — красным. В лесу подсветка включена всегда.",
                value => { HighlightAllFighters = value; ApplyBoardViewToggles(); }));
            row.Add(SmallToggle("sandbox-grid-toggle", "Сетка", ShowAllCells,
                "Выделить все клетки поля: обычные — светлым контуром, особая местность — жёлтым, опасность — оранжевым, закрытые — красным.",
                value => { ShowAllCells = value; ApplyBoardViewToggles(); }));
            return row;
        }

        private static Toggle SmallToggle(string name, string label, bool value, string tooltip, System.Action<bool> changed)
        {
            Toggle toggle = new Toggle(label) { name = name, value = value, tooltip = tooltip };
            toggle.style.marginLeft = 0f;
            toggle.style.marginRight = 12f;
            toggle.style.marginTop = 0f;
            toggle.style.marginBottom = 0f;
            toggle.style.fontSize = 10f;
            toggle.style.color = new Color(0.62f, 0.61f, 0.56f, 1f);
            Label text = toggle.Q<Label>();
            if (text != null)
            {
                text.style.minWidth = 0f;
                text.style.marginRight = 4f;
                text.style.fontSize = 10f;
                text.style.color = new Color(0.62f, 0.61f, 0.56f, 1f);
            }
            VisualElement check = toggle.Q(className: Toggle.checkmarkUssClassName);
            if (check != null)
            {
                check.style.width = 11f;
                check.style.height = 11f;
            }
            toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
            return toggle;
        }

        private void ApplyBoardViewToggles()
        {
            if (board == null)
                return;
            board.HighlightAll = HighlightAllFighters;
            board.ShowAllCells = ShowAllCells;
        }
    }
}
