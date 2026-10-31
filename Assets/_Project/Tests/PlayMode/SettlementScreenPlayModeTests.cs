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

    // Абсолютные игровые минуты партии (бриф правок п. 14).
    private static double Minutes(GameState campaign)
    {
        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(campaign);
        return (clock.Day * 24.0 + clock.HourOfDay) * 60.0;
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

    // Бриф правок п. 4: двойной клик и правая кнопка — настоящими событиями
    // указателя с числом нажатий.
    private static IEnumerator PointerPress(VisualElement element, int button, int clicks)
    {
        IPanel panel = element.panel;
        Event down = new Event { type = EventType.MouseDown, button = button, clickCount = clicks, mousePosition = element.worldBound.center };
        using (PointerDownEvent evt = PointerDownEvent.GetPooled(down))
        {
            evt.target = element;
            element.SendEvent(evt);
        }
        // Бриф правок (19 пунктов) п. 4: настоящий клик — нажатие и отпускание
        // (строки торговли ждут отпускания, чтобы отличить клик от перетаскивания).
        Event up = new Event { type = EventType.MouseUp, button = button, clickCount = clicks, mousePosition = element.worldBound.center };
        using (PointerUpEvent evt = PointerUpEvent.GetPooled(up))
        {
            evt.target = element;
            panel.visualTree.SendEvent(evt);
        }
        yield return Frames(2);
    }

    // Перетаскивание указателем: нажатие на строке, движение, отпускание над целью.
    private static IEnumerator PointerDrag(VisualElement from, Vector2 to)
    {
        Vector2 start = from.worldBound.center;
        IPanel panel = from.panel;
        using (PointerDownEvent evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, clickCount = 1, mousePosition = start }))
        {
            evt.target = from;
            from.SendEvent(evt);
        }
        yield return null;
        foreach (float t in new[] { 0.1f, 0.5f, 1f })
        {
            Vector2 point = Vector2.Lerp(start, to, t);
            using (PointerMoveEvent evt = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = point }))
                panel.visualTree.SendEvent(evt);
            yield return null;
        }
        using (PointerUpEvent evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, clickCount = 1, mousePosition = to }))
            panel.visualTree.SendEvent(evt);
        yield return Frames(2);
    }

    private static VisualElement TradeRow(VisualElement root, string list, string nameStart)
    {
        return root.Q<ScrollView>(list).Query(className: "trade-row").ToList()
            .First(row => row.Q<Label>(className: "trade-row-name").text.StartsWith(nameStart));
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
        Invoke(main, "StartNewGameFromMenu");
        yield return Frames(10);

        GameState campaign = CampaignSession.Current;
        TestPeople.AdmitAll(campaign);
        campaign.ArmySupply = 10;
        LocationData target = campaign.Locations.First(location => !location.IsWaypoint && (location.IsVisibleOnMap || location.RevealedByFog));
        Assert.IsTrue(campaign.TryStartExpedition(target.Id, new List<string> { "garrick" }, out string message), message);
        campaign.ActiveExpedition.RouteIndex = 1;
        // Без кадра между стартом похода и отладочным входом: иначе отряд
        // успевает пробежать и открыть дорожную встречу (нестабильно в полном прогоне).

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
        double minutes = Minutes(campaign);
        yield return Frames(30);
        Assert.AreEqual(minutes, Minutes(campaign), 0.01, "Стоя на экране поселения, время не идёт.");
        yield return Click(FindButton(screen.Q("settlement-actions"), "Лавка у торга"));
        VisualElement trade = root.Q("settlement-trade-page");
        Assert.AreEqual(DisplayStyle.Flex, trade.resolvedStyle.display);
        // Одиночный клик только подсвечивает; двойной — единица в сделку.
        yield return PointerPress(TradeRow(root, "trade-merchant-list", "Припасы"), 0, 1);
        Assert.AreEqual(0, root.Q<ScrollView>("trade-buy-offer").Query(className: "trade-row").ToList().Count, "Клик ничего не кладёт в сделку.");
        yield return PointerPress(TradeRow(root, "trade-merchant-list", "Припасы"), 0, 2);
        yield return PointerPress(TradeRow(root, "trade-merchant-list", "Припасы"), 0, 2);
        yield return PointerPress(TradeRow(root, "trade-merchant-list", "Припасы"), 0, 2);
        Assert.AreEqual("Припасы в дорогу ×3", root.Q<ScrollView>("trade-buy-offer").Q<Label>(className: "trade-row-name").text);
        // Двойной клик по строке сделки возвращает одну единицу.
        yield return PointerPress(root.Q<ScrollView>("trade-buy-offer").Q(className: "trade-row"), 0, 2);
        Assert.AreEqual("Припасы в дорогу ×2", root.Q<ScrollView>("trade-buy-offer").Q<Label>(className: "trade-row-name").text);
        StringAssert.StartsWith("Доплатить ", root.Q<Label>("trade-net").text);
        // Правая кнопка — сведения, без переноса.
        yield return PointerPress(TradeRow(root, "trade-merchant-list", "Припасы"), 1, 1);
        Assert.AreEqual(DisplayStyle.Flex, root.Q("trade-item-popup").resolvedStyle.display, "ПКМ открывает сведения.");
        Assert.AreEqual("Припасы в дорогу ×2", root.Q<ScrollView>("trade-buy-offer").Q<Label>(className: "trade-row-name").text, "ПКМ ничего не переносит.");
        yield return CaptureBoth(main, "settlement_02b_trade_popup");
        yield return Click(root.Q<Button>("trade-item-popup-close"));
        Assert.AreEqual(DisplayStyle.None, root.Q("trade-item-popup").resolvedStyle.display);
        yield return CaptureBoth(main, "settlement_02_trade_cart");
        Assert.AreEqual(gold, campaign.ArmyGold, "Корзина — только намерение.");
        Assert.AreEqual(minutes, Minutes(campaign), 0.01, "Просмотр товара и корзина времени не тратят.");
        Button confirm = root.Q<Button>("trade-confirm-button");
        yield return Click(confirm);
        Assert.AreEqual(supply + 2, campaign.ArmySupply);
        Assert.AreEqual(minutes + SettlementCatalog.Current.ActionMinutes, Minutes(campaign), 0.01, "Подтверждённая сделка — один шаг часов.");
        minutes = Minutes(campaign);
        int paid = gold - campaign.ArmyGold;
        Assert.Greater(paid, 0);
        Assert.IsFalse(confirm.enabledInHierarchy, "После сделки корзина пуста — повторить нечего.");
        TradeCart stale = new TradeCart { LocationId = SettlementCampDevFixture.SettlementId, MerchantId = "trade.store" };
        stale.SetQuantity(TradeLineKind.Supplies, string.Empty, 2);
        Assert.IsFalse(TradeService.TryConfirm(campaign, stale, paid, out _), "Старая ревизия не проходит.");
        Assert.AreEqual(supply + 2, campaign.ArmySupply);

        // Смешанный обмен: покупка вещи и продажа припасов — один итог,
        // одна сделка, один шаг часов.
        VisualElement goodsRow = root.Q<ScrollView>("trade-merchant-list").Query(className: "trade-row").ToList()
            .First(row => !row.Q<Label>(className: "trade-row-name").text.StartsWith("Припасы") && !row.ClassListContains("trade-row--disabled"));
        yield return PointerPress(goodsRow, 0, 2);
        yield return PointerPress(TradeRow(root, "trade-party-list", "Припасы отряда"), 0, 2);
        Assert.AreEqual(1, root.Q<ScrollView>("trade-buy-offer").Query(className: "trade-row").ToList().Count);
        Assert.AreEqual(1, root.Q<ScrollView>("trade-sell-offer").Query(className: "trade-row").ToList().Count);
        int goldMixed = campaign.ArmyGold;
        int supplyMixed = campaign.ArmySupply;
        string net = root.Q<Label>("trade-net").text;
        yield return CaptureBoth(main, "settlement_02c_trade_mixed");
        yield return Click(confirm);
        int change = campaign.ArmyGold - goldMixed;
        Assert.AreEqual(supplyMixed - 1, campaign.ArmySupply, "Продана единица припасов.");
        Assert.AreEqual(change < 0 ? "Доплатить " + (-change) + " золота" : change > 0 ? "Получить " + change + " золота" : "Без доплаты", net,
            "Итог на экране совпал с применённым.");
        Assert.AreEqual(minutes + SettlementCatalog.Current.ActionMinutes, Minutes(campaign), 0.01, "Смешанная сделка — один шаг часов.");
        minutes = Minutes(campaign);

        // Отмена ничего не тратит.
        int goldAfter = campaign.ArmyGold;
        int supplyAfter = campaign.ArmySupply;
        yield return PointerPress(TradeRow(root, "trade-merchant-list", "Припасы"), 0, 2);
        yield return Click(root.Q<Button>("trade-cancel-button"));
        yield return Click(root.Q<Button>("settlement-confirm-yes"));
        Assert.AreEqual(goldAfter, campaign.ArmyGold);
        Assert.AreEqual(supplyAfter, campaign.ArmySupply);
        Assert.AreEqual(0, root.Q<ScrollView>("trade-buy-offer").Query(className: "trade-row").ToList().Count, "Отменённая сделка пуста.");
        Assert.AreEqual(minutes, Minutes(campaign), 0.01, "Отмена сделки времени не тратит.");
        // Бриф правок (19 пунктов) п. 4: торговец слева, вещи героя справа,
        // в центре рядом «Вы покупаете» и «Вы продаёте»; перетаскивание.
        VisualElement buyZone = root.Q("trade-buy-zone");
        VisualElement sellZone = root.Q("trade-sell-zone");
        Assert.Less(root.Q("trade-merchant-list").worldBound.xMax, buyZone.worldBound.xMin + 1f, "Товар торговца — слева.");
        Assert.Less(buyZone.worldBound.xMax, sellZone.worldBound.xMin + 1f, "«Вы покупаете» и «Вы продаёте» — рядом.");
        Assert.Less(sellZone.worldBound.xMax, root.Q("trade-party-list").worldBound.xMin + 1f, "Вещи героя — справа.");
        yield return PointerDrag(TradeRow(root, "trade-merchant-list", "Припасы"), buyZone.worldBound.center);
        Assert.AreEqual(1, root.Q<ScrollView>("trade-buy-offer").Query(className: "trade-row").ToList().Count, "Перетащенный товар — в покупках.");
        yield return PointerDrag(TradeRow(root, "trade-party-list", "Припасы отряда"), sellZone.worldBound.center);
        Assert.AreEqual(1, root.Q<ScrollView>("trade-sell-offer").Query(className: "trade-row").ToList().Count, "Своя вещь — в продажах.");
        yield return PointerDrag(TradeRow(root, "trade-merchant-list", "Припасы"), sellZone.worldBound.center);
        Assert.AreEqual("Припасы в дорогу", root.Q<ScrollView>("trade-buy-offer").Q<Label>(className: "trade-row-name").text,
            "Товар торговца в «Вы продаёте» не кладётся.");
        yield return CaptureBoth(main, "settlement_02d_trade_drag");
        // Правая кнопка — сведения и в частях сделки.
        yield return PointerPress(root.Q<ScrollView>("trade-sell-offer").Q(className: "trade-row"), 1, 1);
        Assert.AreEqual(DisplayStyle.Flex, root.Q("trade-item-popup").resolvedStyle.display, "ПКМ в «Вы продаёте» — сведения.");
        yield return Click(root.Q<Button>("trade-item-popup-close"));
        // Вытащить строку из сделки — вернуть.
        yield return PointerDrag(root.Q<ScrollView>("trade-buy-offer").Q(className: "trade-row"), root.Q("trade-merchant-list").worldBound.center);
        yield return PointerDrag(root.Q<ScrollView>("trade-sell-offer").Q(className: "trade-row"), root.Q("trade-party-list").worldBound.center);
        Assert.AreEqual(0, root.Q<ScrollView>("trade-buy-offer").Query(className: "trade-row").ToList().Count, "Вытащенная строка вернулась.");
        Assert.AreEqual(0, root.Q<ScrollView>("trade-sell-offer").Query(className: "trade-row").ToList().Count);
        Assert.AreEqual(goldAfter, campaign.ArmyGold, "Перетаскивание — только намерение.");
        Assert.AreEqual(minutes, Minutes(campaign), 0.01, "Перетаскивание времени не тратит.");

        yield return Click(root.Q<Button>("trade-back-button"));
        Assert.AreEqual(DisplayStyle.None, trade.resolvedStyle.display);

        // Корчма: платные сведения, сцена рассказа, возвращение в корчму.
        yield return Click(FindButton(screen.Q("settlement-actions"), "Корчма у торга"));
        Assert.AreEqual("Корчма у торга", root.Q<Label>("settlement-title").text);
        yield return CaptureBoth(main, "settlement_03_tavern");
        int beforeInfo = campaign.ArmyGold;
        minutes = Minutes(campaign);
        yield return Click(FindButton(screen.Q("settlement-actions"), "Где дед прятал"));
        yield return Click(root.Q<Button>("settlement-confirm-yes"));
        yield return Frames(3);
        Assert.AreEqual(beforeInfo - 12, campaign.ArmyGold);
        Assert.IsTrue(campaign.Narrative.HasKnowledge(ItemCatalog.OldBedCacheKnowledge));
        Assert.IsTrue(Get<bool>(main, "IsNarrativeDialogueActive"), "Сцена рассказа открыта.");
        Assert.IsFalse(Get<bool>(main, "IsSettlementScreenOpen"), "Под разговором экран не принимает действий.");
        Assert.AreEqual(DisplayStyle.Flex, screen.resolvedStyle.display, "Разговор — поверх экрана поселения.");
        // Город виден (экран непрозрачен), но клик по его кнопке забирает
        // слой разговора.
        Assert.AreEqual(1f, screen.resolvedStyle.opacity, 0.001f, "Экран поселения под разговором непрозрачен.");
        Button underDialogue = screen.Q("settlement-actions").Query<Button>().First();
        VisualElement picked = screen.panel.Pick(underDialogue.worldBound.center);
        Assert.IsFalse(picked == underDialogue || underDialogue.Contains(picked), "Кнопка поселения под разговором не нажимается.");
        yield return CaptureBoth(main, "settlement_06_dialogue_over_city");
        Assert.IsTrue(ContinuousSimulationSystem.IsPaused(campaign));
        Invoke(main, "CloseNarrativeDialogue");
        yield return Frames(4);
        Assert.IsTrue(Get<bool>(main, "IsSettlementScreenOpen"), "После сцены — снова корчма.");
        Assert.AreEqual("Корчма у торга", root.Q<Label>("settlement-title").text);
        Assert.IsTrue(ContinuousSimulationSystem.IsPaused(campaign), "Между сценой и корчмой время не пошло.");
        Assert.AreEqual(minutes + SettlementCatalog.Current.ActionMinutes, Minutes(campaign), 0.01, "Завершённый разговор — один шаг часов.");
        yield return Frames(20);
        Assert.AreEqual(minutes + SettlementCatalog.Current.ActionMinutes, Minutes(campaign), 0.01, "После шага часы снова стоят.");


        // Люди поселения: полный список, возврат на главную страницу.
        yield return Click(FindButton(screen.Q("settlement-actions"), "Вернуться в поселение"));
        yield return Click(FindButton(screen.Q("settlement-actions"), "Люди поселения"));
        Assert.AreEqual("Люди поселения", root.Q<Label>("settlement-title").text);
        // Бриф правок п. 2: люди — плашками в основной области, в левом
        // столбце только «Назад»; разговор из списка возвращает в список.
        VisualElement gallery = root.Q("settlement-people-gallery");
        Assert.AreEqual(DisplayStyle.Flex, gallery.resolvedStyle.display);
        Assert.Greater(gallery.childCount, 0, "В основной области есть люди.");
        Assert.AreEqual(1, screen.Q("settlement-actions").Query<Button>().ToList().Count, "В столбце — только «Назад».");
        Assert.Greater(gallery.worldBound.xMin, root.Q("settlement-panel").worldBound.xMax - 1f, "Плашки — справа от панели, над городом.");
        yield return CaptureBoth(main, "settlement_05_people");
        // Бриф правок (19 пунктов) п. 3: компактные строки по вертикали у
        // левого края — портрет слева, имя справа, описание под именем.
        List<VisualElement> rows = gallery.Children().ToList();
        VisualElement firstRow = rows[0];
        Assert.AreEqual(gallery.worldBound.xMin, firstRow.worldBound.xMin, 2f, "Строки — у левого края.");
        Assert.LessOrEqual(firstRow.Q(className: "settlement-person-portrait").worldBound.xMax,
            firstRow.Q<Label>(className: "settlement-person-name").worldBound.xMin, "Портрет слева от имени.");
        Assert.Greater(firstRow.Q<Label>(className: "settlement-person-role").worldBound.yMin,
            firstRow.Q<Label>(className: "settlement-person-name").worldBound.yMin, "Описание под именем.");
        if (rows.Count > 1)
            Assert.Greater(rows[1].worldBound.yMin, firstRow.worldBound.yMin, "Строки идут по вертикали.");

        // Бриф правок (19 пунктов) п. 2: торговец — разговор, «Покажи свой
        // товар» — торговля, «Назад» — снова разговор.
        VisualElement trader = rows.First(c => c.tooltip == "Клик — поговорить с торговцем.");
        double talkMinutes = Minutes(campaign);
        yield return ClickElement(trader);
        Assert.IsTrue(Get<bool>(main, "IsSettlementTalkOpen"), "Клик по торговцу — разговор.");
        Assert.AreEqual(DisplayStyle.None, trade.resolvedStyle.display, "Торговля сама не открылась.");
        yield return CaptureBoth(main, "settlement_05b_merchant_talk");
        yield return Click(root.Q<Button>("merchant-talk-trade"));
        Assert.AreEqual(DisplayStyle.Flex, trade.resolvedStyle.display, "«Покажи свой товар» — торговля.");
        Assert.IsFalse(Get<bool>(main, "IsSettlementTalkOpen"));
        yield return Click(root.Q<Button>("trade-back-button"));
        Assert.AreEqual(DisplayStyle.None, trade.resolvedStyle.display);
        Assert.IsTrue(Get<bool>(main, "IsSettlementTalkOpen"), "После торговли — снова разговор с торговцем.");
        yield return Click(root.Q<Button>("merchant-talk-leave"));
        Assert.IsFalse(Get<bool>(main, "IsSettlementTalkOpen"));
        Assert.AreEqual("Люди поселения", root.Q<Label>("settlement-title").text, "Без петель — назад к списку.");
        Assert.AreEqual(talkMinutes, Minutes(campaign), 0.01, "Разговор и просмотр товара времени не тратят.");

        // Список перерисован после торговли — строки берутся заново.
        VisualElement talker = gallery.Children().First(c => c.tooltip == "Клик — поговорить.");
        yield return ClickElement(talker);
        yield return Frames(3);
        Assert.IsTrue(Get<bool>(main, "IsNarrativeDialogueActive"), "Разговор из общего списка открылся.");
        Assert.AreEqual(DisplayStyle.Flex, screen.resolvedStyle.display, "Под разговором — город.");
        Invoke(main, "CloseNarrativeDialogue");
        yield return Frames(4);
        Assert.AreEqual("Люди поселения", root.Q<Label>("settlement-title").text, "После разговора — снова список людей.");
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
