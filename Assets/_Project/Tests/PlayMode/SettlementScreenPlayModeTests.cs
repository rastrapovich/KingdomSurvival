using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// 12Т (SETTLEMENTS_TRADE_TAVERN_SPEC.md §16.2): экран поселения в настоящей
// сцене. Техническое торговое село (профиль из Базы поселений): обычный вход
// сразу открывает экран (бриф исправлений п. 3; время стоит) → торговля
// припасами (повторный клик не дублирует сделку, отмена корзины ничего не
// тратит) → корчма → платные сведения и сцена рассказа поверх экрана (п. 12)
// → возвращение в корчму → люди поселения (п. 14) → доска (чтение ничего не
// принимает) → выход без лагеря → повторный вход в то же место без траты
// времени (п. 4). Снимки 1920×1080 и 1280×720 — при заданной KS_SCREENSHOT_DIR.
public sealed class SettlementScreenPlayModeTests
{
    private const string MainScene = "Prototype_Main";
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

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

    private static MonoBehaviour FindBehaviour(string typeName)
    {
        return Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .FirstOrDefault(behaviour => behaviour.GetType().Name == typeName);
    }

    private static object Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, AnyInstance);
        Assert.IsNotNull(info, "Нет метода " + method);
        return info.Invoke(target, args);
    }

    private static T Get<T>(object target, string property)
    {
        return (T)target.GetType().GetProperty(property, AnyInstance).GetValue(target);
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static Button FindButton(VisualElement scope, string textStart)
    {
        Button button = scope.Query<Button>().ToList()
            .FirstOrDefault(b => b.resolvedStyle.display != DisplayStyle.None && b.text != null && b.text.StartsWith(textStart));
        Assert.IsNotNull(button, "Нет кнопки «" + textStart + "»: " +
                                 string.Join(" | ", scope.Query<Button>().ToList().Select(b => b.text)));
        return button;
    }

    private static IEnumerator Click(Button button)
    {
        Assert.IsTrue(button.enabledInHierarchy, "Кнопка «" + button.text + "» недоступна.");
        using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
        {
            submit.target = button;
            button.SendEvent(submit);
        }
        yield return Frames(2);
    }

    private static IEnumerator ClickElement(VisualElement element)
    {
        using (ClickEvent click = ClickEvent.GetPooled())
        {
            click.target = element;
            element.SendEvent(click);
        }
        yield return Frames(2);
    }

    private static IEnumerator Capture(MonoBehaviour main, string name, int width, int height)
    {
        string directory = System.Environment.GetEnvironmentVariable("KS_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory))
            yield break;
        PanelSettings settings = main.GetComponent<UIDocument>().panelSettings;
        RenderTexture previous = settings.targetTexture;
        RenderTexture texture = new RenderTexture(width, height, 24);
        settings.targetTexture = texture;
        yield return Frames(4);
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = texture;
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        RenderTexture.active = active;
        settings.targetTexture = previous;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + "_" + width + "x" + height + ".png"), image.EncodeToPNG());
        Object.Destroy(image);
        texture.Release();
        Object.Destroy(texture);
        yield return Frames(2);
    }

    private static IEnumerator CaptureBoth(MonoBehaviour main, string name)
    {
        yield return Capture(main, name, 1920, 1080);
        yield return Capture(main, name, 1280, 720);
    }

    [UnityTest]
    public IEnumerator Enter_Trade_Tavern_Board_Exit_InRealScene()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        Invoke(main, "StartNewFreePlayFromMenu");
        yield return Frames(10);

        GameState campaign = CampaignSession.Current;
        campaign.ArmySupply = 10;
        LocationData target = campaign.Locations.First(location => !location.IsWaypoint && location.IsVisibleOnMap);
        Assert.IsTrue(campaign.TryStartExpedition(target.Id, new List<string> { "garrick" }, out string message), message);
        campaign.ActiveExpedition.RouteIndex = 1;
        yield return null;

        Invoke(main, "DebugSettlementTrade");
        yield return Frames(3);
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        // Со зверем, которого внутрь не пустят, — окно места с лагерем снаружи.
        Assert.IsTrue(Get<bool>(main, "IsLocationInteractionActive"), "Кого-то не пускают — предложен лагерь.");
        Assert.IsFalse(Get<bool>(main, "IsSettlementScreenOpen"));
        Invoke(main, "CloseLocationInteraction");
        campaign.ActiveExpedition.FighterIds.Remove(SettlementCampDevFixture.CreatureId);
        yield return Frames(2);
        Invoke(main, "TryOpenLocationInteraction", SettlementCampDevFixture.SettlementId);
        yield return Frames(3);

        Assert.IsTrue(Get<bool>(main, "IsSettlementScreenOpen"), "Обычный вход — сразу экран поселения.");
        Assert.IsFalse(Get<bool>(main, "IsLocationInteractionActive"), "Окна с лагерем нет: никого не заворачивают.");
        Assert.IsFalse(SettlementCampService.HasCamp(campaign), "Лагерь не ставился.");
        Assert.IsTrue(SettlementCampService.IsCommanderInside(campaign));
        Assert.IsTrue(ContinuousSimulationSystem.IsPaused(campaign), "Просмотр поселения время не двигает.");
        VisualElement screen = root.Q("settlement-screen");
        yield return CaptureBoth(main, "settlement_01_main");

        // Торговля: две единицы припасов, повторный клик — без второй сделки.
        int gold = campaign.ArmyGold;
        int supply = campaign.ArmySupply;
        yield return Click(FindButton(screen.Q("settlement-actions"), "Лавка у торга"));
        VisualElement trade = root.Q("settlement-trade-page");
        Assert.AreEqual(DisplayStyle.Flex, trade.resolvedStyle.display);
        VisualElement suppliesRow = root.Q<ScrollView>("trade-merchant-list").Query(className: "trade-row").ToList()
            .First(row => row.Q<Label>(className: "trade-row-name").text.StartsWith("Припасы"));
        yield return ClickElement(suppliesRow);
        Assert.AreEqual("1", root.Q<Label>("trade-quantity").text, "Выбор ставит количество 1.");
        yield return Click(root.Q<Button>("trade-plus-button"));
        yield return CaptureBoth(main, "settlement_02_trade_cart");
        Assert.AreEqual(gold, campaign.ArmyGold, "Корзина — только намерение.");
        Button confirm = root.Q<Button>("trade-confirm-button");
        yield return Click(confirm);
        Assert.AreEqual(supply + 2, campaign.ArmySupply);
        int paid = gold - campaign.ArmyGold;
        Assert.Greater(paid, 0);
        Assert.IsFalse(confirm.enabledInHierarchy, "После сделки корзина пуста — повторить нечего.");
        TradeCart stale = new TradeCart { LocationId = SettlementCampDevFixture.SettlementId, MerchantId = "trade.store" };
        stale.SetQuantity(TradeLineKind.Supplies, string.Empty, 2);
        Assert.IsFalse(TradeService.TryConfirm(campaign, stale, paid, out _), "Старая ревизия не проходит.");
        Assert.AreEqual(supply + 2, campaign.ArmySupply);

        // Отмена корзины ничего не тратит.
        int goldAfter = campaign.ArmyGold;
        suppliesRow = root.Q<ScrollView>("trade-merchant-list").Query(className: "trade-row").ToList()
            .First(row => row.Q<Label>(className: "trade-row-name").text.StartsWith("Припасы"));
        yield return ClickElement(suppliesRow);
        yield return Click(root.Q<Button>("trade-cancel-button"));
        yield return Click(root.Q<Button>("settlement-confirm-yes"));
        Assert.AreEqual(goldAfter, campaign.ArmyGold);
        Assert.AreEqual(supply + 2, campaign.ArmySupply);
        yield return Click(root.Q<Button>("trade-back-button"));
        Assert.AreEqual(DisplayStyle.None, trade.resolvedStyle.display);

        // Корчма: платные сведения, сцена рассказа, возвращение в корчму.
        yield return Click(FindButton(screen.Q("settlement-actions"), "Корчма у торга"));
        Assert.AreEqual("Корчма у торга", root.Q<Label>("settlement-title").text);
        yield return CaptureBoth(main, "settlement_03_tavern");
        int beforeInfo = campaign.ArmyGold;
        yield return Click(FindButton(screen.Q("settlement-actions"), "Где дед прятал"));
        yield return Click(root.Q<Button>("settlement-confirm-yes"));
        yield return Frames(3);
        Assert.AreEqual(beforeInfo - 12, campaign.ArmyGold);
        Assert.IsTrue(campaign.Narrative.HasKnowledge(ItemCatalog.OldBedCacheKnowledge));
        Assert.IsTrue(Get<bool>(main, "IsNarrativeDialogueActive"), "Сцена рассказа открыта.");
        Assert.IsFalse(Get<bool>(main, "IsSettlementScreenOpen"), "Под разговором экран не принимает действий.");
        Assert.AreEqual(DisplayStyle.Flex, screen.resolvedStyle.display, "Разговор — поверх экрана поселения.");
        Assert.IsFalse(screen.enabledInHierarchy, "Кнопки экрана под разговором не нажимаются.");
        Assert.IsTrue(ContinuousSimulationSystem.IsPaused(campaign));
        Invoke(main, "CloseNarrativeDialogue");
        yield return Frames(4);
        Assert.IsTrue(Get<bool>(main, "IsSettlementScreenOpen"), "После сцены — снова корчма.");
        Assert.AreEqual("Корчма у торга", root.Q<Label>("settlement-title").text);
        Assert.IsTrue(ContinuousSimulationSystem.IsPaused(campaign), "Между сценой и корчмой время не пошло.");

        Assert.IsTrue(screen.enabledInHierarchy);

        // Люди поселения: полный список, возврат на главную страницу.
        yield return Click(FindButton(screen.Q("settlement-actions"), "Вернуться в поселение"));
        yield return Click(FindButton(screen.Q("settlement-actions"), "Люди поселения"));
        Assert.AreEqual("Люди поселения", root.Q<Label>("settlement-title").text);
        Assert.Greater(screen.Q("settlement-actions").Query<Button>().ToList().Count, 1, "В списке есть люди.");
        yield return CaptureBoth(main, "settlement_05_people");
        yield return Click(FindButton(screen.Q("settlement-actions"), "Назад"));

        // Доска: чтение ничего не принимает.
        yield return Click(FindButton(screen.Q("settlement-actions"), "Доска у торга"));
        VisualElement firstNotice = root.Q<ScrollView>("board-list").Query(className: "board-row").First();
        Assert.IsNotNull(firstNotice, "На доске есть объявления.");
        yield return ClickElement(firstNotice);
        yield return CaptureBoth(main, "settlement_04_board");
        Assert.IsFalse(campaign.Narrative.HasFlag("freeplay.cache.errand.accepted"), "Прочитать — не значит принять.");
        yield return Click(root.Q<Button>("board-back-button"));

        // Выход без лагеря: отряд стоит у поселения, окна места нет.
        yield return Click(root.Q<Button>("settlement-exit-button"));
        yield return Frames(2);
        Assert.IsFalse(SettlementCampService.IsCommanderInside(campaign));
        Assert.IsFalse(Get<bool>(main, "IsSettlementScreenOpen"));
        Assert.IsFalse(Get<bool>(main, "IsLocationInteractionActive"), "Без лагеря окна места нет.");

        // Снаряжение (бриф исправлений п. 2, 7, 8): Командир у куклы, сумка
        // сеткой с припасами и золотом на том же экране.
        Invoke(main, "OpenHeroScreen");
        yield return Frames(3);
        yield return Click(root.Q<Button>("hero-screen-tab-gear"));
        Assert.Greater(root.Q<VisualElement>("hero-screen-unit-card-available").childCount, 30, "Припасы и 30 ячеек сумки.");
        yield return CaptureBoth(main, "hero_gear");
        Invoke(main, "CloseHeroScreen");
        yield return Frames(3);

        // Повторный клик по тому же месту — снова экран, время не тратится.
        ContinuousClockSnapshot before = ContinuousSimulationSystem.GetClock(campaign);
        Invoke(main, "TryOpenLocationInteraction", SettlementCampDevFixture.SettlementId);
        yield return Frames(3);
        Assert.IsTrue(Get<bool>(main, "IsSettlementScreenOpen"), "Повторный вход открывает экран.");
        ContinuousClockSnapshot after = ContinuousSimulationSystem.GetClock(campaign);
        Assert.AreEqual(before.Day, after.Day);
        Assert.AreEqual(before.HourOfDay, after.HourOfDay, 1e-6, "Повторный вход времени не тратит.");
    }
}
