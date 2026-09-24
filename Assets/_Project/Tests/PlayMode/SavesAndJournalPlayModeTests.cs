using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
