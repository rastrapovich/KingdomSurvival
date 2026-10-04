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

// ПР-12К (канон v1.53 §28.9): лагерь у технического поселения через
// настоящее окно места. Существо-союзник видно как «не допускается» и ждёт
// в лагере; внутри — только вошедшие; возврат к лагерю и сбор.
public sealed class SettlementCampPlayModeTests
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

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static List<Button> Choices(VisualElement root)
    {
        return root.Q("narrative-dialogue-overlay").Query<Button>().ToList()
            .Where(button => button.resolvedStyle.display != DisplayStyle.None).ToList();
    }

    private static Button Choice(VisualElement root, string textStart)
    {
        Button button = Choices(root).FirstOrDefault(candidate => candidate.text.StartsWith(textStart));
        Assert.IsNotNull(button, "Нет кнопки «" + textStart + "»: " + string.Join(" | ", Choices(root).Select(b => b.text)));
        return button;
    }

    private static IEnumerator Click(Button button)
    {
        Assert.IsTrue(button.enabledInHierarchy, "Кнопка «" + button.text + "» недоступна.");
        using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
        {
            submit.target = button;
            button.SendEvent(submit);
        }
        yield return Frames(2);
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

    [UnityTest]
    public IEnumerator Camp_PickGroup_CreatureWaits_ReturnAndGather()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        Invoke(main, "StartNewFreePlayFromMenu");
        yield return Frames(10);

        GameState campaign = CampaignSession.Current;
        campaign.ArmySupply = 30;
        LocationData target = campaign.Locations.First(location => !location.IsWaypoint && location.IsVisibleOnMap);
        Assert.IsTrue(campaign.TryStartExpedition(target.Id, new List<string> { "garrick" }, out string message, HomePeopleService.OstafiyId), message);
        campaign.ActiveExpedition.RouteIndex = 1;
        yield return null;

        Invoke(main, "DebugSettlementCamp");
        yield return Frames(3);
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        Assert.IsTrue((bool)main.GetType().GetProperty("IsLocationInteractionActive", AnyInstance).GetValue(main), "Окно поселения открыто.");
        List<string> texts = root.Q("narrative-dialogue-overlay").Query<TextElement>().ToList().Select(l => l.text).ToList();
        Assert.IsTrue(texts.Any(text => text != null && text.Contains("Не допускается")),
            "Причина отказа видна заранее. Тексты окна: " + string.Join(" | ", texts));
        Assert.IsFalse(Choice(root, "ВОЙТИ ВСЕМ ОТРЯДОМ").enabledInHierarchy, "Существо не пустят — всем отрядом не войти.");

        yield return Click(Choice(root, "РАЗБИТЬ ЛАГЕРЬ СНАРУЖИ"));
        Assert.IsFalse(Choice(root, "Зверь-союзник").enabledInHierarchy, "Недопущенного не выбрать.");
        yield return Click(Choice(root, "Остафий"));
        StringAssert.Contains("останется в лагере", Choice(root, "Остафий").text);
        yield return Capture(main, "camp_01_picker");
        yield return Click(Choice(root, "ВОЙТИ"));

        Assert.IsTrue(SettlementCampService.IsCommanderInside(campaign));
        CollectionAssert.AreEquivalent(new[] { HomePeopleService.OstafiyId, SettlementCampDevFixture.CreatureId },
            campaign.SettlementCamp.WaitingIds);
        Assert.IsFalse(PartyPresence.IsPresent(campaign, SettlementCampDevFixture.CreatureId));
        Assert.IsTrue(PartyPresence.IsPresent(campaign, "garrick"));
        Assert.IsTrue(PartyPresence.IsPresent(campaign, SettlementCampDevFixture.MercenaryId));
        yield return Capture(main, "camp_02_inside");

        yield return Click(Choice(root, "ВЕРНУТЬСЯ К ЛАГЕРЮ"));
        Assert.IsFalse(SettlementCampService.IsCommanderInside(campaign));
        Assert.IsTrue(PartyPresence.IsPresent(campaign, SettlementCampDevFixture.CreatureId), "У лагеря все рядом.");
        yield return Capture(main, "camp_03_at_camp");

        yield return Click(Choice(root, "СОБРАТЬСЯ И ПРОДОЛЖИТЬ ПУТЬ"));
        Assert.IsNull(campaign.SettlementCamp, "Разделение снято.");
        CollectionAssert.AreEquivalent(PartyPresence.ExpeditionIds(campaign), PartyPresence.PresentIds(campaign));
    }
}
