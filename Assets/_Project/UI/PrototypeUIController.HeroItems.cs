using System.Collections.Generic;
using KingdomSurvival.ItemDatabase;
using UnityEngine;
using UnityEngine.UIElements;

// ПР-08 (ProjectDocs/PR08_HERO_ITEMS_SPEC.md): вещи на экране героя.
// Бриф исправлений 29.10.2026 (п. 2, 7, 8): вкладка «Снаряжение» — временно
// только Командир (полоса людей скрыта, отряд и его вещи в игре остаются).
// Кукла: портрет в центре, оружие у руки, защита у груди, особый предмет
// отдельно; характеристики сразу под ней. Сумка — сетка квадратных ячеек на
// все места (ItemService.HeroPackSize), отдельная плитка припасов (общий
// ArmySupply, не вещь); дома — сетка кладовой. Клик — выбрать вещь и её
// действия, правая кнопка — окно сведений из Базы предметов. Запасы (золото
// и припасы) — на этом же экране. Все изменения — командами ItemService.
public partial class PrototypeUIController
{
    private static readonly ItemSlot[] HeroItemSlots = { ItemSlot.Weapon, ItemSlot.Protection, ItemSlot.Special };
    private static readonly string[] HeroItemSlotCaptions = { "Оружие", "Защита", "Особое" };
    private const string HeroSuppliesCellId = "supplies";

    private bool heroItemsBound;
    private string heroItemsMessage = string.Empty;
    // Выбранная ячейка: InstanceId вещи или HeroSuppliesCellId.
    private string heroBagSelectedId;

    private readonly VisualElement[] heroItemSlotRoots = new VisualElement[3];
    private readonly VisualElement[] heroItemSlotIcons = new VisualElement[3];
    private readonly Label[] heroItemSlotNames = new Label[3];
    private readonly Label[] heroItemSlotEffects = new Label[3];
    private readonly VisualElement[] heroItemSlotActions = new VisualElement[3];
    private Label heroInventoryTitle;
    private VisualElement heroStoryList;
    private VisualElement heroStorageList;
    private Label heroStorageGridTitle;
    private VisualElement heroStorageGrid;
    private Label heroBagDetailsName;
    private Label heroBagDetailsText;
    private VisualElement heroBagDetailsActions;
    private VisualElement heroHeroGearList;
    private Label heroItemsMessageLabel;
    private Label heroStatesHint;
    private VisualElement heroExperienceTrack;
    private VisualElement heroItemPopup;
    private VisualElement heroItemPopupIcon;
    private Label heroItemPopupName;
    private Label heroItemPopupKind;
    private Label heroItemPopupText;

    private bool BindHeroItems()
    {
        heroInventoryTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-inventory-title");
        heroStoryList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-inventory-story");
        heroStorageList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-unit-card-available");
        heroStorageGridTitle = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-storage-title");
        heroStorageGrid = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-storage-grid");
        heroBagDetailsName = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-bag-details-name");
        heroBagDetailsText = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-bag-details-text");
        heroBagDetailsActions = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-bag-details-actions");
        heroHeroGearList = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-hero-gear-list");
        heroItemsMessageLabel = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-inventory-message");
        heroStatesHint = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-states-hint");
        heroExperienceTrack = interfaceRoot.Q<VisualElement>("hero-screen-experience-track");
        heroItemPopup = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-item-popup");
        heroItemPopupIcon = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, "hero-screen-item-popup-icon");
        heroItemPopupName = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-item-popup-name");
        heroItemPopupKind = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-item-popup-kind");
        heroItemPopupText = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, "hero-screen-item-popup-text");
        Button popupClose = BindRequiredElement<Button>(interfaceRoot, HeroScreenName, "hero-screen-item-popup-close");

        bool ok = heroInventoryTitle != null && heroStoryList != null && heroStorageList != null && heroStorageGridTitle != null &&
                  heroStorageGrid != null && heroBagDetailsName != null && heroBagDetailsText != null && heroBagDetailsActions != null &&
                  heroHeroGearList != null && heroItemsMessageLabel != null && heroStatesHint != null && heroItemPopup != null &&
                  heroItemPopupIcon != null && heroItemPopupName != null && heroItemPopupKind != null && heroItemPopupText != null &&
                  popupClose != null;

        for (int i = 0; i < 3; i++)
        {
            string prefix = "hero-screen-equipment-slot-" + (i + 1);
            heroItemSlotRoots[i] = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, prefix);
            heroItemSlotIcons[i] = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, prefix + "-icon");
            heroItemSlotNames[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, prefix + "-name");
            heroItemSlotEffects[i] = BindRequiredElement<Label>(interfaceRoot, HeroScreenName, prefix + "-effect");
            heroItemSlotActions[i] = BindRequiredElement<VisualElement>(interfaceRoot, HeroScreenName, prefix + "-actions");
            ok &= heroItemSlotRoots[i] != null && heroItemSlotIcons[i] != null && heroItemSlotNames[i] != null &&
                  heroItemSlotEffects[i] != null && heroItemSlotActions[i] != null;
        }
        if (!ok)
            return false;

        // ПКМ по надетой вещи — сведения.
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            heroItemSlotRoots[i].RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1)
                    return;
                ItemInstanceData item = ItemService.Equipped(gameState, HeroItemsTargetPersonId, HeroItemSlots[index]);
                if (item != null)
                    ShowHeroItemPopup(item);
                evt.StopPropagation();
            });
        }

        popupClose.clicked += HideHeroItemPopup;
        heroItemPopup.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target == heroItemPopup)
                HideHeroItemPopup();
        });
        heroItemPopup.style.display = DisplayStyle.None;
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

    // На кого надевать: на «Снаряжении» — всегда Командир (п. 8 брифа
    // исправлений); на «Развитии» — выбранный в полосе людей.
    private string HeroItemsTargetPersonId
    {
        get
        {
            CommanderData commander = gameState.GetSelectedCommander();
            if (heroScreenTab == HeroScreenTab.Gear || string.IsNullOrEmpty(heroCardPersonId))
                return commander != null ? commander.Id : null;
            return heroCardPersonId;
        }
    }

    private void RefreshHeroItems()
    {
        if (!heroItemsBound || gameState == null)
            return;

        ItemService.EnsureInventory(gameState);
        RebuildHeroStory();
        RebuildHeroBag();
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
        SetHeroItemIcon(heroItemSlotIcons[index], definition);

        if (definition == null)
        {
            heroItemSlotNames[index].text = "пусто";
            heroItemSlotEffects[index].text = "Надеть — из сумки.";
            return;
        }

        heroItemSlotNames[index].text = definition.Name;
        heroItemSlotEffects[index].text = EffectText(definition);
        string instanceId = item.InstanceId;
        AddHeroItemButton(heroItemSlotActions[index], "Снять", () =>
            RunHeroCardCommand(ItemService.TryUnequip(gameState, instanceId, out string message), message));
    }

    // Изображение вещи из Базы предметов; без него — первая буква названия.
    private static void SetHeroItemIcon(VisualElement icon, ItemDefinition definition)
    {
        icon.Clear();
        Sprite sprite = definition != null ? ItemDatabaseRuntime.Icon(definition.Id) : null;
        icon.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : new StyleBackground(StyleKeyword.None);
        if (definition != null && sprite == null)
        {
            Label letter = new Label(string.IsNullOrEmpty(definition.Name) ? "?" : definition.Name.Substring(0, 1)) { pickingMode = PickingMode.Ignore };
            letter.AddToClassList("hero-bag-cell-letter");
            icon.Add(letter);
        }
    }

    // Сюжетные вещи — метками с описанием по наведению: их нельзя потерять,
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
            AddHeroScreenHint(heroStoryList, "Пока нет — важные для истории вещи появятся здесь, их нельзя потерять.");
    }

    // ------------------------------------------------------------------
    // Сумка: сетка на все места + плитка припасов; дома — кладовая.
    // ------------------------------------------------------------------

    private void RebuildHeroBag()
    {
        heroStorageList.Clear();
        heroStorageGrid.Clear();
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
        pack.Sort(CompareHeroBagItems);

        heroInventoryTitle.text = "СУМКА — занято " + pack.Count + " / " + ItemService.HeroPackSize;
        heroStorageList.Add(CreateHeroSuppliesCell());
        for (int i = 0; i < ItemService.HeroPackSize; i++)
            heroStorageList.Add(i < pack.Count ? CreateHeroBagCell(pack[i]) : CreateHeroBagEmptyCell());
        // Вещи сверх мест (старые сохранения) не теряются — тоже в сетке.
        for (int i = ItemService.HeroPackSize; i < pack.Count; i++)
            heroStorageList.Add(CreateHeroBagCell(pack[i]));

        bool home = !HomePeopleService.HasDeparted(gameState);
        List<ItemInstanceData> storage = new List<ItemInstanceData>();
        if (home && !IsFreePlayWithoutHome)
        {
            foreach (ItemInstanceData item in ItemService.Storage(gameState))
            {
                if (ItemCatalog.Find(item.ItemId) != null)
                    storage.Add(item);
            }
            storage.Sort(CompareHeroBagItems);
        }
        bool showStorage = storage.Count > 0;
        heroStorageGridTitle.style.display = showStorage ? DisplayStyle.Flex : DisplayStyle.None;
        heroStorageGrid.style.display = showStorage ? DisplayStyle.Flex : DisplayStyle.None;
        heroStorageGridTitle.text = "КЛАДОВАЯ ДОМА — " + storage.Count;
        foreach (ItemInstanceData item in storage)
            heroStorageGrid.Add(CreateHeroBagCell(item));

        RenderHeroBagDetails();
    }

    private static int CompareHeroBagItems(ItemInstanceData a, ItemInstanceData b)
    {
        ItemDefinition da = ItemCatalog.Find(a.ItemId);
        ItemDefinition db = ItemCatalog.Find(b.ItemId);
        int byKind = ((int)da.Kind).CompareTo((int)db.Kind);
        if (byKind != 0)
            return byKind;
        int byName = string.Compare(da.Name, db.Name, System.StringComparison.CurrentCulture);
        return byName != 0 ? byName : string.CompareOrdinal(a.InstanceId, b.InstanceId);
    }

    private VisualElement CreateHeroBagCell(ItemInstanceData item)
    {
        ItemDefinition definition = ItemCatalog.Find(item.ItemId);
        VisualElement cell = new VisualElement { name = "hero-bag-cell-" + item.InstanceId };
        cell.AddToClassList("hero-bag-cell");
        cell.EnableInClassList("hero-bag-cell--selected", heroBagSelectedId == item.InstanceId);
        cell.EnableInClassList("hero-bag-cell--storage", string.IsNullOrEmpty(item.OwnerPersonId));
        VisualElement icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("hero-bag-cell-icon");
        SetHeroItemIcon(icon, definition);
        cell.Add(icon);
        if (definition.HasUse)
        {
            Label uses = new Label("×" + item.UsesLeft) { pickingMode = PickingMode.Ignore };
            uses.AddToClassList("hero-bag-cell-badge");
            cell.Add(uses);
        }
        cell.tooltip = definition.Name + " — " + HeroItemKindLabel(definition) + ". ПКМ — сведения.";
        string instanceId = item.InstanceId;
        cell.RegisterCallback<PointerDownEvent>(evt =>
        {
            evt.StopPropagation();
            ItemInstanceData current = ItemService.Find(gameState, instanceId);
            if (current == null)
                return;
            if (evt.button == 1)
            {
                ShowHeroItemPopup(current);
                return;
            }
            if (evt.button == 2)
                return;
            heroBagSelectedId = instanceId;
            heroItemsMessage = string.Empty;
            // Бриф правок п. 5: двойной клик надевает пригодную вещь в её
            // слот (прежняя — в сумку, тем же TryEquip с проверкой места).
            // Расходники и неподходящее двойной клик не трогает.
            if (evt.clickCount >= 2)
            {
                ItemDefinition currentDefinition = ItemCatalog.Find(current.ItemId);
                string personId = HeroItemsTargetPersonId;
                if (currentDefinition != null && currentDefinition.Slot != ItemSlot.None && !string.IsNullOrEmpty(personId))
                {
                    RunHeroCardCommand(ItemService.TryEquip(gameState, instanceId, personId, out string message), message);
                    return;
                }
            }
            RefreshHeroScreen();
        });
        return cell;
    }

    private static VisualElement CreateHeroBagEmptyCell()
    {
        VisualElement cell = new VisualElement();
        cell.AddToClassList("hero-bag-cell");
        cell.AddToClassList("hero-bag-cell--empty");
        return cell;
    }

    // Плитка припасов: тот же ArmySupply, что в походе и торговле; места
    // в сумке не занимает.
    private VisualElement CreateHeroSuppliesCell()
    {
        VisualElement cell = new VisualElement { name = "hero-bag-cell-supplies" };
        cell.AddToClassList("hero-bag-cell");
        cell.AddToClassList("hero-bag-cell--supplies");
        cell.EnableInClassList("hero-bag-cell--selected", heroBagSelectedId == HeroSuppliesCellId);
        Label caption = new Label("Припасы") { pickingMode = PickingMode.Ignore };
        caption.AddToClassList("hero-bag-cell-caption");
        cell.Add(caption);
        Label count = new Label(gameState.ArmySupply.ToString()) { pickingMode = PickingMode.Ignore };
        count.AddToClassList("hero-bag-cell-count");
        cell.Add(count);
        cell.tooltip = "Припасы отряда: " + gameState.ArmySupply + ". Места в сумке не занимают.";
        cell.RegisterCallback<PointerDownEvent>(evt =>
        {
            evt.StopPropagation();
            if (evt.button == 1)
            {
                ShowHeroSuppliesPopup();
                return;
            }
            heroBagSelectedId = HeroSuppliesCellId;
            RefreshHeroScreen();
        });
        return cell;
    }

    // Под сеткой — выбранная вещь: что это и что с ней можно сделать.
    private void RenderHeroBagDetails()
    {
        heroBagDetailsActions.Clear();
        if (heroBagSelectedId == HeroSuppliesCellId)
        {
            heroBagDetailsName.text = "Припасы отряда: " + gameState.ArmySupply;
            heroBagDetailsText.text = "Общий запас похода. Отряд ест " + gameState.ExpeditionSupplyConsumption +
                                      " в день. Купить и продать — у торговцев в поселениях; дома — панелью запасов.";
            return;
        }
        ItemInstanceData item = string.IsNullOrEmpty(heroBagSelectedId) ? null : ItemService.Find(gameState, heroBagSelectedId);
        ItemDefinition definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
        if (definition == null || item.Slot != ItemSlot.None)
        {
            heroBagSelectedId = null;
            heroBagDetailsName.text = "Выберите вещь";
            heroBagDetailsText.text = "Клик — выбрать, правая кнопка — подробные сведения.";
            return;
        }

        string targetId = HeroItemsTargetPersonId;
        heroBagDetailsName.text = definition.Name + (definition.HasUse ? "  ×" + item.UsesLeft : string.Empty);
        heroBagDetailsText.text = Join(HeroItemKindLabel(definition) + ". " + EffectText(definition), DescribeFit(definition, targetId));
        AddPersonItemActions(heroBagDetailsActions, item, definition, targetId);
        AddItemActions(heroBagDetailsActions, item, definition, !string.IsNullOrEmpty(item.OwnerPersonId));
    }

    private bool FitsHeroPerson(ItemDefinition definition, string personId)
    {
        if (string.IsNullOrEmpty(personId))
            return false;
        if (definition.Slot != ItemSlot.None)
            return ItemService.CanUseSlotItem(gameState, personId, definition, out _);
        // 12Т: вещь, которую дают человеку (лечение, отвар), подходит тому,
        // кому она сейчас поможет, — тем же расчётом, что и применение.
        if (definition.HasUse && ItemUseService.NeedsTarget(definition))
        {
            foreach (ItemInstanceData item in ItemService.Get(gameState).Items)
            {
                if (item.ItemId == definition.Id)
                    return ItemUseService.Preview(gameState, item, personId).CanUse;
            }
        }
        return false;
    }

    private static string HeroItemKindLabel(ItemDefinition definition)
    {
        switch (definition.Kind)
        {
            case ItemKind.Weapon: return "оружие";
            case ItemKind.Protection: return "защита";
            case ItemKind.Special: return definition.Artifact ? "особое, артефакт" : "особое";
            case ItemKind.Consumable: return "расходное";
            case ItemKind.TradeGood: return "товар";
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
        else if (definition.HasUse && ItemUseService.NeedsTarget(definition))
        {
            AddHeroItemButton(parent, definition.UseLabel, () =>
                RunHeroCardCommand(ItemService.TryUse(gameState, instanceId, personId, out string message), message))
                .AddToClassList("hero-item-button--primary");
        }
    }

    // Общие для отряда действия с вещью: переложить и использовать то, что
    // не требует выбрать человека.
    private void AddItemActions(VisualElement parent, ItemInstanceData item, ItemDefinition definition, bool inPack)
    {
        string instanceId = item.InstanceId;
        if (definition.HasUse && !ItemUseService.NeedsTarget(definition))
        {
            CommanderData commander = gameState.GetSelectedCommander();
            string commanderId = commander != null ? commander.Id : null;
            AddHeroItemButton(parent, definition.UseLabel, () =>
                RunHeroItemCommand(ItemService.TryUse(gameState, instanceId, commanderId, out string message), message));
        }

        bool home = !HomePeopleService.HasDeparted(gameState) && !IsFreePlayWithoutHome;
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

    // ------------------------------------------------------------------
    // Окно сведений о вещи (правая кнопка): всё, что задано в Базе
    // предметов, — вид, описание, происхождение, механика, применения, цена.
    // ------------------------------------------------------------------

    private void ShowHeroItemPopup(ItemInstanceData item)
    {
        ItemDefinition definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
        if (definition == null || heroItemPopup == null)
            return;
        SetHeroItemIcon(heroItemPopupIcon, definition);
        heroItemPopupIcon.style.display = DisplayStyle.Flex;
        heroItemPopupName.text = definition.Name;
        heroItemPopupKind.text = HeroItemKindLabel(definition) +
                                 (definition.Slot != ItemSlot.None ? " · слот: " + SlotCaption(definition.Slot) : string.Empty) +
                                 (definition.Unique ? " · одна на мир" : string.Empty);
        heroItemPopupText.text = string.Join("\n\n", ItemInfoLines(definition, item,
            definition.Price > 0 ? "Цена: " + definition.Price + " зол. (торговцы платят меньше)." : "Цены нет."));
        heroItemPopup.style.display = DisplayStyle.Flex;
        heroItemPopup.BringToFront();
    }

    // Бриф правок (19 пунктов) п. 6: одинаковые сведения о вещи в сумке,
    // торговле и по ПКМ — описание, «Свойства» (что даёт надетая вещь),
    // «Действие» только у вещи с применением (по её эффектам в данных, а не
    // по названию), затем цена. Пустых разделов нет.
    private static List<string> ItemInfoLines(ItemDefinition definition, ItemInstanceData item, string priceLine)
    {
        List<string> lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(definition.Description))
            lines.Add(definition.Description);
        if (!string.IsNullOrWhiteSpace(definition.Origin))
            lines.Add(definition.Origin);
        if (!definition.Modifier.IsZero)
            lines.Add("Свойства: " + definition.Modifier.Describe() + (definition.Slot != ItemSlot.None ? " — пока надета." : "."));
        if (definition.HasUse)
        {
            string mechanics = ItemUseService.Describe(definition);
            if (!string.IsNullOrEmpty(mechanics))
                lines.Add("Действие: " + mechanics + ".");
            if (item != null)
                lines.Add("Осталось применений: " + item.UsesLeft + " из " + definition.Uses + ".");
        }
        if (definition.UnitTypes != null && definition.UnitTypes.Length > 0)
            lines.Add("Подходит: " + string.Join(", ", definition.UnitTypes) + ".");
        if (!string.IsNullOrEmpty(priceLine))
            lines.Add(priceLine);
        if (definition.IsTradeProtected)
            lines.Add("Не продаётся и не теряется.");
        return lines;
    }

    private void ShowHeroSuppliesPopup()
    {
        if (heroItemPopup == null)
            return;
        SetHeroItemIcon(heroItemPopupIcon, null);
        heroItemPopupIcon.style.display = DisplayStyle.Flex;
        heroItemPopupName.text = "Припасы отряда";
        heroItemPopupKind.text = "общий запас похода";
        heroItemPopupText.text = "Сейчас: " + gameState.ArmySupply + ". " + DescribePartyResources(gameState, out _) +
                                 "\n\nКрупа, сало, сухари — едят все в походе. Места в сумке не занимают; покупаются и продаются у торговцев.";
        heroItemPopup.style.display = DisplayStyle.Flex;
        heroItemPopup.BringToFront();
    }

    private void HideHeroItemPopup()
    {
        if (heroItemPopup != null)
            heroItemPopup.style.display = DisplayStyle.None;
    }

    private static string SlotCaption(ItemSlot slot)
    {
        switch (slot)
        {
            case ItemSlot.Weapon: return "оружие";
            case ItemSlot.Protection: return "защита";
            default: return "особый предмет";
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

    // 12Т: механика вещи — тем же описанием, что в Базе предметов и в
    // магазине (ItemUseService.Describe). Вещи без механики — для историй и
    // проверок «вещь рядом»: показываем её описание.
    private static string EffectText(ItemDefinition definition)
    {
        string mechanics = ItemUseService.Describe(definition);
        if (mechanics.Length > 0)
            return mechanics;
        switch (definition.Id)
        {
            case ItemCatalog.RopeWithHooks: return "У брода и на подъёме — быстрее.";
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
