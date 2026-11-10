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
                float frameMs = 0f;
                for (int i = 0; i < 90; i++)
                {
                    yield return null;
                    frameMs += Time.unscaledDeltaTime * 1000f;
                }
                Log($"round {round + 1}: ready in {readyMs:0} ms (battle screen {invokeMs:0} ms, build {driver.Arena.BuildMs:0} ms), " +
                    $"decor from asset database {driver.Arena.UsesAssetDatabase} ({driver.Arena.DecorFeatures}), frame {frameMs / 90f:0.0} ms, " +
                    $"memory {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0} MB");
                if (round == 0)
                    Save(surface);
                Invoke(sandbox, "BuildSetupScreen");
                for (int i = 0; i < 10; i++)
                    yield return null;
            }
            Log(ok ? "PASS" : "FAIL");
            Application.Quit(ok ? 0 : 1);
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
