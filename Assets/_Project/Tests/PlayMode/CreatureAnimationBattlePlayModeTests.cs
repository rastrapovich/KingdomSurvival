using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.BattleSandbox;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// ПР-12З: проигрывание анимаций на настоящем поле боя в сцене BattleSandbox.
// Набор собирается в памяти из крошечных кадров с именами «Действие_Ракурс_N»,
// поэтому по имени текущего спрайта видно, что показывает каждый боец.
public sealed class CreatureAnimationBattlePlayModeTests
{
    private const string BattleScene = "BattleSandbox";
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private readonly List<Object> created = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in created)
        {
            if (item != null)
                Object.Destroy(item);
        }
        created.Clear();
        CampaignSession.Reset();
    }

    [UnityTest]
    public IEnumerator AnimatedCreatures_WalkAttackReactDie_IndependentlyAndReleaseCleanly()
    {
        SceneManager.LoadScene(BattleScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != BattleScene; i++)
            yield return null;
        UIDocument document = null;
        for (int i = 0; i < 120 && document == null; i++)
        {
            document = Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None).FirstOrDefault(d => d.rootVisualElement != null);
            yield return null;
        }
        Assert.IsNotNull(document, "Сцена боя открыта.");

        // Отдельное поле поверх экрана выбора состава: контроллер его не трогает.
        HexBoardElement board = new HexBoardElement { name = "test-animation-board" };
        board.style.position = Position.Absolute;
        board.style.left = 0f;
        board.style.top = 0f;
        board.style.width = 900f;
        board.style.height = 620f;
        document.rootVisualElement.Add(board);

        CreatureAnimationSetData set = BuildSet();
        SetVisuals(board, set);

        // --- Сцена 1: удар насмерть. Второе существо того же типа не реагирует.
        SandboxBattle killBattle = Battle(heroDamage: 50, beastHp: 10, beastPosition: new HexCoord(2, 2), extraBeastPosition: new HexCoord(4, 4));
        board.SetBattle(killBattle, null);
        yield return WaitFrames(6);

        Assert.AreEqual(3, Images(board).Count, "Все трое нарисованы картинками набора.");
        StringAssert.StartsWith("Idle_Front_", SpriteName(board, "hero"), "Герой слева смотрит вправо (таблица ракурсов по умолчанию: Front — вправо).");
        StringAssert.StartsWith("Idle_Back_", SpriteName(board, "beastA"), "Противник справа смотрит влево (Back).");

        int mark = killBattle.HitRecords.Count;
        Assert.IsTrue(killBattle.TryAttack("hero", "beastA", out string message), message);
        List<SandboxHitRecord> records = killBattle.GetHitRecordsSince(mark);
        Assert.AreEqual(1, records.Count);
        bool done = false;
        Assert.IsTrue(PlayHitSequence(board, records, () => done = true));
        yield return WaitFrames(3);
        StringAssert.StartsWith("Attack_", SpriteName(board, "hero"));
        StringAssert.StartsWith("Idle_", SpriteName(board, "beastB"), "Удар по одному не переключает другого того же типа.");
        Assert.IsTrue(HealthBars(board).Contains("beastA"), "До маркера павший ещё жив на поле.");

        yield return WaitUntil(() => done, 5f);
        Assert.IsTrue(done, "Очередь не зависла.");
        Assert.AreEqual(0, killBattle.GetUnit("beastA").HitPoints, "Исход посчитан моделью один раз.");
        StringAssert.StartsWith("Death_", SpriteName(board, "beastA"), "Павший остаётся кадром смерти.");
        yield return WaitSeconds(0.6f);
        Assert.AreEqual("Death_Back_3", SpriteName(board, "beastA"), "Смерть держит последний кадр и не возвращается к ожиданию.");
        Assert.IsFalse(HealthBars(board).Contains("beastA"), "У павшего нет полосы здоровья.");
        StringAssert.StartsWith("Idle_", SpriteName(board, "hero"), "После атаки — снова ожидание.");

        // --- Сцена 2: ходьба с поворотом, удар без смерти и ответный удар.
        SandboxBattle duel = Battle(heroDamage: 3, beastHp: 40, beastPosition: new HexCoord(3, 3), extraBeastPosition: new HexCoord(5, 0));
        board.SetBattle(duel, null);
        yield return WaitFrames(4);

        List<HexCoord> path = new List<HexCoord> { new HexCoord(1, 2), new HexCoord(2, 2), new HexCoord(2, 3) };
        bool moved = false;
        Assert.IsTrue(board.PlayMoveAnimation("hero", path, () => moved = duel.TryMove("hero", new HexCoord(2, 3), out _)));
        yield return WaitFrames(2);
        StringAssert.StartsWith("Walk_Front_", SpriteName(board, "hero"), "Первый отрезок — вправо.");
        yield return WaitUntil(() => moved, 3f);
        Assert.IsTrue(moved);
        StringAssert.StartsWith("Idle_FrontLeft_", SpriteName(board, "hero"), "Последний отрезок — вправо-вниз: ракурс сменился на повороте.");

        mark = duel.HitRecords.Count;
        Assert.IsTrue(duel.TryAttack("hero", "beastA", out message), message);
        Assert.IsTrue(duel.HasPendingRetaliation);
        Assert.IsTrue(duel.TryResolvePendingRetaliation(out message), message);
        records = duel.GetHitRecordsSince(mark);
        Assert.AreEqual(2, records.Count);
        done = false;
        Assert.IsTrue(PlayHitSequence(board, records, () => done = true));
        bool beastWasHit = false;
        bool beastAnswered = false;
        bool heroWasHit = false;
        float deadline = Time.realtimeSinceStartup + 5f;
        while (!done && Time.realtimeSinceStartup < deadline)
        {
            beastWasHit |= SpriteName(board, "beastA").StartsWith("Hit_", StringComparison.Ordinal);
            beastAnswered |= beastWasHit && SpriteName(board, "beastA").StartsWith("Attack_", StringComparison.Ordinal);
            heroWasHit |= beastAnswered && SpriteName(board, "hero").StartsWith("Hit_", StringComparison.Ordinal);
            yield return null;
        }
        Assert.IsTrue(done);
        Assert.IsTrue(beastWasHit, "Живое существо реагирует на удар.");
        Assert.IsTrue(beastAnswered, "Ответный удар — отдельная атака после реакции.");
        Assert.IsTrue(heroWasHit, "Атакующий получает ответ.");
        Assert.AreEqual(37, duel.GetUnit("beastA").HitPoints);

        // --- Освобождение: поле снимается с экрана, таймеры не бегут дальше.
        board.RemoveFromHierarchy();
        yield return WaitFrames(10);
    }

    private CreatureAnimationSetData BuildSet()
    {
        CreatureAnimationSetData set = new CreatureAnimationSetData("test_shared", "Тестовый набор");
        set.SetCanvasSize(new Vector2Int(16, 16));
        foreach (CreatureAnimationAction action in new[]
                 {
                     CreatureAnimationAction.Idle, CreatureAnimationAction.Walk, CreatureAnimationAction.Attack,
                     CreatureAnimationAction.Hit, CreatureAnimationAction.Death
                 })
        {
            CreatureAnimationClipData clip = set.GetOrAddClip(action);
            foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
            {
                List<Sprite> frames = new List<Sprite>();
                for (int i = 0; i < 4; i++)
                {
                    Texture2D texture = new Texture2D(16, 16);
                    created.Add(texture);
                    Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0f));
                    sprite.name = action + "_" + direction + "_" + i;
                    created.Add(sprite);
                    frames.Add(sprite);
                }
                clip.GetOrAddDirection(direction).SetFrames(frames, new[] { 1, 2, 3, 4 });
            }
        }
        return set;
    }

    private static void SetVisuals(HexBoardElement board, CreatureAnimationSetData set)
    {
        Type visualType = typeof(HexBoardElement).Assembly.GetType("KingdomSurvival.BattleSandbox.SandboxUnitVisual");
        IDictionary visuals = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), visualType));
        foreach (string typeId in new[] { "hero", "beast" })
        {
            visuals[typeId] = Activator.CreateInstance(
                visualType,
                AnyInstance,
                null,
                new object[] { null, null, 1f, Vector2.zero, string.Empty, 1f, set },
                null);
        }
        MethodInfo setVisuals = typeof(HexBoardElement).GetMethod("SetUnitVisuals", AnyInstance);
        setVisuals.Invoke(board, new object[] { visuals, null });
    }

    private static bool PlayHitSequence(HexBoardElement board, List<SandboxHitRecord> records, Action onComplete)
    {
        MethodInfo play = typeof(HexBoardElement).GetMethod("PlayHitSequence", AnyInstance);
        return (bool)play.Invoke(board, new object[] { records, onComplete });
    }

    private static SandboxBattle Battle(int heroDamage, int beastHp, HexCoord beastPosition, HexCoord extraBeastPosition)
    {
        SandboxUnitDefinition hero = new SandboxUnitDefinition("hero", "Герой", SandboxUnitRole.Militia, 40, 3, 3, heroDamage, 4, 10, 1);
        SandboxUnitDefinition beast = new SandboxUnitDefinition("beast", "Зверь", SandboxUnitRole.Beast, beastHp, 3, 3, 3, 3, 5, 1);
        SandboxBattle battle = new SandboxBattle(
            6,
            5,
            new[]
            {
                new SandboxUnitState("hero", hero, SandboxTeam.Player, new HexCoord(1, 2)),
                new SandboxUnitState("beastA", beast, SandboxTeam.Enemy, beastPosition),
                new SandboxUnitState("beastB", beast, SandboxTeam.Enemy, extraBeastPosition)
            });
        battle.Start();
        return battle;
    }

    private static IDictionary Images(HexBoardElement board)
    {
        return (IDictionary)typeof(HexBoardElement).GetField("unitImages", AnyInstance).GetValue(board);
    }

    private static ICollection<string> HealthBars(HexBoardElement board)
    {
        IDictionary bars = (IDictionary)typeof(HexBoardElement).GetField("unitHealthBars", AnyInstance).GetValue(board);
        return bars.Keys.Cast<string>().ToList();
    }

    private static string SpriteName(HexBoardElement board, string unitId)
    {
        IDictionary images = Images(board);
        Assert.IsTrue(images.Contains(unitId), "Нет картинки бойца " + unitId);
        Sprite sprite = ((Image)images[unitId]).sprite;
        return sprite != null ? sprite.name : string.Empty;
    }

    private static IEnumerator WaitFrames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static IEnumerator WaitSeconds(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until)
            yield return null;
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeout)
    {
        float until = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < until)
            yield return null;
    }
}
