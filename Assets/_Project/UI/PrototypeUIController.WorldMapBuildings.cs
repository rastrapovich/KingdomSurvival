using System.Collections.Generic;
using KingdomSurvival.MapLabBridge;
using KingdomSurvival.WorldMapVisual;
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

    // Цель здания: курсор ловит только непрозрачная часть рисунка (маска
    // альфы), прозрачные углы и просветы — нет.
    private sealed class MaskedTarget : VisualElement
    {
        public MapLabSpriteMask Mask;

        public override bool ContainsPoint(Vector2 localPoint)
        {
            if (!base.ContainsPoint(localPoint))
                return false;
            if (Mask == null || layout.width <= 0f || layout.height <= 0f)
                return true;
            return Mask.Hit(localPoint.x / layout.width, localPoint.y / layout.height);
        }
    }

    private string worldMapApproachRole;
    private string worldMapPendingRole, worldMapPendingSettlement;
    private readonly List<(Label Label, Rect Rect)> worldMapBuildingLabels = new List<(Label, Rect)>();

    // Подписи зданий — над верхним краем рисунка, по центру, в координатах экрана.
    private void PlaceWorldMapBuildingLabels(bool visible)
    {
        foreach ((Label label, Rect rect) in worldMapBuildingLabels)
        {
            if (!visible)
            {
                label.style.display = DisplayStyle.None;
                continue;
            }
            if (label.style.display == DisplayStyle.None)
                continue;
            Vector2 top = MapPercentToWorldMapViewport(rect.center.x, rect.yMin);
            float width = float.IsNaN(label.resolvedStyle.width) ? 0f : label.resolvedStyle.width;
            float height = float.IsNaN(label.resolvedStyle.height) ? 0f : label.resolvedStyle.height;
            label.style.translate = new Translate(top.x - width * 0.5f, top.y - height - 4f);
        }
    }
    private readonly Dictionary<string, (VisualElement Glow, Label Label)> worldMapBuildingGlows = new Dictionary<string, (VisualElement, Label)>();

    // Герой идёт по поселению к двери здания (подсветка и подпись держатся),
    // у двери — окно услуги.
    private void StartWalkToBuilding(string settlementId, string role)
    {
        Vector2 door = Vector2.zero;
        bool found = false;
        MapLabGameLive.World world = MapLabGameLive.Current;
        if (world != null)
            foreach ((string id, string r, Rect _, Vector2 d) in world.Buildings)
                if (id == settlementId && r == role)
                {
                    door = d;
                    found = true;
                }
        if (!found && worldMapMapLab != null)
            foreach ((string id, string r, Rect rect) in worldMapMapLab.Buildings)
                if (id == settlementId && r == role)
                {
                    door = new Vector2(rect.center.x, rect.yMax);
                    found = true;
                }
        worldMapApproachRole = role;
        if (worldMapBuildingGlows.TryGetValue(GlowKey(settlementId, role), out (VisualElement Glow, Label Label) ui))
        {
            ui.Glow.style.opacity = 1f;
            ui.Label.text = ServiceRoleTitle(role) + " — идём…";
            ui.Label.style.display = DisplayStyle.Flex;
        }
        void Arrive()
        {
            worldMapApproachRole = null;
            if (worldMapBuildingGlows.TryGetValue(GlowKey(settlementId, role), out (VisualElement Glow, Label Label) done))
            {
                done.Glow.style.opacity = 0f;
                done.Label.text = ServiceRoleTitle(role);
                done.Label.style.display = DisplayStyle.None;
            }
            OpenSettlementFromMap(role);
        }
        if (found)
            WalkWorldMapHeroTo(door, Arrive);
        else
            Arrive();
    }

    private static string GlowKey(string settlementId, string role) => settlementId + "|" + role;

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
            // Над слоями значков, маршрутов и дорог: каждый из них во всю карту
            // и ловит курсор, поэтому цели зданий лежат выше (под маркером отряда).
            VisualElement parent = worldMapMarkers?.parent ?? worldMap;
            int index = worldMapMarkers != null ? parent.IndexOf(worldMapMarkers) + 1 : parent.childCount;
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
            StyleSettlementMapButton(general);
            settlementMapPanel.Add(general);
            Button leave = new Button(LeaveSettlementFromMap) { text = "Уйти", name = "settlement-map-leave" };
            leave.tooltip = "Выйти из поселения к стоянке у входа.";
            StyleSettlementMapButton(leave);
            settlementMapPanel.Add(leave);
            settlementMapPanel.style.display = DisplayStyle.None;
            worldMapViewport.Add(settlementMapPanel);
        }
    }

    private static void StyleSettlementMapButton(Button button)
    {
        button.style.backgroundColor = new Color(0.32f, 0.25f, 0.16f, 1f);
        button.style.color = new Color(0.97f, 0.92f, 0.8f);
        button.style.borderTopColor = button.style.borderBottomColor = button.style.borderLeftColor = button.style.borderRightColor = new Color(0.62f, 0.5f, 0.32f);
        button.style.paddingLeft = button.style.paddingRight = 12;
        button.style.fontSize = 15;
    }

    // Каждый кадр: дешёвая проверка, перестройка — только при смене поселения
    // или рисунка; при открытом окне поселения цели и панель скрыты.
    private void TickWorldMapBuildings()
    {
        EnsureWorldMapBuildingUi();
        if (worldMapBuildingLayer == null)
            return;
        string insideId = gameState != null && !isGameOver ? SettlementCampService.InsideLocationId(gameState) : string.Empty;
        bool isInside = !string.IsNullOrEmpty(insideId) && SettlementMapMode(insideId);
        // Здания всех известных поселений — цели всегда: клик издалека ведёт
        // отряд в поселение, у входа — вход, внутри — герой идёт к двери.
        // Отряд идёт прямо к двери выбранного здания (не через центр
        // поселения): дошёл и встал — вход и окно этого здания.
        if (!string.IsNullOrEmpty(worldMapPendingRole) && gameState != null && !IsSettlementScreenOpen)
        {
            ExpeditionData expedition = gameState.ActiveExpedition;
            if (expedition == null || expedition.LocationId != worldMapPendingSettlement)
            {
                // Игрок отдал другой приказ — к зданию больше не идём.
                worldMapPendingRole = null;
                worldMapPendingSettlement = null;
            }
            else if (expedition.Phase == CommanderState.AtLocation && !ContinuousSimulationSystem.IsExpeditionRunning(gameState))
            {
                string role = worldMapPendingRole, settlement = worldMapPendingSettlement;
                worldMapPendingRole = null;
                worldMapPendingSettlement = null;
                EnterBuildingNow(settlement, role);
            }
        }
        // Цели зданий — у поселений, видимых на карте, и у тех, чьи здания
        // уже на разведанной земле (здание видно — значит, к нему можно идти).
        string key = VisibleBuildingSettlementsKey();
        if (key != worldMapBuildingsShownFor || !ReferenceEquals(worldMapBuildingsShownResult, worldMapMapLab))
            BuildWorldMapBuildingTargets(key);
        bool screen = IsSettlementScreenOpen || IsLocalScreenOpen;
        bool show = !string.IsNullOrEmpty(key) && !screen && !isGameOver;
        // Слой общий с деталями мест (по глубине): виден на живой карте, пока
        // нет окна поселения или места.
        worldMapBuildingLayer.style.display = worldMapMapLab != null && !screen && !isGameOver ? DisplayStyle.Flex : DisplayStyle.None;
        PlaceWorldMapBuildingLabels(show);
        PlaceWorldMapVillageNames(show, isInside ? insideId : null);
        if (settlementMapPanel != null)
        {
            // Плашка «Поселение / Уйти» не показывается: в поселение входят
            // через его здания, выходят — закрыв окно.
            settlementMapPanel.style.display = DisplayStyle.None;
            if (isInside && settlementMapTitle != null)
                settlementMapTitle.text = gameState.FindLocation(insideId)?.Name ?? "Поселение";
        }
    }

    // Поселения с зданиями на рисунке, которые видны на карте (известны).
    private string VisibleBuildingSettlementsKey()
    {
        if (worldMapMapLab == null || gameState == null)
            return string.Empty;
        List<string> ids = new List<string>();
        foreach ((string id, string _, Rect _) in worldMapMapLab.Buildings)
        {
            if (ids.Contains(id))
                continue;
            LocationData location = gameState.FindLocation(id);
            if (location == null)
                continue;
            bool seen = location.IsVisibleOnMap;
            if (!seen)
                foreach ((string bid, string _, Rect r) in worldMapMapLab.Buildings)
                    if (bid == id && WorldMapFog.IsExploredAtPercent(gameState, r.center.x, r.yMax))
                    {
                        seen = true;
                        break;
                    }
            if (seen)
                ids.Add(id);
        }
        return string.Join(",", ids);
    }


    // Точка двери здания (проценты полотна).
    private bool WorldMapDoor(string settlementId, string role, out Vector2 door)
    {
        door = Vector2.zero;
        MapLabGameLive.World world = MapLabGameLive.Current;
        if (world == null)
            return false;
        foreach ((string id, string r, Rect _, Vector2 d) in world.Buildings)
            if (id == settlementId && r == role)
            {
                door = d;
                return true;
            }
        return false;
    }

    // Отряд у двери: войти в поселение (всем, без экрана) и открыть окно здания.
    private void EnterBuildingNow(string settlementId, string role)
    {
        if (SettlementCampService.InsideLocationId(gameState) != settlementId)
        {
            if (!SettlementCampService.Enter(gameState, SettlementCampService.Companions(gameState), out string reason))
            {
                // Кого-то не пускают — прежний вход с выбором, кто идёт.
                if (!TryOpenLocationInteraction(settlementId))
                    AddReport(reason);
                return;
            }
        }
        OpenSettlementFromMap(role);
    }

    private void BuildWorldMapBuildingTargets(string key)
    {
        RemoveWorldMapTargets("world-map-building-");
        worldMapBuildingGlows.Clear();
        foreach ((Label l, Rect _) in worldMapBuildingLabels)
            l.RemoveFromHierarchy();
        worldMapBuildingLabels.Clear();
        worldMapBuildingsShownFor = key;
        worldMapBuildingsShownResult = worldMapMapLab;
        if (string.IsNullOrEmpty(key) || worldMapMapLab == null)
            return;
        HashSet<string> shown = new HashSet<string>(key.Split(','));
        foreach ((string id, string role, Rect rect) in worldMapMapLab.Buildings)
        {
            if (!shown.Contains(id))
                continue;
            string capturedRole = role;
            Texture2D texture = MapLabGameMap.ServiceTexture(role);
            MapLabSpriteMask mask = MapLabSpriteMask.For(texture);
            MaskedTarget target = new MaskedTarget { name = "world-map-building-" + role, pickingMode = PickingMode.Position, Mask = mask };
            target.userData = id;
            target.style.position = Position.Absolute;
            target.style.left = Length.Percent(rect.x);
            target.style.top = Length.Percent(rect.y);
            target.style.width = Length.Percent(rect.width);
            target.style.height = Length.Percent(rect.height);
            target.tooltip = ServiceRoleTitle(role);
            // Подсветка — по маске рисунка: мягкое осветление силуэта и тонкий
            // тёплый контур по краю; в покое невидима. Подпись — над зданием.
            VisualElement glow = new VisualElement { pickingMode = PickingMode.Ignore };
            glow.style.position = Position.Absolute;
            glow.style.left = 0;
            glow.style.top = 0;
            glow.style.right = 0;
            glow.style.bottom = 0;
            if (mask?.Glow != null)
                glow.style.backgroundImage = new StyleBackground(Background.FromTexture2D(mask.Glow));
            glow.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
            glow.style.opacity = 0f;
            glow.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
            glow.style.transitionDuration = new List<TimeValue> { new TimeValue(0.1f, TimeUnit.Second) };
            target.Add(glow);
            Label label = new Label(ServiceRoleTitle(role)) { pickingMode = PickingMode.Ignore };
            // Подпись — в слое экрана (не приближается вместе с картой): над
            // зданием, обычного размера при любом масштабе.
            label.style.position = Position.Absolute;
            label.style.left = 0;
            label.style.top = 0;
            label.style.fontSize = 15;
            label.style.color = new Color(0.98f, 0.93f, 0.8f);
            label.style.backgroundColor = new Color(0.18f, 0.14f, 0.1f, 0.82f);
            label.style.paddingLeft = label.style.paddingRight = 6;
            label.style.borderTopLeftRadius = label.style.borderTopRightRadius = label.style.borderBottomLeftRadius = label.style.borderBottomRightRadius = 4;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.display = DisplayStyle.None;
            worldMapViewport.Add(label);
            worldMapBuildingLabels.Add((label, rect));
            target.RegisterCallback<PointerEnterEvent>(_ =>
            {
                glow.style.opacity = 1f;
                label.style.display = DisplayStyle.Flex;
            });
            target.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (worldMapApproachRole == capturedRole)
                    return;
                glow.style.opacity = 0f;
                label.style.display = DisplayStyle.None;
            });
            string capturedSettlement = id;
            // Курсор над зданием не начинает бег по карте (нажатие — здесь).
            target.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            target.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                OnWorldMapBuildingClicked(capturedSettlement, capturedRole);
            });
            worldMapBuildingGlows[GlowKey(id, role)] = (glow, label);
            worldMapBuildingLayer.Add(target);
        }
        SortWorldMapTargetsByDepth();
    }

    // Убрать цели одного вида (здания или детали) из общего слоя.
    private void RemoveWorldMapTargets(string prefix)
    {
        if (worldMapBuildingLayer == null)
            return;
        List<VisualElement> remove = new List<VisualElement>();
        foreach (VisualElement child in worldMapBuildingLayer.Children())
            if (child.name != null && child.name.StartsWith(prefix))
                remove.Add(child);
        foreach (VisualElement child in remove)
            child.RemoveFromHierarchy();
    }

    // Ближний к зрителю рисунок (ниже его опора на карте) — поверх дальнего:
    // курсор над столбом перед избой выбирает столб, над избой — избу.
    private void SortWorldMapTargetsByDepth()
    {
        if (worldMapBuildingLayer == null)
            return;
        float Bottom(VisualElement e) => e.style.top.value.value + e.style.height.value.value;
        worldMapBuildingLayer.Sort((a, b) => Bottom(a).CompareTo(Bottom(b)));
    }

    // Клик по зданию: внутри — идём к двери; у входа — входим и идём; издалека —
    // отряд отправляется в поселение, после входа герой сам идёт к зданию.
    private void OnWorldMapBuildingClicked(string settlementId, string role)
    {
        if (!string.IsNullOrEmpty(worldMapApproachRole) || IsSettlementScreenOpen || gameState == null || isGameOver)
            return;
        LocationData location = gameState.FindLocation(settlementId);
        if (location == null || !WorldMapDoor(settlementId, role, out Vector2 door))
            return;
        // Здание видно — поселение известно.
        location.IsVisibleOnMap = true;
        location.IsDiscovered = true;
        ExpeditionData expedition = gameState.ActiveExpedition;
        if (expedition != null && (gameState.HasPendingExpeditionDecision || expedition.IsLocationResearchInProgress))
            return;
        // Герой ходит по карте сам, без входа в поселение и выхода из него:
        // пребывание внутри снимается молча (лагерь с ожидающими — как прежде).
        if (SettlementCampService.HasVisit(gameState) && !SettlementCampService.HasCamp(gameState))
            SettlementCampService.ReturnToCamp(gameState);
        // Уже у этой двери — сразу внутрь.
        if (expedition != null && expedition.Phase == CommanderState.AtLocation && expedition.LocationId == settlementId
            && HeroNearWorldMapPoint(door.x, door.y, 0.4) && !ContinuousSimulationSystem.IsExpeditionRunning(gameState))
        {
            EnterBuildingNow(settlementId, role);
            return;
        }
        worldMapPendingRole = role;
        worldMapPendingSettlement = settlementId;
        ShowWorldMapClickMarker(door.x, door.y);
        IssueContinuousMapOrder(door.x, door.y, location.Id);
        if (!gameState.HasActiveExpedition || gameState.ActiveExpedition.LocationId != settlementId)
        {
            worldMapPendingRole = null;
            worldMapPendingSettlement = null;
        }
    }

    // ---------- Подписи поселений вместо кнопок ----------

    private readonly List<(VisualElement Label, string Id)> worldMapVillageNames = new List<(VisualElement, string)>();

    private void ClearWorldMapVillageNames()
    {
        foreach ((VisualElement label, string _) in worldMapVillageNames)
            label.RemoveFromHierarchy();
        worldMapVillageNames.Clear();
    }

    // Поселение со зданиями на живой карте — не кнопка, а подпись: входят,
    // кликнув по нужному зданию.
    private void CreateWorldMapVillageName(LocationData location)
    {
        if (worldMapViewport == null)
            return;
        // Подпись: герб стороны, которой подчиняется поселение (если есть), и название.
        // На дальнем отдалении подпись — вход в поселение целиком (общий
        // экран: люди, ночлег, все дела): отряд идёт туда, прибытие открывает.
        VisualElement plate = new VisualElement { name = "world-map-village-plate-" + location.Id, pickingMode = PickingMode.Position };
        string plateId = location.Id;
        plate.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
        plate.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            if (gameState == null || isGameOver || IsSettlementScreenOpen)
                return;
            worldMapPendingRole = null;
            worldMapPendingSettlement = null;
            ExpeditionData e = gameState.ActiveExpedition;
            if (e != null && e.Phase == CommanderState.AtLocation && e.LocationId == plateId && !e.HasTimedActivity)
                TryOpenLocationInteraction(plateId);
            else
                SelectWorldMapLocation(plateId);
        });
        plate.style.position = Position.Absolute;
        plate.style.left = 0;
        plate.style.top = 0;
        plate.style.flexDirection = FlexDirection.Row;
        plate.style.alignItems = Align.Center;
        plate.style.backgroundColor = new Color(0.16f, 0.13f, 0.1f, 0.62f);
        plate.style.paddingLeft = plate.style.paddingRight = 6;
        plate.style.borderTopLeftRadius = plate.style.borderTopRightRadius = plate.style.borderBottomLeftRadius = plate.style.borderBottomRightRadius = 4;
        Texture2D emblem = VillageEmblem(location);
        if (emblem != null)
        {
            Image image = new Image { name = "world-map-village-emblem-" + location.Id, image = emblem, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.style.width = 18;
            image.style.height = 18;
            image.style.marginRight = 4;
            plate.Add(image);
        }
        Label label = new Label(location.Name) { name = "world-map-village-name-" + location.Id, pickingMode = PickingMode.Ignore };
        label.style.fontSize = 14;
        label.style.color = new Color(0.97f, 0.93f, 0.82f);
        label.style.whiteSpace = WhiteSpace.NoWrap;
        plate.Add(label);
        worldMapViewport.Add(plate);
        worldMapVillageNames.Add((plate, location.Id));
    }

    // Герб стороны-владельца: ближайшая деревня плана и её подчинение.
    private Texture2D VillageEmblem(LocationData location)
    {
        GeneratedWorldMap map = gameState?.GeneratedMap;
        if (map?.Villages == null)
            return null;
        GeneratedMapSite best = null;
        float bestDistance = 1.5f;
        foreach (GeneratedMapSite site in map.Villages)
        {
            float d = Mathf.Abs(site.XPercent - location.MapXPercent) + Mathf.Abs(site.YPercent - location.MapYPercent);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = site;
            }
        }
        if (best == null || string.IsNullOrEmpty(best.ControllerFactionId))
            return null;
        WorldMapGeneratorAsset generator = WorldMapVisualRuntime.LoadGenerator();
        WorldMapFactionDefinition faction = WorldMapFactionCatalog.Find(generator != null ? generator.Settings.Factions : WorldMapFactionCatalog.DefaultFactions(), best.ControllerFactionId);
        return faction != null ? MapLabGameMap.ArtTexture(faction.Emblem) : null;
    }

    // Названия деревень — только при дальнем отдалении (нижняя треть шкалы
    // приближения); на среднем и ближнем плане деревню видно и так.
    private bool WorldMapFarZoom()
    {
        float min = Mathf.Max(0.0001f, worldMapMinZoom), max = Mathf.Max(min * 1.001f, worldMapMaxZoom);
        float t = Mathf.InverseLerp(Mathf.Log(min), Mathf.Log(max), Mathf.Log(Mathf.Max(0.0001f, worldMapZoom)));
        return t <= 0.3f;
    }

    private void PlaceWorldMapVillageNames(bool visible, string insideId)
    {
        bool far = WorldMapFarZoom();
        foreach ((VisualElement label, string id) in worldMapVillageNames)
        {
            LocationData location = gameState?.FindLocation(id);
            bool on = visible && far && location != null && location.IsVisibleOnMap && id != insideId;
            label.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on)
                continue;
            Vector2 at = MapPercentToWorldMapViewport(location.MapXPercent, location.MapYPercent);
            float width = float.IsNaN(label.resolvedStyle.width) ? 0f : label.resolvedStyle.width;
            label.style.translate = new Translate(at.x - width * 0.5f, at.y + 6f);
        }
    }

    // Подход к зданию и окно его услуги. role == null — общий вид поселения.
    private void OpenSettlementFromMap(string role)
    {
        string inside = gameState != null ? SettlementCampService.InsideLocationId(gameState) : string.Empty;
        if (string.IsNullOrEmpty(inside) || IsSettlementScreenOpen)
            return;
        OpenSettlementScreen(inside, SettlementMode.Main);
        settlementBuildingRole = role;
        settlementBuildingAction = null;
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
                settlementBuildingAction = action;
                settlementMode = SettlementMode.Building;
                RenderSettlement();
                return;
            }
            // У знахарки своей страницы нет — показывается страница здания
            // (её дело и «Выйти»), а не общий экран поселения.
            if (action.Type == SettlementActionType.Healer)
            {
                settlementBuildingAction = action;
                settlementMode = SettlementMode.Building;
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
