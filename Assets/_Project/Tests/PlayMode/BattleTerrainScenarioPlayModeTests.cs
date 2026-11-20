using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.MapLab;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Бриф «Поверхности и окружение боевой арены», этап 6: три воспроизводимых
// сценария в настоящей сцене полигона (лесной брод с мостом, болотная
// поляна с болотным ходоком, руины со стенкой и холмом). Для каждого — ход
// игрока, изменение поверхности и ход ИИ; руины — во всех трёх видах.
// Кадры — в Logs/TerrainScenarios (не в git), строки «TERRAIN:» — в журнале.
// Настоящий рендер — только при запуске редактора без -batchmode.
public sealed class BattleTerrainScenarioPlayModeTests
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static object Member(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        if (field != null) return field.GetValue(target);
        return target.GetType().GetProperty(name, AnyInstance)?.GetValue(target);
    }

    private static void SetMember(object target, string name, object value) =>
        target.GetType().GetField(name, AnyInstance).SetValue(target, value);

    private static object Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, AnyInstance).Invoke(target, args);

    private static void Log(string text) => Debug.Log("TERRAIN: " + text);

    private static string OutDir
    {
        get
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainScenarios");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        if (Application.isBatchMode)
            yield break;
        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), shot.EncodeToPNG());
        Object.Destroy(shot);
    }

    private static IEnumerator OpenSandbox(List<MonoBehaviour> result)
    {
        CampaignSession.Reset();
        SceneManager.LoadScene("BattleSandbox");
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != "BattleSandbox"; i++)
            yield return null;
        MonoBehaviour sandbox = null;
        for (int i = 0; i < 120 && (sandbox == null || !(bool)Member(sandbox, "initialized")); i++)
        {
            sandbox = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(item => item.GetType().Name == "BattleSandboxController");
            yield return null;
        }
        Assert.IsNotNull(sandbox);
        result.Add(sandbox);
    }

    private static IEnumerator StartScenario(MonoBehaviour sandbox, SandboxArenaTheme theme, int seed, string[] fighters, string[] enemies)
    {
        ((System.Collections.IDictionary)Invoke(sandbox, "CountsFor", false)).Clear();
        ((System.Collections.IDictionary)Invoke(sandbox, "CountsFor", true)).Clear();
        Invoke(sandbox, "BuildSetupScreen");
        foreach (string id in fighters) Invoke(sandbox, "ChangeCount", false, id, 1);
        foreach (string id in enemies) Invoke(sandbox, "ChangeCount", true, id, 1);
        SetMember(sandbox, "sandboxThemeLabel", SandboxArenaThemes.Label(theme));
        SetMember(sandbox, "sandboxSeedText", seed.ToString());
        Invoke(sandbox, "StartBattle");
        float until = Time.realtimeSinceStartup + 90f;
        while (Time.realtimeSinceStartup < until)
        {
            BattlefieldView surface = (BattlefieldView)Member(sandbox, "BattlefieldSurface");
            if (surface?.Relief is MapLabArenaDriver && surface.HasSceneTexture)
                break;
            yield return null;
        }
        for (int i = 0; i < 20; i++) yield return null;
    }

    private static IEnumerator WaitPlayerTurn(MonoBehaviour sandbox)
    {
        float until = Time.realtimeSinceStartup + 40f;
        while (Time.realtimeSinceStartup < until)
        {
            SandboxBattle battle = (SandboxBattle)Member(sandbox, "battle");
            if (battle.Phase != SandboxBattlePhase.InProgress)
                yield break;
            if (battle.CurrentUnit != null && battle.CurrentUnit.Team == SandboxTeam.Player && !(bool)Member(sandbox, "combatAnimationRunning"))
                yield break;
            yield return null;
        }
    }

    // Ход игрока: текущий боец уходит в дальнюю достижимую клетку.
    private static string PlayerMove(MonoBehaviour sandbox, SandboxBattle battle)
    {
        SandboxUnitState unit = battle.CurrentUnit;
        if (unit == null || unit.Team != SandboxTeam.Player)
            return "нет хода игрока";
        HexCoord from = unit.Position;
        KeyValuePair<HexCoord, int> target = battle.GetReachable(unit.Id).OrderByDescending(p => p.Key.Q).ThenBy(p => p.Key).FirstOrDefault();
        bool moved = battle.TryMove(unit.Id, target.Key, out string message);
        Invoke(sandbox, "RefreshBattleScreen");
        return unit.DisplayLabel + " " + from + " → " + unit.Position + " (цена " + target.Value + "): " + (moved ? message : "отказ " + message);
    }

    private static IEnumerator EnemyTurn(MonoBehaviour sandbox, List<string> log)
    {
        SandboxBattle battle = (SandboxBattle)Member(sandbox, "battle");
        List<string> before = ((List<string>)Member(sandbox, "battleLog")).ToList();
        float until = Time.realtimeSinceStartup + 60f;
        // Завершать ходы отряда, пока не сходит хотя бы один противник.
        bool enemyActed = false;
        while (!enemyActed && battle.Phase == SandboxBattlePhase.InProgress && Time.realtimeSinceStartup < until)
        {
            if (battle.CurrentUnit != null && battle.CurrentUnit.Team == SandboxTeam.Player && !(bool)Member(sandbox, "combatAnimationRunning"))
                Invoke(sandbox, "EndPlayerActivation");
            yield return null;
            enemyActed |= battle.CurrentUnit != null && battle.CurrentUnit.Team == SandboxTeam.Enemy;
        }
        // Ждать, пока не сходит хотя бы один противник и ход не вернётся.
        while (Time.realtimeSinceStartup < until && battle.Phase == SandboxBattlePhase.InProgress &&
               (battle.CurrentUnit == null || battle.CurrentUnit.Team != SandboxTeam.Player || (bool)Member(sandbox, "combatAnimationRunning")))
            yield return null;
        log.AddRange(((List<string>)Member(sandbox, "battleLog")).Skip(before.Count));
    }

    [UnityTest]
    public IEnumerator ThreeTerrainScenarios_InRealSandboxScene()
    {
        Assert.IsTrue(MapLabArena.Available, "Арена MapLab включена.");
        Log("экран " + Screen.width + "×" + Screen.height + ", графика " + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName +
            ", batchmode " + Application.isBatchMode);
        List<MonoBehaviour> found = new List<MonoBehaviour>();
        yield return OpenSandbox(found);
        MonoBehaviour sandbox = found[0];
        string savedView = MapLabMapViews.ActiveId;
        MapLabMapViews.ActiveId = MapLabMapViews.ClassicId;

        (string Name, SandboxArenaTheme Theme, int Seed, string[] Fighters, string[] Enemies)[] scenarios =
        {
            ("1_ford_bridge", SandboxArenaTheme.FordBridge, 4, new[] { "guard", "archer" }, new[] { "wolf", "wolf" }),
            ("2_bog_swamp_walker", SandboxArenaTheme.BogGlade, 12, new[] { "guard", "archer", "spearman" }, new[] { "wolf", "wolf" }),
            ("3_ruins_wall_hill", SandboxArenaTheme.RuinedHillfort, 7, new[] { "guard", "archer" }, new[] { "wolf", "wolf", "wolf" }),
        };
        int objectsAfterFirst = -1;
        foreach (var s in scenarios)
        {
            yield return StartScenario(sandbox, s.Theme, s.Seed, s.Fighters, s.Enemies);
            SandboxBattle battle = (SandboxBattle)Member(sandbox, "battle");
            Assert.IsNotNull(battle.ArenaReport, s.Name);
            Assert.IsEmpty(battle.ArenaReport.Issues, s.Name);
            Log(s.Name + ": " + battle.ArenaReport.Summary() + "; состав " + string.Join(", ", battle.Units.Select(u => u.Id + "@" + u.Position)));
            HexBoardElement board = (HexBoardElement)Member(sandbox, "Board");
            object layout = Invoke(board, "CalculateLayout");
            float hexPoints = (float)layout.GetType().GetProperty("Size").GetValue(layout);
            Rect frame = (Rect)layout.GetType().GetProperty("Frame").GetValue(layout);
            float ppp = board.panel != null ? board.panel.scaledPixelsPerPoint : 1f;
            Log(s.Name + $": радиус гекса {hexPoints:0.0} pt = {hexPoints * ppp:0.0} px экрана, кадр поля {frame.width * ppp:0}×{frame.height * ppp:0} px, масштаб панели {ppp:0.00}");

            if (s.Theme == SandboxArenaTheme.BogGlade)
            {
                // Носителей тега в каталоге нет: выдать его одному волку для проверки.
                SandboxUnitState walker = battle.Units.First(u => u.Team == SandboxTeam.Enemy);
                ((HashSet<string>)walker.Definition.GetType().GetField("tagIds", AnyInstance).GetValue(walker.Definition)).Add(SandboxTerrainTags.SwampWalker);
                Log(s.Name + ": болотный ходок — " + walker.Id + " (тег выдан тестом)");
            }
            yield return Shot(s.Name + "_a_start");

            yield return WaitPlayerTurn(sandbox);
            Log(s.Name + ": ход игрока — " + PlayerMove(sandbox, battle));
            yield return Shot(s.Name + "_b_player_move");

            // Изменение поверхности: смола у противника и поджог; у брода —
            // ток по воде под мостом.
            SandboxUnitState foe = battle.Units.First(u => u.Team == SandboxTeam.Enemy && !u.IsDefeated);
            HexCoord tarCell = foe.Position.Neighbors().OrderBy(c => c).FirstOrDefault(c => battle.CanPlaceEffect(c, SandboxEffectKind.Tar) && battle.GetUnitAt(c) == null);
            battle.AddEffect(tarCell, SandboxEffectKind.Tar, 3);
            List<string> notes = battle.ApplyImpulse(SandboxImpulse.Fire, new[] { tarCell }, 2);
            if (s.Theme == SandboxArenaTheme.FordBridge)
            {
                HexCoord water = battle.GroundCells.Where(p => p.Value == SandboxGround.DeepWater && !battle.HasBridgeAt(p.Key)).Select(p => p.Key).OrderBy(c => c).First();
                notes.AddRange(battle.ApplyImpulse(SandboxImpulse.Electric, new[] { water }, 2));
            }
            Invoke(sandbox, "RefreshBattleScreen");
            Log(s.Name + ": поверхность — " + string.Join(" ", notes) + " [" + battle.DescribeCell(foe, tarCell) + "]");
            for (int i = 0; i < 30; i++) yield return null; // проявление области 0,4 с
            yield return Shot(s.Name + "_c_surface");

            List<string> enemyLog = new List<string>();
            yield return EnemyTurn(sandbox, enemyLog);
            Log(s.Name + ": ход ИИ — " + string.Join(" | ", enemyLog));
            yield return Shot(s.Name + "_d_ai_turn");

            if (s.Theme == SandboxArenaTheme.RuinedHillfort)
            {
                // Вид меняет рисунок арены при сборке: тот же seed и состав заново.
                foreach (string view in new[] { MapLabMapViews.ClassicId, MapLabMapViews.GloomId, MapLabMapViews.SepiaId })
                {
                    MapLabMapViews.ActiveId = view;
                    yield return StartScenario(sandbox, s.Theme, s.Seed, s.Fighters, s.Enemies);
                    SandboxBattle again = (SandboxBattle)Member(sandbox, "battle");
                    Assert.AreEqual(battle.GroundCells.Count, again.GroundCells.Count, "Вид не меняет раскладку.");
                    again.AddEffect(again.Units.First(u => u.Team == SandboxTeam.Enemy).Position.Neighbors().First(c => again.CanPlaceEffect(c, SandboxEffectKind.Fire) && again.GetUnitAt(c) == null), SandboxEffectKind.Fire, 2);
                    Invoke(sandbox, "RefreshBattleScreen");
                    for (int i = 0; i < 40; i++) yield return null;
                    yield return Shot(s.Name + "_view_" + view);
                }
                MapLabMapViews.ActiveId = MapLabMapViews.ClassicId;
            }
            int objects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length;
            Log(s.Name + ": объектов сцены " + objects + ", RenderTexture " + Resources.FindObjectsOfTypeAll<RenderTexture>().Length);
            if (objectsAfterFirst < 0)
                objectsAfterFirst = objects;
        }
        MapLabMapViews.ActiveId = savedView;

        // Замеры (до/после): прежний генератор против темы; путь и ход ИИ.
        Measure(null, "прежний генератор");
        foreach (SandboxArenaTheme theme in new[] { SandboxArenaTheme.FordBridge, SandboxArenaTheme.BogGlade, SandboxArenaTheme.RuinedHillfort })
            Measure(theme, SandboxArenaThemes.Label(theme));
    }

    private static void Measure(SandboxArenaTheme? theme, string label)
    {
        List<SandboxUnitDefinition> players = SandboxRoster.PlayerRoster.Take(5).ToList();
        List<SandboxUnitDefinition> enemies = SandboxRoster.EnemyRoster.Concat(SandboxRoster.EnemyRoster).Take(6).ToList();
        Stopwatch watch = Stopwatch.StartNew();
        const int builds = 40;
        for (int i = 0; i < builds; i++)
            SandboxRoster.CreateBattle(players, enemies, 100 + i, theme: theme);
        double createMs = watch.Elapsed.TotalMilliseconds / builds;

        SandboxBattle battle = SandboxRoster.CreateBattle(players, enemies, 7, theme: theme);
        watch.Restart();
        const int paths = 200;
        for (int i = 0; i < paths; i++)
            battle.GetReachable(battle.CurrentUnit.Id);
        double pathMs = watch.Elapsed.TotalMilliseconds / paths;

        int turns = 0;
        watch.Restart();
        while (battle.Phase == SandboxBattlePhase.InProgress && turns < 60)
        {
            SandboxAiExecutor.TakeTurn(battle);
            turns++;
        }
        double aiMs = watch.Elapsed.TotalMilliseconds / Mathf.Max(1, turns);
        Log($"замер «{label}» (5×6, арена 10×7): создание боя {createMs:0.00} мс, пересчёт пути {pathMs:0.000} мс, ход ИИ {aiMs:0.00} мс ({turns} ходов)");
    }
}
