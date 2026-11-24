using System;
using System.Collections;
using System.Collections.Generic;
using KingdomSurvival.MapLab;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace KingdomSurvival.MapLabBridge
{
    public enum MapLabArenaCellKind
    {
        Open,
        Hill,
        Blocked,
    }

    // Принятая тактическая раскладка обычной арены в координатах кадра поля:
    // высота кадра — 1, ширина — Aspect, Y вниз (как BattlefieldFrame).
    // Рисунок строится по этим данным, а не по своему случайному рельефу.
    public sealed class MapLabArenaSpec
    {
        public int Seed;
        public float Aspect = 16f / 9f;
        // Сжатие гекса по высоте на экране (BattlefieldFrame.VerticalScale):
        // камера арены смотрит под углом asin(VerticalScale), и плоские гексы
        // доски ложатся на землю без искажения.
        public float VerticalScale = 0.75f;
        // Радиус гекса в долях высоты кадра.
        public float HexNominal = 0.08f;
        // Единиц мира MapLab на радиус гекса (масштаб деревьев и мазков).
        public float HexWorld = 3f;
        // Подъём вершины холма на экране, в радиусах гекса.
        // 20.11.2026: 0,3 → 0,5 — холм читается рельефом без подложки.
        public float HillLiftHexes = 0.5f;
        public readonly List<(Vector2 Center, MapLabArenaCellKind Kind)> Cells = new List<(Vector2, MapLabArenaCellKind)>();
        // Стартовые клетки бойцов (центры, доли кадра): трава и мелочь их не
        // закрывают.
        public readonly List<Vector2> KeepClear = new List<Vector2>();
        // Объекты местности боя (центр клетки, вид): рисунок из слота Базы
        // ассетов «arena-object:<вид>», если он есть.
        public readonly List<(Vector2 Center, string Kind)> Objects = new List<(Vector2, string)>();
        // Поверхности клеток (центр, слои MapLabArenaSurfaces.Bit) — в краске земли.
        public readonly List<(Vector2 Center, ulong Layers)> Surfaces = new List<(Vector2, ulong)>();
        // Лесные клетки (центры): на клетке — молодые деревья и кусты Базы
        // ассетов (MapLabArena.Decor, «лесные клетки»). Лес в бою не выгорает.
        public readonly List<Vector2> Forest = new List<Vector2>();
        // Плотность декора (трава, мелочь, число групп деревьев): 1 — обычная.
        // Свой поток случайности: рельеф, клетки и бонусы от неё не зависят.
        public float DecorDensity = 1f;
        // Единиц мира MapLab на единицу мира мест (размер рисунков Базы
        // ассетов на арене). 0 — из MapLabGameAssets.
        public float AssetScale;
        // Рисунок арены из «Генератора арен» (null — текущие настройки ассета).
        public MapLabArenaLook Look;
        // Обработка кадра (null — без неё). Меняется и после сборки.
        public KingdomSurvival.BattlefieldDatabase.LocationPostEffects Frame;
    }

    // Боевая арена в стиле карты: маленький мир MapLab по раскладке боя.
    // Высота задана явно (PlanHeight композиции): холм — ровно на клетках,
    // где правила боя дают холм (Difficult), соседние холмы сливаются в гряду.
    // Деревья — массами по краям кадра, не заслоняя клеток; на непроходимых
    // клетках — камни. Камера ортографическая под наклоном asin(VerticalScale)
    // снимает ровно кадр поля 16:9 в RenderTexture. Тактическая модель от
    // рисунка не зависит: рисунок только читает её.
    public sealed partial class MapLabArena : IDisposable
    {
        // Свой слой (карта партии — 31, поле ассетов окна — 30).
        public const int Layer = 29;
        // Мир арены — в начале координат, как карта партии (шейдеры MapLab
        // берут координаты покраски из мировых); их разделяет слой камеры.
        private static readonly Vector3 WorldOffset = Vector3.zero;
        private const float Margin = 10f;
        private const float TallestTree = 6.5f;
        private const string BlockKind = "arena-block";
        private static readonly string[] BlockArt = { "Map05_Rock_Outcrop_D", "Map05_Rock_Angular_B", "Forked_Boulder_A", "Map05_Rock_Round_A" };

        public MapLabArenaSpec Spec { get; }
        public string Error { get; private set; } = string.Empty;
        public double BuildMs { get; private set; }
        public bool Ready => view != null && view.Data != null && pending == null && string.IsNullOrEmpty(Error);
        public bool Building => pending != null;
        public float Progress => Ready ? 1f : view != null ? view.BuildProgress : 0f;
        public MapLabView View => view;
        // Камера кадра арены (проход «Холста» обработки кадра привязывается к ней).
        public Camera FrameCamera => camera;
        // Всплеск линзы сейчас (удар в бою) и тряска в долях высоты кадра.
        public System.Func<float> LensPulse;
        public Vector2 ShakeShare;

        // Проход по траве (бой): точки ног бойца и «следы» за ним — кадр поля
        // (доли высоты кадра, Y вниз) и сила 0..1. Низкие карточки (трава, мелкие
        // растения) в шейдере отклоняются от этих точек только на время кадра.
        private const int MaxWalkers = 6;
        private readonly Vector4[] walkers = new Vector4[MaxWalkers];
        private int walkerCount;

        public void SetWalkers(IReadOnlyList<(Vector2 Nominal, float Strength)> points)
        {
            walkerCount = 0;
            if (points == null) return;
            float radius = Spec.HexWorld * 1.3f;
            for (int i = 0; i < points.Count && walkerCount < MaxWalkers; i++)
            {
                if (points[i].Strength <= 0.01f) continue;
                Vector2 world = ToWorld(points[i].Nominal);
                walkers[walkerCount++] = new Vector4(WorldOffset.x + world.x, WorldOffset.z + world.y, radius, Mathf.Clamp01(points[i].Strength));
            }
        }

        private void ApplyWalkers()
        {
            Shader.SetGlobalVectorArray("_MapLabWalkers", walkers);
            Shader.SetGlobalFloat("_MapLabWalkerCount", walkerCount);
            // Выше этого карточка не гнётся: деревья и постройки стоят.
            Shader.SetGlobalFloat("_MapLabBendMaxHeight", walkerCount > 0 ? Spec.HexWorld * 0.7f : 0f);
        }

        private static void ClearWalkers() => Shader.SetGlobalFloat("_MapLabWalkerCount", 0f);

        // Ветер обработки кадра (раздел «Рисунки» → «Ветер»): качает деревья и
        // растения карточек (шейдер «Art Card», _WindKind материала). Размах
        // задан в единицах мира мест — здесь переводится масштабом рисунков арены;
        // граница высоты — доля роста рисунка. Ставится на время кадра, после —
        // нули (карта партии рисуется тем же шейдером и не качается).
        private static readonly int WindId = Shader.PropertyToID("_MapLabWind");
        private static readonly int WindShareId = Shader.PropertyToID("_MapLabWindShare");
        private static readonly int WindFloorId = Shader.PropertyToID("_MapLabWindFloor");
        private float windScale = 1f;
        private float windSeconds;

        // Ветер в кадре сейчас есть (кадр нужно перерисовывать со временем).
        public bool Windy
        {
            get
            {
                KingdomSurvival.BattlefieldDatabase.LocationSpriteEffects s = Spec.Frame != null && Spec.Frame.Enabled ? Spec.Frame.Sprites : null;
                return s != null && s.Wind && s.WindStrength > 0f && (s.WindTrees > 0f || s.WindPlants > 0f);
            }
        }

        private void ApplyWind(bool newFrame)
        {
            if (newFrame) windSeconds = Time.realtimeSinceStartup;
            if (!Windy)
            {
                ClearWind();
                return;
            }
            KingdomSurvival.BattlefieldDatabase.LocationSpriteEffects s = Spec.Frame.Sprites;
            Shader.SetGlobalVector(WindId, new Vector4(Mathf.Max(0f, s.WindStrength) * windScale, Mathf.Max(0f, s.WindSpeed),
                Mathf.Clamp01(s.WindGusts), 0f));
            // Граница высоты — доля роста самого рисунка: у деревьев и у кустов/травы своя.
            Shader.SetGlobalVector(WindFloorId, new Vector4(Mathf.Clamp(s.WindTreeFloor, 0f, 0.95f), Mathf.Clamp(s.WindPlantFloor, 0f, 0.95f), 0f, 0f));
            Shader.SetGlobalVector(WindShareId, new Vector4(Mathf.Max(0f, s.WindTrees), Mathf.Max(0f, s.WindPlants),
                Mathf.Clamp(s.WindDirection, -1f, 1f), windSeconds));
        }

        private static void ClearWind()
        {
            Shader.SetGlobalVector(WindId, Vector4.zero);
            Shader.SetGlobalVector(WindShareId, Vector4.zero);
            Shader.SetGlobalVector(WindFloorId, Vector4.zero);
        }

        // Силуэтные тени карточек (проход «SilhouetteShadow» шейдера карточек): только в нижнем кадре,
        // в слое объектов и слоях «впереди бойца» их нет.
        private static readonly int ShadowId = Shader.PropertyToID("_MapLabShadow");
        private static readonly int ShadowColorId = Shader.PropertyToID("_MapLabShadowColor");

        public static readonly Color SilhouetteShadowColor = new Color(0.07f, 0.09f, 0.03f, 1f);
        public const float SilhouetteShadowOpacity = 0.42f;
        // Длина тени в долях высоты: свет слева, тень падает вправо и назад (см. ShadowGround).
        public const float SilhouetteShadowLength = 0.75f;

        private void SetShadows(bool on)
        {
            if (!on)
            {
                Shader.SetGlobalVector(ShadowId, Vector4.zero);
                return;
            }
            Vector2 ground = ShadowGround();
            Shader.SetGlobalVector(ShadowId, new Vector4(ground.x, ground.y, SilhouetteShadowOpacity, 1f));
            Shader.SetGlobalColor(ShadowColorId, SilhouetteShadowColor);
        }

        // Тень по земле (x, z мира) на единицу высоты рисунка — общая для карточек и бойцов.
        // Силуэт ложится сдвигом (основание вдоль кадра, верх — по этому вектору): при свете
        // сбоку вектор почти вдоль кадра и тень сжалась бы в нитку, поэтому он отводится
        // назад (от зрителя) не меньше чем на 25°.
        public Vector2 ShadowGround()
        {
            Vector3 light = view != null && view.Settings != null ? view.Settings.LightDirection : new Vector3(-0.62f, 0.72f, 0.06f);
            float rise = Mathf.Max(0.15f, light.y);
            Vector2 g = new Vector2(-light.x, -light.z) / rise * SilhouetteShadowLength;
            float length = g.magnitude;
            if (length < 1e-4f)
                return new Vector2(0f, SilhouetteShadowLength * 0.5f);
            const float minSin = 0.423f; // sin 25°
            if (Mathf.Abs(g.y) < length * minSin)
                g = new Vector2(Mathf.Sign(g.x) * length * 0.906f, length * minSin);
            return g;
        }

        // Оси тени на экране в долях рисунка бойца (Y вниз): правая сторона рисунка —
        // вдоль кадра (Across = (1, 0)), верх — вдоль Along (вектор ShadowGround с
        // глубиной, сжатой наклоном камеры). Тот же сдвиг, что в проходе
        // «SilhouetteShadow» шейдера карточек.
        public void ShadowScreenAxes(out Vector2 along, out Vector2 across)
        {
            Vector2 g = ShadowGround();
            float s = sin > 0.05f ? sin : 0.77f;
            along = new Vector2(g.x, -g.y * s);
            across = Vector2.right;
        }

        // Направление тени на экране (единичный вектор, Y вниз) — для теней бойцов доски.
        public Vector2 ShadowScreenDirection()
        {
            Vector3 light = view != null && view.Settings != null ? view.Settings.LightDirection : new Vector3(-0.62f, 0.72f, 0.06f);
            Vector2 d = new Vector2(-light.x, light.z);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.right;
        }

        public float Mean => mean;

        private MapLabView view;
        private MapLabSettings settings;
        private Camera camera;
        private MapLabCameraRig rig;
        private RenderTexture target;
        private IEnumerator pending;
        private System.Diagnostics.Stopwatch watch;
        private float k, depth, zBottom, mean, sin, cos, pitch;
        private Color paper = Color.white;
        private bool disposed;
        private double artMs, planMs;
        private static readonly List<MapLabArena> liveArenas = new List<MapLabArena>();
        // Рисунки Базы ассетов для арены (по назначению) и их роли в декоре.
        private MapLabArtSet decorSet;
        private readonly List<MapLabDecorArt> decorArt = new List<MapLabDecorArt>();
        public int DecorFeatures { get; private set; }
        public bool UsesAssetDatabase { get; private set; }
        private bool UsesAssetDatabaseHint => decorArt.FindAll(art => art.Role == MapLabDecorRole.Tree).Count >= 3;

        private MapLabArena(MapLabArenaSpec spec)
        {
            Spec = spec;
        }

        public static bool Available
        {
            get
            {
                MapLabGameAssets assets = MapLabGameAssets.Load();
                return assets != null && assets.Profile != null && assets.ArenaInBattle;
            }
        }

        // Начать пошаговую сборку (тяжёлые расчёты — в фоне, см. MapLabView.GenerateStepwise).
        public static MapLabArena Begin(MapLabArenaSpec spec)
        {
            // Настоящая арена: незаконченный прогрев не отнимает у неё время.
            MapLabArenaPrewarm.Cancel();
            return Create(spec);
        }

        // Сборка для прогрева (MapLabArenaPrewarm), без отмены прогрева.
        internal static MapLabArena BeginWarmup(MapLabArenaSpec spec) => Create(spec);

        private static MapLabArena Create(MapLabArenaSpec spec)
        {
            PurgeReleased();
            MapLabArena arena = new MapLabArena(spec ?? throw new ArgumentNullException(nameof(spec)));
            liveArenas.Add(arena);
            arena.Start();
            return arena;
        }

        // Собрать сразу (проверки, пакетный режим).
        public static MapLabArena Build(MapLabArenaSpec spec)
        {
            MapLabArena arena = Begin(spec);
            while (!arena.Advance())
                System.Threading.Thread.Sleep(1);
            return arena;
        }

        private void Start()
        {
            watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                MapLabGameAssets assets = MapLabGameAssets.Load();
                if (assets == null || assets.Profile == null)
                    throw new InvalidOperationException("нет MapLabGameAssets (Генератор карты → Сервис → Подготовить рисунок карты для игры)");
                pitch = Mathf.Asin(Mathf.Clamp(Spec.VerticalScale, 0.2f, 0.98f)) * Mathf.Rad2Deg;
                sin = Mathf.Sin(pitch * Mathf.Deg2Rad);
                cos = Mathf.Cos(pitch * Mathf.Deg2Rad);
                k = Spec.HexWorld / Mathf.Max(1e-4f, Spec.HexNominal);
                depth = k / sin;

                settings = Object.Instantiate(assets.Profile);
                settings.name = "MapLab Arena Profile";
                settings.hideFlags = HideFlags.HideAndDontSave;
                float scale = Spec.AssetScale > 0f ? Spec.AssetScale : assets.ArenaAssetScale;
                windScale = Mathf.Max(0.01f, scale);
                decorSet = MapLabArtAssets.BuildSet(true, scale, decorArt, RoleScale);
                artMs = watch.Elapsed.TotalMilliseconds;
                ConfigureSettings(settings);
                float side = settings.General.MapSize;
                zBottom = (side - depth) * 0.5f;
                settings.RegionPlan = BuildComposition(side);
                planMs = watch.Elapsed.TotalMilliseconds - artMs;

                GameObject cameraObject = new GameObject("MapLab Arena Camera") { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.allowMSAA = true;
                camera.allowHDR = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.cullingMask = (1 << Layer) | (1 << OverlayLayer);
                rig = cameraObject.AddComponent<MapLabCameraRig>();
                rig.enabled = false;

                GameObject mapObject = new GameObject("MapLab Arena") { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
                mapObject.transform.position = WorldOffset;
                mapObject.SetActive(false);
                view = mapObject.AddComponent<MapLabView>();
                view.GenerateOnLoad = false;
                MapLabMarker marker = mapObject.AddComponent<MapLabMarker>();
                marker.AutoPlace = false;
                marker.enabled = false;
                assets.ApplyTo(view);
                view.CameraRig = rig;
                view.Settings = settings;
                view.FeatureExtraSets = new List<MapLabArtSet>();
                if (decorSet != null) view.FeatureExtraSets.Add(decorSet);
                if (assets.ArtSet05 != null) view.FeatureExtraSets.Add(assets.ArtSet05);
                if (assets.ArtSetPoi != null) view.FeatureExtraSets.Add(assets.ArtSetPoi);
                PrepareSurfaces();
                pending = view.GenerateStepwise();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        // Продвинуть сборку на шаг; true — закончена (готово или ошибка).
        public bool Advance()
        {
            if (pending == null)
                return true;
            try
            {
                if (pending.MoveNext())
                    return false;
                pending = null;
                Finish();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
            return true;
        }

        private void Fail(Exception exception)
        {
            pending = null;
            Error = "Арена MapLab: " + exception.Message;
            Debug.LogException(exception);
            if (watch != null)
                BuildMs = watch.Elapsed.TotalMilliseconds;
        }

        private void Finish()
        {
            if (view == null || view.Data == null)
                throw new InvalidOperationException("генерация не выполнена — " + view?.LastError);
            Transform markerRoot = view.transform.Find(MapLabMarker.RootName);
            if (markerRoot != null)
                markerRoot.gameObject.SetActive(false);
            SetLayer(view.transform);
            SplitOverlay();
            // Опорная высота — ровная земля открытых клеток: на них подъём 0,
            // и сетка там лежит как прежде.
            float sum = 0f;
            int count = 0;
            foreach ((Vector2 center, MapLabArenaCellKind kind) in Spec.Cells)
            {
                if (kind != MapLabArenaCellKind.Open || riverCells.Contains(CellKey(center)))
                    continue;
                Vector2 w = ToWorld(center);
                sum += view.Data.SampleHeight(w.x, w.y);
                count++;
            }
            mean = count > 0 ? sum / count : 0f;
            // До активации мира: чужие камеры (сцена боя, URP 2D) слой арены не
            // рисуют — иначе террейн и шейдеры MapLab попадали в кадр сцены.
            StripOtherCameras();
            paper = view.Palette.Paper;
            camera.backgroundColor = paper;
            view.gameObject.SetActive(true);
            BuildMs = watch.Elapsed.TotalMilliseconds;
            Debug.Log($"MapLab: арена построена за {BuildMs / 1000.0:0.0} с (рисунки Базы ассетов {artMs:0} мс, план {planMs:0} мс, генерация и сборка {BuildMs - artMs - planMs:0} мс).");
        }

        private MapLabArenaLook Look => Spec.Look ??= ArenaGeneratorAsset.CurrentLook.Clone();

        private float RoleScale(MapLabDecorRole role) =>
            role == MapLabDecorRole.Tree ? Look.TreeScale
            : role == MapLabDecorRole.LowGrass || role == MapLabDecorRole.TallGrass ? Look.GrassScale
            : role == MapLabDecorRole.TallPlant ? Look.WeedScale : 1f;

        private void ConfigureSettings(MapLabSettings s)
        {
            // Плотности мазков MapLab профиля (до отключения на арене).
            float assetsReed = Mathf.Max(0.2f, s.Stage05.ReedDensity);
            float width = Spec.Aspect * k;
            float side = width + 2f * Margin;
            s.General.MapSize = side;
            s.General.Seed = Spec.Seed;
            s.General.Quality = MapLabQuality.Samples257;
            s.Stage05.WideFrame = false;
            // Тени арены: край размыт (трава и кусты рядом с бойцами не «лужи»).
            s.Stage03.ShadowSoftEdge = Spec.HexWorld * 0.16f;
            s.Stage03.SilhouetteShadows = true;
            // Арена мала: меш на рисунок целиком, без кусков подгрузки — втрое меньше
            // рендереров и вызовов отрисовки в каждом проходе кадра.
            s.Stage05.ArtChunkSize = 4096f;
            s.Stage05.FrameWidth = 0f;
            s.Stage05.PaintStreaming = MapLabPaintStreaming.Off;
            // Крупный план: покраска плотнее, чем на карте мира.
            s.Stage03.PaintTexelsPerUnit = Mathf.Clamp(Look.PaintTexelsPerUnit, 8f, 48f);
            // Без alpha-to-coverage: на D3D12 (GTX 1070) рендер арены с ним ронял
            // редактор и игру при выходе — и с MSAA, и без. Края — по порогу альфы.
            s.Stage05.AlphaToCoverage = false;
            s.Relief.Source = MapLabHeightSource.Procedural;
            s.Relief.HeightRange = Mathf.Max(0.2f, Spec.HillLiftHexes * Spec.HexWorld / Mathf.Max(0.2f, cos));
            s.Relief.FineNoise = 0.05f;
            s.Relief.Smoothing = 1;
            s.RoadsAndFields.FieldDensity = 0f;
            s.Settlement.HousesMin = 0;
            s.Settlement.HousesMax = 0;
            s.Vegetation.ForestDensity = Mathf.Max(0.5f, s.Vegetation.ForestDensity);
            s.Vegetation.SingleTrees = 0f;
            // Трава, камыш, камни и сено MapLab на арене не нужны: декор арены —
            // рисунки Базы ассетов. Меньше работы GPU на кадр.
            if (UsesAssetDatabaseHint)
            {
                s.Stage05.GrassDensity = 0f;
                s.Stage05.InkGrassDensity = 0f;
                s.Stage05.ReedDensity = 0f;
                s.Stage05.RockDensity = 0f;
                s.Stage05.HayDensity = 0f;
            }
            // Камыш MapLab у воды реки арены (настройка «Генератора арен»).
            if (Look.ReedDensity > 0f)
                s.Stage05.ReedDensity = Mathf.Max(0f, assetsReed * Look.ReedDensity);
        }

        // Кадр поля (доли высоты кадра, Y вниз) → мир MapLab (x, z) на земле.
        public Vector2 ToWorld(Vector2 nominal) =>
            new Vector2(Margin + nominal.x * k, zBottom + (1f - nominal.y) * depth);

        private MapLabComposition BuildComposition(float side)
        {
            MapLabComposition c = new MapLabComposition
            {
                FromPlan = true,
                // Без случайных одиночных деревьев, кустов и валунов посреди поля.
                AssetField = true,
                NoSlopeRock = true,
                PlayRect = new Rect(Margin, zBottom, Spec.Aspect * k, depth),
                MapEdgeForest = 0f,
                LakeLevelFraction = 0f,
            };
            float step = Spec.HexWorld * 0.2f;
            int cols = Mathf.Max(2, Mathf.CeilToInt(side / step) + 1);
            c.PlanCols = cols;
            c.PlanRows = cols;
            c.PlanRect = new Rect(0f, 0f, side, side);
            c.PlanHeight = new float[cols * cols];
            c.PlanForest = new float[cols * cols];
            c.PlanField = new float[cols * cols];
            PlanRiver(c);

            List<Vector2> hills = new List<Vector2>();
            List<Vector2> cells = new List<Vector2>();
            foreach ((Vector2 center, MapLabArenaCellKind kind) in Spec.Cells)
            {
                Vector2 w = ToWorld(center);
                cells.Add(w);
                if (kind == MapLabArenaCellKind.Hill)
                    hills.Add(w);
            }
            float h = Spec.HexWorld;
            // Деревья не выше TallestTree: дерево в точке p заслоняет на экране
            // клетки позади себя на глубину T·cos/sin — там деревьям не место.
            float shadowDepth = TallestTree * cos / sin;
            Vector2 frameCenter = new Vector2(Margin + Spec.Aspect * k * 0.5f, zBottom + depth * 0.5f);
            // Рисунки Базы ассетов для арены есть — деревья, трава и камни
            // ставятся явно (MapLabArena.Decor.cs); иначе прежний лес MapLab.
            UsesAssetDatabase = decorArt.FindAll(art => art.Role == MapLabDecorRole.Tree).Count >= 3;
            for (int z = 0; z < cols; z++)
            {
                for (int x = 0; x < cols; x++)
                {
                    Vector2 p = new Vector2(x / (float)(cols - 1) * side, z / (float)(cols - 1) * side);
                    int i = z * cols + x;
                    c.PlanHeight[i] = HillHeight(hills, p, h);
                    c.PlanForest[i] = UsesAssetDatabase ? 0f : Forest(cells, p, h, shadowDepth, frameCenter);
                }
            }

            if (UsesAssetDatabase)
            {
                PlaceDecor(c, cells, frameCenter);
                return c;
            }

            // Непроходимые клетки — камнем (вид по клетке, устойчив к seed).
            int index = 0;
            foreach ((Vector2 center, MapLabArenaCellKind kind) in Spec.Cells)
            {
                index++;
                if (kind != MapLabArenaCellKind.Blocked)
                    continue;
                int pick = (int)(MapLabRandom.StreamSeed(Spec.Seed, "arena-block-" + index, MapLabData.AlgorithmVersion) % (ulong)BlockArt.Length);
                c.Features.Add((ToWorld(center), BlockArt[pick], BlockKind));
            }
            return c;
        }

        // Объединение мягких куполов клеток холма: плато над клеткой, склон
        // уходит в соседние клетки, но их центр остаётся у подножия.
        public static float HillHeight(List<Vector2> hills, Vector2 p, float hex)
        {
            float rest = 1f;
            foreach (Vector2 c in hills)
            {
                float d = Vector2.Distance(p, c) / hex;
                if (d >= 1.65f)
                    continue;
                float bump = 1f - Smooth(0.35f, 1.65f, d);
                rest *= 1f - bump;
            }
            return 1f - rest;
        }

        private float Forest(List<Vector2> cells, Vector2 p, float hex, float shadowDepth, Vector2 frameCenter)
        {
            float nearest = float.MaxValue;
            bool coversCell = false;
            foreach (Vector2 c in cells)
            {
                float d = Vector2.Distance(p, c);
                nearest = Mathf.Min(nearest, d);
                // Клетка позади точки на экране под деревом.
                if (Mathf.Abs(c.x - p.x) < hex * 1.7f && c.y > p.y && c.y - p.y < shadowDepth + hex)
                    coversCell = true;
            }
            if (coversCell)
                return 0f;
            float away = Smooth(hex * 1.6f, hex * 3.2f, nearest);
            // Углы кадра гуще краёв: игровая середина остаётся открытой.
            Vector2 offset = p - frameCenter;
            float corner = Mathf.Clamp01(Mathf.Abs(offset.x) / (Spec.Aspect * k * 0.5f)) * Mathf.Clamp01(Mathf.Abs(offset.y) / (depth * 0.5f));
            return away * Mathf.Lerp(0.75f, 1f, corner);
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // Подъём земли в точке кадра (доли кадра 0..1, Y вниз) — в долях высоты
        // кадра, вверх положительный. Та же высота, что у нарисованной земли.
        public float LiftShare(Vector2 frameShare)
        {
            if (!Ready)
                return 0f;
            Vector2 w = ToWorld(new Vector2(frameShare.x * Spec.Aspect, frameShare.y));
            float height = view.Data.SampleHeight(w.x, w.y);
            return (height - mean) * cos / k;
        }

        // Нарисовать кадр поля для области экрана pixelWidth × pixelHeight.
        // Кадр поля всегда 16:9, UI растягивает текстуру на кадр: текстура
        // заводится под высоту с запасом (кратно 128) и пересоздаётся, только
        // если область заметно выросла. Уменьшение окна её не трогает —
        // на D3D12 частое пересоздание текстуры кадра роняло устройство.
        public RenderTexture Render(int pixelWidth, int pixelHeight)
        {
            PurgeReleased();
            if (!Ready || pixelWidth < 8 || pixelHeight < 8)
                return null;
            int wantHeight = Mathf.Clamp(pixelHeight, 64, 2304);
            if (target == null || target.height < wantHeight * 0.85f)
            {
                pixelHeight = Mathf.Min(2304, (wantHeight + 127) / 128 * 128);
                pixelWidth = Mathf.RoundToInt(pixelHeight * Spec.Aspect);
            }
            else
            {
                pixelWidth = target.width;
                pixelHeight = target.height;
            }
            if (target == null || target.width != pixelWidth || target.height != pixelHeight)
            {
                ReleaseTarget();
                // Показываемая текстура — без MSAA: её читает UI Toolkit (MSAA-
                // текстура, которую читает панель, на D3D12 вешала устройство).
                target = new RenderTexture(pixelWidth, pixelHeight, 24, GraphicsFormat.R8G8B8A8_SRGB)
                {
                    name = "MapLab Arena",
                    antiAliasing = 1,
                    hideFlags = HideFlags.DontSave,
                };
                target.Create();
            }
            Vector2 centerWorld = ToWorld(new Vector2(Spec.Aspect * 0.5f, 0.5f));
            Vector3 focus = WorldOffset + new Vector3(centerWorld.x, mean, centerWorld.y);
            Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
            float size = view.Data.Size;
            float distance = size * 2f + 200f;
            camera.orthographicSize = k * 0.5f;
            camera.aspect = pixelWidth / (float)pixelHeight;
            camera.nearClipPlane = Mathf.Max(0.3f, distance - size * 1.6f - 60f);
            camera.farClipPlane = distance + size * 1.6f + 60f;
            Vector3 shake = rotation * new Vector3(ShakeShare.x, ShakeShare.y, 0f) * k;
            camera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance + shake, rotation);
            rig.Target = focus;
            view.OnCameraChanged();
            view.PumpStreaming(false);
            StripOtherCameras();
            Shader.SetGlobalFloat("_MapLabPixelScale", 1f);
            camera.backgroundColor = paper;
            bool graded = BeginFrame();
            ApplyWalkers();
            ApplyWind(true);
            UpdateParticles(focus, rotation, k * 0.5f * camera.aspect, k * 0.5f);
            SetShadows(true);
            try
            {
                if (EffectiveFrameMsaa > 1)
                {
                    // MSAA-кадр камеры — временный (пул Unity), в показываемую
                    // текстуру он сводится; UI читает только текстуру без MSAA.
                    RenderTextureDescriptor descriptor = new RenderTextureDescriptor(target.width, target.height, GraphicsFormat.R8G8B8A8_SRGB, 24) { msaaSamples = EffectiveFrameMsaa };
                    RenderTexture frame = RenderTexture.GetTemporary(descriptor);
                    camera.targetTexture = frame;
                    camera.Render();
                    camera.targetTexture = null;
                    Graphics.Blit(frame, target);
                    RenderTexture.ReleaseTemporary(frame);
                }
                else
                {
                    camera.targetTexture = target;
                    camera.Render();
                    camera.targetTexture = null;
                }
                if (!SkipOverlayForBench)
                    RenderOverlay(graded);
            }
            finally
            {
                ClearWalkers();
                ClearWind();
                SetShadows(false);
                framePost?.End(camera);
            }
            return target;
        }

        // Замер смоук-теста: только нижний кадр, без слоя объектов.
        private bool SkipOverlayForBench;

        public RenderTexture RenderGroundOnlyForBench(int pixelWidth, int pixelHeight)
        {
            SkipOverlayForBench = true;
            try { return Render(pixelWidth, pixelHeight); }
            finally { SkipOverlayForBench = false; }
        }

        private static void SetLayer(Transform t)
        {
            t.gameObject.layer = Layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayer(t.GetChild(i));
        }

        // Кадр арены показывает UI Toolkit: старую текстуру нельзя освобождать
        // в том же кадре, где её ещё рисует панель (GPU обратится к удалённому
        // ресурсу — зависание устройства). Освобождение — через несколько кадров.
        private const int ReleaseDelayFrames = 4;
        private static readonly List<(RenderTexture Texture, int Frame)> released = new List<(RenderTexture, int)>();

        private void ReleaseTarget()
        {
            if (target == null)
                return;
            released.Add((target, Time.frameCount + ReleaseDelayFrames));
            target = null;
        }

        // Освободить отложенные текстуры, чей срок прошёл (all — все сразу).
        public static void PurgeReleased(bool all = false)
        {
            for (int i = released.Count - 1; i >= 0; i--)
            {
                if (!all && released[i].Frame > Time.frameCount)
                    continue;
                RenderTexture texture = released[i].Texture;
                released.RemoveAt(i);
                if (texture == null)
                    continue;
                texture.Release();
                Object.DestroyImmediate(texture);
            }
        }

        public static int PendingReleases => released.Count;

        // Выборок MSAA у кадра камеры. 4 — кадр рисуется во временную MSAA-
        // текстуру и сводится в показываемую (без MSAA). На D3D12 рендер MapLab
        // прямо в текстуру без MSAA ронял игру при выходе, а MSAA-текстура,
        // которую читает UI, вешала устройство. 1 — без MSAA (для проверок).
        public static int FrameMsaa = 0;

        // 0 — по месту: в редакторе 1 (MSAA-рендер MapLab в Play Mode на D3D12
        // ронял устройство даже без alpha-to-coverage), в сборке 4 (там MSAA
        // стабилен и сглаживает края геометрии). Обход наблюдений 09.11.2026
        // (GTX 1070, D3D12; на D3D11 оба варианта чистые), не первопричина.
        public static int EffectiveFrameMsaa => FrameMsaa > 0 ? FrameMsaa : Application.isEditor ? 1 : 4;

        // Слой арены видит только камера арены (в том числе камеры сцены,
        // появившиеся позже; вызывается каждый кадр, пока арена жива).
        public static void StripOtherCameras()
        {
            int count = AllCameras(out Camera[] cameras);
            for (int i = 0; i < count; i++)
            {
                Camera other = cameras[i];
                if ((other.cullingMask & ArenaMask) == 0 || IsArenaCamera(other))
                    continue;
                other.cullingMask &= ~ArenaMask;
            }
        }

        private static bool IsArenaCamera(Camera camera)
        {
            for (int i = 0; i < liveArenas.Count; i++)
                if (liveArenas[i].camera == camera)
                    return true;
            return false;
        }

        // Все камеры сцены в переиспользуемый буфер (Camera.allCameras каждый
        // кадр создавал новый массив).
        private static Camera[] cameraBuffer = new Camera[8];

        internal static int AllCameras(out Camera[] cameras)
        {
            if (cameraBuffer.Length < Camera.allCamerasCount)
                cameraBuffer = new Camera[Camera.allCamerasCount * 2];
            cameras = cameraBuffer;
            return Camera.GetAllCameras(cameraBuffer);
        }

        public static bool HasLive => liveArenas.Count > 0;

        // Выход из игры или Play Mode: освободить живую арену и отложенные
        // текстуры, пока устройство ещё работает (объекты HideAndDontSave
        // иначе переживают Play Mode).
        public static void ShutdownAll()
        {
            foreach (MapLabArena arena in liveArenas.ToArray())
                arena.Dispose();
            PurgeReleased(true);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            liveArenas.Remove(this);
            pending = null;
            if (view != null)
            {
                view.Clear();
                Object.DestroyImmediate(view.gameObject);
                view = null;
            }
            if (camera != null)
                Object.DestroyImmediate(camera.gameObject);
            if (settings != null)
                Object.DestroyImmediate(settings);
            ReleaseOverlay();
            framePost?.Dispose();
            DisposeParticles();
            framePost = null;
            if (decorSet != null)
                Object.DestroyImmediate(decorSet);
            // Поле уже отвязано от кадра (драйвер снимает текстуру до Dispose):
            // освобождается сразу, вместе с отложенными с прошлых кадров.
            if (target != null)
            {
                target.Release();
                Object.DestroyImmediate(target);
                target = null;
            }
            if (liveArenas.Count == 0)
                PurgeReleased(true);
        }
    }
}
