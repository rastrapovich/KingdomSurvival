using System;
using System.Collections.Generic;

// 12Т: изменяемое состояние поселений конкретной партии. Хранится в
// GameState.Settlements и сохраняется вместе с ним (JsonUtility: только
// списки, без словарей и null-ссылок). Ассеты баз во время игры не меняются.

[Serializable]
public sealed class MerchantStockStateData
{
    public string ItemId = string.Empty;
    public bool IsSupplies;
    public int Count;
}

[Serializable]
public sealed class MerchantStateData
{
    public string MerchantId = string.Empty;
    public bool Initialized;
    public int Money;
    public double NextRefreshHour;
    // Базовое предложение по строкам ассортимента.
    public List<MerchantStockStateData> Stock = new List<MerchantStockStateData>();
    // Проданные игроком экземпляры — тот же InstanceId и заряды; доступны для
    // выкупа до ближайшего обновления.
    public List<ItemInstanceData> Buyback = new List<ItemInstanceData>();
    // Уникальные строки, уже ушедшие с прилавка навсегда.
    public List<string> SoldOutUnique = new List<string>();
    // Растёт с каждым изменением: корзина со старой ревизией не проходит.
    public int Revision;
}

[Serializable]
public sealed class TavernOfferStateData
{
    public string OfferId = string.Empty;
    public int TimesTaken;
    public double LastTakenHour;
}

[Serializable]
public sealed class NoticeStateData
{
    public string NoticeId = string.Empty;
    public bool Read;
    // Начало текущего размещения (игровые часы); -1 — ещё не вывешено.
    public double PostedHour = -1.0;
    public int Postings;
}

[Serializable]
public sealed class SettlementInstanceData
{
    public string LocationId = string.Empty;
    public string ProfileId = string.Empty;
    public bool Visited;
    public List<MerchantStateData> Merchants = new List<MerchantStateData>();
    public List<TavernOfferStateData> Offers = new List<TavernOfferStateData>();
    public List<NoticeStateData> Notices = new List<NoticeStateData>();
}

[Serializable]
public sealed class ErrandStateData
{
    public string ErrandId = string.Empty;
    public double AcceptedHour;
    public bool Finished;
    public bool Failed;
}

[Serializable]
public sealed class SettlementsStateData
{
    public List<SettlementInstanceData> Instances = new List<SettlementInstanceData>();
    public List<ErrandStateData> Errands = new List<ErrandStateData>();
    // Уникальные вещи, уже выданные в мир (покупкой): второй раз их не
    // предложит ни один торговец.
    public List<string> IssuedUniqueItemIds = new List<string>();
    // Сделки по порядку (для донесений и защиты от повтора).
    public int TradeSequence;

    // Оплаченный ночлег в корчме: идёт вместе с ночлегом лагеря
    // (CampRest), пустой LocationId — ночлега нет.
    public string LodgingLocationId = string.Empty;
    public string LodgingName = string.Empty;
    public int LodgingHealHitPoints;
    public List<string> LodgingServedIds = new List<string>();
}
