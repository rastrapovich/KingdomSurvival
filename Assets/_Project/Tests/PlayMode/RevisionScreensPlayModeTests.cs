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

// Бриф правок (22 пункта): наблюдаемый результат на настоящей сцене —
// журнал отдельным непрозрачным экраном без хода времени (п. 11), донесения
// сворачиваются (п. 12), подсказка значка карты (п. 3), темп ×1/×2/×3 не
// запускает часы у стоящего героя и ускоряет бег без удорожания (п. 18),
// страницы героя без служебных повторов (пп. 6–10). Снимки 1920×1080 и
// 1280×720 — при KS_SCREENSHOT_DIR.
public sealed class RevisionScreensPlayModeTests
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

    private static object Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, AnyInstance);
        Assert.IsNotNull(info, "Нет метода " + method);
        return info.Invoke(target, args);
    }

    private static T Get<T>(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name, AnyInstance);
        if (property != null)
            return (T)property.GetValue(target);
        return (T)target.GetType().GetField(name, AnyInstance).GetValue(target);
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static double Hours(GameState campaign)
    {
        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(campaign);
        return clock.Day * 24.0 + clock.HourOfDay;
    }

    private static IEnumerator Click(Button button)
    {
        using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
        {
            submit.target = button;
            button.SendEvent(submit);
        }
        yield return Frames(2);
    }

    private static IEnumerator Capture(MonoBehaviour main, string name)
    {
        string directory = System.Environment.GetEnvironmentVariable("KS_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory))
            yield break;
        foreach ((int width, int height) in new[] { (1920, 1080), (1280, 720) })
        {
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
    }

    [UnityTest]
    public IEnumerator FreePlay_JournalReportsTooltipTempoAndHeroPages()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .First(b => b.GetType().Name == "PrototypeUIController");
        Invoke(main, "StartNewFreePlayFromMenu");
        yield return Frames(30);
        GameState campaign = CampaignSession.Current;
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;

        // П. 19: старт — стоянка у дороги (запись Хроники), не поселение.
        Assert.IsNotNull(Chronicle.Find(campaign, "start.camp"), "Старт описан как лагерь.");
        Assert.IsFalse(Get<bool>(main, "IsSettlementScreenOpen"), "Пустое поселение не открывается.");
        yield return Capture(main, "rev_01_map_start");

        // П. 12: донесения свёрнуты в кнопку, раскрываются и сворачиваются.
        Button reports = root.Q<Button>("reports-toggle-button");
        Assert.IsNotNull(reports, "Есть кнопка донесений.");
        VisualElement dock = root.Q(className: "shell-reports-dock");
        Assert.AreEqual(DisplayStyle.None, dock.resolvedStyle.display, "Свёрнуто — места не занимает.");
        double hours = Hours(campaign);
        yield return Click(reports);
        Assert.AreEqual(DisplayStyle.Flex, dock.resolvedStyle.display, "Список раскрылся.");
        yield return Capture(main, "rev_02_reports_open");
        yield return Click(reports);
        Assert.AreEqual(DisplayStyle.None, dock.resolvedStyle.display, "Список свернулся.");
        Assert.AreEqual(hours, Hours(campaign), 1e-6, "Донесения время не трогают.");

        // П. 3: подсказка значка карты по наведению; уходит с курсором.
        // Бриф правок (19 пунктов) п. 1: в начале партии места за округой
        // старта под туманом — открываем клетку ближайшего, как путь героя.
        LocationData seen = campaign.Locations.First(l => !l.IsWaypoint && l.RevealedByFog);
        WorldMapFog.RevealAround(campaign, seen.MapXPercent, seen.MapYPercent);
        Invoke(main, "RefreshWorldMapPanel");
        yield return Frames(3);
        Assert.IsTrue(seen.IsVisibleOnMap, "Открытая клетка делает место известным.");
        Button node = root.Query<Button>(className: "world-map-node").ToList().First(n => n.resolvedStyle.display != DisplayStyle.None);
        string locationId = node.name.Substring("world-map-node-".Length);
        using (PointerEnterEvent enter = PointerEnterEvent.GetPooled())
        {
            enter.target = node;
            node.SendEvent(enter);
        }
        yield return Frames(2);
        VisualElement tooltip = root.Q("world-map-node-tooltip");
        Assert.IsNotNull(tooltip, "Подсказка создана.");
        Assert.AreEqual(DisplayStyle.Flex, tooltip.resolvedStyle.display);
        Assert.AreEqual(campaign.FindLocation(locationId).Name, tooltip.Q<Label>(className: "world-map-node-tooltip-title").text);
        Assert.AreEqual(PickingMode.Ignore, tooltip.pickingMode, "Подсказка не мешает кликнуть значок.");
        yield return Capture(main, "rev_03_map_tooltip");
        using (PointerLeaveEvent leave = PointerLeaveEvent.GetPooled())
        {
            leave.target = node;
            node.SendEvent(leave);
        }
        yield return Frames(2);
        Assert.AreEqual(DisplayStyle.None, tooltip.resolvedStyle.display, "Подсказка ушла с курсором.");
        Assert.IsFalse(campaign.HasActiveExpedition, "Наведение героя не двигает.");

        // П. 18: темп у стоящего героя часы не запускает.
        Button tempo3 = root.Q<Button>("map-tempo-3");
        Assert.IsNotNull(tempo3, "Есть переключатель темпа.");
        yield return Click(tempo3);
        Assert.IsTrue(tempo3.ClassListContains("map-tempo-button--active"), "Активный темп виден.");
        hours = Hours(campaign);
        yield return Frames(30);
        Assert.AreEqual(hours, Hours(campaign), 1e-6, "×3 на стоящем герое часы не запускает.");

        // П. 11: журнал — непрозрачный отдельный экран, время стоит.
        Invoke(main, "OpenJournal");
        yield return Frames(3);
        VisualElement journal = root.Q("journal-overlay");
        Assert.AreEqual(DisplayStyle.Flex, journal.resolvedStyle.display);
        Assert.AreEqual(1f, journal.resolvedStyle.backgroundColor.a, 0.001f, "Журнал непрозрачен — карта не просвечивает.");
        Assert.AreEqual(DisplayStyle.None, root.Q("journal-main-section").resolvedStyle.display, "«Основной цели» пока нет.");
        yield return Capture(main, "rev_04_journal");
        Invoke(main, "CloseJournal");
        yield return Frames(2);

        // П. 18: бег — те же часы на клетку, ×3 идёт втрое быстрее.
        campaign.ArmySupply = 40;
        LocationData far = campaign.Locations.Where(l => !l.IsWaypoint && (l.IsVisibleOnMap || l.RevealedByFog))
            .OrderByDescending(l => Mathf.Abs(l.MapXPercent - WorldMapNavigation.CapitalXPercent) + Mathf.Abs(l.MapYPercent - WorldMapNavigation.CapitalYPercent))
            .First();
        Assert.IsTrue(campaign.TryStartExpedition(far.Id, new List<string>(), out string message), message);
        yield return Click(root.Q<Button>("map-tempo-1"));
        yield return Frames(20);
        // Часы за клетку зависят от местности — окна ×1 и ×3 чередуются,
        // чтобы участок пути усреднился.
        double hours1 = 0, hours3 = 0, seconds1 = 0, seconds3 = 0;
        for (int pass = 0; pass < 8; pass++)
        {
            bool fast = pass % 2 == 1;
            yield return Click(root.Q<Button>(fast ? "map-tempo-3" : "map-tempo-1"));
            double startHours = Hours(campaign);
            float startTime = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startTime < 0.4f)
                yield return null;
            double gained = Hours(campaign) - startHours;
            double elapsed = Time.realtimeSinceStartup - startTime;
            if (fast) { hours3 += gained; seconds3 += elapsed; } else { hours1 += gained; seconds1 += elapsed; }
        }
        double rate1 = hours1 / seconds1, rate3 = hours3 / seconds3;
        Debug.Log("Темп карты: часов за секунду ×1 = " + rate1.ToString("F2") + ", ×3 = " + rate3.ToString("F2"));
        Assert.Greater(rate1, 0.0, "На ×1 часы идут, пока герой бежит.");
        Assert.That(rate3 / rate1, Is.InRange(2.0, 4.0), "×3 — втрое быстрее в реальных секундах.");
        yield return Click(root.Q<Button>("map-tempo-1"));

        // Бриф правок (19 пунктов) п. 19: кнопки нет, пока выбора нет; выбор
        // появился — кнопка с числом у портрета, окно само не открывается.
        CommanderData hero = campaign.GetSelectedCommander();
        Invoke(main, "OpenHeroScreen");
        yield return Frames(4);
        Assert.AreEqual(DisplayStyle.None, root.Q("hero-screen-level-up").resolvedStyle.display, "Без выбора кнопки нет.");
        Invoke(main, "CloseHeroScreen");
        yield return Frames(2);
        for (int i = 0; i < 400 && CharacterProgressionService.PendingChoices(campaign, hero.Id) < 2; i++)
            CharacterProgressionService.AwardExperience(campaign, hero.Id, "test.levelup." + i, 50);
        int pendingChoices = CharacterProgressionService.PendingChoices(campaign, hero.Id);
        Assert.GreaterOrEqual(pendingChoices, 2, "Набралось два выбора развития.");
        yield return Frames(4);
        Assert.IsFalse(Get<bool>(main, "IsHeroScreenOpen"), "Окно выбора само не открылось.");

        // Пп. 6–10: страницы героя.
        Invoke(main, "OpenHeroScreen");
        yield return Frames(4);
        Button levelUp = root.Q<Button>("hero-screen-level-up");
        Assert.AreEqual(DisplayStyle.Flex, levelUp.resolvedStyle.display, "Есть выбор — кнопка у портрета.");
        Assert.AreEqual("Повысить уровень (" + pendingChoices + ")", levelUp.text, "Несколько выборов — с числом.");
        Assert.AreEqual(DisplayStyle.None, root.Q("hero-screen-identity-title").resolvedStyle.display, "Служебного «КОМАНДИР» нет.");
        Assert.AreEqual(DisplayStyle.None, root.Q("hero-screen-state-phase-chip").resolvedStyle.display, "Статуса «В пути» нет.");
        Assert.AreEqual(DisplayStyle.None, root.Q("hero-screen-hero-gear").resolvedStyle.display, "Плашки «Снаряжение» нет.");
        Assert.AreEqual(DisplayStyle.None, root.Q("hero-screen-party-glance").resolvedStyle.display, "Плашки «Отряд» нет.");
        StringAssert.DoesNotContain("/", root.Q<Label>("hero-screen-quality-strength-value").text, "Качество — одним числом.");
        Assert.IsNotNull(root.Q("hero-screen-hero-states"), "Группа иконок состояний на главной странице.");
        yield return Capture(main, "rev_05_hero_page");

        yield return Click(root.Q<Button>("hero-screen-tab-squad"));
        Assert.AreEqual(DisplayStyle.None, root.Q("hero-screen-states").resolvedStyle.display, "Блока «Люди отряда» нет.");
        VisualElement rosterRow = root.Q("hero-screen-roster-row");
        Assert.AreEqual(2, rosterRow.IndexOf(root.Q("hero-screen-roster-commander")), "Командир — в центре пяти позиций.");
        Assert.AreEqual(campaign.ActiveExpedition.RetinueIds.Count, root.Q("hero-screen-retinue-portraits").childCount, "Портрет — на каждого в свите, пустых нет.");
        // Бриф правок (19 пунктов) п. 17: свита над рядом, ряд по центру,
        // командир крупнее, в свободной игре — без Лады и Остафия.
        VisualElement commanderCard = root.Q("hero-screen-roster-commander");
        VisualElement fighterCard = root.Q("hero-screen-roster-fighter-1");
        Assert.Less(root.Q("hero-screen-retinue").worldBound.yMax, rosterRow.worldBound.yMin + 1f, "Свита — над рядом позиций.");
        Assert.AreEqual(commanderCard.worldBound.center.y, fighterCard.worldBound.center.y, 2f, "Карточки — в одной полосе высоты.");
        Assert.That(commanderCard.worldBound.width / fighterCard.worldBound.width, Is.InRange(1.1f, 1.2f), "Командир крупнее примерно на 15%.");
        Rect row = rosterRow.worldBound;
        float leftGap = rosterRow.Children().Min(c => c.worldBound.xMin) - row.xMin;
        float rightGap = row.xMax - rosterRow.Children().Max(c => c.worldBound.xMax);
        Assert.AreEqual(leftGap, rightGap, 12f, "Ряд — по центру.");
        Assert.AreEqual(DisplayStyle.None, root.Q("hero-screen-retinue-row").resolvedStyle.display, "Выбора Лады и Остафия нет.");
        Assert.IsEmpty(campaign.ActiveExpedition.RetinueIds, "Свита новой свободной игры пуста.");
        yield return Capture(main, "rev_06_squad_page");

        yield return Click(root.Q<Button>("hero-screen-tab-gear"));
        Assert.AreEqual(DisplayStyle.Flex, root.Q("hero-screen-unit-level-up").resolvedStyle.display, "Кнопка и у портрета на карточке человека.");
        Assert.AreEqual(DisplayStyle.None, root.Q("hero-screen-unit-card-heading").resolvedStyle.display, "Подписи «Командир» в снаряжении нет.");
        Assert.AreEqual(DisplayStyle.None, root.Q("hero-screen-unit-card-level").resolvedStyle.display, "Уровня в снаряжении нет.");
        Assert.AreEqual(DisplayStyle.None, root.Q("hero-screen-inventory-story").resolvedStyle.display, "Подписи сюжетных вещей нет.");
        yield return Capture(main, "rev_07_gear_page");
        yield return Click(root.Q<Button>("hero-screen-unit-level-up"));
        Assert.IsTrue(root.Q("hero-screen-unit-card-development").ClassListContains("hero-screen-person-page--active"), "Кнопка ведёт к выбору развития.");
        Assert.AreEqual(pendingChoices, CharacterProgressionService.PendingChoices(campaign, hero.Id), "Выбор ждёт, пока его не сделают.");
        yield return Capture(main, "rev_07b_growth_choice");
        Invoke(main, "CloseHeroScreen");
        yield return Frames(2);

        // П. 15: событие из очереди — слоем диалога, а не отдельной табличкой;
        // «ПОНЯТНО» закрывает его прежней логикой.
        Invoke(main, "QueueNotice", new StrategicModalNotice
        {
            Title = "ПРОВЕРОЧНОЕ СОБЫТИЕ", Description = "У дороги лежит перевёрнутая телега.", Consequence = "Ничего не случилось."
        }, -1);
        Invoke(main, "TryShowNextQueuedModal");
        yield return Frames(3);
        VisualElement dialogueOverlay = root.Q("narrative-dialogue-overlay");
        Assert.AreEqual(DisplayStyle.Flex, dialogueOverlay.resolvedStyle.display, "Событие — в слое диалога.");
        Assert.AreEqual(DisplayStyle.None, root.Q("incident-modal-overlay").resolvedStyle.display, "Старой таблички нет.");
        Assert.AreEqual("ПРОВЕРОЧНОЕ СОБЫТИЕ", root.Q<Label>("narrative-dialogue-speaker").text);
        Button understood = root.Q("narrative-dialogue-choices").Query<Button>().ToList().Single();
        Assert.AreEqual("ПОНЯТНО", understood.text);
        yield return Capture(main, "rev_08_event_dialogue");
        yield return Click(understood);
        yield return Frames(2);
        Assert.AreEqual(DisplayStyle.None, dialogueOverlay.resolvedStyle.display, "Подтверждённое событие закрыто.");
        Assert.IsNull(Get<object>(main, "activeQueuedModal"), "Очередь событий пуста.");
    }
}
