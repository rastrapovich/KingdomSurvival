using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// ПР-08 (ProjectDocs/PR08_HERO_ITEMS_SPEC.md §10): вещи с владельцем,
// единая сборка боевых чисел, изнеможение, припасы остаются у отряда.
public sealed class ItemsPr08Tests
{
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
        previousProvider = GameState.UnitStatsProvider;
        GameState.UnitStatsProvider = new FixedStats();
    }

    [TearDown]
    public void TearDown()
    {
        GameState.UnitStatsProvider = previousProvider;
    }

    private static GameState NewGame()
    {
        GameState state = new CampaignSetup { WorldSeed = 20260925 }.CreateCampaign();
        state.ArmySupply = 100;
        return state;
    }

    private static string HeroId(GameState state) => state.GetSelectedCommander().Id;

    private static ItemInstanceData Equipped(GameState state, string personId, ItemSlot slot)
    {
        return ItemService.Equipped(state, personId, slot);
    }

    private static string ItemIdOf(ItemInstanceData item) => item != null ? item.ItemId : null;

    private static void Depart(GameState state, IEnumerable<string> fighters)
    {
        LocationData target = state.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(state.TryStartExpedition(target.Id, fighters.ToList(), out string message), message);
        state.ActiveExpedition.RouteIndex = 1;
    }

    // --- Стартовый набор ---

    [Test]
    public void StartingKit_EquippedOnce_StorageHasTwoFinds()
    {
        GameState state = NewGame();
        Assert.AreEqual(ItemCatalog.Sword, ItemIdOf(Equipped(state, HeroId(state), ItemSlot.Weapon)));
        Assert.AreEqual(ItemCatalog.PaddedCoat, ItemIdOf(Equipped(state, HeroId(state), ItemSlot.Protection)));
        Assert.AreEqual(ItemCatalog.SwordAndShield, ItemIdOf(Equipped(state, "garrick", ItemSlot.Weapon)));
        Assert.AreEqual(ItemCatalog.MailShirt, ItemIdOf(Equipped(state, "garrick", ItemSlot.Protection)));
        Assert.AreEqual(ItemCatalog.HealerBag, ItemIdOf(Equipped(state, "marta", ItemSlot.Special)));
        CollectionAssert.AreEquivalent(new[] { ItemCatalog.StoreroomMail, ItemCatalog.OldHuntingBow },
            ItemService.Storage(state).Select(i => i.ItemId));

        int count = state.Inventory.Items.Count;
        ItemService.EnsureInventory(state);
        ItemService.EnsureInventory(state);
        Assert.AreEqual(count, state.Inventory.Items.Count, "Стартовый набор — один раз.");
    }

    // --- Сборка боевых чисел ---

    [Test]
    public void Assembler_TemplatePlusItems_AndMaxHpFollows()
    {
        GameState state = NewGame();
        UnitCombatStats garrick = CombatStatsAssembler.Compute(state, "garrick").Final;
        Assert.AreEqual(5, garrick.Defense, "Шаблон 2 + щит 1 + кольчуга 2.");
        Assert.AreEqual(2, garrick.Initiative, "Кольчуга −1 инициатива.");

        AssembledCombatStats edric = CombatStatsAssembler.Compute(state, "edric");
        Assert.AreEqual(3, edric.Final.Attack);
        Assert.AreEqual(22, edric.Final.MaxHitPoints);
        Assert.AreEqual(22, HomePeopleService.Find(state, "edric").MaxHitPoints, "Максимум HP человека = собранный.");
        Assert.IsTrue(edric.Sources.Any(s => s.StartsWith("Лук")));
    }

    [Test]
    public void Assembler_HeroQualities_ApplyByThresholds()
    {
        GameState state = NewGame();
        HeroProfileData profile = state.GetSelectedCommander().HeroProfile;
        UnitCombatStats before = CombatStatsAssembler.Compute(state, HeroId(state)).Final;

        profile.SetQuality(HeroQuality.Strength, 7);
        profile.SetQuality(HeroQuality.Fortitude, 7);
        profile.SetQuality(HeroQuality.Dexterity, 7);
        UnitCombatStats after = CombatStatsAssembler.Compute(state, HeroId(state)).Final;

        Assert.AreEqual(before.Damage + 1, after.Damage);
        Assert.AreEqual(before.MaxHitPoints + 4, after.MaxHitPoints);
        Assert.AreEqual(before.Initiative + 1, after.Initiative);
    }

    [Test]
    public void BattleRequest_CarriesAssembledStats()
    {
        GameState state = NewGame();
        Depart(state, new[] { "garrick" });
        CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, "test");
        CampaignBattleParticipant garrick = request.Participants.Single(p => p.PersonId == "garrick");

        Assert.IsTrue(garrick.HasAssembledStats);
        Assert.AreEqual(CombatStatsAssembler.Compute(state, "garrick").Final.Defense, garrick.Defense);
        Assert.AreEqual(5, garrick.Defense);
    }

    // --- Команды ---

    [Test]
    public void Equip_SwapsPreviousToStorage_OneCopyEach()
    {
        GameState state = NewGame();
        ItemInstanceData mail = ItemService.Storage(state).Single(i => i.ItemId == ItemCatalog.StoreroomMail);
        int total = state.Inventory.Items.Count;

        Assert.IsTrue(ItemService.TryEquip(state, mail.InstanceId, "torvin", out string message), message);
        Assert.AreEqual(ItemCatalog.StoreroomMail, ItemIdOf(Equipped(state, "torvin", ItemSlot.Protection)));
        Assert.IsTrue(ItemService.Storage(state).Any(i => i.ItemId == ItemCatalog.PaddedCoat), "Стёганка ушла в кладовую.");
        Assert.AreEqual(total, state.Inventory.Items.Count, "Передача не копирует вещь.");
        Assert.AreEqual(4, CombatStatsAssembler.Compute(state, "torvin").Final.Defense);
    }

    [Test]
    public void Equip_WrongRoleOrNonFighter_Rejected()
    {
        GameState state = NewGame();
        ItemInstanceData bow = ItemService.Storage(state).Single(i => i.ItemId == ItemCatalog.OldHuntingBow);

        Assert.IsFalse(ItemService.TryEquip(state, bow.InstanceId, "torvin", out string message));
        StringAssert.Contains("для стрелков", message);
        Assert.IsFalse(ItemService.TryEquip(state, bow.InstanceId, HomePeopleService.UlyanaId, out message));
        StringAssert.Contains("не воюет", message);
        Assert.IsTrue(ItemService.TryEquip(state, bow.InstanceId, "edric", out message), message);
        Assert.AreEqual(2, CombatStatsAssembler.Compute(state, "edric").Final.AttackRange);
    }

    [Test]
    public void Departed_StorageUnreachable_PartyItemsPresent()
    {
        GameState state = NewGame();
        Depart(state, new[] { "garrick" });
        ItemInstanceData mail = ItemService.Storage(state).Single(i => i.ItemId == ItemCatalog.StoreroomMail);

        Assert.IsFalse(ItemService.TryTakeToPack(state, mail.InstanceId, out string message));
        Assert.AreEqual("Нужно вернуться в Дом.", message);
        List<string> present = ItemService.GetPresentItemIds(state);
        CollectionAssert.DoesNotContain(present, ItemCatalog.StoreroomMail);
        CollectionAssert.Contains(present, ItemCatalog.SwordAndShield, "Вещи отряда рядом.");
        CollectionAssert.DoesNotContain(present, ItemCatalog.Spear, "Копьё Торвина осталось дома.");
    }

    [Test]
    public void Unequip_InExpedition_GoesToHeroPack_PackLimited()
    {
        GameState state = NewGame();
        Depart(state, new[] { "garrick", "edric", "torvin", "agnessa" });

        foreach (string id in new[] { "garrick", "edric", "torvin", "agnessa" })
        {
            ItemInstanceData protection = Equipped(state, id, ItemSlot.Protection);
            Assert.IsTrue(ItemService.TryUnequip(state, protection.InstanceId, out string message), message);
        }
        Assert.AreEqual(ItemService.HeroPackSize, ItemService.HeroPackCount(state));

        ItemInstanceData weapon = Equipped(state, "garrick", ItemSlot.Weapon);
        Assert.IsFalse(ItemService.TryUnequip(state, weapon.InstanceId, out string full));
        StringAssert.Contains("Сумка героя полна", full);
        Assert.IsNotNull(Equipped(state, "garrick", ItemSlot.Weapon), "Отказ ничего не меняет.");
    }

    [Test]
    public void StoryItem_SyncedToHero_CannotBeStored()
    {
        GameState state = NewGame();
        state.Narrative.GrantItem(ItemCatalog.SevenToothGauge);
        ItemService.SyncStoryItems(state);
        ItemService.SyncStoryItems(state);

        List<ItemInstanceData> gauges = state.Inventory.Items.Where(i => i.ItemId == ItemCatalog.SevenToothGauge).ToList();
        Assert.AreEqual(1, gauges.Count);
        Assert.AreEqual(HeroId(state), gauges[0].OwnerPersonId);
        Assert.IsFalse(ItemService.TryStore(state, gauges[0].InstanceId, out string message));
        StringAssert.Contains("остаётся при герое", message);
    }

    [Test]
    public void DeadFighter_ItemsGoToStorage()
    {
        GameState state = NewGame();
        HomePeopleService.MarkDead(state, "garrick", "test");
        Assert.IsTrue(ItemService.Storage(state).Any(i => i.ItemId == ItemCatalog.SwordAndShield));
        Assert.IsTrue(ItemService.Storage(state).Any(i => i.ItemId == ItemCatalog.MailShirt));
        Assert.AreEqual(0, ItemService.OwnedBy(state, "garrick").Count);
    }

    [Test]
    public void Consumables_DriedFishInExpedition_HerbsOnExhausted()
    {
        GameState state = NewGame();
        ItemInstanceData fish = ItemService.GrantOnce(state, "test.fish", ItemCatalog.DriedFish, HeroId(state));
        ItemInstanceData herbs = ItemService.GrantOnce(state, "test.herbs", ItemCatalog.UlyanaHerbs, HeroId(state));

        Assert.IsFalse(ItemService.TryUse(state, fish.InstanceId, null, out string message), "Дома рыбу в дорогу не едят.");
        Depart(state, new[] { "garrick" });
        int supply = state.ArmySupply;
        for (int i = 0; i < 3; i++)
            Assert.IsTrue(ItemService.TryUse(state, fish.InstanceId, null, out message), message);
        Assert.AreEqual(supply + 6, state.ArmySupply);
        Assert.IsNull(ItemService.Find(state, fish.InstanceId), "Израсходованная вещь исчезает.");

        Assert.IsFalse(ItemService.TryUse(state, herbs.InstanceId, "garrick", out message));
        StringAssert.Contains("не изнеможён", message);
        HomePeopleService.Find(state, "garrick").Exhausted = true;
        Assert.IsTrue(ItemService.TryUse(state, herbs.InstanceId, "garrick", out message), message);
        Assert.IsFalse(HomePeopleService.Find(state, "garrick").Exhausted);
        Assert.AreEqual(1, herbs.UsesLeft);
    }

    // --- Изнеможение ---

    [Test]
    public void Exhaustion_LowersStats_ClearsAfterNightAtHome()
    {
        GameState state = NewGame();
        ResidentState garrick = HomePeopleService.Find(state, "garrick");
        UnitCombatStats before = CombatStatsAssembler.Compute(state, "garrick").Final;
        garrick.Exhausted = true;
        UnitCombatStats tired = CombatStatsAssembler.Compute(state, "garrick").Final;
        Assert.AreEqual(before.Attack - 1, tired.Attack);
        Assert.AreEqual(before.Initiative - 1, tired.Initiative);

        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.SetSpeedMultiplier(state, ContinuousSimulationSystem.MaximumSpeedMultiplier);
        int day = state.Day;
        for (int i = 0; i < 40 && state.Day == day; i++)
            ContinuousSimulationSystem.Advance(state, 1f, false);
        Assert.IsFalse(garrick.Exhausted, "Ночь дома снимает изнеможение.");
    }

    [Test]
    public void SecondHungryNightOnTheRoad_ExhaustsParty()
    {
        GameState state = NewGame();
        Depart(state, new[] { "garrick" });
        state.ArmySupply = 0;

        ContinuousSimulationSystem.SetPaused(state, false);
        ContinuousSimulationSystem.SetSpeedMultiplier(state, ContinuousSimulationSystem.MaximumSpeedMultiplier);
        for (int i = 0; i < 80 && state.ConsecutiveExpeditionSupplyShortageDays < 2; i++)
            ContinuousSimulationSystem.Advance(state, 1f, false);

        Assert.IsTrue(HomePeopleService.Find(state, "garrick").Exhausted);
        Assert.IsTrue(HomePeopleService.Find(state, HeroId(state)).Exhausted);
        Assert.IsFalse(HomePeopleService.Find(state, "edric").Exhausted, "Оставшиеся дома не изнеможены.");
    }

    // --- Сохранение и возвращение ---

    [Test]
    public void SaveLoad_KeepsInventoryAndExhaustion_NoDuplicateKit()
    {
        GameState state = NewGame();
        ItemInstanceData mail = ItemService.Storage(state).Single(i => i.ItemId == ItemCatalog.StoreroomMail);
        Assert.IsTrue(ItemService.TryEquip(state, mail.InstanceId, "torvin", out string message), message);
        HomePeopleService.Find(state, "edric").Exhausted = true;
        int total = state.Inventory.Items.Count;

        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state));
        GameState restored = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));

        Assert.AreEqual(total, restored.Inventory.Items.Count);
        Assert.AreEqual(ItemCatalog.StoreroomMail, ItemIdOf(Equipped(restored, "torvin", ItemSlot.Protection)));
        Assert.IsTrue(HomePeopleService.Find(restored, "edric").Exhausted);
    }

    [Test]
    public void OldSave_WithoutInventory_GetsStartingKitOnce()
    {
        GameState state = NewGame();
        state.Inventory = null;
        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state));
        GameState restored = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));

        Assert.AreEqual(ItemCatalog.SwordAndShield, ItemIdOf(Equipped(restored, "garrick", ItemSlot.Weapon)));
        int count = restored.Inventory.Items.Count;
        ItemService.EnsureInventory(restored);
        Assert.AreEqual(count, restored.Inventory.Items.Count);
    }

    [Test]
    public void Return_KeepsSuppliesWithParty_NoReturnNotice()
    {
        GameState state = NewGame();
        Depart(state, new[] { "garrick" });
        state.ArmySupply = 17;
        state.ArmyGold = 5;
        int food = state.Food;
        int gold = state.Gold;

        state.CompleteExpeditionReturn();

        Assert.AreEqual(17, state.ArmySupply, "Припасы остаются у отряда.");
        Assert.AreEqual(5, state.ArmyGold);
        Assert.AreEqual(food, state.Food, "В Дом ничего не передаётся.");
        Assert.AreEqual(gold, state.Gold);
        Assert.IsFalse(state.HasActiveExpedition);
    }
}
