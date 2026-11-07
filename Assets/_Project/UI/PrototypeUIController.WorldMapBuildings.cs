using System.Collections.Generic;
using KingdomSurvival.MapLabBridge;
using UnityEngine;
using UnityEngine.UIElements;

// 12У-G (мастер-бриф перехода на MapLab, поставка «Отдельные здания»;
// задача подсветки до клика из пакета выбранных мест): внутри поселения на
// карте MapLab здания его услуг — отдельные цели. Наведение подсвечивает
// здание и называет услугу, клик — отряд подходит, открывается окно этой
// услуги (тот же код действий поселения, без второй копии). Общий экран
// поселения больше не открывается сам при входе: он доступен кнопкой
// «Поселение»; выход из окна услуги возвращает на карту поселения, уход из
// поселения — кнопка «Уйти». Без рисунка MapLab (прежний рисовальщик,
// автотесты) поведение прежнее: экран поселения открывается сам.
public partial class PrototypeUIController
{
    private VisualElement worldMapBuildingLayer;
    private VisualElement settlementMapPanel;
    private Label settlementMapTitle;
    private string worldMapBuildingsShownFor;
    private MapLabGameMap.Result worldMapBuildingsShownResult;

    private static readonly string[] ServiceRoleOrder = { "tavern", "merchant", "board", "healer", "elder" };

    private static string ServiceRoleTitle(string role)
    {
        switch (role)
        {
            case "tavern": return "Корчма";
            case "merchant": return "Лавка";
            case "board": return "Столб объявлений";
            case "healer": return "Знахарка";
            case "elder": return "Разговор";
            default: return "Поселение";
        }
    }

    // Услуги поселения, у которых есть здание: по одному действию на роль, в
    // порядке ServiceRoleOrder. Набор зависит только от данных профиля, не от
    // текущих условий: здание стоит всегда, доступность проверяется при клике.
    private List<(string Role, SettlementActionDefinition Action)> SettlementBuildingActions(string locationId)
    {
        List<(string, SettlementActionDefinition)> result = new List<(string, SettlementActionDefinition)>();
        SettlementProfileDefinition profile = gameState != null ? SettlementService.Profile(gameState, locationId) : null;
        if (profile?.Actions == null)
            return result;
        foreach (string role in ServiceRoleOrder)
        {
            foreach (SettlementActionDefinition action in profile.Actions)
            {
                if (action == null || RoleOf(action) != role || !BuildingActionWired(profile, action, locationId))
                    continue;
                result.Add((role, action));
                break;
            }
        }
        return result;
    }

    private static string RoleOf(SettlementActionDefinition action)
    {
        switch (action.Type)
        {
            case SettlementActionType.Tavern: return "tavern";
            case SettlementActionType.Trade: return "merchant";
            case SettlementActionType.NoticeBoard: return "board";
            case SettlementActionType.Healer: return "healer";
            case SettlementActionType.Dialogue: return "elder";
            default: return null;
        }
    }

    private bool BuildingActionWired(SettlementProfileDefinition profile, SettlementActionDefinition action, string locationId)
    {
        switch (action.Type)
        {
            case SettlementActionType.Dialogue: return !string.IsNullOrEmpty(action.TargetId) && SettlementDialogueExists(action.TargetId);
            case SettlementActionType.Trade: return profile.FindMerchant(action.TargetId) != null;
            case SettlementActionType.Tavern: return TavernService.Tavern(gameState, locationId) != null;
            case SettlementActionType.NoticeBoard: return NoticeBoardService.Board(gameState, locationId) != null;
            case SettlementActionType.Healer: return true;
            default: return false;
        }
    }

    // Услуги всех поселений партии — для снимка карты (здания рисуются сразу).
    private Dictionary<string, List<string>> MapLabSettlementServices()
    {
        Dictionary<string, List<string>> services = new Dictionary<string, List<string>>();
        if (gameState?.Settlements?.Instances == null)
            return services;
        foreach (SettlementInstanceData instance in gameState.Settlements.Instances)
        {
            if (instance == null || string.IsNullOrEmpty(instance.LocationId))
                continue;
            List<string> roles = new List<string>();
            foreach ((string role, SettlementActionDefinition _) in SettlementBuildingActions(instance.LocationId))
                roles.Add(role);
            if (roles.Count > 0)
                services[instance.LocationId] = roles;
        }
        return services;
    }

    // Поселение показано зданиями (есть рисунок MapLab и здания этого места).
    private bool SettlementMapMode(string locationId)
    {
        if (worldMapMapLab == null || string.IsNullOrEmpty(locationId))
            return false;
        foreach ((string id, string _, Rect _) in worldMapMapLab.Buildings)
            if (id == locationId)
                return true;
        return false;
    }

    private void EnsureWorldMapBuildingUi()
    {
        if (worldMap == null)
            return;
        if (worldMapBuildingLayer == null)
        {
            worldMapBuildingLayer = new VisualElement { name = "world-map-buildings", pickingMode = PickingMode.Ignore };
            worldMapBuildingLayer.style.position = Position.Absolute;
            worldMapBuildingLayer.style.left = 0;
            worldMapBuildingLayer.style.top = 0;
            worldMapBuildingLayer.style.right = 0;
            worldMapBuildingLayer.style.bottom = 0;
            VisualElement parent = worldMapArtLayers?.parent ?? worldMap;
            int index = worldMapArtLayers != null ? parent.IndexOf(worldMapArtLayers) + 1 : parent.childCount;
            parent.Insert(Mathf.Clamp(index, 0, parent.childCount), worldMapBuildingLayer);
        }
        if (settlementMapPanel == null && worldMapViewport != null)
        {
            settlementMapPanel = new VisualElement { name = "settlement-map-panel" };
            settlementMapPanel.style.position = Position.Absolute;
            settlementMapPanel.style.top = 12;
            settlementMapPanel.style.alignSelf = Align.Center;
            settlementMapPanel.style.left = Length.Percent(50);
            settlementMapPanel.style.translate = new Translate(Length.Percent(-50), 0);
            settlementMapPanel.style.flexDirection = FlexDirection.Row;
            settlementMapPanel.style.alignItems = Align.Center;
            settlementMapPanel.style.paddingLeft = 10;
            settlementMapPanel.style.paddingRight = 10;
            settlementMapPanel.style.paddingTop = 6;
            settlementMapPanel.style.paddingBottom = 6;
            settlementMapPanel.style.backgroundColor = new Color(0.16f, 0.13f, 0.1f, 0.86f);
            settlementMapPanel.style.borderTopLeftRadius = settlementMapPanel.style.borderTopRightRadius =
                settlementMapPanel.style.borderBottomLeftRadius = settlementMapPanel.style.borderBottomRightRadius = 6;
            settlementMapTitle = new Label { name = "settlement-map-title" };
            settlementMapTitle.style.color = new Color(0.95f, 0.9f, 0.78f);
            settlementMapTitle.style.marginRight = 10;
            settlementMapPanel.Add(settlementMapTitle);
            Button general = new Button(() => OpenSettlementFromMap(null)) { text = "Поселение", name = "settlement-map-general" };
            general.tooltip = "Общий вид поселения: люди, ночлег, прогулка и все дела.";
            settlementMapPanel.Add(general);
            Button leave = new Button(LeaveSettlementFromMap) { text = "Уйти", name = "settlement-map-leave" };
            leave.tooltip = "Выйти из поселения к стоянке у входа.";
            settlementMapPanel.Add(leave);
            settlementMapPanel.style.display = DisplayStyle.None;
            worldMapViewport.Add(settlementMapPanel);
        }
    }

    // Каждый кадр: дешёвая проверка, перестройка — только при смене поселения
    // или рисунка; при открытом окне поселения цели и панель скрыты.
    private void TickWorldMapBuildings()
    {
        EnsureWorldMapBuildingUi();
        if (worldMapBuildingLayer == null)
            return;
        string inside = gameState != null && !isGameOver ? SettlementCampService.InsideLocationId(gameState) : string.Empty;
        bool show = !string.IsNullOrEmpty(inside) && SettlementMapMode(inside);
        if (!show)
            inside = string.Empty;
        if (inside != worldMapBuildingsShownFor || !ReferenceEquals(worldMapBuildingsShownResult, worldMapMapLab))
            BuildWorldMapBuildingTargets(inside);
        bool screen = IsSettlementScreenOpen;
        worldMapBuildingLayer.style.display = show && !screen ? DisplayStyle.Flex : DisplayStyle.None;
        if (settlementMapPanel != null)
        {
            settlementMapPanel.style.display = show && !screen ? DisplayStyle.Flex : DisplayStyle.None;
            if (show && settlementMapTitle != null)
                settlementMapTitle.text = gameState.FindLocation(inside)?.Name ?? "Поселение";
        }
    }

    private void BuildWorldMapBuildingTargets(string locationId)
    {
        worldMapBuildingLayer.Clear();
        worldMapBuildingsShownFor = locationId;
        worldMapBuildingsShownResult = worldMapMapLab;
        if (string.IsNullOrEmpty(locationId) || worldMapMapLab == null)
            return;
        foreach ((string id, string role, Rect rect) in worldMapMapLab.Buildings)
        {
            if (id != locationId)
                continue;
            string capturedRole = role;
            VisualElement target = new VisualElement { name = "world-map-building-" + role, pickingMode = PickingMode.Position };
            target.style.position = Position.Absolute;
            target.style.left = Length.Percent(rect.x);
            target.style.top = Length.Percent(rect.y);
            target.style.width = Length.Percent(rect.width);
            target.style.height = Length.Percent(rect.height);
            target.tooltip = ServiceRoleTitle(role);
            // Подсветка — тот же рисунок здания поверх снимка, светлее и чуть
            // крупнее; в покое невидима. Подпись — над зданием.
            VisualElement glow = new VisualElement { pickingMode = PickingMode.Ignore };
            glow.style.position = Position.Absolute;
            glow.style.left = 0;
            glow.style.top = 0;
            glow.style.right = 0;
            glow.style.bottom = 0;
            Texture2D texture = MapLabGameMap.ServiceTexture(role);
            if (texture != null)
                glow.style.backgroundImage = new StyleBackground(Background.FromTexture2D(texture));
            glow.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
            glow.style.opacity = 0f;
            glow.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(92));
            glow.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity"), new StylePropertyName("scale") };
            glow.style.transitionDuration = new List<TimeValue> { new TimeValue(0.1f, TimeUnit.Second) };
            target.Add(glow);
            Label label = new Label(ServiceRoleTitle(role)) { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.bottom = Length.Percent(100);
            label.style.left = Length.Percent(50);
            label.style.translate = new Translate(Length.Percent(-50), 0);
            label.style.color = new Color(0.98f, 0.93f, 0.8f);
            label.style.backgroundColor = new Color(0.18f, 0.14f, 0.1f, 0.82f);
            label.style.paddingLeft = label.style.paddingRight = 6;
            label.style.borderTopLeftRadius = label.style.borderTopRightRadius = label.style.borderBottomLeftRadius = label.style.borderBottomRightRadius = 4;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.display = DisplayStyle.None;
            target.Add(label);
            target.RegisterCallback<PointerEnterEvent>(_ =>
            {
                glow.style.opacity = 0.55f;
                glow.style.scale = new Scale(new Vector3(1.05f, 1.05f, 1f));
                glow.style.unityBackgroundImageTintColor = new Color(1f, 0.97f, 0.88f, 1f);
                label.style.display = DisplayStyle.Flex;
            });
            target.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                glow.style.opacity = 0f;
                glow.style.scale = new Scale(Vector3.one);
                label.style.display = DisplayStyle.None;
            });
            target.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                OpenSettlementFromMap(capturedRole);
            });
            worldMapBuildingLayer.Add(target);
        }
    }

    // Подход к зданию и окно его услуги. role == null — общий вид поселения.
    private void OpenSettlementFromMap(string role)
    {
        string inside = gameState != null ? SettlementCampService.InsideLocationId(gameState) : string.Empty;
        if (string.IsNullOrEmpty(inside) || IsSettlementScreenOpen)
            return;
        OpenSettlementScreen(inside, SettlementMode.Main);
        if (string.IsNullOrEmpty(role))
            return;
        foreach ((string r, SettlementActionDefinition action) in SettlementBuildingActions(inside))
        {
            if (r != role)
                continue;
            NarrativeEvaluationContext context = SettlementService.Context(gameState);
            bool enabled = (action.ShowConditions?.Evaluate(context) ?? true) && (action.EnableConditions?.Evaluate(context) ?? true);
            if (!enabled)
            {
                settlementMessageText = string.IsNullOrWhiteSpace(action.DisabledReason) ? ServiceRoleTitle(role) + ": сейчас закрыто." : action.DisabledReason;
                RenderSettlement();
                return;
            }
            RunSettlementAction(action);
            return;
        }
    }

    private void LeaveSettlementFromMap()
    {
        if (gameState == null || IsSettlementScreenOpen)
            return;
        string inside = SettlementCampService.InsideLocationId(gameState);
        if (string.IsNullOrEmpty(inside))
            return;
        bool hadCamp = SettlementCampService.HasCamp(gameState);
        SettlementCampService.ReturnToCamp(gameState);
        RefreshInterface();
        Autosave();
        if (hadCamp)
            TryOpenLocationInteraction(inside);
        else
            AddReport("Отряд вышел из поселения.");
    }
}
