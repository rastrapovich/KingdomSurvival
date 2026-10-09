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

// Камера исследуемого места и боя на месте (Старая шахта на настоящей
// сцене): масштаб кнопками, закреплённая камера стоит на месте и держит
// масштаб, отпущенная снова следует за командиром; в бою приближается само
// поле, рисунок места подстраивается под него.
public sealed class LocalCameraPlayModeTests
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

    private static object Member(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name, AnyInstance);
        if (property != null)
            return property.GetValue(target);
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        Assert.IsNotNull(field, "Нет поля " + name);
        return field.GetValue(target);
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static IEnumerator Until(System.Func<bool> condition, float seconds = 20f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    private static LocalFreeMover Mover(MonoBehaviour main) => (LocalFreeMover)Member(main, "localMover");
    private static object Renderer(MonoBehaviour main) => Member(main, "localRenderer");
    private static Vector2 ViewCenter(MonoBehaviour main) => (Vector2)Member(Renderer(main), "ViewCenter");
    private static float ViewHeight(MonoBehaviour main) => (float)Member(Renderer(main), "ViewHeight");
    private static bool DialogueActive(MonoBehaviour main) => (bool)Member(main, "IsNarrativeDialogueActive");

    private static IEnumerator WalkTo(MonoBehaviour main, Vector2 point)
    {
        Invoke(main, "OnLocalGroundClicked", point);
        yield return Until(() => DialogueActive(main) || Member(main, "localBattle") != null || !Mover(main).LeaderHasOrder);
        yield return Frames(30);
    }

    private static IEnumerator Capture(MonoBehaviour main, string name)
    {
        string directory = System.Environment.GetEnvironmentVariable("KS_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory))
            yield break;
        PanelSettings settings = main.GetComponent<UIDocument>().panelSettings;
        RenderTexture previous = settings.targetTexture;
        RenderTexture texture = new RenderTexture(1920, 1080, 24);
        settings.targetTexture = texture;
        yield return Frames(4);
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = texture;
        Texture2D image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        image.Apply();
        RenderTexture.active = active;
        settings.targetTexture = previous;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
        Object.Destroy(image);
        texture.Release();
        Object.Destroy(texture);
        yield return Frames(2);
    }

    [UnityTest]
    public IEnumerator Camera_ZoomLockAndBattleZoom()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .First(behaviour => behaviour.GetType().Name == "PrototypeUIController");
        Invoke(main, "StartNewFreePlayFromMenu");
        yield return Frames(10);

        GameState campaign = CampaignSession.Current;
        campaign.Food = 40;
        campaign.ArmySupply = 30;
        campaign.Narrative.SetFlag("freeplay.mine.known");
        CampaignContent.Refresh(campaign);
        Assert.IsTrue(campaign.TryStartExpedition("mine", new List<string> { "garrick", "torvin" }, out string message, HomePeopleService.OstafiyId), message);
        campaign.ActiveExpedition.Phase = CommanderState.AtLocation;
        yield return null;
        Assert.IsTrue((bool)Invoke(main, "TryOpenLocationInteraction", "mine"));
        yield return Frames(10);

        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        VisualElement panel = root.Q("local-camera-panel");
        Assert.IsNotNull(panel, "Панель камеры на экране места.");
        Assert.AreEqual("100%", root.Q<Label>("local-camera-zoom").text);

        // Приближение: кадр места становится ниже.
        float before = ViewHeight(main);
        Invoke(main, "ZoomLocalCamera", 1.5f);
        yield return Frames(3);
        Assert.AreEqual(1.5f, (float)Member(main, "localZoom"), 1e-3f);
        Assert.Less(ViewHeight(main), before * 0.8f, "Камера приблизилась.");
        Assert.AreEqual("150%", root.Q<Label>("local-camera-zoom").text);
        yield return Capture(main, "camera_01_zoomed");

        // Закрепить: командир уходит — кадр и масштаб на месте.
        Invoke(main, "ToggleLocalCameraLock");
        Vector2 lockedCenter = ViewCenter(main);
        float lockedHeight = ViewHeight(main);
        Invoke(main, "ZoomLocalCamera", 1.5f);
        Assert.AreEqual(1.5f, (float)Member(main, "localZoom"), 1e-3f, "Закреплённая камера держит масштаб.");
        Vector2 start = new Vector2((float)Mover(main).Leader.X, (float)Mover(main).Leader.Y);
        yield return WalkTo(main, new Vector2(760, 600));
        Vector2 walked = new Vector2((float)Mover(main).Leader.X, (float)Mover(main).Leader.Y);
        Assert.Greater(Vector2.Distance(start, walked), 100f, "Командир ушёл.");
        Assert.Less(Vector2.Distance(lockedCenter, ViewCenter(main)), 0.5f, "Закреплённая камера стоит на месте.");
        Assert.AreEqual(lockedHeight, ViewHeight(main), 0.01f);
        yield return Capture(main, "camera_02_locked");

        // Отпустить: камера снова следует за командиром.
        Invoke(main, "ToggleLocalCameraLock");
        yield return WalkTo(main, new Vector2(620, 470));
        yield return Frames(60);
        Assert.Greater(Vector2.Distance(lockedCenter, ViewCenter(main)), 1f, "Отпущенная камера следует за командиром.");

        // Бой на месте: приближается поле, рисунок места — вместе с ним.
        yield return WalkTo(main, new Vector2(1180, 590));
        yield return Until(() => DialogueActive(main), 5f);
        object session = Member(main, "narrativeDialogueSession");
        for (int i = 0; i < 10 && DialogueActive(main); i++)
        {
            object view = Invoke(session, "BuildView");
            IList choices = (IList)Member(view, "AvailableChoices");
            object choice = choices.Cast<object>().FirstOrDefault(c => (string)Member(c, "ChoiceId") == "freeplay.mine.local.lair.fight") ??
                            (choices.Count == 1 ? choices[0] : null);
            Assert.IsNotNull(choice);
            Invoke(main, "OnNarrativeDialogueChoiceSelected", (string)Member(choice, "ChoiceId"), (string)Member(choice, "Text"), Member(choice, "Kind"));
            yield return null;
        }
        yield return Until(() => Member(main, "localBattle") != null, 5f);
        object battle = Member(main, "localBattle");
        Assert.IsNotNull(battle, "Бой начался.");
        yield return Frames(10);
        VisualElement surfaceBefore = (VisualElement)Member(battle, "BattlefieldSurface");
        Assert.IsNotNull(surfaceBefore);
        Assert.Greater((int)Member(Renderer(main), "BattleFigureCount"), 0, "Фигуры боя на месте.");
        Assert.AreEqual("100%", root.Q<Label>("local-camera-zoom").text, "Новый бой — обычный масштаб.");
        yield return Capture(main, "camera_03_battle");

        float battleHeight = ViewHeight(main);
        Invoke(main, "ZoomLocalCamera", 1.6f);
        yield return Frames(5);
        VisualElement surface = (VisualElement)Member(battle, "BattlefieldSurface");
        Assert.AreEqual(1.6f, surface.resolvedStyle.scale.value.x, 1e-3f, "Поле боя приблизилось.");
        Assert.AreEqual(battleHeight / 1.6f, ViewHeight(main), battleHeight * 0.02f, "Рисунок места приблизился вместе с полем.");
        yield return Capture(main, "camera_04_battle_zoomed");

        Invoke(main, "ToggleLocalCameraLock");
        Invoke(main, "ZoomLocalCamera", 0.5f);
        yield return Frames(3);
        Assert.AreEqual(1.6f, surface.resolvedStyle.scale.value.x, 1e-3f, "Закреплённая камера в бою держит масштаб.");
        Invoke(main, "ToggleLocalCameraLock");

        Invoke(battle, "RetreatFromCampaignBattle");
        yield return Frames(5);
        Assert.IsNull(Member(main, "localBattle"));
        Assert.AreEqual("150%", root.Q<Label>("local-camera-zoom").text, "После боя — масштаб места.");
    }
}
