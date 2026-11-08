using System.Collections.Generic;
using KingdomSurvival.MapLabBridge;
using UnityEngine;
using UnityEngine.UIElements;

// 12У-G (запрос пользователя 08.11.2026: «объекты, чтобы с ними можно
// взаимодействовать»): детали мест на живой карте (сети, кладбище, грот,
// навес, сломанная телега...) и группы мира — цели. Наведение подсвечивает
// рисунок (по маске альфы, как у зданий) и называет место; клик — карточка
// с описанием и кнопкой «Идти сюда». Содержания у деталей пока нет: только
// описание места (CONTENT_TODO пакета); новых действий и наград нет.
// Видны цели только на разведанной земле — скрытое не выдаётся.
public partial class PrototypeUIController
{
    private VisualElement worldMapFeatureLayer;
    private MapLabGameLive.World worldMapFeaturesWorld;
    private float worldMapFeatureTimer;
    private readonly List<(GeneratedMapSite Site, List<VisualElement> Targets, Label Label, Rect Union)> worldMapFeatureTargets =
        new List<(GeneratedMapSite, List<VisualElement>, Label, Rect)>();

    // Карточка места или группы.
    private VisualElement worldMapInfoCard;
    private Label worldMapInfoTitle, worldMapInfoText;
    private Button worldMapInfoGo;
    private System.Action worldMapInfoGoAction;

    // ---------- Найденные места: рисунок вместо значка ----------

    private readonly List<(Label Label, LocationData Location, float HeightPx)> worldMapPlacePictures = new List<(Label, LocationData, float)>();

    private void ClearWorldMapPlacePictures()
    {
        foreach ((Label label, LocationData _, float _) in worldMapPlacePictures)
            label.RemoveFromHierarchy();
        worldMapPlacePictures.Clear();
    }

    // Место генератора (не поселение), найденное на живой карте: рисунок
    // MapLab в его точке (видимые места уже нарисованы самим MapLab — там
    // цель даёт деталь места; здесь — те, что были скрытыми). Наведение —
    // подсветка и название, клик — как по значку: отряд идёт к месту.
    private bool TryCreateWorldMapPlacePicture(LocationData location)
    {
        MapLabGameLive.World world = MapLabGameLive.Current;
        if (worldMapMapLab == null || world == null || !world.Ready || location == null || location.IsSettlement || string.IsNullOrEmpty(location.PlaceKind))
            return false;
        // Видимое место плана нарисовано MapLab — значок не нужен, цель уже есть.
        foreach ((GeneratedMapSite site, Rect _, List<(Rect Rect, string ArtId)> _) in world.Features)
            if (site.Kind == location.PlaceKind && Mathf.Abs(site.XPercent - location.MapXPercent) < 0.6f && Mathf.Abs(site.YPercent - location.MapYPercent) < 0.6f)
                return true;
        if (!MapLabGameMap.PlaceArt(location.PlaceKind, out Texture2D texture, out float worldHeight, out float aspect, out Vector2 pivot))
            return false;
        float pxPerUnit = world.CanvasPixelsPerUnit(worldMapCanvasWidth);
        float height = worldHeight * pxPerUnit;
        float width = height * aspect;
        float shift = MapLabGameLive.ElevationShiftPercent(world, location.MapXPercent, location.MapYPercent);
        float baseX = location.MapXPercent / 100f * worldMapCanvasWidth;
        float baseY = (location.MapYPercent + shift) / 100f * worldMapCanvasHeight;
        MapLabSpriteMask mask = MapLabSpriteMask.For(texture);
        string id = location.Id;
        MaskedTarget picture = new MaskedTarget { name = "world-map-place-" + id, pickingMode = PickingMode.Position, Mask = mask };
        picture.style.position = Position.Absolute;
        picture.style.left = baseX - pivot.x * width;
        picture.style.top = baseY - (1f - pivot.y) * height;
        picture.style.width = width;
        picture.style.height = height;
        picture.style.backgroundImage = new StyleBackground(Background.FromTexture2D(texture));
        picture.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
        VisualElement glow = new VisualElement { pickingMode = PickingMode.Ignore };
        glow.style.position = Position.Absolute;
        glow.style.left = glow.style.top = glow.style.right = glow.style.bottom = 0;
        if (mask?.Glow != null)
            glow.style.backgroundImage = new StyleBackground(Background.FromTexture2D(mask.Glow));
        glow.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
        glow.style.opacity = 0f;
        picture.Add(glow);
        Label label = MakeWorldMapHoverLabel(location.Name);
        picture.RegisterCallback<PointerEnterEvent>(_ =>
        {
            glow.style.opacity = 1f;
            label.style.display = DisplayStyle.Flex;
        });
        picture.RegisterCallback<PointerLeaveEvent>(_ =>
        {
            glow.style.opacity = 0f;
            label.style.display = DisplayStyle.None;
        });
        picture.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
        picture.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            if (gameState == null || isGameOver)
                return;
            ExpeditionData expedition = gameState.ActiveExpedition;
            if (expedition != null && expedition.Phase == CommanderState.AtLocation && expedition.LocationId == id && !expedition.HasTimedActivity)
            {
                TryOpenLocationInteraction(id);
                return;
            }
            SelectWorldMapLocation(id);
        });
        worldMapMarkers.Add(picture);
        worldMapPlacePictures.Add((label, location, height));
        return true;
    }

    private void PlaceWorldMapPlacePictureLabels()
    {
        foreach ((Label label, LocationData location, float height) in worldMapPlacePictures)
        {
            if (label.style.display == DisplayStyle.None)
                continue;
            Vector2 at = MapPercentToWorldMapViewport(location.MapXPercent, location.MapYPercent);
            float width = float.IsNaN(label.resolvedStyle.width) ? 0f : label.resolvedStyle.width;
            float lh = float.IsNaN(label.resolvedStyle.height) ? 0f : label.resolvedStyle.height;
            label.style.translate = new Translate(at.x - width * 0.5f, at.y - height * worldMapZoom - lh - 4f);
        }
    }

    private void TickWorldMapFeatures()
    {
        PlaceWorldMapPlacePictureLabels();
        MapLabGameLive.World world = MapLabGameLive.Current;
        bool active = worldMapMapLab != null && world != null && world.Ready && gameState != null && gameState.HasGeneratedMap
                      && ReferenceEquals(world.Plan, gameState.GeneratedMap) && !isGameOver;
        if (worldMap == null)
            return;
        if (worldMapFeatureLayer == null)
        {
            worldMapFeatureLayer = new VisualElement { name = "world-map-features", pickingMode = PickingMode.Ignore };
            worldMapFeatureLayer.style.position = Position.Absolute;
            worldMapFeatureLayer.style.left = worldMapFeatureLayer.style.top = worldMapFeatureLayer.style.right = worldMapFeatureLayer.style.bottom = 0;
            VisualElement parent = worldMapMarkers?.parent ?? worldMap;
            int index = worldMapMarkers != null ? parent.IndexOf(worldMapMarkers) + 1 : parent.childCount;
            parent.Insert(Mathf.Clamp(index, 0, parent.childCount), worldMapFeatureLayer);
        }
        if (!ReferenceEquals(world, worldMapFeaturesWorld))
            BuildWorldMapFeatureTargets(active ? world : null);
        bool screen = IsSettlementScreenOpen || IsLocalScreenOpen;
        worldMapFeatureLayer.style.display = active && !screen ? DisplayStyle.Flex : DisplayStyle.None;
        if (!active || screen)
        {
            foreach ((GeneratedMapSite _, List<VisualElement> _, Label label, Rect _) in worldMapFeatureTargets)
                label.style.display = DisplayStyle.None;
            HideWorldMapInfoCard();
            return;
        }
        // Видимость по разведанной земле — не каждый кадр.
        worldMapFeatureTimer += Time.unscaledDeltaTime;
        if (worldMapFeatureTimer >= 0.5f)
        {
            worldMapFeatureTimer = 0f;
            foreach ((GeneratedMapSite site, List<VisualElement> targets, Label _, Rect _) in worldMapFeatureTargets)
            {
                bool known = WorldMapFog.IsExploredAtPercent(gameState, site.XPercent, site.YPercent);
                foreach (VisualElement t in targets)
                    t.style.display = known ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
        foreach ((GeneratedMapSite _, List<VisualElement> _, Label label, Rect union) in worldMapFeatureTargets)
        {
            if (label.style.display == DisplayStyle.None)
                continue;
            Vector2 top = MapPercentToWorldMapViewport(union.center.x, union.yMin);
            float width = float.IsNaN(label.resolvedStyle.width) ? 0f : label.resolvedStyle.width;
            float height = float.IsNaN(label.resolvedStyle.height) ? 0f : label.resolvedStyle.height;
            label.style.translate = new Translate(top.x - width * 0.5f, top.y - height - 4f);
        }
    }

    private void BuildWorldMapFeatureTargets(MapLabGameLive.World world)
    {
        foreach ((GeneratedMapSite _, List<VisualElement> _, Label label, Rect _) in worldMapFeatureTargets)
            label.RemoveFromHierarchy();
        worldMapFeatureTargets.Clear();
        worldMapFeatureLayer.Clear();
        worldMapFeaturesWorld = world;
        if (world == null)
            return;
        foreach ((GeneratedMapSite site, Rect union, List<(Rect Rect, string ArtId)> parts) in world.Features)
        {
            GeneratedMapSite captured = site;
            Label label = MakeWorldMapHoverLabel(site.Name);
            List<VisualElement> targets = new List<VisualElement>();
            List<VisualElement> glows = new List<VisualElement>();
            void Hover(bool on)
            {
                foreach (VisualElement g in glows)
                    g.style.opacity = on ? 1f : 0f;
                label.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }
            foreach ((Rect rect, string artId) in parts)
            {
                MapLabSpriteMask mask = MapLabSpriteMask.For(MapLabGameMap.ArtTexture(artId));
                MaskedTarget target = new MaskedTarget { name = "world-map-feature-" + site.Kind, pickingMode = PickingMode.Position, Mask = mask, userData = site };
                target.style.position = Position.Absolute;
                target.style.left = Length.Percent(rect.x);
                target.style.top = Length.Percent(rect.y);
                target.style.width = Length.Percent(rect.width);
                target.style.height = Length.Percent(rect.height);
                VisualElement glow = new VisualElement { pickingMode = PickingMode.Ignore };
                glow.style.position = Position.Absolute;
                glow.style.left = glow.style.top = glow.style.right = glow.style.bottom = 0;
                if (mask?.Glow != null)
                    glow.style.backgroundImage = new StyleBackground(Background.FromTexture2D(mask.Glow));
                glow.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
                glow.style.opacity = 0f;
                target.Add(glow);
                glows.Add(glow);
                target.RegisterCallback<PointerEnterEvent>(_ => Hover(true));
                target.RegisterCallback<PointerLeaveEvent>(_ => Hover(false));
                target.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                target.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    // Место игры (видимое место генератора) — как клик по его значку:
                    // отряд идёт туда, прибытие открывает место.
                    LocationData place = PlaceLocationFor(captured);
                    if (place != null)
                    {
                        ExpeditionData expedition = gameState?.ActiveExpedition;
                        if (expedition != null && expedition.Phase == CommanderState.AtLocation && expedition.LocationId == place.Id && !expedition.HasTimedActivity)
                            TryOpenLocationInteraction(place.Id);
                        else
                            SelectWorldMapLocation(place.Id);
                        return;
                    }
                    ShowWorldMapInfoCard(captured.Name, captured.Description, "Идти сюда",
                        () => GoToWorldMapPoint(captured.XPercent, captured.YPercent));
                });
                target.style.display = DisplayStyle.None;
                worldMapFeatureLayer.Add(target);
                targets.Add(target);
            }
            worldMapFeatureTargets.Add((site, targets, label, union));
        }
    }

    // Место игры, созданное из места плана (по виду и точке); null — деталь.
    private LocationData PlaceLocationFor(GeneratedMapSite site)
    {
        if (gameState?.Locations == null || site == null)
            return null;
        foreach (LocationData location in gameState.Locations)
            if (location != null && !location.IsWaypoint && location.PlaceKind == site.Kind && location.IsVisibleOnMap
                && Mathf.Abs(location.MapXPercent - site.XPercent) < 0.6f && Mathf.Abs(location.MapYPercent - site.YPercent) < 0.6f)
                return location;
        return null;
    }

    private Label MakeWorldMapHoverLabel(string text)
    {
        Label label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.style.position = Position.Absolute;
        label.style.left = 0;
        label.style.top = 0;
        label.style.fontSize = 15;
        label.style.color = new Color(0.98f, 0.93f, 0.8f);
        label.style.backgroundColor = new Color(0.18f, 0.14f, 0.1f, 0.82f);
        label.style.paddingLeft = label.style.paddingRight = 6;
        label.style.borderTopLeftRadius = label.style.borderTopRightRadius = label.style.borderBottomLeftRadius = label.style.borderBottomRightRadius = 4;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.display = DisplayStyle.None;
        worldMapViewport?.Add(label);
        return label;
    }

    // Отряд идёт к точке карты (тот же приказ, что клик по земле).
    private void GoToWorldMapPoint(float x, float y)
    {
        if (gameState == null || isGameOver)
            return;
        string inside = SettlementCampService.InsideLocationId(gameState);
        if (!string.IsNullOrEmpty(inside))
        {
            AddReport("Отряд в поселении: сначала «Уйти».");
            return;
        }
        ShowWorldMapClickMarker(x, y);
        IssueContinuousMapOrder(x, y, null);
    }

    // ---------- Карточка ----------

    private void EnsureWorldMapInfoCard()
    {
        if (worldMapInfoCard != null || worldMapViewport == null)
            return;
        worldMapInfoCard = new VisualElement { name = "world-map-info-card", pickingMode = PickingMode.Position };
        worldMapInfoCard.style.position = Position.Absolute;
        worldMapInfoCard.style.right = 16;
        worldMapInfoCard.style.top = 64;
        worldMapInfoCard.style.width = 320;
        worldMapInfoCard.style.paddingLeft = worldMapInfoCard.style.paddingRight = 12;
        worldMapInfoCard.style.paddingTop = worldMapInfoCard.style.paddingBottom = 10;
        worldMapInfoCard.style.backgroundColor = new Color(0.16f, 0.13f, 0.1f, 0.94f);
        worldMapInfoCard.style.borderTopLeftRadius = worldMapInfoCard.style.borderTopRightRadius =
            worldMapInfoCard.style.borderBottomLeftRadius = worldMapInfoCard.style.borderBottomRightRadius = 6;
        worldMapInfoTitle = new Label { name = "world-map-info-title" };
        worldMapInfoTitle.style.fontSize = 17;
        worldMapInfoTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        worldMapInfoTitle.style.color = new Color(0.97f, 0.92f, 0.8f);
        worldMapInfoCard.Add(worldMapInfoTitle);
        worldMapInfoText = new Label { name = "world-map-info-text" };
        worldMapInfoText.style.whiteSpace = WhiteSpace.Normal;
        worldMapInfoText.style.color = new Color(0.86f, 0.82f, 0.72f);
        worldMapInfoText.style.fontSize = 14;
        worldMapInfoText.style.marginTop = 6;
        worldMapInfoText.style.marginBottom = 8;
        worldMapInfoCard.Add(worldMapInfoText);
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        worldMapInfoGo = new Button(() =>
        {
            System.Action go = worldMapInfoGoAction;
            HideWorldMapInfoCard();
            go?.Invoke();
        }) { text = "Идти сюда", name = "world-map-info-go" };
        StyleSettlementMapButton(worldMapInfoGo);
        row.Add(worldMapInfoGo);
        Button close = new Button(HideWorldMapInfoCard) { text = "Закрыть", name = "world-map-info-close" };
        StyleSettlementMapButton(close);
        row.Add(close);
        worldMapInfoCard.Add(row);
        worldMapInfoCard.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
        worldMapInfoCard.style.display = DisplayStyle.None;
        worldMapViewport.Add(worldMapInfoCard);
    }

    private void ShowWorldMapInfoCard(string title, string text, string goLabel, System.Action go)
    {
        EnsureWorldMapInfoCard();
        if (worldMapInfoCard == null)
            return;
        worldMapInfoTitle.text = title ?? string.Empty;
        worldMapInfoText.text = string.IsNullOrWhiteSpace(text) ? "—" : text;
        worldMapInfoGoAction = go;
        worldMapInfoGo.text = goLabel ?? "Идти сюда";
        worldMapInfoGo.style.display = go != null ? DisplayStyle.Flex : DisplayStyle.None;
        worldMapInfoCard.style.display = DisplayStyle.Flex;
        worldMapInfoCard.BringToFront();
    }

    private void HideWorldMapInfoCard()
    {
        worldMapInfoGoAction = null;
        if (worldMapInfoCard != null)
            worldMapInfoCard.style.display = DisplayStyle.None;
    }
}
