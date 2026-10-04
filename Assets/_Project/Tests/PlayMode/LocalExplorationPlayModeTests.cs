using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// ПР-12К (канон v1.54 §28.3, §28.6): Старая шахта на настоящей сцене.
// Свободная игра → отряд у шахты → вход открывает локальную карту (а не
// текстовое окно) → ходьба как на глобальной карте со спутниками → отвалы
// через существующий диалог → шаг за проход: реплика и бой поверх того же
// рисунка → отход к выходу →
// снова бой и победа → выход на карту → повторный вход без зверей.
// Контроллер — через рефлексию (Assembly-CSharp тестам недоступна).
public sealed class LocalExplorationPlayModeTests
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

    private static MonoBehaviour FindBehaviour(string typeName)
    {
        return Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .FirstOrDefault(behaviour => behaviour.GetType().Name == typeName);
    }

    private static object Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, AnyInstance);
        Assert.IsNotNull(info, "Нет метода " + method);
        return info.Invoke(target, args);
    }

    private static object GetField(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        Assert.IsNotNull(field, "Нет поля " + name);
        return field.GetValue(target);
    }

    private static object Member(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name, AnyInstance);
        if (property != null)
            return property.GetValue(target);
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        Assert.IsNotNull(field, "Нет поля " + name);
        return field.GetValue(target);
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static bool DialogueActive(MonoBehaviour main) => (bool)Member(main, "IsNarrativeDialogueActive");

    // Проходит открытый разговор: «дальше» пропускается; если ответ не
    // указан, выбирается единственный доступный.
    private static IEnumerator Choose(MonoBehaviour main, string choiceId = null)
    {
        object session = GetField(main, "narrativeDialogueSession");
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

    private static LocalFreeMover Mover(MonoBehaviour main) => (LocalFreeMover)GetField(main, "localMover");

    private static Vector2 Leader(MonoBehaviour main) => new Vector2((float)Mover(main).Leader.X, (float)Mover(main).Leader.Y);

    private static bool HasActor(MonoBehaviour main, string id) => (bool)Invoke(GetField(main, "localRenderer"), "HasActor", id);

    // Ждать условия по реальному времени: ходьба идёт с реальной скоростью.
    private static IEnumerator Until(System.Func<bool> condition, float seconds = 20f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    private static IEnumerator WalkTo(MonoBehaviour main, Vector2 point)
    {
        Invoke(main, "OnLocalGroundClicked", point);
        yield return Until(() =>
        {
            LocalFreeMover mover = Mover(main);
            return mover == null || DialogueActive(main) || GetField(main, "localBattle") != null || !mover.LeaderHasOrder;
        });
    }

    private static IEnumerator Capture(MonoBehaviour main, string name)
    {
        string directory = System.Environment.GetEnvironmentVariable("KS_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory))
            yield break;
        UIDocument document = main.GetComponent<UIDocument>();
        PanelSettings settings = document.panelSettings;
        RenderTexture previous = settings.targetTexture;
        RenderTexture texture = new RenderTexture(1920, 1080, 24);
        settings.targetTexture = texture;
        // В batchmode конца кадра нет (WaitForEndOfFrame не наступает):
        // панель рисуется в текстуру за обычные кадры.
        yield return Frames(4);
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = texture;
        Texture2D image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
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

    private static void ForceLocalVictory(object controller)
    {
        object battle = GetField(controller, "battle");
        foreach (object unit in (IEnumerable)battle.GetType().GetProperty("Units").GetValue(battle))
        {
            if (unit.GetType().GetProperty("Team").GetValue(unit).ToString() == "Enemy")
                unit.GetType().GetProperty("HitPoints").GetSetMethod(true).Invoke(unit, new object[] { 0 });
        }
        Invoke(battle, "EvaluateBattleOutcome");
        GetField(controller, "combatAnimationRunning");
        controller.GetType().GetField("combatAnimationRunning", AnyInstance).SetValue(controller, false);
    }

    [UnityTest]
    public IEnumerator OldMine_WalkSearchFightRetreatWinLeaveAndReturn()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        Invoke(main, "StartNewFreePlayFromMenu");
        yield return Frames(10);

        GameState campaign = CampaignSession.Current;
        campaign.Food = 40;
        campaign.ArmySupply = 30;
        campaign.Narrative.SetFlag("freeplay.mine.known");
        CampaignContent.Refresh(campaign);
        Assert.IsTrue(campaign.TryStartExpedition("mine", new List<string> { "garrick", "torvin" }, out string message, HomePeopleService.OstafiyId), message);
        campaign.ActiveExpedition.Phase = CommanderState.AtLocation;
        yield return null;

        // Вход: локальная карта вместо текстового окна.
        Assert.IsTrue((bool)Invoke(main, "TryOpenLocationInteraction", "mine"));
        yield return Frames(5);
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        Assert.AreEqual(DisplayStyle.Flex, root.Q("local-exploration-screen").resolvedStyle.display);
        Assert.IsFalse((bool)Member(main, "IsLocationInteractionActive"), "Текстовый вход не показан одновременно.");
        Assert.IsTrue(LocalExplorationService.IsActive(campaign));
        Assert.AreEqual(4, Mover(main).Members.Count, "Виден весь отряд: командир, двое бойцов и свита.");
        Assert.IsTrue(HasActor(main, "mine.beast.1") && HasActor(main, "mine.beast.2"), "Звери видны в логове.");
        Assert.IsTrue(Mover(main).Members.All(m => HasActor(main, m.Id)), "Отряд виден.");
        Assert.IsNotNull(root.Q<Image>("local-exploration-image").image, "Рисунок места рисует общий рендерер Базы локаций.");
        double hourAtEntry = ContinuousSimulationSystem.GetClock(campaign).HourOfDay;
        yield return Frames(30);
        Assert.AreEqual(hourAtEntry, ContinuousSimulationSystem.GetClock(campaign).HourOfDay, 1e-6, "Стоя на месте, время не идёт.");
        yield return Capture(main, "mine_01_entry");

        // Ходьба как на глобальной карте: спутники следом, без наложения;
        // время идёт пройденным путём.
        Vector2 hall = new Vector2(760, 600);
        yield return WalkTo(main, hall);
        yield return Until(() => Mover(main).IsIdle && Mover(main).IsGathered, 10f);
        LocalFreeMover mover = Mover(main);
        Assert.IsTrue(mover.IsGathered, "Спутники подошли следом.");
        Assert.Less(Vector2.Distance(hall, Leader(main)), 1f);
        for (int a = 0; a < mover.Members.Count; a++)
            for (int b = a + 1; b < mover.Members.Count; b++)
                Assert.Greater(Vector2.Distance(new Vector2((float)mover.Members[a].X, (float)mover.Members[a].Y),
                    new Vector2((float)mover.Members[b].X, (float)mover.Members[b].Y)), 20f, "Никто не стоит в другом: " +
                    string.Join("; ", mover.Members.Select(m => m.Id + " " + m.X.ToString("0") + "," + m.Y.ToString("0"))));
        Assert.Greater(ContinuousSimulationSystem.GetClock(campaign).HourOfDay, hourAtEntry, "Путь тратит время.");
        // Зажатая кнопка ведёт командира за курсором: новая точка сразу меняет
        // цель (стена — цель не меняется).
        Invoke(main, "OnLocalHeldAt", new Vector2(620, 470));
        Invoke(main, "OnLocalHeldAt", new Vector2(990, 400));
        Invoke(main, "OnLocalHeldAt", new Vector2(700, 680));
        yield return Until(() => !Mover(main).LeaderHasOrder, 10f);
        Assert.Less(Vector2.Distance(new Vector2(700, 680), Leader(main)), 1f, "Командир шёл за курсором до последней точки.");
        yield return WalkTo(main, hall);
        yield return Until(() => Mover(main).IsIdle && Mover(main).IsGathered, 10f);

        // Клик в камень — к ближайшей доступной точке, без телепорта.
        Invoke(main, "OnLocalGroundClicked", new Vector2(760, 150));
        Assert.IsFalse(Mover(main).LastOrderReachesTarget);
        yield return Until(() => !Mover(main).LeaderHasOrder, 10f);
        Assert.IsTrue(Mover(main).IsPassable(Leader(main).x, Leader(main).y));
        Assert.Less(Leader(main).y, 400f, "Командир дошёл до края выработки.");
        yield return WalkTo(main, hall);

        // Отвалы: подойти и перебрать — существующая сцена и флаг.
        Invoke(main, "OnLocalObjectClicked", "mine.dumps");
        yield return Until(() => DialogueActive(main));
        Assert.IsTrue(DialogueActive(main), "Сцена отвалов открылась у объекта.");
        yield return Capture(main, "mine_02_dumps_dialogue");
        yield return Choose(main, "freeplay.mine.local.dumps.search");
        yield return Choose(main);
        Assert.IsFalse(DialogueActive(main));
        Assert.IsTrue(campaign.Narrative.HasFlag("freeplay.mine.entered"));

        // Шаг за проход: реплика, затем бой на том же поле.
        yield return WalkTo(main, new Vector2(1180, 590));
        yield return Until(() => DialogueActive(main), 5f);
        Assert.IsTrue(DialogueActive(main), "Перед боем — реплика логова.");
        yield return Choose(main, "freeplay.mine.local.lair.fight");
        for (int i = 0; i < 30 && GetField(main, "localBattle") == null; i++)
            yield return null;
        object battleHost = GetField(main, "localBattle");
        Assert.IsNotNull(battleHost, "Бой начался на месте.");
        Assert.AreEqual(MainScene, SceneManager.GetActiveScene().name, "Отдельная арена не грузится.");
        Assert.IsNotNull(root.Q("local-exploration-field").Q("battle-sandbox-board"));
        Assert.IsFalse((bool)Member(GetField(main, "localRenderer"), "ActorsVisible"), "Фигуры боя рисует поле боя, рисунок места — под ним.");
        yield return Frames(10);
        yield return Capture(main, "mine_03_battle");
        Assert.IsFalse((bool)Invoke(main, "SaveCampaign", CampaignSaveStore.ManualSlotIds[0]),
            "Во время боя сохранить нельзя — есть сохранение перед боем.");

        // Отход: цена один раз, отряд у выхода, бой не начинается снова.
        int supply = campaign.ArmySupply;
        Invoke(battleHost, "RetreatFromCampaignBattle");
        yield return Frames(5);
        Assert.IsNull(GetField(main, "localBattle"));
        Assert.AreEqual(supply - CampaignBattleBridge.RetreatSupplyLoss, campaign.ArmySupply);
        Assert.Less(Vector2.Distance(new Vector2(499, 551), Leader(main)), 1f, "Отряд у безопасной точки.");
        yield return Frames(30);
        Assert.IsNull(GetField(main, "localBattle"), "Бой не зациклился.");
        Assert.IsFalse(DialogueActive(main));
        Assert.AreEqual(2, LocalExplorationService.AliveEnemies(campaign, LocalExplorationService.ActiveDefinition(campaign)).Count);

        // Снова в штольню — победа.
        yield return WalkTo(main, new Vector2(1180, 590));
        yield return Until(() => DialogueActive(main), 5f);
        Assert.IsTrue(DialogueActive(main), "Снова реплика логова — новый бой после отхода.");
        yield return Choose(main, "freeplay.mine.local.lair.fight");
        for (int i = 0; i < 30 && GetField(main, "localBattle") == null; i++)
            yield return null;
        battleHost = GetField(main, "localBattle");
        Assert.IsNotNull(battleHost);
        ForceLocalVictory(battleHost);
        Invoke(battleHost, "ReturnToCampaign");
        yield return Frames(5);
        Assert.IsTrue(campaign.Narrative.HasFlag("freeplay.mine.lair_cleared"), "Итог истории — прежний флаг.");
        Assert.IsEmpty(LocalExplorationService.AliveEnemies(campaign, LocalExplorationService.ActiveDefinition(campaign)));
        Assert.IsTrue(LocalExplorationService.IsActive(campaign), "После победы исследование продолжается.");
        yield return Capture(main, "mine_04_after_victory");

        // Сохранение и загрузка в шахте: тот же слой, позиции и итоги.
        yield return Until(() => Mover(main).IsIdle, 10f);
        Vector2 leaderBefore = Leader(main);
        Assert.IsTrue((bool)Invoke(main, "SaveCampaign", CampaignSaveStore.ManualSlotIds[0]));
        Assert.IsTrue((bool)Invoke(main, "LoadCampaign", CampaignSaveStore.ManualSlotIds[0]));
        yield return Frames(5);
        Assert.AreNotSame(campaign, CampaignSession.Current, "Загружена партия из файла.");
        campaign = CampaignSession.Current;
        Assert.IsTrue(LocalExplorationService.IsActive(campaign), "После загрузки — снова в шахте.");
        Assert.IsNotNull(Mover(main), "Экран места поднят заново.");
        Assert.Less(Vector2.Distance(leaderBefore, Leader(main)), 1f, "Командир там же.");
        Assert.IsTrue(campaign.Narrative.HasFlag("freeplay.mine.lair_cleared"));
        Assert.IsEmpty(LocalExplorationService.AliveEnemies(campaign, LocalExplorationService.ActiveDefinition(campaign)),
            "Повтор восстановления не возвращает зверей.");

        // Выход: группа собирается и выходит на глобальную карту.
        Invoke(main, "OnLocalExitClicked");
        yield return Until(() => !LocalExplorationService.IsActive(campaign), 30f);
        Assert.IsFalse(LocalExplorationService.IsActive(campaign), "Отряд вышел.");
        Assert.AreEqual(DisplayStyle.None, root.Q("local-exploration-screen").resolvedStyle.display);
        Assert.AreEqual(CommanderState.AtLocation, campaign.ActiveExpedition.Phase, "Поход продолжается у входа.");

        // Повторный вход: звери и находки не возвращаются.
        Assert.IsTrue((bool)Invoke(main, "TryOpenLocationInteraction", "mine"));
        yield return Frames(5);
        Assert.IsFalse(HasActor(main, "mine.beast.1") || HasActor(main, "mine.beast.2"), "Зверей нет.");
        Assert.AreEqual(4, Mover(main).Members.Count, "Отряд на месте.");
        LocalLocationDefinition mine = LocalExplorationService.ActiveDefinition(campaign);
        Assert.IsFalse(LocalExplorationService.IsObjectAvailable(campaign, mine, mine.FindObject("mine.dumps")));
    }
}
