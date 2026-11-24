using System.Collections.Generic;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.MapLabBridge;
using KingdomSurvival.WorldMapVisual;
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
        worldMapLiveImage.style.backgroundColor = WorldMapLiveBackdrop;
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
        // Карта готова: заранее прогреть арену боя (первая сборка в сеансе
        // иначе в 2–3 раза дольше и пришлась бы на начало первого боя).
        MapLabArenaPrewarm.Begin();
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
        {
            foreach (VisualElement m in worldMapLiveMargins)
                if (m != null)
                    m.style.display = DisplayStyle.None;
            return;
        }
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
        PlaceWorldMapLiveMargins(a, b, width, height);
        if (!ReferenceEquals(worldMapLiveShown, rt))
        {
            worldMapLiveImage.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            worldMapLiveImage.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
            worldMapLiveShown = rt;
        }
        worldMapLiveImage.MarkDirtyRepaint();
    }

    // За краем полотна карты рисунок MapLab — незатянутые туманом окраины
    // (светлые полосы при отдалении). Там — цвет тумана, сплошной рамкой.
    private readonly VisualElement[] worldMapLiveMargins = new VisualElement[4];

    private void PlaceWorldMapLiveMargins(Vector2 a, Vector2 b, float width, float height)
    {
        if (worldMapLiveImage == null)
            return;
        if (worldMapLiveMargins[0] == null)
        {
            Color fog = WorldMapFogColor();
            fog.a = 1f;
            int index = worldMapViewport.IndexOf(worldMapLiveImage) + 1;
            for (int i = 0; i < 4; i++)
            {
                VisualElement m = new VisualElement { name = "world-map-live-margin-" + i, pickingMode = PickingMode.Ignore };
                m.style.position = Position.Absolute;
                m.style.backgroundColor = fog;
                worldMapViewport.Insert(index, m);
                worldMapLiveMargins[i] = m;
            }
        }
        float left = Mathf.Clamp(a.x, 0f, width), right = Mathf.Clamp(b.x, 0f, width);
        float top = Mathf.Clamp(a.y, 0f, height), bottom = Mathf.Clamp(b.y, 0f, height);
        void Set(VisualElement m, float x, float y, float w, float h)
        {
            m.style.display = w > 0.5f && h > 0.5f ? DisplayStyle.Flex : DisplayStyle.None;
            m.style.left = x;
            m.style.top = y;
            m.style.width = Mathf.Max(0f, w);
            m.style.height = Mathf.Max(0f, h);
        }
        Set(worldMapLiveMargins[0], 0f, 0f, width, top);
        Set(worldMapLiveMargins[1], 0f, bottom, width, height - bottom);
        Set(worldMapLiveMargins[2], 0f, top, left, bottom - top);
        Set(worldMapLiveMargins[3], right, top, width - right, bottom - top);
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
    // Сектор анимации героя — по шести направлениям боя.
    private HexFacing worldMapHeroAnimationFacing = HexFacing.SouthEast;
    private Vector2 worldMapHeroLastPercent;

    // Скорость ходьбы по поселению — гексов в секунду реального времени.
    // Шаг по поселению — от общей скорости передвижения по карте.
    private static float WorldMapSettlementWalkHexesPerSecond => Mathf.Clamp((float)WorldMapMovementRules.Current.SafeRunSpeedHexesPerSecond * 0.7f, 0.15f, 3f);

    // Фигура героя MapLab (шесть ракурсов набора фишки). False — живой
    // карты нет, герой рисуется прежним способом.
    private bool TickWorldMapHeroMapLab(float dt, bool running)
    {
        MapLabGameLive.World world = MapLabGameLive.Current;
        if (worldMapHeroImage == null)
            return false;
        // Живая карта ещё строится — прежняя анимация героя не показывается
        // (экран загрузки), фигура MapLab появится вместе с картой.
        if (WorldMapLiveWanted() && (worldMapMapLab == null || world == null || !world.Ready))
        {
            worldMapHeroImage.style.display = DisplayStyle.None;
            MapLabLiveFigures.Hide("hero");
            if (worldMapHeroFallback != null)
                worldMapHeroFallback.style.display = DisplayStyle.None;
            return true;
        }
        if (worldMapMapLab == null || world == null || !world.Ready)
        {
            MapLabLiveFigures.Hide("hero");
            return false;
        }
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
            worldMapHeroAnimationFacing = WorldMapAnimationDatabaseAsset.FacingFromScreen(
                new Vector2(motion.x * worldMapCanvasWidth, motion.y * worldMapCanvasHeight), worldMapHeroAnimationFacing);
        worldMapHeroLastPercent = percent;

        // Анимированный набор героя из раздела «Глобальная карта» Базы
        // анимаций: бег — «Ходьба», стоянка — «Ожидание». Без набора (или без
        // шейдера фигур) — false: герой рисуется поверх карты, как без MapLab.
        WorldMapAnimationDatabaseAsset mapAnimations = WorldMapAnimationDatabaseAsset.Current;
        WorldMapAnimationSetData heroSet = mapAnimations != null ? mapAnimations.HeroSet : null;
        if (heroSet != null && TickWorldMapHeroAnimatedMapLab(world, mapAnimations, heroSet, dt, running, percent, motion))
            return true;
        MapLabLiveFigures.Hide("hero");
        return false;
    }

    private readonly WorldMapSetAnimator worldMapHeroMapAnimator = new WorldMapSetAnimator();

    // Герой — анимация набора карты в сцене MapLab. True — фигура поставлена.
    private bool TickWorldMapHeroAnimatedMapLab(
        MapLabGameLive.World world,
        WorldMapAnimationDatabaseAsset database,
        WorldMapAnimationSetData set,
        float dt,
        bool running,
        Vector2 percent,
        Vector2 motion)
    {
        if (!MapLabLiveFigures.Available)
            return false;
        WorldMapHexGrid grid = WorldMapNavigation.Grid;
        float hexPercent = (float)(grid.HexWidth / grid.CanvasWidth * 100.0);
        // Шаг по земле (карта сжата наклоном камеры по высоте).
        float stepHexes = WorldMapSetAnimator.GroundHexes(
            motion,
            worldMapCanvasHeight / Mathf.Max(1f, worldMapCanvasWidth),
            worldMapMapLab != null ? worldMapMapLab.HeightScale : 1f,
            hexPercent);

        CreatureAnimationDirection direction = WorldMapAnimationDatabaseAsset.BattleDirection(worldMapHeroAnimationFacing);
        bool sync = set.SyncCadenceToSpeed;
        Sprite sprite = worldMapHeroMapAnimator.Tick(set, direction, running, dt, stepHexes, sync);
        if (sprite == null || sprite.texture == null)
            return false;

        bool mirrored = worldMapHeroMapAnimator.IsMirrored;
        Vector2 canvas = WorldMapSetAnimator.CanvasOf(set.Animation, sprite);
        float aspect = canvas.x / Mathf.Max(1f, canvas.y);
        // Рост — в ширинах клетки карты; ширина клетки в мире — из процентов.
        float hexWorldWidth = hexPercent / 100f * world.Play.width;
        float worldHeight = set.HeightInHexes * hexWorldWidth;
        Vector2 pivot = WorldMapSetAnimator.CardPivot(set.Animation, worldMapHeroMapAnimator.Clip, mirrored);

        // Анимированная фигура сама качает корпус — подскока (bob) нет.
        if (!MapLabLiveFigures.Place(world, "hero", sprite.texture, WorldMapSetAnimator.FrameUv(sprite, mirrored), FrameOutline(sprite), percent, worldHeight, aspect, pivot, 0f, true))
            return false;

        worldMapHeroImage.style.display = DisplayStyle.None;
        if (worldMapHeroFallback != null)
            worldMapHeroFallback.style.display = DisplayStyle.None;
        return true;
    }

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
