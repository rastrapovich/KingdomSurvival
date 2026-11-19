using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Смена дня и ночи (бриф 19.11.2026) в настоящей сцене: круг суток стоит
// справа от цифровых часов и не перекрывает темп карты и навигацию; поворот
// диска и свет живой карты совпадают с часами мира в 08/12/18/00; на паузе
// неподвижны; с ходом времени диск вращается в ту же сторону ровно на
// 15° за игровой час. Кадры — при заданной KS_SCREENSHOT_DIR.
public sealed class DayNightPlayModeTests
{
    private const string MainScene = "Prototype_Main";
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
        PlayModeSaveIsolation.Begin();
        MapLabGameMap.AllowInBatch = true;
    }

    [TearDown]
    public void TearDown()
    {
        MapLabGameMap.AllowInBatch = false;
        MapLabGameLive.Clear();
        CampaignSession.Reset();
        PlayModeSaveIsolation.End();
    }

    private static MonoBehaviour FindBehaviour(string typeName) =>
        Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(b => b.GetType().Name == typeName);

    private static void Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, AnyInstance);
        Assert.IsNotNull(info, "Нет метода " + method);
        info.Invoke(target, args);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, AnyInstance).GetValue(target);

    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, AnyInstance).SetValue(target, value);

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static IEnumerator Capture(MonoBehaviour main, string name)
    {
        string directory = System.Environment.GetEnvironmentVariable("KS_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory))
            yield break;
        const int width = 1920, height = 1080;
        PanelSettings settings = main.GetComponent<UIDocument>().panelSettings;
        RenderTexture previous = settings.targetTexture;
        RenderTexture texture = new RenderTexture(width, height, 24);
        settings.targetTexture = texture;
        yield return Frames(6);
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = texture;
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
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

    private static void SetHour(GameState campaign, double hour)
    {
        ContinuousSimulationSnapshotData snapshot = ContinuousSimulationSystem.ExportSnapshot(campaign);
        snapshot.HourOfDay = hour;
        ContinuousSimulationSystem.RestoreSnapshot(campaign, snapshot);
    }

    private static float DiscAngle(VisualElement disc) => disc.style.rotate.value.angle.ToDegrees();

    private static float AngleDelta(float a, float b)
    {
        float d = Mathf.Repeat(a - b + 180f, 360f) - 180f;
        return d;
    }

    [UnityTest]
    public IEnumerator DayNightDial_FollowsWorldClock_AndTintsOnlyTheMap()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        Invoke(main, "StartNewGameFromMenu");
        float started = Time.realtimeSinceStartup;
        while ((MapLabGameLive.Current == null || !MapLabGameLive.Current.Ready) && Time.realtimeSinceStartup - started < 300f)
            yield return null;
        Assert.IsTrue(MapLabGameLive.Current != null && MapLabGameLive.Current.Ready, "Живая карта построена.");
        yield return Frames(60);
        GameState campaign = CampaignSession.Current;

        // Пауза игрока — часы стоят, выставляем время сами.
        SetField(main, "userTimePaused", true);
        yield return Frames(5);

        VisualElement dial = root.Q("day-night-dial");
        VisualElement disc = root.Q("day-night-disc");
        Label dayLabel = root.Q<Label>("day-label");
        Assert.IsNotNull(dial, "Круг суток создан.");
        Assert.IsNotNull(disc);
        Assert.AreSame(dayLabel.parent, dial.parent, "Круг — в той же нижней панели, что и часы.");
        Assert.AreEqual(dial.parent.IndexOf(dayLabel) + 1, dial.parent.IndexOf(dial), "Круг сразу справа от «День N · HH:MM».");
        Assert.IsFalse(dial is Button, "Круг не кнопка.");
        Assert.AreEqual(1, root.Query("day-night-dial").ToList().Count, "Круг один, не пересоздаётся.");

        Rect dialRect = dial.worldBound;
        Assert.Greater(dialRect.width, 60f);
        Assert.LessOrEqual(dialRect.width, 72f);
        VisualElement tempo = root.Q("map-tempo-bar");
        if (tempo != null)
            Assert.IsFalse(dialRect.Overlaps(tempo.worldBound), "Круг не перекрывает темп карты.");
        Assert.IsFalse(dialRect.Overlaps(dayLabel.worldBound), "Круг не перекрывает часы.");
        foreach (Button nav in root.Query<Button>(className: "shell-nav-button").ToList())
            Assert.IsFalse(dialRect.Overlaps(nav.worldBound), "Круг не перекрывает навигацию: " + nav.name);
        Rect screen = root.worldBound;
        Assert.IsTrue(dialRect.xMin >= screen.xMin && dialRect.xMax <= screen.xMax && dialRect.yMax <= screen.yMax + 0.5f, "Круг в пределах экрана.");

        VisualElement live = Field<VisualElement>(main, "worldMapLiveImage");
        Assert.IsNotNull(live);

        foreach (double hour in new[] { 8.0, 12.0, 18.0, 0.0 })
        {
            SetHour(campaign, hour);
            yield return Frames(5);
            double absolute = ContinuousSimulationSystem.GetAbsoluteHours(campaign);
            Assert.AreEqual((float)DayNightCycle.GetDiscRotationWrapped(absolute), DiscAngle(disc), 0.05f, "Поворот диска в " + hour);
            DayNightLight light = DayNightCycle.GetLight(hour);
            Color tint = live.style.unityBackgroundImageTintColor.value;
            Assert.AreEqual(light.R, tint.r, 0.01f, "Свет карты в " + hour);
            Assert.AreEqual(light.B, tint.b, 0.01f, "Свет карты в " + hour);
            StringAssert.AreEqualIgnoringCase(DayNightCycle.GetPhaseName(hour), dial.tooltip.Replace("Время суток: ", "").TrimEnd('.'), "Подсказка в " + hour);
            yield return Capture(main, "daynight_" + ((int)hour).ToString("00"));
        }

        // Интерфейс и значки не тонируются вместе с картой.
        Assert.AreEqual(Color.white, dayLabel.resolvedStyle.unityBackgroundImageTintColor, "Часы не тонируются.");

        // Пауза: диск и свет неподвижны.
        SetHour(campaign, 21.0);
        yield return Frames(5);
        float pausedAngle = DiscAngle(disc);
        Color pausedTint = live.style.unityBackgroundImageTintColor.value;
        yield return Frames(60);
        Assert.AreEqual(pausedAngle, DiscAngle(disc), 1e-4f, "На паузе диск стоит.");
        Assert.AreEqual(pausedTint, live.style.unityBackgroundImageTintColor.value, "На паузе свет не меняется.");

        // Ход времени через полночь: диск идёт в одну сторону, 15° за час.
        SetHour(campaign, 23.5);
        yield return Frames(3);
        SetField(main, "userTimePaused", false);
        Invoke(main, "SetMapTempo", 3);
        double previousHours = ContinuousSimulationSystem.GetAbsoluteHours(campaign);
        float previousAngle = DiscAngle(disc);
        int startDay = campaign.Day;
        float runStarted = Time.realtimeSinceStartup;
        while (campaign.Day == startDay && Time.realtimeSinceStartup - runStarted < 60f)
        {
            yield return null;
            double hours = ContinuousSimulationSystem.GetAbsoluteHours(campaign);
            float angle = DiscAngle(disc);
            float delta = AngleDelta(angle, previousAngle);
            Assert.LessOrEqual(delta, 1e-3f, "Диск не крутится назад.");
            Assert.AreEqual(-(hours - previousHours) * 15.0, delta, 0.1, "15° за игровой час.");
            previousHours = hours;
            previousAngle = angle;
        }
        Assert.AreEqual(startDay + 1, campaign.Day, "Наступили новые сутки.");
        yield return Frames(10);
        Assert.AreEqual((float)DayNightCycle.GetDiscRotationWrapped(ContinuousSimulationSystem.GetAbsoluteHours(campaign)), DiscAngle(disc), 0.05f);
        SetField(main, "userTimePaused", true);
    }
}
