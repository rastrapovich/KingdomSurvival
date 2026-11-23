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

// Запрос 23.11.2026: наклон камеры живой карты следует за приближением (базовый на общем виде →
// 35° на наибольшем). В настоящей сцене игры: зум колесом (ZoomWorldMapAroundScreenPoint) от
// минимума до максимума и обратно — точка земли под курсором стоит на месте, высота полотна
// меняется без скачков; значки зданий и мест на 50°/42°/35° стоят на проекции своей опоры через
// камеру мира; цели не «прыгают» на кадр после шага зума; клик по земле и по зданию на 35°.
// Кадры 1920×1080 — при заданной KS_SCREENSHOT_DIR.
public sealed class WorldMapPitchPlayModeTests
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

    private static VisualElement Viewport(MonoBehaviour main) => Field<VisualElement>(main, "worldMapViewport");

    private static Vector2 ViewportCenter(MonoBehaviour main)
    {
        VisualElement v = Viewport(main);
        return new Vector2(v.resolvedStyle.width * 0.5f, v.resolvedStyle.height * 0.5f);
    }

    // Земля под точкой рамки карты (как при клике): проценты карты пути.
    private static Vector2 GroundAt(MonoBehaviour main, Vector2 viewportPoint) =>
        (Vector2)Invoke(main, "WorldMapPanelToPercent", Viewport(main).LocalToWorld(viewportPoint));

    // Где земля (проценты карты пути) нарисована сейчас: точка рамки.
    private static Vector2 ScreenOfGround(MonoBehaviour main, Vector2 ground)
    {
        float shift = MapLabGameLive.ElevationShiftPercent(MapLabGameLive.Current, ground.x, ground.y);
        return (Vector2)Invoke(main, "MapPercentToWorldMapViewport", ground.x, ground.y + shift);
    }

    private static void SetZoom(MonoBehaviour main, float zoom)
    {
        SetField(main, "worldMapZoom", zoom);
        SetField(main, "worldMapZoomTarget", zoom);
    }

    // Приближение на доле диапазона (в логарифме): 0 — минимум, 1 — максимум.
    private static float ZoomAtShare(MonoBehaviour main, float share)
    {
        float min = Field<float>(main, "worldMapMinZoom"), max = Field<float>(main, "worldMapMaxZoom");
        return min * Mathf.Pow(max / min, share);
    }

    // Приближение, при котором наклон равен заданному (деление пополам по логарифму).
    private static float ZoomForPitch(MonoBehaviour main, float pitch)
    {
        float min = Field<float>(main, "worldMapMinZoom"), max = Field<float>(main, "worldMapMaxZoom");
        float lo = 0f, hi = 1f;
        for (int i = 0; i < 40; i++)
        {
            float mid = (lo + hi) * 0.5f;
            float zoom = min * Mathf.Pow(max / min, mid);
            if (MapLabGameLive.PitchForZoom(zoom, min, max, MapLabGameLive.Current.BasePitch) > pitch)
                lo = mid;
            else
                hi = mid;
        }
        return min * Mathf.Pow(max / min, (lo + hi) * 0.5f);
    }

    private static IEnumerator StartGame(System.Action<MonoBehaviour, GameState> ready)
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
        Assert.IsTrue(MapLabGameLive.Current != null && MapLabGameLive.Current.Ready, "Живая карта построена.");
        yield return Frames(60);
        GameState campaign = CampaignSession.Current;
        TestPeople.AdmitAll(campaign);
        ready(main, campaign);
    }

    // Ближайшая к старту деревня со зданиями услуг; делает её видимой на карте.
    private static LocationData NearestVillage(MonoBehaviour main, GameState campaign)
    {
        string id = MapLabGameLive.Current.Buildings.Select(b => b.SettlementId).Distinct()
            .Where(v => campaign.FindLocation(v) != null)
            .OrderBy(v => Mathf.Abs(campaign.FindLocation(v).MapXPercent - WorldMapNavigation.CapitalXPercent) + Mathf.Abs(campaign.FindLocation(v).MapYPercent - WorldMapNavigation.CapitalYPercent))
            .First();
        LocationData village = campaign.FindLocation(id);
        village.IsVisibleOnMap = true;
        village.IsDiscovered = true;
        WorldMapFog.RevealAround(campaign, village.MapXPercent, village.MapYPercent, 7.0);
        Invoke(main, "RefreshWorldMapPanel");
        return village;
    }

    // Зум шагами от минимума до максимума и обратно: точка земли под курсором стоит, полотно без скачков.
    [UnityTest]
    public IEnumerator ZoomThroughPitchRange_KeepsGroundUnderCursor_AndCanvasSmooth()
    {
        MonoBehaviour main = null;
        yield return StartGame((m, c) => main = m);
        float min = Field<float>(main, "worldMapMinZoom"), max = Field<float>(main, "worldMapMaxZoom");
        Assert.Greater(max, min * 2f, "есть диапазон приближения");
        MapLabGameLive.World world = MapLabGameLive.Current;
        SetZoom(main, min);
        Invoke(main, "CenterWorldMapOn", 50f, 50f);
        Invoke(main, "ApplyWorldMapViewportTransform");
        yield return Frames(8);
        Assert.That(world.Pitch, Is.EqualTo(world.BasePitch).Within(0.05f), "на минимальном зуме — базовый наклон");

        const int steps = 24;
        float factor = Mathf.Pow(max / min, 1f / steps);
        float maxError = 0f, maxJump = 0f, minPitch = world.Pitch, maxPitch = world.Pitch;
        int checkedSteps = 0;
        for (int direction = 0; direction < 2; direction++)
        {
            for (int i = 0; i < steps; i++)
            {
                Vector2 pivot = ViewportCenter(main) + new Vector2(40f, 20f);
                Vector2 ground = GroundAt(main, pivot);
                float heightBefore = Field<float>(main, "worldMapCanvasHeight");
                Invoke(main, "ZoomWorldMapAroundScreenPoint", direction == 0 ? factor : 1f / factor, pivot);
                float heightAfter = Field<float>(main, "worldMapCanvasHeight");
                // Сразу после шага (тот же кадр) и через кадр — земля под курсором на месте.
                float zoom = Field<float>(main, "worldMapZoom");
                VisualElement viewport = Viewport(main);
                bool free = Field<float>(main, "worldMapCanvasWidth") * zoom > viewport.resolvedStyle.width * 1.05f
                            && heightAfter * zoom > viewport.resolvedStyle.height * 1.05f;
                float errNow = Vector2.Distance(ScreenOfGround(main, ground), pivot);
                yield return null;
                float errLater = Vector2.Distance(ScreenOfGround(main, ground), pivot);
                if (free)
                {
                    maxError = Mathf.Max(maxError, errNow, errLater);
                    checkedSteps++;
                }
                Assert.Less(errNow, 2f, $"шаг {direction}/{i}: земля под курсором сдвинулась на {errNow:0.0} px сразу после шага (наклон {world.Pitch:0.0}°)");
                Assert.Less(errLater, 2f, $"шаг {direction}/{i}: земля под курсором сдвинулась на {errLater:0.0} px через кадр");
                float jump = Mathf.Abs(heightAfter / heightBefore - 1f);
                maxJump = Mathf.Max(maxJump, jump);
                Assert.Less(jump, 0.05f, $"шаг {direction}/{i}: высота полотна скачет на {jump:P1}");
                minPitch = Mathf.Min(minPitch, world.Pitch);
                maxPitch = Mathf.Max(maxPitch, world.Pitch);
            }
        }
        Debug.Log($"Pitch zoom: шагов проверено {checkedSteps}, наибольший сдвиг точки под курсором {maxError:0.00} px, наибольший скачок высоты полотна {maxJump:P2}, наклон {minPitch:0.0}…{maxPitch:0.0}°");
        Assert.That(minPitch, Is.EqualTo(MapLabGameLive.ClosePitch).Within(0.2f), "на максимальном приближении — 35°");
        Assert.That(maxPitch, Is.EqualTo(world.BasePitch).Within(0.2f), "на общем виде — базовый");
        Assert.GreaterOrEqual(checkedSteps, 20);
    }

    // Значки: цели зданий и мест на 50°, 42°, 35° стоят на проекции опоры через камеру мира; после
    // шага зума они переставлены в тот же кадр.
    [UnityTest]
    public IEnumerator BuildingAndPlaceTargets_FollowCameraProjection_AtEveryPitch_WithoutOneFrameJump()
    {
        MonoBehaviour main = null;
        GameState campaign = null;
        yield return StartGame((m, c) => { main = m; campaign = c; });
        LocationData village = NearestVillage(main, campaign);
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        MapLabGameLive.World world = MapLabGameLive.Current;
        int index = world.Buildings.FindIndex(b => b.SettlementId == village.Id && b.Role == "merchant");
        if (index < 0)
            index = world.Buildings.FindIndex(b => b.SettlementId == village.Id);
        Assert.GreaterOrEqual(index, 0, "у деревни есть здание");
        string role = world.Buildings[index].Role;
        float worst = 0f;

        foreach (float pitch in new[] { 50f, 42f, 35f })
        {
            SetZoom(main, pitch >= 50f ? ZoomAtShare(main, 0.4f) : ZoomForPitch(main, pitch));
            Rect rect0 = world.Buildings[index].Percent;
            Invoke(main, "CenterWorldMapOn", rect0.center.x, rect0.center.y);
            Invoke(main, "ApplyWorldMapViewportTransform");
            yield return Frames(10);
            Assert.That(world.Pitch, Is.EqualTo(pitch).Within(0.2f));
            worst = Mathf.Max(worst, CheckBuildingProjection(main, world, root, index, village.Id, role, $"{pitch}°"));

            // Шаг зума с курсором у здания: цели переставлены сразу, до первого кадра после шага.
            Vector2 pivot = ViewportCenter(main) + new Vector2(60f, 30f);
            Invoke(main, "ZoomWorldMapAroundScreenPoint", pitch > 40f ? 1.15f : 1f / 1.15f, pivot);
            AssertTargetStylesMatchWorld(root, world, index, village.Id, role, $"сразу после шага зума ({pitch}°)");
            yield return Frames(3);
            AssertTargetStylesMatchWorld(root, world, index, village.Id, role, $"через кадры ({pitch}°)");
        }
        Debug.Log($"Pitch targets: наибольшее расхождение опоры здания и проекции камеры {worst:0.00} px");
    }

    private static float CheckBuildingProjection(MonoBehaviour main, MapLabGameLive.World world, VisualElement root, int index, string villageId, string role, string label)
    {
        VisualElement target = root.Query<VisualElement>("world-map-building-" + role).ToList().FirstOrDefault(e => (e.userData as string) == villageId);
        Assert.IsNotNull(target, $"{label}: цель здания {role}");
        MapLabGameLive.CardSource source = world.BuildingSources[index];
        VisualElement viewport = Viewport(main);
        // Камера мира: проекция опоры (последний рисованный кадр) в рамку карты.
        Vector3 vp = world.Camera.WorldToViewportPoint(source.Base);
        Vector2 byCamera = new Vector2(vp.x * viewport.resolvedStyle.width, (1f - vp.y) * viewport.resolvedStyle.height);
        // Цель здания: опора по её прямоугольнику в рамке.
        Rect wb = target.worldBound;
        Vector2 byTarget = viewport.WorldToLocal(new Vector2(wb.x + source.Pivot.x * wb.width, wb.y + (1f - source.Pivot.y) * wb.height));
        float error = Vector2.Distance(byCamera, byTarget);
        Assert.Less(error, 3f, $"{label}: опора цели здания и проекция камеры расходятся на {error:0.0} px (камера {byCamera}, цель {byTarget})");
        return error;
    }

    private static void AssertTargetStylesMatchWorld(VisualElement root, MapLabGameLive.World world, int index, string villageId, string role, string label)
    {
        VisualElement target = root.Query<VisualElement>("world-map-building-" + role).ToList().FirstOrDefault(e => (e.userData as string) == villageId);
        Assert.IsNotNull(target, label + ": цель здания");
        Rect rect = world.Buildings[index].Percent;
        Assert.That(target.style.left.value.value, Is.EqualTo(rect.x).Within(0.001f), label + ": left");
        Assert.That(target.style.top.value.value, Is.EqualTo(rect.y).Within(0.001f), label + ": top");
        Assert.That(target.style.width.value.value, Is.EqualTo(rect.width).Within(0.001f), label + ": width");
        Assert.That(target.style.height.value.value, Is.EqualTo(rect.height).Within(0.001f), label + ": height");
        // Детали мест: каждая цель стоит на одном из прямоугольников своего места.
        foreach (VisualElement t in root.Query<VisualElement>().Where(e => e.name != null && e.name.StartsWith("world-map-feature-")).ToList())
        {
            GeneratedMapSite site = t.userData as GeneratedMapSite;
            Assert.IsNotNull(site, label + ": у цели места есть место");
            bool match = false;
            foreach ((GeneratedMapSite s, Rect _, List<(Rect Rect, string ArtId)> parts) in world.Features)
                if (s == site)
                    foreach ((Rect r, string _) in parts)
                        match |= Mathf.Abs(t.style.left.value.value - r.x) < 0.001f && Mathf.Abs(t.style.top.value.value - r.y) < 0.001f
                                 && Mathf.Abs(t.style.height.value.value - r.height) < 0.001f;
            Assert.IsTrue(match, $"{label}: цель места {site.Name} стоит не по текущему наклону");
        }
    }

    // Клик по земле и по зданию на 35°; кадры у поселения на 50° и 35°.
    [UnityTest]
    public IEnumerator ClickOnGroundAndBuilding_AtMaxZoom_HitsTheDrawnPoint()
    {
        MonoBehaviour main = null;
        GameState campaign = null;
        yield return StartGame((m, c) => { main = m; campaign = c; });
        LocationData village = NearestVillage(main, campaign);
        VisualElement root = main.GetComponent<UIDocument>().rootVisualElement;
        MapLabGameLive.World world = MapLabGameLive.Current;
        ContinuousSimulationSystem.SetSpeedMultiplier(campaign, ContinuousSimulationSystem.MaximumSpeedMultiplier);
        int index = world.Buildings.FindIndex(b => b.SettlementId == village.Id && b.Role == "merchant");
        if (index < 0)
            index = world.Buildings.FindIndex(b => b.SettlementId == village.Id);
        string role = world.Buildings[index].Role;
        Rect rect = world.Buildings[index].Percent;

        // Кадр у поселения на 50° (вид со средним приближением), затем на 35°.
        SetZoom(main, ZoomAtShare(main, 0.4f));
        Invoke(main, "CenterWorldMapOn", rect.center.x, rect.center.y);
        Invoke(main, "ApplyWorldMapViewportTransform");
        yield return Frames(20);
        yield return Capture(main, "pitch_50_village");

        float max = Field<float>(main, "worldMapMaxZoom");
        SetZoom(main, max);
        Invoke(main, "CenterWorldMapOn", rect.center.x, rect.center.y);
        Invoke(main, "ApplyWorldMapViewportTransform");
        yield return Frames(20);
        Assert.That(world.Pitch, Is.EqualTo(MapLabGameLive.ClosePitch).Within(0.2f));
        yield return Capture(main, "pitch_35_village");

        // Клик по зданию на 35°: курсор над его целью попадает именно в неё.
        VisualElement target = root.Query<VisualElement>("world-map-building-" + role).ToList().FirstOrDefault(e => (e.userData as string) == village.Id);
        Assert.IsNotNull(target, "цель здания на 35°");
        Rect bounds = target.worldBound;
        VisualElement picked = root.panel.Pick(new Vector2(bounds.center.x, bounds.yMin + bounds.height * 0.62f));
        Assert.IsTrue(picked != null && (picked == target || target.Contains(picked)), "клик по зданию на 35° попадает в его цель: " + (picked?.name ?? "ничто"));

        // Клик по земле рядом с героем: обратный пик → отметка и герой в этой точке.
        EnsureHeroOnMap(campaign);
        ExpeditionData expedition = campaign.ActiveExpedition;
        float heroX = expedition != null ? expedition.CurrentMapXPercent : WorldMapNavigation.CapitalXPercent;
        float heroY = expedition != null ? expedition.CurrentMapYPercent : WorldMapNavigation.CapitalYPercent;
        Invoke(main, "CenterWorldMapOn", heroX, heroY + MapLabGameLive.ElevationShiftPercent(world, heroX, heroY));
        Invoke(main, "ApplyWorldMapViewportTransform");
        yield return Frames(15);
        Vector2 click = ViewportCenter(main) + new Vector2(110f, -30f);
        Vector2 ground = GroundAt(main, click);
        Invoke(main, "GoToWorldMapPoint", ground.x, ground.y);
        yield return Frames(3);
        float markerX = Field<float>(main, "worldMapClickMarkerXPercent"), markerY = Field<float>(main, "worldMapClickMarkerYPercent");
        Vector2 markerScreen = ScreenOfGround(main, new Vector2(markerX, markerY));
        Assert.Less(Vector2.Distance(markerScreen, click), 3f, $"отметка клика на 35° стоит под курсором (расхождение {Vector2.Distance(markerScreen, click):0.0} px)");
        float t0 = Time.realtimeSinceStartup;
        while (ContinuousSimulationSystem.HasMovementOrActivityInProgress(campaign) && Time.realtimeSinceStartup - t0 < 90f)
            yield return null;
        yield return Frames(10);
        ExpeditionData after = campaign.ActiveExpedition;
        Assert.IsNotNull(after, "герой на карте");
        Vector2 heroScreen = ScreenOfGround(main, new Vector2(after.CurrentMapXPercent, after.CurrentMapYPercent));
        float heroError = Vector2.Distance(heroScreen, click);
        Debug.Log($"Pitch click: отметка {Vector2.Distance(markerScreen, click):0.0} px, герой {heroError:0.0} px от точки клика; цель клика ({ground.x:0.00}; {ground.y:0.00}), герой ({after.CurrentMapXPercent:0.00}; {after.CurrentMapYPercent:0.00})");
        Assert.Less(heroError, 6f, "герой встал в точку клика на 35°");
        yield return Capture(main, "pitch_35_hero_after_click");
    }

    private static void EnsureHeroOnMap(GameState campaign)
    {
        // Партия начинается в лагере у дороги: отряд «на карте» появляется с первым приказом.
        if (campaign.ActiveExpedition == null)
            return;
    }
}
