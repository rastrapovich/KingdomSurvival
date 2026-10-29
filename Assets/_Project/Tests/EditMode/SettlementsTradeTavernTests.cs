using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// 12Т (ProjectDocs/SETTLEMENTS_TRADE_TAVERN_SPEC.md §16.1): доменные
// инварианты поселений — эффекты вещей из данных, присутствие, торговля «всё
// или ничего», обновление по игровым часам, уникальные предложения, корчма,
// ночлег, поручения и доска, детерминированный выбор профиля и сохранение.
public sealed class SettlementsTradeTavernTests
{
    private const string Post = SettlementTradeDevFixture.TradingPostProfileId;
    private const string Here = SettlementCampDevFixture.SettlementId;
    private const string MerchantId = "test.merchant";
    private const string InfoKnowledge = "test.knowledge.cache";
    private const string InfoFlag = "test.flag.cache_area";
    private const string ErrandId = "test.errand";
    private const string ErrandAccepted = "test.errand.accepted";
    private const string ErrandDone = "test.errand.done";
    private const string ErrandFailed = "test.errand.failed";

    private sealed class FixedStats : IUnitStatsProvider
    {
        public bool TryGetCombatStats(string unitTypeId, out UnitCombatStats stats)
        {
            stats = new UnitCombatStats { MaxHitPoints = 20, Attack = 2, Defense = 2, Damage = 3, Movement = 3, Initiative = 3, AttackRange = 1 };
            return !string.IsNullOrEmpty(unitTypeId);
        }
    }

    private IUnitStatsProvider previousProvider;

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
        previousProvider = GameState.UnitStatsProvider;
        GameState.UnitStatsProvider = new FixedStats();
        ItemCatalog.ResetToDefaults();
        SettlementCatalog.Current = TestCatalog();
    }

    [TearDown]
    public void TearDown()
    {
        GameState.UnitStatsProvider = previousProvider;
        ItemCatalog.ResetToDefaults();
        SettlementCatalog.Reset();
    }

    // ------------------------------------------------------------------
    // Данные для проверки
    // ------------------------------------------------------------------

    private static SettlementCatalogData TestCatalog()
    {
        SettlementProfileDefinition post = new SettlementProfileDefinition
        {
            Id = Post, Name = "Торговое село (тест)", Kind = SettlementKind.TradingPost,
            AllowGenerated = true, GenerationWeight = 2, GuaranteeInGenerated = true
        };
        MerchantDefinition merchant = new MerchantDefinition
        {
            Id = MerchantId, DisplayName = "Тестовый лавочник", BuyPercent = 100, SellPercent = 50,
            StartMoney = 40, MoneyTarget = 40, RefreshHours = 24
        };
        merchant.Stock.Add(new MerchantStockDefinition { IsSupplies = true, TargetCount = 10, SupplyUnitPrice = 2 });
        merchant.Stock.Add(new MerchantStockDefinition { ItemId = ItemCatalog.SalveJar, TargetCount = 2 });
        merchant.Stock.Add(new MerchantStockDefinition { ItemId = ItemCatalog.Knife, TargetCount = 1 });
        merchant.Stock.Add(new MerchantStockDefinition { ItemId = ItemCatalog.AurochsHorn, TargetCount = 1, Unique = true });
        post.Merchants.Add(merchant);

        post.Tavern = new TavernDefinition
        {
            Enabled = true, Id = "test.tavern", Name = "Тестовая корчма",
            Keeper = new SettlementPersonDefinition { Id = "keeper", DisplayName = "Хозяин" },
            Lodging = new TavernLodgingDefinition { Enabled = true, Price = 5, Hours = 8, HealHitPoints = 3 }
        };
        post.Tavern.Visitors.Add(new SettlementPersonDefinition { Id = "carter", DisplayName = "Возчик" });
        post.Tavern.Offers.Add(new TavernOfferDefinition
        {
            Id = "offer.info", Label = "Где тайник", PersonId = "carter", Price = 12,
            KnowledgeId = InfoKnowledge, StageFlagId = InfoFlag, KnowledgeText = "У расколотой ивы."
        });
        post.Tavern.Offers.Add(new TavernOfferDefinition { Id = "offer.errand", Label = "Взяться", PersonId = "carter", ErrandId = ErrandId });

        post.Board = new NoticeBoardDefinition { Enabled = true, Name = "Доска" };
        post.Board.Notices.Add(new NoticeDefinition
        {
            Id = "notice.request", Title = "Просьба", Text = "Помогите.", Type = NoticeType.Request,
            ErrandId = ErrandId, ContactPersonId = "carter", HideConditions = Flag(ErrandDone)
        });
        post.Board.Notices.Add(new NoticeDefinition
        {
            Id = "notice.warning", Title = "Брод", Text = "Вязко.", Type = NoticeType.Warning,
            KnowledgeId = "test.knowledge.ford", KnowledgeText = "Брод вязкий.", HideConditions = Flag("test.ford_fixed")
        });
        post.Board.Notices.Add(new NoticeDefinition
        {
            Id = "notice.news", Title = "Брод починили", Text = "Торг пошёл.", Type = NoticeType.LocalNews,
            ShowConditions = Flag("test.ford_fixed")
        });
        post.Board.Notices.Add(new NoticeDefinition
        {
            Id = "notice.timed", Title = "Ярмарка", Text = "Скоро.", Type = NoticeType.LocalNews,
            LifetimeHours = 24, RepostAfterHours = 48
        });

        SettlementProfileDefinition village = new SettlementProfileDefinition
        {
            Id = "test.village", Name = "Деревня (тест)", Kind = SettlementKind.Village,
            AllowGenerated = true, GenerationWeight = 3, GuaranteeInGenerated = true
        };
        SettlementProfileDefinition town = new SettlementProfileDefinition
        {
            Id = "test.town", Name = "Городок (тест)", Kind = SettlementKind.Town,
            AllowGenerated = true, GenerationWeight = 1, GuaranteeInGenerated = true
        };

        ErrandDefinition errand = new ErrandDefinition
        {
            Id = ErrandId, Title = "Тестовая просьба", Summary = "Сделать дело.",
            AcceptedFlag = ErrandAccepted, CompletedFlag = ErrandDone, FailedFlag = ErrandFailed,
            CompletionLimitHours = 48
        };
        errand.Stages.Add(new ErrandStageDefinition { Flag = "test.errand.stage", Text = "Половина сделана." });
        return new SettlementCatalogData(new[] { post, village, town }, new[] { errand });
    }

    private static NarrativeConditionGroup Flag(string flag)
    {
        return new NarrativeConditionGroup
        {
            Conditions = new List<NarrativeCondition> { new NarrativeCondition { Type = NarrativeConditionType.FlagSet, StringParam = flag } }
        };
    }

    private static GameState NewCampaign()
    {
        GameState state = new CampaignSetup { WorldSeed = 20261029 }.CreateCampaign();
        state.ArmySupply = 50;
        return state;
    }

    // Отряд (герой, Гаррик, Марта + наёмник и зверь фикстуры) вошёл в
    // техническое торговое село; зверя не пускают — он ждёт в лагере.
    private static GameState InsidePost()
    {
        GameState state = NewCampaign();
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string> { "garrick", "marta" }, out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        Assert.IsTrue(SettlementTradeDevFixture.Apply(state, Post, out string report), report);
        List<string> entering = SettlementCampService.Companions(state)
            .Where(id => string.IsNullOrEmpty(SettlementCampService.AdmissionBlockReason(state, id))).ToList();
        Assert.IsTrue(SettlementCampService.Enter(state, entering, out string reason), reason);
        return state;
    }

    private static TradeCart Cart(GameState state)
    {
        Assert.IsTrue(TradeService.TryOpen(state, Here, MerchantId, out _, out MerchantStateData merchant, out string reason), reason);
        return new TradeCart { LocationId = Here, MerchantId = MerchantId, Revision = merchant.Revision };
    }

    private static MerchantStateData Merchant(GameState state, string locationId = Here)
    {
        return SettlementService.FindMerchantState(SettlementService.FindInstance(state, locationId), MerchantId);
    }

    private static string Hero(GameState state) => state.GetSelectedCommander().Id;

    private static ItemInstanceData Give(GameState state, string itemId, string owner = null)
    {
        return ItemService.CreateInstance(state, itemId, owner ?? Hero(state));
    }

    // ------------------------------------------------------------------
    // Вещи и эффекты
    // ------------------------------------------------------------------

    [Test]
    public void Catalog_OldItemsMigrated_WithSameIdsAndBehavior()
    {
        string[] old =
        {
            ItemCatalog.Sword, ItemCatalog.SwordAndShield, ItemCatalog.Bow, ItemCatalog.Spear, ItemCatalog.Knife, ItemCatalog.Axe,
            ItemCatalog.PaddedCoat, ItemCatalog.MailShirt, ItemCatalog.Jacket, ItemCatalog.FisherCoat, ItemCatalog.HealerBag,
            ItemCatalog.StoreroomMail, ItemCatalog.OldHuntingBow, ItemCatalog.RopeWithHooks, ItemCatalog.DriedFish,
            ItemCatalog.UlyanaHerbs, ItemCatalog.SevenToothGauge
        };
        foreach (string id in old)
            Assert.IsNotNull(ItemCatalog.Find(id), id);
        ItemDefinition fish = ItemCatalog.Find(ItemCatalog.DriedFish);
        Assert.AreEqual(3, fish.Uses);
        Assert.AreEqual(ItemUseEffectType.AddSupplies, fish.UseEffects.Single().Type);
        Assert.AreEqual(2, fish.UseEffects.Single().Amount);
        ItemDefinition herbs = ItemCatalog.Find(ItemCatalog.UlyanaHerbs);
        Assert.AreEqual(2, herbs.Uses);
        Assert.AreEqual(ItemUseEffectType.RemoveExhaustion, herbs.UseEffects.Single().Type);
        Assert.IsTrue(ItemCatalog.Find(ItemCatalog.SevenToothGauge).IsTradeProtected);
        foreach (ItemDefinition item in ItemCatalog.All)
            CollectionAssert.IsEmpty(ItemCatalogValidator.Validate(item, ItemCatalog.All), item.Id);
    }

    [Test]
    public void NewConsumable_FromData_HealsWithoutCodeAndKeepsChargeWhenUseless()
    {
        List<ItemDefinition> items = ItemCatalog.DefaultDefinitions();
        items.Add(new ItemDefinition
        {
            Id = "test.tonic", Name = "Настой", Kind = ItemKind.Consumable, Uses = 2, Price = 3,
            UseEffects = new List<ItemUseEffect> { new ItemUseEffect { Type = ItemUseEffectType.RestoreHitPoints, Amount = 3 } }
        });
        ItemCatalog.Current = new ItemCatalogData(items);
        GameState state = InsidePost();
        ItemInstanceData tonic = Give(state, "test.tonic");
        ResidentState garrick = HomePeopleService.Find(state, "garrick");

        Assert.IsFalse(ItemService.TryUse(state, tonic.InstanceId, "garrick", out string message), "Гаррик цел — заряд не тратится.");
        StringAssert.Contains("цел", message);
        Assert.AreEqual(2, tonic.UsesLeft);

        HomePeopleService.SetHitPoints(garrick, garrick.MaxHitPoints - 2);
        Assert.IsTrue(ItemService.TryUse(state, tonic.InstanceId, "garrick", out message), message);
        Assert.AreEqual(garrick.MaxHitPoints, garrick.CurrentHitPoints, "Не выше максимума.");
        Assert.AreEqual(1, tonic.UsesLeft);
    }

    [Test]
    public void MixedConsumable_UsefulPartApplies_UselessPartDoesNotBlock()
    {
        List<ItemDefinition> items = ItemCatalog.DefaultDefinitions();
        items.Add(new ItemDefinition
        {
            Id = "test.ration", Name = "Паёк", Kind = ItemKind.Consumable, Uses = 1, Price = 3,
            UseEffects = new List<ItemUseEffect>
            {
                new ItemUseEffect { Type = ItemUseEffectType.RestoreHitPoints, Amount = 2 },
                new ItemUseEffect { Type = ItemUseEffectType.AddSupplies, Amount = 3 }
            }
        });
        ItemCatalog.Current = new ItemCatalogData(items);

        GameState home = NewCampaign();
        ItemInstanceData atHome = Give(home, "test.ration");
        int supplyHome = home.ArmySupply;
        Assert.IsFalse(ItemService.TryUse(home, atHome.InstanceId, null, out string reason), "Дома и цел — нечем помочь.");
        Assert.AreEqual(1, atHome.UsesLeft);
        Assert.AreEqual(supplyHome, home.ArmySupply, "Половины эффекта нет.");

        GameState state = InsidePost();
        ItemInstanceData ration = Give(state, "test.ration");
        int supply = state.ArmySupply;
        ItemUsePreview preview = ItemUseService.Preview(state, ration, null);
        Assert.IsTrue(preview.CanUse);
        Assert.AreEqual(1, preview.Lines.Count, "Показано только то, что сработает.");
        Assert.IsTrue(ItemService.TryUse(state, ration.InstanceId, null, out reason), reason);
        Assert.AreEqual(supply + 3, state.ArmySupply);
        Assert.IsNull(ItemService.Find(state, ration.InstanceId));
    }

    [Test]
    public void KnowledgeItem_GrantsOnce_WithStageFlagAndOneChronicleEntry()
    {
        GameState state = InsidePost();
        ItemInstanceData bark = Give(state, ItemCatalog.BirchBarkScheme);
        ItemInstanceData second = Give(state, ItemCatalog.BirchBarkScheme);
        Assert.IsTrue(ItemService.TryUse(state, bark.InstanceId, null, out string message), message);
        Assert.IsTrue(state.Narrative.HasKnowledge(ItemCatalog.OldBedCacheKnowledge));
        Assert.IsTrue(state.Narrative.HasFlag(ItemCatalog.OldBedCacheStageFlag));
        Assert.IsNull(ItemService.Find(state, bark.InstanceId), "Береста изучена и израсходована.");
        int chronicle = Chronicle.Get(state).Entries.Count;

        Assert.IsFalse(ItemService.TryUse(state, second.InstanceId, null, out message), "Уже известно — второй экземпляр не тратится.");
        Assert.AreEqual(1, second.UsesLeft);
        Assert.AreEqual(chronicle, Chronicle.Get(state).Entries.Count);
    }

    [Test]
    public void EquipAndUnequip_DoNotHealInfinitely()
    {
        GameState state = InsidePost();
        ResidentState garrick = HomePeopleService.Find(state, "garrick");
        ItemInstanceData vest = Give(state, ItemCatalog.ScaleVest);
        HomePeopleService.SetHitPoints(garrick, garrick.MaxHitPoints - 4);
        int hurt = garrick.CurrentHitPoints;
        for (int i = 0; i < 3; i++)
        {
            Assert.IsTrue(ItemService.TryEquip(state, vest.InstanceId, "garrick", out string message), message);
            Assert.IsTrue(ItemService.TryUnequip(state, vest.InstanceId, out message), message);
        }
        Assert.LessOrEqual(garrick.CurrentHitPoints, hurt, "Надевание и снятие не лечат.");
        Assert.AreEqual(CombatStatsAssembler.Compute(state, "garrick").Final.MaxHitPoints, garrick.MaxHitPoints, "Экран и бой — одно число.");
    }

    [Test]
    public void Presence_WaitingInCamp_GivesNoItemsAndCannotBeEquipped()
    {
        GameState state = InsidePost();
        string creature = SettlementCampDevFixture.CreatureId;
        Assert.IsTrue(PartyPresence.IsWaitingInCamp(state, creature));
        ItemInstanceData rope = Give(state, ItemCatalog.RopeWithHooks, creature);
        Assert.IsFalse(ItemService.IsReachable(state, rope));
        CollectionAssert.DoesNotContain(ItemService.GetPresentItemIds(state), ItemCatalog.RopeWithHooks,
            "Вещь ожидающего не участвует в сцене внутри.");
        ItemInstanceData knife = Give(state, ItemCatalog.Knife);
        Assert.IsFalse(ItemService.TryEquip(state, knife.InstanceId, creature, out _));
        CollectionAssert.IsEmpty(TradeService.Sellables(state, SettlementService.Profile(state, Here).FindMerchant(MerchantId))
            .Where(s => s.Instance.InstanceId == rope.InstanceId));

        SettlementCampService.ReturnToCamp(state);
        Assert.IsTrue(ItemService.IsReachable(state, rope), "У лагеря все снова рядом.");
    }

    // ------------------------------------------------------------------
    // Торговля
    // ------------------------------------------------------------------

    [Test]
    public void Trade_MixedDeal_ChangesExactlyByQuote()
    {
        GameState state = InsidePost();
        state.ArmyGold = 20;
        ItemInstanceData wax = Give(state, ItemCatalog.Wax);
        TradeCart cart = Cart(state);
        cart.SetQuantity(TradeLineKind.Stock, ItemCatalog.SalveJar, 1);
        cart.SetQuantity(TradeLineKind.Supplies, string.Empty, 3);
        cart.ToggleSell(wax.InstanceId);
        TradeQuote quote = TradeService.Quote(state, cart);
        Assert.IsTrue(quote.Valid, quote.Reason);
        Assert.AreEqual(8 + 6, quote.BuyTotal);
        Assert.AreEqual(3, quote.SellTotal);
        int supply = state.ArmySupply;
        int merchantMoney = Merchant(state).Money;

        Assert.IsTrue(TradeService.TryConfirm(state, cart, quote.Net, out string message), message);
        Assert.AreEqual(20 - quote.Net, state.ArmyGold);
        Assert.AreEqual(merchantMoney + quote.Net, Merchant(state).Money);
        Assert.AreEqual(supply + 3, state.ArmySupply, "Припасы — сразу в запас похода, не вещи в сумке.");
        Assert.IsNotNull(ItemService.OwnedBy(state, Hero(state)).FirstOrDefault(i => i.ItemId == ItemCatalog.SalveJar));
        Assert.IsNull(ItemService.Find(state, wax.InstanceId));
        Assert.AreEqual(wax.InstanceId, Merchant(state).Buyback.Single().InstanceId, "Проданный экземпляр — в выкупе тем же.");
        Assert.IsTrue(cart.IsEmpty);
    }

    [Test]
    public void Trade_NotEnoughMoney_EitherSide_ChangesNothing()
    {
        GameState state = InsidePost();
        state.ArmyGold = 3;
        TradeCart cart = Cart(state);
        cart.SetQuantity(TradeLineKind.Stock, ItemCatalog.SalveJar, 1);
        Assert.IsFalse(TradeService.TryConfirm(state, cart, 8, out string message));
        StringAssert.Contains("Не хватает", message);
        Assert.AreEqual(3, state.ArmyGold);
        Assert.AreEqual(2, Merchant(state).Stock.First(s => s.ItemId == ItemCatalog.SalveJar).Count);

        Merchant(state).Money = 0;
        ItemInstanceData mail = Give(state, ItemCatalog.MailShirt);
        TradeCart sell = Cart(state);
        sell.ToggleSell(mail.InstanceId);
        Assert.IsFalse(TradeService.TryConfirm(state, sell, -15, out message));
        StringAssert.Contains("торговца", message);
        Assert.IsNotNull(ItemService.Find(state, mail.InstanceId));
        Assert.AreEqual(3, state.ArmyGold);
    }

    [Test]
    public void Trade_SellingFreesSlot_ForPurchaseInSameDeal()
    {
        GameState state = InsidePost();
        state.ArmyGold = 50;
        while (ItemService.HeroPackCount(state) < ItemService.HeroPackSize)
            Give(state, ItemCatalog.Wax);
        TradeCart buyOnly = Cart(state);
        buyOnly.SetQuantity(TradeLineKind.Stock, ItemCatalog.Knife, 1);
        TradeQuote full = TradeService.Quote(state, buyOnly);
        Assert.IsFalse(full.Valid, "Полная сумка — без продажи не вместить.");
        Assert.IsFalse(TradeService.TryConfirm(state, buyOnly, full.Net, out _));
        Assert.AreEqual(50, state.ArmyGold);

        TradeCart swap = Cart(state);
        swap.SetQuantity(TradeLineKind.Stock, ItemCatalog.Knife, 1);
        swap.ToggleSell(ItemService.OwnedBy(state, Hero(state)).First(i => i.ItemId == ItemCatalog.Wax).InstanceId);
        TradeQuote quote = TradeService.Quote(state, swap);
        Assert.IsTrue(quote.Valid, quote.Reason);
        Assert.IsTrue(TradeService.TryConfirm(state, swap, quote.Net, out string message), message);
        Assert.AreEqual(ItemService.HeroPackSize, ItemService.HeroPackCount(state));
    }

    [Test]
    public void Trade_InvalidCarts_AreRejected()
    {
        GameState state = InsidePost();
        state.ArmyGold = 100;
        ItemInstanceData wax = Give(state, ItemCatalog.Wax);

        TradeCart negative = Cart(state);
        negative.Buy.Add(new TradeBuyLine { Kind = TradeLineKind.Stock, Key = ItemCatalog.Knife, Quantity = -2 });
        Assert.IsFalse(TradeService.Quote(state, negative).Valid);

        TradeCart overflow = Cart(state);
        overflow.Buy.Add(new TradeBuyLine { Kind = TradeLineKind.Supplies, Key = string.Empty, Quantity = int.MaxValue });
        Assert.IsFalse(TradeService.Quote(state, overflow).Valid);

        TradeCart duplicate = Cart(state);
        duplicate.SellInstanceIds.Add(wax.InstanceId);
        duplicate.SellInstanceIds.Add(wax.InstanceId);
        Assert.IsFalse(TradeService.Quote(state, duplicate).Valid);

        TradeCart forged = Cart(state);
        forged.SellInstanceIds.Add("item.9999");
        Assert.IsFalse(TradeService.Quote(state, forged).Valid);

        TradeCart stale = Cart(state);
        stale.Revision--;
        stale.SetQuantity(TradeLineKind.Stock, ItemCatalog.Knife, 1);
        Assert.IsFalse(TradeService.TryConfirm(state, stale, 4, out _), "Устаревшая ревизия не проходит.");
        Assert.AreEqual(100, state.ArmyGold);
    }

    [Test]
    public void Trade_StoryEquippedAndProtected_CannotBeSoldEvenByCommand()
    {
        GameState state = InsidePost();
        Give(state, ItemCatalog.SevenToothGauge);
        ItemInstanceData gauge = ItemService.FindFirst(state, ItemCatalog.SevenToothGauge);
        ItemInstanceData equipped = ItemService.Equipped(state, Hero(state), ItemSlot.Weapon);
        foreach (ItemInstanceData item in new[] { gauge, equipped })
        {
            TradeCart cart = Cart(state);
            cart.ToggleSell(item.InstanceId);
            Assert.IsFalse(TradeService.TryConfirm(state, cart, -1, out string message), item.ItemId);
            Assert.IsNotNull(ItemService.Find(state, item.InstanceId));
        }
    }

    [Test]
    public void Trade_RepeatedConfirm_AppliesOnce()
    {
        GameState state = InsidePost();
        state.ArmyGold = 30;
        TradeCart cart = Cart(state);
        cart.SetQuantity(TradeLineKind.Stock, ItemCatalog.Knife, 1);
        TradeQuote quote = TradeService.Quote(state, cart);
        TradeCart copy = new TradeCart { LocationId = cart.LocationId, MerchantId = cart.MerchantId, Revision = cart.Revision };
        copy.SetQuantity(TradeLineKind.Stock, ItemCatalog.Knife, 1);
        Assert.IsTrue(TradeService.TryConfirm(state, cart, quote.Net, out string message), message);
        Assert.IsFalse(TradeService.TryConfirm(state, cart, quote.Net, out _), "Та же корзина пуста.");
        Assert.IsFalse(TradeService.TryConfirm(state, copy, quote.Net, out _), "Второй клик со старой ревизией не проходит.");
        Assert.AreEqual(30 - quote.Net, state.ArmyGold);
        Assert.AreEqual(1, ItemService.OwnedBy(state, Hero(state)).Count(i => i.ItemId == ItemCatalog.Knife && i.Slot == ItemSlot.None));
    }

    [Test]
    public void Buyback_ReturnsSameInstance_WithChargesAndProportionalPrice()
    {
        GameState state = InsidePost();
        state.ArmyGold = 30;
        ItemInstanceData salve = Give(state, ItemCatalog.SalveJar);
        salve.UsesLeft = 1;
        MerchantDefinition definition = SettlementService.Profile(state, Here).FindMerchant(MerchantId);
        TradeCart sell = Cart(state);
        sell.ToggleSell(salve.InstanceId);
        TradeQuote sold = TradeService.Quote(state, sell);
        Assert.AreEqual(2, sold.SellTotal, "Половина зарядов — половина цены продажи.");
        Assert.IsTrue(TradeService.TryConfirm(state, sell, sold.Net, out string message), message);

        TradeCart back = Cart(state);
        back.SetQuantity(TradeLineKind.Buyback, salve.InstanceId, 1);
        TradeQuote bought = TradeService.Quote(state, back);
        Assert.AreEqual(TradeService.BuyPrice(ItemCatalog.Find(ItemCatalog.SalveJar), definition, 1), bought.BuyTotal);
        Assert.Greater(bought.BuyTotal, sold.SellTotal, "Перепродажа без прибыли.");
        Assert.IsTrue(TradeService.TryConfirm(state, back, bought.Net, out message), message);
        ItemInstanceData returned = ItemService.Find(state, salve.InstanceId);
        Assert.IsNotNull(returned);
        Assert.AreEqual(1, returned.UsesLeft);
    }

    [Test]
    public void Refresh_OnlyByGameHours_ReopenAndLoadDoNotRestock()
    {
        GameState state = InsidePost();
        state.ArmyGold = 40;
        TradeCart cart = Cart(state);
        cart.SetQuantity(TradeLineKind.Stock, ItemCatalog.Knife, 1);
        Assert.IsTrue(TradeService.TryConfirm(state, cart, TradeService.Quote(state, cart).Net, out string message), message);
        ItemInstanceData wax = Give(state, ItemCatalog.Wax);
        TradeCart sell = Cart(state);
        sell.ToggleSell(wax.InstanceId);
        Assert.IsTrue(TradeService.TryConfirm(state, sell, TradeService.Quote(state, sell).Net, out message), message);
        Merchant(state).Money = 400; // честно заработанное не обрезается

        for (int i = 0; i < 3; i++)
            Cart(state);
        Assert.AreEqual(0, Merchant(state).Stock.First(s => s.ItemId == ItemCatalog.Knife).Count, "Открытие окна ничего не пополняет.");

        GameState loaded = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(
            JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        Assert.AreEqual(0, Merchant(loaded).Stock.First(s => s.ItemId == ItemCatalog.Knife).Count, "Загрузка не пополняет.");

        state.Day += 30; // много пропущенных циклов — одно пополнение
        Cart(state);
        MerchantStateData merchant = Merchant(state);
        Assert.AreEqual(1, merchant.Stock.First(s => s.ItemId == ItemCatalog.Knife).Count, "До целевого, не сверх.");
        Assert.AreEqual(400, merchant.Money);
        CollectionAssert.IsEmpty(merchant.Buyback, "Выкуп ушёл при обновлении.");
        int revision = merchant.Revision;
        Cart(state);
        Assert.AreEqual(revision, Merchant(state).Revision, "Та же отметка времени — без второго пополнения.");
    }

    [Test]
    public void UniqueOffer_NotRestocked_AndNotOfferedElsewhere()
    {
        GameState state = InsidePost();
        state.ArmyGold = 100;
        TradeCart cart = Cart(state);
        cart.SetQuantity(TradeLineKind.Stock, ItemCatalog.AurochsHorn, 1);
        Assert.IsTrue(TradeService.TryConfirm(state, cart, TradeService.Quote(state, cart).Net, out string message), message);
        state.Day += 10;
        Cart(state);
        Assert.AreEqual(0, Merchant(state).Stock.First(s => s.ItemId == ItemCatalog.AurochsHorn).Count);

        // Второе поселение того же профиля: рог уже в мире — не предлагается.
        string second = AddSecondSettlement(state);
        Assert.IsTrue(TradeService.TryOpen(state, second, MerchantId, out MerchantDefinition definition, out MerchantStateData merchant, out string reason), reason);
        TradeOfferView horn = TradeService.Offers(state, definition, merchant).FirstOrDefault(o => o.Key == ItemCatalog.AurochsHorn);
        Assert.IsTrue(horn == null || horn.Available == 0);
    }

    // Второе техническое поселение того же профиля у отряда; отряд входит в него.
    private static string AddSecondSettlement(GameState state)
    {
        SettlementCampService.Gather(state);
        LocationData second = new LocationData("dev.settlement2", "Второе село", 0.0, "низкая")
        {
            MapXPercent = state.ActiveExpedition.CurrentMapXPercent,
            MapYPercent = state.ActiveExpedition.CurrentMapYPercent,
            IsSettlement = true
        };
        state.Locations.Add(second);
        state.Settlements.Instances.Add(new SettlementInstanceData { LocationId = second.Id, ProfileId = Post });
        state.ActiveExpedition.LocationId = second.Id;
        List<string> entering = SettlementCampService.Companions(state)
            .Where(id => string.IsNullOrEmpty(SettlementCampService.AdmissionBlockReason(state, id))).ToList();
        Assert.IsTrue(SettlementCampService.Enter(state, entering, out string reason), reason);
        return second.Id;
    }

    [Test]
    public void TwoSettlementsOfOneProfile_HaveIndependentWalletsAndStock()
    {
        GameState state = InsidePost();
        state.ArmyGold = 30;
        TradeCart cart = Cart(state);
        cart.SetQuantity(TradeLineKind.Stock, ItemCatalog.SalveJar, 2);
        Assert.IsTrue(TradeService.TryConfirm(state, cart, TradeService.Quote(state, cart).Net, out string message), message);
        string second = AddSecondSettlement(state);
        Assert.IsTrue(TradeService.TryOpen(state, second, MerchantId, out _, out MerchantStateData other, out string reason), reason);
        Assert.AreEqual(40, other.Money);
        Assert.AreEqual(2, other.Stock.First(s => s.ItemId == ItemCatalog.SalveJar).Count);
        Assert.AreEqual(0, Merchant(state, Here).Stock.First(s => s.ItemId == ItemCatalog.SalveJar).Count);
    }

    [Test]
    public void Trade_OutsideSettlement_IsRefused()
    {
        GameState state = InsidePost();
        SettlementCampService.ReturnToCamp(state);
        Assert.IsFalse(TradeService.TryOpen(state, Here, MerchantId, out _, out _, out string reason));
        StringAssert.Contains("войдите", reason);
    }

    // ------------------------------------------------------------------
    // Корчма, ночлег, поручения, доска
    // ------------------------------------------------------------------

    [Test]
    public void InfoPurchase_PaymentAndKnowledgeAtomic_NotSoldTwice()
    {
        GameState state = InsidePost();
        state.ArmyGold = 5;
        Assert.IsFalse(TavernService.TryTakeOffer(state, Here, "offer.info", out string message, out _));
        Assert.IsFalse(state.Narrative.HasKnowledge(InfoKnowledge), "Без денег — без знания.");
        Assert.AreEqual(5, state.ArmyGold);

        state.ArmyGold = 20;
        Assert.IsTrue(TavernService.TryTakeOffer(state, Here, "offer.info", out message, out _), message);
        Assert.AreEqual(8, state.ArmyGold);
        Assert.IsTrue(state.Narrative.HasKnowledge(InfoKnowledge));
        Assert.IsTrue(state.Narrative.HasFlag(InfoFlag));
        Assert.IsNotNull(Chronicle.Find(state, "knowledge.source." + InfoKnowledge), "Записан источник сведений.");

        Assert.IsFalse(TavernService.TryTakeOffer(state, Here, "offer.info", out message, out _));
        Assert.AreEqual(8, state.ArmyGold, "Повтор не списывает деньги.");
        Assert.IsFalse(TavernService.Offers(state, Here).Any(o => o.Offer.Id == "offer.info"), "Однократное взятое скрыто.");
    }

    [Test]
    public void Errand_SharedBetweenBoardTavernAndDialogue()
    {
        GameState state = InsidePost();
        NoticeView notice = NoticeBoardService.Notices(state, Here).Single(n => n.Notice.Id == "notice.request");
        Assert.IsFalse(state.Narrative.HasFlag(ErrandAccepted), "Чтение ничего не принимает.");
        NoticeBoardService.MarkRead(state, Here, notice.Notice.Id);
        Assert.IsFalse(state.Narrative.HasFlag(ErrandAccepted));
        Assert.IsTrue(notice.Actions.Any(a => a.Kind == NoticeActionKind.AcceptErrand));

        Assert.IsTrue(NoticeBoardService.TryAccept(state, Here, "notice.request", out string message), message);
        Assert.IsTrue(state.Narrative.HasFlag(ErrandAccepted));
        // Бриф правок п. 22: принятое поручение в корчме больше не предлагается
        // (строка исчезает, а не сереет).
        Assert.IsFalse(TavernService.Offers(state, Here).Any(o => o.Offer.Id == "offer.errand"), "В корчме то же поручение уже принято.");
        Assert.IsFalse(NoticeBoardService.Notices(state, Here).Single(n => n.Notice.Id == "notice.request").Actions
            .Any(a => a.Kind == NoticeActionKind.AcceptErrand));
        Assert.AreEqual(1, ErrandService.BuildGoals(state).Count(g => g.Id == "errand." + ErrandId));

        state.Narrative.SetFlag("test.errand.stage");
        StringAssert.Contains("Половина", ErrandService.BuildGoals(state).Single().CurrentStep);
        state.Narrative.SetFlag(ErrandDone); // выполнено в диалоге
        ErrandService.Refresh(state);
        ErrandService.Refresh(state);
        Assert.AreEqual(JournalGoalState.Completed, ErrandService.BuildGoals(state).Single().State);
        Assert.AreEqual(1, Chronicle.Get(state).Entries.Count(e => e.Id == "errand." + ErrandId + ".done"), "Одна запись итога.");
        Assert.IsFalse(NoticeBoardService.Notices(state, Here).Any(n => n.Notice.Id == "notice.request"), "Объявление снято после выполнения.");
    }

    [Test]
    public void Errand_AcceptedInDialogue_GetsDeadline_AndFailsWhenLate()
    {
        GameState state = InsidePost();
        state.Narrative.SetFlag(ErrandAccepted);
        ErrandService.Refresh(state);
        Assert.IsNotNull(ErrandService.Find(state, ErrandId), "Принятие из разговора отмечено.");
        StringAssert.Contains("Срок", ErrandService.DescribeLimit(SettlementCatalog.Current.FindErrand(ErrandId)));
        state.Day += 3;
        List<string> reports = ErrandService.Refresh(state);
        Assert.AreEqual(1, reports.Count);
        Assert.IsTrue(state.Narrative.HasFlag(ErrandFailed));
        Assert.AreEqual(JournalGoalState.Failed, ErrandService.BuildGoals(state).Single().State);
        Assert.AreEqual(0, ErrandService.Refresh(state).Count, "Провал — один раз.");
    }

    [Test]
    public void Board_ConsequencesLifetimeAndReading()
    {
        GameState state = InsidePost();
        List<string> ids = NoticeBoardService.Notices(state, Here).Select(n => n.Notice.Id).ToList();
        CollectionAssert.Contains(ids, "notice.warning");
        CollectionAssert.DoesNotContain(ids, "notice.news");

        Assert.IsTrue(NoticeBoardService.TryRecordKnowledge(state, Here, "notice.warning", out string message), message);
        Assert.IsTrue(state.Narrative.HasKnowledge("test.knowledge.ford"));
        Assert.IsFalse(NoticeBoardService.TryRecordKnowledge(state, Here, "notice.warning", out _), "Повторно не выдаётся.");

        state.Narrative.SetFlag("test.ford_fixed");
        ids = NoticeBoardService.Notices(state, Here).Select(n => n.Notice.Id).ToList();
        CollectionAssert.DoesNotContain(ids, "notice.warning", "Предупреждение исчезло после события.");
        CollectionAssert.Contains(ids, "notice.news", "Появилась новость.");

        CollectionAssert.Contains(ids, "notice.timed");
        state.Day += 2;
        CollectionAssert.DoesNotContain(NoticeBoardService.Notices(state, Here).Select(n => n.Notice.Id), "notice.timed", "Срок вышел.");
        state.Day += 2;
        CollectionAssert.Contains(NoticeBoardService.Notices(state, Here).Select(n => n.Notice.Id), "notice.timed", "Вывесили снова.");
    }

    [Test]
    public void Lodging_PaysOnce_HealsOnlyThoseInside()
    {
        GameState state = InsidePost();
        state.ArmyGold = 10;
        ResidentState garrick = HomePeopleService.Find(state, "garrick");
        ResidentState creature = HomePeopleService.Find(state, SettlementCampDevFixture.CreatureId);
        HomePeopleService.SetHitPoints(garrick, garrick.MaxHitPoints - 5);
        HomePeopleService.SetHitPoints(creature, creature.MaxHitPoints - 5);
        int creatureHp = creature.CurrentHitPoints;

        Assert.IsTrue(TavernService.TryStartLodging(state, Here, out string message), message);
        Assert.AreEqual(5, state.ArmyGold);
        Assert.IsTrue(TavernService.IsLodging(state));
        Assert.IsFalse(TavernService.TryStartLodging(state, Here, out _), "Второй раз не платят — отряд уже спит.");
        Assert.AreEqual(5, state.ArmyGold);

        List<string> messages = new List<string>();
        state.ActiveExpedition.ActiveActivity = null;
        CampRest.CompleteRest(state, messages);
        Assert.AreEqual(garrick.MaxHitPoints - 2, garrick.CurrentHitPoints, "Вошедшему — лечение корчмы.");
        Assert.AreEqual(creatureHp, creature.CurrentHitPoints, "Ожидающему в лагере — нет.");
        Assert.IsTrue(string.IsNullOrEmpty(state.Settlements.LodgingLocationId));
    }

    // ------------------------------------------------------------------
    // Профили, сохранение, совместимость
    // ------------------------------------------------------------------

    [Test]
    public void GeneratedVillages_GetDeterministicProfiles_WithGuarantees()
    {
        GameState a = NewCampaign();
        GameState b = NewCampaign();
        foreach (GameState state in new[] { a, b })
        {
            foreach (LocationData location in state.Locations)
                location.IsSettlement = false;
            for (int i = 0; i < 5; i++)
            {
                state.Locations.Add(new LocationData("gen-village-" + (i + 1), "Деревня", 0, "низкая")
                {
                    PlaceKind = GeneratedMapPlaces.VillageKind, MapXPercent = 20 + i * 10, MapYPercent = 50
                });
            }
            state.Settlements = new SettlementsStateData();
            SettlementService.SyncLocations(state);
        }
        string Map(GameState s) => string.Join(",", s.Settlements.Instances.Select(i => i.LocationId + "=" + i.ProfileId));
        Assert.AreEqual(Map(a), Map(b), "Тот же seed — те же профили.");
        CollectionAssert.IsSubsetOf(new[] { Post, "test.village", "test.town" }, a.Settlements.Instances.Select(i => i.ProfileId).Distinct().ToList(),
            "Каждый обязательный профиль есть.");
        Assert.IsTrue(a.Locations.Where(l => l.PlaceKind == GeneratedMapPlaces.VillageKind).All(l => l.IsSettlement));

        string before = Map(a);
        SettlementCatalog.Current = new SettlementCatalogData(new[]
        {
            new SettlementProfileDefinition { Id = "test.new", Name = "Новый", AllowGenerated = true, GenerationWeight = 100 }
        });
        SettlementService.SyncLocations(a);
        Assert.AreEqual(before, Map(a), "Уже назначенные профили не меняются от правки базы.");
    }

    [Test]
    public void SaveLoad_KeepsSettlementStateAndPresence()
    {
        GameState state = InsidePost();
        state.ArmyGold = 40;
        ItemInstanceData wax = Give(state, ItemCatalog.Wax);
        TradeCart cart = Cart(state);
        cart.SetQuantity(TradeLineKind.Stock, ItemCatalog.Knife, 1);
        cart.ToggleSell(wax.InstanceId);
        Assert.IsTrue(TradeService.TryConfirm(state, cart, TradeService.Quote(state, cart).Net, out string message), message);
        Assert.IsTrue(TavernService.TryTakeOffer(state, Here, "offer.info", out message, out _), message);
        Assert.IsTrue(NoticeBoardService.TryAccept(state, Here, "notice.request", out message), message);
        NoticeBoardService.MarkRead(state, Here, "notice.warning");
        int money = state.ArmyGold;
        int merchantMoney = Merchant(state).Money;
        List<string> waiting = state.SettlementCamp.WaitingIds.ToList();

        CampaignSaveData data = JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state)));
        GameState loaded = CampaignSaveService.RestoreCampaign(data);
        Assert.AreEqual(money, loaded.ArmyGold);
        Assert.AreEqual(merchantMoney, Merchant(loaded).Money);
        Assert.AreEqual(0, Merchant(loaded).Stock.First(s => s.ItemId == ItemCatalog.Knife).Count);
        Assert.AreEqual(wax.InstanceId, Merchant(loaded).Buyback.Single().InstanceId);
        Assert.IsTrue(SettlementCampService.IsCommanderInside(loaded));
        CollectionAssert.AreEqual(waiting, loaded.SettlementCamp.WaitingIds);
        Assert.IsTrue(loaded.Narrative.HasKnowledge(InfoKnowledge));
        Assert.IsTrue(loaded.Narrative.HasFlag(ErrandAccepted));
        Assert.IsNotNull(ErrandService.Find(loaded, ErrandId));
        Assert.IsTrue(NoticeBoardService.Notices(loaded, Here).Single(n => n.Notice.Id == "notice.warning").Read);
        Assert.IsFalse(TavernService.TryTakeOffer(loaded, Here, "offer.info", out _, out _), "Купленное не продаётся снова после загрузки.");
    }

    [Test]
    public void SaveLoad_InsideWithoutCamp_IsRestored()
    {
        GameState state = NewCampaign();
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, new List<string> { "garrick" }, out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
        Assert.IsTrue(SettlementTradeDevFixture.Apply(state, Post, out message), message);
        // Без существа-союзника фикстуры: все входят вместе, лагеря нет.
        state.ActiveExpedition.FighterIds.Remove(SettlementCampDevFixture.CreatureId);
        Assert.IsTrue(SettlementCampService.Enter(state, SettlementCampService.Companions(state), out message), message);
        Assert.IsFalse(SettlementCampService.HasCamp(state), "Лагеря нет.");
        Assert.IsTrue(SettlementCampService.IsCommanderInside(state), "Но пребывание внутри записано.");

        GameState loaded = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(
            JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state))));
        Assert.IsTrue(SettlementCampService.IsCommanderInside(loaded));
        Assert.IsFalse(SettlementCampService.HasCamp(loaded));
        SettlementCampService.ReturnToCamp(loaded);
        Assert.IsNull(loaded.SettlementCamp, "Вышли — записи нет, лагеря не было.");
    }

    [Test]
    public void OldSave_WithoutSettlements_InitializesOnceWithoutTouchingItems()
    {
        GameState state = NewCampaign();
        int items = state.Inventory.Items.Count;
        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state));
        int start = json.IndexOf("\"Settlements\":", System.StringComparison.Ordinal);
        Assert.Greater(start, 0);
        // Вырезаем блок Settlements целиком — как в сохранении до 12Т.
        int depth = 0;
        int end = start + "\"Settlements\":".Length;
        for (; end < json.Length; end++)
        {
            if (json[end] == '{') depth++;
            if (json[end] == '}' && --depth == 0) { end++; break; }
        }
        if (json[end] == ',')
            end++;
        string old = json.Remove(start, end - start);
        GameState loaded = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(old));
        Assert.IsNotNull(loaded.Settlements);
        Assert.IsNotNull(loaded.Settlements.Instances);
        Assert.AreEqual(items, loaded.Inventory.Items.Count, "Вещи не тронуты, стартовый набор не выдан второй раз.");
    }
}
