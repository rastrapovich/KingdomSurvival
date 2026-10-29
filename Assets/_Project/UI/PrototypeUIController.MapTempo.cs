using UnityEngine.UIElements;

// Бриф правок (22 пункта) п. 18: темп ×1/×2/×3 рядом с часами. Темп
// ускоряет только воспроизведение — те же игровые часы и припасы за маршрут
// проходят за меньшее число реальных секунд: множится время симуляции кадра,
// а не цена пути. Сам темп часы не запускает: идут они, как и прежде, только
// пока герой идёт или занят делом (RefreshAutoTimeState). Дискретные шаги
// поселения (разговор, сделка) и бой темп не меняет; глобальный
// Time.timeScale не трогается.
public partial class PrototypeUIController
{
    private static readonly int[] MapTempoSteps = { 1, 2, 3 };

    private int mapTempo = 1;
    private VisualElement mapTempoBar;
    private readonly Button[] mapTempoButtons = new Button[MapTempoSteps.Length];

    private int MapTempo => mapTempo;

    private void EnsureMapTempoBar()
    {
        if (mapTempoBar != null || dayLabel == null || dayLabel.parent == null)
            return;

        mapTempoBar = new VisualElement { name = "map-tempo-bar" };
        mapTempoBar.AddToClassList("map-tempo-bar");
        for (int i = 0; i < MapTempoSteps.Length; i++)
        {
            int tempo = MapTempoSteps[i];
            Button button = new Button(() => SetMapTempo(tempo))
            {
                name = "map-tempo-" + tempo,
                text = "×" + tempo,
                tooltip = tempo == 1
                    ? "Обычный темп."
                    : "Ускорить ход и долгие дела в " + tempo + " раза. Игровое время и припасы — те же."
            };
            button.AddToClassList("map-tempo-button");
            mapTempoButtons[i] = button;
            mapTempoBar.Add(button);
        }
        dayLabel.parent.Insert(dayLabel.parent.IndexOf(dayLabel) + 1, mapTempoBar);
        RefreshMapTempoBar();
    }

    private void SetMapTempo(int tempo)
    {
        mapTempo = System.Array.IndexOf(MapTempoSteps, tempo) >= 0 ? tempo : 1;
        RefreshMapTempoBar();
    }

    private void RefreshMapTempoBar()
    {
        for (int i = 0; i < MapTempoSteps.Length; i++)
            mapTempoButtons[i]?.EnableInClassList("map-tempo-button--active", MapTempoSteps[i] == mapTempo);
    }
}
