using System.Collections.Generic;
using KingdomSurvival.ItemDatabase;
using KingdomSurvival.UnitDatabase;
using UnityEngine;
using UnityEngine.UIElements;

// 12Т (SETTLEMENTS_TRADE_TAVERN_SPEC.md §10) и брифы правок (22 пункта) п. 4,
// (19 пунктов) п. 4: торговля — одна общая сделка. Слева товар торговца,
// справа вещи героя (и припасы), в центре рядом «Вы покупаете» и «Вы
// продаёте». Перетаскивание или двойной клик кладёт единицу в предложение;
// строку предложения возвращают ×/−, двойным кликом или вытащив её из
// своей части; правая кнопка — сведения во всех четырёх областях (ничего не
// переносит). Предложение — только
// намерение: вещи и золото меняет одна команда TradeService.TryConfirm (всё
// или ничего, с повторной проверкой остатков, денег и места в сумке).
public partial class PrototypeUIController
{
    private static readonly string[] TradeFilterChoices = { "Все", "Оружие", "Защита", "Особое", "Расходное", "Товары" };

    private VisualElement tradePage;
    private Label tradeTitle;
    private Label tradeSubtitle;
    private TextField tradeMerchantSearch;
    private DropdownField tradeMerchantFilter;
    private ScrollView tradeMerchantList;
    private TextField tradePartySearch;
    private DropdownField tradePartyFilter;
    private ScrollView tradePartyList;
    private ScrollView tradeSellOffer;
    private ScrollView tradeBuyOffer;
    private Label tradeSellTotal;
    private Label tradeBuyTotal;
    private Label tradeMoney;
    private Label tradeTotals;
    private Label tradeNet;
    private Label tradeReason;
    private Button tradeConfirmButton;
    private VisualElement tradePopup;
    private Image tradePopupIcon;
    private Label tradePopupName;
    private Label tradePopupMeta;
    private Label tradePopupText;
    private Label tradePopupMechanics;
    private Label tradePopupCompare;
    private Label tradePopupPrice;

    private VisualElement tradeMerchantPortrait;
    private VisualElement tradeHeroPortraitSlot;
    private UnitPortraitElement tradeHeroPortrait;
    private Label tradeMerchantMoney;
    private Label tradePartyMoney;
    private readonly TradeCart tradeCart = new TradeCart();
    private string tradeMerchantId;
    // Подсвеченная строка (одиночный клик): ключ строки любого списка.
    private string tradeHighlightKey;

    private bool IsSettlementTradeOpen => tradePage != null && tradePage.style.display == DisplayStyle.Flex;
    private bool IsTradePopupOpen => tradePopup != null && tradePopup.style.display == DisplayStyle.Flex;

    private void BindSettlementTradeUi()
    {
        const string screen = "Торговля";
        tradePage = BindRequiredElement<VisualElement>(settlementScreen, screen, "settlement-trade-page");
        tradeTitle = BindRequiredElement<Label>(settlementScreen, screen, "trade-title");
        tradeSubtitle = BindRequiredElement<Label>(settlementScreen, screen, "trade-subtitle");
        tradeMerchantSearch = BindRequiredElement<TextField>(settlementScreen, screen, "trade-merchant-search");
        tradeMerchantFilter = BindRequiredElement<DropdownField>(settlementScreen, screen, "trade-merchant-filter");
        tradeMerchantList = BindRequiredElement<ScrollView>(settlementScreen, screen, "trade-merchant-list");
        tradePartySearch = BindRequiredElement<TextField>(settlementScreen, screen, "trade-party-search");
        tradePartyFilter = BindRequiredElement<DropdownField>(settlementScreen, screen, "trade-party-filter");
        tradePartyList = BindRequiredElement<ScrollView>(settlementScreen, screen, "trade-party-list");
        tradeSellOffer = BindRequiredElement<ScrollView>(settlementScreen, screen, "trade-sell-offer");
        tradeBuyOffer = BindRequiredElement<ScrollView>(settlementScreen, screen, "trade-buy-offer");
        tradeSellTotal = BindRequiredElement<Label>(settlementScreen, screen, "trade-sell-total");
        tradeBuyTotal = BindRequiredElement<Label>(settlementScreen, screen, "trade-buy-total");
        tradeMoney = BindRequiredElement<Label>(settlementScreen, screen, "trade-money");
        tradeTotals = BindRequiredElement<Label>(settlementScreen, screen, "trade-totals");
        tradeNet = BindRequiredElement<Label>(settlementScreen, screen, "trade-net");
        tradeReason = BindRequiredElement<Label>(settlementScreen, screen, "trade-reason");
        tradeConfirmButton = BindRequiredElement<Button>(settlementScreen, screen, "trade-confirm-button");
        tradeMerchantPortrait = BindRequiredElement<VisualElement>(settlementScreen, screen, "trade-merchant-portrait");
        tradeHeroPortraitSlot = BindRequiredElement<VisualElement>(settlementScreen, screen, "trade-hero-portrait");
        tradeMerchantMoney = BindRequiredElement<Label>(settlementScreen, screen, "trade-merchant-money");
        tradePartyMoney = BindRequiredElement<Label>(settlementScreen, screen, "trade-party-money");
        tradePopup = BindRequiredElement<VisualElement>(settlementScreen, screen, "trade-item-popup");
        tradePopupIcon = BindRequiredElement<Image>(settlementScreen, screen, "trade-item-popup-icon");
        tradePopupName = BindRequiredElement<Label>(settlementScreen, screen, "trade-item-popup-name");
        tradePopupMeta = BindRequiredElement<Label>(settlementScreen, screen, "trade-item-popup-meta");
        tradePopupText = BindRequiredElement<Label>(settlementScreen, screen, "trade-item-popup-text");
        tradePopupMechanics = BindRequiredElement<Label>(settlementScreen, screen, "trade-item-popup-mechanics");
        tradePopupCompare = BindRequiredElement<Label>(settlementScreen, screen, "trade-item-popup-compare");
        tradePopupPrice = BindRequiredElement<Label>(settlementScreen, screen, "trade-item-popup-price");
        Button popupClose = BindRequiredElement<Button>(settlementScreen, screen, "trade-item-popup-close");
        Button cancel = BindRequiredElement<Button>(settlementScreen, screen, "trade-cancel-button");
        Button back = BindRequiredElement<Button>(settlementScreen, screen, "trade-back-button");
        if (tradePage == null || tradeMerchantList == null || tradePartyList == null || tradeConfirmButton == null || tradePopup == null)
            return;

        foreach (DropdownField filter in new[] { tradeMerchantFilter, tradePartyFilter })
        {
            filter.choices = new List<string>(TradeFilterChoices);
            filter.index = 0;
            filter.RegisterValueChangedCallback(_ => RenderSettlementTrade());
        }
        tradeMerchantSearch.RegisterValueChangedCallback(_ => RenderSettlementTrade());
        tradePartySearch.RegisterValueChangedCallback(_ => RenderSettlementTrade());
        tradeConfirmButton.clicked += OnTradeConfirmClicked;
        cancel.clicked += () =>
        {
            if (tradeCart.IsEmpty)
                return;
            AskSettlementConfirm("Отменить сделку? Ничего не будет куплено и продано.", () =>
            {
                ResetTradeCart();
                RenderSettlementTrade();
            });
        };
        back.clicked += OnTradeBackClicked;
        popupClose.clicked += HideTradePopup;
        tradePopup.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target == tradePopup)
            {
                HideTradePopup();
                evt.StopPropagation();
            }
        });
        tradePage.style.display = DisplayStyle.None;
        BindSettlementTradeDrag();
    }

    private void OpenSettlementTrade(string merchantId)
    {
        if (!IsSettlementScreenOpen || tradePage == null)
            return;
        if (!TradeService.TryOpen(gameState, settlementLocationId, merchantId, out _, out MerchantStateData merchant, out string reason))
        {
            ShowSettlementMessage(reason);
            return;
        }
        HideSettlementBoard();
        tradeMerchantId = merchantId;
        tradeCart.Clear();
        tradeCart.LocationId = settlementLocationId;
        tradeCart.MerchantId = merchantId;
        tradeCart.Revision = merchant.Revision;
        tradeHighlightKey = null;
        tradeMerchantSearch.SetValueWithoutNotify(string.Empty);
        tradePartySearch.SetValueWithoutNotify(string.Empty);
        HideTradePopup();
        tradePage.style.display = DisplayStyle.Flex;
        tradePage.BringToFront();
        SyncSettlementPageClass();
        RenderSettlementTrade();
    }

    private void HideSettlementTrade()
    {
        CancelTradeDrag();
        HideTradePopup();
        if (tradePage != null)
            tradePage.style.display = DisplayStyle.None;
        ResetTradeCart();
        SyncSettlementPageClass();
    }

    private bool TradeHasUnconfirmedCart() => IsSettlementTradeOpen && !tradeCart.IsEmpty;

    private void ResetTradeCart()
    {
        tradeCart.Clear();
        if (gameState != null && !string.IsNullOrEmpty(tradeMerchantId) && IsSettlementScreenOpen &&
            TradeService.TryOpen(gameState, settlementLocationId, tradeMerchantId, out _, out MerchantStateData merchant, out _))
            tradeCart.Revision = merchant.Revision;
    }

    private void OnTradeBackClicked()
    {
        if (IsTradePopupOpen)
        {
            HideTradePopup();
            return;
        }
        if (!tradeCart.IsEmpty)
        {
            AskSettlementConfirm("Отменить сделку? Ничего не будет куплено и продано.", () =>
            {
                HideSettlementTrade();
                RenderSettlement();
                ReturnToMerchantTalkAfterTrade();
            });
            return;
        }
        HideSettlementTrade();
        RenderSettlement();
        // Бриф правок (19 пунктов) п. 2: из разговора — назад в разговор,
        // из кнопки лавки — на страницу поселения.
        ReturnToMerchantTalkAfterTrade();
    }

    // ------------------------------------------------------------------
    // Показ
    // ------------------------------------------------------------------

    private void RenderSettlementTrade()
    {
        if (!IsSettlementTradeOpen || gameState == null)
            return;
        if (!TradeService.TryOpen(gameState, settlementLocationId, tradeMerchantId, out MerchantDefinition definition,
                out MerchantStateData merchant, out string reason))
        {
            HideSettlementTrade();
            ShowSettlementMessage(reason);
            return;
        }

        tradeTitle.text = definition.DisplayName;
        tradeSubtitle.text = string.IsNullOrEmpty(definition.Role) ? string.Empty : definition.Role;
        ApplyTradePortraits(definition);
        tradeMerchantMoney.text = "Золото торговца: " + merchant.Money;
        tradePartyMoney.text = "Ваше золото: " + gameState.ArmyGold;

        List<TradeOfferView> offers = TradeService.Offers(gameState, definition, merchant);
        List<TradeSellView> sellables = TradeService.Sellables(gameState, definition);
        sellables.Sort(CompareSellables);
        DropStaleTradeLines(offers, sellables);

        // Товар торговца: исходный ассортимент; то, что уже в предложении,
        // подсвечено, остаток считается за вычетом предложенного.
        tradeMerchantList.Clear();
        foreach (TradeOfferView offer in offers)
        {
            if (!PassesTradeFilter(offer.Item, offer.Kind == TradeLineKind.Supplies, tradeMerchantFilter.index, tradeMerchantSearch.value, offer.Name))
                continue;
            tradeMerchantList.Add(CreateTradeOfferRow(offer));
        }
        if (tradeMerchantList.childCount == 0)
            tradeMerchantList.Add(CreateTradeEmpty(offers.Count == 0 ? "У торговца пусто — подвезут позже." : "Ничего под этот отбор."));

        tradePartyList.Clear();
        if (gameState.ArmySupply > 0 && PassesTradeFilter(null, true, tradePartyFilter.index, tradePartySearch.value, "Припасы"))
            tradePartyList.Add(CreateTradeSuppliesSellRow(definition));
        foreach (TradeSellView sell in sellables)
        {
            if (!PassesTradeFilter(sell.Item, false, tradePartyFilter.index, tradePartySearch.value, sell.Item.Name))
                continue;
            tradePartyList.Add(CreateTradeSellRow(sell));
        }
        if (tradePartyList.childCount == 0)
            tradePartyList.Add(CreateTradeEmpty(sellables.Count == 0 && gameState.ArmySupply <= 0
                ? "Продать нечего: надетое сначала снимите в снаряжении; вещи оставшихся в лагере отсюда не видны."
                : "Ничего под этот отбор."));

        int buyTotal = RenderTradeBuyOffer(offers);
        int sellTotal = RenderTradeSellOffer(definition, sellables);
        RenderTradeSummary(buyTotal, sellTotal);
    }

    // Строки предложения, которых больше нет на прилавке или у отряда
    // (продано другим путём, вещь ушла), из предложения убираются.
    private void DropStaleTradeLines(List<TradeOfferView> offers, List<TradeSellView> sellables)
    {
        for (int i = tradeCart.Buy.Count - 1; i >= 0; i--)
        {
            TradeBuyLine line = tradeCart.Buy[i];
            TradeOfferView offer = offers.Find(o => o.Kind == line.Kind && o.Key == line.Key);
            if (offer == null || !offer.Enabled)
                tradeCart.Buy.RemoveAt(i);
            else if (line.Quantity > TradeMaxQuantity(offer))
                line.Quantity = TradeMaxQuantity(offer);
        }
        tradeCart.SellInstanceIds.RemoveAll(id => !sellables.Exists(s => s.Instance.InstanceId == id && s.Enabled));
        tradeCart.SellSupplies = Mathf.Clamp(tradeCart.SellSupplies, 0, Mathf.Max(0, gameState.ArmySupply));
    }

    private int RenderTradeBuyOffer(List<TradeOfferView> offers)
    {
        tradeBuyOffer.Clear();
        int total = 0;
        foreach (TradeBuyLine line in tradeCart.Buy)
        {
            TradeOfferView offer = offers.Find(o => o.Kind == line.Kind && o.Key == line.Key);
            if (offer == null)
                continue;
            int sum = offer.UnitPrice * line.Quantity;
            total += sum;
            bool stepper = line.Kind != TradeLineKind.Buyback && TradeMaxQuantity(offer) > 1;
            TradeOfferView captured = offer;
            tradeBuyOffer.Add(CreateTradeOfferLine(offer.Item?.Id, offer.Name, line.Quantity, sum, stepper,
                () => ChangeTradeBuy(captured, -1), () => ChangeTradeBuy(captured, 1),
                () => ShowTradePopupForOffer(captured), TradeDragSource.BuyOffer,
                () => ChangeTradeBuy(captured, -tradeCart.QuantityOf(captured.Kind, captured.Key))));
        }
        if (tradeBuyOffer.childCount == 0)
            tradeBuyOffer.Add(CreateTradeOfferEmpty("Перетащите сюда товар торговца слева или дважды щёлкните по нему."));
        tradeBuyTotal.text = "Покупки: " + total + " зол.";
        return total;
    }

    private int RenderTradeSellOffer(MerchantDefinition definition, List<TradeSellView> sellables)
    {
        tradeSellOffer.Clear();
        int total = 0;
        if (tradeCart.SellSupplies > 0)
        {
            int unit = TradeService.SupplySellPrice(definition);
            int sum = unit * tradeCart.SellSupplies;
            total += sum;
            tradeSellOffer.Add(CreateTradeOfferLine(null, "Припасы отряда", tradeCart.SellSupplies, sum, true,
                () => ChangeTradeSupplySale(-1), () => ChangeTradeSupplySale(1), () => ShowTradeSuppliesPopup(definition),
                TradeDragSource.SellOffer, () => ChangeTradeSupplySale(-tradeCart.SellSupplies)));
        }
        foreach (string instanceId in tradeCart.SellInstanceIds)
        {
            TradeSellView sell = sellables.Find(s => s.Instance.InstanceId == instanceId);
            if (sell == null)
                continue;
            total += sell.UnitPrice;
            TradeSellView captured = sell;
            tradeSellOffer.Add(CreateTradeOfferLine(sell.Item.Id, sell.Item.Name, 1, sell.UnitPrice, false,
                () => RemoveTradeSell(captured), null, () => ShowTradePopupForSell(captured),
                TradeDragSource.SellOffer, () => RemoveTradeSell(captured)));
        }
        if (tradeSellOffer.childCount == 0)
            tradeSellOffer.Add(CreateTradeOfferEmpty("Перетащите сюда свою вещь справа или дважды щёлкните по ней."));
        tradeSellTotal.text = "Продажи: " + total + " зол.";
        return total;
    }

    // Итог для героя = выручка − покупки: «Доплатить», «Получить» или «Без
    // доплаты». Те же цены, что у прилавка; подтверждение сверяет итог с
    // расчётом TradeService.
    private void RenderTradeSummary(int buyTotal, int sellTotal)
    {
        TradeQuote quote = TradeService.Quote(gameState, tradeCart);
        int pack = ItemService.HeroPackCount(gameState);
        int net = buyTotal - sellTotal;
        if (tradeCart.IsEmpty)
        {
            tradeNet.text = "Сделка пуста";
            tradeMoney.text = "Ваше золото: " + gameState.ArmyGold;
            tradeTotals.text = "Заполнение сумки: " + pack + " / " + ItemService.HeroPackSize;
        }
        else
        {
            tradeNet.text = net > 0 ? "Доплатить " + net + " золота"
                : net < 0 ? "Получить " + (-net) + " золота"
                : "Без доплаты";
            tradeMoney.text = "Покупки " + buyTotal + " · продажи " + sellTotal + " · ваше золото: " +
                              gameState.ArmyGold + " → " + (gameState.ArmyGold - net);
            int packAfter = quote.Valid ? quote.PackAfter : pack;
            tradeTotals.text = "Заполнение сумки: " + pack + " / " + ItemService.HeroPackSize +
                               (quote.Valid && packAfter != pack ? " · после сделки: " + packAfter + " / " + ItemService.HeroPackSize : string.Empty) +
                               (quote.SuppliesGained > 0 ? " · припасы +" + quote.SuppliesGained
                                   : quote.SuppliesSold > 0 ? " · припасы −" + quote.SuppliesSold : string.Empty);
        }
        tradeReason.text = tradeCart.IsEmpty || quote.Valid ? string.Empty : quote.Reason;
        tradeConfirmButton.SetEnabled(!tradeCart.IsEmpty && quote.Valid && quote.Net == net);
    }

    private VisualElement CreateTradeOfferRow(TradeOfferView offer)
    {
        int inCart = tradeCart.QuantityOf(offer.Kind, offer.Key);
        int left = Mathf.Max(0, offer.Available - inCart);
        string detail;
        if (offer.Kind == TradeLineKind.Supplies)
            detail = "в общие припасы похода · есть: " + left;
        else if (offer.Kind == TradeLineKind.Buyback)
            detail = "выкуп проданного" + (offer.Item.Uses > 0 ? " · осталось применений: " + offer.Instance.UsesLeft : string.Empty);
        else
            detail = TradeKindLabel(offer.Item) + " · есть: " + left;
        if (!offer.Enabled)
            detail += " · " + offer.Reason;
        string key = "buy:" + offer.Kind + ":" + offer.Key;
        TradeOfferView captured = offer;
        VisualElement row = CreateTradeRow(offer.Item?.Id, offer.Name, detail, offer.UnitPrice + " зол.",
            inCart > 0 ? "в сделке ×" + inCart : string.Empty, tradeHighlightKey == key, inCart > 0, !offer.Enabled);
        WireTradeRow(row, key, () => ChangeTradeBuy(captured, 1), () => ShowTradePopupForOffer(captured),
            TradeDragSource.Merchant, offer.Name);
        return row;
    }

    private VisualElement CreateTradeSellRow(TradeSellView sell)
    {
        bool inCart = tradeCart.SellInstanceIds.Contains(sell.Instance.InstanceId);
        string detail = TradeKindLabel(sell.Item) + (sell.Item.Uses > 0 ? " · применений: " + sell.Instance.UsesLeft : string.Empty) +
                        (sell.Enabled ? string.Empty : " · " + sell.Reason);
        string key = "sell:" + sell.Instance.InstanceId;
        TradeSellView captured = sell;
        VisualElement row = CreateTradeRow(sell.Item.Id, sell.Item.Name, detail, sell.Enabled ? sell.UnitPrice + " зол." : "—",
            inCart ? "в сделке" : string.Empty, tradeHighlightKey == key, inCart, !sell.Enabled);
        WireTradeRow(row, key, () => AddTradeSell(captured), () => ShowTradePopupForSell(captured),
            TradeDragSource.Hero, sell.Item.Name);
        return row;
    }

    // Припасы отряда на продажу — одной строкой вверху своей стороны.
    private VisualElement CreateTradeSuppliesSellRow(MerchantDefinition definition)
    {
        int left = gameState.ArmySupply - tradeCart.SellSupplies;
        VisualElement row = CreateTradeRow(null, "Припасы отряда", "есть: " + left + " · общий запас похода",
            TradeService.SupplySellPrice(definition) + " зол.", tradeCart.SellSupplies > 0 ? "в сделке ×" + tradeCart.SellSupplies : string.Empty,
            tradeHighlightKey == "sell:supplies", tradeCart.SellSupplies > 0, false);
        row.AddToClassList("trade-row--supplies");
        WireTradeRow(row, "sell:supplies", () => ChangeTradeSupplySale(1), () => ShowTradeSuppliesPopup(definition),
            TradeDragSource.Hero, "Припасы отряда");
        return row;
    }

    // Клик — подсветить строку; перетаскивание в свою часть сделки или
    // двойной клик — действие; правая кнопка — сведения (ничего не переносит).
    private void WireTradeRow(VisualElement row, string key, System.Action onDouble, System.Action onDetails,
        TradeDragSource source, string dragLabel)
    {
        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 1)
            {
                evt.StopPropagation();
                onDetails?.Invoke();
                return;
            }
            if (evt.button != 0)
                return;
            evt.StopPropagation();
            if (evt.clickCount >= 2)
            {
                CancelTradeDrag();
                tradeHighlightKey = key;
                onDouble?.Invoke();
                return;
            }
            BeginTradeDragCandidate(evt, source, key, dragLabel, onDouble);
        });
    }

    private VisualElement CreateTradeOfferLine(string itemId, string name, int quantity, int sum, bool stepper,
        System.Action onLess, System.Action onMore, System.Action onDetails, TradeDragSource source, System.Action onRemoveAll)
    {
        VisualElement row = CreateTradeRow(itemId, quantity > 1 ? name + " ×" + quantity : name, string.Empty, sum + " зол.",
            string.Empty, false, true, false);
        row.AddToClassList("trade-row--offer");
        if (stepper)
        {
            Button less = new Button(() => onLess()) { text = "−", tooltip = "Убрать одну" };
            less.AddToClassList("ks-button");
            less.AddToClassList("trade-offer-step");
            row.Add(less);
            Button more = new Button(() => onMore?.Invoke()) { text = "+", tooltip = "Добавить одну" };
            more.AddToClassList("ks-button");
            more.AddToClassList("trade-offer-step");
            row.Add(more);
        }
        else
        {
            Button remove = new Button(() => onLess()) { text = "×", tooltip = "Вернуть" };
            remove.AddToClassList("ks-button");
            remove.AddToClassList("trade-offer-step");
            row.Add(remove);
        }
        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 1)
            {
                evt.StopPropagation();
                onDetails?.Invoke();
            }
            else if (evt.button == 0 && !(evt.target is Button))
            {
                evt.StopPropagation();
                if (evt.clickCount >= 2)
                {
                    CancelTradeDrag();
                    onLess();
                }
                else
                {
                    BeginTradeDragCandidate(evt, source, null, name, onRemoveAll);
                }
            }
        });
        return row;
    }

    private static Label CreateTradeOfferEmpty(string text)
    {
        Label label = new Label(text);
        label.AddToClassList("trade-offer-empty");
        return label;
    }

    // ------------------------------------------------------------------
    // Изменение предложения (только намерение)
    // ------------------------------------------------------------------

    private static int TradeMaxQuantity(TradeOfferView offer)
    {
        if (offer.Kind == TradeLineKind.Buyback)
            return 1;
        return offer.Item != null && offer.Item.Unique ? Mathf.Min(1, offer.Available) : offer.Available;
    }

    private void ChangeTradeBuy(TradeOfferView offer, int delta)
    {
        if (delta > 0 && !offer.Enabled)
        {
            tradeReason.text = offer.Reason;
            return;
        }
        int quantity = Mathf.Clamp(tradeCart.QuantityOf(offer.Kind, offer.Key) + delta, 0, TradeMaxQuantity(offer));
        tradeCart.SetQuantity(offer.Kind, offer.Key, quantity);
        RenderSettlementTrade();
    }

    private void AddTradeSell(TradeSellView sell)
    {
        if (!sell.Enabled)
        {
            RenderSettlementTrade();
            tradeReason.text = sell.Reason;
            return;
        }
        if (!tradeCart.SellInstanceIds.Contains(sell.Instance.InstanceId))
            tradeCart.ToggleSell(sell.Instance.InstanceId);
        RenderSettlementTrade();
    }

    private void RemoveTradeSell(TradeSellView sell)
    {
        if (tradeCart.SellInstanceIds.Contains(sell.Instance.InstanceId))
            tradeCart.ToggleSell(sell.Instance.InstanceId);
        RenderSettlementTrade();
    }

    private void ChangeTradeSupplySale(int delta)
    {
        tradeCart.SellSupplies = Mathf.Clamp(tradeCart.SellSupplies + delta, 0, gameState.ArmySupply);
        RenderSettlementTrade();
    }

    // ------------------------------------------------------------------
    // Сведения по правой кнопке
    // ------------------------------------------------------------------

    private void ShowTradePopupForOffer(TradeOfferView offer)
    {
        if (offer.Kind == TradeLineKind.Supplies)
        {
            ShowTradePopup(null, "Припасы в дорогу", "общий запас похода, не вещь в сумке",
                "Крупа, сало, сухари — сразу в общие припасы отряда. Места в сумке не занимают.",
                "+1 припас за единицу. Отряд ест " + gameState.ExpeditionSupplyConsumption + " в день.",
                "Сейчас припасов: " + gameState.ArmySupply + ".",
                "Цена: " + offer.UnitPrice + " зол. за единицу · есть: " + offer.Available);
            return;
        }
        ShowTradeItemPopup(offer.Item, offer.Instance, "Цена: " + offer.UnitPrice + " зол." +
                                                       (offer.Kind == TradeLineKind.Buyback ? " (выкуп)" : " · есть: " + offer.Available) +
                                                       (offer.Enabled ? string.Empty : "\n" + offer.Reason));
    }

    private void ShowTradePopupForSell(TradeSellView sell)
    {
        ShowTradeItemPopup(sell.Item, sell.Instance, sell.Enabled ? "Торговец даст: " + sell.UnitPrice + " зол." : sell.Reason);
    }

    private void ShowTradeSuppliesPopup(MerchantDefinition definition)
    {
        ShowTradePopup(null, "Припасы отряда", "общий запас похода",
            "Лишнее можно продать: торговец платит за единицу меньше, чем просит сам.",
            "Отряд ест " + gameState.ExpeditionSupplyConsumption + " в день.",
            "Сейчас припасов: " + gameState.ArmySupply + ".",
            "Торговец даст: " + TradeService.SupplySellPrice(definition) + " зол. за единицу");
    }

    private void ShowTradeItemPopup(ItemDefinition item, ItemInstanceData instance, string price)
    {
        if (item == null)
            return;
        string meta = TradeKindLabel(item) + (item.Unique ? " · одна на мир" : string.Empty) +
                      (item.Uses > 0 ? " · применений: " + (instance != null ? instance.UsesLeft : item.Uses) + " из " + item.Uses : string.Empty);
        // Бриф правок (19 пунктов) п. 6: те же сведения, что в сумке.
        ShowTradePopup(item.Id, item.Name, meta, string.Join("\n\n", ItemInfoLines(item, instance, null)),
            string.Empty, DescribeTradeCompare(item), price);
    }

    private void ShowTradePopup(string itemId, string name, string meta, string text, string mechanics, string compare, string price)
    {
        Sprite sprite = string.IsNullOrEmpty(itemId) ? null : ItemDatabaseRuntime.Icon(itemId);
        tradePopupIcon.image = sprite != null ? sprite.texture : null;
        if (sprite != null)
            tradePopupIcon.uv = new Rect(sprite.textureRect.x / sprite.texture.width, sprite.textureRect.y / sprite.texture.height,
                sprite.textureRect.width / sprite.texture.width, sprite.textureRect.height / sprite.texture.height);
        tradePopupIcon.scaleMode = ScaleMode.ScaleToFit;
        tradePopupName.text = name;
        tradePopupMeta.text = meta;
        tradePopupText.text = text;
        tradePopupMechanics.text = mechanics;
        tradePopupMechanics.style.display = string.IsNullOrEmpty(mechanics) ? DisplayStyle.None : DisplayStyle.Flex;
        tradePopupCompare.text = compare;
        tradePopupCompare.style.display = string.IsNullOrEmpty(compare) ? DisplayStyle.None : DisplayStyle.Flex;
        tradePopupPrice.text = price;
        tradePopup.style.display = DisplayStyle.Flex;
        tradePopup.BringToFront();
    }

    private void HideTradePopup()
    {
        if (tradePopup != null)
            tradePopup.style.display = DisplayStyle.None;
    }

    // Портреты: торговец — говорящий Базы диалогов (или инициал), герой —
    // тот же портрет, что на экране героя.
    private void ApplyTradePortraits(MerchantDefinition definition)
    {
        tradeMerchantPortrait.Clear();
        if (narrativeDialogueDatabase == null)
            narrativeDialogueDatabase = KingdomSurvival.DialogueDatabase.DialogueDatabaseRuntime.LoadDefaultDatabase();
        Sprite sprite = !string.IsNullOrEmpty(definition.SpeakerId) ? narrativeDialogueDatabase?.FindSpeaker(definition.SpeakerId)?.Portrait : null;
        tradeMerchantPortrait.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : new StyleBackground(StyleKeyword.None);
        if (sprite == null)
        {
            Label initial = new Label(string.IsNullOrEmpty(definition.DisplayName) ? "?" : definition.DisplayName.Substring(0, 1));
            initial.AddToClassList("trade-portrait-initial");
            tradeMerchantPortrait.Add(initial);
        }
        if (tradeHeroPortrait == null)
            tradeHeroPortrait = CreateHeroScreenPortrait(tradeHeroPortraitSlot);
        ApplyHeroScreenPortrait(tradeHeroPortrait, ResolveHeroScreenUnit(gameState.GetSelectedCommander()));
    }

    private VisualElement CreateTradeRow(string itemId, string name, string detail, string price, string cart,
        bool selected, bool inCart, bool disabled)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("trade-row");
        row.EnableInClassList("trade-row--selected", selected);
        row.EnableInClassList("trade-row--in-cart", inCart);
        row.EnableInClassList("trade-row--disabled", disabled);
        VisualElement icon = new VisualElement();
        icon.AddToClassList("trade-row-icon");
        Sprite sprite = string.IsNullOrEmpty(itemId) ? null : ItemDatabaseRuntime.Icon(itemId);
        if (sprite != null)
            icon.style.backgroundImage = new StyleBackground(sprite);
        row.Add(icon);
        VisualElement text = new VisualElement();
        text.AddToClassList("trade-row-text");
        Label nameLabel = new Label(name);
        nameLabel.AddToClassList("trade-row-name");
        text.Add(nameLabel);
        if (!string.IsNullOrEmpty(detail))
        {
            Label detailLabel = new Label(detail);
            detailLabel.AddToClassList("trade-row-detail");
            text.Add(detailLabel);
        }
        row.Add(text);
        Label priceLabel = new Label(price);
        priceLabel.AddToClassList("trade-row-price");
        row.Add(priceLabel);
        if (!string.IsNullOrEmpty(cart))
        {
            Label cartLabel = new Label(cart);
            cartLabel.AddToClassList("trade-row-cart");
            row.Add(cartLabel);
        }
        return row;
    }

    private static Label CreateTradeEmpty(string text)
    {
        Label label = new Label(text);
        label.AddToClassList("trade-row-detail");
        label.AddToClassList("settlement-action-hint");
        return label;
    }

    // Сравнение с надетым у Командира тем же общим расчётом (StatModifier).
    private string DescribeTradeCompare(ItemDefinition item)
    {
        if (item.Slot == ItemSlot.None)
            return item.Kind == ItemKind.TradeGood ? "Товар: его продают, а не носят." : string.Empty;
        CommanderData commander = gameState.GetSelectedCommander();
        if (commander == null)
            return string.Empty;
        ItemInstanceData current = ItemService.Equipped(gameState, commander.Id, item.Slot);
        ItemDefinition currentDefinition = current != null ? ItemCatalog.Find(current.ItemId) : null;
        string fit = ItemService.CanUseSlotItem(gameState, commander.Id, item, out string reason) ? string.Empty : " (" + reason + ")";
        if (currentDefinition == null)
            return "У Командира этот слот свободен" + fit + ".";
        string mod = currentDefinition.Modifier.Describe();
        return "Сейчас у Командира: " + currentDefinition.Name + (mod.Length > 0 ? " (" + mod + ")" : string.Empty) + fit + ".";
    }

    // Подтверждение: та же сумма, что на экране; повторный клик по уже
    // проведённой сделке ничего не делает (ревизия торговца сменилась,
    // предложение пусто).
    private void OnTradeConfirmClicked()
    {
        if (!IsSettlementTradeOpen || tradeCart.IsEmpty)
            return;
        TradeQuote shown = TradeService.Quote(gameState, tradeCart);
        if (!shown.Valid)
        {
            RenderSettlementTrade();
            return;
        }
        if (!TradeService.TryConfirm(gameState, tradeCart, shown.Net, out string message))
        {
            ResetTradeCart();
            RenderSettlementTrade();
            tradeReason.text = message;
            return;
        }
        AddReport(message);
        tradeHighlightKey = null;
        // Подтверждённая непустая сделка — один шаг часов (п. 14).
        SpendSettlementActionTime();
        Autosave();
        RefreshInterface();
        RenderSettlementTrade();
        tradeReason.text = message;
    }

    private static bool PassesTradeFilter(ItemDefinition item, bool supplies, int filter, string search, string name)
    {
        if (!string.IsNullOrWhiteSpace(search) &&
            (name ?? string.Empty).IndexOf(search.Trim(), System.StringComparison.CurrentCultureIgnoreCase) < 0)
            return false;
        if (filter <= 0)
            return true;
        if (supplies)
            return filter == 4;
        switch (filter)
        {
            case 1: return item.Kind == ItemKind.Weapon;
            case 2: return item.Kind == ItemKind.Protection;
            case 3: return item.Kind == ItemKind.Special;
            case 4: return item.Kind == ItemKind.Consumable;
            case 5: return item.Kind == ItemKind.TradeGood;
            default: return true;
        }
    }

    private static int CompareSellables(TradeSellView a, TradeSellView b)
    {
        int byKind = ((int)a.Item.Kind).CompareTo((int)b.Item.Kind);
        if (byKind != 0)
            return byKind;
        int byName = string.Compare(a.Item.Name, b.Item.Name, System.StringComparison.CurrentCulture);
        return byName != 0 ? byName : string.CompareOrdinal(a.Instance.InstanceId, b.Instance.InstanceId);
    }

    private static string TradeKindLabel(ItemDefinition item)
    {
        string kind;
        switch (item.Kind)
        {
            case ItemKind.Weapon: kind = "оружие"; break;
            case ItemKind.Protection: kind = "защита"; break;
            case ItemKind.Special: kind = "особое"; break;
            case ItemKind.Consumable: kind = "расходное"; break;
            case ItemKind.TradeGood: kind = "товар"; break;
            default: kind = "сюжетное"; break;
        }
        return item.Artifact ? kind + ", артефакт" : kind;
    }
}
