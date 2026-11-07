using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// 12У-F/G: живая карта MapLab в настоящей сцене игры. Новая партия → экран
// карты рисует MapLab камерой (не снимок) → приближение к деревне → отряд у
// поселения входит → здания услуг на карте, герой — фигурка MapLab → клик
// по зданию: герой идёт к двери, у двери — окно услуги. Кадры 1920×1080 —
// при заданной KS_SCREENSHOT_DIR.
public sealed class MapLabLiveMapPlayModeTests
{
    private const string MainScene = "Prototype_Main";
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [SetUp]
    public void SetUp()
    {
        CampaignSession.Reset();
        PlayModeSaveIsolation.Begin();
        MapLabGameMap.AllowInBatch = true;
    }

    [TearDown]
    public void TearDown()
    {
        MapLabGameMap.AllowInBatch = false;
        MapLabGameLive.Clear();
        CampaignSession.Reset();
        PlayModeSaveIsolation.End();
    }

    private static MonoBehaviour FindBehaviour(string typeName) =>
        Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(b => b.GetType().Name == typeName);

    private static object Invoke(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, AnyInstance);
        Assert.IsNotNull(info, "Нет метода " + method);
        return info.Invoke(target, args);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, AnyInstance).GetValue(target);

    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, AnyInstance).SetValue(target, value);

    private static T Get<T>(object target, string property) => (T)target.GetType().GetProperty(property, AnyInstance).GetValue(target);

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
            yield return null;
    }

    private static IEnumerator Capture(MonoBehaviour main, string name)
    {
        string directory = System.Environment.GetEnvironmentVariable("KS_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory))
            yield break;
        const int width = 1920, height = 1080;
        PanelSettings settings = main.GetComponent<UIDocument>().panelSettings;
        RenderTexture previous = settings.targetTexture;
        RenderTexture texture = new RenderTexture(width, height, 24);
        settings.targetTexture = texture;
        yield return Frames(6);
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = texture;
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
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

    private static IEnumerator ZoomTo(MonoBehaviour main, float xPercent, float yPercent, float zoomShare)
    {
        float max = Field<float>(main, "worldMapMaxZoom"), min = Field<float>(main, "worldMapMinZoom");
        float zoom = Mathf.Lerp(min, max, zoomShare);
        SetField(main, "worldMapZoom", zoom);
        SetField(main, "worldMapZoomTarget", zoom);
        Invoke(main, "CenterWorldMapOn", xPercent, yPercent);
        Invoke(main, "ApplyWorldMapViewportTransform");
        yield return Frames(30);
    }

    [UnityTest]
    public IEnumerator LiveMap_HeroFigure_WalkToBuilding_OpensService()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        Invoke(main, "StartNewGameFromMenu");
        yield return Frames(10);
        GameState campaign = CampaignSession.Current;
        Assert.IsTrue(campaign.HasGeneratedMap, "Свободная игра — сгенерированная карта.");

        // Мир MapLab строится в кадре после показа «Рисуем карту…».
        float started = Time.realtimeSinceStartup;
        while ((MapLabGameLive.Current == null || !MapLabGameLive.Current.Ready) && Time.realtimeSinceStartup - started < 300f)
            yield return null;
        Assert.IsNotNull(MapLabGameLive.Current, "Живая карта не начала строиться.");
        Assert.IsTrue(MapLabGameLive.Current.Ready, "Живая карта не построена: " + MapLabGameLive.Current.Error);
        Debug.Log($"MapLab live: построено за {MapLabGameLive.Current.BuildMs / 1000.0:0.0} с, зданий {MapLabGameLive.Current.Buildings.Count}");
        yield return Frames(60);
        Assert.IsNotNull(MapLabGameLive.Current.Target, "Камера MapLab рисует видимую часть карты.");
        yield return Capture(main, "live_01_map");

        // Деревня с зданиями услуг.
        TestPeople.AdmitAll(campaign);
        string villageId = MapLabGameLive.Current.Buildings.Select(b => b.SettlementId).FirstOrDefault(id => campaign.FindLocation(id) != null);
        Assert.IsNotNull(villageId, "У деревень карты есть здания услуг.");
        LocationData village = campaign.FindLocation(villageId);
        yield return ZoomTo(main, village.MapXPercent, village.MapYPercent, 0.55f);
        yield return Capture(main, "live_02_village_mid");
        yield return ZoomTo(main, village.MapXPercent, village.MapYPercent, 1f);
        yield return Capture(main, "live_03_village_close");

        // Отряд у входа в деревню → вход: общий экран сам не открывается.
        Assert.IsTrue(campaign.TryStartExpedition(village.Id, new List<string> { "garrick" }, out string message), message);
        ExpeditionData expedition = campaign.ActiveExpedition;
        expedition.CurrentMapXPercent = village.MapXPercent;
        expedition.CurrentMapYPercent = village.MapYPercent;
        expedition.Phase = CommanderState.AtLocation;
        expedition.LocationId = village.Id;
        // Маршрут пройден: отряд стоит у входа, симуляция его не двигает.
        expedition.Route = new List<MapPointData> { new MapPointData(village.MapXPercent, village.MapYPercent) };
        expedition.RouteIndex = 0;
        expedition.RemainingRouteCells = 0;
        yield return Frames(5);
        Invoke(main, "TryOpenLocationInteraction", village.Id);
        yield return Frames(10);
        if (Get<bool>(main, "IsLocationInteractionActive"))
            Invoke(main, "CloseLocationInteraction");
        yield return Frames(10);
        Assert.IsTrue(SettlementCampService.IsCommanderInside(campaign), "Отряд вошёл в поселение.");
        Assert.IsFalse(Get<bool>(main, "IsSettlementScreenOpen"), "На живой карте общий экран поселения сам не открывается.");
        yield return Frames(60);

        // Герой — фигурка набора фишки MapLab.
        Image hero = main.GetComponent<UIDocument>().rootVisualElement.Q<Image>("world-map-hero-figure");
        Assert.IsNotNull(hero);
        Assert.IsTrue(MapLabGameLive.HeroFrame(0, out Texture2D frame, out _, out _, out _), "Есть кадры фишки MapLab.");
        Assert.IsNotNull(hero.image, "Герой нарисован кадром фишки MapLab.");
        yield return Capture(main, "live_04_inside");

        // Клик по зданию услуги: герой идёт к двери, затем окно услуги.
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        List<VisualElement> buildings = root.Query<VisualElement>().Where(e => e.name != null && e.name.StartsWith("world-map-building-")).ToList();
        Assert.IsNotEmpty(buildings, "На карте поселения есть цели зданий.");
        Debug.Log("MapLab live: здания поселения — " + string.Join(", ", buildings.Select(b => b.name)));
        VisualElement building = buildings.FirstOrDefault(b => b.name == "world-map-building-merchant") ?? buildings[0];
        using (PointerEnterEvent enter = PointerEnterEvent.GetPooled())
        {
            enter.target = building;
            building.SendEvent(enter);
        }
        yield return Frames(10);
        yield return Capture(main, "live_05_hover");
        using (ClickEvent click = ClickEvent.GetPooled())
        {
            click.target = building;
            building.SendEvent(click);
        }
        yield return Frames(40);
        yield return Capture(main, "live_06_walking");
        started = Time.realtimeSinceStartup;
        while (!Get<bool>(main, "IsSettlementScreenOpen") && Time.realtimeSinceStartup - started < 30f)
            yield return null;
        Assert.IsTrue(Get<bool>(main, "IsSettlementScreenOpen"), "Дошли до двери — окно услуги открыто.");
        yield return Frames(5);
        if (building.name == "world-map-building-merchant")
            Assert.IsTrue(Get<bool>(main, "IsSettlementTradeOpen"), "Клик по лавке — сразу торговля.");
        yield return Capture(main, "live_07_service");
    }
}
