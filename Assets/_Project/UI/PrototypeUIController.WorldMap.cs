using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private VisualElement worldMap;
    private VisualElement worldMapTerrain;
    private VisualElement worldMapRoutes;
    private VisualElement worldMapMarkers;
    private Button worldMapCapitalButton;
    private VisualElement worldMapArmyMarker;
    private Label worldMapArmyMarkerLabel;
    private Label worldMapHintLabel;
    private VisualElement mapSelectionCard;
    private Label mapSelectionTitle;
    private Label mapSelectionDetails;
    private Button mapSendButton;
    private string selectedMapLocationId;
    private bool hasSelectedMapPoint;
    private float selectedMapXPercent;
    private float selectedMapYPercent;
    private List<MapPointData> selectedMapRoute = new List<MapPointData>();

    private void FindWorldMapElements(VisualElement root)
    {
        worldMap = root.Q<VisualElement>("world-map");
        worldMapTerrain = root.Q<VisualElement>("world-map-terrain");
        worldMapRoutes = root.Q<VisualElement>("world-map-routes");
        worldMapMarkers = root.Q<VisualElement>("world-map-markers");
        worldMapCapitalButton = root.Q<Button>("world-map-capital-button");
        worldMapArmyMarker = root.Q<VisualElement>("world-map-army-marker");
        worldMapArmyMarkerLabel = root.Q<Label>("world-map-army-marker-label");
        worldMapHintLabel = root.Q<Label>("world-map-hint-label");
        mapSelectionCard = root.Q<VisualElement>("map-selection-card");
        mapSelectionTitle = root.Q<Label>("map-selection-title");
        mapSelectionDetails = root.Q<Label>("map-selection-details");
        mapSendButton = root.Q<Button>("map-send-button");
        if (worldMapTerrain != null) worldMapTerrain.pickingMode = PickingMode.Ignore;
        if (worldMapRoutes != null) worldMapRoutes.pickingMode = PickingMode.Ignore;
        if (worldMapArmyMarker != null) worldMapArmyMarker.pickingMode = PickingMode.Ignore;
    }

    private bool WorldMapElementsExist() =>
        worldMap != null && worldMapTerrain != null && worldMapRoutes != null &&
        worldMapMarkers != null && worldMapCapitalButton != null &&
        worldMapArmyMarker != null && worldMapArmyMarkerLabel != null &&
        worldMapHintLabel != null && mapSelectionCard != null &&
        mapSelectionTitle != null && mapSelectionDetails != null && mapSendButton != null;

    private void RegisterWorldMapCallbacks()
    {
        worldMap.RegisterCallback<PointerDownEvent>(OnWorldMapPointerDown);
        worldMapCapitalButton.clicked += OnWorldMapCapitalClicked;
        mapSendButton.clicked += OnWorldMapSendClicked;
    }

    private void UnregisterWorldMapCallbacks()
    {
        worldMap.UnregisterCallback<PointerDownEvent>(OnWorldMapPointerDown);
        worldMapCapitalButton.clicked -= OnWorldMapCapitalClicked;
        mapSendButton.clicked -= OnWorldMapSendClicked;
    }

    private void ResetWorldMapSelection()
    {
        selectedMapLocationId = null;
        hasSelectedMapPoint = false;
        selectedMapRoute.Clear();
    }

    private void OnWorldMapPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || isGameOver || gameState.HasActiveExpedition)
            return;
        VisualElement clicked = evt.target as VisualElement;
        if (clicked != worldMap && clicked != worldMapTerrain &&
            clicked != worldMapRoutes && clicked != worldMapMarkers)
            return;
        Vector2 local = worldMap.WorldToLocal(evt.position);
        float width = Math.Max(1f, worldMap.resolvedStyle.width);
        float height = Math.Max(1f, worldMap.resolvedStyle.height);
        SelectWorldMapPoint(
            WorldMapNavigation.ClampMapX(local.x / width * 100f),
            WorldMapNavigation.ClampMapY(local.y / height * 100f));
        evt.StopPropagation();
    }

    private void SelectWorldMapPoint(float xPercent, float yPercent)
    {
        selectedMapLocationId = null;
        hasSelectedMapPoint = true;
        selectedMapXPercent = xPercent;
        selectedMapYPercent = yPercent;
        selectedMapRoute = WorldMapNavigation.FindPath(
            WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent,
            xPercent, yPercent);
        if (selectedMapRoute.Count > 0)
        {
            MapPointData snappedTarget = selectedMapRoute[selectedMapRoute.Count - 1];
            selectedMapXPercent = snappedTarget.XPercent;
            selectedMapYPercent = snappedTarget.YPercent;
        }
        RefreshWorldMapPanel();
    }

    private void SelectWorldMapLocation(string locationId)
    {
        LocationData location = gameState.FindLocation(locationId);
        if (location == null || !location.IsVisibleOnMap) return;
        selectedMapLocationId = location.Id;
        hasSelectedMapPoint = true;
        selectedMapXPercent = location.MapXPercent;
        selectedMapYPercent = location.MapYPercent;
        selectedMapRoute = WorldMapNavigation.FindPath(
            WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent,
            selectedMapXPercent, selectedMapYPercent);
        RefreshWorldMapPanel();
    }

    private void OnWorldMapSendClicked()
    {
        if (!hasSelectedMapPoint) return;
        string resultMessage;
        bool started = gameState.TryStartExpeditionToMapPoint(
            selectedMapXPercent, selectedMapYPercent, selectedMapLocationId,
            string.IsNullOrEmpty(selectedMapLocationId),
            new List<string>(selectedFighterIds), out resultMessage);
        AddReport(resultMessage);
        if (started)
        {
            CommanderData commander = gameState.FindCommander(
                gameState.ActiveExpedition.CommanderId);
            if (commander != null) commander.State = CommanderState.InCastle;
        }
        RefreshStableUiAfterStateChange();
    }

    private void OnWorldMapCapitalClicked()
    {
        if (gameState.HasActiveExpedition) OnStableExpeditionActionClicked();
    }

    private void RefreshWorldMapPanel()
    {
        if (gameState == null || !WorldMapElementsExist()) return;
        worldMapTerrain.Clear();
        worldMapRoutes.Clear();
        worldMapMarkers.Clear();
        DrawBlockedTerrain();
        foreach (LocationData location in gameState.Locations)
            if (location.IsVisibleOnMap) CreateWorldMapNode(location);
        if (gameState.HasActiveExpedition)
            DrawRoute(gameState.ActiveExpedition.Route, "world-map-route-dot-active");
        else if (hasSelectedMapPoint)
            DrawRoute(selectedMapRoute, "world-map-route-dot-preview");
        if (hasSelectedMapPoint && !gameState.HasActiveExpedition) CreateDestinationMarker();
        RefreshWorldMapHint();
        RefreshWorldMapCapital();
        RefreshWorldMapArmyMarker();
        RefreshWorldMapSelectionCard();
    }

    private void DrawBlockedTerrain()
    {
        for (int y = 0; y < WorldMapNavigation.GridHeight; y++)
        for (int x = 0; x < WorldMapNavigation.GridWidth; x++)
        {
            float px = x * 100f / (WorldMapNavigation.GridWidth - 1);
            float py = y * 100f / (WorldMapNavigation.GridHeight - 1);
            if (!WorldMapNavigation.IsBlockedPercent(px, py)) continue;
            VisualElement cell = new VisualElement();
            cell.AddToClassList("world-map-blocked-cell");
            cell.style.left = new Length(px, LengthUnit.Percent);
            cell.style.top = new Length(py, LengthUnit.Percent);
            worldMapTerrain.Add(cell);
        }
    }

    private void DrawRoute(List<MapPointData> route, string extraClass)
    {
        if (route == null) return;
        for (int i = 1; i < route.Count; i++)
        {
            VisualElement dot = new VisualElement();
            dot.AddToClassList("world-map-route-dot");
            dot.AddToClassList(extraClass);
            dot.style.left = new Length(route[i].XPercent, LengthUnit.Percent);
            dot.style.top = new Length(route[i].YPercent, LengthUnit.Percent);
            worldMapRoutes.Add(dot);
        }
    }

    private void CreateWorldMapNode(LocationData location)
    {
        string id = location.Id;
        Button node = new Button(() => SelectWorldMapLocation(id));
        node.name = "world-map-node-" + id;
        node.text = location.IsExplored ? "✓" : "●";
        node.tooltip = location.Name + "\n" + location.RegionName + "\nУгроза: " + location.Threat;
        node.AddToClassList("world-map-node");
        node.AddToClassList("world-map-node-known");
        node.style.left = new Length(location.MapXPercent, LengthUnit.Percent);
        node.style.top = new Length(location.MapYPercent, LengthUnit.Percent);
        if (location.IsExplored) node.AddToClassList("world-map-node-explored");
        if (location.Id == selectedMapLocationId) node.AddToClassList("world-map-node-selected");
        if (gameState.HasActiveExpedition && location.Id == gameState.ActiveExpedition.LocationId)
            node.AddToClassList("world-map-node-active");
        node.SetEnabled(!isGameOver && !gameState.HasActiveExpedition);
        worldMapMarkers.Add(node);
    }

    private void CreateDestinationMarker()
    {
        VisualElement marker = new VisualElement();
        marker.AddToClassList("world-map-destination-marker");
        marker.style.left = new Length(selectedMapXPercent, LengthUnit.Percent);
        marker.style.top = new Length(selectedMapYPercent, LengthUnit.Percent);
        worldMapMarkers.Add(marker);
    }

    private void RefreshWorldMapHint()
    {
        if (!gameState.HasActiveExpedition)
        {
            worldMapHintLabel.text = selectedFighterIds.Count > 0
                ? "Нажмите на любую точку для разведки или выберите кружок известной локации. Маршрут рассчитывается автоматически."
                : "Сначала перенесите бойцов в гарнизон командира на экране «Армия».";
            return;
        }
        ExpeditionData expedition = gameState.ActiveExpedition;
        if (gameState.HasPendingExpeditionDecision)
            worldMapHintLabel.text = "Армия остановилась и ждёт приказа по значимому событию.";
        else if (expedition.Phase == CommanderState.TravellingToLocation)
            worldMapHintLabel.text = "Армия движется к цели. Осталось: " + expedition.DaysRemaining + " " + GetDayWord(expedition.DaysRemaining) + ".";
        else if (expedition.Phase == CommanderState.ReturningToCastle)
            worldMapHintLabel.text = "Армия возвращается с текущей позиции. Осталось: " + expedition.DaysRemaining + " " + GetDayWord(expedition.DaysRemaining) + ".";
        else worldMapHintLabel.text = "Армия находится в выбранной точке карты.";
    }

    private void RefreshWorldMapCapital()
    {
        bool active = gameState.HasActiveExpedition;
        worldMapCapitalButton.RemoveFromClassList("world-map-capital-return");
        if (active) worldMapCapitalButton.AddToClassList("world-map-capital-return");
        worldMapCapitalButton.text = active ? "ВЕРНУТЬСЯ\nВ СТОЛИЦУ" : "СТОЛИЦА";
        worldMapCapitalButton.SetEnabled(active && !gameState.HasPendingExpeditionDecision &&
            gameState.ActiveExpedition.Phase != CommanderState.ReturningToCastle &&
            !gameState.ActiveExpedition.IsExplorationInProgress);
    }

    private void RefreshWorldMapArmyMarker()
    {
        if (!gameState.HasActiveExpedition)
        {
            worldMapArmyMarker.style.display = DisplayStyle.None;
            return;
        }
        ExpeditionData expedition = gameState.ActiveExpedition;
        worldMapArmyMarker.style.display = DisplayStyle.Flex;
        worldMapArmyMarker.style.left = new Length(expedition.CurrentMapXPercent, LengthUnit.Percent);
        worldMapArmyMarker.style.top = new Length(expedition.CurrentMapYPercent, LengthUnit.Percent);
        worldMapArmyMarkerLabel.text = expedition.DaysRemaining > 0 ? expedition.DaysRemaining + " дн." : "на месте";
    }

    private void RefreshWorldMapSelectionCard()
    {
        if (gameState.HasActiveExpedition)
        {
            mapSelectionTitle.text = "АКТИВНЫЙ МАРШРУТ";
            mapSelectionDetails.text = "Маркер показывает фактическую позицию армии. Возврат строится отсюда, а не от конечной цели.";
            mapSendButton.text = "АРМИЯ УЖЕ В ПОХОДЕ";
            mapSendButton.SetEnabled(false);
            return;
        }
        if (!hasSelectedMapPoint)
        {
            mapSelectionTitle.text = "ВЫБЕРИТЕ ТОЧКУ НА КАРТЕ";
            mapSelectionDetails.text = "Пустая точка запускает разведку сектора. Кружок отправляет армию в известную локацию.";
            mapSendButton.text = "ВЫБЕРИТЕ ЦЕЛЬ";
            mapSendButton.SetEnabled(false);
            return;
        }
        LocationData location = string.IsNullOrEmpty(selectedMapLocationId) ? null : gameState.FindLocation(selectedMapLocationId);
        int days = WorldMapNavigation.CalculateDays(selectedMapRoute);
        int expectedSupply = days * gameState.ExpeditionSupplyConsumption;
        mapSelectionTitle.text = location != null ? location.Name.ToUpper() : "РАЗВЕДКА СЕКТОРА";
        mapSelectionDetails.text = GameState.GetRegionName(selectedMapXPercent, selectedMapYPercent) +
            " · путь: " + days + " " + GetDayWord(days) + " · снабжение в одну сторону: " + expectedSupply + ".\n" +
            (location != null ? "Угроза: " + location.Threat + ". " + GetWorldMapLocationStatus(location)
                : "По прибытии откроется одна из ещё не найденных локаций текущего правления.");
        bool canSend = !isGameOver && selectedFighterIds.Count > 0 && days > 0;
        mapSendButton.text = selectedFighterIds.Count == 0 ? "СНАЧАЛА ВЫБЕРИТЕ БОЙЦОВ"
            : location != null ? "ОТПРАВИТЬ В ЛОКАЦИЮ" : "ОТПРАВИТЬ НА РАЗВЕДКУ";
        mapSendButton.SetEnabled(canSend);
    }

    private static string GetWorldMapLocationStatus(LocationData location)
    {
        if (location.IsExplored) return "Состояние: исследована.";
        if (location.ExplorationDays > 0) return "Состояние: доступна для исследования.";
        return "Состояние: обнаружена.";
    }
}
