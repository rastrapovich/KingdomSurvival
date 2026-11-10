using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Арена MapLab в настоящей сцене полигона: кадр арены под доской, выбор
// мышью по видимому гексу, движение фигуры по рельефу без скачка, смена
// размера поля, время кадра против прежней картинки и серия входов в бой
// без накопления объектов и текстур.
public sealed class MapLabArenaPlayModeTests
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static object Member(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        if (field != null) return field.GetValue(target);
        return target.GetType().GetProperty(name, AnyInstance)?.GetValue(target);
    }

    private static object Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, AnyInstance).Invoke(target, args);

    private static IEnumerator LoadSandbox(List<MonoBehaviour> result)
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
        Invoke(sandbox, "ChangeCount", false, "guard", 1);
        Invoke(sandbox, "ChangeCount", false, "archer", 1);
        Invoke(sandbox, "ChangeCount", true, "wolf", 1);
        result.Add(sandbox);
    }

    // Начать бой и дождаться готовой арены (кадр под доской, рельеф подключён).
    private static IEnumerator StartAndWait(MonoBehaviour sandbox, List<MapLabArenaDriver> result)
    {
        Invoke(sandbox, "StartBattle");
        MapLabArenaDriver driver = null;
        bool loadingSeen = false;
        // Сборка идёт по шагам в фоне: ждать по времени, а не по числу кадров.
        float until = Time.realtimeSinceStartup + 90f;
        while (Time.realtimeSinceStartup < until)
        {
            BattlefieldView surface = (BattlefieldView)Member(sandbox, "BattlefieldSurface");
            loadingSeen |= surface?.Q("arena-loading") != null;
            driver = surface?.Relief as MapLabArenaDriver;
            if (driver != null && surface.HasSceneTexture)
                break;
            yield return null;
        }
        Assert.IsNotNull(driver, "Арена MapLab подключилась к полю.");
        Assert.IsTrue(loadingSeen, "Пока арена строится, поле закрыто экраном загрузки.");
        BattlefieldView shownSurface = (BattlefieldView)Member(sandbox, "BattlefieldSurface");
        Assert.IsNull(shownSurface.Q("arena-loading"), "Арена показана — экран загрузки снят.");
        Assert.IsFalse(MapLabArenaDriver.Loading);
        result.Add(driver);
    }

    [UnityTest]
    public IEnumerator Arena_PicksVisibleCells_MovesAlongRelief_SurvivesResize()
    {
        Assert.IsTrue(MapLabArena.Available, "Арена MapLab включена (MapLabGameAssets.ArenaInBattle).");
        List<MonoBehaviour> found = new List<MonoBehaviour>();
        yield return LoadSandbox(found);
        MonoBehaviour sandbox = found[0];
        List<MapLabArenaDriver> drivers = new List<MapLabArenaDriver>();
        yield return StartAndWait(sandbox, drivers);
        MapLabArenaDriver driver = drivers[0];
        Debug.Log($"Арена PlayMode: сборка {driver.Arena.BuildMs:0} мс, декор Базы ассетов {driver.Arena.UsesAssetDatabase}, элементов {driver.Arena.DecorFeatures}.");

        HexBoardElement board = (HexBoardElement)Member(sandbox, "Board");
        BattlefieldView surface = (BattlefieldView)Member(sandbox, "BattlefieldSurface");
        Assert.AreSame(driver, board.Relief, "Доска поднимает центры по тому же рельефу.");
        SandboxBattle battle = (SandboxBattle)Member(sandbox, "battle");
        for (int i = 0; i < 5; i++) yield return null;

        yield return CheckPicking(board, battle, "исходный размер");

        // Подъём центров совпадает с высотой нарисованной земли.
        object layout = Invoke(board, "CalculateLayout");
        MethodInfo getCenter = layout.GetType().GetMethod("GetCenter");
        MethodInfo getFlat = layout.GetType().GetMethod("GetFlatCenter");
        Rect frame = (Rect)layout.GetType().GetProperty("Frame").GetValue(layout);
        int hills = 0;
        foreach (HexCoord cell in BattlefieldFrame.ActiveCells(surface.Battlefield))
        {
            Vector2 flat = (Vector2)getFlat.Invoke(layout, new object[] { cell });
            Vector2 shown = (Vector2)getCenter.Invoke(layout, new object[] { cell });
            float lift = driver.Arena.LiftShare(new Vector2((flat.x - frame.x) / frame.width, (flat.y - frame.y) / frame.height)) * frame.height;
            Assert.AreEqual(flat.y - lift, shown.y, 0.01f, "Центр клетки — на земле");
            if (battle.GetTerrain(cell) == SandboxTerrain.Difficult)
            {
                hills++;
                Assert.Greater(flat.y - shown.y, frame.height * 0.01f, $"Клетка холма {cell.Q},{cell.R} видна выше");
            }
        }
        Assert.Greater(hills, 0);

        // Движение: фигура идёт по поднятым центрам, без скачка в конце шага.
        SandboxUnitState unit = battle.Units.First(item => item.Team == SandboxTeam.Player);
        HexCoord next = unit.Position.Neighbors().Where(cell => BattlefieldFrame.IsActiveCell(surface.Battlefield, cell.Q, cell.R) &&
                battle.GetTerrain(cell) != SandboxTerrain.Impassable && battle.GetUnitAt(cell) == null)
            .OrderByDescending(cell => battle.GetTerrain(cell) == SandboxTerrain.Difficult ? 1 : 0).First();
        bool done = false;
        Assert.IsTrue(board.PlayMoveAnimation(unit.Id, new List<HexCoord> { unit.Position, next }, () => done = true));
        Vector2 previous = (Vector2)Member(board, "movementVisualPosition");
        float size = (float)layout.GetType().GetProperty("Size").GetValue(layout);
        float largestStep = 0f;
        float stepUntil = Time.realtimeSinceStartup + 15f;
        while (!done && Time.realtimeSinceStartup < stepUntil)
        {
            yield return null;
            if (!board.IsAnimating) continue;
            Vector2 now = (Vector2)Member(board, "movementVisualPosition");
            largestStep = Mathf.Max(largestStep, Vector2.Distance(now, previous));
            previous = now;
        }
        Assert.IsTrue(done, "Шаг завершён.");
        Assert.Less(largestStep, size * 0.6f, "Без телепорта: шаг за кадр меньше половины гекса.");
        Vector2 end = (Vector2)getCenter.Invoke(layout, new object[] { next });
        Assert.Less(Vector2.Distance(previous, end), size * 0.35f, "Конец шага — у поднятого центра цели.");

        // Смена размера поля: кадр перерисован, выбор снова верный.
        Texture before = surface.Q<Image>("battlefield-background").image;

        surface.style.flexGrow = 0f;
        surface.style.width = 900f;
        surface.style.height = 600f;
        for (int i = 0; i < 30; i++) yield return null;
        Texture after = surface.Q<Image>("battlefield-background").image;
        Assert.IsNotNull(after, "Кадр арены на месте после смены размера.");
        Assert.AreEqual(16f / 9f, after.width / (float)after.height, 0.01f, "Кадр арены — 16:9 (UI растягивает его на кадр поля).");
        yield return CheckPicking(board, battle, "после смены размера");
        SaveShot(after, "arena-playmode.png");

        // Время кадра: арена против прежней картинки поля (тот же состав).
        float arenaMs = 0f;
        for (int i = 0; i < 120; i++) { yield return null; arenaMs += Time.unscaledDeltaTime * 1000f; }
        arenaMs /= 120f;
        MapLabGameAssets assets = MapLabGameAssets.Load();
        assets.ArenaInBattle = false;
        try
        {
            Invoke(sandbox, "BuildSetupScreen");
            for (int i = 0; i < 10; i++) yield return null;
            Invoke(sandbox, "StartBattle");
            for (int i = 0; i < 60; i++) yield return null;
            float baseMs = 0f;
            for (int i = 0; i < 120; i++) { yield return null; baseMs += Time.unscaledDeltaTime * 1000f; }
            baseMs /= 120f;
            Debug.Log($"Арена PlayMode: среднее время кадра — арена MapLab {arenaMs:0.0} мс, прежнее поле {baseMs:0.0} мс.");
        }
        finally
        {
            assets.ArenaInBattle = true;
        }
    }

    private static IEnumerator CheckPicking(HexBoardElement board, SandboxBattle battle, string when)
    {
        object layout = Invoke(board, "CalculateLayout");
        MethodInfo getCenter = layout.GetType().GetMethod("GetCenter");
        MethodInfo tryGet = typeof(HexBoardElement).GetMethod("TryGetHexAt", AnyInstance);
        int checkedCells = 0;
        foreach (HexCoord cell in SandboxArenaShape.Cells())
        {
            if (!(bool)Invoke(board, "IsBoardCell", cell))
                continue;
            Vector2 shown = (Vector2)getCenter.Invoke(layout, new object[] { cell });
            object[] args = { shown, null };
            Assert.IsTrue((bool)tryGet.Invoke(board, args), $"Клик по видимому центру {cell.Q},{cell.R} ({when})");
            Assert.AreEqual(cell, (HexCoord)args[1], $"Клик выбирает свою клетку {cell.Q},{cell.R} ({when})");
            checkedCells++;
        }
        Assert.Greater(checkedCells, 40);
        yield return null;
    }

    private static void SaveShot(Texture texture, string name)
    {
        if (!(texture is RenderTexture rt)) return;
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        shot.Apply();
        RenderTexture.active = previous;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/MapLabArena"));
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), shot.EncodeToPNG());
        Object.Destroy(shot);
    }

    [UnityTest]
    public IEnumerator TwentyBattles_DoNotAccumulateArenaObjectsOrTextures()
    {
        Assert.IsTrue(MapLabArena.Available);
        List<MonoBehaviour> found = new List<MonoBehaviour>();
        yield return LoadSandbox(found);
        MonoBehaviour sandbox = found[0];
        int Count() => Resources.FindObjectsOfTypeAll<GameObject>().Count(item => item.name == "MapLab Arena" || item.name == "MapLab Arena Camera");
        int Textures() => Resources.FindObjectsOfTypeAll<RenderTexture>().Count(item => item.name == "MapLab Arena");
        int Sets() => Resources.FindObjectsOfTypeAll<KingdomSurvival.MapLab.MapLabArtSet>().Count(item => item.name.StartsWith("Арена"));
        // Прогрев из меню полигона (своя арена) — не часть серии боёв.
        MapLabArenaPrewarm.Cancel();
        for (int i = 0; i < 3; i++) yield return null;
        int objectsBefore = Count(), texturesBefore = Textures(), setsBefore = Sets();
        long memoryBefore = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        List<double> builds = new List<double>();
        for (int round = 0; round < 20; round++)
        {
            List<MapLabArenaDriver> drivers = new List<MapLabArenaDriver>();
            yield return StartAndWait(sandbox, drivers);
            builds.Add(drivers[0].Arena.BuildMs);
            Invoke(sandbox, "BuildSetupScreen");
            for (int i = 0; i < 5; i++) yield return null;
        }
        for (int i = 0; i < 10; i++) yield return null;
        System.GC.Collect();
        yield return Resources.UnloadUnusedAssets();
        long memoryAfter = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        Debug.Log($"Арена PlayMode: 20 боёв, сборка {builds.Min():0}–{builds.Max():0} мс (средн. {builds.Average():0}), " +
                  $"память {memoryBefore / 1048576f:0} → {memoryAfter / 1048576f:0} МБ.");
        Assert.AreEqual(objectsBefore, Count(), "Объекты арены освобождены.");
        // Текстура последнего кадра освобождается отложенно (не в кадре, где её
        // ещё рисует панель): одна может ждать следующей арены.
        Assert.LessOrEqual(Textures(), texturesBefore + 1, "Текстуры кадра арены освобождены.");
        Assert.LessOrEqual(MapLabArena.PendingReleases, 1, "Отложенных текстур не больше одной.");
        Assert.AreEqual(setsBefore, Sets(), "Наборы рисунков арены освобождены.");
    }
}
