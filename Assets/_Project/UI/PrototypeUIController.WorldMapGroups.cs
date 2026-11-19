using System.Collections.Generic;
using KingdomSurvival.MapLabBridge;
using UnityEngine;
using UnityEngine.UIElements;

// 12У-G, пакет «Фракции на карте»: постоянные группы мира на карте MapLab.
// Бриф «Живое население» (19.11.2026): показ читает runtime-состояние партии
// (WorldPopulationService) — уничтоженная группа не рисуется, группа вне
// маршрута стоит в своей опорной точке, новые группы источника появляются
// сами. Видна группа только вблизи отряда на разведанной земле (в тумане
// фигуры живого положения нет). Звери и существа без набора ракурсов —
// одиночный рисунок из Базы существ (без выдуманных направлений, вне
// сортировки сцены). Подпись наведения — название, занятие и состав по
// уровню знания; «!» над фигурой — группа идёт на отряд или следит за ним.
// Клик: рядом с мирной группой — разговор, иначе карточка и «Идти сюда»
// (обычный приказ движения, без телепорта и боя через всю карту).
public partial class PrototypeUIController
{
    private VisualElement worldMapGroupLayer;
    private readonly Dictionary<string, (VisualElement Marker, int Direction)> worldMapGroupMarkers = new Dictionary<string, (VisualElement, int)>();
    private float worldMapGroupTimer;
    private GeneratedWorldMap worldMapGroupsMap;
    private WorldMapHexGrid worldMapGroupsGrid;
    private readonly Dictionary<string, Label> worldMapGroupLabels = new Dictionary<string, Label>();
    private readonly Dictionary<string, Label> worldMapGroupAlerts = new Dictionary<string, Label>();
    private readonly Dictionary<string, Sprite> worldMapGroupSprites = new Dictionary<string, Sprite>();
    private readonly HashSet<string> worldMapGroupSeen = new HashSet<string>();

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
            worldMapGroupShadows.Clear();
            foreach (Label l in worldMapGroupLabels.Values)
                l.RemoveFromHierarchy();
            worldMapGroupLabels.Clear();
            worldMapGroupAlerts.Clear();
            worldMapGroupsMap = map;
            worldMapGroupsGrid = map?.CreateGrid();
        }
        if (!active)
        {
            if (worldMapGroupLayer != null)
                worldMapGroupLayer.style.display = DisplayStyle.None;
            foreach (Label l in worldMapGroupLabels.Values)
                l.style.display = DisplayStyle.None;
            foreach (string id in worldMapGroupMarkers.Keys)
                MapLabLiveFigures.Hide("group-" + id);
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
        WorldPopulationStateData population = WorldPopulationService.EnsureState(gameState, hour);
        if (population == null)
            return;
        ExpeditionData expedition = gameState.ActiveExpedition;
        float heroX = expedition != null ? expedition.CurrentMapXPercent : WorldMapNavigation.CapitalXPercent;
        float heroY = expedition != null ? expedition.CurrentMapYPercent : WorldMapNavigation.CapitalYPercent;
        double sight = WorldMapMovementRules.Current.FogRevealRadiusHexes + 1.0;
        float hexPx = (float)(grid.HexWidth / grid.CanvasWidth * worldMapCanvasWidth);
        // Рост фигуры группы — как у фигурки героя (одинаковый масштаб людей).
        float figureHeight = hexPx * 1.1f;
        MapLabGameLive.World live = MapLabGameLive.Current;
        float sceneFigureHeight = 0f;
        if (live != null && live.Ready && MapLabGameLive.HeroFrame(0, out Texture2D _, out float heroWorldHeight, out float _, out Vector2 _))
        {
            figureHeight = heroWorldHeight * live.CanvasPixelsPerUnit(worldMapCanvasWidth);
            sceneFigureHeight = heroWorldHeight;
        }

        worldMapGroupSeen.Clear();
        foreach (PopulationGroupState group in population.Groups)
        {
            if (group == null)
                continue;
            GeneratedMapActor actor = WorldMapGroupDefinition(group);
            string id = group.GroupId;
            worldMapGroupSeen.Add(id);
            worldMapGroupMarkers.TryGetValue(id, out (VisualElement Marker, int Direction) entry);
            worldMapGroupLabels.TryGetValue(id, out Label hoverLabel);
            worldMapGroupAlerts.TryGetValue(id, out Label alert);
            bool drawable = group.IsActive && actor != null && (!string.IsNullOrEmpty(actor.FigureSet) || WorldMapGroupSprite(actor) != null);
            float dx = group.DirX, dy = group.DirY;
            bool waiting = false;
            MapPointData at = drawable
                ? WorldPopulationService.GroupPosition(gameState, group, hour, grid, out dx, out dy, out waiting)
                : new MapPointData(0f, 0f);
            double distance = grid.DistanceHexes(heroX, heroY, at.XPercent, at.YPercent);
            bool visible = drawable && distance <= sight && WorldMapFog.IsExploredAtPercent(gameState, at.XPercent, at.YPercent);
            if (!visible)
            {
                HideWorldMapGroup(id, entry.Marker, hoverLabel, alert, figureHeight);
                continue;
            }
            bool sprite = string.IsNullOrEmpty(actor.FigureSet);
            int direction = sprite ? 0 : waiting && entry.Marker != null ? entry.Direction : WorldMapActorMotion.DirectionIndex(dx, dy);
            if (entry.Marker == null)
            {
                VisualElement marker = new VisualElement { name = "world-map-group-" + id, pickingMode = PickingMode.Position };
                marker.style.position = Position.Absolute;
                marker.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                Label label = MakeWorldMapHoverLabel(group.Name);
                worldMapGroupLabels[id] = label;
                hoverLabel = label;
                alert = new Label("!") { pickingMode = PickingMode.Ignore };
                alert.style.position = Position.Absolute;
                alert.style.left = new Length(50, LengthUnit.Percent);
                alert.style.top = -18;
                alert.style.fontSize = 18;
                alert.style.unityFontStyleAndWeight = FontStyle.Bold;
                alert.style.color = new Color(0.96f, 0.82f, 0.45f);
                alert.style.display = DisplayStyle.None;
                marker.Add(alert);
                worldMapGroupAlerts[id] = alert;
                string capturedId = id;
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
                marker.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    OnWorldMapGroupClicked(capturedId);
                });
                worldMapGroupLayer.Add(marker);
                entry = (marker, -1);
            }
            if (entry.Direction != direction)
            {
                if (sprite)
                {
                    Sprite s = WorldMapGroupSprite(actor);
                    entry.Marker.style.backgroundImage = s != null ? new StyleBackground(Background.FromSprite(s)) : new StyleBackground(StyleKeyword.None);
                }
                else
                {
                    Texture2D texture = MapLabGameMap.FigureTexture(actor.FigureSet, direction);
                    entry.Marker.style.backgroundImage = texture != null ? new StyleBackground(Background.FromTexture2D(texture)) : new StyleBackground(StyleKeyword.None);
                }
                entry.Direction = direction;
            }
            // Зверь/существо — немного ниже человека, рисунок вписан в квадрат.
            float height = sprite ? figureHeight * 0.85f : figureHeight;
            entry.Marker.style.display = DisplayStyle.Flex;
            // Опора фигуры — низ по центру, в точке группы.
            entry.Marker.style.width = height;
            entry.Marker.style.height = height;
            entry.Marker.style.left = at.XPercent / 100f * worldMapCanvasWidth - height * 0.5f;
            entry.Marker.style.top = at.YPercent / 100f * worldMapCanvasHeight - height * 0.97f;
            // Фигура людей — в сцене карты (сортировка с деревьями и домами);
            // элемент UI остаётся прозрачной целью наведения и клика.
            Texture2D sceneTexture = !sprite && sceneFigureHeight > 0f ? MapLabGameMap.FigureTexture(actor.FigureSet, direction) : null;
            bool inScene = sceneTexture != null && MapLabLiveFigures.Place(live, "group-" + id, sceneTexture, FigureOutline(sceneTexture),
                new Vector2(at.XPercent, at.YPercent), sceneFigureHeight, sceneTexture.width / (float)Mathf.Max(1, sceneTexture.height), new Vector2(0.5f, 0.03f), 0f, true);
            if (!inScene)
                MapLabLiveFigures.Hide("group-" + id);
            entry.Marker.style.unityBackgroundImageTintColor = inScene ? new Color(1f, 1f, 1f, 0f) : Color.white;
            PlaceGroupShadow(id, at.XPercent / 100f * worldMapCanvasWidth - height * 0.5f, at.YPercent / 100f * worldMapCanvasHeight - height * 0.97f, height, !inScene);
            worldMapGroupMarkers[id] = entry;

            bool alerting = group.TargetId == WorldPopulationService.HeroTarget &&
                            (group.Activity == PopulationGroupState.Chase || group.Activity == PopulationGroupState.Observe);
            if (alert != null)
                alert.style.display = alerting ? DisplayStyle.Flex : DisplayStyle.None;
            if (hoverLabel != null)
            {
                hoverLabel.text = WorldPopulationService.HoverText(gameState, group, distance);
                if (hoverLabel.style.display == DisplayStyle.Flex)
                {
                    Vector2 head = MapPercentToWorldMapViewport(at.XPercent, at.YPercent);
                    float lw = float.IsNaN(hoverLabel.resolvedStyle.width) ? 0f : hoverLabel.resolvedStyle.width;
                    hoverLabel.style.translate = new Translate(head.x - lw * 0.5f, head.y - height * worldMapZoom - 22f);
                }
            }
        }

        // Группы, которых больше нет в состоянии (новая карта и т. п.).
        foreach (KeyValuePair<string, (VisualElement Marker, int Direction)> pair in worldMapGroupMarkers)
        {
            if (worldMapGroupSeen.Contains(pair.Key))
                continue;
            worldMapGroupLabels.TryGetValue(pair.Key, out Label label);
            worldMapGroupAlerts.TryGetValue(pair.Key, out Label alert);
            HideWorldMapGroup(pair.Key, pair.Value.Marker, label, alert, figureHeight);
        }
    }

    private void HideWorldMapGroup(string id, VisualElement marker, Label hoverLabel, Label alert, float figureHeight)
    {
        if (marker != null)
            marker.style.display = DisplayStyle.None;
        if (hoverLabel != null)
            hoverLabel.style.display = DisplayStyle.None;
        if (alert != null)
            alert.style.display = DisplayStyle.None;
        PlaceGroupShadow(id, 0f, 0f, figureHeight, false);
        MapLabLiveFigures.Hide("group-" + id);
    }

    private GeneratedMapActor WorldMapGroupDefinition(PopulationGroupState group) =>
        WorldPopulationService.DefinitionOf(gameState, group);

    private Sprite WorldMapGroupSprite(GeneratedMapActor actor)
    {
        if (actor == null || string.IsNullOrEmpty(actor.UnitArtId))
            return null;
        if (worldMapGroupSprites.TryGetValue(actor.UnitArtId, out Sprite cached))
            return cached;
        Sprite sprite = (GameState.UnitStatsProvider as KingdomSurvival.UnitDatabase.UnitDatabaseStatsProvider)?.MapSprite(actor.UnitArtId);
        worldMapGroupSprites[actor.UnitArtId] = sprite;
        return sprite;
    }

    // Клик по группе: разговор рядом с мирной группой; иначе — карточка
    // наблюдения и обычный приказ «Идти сюда».
    private void OnWorldMapGroupClicked(string groupId)
    {
        PopulationGroupState group = WorldPopulationService.FindGroup(gameState, groupId);
        if (group == null || gameState == null)
            return;
        ExpeditionData expedition = gameState.ActiveExpedition;
        float heroX = expedition != null ? expedition.CurrentMapXPercent : WorldMapNavigation.CapitalXPercent;
        float heroY = expedition != null ? expedition.CurrentMapYPercent : WorldMapNavigation.CapitalYPercent;
        if (expedition != null && WorldPopulationService.TryTalk(gameState, groupId, heroX, heroY, out string title, out string text))
        {
            ShowWorldMapInfoCard(title, text, null, null);
            return;
        }
        WorldMapHexGrid grid = worldMapGroupsGrid ?? gameState.GeneratedMap?.CreateGrid();
        if (grid == null)
            return;
        double hour = ContinuousSimulationSystem.GetAbsoluteHours(gameState);
        MapPointData at = WorldPopulationService.GroupPosition(gameState, group, hour, grid, out _, out _, out _);
        GeneratedMapActor actor = WorldMapGroupDefinition(group);
        string description = (actor?.Description ?? string.Empty) + "\n" +
                             WorldPopulationService.HoverText(gameState, group, grid.DistanceHexes(heroX, heroY, at.XPercent, at.YPercent));
        bool talkable = group.Behavior == WorldPopulationBehavior.Peaceful || group.Behavior == WorldPopulationBehavior.Patrol;
        float x = at.XPercent, y = at.YPercent;
        ShowWorldMapInfoCard(group.Name, description.Trim(), talkable ? "Подойти" : null, talkable ? () => GoToWorldMapPoint(x, y) : (System.Action)null);
    }
}
