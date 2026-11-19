using System.Collections.Generic;
using KingdomSurvival.MapLabBridge;
using KingdomSurvival.WorldMapVisual;
using UnityEngine;
using UnityEngine.UIElements;

// Метки карты мира (бриф «Метки мест и доступных квестов», 19.11.2026):
// «?» над известным, но ещё не исследованным местом и «!» там, где сейчас
// можно взять новое поручение (WorldMapMarkerService — одно вычисление для
// метки, подсказки и карточки). Отдельный слой поверх карты: позиция следует
// месту, размер на экране постоянный, символ фронтальный и не проходит через
// цветокоррекцию вида карты; картинка — по стилю выбранного вида (classic,
// sepia, gloom; неизвестный вид — classic). Пересчёт — при обновлении панели
// карты (открытие, загрузка, открытие и исследование мест, поручения, сутки),
// а не каждый кадр; каждый кадр — только положение. Клик — то же, что клик по
// месту: отряд идёт к нему (у места — вход). Задание не принимается и место не
// исследуется кликом.
public partial class PrototypeUIController
{
    private sealed class WorldMapQuestMarker
    {
        public string LocationId;
        public WorldMapMarkerInfo Info;
        public VisualElement Element;
        // Якорь в процентах полотна; Lifted — якорь уже над рисунком места
        // (верх рисунка), иначе — точка места (метка выше значка/деревни).
        public Vector2 Anchor;
        public bool Lifted;
        public string StyleId;
    }

    private VisualElement worldMapQuestMarkerLayer;
    private readonly Dictionary<string, WorldMapQuestMarker> worldMapQuestMarkers = new Dictionary<string, WorldMapQuestMarker>();
    private readonly List<WorldMapQuestMarker> worldMapQuestMarkerOrder = new List<WorldMapQuestMarker>();
    private readonly List<Vector2> worldMapQuestMarkerPlaced = new List<Vector2>();

    private void EnsureWorldMapQuestMarkerLayer()
    {
        if (worldMapQuestMarkerLayer != null || worldMapViewport == null)
            return;
        worldMapQuestMarkerLayer = new VisualElement { name = "world-map-quest-markers", pickingMode = PickingMode.Ignore };
        worldMapQuestMarkerLayer.style.position = Position.Absolute;
        worldMapQuestMarkerLayer.style.left = worldMapQuestMarkerLayer.style.top = 0;
        worldMapQuestMarkerLayer.style.right = worldMapQuestMarkerLayer.style.bottom = 0;
        worldMapQuestMarkerLayer.style.overflow = Overflow.Hidden;
        // Над рисунком карты, зданиями и фигурами — под кнопками и панелями карты.
        VisualElement nav = worldMapViewport.Q<VisualElement>("world-map-nav-controls");
        int index = nav != null && nav.parent == worldMapViewport ? worldMapViewport.IndexOf(nav) : worldMapViewport.childCount;
        worldMapViewport.Insert(index, worldMapQuestMarkerLayer);
    }

    // Пересчёт меток всех видимых мест (обновление панели карты).
    private void RebuildWorldMapQuestMarkers()
    {
        EnsureWorldMapQuestMarkerLayer();
        if (worldMapQuestMarkerLayer == null || gameState == null)
            return;
        HashSet<string> alive = new HashSet<string>();
        foreach (LocationData location in gameState.Locations)
        {
            WorldMapMarkerInfo info = WorldMapMarkerService.For(gameState, location);
            if (info.Kind == WorldMapMarkerKind.None)
                continue;
            alive.Add(location.Id);
            if (!worldMapQuestMarkers.TryGetValue(location.Id, out WorldMapQuestMarker marker))
            {
                marker = new WorldMapQuestMarker { LocationId = location.Id, Element = CreateWorldMapQuestMarkerElement(location.Id) };
                worldMapQuestMarkers[location.Id] = marker;
                worldMapQuestMarkerLayer.Add(marker.Element);
            }
            if (marker.Info.Kind != info.Kind)
                marker.StyleId = null;
            marker.Info = info;
            marker.Element.name = "world-map-quest-marker-" + location.Id;
            marker.Element.EnableInClassList("world-map-quest-marker--quest", info.Kind == WorldMapMarkerKind.QuestAvailable);
            marker.Element.EnableInClassList("world-map-quest-marker--unknown", info.Kind == WorldMapMarkerKind.UnknownPlace);
            marker.Anchor = WorldMapQuestMarkerAnchor(location, out marker.Lifted);
        }
        List<string> gone = new List<string>();
        foreach (KeyValuePair<string, WorldMapQuestMarker> pair in worldMapQuestMarkers)
            if (!alive.Contains(pair.Key))
                gone.Add(pair.Key);
        foreach (string id in gone)
        {
            worldMapQuestMarkers[id].Element.RemoveFromHierarchy();
            worldMapQuestMarkers.Remove(id);
        }
        // Порядок раскладки: «!» раньше «?», дальше — по ID (устойчиво).
        worldMapQuestMarkerOrder.Clear();
        worldMapQuestMarkerOrder.AddRange(worldMapQuestMarkers.Values);
        worldMapQuestMarkerOrder.Sort((a, b) =>
        {
            int kind = (b.Info.Kind == WorldMapMarkerKind.QuestAvailable).CompareTo(a.Info.Kind == WorldMapMarkerKind.QuestAvailable);
            return kind != 0 ? kind : string.CompareOrdinal(a.LocationId, b.LocationId);
        });
    }

    private VisualElement CreateWorldMapQuestMarkerElement(string locationId)
    {
        VisualElement element = new VisualElement { pickingMode = PickingMode.Position };
        element.AddToClassList("world-map-quest-marker");
        element.style.position = Position.Absolute;
        element.style.left = element.style.top = 0;
        element.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
        element.style.display = DisplayStyle.None;
        element.RegisterCallback<PointerEnterEvent>(_ => ShowWorldMapNodeTooltip(element, locationId));
        element.RegisterCallback<PointerLeaveEvent>(_ => HideWorldMapNodeTooltip(locationId));
        element.RegisterCallback<DetachFromPanelEvent>(_ => HideWorldMapNodeTooltip(locationId));
        element.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
        element.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            OnWorldMapQuestMarkerClicked(locationId);
        });
        return element;
    }

    // То же, что клик по месту (значок, рисунок, подпись поселения).
    private void OnWorldMapQuestMarkerClicked(string locationId)
    {
        if (gameState == null || isGameOver || IsSettlementScreenOpen)
            return;
        bool canChangeRoute = !gameState.HasActiveExpedition ||
                              (!gameState.HasPendingExpeditionDecision && !gameState.ActiveExpedition.IsLocationResearchInProgress);
        ExpeditionData expedition = gameState.ActiveExpedition;
        if (expedition != null && expedition.Phase == CommanderState.AtLocation && expedition.LocationId == locationId && !expedition.HasTimedActivity)
        {
            TryOpenLocationInteraction(locationId);
            return;
        }
        if (!canChangeRoute)
            return;
        worldMapPendingRole = null;
        worldMapPendingSettlement = null;
        SelectWorldMapLocation(locationId);
    }

    // Якорь метки: верх рисунка места (место нарисовано MapLab или своим
    // рисунком) — иначе точка места на земле (с подъёмом по рельефу).
    private Vector2 WorldMapQuestMarkerAnchor(LocationData location, out bool lifted)
    {
        lifted = false;
        MapLabGameLive.World world = MapLabGameLive.Current;
        if (!location.IsSettlement && world != null && world.Ready && !string.IsNullOrEmpty(location.PlaceKind))
        {
            foreach ((GeneratedMapSite site, Rect rect, List<(Rect Rect, string ArtId)> _) in world.Features)
                if (site.Kind == location.PlaceKind && Mathf.Abs(site.XPercent - location.MapXPercent) < 0.6f &&
                    Mathf.Abs(site.YPercent - location.MapYPercent) < 0.6f)
                {
                    lifted = true;
                    return new Vector2(rect.center.x, rect.yMin);
                }
        }
        VisualElement picture = worldMapMarkers?.Q<VisualElement>("world-map-place-" + location.Id);
        if (picture != null && worldMapCanvasWidth > 0f && worldMapCanvasHeight > 0f &&
            picture.style.top.keyword == StyleKeyword.Undefined && picture.style.left.keyword == StyleKeyword.Undefined)
        {
            float left = picture.style.left.value.value, top = picture.style.top.value.value, width = picture.style.width.value.value;
            lifted = true;
            return new Vector2((left + width * 0.5f) / worldMapCanvasWidth * 100f, top / worldMapCanvasHeight * 100f);
        }
        float shift = world != null && world.Ready ? MapLabGameLive.ElevationShiftPercent(world, location.MapXPercent, location.MapYPercent) : 0f;
        return new Vector2(location.MapXPercent, location.MapYPercent + shift);
    }

    // Каждый кадр: положение, размер и картинка по стилю вида.
    private void TickWorldMapQuestMarkers()
    {
        if (worldMapQuestMarkerLayer == null)
            return;
        bool shown = gameState != null && !isGameOver && IsWorldMapScreenOpen && !IsSettlementScreenOpen && !IsLocalScreenOpen &&
                     worldMapQuestMarkers.Count > 0;
        worldMapQuestMarkerLayer.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
        if (!shown || worldMapViewport == null)
            return;
        WorldMapMarkerArt art = WorldMapMarkerArt.Load();
        WorldMapMarkerStyle style = art != null ? art.Find(MapLabGameViews.ActiveId) : null;
        float symbolHeight = art != null ? art.SymbolHeight : 32f;
        float gap = art != null ? art.Gap : 8f;
        float spacing = art != null ? art.MinSpacing : 26f;
        float width = worldMapViewport.resolvedStyle.width, height = worldMapViewport.resolvedStyle.height;
        if (float.IsNaN(width) || float.IsNaN(height))
            return;
        bool far = WorldMapFarZoom();
        worldMapQuestMarkerPlaced.Clear();
        foreach (WorldMapQuestMarker marker in worldMapQuestMarkerOrder)
        {
            VisualElement element = marker.Element;
            bool quest = marker.Info.Kind == WorldMapMarkerKind.QuestAvailable;
            WorldMapMarkerSymbol symbol = style == null ? null : quest ? style.Exclamation : style.Question;
            if (symbol?.Texture == null)
            {
                element.style.display = DisplayStyle.None;
                continue;
            }
            string styleId = style.Id + (quest ? "!" : "?");
            if (marker.StyleId != styleId)
            {
                element.style.backgroundImage = new StyleBackground(Background.FromTexture2D(symbol.Texture));
                marker.StyleId = styleId;
            }
            Rect visible = symbol.Visible.height > 0.01f ? symbol.Visible : new Rect(0f, 0f, 1f, 1f);
            float size = symbolHeight / visible.height;
            Vector2 at = MapPercentToWorldMapViewport(marker.Anchor.x, marker.Anchor.y);
            // Низ видимой части символа — над якорем с отступом; точка места —
            // ещё выше значка или середины деревни.
            float bottom = at.y - gap - (marker.Lifted ? 0f : 18f);
            Vector2 center = new Vector2(at.x, bottom - symbolHeight * 0.5f);
            // Плотная группа: раздвинуть по горизонтали (не дальше двух шагов);
            // на дальнем масштабе «?» рядом с «!» не показывается.
            bool hidden = false;
            for (int step = 0; step < 3; step++)
            {
                bool clash = false;
                foreach (Vector2 other in worldMapQuestMarkerPlaced)
                    if (Mathf.Abs(other.x - center.x) < spacing && Mathf.Abs(other.y - center.y) < symbolHeight)
                    {
                        clash = true;
                        break;
                    }
                if (!clash)
                    break;
                if (far && !quest || step == 2)
                {
                    hidden = true;
                    break;
                }
                center.x += (step % 2 == 0 ? 1f : -2f) * spacing;
            }
            bool inside = center.x > -symbolHeight && center.x < width + symbolHeight && center.y > -symbolHeight && center.y < height + symbolHeight;
            if (hidden || !inside)
            {
                element.style.display = DisplayStyle.None;
                continue;
            }
            worldMapQuestMarkerPlaced.Add(center);
            element.style.display = DisplayStyle.Flex;
            element.style.width = size;
            element.style.height = size;
            float left = center.x - (visible.center.x) * size;
            float top = center.y + symbolHeight * 0.5f - visible.yMax * size;
            element.style.translate = new Translate(left, top);
        }
    }
}
