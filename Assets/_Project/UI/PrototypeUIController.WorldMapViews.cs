using KingdomSurvival.MapLabBridge;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

// Виды карты (17.11.2026): кнопка «Вид: …» на экране карты переключает облик
// по кругу — тот же мир, другая «шкура» (MapLabMapViews). Цветокоррекция
// вида применяется живой картой при следующем кадре без пересборки; если у
// вида своя палитра или свои рисунки — живая карта пересобирается («Рисуем
// карту…»). Выбор — настройка показа (не данные партии), его видит и окно
// «Генератор карты»: смена вида там во время Play Mode тоже подхватывается.
public partial class PrototypeUIController
{
    private Button worldMapViewButton;
    private string worldMapViewShownId;

    private void RegisterWorldMapViewButton()
    {
        if (worldMapViewButton == null)
            return;
        worldMapViewButton.clicked += OnWorldMapViewButtonClicked;
        UpdateWorldMapViewButton();
    }

    private void UnregisterWorldMapViewButton()
    {
        if (worldMapViewButton != null)
            worldMapViewButton.clicked -= OnWorldMapViewButtonClicked;
    }

    private void OnWorldMapViewButtonClicked()
    {
        MapLabGameViews.Cycle();
        ApplyWorldMapViewIfChanged();
    }

    private void UpdateWorldMapViewButton()
    {
        if (worldMapViewButton == null)
            return;
        int count = MapLabGameViews.Count;
        worldMapViewButton.style.display = count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        worldMapViewButton.text = "Вид: " + MapLabGameViews.ActiveName;
        string note = MapLabGameViews.ActiveNote;
        worldMapViewButton.tooltip = "Облик карты — нажмите, чтобы сменить" + (string.IsNullOrEmpty(note) ? "." : ".\n" + note);
    }

    // Каждый кадр экрана карты: выбранный вид сменился (кнопкой или в окне
    // генератора) — туман, фон, подписи и при необходимости живая карта.
    private void ApplyWorldMapViewIfChanged()
    {
        string id = MapLabGameViews.ActiveId;
        if (id == worldMapViewShownId)
            return;
        bool first = worldMapViewShownId == null;
        worldMapViewShownId = id;
        UpdateWorldMapViewButton();
        if (worldMapLiveImage != null)
            worldMapLiveImage.style.backgroundColor = MapLabGameViews.Background(new Color(0.86f, 0.82f, 0.72f));
        Color fog = WorldMapViewFogColor();
        fog.a = 1f;
        foreach (VisualElement m in worldMapLiveMargins)
            if (m != null)
                m.style.backgroundColor = fog;
        // Туман войны — перерисовать новым цветом.
        worldMapFogVersion = -1;
        TickWorldMapFog(true);
        if (first)
            return;
        // Своя палитра или свои рисунки вида — пересобрать живую карту.
        if (WorldMapLiveWanted() && MapLabGameViews.LiveNeedsRebuild && gameState != null && gameState.HasGeneratedMap)
        {
            worldMapMapLab = null;
            ScheduleWorldMapLiveBuild();
        }
        RefreshWorldMapPanel();
    }

    // Цвет тумана войны: вид может задать свой (сепия — бурый, мрачный — почти чёрный).
    private static Color WorldMapViewFogColor()
    {
        if (MapLabGameViews.TryFogColor(out Color color))
            return color;
        WorldMapMovementSettingsAsset settings = WorldMapVisualRuntime.LoadMovementSettings();
        return settings != null ? settings.FogColor : new Color(0.055f, 0.06f, 0.07f, 0.86f);
    }
}
