using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Бриф исправлений п. 16: замер глобальной карты свободной игры в одинаковых
// условиях — покой, панорамирование камеры, бег героя. Пишет время кадра
// главного потока (среднее, 95-й и 99-й перцентиль, максимум, кадры дольше
// 16,7 мс), выделения GC за кадр и самые дорогие профайлерные метки фазы.
// Отчёт — в консоль и (при KS_PERF_DIR) в файл. Порог не проверяется: в
// batchmode время кадра не равно времени кадра на мониторе игрока.
public sealed class WorldMapPerformancePlayModeTests
{
    private const string MainScene = "Prototype_Main";
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int FramesPerPhase = 240;

    private static readonly string[] SamplerHints =
    {
        "UIElements", "UIR", "Layout", "BehaviourUpdate", "PrototypeUI", "Painter", "TextCore", "Text",
        "GC.Alloc", "GC.Collect", "Render", "Camera", "Canvas", "Mesh", "Panel", "Style", "Update"
    };

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

    private sealed class Phase
    {
        public string Name;
        public readonly List<double> FrameMs = new List<double>();
        public readonly List<long> GcBytes = new List<long>();
        public readonly Dictionary<string, double> SamplerMs = new Dictionary<string, double>();
    }

    [UnityTest]
    public IEnumerator FreePlayMap_IdlePanRun_FrameTimes()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        for (int i = 0; i < 30; i++)
            yield return null;
        MonoBehaviour main = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .First(b => b.GetType().Name == "PrototypeUIController");
        main.GetType().GetMethod("StartNewFreePlayFromMenu", AnyInstance).Invoke(main, null);
        for (int i = 0; i < 60; i++)
            yield return null;

        GameState campaign = CampaignSession.Current;
        Assert.IsNotNull(campaign);

        List<string> names = new List<string>();
        Sampler.GetNames(names);
        List<(string Name, Recorder Recorder)> recorders = names
            .Where(n => SamplerHints.Any(h => n.Contains(h)))
            .Select(n => (n, Recorder.Get(n)))
            .Where(r => r.Item2 != null && r.Item2.isValid)
            .ToList();
        foreach ((string _, Recorder recorder) in recorders)
            recorder.enabled = true;

        ProfilerRecorder mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        ProfilerRecorder gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);

        List<Phase> phases = new List<Phase>();

        Phase idle = new Phase { Name = "Покой на карте" };
        phases.Add(idle);
        for (int i = 0; i < FramesPerPhase; i++)
        {
            yield return null;
            Sample(idle, mainThread, gc, recorders);
        }

        // Панорамирование: смещение камеры каждый кадр тем же путём, что
        // перетаскивание средней кнопкой.
        Phase pan = new Phase { Name = "Панорамирование камеры" };
        phases.Add(pan);
        FieldInfo panX = main.GetType().GetField("worldMapPanOffsetX", AnyInstance);
        FieldInfo panY = main.GetType().GetField("worldMapPanOffsetY", AnyInstance);
        MethodInfo clamp = main.GetType().GetMethod("ClampWorldMapPan", AnyInstance);
        MethodInfo apply = main.GetType().GetMethod("ApplyWorldMapViewportTransform", AnyInstance);
        MethodInfo suspend = main.GetType().GetMethod("SuspendWorldMapCameraFollow", AnyInstance);
        suspend?.Invoke(main, null);
        float startX = (float)panX.GetValue(main);
        float startY = (float)panY.GetValue(main);
        for (int i = 0; i < FramesPerPhase; i++)
        {
            float t = i / (float)FramesPerPhase * Mathf.PI * 2f;
            panX.SetValue(main, startX + Mathf.Sin(t) * 400f);
            panY.SetValue(main, startY + Mathf.Cos(t) * 250f);
            clamp.Invoke(main, null);
            apply.Invoke(main, null);
            yield return null;
            Sample(pan, mainThread, gc, recorders);
        }

        // Бег героя к дальнему месту (камера следует).
        Phase run = new Phase { Name = "Бег героя" };
        phases.Add(run);
        LocationData target = campaign.Locations
            .Where(l => !l.IsWaypoint && l.IsVisibleOnMap)
            .OrderByDescending(l => Mathf.Abs(l.MapXPercent - WorldMapNavigation.CapitalXPercent) + Mathf.Abs(l.MapYPercent - WorldMapNavigation.CapitalYPercent))
            .FirstOrDefault();
        if (target != null)
        {
            campaign.ArmySupply = 30;
            campaign.TryStartExpedition(target.Id, new List<string> { "garrick" }, out _);
        }
        for (int i = 0; i < FramesPerPhase; i++)
        {
            yield return null;
            Sample(run, mainThread, gc, recorders);
        }

        mainThread.Dispose();
        gc.Dispose();
        foreach ((string _, Recorder recorder) in recorders)
            recorder.enabled = false;

        string report = BuildReport(phases);
        Debug.Log(report);
        string directory = System.Environment.GetEnvironmentVariable("KS_PERF_DIR");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "worldmap_perf.txt"), report);
        }
        Assert.Greater(idle.FrameMs.Count, 0);

        // Снимки после замера (смена размера панели не попадает в цифры):
        // карта, сдвинутая к краю, должна обрезаться рамкой просмотра.
        panX.SetValue(main, startX - 300f);
        panY.SetValue(main, startY - 200f);
        clamp.Invoke(main, null);
        apply.Invoke(main, null);
        yield return Capture(main, "worldmap_pan", 1920, 1080);
        yield return Capture(main, "worldmap_pan", 1280, 720);
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
        for (int i = 0; i < 4; i++)
            yield return null;
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
        yield return null;
    }

    private static void Sample(Phase phase, ProfilerRecorder mainThread, ProfilerRecorder gc, List<(string Name, Recorder Recorder)> recorders)
    {
        double frame = mainThread.Valid && mainThread.Count > 0 ? mainThread.LastValue / 1e6 : Time.unscaledDeltaTime * 1000.0;
        phase.FrameMs.Add(frame);
        phase.GcBytes.Add(gc.Valid && gc.Count > 0 ? gc.LastValue : 0);
        foreach ((string name, Recorder recorder) in recorders)
        {
            double ms = recorder.elapsedNanoseconds / 1e6;
            if (ms <= 0)
                continue;
            phase.SamplerMs.TryGetValue(name, out double sum);
            phase.SamplerMs[name] = sum + ms;
        }
    }

    private static string BuildReport(List<Phase> phases)
    {
        StringBuilder text = new StringBuilder();
        text.AppendLine("Глобальная карта свободной игры — замер главного потока (" + FramesPerPhase + " кадров на фазу).");
        text.AppendLine("Устройство: " + SystemInfo.processorType + " · " + SystemInfo.graphicsDeviceName + " · " +
                        SystemInfo.systemMemorySize + " МБ · экран " + Screen.width + "×" + Screen.height +
                        (Application.isBatchMode ? " · batchmode" : string.Empty));
        foreach (Phase phase in phases)
        {
            List<double> sorted = phase.FrameMs.OrderBy(v => v).ToList();
            double avg = sorted.Average();
            double p95 = sorted[(int)(sorted.Count * 0.95) - 1];
            double p99 = sorted[(int)(sorted.Count * 0.99) - 1];
            int slow = sorted.Count(v => v > 16.7);
            double gcAvg = phase.GcBytes.Average() / 1024.0;
            text.AppendLine();
            text.AppendLine("== " + phase.Name);
            text.AppendLine(string.Format("кадр: среднее {0:F2} мс, p95 {1:F2}, p99 {2:F2}, макс {3:F2}; дольше 16,7 мс: {4}; GC в кадре: {5:F1} КБ",
                avg, p95, p99, sorted.Last(), slow, gcAvg));
            foreach (KeyValuePair<string, double> top in phase.SamplerMs.OrderByDescending(p => p.Value).Take(15))
                text.AppendLine(string.Format("  {0,-60} {1,8:F3} мс/кадр", top.Key, top.Value / phase.FrameMs.Count));
        }
        return text.ToString();
    }
}
