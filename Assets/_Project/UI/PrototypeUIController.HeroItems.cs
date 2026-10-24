using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// ПР-08 (ProjectDocs/PR08_HERO_ITEMS_SPEC.md): вещи на экране героя.
// Вкладка «Снаряжение» — рабочее место выбранного слева человека: три его
// слота (HeroCard.cs), сумка героя (4 места), сюжетные вещи и кладовая Дома
// (только дома). Каждая вещь сразу говорит, подходит ли она выбранному
// человеку, и надевается на него одной кнопкой; кладовая фильтруется по
// типу и пригодности и упорядочивается по типу, силе или названию. Клик по
// пустому слоту показывает в кладовой вещи этого типа. Все изменения —
// командами ItemService; кнопки, без перетаскивания. Сводка снаряжения
// Командира — на вкладке «Герой».
public partial class PrototypeUIController
{
    private static readonly ItemSlot[] HeroItemSlots = { ItemSlot.Weapon, ItemSlot.Protection, ItemSlot.Special };
    private static readonly string[] HeroItemSlotCaptions = { "Оружие", "Защита", "Особое" };

    private enum HeroStoreFilter
    {
        All,
        Fits,
        Weapon,
        Protection,
        Special,
        Consumable
    }

    private enum HeroStoreSort
    {
        Kind,
        Power,
        Name
    }

    private bool heroItemsBound;
    private string heroItemsMessage = string.Empty;

    private readonly VisualElement[] heroItemSlotRoots = new VisualElement[3];
    private readonly Label[] heroItemSlotNames = new Label[3];
    private readonly Label[] heroItemSlotEffects = new Label[3];
    private readonly VisualElement[] heroItemSlotActions = new VisualElement[3];
    private readonly VisualElement[] heroPackTiles = new VisualElement[ItemService.HeroPackSize];
    private readonly Label[] heroPackNames = new Label[ItemService.HeroPackSize];
    private readonly Label[] heroPackEffects = new Label[ItemService.HeroPackSize];
    private readonly VisualElement[] heroPackActions = new VisualElement[ItemService.HeroPackSize];
    private Label heroPackTitle;
    private VisualElement heroStoryList;
    private Label heroStorageTitle;
    private VisualElement heroStorageList;
    private ScrollView heroStorageScroll;
    private VisualElement heroHeroGearList;
    private Label heroItemsMessageLabel;
    private Label heroStatesHint;
    private VisualElement heroExperienceTrack;
    private HeroToggleGroup<HeroStoreFilter> heroStoreFilter;
    private HeroToggleGroup<HeroStoreSort> heroStoreSort;

    private bool BindHeroItems()
    {
        heroPackTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-inventory-title");
        heroStoryList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-inventory-story");
        heroStorageTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-available-title");
        heroStorageList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-available");
        heroStorageScroll = BindRequiredElement<ScrollView>(interfaceRoot, HeroScreenName, "hero-screen-inventory-scroll");
        heroHeroGearList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-hero-gear-list");
        heroItemsMessageLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-inventory-message");
        heroStatesHint = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-states-hint");
        heroExperienceTrack = interfaceRoot.Q<VisualElement>("hero-screen-experience-track");

        bool ok = heroPackTitle != null && heroStoryList != null && heroStorageTitle != null && heroStorageList != null &&
                  heroStorageScroll != null && heroHeroGearList != null && heroItemsMessageLabel != null && heroStatesHint != null;

        for (int i = 0; i < 3; i++)
        {
            string prefix = "hero-screen-equipment-slot-" + (i + 1);
            heroItemSlotRoots[i] = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, prefix);
            heroItemSlotNames[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, prefix + "-name");
            heroItemSlotEffects[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, prefix + "-effect");
            heroItemSlotActions[i] = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, prefix + "-actions");
            ok &= heroItemSlotRoots[i] != null && heroItemSlotNames[i] != null && heroItemSlotEffects[i] != null && heroItemSlotActions[i] != null;
        }

        for (int i = 0; i < ItemService.HeroPackSize; i++)
        {
            string prefix = "hero-screen-inventory-slot-" + (i + 1);
            heroPackTiles[i] = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, prefix);
            heroPackNames[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, prefix + "-name");
            heroPackEffects[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, prefix + "-effect");
            heroPackActions[i] = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, prefix + "-actions");
            ok &= heroPackTiles[i] != null && heroPackNames[i] != null && heroPackEffects[i] != null && heroPackActions[i] != null;
        }

        heroStoreFilter = new HeroToggleGroup<HeroStoreFilter>(HeroStoreFilter.All, OnHeroStoreViewChanged);
        heroStoreSort = new HeroToggleGroup<HeroStoreSort>(HeroStoreSort.Kind, OnHeroStoreViewChanged);
        ok &= heroStoreFilter.Add(BindHeroToggle("hero-screen-inventory-filter-all"), HeroStoreFilter.All);
        ok &= heroStoreFilter.Add(BindHeroToggle("hero-screen-inventory-filter-fits"), HeroStoreFilter.Fits);
        ok &= heroStoreFilter.Add(BindHeroToggle("hero-screen-inventory-filter-weapon"), HeroStoreFilter.Weapon);
        ok &= heroStoreFilter.Add(BindHeroToggle("hero-screen-inventory-filter-protection"), HeroStoreFilter.Protection);
        ok &= heroStoreFilter.Add(BindHeroToggle("hero-screen-inventory-filter-special"), HeroStoreFilter.Special);
        ok &= heroStoreFilter.Add(BindHeroToggle("hero-screen-inventory-filter-consumable"), HeroStoreFilter.Consumable);
        ok &= heroStoreSort.Add(BindHeroToggle("hero-screen-inventory-sort-kind"), HeroStoreSort.Kind);
        ok &= heroStoreSort.Add(BindHeroToggle("hero-screen-inventory-sort-power"), HeroStoreSort.Power);
        ok &= heroStoreSort.Add(BindHeroToggle("hero-screen-inventory-sort-name"), HeroStoreSort.Name);

        if (!ok)
            return false;

        // Пустой слот — показать в кладовой вещи этого типа.
        HeroStoreFilter[] slotFilters = { HeroStoreFilter.Weapon, HeroStoreFilter.Protection, HeroStoreFilter.Special };
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            heroItemSlotRoots[i].RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button || !heroItemSlotRoots[index].ClassListContains("hero-screen-unit-card-slot--empty"))
                    return;
                heroStoreFilter.Set(slotFilters[index], true);
            });
        }

        heroItemsBound = true;
        return true;
    }

    private void OnHeroStoreViewChanged()
    {
        heroStorageScroll.scrollOffset = Vector2.zero;
        RefreshHeroScreen();
    }

    // Кто в отряде сейчас: дома — все живые Командир и бойцы Дома, в пути —
    // Командир и те, кто ушёл с ним.
    private bool IsHeroItemsCandidate(ResidentState resident)
    {
        if (resident.TravelRole != ResidentTravelRole.Commander && resident.TravelRole != ResidentTravelRole.Combatant)
            return false;
        if (!HomePeopleService.HasDeparted(gameState))
            return resident.IsHomeMember;
        CommanderData commander = gameState.GetSelectedCommander();
        return (commander != null && commander.Id == resident.PersonId) ||
               HomePeopleService.IsInExpedition(gameState, resident.PersonId);
    }

    // На кого надевать: выбранный на рабочем месте, иначе Командир.
    private string HeroItemsTargetPersonId
    {
        get
        {
            if (!string.IsNullOrEmpty(heroCardPersonId))
                return heroCardPersonId;
            CommanderData commander = gameState.GetSelectedCommander();
            return commander != null ? commander.Id : null;
        }
    }

    private void RefreshHeroItems()
    {
        if (!heroItemsBound || gameState == null)
            return;

        ItemService.EnsureInventory(gameState);
        FillHeroPack();
        RebuildHeroStory();
        RebuildHeroStorage();
        RefreshHeroGearSummary();
        RefreshHeroPath();
        heroItemsMessageLabel.text = heroItemsMessage;
    }

    private void FillHeroItemSlot(int index, string personId, ItemSlot slot)
    {
        ItemInstanceData item = ItemService.Equipped(gameState, personId, slot);
        ItemDefinition definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
        heroItemSlotActions[index].Clear();
        heroItemSlotRoots[index].EnableInClassList("hero-screen-unit-card-slot--empty", definition == null);

        if (definition == null)
        {
            heroItemSlotNames[index].text = "пусто";
            heroItemSlotEffects[index].text = HomePeopleService.HasDeparted(gameState)
                ? "Надеть можно из сумки героя."
                : "Клик — показать подходящее в кладовой.";
            return;
        }

        heroItemSlotNames[index].text = definition.Name;
        heroItemSlotEffects[index].text = EffectText(definition);
        string instanceId = item.InstanceId;
        AddHeroItemButton(heroItemSlotActions[index], "Снять", () =>
            RunHeroCardCommand(ItemService.TryUnequip(gameState, instanceId, out string message), message));
    }

    private void FillHeroPack()
    {
        CommanderData commander = gameState.GetSelectedCommander();
        List<ItemInstanceData> pack = new List<ItemInstanceData>();
        if (commander != null)
        {
            foreach (ItemInstanceData item in ItemService.OwnedBy(gameState, commander.Id))
            {
                ItemDefinition definition = ItemCatalog.Find(item.ItemId);
                if (item.Slot == ItemSlot.None && definition != null && !definition.IsStory)
                    pack.Add(item);
            }
        }

        heroPackTitle.text = "СУМКА ГЕРОЯ — " + pack.Count + " / " + ItemService.HeroPackSize;
        string targetId = HeroItemsTargetPersonId;
        for (int i = 0; i < ItemService.HeroPackSize; i++)
        {
            heroPackActions[i].Clear();
            heroPackTiles[i].EnableInClassList("hero-pack-tile--empty", i >= pack.Count);
            if (i >= pack.Count)
            {
                heroPackNames[i].text = "свободно";
                heroPackEffects[i].text = string.Empty;
                continue;
            }

            ItemInstanceData item = pack[i];
            ItemDefinition definition = ItemCatalog.Find(item.ItemId);
            heroPackNames[i].text = definition.Name + (definition.IsConsumable ? " ×" + item.UsesLeft : string.Empty);
            heroPackEffects[i].text = Join(EffectText(definition), DescribeFit(definition, targetId));
            AddPersonItemActions(heroPackActions[i], item, definition, targetId);
            AddItemActions(heroPackActions[i], item, definition, true);
        }
    }

    // Сюжетные вещи — метки с описанием по наведению: их нельзя потерять,
    // действий с ними нет.
    private void RebuildHeroStory()
    {
        heroStoryList.Clear();
        CommanderData commander = gameState.GetSelectedCommander();
        if (commander != null)
        {
            foreach (ItemInstanceData item in ItemService.OwnedBy(gameState, commander.Id))
            {
                ItemDefinition definition = ItemCatalog.Find(item.ItemId);
                if (definition != null && definition.IsStory)
                    AddHeroScreenChip(heroStoryList, definition.Name, HeroScreenTraitChipColor, definition.Description);
            }
        }

        if (heroStoryList.childCount == 0)
            AddHeroScreenHint(heroStoryList, "Пока нет. Важные для истории вещи лежат здесь — их нельзя потерять.");
    }

    // Кладовая: фильтр, порядок и пригодность для выбранного человека.
    private void RebuildHeroStorage()
    {
        heroStorageList.Clear();
        if (HomePeopleService.HasDeparted(gameState))
        {
            heroStorageTitle.text = "КЛАДОВАЯ";
            heroStorageList.Add(CreateHeroItemRow("Недоступна в пути", "Кладовая — только дома; в пути надеть можно то, что в сумке героя."));
            return;
        }

        string targetId = HeroItemsTargetPersonId;
        List<ItemInstanceData> storage = ItemService.Storage(gameState);
        heroStorageTitle.text = "КЛАДОВАЯ — " + storage.Count;

        int fits = 0, weapon = 0, protection = 0, special = 0, consumable = 0;
        List<ItemInstanceData> shown = new List<ItemInstanceData>();
        foreach (ItemInstanceData item in storage)
        {
            ItemDefinition definition = ItemCatalog.Find(item.ItemId);
            if (definition == null)
                continue;
            bool fit = FitsHeroPerson(definition, targetId);
            if (fit) fits++;
            switch (definition.Kind)
            {
                case ItemKind.Weapon: weapon++; break;
                case ItemKind.Protection: protection++; break;
                case ItemKind.Special: special++; break;
                case ItemKind.Consumable: consumable++; break;
            }
            if (PassesHeroStoreFilter(definition, fit, heroStoreFilter.Value))
                shown.Add(item);
        }

        heroStoreFilter.SetCount(HeroStoreFilter.All, storage.Count);
        heroStoreFilter.SetCount(HeroStoreFilter.Fits, fits);
        heroStoreFilter.SetCount(HeroStoreFilter.Weapon, weapon);
        heroStoreFilter.SetCount(HeroStoreFilter.Protection, protection);
        heroStoreFilter.SetCount(HeroStoreFilter.Special, special);
        heroStoreFilter.SetCount(HeroStoreFilter.Consumable, consumable);

        HeroStoreSort sort = heroStoreSort.Value;
        shown.Sort((a, b) => CompareHeroStoreItems(a, b, sort, targetId));

        foreach (ItemInstanceData item in shown)
            heroStorageList.Add(CreateHeroStoreRow(item, ItemCatalog.Find(item.ItemId), targetId));

        if (shown.Count == 0)
        {
            heroStorageList.Add(CreateHeroItemRow(
                storage.Count == 0 ? "Пусто" : "Ничего под этот фильтр",
                storage.Count == 0 ? "Снятые дома вещи попадают сюда." : "Выберите «Все», чтобы увидеть всю кладовую."));
        }
    }

    private bool FitsHeroPerson(ItemDefinition definition, string personId)
    {
        if (string.IsNullOrEmpty(personId))
            return false;
        if (definition.Slot != ItemSlot.None)
            return ItemService.CanUseSlotItem(gameState, personId, definition, out _);
        if (definition.Id == ItemCatalog.UlyanaHerbs)
        {
            ResidentState resident = HomePeopleService.Find(gameState, personId);
            return resident != null && resident.Exhausted;
        }
        return false;
    }

    private static bool PassesHeroStoreFilter(ItemDefinition definition, bool fits, HeroStoreFilter filter)
    {
        switch (filter)
        {
            case HeroStoreFilter.Fits: return fits;
            case HeroStoreFilter.Weapon: return definition.Kind == ItemKind.Weapon;
            case HeroStoreFilter.Protection: return definition.Kind == ItemKind.Protection;
            case HeroStoreFilter.Special: return definition.Kind == ItemKind.Special;
            case HeroStoreFilter.Consumable: return definition.Kind == ItemKind.Consumable;
            default: return true;
        }
    }

    private int CompareHeroStoreItems(ItemInstanceData a, ItemInstanceData b, HeroStoreSort sort, string targetId)
    {
        ItemDefinition da = ItemCatalog.Find(a.ItemId);
        ItemDefinition db = ItemCatalog.Find(b.ItemId);
        int result;
        switch (sort)
        {
            case HeroStoreSort.Power:
                result = HeroItemPower(db).CompareTo(HeroItemPower(da));
                break;
            case HeroStoreSort.Name:
                result = 0;
                break;
            default:
                // По типу, внутри — сначала то, что подходит выбранному.
                result = ((int)da.Kind).CompareTo((int)db.Kind);
                if (result == 0)
                    result = FitsHeroPerson(db, targetId).CompareTo(FitsHeroPerson(da, targetId));
                break;
        }
        return result != 0 ? result : string.Compare(da.Name, db.Name, System.StringComparison.CurrentCulture);
    }

    // Грубая «сила» вещи для порядка: сумма прибавок, жизни — вдвое легче.
    private static int HeroItemPower(ItemDefinition definition)
    {
        StatModifier m = definition.Modifier;
        return m.Attack + m.Defense + m.Damage + m.Movement + m.Initiative + m.AttackRange + m.MaxHitPoints / 2;
    }

    private static string HeroItemKindLabel(ItemDefinition definition)
    {
        switch (definition.Kind)
        {
            case ItemKind.Weapon: return "оружие";
            case ItemKind.Protection: return "защита";
            case ItemKind.Special: return "особое";
            case ItemKind.Consumable: return "расходное";
            default: return "сюжетное";
        }
    }

    // Что вещь значит для выбранного человека: заменит ли она надетое.
    private string DescribeFit(ItemDefinition definition, string personId)
    {
        if (string.IsNullOrEmpty(personId) || definition.Slot == ItemSlot.None)
            return string.Empty;
        if (!ItemService.CanUseSlotItem(gameState, personId, definition, out string reason))
            return string.IsNullOrEmpty(reason) ? "не подходит" : reason;
        ItemInstanceData current = ItemService.Equipped(gameState, personId, definition.Slot);
        ItemDefinition currentDefinition = current != null ? ItemCatalog.Find(current.ItemId) : null;
        return currentDefinition != null ? "заменит «" + currentDefinition.Name + "»" : "слот свободен";
    }

    private VisualElement CreateHeroStoreRow(ItemInstanceData item, ItemDefinition definition, string targetId)
    {
        bool fit = FitsHeroPerson(definition, targetId);
        VisualElement row = new VisualElement();
        row.AddToClassList("hero-item-row");
        row.AddToClassList("hero-store-row");
        row.EnableInClassList("hero-item-row--fits", fit);
        row.EnableInClassList("hero-item-row--unfit", definition.Slot != ItemSlot.None && !fit);

        VisualElement text = new VisualElement();
        text.AddToClassList("hero-store-row-text");
        VisualElement head = new VisualElement();
        head.AddToClassList("hero-store-row-head");
        // Имя — точное название вещи (по нему строку находят тесты).
        Label name = new Label(definition.Name);
        name.AddToClassList("hero-item-slot-name");
        head.Add(name);
        if (definition.IsConsumable)
            head.Add(CreateHeroItemTag("×" + item.UsesLeft, false));
        head.Add(CreateHeroItemTag(HeroItemKindLabel(definition), false));
        if (fit)
            head.Add(CreateHeroItemTag("подходит", true));
        text.Add(head);

        Label effect = new Label(Join(EffectText(definition), DescribeFit(definition, targetId)));
        effect.AddToClassList("hero-item-slot-effect");
        text.Add(effect);
        row.Add(text);

        VisualElement actions = new VisualElement();
        actions.AddToClassList("hero-item-actions");
        AddPersonItemActions(actions, item, definition, targetId);
        AddItemActions(actions, item, definition, false);
        row.Add(actions);
        return row;
    }

    private static Label CreateHeroItemTag(string text, bool fits)
    {
        Label tag = new Label(text);
        tag.AddToClassList("hero-item-tag");
        tag.EnableInClassList("hero-item-tag--fits", fits);
        return tag;
    }

    // Действия с вещью для выбранного человека: надеть, дать отвар.
    private void AddPersonItemActions(VisualElement parent, ItemInstanceData item, ItemDefinition definition, string personId)
    {
        if (string.IsNullOrEmpty(personId) || !FitsHeroPerson(definition, personId))
            return;

        string instanceId = item.InstanceId;
        if (definition.Slot != ItemSlot.None)
        {
            AddHeroItemButton(parent, "Надеть", () =>
                RunHeroCardCommand(ItemService.TryEquip(gameState, instanceId, personId, out string message), message))
                .AddToClassList("hero-item-button--primary");
        }
        else if (definition.Id == ItemCatalog.UlyanaHerbs)
        {
            AddHeroItemButton(parent, "Дать отвар", () =>
                RunHeroCardCommand(ItemService.TryUse(gameState, instanceId, personId, out string message), message))
                .AddToClassList("hero-item-button--primary");
        }
    }

    // Общие для отряда действия с вещью: переложить и использовать то, что
    // не требует выбрать человека.
    private void AddItemActions(VisualElement parent, ItemInstanceData item, ItemDefinition definition, bool inPack)
    {
        string instanceId = item.InstanceId;
        if (definition.IsConsumable && item.ItemId != ItemCatalog.UlyanaHerbs)
        {
            CommanderData commander = gameState.GetSelectedCommander();
            string commanderId = commander != null ? commander.Id : null;
            AddHeroItemButton(parent, "Использовать", () =>
                RunHeroItemCommand(ItemService.TryUse(gameState, instanceId, commanderId, out string message), message));
        }

        bool home = !HomePeopleService.HasDeparted(gameState);
        if (inPack && home)
        {
            AddHeroItemButton(parent, "В кладовую", () =>
                RunHeroItemCommand(ItemService.TryStore(gameState, instanceId, out string message), message));
        }
        else if (!inPack)
        {
            AddHeroItemButton(parent, "В сумку", () =>
                RunHeroItemCommand(ItemService.TryTakeToPack(gameState, instanceId, out string message), message));
        }
    }

    // Вкладка «Герой»: что на Командире сейчас.
    private void RefreshHeroGearSummary()
    {
        heroHeroGearList.Clear();
        CommanderData commander = gameState.GetSelectedCommander();
        if (commander == null)
            return;

        for (int i = 0; i < HeroItemSlots.Length; i++)
        {
            ItemInstanceData item = ItemService.Equipped(gameState, commander.Id, HeroItemSlots[i]);
            ItemDefinition definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
            heroHeroGearList.Add(CreateHeroItemRow(
                HeroItemSlotCaptions[i] + ": " + (definition != null ? definition.Name : "пусто"),
                definition != null ? EffectText(definition) : string.Empty));
        }
    }

    private Button AddHeroItemButton(VisualElement parent, string text, System.Action action)
    {
        Button button = new Button(action) { text = text };
        button.AddToClassList("ks-button");
        button.AddToClassList("hero-item-button");
        parent.Add(button);
        return button;
    }

    private static VisualElement CreateHeroItemRow(string title, string description)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("hero-item-row");
        Label name = new Label(title);
        name.AddToClassList("hero-item-slot-name");
        Label text = new Label(description ?? string.Empty);
        text.AddToClassList("hero-item-slot-effect");
        row.Add(name);
        row.Add(text);
        return row;
    }

    private static string EffectText(ItemDefinition definition)
    {
        string modifier = definition.Modifier.Describe();
        if (modifier.Length > 0)
            return modifier;
        switch (definition.Id)
        {
            case ItemCatalog.RopeWithHooks: return "У брода и на подъёме — быстрее.";
            case ItemCatalog.DriedFish: return "В походе: +2 припаса.";
            case ItemCatalog.UlyanaHerbs: return "Снимает изнеможение.";
            case ItemCatalog.HealerBag: return "Нужна лекарю.";
            default: return string.Empty;
        }
    }

    private void RunHeroItemCommand(bool ok, string message)
    {
        heroItemsMessage = message ?? string.Empty;
        if (ok && !string.IsNullOrEmpty(message))
            AddReport(message);
        homePeopleSignature = null;
        homePrepSignature = null;
        RefreshInterface();
        RefreshHeroScreen();
    }

    // Состояние словами и последствием (§5 ТЗ).
    private string DescribeCondition(ResidentState person)
    {
        List<string> parts = new List<string>();
        if (person.Injury == ResidentInjury.Recovering)
            parts.Add("Тяжёлая рана — в поход не пойдёт, пока не долечат дома.");
        else if (person.HasCombatState && person.CurrentHitPoints < person.MaxHitPoints)
            parts.Add("Рана: " + person.CurrentHitPoints + "/" + person.MaxHitPoints + " HP — лечится уходом дома.");
        if (person.Exhausted)
            parts.Add("Изнеможён — атака и инициатива ниже; пройдёт после ночи дома.");
        return parts.Count == 0 ? person.DisplayName + ": цел." : person.DisplayName + ": " + string.Join(" ", parts);
    }

    // Канон v1.48 §27: уровень и опыт Командира на вкладке «Герой».
    private void RefreshHeroPath()
    {
        CommanderData commander = gameState.GetSelectedCommander();
        PersonProgressionData record = commander != null ? CharacterProgressionService.Get(gameState, commander.Id) : null;
        if (record == null)
        {
            if (heroExperienceTrack != null)
                heroExperienceTrack.style.display = DisplayStyle.None;
            heroScreenLevelLabel.text = string.Empty;
            heroScreenExperienceLabel.text = string.Empty;
            return;
        }

        CharacterProgressionService.GetLevelProgress(gameState, record, out int current, out int required);
        heroScreenLevelLabel.text = "УР. " + record.Level;
        string progress = required > 0 ? current + " / " + required + " опыта" : "предел пути";
        if (CharacterProgressionService.PendingChoices(gameState, commander.Id) > 0)
            progress += " · ждёт выбор развития";
        heroScreenExperienceLabel.text = progress;
        if (heroExperienceTrack != null)
            heroExperienceTrack.style.display = DisplayStyle.Flex;
        float percent = required > 0 ? Mathf.Clamp01((float)current / required) * 100f : 100f;
        heroScreenExperienceFill.style.width = Length.Percent(percent);
    }
}
