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
        TestPeople.AdmitAll(state);
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
        WorldMapNavigation.AdvanceRouteByCells(state.ActiveExpedition, 1);
        state.ActiveExpedition.LastTravelPoints.Clear();
    }

    // --- Стартовый набор ---

    [Test]
    public void StartingKit_EquippedOnce_NoStoreroom()
    {
        GameState state = NewGame();
        Assert.AreEqual(ItemCatalog.Sword, ItemIdOf(Equipped(state, HeroId(state), ItemSlot.Weapon)));
        Assert.AreEqual(ItemCatalog.PaddedCoat, ItemIdOf(Equipped(state, HeroId(state), ItemSlot.Protection)));
        Assert.AreEqual(ItemCatalog.SwordAndShield, ItemIdOf(Equipped(state, "garrick", ItemSlot.Weapon)));
        Assert.AreEqual(ItemCatalog.MailShirt, ItemIdOf(Equipped(state, "garrick", ItemSlot.Protection)));
        Assert.AreEqual(ItemCatalog.HealerBag, ItemIdOf(Equipped(state, "marta", ItemSlot.Special)));
        // Канон v1.59: кладовой Дома нет — у каждой вещи есть владелец.
        Assert.IsFalse(state.Inventory.Items.Any(i => string.IsNullOrEmpty(i.OwnerPersonId)));

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
        Assert.AreEqual(22, PeopleService.Find(state, "edric").MaxHitPoints, "Максимум HP человека = собранный.");
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
    public void Equip_SwapsPreviousToHeroPack_OneCopyEach()
    {
        GameState state = NewGame();
        ItemInstanceData mail = ItemService.GrantOnce(state, "test.mail", ItemCatalog.MailShirt, HeroId(state));
        int total = state.Inventory.Items.Count;

        Assert.IsTrue(ItemService.TryEquip(state, mail.InstanceId, "torvin", out string message), message);
        Assert.AreEqual(ItemCatalog.MailShirt, ItemIdOf(Equipped(state, "torvin", ItemSlot.Protection)));
        Assert.IsTrue(ItemService.OwnedBy(state, HeroId(state)).Any(i => i.ItemId == ItemCatalog.PaddedCoat && i.Slot == ItemSlot.None),
            "Стёганка ушла туда, откуда пришла кольчуга, — в сумку героя.");
        Assert.AreEqual(total, state.Inventory.Items.Count, "Передача не копирует вещь.");
        Assert.AreEqual(4, CombatStatsAssembler.Compute(state, "torvin").Final.Defense);
    }

    [Test]
    public void Equip_WrongRoleOrNonFighter_Rejected()
    {
        GameState state = NewGame();
        ItemInstanceData bow = ItemService.GrantOnce(state, "test.bow", ItemCatalog.Bow, HeroId(state));

        Assert.IsFalse(ItemService.TryEquip(state, bow.InstanceId, "torvin", out string message));
        StringAssert.Contains("для стрелков", message);
        Assert.IsFalse(ItemService.TryEquip(state, bow.InstanceId, "ostafiy", out message));
        StringAssert.Contains("не воюет", message);
        Assert.IsTrue(ItemService.TryEquip(state, bow.InstanceId, "edric", out message), message);
    }

    [Test]
    public void Departed_OnlyPartyItemsPresent()
    {
        GameState state = NewGame();
        Depart(state, new[] { "garrick" });
        ItemInstanceData spear = ItemService.OwnedBy(state, "torvin").Single(i => i.ItemId == ItemCatalog.Spear);

        Assert.IsFalse(ItemService.TryTakeToPack(state, spear.InstanceId, out string message));
        Assert.AreEqual("Эта вещь сейчас не рядом.", message);
        List<string> present = ItemService.GetPresentItemIds(state);
        CollectionAssert.Contains(present, ItemCatalog.SwordAndShield, "Вещи отряда рядом.");
        CollectionAssert.DoesNotContain(present, ItemCatalog.Spear, "Копьё Торвина не в походе.");
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
        Assert.AreEqual(4, ItemService.HeroPackCount(state), "Снятое в пути — в сумку героя.");

        // Сумка вмещает много, но не бесконечно: добить до предела.
        for (int i = ItemService.HeroPackCount(state); i < ItemService.HeroPackSize; i++)
            ItemService.GrantOnce(state, "test.fill." + i, ItemCatalog.DriedFish, HeroId(state));
        Assert.AreEqual(ItemService.HeroPackSize, ItemService.HeroPackCount(state));

        ItemInstanceData weapon = Equipped(state, "garrick", ItemSlot.Weapon);
        Assert.IsFalse(ItemService.TryUnequip(state, weapon.InstanceId, out string full));
        StringAssert.Contains("Сумка героя полна", full);
        Assert.IsNotNull(Equipped(state, "garrick", ItemSlot.Weapon), "Отказ ничего не меняет.");
    }

    [Test]
    public void RemovedCampaignItem_InNarrative_IsNotCreated()
    {
        // Канон v1.59: вещи удалённой кампании сняты с каталога — их метка в
        // старом состоянии сюжета не превращается в вещь.
        GameState state = NewGame();
        int count = state.Inventory.Items.Count;
        state.Narrative.GrantItem("chapter01.item.seven_tooth_gauge");
        ItemService.SyncStoryItems(state);

        Assert.IsNull(ItemCatalog.Find("chapter01.item.seven_tooth_gauge"));
        Assert.AreEqual(count, state.Inventory.Items.Count);
    }

    [Test]
    public void DeadFighter_ItemsGoToHero()
    {
        GameState state = NewGame();
        PeopleService.MarkDead(state, "garrick", "test");
        Assert.IsTrue(ItemService.OwnedBy(state, HeroId(state)).Any(i => i.ItemId == ItemCatalog.SwordAndShield));
        Assert.IsTrue(ItemService.OwnedBy(state, HeroId(state)).Any(i => i.ItemId == ItemCatalog.MailShirt));
        Assert.AreEqual(0, ItemService.OwnedBy(state, "garrick").Count);
    }

    [Test]
    public void Consumables_DriedFishInExpedition_HerbsOnExhausted()
    {
        GameState state = NewGame();
        ItemInstanceData fish = ItemService.GrantOnce(state, "test.fish", ItemCatalog.DriedFish, HeroId(state));
        ItemInstanceData herbs = ItemService.GrantOnce(state, "test.herbs", ItemCatalog.BitterInfusion, HeroId(state));

        Assert.IsFalse(ItemService.TryUse(state, fish.InstanceId, null, out string message), "Дома рыбу в дорогу не едят.");
        Depart(state, new[] { "garrick" });
        int supply = state.ArmySupply;
        for (int i = 0; i < 3; i++)
            Assert.IsTrue(ItemService.TryUse(state, fish.InstanceId, null, out message), message);
        Assert.AreEqual(supply + 6, state.ArmySupply);
        Assert.IsNull(ItemService.Find(state, fish.InstanceId), "Израсходованная вещь исчезает.");

        Assert.IsFalse(ItemService.TryUse(state, herbs.InstanceId, "garrick", out message));
        StringAssert.Contains("не изнеможён", message);
        PeopleService.Find(state, "garrick").Exhausted = true;
        Assert.IsTrue(ItemService.TryUse(state, herbs.InstanceId, "garrick", out message), message);
        Assert.IsFalse(PeopleService.Find(state, "garrick").Exhausted);
        Assert.IsNull(ItemService.Find(state, herbs.InstanceId), "Отвар на один раз.");
    }

    // --- Изнеможение ---

    [Test]
    public void Exhaustion_LowersStats()
    {
        GameState state = NewGame();
        ResidentState garrick = PeopleService.Find(state, "garrick");
        UnitCombatStats before = CombatStatsAssembler.Compute(state, "garrick").Final;
        garrick.Exhausted = true;
        UnitCombatStats tired = CombatStatsAssembler.Compute(state, "garrick").Final;
        Assert.AreEqual(before.Attack - 1, tired.Attack);
        Assert.AreEqual(before.Initiative - 1, tired.Initiative);
    }

    // --- Сохранение и возвращение ---

    [Test]
    public void SaveLoad_KeepsInventoryAndExhaustion_NoDuplicateKit()
    {
        GameState state = NewGame();
        ItemInstanceData mail = ItemService.GrantOnce(state, "test.mail", ItemCatalog.MailShirt, HeroId(state));
        Assert.IsTrue(ItemService.TryEquip(state, mail.InstanceId, "torvin", out string message), message);
        PeopleService.Find(state, "edric").Exhausted = true;
        int total = state.Inventory.Items.Count;

        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state));
        GameState restored = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));

        Assert.AreEqual(total, restored.Inventory.Items.Count);
        Assert.AreEqual(ItemCatalog.MailShirt, ItemIdOf(Equipped(restored, "torvin", ItemSlot.Protection)));
        Assert.IsTrue(PeopleService.Find(restored, "edric").Exhausted);
    }

    [Test]
    public void OldSave_WithoutInventory_GetsStartingKitOnce()
    {
        GameState state = NewGame();
        state.Inventory = null;
        string json = JsonUtility.ToJson(CampaignSaveService.ExportCampaign(state));
        GameState restored = CampaignSaveService.RestoreCampaign(JsonUtility.FromJson<CampaignSaveData>(json));

        Assert.AreEqual(ItemCatalog.Sword, ItemIdOf(Equipped(restored, HeroId(restored), ItemSlot.Weapon)));
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

        state.CompleteExpeditionReturn();

        Assert.AreEqual(17, state.ArmySupply, "Припасы остаются у отряда.");
        Assert.AreEqual(5, state.ArmyGold);
        Assert.IsFalse(state.HasActiveExpedition);
    }
}
