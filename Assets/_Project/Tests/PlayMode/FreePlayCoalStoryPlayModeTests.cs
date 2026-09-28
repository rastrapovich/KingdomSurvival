using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// ПР-12Б, история И-1 «Уголь для Лады» на настоящих сценах: свободная игра
// из меню → разговор с Ладой → хутор на карте → сцена на хуторе → охота →
// бой «Вожак у угольных ям» в BattleSandbox → отход → хутор уходит к дальним
// ямам. Контроллеры — через рефлексию, как в CampaignBattlePlayModeTests.
public sealed class FreePlayCoalStoryPlayModeTests
{
    private const string MainScene = "Prototype_Main";
    private const string BattleScene = "BattleSandbox";
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

    private static IEnumerator WaitForScene(string sceneName)
    {
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != sceneName; i++)
            yield return null;
        Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name);
        for (int i = 0; i < 20; i++)
            yield return null;
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

    // Поле или свойство (у представлений диалога — открытые поля).
    private static object Property(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name);
        if (property != null)
            return property.GetValue(target);
        FieldInfo field = target.GetType().GetField(name);
        Assert.IsNotNull(field, "Нет поля " + name);
        return field.GetValue(target);
    }

    // Проходит открытый разговор через настоящий UI: «читать дальше»
    // пропускается, затем нажимаются ответы по порядку.
    private static IEnumerator Choose(MonoBehaviour main, params string[] choiceIds)
    {
        object session = GetField(main, "narrativeDialogueSession");
        foreach (string choiceId in choiceIds)
        {
            for (int i = 0; i < 20; i++)
            {
                object view = Invoke(session, "BuildView");
                IList choices = (IList)Property(view, "AvailableChoices");
                object choice = choices.Cast<object>().FirstOrDefault(c => (string)Property(c, "ChoiceId") == choiceId);
                if (choice == null && choices.Count == 1 && Property(choices[0], "Kind").ToString() == "Continue")
                    choice = choices[0];
                Assert.IsNotNull(choice, "Нет ответа " + choiceId);
                string id = (string)Property(choice, "ChoiceId");
                Invoke(main, "OnNarrativeDialogueChoiceSelected", id, (string)Property(choice, "Text"), Property(choice, "Kind"));
                yield return null;
                if (id == choiceId)
                    break;
            }
        }
    }

    [UnityTest]
    public IEnumerator Coal_LadaHutorHuntRetreat_HutorMovesAway()
    {
        SceneManager.LoadScene(MainScene);
        yield return WaitForScene(MainScene);
        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        Invoke(main, "StartNewFreePlayFromMenu");
        for (int i = 0; i < 10; i++)
            yield return null;
        GameState campaign = CampaignSession.Current;
        campaign.Food = 40;
        campaign.ArmySupply = 30;

        Assert.IsTrue(CampaignContent.DescribeHomeCares(campaign).Any(c => c.Id == "home.care.freeplay_coal"), "Забота «Уголь у Лады».");
        Assert.IsTrue((bool)Invoke(main, "TryOpenNarrativeDialogueById", "freeplay_coal_lada"));
        yield return Choose(main, "freeplay.coal.lada.go", "freeplay.coal.lada.take");
        for (int i = 0; i < 5; i++)
            yield return null;
        LocationData hutor = campaign.FindLocation("freeplay.coalburners");
        Assert.IsNotNull(hutor, "После разговора с Ладой хутор на карте.");

        Assert.IsTrue(campaign.TryStartExpedition(hutor.Id, new System.Collections.Generic.List<string> { "garrick" }, out string message), message);
        campaign.ActiveExpedition.Phase = CommanderState.AtLocation;
        yield return null;
        Assert.IsTrue((bool)Invoke(main, "TryOpenNarrativeDialogueById", "freeplay_coal_hutor"));
        yield return Choose(main, "freeplay.coal.hutor.hunt", "freeplay.coal.hutor.wait");

        yield return WaitForScene(BattleScene);
        MonoBehaviour sandbox = FindBehaviour("BattleSandboxController");
        for (int i = 0; i < 60 && GetField(sandbox, "battle") == null; i++)
            yield return null;
        object battle = GetField(sandbox, "battle");
        Assert.AreEqual(2, ((IEnumerable)battle.GetType().GetProperty("Units").GetValue(battle)).Cast<object>()
            .Count(unit => unit.GetType().GetProperty("Team").GetValue(unit).ToString() == "Enemy"), "Вожак и зверь.");

        Invoke(sandbox, "RetreatFromCampaignBattle");
        yield return WaitForScene(MainScene);

        Assert.AreSame(campaign, CampaignSession.Current);
        Assert.IsTrue(campaign.Narrative.HasFlag("freeplay.coal.hutor_moved"), "Отход — хутор уходит к дальним ямам.");
        Assert.IsFalse(campaign.Narrative.HasFlag("freeplay.coal.coal"));
        Assert.AreEqual("freeplay_coal_hutor_after", CampaignContent.LocationEntry(campaign, hutor.Id).DialogueId);
    }
}
