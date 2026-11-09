using System;
using System.Collections;
using System.Collections.Generic;
using KingdomSurvival.MapLab;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace KingdomSurvival.MapLabBridge
{
    // 12У-F/G: живая карта MapLab в игре. MapLab-мир плана партии строится
    // один раз (скрытый слой), и каждый кадр камера MapLab рисует ровно ту
    // часть карты, что видна на экране, в экранном разрешении — тот же
    // рисунок, что в предпросмотре генератора, без снимка и растяжения.
    // Земля проецируется ортографической камерой под рабочим наклоном: x — как
    // на холсте, глубина — со сжатием sin(наклона); поэтому полотно UI имеет
    // высоту CanvasHeight·sin(наклона), и проценты значков, героя, тумана и
    // областей поиска совпадают с рисунком. Подробные участки покраски
    // подгружаются в фоне по мере приближения камеры (как в предпросмотре).
    public static class MapLabGameLive
    {
        public const int Layer = 31;

        public sealed class World
        {
            public GeneratedWorldMap Plan;
            public string ServicesKey;
            public MapLabView View;
            public Camera Camera;
            public MapLabCameraRig Rig;
            public MapLabSettings Settings;
            public Rect Play;
            public float Mean, Pitch, Sin, Cos;
            public RenderTexture Target;
            public string Error = string.Empty;
            public double BuildMs;
            // Пошаговая сборка: вид до готовности, шаги и часы сборки.
            public MapLabView PendingView;
            public IEnumerator Pending;
            public System.Diagnostics.Stopwatch Watch;
            public MapLabAssetsSnapshot Assets;
            public bool Building => Pending != null;
            // Что сейчас строится (для подписи «Рисуем карту…»).
            public string Stage => PendingView != null ? PendingView.BuildStage : string.Empty;
            // Доля готовности 0..1 (полоска загрузки).
            public float Progress => Ready ? 1f : PendingView != null ? PendingView.BuildProgress : 0f;
            // Здания услуг: поселение, роль, прямоугольник рисунка (проценты
            // полотна) и точка двери (проценты, на земле перед зданием).
            public readonly List<(string SettlementId, string Role, Rect Percent, Vector2 Door)> Buildings = new List<(string, string, Rect, Vector2)>();
            // Детали мест: место плана, общий прямоугольник рисунка всех его
            // частей (проценты полотна) и опоры частей.
            public readonly List<(GeneratedMapSite Site, Rect Percent, List<(Rect Rect, string ArtId)> Parts)> Features = new List<(GeneratedMapSite, Rect, List<(Rect, string)>)>();
            public bool Ready => View != null && View.Data != null && string.IsNullOrEmpty(Error);
            // Пикселей полотна UI на единицу мира MapLab (по ширине).
            public float CanvasPixelsPerUnit(float canvasWidth) => canvasWidth / Mathf.Max(1f, Play.width);
        }

        private static World current;

        public static World Current => current;

        // Сжатие глубины по наклону из ресурсов (известно до построения мира).
        public static float ExpectedHeightScale
        {
            get
            {
                MapLabGameAssets assets = MapLabGameAssets.Load();
                return Mathf.Sin(Mathf.Clamp(assets != null ? assets.PitchDegrees : 50f, 20f, 80f) * Mathf.Deg2Rad);
            }
        }

        public static bool Matches(GeneratedWorldMap plan, string servicesKey) =>
            current != null && ReferenceEquals(current.Plan, plan) && current.ServicesKey == servicesKey;

        public static void Clear()
        {
            WorldMapObstacles.Clear();
            if (current == null)
                return;
            MapLabView view = current.View != null ? current.View : current.PendingView;
            current.Pending = null;
            if (view != null)
            {
                view.Clear();
                Object.DestroyImmediate(view.gameObject);
            }
            if (current.Camera != null)
                Object.DestroyImmediate(current.Camera.gameObject);
            if (current.Settings != null)
                Object.DestroyImmediate(current.Settings);
            if (current.Target != null)
            {
                current.Target.Release();
                Object.DestroyImmediate(current.Target);
            }
            current = null;
        }

        // Построить мир MapLab для плана партии сразу (пакетные проверки).
        public static World Build(GeneratedWorldMap plan, IDictionary<string, List<string>> services)
        {
            World w = Begin(plan, services);
            while (!Advance(w))
                System.Threading.Thread.Sleep(1);
            return w;
        }

        // Начать пошаговую сборку: тяжёлые расчёты идут в фоне, а Advance раз
        // в кадр создаёт готовые части в главном потоке (игра не замирает).
        public static World Begin(GeneratedWorldMap plan, IDictionary<string, List<string>> services)
        {
            string key = MapLabGameMap.ServicesKey(services);
            if (Matches(plan, key))
                return current;
            Clear();
            World w = new World { Plan = plan, ServicesKey = key };
            current = w;
            MapLabGameAssets assets = MapLabGameAssets.Load();
            w.Watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (assets == null || assets.Profile == null)
                    throw new InvalidOperationException("нет MapLabGameAssets (Генератор карты → Сервис → Подготовить рисунок карты для игры)");
                w.Settings = Object.Instantiate(assets.Profile);
                w.Settings.name = "MapLab Game Live Profile";
                MapLabComposition composition = MapLabRegionPlan.Apply(plan, w.Settings, includeActors: false);
                if (services != null)
                    foreach (MapLabComposition.Settlement st in composition.Settlements)
                        if (services.TryGetValue(st.Id, out List<string> roles) && roles != null)
                            st.Services.AddRange(roles);
                // Подгрузка покраски — как в предпросмотре больших карт.
                w.Settings.Stage05.PaintStreaming = MapLabPaintStreaming.On;

                GameObject cameraObject = new GameObject("MapLab Live Camera") { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
                w.Camera = cameraObject.AddComponent<Camera>();
                w.Camera.enabled = false;
                w.Camera.orthographic = true;
                w.Camera.allowMSAA = true;
                w.Camera.allowHDR = false;
                w.Camera.clearFlags = CameraClearFlags.SolidColor;
                w.Camera.cullingMask = 1 << Layer;
                w.Rig = cameraObject.AddComponent<MapLabCameraRig>();
                w.Rig.enabled = false;

                GameObject mapObject = new GameObject("MapLab Live") { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
                // Пока строится — неактивен: части не видны чужим камерам и
                // подгрузка покраски не стартует раньше конца сборки.
                mapObject.SetActive(false);
                MapLabView view = mapObject.AddComponent<MapLabView>();
                view.GenerateOnLoad = false;
                MapLabMarker marker = mapObject.AddComponent<MapLabMarker>();
                marker.AutoPlace = false;
                marker.enabled = false;
                assets.ApplyTo(view);
                view.CameraRig = w.Rig;
                view.Settings = w.Settings;
                w.PendingView = view;
                w.Assets = new MapLabAssetsSnapshot { PitchDegrees = assets.PitchDegrees };
                w.Pending = view.GenerateStepwise();
            }
            catch (Exception exception)
            {
                Fail(w, exception);
            }
            return w;
        }

        // Продвинуть сборку на шаг; true — сборка закончена (готово или ошибка).
        public static bool Advance(World w)
        {
            if (w == null || w.Pending == null)
                return true;
            try
            {
                if (w.Pending.MoveNext())
                    return false;
                w.Pending = null;
                Finish(w);
            }
            catch (Exception exception)
            {
                Fail(w, exception);
            }
            return true;
        }

        private static void Fail(World w, Exception exception)
        {
            w.Pending = null;
            w.Error = "MapLab: " + exception.Message;
            Debug.LogException(exception);
            if (w.Watch != null)
                w.BuildMs = w.Watch.Elapsed.TotalMilliseconds;
        }

        private static void Finish(World w)
        {
            MapLabView view = w.PendingView;
            {
                if (view == null || view.Data == null)
                    throw new InvalidOperationException("генерация не выполнена — " + view?.LastError);
                GameObject mapObject = view.gameObject;
                w.View = view;
                Transform markerRoot = mapObject.transform.Find(MapLabMarker.RootName);
                if (markerRoot != null)
                    markerRoot.gameObject.SetActive(false);
                SetLayer(mapObject.transform);

                MapLabData data = view.Data;
                w.Play = data.PlayRect.width > 0f ? data.PlayRect : new Rect(0f, 0f, data.Size, data.Size);
                w.Pitch = Mathf.Clamp(w.Assets.PitchDegrees, 20f, 80f);
                w.Sin = Mathf.Sin(w.Pitch * Mathf.Deg2Rad);
                w.Cos = Mathf.Cos(w.Pitch * Mathf.Deg2Rad);
                float mean = 0f;
                for (int i = 0; i < 16; i++)
                    for (int j = 0; j < 16; j++)
                        mean += data.SampleHeight(w.Play.xMin + (i + 0.5f) / 16f * w.Play.width, w.Play.yMin + (j + 0.5f) / 16f * w.Play.height);
                w.Mean = mean / 256f;
                w.Camera.backgroundColor = view.Palette.Paper;
                foreach (MapLabServiceBuilding b in data.ServiceBuildings)
                {
                    Vector2 basePercent = ToPercent(w, b.Base);
                    float widthPercent = b.Width / w.Play.width * 100f;
                    float heightPercent = b.Height * 100f / (w.Play.height * w.Sin);
                    Rect rect = new Rect(basePercent.x - b.Pivot.x * widthPercent, basePercent.y - (1f - b.Pivot.y) * heightPercent, widthPercent, heightPercent);
                    // Точка подхода — на земле перед зданием (к зрителю), за его
                    // основанием: герой встаёт рядом, а не внутри рисунка. Узкий
                    // столб объявлений — подходят сбоку.
                    Vector3 approach = b.Role == "board"
                        ? b.Base + new Vector3(b.Width * 0.45f + 0.9f, 0f, -0.4f)
                        : b.Base + new Vector3(0f, 0f, -(b.Width * 0.18f + 1.1f));
                    approach.y = data.SampleHeight(approach.x, approach.z);
                    w.Buildings.Add((b.SettlementId, b.Role, rect, NavPercent(w, approach.x, approach.z)));
                }
                // Детали мест: части — к месту плана по виду и ближайшей точке.
                Dictionary<GeneratedMapSite, List<(Rect Rect, string ArtId)>> parts = new Dictionary<GeneratedMapSite, List<(Rect, string)>>();
                foreach (MapLabFeatureSprite f in data.FeatureSprites)
                {
                    GeneratedMapSite site = NearestFeature(w, f.Kind, f.Point);
                    if (site == null)
                        continue;
                    Vector2 basePercent = ToPercent(w, f.Base);
                    float widthPercent = f.Width / w.Play.width * 100f;
                    float heightPercent = f.Height * 100f / (w.Play.height * w.Sin);
                    Rect rect = new Rect(basePercent.x - f.Pivot.x * widthPercent, basePercent.y - (1f - f.Pivot.y) * heightPercent, widthPercent, heightPercent);
                    if (!parts.TryGetValue(site, out List<(Rect Rect, string ArtId)> list))
                        parts[site] = list = new List<(Rect, string)>();
                    list.Add((rect, f.ArtId));
                }
                foreach (KeyValuePair<GeneratedMapSite, List<(Rect Rect, string ArtId)>> pair in parts)
                {
                    Rect union = pair.Value[0].Rect;
                    foreach ((Rect r, string _) in pair.Value)
                        union = Rect.MinMaxRect(Mathf.Min(union.xMin, r.xMin), Mathf.Min(union.yMin, r.yMin), Mathf.Max(union.xMax, r.xMax), Mathf.Max(union.yMax, r.yMax));
                    w.Features.Add((pair.Key, union, pair.Value));
                }
                WorldMapObstacles.Set(Obstacles(w, data));
                mapObject.SetActive(true);
            }
            w.BuildMs = w.Watch.Elapsed.TotalMilliseconds;
            if (w.Ready)
                Debug.Log($"MapLab: живая карта партии построена за {w.BuildMs / 1000.0:0.0} с, зданий услуг {w.Buildings.Count}.");
        }

        public struct MapLabAssetsSnapshot
        {
            public float PitchDegrees;
        }

        // Место плана (деталь или видимое место) по виду и точке рисунка.
        private static GeneratedMapSite NearestFeature(World w, string kind, Vector2 point)
        {
            GeneratedMapSite best = null;
            float bestDistance = float.MaxValue;
            void Consider(List<GeneratedMapSite> list)
            {
                if (list == null)
                    return;
                foreach (GeneratedMapSite site in list)
                {
                    if (site == null || site.Hidden || site.Kind != kind)
                        continue;
                    Vector2 at = new Vector2(w.Play.xMin + site.XPercent / 100f * w.Play.width, w.Play.yMax - site.YPercent / 100f * w.Play.height);
                    float d = (at - point).sqrMagnitude;
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = site;
                    }
                }
            }
            Consider(w.Plan.Features);
            Consider(w.Plan.Landmarks);
            return best;
        }

        private static void SetLayer(Transform t)
        {
            t.gameObject.layer = Layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayer(t.GetChild(i));
        }

        // Точка мира → проценты полотна UI (с учётом высоты рельефа: точка на
        // холме видна выше — как на рисунке).
        // Основания построек (дома, амбары) и предметов мест — препятствия пути
        // героя и групп (проценты карты). Лес, трава и камни — не препятствия.
        private static List<WorldMapObstacle> Obstacles(World w, MapLabData data)
        {
            List<WorldMapObstacle> list = new List<WorldMapObstacle>();
            void Add(Vector3 center, Vector2 axisU, float halfU, float halfV)
            {
                Vector2 axisV = new Vector2(-axisU.y, axisU.x);
                float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 c = new Vector2(center.x, center.z) + axisU * (i % 2 == 0 ? -halfU : halfU) + axisV * (i < 2 ? -halfV : halfV);
                    Vector2 p = NavPercent(w, c.x, c.y);
                    xMin = Mathf.Min(xMin, p.x);
                    yMin = Mathf.Min(yMin, p.y);
                    xMax = Mathf.Max(xMax, p.x);
                    yMax = Mathf.Max(yMax, p.y);
                }
                list.Add(new WorldMapObstacle(xMin, yMin, xMax, yMax));
            }
            foreach (MapLabPlacement o in data.Objects)
            {
                if (o.Kind != MapLabObjectKind.House && o.Kind != MapLabObjectKind.Barn)
                    continue;
                Vector2 ridge = new Vector2(Mathf.Cos(o.Yaw * Mathf.Deg2Rad), Mathf.Sin(o.Yaw * Mathf.Deg2Rad));
                Vector2 size = o.Size.sqrMagnitude > 0.01f ? o.Size : new Vector2(4f, 3f);
                // Чуть уже основания: путь вдоль стены не упирается в угол.
                Add(o.Position, ridge, size.x * 0.45f, size.y * 0.45f);
            }
            foreach (MapLabFeatureSprite f in data.FeatureSprites)
            {
                float half = Mathf.Clamp(f.Width * 0.28f, 0.25f, 3f);
                Add(f.Base, Vector2.right, half, half * 0.6f);
            }
            return list;
        }

        // Точка земли → проценты карты пути (без сдвига рисунка по высоте):
        // в этих координатах ходят герой и группы.
        public static Vector2 NavPercent(World w, float x, float z) =>
            new Vector2((x - w.Play.xMin) / w.Play.width * 100f, (w.Play.yMax - z) / w.Play.height * 100f);

        public static Vector2 ToPercent(World w, Vector3 p)
        {
            float x = (p.x - w.Play.xMin) / w.Play.width * 100f;
            float v = (p.y - w.Mean) * w.Cos + (p.z - w.Play.yMax) * w.Sin;
            float y = -v / (w.Play.height * w.Sin) * 100f;
            return new Vector2(x, y);
        }

        // Высота земли в точке (проценты холста) → сдвиг точки на рисунке, %.
        public static float ElevationShiftPercent(World w, float xPercent, float yPercent)
        {
            if (w?.View?.Data == null)
                return 0f;
            float x = w.Play.xMin + xPercent / 100f * w.Play.width;
            float z = w.Play.yMax - yPercent / 100f * w.Play.height;
            float h = w.View.Data.SampleHeight(x, z);
            return -(h - w.Mean) * w.Cos / (w.Play.height * w.Sin) * 100f;
        }

        // Нарисовать видимую часть: visible — видимый прямоугольник полотна в
        // процентах, pixelWidth/Height — размер области экрана в пикселях.
        public static RenderTexture Render(World w, Rect visible, int pixelWidth, int pixelHeight)
        {
            if (w == null || !w.Ready || pixelWidth < 8 || pixelHeight < 8 || visible.width <= 0f || visible.height <= 0f)
                return null;
            pixelWidth = Mathf.Min(pixelWidth, 4096);
            pixelHeight = Mathf.Min(pixelHeight, 4096);
            if (w.Target == null || w.Target.width != pixelWidth || w.Target.height != pixelHeight)
            {
                if (w.Target != null)
                {
                    w.Target.Release();
                    Object.DestroyImmediate(w.Target);
                }
                w.Target = new RenderTexture(pixelWidth, pixelHeight, 24, GraphicsFormat.R8G8B8A8_SRGB)
                {
                    name = "MapLab Live",
                    antiAliasing = SupportedMsaa(),
                    hideFlags = HideFlags.DontSave,
                };
                w.Target.Create();
            }
            float uLeft = visible.xMin / 100f * w.Play.width, uRight = visible.xMax / 100f * w.Play.width;
            float vTop = visible.yMin / 100f * w.Play.height * w.Sin, vBottom = visible.yMax / 100f * w.Play.height * w.Sin;
            float uc = (uLeft + uRight) * 0.5f, vc = (vTop + vBottom) * 0.5f;
            Vector3 target = new Vector3(w.Play.xMin + uc, w.Mean, w.Play.yMax - vc / w.Sin);
            Quaternion rotation = Quaternion.Euler(w.Pitch, 0f, 0f);
            float size = w.View.Data.Size;
            float distance = size * 2f + 200f;
            w.Camera.orthographicSize = Mathf.Max(0.5f, (vBottom - vTop) * 0.5f);
            w.Camera.aspect = pixelWidth / (float)pixelHeight;
            w.Camera.nearClipPlane = Mathf.Max(0.3f, distance - size * 1.6f - 60f);
            w.Camera.farClipPlane = distance + size * 1.6f + 60f;
            w.Camera.transform.SetPositionAndRotation(target - rotation * Vector3.forward * distance, rotation);
            w.Rig.Target = target;
            w.View.OnCameraChanged();
            w.View.PumpStreaming(false);
            // Свой слой видит только эта камера.
            foreach (Camera other in Camera.allCameras)
                if (other != w.Camera && (other.cullingMask & (1 << Layer)) != 0)
                    other.cullingMask &= ~(1 << Layer);
            w.Camera.targetTexture = w.Target;
            Shader.SetGlobalFloat("_MapLabPixelScale", 1f);
            w.Camera.Render();
            w.Camera.targetTexture = null;
            return w.Target;
        }

        private static int SupportedMsaa()
        {
            int samples = QualitySettings.antiAliasing > 1 ? QualitySettings.antiAliasing : 4;
            return Mathf.Clamp(samples, 1, 8);
        }

        // ---------- фигура героя ----------

        // Кадр героя для одного из шести ракурсов (набор фишки этапа 05):
        // текстура, высота в единицах мира, соотношение сторон и опора.
        public static bool HeroFrame(int direction, out Texture2D texture, out float worldHeight, out float aspect, out Vector2 pivot)
        {
            texture = null;
            worldHeight = 0f;
            aspect = 1f;
            pivot = new Vector2(0.5f, 0f);
            MapLabGameAssets assets = MapLabGameAssets.Load();
            MapLabMarkerSet set = assets != null ? assets.MarkerSet05 ?? assets.MarkerSet04 : null;
            if (set == null)
                return false;
            MapLabArtEntry entry = set.View((MapLabMarkerDirection)(((direction % 6) + 6) % 6));
            if (entry == null || !entry.HasTexture)
                return false;
            texture = entry.Texture;
            worldHeight = entry.CardHeight;
            aspect = entry.Aspect;
            pivot = entry.Pivot;
            return true;
        }
    }
}
