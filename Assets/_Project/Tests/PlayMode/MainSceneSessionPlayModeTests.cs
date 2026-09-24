using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// ПР-01: дымовые проверки настоящей сцены Prototype_Main. Любая ошибка или
// исключение в консоли за время теста валит его (поведение Unity Test
// Framework по умолчанию) — так ловятся места, не готовые к запуску без
// кампании. PrototypeUIController живёт в Assembly-CSharp, недоступной из
// тестовой сборки, поэтому его методы вызываются через рефлексию — ровно
// те же, что вызывают кнопки меню.
public sealed class MainSceneSessionPlayModeTests
{
    private const string MainScene = "Prototype_Main";
    private const int SettleFrames = 30;

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        CampaignSession.Reset();
    }

    private static IEnumerator LoadMainScene()
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(MainScene, LoadSceneMode.Single);
        while (!load.isDone)
            yield return null;
        for (int i = 0; i < SettleFrames; i++)
            yield return null;
    }

    private static MonoBehaviour FindController()
    {
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (behaviour.GetType().Name == "PrototypeUIController")
                return behaviour;
        }

        Assert.Fail("PrototypeUIController не найден в сцене " + MainScene + ".");
        return null;
    }

    private static void Invoke(MonoBehaviour controller, string method)
    {
        MethodInfo info = controller.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(info, "Нет метода " + method);
        info.Invoke(controller, null);
    }

    private static bool IsOverlayOpen(MonoBehaviour controller, string name)
    {
        VisualElement root = controller.GetComponent<UIDocument>().rootVisualElement;
        VisualElement overlay = root.Q<VisualElement>(name);
        Assert.IsNotNull(overlay, name);
        return overlay.ClassListContains("game-menu-overlay--open");
    }

    [UnityTest]
    public IEnumerator Startup_WithoutCampaign_ShowsMainMenu_AndCreatesNothing()
    {
        yield return LoadMainScene();

        MonoBehaviour controller = FindController();
        Assert.IsFalse(CampaignSession.HasActive, "Показ интерфейса не должен создавать кампанию.");
        Assert.IsTrue(IsOverlayOpen(controller, "main-menu-overlay"));
    }

    [UnityTest]
    public IEnumerator NewGame_FromMenu_StartsCampaign_AndSurvivesSceneReload()
    {
        yield return LoadMainScene();
        MonoBehaviour controller = FindController();

        Invoke(controller, "StartNewGameFromMenu");
        for (int i = 0; i < SettleFrames; i++)
            yield return null;

        Assert.IsTrue(CampaignSession.HasActive);
        Assert.IsFalse(IsOverlayOpen(controller, "main-menu-overlay"));
        GameState campaign = CampaignSession.Current;
        int generation = CampaignSession.Generation;
        int day = campaign.Day;

        // Смена сцены (как будущий переход в бой и обратно) не создаёт
        // вторую кампанию: новая копия сцены подхватывает ту же.
        yield return LoadMainScene();
        MonoBehaviour reloaded = FindController();

        Assert.AreSame(campaign, CampaignSession.Current);
        Assert.AreEqual(generation, CampaignSession.Generation, "Возврат в сцену не должен начинать новую кампанию.");
        Assert.AreEqual(day, CampaignSession.Current.Day);
        Assert.IsFalse(IsOverlayOpen(reloaded, "main-menu-overlay"), "Идущая кампания подхватывается без главного меню.");
    }

    [UnityTest]
    public IEnumerator PauseMenu_StopsTime_AndMainMenuKeepsCampaign()
    {
        yield return LoadMainScene();
        MonoBehaviour controller = FindController();
        Invoke(controller, "StartNewGameFromMenu");
        for (int i = 0; i < SettleFrames; i++)
            yield return null;

        GameState campaign = CampaignSession.Current;

        Invoke(controller, "OpenPauseMenu");
        yield return null;
        Assert.IsTrue(IsOverlayOpen(controller, "pause-menu-overlay"));
        Assert.IsTrue(ContinuousSimulationSystem.IsPaused(campaign), "Пока открыта пауза, время стоит.");

        Invoke(controller, "OnPauseMenuMainMenuClicked");
        yield return null;
        Assert.IsTrue(IsOverlayOpen(controller, "main-menu-overlay"));
        Assert.AreSame(campaign, CampaignSession.Current, "Выход в главное меню не бросает кампанию.");

        Invoke(controller, "OnMainMenuContinueClicked");
        yield return null;
        Assert.IsFalse(IsOverlayOpen(controller, "main-menu-overlay"));
        Assert.AreSame(campaign, CampaignSession.Current);
    }
}
