using System;
using System.Collections.Generic;

// ПР-08: все изменения вещей — атомарными командами с причиной отказа.
// Один предмет — один экземпляр: передача меняет владельца, а не копирует.
// Дома доступны кладовая и все живые жители; в пути — только вещи тех,
// кто в отряде, кладовая недоступна («Нужно вернуться в Дом»).
public static class ItemService
{
    public const int HeroPackSize = 4;
    public const string StartingKitGrant = "pr08.grant.starting_kit";

    // Стартовое снаряжение (§7 ТЗ с правкой §0.4).
    private static readonly Dictionary<string, string[]> StartingKit = new Dictionary<string, string[]>
    {
        { "garrick", new[] { ItemCatalog.SwordAndShield, ItemCatalog.MailShirt } },
        { "edric", new[] { ItemCatalog.Bow, ItemCatalog.Jacket } },
        { "marta", new[] { ItemCatalog.Knife, ItemCatalog.Jacket, ItemCatalog.HealerBag } },
        { "torvin", new[] { ItemCatalog.Spear, ItemCatalog.PaddedCoat } },
        { "agnessa", new[] { ItemCatalog.Bow, ItemCatalog.Jacket } },
        { HomePeopleService.TikhonId, new[] { ItemCatalog.Axe, ItemCatalog.FisherCoat } }
    };

    private static readonly string[] HeroKit = { ItemCatalog.Sword, ItemCatalog.PaddedCoat };
    private static readonly string[] StoreroomKit = { ItemCatalog.StoreroomMail, ItemCatalog.OldHuntingBow };

    public static InventoryData Get(GameState state)
    {
        if (state.Inventory == null)
            state.Inventory = new InventoryData();
        if (state.Inventory.Items == null)
            state.Inventory.Items = new List<ItemInstanceData>();
        if (state.Inventory.AppliedGrants == null)
            state.Inventory.AppliedGrants = new List<string>();
        return state.Inventory;
    }

    // Новая партия и загрузка: стартовый набор один раз, сюжетные метки
    // NarrativeState.Items — в сюжетные вещи героя, снаряжение принятых
    // позже людей (Тихон). Идемпотентно — можно вызывать часто.
    public static void EnsureInventory(GameState state)
    {
        if (state?.People == null)
            return;

        InventoryData inventory = Get(state);
        if (!inventory.AppliedGrants.Contains(StartingKitGrant))
        {
            inventory.AppliedGrants.Add(StartingKitGrant);
            CommanderData commander = state.GetSelectedCommander();
            if (commander != null)
            {
                foreach (string itemId in HeroKit)
                    Equip(state, Create(state, itemId, commander.Id));
            }
            foreach (string itemId in StoreroomKit)
                Create(state, itemId, string.Empty);
        }

        foreach (KeyValuePair<string, string[]> kit in StartingKit)
        {
            ResidentState resident = HomePeopleService.Find(state, kit.Key);
            string grant = "pr08.grant.kit." + kit.Key;
            if (resident == null || inventory.AppliedGrants.Contains(grant))
                continue;
            inventory.AppliedGrants.Add(grant);
            foreach (string itemId in kit.Value)
                Equip(state, Create(state, itemId, kit.Key));
        }

        SyncStoryItems(state);
        RefreshAllMaxHitPoints(state);
    }

    // Сюжетная метка из диалогов/исходов главы становится вещью у героя.
    public static void SyncStoryItems(GameState state)
    {
        if (state?.Narrative?.Items == null)
            return;
        CommanderData commander = state.GetSelectedCommander();
        if (commander == null)
            return;

        foreach (string itemId in state.Narrative.Items)
        {
            ItemDefinition definition = ItemCatalog.Find(itemId);
            if (definition == null || FindFirst(state, itemId) != null)
                continue;
            Create(state, itemId, commander.Id);
        }
    }

    // Однократная выдача находки (grantId — стабильный ID операции).
    public static ItemInstanceData GrantOnce(GameState state, string grantId, string itemId, string ownerPersonId)
    {
        InventoryData inventory = Get(state);
        if (inventory.AppliedGrants.Contains(grantId))
            return null;
        inventory.AppliedGrants.Add(grantId);
        return Create(state, itemId, ownerPersonId ?? string.Empty);
    }

    public static bool HasGrant(GameState state, string grantId)
    {
        return Get(state).AppliedGrants.Contains(grantId);
    }

    private static ItemInstanceData Create(GameState state, string itemId, string ownerPersonId)
    {
        ItemDefinition definition = ItemCatalog.Find(itemId);
        if (definition == null)
            throw new ArgumentException("Неизвестная вещь: " + itemId, nameof(itemId));

        InventoryData inventory = Get(state);
        ItemInstanceData instance = new ItemInstanceData
        {
            InstanceId = "item." + inventory.NextNumber.ToString("0000"),
            ItemId = itemId,
            OwnerPersonId = ownerPersonId ?? string.Empty,
            Slot = ItemSlot.None,
            UsesLeft = definition.Uses
        };
        inventory.NextNumber++;
        inventory.Items.Add(instance);
        return instance;
    }

    private static void Equip(GameState state, ItemInstanceData instance)
    {
        ItemDefinition definition = ItemCatalog.Find(instance.ItemId);
        if (definition != null && definition.Slot != ItemSlot.None)
            instance.Slot = definition.Slot;
    }

    // ------------------------------------------------------------------
    // Запросы
    // ------------------------------------------------------------------

    public static ItemInstanceData Find(GameState state, string instanceId)
    {
        foreach (ItemInstanceData item in Get(state).Items)
        {
            if (item.InstanceId == instanceId)
                return item;
        }
        return null;
    }

    public static ItemInstanceData FindFirst(GameState state, string itemId)
    {
        foreach (ItemInstanceData item in Get(state).Items)
        {
            if (item.ItemId == itemId)
                return item;
        }
        return null;
    }

    public static List<ItemInstanceData> OwnedBy(GameState state, string personId)
    {
        List<ItemInstanceData> result = new List<ItemInstanceData>();
        foreach (ItemInstanceData item in Get(state).Items)
        {
            if (item.OwnerPersonId == (personId ?? string.Empty))
                result.Add(item);
        }
        return result;
    }

    public static List<ItemInstanceData> Storage(GameState state)
    {
        return OwnedBy(state, string.Empty);
    }

    public static ItemInstanceData Equipped(GameState state, string personId, ItemSlot slot)
    {
        foreach (ItemInstanceData item in Get(state).Items)
        {
            if (item.OwnerPersonId == personId && item.Slot == slot && slot != ItemSlot.None)
                return item;
        }
        return null;
    }

    public static StatModifier EquippedModifier(GameState state, string personId)
    {
        StatModifier total = new StatModifier();
        foreach (ItemInstanceData item in Get(state).Items)
        {
            if (item.OwnerPersonId != personId || item.Slot == ItemSlot.None)
                continue;
            ItemDefinition definition = ItemCatalog.Find(item.ItemId);
            if (definition == null)
                continue;
            total.MaxHitPoints += definition.Modifier.MaxHitPoints;
            total.Attack += definition.Modifier.Attack;
            total.Defense += definition.Modifier.Defense;
            total.Damage += definition.Modifier.Damage;
            total.Movement += definition.Modifier.Movement;
            total.Initiative += definition.Modifier.Initiative;
            total.AttackRange += definition.Modifier.AttackRange;
        }
        return total;
    }

    // Сумка героя: его неэкипированные несюжетные вещи.
    public static int HeroPackCount(GameState state)
    {
        CommanderData commander = state.GetSelectedCommander();
        if (commander == null)
            return 0;
        int count = 0;
        foreach (ItemInstanceData item in OwnedBy(state, commander.Id))
        {
            ItemDefinition definition = ItemCatalog.Find(item.ItemId);
            if (item.Slot == ItemSlot.None && definition != null && !definition.IsStory)
                count++;
        }
        return count;
    }

    // Кто сейчас рядом с героем: в пути — отряд, дома — все живые жители.
    private static bool IsWithHero(GameState state, string personId)
    {
        ResidentState resident = HomePeopleService.Find(state, personId);
        if (resident == null || !resident.IsAlive)
            return false;
        if (HomePeopleService.HasDeparted(state))
        {
            CommanderData commander = state.GetSelectedCommander();
            return (commander != null && commander.Id == personId) ||
                   HomePeopleService.IsInExpedition(state, personId);
        }
        return resident.IsHomeMember;
    }

    public static bool IsReachable(GameState state, ItemInstanceData item)
    {
        if (item == null)
            return false;
        if (string.IsNullOrEmpty(item.OwnerPersonId))
            return !HomePeopleService.HasDeparted(state);
        return IsWithHero(state, item.OwnerPersonId);
    }

    // Вещи «рядом» для условий ItemPresent и модификаторов проверок: в пути
    // — у участников отряда, дома — у жителей и в кладовой. Прежние
    // сюжетные метки NarrativeState.Items учитываются всегда.
    public static List<string> GetPresentItemIds(GameState state)
    {
        List<string> ids = new List<string>();
        if (state?.Narrative?.Items != null)
            ids.AddRange(state.Narrative.Items);
        if (state?.Inventory?.Items == null)
            return ids;

        foreach (ItemInstanceData item in state.Inventory.Items)
        {
            if (IsReachable(state, item) && !ids.Contains(item.ItemId))
                ids.Add(item.ItemId);
        }
        return ids;
    }

    public static bool IsPresent(GameState state, string itemId)
    {
        return GetPresentItemIds(state).Contains(itemId);
    }

    public static bool CanUseSlotItem(GameState state, string personId, ItemDefinition definition, out string reason)
    {
        reason = string.Empty;
        ResidentState resident = HomePeopleService.Find(state, personId);
        if (resident == null || !resident.IsAlive)
        {
            reason = "Этого человека нет среди живых.";
            return false;
        }
        if (resident.TravelRole != ResidentTravelRole.Commander && resident.TravelRole != ResidentTravelRole.Combatant)
        {
            reason = resident.AgeGroup == ResidentAgeGroup.Child
                ? resident.DisplayName + " — ребёнок: снаряжение не для него."
                : resident.DisplayName + " не воюет — снаряжение ему ни к чему.";
            return false;
        }
        if (definition.UnitTypes != null && definition.UnitTypes.Length > 0 &&
            Array.IndexOf(definition.UnitTypes, resident.UnitTypeId) < 0)
        {
            reason = definition.Name + " — для стрелков; " + resident.DisplayName + " с ним не управится.";
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------
    // Команды
    // ------------------------------------------------------------------

    // Надеть вещь на человека. Прежняя вещь из того же слота уходит туда,
    // откуда пришла новая (кладовая или сумка героя), либо в кладовую/сумку.
    public static bool TryEquip(GameState state, string instanceId, string personId, out string message)
    {
        ItemInstanceData item = Find(state, instanceId);
        ItemDefinition definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
        if (definition == null)
        {
            message = "Такой вещи нет.";
            return false;
        }
        if (definition.Slot == ItemSlot.None)
        {
            message = definition.Name + " не надевают.";
            return false;
        }
        if (!IsReachable(state, item) || !IsWithHero(state, personId))
        {
            message = "Вещь и человек должны быть рядом. Кладовая — только дома.";
            return false;
        }
        if (!CanUseSlotItem(state, personId, definition, out message))
            return false;
        if (item.OwnerPersonId == personId && item.Slot == definition.Slot)
        {
            message = string.Empty;
            return true;
        }

        ItemInstanceData previous = Equipped(state, personId, definition.Slot);
        string sourceOwner = item.OwnerPersonId;
        bool sourceWasSlot = item.Slot != ItemSlot.None;

        if (previous != null)
        {
            if (!TryPlaceUnequipped(state, previous, sourceWasSlot ? null : sourceOwner, item, out message))
                return false;
        }

        item.OwnerPersonId = personId;
        item.Slot = definition.Slot;
        RefreshMaxHitPoints(state, personId);
        if (!string.IsNullOrEmpty(sourceOwner) && sourceOwner != personId)
            RefreshMaxHitPoints(state, sourceOwner);

        ResidentState resident = HomePeopleService.Find(state, personId);
        message = (resident != null ? resident.DisplayName : personId) + ": " + definition.Name + ".";
        return true;
    }

    // Снять: дома — в кладовую, в пути — в сумку героя.
    public static bool TryUnequip(GameState state, string instanceId, out string message)
    {
        ItemInstanceData item = Find(state, instanceId);
        ItemDefinition definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
        if (definition == null || item.Slot == ItemSlot.None)
        {
            message = "Эта вещь не надета.";
            return false;
        }
        if (!IsReachable(state, item))
        {
            message = "Эта вещь сейчас не рядом.";
            return false;
        }

        string owner = item.OwnerPersonId;
        if (!TryPlaceUnequipped(state, item, null, null, out message))
            return false;
        RefreshMaxHitPoints(state, owner);
        message = definition.Name + (string.IsNullOrEmpty(item.OwnerPersonId) ? " — в кладовой." : " — в сумке героя.");
        return true;
    }

    // Отнести в кладовую (дома). Сюжетные вещи остаются при герое.
    public static bool TryStore(GameState state, string instanceId, out string message)
    {
        ItemInstanceData item = Find(state, instanceId);
        ItemDefinition definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
        if (definition == null)
        {
            message = "Такой вещи нет.";
            return false;
        }
        if (definition.IsStory)
        {
            message = definition.Name + " остаётся при герое.";
            return false;
        }
        if (HomePeopleService.HasDeparted(state))
        {
            message = "Нужно вернуться в Дом.";
            return false;
        }
        if (!IsReachable(state, item))
        {
            message = "Эта вещь сейчас не рядом.";
            return false;
        }

        string owner = item.OwnerPersonId;
        item.OwnerPersonId = string.Empty;
        item.Slot = ItemSlot.None;
        if (!string.IsNullOrEmpty(owner))
            RefreshMaxHitPoints(state, owner);
        message = definition.Name + " — в кладовой.";
        return true;
    }

    // Взять в сумку героя (из кладовой дома или у человека рядом).
    public static bool TryTakeToPack(GameState state, string instanceId, out string message)
    {
        ItemInstanceData item = Find(state, instanceId);
        ItemDefinition definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
        CommanderData commander = state.GetSelectedCommander();
        if (definition == null || commander == null)
        {
            message = "Такой вещи нет.";
            return false;
        }
        if (!IsReachable(state, item))
        {
            message = string.IsNullOrEmpty(item.OwnerPersonId) ? "Нужно вернуться в Дом." : "Эта вещь сейчас не рядом.";
            return false;
        }
        if (item.OwnerPersonId == commander.Id && item.Slot == ItemSlot.None)
        {
            message = string.Empty;
            return true;
        }
        if (!definition.IsStory && HeroPackCount(state) >= HeroPackSize)
        {
            message = "Сумка героя полна: не больше " + HeroPackSize + " вещей.";
            return false;
        }

        string owner = item.OwnerPersonId;
        item.OwnerPersonId = commander.Id;
        item.Slot = ItemSlot.None;
        if (!string.IsNullOrEmpty(owner))
            RefreshMaxHitPoints(state, owner);
        message = definition.Name + " — в сумке героя.";
        return true;
    }

    // Использовать расходуемую вещь. Сушёная рыба — только в походе,
    // травы — на изнеможённого рядом.
    public static bool TryUse(GameState state, string instanceId, string targetPersonId, out string message)
    {
        ItemInstanceData item = Find(state, instanceId);
        ItemDefinition definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
        if (definition == null || !definition.IsConsumable || item.UsesLeft <= 0)
        {
            message = "Это нельзя использовать.";
            return false;
        }
        if (!IsReachable(state, item))
        {
            message = "Эта вещь сейчас не рядом.";
            return false;
        }

        switch (item.ItemId)
        {
            case ItemCatalog.DriedFish:
                if (!state.HasActiveExpedition)
                {
                    message = "Рыбу в дорогу едят в походе.";
                    return false;
                }
                state.ArmySupply += 2;
                message = "Отряд поел сушёной рыбы: +2 припаса.";
                break;

            case ItemCatalog.UlyanaHerbs:
                ResidentState target = HomePeopleService.Find(state, targetPersonId);
                if (target == null || !IsWithHero(state, targetPersonId))
                {
                    message = "Отвар нужно дать тому, кто рядом.";
                    return false;
                }
                if (!target.Exhausted)
                {
                    message = target.DisplayName + " не изнеможён — отвар не нужен.";
                    return false;
                }
                target.Exhausted = false;
                message = target.DisplayName + ": отвар Ульяны снял изнеможение.";
                break;

            default:
                message = "Это нельзя использовать.";
                return false;
        }

        item.UsesLeft--;
        if (item.UsesLeft <= 0)
            Get(state).Items.Remove(item);
        return true;
    }

    // Человек погиб: сюжетные вещи — к герою, остальное — в кладовую Дома
    // (в пути недоступна, как и вся кладовая).
    public static void OnPersonDied(GameState state, string personId)
    {
        if (state?.Inventory?.Items == null)
            return;
        CommanderData commander = state.GetSelectedCommander();
        foreach (ItemInstanceData item in state.Inventory.Items)
        {
            if (item.OwnerPersonId != personId)
                continue;
            ItemDefinition definition = ItemCatalog.Find(item.ItemId);
            bool story = definition != null && definition.IsStory;
            item.OwnerPersonId = story && commander != null && commander.Id != personId ? commander.Id : string.Empty;
            item.Slot = ItemSlot.None;
        }
    }

    private static bool TryPlaceUnequipped(GameState state, ItemInstanceData item, string preferredOwner,
        ItemInstanceData incoming, out string message)
    {
        message = string.Empty;
        CommanderData commander = state.GetSelectedCommander();
        bool departed = HomePeopleService.HasDeparted(state);

        // В пути вещь не может уйти в кладовую — только в сумку героя.
        string owner = preferredOwner ?? (departed ? commander?.Id : string.Empty);
        if (string.IsNullOrEmpty(owner) && departed)
            owner = commander?.Id;

        if (!string.IsNullOrEmpty(owner) && commander != null && owner == commander.Id)
        {
            int packAfter = HeroPackCount(state) + 1;
            if (incoming != null && incoming.OwnerPersonId == commander.Id && incoming.Slot == ItemSlot.None)
                packAfter--;
            if (packAfter > HeroPackSize)
            {
                message = "Сумка героя полна: сначала освободите место.";
                return false;
            }
        }

        item.OwnerPersonId = owner ?? string.Empty;
        item.Slot = ItemSlot.None;
        return true;
    }

    // ------------------------------------------------------------------
    // Максимум HP зависит от вещей и качеств (CombatStatsAssembler).
    // ------------------------------------------------------------------

    public static void RefreshAllMaxHitPoints(GameState state)
    {
        foreach (ResidentState resident in HomePeopleService.All(state))
            RefreshMaxHitPoints(state, resident.PersonId);
    }

    public static void RefreshMaxHitPoints(GameState state, string personId)
    {
        ResidentState resident = HomePeopleService.Find(state, personId);
        if (resident == null || !resident.HasCombatState || !resident.IsAlive)
            return;

        int newMax = CombatStatsAssembler.Compute(state, personId).Final.MaxHitPoints;
        if (newMax <= 0 || newMax == resident.MaxHitPoints)
            return;

        bool wasFull = resident.CurrentHitPoints >= resident.MaxHitPoints;
        resident.MaxHitPoints = newMax;
        resident.CurrentHitPoints = wasFull ? newMax : Math.Min(resident.CurrentHitPoints, newMax);
        if (resident.RecoveryBaseHitPoints > newMax)
            resident.RecoveryBaseHitPoints = newMax;
    }
}
