using System.Collections;
using System.Collections.Generic;
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

// Бриф правок (22 пункта) п. 20: бой в Старой шахте — полный путь игрока
// настоящими событиями указателя: дождаться своего хода, шагнуть кликом по
// клетке, ударить врага, закончить ход; враги отвечают в очередь.
public sealed class OldMineBattlePlayModeTests
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

    // Клик настоящим событием указателя в точке панели: цель выбирает сама
    // панель (что сверху — то и получает клик).
    private static void ClickAt(IPanel panel, Vector2 point, int button = 0)
    {
        Event down = new Event { type = EventType.MouseDown, mousePosition = point, button = button, clickCount = 1 };
        using (PointerDownEvent evt = PointerDownEvent.GetPooled(down))
            panel.visualTree.SendEvent(evt);
        Event up = new Event { type = EventType.MouseUp, mousePosition = point, button = button, clickCount = 1 };
        using (PointerUpEvent evt = PointerUpEvent.GetPooled(up))
            panel.visualTree.SendEvent(evt);
    }

    private static IEnumerator CaptureShot(MonoBehaviour main, string name)
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
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), image.EncodeToPNG());
        Object.Destroy(image);
        texture.Release();
        Object.Destroy(texture);
        yield return Frames(2);
    }

    private static string PickPath(IPanel panel, Vector2 point)
    {
        VisualElement picked = panel.Pick(point);
        StringBuilder path = new StringBuilder();
        for (VisualElement e = picked; e != null && path.Length < 400; e = e.parent)
            path.Append(string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name).Append(" < ");
        return path.ToString();
    }

    private static Vector2 HexPoint(HexBoardElement board, HexCoord coord)
    {
        object layout = board.GetType().GetMethod("CalculateLayout", AnyInstance).Invoke(board, null);
        Vector2 local = (Vector2)layout.GetType().GetMethod("GetCenter").Invoke(layout, new object[] { coord });
        return board.LocalToWorld(local);
    }

    private static string State(object controller)
    {
        SandboxBattle battle = (SandboxBattle)Field(controller, "battle");
        if (battle == null)
            return "боя нет";
        SandboxUnitState current = battle.CurrentUnit;
        return "фаза " + battle.Phase + ", раунд " + battle.Round + ", ход " + (current != null ? current.Id + "/" + current.Team +
               " ОД " + current.ActionPoints + " движ " + current.RemainingMovement : "-") +
               ", анимация " + Field(controller, "combatAnimationRunning") + ", враг запланирован " + Field(controller, "enemyStepScheduled");
    }

    private static bool PlayerTurn(object controller)
    {
        SandboxBattle battle = (SandboxBattle)Field(controller, "battle");
        return battle != null && battle.Phase == SandboxBattlePhase.InProgress && battle.CurrentUnit != null &&
               battle.CurrentUnit.Team == SandboxTeam.Player && !(bool)Field(controller, "combatAnimationRunning");
    }

    [UnityTest]
    public IEnumerator OldMine_Battle_WithFighters_PlayerMovesAttacksAndEndsTurn_ByPointer()
    {
        return Run(new List<string> { "garrick", "torvin" }, "ostafiy");
    }

    // Герой в одиночку — как в живой партии, где бой застревал.
    [UnityTest]
    public IEnumerator OldMine_Battle_HeroAlone_PlayerMovesAttacksAndEndsTurn_ByPointer()
    {
        return Run(new List<string>(), null);
    }

    // Живая партия пользователя (путь к сохранению — KS_USER_SAVE): тот же
    // отряд, вещи и состояние шахты, что были при застревании боя.
    [UnityTest]
    public IEnumerator OldMine_Battle_FromUserSave_ByPointer()
    {
        string save = System.Environment.GetEnvironmentVariable("KS_USER_SAVE");
        if (string.IsNullOrEmpty(save) || !System.IO.File.Exists(save))
            Assert.Ignore("Нет KS_USER_SAVE — проверка живой партии не запускается.");
        return Run(null, null, save);
    }

    private IEnumerator Run(List<string> fighters, string retinue, string userSave = null)
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .First(b => b.GetType().Name == "PrototypeUIController");
        GameState campaign;
        if (userSave != null)
        {
            System.IO.Directory.CreateDirectory(CampaignSaveStore.DirectoryOverride);
            System.IO.File.Copy(userSave, System.IO.Path.Combine(CampaignSaveStore.DirectoryOverride, "campaign.autosave.json"), true);
            Assert.IsTrue((bool)Invoke(main, "LoadCampaign", CampaignSaveStore.AutosaveSlotId), "Сохранение загружено.");
            Invoke(main, "HideMainMenu");
            yield return Frames(10);
            campaign = CampaignSession.Current;
            TestPeople.AdmitAll(campaign);
            LocationData mine = campaign.FindLocation("mine");
            Assert.IsNotNull(mine, "Шахта есть на карте партии.");
            // Из поселения, где стоял отряд, — к шахте.
            if ((bool)Member(main, "IsSettlementScreenOpen"))
                Invoke(main, "CloseSettlementScreen");
            if ((bool)Member(main, "IsLocationInteractionActive"))
                Invoke(main, "CloseLocationInteraction");
            campaign.SettlementCamp = new SettlementCampData();
            yield return Frames(3);
            ExpeditionData expedition = campaign.ActiveExpedition;
            expedition.LocationId = mine.Id;
            expedition.CurrentMapXPercent = mine.MapXPercent;
            expedition.CurrentMapYPercent = mine.MapYPercent;
            expedition.Phase = CommanderState.AtLocation;
            expedition.Route.Clear();
            Debug.Log("Живая партия: отряд " + string.Join(",", expedition.FighterIds) + ", свита " + string.Join(",", expedition.RetinueIds));
        }
        else
        {
            Invoke(main, "StartNewGameFromMenu");
            yield return Frames(10);
            campaign = CampaignSession.Current;
            TestPeople.AdmitAll(campaign);
            campaign.ArmySupply = 30;
            campaign.Narrative.SetFlag("freeplay.mine.known");
            CampaignContent.Refresh(campaign);
            Assert.IsTrue(campaign.TryStartExpedition("mine", fighters, out string message, retinue), message);
            campaign.ActiveExpedition.Phase = CommanderState.AtLocation;
        }
        yield return null;
        Assert.IsTrue((bool)Invoke(main, "TryOpenLocationInteraction", "mine"), "Шахта не открылась: диалог " + DialogueActive(main) +
            ", окно места " + Member(main, "IsLocationInteractionActive") + ", поселение " + Member(main, "IsSettlementScreenOpen") +
            ", модальное " + Invoke(main, "HasBlockingModalWorkExceptCamp") + ": меню " + Member(main, "IsGameMenuOpen") +
            ", герой " + Member(main, "IsHeroScreenOpen") + ", решение " + campaign.HasPendingExpeditionDecision + ", инцидент " + Field(main, "openedIncident") +
            ", очередь " + Field(main, "activeQueuedModal") + "/" + ((ICollection)Field(main, "queuedModals")).Count);
        yield return Frames(5);

        yield return WalkTo(main, new Vector2(760, 600));
        Invoke(main, "OnLocalObjectClicked", "mine.dumps");
        yield return Until(() => DialogueActive(main), 10f);
        yield return Choose(main, "freeplay.mine.local.dumps.search");
        yield return Choose(main);
        yield return WalkTo(main, new Vector2(1180, 590));
        yield return Until(() => DialogueActive(main), 5f);
        Assert.IsTrue(DialogueActive(main), "Перед боем — реплика логова.");
        yield return Choose(main, "freeplay.mine.local.lair.fight");
        yield return Until(() => Field(main, "localBattle") != null, 5f);
        object controller = Field(main, "localBattle");
        Assert.IsNotNull(controller, "Бой начался.");
        IPanel panel = main.GetComponent<UIDocument>().rootVisualElement.panel;

        // Свой ход — после хода зверей (у них инициатива выше).
        StringBuilder log = new StringBuilder();
        string last = null;
        float deadline = Time.realtimeSinceStartup + 40f;
        while (!PlayerTurn(controller) && Time.realtimeSinceStartup < deadline)
        {
            string state = State(controller);
            if (state != last)
                log.AppendLine(state);
            last = state;
            yield return null;
        }
        Assert.IsTrue(PlayerTurn(controller), "Ход игрока не наступил:\n" + log);

        // Шаг кликом по свободной достижимой клетке рядом.
        SandboxBattle battle = (SandboxBattle)Field(controller, "battle");
        HexBoardElement board = (HexBoardElement)Field(controller, "board");
        SandboxUnitState hero = battle.CurrentUnit;
        HexCoord from = hero.Position;
        HexCoord step = from.Neighbors().FirstOrDefault(c => battle.IsInside(c) && battle.GetUnitAt(c) == null &&
            SandboxMovementPath.TryBuild(battle, hero.Id, c, out IReadOnlyList<HexCoord> path, out int cost) && cost > 0);
        Assert.AreNotEqual(from, step, "Есть куда шагнуть.");
                // Живой игрок сначала кликает по своему бойцу: это не открывает
        // модальное окно и не перехватывает следующий клик (раньше — да).
        ClickAt(panel, HexPoint(board, from));
        yield return Frames(5);
        Assert.AreEqual(DisplayStyle.None, main.GetComponent<UIDocument>().rootVisualElement.Q("sandbox-fighter-details-window").resolvedStyle.display,
            "Клик по своему бойцу не открывает окно сведений.");
        // ПКМ — сведения; закрываются кликом мимо.
        ClickAt(panel, HexPoint(board, from), 1);
        yield return Frames(3);
        Assert.AreEqual(DisplayStyle.Flex, main.GetComponent<UIDocument>().rootVisualElement.Q("sandbox-fighter-details-window").resolvedStyle.display,
            "ПКМ открывает сведения бойца.");
        yield return CaptureShot(main, "mine_battle_details");
        ClickAt(panel, new Vector2(20f, 300f));
        yield return Frames(3);
        Assert.AreEqual(DisplayStyle.None, main.GetComponent<UIDocument>().rootVisualElement.Q("sandbox-fighter-details-window").resolvedStyle.display,
            "Клик мимо закрывает сведения.");
        Vector2 point = HexPoint(board, step);
        string picked = PickPath(panel, point);
        ClickAt(panel, point);
        yield return Until(() => hero.Position != from && !(bool)Field(controller, "combatAnimationRunning"), 10f);
        Assert.AreNotEqual(from, hero.Position, "Клик по клетке не сдвинул бойца. Под курсором: " + picked + "\n" + State(controller) + "\n" + log);

        // Закончить ход кнопкой — очередь идёт дальше.
        Button endTurn = main.GetComponent<UIDocument>().rootVisualElement.Query<Button>().ToList()
            .FirstOrDefault(b => b.text == "ЗАКОНЧИТЬ ХОД" && b.resolvedStyle.display != DisplayStyle.None);
        Assert.IsNotNull(endTurn, "Есть кнопка «Закончить ход».");
        string heroId = hero.Id;
        using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
        {
            submit.target = endTurn;
            endTurn.SendEvent(submit);
        }
        yield return Until(() => battle.CurrentUnit == null || battle.CurrentUnit.Id != heroId, 5f);
        Assert.AreNotEqual(heroId, battle.CurrentUnit?.Id, "Ход передан дальше.");

        // Бой до конца: в свой ход — сблизиться и ударить кликами, закончить
        // ход; враги действуют сами. Застой без перемен дольше 20 с — ошибка.
        string lastState = null;
        float lastChange = Time.realtimeSinceStartup;
        float battleDeadline = Time.realtimeSinceStartup + 240f;
        int playerTurns = 0;
        int playerHits = 0;
        while (Field(main, "localBattle") != null && battle.Phase == SandboxBattlePhase.InProgress && Time.realtimeSinceStartup < battleDeadline)
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
                yield return null;
                continue;
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
                    ClickAt(panel, HexPoint(board, toward));
                    yield return Until(() => !(bool)Field(controller, "combatAnimationRunning") && !board.IsAnimating, 15f);
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

        Assert.AreNotEqual(SandboxBattlePhase.InProgress, battle.Phase, "Бой не закончился за отведённое время:\n" + log);
        Assert.Greater(playerHits, 0, "Игрок наносил удары кликом.");
        Debug.Log("Бой в шахте: ходов игрока " + playerTurns + ", попаданий " + playerHits + ", итог " + battle.Phase + "\n" + log +
                  "\nЖурнал боя:\n" + string.Join("\n", (IEnumerable<string>)Field(controller, "battleLog")));
        if (battle.Phase == SandboxBattlePhase.PlayerVictory)
        {
            Invoke(controller, "ReturnToCampaign");
            yield return Frames(5);
            Assert.IsNull(Field(main, "localBattle"), "После победы — снова исследование.");
            Assert.IsTrue(LocalExplorationService.IsActive(campaign));
        }
    }
}
