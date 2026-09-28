using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// 12Е-7: сработавшая особенность видна в настоящем окне диалога (кто,
// какая, что дала — сразу под итогом проверки) и в донесениях (строка
// «[ОСОБЕННОСТЬ]» одним нейтральным цветом).
public sealed class FeaturePresentationPlayModeTests
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

    private static MonoBehaviour FindController()
    {
        MonoBehaviour controller = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .FirstOrDefault(behaviour => behaviour.GetType().Name == "PrototypeUIController");
        Assert.IsNotNull(controller);
        return controller;
    }

    private static object Invoke(object target, string method, params object[] arguments)
    {
        MethodInfo info = target.GetType().GetMethod(method, AnyInstance);
        Assert.IsNotNull(info, "Нет метода " + method);
        return info.Invoke(target, arguments);
    }

    private static object GetField(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        Assert.IsNotNull(field, "Нет поля " + name);
        return field.GetValue(target);
    }

    private static IEnumerator StartFreePlay()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        for (int i = 0; i < 20; i++)
            yield return null;
        Invoke(FindController(), "StartNewFreePlayFromMenu");
        for (int i = 0; i < 10; i++)
            yield return null;
    }

    private static FeatureActivation Lead(GameState campaign)
    {
        string hero = campaign.GetSelectedCommander().Id;
        Assert.IsTrue(CharacterFeatureService.Grant(campaign, hero, FeatureIds.Lead, 1, FeatureSource.Story, null, out string message), message);
        FeatureActivation activation = new FeatureActivation
        {
            PersonId = hero,
            FeatureId = FeatureIds.Lead,
            Trigger = FeatureTrigger.CheckResolved,
            Text = "замеченное пригодится — следующее Расследование в этом разговоре +1."
        };
        FeatureDispatcher.Record(campaign, activation);
        return activation;
    }

    [UnityTest]
    public IEnumerator Dialogue_ShowsWhoWhichAndWhatFeatureGave()
    {
        yield return StartFreePlay();
        GameState campaign = CampaignSession.Current;
        FeatureActivation activation = Lead(campaign);

        MonoBehaviour main = FindController();
        Assert.IsTrue((bool)Invoke(main, "TryOpenNarrativeDialogueById", "chapter01_dialogue_camp_beasts"), "Диалог открылся.");
        object view = Invoke(GetField(main, "narrativeDialogueSession"), "BuildView");
        Invoke(main, "DisplayNarrativeView", view, null, new List<FeatureActivation> { activation });
        yield return null;

        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        List<VisualElement> features = root.Query<VisualElement>(className: "narrative-dialogue-history-feature").ToList();
        Assert.AreEqual(1, features.Count, "Одна строка особенности.");
        string title = features[0].Q<Label>(className: "narrative-dialogue-history-feature-title").text;
        StringAssert.Contains("«Зацепка»", title);
        StringAssert.Contains(CharacterProgressionService.DisplayName(campaign, activation.PersonId), title);
        StringAssert.Contains("следующее Расследование", features[0].Q<Label>(className: "narrative-dialogue-history-feature-body").text);

        Invoke(main, "CloseNarrativeDialogue");
    }

    [UnityTest]
    public IEnumerator Reports_FeatureLine_HasItsOwnNeutralColor()
    {
        yield return StartFreePlay();
        GameState campaign = CampaignSession.Current;
        FeatureActivation activation = Lead(campaign);

        MonoBehaviour main = FindController();
        Invoke(main, "AddReport", "[БОЙ] Победа.\n" + FeaturePresentation.Line(campaign, activation), null);
        for (int i = 0; i < 10; i++)
            yield return null;

        string text = ((Label)GetField(main, "reportHistoryLabel")).text;
        StringAssert.Contains("<color=#8FA5BE>" + FeaturePresentation.Tag, text);
        StringAssert.DoesNotContain("<color=#8FA5BE>[БОЙ]", text, "Обычная строка не окрашена.");
    }
}
