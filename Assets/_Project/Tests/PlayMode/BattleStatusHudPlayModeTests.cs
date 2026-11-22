using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using KingdomSurvival.BattleSandbox;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

// Бриф завершения, этапы 15 и 18: значки состояний над бойцами в настоящем
// полигоне — 13 бойцов по 12 состояний, «+N» после шести, исчезновение после
// гибели, замер времени кадра и памяти в установившемся режиме.
public sealed class BattleStatusHudPlayModeTests
{
    private const string BattleScene = "BattleSandbox";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [SetUp]
    public void SetUp() => CampaignSession.Reset();

    [TearDown]
    public void TearDown() => CampaignSession.Reset();

    private static object Get(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, Any);
        if (field != null)
            return field.GetValue(target);
        PropertyInfo property = target.GetType().GetProperty(name, Any);
        Assert.IsNotNull(property, "Нет члена " + name);
        return property.GetValue(target);
    }

    private static object Call(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, Any);
        Assert.IsNotNull(info, "Нет метода " + method);
        return info.Invoke(target, args);
    }

    [UnityTest]
    public IEnumerator ThirteenFighters_TwelveStatuses_IconsCapped_FrameAndMemoryMeasured()
    {
        SceneManager.LoadScene(BattleScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != BattleScene; i++)
            yield return null;
        MonoBehaviour sandbox = null;
        for (int i = 0; i < 120 && (sandbox == null || !(bool)Get(sandbox, "initialized")); i++)
        {
            sandbox = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(b => b.GetType().Name == "BattleSandboxController");
            yield return null;
        }
        Assert.IsNotNull(sandbox);

        for (int i = 0; i < 5; i++)
            Call(sandbox, "ChangeCount", false, "guard", 1);
        for (int i = 0; i < 8; i++)
            Call(sandbox, "ChangeCount", true, "wolf", 1);
        Call(sandbox, "StartBattle");
        float arenaDeadline = Time.realtimeSinceStartup + 60f;
        while (MapLabArenaDriver.Loading && Time.realtimeSinceStartup < arenaDeadline)
            yield return null;
        Assert.IsFalse(MapLabArenaDriver.Loading, "Арена собрана.");

        SandboxBattle battle = (SandboxBattle)Get(sandbox, "battle");
        Assert.IsNotNull(battle);
        List<SandboxUnitState> fighters = battle.Units.Where(u => !u.IsDefeated).ToList();
        Assert.GreaterOrEqual(fighters.Count, 10, "в бою 10+ участников");

        // Двенадцать состояний без сильного контроля на каждом (бой должен идти).
        List<string> pool = SandboxStatusCatalog.All.Where(def => !def.StrongControl).Select(def => def.Id).ToList();
        int applied = 0;
        foreach (SandboxUnitState unit in fighters)
        {
            int onUnit = 0;
            SandboxUnitState source = fighters.First(other => other.Team != unit.Team);
            foreach (string id in pool)
            {
                if (onUnit >= 12)
                    break;
                if (battle.ApplyStatus(id, unit, source).Applied)
                    onUnit++;
            }
            applied += onUnit;
        }
        Assert.Greater(applied, fighters.Count * 6, "больше шести состояний на бойце");

        // Ждём, пока поле перестроит группы значков под новые состояния.
        object board = Get(sandbox, "board");
        IDictionary groups = (IDictionary)Get(board, "statusGroups");
        bool LaidOut(SandboxUnitState unit)
        {
            if (!groups.Contains(unit.Id))
                return false;
            object g = groups[unit.Id];
            return ((IList)Get(g, "Views")).Count > 6 && (int)Get(g, "VisibleCount") > 0 && Get(g, "More") != null;
        }
        for (int i = 0; i < 300 && !fighters.Where(u => battle.GetStatusViews(u.Id, SandboxTeam.Player).Count > 6).Any(LaidOut); i++)
            yield return null;
        for (int i = 0; i < 10; i++)
            yield return null;

        // Значки: не больше шести видимых и «+N» у каждого разложенного, у кого больше шести.
        int capped = 0;
        foreach (SandboxUnitState unit in fighters)
        {
            int views = battle.GetStatusViews(unit.Id, SandboxTeam.Player).Count;
            if (views <= 6 || !groups.Contains(unit.Id))
                continue;
            object group = groups[unit.Id];
            // Группа ещё со старым списком (боец вне видимой фигуры) — не проверяем.
            if (((IList)Get(group, "Views")).Count <= 6 || (int)Get(group, "VisibleCount") == 0)
                continue;
            int visible = (int)Get(group, "VisibleCount");
            Assert.LessOrEqual(visible, 6, unit.Id);
            object more = Get(group, "More");
            Assert.IsNotNull(more, unit.Id + ": есть «+N»");
            VisualElement moreRoot = (VisualElement)Get(more, "Root");
            Assert.AreNotEqual(DisplayStyle.None, moreRoot.resolvedStyle.display, unit.Id + ": «+N» виден");
            capped++;
        }
        Assert.Greater(capped, 0, "проверены группы с «+N»");

        // Замер: 180 кадров установившегося режима.
        System.GC.Collect();
        long monoBefore = Profiler.GetMonoUsedSizeLong();
        List<float> frames = new List<float>();
        Stopwatch watch = new Stopwatch();
        for (int i = 0; i < 180; i++)
        {
            watch.Restart();
            yield return null;
            frames.Add((float)watch.Elapsed.TotalMilliseconds);
        }
        long monoAfter = Profiler.GetMonoUsedSizeLong();
        frames.Sort();
        float avg = frames.Average();
        float p95 = frames[(int)(frames.Count * 0.95f)];
        float max = frames[frames.Count - 1];
        float growthMb = (monoAfter - monoBefore) / (1024f * 1024f);
        Debug.Log("[PERF][StatusHud] участников " + fighters.Count + ", состояний " + applied + ", кадр ср " + avg.ToString("0.0") +
                  " мс, p95 " + p95.ToString("0.0") + " мс, макс " + max.ToString("0.0") + " мс, рост Mono " + growthMb.ToString("0.00") + " МБ за 180 кадров");
        Assert.Less(growthMb, 32f, "нет заметного роста памяти в установившемся режиме");
        // Снятие значков после гибели — EditMode NewSystemsAiPreviewTests (HP здесь не изменить).
    }
}
