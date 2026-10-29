using System;
using System.Collections.Generic;

// 12Т (SETTLEMENTS_TRADE_TAVERN_SPEC.md §10): торговля корзиной. Корзина —
// временное намерение UI (в сохранение не попадает): нажатия +/− ничего не
// меняют. Quote считает показанные цены и итог; Confirm заново проверяет всё
// и одной операцией переносит вещи, деньги и припасы — или не меняет ничего.
// Повтор подтверждения отсекает ревизия торговца: после успешной сделки она
// растёт, и та же корзина уже не проходит.
//
// Деньги — кошелёк похода (GameState.ArmyGold), не казна Дома (Gold).

public enum TradeLineKind
{
    // Строка ассортимента (ItemId).
    Stock,
    // Выкуп проданного экземпляра (InstanceId).
    Buyback,
    // Абстрактные припасы отряда.
    Supplies
}

public sealed class TradeBuyLine
{
    public TradeLineKind Kind;
    // ItemId для Stock, InstanceId для Buyback, пусто для Supplies.
    public string Key = string.Empty;
    public int Quantity;
}

public sealed class TradeCart
{
    public string LocationId = string.Empty;
    public string MerchantId = string.Empty;
    // Ревизия торговца, на которой показаны цены.
    public int Revision;
    public readonly List<TradeBuyLine> Buy = new List<TradeBuyLine>();
    public readonly List<string> SellInstanceIds = new List<string>();
    // Бриф исправлений п. 2: припасы отряда тоже продаются (единиц).
    public int SellSupplies;

    public bool IsEmpty => Buy.Count == 0 && SellInstanceIds.Count == 0 && SellSupplies == 0;

    public void Clear()
    {
        Buy.Clear();
        SellInstanceIds.Clear();
        SellSupplies = 0;
    }

    public int QuantityOf(TradeLineKind kind, string key)
    {
        TradeBuyLine line = Buy.Find(l => l.Kind == kind && l.Key == (key ?? string.Empty));
        return line != null ? line.Quantity : 0;
    }

    public void SetQuantity(TradeLineKind kind, string key, int quantity)
    {
        key ??= string.Empty;
        TradeBuyLine line = Buy.Find(l => l.Kind == kind && l.Key == key);
        if (quantity <= 0)
        {
            if (line != null)
                Buy.Remove(line);
            return;
        }
        if (line == null)
            Buy.Add(new TradeBuyLine { Kind = kind, Key = key, Quantity = quantity });
        else
            line.Quantity = quantity;
    }

    public void ToggleSell(string instanceId)
    {
        if (!SellInstanceIds.Remove(instanceId))
            SellInstanceIds.Add(instanceId);
    }
}

// Строка ассортимента для показа: что, почём, сколько.
public sealed class TradeOfferView
{
    public TradeLineKind Kind;
    public string Key = string.Empty;
    public ItemDefinition Item;
    public ItemInstanceData Instance;
    public int Available;
    public int UnitPrice;
    public int SupplyUnits;
    public bool Enabled = true;
    public string Reason = string.Empty;
    public string Name => Kind == TradeLineKind.Supplies ? "Припасы в дорогу" : Item != null ? Item.Name : Key;
}

// Вещь отряда, которую можно (или нельзя) продать.
public sealed class TradeSellView
{
    public ItemInstanceData Instance;
    public ItemDefinition Item;
    public int UnitPrice;
    public bool Enabled = true;
    public string Reason = string.Empty;
}

public sealed class TradeQuote
{
    public bool Valid;
    public string Reason = string.Empty;
    public int BuyTotal;
    public int SellTotal;
    // Положительное — игрок доплачивает, отрицательное — получает.
    public int Net => BuyTotal - SellTotal;
    public int PlayerMoneyAfter;
    public int MerchantMoneyAfter;
    public int PackAfter;
    public int PackLimit = ItemService.HeroPackSize;
    public int SuppliesGained;
    public int SuppliesSold;
    public int Revision;
}

public static class TradeService
{
    public const int MaxQuantity = 999;

    // ------------------------------------------------------------------
    // Цены: базовая цена × процент торговца. Покупка — вверх, продажа —
    // вниз; платная вещь стоит хотя бы 1; продажа не дороже покупки.
    // Частично использованная вещь — по оставшимся применениям.
    // ------------------------------------------------------------------

    public static int BuyPrice(ItemDefinition item, MerchantDefinition merchant, int usesLeft = -1)
    {
        if (item == null || merchant == null || item.Price <= 0)
            return 0;
        long numerator = (long)item.Price * Math.Max(0, merchant.BuyPercent);
        long denominator = 100;
        if (usesLeft >= 0 && item.Uses > 0)
        {
            numerator *= Math.Max(0, Math.Min(usesLeft, item.Uses));
            denominator *= item.Uses;
        }
        long price = (numerator + denominator - 1) / denominator;
        return (int)Math.Max(1, Math.Min(int.MaxValue / 4, price));
    }

    public static int SellPrice(ItemDefinition item, MerchantDefinition merchant, int usesLeft = -1)
    {
        if (item == null || merchant == null || item.Price <= 0)
            return 0;
        long numerator = (long)item.Price * Math.Max(0, merchant.SellPercent);
        long denominator = 100;
        if (usesLeft >= 0 && item.Uses > 0)
        {
            numerator *= Math.Max(0, Math.Min(usesLeft, item.Uses));
            denominator *= item.Uses;
        }
        long price = numerator / denominator;
        int sell = (int)Math.Max(1, Math.Min(int.MaxValue / 4, price));
        return Math.Min(sell, BuyPrice(item, merchant, usesLeft));
    }

    // Цена единицы припасов, которые торговец покупает у отряда: от его
    // цены продажи припасов (или 2, если своих нет), не дороже её.
    public static int SupplySellPrice(MerchantDefinition merchant)
    {
        if (merchant == null)
            return 0;
        MerchantStockDefinition line = merchant.Stock?.Find(s => s != null && s.IsSupplies);
        int unit = line != null ? Math.Max(1, line.SupplyUnitPrice) : 2;
        int price = (int)((long)unit * Math.Max(0, merchant.SellPercent) / 100);
        return Math.Max(1, Math.Min(unit, price));
    }

    // Нетронутая вещь (без зарядов или с полными) — неотличима от новой:
    // выкуп такой вещи показывается одной карточкой с обычным товаром.
    private static bool IsPlain(ItemInstanceData instance, ItemDefinition item)
    {
        return item != null && (item.Uses <= 0 || instance.UsesLeft >= item.Uses);
    }

    private static List<ItemInstanceData> PlainBuyback(MerchantStateData merchant, string itemId)
    {
        List<ItemInstanceData> result = new List<ItemInstanceData>();
        foreach (ItemInstanceData instance in merchant.Buyback)
        {
            if (instance.ItemId == itemId && IsPlain(instance, ItemCatalog.Find(itemId)))
                result.Add(instance);
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Показ
    // ------------------------------------------------------------------

    public static bool TryOpen(GameState state, string locationId, string merchantId,
        out MerchantDefinition definition, out MerchantStateData merchant, out string reason)
    {
        merchant = null;
        definition = SettlementService.Profile(state, locationId)?.FindMerchant(merchantId);
        if (!SettlementService.CanActInside(state, locationId, out reason))
            return false;
        if (definition == null)
        {
            reason = "Этого торговца здесь нет.";
            return false;
        }
        merchant = SettlementService.EnsureMerchant(state, locationId, definition);
        return merchant != null;
    }

    public static List<TradeOfferView> Offers(GameState state, MerchantDefinition definition, MerchantStateData merchant)
    {
        List<TradeOfferView> offers = new List<TradeOfferView>();
        if (definition == null || merchant == null)
            return offers;
        HashSet<string> listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (MerchantStockDefinition line in definition.Stock)
        {
            if (line == null)
                continue;
            MerchantStockStateData stock = SettlementService.FindStock(merchant, line);
            int count = stock != null ? stock.Count : 0;
            if (line.IsSupplies)
            {
                // Бриф исправлений п. 10: кончилось — карточки нет.
                if (count <= 0)
                    continue;
                offers.Add(new TradeOfferView
                {
                    Kind = TradeLineKind.Supplies,
                    Available = count,
                    UnitPrice = Math.Max(1, line.SupplyUnitPrice),
                    SupplyUnits = 1
                });
                continue;
            }
            ItemDefinition item = ItemCatalog.Find(line.ItemId);
            if (item == null || listed.Contains(item.Id))
                continue; // битая ссылка видна в редакторе, игроку — нет
            listed.Add(item.Id);
            bool issued = item.Unique && SettlementService.IsUniqueIssued(state, item.Id);
            int available = (issued ? 0 : count) + PlainBuyback(merchant, item.Id).Count;
            if (available <= 0 || !item.CanBuy || item.IsTradeProtected)
                continue;
            offers.Add(new TradeOfferView
            {
                Kind = TradeLineKind.Stock,
                Key = item.Id,
                Item = item,
                Available = available,
                UnitPrice = BuyPrice(item, definition, item.Uses > 0 ? item.Uses : -1)
            });
        }
        // Проданные отрядом нетронутые вещи, которых нет в ассортименте, —
        // одной карточкой на вид; вещи с другими зарядами — каждая своей.
        foreach (ItemInstanceData instance in merchant.Buyback)
        {
            ItemDefinition item = ItemCatalog.Find(instance.ItemId);
            if (item == null)
                continue;
            if (IsPlain(instance, item))
            {
                if (listed.Contains(item.Id))
                    continue;
                listed.Add(item.Id);
                offers.Add(new TradeOfferView
                {
                    Kind = TradeLineKind.Stock,
                    Key = item.Id,
                    Item = item,
                    Available = PlainBuyback(merchant, item.Id).Count,
                    UnitPrice = BuyPrice(item, definition, item.Uses > 0 ? item.Uses : -1)
                });
                continue;
            }
            offers.Add(new TradeOfferView
            {
                Kind = TradeLineKind.Buyback,
                Key = instance.InstanceId,
                Item = item,
                Instance = instance,
                Available = 1,
                UnitPrice = BuyPrice(item, definition, item.Uses > 0 ? instance.UsesLeft : -1)
            });
        }
        return offers;
    }

    // Что отряд может продать здесь: неэкипированные вещи вошедших. Вещи
    // ожидающих в лагере и кладовая Дома отсюда не видны.
    public static List<TradeSellView> Sellables(GameState state, MerchantDefinition definition)
    {
        List<TradeSellView> result = new List<TradeSellView>();
        if (state?.Inventory?.Items == null)
            return result;
        foreach (ItemInstanceData instance in ItemService.Get(state).Items)
        {
            if (string.IsNullOrEmpty(instance.OwnerPersonId) || !ItemService.IsReachable(state, instance))
                continue;
            ItemDefinition item = ItemCatalog.Find(instance.ItemId);
            if (item == null || item.IsStory)
                continue;
            if (instance.Slot != ItemSlot.None)
                continue;
            TradeSellView view = new TradeSellView
            {
                Instance = instance,
                Item = item,
                UnitPrice = SellPrice(item, definition, item.Uses > 0 ? instance.UsesLeft : -1)
            };
            if (item.IsTradeProtected || !item.CanSell)
            {
                view.Enabled = false;
                view.Reason = "Эту вещь не продают.";
            }
            else if (view.UnitPrice <= 0)
            {
                view.Enabled = false;
                view.Reason = "Торговцу это не нужно.";
            }
            result.Add(view);
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Расчёт и подтверждение
    // ------------------------------------------------------------------

    public static TradeQuote Quote(GameState state, TradeCart cart)
    {
        Validate(state, cart, out TradeQuote quote, out _, out _, out _, out _);
        return quote;
    }

    // Подтвердить сделку. expectedNet — итог, который видел игрок: при
    // изменившихся ценах/остатках сделка не проходит и просит перепроверить.
    public static bool TryConfirm(GameState state, TradeCart cart, int expectedNet, out string message)
    {
        if (!Validate(state, cart, out TradeQuote quote, out MerchantDefinition definition, out MerchantStateData merchant,
                out List<ItemInstanceData> sold, out List<(TradeBuyLine Line, ItemDefinition Item, ItemInstanceData Buyback)> bought))
        {
            message = quote.Reason;
            return false;
        }
        if (quote.Net != expectedNet)
        {
            message = "Цены или запас изменились — проверьте итог сделки ещё раз.";
            return false;
        }

        // Всё проверено — применяем одной операцией, без промежуточных отказов.
        CommanderData commander = state.GetSelectedCommander();
        List<string> soldNames = new List<string>();
        List<string> boughtNames = new List<string>();
        foreach (ItemInstanceData instance in sold)
        {
            ItemDefinition item = ItemCatalog.Find(instance.ItemId);
            ItemService.Detach(state, instance);
            instance.OwnerPersonId = string.Empty;
            instance.Slot = ItemSlot.None;
            merchant.Buyback.Add(instance);
            soldNames.Add(item.Name);
        }
        foreach ((TradeBuyLine line, ItemDefinition item, ItemInstanceData buyback) in bought)
        {
            switch (line.Kind)
            {
                case TradeLineKind.Supplies:
                {
                    MerchantStockStateData stock = merchant.Stock.Find(s => s.IsSupplies);
                    stock.Count -= line.Quantity;
                    state.ArmySupply += line.Quantity;
                    boughtNames.Add("припасы ×" + line.Quantity);
                    break;
                }
                case TradeLineKind.Buyback:
                    merchant.Buyback.Remove(buyback);
                    ItemService.Attach(state, buyback, commander.Id);
                    boughtNames.Add(item.Name);
                    break;
                default:
                {
                    // Сначала — те же экземпляры, что отряд продал (InstanceId
                    // сохраняется), затем новые из запаса торговца.
                    List<ItemInstanceData> plain = PlainBuyback(merchant, item.Id);
                    int fromBuyback = Math.Min(line.Quantity, plain.Count);
                    for (int i = 0; i < fromBuyback; i++)
                    {
                        merchant.Buyback.Remove(plain[i]);
                        ItemService.Attach(state, plain[i], commander.Id);
                    }
                    int fromStock = line.Quantity - fromBuyback;
                    if (fromStock > 0)
                    {
                        MerchantStockStateData stock = merchant.Stock.Find(s => !s.IsSupplies && s.ItemId == item.Id);
                        stock.Count -= fromStock;
                        for (int i = 0; i < fromStock; i++)
                            ItemService.CreateInstance(state, item.Id, commander.Id);
                        MerchantStockDefinition stockLine = definition.Stock.Find(s => s != null && !s.IsSupplies && s.ItemId == item.Id);
                        if (stockLine != null && stockLine.Unique && stock.Count <= 0 && !merchant.SoldOutUnique.Contains(item.Id))
                            merchant.SoldOutUnique.Add(item.Id);
                        if (item.Unique && !state.Settlements.IssuedUniqueItemIds.Contains(item.Id))
                            state.Settlements.IssuedUniqueItemIds.Add(item.Id);
                    }
                    boughtNames.Add(line.Quantity > 1 ? item.Name + " ×" + line.Quantity : item.Name);
                    break;
                }
            }
        }

        if (quote.SuppliesSold > 0)
        {
            state.ArmySupply -= quote.SuppliesSold;
            MerchantStockStateData supplyStock = merchant.Stock.Find(s => s.IsSupplies);
            if (supplyStock != null)
                supplyStock.Count += quote.SuppliesSold;
            soldNames.Add("припасы ×" + quote.SuppliesSold);
        }
        state.ArmyGold = quote.PlayerMoneyAfter;
        merchant.Money = quote.MerchantMoneyAfter;
        merchant.Revision++;
        state.Settlements.TradeSequence++;
        cart.Clear();
        cart.Revision = merchant.Revision;

        List<string> parts = new List<string>();
        if (boughtNames.Count > 0)
            parts.Add("куплено: " + string.Join(", ", boughtNames));
        if (soldNames.Count > 0)
            parts.Add("продано: " + string.Join(", ", soldNames));
        string money = quote.Net > 0 ? "отдано " + quote.Net + " зол." : quote.Net < 0 ? "получено " + (-quote.Net) + " зол." : "без доплаты";
        message = "Сделка: " + string.Join("; ", parts) + " (" + money + ").";
        return true;
    }

    private static bool Validate(GameState state, TradeCart cart, out TradeQuote quote,
        out MerchantDefinition definition, out MerchantStateData merchant,
        out List<ItemInstanceData> sold, out List<(TradeBuyLine Line, ItemDefinition Item, ItemInstanceData Buyback)> bought)
    {
        quote = new TradeQuote();
        sold = new List<ItemInstanceData>();
        bought = new List<(TradeBuyLine, ItemDefinition, ItemInstanceData)>();
        merchant = null;
        definition = null;
        if (cart == null)
        {
            quote.Reason = "Сделки нет.";
            return false;
        }
        if (!TryOpen(state, cart.LocationId, cart.MerchantId, out definition, out merchant, out string reason))
        {
            quote.Reason = reason;
            return false;
        }
        quote.Revision = merchant.Revision;
        CommanderData commander = state.GetSelectedCommander();
        if (commander == null)
        {
            quote.Reason = "Командира нет.";
            return false;
        }
        if (cart.Revision != merchant.Revision)
        {
            quote.Reason = "Прилавок изменился — проверьте сделку ещё раз.";
            return false;
        }

        long buyTotal = 0;
        long sellTotal = 0;
        int packFreed = 0;
        int packAdded = 0;
        int supplies = 0;

        HashSet<string> sellIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string instanceId in cart.SellInstanceIds)
        {
            if (!sellIds.Add(instanceId ?? string.Empty))
            {
                quote.Reason = "Одна вещь указана в сделке дважды.";
                return false;
            }
            ItemInstanceData instance = ItemService.Find(state, instanceId);
            ItemDefinition item = instance != null ? ItemCatalog.Find(instance.ItemId) : null;
            if (item == null || string.IsNullOrEmpty(instance.OwnerPersonId) || !ItemService.IsReachable(state, instance))
            {
                quote.Reason = "Этой вещи нет у тех, кто здесь.";
                return false;
            }
            if (instance.Slot != ItemSlot.None)
            {
                quote.Reason = item.Name + " надета — сначала снимите её.";
                return false;
            }
            if (item.IsTradeProtected || !item.CanSell)
            {
                quote.Reason = item.Name + " не продаётся.";
                return false;
            }
            int price = SellPrice(item, definition, item.Uses > 0 ? instance.UsesLeft : -1);
            if (price <= 0)
            {
                quote.Reason = "Торговцу не нужна вещь «" + item.Name + "».";
                return false;
            }
            sellTotal += price;
            if (instance.OwnerPersonId == commander.Id && !item.IsStory)
                packFreed++;
            sold.Add(instance);
        }

        HashSet<string> buyKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (TradeBuyLine line in cart.Buy)
        {
            if (line == null || line.Quantity <= 0 || line.Quantity > MaxQuantity)
            {
                quote.Reason = "Неверное количество в сделке.";
                return false;
            }
            if (!buyKeys.Add(line.Kind + ":" + line.Key))
            {
                quote.Reason = "Одна строка указана в сделке дважды.";
                return false;
            }
            switch (line.Kind)
            {
                case TradeLineKind.Supplies:
                {
                    MerchantStockDefinition stockLine = definition.Stock.Find(s => s != null && s.IsSupplies);
                    MerchantStockStateData stock = merchant.Stock.Find(s => s.IsSupplies);
                    if (stockLine == null || stock == null || stock.Count < line.Quantity)
                    {
                        quote.Reason = "Столько припасов у торговца нет.";
                        return false;
                    }
                    buyTotal += (long)Math.Max(1, stockLine.SupplyUnitPrice) * line.Quantity;
                    supplies += line.Quantity;
                    bought.Add((line, null, null));
                    break;
                }
                case TradeLineKind.Buyback:
                {
                    ItemInstanceData instance = merchant.Buyback.Find(i => i.InstanceId == line.Key);
                    ItemDefinition item = instance != null ? ItemCatalog.Find(instance.ItemId) : null;
                    if (item == null || line.Quantity != 1)
                    {
                        quote.Reason = "Этой вещи у торговца уже нет.";
                        return false;
                    }
                    if (sellIds.Contains(line.Key))
                    {
                        quote.Reason = "Одну вещь нельзя продать и купить в одной сделке.";
                        return false;
                    }
                    buyTotal += BuyPrice(item, definition, item.Uses > 0 ? instance.UsesLeft : -1);
                    packAdded++;
                    bought.Add((line, item, instance));
                    break;
                }
                default:
                {
                    ItemDefinition item = ItemCatalog.Find(line.Key);
                    bool inAssortment = item != null && definition.Stock.Find(s => s != null && !s.IsSupplies && s.ItemId == item.Id) != null;
                    MerchantStockStateData stock = inAssortment ? merchant.Stock.Find(s => !s.IsSupplies && s.ItemId == item.Id) : null;
                    int plain = item != null ? PlainBuyback(merchant, item.Id).Count : 0;
                    int stockCount = stock != null ? stock.Count : 0;
                    if (item == null || (stock == null && plain == 0))
                    {
                        quote.Reason = "Этого у торговца нет.";
                        return false;
                    }
                    if (!item.CanBuy || item.IsTradeProtected)
                    {
                        quote.Reason = item.Name + " не продаётся.";
                        return false;
                    }
                    bool issued = item.Unique && plain == 0 && SettlementService.IsUniqueIssued(state, item.Id);
                    if ((issued ? plain : stockCount + plain) < line.Quantity)
                    {
                        quote.Reason = "У торговца нет столько: «" + item.Name + "».";
                        return false;
                    }
                    if (item.Unique && line.Quantity > 1)
                    {
                        quote.Reason = "«" + item.Name + "» — такая вещь одна.";
                        return false;
                    }
                    buyTotal += (long)BuyPrice(item, definition, item.Uses > 0 ? item.Uses : -1) * line.Quantity;
                    packAdded += line.Quantity;
                    bought.Add((line, item, null));
                    break;
                }
            }
        }

        if (cart.SellSupplies < 0 || cart.SellSupplies > MaxQuantity)
        {
            quote.Reason = "Неверное количество припасов.";
            return false;
        }
        if (cart.SellSupplies > 0)
        {
            if (supplies > 0)
            {
                quote.Reason = "Припасы в одной сделке либо покупают, либо продают.";
                return false;
            }
            if (cart.SellSupplies > state.ArmySupply)
            {
                quote.Reason = "У отряда только " + state.ArmySupply + " припасов.";
                return false;
            }
            sellTotal += (long)SupplySellPrice(definition) * cart.SellSupplies;
            quote.SuppliesSold = cart.SellSupplies;
        }

        if (buyTotal > int.MaxValue / 2 || sellTotal > int.MaxValue / 2)
        {
            quote.Reason = "Слишком большая сделка.";
            return false;
        }

        quote.BuyTotal = (int)buyTotal;
        quote.SellTotal = (int)sellTotal;
        quote.SuppliesGained = supplies;
        quote.PlayerMoneyAfter = (int)(state.ArmyGold - buyTotal + sellTotal);
        quote.MerchantMoneyAfter = (int)(merchant.Money + buyTotal - sellTotal);
        // Вместимость — по итоговому состоянию: проданное освобождает место.
        quote.PackAfter = ItemService.HeroPackCount(state) - packFreed + packAdded;

        if (cart.IsEmpty)
        {
            quote.Reason = "Корзина пуста.";
            return false;
        }
        if (quote.PlayerMoneyAfter < 0)
        {
            quote.Reason = "Не хватает " + (-quote.PlayerMoneyAfter) + " зол.";
            return false;
        }
        if (quote.MerchantMoneyAfter < 0)
        {
            quote.Reason = "У торговца не хватает " + (-quote.MerchantMoneyAfter) + " зол., чтобы расплатиться.";
            return false;
        }
        if (quote.PackAfter > ItemService.HeroPackSize)
        {
            quote.Reason = "Сумка героя не вместит: " + quote.PackAfter + " из " + ItemService.HeroPackSize + ".";
            return false;
        }
        quote.Valid = true;
        return true;
    }
}
