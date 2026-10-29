using System.Collections.Generic;
using KingdomSurvival.ItemDatabase;
using UnityEngine;
using UnityEngine.UIElements;

// 12Т (SETTLEMENTS_TRADE_TAVERN_SPEC.md §10): страница торговли поселения.
// Слева — ассортимент торговца (и выкуп), справа — неэкипированные вещи
// вошедших; в середине карточка выбранной вещи. Корзина — только намерение:
// +/− и переключение ничего не меняют. Внизу закреплены деньги обеих
// сторон, суммы, итог «Доплатить/Получить», места в сумке после сделки и
// кнопки. Подтверждение — одна команда TradeService.TryConfirm (всё или
// ничего); цены и остатки считает только она и её Quote.
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
    private Image tradeCardImage;
    private Label tradeCardName;
    private Label tradeCardMeta;
    private Label tradeCardDescription;
    private Label tradeCardMechanics;
    private Label tradeCardCompare;
    private Label tradeCardPrice;
    private Button tradeMinusButton;
    private Label tradeQuantity;
    private Button tradePlusButton;
    private Button tradeToggleButton;
    private Label tradeCardReason;
    private Label tradeMoney;
    private Label tradeTotals;
    private Label tradeNet;
    private Label tradeReason;
    private Button tradeConfirmButton;

    private readonly TradeCart tradeCart = new TradeCart();
    private string tradeMerchantId;
    // Выбранная строка: продажа — InstanceId отряда, покупка — вид и ключ.
    private bool tradeSelectedIsSell;
    private TradeLineKind tradeSelectedKind;
    private string tradeSelectedKey;

    private bool IsSettlementTradeOpen => tradePage != null && tradePage.style.display == DisplayStyle.Flex;

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
        tradeCardImage = BindRequiredElement<Image>(settlementScreen, screen, "trade-card-image");
        tradeCardName = BindRequiredElement<Label>(settlementScreen, screen, "trade-card-name");
        tradeCardMeta = BindRequiredElement<Label>(settlementScreen, screen, "trade-card-meta");
        tradeCardDescription = BindRequiredElement<Label>(settlementScreen, screen, "trade-card-description");
        tradeCardMechanics = BindRequiredElement<Label>(settlementScreen, screen, "trade-card-mechanics");
        tradeCardCompare = BindRequiredElement<Label>(settlementScreen, screen, "trade-card-compare");
        tradeCardPrice = BindRequiredElement<Label>(settlementScreen, screen, "trade-card-price");
        tradeMinusButton = BindRequiredElement<Button>(settlementScreen, screen, "trade-minus-button");
        tradeQuantity = BindRequiredElement<Label>(settlementScreen, screen, "trade-quantity");
        tradePlusButton = BindRequiredElement<Button>(settlementScreen, screen, "trade-plus-button");
        tradeToggleButton = BindRequiredElement<Button>(settlementScreen, screen, "trade-toggle-button");
        tradeCardReason = BindRequiredElement<Label>(settlementScreen, screen, "trade-card-reason");
        tradeMoney = BindRequiredElement<Label>(settlementScreen, screen, "trade-money");
        tradeTotals = BindRequiredElement<Label>(settlementScreen, screen, "trade-totals");
        tradeNet = BindRequiredElement<Label>(settlementScreen, screen, "trade-net");
        tradeReason = BindRequiredElement<Label>(settlementScreen, screen, "trade-reason");
        tradeConfirmButton = BindRequiredElement<Button>(settlementScreen, screen, "trade-confirm-button");
        Button cancel = BindRequiredElement<Button>(settlementScreen, screen, "trade-cancel-button");
        Button back = BindRequiredElement<Button>(settlementScreen, screen, "trade-back-button");
        if (tradePage == null || tradeMerchantList == null || tradePartyList == null || tradeConfirmButton == null)
            return;

        foreach (DropdownField filter in new[] { tradeMerchantFilter, tradePartyFilter })
        {
            filter.choices = new List<string>(TradeFilterChoices);
            filter.index = 0;
            filter.RegisterValueChangedCallback(_ => RenderSettlementTrade());
        }
        tradeMerchantSearch.RegisterValueChangedCallback(_ => RenderSettlementTrade());
        tradePartySearch.RegisterValueChangedCallback(_ => RenderSettlementTrade());
        tradeMinusButton.clicked += () => ChangeTradeQuantity(-1);
        tradePlusButton.clicked += () => ChangeTradeQuantity(1);
        tradeToggleButton.clicked += OnTradeToggleClicked;
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
        tradePage.style.display = DisplayStyle.None;
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
        tradeSelectedKey = null;
        tradeMerchantSearch.SetValueWithoutNotify(string.Empty);
        tradePartySearch.SetValueWithoutNotify(string.Empty);
        tradePage.style.display = DisplayStyle.Flex;
        tradePage.BringToFront();
        SyncSettlementPageClass();
        RenderSettlementTrade();
    }

    private void HideSettlementTrade()
    {
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
        if (!tradeCart.IsEmpty)
        {
            AskSettlementConfirm("Отменить сделку? Ничего не будет куплено и продано.", () =>
            {
                HideSettlementTrade();
                RenderSettlement();
            });
            return;
        }
        HideSettlementTrade();
        RenderSettlement();
    }

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
        tradeSubtitle.text = (string.IsNullOrEmpty(definition.Role) ? string.Empty : definition.Role + " · ") +
                             "покупает за " + definition.SellPercent + "% цены, продаёт за " + definition.BuyPercent + "%";

        List<TradeOfferView> offers = TradeService.Offers(gameState, definition, merchant);
        List<TradeSellView> sellables = TradeService.Sellables(gameState, definition);
        sellables.Sort(CompareSellables);

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
        foreach (TradeSellView sell in sellables)
        {
            if (!PassesTradeFilter(sell.Item, false, tradePartyFilter.index, tradePartySearch.value, sell.Item.Name))
                continue;
            tradePartyList.Add(CreateTradeSellRow(sell));
        }
        if (tradePartyList.childCount == 0)
            tradePartyList.Add(CreateTradeEmpty(sellables.Count == 0
                ? "Продать нечего: надетое сначала снимите в снаряжении; вещи оставшихся в лагере отсюда не видны."
                : "Ничего под этот отбор."));

        RenderTradeCard(definition, offers, sellables);

        TradeQuote quote = TradeService.Quote(gameState, tradeCart);
        int playerAfter = tradeCart.IsEmpty ? gameState.ArmyGold : quote.PlayerMoneyAfter;
        int merchantAfter = tradeCart.IsEmpty ? merchant.Money : quote.MerchantMoneyAfter;
        int packAfter = tradeCart.IsEmpty ? ItemService.HeroPackCount(gameState) : quote.PackAfter;
        tradeMoney.text = "Ваше золото: " + gameState.ArmyGold + " → " + playerAfter +
                          "   ·   у торговца: " + merchant.Money + " → " + merchantAfter;
        tradeTotals.text = "Покупки: " + quote.BuyTotal + "   ·   продажи: " + quote.SellTotal +
                           "   ·   сумка после сделки: " + packAfter + " / " + ItemService.HeroPackSize +
                           (quote.SuppliesGained > 0 ? "   ·   припасы +" + quote.SuppliesGained : string.Empty);
        tradeNet.text = tradeCart.IsEmpty ? "Корзина пуста"
            : quote.Net > 0 ? "Доплатить: " + quote.Net + " зол."
            : quote.Net < 0 ? "Получить: " + (-quote.Net) + " зол."
            : "Без доплаты";
        tradeReason.text = tradeCart.IsEmpty ? string.Empty : quote.Valid ? string.Empty : quote.Reason;
        tradeConfirmButton.SetEnabled(!tradeCart.IsEmpty && quote.Valid);
    }

    private VisualElement CreateTradeOfferRow(TradeOfferView offer)
    {
        int inCart = tradeCart.QuantityOf(offer.Kind, offer.Key);
        string detail;
        if (offer.Kind == TradeLineKind.Supplies)
            detail = "в общие припасы похода · есть: " + offer.Available;
        else if (offer.Kind == TradeLineKind.Buyback)
            detail = "выкуп проданного" + (offer.Item.Uses > 0 ? " · осталось применений: " + offer.Instance.UsesLeft : string.Empty);
        else
            detail = TradeKindLabel(offer.Item) + " · есть: " + offer.Available;
        if (!offer.Enabled)
            detail += " · " + offer.Reason;
        bool selected = !tradeSelectedIsSell && tradeSelectedKind == offer.Kind && tradeSelectedKey == offer.Key;
        TradeOfferView captured = offer;
        return CreateTradeRow(offer.Item?.Id, offer.Name, detail, offer.UnitPrice + " зол.", inCart > 0 ? "×" + inCart : string.Empty,
            selected, inCart > 0, !offer.Enabled, () =>
            {
                tradeSelectedIsSell = false;
                tradeSelectedKind = captured.Kind;
                tradeSelectedKey = captured.Key;
                RenderSettlementTrade();
            });
    }

    private VisualElement CreateTradeSellRow(TradeSellView sell)
    {
        bool inCart = tradeCart.SellInstanceIds.Contains(sell.Instance.InstanceId);
        string detail = TradeKindLabel(sell.Item) + (sell.Item.Uses > 0 ? " · применений: " + sell.Instance.UsesLeft : string.Empty) +
                        (sell.Enabled ? string.Empty : " · " + sell.Reason);
        bool selected = tradeSelectedIsSell && tradeSelectedKey == sell.Instance.InstanceId;
        string instanceId = sell.Instance.InstanceId;
        return CreateTradeRow(sell.Item.Id, sell.Item.Name, detail, sell.Enabled ? sell.UnitPrice + " зол." : "—", inCart ? "продать" : string.Empty,
            selected, inCart, !sell.Enabled, () =>
            {
                tradeSelectedIsSell = true;
                tradeSelectedKey = instanceId;
                RenderSettlementTrade();
            });
    }

    private VisualElement CreateTradeRow(string itemId, string name, string detail, string price, string cart,
        bool selected, bool inCart, bool disabled, System.Action onSelect)
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
        Label detailLabel = new Label(detail);
        detailLabel.AddToClassList("trade-row-detail");
        text.Add(detailLabel);
        row.Add(text);
        Label priceLabel = new Label(price);
        priceLabel.AddToClassList("trade-row-price");
        row.Add(priceLabel);
        Label cartLabel = new Label(cart);
        cartLabel.AddToClassList("trade-row-cart");
        row.Add(cartLabel);
        row.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            onSelect();
        });
        return row;
    }

    private static Label CreateTradeEmpty(string text)
    {
        Label label = new Label(text);
        label.AddToClassList("trade-row-detail");
        label.AddToClassList("settlement-action-hint");
        return label;
    }

    private void RenderTradeCard(MerchantDefinition definition, List<TradeOfferView> offers, List<TradeSellView> sellables)
    {
        TradeOfferView offer = null;
        TradeSellView sell = null;
        if (tradeSelectedIsSell)
            sell = sellables.Find(s => s.Instance.InstanceId == tradeSelectedKey);
        else if (tradeSelectedKey != null)
            offer = offers.Find(o => o.Kind == tradeSelectedKind && o.Key == tradeSelectedKey);

        ItemDefinition item = sell != null ? sell.Item : offer?.Item;
        bool any = sell != null || offer != null;
        tradeMinusButton.style.display = DisplayStyle.None;
        tradePlusButton.style.display = DisplayStyle.None;
        tradeQuantity.style.display = DisplayStyle.None;
        tradeToggleButton.style.display = DisplayStyle.None;
        tradeCardReason.text = string.Empty;
        Sprite sprite = item != null ? ItemDatabaseRuntime.Icon(item.Id) : null;
        tradeCardImage.image = sprite != null ? sprite.texture : null;
        if (sprite != null)
            tradeCardImage.uv = new Rect(sprite.textureRect.x / sprite.texture.width, sprite.textureRect.y / sprite.texture.height,
                sprite.textureRect.width / sprite.texture.width, sprite.textureRect.height / sprite.texture.height);
        tradeCardImage.scaleMode = ScaleMode.ScaleToFit;

        if (!any)
        {
            tradeCardName.text = "Выберите вещь";
            tradeCardMeta.text = "Слева — товар торговца, справа — вещи отряда.";
            tradeCardDescription.text = string.Empty;
            tradeCardMechanics.text = string.Empty;
            tradeCardCompare.text = string.Empty;
            tradeCardPrice.text = string.Empty;
            return;
        }

        if (offer != null && offer.Kind == TradeLineKind.Supplies)
        {
            tradeCardName.text = "Припасы в дорогу";
            tradeCardMeta.text = "общий запас похода, не вещь в сумке";
            tradeCardDescription.text = "Крупа, сало, сухари — сразу в общие припасы отряда. Места в сумке не занимают.";
            tradeCardMechanics.text = "+1 припас за единицу. Отряд ест " + gameState.ExpeditionSupplyConsumption + " в день.";
            tradeCardCompare.text = "Сейчас припасов: " + gameState.ArmySupply + ".";
        }
        else
        {
            tradeCardName.text = item.Name;
            tradeCardMeta.text = TradeKindLabel(item) + (item.Unique ? " · одна на мир" : string.Empty) +
                                 (item.Uses > 0 ? " · применений: " + (sell != null ? sell.Instance.UsesLeft : offer.Instance != null ? offer.Instance.UsesLeft : item.Uses) + " из " + item.Uses : string.Empty);
            tradeCardDescription.text = item.Description + (string.IsNullOrWhiteSpace(item.Origin) ? string.Empty : "\n\n" + item.Origin);
            string mechanics = ItemUseService.Describe(item);
            tradeCardMechanics.text = string.IsNullOrEmpty(mechanics) ? string.Empty : "Действие: " + mechanics;
            tradeCardCompare.text = DescribeTradeCompare(item);
        }

        if (sell != null)
        {
            bool inCart = tradeCart.SellInstanceIds.Contains(sell.Instance.InstanceId);
            tradeCardPrice.text = sell.Enabled ? "Торговец даст: " + sell.UnitPrice + " зол." : string.Empty;
            tradeToggleButton.text = inCart ? "Не продавать" : "Продать";
            tradeToggleButton.style.display = DisplayStyle.Flex;
            tradeToggleButton.SetEnabled(sell.Enabled);
            tradeCardReason.text = sell.Enabled ? string.Empty : sell.Reason;
            return;
        }

        tradeCardPrice.text = "Цена: " + offer.UnitPrice + " зол. за " + (offer.Kind == TradeLineKind.Supplies ? "единицу" : "штуку");
        if (offer.Kind == TradeLineKind.Buyback)
        {
            bool inCart = tradeCart.QuantityOf(TradeLineKind.Buyback, offer.Key) > 0;
            tradeToggleButton.text = inCart ? "Не выкупать" : "Выкупить";
            tradeToggleButton.style.display = DisplayStyle.Flex;
            tradeToggleButton.SetEnabled(true);
            return;
        }
        int quantity = tradeCart.QuantityOf(offer.Kind, offer.Key);
        tradeQuantity.text = quantity.ToString();
        tradeQuantity.style.display = DisplayStyle.Flex;
        tradeMinusButton.style.display = DisplayStyle.Flex;
        tradePlusButton.style.display = DisplayStyle.Flex;
        tradeMinusButton.SetEnabled(quantity > 0);
        int max = offer.Item != null && offer.Item.Unique ? Mathf.Min(1, offer.Available) : offer.Available;
        tradePlusButton.SetEnabled(offer.Enabled && quantity < max);
        tradeCardReason.text = offer.Enabled ? string.Empty : offer.Reason;
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

    private void ChangeTradeQuantity(int delta)
    {
        if (tradeSelectedIsSell || tradeSelectedKey == null)
            return;
        int quantity = Mathf.Clamp(tradeCart.QuantityOf(tradeSelectedKind, tradeSelectedKey) + delta, 0, TradeService.MaxQuantity);
        tradeCart.SetQuantity(tradeSelectedKind, tradeSelectedKey, quantity);
        RenderSettlementTrade();
    }

    private void OnTradeToggleClicked()
    {
        if (tradeSelectedKey == null)
            return;
        if (tradeSelectedIsSell)
            tradeCart.ToggleSell(tradeSelectedKey);
        else if (tradeSelectedKind == TradeLineKind.Buyback)
            tradeCart.SetQuantity(TradeLineKind.Buyback, tradeSelectedKey, tradeCart.QuantityOf(TradeLineKind.Buyback, tradeSelectedKey) > 0 ? 0 : 1);
        RenderSettlementTrade();
    }

    // Подтверждение: та же сумма, что на экране; повторный клик по уже
    // проведённой корзине ничего не делает (ревизия торговца сменилась,
    // корзина пуста).
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
            tradeReason.text = message;
            ResetTradeCart();
            RenderSettlementTrade();
            tradeReason.text = message;
            return;
        }
        AddReport(message);
        tradeSelectedKey = null;
        homePeopleSignature = null;
        homePrepSignature = null;
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
