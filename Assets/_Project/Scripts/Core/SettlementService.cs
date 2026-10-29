using System;
using System.Collections.Generic;

// 12Т (SETTLEMENTS_TRADE_TAVERN_SPEC.md §5–§7): поселения конкретной партии.
// Профиль — авторское определение (SettlementCatalog); экземпляр — LocationId
// этой партии с выбранным профилем и своим состоянием. Один профиль на трёх
// деревнях — три независимых кошелька и запаса.
//
// Выбор профиля деревни генератора детерминирован (seed партии + ID места,
// свой стабильный хэш, не string.GetHashCode) и фиксируется в сохранении.
// Генератор карты базу не читает: рельеф, дороги и потоки случайных чисел
// прежние.
public static class SettlementService
{
    public static SettlementsStateData EnsureState(GameState state)
    {
        if (state.Settlements == null)
            state.Settlements = new SettlementsStateData();
        SettlementsStateData data = state.Settlements;
        data.Instances ??= new List<SettlementInstanceData>();
        data.Errands ??= new List<ErrandStateData>();
        data.IssuedUniqueItemIds ??= new List<string>();
        data.LodgingServedIds ??= new List<string>();
        data.LodgingLocationId ??= string.Empty;
        data.LodgingName ??= string.Empty;
        // Ночлег в корчме живёт только вместе с остановкой лагеря.
        if (!string.IsNullOrEmpty(data.LodgingLocationId) && !CampRest.IsResting(state))
        {
            data.LodgingLocationId = string.Empty;
            data.LodgingServedIds.Clear();
        }
        foreach (SettlementInstanceData instance in data.Instances)
        {
            if (instance == null)
                continue;
            instance.Merchants ??= new List<MerchantStateData>();
            instance.Offers ??= new List<TavernOfferStateData>();
            instance.Notices ??= new List<NoticeStateData>();
            foreach (MerchantStateData merchant in instance.Merchants)
            {
                if (merchant == null)
                    continue;
                merchant.Stock ??= new List<MerchantStockStateData>();
                merchant.Buyback ??= new List<ItemInstanceData>();
                merchant.SoldOutUnique ??= new List<string>();
            }
        }
        return data;
    }

    // ------------------------------------------------------------------
    // Привязка мест к профилям
    // ------------------------------------------------------------------

    // Отмечает посещаемые места и назначает профили тем, у кого их ещё нет.
    // Идемпотентно: уже назначенный профиль не меняется (даже если в базе
    // появились новые шаблоны).
    public static void SyncLocations(GameState state)
    {
        if (state?.Locations == null)
            return;
        SettlementsStateData data = EnsureState(state);
        SettlementCatalogData catalog = SettlementCatalog.Current;

        List<LocationData> generated = new List<LocationData>();
        foreach (LocationData location in state.Locations)
        {
            if (location == null || location.IsWaypoint)
                continue;
            if (location.PlaceKind == GeneratedMapPlaces.VillageKind)
                location.IsSettlement = true;

            SettlementProfileDefinition bound = catalog.ProfileBoundTo(location.Id);
            if (bound != null)
            {
                location.IsSettlement = true;
                if (FindInstance(state, location.Id) == null)
                    data.Instances.Add(new SettlementInstanceData { LocationId = location.Id, ProfileId = bound.Id });
                continue;
            }
            if (location.PlaceKind == GeneratedMapPlaces.VillageKind && FindInstance(state, location.Id) == null)
                generated.Add(location);
        }

        if (generated.Count > 0)
            AssignGenerated(state, generated, catalog);
    }

    // Сначала по одному «обязательному» профилю — ближайшим к стоянке
    // деревням (их легко найти в каждой партии), затем — по весам.
    private static void AssignGenerated(GameState state, List<LocationData> villages, SettlementCatalogData catalog)
    {
        List<SettlementProfileDefinition> templates = new List<SettlementProfileDefinition>();
        foreach (SettlementProfileDefinition profile in catalog.Profiles)
        {
            if (profile.AllowGenerated && profile.GenerationWeight > 0)
                templates.Add(profile);
        }
        if (templates.Count == 0)
            return;

        float startX = WorldMapNavigation.CapitalXPercent;
        float startY = WorldMapNavigation.CapitalYPercent;
        villages.Sort((a, b) =>
        {
            double da = Sq(a.MapXPercent - startX) + Sq(a.MapYPercent - startY);
            double db = Sq(b.MapXPercent - startX) + Sq(b.MapYPercent - startY);
            int byDistance = da.CompareTo(db);
            return byDistance != 0 ? byDistance : string.CompareOrdinal(a.Id, b.Id);
        });

        HashSet<string> present = new HashSet<string>(StringComparer.Ordinal);
        foreach (SettlementInstanceData instance in state.Settlements.Instances)
            present.Add(instance.ProfileId);

        int next = 0;
        foreach (SettlementProfileDefinition profile in templates)
        {
            if (!profile.GuaranteeInGenerated || present.Contains(profile.Id) || next >= villages.Count)
                continue;
            Assign(state, villages[next++], profile);
            present.Add(profile.Id);
        }

        int totalWeight = 0;
        foreach (SettlementProfileDefinition profile in templates)
            totalWeight += profile.GenerationWeight;
        for (int i = next; i < villages.Count; i++)
        {
            uint roll = StableHash(state.WorldSeed + ":" + villages[i].Id) % (uint)totalWeight;
            int acc = 0;
            foreach (SettlementProfileDefinition profile in templates)
            {
                acc += profile.GenerationWeight;
                if (roll < acc)
                {
                    Assign(state, villages[i], profile);
                    break;
                }
            }
        }
    }

    private static void Assign(GameState state, LocationData location, SettlementProfileDefinition profile)
    {
        state.Settlements.Instances.Add(new SettlementInstanceData { LocationId = location.Id, ProfileId = profile.Id });
        // Название конкретного места приоритетнее; безымянной деревне
        // генератора — название профиля.
        if (string.IsNullOrWhiteSpace(location.Name) || location.Name == "Деревня")
            location.Name = string.IsNullOrWhiteSpace(profile.Name) ? location.Name : profile.Name;
        if (string.IsNullOrWhiteSpace(location.InteractionDescription) ||
            location.InteractionDescription.StartsWith("Несколько срубов за плетнём", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(profile.ShortDescription))
                location.InteractionDescription = profile.ShortDescription;
        }
    }

    private static double Sq(double v) => v * v;

    // FNV-1a: стабилен между запусками и платформами (в отличие от
    // string.GetHashCode).
    public static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text ?? string.Empty)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }

    // ------------------------------------------------------------------
    // Запросы
    // ------------------------------------------------------------------

    public static SettlementInstanceData FindInstance(GameState state, string locationId)
    {
        if (state?.Settlements?.Instances == null || string.IsNullOrEmpty(locationId))
            return null;
        return state.Settlements.Instances.Find(instance => instance != null && instance.LocationId == locationId);
    }

    // Профиль места; null — у места нет профиля (или он удалён из базы:
    // экран показывает нейтральное поселение без услуг, состояние сохраняется).
    public static SettlementProfileDefinition Profile(GameState state, string locationId)
    {
        SettlementInstanceData instance = FindInstance(state, locationId);
        return instance != null ? SettlementCatalog.Current.FindProfile(instance.ProfileId) : null;
    }

    // Контекст условий — тот же, что у диалогов: присутствующие спутники и
    // вещи рядом (оставленные в лагере не в счёт), размер группы здесь.
    public static NarrativeEvaluationContext Context(GameState state)
    {
        CommanderData commander = state.GetSelectedCommander();
        HeroProfileData hero = commander?.HeroProfile ?? new HeroProfileData();
        if (state.Narrative == null)
            state.Narrative = new NarrativeStateData();
        return new NarrativeEvaluationContext(hero, state.Narrative,
            state.HasActiveExpedition ? PartyPresence.PresentCompanionIds(state) : new List<string>(),
            ItemService.GetPresentItemIds(state), state.WorldSeed,
            state.HasActiveExpedition ? PartyPresence.PresentPartySize(state) : 1, state);
    }

    public static bool Check(GameState state, NarrativeConditionGroup group)
    {
        return group == null || group.Evaluate(Context(state));
    }

    // Можно ли сейчас действовать внутри этого поселения (торговля, корчма,
    // доска). Каждая команда проверяет это сама — UI в обход не пройдёт.
    public static bool CanActInside(GameState state, string locationId, out string reason)
    {
        reason = string.Empty;
        if (state == null || !state.HasActiveExpedition)
        {
            reason = "Отряд не в походе.";
            return false;
        }
        ExpeditionData expedition = state.ActiveExpedition;
        if (expedition.Phase != CommanderState.AtLocation || expedition.LocationId != locationId)
        {
            reason = "Сначала нужно дойти до поселения.";
            return false;
        }
        if (SettlementCampService.InsideLocationId(state) != locationId)
        {
            reason = "Сначала войдите в поселение.";
            return false;
        }
        if (state.HasPendingExpeditionDecision)
        {
            reason = "Сначала нужно принять решение.";
            return false;
        }
        if (expedition.HasTimedActivity)
        {
            reason = "Отряд занят: " + expedition.ActiveActivity.DisplayName + ".";
            return false;
        }
        if (LocalExplorationService.IsActive(state))
        {
            reason = "Сначала выйдите из места.";
            return false;
        }
        return true;
    }

    // Описание с учётом условных вариантов: наибольший приоритет, затем
    // порядок записи — без случайных переключений при перерисовке.
    public static SettlementVariantDefinition ActiveVariant(GameState state, SettlementProfileDefinition profile)
    {
        if (profile?.Variants == null)
            return null;
        SettlementVariantDefinition best = null;
        NarrativeEvaluationContext context = Context(state);
        foreach (SettlementVariantDefinition variant in profile.Variants)
        {
            if (variant == null || !(variant.Conditions?.Evaluate(context) ?? true))
                continue;
            if (best == null || variant.Priority > best.Priority)
                best = variant;
        }
        return best;
    }

    public static string Describe(GameState state, LocationData location, SettlementProfileDefinition profile)
    {
        SettlementVariantDefinition variant = ActiveVariant(state, profile);
        if (variant != null && !string.IsNullOrWhiteSpace(variant.Description))
            return variant.Description;
        if (profile != null && !string.IsNullOrWhiteSpace(profile.ShortDescription))
            return profile.ShortDescription;
        return location != null && !string.IsNullOrWhiteSpace(location.InteractionDescription)
            ? location.InteractionDescription
            : "Поселение. Люди заняты своими делами.";
    }

    public static List<SettlementPersonDefinition> VisiblePeople(GameState state, IEnumerable<SettlementPersonDefinition> people)
    {
        List<SettlementPersonDefinition> result = new List<SettlementPersonDefinition>();
        if (people == null)
            return result;
        NarrativeEvaluationContext context = Context(state);
        foreach (SettlementPersonDefinition person in people)
        {
            if (person != null && !string.IsNullOrEmpty(person.Id) && (person.Conditions?.Evaluate(context) ?? true))
                result.Add(person);
        }
        return result;
    }

    public static void MarkVisited(GameState state, string locationId)
    {
        SettlementInstanceData instance = FindInstance(state, locationId);
        if (instance != null)
            instance.Visited = true;
    }

    // ------------------------------------------------------------------
    // Торговцы: инициализация и обновление по игровым часам
    // ------------------------------------------------------------------

    public static MerchantStateData FindMerchantState(SettlementInstanceData instance, string merchantId)
    {
        return instance?.Merchants?.Find(m => m != null && m.MerchantId == merchantId);
    }

    // Состояние торговца: заводится один раз на экземпляр поселения, затем
    // пополняется только по игровым часам (сохранённый NextRefreshHour).
    // Открытие окна, загрузка и смена реальных суток ничего не добавляют;
    // пропуск многих циклов — одно пополнение до целевого уровня.
    public static MerchantStateData EnsureMerchant(GameState state, string locationId, MerchantDefinition definition)
    {
        SettlementInstanceData instance = FindInstance(state, locationId);
        if (instance == null || definition == null)
            return null;
        EnsureState(state);
        MerchantStateData merchant = FindMerchantState(instance, definition.Id);
        double now = ContinuousSimulationSystem.GetAbsoluteHours(state);
        if (merchant == null)
        {
            merchant = new MerchantStateData { MerchantId = definition.Id };
            instance.Merchants.Add(merchant);
        }

        if (!merchant.Initialized)
        {
            merchant.Initialized = true;
            merchant.Money = Math.Max(0, definition.StartMoney);
            merchant.Stock.Clear();
            foreach (MerchantStockDefinition line in definition.Stock)
            {
                if (line == null || (!line.IsSupplies && string.IsNullOrEmpty(line.ItemId)))
                    continue;
                merchant.Stock.Add(new MerchantStockStateData
                {
                    ItemId = line.IsSupplies ? string.Empty : line.ItemId,
                    IsSupplies = line.IsSupplies,
                    Count = UniqueAvailable(state, merchant, line) ? Math.Max(0, line.TargetCount) : 0
                });
            }
            merchant.NextRefreshHour = definition.RefreshHours > 0 ? now + definition.RefreshHours : double.MaxValue;
            merchant.Revision++;
            return merchant;
        }

        if (definition.RefreshHours > 0 && now >= merchant.NextRefreshHour)
        {
            foreach (MerchantStockDefinition line in definition.Stock)
            {
                if (line == null || line.Unique)
                    continue;
                MerchantStockStateData stock = FindStock(merchant, line);
                if (stock == null)
                {
                    stock = new MerchantStockStateData { ItemId = line.IsSupplies ? string.Empty : line.ItemId, IsSupplies = line.IsSupplies };
                    merchant.Stock.Add(stock);
                }
                if (stock.Count < line.TargetCount)
                    stock.Count = line.TargetCount;
            }
            if (merchant.Money < definition.MoneyTarget)
                merchant.Money = definition.MoneyTarget;
            // Выкупленное у игрока за это время ушло другим покупателям.
            merchant.Buyback.Clear();
            merchant.NextRefreshHour = now + definition.RefreshHours;
            merchant.Revision++;
        }
        return merchant;
    }

    public static MerchantStockStateData FindStock(MerchantStateData merchant, MerchantStockDefinition line)
    {
        if (merchant?.Stock == null || line == null)
            return null;
        return merchant.Stock.Find(s => s != null && s.IsSupplies == line.IsSupplies &&
                                        (line.IsSupplies || s.ItemId == line.ItemId));
    }

    // Уникальная строка: не ушла с этого прилавка и сама вещь (если она
    // уникальна в Базе предметов) ещё не выдана в мир.
    private static bool UniqueAvailable(GameState state, MerchantStateData merchant, MerchantStockDefinition line)
    {
        if (line.IsSupplies)
            return true;
        if (line.Unique && merchant.SoldOutUnique.Contains(line.ItemId))
            return false;
        ItemDefinition item = ItemCatalog.Find(line.ItemId);
        return item == null || !item.Unique || !IsUniqueIssued(state, line.ItemId);
    }

    public static bool IsUniqueIssued(GameState state, string itemId)
    {
        EnsureState(state);
        if (state.Settlements.IssuedUniqueItemIds.Contains(itemId))
            return true;
        // Уже есть у отряда или у какого-то торговца в выкупе.
        if (ItemService.FindFirst(state, itemId) != null)
            return true;
        foreach (SettlementInstanceData instance in state.Settlements.Instances)
        {
            foreach (MerchantStateData merchant in instance.Merchants)
            {
                if (merchant.Buyback.Exists(item => item.ItemId == itemId))
                    return true;
            }
        }
        return false;
    }
}
