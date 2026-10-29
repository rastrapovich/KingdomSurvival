using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// ПР-07А-2: панель «Подготовка похода» на настоящем экране Дома. Кнопки
// карточек меняют общий подготовленный состав (тот же, что читают экран
// героя и карта), прогноз «После выхода» показывает последствия, люди при
// этом остаются дома. Перетаскивание — те же команды; жесты мышью
// проверяются вручную.
public sealed class HomeScreenPlayModeTests
{
    private const string MainScene = "Prototype_Main";

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
        PlayModeSaveIsolation.Begin();
    }

    [TearDown]
    public void TearDown()
    {
        CampaignSession.Reset();
        PlayModeSaveIsolation.End();
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static MonoBehaviour FindController()
    {
        MonoBehaviour controller = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .FirstOrDefault(behaviour => behaviour.GetType().Name == "PrototypeUIController");
        Assert.IsNotNull(controller);
        return controller;
    }

    private static void Invoke(MonoBehaviour controller, string method, params object[] arguments)
    {
        MethodInfo info = controller.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(info, "Нет метода " + method);
        info.Invoke(controller, arguments.Length > 0 ? arguments : null);
    }

    // Вкладка экрана героя (вложенный закрытый enum контроллера).
    private static object HeroTab(MonoBehaviour controller, string name)
    {
        System.Type type = controller.GetType().GetNestedType("HeroScreenTab", BindingFlags.NonPublic);
        return System.Enum.Parse(type, name);
    }

    private static void Click(Button button)
    {
        using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
        {
            submit.target = button;
            button.SendEvent(submit);
        }
    }

    private static Button CandidateButton(VisualElement root, string personName, string buttonText)
    {
        VisualElement candidates = root.Q<VisualElement>("home-prep-candidates");
        VisualElement card = candidates.Query<VisualElement>(className: "home-prep-candidate").ToList()
            .FirstOrDefault(c => c.Query<Label>().ToList().Any(l => l.text == personName));
        Assert.IsNotNull(card, "Нет карточки кандидата " + personName);
        Button button = card.Query<Button>().ToList().FirstOrDefault(b => b.text == buttonText);
        Assert.IsNotNull(button, "Нет кнопки «" + buttonText + "» у " + personName);
        return button;
    }

    // ПР-09: лагерь в пути — «Встать на ночлег» запускает остановку на 8 ч и закрывает экран.
    [UnityTest]
    public IEnumerator Camp_RestButton_StartsNightStop()
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(MainScene, LoadSceneMode.Single);
        while (!load.isDone)
            yield return null;
        yield return Frames(20);

        MonoBehaviour controller = FindController();
        Invoke(controller, "StartNewGameFromMenu");
        yield return Frames(20);

        GameState campaign = CampaignSession.Current;
        campaign.ArmySupply = 50;
        LocationData target = campaign.Locations.First(location => !location.IsWaypoint);
        Assert.IsTrue(campaign.TryStartExpedition(target.Id, new List<string> { "garrick" }, out string message), message);
        campaign.ActiveExpedition.RouteIndex = 1;
        yield return Frames(3);

        // В главе лагерь открывается после сцены «После телеги».
        campaign.Narrative.SetFlag("chapter01.flag.camp_unlocked");
        Invoke(controller, "OpenCampScreen");
        yield return Frames(3);
        VisualElement root = controller.GetComponent<UIDocument>().rootVisualElement;
        Button rest = root.Q<Button>("camp-rest-button");
        Assert.IsTrue(rest.enabledSelf, root.Q<Label>("camp-actions-placeholder").text);
        StringAssert.Contains("Стоянка:", root.Q<Label>("camp-location-label").text);

        Click(rest);
        yield return Frames(3);
        Assert.IsTrue(CampRest.IsResting(campaign), "Отряд встал на ночлег.");
        Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("camp-screen").style.display.value);
    }

    // ПР-08 + карточка человека: экран героя — про весь отряд; слоты,
    // «Надеть» из кладовой и собранные числа — в карточке человека.
    [UnityTest]
    public IEnumerator HeroScreen_PersonCard_ShowsEquipment_AndEquipsFromStoreroom()
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(MainScene, LoadSceneMode.Single);
        while (!load.isDone)
            yield return null;
        yield return Frames(20);

        MonoBehaviour controller = FindController();
        Invoke(controller, "StartNewGameFromMenu");
        yield return Frames(20);
        Invoke(controller, "OpenHeroScreen");
        yield return Frames(5);

        GameState campaign = CampaignSession.Current;
        VisualElement root = controller.GetComponent<UIDocument>().rootVisualElement;
        StringAssert.DoesNotContain("позже", root.Q<Label>("hero-screen-states-hint").text);
        Assert.Greater(root.Q<VisualElement>("hero-screen-squad-list").childCount, 1, "Список отряда: Командир и бойцы.");
        Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("hero-screen-unit-card").resolvedStyle.display, "Карточка закрыта, пока её не открыли.");

        Invoke(controller, "ShowHeroPersonCard", campaign.GetSelectedCommander().Id);
        yield return Frames(3);
        Assert.AreEqual(DisplayStyle.Flex, root.Q<VisualElement>("hero-screen-unit-card").style.display.value);
        Assert.AreEqual("Меч", root.Q<Label>("hero-screen-equipment-slot-1-name").text);
        StringAssert.StartsWith("УР. ", root.Q<Label>("hero-screen-unit-card-level").text);

        // Бриф исправлений п. 8: «Снаряжение» — только Командир; бойцы — на «Развитии».
        string garrick = campaign.FindFighter("garrick").Name;
        Invoke(controller, "ShowHeroPersonOnTab", "garrick", HeroTab(controller, "Growth"));
        yield return Frames(3);
        Assert.AreEqual(garrick.ToUpperInvariant(), root.Q<Label>("hero-screen-unit-card-title").text);
        Assert.Greater(root.Q<VisualElement>("hero-screen-unit-card-competencies").childCount, 0, "Компетенции бойца в карточке.");

        Invoke(controller, "ShowHeroPersonCard", "garrick");
        Click(root.Q<Button>("hero-screen-tab-gear"));
        yield return Frames(3);
        string commander = campaign.GetSelectedCommander().Name;
        Assert.AreEqual(commander.ToUpperInvariant(), root.Q<Label>("hero-screen-unit-card-title").text, "На «Снаряжении» — Командир.");

        // Кольчуга из клети: ячейка кладовой → «Надеть» в сведениях под сеткой.
        ItemInstanceData mail = ItemService.Storage(campaign).First(i => i.ItemId == ItemCatalog.StoreroomMail);
        VisualElement cell = root.Q<VisualElement>("hero-bag-cell-" + mail.InstanceId);
        Assert.IsNotNull(cell, "Вещь кладовой — ячейкой сетки.");
        using (PointerDownEvent down = PointerDownEvent.GetPooled())
        {
            down.target = cell;
            cell.SendEvent(down);
        }
        yield return Frames(3);
        Button equip = root.Q<VisualElement>("hero-screen-bag-details-actions").Query<Button>().ToList().First(b => b.text == "Надеть");
        Click(equip);
        yield return Frames(3);

        string heroId = campaign.GetSelectedCommander().Id;
        Assert.AreEqual(ItemCatalog.StoreroomMail, ItemService.Equipped(campaign, heroId, ItemSlot.Protection).ItemId);
        Assert.AreEqual("Кольчуга из клети", root.Q<Label>("hero-screen-equipment-slot-2-name").text);
        Assert.AreEqual(CombatStatsAssembler.Compute(campaign, heroId).Final.Defense.ToString(),
            root.Q<Label>("hero-screen-unit-card-stat-defense-value").text, "Карточка показывает собранные числа.");

        // Бриф правок п. 5: двойной клик по снятой вещи надевает её обратно,
        // надетая уходит в сумку — без потерь и дублей.
        ItemInstanceData coat = campaign.Inventory.Items.First(i => i.Slot == ItemSlot.None && i.InstanceId != mail.InstanceId &&
            ItemCatalog.Find(i.ItemId)?.Slot == ItemSlot.Protection && root.Q<VisualElement>("hero-bag-cell-" + i.InstanceId) != null);
        int itemCount = campaign.Inventory.Items.Count;
        VisualElement coatCell = root.Q<VisualElement>("hero-bag-cell-" + coat.InstanceId);
        Assert.IsNotNull(coatCell, "Снятая защита — в сетке сумки.");
        Event doubleClick = new Event { type = EventType.MouseDown, button = 0, clickCount = 2, mousePosition = coatCell.worldBound.center };
        using (PointerDownEvent down = PointerDownEvent.GetPooled(doubleClick))
        {
            down.target = coatCell;
            coatCell.SendEvent(down);
        }
        yield return Frames(3);
        Assert.AreEqual(coat.InstanceId, ItemService.Equipped(campaign, heroId, ItemSlot.Protection).InstanceId, "Двойной клик надел вещь.");
        Assert.AreEqual(itemCount, campaign.Inventory.Items.Count, "Вещей не прибавилось и не убавилось.");
        Assert.AreEqual(ItemSlot.None, ItemService.Find(campaign, mail.InstanceId).Slot, "Прежняя защита снята.");
        Assert.AreEqual(CombatStatsAssembler.Compute(campaign, heroId).Final.Defense.ToString(),
            root.Q<Label>("hero-screen-unit-card-stat-defense-value").text, "Характеристики обновились сразу.");
    }

    // Экран героя на вкладках: каждая вкладка показывает свою страницу,
    // «Снаряжение» и «Развитие» — рабочее место человека; фильтры таблицы
    // отряда и кладовой сужают списки, порядок по имени — по алфавиту.
    [UnityTest]
    public IEnumerator HeroScreen_Tabs_SwitchPages_AndListsFilterAndSort()
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(MainScene, LoadSceneMode.Single);
        while (!load.isDone)
            yield return null;
        yield return Frames(20);

        MonoBehaviour controller = FindController();
        Invoke(controller, "StartNewGameFromMenu");
        yield return Frames(20);
        Invoke(controller, "OpenHeroScreen");
        yield return Frames(5);

        VisualElement root = controller.GetComponent<UIDocument>().rootVisualElement;
        VisualElement personPlace = root.Q<VisualElement>("hero-screen-unit-card");
        Assert.IsTrue(root.Q<VisualElement>("hero-screen-page-hero").ClassListContains("hero-screen-page--active"));
        Assert.AreEqual(DisplayStyle.None, personPlace.style.display.value);

        Click(root.Q<Button>("hero-screen-tab-squad"));
        yield return Frames(3);
        Assert.IsTrue(root.Q<VisualElement>("hero-screen-page-squad").ClassListContains("hero-screen-page--active"));
        Assert.IsFalse(root.Q<VisualElement>("hero-screen-page-hero").ClassListContains("hero-screen-page--active"));
        List<VisualElement> all = SquadRows(root);
        Assert.Greater(all.Count, 1, "Таблица отряда: Командир и бойцы.");

        Click(root.Q<Button>("hero-screen-squad-filter-roster"));
        yield return Frames(3);
        List<VisualElement> roster = SquadRows(root);
        Assert.Greater(roster.Count, 0);
        Assert.LessOrEqual(roster.Count, all.Count);
        Assert.IsTrue(roster.All(row => row.ClassListContains("hero-squad-row--commander") || row.ClassListContains("hero-squad-row--roster")),
            "Фильтр «В составе» — только Командир и бойцы похода.");

        Click(root.Q<Button>("hero-screen-squad-filter-all"));
        Click(root.Q<Button>("hero-screen-squad-sort-name"));
        yield return Frames(3);
        List<string> names = SquadRows(root).Select(row => row.Q<Label>(className: "hero-squad-name").text).ToList();
        CollectionAssert.AreEqual(names.OrderBy(n => n, System.StringComparer.CurrentCulture).ToList(), names, "Порядок «Имя».");

        Click(root.Q<Button>("hero-screen-tab-growth"));
        yield return Frames(3);
        Assert.AreEqual(DisplayStyle.Flex, personPlace.style.display.value);
        Assert.IsTrue(root.Q<VisualElement>("hero-screen-unit-card-development").ClassListContains("hero-screen-person-page--active"));
        Assert.Greater(root.Q<VisualElement>("hero-screen-people-rail-list").childCount, 1, "Полоса людей отряда.");

        Click(root.Q<Button>("hero-screen-tab-gear"));
        yield return Frames(3);
        Assert.IsTrue(root.Q<VisualElement>("hero-screen-unit-card-gear").ClassListContains("hero-screen-person-page--active"));
        Assert.IsTrue(personPlace.ClassListContains("hero-screen-unit-card--gear"), "На «Снаряжении» полоса людей скрыта.");
        VisualElement bag = root.Q<VisualElement>("hero-screen-unit-card-available");
        int slots = bag.Children().Count(c => c.ClassListContains("hero-bag-cell") && !c.ClassListContains("hero-bag-cell--supplies"));
        Assert.AreEqual(ItemService.HeroPackSize, slots, "Сумка — сетка на все места.");
        Assert.IsNotNull(bag.Q<VisualElement>("hero-bag-cell-supplies"), "Припасы видны в сумке.");
        Assert.IsTrue(root.Q<VisualElement>("hero-screen-storage-grid").Children().Any(c => c.tooltip.StartsWith("Кольчуга из клети")),
            "Дома видна кладовая.");

        // Переходы между вкладками — только вкладками: ни в таблице отряда,
        // ни на «Герое» нет кнопок, ведущих на другие вкладки.
        string[] links = { "Вещи", "Развитие", "Выбрать развитие" };
        Assert.IsFalse(root.Q<VisualElement>("hero-screen-squad-list").Query<Button>().ToList().Any(b => links.Contains(b.text)));
        Assert.AreEqual(0, root.Q<VisualElement>("hero-screen-page-hero").Query<Button>().ToList().Count,
            "На «Герое» кнопок нет: запасы перенесены на «Снаряжение».");

        Click(root.Q<Button>("hero-screen-tab-journey"));
        yield return Frames(3);
        Assert.IsTrue(root.Q<VisualElement>("hero-screen-page-journey").ClassListContains("hero-screen-page--active"));
        Assert.AreEqual(DisplayStyle.None, personPlace.style.display.value);
    }

    private static List<VisualElement> SquadRows(VisualElement root)
    {
        return root.Q<VisualElement>("hero-screen-squad-list").Children()
            .Where(child => child.ClassListContains("hero-squad-row")).ToList();
    }

    [UnityTest]
    public IEnumerator PrepPanel_Buttons_ChangeSharedRoster_AndForecast_PeopleStayHome()
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(MainScene, LoadSceneMode.Single);
        while (!load.isDone)
            yield return null;
        yield return Frames(20);

        MonoBehaviour controller = FindController();
        Invoke(controller, "StartNewGameFromMenu");
        yield return Frames(20);

        GameState campaign = CampaignSession.Current;
        Assert.IsNotNull(campaign);
        VisualElement root = controller.GetComponent<UIDocument>().rootVisualElement;
        int homeBefore = campaign.DailyFoodConsumption;

        string garrick = campaign.FindFighter("garrick").Name;
        Click(CandidateButton(root, garrick, "Взять в отряд"));
        yield return Frames(5);
        CollectionAssert.AreEqual(new[] { "garrick" }, ExpeditionPreparation.GetFighterIds(campaign).ToArray());

        Click(CandidateButton(root, "Лада", "Взять в свиту"));
        yield return Frames(5);
        Assert.AreEqual(HomePeopleService.LadaId, ExpeditionPreparation.GetRetinueId(campaign));

        VisualElement slots = root.Q<VisualElement>("home-prep-slots");
        Assert.IsTrue(slots.Query<Label>().ToList().Any(l => l.text == garrick), "Боец виден в месте состава.");
        Assert.IsTrue(slots.Query<Label>().ToList().Any(l => l.text == "Лада"), "Специалист виден в месте свиты.");

        string forecast = root.Q<Label>("home-prep-forecast").text;
        StringAssert.Contains("Ремонт продолжит Остафий — медленнее", forecast);
        // 12Е-9: что состав даёт в пути.
        StringAssert.Contains("\nВ пути:\n", forecast);
        StringAssert.Contains("Дозор — бойцы отряда", forecast);
        Assert.AreEqual(homeBefore, campaign.DailyFoodConsumption, "Подготовка не уводит людей из Дома.");

        Assert.IsTrue(root.Q<VisualElement>("home-people-list").Query<Label>().ToList()
            .Any(l => l.text.StartsWith("собирается в поход")), "Выбранные отмечены в списке жителей.");
    }
}
