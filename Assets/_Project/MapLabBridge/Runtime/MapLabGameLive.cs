using System;
using System.Collections;
using System.Collections.Generic;
using KingdomSurvival.MapLab;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using BridgeData = KingdomSurvival.MapLab.MapLabBridge;
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
            public Color Paper = Color.white;
            public MapLabView View;
            public Camera Camera;
            public MapLabCameraRig Rig;
            public MapLabSettings Settings;
            public Rect Play;
            public float Mean, Pitch, Sin, Cos;
            // Базовый наклон (из ресурсов) и наибольший сдвиг по высоте, ед. мира.
            public float BasePitch, MaxOffset;
            // Опоры карточек в мире (для пересчёта процентов при смене наклона): здания
            // — параллельно Buildings, части мест — параллельно Features.
            public readonly List<CardSource> BuildingSources = new List<CardSource>();
            public readonly List<List<CardSource>> FeatureSources = new List<List<CardSource>>();
            // Наибольший сдвиг рисунка по высоте, % полотна (с запасом): окно
            // поиска точки земли под курсором.
            public float MaxShiftPercent = 12f;
            public RenderTexture Target;
            public string Error = string.Empty;
            public double BuildMs;
            // Пошаговая сборка: вид до готовности, шаги и часы сборки.
            public MapLabView PendingView;
            public IEnumerator Pending;
            public System.Diagnostics.Stopwatch Watch;
            public MapLabAssetsSnapshot Assets;
            // Лес из Базы ассетов (набор пород карты), освобождается с миром.
            public MapLabArtSet ForestSet;
            public int ForestDeciduous, ForestConifers;
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

        // Выход из игры или из Play Mode (Application.quitting срабатывает и при
        // выходе из Play Mode в редакторе): мир живой карты, арены и прогрев —
        // объекты HideAndDontSave, без очистки они остаются в редакторе и
        // попадают в кадр других камер (сцена «Генератора карты» рисовала
        // поверх своей карты лес оставшейся живой карты).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void HookShutdown()
        {
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
            RenderPipelineManager.beginContextRendering -= HideFromOtherCameras;
            RenderPipelineManager.beginContextRendering += HideFromOtherCameras;
        }

        // Мир живой карты остаётся в памяти и вне экрана карты (бой, место),
        // чтобы после боя карта не пересобиралась. Его слой видит только
        // камера карты: перед кадром любой другой игровой камеры слой с неё
        // снимается. Прежде слой снимался лишь в Render() карты, и Main Camera
        // каждой новой сцены боя («Everything») рисовала весь мир карты —
        // тысячи рендереров леса и домов — под интерфейсом боя каждый кадр.
        private static void HideFromOtherCameras(ScriptableRenderContext context, List<Camera> cameras)
        {
            World w = current;
            if (w == null || cameras == null)
                return;
            const int bit = 1 << Layer;
            for (int i = 0; i < cameras.Count; i++)
            {
                Camera camera = cameras[i];
                if (camera != null && camera != w.Camera && camera.cameraType == CameraType.Game && (camera.cullingMask & bit) != 0)
                    camera.cullingMask &= ~bit;
            }
        }

        public static void Shutdown()
        {
            try
            {
                Clear();
                MapLabArenaPrewarm.Cancel();
                MapLabArena.ShutdownAll();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

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
            WorldMapBridges.Clear();
            WorldMapWalls.Clear();
            MapLabLiveFigures.ClearAll();
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
            if (current.ForestSet != null)
                Object.DestroyImmediate(current.ForestSet);
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
                // Карточки растительности и мелочи — крупными кусками: при куске
                // 64 карта держала ~4000 рендереров и ~500 смен шейдера за кадр
                // (≈9 мс главного потока на отрисовку в редакторе), при 256 —
                // ~1000 и ~200 (≈5 мс). Лишние вершины за краем кадра видеокарте
                // почти ничего не стоят.
                w.Settings.Stage05.ArtChunkSize = Mathf.Max(w.Settings.Stage05.ArtChunkSize, LiveArtChunkSize);

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
                if (assets.MapForestFromAssets && view.ArtSet03 != null)
                {
                    w.ForestSet = MapLabArtAssets.BuildMapForestSet(view.ArtSet03, assets.MapAssetScale, out w.ForestDeciduous, out w.ForestConifers);
                    if (w.ForestSet != null)
                        view.ArtSet03 = w.ForestSet;
                }
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
                float mean = 0f, lowest = float.MaxValue, highest = float.MinValue;
                for (int i = 0; i < 16; i++)
                    for (int j = 0; j < 16; j++)
                    {
                        float sample = data.SampleHeight(w.Play.xMin + (i + 0.5f) / 16f * w.Play.width, w.Play.yMin + (j + 0.5f) / 16f * w.Play.height);
                        mean += sample;
                        lowest = Mathf.Min(lowest, sample);
                        highest = Mathf.Max(highest, sample);
                    }
                w.Mean = mean / 256f;
                // Запас на уступы и настилы мостов, которых редкая сетка не видит.
                w.BasePitch = w.Pitch;
                w.MaxOffset = Mathf.Max(highest - w.Mean, w.Mean - lowest) * 1.3f + 4f;
                w.MaxShiftPercent = Mathf.Max(2f, w.MaxOffset * w.Cos / (w.Play.height * w.Sin) * 100f);
                w.Paper = view.Palette.Paper;
                w.Camera.backgroundColor = w.Paper;
                foreach (MapLabServiceBuilding b in data.ServiceBuildings)
                {
                    CardSource source = new CardSource { Base = b.Base, Width = b.Width, Height = b.Height, Pivot = b.Pivot };
                    Rect rect = CardRect(w, source);
                    w.BuildingSources.Add(source);
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
                Dictionary<GeneratedMapSite, List<(CardSource Source, string ArtId)>> parts = new Dictionary<GeneratedMapSite, List<(CardSource, string)>>();
                foreach (MapLabFeatureSprite f in data.FeatureSprites)
                {
                    GeneratedMapSite site = NearestFeature(w, f.Kind, f.Point);
                    if (site == null)
                        continue;
                    if (!parts.TryGetValue(site, out List<(CardSource Source, string ArtId)> list))
                        parts[site] = list = new List<(CardSource, string)>();
                    list.Add((new CardSource { Base = f.Base, Width = f.Width, Height = f.Height, Pivot = f.Pivot }, f.ArtId));
                }
                foreach (KeyValuePair<GeneratedMapSite, List<(CardSource Source, string ArtId)>> pair in parts)
                {
                    List<(Rect Rect, string ArtId)> rects = new List<(Rect, string)>();
                    List<CardSource> sources = new List<CardSource>();
                    foreach ((CardSource source, string artId) in pair.Value)
                    {
                        rects.Add((CardRect(w, source), artId));
                        sources.Add(source);
                    }
                    w.Features.Add((pair.Key, UnionOf(rects), rects));
                    w.FeatureSources.Add(sources);
                }
                WorldMapObstacles.Set(Obstacles(w, data));
                WorldMapBridges.Set(BridgeSpans(w, data));
                WorldMapWalls.Set(LedgeWalls(data, w.Play), LedgeCells(w.Plan));
                mapObject.SetActive(true);
            }
            w.BuildMs = w.Watch.Elapsed.TotalMilliseconds;
            if (w.Ready)
                Debug.Log($"MapLab: живая карта партии построена за {w.BuildMs / 1000.0:0.0} с, зданий услуг {w.Buildings.Count}.");
        }

        // Карточка в мире: основание, размер (ед. мира) и опора рисунка.
        public struct CardSource
        {
            public Vector3 Base;
            public float Width, Height;
            public Vector2 Pivot;
        }

        // Прямоугольник карточки на полотне, % (для текущего наклона мира).
        public static Rect CardRect(World w, CardSource s)
        {
            Vector2 basePercent = ToPercent(w, s.Base);
            float widthPercent = s.Width / w.Play.width * 100f;
            float heightPercent = s.Height * 100f / (w.Play.height * w.Sin);
            return new Rect(basePercent.x - s.Pivot.x * widthPercent, basePercent.y - (1f - s.Pivot.y) * heightPercent, widthPercent, heightPercent);
        }

        private static Rect UnionOf(List<(Rect Rect, string ArtId)> rects)
        {
            Rect union = rects[0].Rect;
            foreach ((Rect r, string _) in rects)
                union = Rect.MinMaxRect(Mathf.Min(union.xMin, r.xMin), Mathf.Min(union.yMin, r.yMin), Mathf.Max(union.xMax, r.xMax), Mathf.Max(union.yMax, r.yMax));
            return union;
        }

        // Наклон камеры от приближения (запрос 23.11.2026, MAP_STYLE_GUIDE §7): от
        // базового на общем виде до closePitch на наибольшем приближении — плавно,
        // со ступенью в последних 60% диапазона приближения (в логарифме).
        public const float ClosePitch = 35f;

        public static float PitchForZoom(float zoom, float minZoom, float maxZoom, float basePitch, float closePitch = ClosePitch)
        {
            if (minZoom <= 0f || maxZoom <= minZoom * 1.01f)
                return basePitch;
            // Первые 40% диапазона (в логарифме) — базовый наклон, затем плавная ступень к closePitch.
            float u = Mathf.Clamp01(Mathf.Log(Mathf.Max(zoom, minZoom) / minZoom) / Mathf.Log(maxZoom / minZoom));
            float t = Mathf.Clamp01((u - RampStart) / (1f - RampStart));
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(basePitch, closePitch, t);
        }

        public const float RampStart = 0.4f;

        // Сменить наклон камеры мира: меняются только камера (Render берёт Pitch/Sin),
        // сдвиг по высоте и проценты карточек (по сохранённым опорам). Карта и кэш не
        // пересобираются. True — наклон изменился.
        public static bool SetPitch(World w, float pitch)
        {
            if (w == null || !w.Ready)
                return false;
            pitch = Mathf.Clamp(pitch, 20f, 80f);
            if (Mathf.Abs(pitch - w.Pitch) < 0.02f)
                return false;
            w.Pitch = pitch;
            w.Sin = Mathf.Sin(pitch * Mathf.Deg2Rad);
            w.Cos = Mathf.Cos(pitch * Mathf.Deg2Rad);
            w.MaxShiftPercent = Mathf.Max(2f, w.MaxOffset * w.Cos / (w.Play.height * w.Sin) * 100f);
            for (int i = 0; i < w.Buildings.Count && i < w.BuildingSources.Count; i++)
            {
                (string settlementId, string role, Rect _, Vector2 door) = w.Buildings[i];
                w.Buildings[i] = (settlementId, role, CardRect(w, w.BuildingSources[i]), door);
            }
            for (int i = 0; i < w.Features.Count && i < w.FeatureSources.Count; i++)
            {
                (GeneratedMapSite site, Rect _, List<(Rect Rect, string ArtId)> parts) = w.Features[i];
                for (int j = 0; j < parts.Count; j++)
                    parts[j] = (CardRect(w, w.FeatureSources[i][j]), parts[j].ArtId);
                w.Features[i] = (site, UnionOf(parts), parts);
            }
            return true;
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

        // Клетки уступов плана: у них стена рисунка точнее клетки (см. WorldMapWalls).
        public static List<int> LedgeCells(GeneratedWorldMap plan)
        {
            List<int> cells = new List<int>();
            if (plan?.Ledges != null)
                foreach (GeneratedMapLedge ledge in plan.Ledges)
                    if (ledge?.Cells != null)
                        cells.AddRange(ledge.Cells);
            return cells;
        }

        // Стены скальных обрывов рисунка → ядро: на каждый столб линии обрыва
        // (и на зазор между столбами) — четырёхугольник от подножия (низ стены
        // у основания перепада, 1,5 ширины перепада наружу) до края площадки
        // (перепад кончается на ширине перепада за линией) с запасом. Герой и
        // группы стену не пересекают, но подходят к ней вплотную.
        public const float WallMargin = 0.3f;

        // Полуширина стены: вниз — до подножия (1,5 ширины перепада), вверх — до конца
        // склона (ширина перепада); сетка высот на больших картах грубее перепада, и склон
        // шире ячейки сетки. Плюс запас WallMargin.
        public static void WallExtents(MapLabData data, out float low, out float high)
        {
            float sharpness = Mathf.Max(0.4f, data.CliffSharpness);
            low = Mathf.Max(sharpness * 1.5f, data.Cell) + WallMargin;
            high = Mathf.Max(sharpness, data.Cell) + WallMargin;
        }

        public static List<WorldMapWall> LedgeWalls(MapLabData data, Rect play)
        {
            List<WorldMapWall> walls = new List<WorldMapWall>();
            if (data == null)
                return walls;
            WallExtents(data, out float low, out float high);
            Vector2 Percent(Vector2 p) => new Vector2((p.x - play.xMin) / play.width * 100f, (play.yMax - p.y) / play.height * 100f);
            void Add(Vector2 start, Vector2 end, Vector2 outward)
            {
                Vector2 tangent = end - start;
                tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector2.right;
                // Концы чуть длиннее: соседние части перекрываются без щелей.
                start -= tangent * 0.25f;
                end += tangent * 0.25f;
                Vector2 a = Percent(start + outward * low), b = Percent(end + outward * low);
                Vector2 c = Percent(end - outward * high), d = Percent(start - outward * high);
                walls.Add(new WorldMapWall(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y));
            }
            foreach (MapLabCliffLine line in data.CliffLines)
            {
                List<MapLabCliffColumn> columns = line.Columns;
                for (int i = 0; i < columns.Count; i++)
                {
                    Add(columns[i].Start, columns[i].End, columns[i].Outward);
                    if (i + 1 < columns.Count && Vector2.Distance(columns[i].End, columns[i + 1].Start) > 0.05f)
                        Add(columns[i].End, columns[i + 1].Start, (columns[i].Outward + columns[i + 1].Outward).normalized);
                }
            }
            return walls;
        }

        // Настоящие мосты рисунка → ядро (проценты карты пути): путь через
        // реку идёт только по настилу.
        private static List<WorldMapBridgeSpan> BridgeSpans(World w, MapLabData data)
        {
            List<WorldMapBridgeSpan> list = new List<WorldMapBridgeSpan>(data.Bridges.Count);
            foreach (BridgeData b in data.Bridges)
            {
                Vector2 a = NavPercent(w, b.EndA.x, b.EndA.y);
                Vector2 e = NavPercent(w, b.EndB.x, b.EndB.y);
                list.Add(new WorldMapBridgeSpan(a.x, a.y, e.x, e.y, b.Width / w.Play.width * 100f));
            }
            return list;
        }

        // Высота опоры фигуры в точке мира: на настиле моста — настил (не ниже
        // земли), иначе земля. Переход у краёв плавный: фигура не прыгает.
        public static float GroundHeight(World w, float x, float z)
        {
            MapLabData data = w.View.Data;
            float ground = data.SampleHeight(x, z);
            List<BridgeData> bridges = data.Bridges;
            for (int i = 0; i < bridges.Count; i++)
            {
                BridgeData b = bridges[i];
                Vector2 ab = b.EndB - b.EndA;
                float length = ab.magnitude;
                if (length < 1e-3f || b.DeckHeight <= ground)
                    continue;
                Vector2 axis = ab / length;
                Vector2 rel = new Vector2(x, z) - b.EndA;
                float along = Vector2.Dot(rel, axis);
                float across = Mathf.Abs(rel.x * axis.y - rel.y * axis.x);
                float beyond = Mathf.Max(-along, along - length, 0f);
                float half = b.Width * 0.5f + 0.3f;
                float weight = (1f - Mathf.Clamp01((across - half) / 1f)) * (1f - Mathf.Clamp01(beyond / 1.5f));
                if (weight > 0f)
                    ground += (b.DeckHeight - ground) * weight;
            }
            return ground;
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

        // Сдвиг точки на рисунке по её высоте, % полотна: выше среднего — выше на
        // экране. Зависит от наклона камеры (sin, cos) и глубины игровой области.
        public static float ShiftPercent(float height, float mean, float sin, float cos, float playHeight) =>
            -(height - mean) * cos / (playHeight * sin) * 100f;

        // Высота земли в точке (проценты холста) → сдвиг точки на рисунке, %.
        public static float ElevationShiftPercent(World w, float xPercent, float yPercent)
        {
            if (w?.View?.Data == null)
                return 0f;
            float x = w.Play.xMin + xPercent / 100f * w.Play.width;
            float z = w.Play.yMax - yPercent / 100f * w.Play.height;
            float h = GroundHeight(w, x, z);
            return ShiftPercent(h, w.Mean, w.Sin, w.Cos, w.Play.height);
        }

        // Экранная точка (x, y экрана — проценты полотна) → точка земли в
        // процентах карты пути: y + сдвиг(x, y) = yЭкрана. Корней может быть
        // несколько (уступы) — берётся ближайший к камере, то есть с большим y.
        public static float GroundYFromScreenY(World w, float xPercent, float screenYPercent)
        {
            if (w == null || !w.Ready)
                return screenYPercent;
            return SolveGroundY(y => y + ElevationShiftPercent(w, xPercent, y), screenYPercent, w.MaxShiftPercent, 0.25f);
        }

        // Наибольший y из окна [цель − запас; цель + запас], где экранный y
        // точки земли (screenOf) равен целевому; сдвиг может быть разрывным.
        public static float SolveGroundY(Func<float, float> screenOf, float targetY, float maxShift, float step)
        {
            float hi = targetY + maxShift;
            if (screenOf(hi) - targetY <= 0f)
                return hi;
            int count = Mathf.Max(1, Mathf.CeilToInt(2f * maxShift / Mathf.Max(0.01f, step)));
            float previous = hi;
            for (int i = 1; i <= count; i++)
            {
                float y = hi - 2f * maxShift * i / count;
                if (screenOf(y) - targetY <= 0f)
                {
                    float above = previous, below = y;
                    for (int k = 0; k < 16; k++)
                    {
                        float mid = (above + below) * 0.5f;
                        if (screenOf(mid) - targetY > 0f)
                            above = mid;
                        else
                            below = mid;
                    }
                    return (above + below) * 0.5f;
                }
                previous = y;
            }
            return targetY - (screenOf(targetY) - targetY);
        }

        // Размер куска карточек растительности и мелочи на живой карте (см. Begin).
        private const float LiveArtChunkSize = 256f;

        // Предел высоты кадра живой карты в пикселях (0 — по экрану), как у арены
        // (MapLabArenaDriver.MaxRenderHeight). 24.11.2026: на экране 4K карта
        // рисовалась в 3784×1920 и упиралась в видеокарту (GTX 1070: 14–21 мс
        // кадра); интерфейс растягивает кадр на рамку просмотра.
        public static int MaxRenderHeight = 1440;

        // Нарисовать видимую часть: visible — видимый прямоугольник полотна в
        // процентах, pixelWidth/Height — размер области экрана в пикселях.
        public static RenderTexture Render(World w, Rect visible, int pixelWidth, int pixelHeight)
        {
            if (w == null || !w.Ready || pixelWidth < 8 || pixelHeight < 8 || visible.width <= 0f || visible.height <= 0f)
                return null;
            // Кадр меньше экрана: толщина туши и порог мелочи — в пикселях
            // экрана (доля кадра к экрану), чтобы на экране линии не толстели.
            float pixelScale = 1f;
            if (MaxRenderHeight > 0 && pixelHeight > MaxRenderHeight)
            {
                pixelScale = MaxRenderHeight / (float)pixelHeight;
                pixelWidth = Mathf.Max(8, Mathf.RoundToInt(pixelWidth * pixelScale));
                pixelHeight = MaxRenderHeight;
            }
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
                    filterMode = FilterMode.Bilinear,
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
            w.Rig.PitchOverride = w.Pitch;
            w.Rig.Target = target;
            // Подкачку покраски OnCameraChanged уже прокачивает (второй вызов
            // в кадре только выделял память).
            w.View.OnCameraChanged();
            // Свой слой видит только эта камера.
            int cameraCount = MapLabArena.AllCameras(out Camera[] cameras);
            for (int i = 0; i < cameraCount; i++)
                if (cameras[i] != w.Camera && (cameras[i].cullingMask & (1 << Layer)) != 0)
                    cameras[i].cullingMask &= ~(1 << Layer);
            w.Camera.targetTexture = w.Target;
            w.Camera.backgroundColor = w.Paper;
            Shader.SetGlobalFloat("_MapLabPixelScale", pixelScale);
            w.Camera.Render();
            w.Camera.targetTexture = null;
            Shader.SetGlobalFloat("_MapLabPixelScale", 1f);
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
