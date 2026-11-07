using System.Collections.Generic;
using KingdomSurvival.MapLabBridge;
using UnityEngine;
using UnityEngine.UIElements;

// 12У-F/G: живая карта MapLab на экране карты. Мир MapLab плана партии
// строится один раз (MapLabGameLive), и каждый кадр камера MapLab рисует
// видимую часть карты в экранном разрешении — тот же рисунок, что в
// предпросмотре генератора. Рисунок лежит позади полотна карты (значки,
// туман, герой, области поиска — поверх него в тех же процентах). Без
// ресурсов MapLab, в пакетном режиме или при ошибке — прежний рисунок карты.
public partial class PrototypeUIController
{
    private VisualElement worldMapLiveImage;
    private Label worldMapLiveLabel;
    private bool worldMapLiveScheduled;
    private bool worldMapLiveFailed;
    private RenderTexture worldMapLiveShown;
    private MapLabGameLive.World worldMapLiveBuilding;
    private bool WorldMapLivePending => worldMapLiveScheduled || worldMapLiveBuilding != null;

    private bool WorldMapLiveWanted() =>
        !worldMapLiveFailed && gameState != null && gameState.HasGeneratedMap && MapLabGameMap.Enabled;

    private void EnsureWorldMapLiveUi()
    {
        if (worldMapViewport == null || worldMapLiveImage != null)
            return;
        worldMapLiveImage = new VisualElement { name = "world-map-maplab-live", pickingMode = PickingMode.Ignore };
        worldMapLiveImage.style.position = Position.Absolute;
        worldMapLiveImage.style.left = 0;
        worldMapLiveImage.style.top = 0;
        worldMapLiveImage.style.right = 0;
        worldMapLiveImage.style.bottom = 0;
        worldMapLiveImage.style.backgroundColor = new Color(0.86f, 0.82f, 0.72f);
        worldMapViewport.Insert(0, worldMapLiveImage);
        worldMapLiveLabel = new Label("Рисуем карту…") { pickingMode = PickingMode.Ignore };
        worldMapLiveLabel.style.position = Position.Absolute;
        worldMapLiveLabel.style.left = Length.Percent(50);
        worldMapLiveLabel.style.top = Length.Percent(45);
        worldMapLiveLabel.style.translate = new Translate(Length.Percent(-50), 0);
        worldMapLiveLabel.style.fontSize = 22;
        worldMapLiveLabel.style.color = new Color(0.3f, 0.24f, 0.16f);
        worldMapLiveLabel.style.display = DisplayStyle.None;
        worldMapViewport.Add(worldMapLiveLabel);
    }

    private void ScheduleWorldMapLiveBuild()
    {
        EnsureWorldMapLiveUi();
        if (MapLabGameLive.Current != null && MapLabGameLive.Matches(gameState.GeneratedMap, MapLabGameMap.ServicesKey(MapLabSettlementServices())) && MapLabGameLive.Current.Ready)
        {
            worldMapMapLab = LiveResult(MapLabGameLive.Current);
            return;
        }
        worldMapMapLab = null;
        if (worldMapLiveScheduled || worldMapViewport == null)
            return;
        worldMapLiveScheduled = true;
        if (worldMapLiveLabel != null)
        {
            worldMapLiveLabel.text = "Рисуем карту…";
            worldMapLiveLabel.style.display = DisplayStyle.Flex;
        }
        worldMapViewport.schedule.Execute(BuildWorldMapLive).ExecuteLater(60);
    }

    // Сборка мира MapLab — пошаговая: расчёты идут в фоне, готовые части
    // создаются раз в кадр (TickWorldMapLive), игра не замирает.
    private void BuildWorldMapLive()
    {
        worldMapLiveScheduled = false;
        if (gameState == null || !gameState.HasGeneratedMap)
            return;
        worldMapLiveBuilding = MapLabGameLive.Begin(gameState.GeneratedMap, MapLabSettlementServices());
        PollWorldMapLiveBuild();
    }

    private void PollWorldMapLiveBuild()
    {
        MapLabGameLive.World world = worldMapLiveBuilding;
        if (world == null)
            return;
        // Партия сменилась во время сборки — эту сборку уже сняли.
        if (!ReferenceEquals(world, MapLabGameLive.Current))
        {
            worldMapLiveBuilding = null;
            return;
        }
        if (!MapLabGameLive.Advance(world))
        {
            if (worldMapLiveLabel != null)
            {
                string stage = world.Stage;
                worldMapLiveLabel.text = string.IsNullOrEmpty(stage) ? "Рисуем карту…" : "Рисуем карту: " + stage + "…";
                worldMapLiveLabel.style.display = DisplayStyle.Flex;
            }
            return;
        }
        worldMapLiveBuilding = null;
        if (worldMapLiveLabel != null)
            worldMapLiveLabel.style.display = DisplayStyle.None;
        if (world == null || !world.Ready)
        {
            // Живой рисунок не получился — прежний рисовальщик и прежнее полотно.
            worldMapLiveFailed = true;
            worldMapMapLab = null;
            if (world != null && !string.IsNullOrEmpty(world.Error))
                AddReport("Карта MapLab не построена: " + world.Error);
            ConfigureWorldMapCanvasSize();
            RefreshWorldMapPanel();
            return;
        }
        worldMapMapLab = LiveResult(world);
        RecalculateWorldMapMaxZoom();
        RefreshWorldMapPanel();
    }

    // Совместимость с кодом зданий и групп: те же поля, что у снимка.
    private static MapLabGameMap.Result LiveResult(MapLabGameLive.World world)
    {
        MapLabGameMap.Result result = new MapLabGameMap.Result
        {
            HeightScale = world.Sin,
            Pitch = world.Pitch,
            // Живой рисунок не ограничивает приближение.
            PixelWidth = 1 << 24,
            PixelHeight = 1 << 24,
        };
        foreach ((string id, string role, Rect rect, Vector2 _) in world.Buildings)
            result.Buildings.Add((id, role, rect));
        return result;
    }

    // Каждый кадр: камера MapLab рисует видимую часть полотна.
    private void TickWorldMapLive()
    {
        PollWorldMapLiveBuild();
        MapLabGameLive.World world = MapLabGameLive.Current;
        bool active = worldMapMapLab != null && world != null && world.Ready && gameState != null && gameState.HasGeneratedMap
                      && ReferenceEquals(world.Plan, gameState.GeneratedMap) && IsWorldMapScreenOpen;
        if (worldMapLiveImage != null)
            worldMapLiveImage.style.display = worldMapMapLab != null || worldMapLiveScheduled || worldMapLiveBuilding != null ? DisplayStyle.Flex : DisplayStyle.None;
        if (!active || worldMapViewport == null)
            return;
        float width = worldMapViewport.resolvedStyle.width, height = worldMapViewport.resolvedStyle.height;
        if (float.IsNaN(width) || float.IsNaN(height) || width < 8f || height < 8f)
            return;
        Vector2 a = MapPercentToWorldMapViewport(0f, 0f), b = MapPercentToWorldMapViewport(100f, 100f);
        float spanX = Mathf.Max(0.001f, b.x - a.x), spanY = Mathf.Max(0.001f, b.y - a.y);
        Rect visible = Rect.MinMaxRect(-a.x / spanX * 100f, -a.y / spanY * 100f, (width - a.x) / spanX * 100f, (height - a.y) / spanY * 100f);
        float scale = worldMapViewport.panel != null ? worldMapViewport.panel.scaledPixelsPerPoint : 1f;
        RenderTexture rt = MapLabGameLive.Render(world, visible, Mathf.RoundToInt(width * scale), Mathf.RoundToInt(height * scale));
        if (rt == null)
            return;
        if (!ReferenceEquals(worldMapLiveShown, rt))
        {
            worldMapLiveImage.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            worldMapLiveImage.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
            worldMapLiveShown = rt;
        }
        worldMapLiveImage.MarkDirtyRepaint();
    }

    // ------------------------------------------------------------------
    // Фигура героя MapLab и ходьба по поселению
    // ------------------------------------------------------------------

    private Vector2 worldMapHeroLocal;
    private bool worldMapHeroLocalActive;
    private string worldMapHeroLocalSettlement;
    private Vector2 worldMapHeroWalkTarget;
    private bool worldMapHeroWalking;
    private System.Action worldMapHeroWalkArrived;
    private int worldMapHeroDirection;
    private Vector2 worldMapHeroLastPercent;
    private float worldMapHeroBob;

    // Скорость ходьбы по поселению — гексов в секунду реального времени.
    private const float WorldMapSettlementWalkHexesPerSecond = 0.55f;

    // Фигура героя MapLab (шесть ракурсов набора фишки). False — живой
    // карты нет, герой рисуется прежним способом.
    private bool TickWorldMapHeroMapLab(float dt, bool running)
    {
        MapLabGameLive.World world = MapLabGameLive.Current;
        if (worldMapMapLab == null || world == null || !world.Ready || worldMapHeroImage == null)
            return false;
        ExpeditionData expedition = gameState.ActiveExpedition;
        Vector2 percent = expedition != null
            ? new Vector2(expedition.CurrentMapXPercent, expedition.CurrentMapYPercent)
            : new Vector2(WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent);

        // Внутри поселения герой ходит по нему сам (к зданиям услуг).
        string inside = SettlementCampService.InsideLocationId(gameState);
        if (!string.IsNullOrEmpty(inside) && SettlementMapMode(inside))
        {
            if (!worldMapHeroLocalActive || worldMapHeroLocalSettlement != inside)
            {
                worldMapHeroLocal = percent;
                worldMapHeroLocalActive = true;
                worldMapHeroLocalSettlement = inside;
                worldMapHeroWalking = false;
            }
            if (worldMapHeroWalking)
            {
                WorldMapHexGrid grid = WorldMapNavigation.Grid;
                float hexPercent = (float)(grid.HexWidth / grid.CanvasWidth * 100.0);
                Vector2 delta = worldMapHeroWalkTarget - worldMapHeroLocal;
                // По высоте полотно сжато наклоном — шаг считаем на земле.
                Vector2 ground = new Vector2(delta.x, delta.y * worldMapCanvasHeight / Mathf.Max(1f, worldMapCanvasWidth) / Mathf.Max(0.2f, worldMapMapLab.HeightScale));
                float step = WorldMapSettlementWalkHexesPerSecond * hexPercent * dt;
                if (ground.magnitude <= step || ground.magnitude < 0.0001f)
                {
                    worldMapHeroLocal = worldMapHeroWalkTarget;
                    worldMapHeroWalking = false;
                    System.Action arrived = worldMapHeroWalkArrived;
                    worldMapHeroWalkArrived = null;
                    arrived?.Invoke();
                }
                else
                {
                    worldMapHeroLocal += delta * (step / ground.magnitude);
                    running = true;
                }
            }
            percent = worldMapHeroLocal;
        }
        else
        {
            worldMapHeroLocalActive = false;
            worldMapHeroWalking = false;
        }

        // Ракурс — по направлению движения (на стоянке — последний).
        Vector2 motion = percent - worldMapHeroLastPercent;
        if (motion.sqrMagnitude > 1e-8f)
            worldMapHeroDirection = KingdomSurvivalDirection(motion.x, motion.y);
        worldMapHeroLastPercent = percent;
        if (!MapLabGameLive.HeroFrame(worldMapHeroDirection, out Texture2D texture, out float worldHeight, out float aspect, out Vector2 pivot))
            return false;

        float zoom = Mathf.Max(0.0001f, worldMapZoom);
        float pxPerUnit = world.CanvasPixelsPerUnit(worldMapCanvasWidth) * zoom;
        float height = worldHeight * pxPerUnit;
        float width = height * aspect;
        // Стоит на нарисованной земле: точка поднимается вместе с рельефом.
        float shift = MapLabGameLive.ElevationShiftPercent(world, percent.x, percent.y);
        Vector2 ground2 = MapPercentToWorldMapViewport(percent.x, percent.y + shift);
        worldMapHeroBob = running ? worldMapHeroBob + dt * 9f : 0f;
        float bob = running ? Mathf.Abs(Mathf.Sin(worldMapHeroBob)) * height * 0.03f : 0f;

        if (worldMapHeroFallback != null && worldMapHeroFallback.style.display != DisplayStyle.None)
            worldMapHeroFallback.style.display = DisplayStyle.None;
        if (worldMapHeroImage.style.display != DisplayStyle.Flex)
            worldMapHeroImage.style.display = DisplayStyle.Flex;
        if (!ReferenceEquals(worldMapHeroImage.image, texture))
        {
            worldMapHeroImage.sprite = null;
            worldMapHeroImage.image = texture;
        }
        if (Mathf.Abs(worldMapHeroAppliedSize.x - width) > 0.01f || Mathf.Abs(worldMapHeroAppliedSize.y - height) > 0.01f)
        {
            worldMapHeroImage.style.width = width;
            worldMapHeroImage.style.height = height;
            worldMapHeroAppliedSize = new Vector2(width, height);
        }
        worldMapHeroImage.style.translate = new Translate(ground2.x - pivot.x * width, ground2.y - (1f - pivot.y) * height - bob);
        return true;
    }

    // Ракурс из шести по направлению на полотне (y вниз — к зрителю), по
    // правилу фишки MapLab: курс 180° — Front, далее через 60°.
    private static int KingdomSurvivalDirection(float dx, float dy) => WorldMapActorMotion.DirectionIndex(dx, dy);

    // Вход в поселение на живой карте: карта наезжает на поселение, чтобы
    // были видны его дома и здания услуг.
    private void FocusWorldMapOnSettlement(LocationData location)
    {
        if (location == null || worldMapViewport == null)
            return;
        float zoom = Mathf.Lerp(worldMapMinZoom, worldMapMaxZoom, 0.6f);
        worldMapZoom = Mathf.Max(worldMapZoom, zoom);
        worldMapZoomTarget = worldMapZoom;
        CenterWorldMapOn(location.MapXPercent, location.MapYPercent);
        ApplyWorldMapViewportTransform();
    }

    // Отряд идёт по поселению к двери здания, затем — дело на месте.
    private void WalkWorldMapHeroTo(Vector2 doorPercent, System.Action arrived)
    {
        if (!worldMapHeroLocalActive)
        {
            arrived?.Invoke();
            return;
        }
        worldMapHeroWalkTarget = doorPercent;
        worldMapHeroWalking = true;
        worldMapHeroWalkArrived = arrived;
    }
}
