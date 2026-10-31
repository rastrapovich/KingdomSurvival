using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Бриф правок (19 пунктов) п. 11: настоящий бой с волками в пещере «Стук за
// завалом» — путь игрока из обычной свободной игры: вход, подход к логову,
// реплика, бой кликами указателя до исхода. Проверяется и расчёт, и показ:
// фигуры боя видны и стоят там же, где клетки поля, по которым кликает
// игрок; удары обеих сторон видны в журнале и меняют HP; после отступления
// и повторного входа последствия сохранены.
public sealed class KnockingCaveBattlePlayModeTests
{
    private const string MainScene = "Prototype_Main";
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string CaveId = "knocking_cave";
    private static readonly Vector2 Den = new Vector2(950f, 600f);

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

    private static object Field(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        Assert.IsNotNull(field, "Нет поля " + name);
        return field.GetValue(target);
    }

    private static object Member(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name, AnyInstance);
        return property != null ? property.GetValue(target) : Field(target, name);
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static IEnumerator Until(System.Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    private static bool DialogueActive(MonoBehaviour main) => (bool)Member(main, "IsNarrativeDialogueActive");

    private static IEnumerator Choose(MonoBehaviour main, string choiceId = null)
    {
        object session = Field(main, "narrativeDialogueSession");
        for (int i = 0; i < 20 && DialogueActive(main); i++)
        {
            object view = Invoke(session, "BuildView");
            IList choices = (IList)Member(view, "AvailableChoices");
            object choice = choiceId != null
                ? choices.Cast<object>().FirstOrDefault(c => (string)Member(c, "ChoiceId") == choiceId)
                : null;
            if (choice == null && choices.Count == 1)
                choice = choices[0];
            Assert.IsNotNull(choice, "Нет ответа " + choiceId);
            string id = (string)Member(choice, "ChoiceId");
            Invoke(main, "OnNarrativeDialogueChoiceSelected", id, (string)Member(choice, "Text"), Member(choice, "Kind"));
            yield return null;
            if (id == choiceId)
                break;
        }
    }

    private static IEnumerator WalkTo(MonoBehaviour main, Vector2 point)
    {
        Invoke(main, "OnLocalGroundClicked", point);
        yield return Until(() =>
        {
            LocalHexMover mover = (LocalHexMover)Field(main, "localMover");
            return mover == null || DialogueActive(main) || Field(main, "localBattle") != null || !mover.LeaderHasOrder;
        }, 20f);
    }

    private static void ClickAt(IPanel panel, Vector2 point, int button = 0)
    {
        Event down = new Event { type = EventType.MouseDown, mousePosition = point, button = button, clickCount = 1 };
        using (PointerDownEvent evt = PointerDownEvent.GetPooled(down))
            panel.visualTree.SendEvent(evt);
        Event up = new Event { type = EventType.MouseUp, mousePosition = point, button = button, clickCount = 1 };
        using (PointerUpEvent evt = PointerUpEvent.GetPooled(up))
            panel.visualTree.SendEvent(evt);
    }

    private static Vector2 HexPoint(HexBoardElement board, HexCoord coord)
    {
        object layout = board.GetType().GetMethod("CalculateLayout", AnyInstance).Invoke(board, null);
        Vector2 local = (Vector2)layout.GetType().GetMethod("GetCenter").Invoke(layout, new object[] { coord });
        return board.LocalToWorld(local);
    }

    private static string PickPath(IPanel panel, Vector2 point)
    {
        VisualElement picked = panel.Pick(point);
        StringBuilder path = new StringBuilder();
        for (VisualElement e = picked; e != null && path.Length < 300; e = e.parent)
            path.Append(string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name).Append(" < ");
        return path.ToString();
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
        Texture2D image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
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

    private static string State(object controller)
    {
        SandboxBattle battle = (SandboxBattle)Field(controller, "battle");
        if (battle == null)
            return "боя нет";
        SandboxUnitState current = battle.CurrentUnit;
        return "фаза " + battle.Phase + ", раунд " + battle.Round + ", ход " +
               (current != null ? current.Id + "/" + current.Team + " ОД " + current.ActionPoints + " движ " + current.RemainingMovement : "-") +
               ", анимация " + Field(controller, "combatAnimationRunning") + ", поле занято " + ((HexBoardElement)Field(controller, "board")).IsAnimating +
               ", HP: " + string.Join(" ", battle.Units.Select(u => u.Id + "=" + u.HitPoints));
    }

    private static bool PlayerTurn(object controller)
    {
        SandboxBattle battle = (SandboxBattle)Field(controller, "battle");
        return battle != null && battle.Phase == SandboxBattlePhase.InProgress && battle.CurrentUnit != null &&
               battle.CurrentUnit.Team == SandboxTeam.Player && !(bool)Field(controller, "combatAnimationRunning");
    }

    private static IEnumerator EnterCave(MonoBehaviour main, GameState campaign, List<string> fighters)
    {
        LocationData cave = campaign.FindLocation(CaveId);
        Assert.IsNotNull(cave, "Пещера есть в свободной игре.");
        cave.IsDiscovered = true;
        cave.IsVisibleOnMap = true;
        campaign.Food = 40;
        campaign.ArmySupply = 30;
        if (!campaign.HasActiveExpedition)
            Assert.IsTrue(campaign.TryStartExpedition(CaveId, fighters, out string message), message);
        campaign.ActiveExpedition.LocationId = CaveId;
        campaign.ActiveExpedition.CurrentMapXPercent = cave.MapXPercent;
        campaign.ActiveExpedition.CurrentMapYPercent = cave.MapYPercent;
        campaign.ActiveExpedition.Phase = CommanderState.AtLocation;
        yield return null;
        Assert.IsTrue((bool)Invoke(main, "TryOpenLocationInteraction", CaveId), "Пещера открылась.");
        yield return Frames(5);
        Assert.IsTrue(LocalExplorationService.IsActive(campaign), "Внутри пещеры.");
    }

    private static IEnumerator StartDenBattle(MonoBehaviour main)
    {
        yield return WalkTo(main, Den);
        yield return Until(() => DialogueActive(main) || Field(main, "localBattle") != null, 5f);
        if (DialogueActive(main))
            yield return Choose(main, "cave.den.fight");
        yield return Until(() => Field(main, "localBattle") != null, 5f);
        Assert.IsNotNull(Field(main, "localBattle"), "Бой с волками начался.");
    }

    [UnityTest]
    public IEnumerator Cave_WolfFight_HeroAlone_VisibleAndPlayable()
    {
        return Run(new List<string>());
    }

    // Подготовленный старт столкновения — со спутницей Агнессой (данные пещеры).
    [UnityTest]
    public IEnumerator Cave_WolfFight_WithAgnessaAndGarrick_VisibleAndPlayable()
    {
        return Run(new List<string> { "agnessa", "garrick" });
    }

    // Отход после удара волка, повторный вход и загрузка: раны героя и волков
    // не сбрасываются, бой начинается с сохранённых HP.
    [UnityTest]
    public IEnumerator Cave_RetreatAfterWolfHits_ReturnAndLoad_KeepConsequences()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .First(b => b.GetType().Name == "PrototypeUIController");
        Invoke(main, "StartNewFreePlayFromMenu");
        yield return Frames(20);
        GameState campaign = CampaignSession.Current;
        yield return EnterCave(main, campaign, new List<string> { "garrick" });
        yield return StartDenBattle(main);
        object controller = Field(main, "localBattle");
        SandboxBattle battle = (SandboxBattle)Field(controller, "battle");
        yield return Until(() => PlayerTurn(controller), 30f);
        int heroHpInBattle = battle.Units.First(u => u.Id == "player:commander").HitPoints;
        Dictionary<string, int> wolvesHp = battle.Units.Where(u => u.Team == SandboxTeam.Enemy).ToDictionary(u => u.Id, u => u.HitPoints);
        Invoke(controller, "RetreatFromCampaignBattle");
        yield return Frames(5);
        Assert.IsNull(Field(main, "localBattle"), "Отступили.");
        ResidentState hero = HomePeopleService.Find(campaign, campaign.GetSelectedCommander().Id);
        Assert.AreEqual(heroHpInBattle, hero.CurrentHitPoints, "Раны героя после отхода сохранены.");
        LocalLocationDefinition cave = LocalExplorationService.ActiveDefinition(campaign);
        foreach (LocalActorStateData wolf in LocalExplorationService.AliveEnemies(campaign, cave))
            Assert.AreEqual(wolvesHp["enemy:" + wolf.ActorId], wolf.HitPoints, "Раны волка " + wolf.ActorId + " сохранены.");

        Assert.IsTrue((bool)Invoke(main, "SaveCampaign", CampaignSaveStore.ManualSlotIds[0]));
        Assert.IsTrue((bool)Invoke(main, "LoadCampaign", CampaignSaveStore.ManualSlotIds[0]));
        yield return Frames(10);
        campaign = CampaignSession.Current;
        Assert.IsTrue(LocalExplorationService.IsActive(campaign), "После загрузки — снова в пещере.");
        yield return StartDenBattle(main);
        controller = Field(main, "localBattle");
        battle = (SandboxBattle)Field(controller, "battle");
        Assert.AreEqual(heroHpInBattle, battle.Units.First(u => u.Id == "player:commander").HitPoints, "Новый бой — с ранами героя.");
        foreach (SandboxUnitState wolf in battle.Units.Where(u => u.Team == SandboxTeam.Enemy))
            Assert.AreEqual(wolvesHp[wolf.Id], wolf.HitPoints, "Волк " + wolf.Id + " не восстановлен молча.");
    }

    private IEnumerator Run(List<string> fighters)
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .First(b => b.GetType().Name == "PrototypeUIController");
        Invoke(main, "StartNewFreePlayFromMenu");
        yield return Frames(20);
        GameState campaign = CampaignSession.Current;
        IPanel panel = main.GetComponent<UIDocument>().rootVisualElement.panel;

        yield return EnterCave(main, campaign, fighters);
        yield return StartDenBattle(main);
        object controller = Field(main, "localBattle");
        SandboxBattle battle = (SandboxBattle)Field(controller, "battle");
        HexBoardElement board = (HexBoardElement)Field(controller, "board");
        object renderer = Field(main, "localRenderer");
        yield return Frames(5);
        yield return Capture(main, "cave_01_battle_start");

        // Показ и расчёт: фигура каждого бойца стоит там же, где его клетка.
        StringBuilder log = new StringBuilder();
        Assert.Greater((int)Member(renderer, "BattleFigureCount"), 0, "Фигуры боя нарисованы.");
        IList frames = (IList)Field(main, "localBattleFrames");
        log.AppendLine("Фигуры: " + frames.Count + ", бойцов: " + battle.Units.Count);
        Rect field = board.worldBound;
        foreach (SandboxUnitState unit in battle.Units)
        {
            Vector2 cell = HexPoint(board, unit.Position);
            log.AppendLine(unit.Id + " клетка " + unit.Position + " на экране " + cell + ", внутри поля " + field.Contains(cell) +
                           ", под курсором: " + PickPath(panel, cell));
            Assert.IsTrue(field.Contains(cell), "Клетка " + unit.Id + " — на видимом поле.\n" + log);
        }

        // Бой до конца кликами; застой дольше 20 с — ошибка.
        string lastState = null;
        float lastChange = Time.realtimeSinceStartup;
        float deadline = Time.realtimeSinceStartup + 240f;
        int playerTurns = 0;
        int playerHits = 0;
        int enemyHits = 0;
        bool shotAfterWolf = false;
        // Показ: кадры анимации и положение фигур, которые рисует рендерер
        // места, пока ходят волки (удар волка, ответ героя).
        Dictionary<string, HashSet<Sprite>> sprites = new Dictionary<string, HashSet<Sprite>>();
        Dictionary<string, (Vector2 Min, Vector2 Max)> moved = new Dictionary<string, (Vector2, Vector2)>();
        Button endTurn = main.GetComponent<UIDocument>().rootVisualElement.Query<Button>().ToList()
            .First(b => b.text == "ЗАКОНЧИТЬ ХОД");
        while (Field(main, "localBattle") != null && battle.Phase == SandboxBattlePhase.InProgress && Time.realtimeSinceStartup < deadline)
        {
            string state = State(controller);
            if (state != lastState)
            {
                log.AppendLine(state);
                lastState = state;
                lastChange = Time.realtimeSinceStartup;
            }
            Assert.Less(Time.realtimeSinceStartup - lastChange, 20f, "Бой застыл:\n" + log);
            if (!PlayerTurn(controller))
            {
                if (!shotAfterWolf)
                {
                    foreach (object frame in (IList)Field(main, "localBattleFrames"))
                    {
                        string id = (string)frame.GetType().GetField("Id").GetValue(frame);
                        Sprite sprite = (Sprite)frame.GetType().GetField("Sprite").GetValue(frame);
                        Vector2 at = ((Rect)frame.GetType().GetField("Rect").GetValue(frame)).center;
                        if (!sprites.ContainsKey(id))
                        {
                            sprites[id] = new HashSet<Sprite>();
                            moved[id] = (at, at);
                        }
                        if (sprite != null)
                            sprites[id].Add(sprite);
                        moved[id] = (Vector2.Min(moved[id].Min, at), Vector2.Max(moved[id].Max, at));
                    }
                }
                yield return null;
                continue;
            }
            if (!shotAfterWolf)
            {
                shotAfterWolf = true;
                yield return Capture(main, "cave_02_after_wolf");
            }

            playerTurns++;
            SandboxUnitState unit = battle.CurrentUnit;
            SandboxUnitState enemy = battle.FindClosestOpponent(unit);
            if (enemy == null)
                break;
            int enemyHp = enemy.HitPoints;
            ClickAt(panel, HexPoint(board, enemy.Position));
            yield return Until(() => !(bool)Field(controller, "combatAnimationRunning") && !board.IsAnimating, 15f);
            if (enemy.HitPoints == enemyHp && battle.CurrentUnit == unit && unit.RemainingMovement > 0)
            {
                HexCoord toward = battle.FindBestMoveToward(unit, enemy.Position);
                if (toward != unit.Position)
                {
                    HexCoord before = unit.Position;
                    ClickAt(panel, HexPoint(board, toward));
                    yield return Until(() => !(bool)Field(controller, "combatAnimationRunning") && !board.IsAnimating, 15f);
                    Assert.AreNotEqual(before, unit.Position, "Клик по клетке сдвинул бойца. Под курсором: " +
                                                              PickPath(panel, HexPoint(board, toward)) + "\n" + log);
                    if (battle.CurrentUnit == unit)
                    {
                        ClickAt(panel, HexPoint(board, enemy.Position));
                        yield return Until(() => !(bool)Field(controller, "combatAnimationRunning") && !board.IsAnimating, 15f);
                    }
                }
            }
            if (enemy.HitPoints < enemyHp)
                playerHits++;
            if (battle.Phase == SandboxBattlePhase.InProgress && battle.CurrentUnit == unit && PlayerTurn(controller))
            {
                using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = endTurn;
                    endTurn.SendEvent(submit);
                }
                yield return Frames(2);
            }
        }
        List<string> journal = ((IEnumerable<string>)Field(controller, "battleLog")).ToList();
        enemyHits = journal.Count(l => l.Contains("Волк") && l.Contains("наносит"));
        Debug.Log("Бой в пещере: ходов игрока " + playerTurns + ", попаданий " + playerHits + ", итог " + battle.Phase +
                  "\n" + log + "\nЖурнал боя:\n" + string.Join("\n", journal));
        Assert.AreNotEqual(SandboxBattlePhase.InProgress, battle.Phase, "Бой дошёл до исхода:\n" + log);
        Assert.Greater(playerHits, 0, "Игрок наносил удары кликом.");
        Assert.Greater(enemyHits, 0, "Удары волков видны в журнале боя.");
        string shown = string.Join("; ", sprites.Select(p => p.Key + ": кадров " + p.Value.Count + ", сдвиг " +
                                                            (moved[p.Key].Max - moved[p.Key].Min).magnitude.ToString("F0")));
        Debug.Log("Показ первого хода волков: " + shown);
        // У героя (набор «militia») кадры анимации меняются. У волка набора
        // анимаций в Базе существ нет (animationSetId пуст) — он показан
        // статичной картинкой с выпадом, вспышкой и числом урона.
        if (sprites.TryGetValue("player:commander", out HashSet<Sprite> heroFrames))
            Assert.Greater(heroFrames.Count, 1, "Герой анимируется (" + shown + ").");
        yield return Capture(main, "cave_03_battle_end");
    }
}
