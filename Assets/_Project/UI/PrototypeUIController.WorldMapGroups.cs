using System.Collections.Generic;
using KingdomSurvival.MapLabBridge;
using UnityEngine;
using UnityEngine.UIElements;

// 12У-G, пакет «Фракции на карте»: постоянные группы мира (дозор, обоз,
// мастер, ватага) на карте MapLab. Положение — функция игрового времени по
// маршруту группы (WorldMapActorMotion), поэтому оно не зависит от экрана и
// не плодит копий. Видна группа только вблизи отряда на разведанной земле;
// подпись при наведении — только название группы (принадлежность наведением
// не раскрывается). Своего действия у групп пока нет — клика нет (содержание
// встреч — CONTENT_TODO пакета). Без рисунка MapLab групп на карте нет.
public partial class PrototypeUIController
{
    private VisualElement worldMapGroupLayer;
    private readonly Dictionary<string, (VisualElement Marker, int Direction)> worldMapGroupMarkers = new Dictionary<string, (VisualElement, int)>();
    private float worldMapGroupTimer;
    private GeneratedWorldMap worldMapGroupsMap;
    private WorldMapHexGrid worldMapGroupsGrid;
    private readonly Dictionary<string, Label> worldMapGroupLabels = new Dictionary<string, Label>();

    private void TickWorldMapGroups()
    {
        worldMapGroupTimer += Time.unscaledDeltaTime;
        // Время идёт само — группы движутся плавно (часто, но не каждый кадр).
        if (worldMapGroupTimer < 0.05f)
            return;
        worldMapGroupTimer = 0f;
        if (worldMap == null)
            return;
        GeneratedWorldMap map = gameState != null && gameState.HasGeneratedMap ? gameState.GeneratedMap : null;
        bool active = map != null && worldMapMapLab != null && map.Actors != null && map.Actors.Count > 0 && !isGameOver;
        if (!ReferenceEquals(map, worldMapGroupsMap))
        {
            worldMapGroupLayer?.Clear();
            worldMapGroupMarkers.Clear();
            foreach (Label l in worldMapGroupLabels.Values)
                l.RemoveFromHierarchy();
            worldMapGroupLabels.Clear();
            worldMapGroupsMap = map;
            worldMapGroupsGrid = map?.CreateGrid();
        }
        if (!active)
        {
            if (worldMapGroupLayer != null)
                worldMapGroupLayer.style.display = DisplayStyle.None;
            foreach (Label l in worldMapGroupLabels.Values)
                l.style.display = DisplayStyle.None;
            return;
        }
        if (worldMapGroupLayer == null)
        {
            worldMapGroupLayer = new VisualElement { name = "world-map-groups", pickingMode = PickingMode.Ignore };
            worldMapGroupLayer.style.position = Position.Absolute;
            worldMapGroupLayer.style.left = 0;
            worldMapGroupLayer.style.top = 0;
            worldMapGroupLayer.style.right = 0;
            worldMapGroupLayer.style.bottom = 0;
            // Над слоями карты (как здания): иначе курсор до групп не доходит.
            VisualElement parent = worldMapMarkers?.parent ?? worldMap;
            int index = worldMapMarkers != null ? parent.IndexOf(worldMapMarkers) + 1 : parent.childCount;
            parent.Insert(Mathf.Clamp(index, 0, parent.childCount), worldMapGroupLayer);
        }
        worldMapGroupLayer.style.display = DisplayStyle.Flex;

        WorldMapHexGrid grid = worldMapGroupsGrid ??= map.CreateGrid();
        double hour = ContinuousSimulationSystem.GetAbsoluteHours(gameState);
        ExpeditionData expedition = gameState.ActiveExpedition;
        float heroX = expedition != null ? expedition.CurrentMapXPercent : WorldMapNavigation.CapitalXPercent;
        float heroY = expedition != null ? expedition.CurrentMapYPercent : WorldMapNavigation.CapitalYPercent;
        double sight = WorldMapMovementRules.Current.FogRevealRadiusHexes + 1.0;
        float hexPx = (float)(grid.HexWidth / grid.CanvasWidth * worldMapCanvasWidth);
        // Рост фигуры группы — как у фигурки героя (одинаковый масштаб людей).
        float figureHeight = hexPx * 1.1f;
        MapLabGameLive.World live = MapLabGameLive.Current;
        if (live != null && live.Ready && MapLabGameLive.HeroFrame(0, out Texture2D _, out float heroWorldHeight, out float _, out Vector2 _))
            figureHeight = heroWorldHeight * live.CanvasPixelsPerUnit(worldMapCanvasWidth);
        foreach (GeneratedMapActor actor in map.Actors)
        {
            if (actor == null || string.IsNullOrEmpty(actor.FigureSet))
                continue;
            MapPointData at = WorldMapActorMotion.PositionAt(actor, hour, grid, out float dx, out float dy, out bool waiting);
            bool visible = grid.DistanceHexes(heroX, heroY, at.XPercent, at.YPercent) <= sight && WorldMapFog.IsExploredAtPercent(gameState, at.XPercent, at.YPercent);
            worldMapGroupMarkers.TryGetValue(actor.ActorId, out (VisualElement Marker, int Direction) entry);
            worldMapGroupLabels.TryGetValue(actor.ActorId, out Label hoverLabel);
            if (!visible)
            {
                if (entry.Marker != null)
                    entry.Marker.style.display = DisplayStyle.None;
                if (hoverLabel != null)
                    hoverLabel.style.display = DisplayStyle.None;
                continue;
            }
            int direction = waiting && entry.Marker != null ? entry.Direction : WorldMapActorMotion.DirectionIndex(dx, dy);
            if (entry.Marker == null)
            {
                VisualElement marker = new VisualElement { name = "world-map-group-" + actor.ActorId, pickingMode = PickingMode.Position };
                marker.style.position = Position.Absolute;
                marker.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                marker.style.width = figureHeight;
                marker.style.height = figureHeight;
                Label label = MakeWorldMapHoverLabel(actor.Name);
                worldMapGroupLabels[actor.ActorId] = label;
                hoverLabel = label;
                GeneratedMapActor captured = actor;
                marker.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    marker.style.scale = new Scale(new Vector3(1.08f, 1.08f, 1f));
                    label.style.display = DisplayStyle.Flex;
                });
                marker.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    marker.style.scale = new Scale(Vector3.one);
                    label.style.display = DisplayStyle.None;
                });
                marker.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                // Клик — карточка группы: кто это видно со стороны; встречи
                // и разговора пока нет (содержание — CONTENT_TODO).
                marker.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    ShowWorldMapInfoCard(captured.Name, captured.Description, null, null);
                });
                worldMapGroupLayer.Add(marker);
                entry = (marker, -1);
            }
            if (entry.Direction != direction)
            {
                Texture2D texture = MapLabGameMap.FigureTexture(actor.FigureSet, direction);
                entry.Marker.style.backgroundImage = texture != null ? new StyleBackground(Background.FromTexture2D(texture)) : new StyleBackground(StyleKeyword.None);
                entry.Direction = direction;
            }
            entry.Marker.style.display = DisplayStyle.Flex;
            // Опора фигуры — низ по центру, в точке маршрута.
            entry.Marker.style.width = figureHeight;
            entry.Marker.style.height = figureHeight;
            entry.Marker.style.left = at.XPercent / 100f * worldMapCanvasWidth - figureHeight * 0.5f;
            entry.Marker.style.top = at.YPercent / 100f * worldMapCanvasHeight - figureHeight * 0.97f;
            worldMapGroupMarkers[actor.ActorId] = entry;
            if (hoverLabel != null && hoverLabel.style.display == DisplayStyle.Flex)
            {
                Vector2 head = MapPercentToWorldMapViewport(at.XPercent, at.YPercent);
                float lw = float.IsNaN(hoverLabel.resolvedStyle.width) ? 0f : hoverLabel.resolvedStyle.width;
                hoverLabel.style.translate = new Translate(head.x - lw * 0.5f, head.y - figureHeight * worldMapZoom - 22f);
            }
        }
    }
}
