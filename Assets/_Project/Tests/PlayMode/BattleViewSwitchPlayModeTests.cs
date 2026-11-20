using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Вид карты в бою (20.11.2026): кнопка «Вид: …» в полигоне переключает
// «Обычный / Мрачный / Сепия» посреди боя — бойцы берут рисунки вида,
// кадр поля перекрашивается. Любая ошибка консоли проваливает тест.
public sealed class BattleViewSwitchPlayModeTests
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private string savedViewId;

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
        savedViewId = MapLabGameViews.ActiveId;
    }

    [TearDown]
    public void TearDown()
    {
        MapLabGameViews.Select(savedViewId);
        CampaignSession.Reset();
    }

    private static object Get(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, AnyInstance);
        if (field != null)
            return field.GetValue(target);
        return target.GetType().GetProperty(name, AnyInstance).GetValue(target);
    }

    private static object Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, AnyInstance).Invoke(target, args);

    // Кадр панели UI через RenderTexture (в пакетном режиме WaitForEndOfFrame
    // не наступает). Средняя яркость — в result[0]; PNG — при KS_SCREENSHOT_DIR.
    private static IEnumerator Capture(MonoBehaviour host, string name, float[] result)
    {
        PanelSettings settings = host.GetComponent<UIDocument>().panelSettings;
        RenderTexture previous = settings.targetTexture;
        RenderTexture texture = new RenderTexture(1920, 1080, 24);
        settings.targetTexture = texture;
        for (int i = 0; i < 6; i++)
            yield return null;
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = texture;
        Texture2D image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        image.Apply();
        RenderTexture.active = active;
        settings.targetTexture = previous;
        result[0] = image.GetPixels32().Average(c => (c.r + c.g + c.b) / 765f);
        string dir = System.Environment.GetEnvironmentVariable("KS_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG());
        }
        Object.Destroy(image);
        texture.Release();
        Object.Destroy(texture);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SandboxBattle_ViewButton_SwitchesFieldAndFighterArt()
    {
        MapLabGameViews.Select("classic");
        SceneManager.LoadScene("BattleSandbox");
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != "BattleSandbox"; i++)
            yield return null;
        BattleSandboxController sandbox = null;
        for (int i = 0; i < 120 && (sandbox == null || !(bool)Get(sandbox, "initialized")); i++)
        {
            sandbox = Object.FindAnyObjectByType<BattleSandboxController>();
            yield return null;
        }
        Assert.IsNotNull(sandbox);

        Invoke(sandbox, "ChangeCount", false, "guard", 1);
        Invoke(sandbox, "ChangeCount", false, "militia", 1);
        Invoke(sandbox, "ChangeCount", true, "blood_kleshchen", 1);
        Invoke(sandbox, "ChangeCount", true, "wolf", 1);
        Invoke(sandbox, "ChangeCount", true, "forest_beast_alpha", 1);
        Invoke(sandbox, "StartBattle");
        float deadline = Time.realtimeSinceStartup + 60f;
        while (MapLabArenaDriver.Loading && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.IsFalse(MapLabArenaDriver.Loading, "Арена собрана.");
        for (int i = 0; i < 30; i++)
            yield return null;

        VisualElement root = sandbox.GetComponent<UIDocument>().rootVisualElement;
        Button viewButton = root.Q<Button>("battle-view-button");
        Assert.IsNotNull(viewButton, "в бою есть кнопка вида");
        Assert.AreEqual(DisplayStyle.Flex, viewButton.resolvedStyle.display, "видов больше одного — кнопка видна");
        StringAssert.Contains("ОБЫЧНЫЙ", viewButton.text);

        object content = Get(sandbox, "unitContent");
        Sprite classicGuard = (Sprite)Get(Invoke(content, "GetVisual", "guard"), "BattlefieldSprite");
        float[] classic = new float[1];
        yield return Capture(sandbox, "battle_view_classic", classic);

        foreach (string viewId in new[] { "gloom", "sepia" })
        {
            MapLabGameViews.Select(viewId);
            for (int i = 0; i < 40; i++)
                yield return null;
            StringAssert.Contains(MapLabGameViews.ActiveName.ToUpperInvariant(), viewButton.text, "надпись кнопки — выбранный вид");
            Sprite guard = (Sprite)Get(Invoke(content, "GetVisual", "guard"), "BattlefieldSprite");
            Assert.IsNotNull(guard);
            Assert.AreNotSame(classicGuard, guard, "гвардеец взял фигуру вида «" + viewId + "»");
            StringAssert.Contains("__" + viewId + "__", guard.name, "фигура из слота вида");
            Sprite beast = (Sprite)Get(Invoke(content, "GetVisual", "forest_beast_alpha"), "BattlefieldSprite");
            StringAssert.Contains("forest_beast_alpha__field__" + viewId + "__", beast.name, "бурогрив в виде «" + viewId + "»");
            float[] shot = new float[1];
            yield return Capture(sandbox, "battle_view_" + viewId, shot);
            Assert.Greater(Mathf.Abs(shot[0] - classic[0]), 0.005f, "кадр поля перекрасился (" + viewId + ")");
        }

        MapLabGameViews.Select("classic");
        for (int i = 0; i < 10; i++)
            yield return null;
        Assert.AreSame(classicGuard, (Sprite)Get(Invoke(content, "GetVisual", "guard"), "BattlefieldSprite"), "возврат к «Обычному»");
    }
}
