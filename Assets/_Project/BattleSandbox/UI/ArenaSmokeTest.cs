using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Проверка арены MapLab в готовой сборке (вне редактора): только с ключом
    // командной строки -arenaSmokeTest [-arenaSmokeRounds N]. Открывает полигон,
    // начинает бой, ждёт арену, пишет замеры в журнал игрока (строки
    // «ARENA_SMOKE:»), сохраняет кадр арены рядом с журналом, проходит
    // несколько боёв подряд и закрывает игру. Без ключа ничего не делает.
    internal sealed class ArenaSmokeTest : MonoBehaviour
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-arenaSmokeTest"))
                return;
            GameObject host = new GameObject("Arena Smoke Test");
            DontDestroyOnLoad(host);
            host.AddComponent<ArenaSmokeTest>();
        }

        private static object Member(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, AnyInstance);
            if (field != null) return field.GetValue(target);
            return target.GetType().GetProperty(name, AnyInstance)?.GetValue(target);
        }

        private static void Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, AnyInstance).Invoke(target, args);

        private static void Log(string text) => Debug.Log("ARENA_SMOKE: " + text);

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int rounds = 5;
            int index = Array.IndexOf(args, "-arenaSmokeRounds");
            if (index >= 0 && index + 1 < args.Length)
                int.TryParse(args[index + 1], out rounds);
            Log($"start, graphics {SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceName}, screen {Screen.width}x{Screen.height}");
            if (args.Contains("-mapSmoke"))
            {
                // Карта партии MapLab (лес из Базы ассетов): собрать, нарисовать кадр, освободить.
                WorldMapGenerationSettings planSettings = WorldMapGenerationSettings.CreateDefault();
                planSettings.HexesAcross = 24;
                GeneratedWorldMap plan = WorldMapGenerator.Generate(planSettings, 7);
                KingdomSurvival.MapLabBridge.MapLabGameLive.World world = KingdomSurvival.MapLabBridge.MapLabGameLive.Begin(plan, null);
                float mapStarted = Time.realtimeSinceStartup, nextReport = 5f;
                while (!KingdomSurvival.MapLabBridge.MapLabGameLive.Advance(world))
                {
                    if (Time.realtimeSinceStartup - mapStarted > nextReport)
                    {
                        nextReport += 5f;
                        Log($"map building {Time.realtimeSinceStartup - mapStarted:0} s: stage «{world.Stage}», progress {world.Progress:0.00}, error «{world.Error}»");
                    }
                    yield return null;
                }
                Log($"map ready {world.Ready}, forest from asset database {world.ForestSet != null} ({world.ForestDeciduous} deciduous, {world.ForestConifers} conifers), build {world.BuildMs:0} ms");
                for (int i = 0; i < 30; i++)
                {
                    KingdomSurvival.MapLabBridge.MapLabGameLive.Render(world, new Rect(30f, 30f, 40f, 40f), 1600, 900);
                    yield return null;
                }
                bool mapReady = world.Ready;
                KingdomSurvival.MapLabBridge.MapLabGameLive.Clear();
                for (int i = 0; i < 10; i++) yield return null;
                Log(mapReady ? "PASS" : "FAIL");
                Application.Quit(mapReady ? 0 : 1);
                yield break;
            }
            int maxHeightIndex = Array.IndexOf(args, "-arenaMaxHeight");
            if (maxHeightIndex >= 0 && maxHeightIndex + 1 < args.Length && int.TryParse(args[maxHeightIndex + 1], out int maxHeight))
                MapLabArenaDriver.MaxRenderHeight = maxHeight;
            if (args.Contains("-arenaMsaa"))
                KingdomSurvival.MapLabBridge.MapLabArena.FrameMsaa = 4;
            if (args.Contains("-arenaBuildOnly") || args.Contains("-arenaRenderOnly"))
            {
                // Мир арены без экрана боя: собрать (и нарисовать кадр) и освободить.
                SandboxBattle probe = SandboxRoster.CreateDefaultBattle(new[] { "guard", "archer" }, SandboxRoster.PlayerRoster, SandboxRoster.EnemyRoster, 3);
                KingdomSurvival.MapLabBridge.MapLabArena bare = KingdomSurvival.MapLabBridge.MapLabArena.Begin(MapLabArenaDriver.BuildSpec(null, probe));
                while (!bare.Advance())
                    yield return null;
                if (args.Contains("-arenaRenderOnly"))
                {
                    bare.Render(1600, 900);
                    yield return null;
                }
                Log($"bare arena ready {bare.Ready}, build {bare.BuildMs:0} ms");
                bare.Dispose();
                for (int i = 0; i < 10; i++) yield return null;
                Log("PASS");
                Application.Quit(0);
                yield break;
            }
            if (SceneManager.GetActiveScene().name != "BattleSandbox")
            {
                SceneManager.LoadScene("BattleSandbox");
                for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != "BattleSandbox"; i++)
                    yield return null;
            }
            BattleSandboxController sandbox = null;
            for (int i = 0; i < 600 && (sandbox == null || !(bool)Member(sandbox, "initialized")); i++)
            {
                sandbox = FindAnyObjectByType<BattleSandboxController>();
                yield return null;
            }
            if (sandbox == null)
            {
                Log("FAIL no sandbox controller");
                Application.Quit(2);
                yield break;
            }
            // -arenaOff: тот же сценарий на прежнем поле (сравнение).
            bool arenaOff = args.Contains("-arenaOff");
            KingdomSurvival.MapLabBridge.MapLabGameAssets assets = KingdomSurvival.MapLabBridge.MapLabGameAssets.Load();
            if (arenaOff && assets != null)
                assets.ArenaInBattle = false;
            // Как у игрока: до первого боя — время в меню, арена прогревается.
            float warmStart = Time.realtimeSinceStartup;
            while (!arenaOff && !KingdomSurvival.MapLabBridge.MapLabArenaPrewarm.Done && Time.realtimeSinceStartup - warmStart < 60f)
                yield return null;
            Log($"prewarm done {KingdomSurvival.MapLabBridge.MapLabArenaPrewarm.Done} after {(Time.realtimeSinceStartup - warmStart) * 1000f:0} ms");
            Invoke(sandbox, "ChangeCount", false, "guard", 1);
            Invoke(sandbox, "ChangeCount", false, "archer", 1);
            Invoke(sandbox, "ChangeCount", true, "wolf", 1);
            bool ok = true;
            for (int round = 0; round < Mathf.Max(1, rounds); round++)
            {
                float started = Time.realtimeSinceStartup;
                Invoke(sandbox, "StartBattle");
                float invokeMs = (Time.realtimeSinceStartup - started) * 1000f;
                MapLabArenaDriver driver = null;
                BattlefieldView surface = null;
                bool loadingShot = round > 0 || arenaOff;
                while (Time.realtimeSinceStartup - started < 90f)
                {
                    if (!loadingShot && Time.realtimeSinceStartup - started > 0.6f)
                    {
                        loadingShot = true;
                        string shotPath = System.IO.Path.Combine(Application.persistentDataPath, "arena-loading.png");
                        ScreenCapture.CaptureScreenshot(shotPath);
                        Log("loading screen " + (MapLabArenaDriver.Loading ? "shown" : "not shown") + ", saved " + shotPath);
                    }
                    surface = sandbox.BattlefieldSurface;
                    driver = surface?.Relief as MapLabArenaDriver;
                    if (driver != null && surface.HasSceneTexture)
                        break;
                    if (arenaOff && surface != null && Time.realtimeSinceStartup - started > 2f)
                        break;
                    yield return null;
                }
                float readyMs = (Time.realtimeSinceStartup - started) * 1000f;
                if (arenaOff)
                {
                    for (int i = 0; i < 30; i++) yield return null;
                    Log($"round {round + 1}: old field (arena off)");
                    Invoke(sandbox, "BuildSetupScreen");
                    for (int i = 0; i < 10; i++) yield return null;
                    continue;
                }
                if (driver == null)
                {
                    Log($"FAIL round {round + 1}: arena not attached");
                    ok = false;
                    break;
                }
                float frameMs = 0f, worst = 0f;
                int arenaFrames = driver.RenderedFrames;
                for (int i = 0; i < 90; i++)
                {
                    yield return null;
                    frameMs += Time.unscaledDeltaTime * 1000f;
                    worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f);
                }
                arenaFrames = driver.RenderedFrames - arenaFrames;
                Log($"round {round + 1}: ready in {readyMs:0} ms (battle screen {invokeMs:0} ms, build {driver.Arena.BuildMs:0} ms), " +
                    $"decor from asset database {driver.Arena.UsesAssetDatabase} ({driver.Arena.DecorFeatures}), frame {frameMs / 90f:0.0} ms (worst {worst:0} ms), arena {arenaFrames * 1000f / Mathf.Max(1f, frameMs):0.0} frames/s, " +
                    $"memory {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0} MB");
                if (round == 0)
                {
                    Save(surface);
                    string screenPath = Path.Combine(Application.persistentDataPath, "arena-screen.png");
                    ScreenCapture.CaptureScreenshot(screenPath);
                    yield return null;
                    yield return null;
                    Log("battle screen saved " + screenPath);
                }
                if (round == 0 && args.Contains("-arenaBench"))
                    yield return Bench(driver);
                Invoke(sandbox, "BuildSetupScreen");
                for (int i = 0; i < 10; i++)
                    yield return null;
            }
            Log(ok ? "PASS" : "FAIL");
            Application.Quit(ok ? 0 : 1);
        }

        // -arenaBench: цена кадра по частям. Кадр игры без перерисовки арены
        // (интерфейс и доска), затем проходы арены с ожиданием видеокарты
        // (чтение 1 пикселя): полный кадр драйвера, нижний кадр с верхним слоем,
        // без обработки кадра, без MSAA, в половину высоты.
        private IEnumerator Bench(MapLabArenaDriver driver)
        {
            MapLabArenaDriver.FreezeRendering = true;
            for (int i = 0; i < 10; i++) yield return null;
            float frameMs = 0f, worst = 0f;
            for (int i = 0; i < 90; i++)
            {
                yield return null;
                frameMs += Time.unscaledDeltaTime * 1000f;
                worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f);
            }
            Log($"bench: game frame with arena frozen {frameMs / 90f:0.0} ms (worst {worst:0} ms)");
            KingdomSurvival.MapLabBridge.MapLabArena arena = driver.Arena;
            Vector2Int size = (Vector2Int)Member(driver, "requestedSize");
            Texture2D pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            void Sync(Texture texture)
            {
                if (!(texture is RenderTexture rt)) return;
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                RenderTexture.active = previous;
            }
            string Measure(string name, Func<Texture> render)
            {
                Sync(render());
                Sync(render());
                double cpu = 0, total = 0;
                const int n = 12;
                for (int i = 0; i < n; i++)
                {
                    System.Diagnostics.Stopwatch w = System.Diagnostics.Stopwatch.StartNew();
                    Texture t = render();
                    cpu += w.Elapsed.TotalMilliseconds;
                    Sync(t);
                    total += w.Elapsed.TotalMilliseconds;
                }
                string line = $"bench {name}: {total / n:0.0} ms (CPU submit {cpu / n:0.0} ms)";
                Log(line);
                return line;
            }
            yield return null;
            Log($"bench size {size.x}x{size.y}, msaa {KingdomSurvival.MapLabBridge.MapLabArena.EffectiveFrameMsaa}, front rows {driver.FrontRowCount}");
            Measure("full driver frame", () => { driver.RenderRequested(); return driver.LastFrameTexture; });
            yield return null;
            Measure("ground+objects", () => arena.Render(size.x, size.y));
            yield return null;
            Measure("front key + 2 rows only", () =>
            {
                arena.PrepareFronts(2);
                arena.RenderFront(0, arena.GroundZ(0.5f));
                return arena.RenderFront(1, arena.GroundZ(0.7f));
            });
            Measure("ground only (overlay off)", () => arena.RenderGroundOnlyForBench(size.x, size.y));
            yield return null;
            var post = arena.Spec.Frame;
            arena.Spec.Frame = null;
            Measure("ground+objects, no frame post", () => arena.Render(size.x, size.y));
            arena.Spec.Frame = post;
            yield return null;
            int msaa = KingdomSurvival.MapLabBridge.MapLabArena.FrameMsaa;
            KingdomSurvival.MapLabBridge.MapLabArena.FrameMsaa = 1;
            Measure("ground+objects, msaa 1", () => arena.Render(size.x, size.y));
            Measure("full driver frame, msaa 1", () => { driver.RenderRequested(); return driver.LastFrameTexture; });
            KingdomSurvival.MapLabBridge.MapLabArena.FrameMsaa = msaa;
            yield return null;
            Measure("full driver frame (again)", () => { driver.RenderRequested(); return driver.LastFrameTexture; });
            yield return null;
            Destroy(pixel);
            MapLabArenaDriver.FreezeRendering = false;
            for (int i = 0; i < 10; i++) yield return null;
        }

        private static void Save(BattlefieldView surface)
        {
            if (!(surface.Q<Image>("battlefield-background")?.image is RenderTexture rt))
                return;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            shot.Apply();
            RenderTexture.active = previous;
            string path = Path.Combine(Application.persistentDataPath, "arena-smoke.png");
            File.WriteAllBytes(path, shot.EncodeToPNG());
            Destroy(shot);
            Log("frame saved " + path);
        }
    }
}
