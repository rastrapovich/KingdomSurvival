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

    // 12У-H: экран «Новая игра» — восемь параметров карты; очень большая
    // карта с «много врагов» создаётся, живая карта строится за экраном загрузки.
    [UnityTest]
    public IEnumerator NewGameScreen_EightMapOptions_HugeMapBuilds()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        Invoke(main, "OnMainMenuNewGameClicked");
        yield return Frames(5);
        Assert.IsNotNull(root.Q("new-game-map-size-row"), "Строка размера карты.");
        Assert.AreEqual(4, root.Q("new-game-map-size-row").childCount, "Четыре размера.");
        VisualElement options = root.Q("new-game-map-options");
        Assert.IsNotNull(options);
        Assert.AreEqual(7, options.childCount, "Ещё семь строк: местность, рельеф, вода, лес, деревни, тайны, враги.");
        Invoke(main, "SelectNewGameMapSize", CampaignStartOptions.HugeMapHexesAcross);
        Invoke(main, "SelectNewGameMapOption", "enemies", CampaignMapOptions.High);
        Invoke(main, "SelectNewGameMapOption", "region", (int)WorldMapRegionKind.RiverValley);
        yield return Frames(5);
        yield return Capture(main, "ng_01_new_game");
        Invoke(main, "OnNewGameStartClicked");
        float started = Time.realtimeSinceStartup;
        bool loading = false;
        while ((MapLabGameLive.Current == null || !MapLabGameLive.Current.Ready) && Time.realtimeSinceStartup - started < 900f)
        {
            loading |= Get<bool>(main, "IsLoadingScreenOpen");
            yield return null;
        }
        GameState campaign = CampaignSession.Current;
        Assert.IsNotNull(campaign);
        Assert.AreEqual(CampaignStartOptions.HugeMapHexesAcross, campaign.GeneratedMap.HexesAcross, "Очень большая карта.");
        Assert.AreEqual(CampaignMapOptions.High, campaign.Configuration.MapOptions.Enemies);
        Assert.AreEqual(WorldMapRegionKind.RiverValley, campaign.Configuration.MapOptions.RegionKind);
        Assert.IsTrue(loading, "Был экран загрузки.");
        Assert.IsNotNull(MapLabGameLive.Current);
        Assert.IsTrue(MapLabGameLive.Current.Ready, "Живая карта очень большой партии построена: " + MapLabGameLive.Current.Error);
        Debug.Log($"MapLab live: очень большая карта ({campaign.GeneratedMap.HexesAcross} клеток) построена за {MapLabGameLive.Current.BuildMs / 1000.0:0.0} с, деревень {campaign.GeneratedMap.Villages.Count}, групп {campaign.GeneratedMap.Actors.Count}");
        yield return Frames(60);
        Assert.IsFalse(Get<bool>(main, "IsLoadingScreenOpen"));
        yield return Capture(main, "ng_02_huge_map");
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

        // Мир MapLab строится по шагам (расчёты в фоне): кадры идут, самый
        // долгий кадр сборки — секунды, а не минута.
        float started = Time.realtimeSinceStartup, last = started, worst = 0f;
        int frames = 0;
        bool loadingSeen = false, capturedLoading = false;
        float progressSeen = 0f;
        while ((MapLabGameLive.Current == null || !MapLabGameLive.Current.Ready) && Time.realtimeSinceStartup - started < 300f)
        {
            yield return null;
            float now = Time.realtimeSinceStartup;
            worst = Mathf.Max(worst, now - last);
            last = now;
            frames++;
            // Экран загрузки закрывает всё, пока карта строится; полоска растёт.
            if (Get<bool>(main, "IsLoadingScreenOpen"))
            {
                loadingSeen = true;
                progressSeen = Mathf.Max(progressSeen, Field<float>(main, "loadingShown"));
                if (!capturedLoading && progressSeen > 0.3f)
                {
                    capturedLoading = true;
                    yield return Capture(main, "live_00_loading");
                }
            }
        }
        Assert.IsTrue(loadingSeen, "Пока строится карта, виден экран загрузки.");
        Assert.Greater(progressSeen, 0.3f, "Полоска загрузки растёт по шагам сборки.");
        Debug.Log($"MapLab live: сборка шла {frames} кадров, самый долгий кадр {worst:0.00} с");
        Assert.IsNotNull(MapLabGameLive.Current, "Живая карта не начала строиться.");
        Assert.IsTrue(MapLabGameLive.Current.Ready, "Живая карта не построена: " + MapLabGameLive.Current.Error);
        Debug.Log($"MapLab live: построено за {MapLabGameLive.Current.BuildMs / 1000.0:0.0} с, зданий {MapLabGameLive.Current.Buildings.Count}");
        Assert.Greater(frames, 30, "Сборка не идёт одним кадром.");
        Assert.Less(worst, 6f, "Ни один кадр сборки не замирает надолго.");
        yield return Frames(60);
        Assert.IsFalse(Get<bool>(main, "IsLoadingScreenOpen"), "Карта готова — экран загрузки убран.");
        Assert.IsNotNull(MapLabGameLive.Current.Target, "Камера MapLab рисует видимую часть карты.");
        Assert.IsNotEmpty(MapLabGameLive.Current.Features, "У деталей мест есть цели наведения.");
        Debug.Log("MapLab live: деталей мест с целями — " + MapLabGameLive.Current.Features.Count);
        yield return Capture(main, "live_01_map");

        // Деревня с зданиями услуг: на живой карте — подпись, а не кнопка.
        TestPeople.AdmitAll(campaign);
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        string villageId = MapLabGameLive.Current.Buildings.Select(b => b.SettlementId)
            .FirstOrDefault(id => campaign.FindLocation(id) != null && MapLabGameLive.Current.Buildings.Any(b => b.SettlementId == id && b.Role == "merchant"));
        Assert.IsNotNull(villageId, "У деревень карты есть лавка.");
        LocationData village = campaign.FindLocation(villageId);
        village.IsVisibleOnMap = true;
        village.IsDiscovered = true;
        Invoke(main, "RefreshWorldMapPanel");
        yield return ZoomTo(main, village.MapXPercent, village.MapYPercent, 0.55f);
        Assert.IsNull(root.Q<VisualElement>("world-map-node-" + village.Id), "Кнопки деревни на живой карте нет.");
        Assert.IsNotNull(root.Q<Label>("world-map-village-name-" + village.Id), "Вместо кнопки — подпись деревни.");
        yield return Capture(main, "live_02_village_mid");

        // Цель лавки этой деревни видна издалека, и курсор над ней попадает
        // именно в неё (не в слои карты над ней).
        VisualElement shop = root.Query<VisualElement>("world-map-building-merchant").ToList().FirstOrDefault(e => (e.userData as string) == village.Id);
        Assert.IsNotNull(shop, "Цель лавки построена, пока отряд далеко.");
        // Камера — на саму лавку (она может стоять в стороне от центра деревни).
        Rect shopPercent = MapLabGameLive.Current.Buildings.First(b => b.SettlementId == village.Id && b.Role == "merchant").Percent;
        yield return ZoomTo(main, shopPercent.center.x, shopPercent.center.y, 0.55f);
        yield return Frames(5);
        Rect bounds = shop.worldBound;
        Vector2 point = new Vector2(bounds.center.x, bounds.yMin + bounds.height * 0.62f);
        VisualElement picked = root.panel.Pick(point);
        Assert.IsNotNull(picked, "Под курсором что-то есть.");
        Assert.IsTrue(picked == shop || shop.Contains(picked), "Курсор над лавкой попадает в лавку, а попал в: " + picked.name);
        using (PointerEnterEvent enter = PointerEnterEvent.GetPooled())
        {
            enter.target = picked;
            picked.SendEvent(enter);
        }
        yield return Frames(10);
        yield return Capture(main, "live_03_hover_far");

        // Клик издалека — отряд идёт в деревню, к лавке.
        using (ClickEvent click = ClickEvent.GetPooled())
        {
            click.target = picked;
            picked.SendEvent(click);
        }
        yield return Frames(5);
        Assert.IsTrue(campaign.HasActiveExpedition, "Клик по зданию отправил отряд.");
        ExpeditionData expedition = campaign.ActiveExpedition;
        Assert.AreEqual(village.Id, expedition.LocationId, "Цель пути — эта деревня.");
        Assert.AreEqual("merchant", Field<string>(main, "worldMapPendingRole"), "После входа — к лавке.");

        // Дорогу пропускаем: отряд у входа, прибытие открывает вход.
        expedition.CurrentMapXPercent = village.MapXPercent;
        expedition.CurrentMapYPercent = village.MapYPercent;
        expedition.Phase = CommanderState.AtLocation;
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

        // Герой — фигурка набора MapLab; после входа сам идёт к лавке.
        Image hero = root.Q<Image>("world-map-hero-figure");
        Assert.IsNotNull(hero);
        Assert.IsTrue(MapLabGameLive.HeroFrame(0, out Texture2D frame, out _, out _, out _), "Есть кадры фишки MapLab.");
        Assert.IsNotNull(hero.image, "Герой нарисован кадром фишки MapLab.");
        yield return Frames(30);
        yield return Capture(main, "live_04_walking");
        float started2 = Time.realtimeSinceStartup;
        while (!Get<bool>(main, "IsSettlementScreenOpen") && Time.realtimeSinceStartup - started2 < 40f)
            yield return null;
        Assert.IsTrue(Get<bool>(main, "IsSettlementScreenOpen"), "Дошли до двери — окно услуги открыто.");
        yield return Frames(5);
        Assert.IsTrue(Get<bool>(main, "IsSettlementTradeOpen"), "Клик по лавке — сразу торговля.");
        yield return Capture(main, "live_05_service");
    }
}
