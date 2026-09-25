using System.Collections.Generic;
using System.Text;
using KingdomSurvival.Chapter01;
using UnityEngine;
using UnityEngine.UIElements;

// ПР-08 (ProjectDocs/PR08_HERO_ITEMS_SPEC.md): вещи и состояния на экране
// героя. Выбранный человек (герой или боец) — его три слота, собранные
// боевые числа и состояние; сумка героя, сюжетные вещи, кладовая Дома (только
// дома). Все изменения — командами ItemService; кнопки, без перетаскивания.
public partial class PrototypeUIController
{
    private static readonly ItemSlot[] HeroItemSlots = { ItemSlot.Weapon, ItemSlot.Protection, ItemSlot.Special };

    private bool heroItemsBound;
    private string heroItemsSelectedPersonId;
    private string heroItemsMessage = string.Empty;

    private VisualElement heroItemsPeopleRow;
    private readonly Label[] heroItemSlotNames = new Label[3];
    private readonly Label[] heroItemSlotEffects = new Label[3];
    private readonly VisualElement[] heroItemSlotActions = new VisualElement[3];
    private readonly Label[] heroPackNames = new Label[ItemService.HeroPackSize];
    private readonly Label[] heroPackEffects = new Label[ItemService.HeroPackSize];
    private readonly VisualElement[] heroPackActions = new VisualElement[ItemService.HeroPackSize];
    private VisualElement heroStoryList;
    private Label heroStorageTitle;
    private VisualElement heroStorageList;
    private Label heroItemsMessageLabel;
    private Label heroEquipmentTitle;
    private Label heroStatsTitle;
    private Label heroStatesHint;
    private VisualElement heroExperienceTrack;
    private readonly string[] heroStatExplanations = new string[7];

    private bool BindHeroItems()
    {
        heroItemsPeopleRow = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-equipment-people");
        heroStoryList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-inventory-story");
        heroStorageTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-inventory-storage-title");
        heroStorageList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-inventory-storage");
        heroItemsMessageLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-inventory-message");
        heroEquipmentTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-equipment-title");
        heroStatsTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-stats-title");
        heroStatesHint = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-states-hint");
        heroExperienceTrack = interfaceRoot.Q<VisualElement>("hero-screen-experience-track");

        bool ok = heroItemsPeopleRow != null && heroStoryList != null && heroStorageTitle != null &&
                  heroStorageList != null && heroItemsMessageLabel != null && heroEquipmentTitle != null &&
                  heroStatsTitle != null && heroStatesHint != null;

        for (int i = 0; i < 3; i++)
        {
            heroItemSlotNames[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-equipment-slot-" + (i + 1) + "-name");
            heroItemSlotEffects[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-equipment-slot-" + (i + 1) + "-effect");
            heroItemSlotActions[i] = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-equipment-slot-" + (i + 1) + "-actions");
            ok &= heroItemSlotNames[i] != null && heroItemSlotEffects[i] != null && heroItemSlotActions[i] != null;
        }

        for (int i = 0; i < ItemService.HeroPackSize; i++)
        {
            heroPackNames[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-inventory-slot-" + (i + 1) + "-name");
            heroPackEffects[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-inventory-slot-" + (i + 1) + "-effect");
            heroPackActions[i] = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-inventory-slot-" + (i + 1) + "-actions");
            ok &= heroPackNames[i] != null && heroPackEffects[i] != null && heroPackActions[i] != null;
        }

        if (!ok)
            return false;

        // Всплывающее «из чего сложилось» для всех боевых чисел.
        for (int i = 0; i < HeroScreenStatSuffixes.Length; i++)
        {
            if (HeroScreenStatSuffixes[i] == "initiative")
                continue;
            VisualElement box = interfaceRoot.Q<VisualElement>("hero-screen-stat-" + HeroScreenStatSuffixes[i]);
            if (box == null)
                continue;
            int index = i;
            string title = HeroScreenUnitCardStatTitles[i];
            box.RegisterCallback<PointerEnterEvent>(_ => ShowHeroScreenStatTooltip(box, title, heroStatExplanations[index]));
            box.RegisterCallback<PointerLeaveEvent>(_ => HideHeroScreenStatTooltip());
        }

        heroItemsBound = true;
        return true;
    }

    // Кого показывает экран: герой по умолчанию; можно выбрать бойца рядом.
    private string HeroItemsPersonId()
    {
        CommanderData commander = gameState.GetSelectedCommander();
        ResidentState selected = HomePeopleService.Find(gameState, heroItemsSelectedPersonId);
        if (selected == null || !selected.IsAlive || !IsHeroItemsCandidate(selected))
            heroItemsSelectedPersonId = commander != null ? commander.Id : null;
        return heroItemsSelectedPersonId;
    }

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

    private void RefreshHeroItems()
    {
        if (!heroItemsBound || gameState == null)
            return;

        ItemService.EnsureInventory(gameState);
        string personId = HeroItemsPersonId();
        ResidentState person = HomePeopleService.Find(gameState, personId);
        string personName = person != null ? person.DisplayName : "Командир";

        RebuildHeroItemsPeople(personId);
        heroEquipmentTitle.text = "СНАРЯЖЕНИЕ — " + personName.ToUpperInvariant();
        heroStatsTitle.text = "БОЕВЫЕ ХАРАКТЕРИСТИКИ — " + personName.ToUpperInvariant();

        for (int i = 0; i < HeroItemSlots.Length; i++)
            FillHeroItemSlot(i, personId, HeroItemSlots[i]);

        FillHeroPack(personId, personName);
        RebuildHeroStory();
        RebuildHeroStorage(personId, personName);
        RefreshHeroItemsStats(personId);
        RefreshHeroItemsState(person);
        RefreshHeroPath();
        heroItemsMessageLabel.text = heroItemsMessage;
    }

    private void RebuildHeroItemsPeople(string selectedId)
    {
        heroItemsPeopleRow.Clear();
        foreach (ResidentState resident in HomePeopleService.All(gameState))
        {
            if (!resident.IsAlive || !IsHeroItemsCandidate(resident))
                continue;
            string id = resident.PersonId;
            Button chip = new Button(() =>
            {
                heroItemsSelectedPersonId = id;
                heroItemsMessage = string.Empty;
                RefreshHeroScreen();
            }) { text = resident.DisplayName };
            chip.AddToClassList("hero-item-person");
            chip.EnableInClassList("hero-item-person--selected", id == selectedId);
            heroItemsPeopleRow.Add(chip);
        }
    }

    private void FillHeroItemSlot(int index, string personId, ItemSlot slot)
    {
        ItemInstanceData item = ItemService.Equipped(gameState, personId, slot);
        ItemDefinition definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
        heroItemSlotActions[index].Clear();

        if (definition == null)
        {
            heroItemSlotNames[index].text = "пусто";
            heroItemSlotEffects[index].text = string.Empty;
            return;
        }

        heroItemSlotNames[index].text = definition.Name;
        heroItemSlotEffects[index].text = EffectText(definition);
        string instanceId = item.InstanceId;
        AddHeroItemButton(heroItemSlotActions[index], "Снять", () =>
            RunHeroItemCommand(ItemService.TryUnequip(gameState, instanceId, out string message), message));
    }

    private void FillHeroPack(string personId, string personName)
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

        for (int i = 0; i < ItemService.HeroPackSize; i++)
        {
            heroPackActions[i].Clear();
            if (i >= pack.Count)
            {
                heroPackNames[i].text = "пусто";
                heroPackEffects[i].text = string.Empty;
                continue;
            }

            ItemInstanceData item = pack[i];
            ItemDefinition definition = ItemCatalog.Find(item.ItemId);
            heroPackNames[i].text = definition.Name + (definition.IsConsumable ? " ×" + item.UsesLeft : string.Empty);
            heroPackEffects[i].text = EffectText(definition);
            AddItemActions(heroPackActions[i], item, definition, personId, personName, true);
        }
    }

    private void RebuildHeroStory()
    {
        heroStoryList.Clear();
        CommanderData commander = gameState.GetSelectedCommander();
        int shown = 0;
        if (commander != null)
        {
            foreach (ItemInstanceData item in ItemService.OwnedBy(gameState, commander.Id))
            {
                ItemDefinition definition = ItemCatalog.Find(item.ItemId);
                if (definition == null || !definition.IsStory)
                    continue;
                heroStoryList.Add(CreateHeroItemRow(definition.Name, definition.Description));
                shown++;
            }
        }

        if (shown == 0)
            heroStoryList.Add(CreateHeroItemRow("Нет", "Важные для истории вещи будут лежать здесь — их нельзя потерять."));
    }

    private void RebuildHeroStorage(string personId, string personName)
    {
        heroStorageList.Clear();
        if (HomePeopleService.HasDeparted(gameState))
        {
            heroStorageTitle.text = "КЛАДОВАЯ ДОМА";
            heroStorageList.Add(CreateHeroItemRow("Недоступна", "Нужно вернуться в Дом."));
            return;
        }

        List<ItemInstanceData> storage = ItemService.Storage(gameState);
        heroStorageTitle.text = "КЛАДОВАЯ ДОМА — " + storage.Count;
        if (storage.Count == 0)
        {
            heroStorageList.Add(CreateHeroItemRow("Пусто", "Снятые дома вещи попадают сюда."));
            return;
        }

        foreach (ItemInstanceData item in storage)
        {
            ItemDefinition definition = ItemCatalog.Find(item.ItemId);
            if (definition == null)
                continue;
            VisualElement row = CreateHeroItemRow(
                definition.Name + (definition.IsConsumable ? " ×" + item.UsesLeft : string.Empty),
                definition.Description + (EffectText(definition).Length > 0 ? " " + EffectText(definition) : string.Empty));
            VisualElement actions = new VisualElement();
            actions.AddToClassList("hero-item-actions");
            AddItemActions(actions, item, definition, personId, personName, false);
            row.Add(actions);
            heroStorageList.Add(row);
        }
    }

    private void AddItemActions(VisualElement parent, ItemInstanceData item, ItemDefinition definition,
        string personId, string personName, bool inPack)
    {
        string instanceId = item.InstanceId;
        if (definition.Slot != ItemSlot.None)
        {
            bool canUse = ItemService.CanUseSlotItem(gameState, personId, definition, out _);
            Button equip = AddHeroItemButton(parent, "Надеть: " + personName, () =>
                RunHeroItemCommand(ItemService.TryEquip(gameState, instanceId, personId, out string message), message));
            equip.SetEnabled(canUse);
        }

        if (definition.IsConsumable)
        {
            AddHeroItemButton(parent, item.ItemId == ItemCatalog.UlyanaHerbs ? "Дать отвар: " + personName : "Использовать", () =>
                RunHeroItemCommand(ItemService.TryUse(gameState, instanceId, personId, out string message), message));
        }

        bool home = !HomePeopleService.HasDeparted(gameState);
        if (inPack && home)
        {
            AddHeroItemButton(parent, "В кладовую", () =>
                RunHeroItemCommand(ItemService.TryStore(gameState, instanceId, out string message), message));
        }
        else if (!inPack)
        {
            AddHeroItemButton(parent, "В сумку героя", () =>
                RunHeroItemCommand(ItemService.TryTakeToPack(gameState, instanceId, out string message), message));
        }
    }

    private Button AddHeroItemButton(VisualElement parent, string text, System.Action action)
    {
        Button button = new Button(action) { text = text };
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

    // Боевые числа выбранного человека — из той же сборки, что уходит в бой.
    private void RefreshHeroItemsStats(string personId)
    {
        AssembledCombatStats stats = CombatStatsAssembler.Compute(gameState, personId);
        if (!stats.HasTemplate)
            return;

        ResidentState resident = HomePeopleService.Find(gameState, personId);
        UnitCombatStats final = stats.Final;
        heroScreenStatValues[0].text = resident != null && resident.HasCombatState
            ? resident.CurrentHitPoints + "/" + final.MaxHitPoints
            : final.MaxHitPoints.ToString();
        heroScreenStatValues[1].text = final.Attack.ToString();
        heroScreenStatValues[2].text = final.Defense.ToString();
        heroScreenStatValues[3].text = final.Damage.ToString();
        heroScreenStatValues[4].text = final.Movement.ToString();
        heroScreenStatValues[5].text = final.Initiative.ToString();
        heroScreenStatValues[6].text = final.AttackRange.ToString();

        UnitCombatStats template = stats.Template;
        int[] baseValues = { template.MaxHitPoints, template.Attack, template.Defense, template.Damage, template.Movement, template.Initiative, template.AttackRange };
        string sources = stats.Sources.Count > 0 ? "\n" + string.Join("\n", stats.Sources) : string.Empty;
        for (int i = 0; i < heroStatExplanations.Length; i++)
            heroStatExplanations[i] = HeroScreenUnitCardStatExplanations[i] + "\nОснова: " + baseValues[i] + sources;
        heroScreenInitiativeExplanation = heroStatExplanations[5];
    }

    private void RefreshHeroItemsState(ResidentState person)
    {
        heroStatesHint.text = person != null ? DescribeCondition(person) : string.Empty;
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

    // «Путь» — сделанный выбор развития героя (§8.2 ТЗ).
    private void RefreshHeroPath()
    {
        if (heroExperienceTrack != null)
            heroExperienceTrack.style.display = DisplayStyle.None;
        heroScreenLevelLabel.text = "ПУТЬ";
        string growth = Chapter01OutcomeApplier.DescribeRoadGrowth(gameState.Narrative);
        heroScreenExperienceLabel.text = growth ?? "Первый выбор пути — по возвращении домой.";
    }
}
