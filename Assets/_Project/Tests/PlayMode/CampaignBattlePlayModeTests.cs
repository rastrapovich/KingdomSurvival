using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// ПР-03: полный круг через настоящие сцены — кампания в походе →
// BattleSandbox с её отрядом → исход → Prototype_Main с той же кампанией.
// Исход боя задаётся напрямую (HP юнитов), сам бой не разыгрывается:
// проверяется мост, а не тактика. Контроллеры обеих сцен — в сборках,
// недоступных тестам, поэтому доступ через рефлексию.
public sealed class CampaignBattlePlayModeTests
{
    private const string MainScene = "Prototype_Main";
    private const string BattleScene = "BattleSandbox";
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

    private static IEnumerator WaitForScene(string sceneName, int maxFrames = 600)
    {
        for (int i = 0; i < maxFrames && SceneManager.GetActiveScene().name != sceneName; i++)
            yield return null;
        Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name);
        for (int i = 0; i < 20; i++)
            yield return null;
    }

    private static object GetField(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        Assert.IsNotNull(field, "Нет поля " + name);
        return field.GetValue(target);
    }

    private static object Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, AnyInstance);
        Assert.IsNotNull(info, "Нет метода " + method);
        return info.Invoke(target, args);
    }

    private static IEnumerator StartCampaignInExpedition(int fighters)
    {
        SceneManager.LoadScene(MainScene);
        yield return WaitForScene(MainScene);

        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        Invoke(main, "StartNewGameFromMenu");
        yield return null;

        GameState campaign = CampaignSession.Current;
        campaign.ArmySupply = 100;
        LocationData target = campaign.Locations.First(location => !location.IsWaypoint);
        List<string> selected = campaign.Fighters.Take(fighters).Select(f => f.Id).ToList();
        Assert.IsTrue(campaign.TryStartExpedition(target.Id, selected, out string message), message);
        yield return null;
    }

    private static IEnumerator EnterBattle()
    {
        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        object[] args = { "test.battle", null };
        bool started = (bool)Invoke(main, "TryStartCampaignBattle", args);
        Assert.IsTrue(started, (string)args[1]);

        yield return WaitForScene(BattleScene);
        MonoBehaviour sandbox = FindBehaviour("BattleSandboxController");
        for (int i = 0; i < 60 && GetField(sandbox, "battle") == null; i++)
            yield return null;
        Assert.IsNotNull(GetField(sandbox, "battle"), "Бой кампании не начался сам.");
    }

    // Задаёт исход: убивает врагов (победа) и выбранных бойцов отряда.
    private static void ForceOutcome(MonoBehaviour sandbox, bool killEnemies, System.Func<string, bool> killPlayerUnit)
    {
        object battle = GetField(sandbox, "battle");
        IEnumerable units = (IEnumerable)battle.GetType().GetProperty("Units").GetValue(battle);
        foreach (object unit in units)
        {
            string team = unit.GetType().GetProperty("Team").GetValue(unit).ToString();
            string id = (string)unit.GetType().GetProperty("Id").GetValue(unit);
            bool kill = team == "Enemy" ? killEnemies : killPlayerUnit(id);
            if (kill)
                unit.GetType().GetProperty("HitPoints").GetSetMethod(true).Invoke(unit, new object[] { 0 });
        }

        Invoke(battle, "EvaluateBattleOutcome");
    }

    [UnityTest]
    public IEnumerator Victory_WithOneLoss_ReturnsToSameCampaign_FighterGoneForGood()
    {
        yield return StartCampaignInExpedition(2);
        GameState campaign = CampaignSession.Current;
        string fallenId = campaign.ActiveExpedition.FighterIds[1];
        int fightersBefore = campaign.Fighters.Count;
        int day = campaign.Day;
        double hour = ContinuousSimulationSystem.GetClock(campaign).HourOfDay;

        yield return EnterBattle();
        Assert.AreSame(campaign, CampaignSession.Current, "В бою кампания та же.");
        Assert.AreEqual(3, ((IEnumerable)GetField(FindBehaviour("BattleSandboxController"), "campaignParticipants")).Cast<object>().Count(),
            "В бою герой и два бойца похода.");

        MonoBehaviour sandbox = FindBehaviour("BattleSandboxController");
        // Юнит 3 — второй боец похода (порядок запроса: герой, бойцы).
        ForceOutcome(sandbox, killEnemies: true, killPlayerUnit: id => id.EndsWith(":3"));
        Invoke(sandbox, "ReturnToCampaign");

        yield return WaitForScene(MainScene);
        Assert.AreSame(campaign, CampaignSession.Current, "Вернулась та же кампания.");
        Assert.AreEqual(day, campaign.Day);
        Assert.GreaterOrEqual(ContinuousSimulationSystem.GetClock(campaign).HourOfDay, hour,
            "Время суток не сбрасывается на 08:00 после боя.");
        Assert.Less(ContinuousSimulationSystem.GetClock(campaign).HourOfDay - hour, 1.0,
            "Пока шёл бой, время кампании стояло.");
        Assert.IsTrue(campaign.HasActiveExpedition, "Поход продолжается.");
        Assert.AreEqual(fightersBefore - 1, campaign.Fighters.Count);
        Assert.IsFalse(campaign.Fighters.Any(f => f.Id == fallenId), "Павший погибает насовсем.");
        // ПР-06А: павший остаётся в реестре людей — погибшим, не удалённым.
        ResidentState fallen = HomePeopleService.Find(campaign, fallenId);
        Assert.IsNotNull(fallen);
        Assert.AreEqual(ResidentLifeStatus.Dead, fallen.LifeStatus);
        Assert.AreEqual(23, campaign.Population);
        ResidentState hero = HomePeopleService.Find(campaign, campaign.GetSelectedCommander().Id);
        Assert.IsTrue(hero.HasCombatState && hero.CurrentHitPoints > 0, "HP героя вернулись из боя в его запись.");
        Assert.IsNull(CampaignSession.TakeCompletedBattle(), "Итог уже применён.");
        Assert.IsTrue(CampaignBattleBridge.IsApplied(campaign, "test.battle"));
    }

    [UnityTest]
    public IEnumerator HeroFalls_SquadBroken_CampaignEnds_MainMenuOpen()
    {
        yield return StartCampaignInExpedition(1);

        yield return EnterBattle();
        MonoBehaviour sandbox = FindBehaviour("BattleSandboxController");
        ForceOutcome(sandbox, killEnemies: false, killPlayerUnit: id => true);
        Invoke(sandbox, "ReturnToCampaign");

        yield return WaitForScene(MainScene);
        Assert.IsFalse(CampaignSession.HasActive, "Отряд разбит — кампания закончена.");

        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        Assert.IsTrue(root.Q<VisualElement>("main-menu-overlay").ClassListContains("game-menu-overlay--open"));
        StringAssert.Contains("Отряд разбит", root.Q<Label>("main-menu-message").text);
    }
}
