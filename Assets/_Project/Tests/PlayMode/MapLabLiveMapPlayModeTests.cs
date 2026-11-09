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

    // Настоящая дорога: клик по каждому зданию услуги из лагеря — отряд идёт
    // сам (без телепорта), входит, герой доходит до двери, открывается окно.
    // Время мира идёт само (канон v1.66): герой дошёл и стоит — часы идут
    // дальше, без окон. Пауза — только кнопкой рядом со скоростями.
    [UnityTest]
    public IEnumerator Time_KeepsRunningAfterHeroStops_PauseButtonStopsIt()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        Invoke(main, "StartNewGameFromMenu");
        float started = Time.realtimeSinceStartup;
        while ((MapLabGameLive.Current == null || !MapLabGameLive.Current.Ready) && Time.realtimeSinceStartup - started < 300f)
            yield return null;
        Assert.IsTrue(MapLabGameLive.Current != null && MapLabGameLive.Current.Ready);
        yield return Frames(60);
        GameState campaign = CampaignSession.Current;
        TestPeople.AdmitAll(campaign);
        ContinuousSimulationSystem.SetSpeedMultiplier(campaign, ContinuousSimulationSystem.MaximumSpeedMultiplier);

        double Hours()
        {
            ContinuousClockSnapshot c = ContinuousSimulationSystem.GetClock(campaign);
            return c.Day * 24.0 + c.HourOfDay;
        }

        // Точки: вокруг старта, к ближайшей деревне (не входя), к детали места.
        List<Vector2> points = new List<Vector2>
        {
            new Vector2(WorldMapNavigation.CapitalXPercent + 3f, WorldMapNavigation.CapitalYPercent - 4f),
            new Vector2(WorldMapNavigation.CapitalXPercent - 4f, WorldMapNavigation.CapitalYPercent - 2f),
        };
        LocationData near = campaign.Locations.Where(l => l != null && l.Id.StartsWith(GeneratedMapPlaces.IdPrefix))
            .OrderBy(l => Mathf.Abs(l.MapXPercent - WorldMapNavigation.CapitalXPercent) + Mathf.Abs(l.MapYPercent - WorldMapNavigation.CapitalYPercent)).FirstOrDefault();
        if (near != null)
            points.Add(new Vector2(near.MapXPercent + 2.5f, near.MapYPercent + 2.5f));
        if (MapLabGameLive.Current.Features.Count > 0)
        {
            Rect f = MapLabGameLive.Current.Features.OrderBy(x => Mathf.Abs(x.Percent.center.x - WorldMapNavigation.CapitalXPercent) + Mathf.Abs(x.Percent.center.y - WorldMapNavigation.CapitalYPercent)).First().Percent;
            points.Add(new Vector2(f.xMax + f.width * 0.15f, f.yMax));
        }

        List<string> failures = new List<string>();
        foreach (Vector2 p in points)
        {
            Invoke(main, "GoToWorldMapPoint", p.x, p.y);
            float t0 = Time.realtimeSinceStartup;
            yield return Frames(10);
            while (ContinuousSimulationSystem.HasMovementOrActivityInProgress(campaign) && Time.realtimeSinceStartup - t0 < 90f)
                yield return null;
            // Окна, открывшиеся по прибытии (если есть), — закрыть, как игрок.
            if (Get<bool>(main, "IsLocationInteractionActive"))
                Invoke(main, "CloseLocationInteraction");
            yield return Frames(10);
            double before = Hours();
            float w0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - w0 < 1.5f)
                yield return null;
            double after = Hours();
            string reason = (string)Invoke(main, "TimeBlockReason");
            Debug.Log($"Время: точка {p}, стоим, за 1,5 с прошло {after - before:0.000} ч; держит: «{reason}»; фаза {campaign.ActiveExpedition?.Phase}; пауза {ContinuousSimulationSystem.IsPaused(campaign)}; независимое {WorldMapMovementRules.Current.IndependentTime}; темп {WorldMapMovementRules.Current.WorldGameHoursPerRealSecond}; скорость {ContinuousSimulationSystem.GetClock(campaign).SpeedMultiplier}; точек пути {campaign.ActiveExpedition?.LastTravelPoints?.Count}");
            if (after - before <= 0.0001)
                failures.Add($"{p}: часы стоят ({reason})");
        }
        Assert.IsEmpty(failures, string.Join("; ", failures));

        // Кнопка паузы — часы стоят, повторное нажатие — идут.
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        Button pause = root.Q<Button>("map-time-pause");
        Assert.IsNotNull(pause, "Кнопка паузы рядом со скоростями.");
        Invoke(main, "ToggleUserTimePause");
        yield return Frames(5);
        double p0 = Hours();
        float s0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - s0 < 1f)
            yield return null;
        Assert.Less(Hours() - p0, 0.0001, "Пауза останавливает время.");
        Invoke(main, "ToggleUserTimePause");
        yield return Frames(5);
        p0 = Hours();
        s0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - s0 < 1f)
            yield return null;
        Assert.Greater(Hours() - p0, 0.0001, "Снятие паузы пускает время.");
    }

    // Герой — фигура в сцене карты: за домом (дальше от зрителя) дом его
    // закрывает, видна контурная часть; перед домом — герой поверх дома.
    // Картинка героя поверх карты при этом скрыта. Кадры — для просмотра.
    [UnityTest]
    public IEnumerator HeroFigure_InScene_BehindAndInFrontOfHouse()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        Invoke(main, "StartNewGameFromMenu");
        float started = Time.realtimeSinceStartup;
        while ((MapLabGameLive.Current == null || !MapLabGameLive.Current.Ready) && Time.realtimeSinceStartup - started < 300f)
            yield return null;
        Assert.IsTrue(MapLabGameLive.Current != null && MapLabGameLive.Current.Ready);
        yield return Frames(60);
        GameState campaign = CampaignSession.Current;
        TestPeople.AdmitAll(campaign);
        ContinuousSimulationSystem.SetSpeedMultiplier(campaign, ContinuousSimulationSystem.MaximumSpeedMultiplier);
        Assert.Greater(WorldMapObstacles.Count, 0, "У построек есть основания-препятствия.");
        // Ближайшее к старту одиночное дерево на открытой земле: встать за
        // его стволом (дальше от зрителя) и перед ним.
        MapLabGameLive.World live = MapLabGameLive.Current;
        Vector2 start = new Vector2(WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent);
        Vector2 tree = live.View.Data.Objects
            .Where(o => o.Kind == KingdomSurvival.MapLab.MapLabObjectKind.Deciduous)
            .Select(o => MapLabGameLive.NavPercent(live, o.Position.x, o.Position.z))
            .Where(p => WorldMapNavigation.GetTerrainAtPercent(p.x, p.y) != WorldMapGameplayTerrainType.Forest && Vector2.Distance(p, start) > 1.5f)
            .OrderBy(p => Vector2.Distance(p, start))
            .First();
        WorldMapObstacle house = new WorldMapObstacle(tree.x - 0.05f, tree.y - 0.02f, tree.x + 0.05f, tree.y + 0.02f);
        float cx = tree.x;
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        foreach ((string name, float y) in new[] { ("behind", tree.y - 0.3f), ("front", tree.y + 0.3f) })
        {
            Invoke(main, "GoToWorldMapPoint", cx, y);
            float t0 = Time.realtimeSinceStartup;
            yield return Frames(10);
            while (ContinuousSimulationSystem.HasMovementOrActivityInProgress(campaign) && Time.realtimeSinceStartup - t0 < 120f)
                yield return null;
            if (Get<bool>(main, "IsLocationInteractionActive"))
                Invoke(main, "CloseLocationInteraction");
            ExpeditionData he = campaign.ActiveExpedition;
            yield return ZoomTo(main, he != null ? he.CurrentMapXPercent : cx, he != null ? he.CurrentMapYPercent : house.YMax, 0.75f);
            Image heroUi = root.Q<Image>("world-map-hero-figure");
            Assert.IsTrue(heroUi == null || heroUi.resolvedStyle.display == DisplayStyle.None, "Картинка героя поверх карты скрыта — герой в сцене.");
            Transform figure = MapLabGameLive.Current.View.transform.Find("MapLab Figure hero");
            Assert.IsNotNull(figure, "Фигура героя стоит в сцене карты.");
            Assert.IsTrue(figure.gameObject.activeSelf, "Фигура героя видна.");
            ExpeditionData e = campaign.ActiveExpedition;
            Debug.Log($"Герой {name}: точка ({e?.CurrentMapXPercent:0.00}, {e?.CurrentMapYPercent:0.00}), дом y {house.YMin:0.00}..{house.YMax:0.00}");
            foreach (Renderer r in figure.GetComponentsInChildren<Renderer>(true))
                Debug.Log($"Герой {name}: {r.name} слой {r.gameObject.layer} активен {r.gameObject.activeInHierarchy} шейдер {r.sharedMaterial?.shader?.name} поддержан {r.sharedMaterial?.shader?.isSupported} очередь {r.sharedMaterial?.renderQueue} текстура {r.sharedMaterial?.mainTexture?.name} границы {r.bounds} позиция {r.transform.position}; камера {MapLabGameLive.Current.Camera.transform.position} маска {MapLabGameLive.Current.Camera.cullingMask}");
            yield return Capture(main, "hero_" + name + "_tree");
        }
    }

    [UnityTest]
    public IEnumerator RealTravel_ClickEachServiceBuilding_OpensItsWindow()
    {
        SceneManager.LoadScene(MainScene);
        for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != MainScene; i++)
            yield return null;
        yield return Frames(20);
        MonoBehaviour main = FindBehaviour("PrototypeUIController");
        Invoke(main, "StartNewGameFromMenu");
        float started = Time.realtimeSinceStartup;
        while ((MapLabGameLive.Current == null || !MapLabGameLive.Current.Ready) && Time.realtimeSinceStartup - started < 300f)
            yield return null;
        Assert.IsTrue(MapLabGameLive.Current != null && MapLabGameLive.Current.Ready);
        yield return Frames(30);
        GameState campaign = CampaignSession.Current;
        TestPeople.AdmitAll(campaign);
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        // Ближайшая к старту деревня с услугами.
        string villageId = MapLabGameLive.Current.Buildings.Select(b => b.SettlementId).Distinct()
            .Where(id => campaign.FindLocation(id) != null)
            .OrderBy(id => Mathf.Abs(campaign.FindLocation(id).MapXPercent - WorldMapNavigation.CapitalXPercent) + Mathf.Abs(campaign.FindLocation(id).MapYPercent - WorldMapNavigation.CapitalYPercent))
            .First();
        LocationData village = campaign.FindLocation(villageId);
        village.IsVisibleOnMap = true;
        village.IsDiscovered = true;
        Invoke(main, "RefreshWorldMapPanel");
        List<string> roles = MapLabGameLive.Current.Buildings.Where(b => b.SettlementId == villageId).Select(b => b.Role).ToList();
        Debug.Log("MapLab real: деревня " + village.Name + ", здания: " + string.Join(", ", roles));
        ContinuousSimulationSystem.SetSpeedMultiplier(campaign, ContinuousSimulationSystem.MaximumSpeedMultiplier);
        List<string> failures = new List<string>();
        foreach (string role in roles)
        {
            // Если окно или поселение открыты — выйти на карту.
            if (Get<bool>(main, "IsSettlementScreenOpen"))
                Invoke(main, "OnSettlementExitClicked");
            yield return Frames(5);
            Rect rect = MapLabGameLive.Current.Buildings.First(b => b.SettlementId == villageId && b.Role == role).Percent;
            yield return ZoomTo(main, rect.center.x, rect.center.y, 0.55f);
            VisualElement target = root.Query<VisualElement>("world-map-building-" + role).ToList().FirstOrDefault(e => (e.userData as string) == villageId);
            if (target == null)
            {
                failures.Add(role + ": нет цели");
                continue;
            }
            Rect bounds = target.worldBound;
            VisualElement picked = root.panel.Pick(new Vector2(bounds.center.x, bounds.yMin + bounds.height * 0.62f));
            if (picked == null || !(picked == target || target.Contains(picked)))
            {
                failures.Add(role + ": курсор попадает в " + (picked?.name ?? "ничто"));
                continue;
            }
            using (ClickEvent click = ClickEvent.GetPooled())
            {
                click.target = picked;
                picked.SendEvent(click);
            }
            float t0 = Time.realtimeSinceStartup;
            string last = string.Empty;
            while (!Get<bool>(main, "IsSettlementScreenOpen") && Time.realtimeSinceStartup - t0 < 120f)
            {
                ExpeditionData e = campaign.ActiveExpedition;
                string state = e == null ? "нет похода" : $"{e.Phase} loc={e.LocationId} inside={SettlementCampService.InsideLocationId(campaign)} pending={Field<string>(main, "worldMapPendingRole")} interaction={Get<bool>(main, "IsLocationInteractionActive")}";
                if (state != last)
                {
                    Debug.Log("MapLab real: " + role + " — " + state);
                    last = state;
                }
                yield return null;
            }
            if (!Get<bool>(main, "IsSettlementScreenOpen"))
                failures.Add(role + ": окно не открылось, последнее состояние — " + last);
            else
            {
                Debug.Log("MapLab real: " + role + " — окно открыто за " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " с");
                yield return Frames(5);
                // Окно здания — не общий экран поселения: знахарка — своя
                // страница, «Вернуться в поселение» нигде нет.
                string mode = Field<object>(main, "settlementMode").ToString();
                Debug.Log("MapLab real: " + role + " — режим окна " + mode);
                if (role == "healer" && mode != "Building")
                    failures.Add(role + ": открыт режим " + mode + ", а не страница здания");
                if (role != "elder" && mode == "Main" && !Get<bool>(main, "IsSettlementTradeOpen") && !Get<bool>(main, "IsSettlementBoardOpen"))
                    failures.Add(role + ": открыт общий экран поселения");
                if (root.Query<Button>().ToList().Any(b => b.text == "Вернуться в поселение" && b.resolvedStyle.display != DisplayStyle.None))
                    failures.Add(role + ": есть кнопка «Вернуться в поселение»");
                // «Назад» из лавки и доски — на карту, а не на экран поселения.
                if (role == "merchant" || role == "board")
                {
                    Invoke(main, role == "merchant" ? "OnTradeBackClicked" : "CloseSettlementBoard");
                    yield return Frames(5);
                    if (Get<bool>(main, "IsSettlementScreenOpen"))
                        failures.Add(role + ": «Назад» оставил экран поселения");
                }
            }
            yield return Frames(5);
        }
        Assert.IsEmpty(failures, string.Join("; ", failures));
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
        // Дворы с оградами всех деревень партии не заходят друг на друга.
        List<KingdomSurvival.MapLab.MapLabYardRect> yards = MapLabGameLive.Current.View.Data.YardRects;
        Assert.Greater(yards.Count, 10, "В деревнях партии есть дворы.");
        for (int ya = 0; ya < yards.Count; ya++)
            for (int yb = ya + 1; yb < yards.Count; yb++)
                Assert.IsFalse(yards[ya].Overlaps(yards[yb], 0.1f), $"Дворы {ya} и {yb} заходят друг на друга.");
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

        // Отряд идёт прямо к двери лавки (цель пути — дверь, не центр деревни).
        MapPointData target = expedition.Route[expedition.Route.Count - 1];
        Vector2 shopDoor = MapLabGameLive.Current.Buildings.First(b => b.SettlementId == village.Id && b.Role == "merchant").Door;
        Assert.Less(Mathf.Abs(target.XPercent - shopDoor.x) + Mathf.Abs(target.YPercent - shopDoor.y), 0.8f, "Путь ведёт к двери лавки, а не в центр поселения.");
        Image hero = root.Q<Image>("world-map-hero-figure");
        Assert.IsNotNull(hero);
        Assert.IsTrue(MapLabGameLive.HeroFrame(0, out Texture2D frame, out _, out _, out _), "Есть кадры фишки MapLab.");
        yield return Frames(30);
        // Герой — фигура в сцене карты (кадр фишки MapLab), не картинка поверх.
        Transform heroFigure = MapLabGameLive.Current.View.transform.Find("MapLab Figure hero");
        Assert.IsNotNull(heroFigure, "Герой нарисован кадром фишки MapLab в сцене карты.");
        Assert.IsTrue(heroFigure.gameObject.activeSelf, "Фигура героя видна.");
        yield return Capture(main, "live_04_walking");
        // Дорогу пропускаем: отряд у двери, прибытие — вход и окно лавки.
        expedition.CurrentMapXPercent = target.XPercent;
        expedition.CurrentMapYPercent = target.YPercent;
        expedition.Phase = CommanderState.AtLocation;
        expedition.Route = new List<MapPointData> { new MapPointData(target.XPercent, target.YPercent) };
        expedition.RouteIndex = 0;
        expedition.RemainingRouteCells = 0;
        Invoke(main, "TryOpenLocationInteraction", village.Id);
        Assert.IsNull(root.Q<VisualElement>("settlement-map-panel")?.resolvedStyle.display == DisplayStyle.Flex ? "плашка" : null, "Плашки «Поселение / Уйти» нет.");
        float started2 = Time.realtimeSinceStartup;
        while (!Get<bool>(main, "IsSettlementScreenOpen") && Time.realtimeSinceStartup - started2 < 40f)
            yield return null;
        Assert.IsTrue(Get<bool>(main, "IsSettlementScreenOpen"), "Дошли до двери — окно услуги открыто.");
        yield return Frames(5);
        Assert.IsTrue(Get<bool>(main, "IsSettlementTradeOpen"), "Клик по лавке — сразу торговля.");
        yield return Capture(main, "live_05_service");
        // Закрыли окно — отряд на карте у лавки, не «внутри поселения».
        Invoke(main, "OnSettlementExitClicked");
        yield return Frames(5);
        Assert.IsFalse(Get<bool>(main, "IsSettlementScreenOpen"));
        Assert.IsFalse(SettlementCampService.IsCommanderInside(campaign), "После окна здания отряд снова на карте.");
    }
}
