using System.Collections;
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

    private static void Invoke(MonoBehaviour controller, string method)
    {
        MethodInfo info = controller.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(info, "Нет метода " + method);
        info.Invoke(controller, null);
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

    // ПР-08: экран героя — слоты выбранного человека и «Надеть» из кладовой.
    [UnityTest]
    public IEnumerator HeroScreen_ShowsEquipment_AndEquipsFromStoreroom()
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
        Assert.AreEqual("Меч", root.Q<Label>("hero-screen-equipment-slot-1-name").text);
        StringAssert.DoesNotContain("позже", root.Q<Label>("hero-screen-states-hint").text);

        string garrick = campaign.FindFighter("garrick").Name;
        Button garrickChip = root.Q<VisualElement>("hero-screen-equipment-people").Query<Button>().ToList()
            .First(b => b.text == garrick);
        Click(garrickChip);
        yield return Frames(3);
        Assert.AreEqual("Кольчуга", root.Q<Label>("hero-screen-equipment-slot-2-name").text);

        Button equip = root.Q<VisualElement>("hero-screen-inventory-storage").Query<Button>().ToList()
            .First(b => b.text == "Надеть: " + garrick && b.parent.parent.Query<Label>().ToList().Any(l => l.text == "Кольчуга из клети"));
        Click(equip);
        yield return Frames(3);

        Assert.AreEqual(ItemCatalog.StoreroomMail, ItemService.Equipped(campaign, "garrick", ItemSlot.Protection).ItemId);
        Assert.AreEqual("Кольчуга из клети", root.Q<Label>("hero-screen-equipment-slot-2-name").text);
        Assert.AreEqual(CombatStatsAssembler.Compute(campaign, "garrick").Final.Defense.ToString(),
            root.Q<Label>("hero-screen-stat-defense-value").text, "Экран показывает собранные числа.");
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
        Assert.AreEqual(homeBefore, campaign.DailyFoodConsumption, "Подготовка не уводит людей из Дома.");

        Assert.IsTrue(root.Q<VisualElement>("home-people-list").Query<Label>().ToList()
            .Any(l => l.text.StartsWith("собирается в поход")), "Выбранные отмечены в списке жителей.");
    }
}
