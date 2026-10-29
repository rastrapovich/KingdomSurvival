using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Бриф правок (22 пункта) пп. 3, 17. Наведение на активный значок карты —
// подсказка: название, тип места и коротко, что там доступно, только из
// уже известного герою (без скрытых историй и будущих наград). Подсказка
// не ловит клики, держится в пределах карты и пропадает при уходе курсора
// или перестройке значков. Наведение героя не двигает и времени не тратит.
// Место без назначенного рисунка показывается явно временным знаком типа
// (короткая подпись в рамке), а не безликим кружком.
public partial class PrototypeUIController
{
    // Временные знаки типов мест генератора — до финального арта.
    private static readonly Dictionary<string, (string Mark, string Label)> MapPlaceKinds = new Dictionary<string, (string, string)>
    {
        { GeneratedMapPlaces.VillageKind, ("Дер", "Поселение") },
        { "hillfort", ("Гор", "Старое городище") },
        { "abandoned-village", ("Зап", "Заброшенное поселение") },
        { "abandoned-yard", ("Дв", "Заброшенный двор") },
        { "sanctuary", ("Св", "Старое святилище") },
        { "crossroads", ("Пк", "Перекрёсток") },
        { "lair", ("Лог", "Лесное логово") },
        { "spring", ("Род", "Родник") },
        { "hunters-hut", ("Хиж", "Охотничья хижина") },
        { "apiary", ("Пас", "Пасека") }
    };

    private VisualElement worldMapNodeTooltip;
    private Label worldMapNodeTooltipTitle;
    private Label worldMapNodeTooltipKind;
    private Label worldMapNodeTooltipText;
    private string worldMapNodeTooltipLocationId;

    private void WireWorldMapNodeTooltip(Button node, LocationData location)
    {
        string id = location.Id;
        node.tooltip = string.Empty;
        node.RegisterCallback<PointerEnterEvent>(_ => ShowWorldMapNodeTooltip(node, id));
        node.RegisterCallback<PointerLeaveEvent>(_ => HideWorldMapNodeTooltip(id));
        node.RegisterCallback<DetachFromPanelEvent>(_ => HideWorldMapNodeTooltip(id));
    }

    private void EnsureWorldMapNodeTooltip()
    {
        if (worldMapNodeTooltip != null || worldMapViewport == null)
            return;
        worldMapNodeTooltip = new VisualElement { name = "world-map-node-tooltip", pickingMode = PickingMode.Ignore };
        worldMapNodeTooltip.AddToClassList("world-map-node-tooltip");
        worldMapNodeTooltipTitle = new Label { pickingMode = PickingMode.Ignore };
        worldMapNodeTooltipTitle.AddToClassList("world-map-node-tooltip-title");
        worldMapNodeTooltipKind = new Label { pickingMode = PickingMode.Ignore };
        worldMapNodeTooltipKind.AddToClassList("world-map-node-tooltip-kind");
        worldMapNodeTooltipText = new Label { pickingMode = PickingMode.Ignore };
        worldMapNodeTooltipText.AddToClassList("world-map-node-tooltip-text");
        worldMapNodeTooltip.Add(worldMapNodeTooltipTitle);
        worldMapNodeTooltip.Add(worldMapNodeTooltipKind);
        worldMapNodeTooltip.Add(worldMapNodeTooltipText);
        worldMapNodeTooltip.style.display = DisplayStyle.None;
        worldMapViewport.Add(worldMapNodeTooltip);
    }

    private void ShowWorldMapNodeTooltip(VisualElement node, string locationId)
    {
        LocationData location = gameState?.FindLocation(locationId);
        if (location == null || worldMapViewport == null)
            return;
        EnsureWorldMapNodeTooltip();
        worldMapNodeTooltipLocationId = locationId;
        worldMapNodeTooltipTitle.text = location.Name;
        worldMapNodeTooltipKind.text = DescribeMapPlaceKind(location);
        string text = DescribeMapPlaceOffer(location);
        worldMapNodeTooltipText.text = text;
        worldMapNodeTooltipText.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        worldMapNodeTooltip.style.display = DisplayStyle.Flex;
        worldMapNodeTooltip.BringToFront();

        // Рядом со значком, справа сверху; у края карты — по другую сторону.
        Rect bounds = worldMapViewport.WorldToLocal(node.worldBound);
        float width = 280f;
        float x = bounds.xMax + 10f;
        if (x + width > worldMapViewport.layout.width - 8f)
            x = bounds.xMin - width - 10f;
        float y = Mathf.Clamp(bounds.yMin - 10f, 8f, Mathf.Max(8f, worldMapViewport.layout.height - 140f));
        worldMapNodeTooltip.style.left = Mathf.Max(8f, x);
        worldMapNodeTooltip.style.top = y;
        worldMapNodeTooltip.style.width = width;
    }

    private void HideWorldMapNodeTooltip(string locationId = null)
    {
        if (worldMapNodeTooltip == null)
            return;
        if (locationId != null && locationId != worldMapNodeTooltipLocationId)
            return;
        worldMapNodeTooltip.style.display = DisplayStyle.None;
        worldMapNodeTooltipLocationId = null;
    }

    private string DescribeMapPlaceKind(LocationData location)
    {
        if (location.IsSettlement)
            return "Поселение";
        if (!string.IsNullOrEmpty(location.PlaceKind) && MapPlaceKinds.TryGetValue(location.PlaceKind, out (string Mark, string Label) kind))
            return kind.Label;
        return LocalLocationCatalog.ForWorldLocation(gameState, location.Id) != null ? "Исследуемое место" : "Место";
    }

    // Что там доступно — по известному: посещённое поселение называет свои
    // службы, непосещённое — только что это поселение; прочее — обыскано ли
    // и первое предложение уже показанного описания места.
    private string DescribeMapPlaceOffer(LocationData location)
    {
        List<string> lines = new List<string>();
        if (location.IsSettlement)
        {
            SettlementInstanceData instance = SettlementService.FindInstance(gameState, location.Id);
            SettlementProfileDefinition profile = SettlementService.Profile(gameState, location.Id);
            if (instance != null && instance.Visited && profile != null)
            {
                List<string> services = new List<string>();
                if (profile.Merchants != null && profile.Merchants.Count > 0)
                    services.Add("торговля");
                if (profile.Tavern != null && profile.Tavern.Enabled)
                    services.Add("корчма" + (profile.Tavern.Lodging != null && profile.Tavern.Lodging.Enabled ? " и ночлег" : string.Empty));
                if (profile.Board != null && profile.Board.Enabled)
                    services.Add("доска объявлений");
                lines.Add(services.Count > 0 ? "Здесь: " + string.Join(", ", services) + "." : "Здесь можно поговорить с людьми.");
            }
            else
            {
                lines.Add("Здесь ещё не были — войти и узнать.");
            }
        }
        else
        {
            if (LocalLocationCatalog.ForWorldLocation(gameState, location.Id) != null)
                lines.Add("Можно войти и осмотреться.");
            lines.Add(location.IsExplored ? "Уже обыскано." : "Ещё не обследовано.");
            string description = FirstSentence(location.InteractionDescription);
            if (!string.IsNullOrEmpty(description))
                lines.Add(description);
        }
        if (gameState.HasActiveExpedition && gameState.ActiveExpedition.LocationId == location.Id &&
            gameState.ActiveExpedition.Phase == CommanderState.AtLocation)
            lines.Add("Отряд здесь — клик, чтобы войти.");
        return string.Join("\n", lines);
    }

    private static string FirstSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        string trimmed = text.Trim();
        int end = trimmed.IndexOfAny(new[] { '.', '!', '?' });
        string sentence = end > 0 ? trimmed.Substring(0, end + 1) : trimmed;
        return sentence.Length > 140 ? sentence.Substring(0, 137) + "…" : sentence;
    }

    // Временный знак типа места, пока рисунок не назначен (п. 17).
    private static void ApplyWorldMapTemporaryMark(Button node, LocationData location)
    {
        string mark;
        if (location.IsSettlement)
            mark = "Дер";
        else if (!string.IsNullOrEmpty(location.PlaceKind) && MapPlaceKinds.TryGetValue(location.PlaceKind, out (string Mark, string Label) kind))
            mark = kind.Mark;
        else
            mark = string.IsNullOrEmpty(location.Name) ? "?" : location.Name.Substring(0, 1);
        node.text = mark;
        node.AddToClassList("world-map-node--temp-mark");
    }
}
