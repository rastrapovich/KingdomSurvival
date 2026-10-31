using UnityEngine;
using UnityEngine.UIElements;

// Бриф правок (19 пунктов) п. 4: перетаскивание в торговле. Товар торговца
// тянут в «Вы покупаете», свою вещь (и припасы) — в «Вы продаёте»; строку
// сделки, вытащенную за пределы своей части, возвращают. Двойной клик
// остаётся равноценной заменой. Перетаскивание меняет только намерение
// (TradeCart) теми же командами, что и двойной клик; вещи и золото меняет
// одно подтверждение сделки.
public partial class PrototypeUIController
{
    private enum TradeDragSource
    {
        None,
        Merchant,
        Hero,
        BuyOffer,
        SellOffer
    }

    private const float TradeDragThresholdPixels = 6f;

    private VisualElement tradeBuyZone;
    private VisualElement tradeSellZone;
    private TradeDragSource tradeDragSource;
    private System.Action tradeDragAction;
    private string tradeDragKey;
    private string tradeDragLabel;
    private Vector2 tradeDragStart;
    private int tradeDragPointerId = -1;
    private bool tradeDragging;
    private Label tradeDragGhost;

    private void BindSettlementTradeDrag()
    {
        tradeBuyZone = settlementScreen?.Q<VisualElement>("trade-buy-zone");
        tradeSellZone = settlementScreen?.Q<VisualElement>("trade-sell-zone");
        if (tradePage == null)
            return;
        tradePage.RegisterCallback<PointerMoveEvent>(OnTradePointerMove);
        tradePage.RegisterCallback<PointerUpEvent>(OnTradePointerUp);
        tradePage.RegisterCallback<PointerCaptureOutEvent>(_ => CancelTradeDrag());
    }

    // Нажатие на строку: кандидат на перетаскивание (или простой клик).
    private void BeginTradeDragCandidate(PointerDownEvent evt, TradeDragSource source, string key, string label, System.Action onDrop)
    {
        if (tradePage == null)
            return;
        tradeDragSource = source;
        tradeDragAction = onDrop;
        tradeDragKey = key;
        tradeDragLabel = label;
        tradeDragStart = evt.position;
        tradeDragPointerId = evt.pointerId;
        tradeDragging = false;
        tradePage.CapturePointer(evt.pointerId);
    }

    private VisualElement TradeDropZone(TradeDragSource source) =>
        source == TradeDragSource.Merchant || source == TradeDragSource.BuyOffer ? tradeBuyZone : tradeSellZone;

    private void OnTradePointerMove(PointerMoveEvent evt)
    {
        if (tradeDragSource == TradeDragSource.None || evt.pointerId != tradeDragPointerId)
            return;
        Vector2 position = evt.position;
        if (!tradeDragging)
        {
            if ((position - tradeDragStart).sqrMagnitude < TradeDragThresholdPixels * TradeDragThresholdPixels)
                return;
            tradeDragging = true;
            HideTradePopup();
            tradeDragGhost = new Label(tradeDragLabel) { pickingMode = PickingMode.Ignore, name = "trade-drag-ghost" };
            tradeDragGhost.AddToClassList("trade-drag-ghost");
            tradePage.Add(tradeDragGhost);
            TradeDropZone(tradeDragSource)?.AddToClassList("trade-offer--drop");
        }
        Vector2 local = position - tradePage.worldBound.position;
        tradeDragGhost.style.left = local.x + 12f;
        tradeDragGhost.style.top = local.y + 8f;
        VisualElement zone = TradeDropZone(tradeDragSource);
        bool inside = zone != null && zone.worldBound.Contains(position);
        bool wantsInside = tradeDragSource == TradeDragSource.Merchant || tradeDragSource == TradeDragSource.Hero;
        zone?.EnableInClassList("trade-offer--drop-hover", inside == wantsInside);
        evt.StopPropagation();
    }

    private void OnTradePointerUp(PointerUpEvent evt)
    {
        if (tradeDragSource == TradeDragSource.None || evt.pointerId != tradeDragPointerId)
            return;
        TradeDragSource source = tradeDragSource;
        System.Action action = tradeDragAction;
        string key = tradeDragKey;
        bool dragged = tradeDragging;
        Vector2 position = evt.position;
        VisualElement zone = TradeDropZone(source);
        bool inside = zone != null && zone.worldBound.Contains(position);
        CancelTradeDrag();
        evt.StopPropagation();

        if (dragged)
        {
            bool fromStock = source == TradeDragSource.Merchant || source == TradeDragSource.Hero;
            // Из списка — внутрь своей части сделки; из сделки — наружу.
            if (fromStock == inside)
                action?.Invoke();
            else
                RenderSettlementTrade();
            return;
        }
        // Простой клик по строке списка — подсветка.
        if (source == TradeDragSource.Merchant || source == TradeDragSource.Hero)
        {
            tradeHighlightKey = key;
            RenderSettlementTrade();
        }
    }

    private void CancelTradeDrag()
    {
        if (tradeDragSource == TradeDragSource.None)
            return;
        int pointer = tradeDragPointerId;
        tradeDragSource = TradeDragSource.None;
        tradeDragAction = null;
        tradeDragging = false;
        tradeDragPointerId = -1;
        tradeDragGhost?.RemoveFromHierarchy();
        tradeDragGhost = null;
        foreach (VisualElement zone in new[] { tradeBuyZone, tradeSellZone })
        {
            zone?.RemoveFromClassList("trade-offer--drop");
            zone?.RemoveFromClassList("trade-offer--drop-hover");
        }
        if (tradePage != null && pointer >= 0 && tradePage.HasPointerCapture(pointer))
            tradePage.ReleasePointer(pointer);
    }
}
