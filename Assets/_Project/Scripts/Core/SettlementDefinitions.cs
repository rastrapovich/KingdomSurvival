using System;
using System.Collections.Generic;

// 12Т (ProjectDocs/SETTLEMENTS_TRADE_TAVERN_SPEC.md): авторские определения
// посещаемых поселений. Чистые данные без UnityEngine: «База поселений»
// (KingdomSurvival.SettlementDatabase) переводит свои записи в эти классы и
// назначает SettlementCatalog.Current. Условия — существующие
// NarrativeConditionGroup (тот же evaluator, что у диалогов). Изменяемое
// состояние конкретной партии — в SettlementState.cs, не здесь.
//
// Конкретные поселения, люди, цены и тексты — [РАБОЧЕЕ].

// Новые значения enum — строго в конец: они сериализуются в базе.
public enum SettlementKind
{
    Village,
    TradingPost,
    Town
}

public enum SettlementActionType
{
    // Разговор с человеком: TargetId — DialogueId.
    Dialogue,
    // Торговля: TargetId — MerchantId этого поселения.
    Trade,
    // Корчма поселения.
    Tavern,
    // Ночлег в корчме / постоялом дворе поселения.
    Rest,
    // «Прогуляться»: TargetId — ID исследуемого места (База локаций).
    LocalMap,
    // Доска объявлений поселения.
    NoticeBoard
}

// Условный вариант описания и изображения: побеждает наибольший приоритет,
// при равенстве — порядок записи. Без совпадений — описание профиля.
[Serializable]
public sealed class SettlementVariantDefinition
{
    public string Id = string.Empty;
    public int Priority;
    public NarrativeConditionGroup Conditions = new NarrativeConditionGroup();
    public string Description = string.Empty;
}

// Человек поселения или корчмы: портрет и имя — из говорящего Базы
// диалогов (SpeakerId), разговор — DialogueId.
[Serializable]
public sealed class SettlementPersonDefinition
{
    public string Id = string.Empty;
    public string SpeakerId = string.Empty;
    public string DisplayName = string.Empty;
    public string Role = string.Empty;
    // Чем занят прямо сейчас — строка корчмы («Греет руки у печи»).
    public string Activity = string.Empty;
    public string DialogueId = string.Empty;
    // Торгует ли этот человек (MerchantId поселения).
    public string MerchantId = string.Empty;
    public NarrativeConditionGroup Conditions = new NarrativeConditionGroup();
}

[Serializable]
public sealed class SettlementActionDefinition
{
    public string Id = string.Empty;
    public SettlementActionType Type;
    public string Label = string.Empty;
    public string Hint = string.Empty;
    public string TargetId = string.Empty;
    // Не выполнено — кнопки нет совсем (сюжетная функция не выдаёт тайну).
    public NarrativeConditionGroup ShowConditions = new NarrativeConditionGroup();
    // Не выполнено — кнопка видна, но недоступна; игрок читает причину.
    public NarrativeConditionGroup EnableConditions = new NarrativeConditionGroup();
    public string DisabledReason = string.Empty;
}

[Serializable]
public sealed class MerchantStockDefinition
{
    public string ItemId = string.Empty;
    // Абстрактные припасы отряда (ArmySupply), а не вещь в сумке.
    public bool IsSupplies;
    // Целевой запас: обновление доводит остаток до него, не суммирует.
    public int TargetCount = 1;
    // Не обновлять: предложение одно на всю партию.
    public bool Unique;
    // Цена единицы припасов (у вещи цена — из Базы предметов).
    public int SupplyUnitPrice = 2;
}

[Serializable]
public sealed class MerchantDefinition
{
    public string Id = string.Empty;
    public string DisplayName = string.Empty;
    public string SpeakerId = string.Empty;
    public string Role = string.Empty;
    // Цена покупки у торговца — базовая × процент (округление вверх).
    public int BuyPercent = 100;
    // Цена продажи торговцу — базовая × процент (округление вниз). Меньше
    // BuyPercent: немедленной прибыли на перепродаже нет.
    public int SellPercent = 50;
    public int StartMoney = 40;
    // Обновление пополняет кошелёк до этого уровня; лишнее не отнимает.
    public int MoneyTarget = 40;
    // Интервал обновления в игровых часах; 0 — не обновляется.
    public double RefreshHours = 72.0;
    public List<MerchantStockDefinition> Stock = new List<MerchantStockDefinition>();
}

// Платное или бесплатное предложение человека корчмы: сведения, просьба.
[Serializable]
public sealed class TavernOfferDefinition
{
    public string Id = string.Empty;
    public string Label = string.Empty;
    public string Description = string.Empty;
    // Кто предлагает (PersonId корчмы) — подпись и источник сведений.
    public string PersonId = string.Empty;
    public int Price;
    public NarrativeConditionGroup Conditions = new NarrativeConditionGroup();
    public bool Once = true;
    // Повтор не раньше, чем через столько часов (если не однократно).
    public double CooldownHours;
    public string KnowledgeId = string.Empty;
    public string KnowledgeText = string.Empty;
    public string StageFlagId = string.Empty;
    public string SetFlagId = string.Empty;
    // Принять поручение (ErrandDefinition) — общее состояние с доской и NPC.
    public string ErrandId = string.Empty;
    // Сцена рассказа после оплаты (необязательно).
    public string DialogueId = string.Empty;
}

[Serializable]
public sealed class TavernLodgingDefinition
{
    public bool Enabled;
    public int Price = 4;
    public double Hours = 8.0;
    // Сколько HP возвращает ночлег под крышей вошедшим (сверх обычного сна).
    public int HealHitPoints = 2;
    public string Description = string.Empty;
}

[Serializable]
public sealed class TavernDefinition
{
    public bool Enabled;
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public SettlementPersonDefinition Keeper = new SettlementPersonDefinition();
    public List<SettlementPersonDefinition> Visitors = new List<SettlementPersonDefinition>();
    public List<TavernOfferDefinition> Offers = new List<TavernOfferDefinition>();
    public TavernLodgingDefinition Lodging = new TavernLodgingDefinition();
}

// Доска объявлений: тип определяет подачу, носитель — как записано (письмо
// редкое: береста, табличка, знак, а то и грамотей читает вслух).
public enum NoticeType
{
    Request,
    Order,
    Warning,
    Wanted,
    TradeOffer,
    LocalNews
}

public enum NoticeMedium
{
    BirchBark,
    WoodenTablet,
    Sign,
    Spoken
}

[Serializable]
public sealed class NoticeDefinition
{
    public string Id = string.Empty;
    public string Title = string.Empty;
    public string Text = string.Empty;
    public NoticeType Type;
    public NoticeMedium Medium;
    public string AuthorName = string.Empty;
    // К кому обратиться: имя и (если он в этом поселении) PersonId.
    public string ContactName = string.Empty;
    public string ContactPersonId = string.Empty;
    // Подключённые действия — только существующие системы.
    public string ErrandId = string.Empty;
    public string DialogueId = string.Empty;
    public string KnowledgeId = string.Empty;
    public string KnowledgeText = string.Empty;
    public string LocationId = string.Empty;
    public string MerchantId = string.Empty;
    public NarrativeConditionGroup ShowConditions = new NarrativeConditionGroup();
    // Выполнено — объявление снято (например, переправу починили).
    public NarrativeConditionGroup HideConditions = new NarrativeConditionGroup();
    // Срок размещения в игровых часах от первого показа; 0 — бессрочно.
    public double LifetimeHours;
    // Через сколько часов после снятия вывешивают снова; 0 — однократно.
    public double RepostAfterHours;
    public string RewardText = string.Empty;
}

[Serializable]
public sealed class NoticeBoardDefinition
{
    public bool Enabled;
    public string Name = string.Empty;
    public string Description = string.Empty;
    // Кто читает неграмотным («Дьячок читает вслух за кружку квасу»).
    public string ReaderNote = string.Empty;
    public List<NoticeDefinition> Notices = new List<NoticeDefinition>();
}

// Этап поручения: текст «Дела», пока выставлен флаг (последний из
// выставленных побеждает).
[Serializable]
public sealed class ErrandStageDefinition
{
    public string Flag = string.Empty;
    public string Text = string.Empty;
}

// Поручение — одно задание с общим состоянием во флагах NarrativeState:
// принять его можно на доске, в корчме или в разговоре (эффект SetFlag того
// же AcceptedFlag); «Дело» в журнале строится отсюда. Это не новая система
// квестов: шаги и награды делают существующие диалоги.
[Serializable]
public sealed class ErrandDefinition
{
    public string Id = string.Empty;
    public string Title = string.Empty;
    public string Summary = string.Empty;
    public string GiverName = string.Empty;
    public string AcceptedFlag = string.Empty;
    public string CompletedFlag = string.Empty;
    public string FailedFlag = string.Empty;
    public string AcceptedStepText = string.Empty;
    public string CompletedText = string.Empty;
    public List<ErrandStageDefinition> Stages = new List<ErrandStageDefinition>();
    // Срок выполнения в игровых часах от принятия; 0 — без срока. Игрок
    // видит его до принятия.
    public double CompletionLimitHours;
    public string RewardText = string.Empty;
}

[Serializable]
public sealed class SettlementProfileDefinition
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public SettlementKind Kind;
    public string ShortDescription = string.Empty;
    public string LongDescription = string.Empty;
    public List<string> Tags = new List<string>();
    public List<SettlementVariantDefinition> Variants = new List<SettlementVariantDefinition>();
    public List<SettlementPersonDefinition> People = new List<SettlementPersonDefinition>();
    public List<SettlementActionDefinition> Actions = new List<SettlementActionDefinition>();
    public List<MerchantDefinition> Merchants = new List<MerchantDefinition>();
    public TavernDefinition Tavern = new TavernDefinition();
    public NoticeBoardDefinition Board = new NoticeBoardDefinition();

    // Привязка: явные места (LocationId авторской карты или истории) и
    // шаблон для деревень генератора свободной игры.
    public List<string> BoundLocationIds = new List<string>();
    public bool AllowGenerated;
    public int GenerationWeight = 1;
    // В каждой сгенерированной партии будет хотя бы одно такое поселение.
    public bool GuaranteeInGenerated;

    public MerchantDefinition FindMerchant(string merchantId)
    {
        return Merchants?.Find(merchant => merchant != null && merchant.Id == merchantId);
    }

    public SettlementPersonDefinition FindPerson(string personId)
    {
        if (string.IsNullOrEmpty(personId))
            return null;
        SettlementPersonDefinition person = People?.Find(p => p != null && p.Id == personId);
        if (person != null || Tavern == null)
            return person;
        if (Tavern.Keeper != null && Tavern.Keeper.Id == personId)
            return Tavern.Keeper;
        return Tavern.Visitors?.Find(p => p != null && p.Id == personId);
    }

    public TavernOfferDefinition FindOffer(string offerId)
    {
        return Tavern?.Offers?.Find(offer => offer != null && offer.Id == offerId);
    }

    public NoticeDefinition FindNotice(string noticeId)
    {
        return Board?.Notices?.Find(notice => notice != null && notice.Id == noticeId);
    }
}

// Набор профилей и поручений партии. В игре его назначает
// SettlementDatabaseRuntime из «Базы поселений»; без назначения — пусто
// (поселения показываются нейтральным экраном без услуг).
public sealed class SettlementCatalogData
{
    private readonly List<SettlementProfileDefinition> profiles = new List<SettlementProfileDefinition>();
    private readonly List<ErrandDefinition> errands = new List<ErrandDefinition>();

    public SettlementCatalogData(IEnumerable<SettlementProfileDefinition> profiles, IEnumerable<ErrandDefinition> errands = null)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        if (profiles != null)
        {
            foreach (SettlementProfileDefinition profile in profiles)
            {
                if (profile != null && !string.IsNullOrWhiteSpace(profile.Id) && ids.Add(profile.Id))
                    this.profiles.Add(profile);
            }
        }
        ids.Clear();
        if (errands != null)
        {
            foreach (ErrandDefinition errand in errands)
            {
                if (errand != null && !string.IsNullOrWhiteSpace(errand.Id) && ids.Add(errand.Id))
                    this.errands.Add(errand);
            }
        }
    }

    public IReadOnlyList<SettlementProfileDefinition> Profiles => profiles;
    public IReadOnlyList<ErrandDefinition> Errands => errands;

    // Бриф правок п. 14: игровые минуты за одно целое действие в поселении —
    // завершённый разговор или подтверждённую непустую сделку. Просмотр,
    // отмена и перенос товара времени не тратят. Рабочее значение — 15.
    public const int DefaultActionMinutes = 15;
    public int ActionMinutes { get; set; } = DefaultActionMinutes;

    public SettlementProfileDefinition FindProfile(string profileId)
    {
        return string.IsNullOrEmpty(profileId) ? null : profiles.Find(p => p.Id == profileId);
    }

    public ErrandDefinition FindErrand(string errandId)
    {
        return string.IsNullOrEmpty(errandId) ? null : errands.Find(e => e.Id == errandId);
    }

    // Профиль, явно привязанный к месту.
    public SettlementProfileDefinition ProfileBoundTo(string locationId)
    {
        if (string.IsNullOrEmpty(locationId))
            return null;
        foreach (SettlementProfileDefinition profile in profiles)
        {
            if (profile.BoundLocationIds != null && profile.BoundLocationIds.Contains(locationId))
                return profile;
        }
        return null;
    }
}

public static class SettlementCatalog
{
    private static SettlementCatalogData current;

    public static SettlementCatalogData Current
    {
        get => current ?? (current = new SettlementCatalogData(null));
        set => current = value;
    }

    public static void Reset()
    {
        current = null;
    }
}
