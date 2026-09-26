using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// ПР-08 (ProjectDocs/PR08_HERO_ITEMS_SPEC.md): вещи на экране героя. Экран
// показывает общее для отряда — сумку героя, сюжетные вещи и кладовую Дома
// (только дома); три слота снаряжения человека и то, что ему можно надеть, —
// в его карточке (PrototypeUIController.HeroCard.cs). Все изменения —
// командами ItemService; кнопки, без перетаскивания.
public partial class PrototypeUIController
{
    private static readonly ItemSlot[] HeroItemSlots = { ItemSlot.Weapon, ItemSlot.Protection, ItemSlot.Special };

    private bool heroItemsBound;
    private string heroItemsMessage = string.Empty;

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
    private Label heroStatesHint;
    private VisualElement heroExperienceTrack;

    private bool BindHeroItems()
    {
        heroStoryList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-inventory-story");
        heroStorageTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-inventory-storage-title");
        heroStorageList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-inventory-storage");
        heroItemsMessageLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-inventory-message");
        heroStatesHint = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-states-hint");
        heroExperienceTrack = interfaceRoot.Q<VisualElement>("hero-screen-experience-track");

        bool ok = heroStoryList != null && heroStorageTitle != null && heroStorageList != null &&
                  heroItemsMessageLabel != null && heroStatesHint != null;

        // Слоты снаряжения живут в карточке человека.
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

        heroItemsBound = true;
        return true;
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

    private void RefreshHeroItems()
    {
        if (!heroItemsBound || gameState == null)
            return;

        ItemService.EnsureInventory(gameState);
        FillHeroPack();
        RebuildHeroStory();
        RebuildHeroStorage();
        RefreshHeroPath();
        heroItemsMessageLabel.text = heroItemsMessage;
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
            AddItemActions(heroPackActions[i], item, definition, true);
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

    private void RebuildHeroStorage()
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
            AddItemActions(actions, item, definition, false);
            row.Add(actions);
            heroStorageList.Add(row);
        }
    }

    // Общие для отряда действия с вещью: переложить и использовать то, что
    // не требует выбрать человека. Надеть и дать отвар — в карточке человека.
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
            AddHeroItemButton(parent, "В сумку героя", () =>
                RunHeroItemCommand(ItemService.TryTakeToPack(gameState, instanceId, out string message), message));
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

    // Канон v1.48 §27: уровень и опыт Командира в карточке героя.
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
