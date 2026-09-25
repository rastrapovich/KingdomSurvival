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

// ПР-04 на настоящей сцене: Хроника не останавливает время, обязательное
// событие закрывает её и показывается одно; сохранение и загрузка по слотам;
// автосохранение перед боем.
public sealed class SavesAndJournalPlayModeTests
{
    private const string MainScene = "Prototype_Main";
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private string saveDirectory;

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
        saveDirectory = PlayModeSaveIsolation.Begin();
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

    private static MonoBehaviour Controller()
    {
        MonoBehaviour controller = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .FirstOrDefault(behaviour => behaviour.GetType().Name == "PrototypeUIController");
        Assert.IsNotNull(controller);
        return controller;
    }

    private static object Invoke(MonoBehaviour target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, AnyInstance);
        Assert.IsNotNull(info, "Нет метода " + method);
        return info.Invoke(target, args);
    }

    private static bool Flag(MonoBehaviour target, string property)
    {
        PropertyInfo info = target.GetType().GetProperty(property, AnyInstance);
        Assert.IsNotNull(info, "Нет свойства " + property);
        return (bool)info.GetValue(target);
    }

    private static IEnumerator NewGame()
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(MainScene);
        while (!load.isDone)
            yield return null;
        yield return Frames(20);
        Invoke(Controller(), "StartNewGameFromMenu");
    }

    private static void StartExpedition(GameState campaign, int fighters)
    {
        campaign.ArmySupply = 100;
        LocationData target = campaign.Locations.First(location => !location.IsWaypoint);
        List<string> selected = campaign.Fighters.Take(fighters).Select(f => f.Id).ToList();
        Assert.IsTrue(campaign.TryStartExpedition(target.Id, selected, out string message), message);
    }

    [UnityTest]
    public IEnumerator Journal_DoesNotStopTime_WhileTravelling()
    {
        yield return NewGame();
        GameState campaign = CampaignSession.Current;
        // Герой в походе — сцены Дома не открываются и не ставят паузу.
        StartExpedition(campaign, 1);
        yield return Frames(10);

        MonoBehaviour controller = Controller();
        Invoke(controller, "OpenJournal");
        Assert.IsTrue(Flag(controller, "IsJournalOpen"));
        double hourBefore = ContinuousSimulationSystem.GetClock(campaign).HourOfDay;
        int dayBefore = campaign.Day;

        yield return Frames(90);

        Assert.IsTrue(Flag(controller, "IsJournalOpen"), "Хроника остаётся открытой.");
        Assert.IsFalse(ContinuousSimulationSystem.IsPaused(campaign), "Открытая Хроника не останавливает время.");
        double hourAfter = ContinuousSimulationSystem.GetClock(campaign).HourOfDay;
        Assert.IsTrue(campaign.Day > dayBefore || hourAfter > hourBefore, "Время шло, пока Хроника была открыта.");
    }

    private static void Click(Button button)
    {
        using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
        {
            submit.target = button;
            button.SendEvent(submit);
        }
    }

    private static void PointerDown(VisualElement element)
    {
        Event systemEvent = new Event { type = EventType.MouseDown, button = 0, mousePosition = element.worldBound.center };
        using (PointerDownEvent down = PointerDownEvent.GetPooled(systemEvent))
        {
            down.target = element;
            element.SendEvent(down);
        }
    }

    private static List<string> RowTitles(VisualElement root) =>
        root.Q<VisualElement>("journal-entries-list").Query<Label>("journal-goal-row-title").ToList().Select(l => l.text).ToList();

    // ПР-11: журнал — «Дела / Сведения / История». Сведение несёт степень
    // уверенности и текст реплики-источника; запись истории — время; «НОВОЕ»
    // снимается кликом и сохраняется в кампании.
    [UnityTest]
    public IEnumerator Journal_KnowledgeAndHistoryTabs_ShowEntries_AndMarkSeen()
    {
        yield return NewGame();
        GameState campaign = CampaignSession.Current;
        StartExpedition(campaign, 1);
        campaign.Narrative.AddKnowledge("chapter01.knowledge.second_loaf_is_ration");
        Chronicle.Record(campaign, "test.history", "Проверка истории", "Запись для проверки.");
        yield return Frames(5);

        MonoBehaviour controller = Controller();
        VisualElement root = controller.GetComponent<UIDocument>().rootVisualElement;
        Invoke(controller, "OpenJournal");

        Click(root.Q<Button>("journal-tab-knowledge"));
        yield return null;
        Assert.AreEqual(DisplayStyle.Flex, root.Q<VisualElement>("journal-entries-section").resolvedStyle.display);
        Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("journal-main-section").resolvedStyle.display);
        CollectionAssert.Contains(RowTitles(root), "Второй хлеб — это паёк");

        VisualElement knowledgeRow = root.Q<VisualElement>("journal-entries-list").Q<VisualElement>("journal-goal-row");
        PointerDown(knowledgeRow);
        yield return null;
        StringAssert.Contains("СЛУХ", root.Q<Label>("journal-detail-panel-title").text);
        Assert.IsFalse(string.IsNullOrWhiteSpace(root.Q<Label>("journal-detail-description").text), "Текст сведения из реплики.");
        Assert.IsTrue(Chronicle.IsSeen(campaign, "knowledge:chapter01.knowledge.second_loaf_is_ration"));

        Click(root.Q<Button>("journal-tab-chronicle"));
        yield return null;
        CollectionAssert.Contains(RowTitles(root), "Проверка истории");
        VisualElement historyRow = root.Q<VisualElement>("journal-entries-list").Query<VisualElement>("journal-goal-row").ToList()
            .First(row => row.Q<Label>("journal-goal-row-title").text == "Проверка истории");
        Assert.AreEqual(DisplayStyle.Flex, historyRow.Q<Label>("journal-goal-row-badge").resolvedStyle.display, "Непрочитанная — «НОВОЕ».");
        PointerDown(historyRow);
        yield return null;
        Assert.AreEqual("Запись для проверки.", root.Q<Label>("journal-detail-description").text);
        Assert.IsTrue(Chronicle.IsSeen(campaign, "test.history"));
        Assert.AreEqual(DisplayStyle.None, root.Q<Button>("journal-detail-map-button").resolvedStyle.display, "Места у записи нет.");

        Click(root.Q<Button>("journal-tab-goals"));
        yield return null;
        Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("journal-entries-section").resolvedStyle.display);
    }

    // ПР-12А: свободная партия из меню — сцена N01 не открывается, «Дела» не
    // пусты, Дом не пуст, режим переживает сохранение и загрузку.
    [UnityTest]
    public IEnumerator FreePlay_FromMenu_NoChapterScene_GoalsAndHome_SurvivesSaveLoad()
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(MainScene);
        while (!load.isDone)
            yield return null;
        yield return Frames(20);
        MonoBehaviour controller = Controller();
        Invoke(controller, "StartNewFreePlayFromMenu");
        GameState campaign = CampaignSession.Current;
        Assert.AreEqual(CampaignStartOptions.FreePlayId, campaign.Configuration.CrisisId);

        // В сюжетной кампании N01 открывается через секунду — здесь ждём дольше.
        float deadline = Time.unscaledTime + 3f;
        while (Time.unscaledTime < deadline)
        {
            Assert.IsFalse(Flag(controller, "IsNarrativeDialogueActive"), "Сцена главы не открывается в свободной игре.");
            yield return null;
        }

        VisualElement root = controller.GetComponent<UIDocument>().rootVisualElement;
        Invoke(controller, "OpenJournal");
        yield return null;
        Assert.Greater(root.Q<VisualElement>("journal-main-section-list").childCount, 0, "«Дела» свободной игры не пусты.");
        Invoke(controller, "CloseJournal");

        Assert.IsTrue((bool)Invoke(controller, "SaveCampaign", "slot3"));
        Assert.IsTrue((bool)Invoke(controller, "LoadCampaign", "slot3"));
        yield return null;
        Assert.AreEqual(CampaignStartOptions.FreePlayId, CampaignSession.Current.Configuration.CrisisId, "Загрузка восстанавливает режим.");
        Assert.IsFalse(CampaignSession.Current.Narrative.Flags.Any(f => f.StartsWith("chapter01.")));
    }

    [UnityTest]
    public IEnumerator MandatoryScene_ClosesJournal_AndShowsAlone()
    {
        yield return NewGame();
        MonoBehaviour controller = Controller();

        // N01 откроется сам через секунду — Хроника открыта раньше.
        Invoke(controller, "OpenJournal");
        Assert.IsTrue(Flag(controller, "IsJournalOpen"));

        // Пауза перед сценой — по реальному времени, а кадры в batchmode
        // идут очень быстро: ждём секунды, не кадры.
        float deadline = Time.unscaledTime + 5f;
        while (Time.unscaledTime < deadline && !Flag(controller, "IsNarrativeDialogueActive"))
            yield return null;

        Assert.IsTrue(Flag(controller, "IsNarrativeDialogueActive"), "Обязательная сцена открылась поверх Хроники.");
        Assert.IsFalse(Flag(controller, "IsJournalOpen"), "Хроника закрылась — два окна сразу не показываются.");
    }

    [UnityTest]
    public IEnumerator SaveToSlot_ThenLoad_RestoresThatCampaign()
    {
        yield return NewGame();
        MonoBehaviour controller = Controller();
        GameState campaign = CampaignSession.Current;
        int savedDay = campaign.Day;

        Assert.IsTrue((bool)Invoke(controller, "SaveCampaign", "slot2"));
        Assert.IsTrue(File.Exists(Path.Combine(saveDirectory, "campaign.slot2.json")),
            "Сохранение пишется в тестовую папку, не в сохранения игрока.");

        campaign.Day = savedDay + 5;
        Assert.IsTrue((bool)Invoke(controller, "LoadCampaign", "slot2"));
        yield return null;

        Assert.AreNotSame(campaign, CampaignSession.Current, "Загрузка — новая кампания из файла.");
        Assert.AreEqual(savedDay, CampaignSession.Current.Day);
    }

    [UnityTest]
    public IEnumerator EnteringBattle_Autosaves_BeforeTheFight()
    {
        yield return NewGame();
        MonoBehaviour controller = Controller();
        StartExpedition(CampaignSession.Current, 1);
        yield return null;

        object[] args = { "test.autosave", null };
        Assert.IsTrue((bool)Invoke(controller, "TryStartCampaignBattle", args), (string)args[1]);
        Assert.IsTrue(File.Exists(Path.Combine(saveDirectory, "campaign.autosave.json")),
            "Перед боем — автосохранение.");

        // Сцена боя загружается; ждём, чтобы тест не оборвал её на полпути.
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != "BattleSandbox"; i++)
            yield return null;
        yield return Frames(20);
    }
}
