using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// 12И (канон v1.50 §9): прямое управление героем на настоящей сцене —
// приказ с карты, фигура бежит, пунктира нет, камера держит героя, в
// свободной точке он останавливается и время встаёт. Контроллер — в сборке,
// недоступной тестам, поэтому доступ через рефлексию.
public sealed class WorldMapHeroPlayModeTests
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

    [UnityTest]
    public IEnumerator MapOrder_HeroRunsWithoutDottedRoute_CameraFollows_StopsSilently()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        for (int i = 0; i < 20; i++)
            yield return null;

        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        Invoke(main, "StartNewGameFromMenu");
        yield return null;

        GameState campaign = CampaignSession.Current;
        campaign.ArmySupply = 100;
        foreach (LocationData location in campaign.Locations)
            location.IsVisibleOnMap = true;
        SkipTodaysRandomChecks(campaign);

        OpenExpeditionsScreen(main);
        for (int i = 0; i < 10; i++)
            yield return null;

        // Цель — свободная точка в нескольких клетках от Дома.
        float targetX = WorldMapNavigation.CapitalXPercent + 6f;
        float targetY = WorldMapNavigation.CapitalYPercent - 4f;
        Invoke(main, "IssueContinuousMapOrder", targetX, targetY, null, false);
        Assert.IsTrue(campaign.HasActiveExpedition, "Клик по карте выводит отряд из Дома.");

        VisualElement root = ((UIDocument)main.GetComponent(typeof(UIDocument))).rootVisualElement;
        VisualElement viewport = root.Q<VisualElement>("world-map-viewport");
        Image hero = root.Q<Image>("world-map-hero-figure");
        VisualElement fallback = root.Q<VisualElement>("world-map-hero-fallback");
        Assert.IsNotNull(hero, "Фигура героя создана.");

        bool sawRunning = false;
        float until = Time.realtimeSinceStartup + 0.3f;
        while (Time.realtimeSinceStartup < until)
        {
            yield return null;
            sawRunning |= ContinuousSimulationSystem.IsExpeditionRunning(campaign);
        }

        Assert.IsTrue(sawRunning, "Отряд побежал сам, без подтверждения.");
        bool figureShown = hero.resolvedStyle.display == DisplayStyle.Flex && hero.sprite != null;
        bool fallbackShown = fallback != null && fallback.resolvedStyle.display == DisplayStyle.Flex;
        Assert.IsTrue(figureShown || fallbackShown, "Герой виден на карте.");
        Assert.AreEqual(0, viewport.Query<VisualElement>(className: "world-map-route-dot").ToList().Count,
            "Траектория не рисуется.");
        Assert.AreEqual(0, root.Q<VisualElement>("world-map").Query<VisualElement>(className: "world-map-route-dot").ToList().Count);

        until = Time.realtimeSinceStartup + 20f;
        while (Time.realtimeSinceStartup < until && campaign.ActiveExpedition.Phase == CommanderState.TravellingToLocation)
            yield return null;

        Assert.AreEqual(CommanderState.AtLocation, campaign.ActiveExpedition.Phase, "Отряд добежал до точки.");
        Assert.AreEqual(targetX, campaign.ActiveExpedition.CurrentMapXPercent, 0.05f);
        Assert.AreEqual(targetY, campaign.ActiveExpedition.CurrentMapYPercent, 0.05f);

        // Камера держит героя около центра viewport.
        until = Time.realtimeSinceStartup + 1.5f;
        while (Time.realtimeSinceStartup < until)
            yield return null;
        Vector2 heroOnScreen = HeroScreenPosition(main, campaign);
        Vector2 center = new Vector2(viewport.resolvedStyle.width, viewport.resolvedStyle.height) * 0.5f;
        Assert.Less(Vector2.Distance(heroOnScreen, center), Mathf.Min(center.x, center.y) * 0.25f,
            "Камера следует за героем.");

        // Стоящий в поле отряд время не тратит.
        double hour = ContinuousSimulationSystem.GetClock(campaign).HourOfDay;
        int day = campaign.Day;
        until = Time.realtimeSinceStartup + 0.5f;
        while (Time.realtimeSinceStartup < until)
            yield return null;
        Assert.AreEqual(day, campaign.Day);
        Assert.AreEqual(hour, ContinuousSimulationSystem.GetClock(campaign).HourOfDay, 0.0001, "Время стоит.");

        // Новый клик — отряд снова бежит от текущей позиции.
        Invoke(main, "IssueContinuousMapOrder", targetX + 3f, targetY, null, false);
        Assert.AreEqual(CommanderState.TravellingToLocation, campaign.ActiveExpedition.Phase);
        Assert.AreEqual(targetX, campaign.ActiveExpedition.Route[0].XPercent, 0.05f, "Путь от текущей позиции.");
    }

    private static void SkipTodaysRandomChecks(GameState campaign)
    {
        ContinuousSimulationSnapshotData clock = ContinuousSimulationSystem.ExportSnapshot(campaign);
        clock.ExpeditionIncidentChecked = true;
        clock.ExpeditionDecisionChecked = true;
        ContinuousSimulationSystem.RestoreSnapshot(campaign, clock);
    }

    private static Vector2 HeroScreenPosition(MonoBehaviour main, GameState campaign)
    {
        MethodInfo method = main.GetType().GetMethod("MapPercentToWorldMapViewport", AnyInstance);
        Assert.IsNotNull(method);
        return (Vector2)method.Invoke(main, new object[]
        {
            campaign.ActiveExpedition.CurrentMapXPercent,
            campaign.ActiveExpedition.CurrentMapYPercent
        });
    }

    private static void OpenExpeditionsScreen(MonoBehaviour main)
    {
        Type screenType = main.GetType().GetNestedType("MainScreen", BindingFlags.NonPublic);
        Assert.IsNotNull(screenType);
        object expeditions = Enum.Parse(screenType, "Expeditions");
        Invoke(main, "OpenScreen", expeditions);
    }

    private static MonoBehaviour FindBehaviour(string typeName)
    {
        return UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .FirstOrDefault(behaviour => behaviour.GetType().Name == typeName);
    }

    private static object Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, AnyInstance);
        Assert.IsNotNull(info, "Нет метода " + method);
        return info.Invoke(target, args);
    }
}
