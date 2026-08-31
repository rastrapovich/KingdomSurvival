using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    private const float WorldMapCapitalXPercent = 50f;
    private const float WorldMapCapitalYPercent = 81f;
    private const float WorldMapNodeCenterXOffsetPercent = 14f;
    private const float WorldMapNodeCenterYOffsetPercent = 9f;
    private const int WorldMapRouteDotCount = 7;

    private VisualElement worldMap;
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

    private void FindWorldMapElements(VisualElement root)
    {
        worldMap = root.Q<VisualElement>("world-map");
        worldMapRoutes = root.Q<VisualElement>("world-map-routes");
        worldMapMarkers = root.Q<VisualElement>("world-map-markers");
        worldMapCapitalButton = root.Q<Button>("world-map-capital-button");
        worldMapArmyMarker = root.Q<VisualElement>("world-map-army-marker");
        worldMapArmyMarkerLabel =
            root.Q<Label>("world-map-army-marker-label");
        worldMapHintLabel = root.Q<Label>("world-map-hint-label");

        mapSelectionCard = root.Q<VisualElement>("map-selection-card");
        mapSelectionTitle = root.Q<Label>("map-selection-title");
        mapSelectionDetails = root.Q<Label>("map-selection-details");
        mapSendButton = root.Q<Button>("map-send-button");

        if (worldMapRoutes != null)
            worldMapRoutes.pickingMode = PickingMode.Ignore;

        if (worldMapArmyMarker != null)
            worldMapArmyMarker.pickingMode = PickingMode.Ignore;
    }

    private bool WorldMapElementsExist()
    {
        return
            worldMap != null &&
            worldMapRoutes != null &&
            worldMapMarkers != null &&
            worldMapCapitalButton != null &&
            worldMapArmyMarker != null &&
            worldMapArmyMarkerLabel != null &&
            worldMapHintLabel != null &&
            mapSelectionCard != null &&
            mapSelectionTitle != null &&
            mapSelectionDetails != null &&
            mapSendButton != null;
    }

    private void RegisterWorldMapCallbacks()
    {
        worldMapCapitalButton.clicked += OnWorldMapCapitalClicked;
        mapSendButton.clicked += OnWorldMapSendClicked;
    }

    private void UnregisterWorldMapCallbacks()
    {
        worldMapCapitalButton.clicked -= OnWorldMapCapitalClicked;
        mapSendButton.clicked -= OnWorldMapSendClicked;
    }

    private void ResetWorldMapSelection()
    {
        selectedMapLocationId = null;
    }

    private void OnWorldMapSendClicked()
    {
        if (string.IsNullOrEmpty(selectedMapLocationId))
            return;

        TrySendExpeditionFromStableUi(selectedMapLocationId);
    }

    private void OnWorldMapCapitalClicked()
    {
        if (!gameState.HasActiveExpedition)
            return;

        OnStableExpeditionActionClicked();
    }

    private void SelectWorldMapLocation(string locationId)
    {
        if (gameState.FindLocation(locationId) == null)
            return;

        selectedMapLocationId = locationId;
        RefreshWorldMapPanel();
    }

    private void RefreshWorldMapPanel()
    {
        if (gameState == null || !WorldMapElementsExist())
            return;

        if (gameState.HasActiveExpedition &&
            string.IsNullOrEmpty(selectedMapLocationId))
        {
            selectedMapLocationId = gameState.ActiveExpedition.LocationId;
        }

        if (!string.IsNullOrEmpty(selectedMapLocationId) &&
            gameState.FindLocation(selectedMapLocationId) == null)
        {
            selectedMapLocationId = null;
        }

        worldMapRoutes.Clear();
        worldMapMarkers.Clear();

        string activeLocationId = gameState.HasActiveExpedition
            ? gameState.ActiveExpedition.LocationId
            : null;

        foreach (LocationData location in gameState.Locations)
        {
            bool isActiveTarget = location.Id == activeLocationId;
            CreateWorldMapRoute(location, isActiveTarget);
            CreateWorldMapNode(location, isActiveTarget);
        }

        RefreshWorldMapHint();
        RefreshWorldMapCapital();
        RefreshWorldMapArmyMarker();
        RefreshWorldMapSelectionCard();
    }

    private void CreateWorldMapRoute(
        LocationData location,
        bool isActiveTarget)
    {
        float targetX =
            location.MapXPercent + WorldMapNodeCenterXOffsetPercent;
        float targetY =
            location.MapYPercent + WorldMapNodeCenterYOffsetPercent;

        for (int i = 1; i <= WorldMapRouteDotCount; i++)
        {
            float progress = i / (float)(WorldMapRouteDotCount + 1);
            VisualElement dot = new VisualElement();
            dot.AddToClassList("world-map-route-dot");

            if (isActiveTarget)
                dot.AddToClassList("world-map-route-dot-active");

            dot.style.left = new Length(
                Mathf.Lerp(WorldMapCapitalXPercent, targetX, progress),
                LengthUnit.Percent);
            dot.style.top = new Length(
                Mathf.Lerp(WorldMapCapitalYPercent, targetY, progress),
                LengthUnit.Percent);
            worldMapRoutes.Add(dot);
        }
    }

    private void CreateWorldMapNode(
        LocationData location,
        bool isActiveTarget)
    {
        string locationId = location.Id;
        Button node = new Button(() => SelectWorldMapLocation(locationId));
        node.name = "world-map-node-" + location.RegionId;
        node.AddToClassList("world-map-node");
        node.style.left = new Length(
            location.MapXPercent,
            LengthUnit.Percent);
        node.style.top = new Length(
            location.MapYPercent,
            LengthUnit.Percent);

        if (location.IsDiscovered)
        {
            node.text =
                location.Name.ToUpper() + "\n" +
                location.RegionName + " · " + location.DistanceDays + " дн.";
            node.tooltip =
                location.RegionName + ". Угроза: " + location.Threat + ".";
            node.AddToClassList("world-map-node-known");

            if (location.IsExplored)
                node.AddToClassList("world-map-node-explored");
        }
        else
        {
            node.text =
                "?\n" + location.RegionName.ToUpper() + "\n" +
                location.DistanceDays + " дн. пути";
            node.tooltip =
                "Неизведанная область. Локация откроется после прибытия.";
            node.AddToClassList("world-map-node-unknown");
        }

        if (location.Id == selectedMapLocationId)
            node.AddToClassList("world-map-node-selected");

        if (isActiveTarget)
            node.AddToClassList("world-map-node-active");

        node.SetEnabled(!isGameOver);
        worldMapMarkers.Add(node);
    }

    private void RefreshWorldMapHint()
    {
        if (!gameState.HasActiveExpedition)
        {
            worldMapHintLabel.text = selectedFighterIds.Count > 0
                ? "Выберите область или открытую локацию. Путь армия пройдёт автоматически."
                : "Сначала перенесите бойцов в гарнизон командира на экране «Армия».";
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        LocationData location = gameState.FindLocation(expedition.LocationId);
        string targetName = location != null
            ? location.TravelTargetName
            : "неизвестная цель";

        if (gameState.HasPendingExpeditionDecision)
        {
            worldMapHintLabel.text =
                "Армия остановилась и ждёт приказа по значимому событию.";
        }
        else if (expedition.Phase == CommanderState.TravellingToLocation)
        {
            worldMapHintLabel.text =
                "Армия идёт к цели «" + targetName + "». Осталось: " +
                expedition.DaysRemaining + " " +
                GetDayWord(expedition.DaysRemaining) + ".";
        }
        else if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            worldMapHintLabel.text =
                "Армия возвращается в столицу. Осталось: " +
                expedition.DaysRemaining + " " +
                GetDayWord(expedition.DaysRemaining) + ".";
        }
        else
        {
            worldMapHintLabel.text =
                "Армия находится в локации «" + targetName + "».";
        }
    }

    private void RefreshWorldMapCapital()
    {
        bool expeditionActive = gameState.HasActiveExpedition;
        bool canOrderReturn = false;

        worldMapCapitalButton.RemoveFromClassList(
            "world-map-capital-return");

        if (expeditionActive)
        {
            ExpeditionData expedition = gameState.ActiveExpedition;
            bool returning =
                expedition.Phase == CommanderState.ReturningToCastle;
            bool exploring = expedition.IsExplorationInProgress;

            canOrderReturn =
                !isGameOver &&
                !gameState.HasPendingExpeditionDecision &&
                !returning &&
                !exploring;

            worldMapCapitalButton.AddToClassList(
                "world-map-capital-return");
            worldMapCapitalButton.text = gameState.CanCancelExpeditionBeforeDayEnd
                ? "СТОЛИЦА\nОТМЕНИТЬ ПРИКАЗ"
                : returning
                    ? "СТОЛИЦА\nАРМИЯ ВОЗВРАЩАЕТСЯ"
                    : "СТОЛИЦА\nВЕРНУТЬ АРМИЮ";
            worldMapCapitalButton.tooltip = canOrderReturn
                ? "Нажмите, чтобы отдать приказ на возвращение"
                : "Сейчас новый приказ на возвращение недоступен";
        }
        else
        {
            worldMapCapitalButton.text = "СТОЛИЦА";
            worldMapCapitalButton.tooltip =
                "Здесь находится армия, когда нет активной экспедиции";
        }

        worldMapCapitalButton.SetEnabled(canOrderReturn);
    }

    private void RefreshWorldMapArmyMarker()
    {
        if (!gameState.HasActiveExpedition)
        {
            worldMapArmyMarker.style.display = DisplayStyle.None;
            return;
        }

        ExpeditionData expedition = gameState.ActiveExpedition;
        LocationData location = gameState.FindLocation(expedition.LocationId);

        if (location == null)
        {
            worldMapArmyMarker.style.display = DisplayStyle.None;
            return;
        }

        float targetX =
            location.MapXPercent + WorldMapNodeCenterXOffsetPercent;
        float targetY =
            location.MapYPercent + WorldMapNodeCenterYOffsetPercent;
        float markerX;
        float markerY;

        if (expedition.Phase == CommanderState.TravellingToLocation)
        {
            float progress = GetWorldMapLegProgress(expedition);
            markerX = Mathf.Lerp(WorldMapCapitalXPercent, targetX, progress);
            markerY = Mathf.Lerp(WorldMapCapitalYPercent, targetY, progress);
        }
        else if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            float progress = GetWorldMapLegProgress(expedition);
            float returnStartFraction = Mathf.Clamp01(
                expedition.LegTotalDays / (float)Mathf.Max(1, location.DistanceDays));
            float returnStartX = Mathf.Lerp(
                WorldMapCapitalXPercent,
                targetX,
                returnStartFraction);
            float returnStartY = Mathf.Lerp(
                WorldMapCapitalYPercent,
                targetY,
                returnStartFraction);

            markerX = Mathf.Lerp(
                returnStartX,
                WorldMapCapitalXPercent,
                progress);
            markerY = Mathf.Lerp(
                returnStartY,
                WorldMapCapitalYPercent,
                progress);
        }
        else
        {
            markerX = targetX;
            markerY = targetY;
        }

        worldMapArmyMarker.style.left = new Length(
            Mathf.Clamp(markerX - 7f, 0f, 88f),
            LengthUnit.Percent);
        worldMapArmyMarker.style.top = new Length(
            Mathf.Clamp(markerY - 4f, 1f, 90f),
            LengthUnit.Percent);
        worldMapArmyMarker.style.display = DisplayStyle.Flex;

        if (gameState.HasPendingExpeditionDecision)
        {
            worldMapArmyMarkerLabel.text = "ЖДЁТ ПРИКАЗА";
        }
        else if (expedition.Phase == CommanderState.TravellingToLocation)
        {
            worldMapArmyMarkerLabel.text =
                "→ " + expedition.DaysRemaining + " ДН.";
        }
        else if (expedition.Phase == CommanderState.ReturningToCastle)
        {
            worldMapArmyMarkerLabel.text =
                "← " + expedition.DaysRemaining + " ДН.";
        }
        else if (expedition.IsExplorationInProgress)
        {
            worldMapArmyMarkerLabel.text = "ИССЛЕДУЕТ";
        }
        else
        {
            worldMapArmyMarkerLabel.text = "НА МЕСТЕ";
        }
    }

    private float GetWorldMapLegProgress(ExpeditionData expedition)
    {
        int totalDays = Mathf.Max(1, expedition.LegTotalDays);
        return Mathf.Clamp01(
            (totalDays - expedition.DaysRemaining) / (float)totalDays);
    }

    private void RefreshWorldMapSelectionCard()
    {
        LocationData selectedLocation = string.IsNullOrEmpty(selectedMapLocationId)
            ? null
            : gameState.FindLocation(selectedMapLocationId);

        if (selectedLocation == null)
        {
            mapSelectionTitle.text = "ВЫБЕРИТЕ ОБЛАСТЬ НА КАРТЕ";
            mapSelectionDetails.text =
                "Три области доступны с начала партии, но локации внутри них неизвестны.";
            mapSendButton.text = "ВЫБЕРИТЕ ЦЕЛЬ";
            mapSendButton.SetEnabled(false);
            return;
        }

        if (selectedLocation.IsDiscovered)
        {
            mapSelectionTitle.text = selectedLocation.Name.ToUpper();
            mapSelectionDetails.text =
                selectedLocation.RegionName + " · расстояние: " +
                selectedLocation.DistanceDays + " " +
                GetDayWord(selectedLocation.DistanceDays) + "\n" +
                "Угроза: " + selectedLocation.Threat + ". " +
                GetWorldMapLocationStatus(selectedLocation);
        }
        else
        {
            mapSelectionTitle.text = selectedLocation.RegionName.ToUpper();
            mapSelectionDetails.text =
                "Расстояние: " + selectedLocation.DistanceDays + " " +
                GetDayWord(selectedLocation.DistanceDays) + ". " +
                "Неизведанная область: по прибытии разведчики обнаружат " +
                "скрытую локацию. Угроза и добыча пока неизвестны.";
        }

        bool canSend =
            !isGameOver &&
            !gameState.HasActiveExpedition &&
            selectedFighterIds.Count > 0;

        if (gameState.HasActiveExpedition)
        {
            mapSendButton.text = "АРМИЯ УЖЕ В ПОХОДЕ";
        }
        else if (selectedFighterIds.Count == 0)
        {
            mapSendButton.text = "СНАЧАЛА ВЫБЕРИТЕ БОЙЦОВ";
        }
        else
        {
            mapSendButton.text = selectedLocation.IsDiscovered
                ? "ОТПРАВИТЬ В ЛОКАЦИЮ"
                : "ОТПРАВИТЬ НА РАЗВЕДКУ";
        }

        mapSendButton.SetEnabled(canSend);
    }

    private string GetWorldMapLocationStatus(LocationData location)
    {
        if (location.IsExplored)
            return "Статус: исследована.";

        if (gameState.HasActiveExpedition &&
            gameState.ActiveExpedition.LocationId == location.Id)
        {
            return "Статус: цель активной экспедиции.";
        }

        return "Статус: обнаружена, но не исследована.";
    }
}
