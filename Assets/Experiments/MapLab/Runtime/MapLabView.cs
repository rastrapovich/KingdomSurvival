using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomSurvival.MapLab
{
    // Координатор тестовой сцены: генерация данных, покраска, меши, материалы.
    // Создаёт и удаляет только объекты своего корня GeneratedMapRoot и только
    // свои временные ресурсы; чужие Terrain, камеры и материалы не трогает.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MapLabView : MonoBehaviour
    {
        public const string RootName = "GeneratedMapRoot";

        [Tooltip("Настройки карты (preset asset).")]
        public MapLabSettings Settings;
        [Tooltip("Камера MapLab (фиксированный азимут, ортографическая).")]
        public MapLabCameraRig CameraRig;
        [Tooltip("Строить карту при открытии сцены и входе в Play Mode.")]
        public bool GenerateOnLoad = true;

        [Header("Шейдеры (назначаются при создании сцены)")]
        public Shader TerrainShader;
        public Shader WaterShader;
        public Shader CardShader;
        public Shader SolidShader;
        public Shader LinesShader;
        public Shader ArtCardShader;
        public Shader ContactShader;
        public Shader InkShader;

        [Header("Художественный набор этапа 02 (локальные PNG; без них — процедурный вид)")]
        public MapLabArtSet ArtSet;
        public MapLabMarkerSet MarkerSet;

        [Header("Набор этапа 03 (деревья, кусты, дома P50/P35); фишка и подложки — от этапа 02")]
        public MapLabArtSet ArtSet03;

        [Header("Этап 04: герой (два набора направлений, 50° и 35°)")]
        public MapLabMarkerSet MarkerSet04;
        public Shader GrassShader;

        [Header("Этап 05: трава, камни, сено, декали, модули оград и мостов, дома в четырёх ракурсах; герой P35")]
        public MapLabArtSet ArtSet05;
        public MapLabMarkerSet MarkerSet05;
        public Shader ModuleShader;

        [Header("Скалы ревизии 06: рисованные стены уступов, торцы, контрфорс, осыпи")]
        public MapLabArtSet ArtSet06;
        public Shader CliffShader;

        [SerializeField, HideInInspector] private GameObject root;

        private MapLabData data;
        private MapLabPalette fallbackPalette;
        private readonly List<Object> owned = new List<Object>();
        private Material terrainMaterial, waterMaterial, cardMaterial, solidMaterial, linesMaterial;
        private Texture2D paintTexture, shadeTexture, maskATexture, maskBTexture, heightTexture;
        private Texture2D paletteTexture, detailTexture, waterTexture, atlasTexture;
        private readonly Dictionary<string, GameObject> groups = new Dictionary<string, GameObject>();
        private static Texture2D cachedDetail, cachedWater, cachedAtlas;
        private readonly Dictionary<MapLabArtEntry, Material> artMaterials = new Dictionary<MapLabArtEntry, Material>();
        private MapLabArtBuilder.Plan artPlan;
        private bool buildingSprites;
        // Этап 03: тайлы краски, обзорная копия, перо, тени объектов, кадр домов.
        private Material inkMaterial, grassMaterial, shoreInkMaterial;
        private readonly Dictionary<MapLabArtEntry, Material> moduleMaterials = new Dictionary<MapLabArtEntry, Material>();
        private Texture2D shoreTexture;
        private MapLabWash.Layer washLayer;
        private List<MapLabWash.Source> washSources;
        public MapLabModules05.Stats ModuleStats { get; private set; }
        public MapLabInk.Stats ShoreInkStats { get; private set; }
        public MapLabInk.Stats FieldInkStats { get; private set; }
        public int WashPatches => washLayer != null ? washLayer.Patches.Count : 0;
        public bool BridgesProcedural05 { get; private set; }
        private readonly Dictionary<MapLabArtEntry, Material> cliffMaterials = new Dictionary<MapLabArtEntry, Material>();
        public bool DrawnCliffs06 { get; private set; }

        public MapLabArtSet ResolvedArtSet06
        {
            get
            {
#if UNITY_EDITOR
                if (ArtSet06 == null)
                    ArtSet06 = UnityEditor.AssetDatabase.LoadAssetAtPath<MapLabArtSet>("Assets/_Project/Art/MapLabCliffs06/MapLabArtSet_Cliffs06.asset");
#endif
                return ArtSet06;
            }
        }
        private readonly List<Texture2D> paintTiles = new List<Texture2D>();
        private readonly List<Vector4> paintRects = new List<Vector4>();
        private Texture2D overviewTexture, objShadowTexture;
        private MapLabPaintFields paintFields;
        private float tileWorld = 64f;
        private int tilesPerSide = 1;
        private readonly List<MeshRenderer> terrainRenderers = new List<MeshRenderer>();
        private MaterialPropertyBlock paintBlock;
        private bool paintContribution;
        private int houseView = 50;
        private bool artHouses03;

        public MapLabData Data => data;
        public MapLabTiledPainter.Result LastTiledPaint { get; private set; }
        public MapLabInk.Stats InkStats { get; private set; }
        public int ShadowShapes { get; private set; }
        public int ShadowResolution { get; private set; }
        public float ShadowMs { get; private set; }
        // Этап 03: какой кадр домов показан (50 или 35) — с гистерезисом.
        public int HouseView => houseView;
        public bool HasArtHouses03 => artHouses03;
        public float PaintTexelsPerUnit => Settings != null && Settings.IsStage03 && LastTiledPaint != null ? LastTiledPaint.TexelsPerUnit : (data != null && Settings != null ? Settings.PaintResolution / data.Size : 0f);
        public MapLabArtBuilder.Plan ArtPlan => artPlan;
        public int ArtMaterialCount => artMaterials.Count;

        // Центр демонстрационного участка: поселение пресета (или центр карты).
        public Vector2 DemoFocus
        {
            get
            {
                if (data == null)
                    return Vector2.one * 128f;
                if (data.Composition != null && data.Composition.SettlementCenter != Vector2.zero)
                    return data.Composition.SettlementCenter;
                return Vector2.one * data.Size * 0.5f;
            }
        }

        // Нарисованные карточки при наклоне не около 50° — ракурс приближённый.
        public bool ArtSpritesApproximate
        {
            get
            {
                if (artPlan == null || artPlan.Count == 0 || CameraRig == null)
                    return false;
                // Этап 03: кадры домов 50° и 35°; дальше 5° от показанного — приближённо.
                if (Settings != null && Settings.IsStage03)
                    return Mathf.Abs(CameraRig.Pitch - houseView) > 5f;
                return Mathf.Abs(CameraRig.Pitch - 50f) > 4f;
            }
        }

        // Набор для деревьев и домов: этапа 03 или 02 по выбранному языку.
        public MapLabArtSet ObjectArtSet => Settings != null && Settings.IsStage03 ? ResolvedArtSet03 : ResolvedArtSet;

        // Фишка: на этапе 04 — герой этапа 04 (если подключён), на этапе 05 —
        // только набор P35 шести направлений, иначе прежняя.
        public MapLabMarkerSet ActiveMarkerSet
        {
            get
            {
#if UNITY_EDITOR
                if (MarkerSet04 == null)
                    MarkerSet04 = UnityEditor.AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>("Assets/_Project/Art/MapLabStage04/MapLabMarkerSet_Stage04.asset");
                if (MarkerSet05 == null)
                    MarkerSet05 = UnityEditor.AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>("Assets/_Project/Art/MapLabStage05/MapLabMarkerSet_Stage05.asset");
#endif
                if (Settings != null && Settings.IsStage05)
                    return MarkerSet05 != null && MarkerSet05.ArtSet != null ? MarkerSet05 : null;
                if (Settings != null && Settings.IsStage04 && MarkerSet04 != null && MarkerSet04.ArtSet != null)
                    return MarkerSet04;
                return MarkerSet;
            }
        }

        public MapLabArtSet ResolvedArtSet05
        {
            get
            {
#if UNITY_EDITOR
                if (ArtSet05 == null)
                    ArtSet05 = UnityEditor.AssetDatabase.LoadAssetAtPath<MapLabArtSet>("Assets/_Project/Art/MapLabStage05/MapLabArtSet_Stage05.asset");
#endif
                return ArtSet05;
            }
        }

        public int GrassTufts { get; private set; }

        public MapLabArtSet ResolvedArtSet03
        {
            get
            {
#if UNITY_EDITOR
                if (ArtSet03 == null)
                    ArtSet03 = UnityEditor.AssetDatabase.LoadAssetAtPath<MapLabArtSet>("Assets/_Project/Art/MapLabStage03/MapLabArtSet_Stage03.asset");
#endif
                return ArtSet03;
            }
        }

        public MapLabMarker Marker => GetComponent<MapLabMarker>();

        // Набор арта: назначенный в сцене или (в редакторе) лежащий по стандартному пути.
        public MapLabArtSet ResolvedArtSet
        {
            get
            {
#if UNITY_EDITOR
                if (ArtSet == null)
                    ArtSet = UnityEditor.AssetDatabase.LoadAssetAtPath<MapLabArtSet>("Assets/_Project/Art/MapLabStage02/MapLabArtSet_Stage02.asset");
                if (MarkerSet == null)
                    MarkerSet = UnityEditor.AssetDatabase.LoadAssetAtPath<MapLabMarkerSet>("Assets/_Project/Art/MapLabStage02/MapLabMarkerSet_Stage02.asset");
#endif
                return ArtSet;
            }
        }
        public GameObject Root => root;
        public string LastError { get; private set; }
        public MapLabStats LastStats { get; private set; }

        public MapLabPalette Palette
        {
            get
            {
                if (Settings != null && Settings.PainterlyStyle.Palette != null)
                    return Settings.PainterlyStyle.Palette;
                if (fallbackPalette == null)
                    fallbackPalette = MapLabPalette.CreateFallback();
                return fallbackPalette;
            }
        }

        private void OnEnable()
        {
            if (data == null)
                DestroyStaleRoot();
            if (GenerateOnLoad && data == null && Settings != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.EditorApplication.delayCall += DelayedGenerate;
                    return;
                }
#endif
                SafeGenerate();
            }
        }

#if UNITY_EDITOR
        private void DelayedGenerate()
        {
            if (this != null && isActiveAndEnabled && data == null && !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                SafeGenerate();
        }
#endif

        private void OnDisable()
        {
            Clear();
        }

        private void OnValidate()
        {
            // Только дешёвое: материалы, палитра объектов, видимость. Тяжёлая
            // перестройка — кнопками окна MapLab.
            if (data != null)
            {
                ApplyMaterialSettings();
                ApplyVisibility();
            }
        }

        private void SafeGenerate()
        {
            try
            {
                Generate(null);
            }
            catch (MapLabGenerator.CancelledException)
            {
            }
            catch (System.Exception exception)
            {
                LastError = exception.Message;
                UnityEngine.Debug.LogException(exception, this);
            }
        }

        // ---------- Команды ----------

        public bool Generate(MapLabGenerator.Progress progress)
        {
            if (!Validate())
                return false;
            Stopwatch watch = Stopwatch.StartNew();
            // Регистрация ракурсов построек этапа 05 нужна расстановке дворов.
            Settings.Stage05.Art = ResolvedArtSet05;
            MapLabData fresh = MapLabGenerator.Generate(Settings, progress);
            if (GetComponent<MapLabMarker>() == null)
                gameObject.AddComponent<MapLabMarker>();
            Clear();
            data = fresh;
            BuildEverything(progress);
            MapLabStats stats = data.Stats;
            stats.GenerateMs = (float)watch.Elapsed.TotalMilliseconds;
            data.Stats = stats;
            LastStats = stats;
            return true;
        }

        public bool RebuildTerrain(MapLabGenerator.Progress progress)
        {
            // Рельеф и всё зависимое (берега, стены уступов, высоты объектов,
            // мосты) — заново из тех же settings и seed.
            return Generate(progress);
        }

        public bool RepaintOnly()
        {
            if (data == null || !Validate())
                return false;
            Repaint();
            ApplyMaterialSettings();
            return true;
        }

        public bool RebuildObjects(MapLabGenerator.Progress progress)
        {
            if (data == null || !Validate())
                return false;
            MapLabGenerator.Report(progress, "Растительность и постройки", 0.2f);
            Settings.Stage05.Art = ResolvedArtSet05;
            MapLabGenerator.RebuildObjects(data, Settings);
            MapLabGenerator.Report(progress, "Объекты", 0.6f);
            DestroyObjectGroups();
            BuildObjects();
            UploadShade();
            Repaint();
            ApplyMaterialSettings();
            ApplyVisibility();
            return true;
        }

        public void Clear()
        {
            if (root != null)
                DestroySafe(root);
            root = null;
            groups.Clear();
            for (int i = 0; i < owned.Count; i++)
                if (owned[i] != null)
                    DestroySafe(owned[i]);
            owned.Clear();
            artMaterials.Clear();
            moduleMaterials.Clear();
            cliffMaterials.Clear();
            DrawnCliffs06 = false;
            artPlan = null;
            shoreTexture = null;
            washLayer = null;
            shoreInkMaterial = null;
            MapLabMarker marker = Marker;
            if (marker != null)
                marker.Clear();
            terrainMaterial = waterMaterial = cardMaterial = solidMaterial = linesMaterial = inkMaterial = grassMaterial = null;
            paintTiles.Clear();
            paintRects.Clear();
            terrainRenderers.Clear();
            overviewTexture = objShadowTexture = null;
            paintFields = null;
            LastTiledPaint = null;
            artHouses03 = false;
            paintTexture = shadeTexture = maskATexture = maskBTexture = heightTexture = null;
            paletteTexture = detailTexture = waterTexture = atlasTexture = null;
            data = null;
        }

        private bool Validate()
        {
            LastError = null;
            if (Settings == null)
            {
                LastError = "Не назначены настройки MapLab (MapLabSettings).";
                return false;
            }
            if (TerrainShader == null) TerrainShader = Shader.Find("Kingdom Survival/MapLab/Terrain");
            if (WaterShader == null) WaterShader = Shader.Find("Kingdom Survival/MapLab/Water");
            if (CardShader == null) CardShader = Shader.Find("Kingdom Survival/MapLab/Card");
            if (SolidShader == null) SolidShader = Shader.Find("Kingdom Survival/MapLab/Solid");
            if (LinesShader == null) LinesShader = Shader.Find("Kingdom Survival/MapLab/Lines");
            if (ArtCardShader == null) ArtCardShader = Shader.Find("Kingdom Survival/MapLab/Art Card");
            if (ContactShader == null) ContactShader = Shader.Find("Kingdom Survival/MapLab/Contact");
            if (InkShader == null) InkShader = Shader.Find("Kingdom Survival/MapLab/Ink");
            if (GrassShader == null) GrassShader = Shader.Find("Kingdom Survival/MapLab/Grass");
            if (ModuleShader == null) ModuleShader = Shader.Find("Kingdom Survival/MapLab/Module");
            if (CliffShader == null) CliffShader = Shader.Find("Kingdom Survival/MapLab/Cliff");
            if (TerrainShader == null || WaterShader == null || CardShader == null || SolidShader == null)
            {
                LastError = "Не найдены шейдеры MapLab (Assets/Experiments/MapLab/Shaders).";
                return false;
            }
            return true;
        }

        // ---------- Построение ----------

        private void BuildEverything(MapLabGenerator.Progress progress)
        {
            Stopwatch watch = Stopwatch.StartNew();
            EnsureRoot();
            CreateSharedResources();
            MapLabGenerator.Report(progress, "Покраска поверхности", 0.94f);
            UploadDataTextures();
            UploadShade();
            Repaint();
            MapLabGenerator.Report(progress, "Меши", 0.97f);
            BuildTerrain();
            BuildWater();
            BuildCliffs();
            BuildObjects();
            BuildInk();
            BuildGrass();
            BuildWireframe();
            ApplyMaterialSettings();
            if (CameraRig != null)
                CameraRig.Bind(this, true);
            ApplyVisibility();
            MapLabMarker marker = Marker;
            if (marker != null)
                marker.OnMapBuilt(this);
            MapLabStats stats = data.Stats;
            stats.PaintMs += 0f;
            CountStats(ref stats);
            stats.ObjectsMs += (float)watch.Elapsed.TotalMilliseconds - stats.PaintMs;
            data.Stats = stats;
        }

        private void EnsureRoot()
        {
            if (root != null)
                return;
            root = new GameObject(RootName) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
        }

        private GameObject Group(string name)
        {
            if (groups.TryGetValue(name, out GameObject group) && group != null)
                return group;
            group = new GameObject(name) { hideFlags = HideFlags.DontSave };
            group.transform.SetParent(root.transform, false);
            groups[name] = group;
            return group;
        }

        private void DestroyGroup(string name)
        {
            if (!groups.TryGetValue(name, out GameObject group) || group == null)
                return;
            foreach (MeshFilter filter in group.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                {
                    owned.Remove(filter.sharedMesh);
                    DestroySafe(filter.sharedMesh);
                }
            }
            DestroySafe(group);
            groups.Remove(name);
        }

        private MeshRenderer AddRenderer(GameObject parent, string name, Mesh mesh, Material material)
        {
            if (mesh == null)
                return null;
            owned.Add(mesh);
            GameObject go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        private T Own<T>(T obj) where T : Object
        {
            obj.hideFlags = HideFlags.DontSave;
            owned.Add(obj);
            return obj;
        }

        private void CreateSharedResources()
        {
            terrainMaterial = Own(new Material(TerrainShader) { name = "MapLab Terrain" });
            waterMaterial = Own(new Material(WaterShader) { name = "MapLab Water" });
            cardMaterial = Own(new Material(CardShader) { name = "MapLab Cards", enableInstancing = false });
            solidMaterial = Own(new Material(SolidShader) { name = "MapLab Solid" });
            if (LinesShader != null)
                linesMaterial = Own(new Material(LinesShader) { name = "MapLab Lines" });
            if (InkShader != null)
            {
                inkMaterial = Own(new Material(InkShader) { name = "MapLab Ink" });
                shoreInkMaterial = Own(new Material(InkShader) { name = "MapLab Shore Ink" });
            }
            if (GrassShader != null)
                grassMaterial = Own(new Material(GrassShader) { name = "MapLab Grass" });
            paintBlock = new MaterialPropertyBlock();
            paletteTexture = Own(MapLabArt.CreatePaletteTexture(Palette));
            // Процедурные фактуры и атлас не зависят от карты — общие на сессию редактора.
            detailTexture = cachedDetail != null ? cachedDetail : cachedDetail = MapLabArt.CreateDetailTexture(1);
            waterTexture = cachedWater != null ? cachedWater : cachedWater = MapLabArt.CreateWaterTexture(1);
            atlasTexture = cachedAtlas != null ? cachedAtlas : cachedAtlas = MapLabArt.CreateVegetationAtlas(7);
        }

        private void UploadDataTextures()
        {
            int n = data.Resolution;
            heightTexture = Own(new Texture2D(n, n, TextureFormat.RFloat, false, true)
            {
                name = "MapLab Height",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            });
            heightTexture.SetPixelData(data.Heights, 0);
            heightTexture.Apply(false, false);
            maskATexture = Own(new Texture2D(n, n, TextureFormat.RGBA32, false, true) { name = "MapLab Masks A", wrapMode = TextureWrapMode.Clamp });
            maskBTexture = Own(new Texture2D(n, n, TextureFormat.RGBA32, false, true) { name = "MapLab Masks B", wrapMode = TextureWrapMode.Clamp });
            UploadMasks();
        }

        private void UploadMasks()
        {
            int n = data.Resolution;
            Color32[] a = new Color32[n * n];
            Color32[] b = new Color32[n * n];
            for (int i = 0; i < a.Length; i++)
            {
                bool water = data.Water[i] != (byte)MapLabWaterKind.Dry && data.Heights[i] < data.WaterLevel[i];
                a[i] = new Color32(B(data.Forest[i]), B(data.Field[i]), B(data.Road[i]), B(data.Cliff[i]));
                b[i] = new Color32((byte)(water ? 255 : 0), B(data.Shore[i]), B(data.Settlement[i]), B(data.Rock[i]));
            }
            maskATexture.SetPixels32(a);
            maskATexture.Apply(false, false);
            maskBTexture.SetPixels32(b);
            maskBTexture.Apply(false, false);
        }

        private static byte B(float v)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        }

        private void UploadShade()
        {
            int r = data.ShadeResolution;
            if (r <= 0 || data.ShadeLight == null)
                return;
            if (shadeTexture == null || shadeTexture.width != r)
            {
                shadeTexture = Own(new Texture2D(r, r, TextureFormat.RGBA32, true, true)
                {
                    name = "MapLab Shade",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                });
            }
            Color32[] pixels = new Color32[r * r];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(B(data.ShadeLight[i]), B(data.ShadeOcclusion[i]), 0, 255);
            shadeTexture.SetPixels32(pixels);
            shadeTexture.Apply(true, false);
            if (maskATexture != null)
                UploadMasks();
        }

        // Сторона чанка рельефа и тайла краски этапа 03: около 64 единиц, кратно шагу сетки.
        private int TerrainSpan => Settings.IsStage03 ? Mathf.Clamp(Mathf.RoundToInt(64f / data.Cell), 8, 128) : Mathf.Clamp(Settings.Advanced.ChunkVertices, 9, 129) - 1;

        private void Repaint()
        {
            if (paletteTexture != null)
                MapLabArt.WritePalette(paletteTexture, Palette);
            if (Settings.IsStage03)
            {
                RepaintTiled();
                return;
            }
            DestroyTiles();
            MapLabPainter.Result paint = MapLabPainter.Paint(data, Settings, Palette);
            if (paintTexture == null || paintTexture.width != paint.Resolution)
            {
                if (paintTexture != null)
                {
                    owned.Remove(paintTexture);
                    DestroySafe(paintTexture);
                }
                paintTexture = Own(new Texture2D(paint.Resolution, paint.Resolution, TextureFormat.RGBA32, true, false)
                {
                    name = "MapLab Paint",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                });
            }
            paintTexture.SetPixels32(paint.Pixels);
            paintTexture.Apply(true, false);
            MapLabStats stats = data.Stats;
            stats.PaintMs = paint.Milliseconds;
            data.Stats = stats;
            ApplyPaintBlocks();
        }

        // Этап 03: тайлы по чанкам рельефа с плотностью PaintTexelsPerUnit.
        private void RepaintTiled()
        {
            tileWorld = TerrainSpan * data.Cell;
            MapLabTiledPainter.Options options = new MapLabTiledPainter.Options
            {
                StrokeContribution = Settings.Diagnostics.Mode == MapLabDiagnosticMode.StrokeContribution,
                RoadInkInPaint = !Settings.Stage03.Ink,
                Washes = Settings.IsStage05 && Settings.Diagnostics.ShowPaint ? WashLayer() : null,
            };
            paintContribution = options.StrokeContribution;
            MapLabTiledPainter.Result result = MapLabTiledPainter.Paint(data, Settings, Palette, tileWorld, options);
            DestroyTiles();
            if (paintTexture != null)
            {
                owned.Remove(paintTexture);
                DestroySafe(paintTexture);
                paintTexture = null;
            }
            tilesPerSide = result.TilesPerSide;
            foreach (MapLabTiledPainter.Tile tile in result.Tiles)
            {
                Texture2D t = Own(new Texture2D(tile.Size, tile.Size, TextureFormat.RGBA32, true, false)
                {
                    name = $"MapLab Paint {tile.X},{tile.Z}",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = Settings.IsStage05 ? Mathf.Clamp(Settings.Stage05.GroundAniso, 1, 16) : 8,
                });
                t.SetPixels32(tile.Pixels);
                t.Apply(true, true);
                tile.Pixels = null;
                paintTiles.Add(t);
                paintRects.Add(tile.Rect);
            }
            overviewTexture = Own(new Texture2D(result.OverviewResolution, result.OverviewResolution, TextureFormat.RGBA32, true, false)
            {
                name = "MapLab Paint Overview",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            });
            overviewTexture.SetPixels32(result.Overview);
            overviewTexture.Apply(true, true);
            result.Overview = null;
            paintFields = result.Fields;
            LastTiledPaint = result;
            UploadShore();
            MapLabStats stats = data.Stats;
            stats.PaintMs = result.Milliseconds;
            data.Stats = stats;
            ApplyPaintBlocks();
        }

        private void DestroyTiles()
        {
            foreach (Texture2D t in paintTiles)
            {
                owned.Remove(t);
                DestroySafe(t);
            }
            paintTiles.Clear();
            paintRects.Clear();
            if (overviewTexture != null)
            {
                owned.Remove(overviewTexture);
                DestroySafe(overviewTexture);
                overviewTexture = null;
            }
        }

        // Каждому чанку рельефа — свой тайл краски (порядок чанков и тайлов один:
        // по Z, затем по X); этап 02 — одна текстура на всю карту.
        private void ApplyPaintBlocks()
        {
            if (paintBlock == null)
                paintBlock = new MaterialPropertyBlock();
            for (int i = 0; i < terrainRenderers.Count; i++)
            {
                MeshRenderer renderer = terrainRenderers[i];
                if (renderer == null)
                    continue;
                if (i < paintTiles.Count)
                {
                    paintBlock.Clear();
                    paintBlock.SetTexture("_PaintTex", paintTiles[i]);
                    paintBlock.SetVector("_PaintRect", paintRects[i]);
                    renderer.SetPropertyBlock(paintBlock);
                }
                else
                {
                    renderer.SetPropertyBlock(null);
                }
            }
        }

        private void BuildTerrain()
        {
            GameObject group = Group("Terrain");
            terrainRenderers.Clear();
            List<Mesh> chunks = MapLabMeshes.BuildTerrainChunks(data, TerrainSpan + 1);
            for (int i = 0; i < chunks.Count; i++)
                terrainRenderers.Add(AddRenderer(group, chunks[i].name, chunks[i], terrainMaterial));
            ApplyPaintBlocks();
        }

        // Этап 03: тушевые кромки дорог и троп по общему профилю пера; этап 05 —
        // ещё перо берега по общей сглаженной границе.
        private void BuildInk()
        {
            DestroyGroup("Ink");
            InkStats = default;
            ShoreInkStats = default;
            if (!Settings.IsStage03 || !Settings.Stage03.Ink || inkMaterial == null || paintFields == null)
                return;
            Mesh mesh = MapLabInk.BuildRoadInk(data, Settings, paintFields, out MapLabInk.Stats stats);
            InkStats = stats;
            AddRenderer(Group("Ink"), "Road Ink", mesh, inkMaterial);
            if (Settings.IsStage05)
            {
                Mesh fieldInk = MapLabInk.BuildFieldInk(data, Settings, paintFields, out MapLabInk.Stats fieldStats);
                FieldInkStats = fieldStats;
                AddRenderer(Group("Ink"), "Field Ink", fieldInk, inkMaterial);
            }
            if (Settings.IsStage05 && shoreInkMaterial != null && paintFields.Shore != null)
            {
                Mesh shore = MapLabInk.BuildShoreInk(data, Settings, paintFields.Shore, out MapLabInk.Stats shoreStats);
                ShoreInkStats = shoreStats;
                AddRenderer(Group("Ink"), "Shore Ink", shore, shoreInkMaterial);
            }
        }

        // Этап 05: акварельные пятна из Wash PNG набора (читаемые текстуры декалей).
        private MapLabWash.Layer WashLayer()
        {
            MapLabArtSet set = ResolvedArtSet05;
            if (washSources == null && set != null)
            {
                washSources = new List<MapLabWash.Source>();
                foreach (MapLabArtEntry e in set.Entries)
                {
                    if (e == null || !e.HasTexture || e.Category != MapLabArtCategory.Decal || !e.Texture.isReadable)
                        continue;
                    washSources.Add(MapLabWash.FromPixels(e.Id, e.Texture.GetPixels32(), e.Texture.width, e.Texture.height, e.OpacityRange));
                }
            }
            washLayer = MapLabWash.Place(data, Settings, washSources);
            return washLayer;
        }

        // Этап 05: общая граница берега — в текстуру для края воды.
        private void UploadShore()
        {
            if (shoreTexture != null)
            {
                owned.Remove(shoreTexture);
                DestroySafe(shoreTexture);
                shoreTexture = null;
            }
            if (paintFields == null || paintFields.Shore == null)
                return;
            MapLabShore shore = paintFields.Shore;
            shoreTexture = Own(new Texture2D(shore.N, shore.N, TextureFormat.RFloat, false, true)
            {
                name = "MapLab Shore",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            });
            shoreTexture.SetPixelData(shore.Above, 0);
            shoreTexture.Apply(false, false);
        }

        // Этап 04: редкие пучки травы (цвет — местный луг из полей покраски).
        // Этап 05: заменены укоренёнными PNG-спрайтами (старые пучки выключены).
        private void BuildGrass()
        {
            DestroyGroup("Grass");
            GrassTufts = 0;
            if (!Settings.IsStage04 || Settings.IsStage05 || grassMaterial == null || paintFields == null || Settings.Stage04.Grass <= 0f)
                return;
            List<MapLabGrass.Tuft> tufts = MapLabGrass.Place(data, Settings);
            GrassTufts = tufts.Count;
            AddRenderer(Group("Grass"), "Grass Tufts", MapLabGrass.Build(data, paintFields, tufts), grassMaterial);
        }

        // Только перо (окончания линий и прочие настройки пера), без перекраски.
        public void RebuildInk()
        {
            if (data == null || !Validate())
                return;
            BuildInk();
            ApplyMaterialSettings();
            ApplyVisibility();
        }

        // Этап 03: перекрасить и пересобрать перо и тени (переключатели окна).
        public bool RebuildStage03Layers()
        {
            if (data == null || !Validate())
                return false;
            Repaint();
            BuildInk();
            BuildGrass();
            BuildObjectShadows();
            ApplyMaterialSettings();
            ApplyVisibility();
            return true;
        }

        private void BuildWater()
        {
            GameObject group = Group("Water");
            AddRenderer(group, "Lakes", MapLabMeshes.BuildLakes(data), waterMaterial);
            AddRenderer(group, "Rivers", MapLabMeshes.BuildRivers(data), waterMaterial);
        }

        private void BuildCliffs()
        {
            GameObject group = Group("Cliffs");
            // Рисованные скалы ревизии 06 вместо граней; без набора — грани этапа 05.
            DrawnCliffs06 = false;
            MapLabCliffs06.ScreeSpots.Clear();
            if (Settings.IsStage05 && Settings.Stage05.DrawnCliffs && CliffShader != null)
            {
                MapLabCliffs06.Kit kit = MapLabCliffs06.KitOf(ResolvedArtSet06);
                if (kit.HasFaces)
                {
                    MapLabCliffs05.ScreeSpots.Clear();
                    MapLabCliffs06.Result cliffs = MapLabCliffs06.Build(data, Settings, kit);
                    List<MapLabArtEntry> keys = new List<MapLabArtEntry>(cliffs.Meshes.Keys);
                    keys.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                    foreach (MapLabArtEntry e in keys)
                        AddRenderer(group, "Cliff " + e.Id, cliffs.Meshes[e].ToMesh("MapLab Cliff " + e.Id), CliffMaterial(e));
                    DrawnCliffs06 = true;
                    return;
                }
            }
            AddRenderer(group, "Cliff Walls", Settings.IsStage05 ? MapLabCliffs05.Build(data, Settings) : MapLabMeshes.BuildCliffs(data, Settings), solidMaterial);
        }

        private Material CliffMaterial(MapLabArtEntry entry)
        {
            if (cliffMaterials.TryGetValue(entry, out Material material) && material != null)
                return material;
            material = Own(new Material(CliffShader) { name = "MapLab Cliff " + entry.Id });
            cliffMaterials[entry] = material;
            ConfigureCliffMaterial(material, entry);
            return material;
        }

        private void ConfigureCliffMaterial(Material m, MapLabArtEntry entry)
        {
            m.SetTexture("_MainTex", entry.Texture);
            m.SetTexture("_ShadeTex", shadeTexture != null ? shadeTexture : Texture2D.whiteTexture);
            m.SetFloat("_MapSize", data != null ? data.Size : Settings.General.MapSize);
            m.SetFloat("_GroundShade", Settings.Diagnostics.ShowReliefLight ? 0.45f : 0f);
            m.SetVector("_LightDir", Settings.LightDirection);
            m.SetFloat("_Saturation", Settings.PainterlyStyle.Saturation);
        }

        private static readonly string[] ObjectGroups = { "Vegetation", "Structures", "Houses", "Boulders", "ArtVegetation", "ArtHouses", "ArtHouses50", "ArtHouses35", "ArtBoulders", "ArtCliffDecor", "ArtHouses05", "ArtDecor05", "Modules05" };

        private void DestroyObjectGroups()
        {
            foreach (string name in ObjectGroups)
                DestroyGroup(name);
        }

        // Процедурные объекты — только для типов, не заменённых рисунками;
        // дома строятся в обоих видах (геометрия и карточка), чтобы смена
        // наклона переключала их без перестройки.
        private void BuildObjects()
        {
            artPlan = new MapLabArtBuilder.Plan();
            MapLabArtBuilder.Plan housePlan = new MapLabArtBuilder.Plan();
            MapLabArtBuilder.Plan housePlan35 = new MapLabArtBuilder.Plan();
            MapLabArtSet set = ObjectArtSet;
            bool stage03 = Settings.IsStage03;
            bool stage05 = Settings.IsStage05;
            MapLabArtSet set05 = stage05 ? ResolvedArtSet05 : null;
            MapLabArtBuilder.Plan decorPlan = new MapLabArtBuilder.Plan();
            if (set05 != null)
            {
                // Этап 05: деревья — набор этапа 03; дома, трава, камни, сено, реквизит — этап 05.
                MapLabArtBuilder.PlanHouses05(data, Settings, set05, housePlan);
                MapLabArtBuilder.PlanDecor05(data, Settings, set05, paintFields != null ? paintFields.Shore : new MapLabShore(data, Settings.Stage05.ShoreSmoothing), decorPlan);
                if (DrawnCliffs06)
                    MapLabArtBuilder.PlanCliffScree06(data, Settings, MapLabCliffs06.KitOf(ResolvedArtSet06), decorPlan);
                foreach (MapLabObjectKind kind in decorPlan.Replaced)
                    artPlan.Replaced.Add(kind);
            }
            if (set != null)
            {
                MapLabArtBuilder.PlanVegetation(data, Settings, set, artPlan);
                if (!stage05)
                {
                    MapLabArtBuilder.PlanBoulders(data, Settings, set, artPlan);
                    MapLabArtBuilder.PlanCliffDecor(data, Settings, set, Settings.Camera.AzimuthDegrees, artPlan);
                }
                if (stage05)
                {
                }
                else if (stage03)
                {
                    // Пары ракурсов одного дома: кадр 50° и кадр 35°, без
                    // скрытого возврата к процедурной геометрии.
                    MapLabArtBuilder.PlanHouses03(data, Settings, set, 50f, housePlan);
                    MapLabArtBuilder.PlanHouses03(data, Settings, set, 35f, housePlan35);
                }
                else if (Settings.Art.Buildings != MapLabBuildingDisplay.Geometry)
                    MapLabArtBuilder.PlanBuildings(data, Settings, set, housePlan);
            }
            artHouses03 = stage03 && !stage05 && housePlan.Count > 0;
            GameObject vegetation = Group("Vegetation");
            List<Mesh> cards = MapLabMeshes.BuildVegetationCards(data, 64f, artPlan.Replaced);
            for (int i = 0; i < cards.Count; i++)
                AddRenderer(vegetation, cards[i].name, cards[i], cardMaterial);
            HashSet<MapLabObjectKind> noHouses = new HashSet<MapLabObjectKind> { MapLabObjectKind.House, MapLabObjectKind.Barn };
            if (artPlan.Replaced.Contains(MapLabObjectKind.Haystack))
                noHouses.Add(MapLabObjectKind.Haystack);
            // Этап 05: мосты и ограды — модули; без подключённых модулей — прежний
            // процедурный мост с явной отметкой (не молчаливая подмена).
            ModuleStats = null;
            BridgesProcedural05 = false;
            if (set05 != null && ModuleShader != null)
            {
                MapLabModules05.Batch modules = MapLabModules05.Build(data, Settings, set05);
                ModuleStats = modules.Stats;
                List<MapLabArtEntry> keys = new List<MapLabArtEntry>(modules.Meshes.Keys);
                keys.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                GameObject moduleGroup = Group("Modules05");
                foreach (MapLabArtEntry e in keys)
                    AddRenderer(moduleGroup, "Module " + e.Id, modules.Meshes[e].ToMesh("MapLab Module " + e.Id), ModuleMaterial(e));
            }
            BridgesProcedural05 = stage05 && (ModuleStats == null || ModuleStats.DeckPanels == 0) && data.Bridges.Count > 0;
            AddRenderer(Group("Structures"), "Structures", MapLabMeshes.BuildStructures(data, Settings, noHouses, stage05 && !BridgesProcedural05), solidMaterial);
            AddRenderer(Group("Houses"), "Houses", MapLabMeshes.BuildHouses(data), solidMaterial);
            AddRenderer(Group("Boulders"), "Boulders", MapLabMeshes.BuildBoulders(data, artPlan.Replaced), solidMaterial);
            BuildArtGroup("ArtVegetation", artPlan, e => e.Category == MapLabArtCategory.Vegetation);
            BuildArtGroup("ArtBoulders", artPlan, e => e.Category == MapLabArtCategory.Rock);
            BuildArtGroup("ArtCliffDecor", artPlan, e => e.Category == MapLabArtCategory.Cliff);
            if (stage05)
            {
                BuildArtGroup("ArtHouses05", housePlan, e => e.Category == MapLabArtCategory.Building);
                BuildArtGroup("ArtDecor05", decorPlan, e => true);
                foreach (KeyValuePair<MapLabArtEntry, List<MapLabArtInstance>> pair in decorPlan.Cards)
                    foreach (MapLabArtInstance inst in pair.Value)
                        artPlan.Add(pair.Key, inst);
            }
            else if (stage03)
            {
                BuildArtGroup("ArtHouses50", housePlan, e => e.Category == MapLabArtCategory.Building);
                BuildArtGroup("ArtHouses35", housePlan35, e => e.Category == MapLabArtCategory.Building);
                houseView = (CameraRig != null ? CameraRig.Pitch : Settings.Camera.PitchFor(1f)) < Settings.Stage03.HouseViewSwitch ? 35 : 50;
            }
            else
                BuildArtGroup("ArtHouses", housePlan, e => e.Category == MapLabArtCategory.Building);
            foreach (KeyValuePair<MapLabArtEntry, List<MapLabArtInstance>> pair in housePlan.Cards)
                foreach (MapLabArtInstance inst in pair.Value)
                    artPlan.Add(pair.Key, inst);
            foreach (MapLabObjectKind kind in housePlan.Replaced)
                artPlan.Replaced.Add(kind);
            BuildObjectShadows();
        }

        // Этап 03: маска стилизованных теней по фактическим объектам (размеры
        // крон — по рисункам, если они подключены).
        private void BuildObjectShadows()
        {
            if (objShadowTexture != null)
            {
                owned.Remove(objShadowTexture);
                DestroySafe(objShadowTexture);
                objShadowTexture = null;
            }
            ShadowShapes = 0;
            if (!Settings.IsStage03 || !Settings.Stage03.ObjectShadows)
                return;
            List<MapLabObjectShadows.Caster> casters = new List<MapLabObjectShadows.Caster>();
            HashSet<MapLabObjectKind> replaced = artPlan != null ? artPlan.Replaced : new HashSet<MapLabObjectKind>();
            if (artPlan != null)
            {
                foreach (KeyValuePair<MapLabArtEntry, List<MapLabArtInstance>> pair in artPlan.Cards)
                {
                    MapLabArtEntry e = pair.Key;
                    if (e.Category != MapLabArtCategory.Vegetation && e.Category != MapLabArtCategory.Rock && e.Category != MapLabArtCategory.Prop)
                        continue;
                    // Этап 05: трава, тростник и галька тени не отбрасывают (мелочь).
                    if (MapLabArtBuilder.IsDetail05(e))
                        continue;
                    bool bush = e.Kind == MapLabObjectKind.Bush || e.Category == MapLabArtCategory.Rock || e.Category == MapLabArtCategory.Prop;
                    foreach (MapLabArtInstance inst in pair.Value)
                    {
                        float w = inst.Width * e.SolidRect.width, h = inst.Height * e.SolidRect.height;
                        casters.Add(new MapLabObjectShadows.Caster
                        {
                            Base = inst.Base,
                            Radius = w * (bush ? 0.36f : 0.32f),
                            CenterHeight = h * (bush ? 0.35f : 0.6f),
                            ContactRadius = w * (bush ? 0.38f : 0.2f),
                        });
                    }
                }
            }
            foreach (MapLabPlacement o in data.Objects)
            {
                switch (o.Kind)
                {
                    case MapLabObjectKind.House:
                    case MapLabObjectKind.Barn:
                        casters.Add(new MapLabObjectShadows.Caster { Base = o.Position, Building = true, Size = o.Size, Yaw = o.Yaw, Height = o.Kind == MapLabObjectKind.House ? 3.4f : 2.6f });
                        break;
                    case MapLabObjectKind.Deciduous:
                    case MapLabObjectKind.DeciduousGroup:
                    case MapLabObjectKind.Conifer:
                    case MapLabObjectKind.Bush:
                        if (replaced.Contains(o.Kind))
                            break;
                        float r = o.Scale * (o.Kind == MapLabObjectKind.Conifer ? 0.3f : 0.42f);
                        casters.Add(new MapLabObjectShadows.Caster { Base = o.Position, Radius = r, CenterHeight = o.Scale * (o.Kind == MapLabObjectKind.Bush ? 0.3f : 0.6f), ContactRadius = r * 0.5f });
                        break;
                    case MapLabObjectKind.Boulder:
                        if (replaced.Contains(o.Kind))
                            break;
                        casters.Add(new MapLabObjectShadows.Caster { Base = o.Position, Radius = o.Scale * 0.5f, CenterHeight = o.Scale * 0.35f, ContactRadius = o.Scale * 0.5f });
                        break;
                }
            }
            MapLabObjectShadows.Result result = MapLabObjectShadows.Compute(data, Settings, casters);
            int res = result.Resolution;
            Color32[] pixels = new Color32[res * res];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(B(result.Cast[i]), B(result.Contact[i]), 0, 255);
            objShadowTexture = Own(new Texture2D(res, res, TextureFormat.RGBA32, true, true)
            {
                name = "MapLab Object Shadows",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            });
            objShadowTexture.SetPixels32(pixels);
            objShadowTexture.Apply(true, true);
            ShadowShapes = result.Shapes;
            ShadowResolution = res;
            ShadowMs = result.Milliseconds;
        }

        // Кадр домов этапа 03 по наклону, с гистерезисом вокруг порога.
        private bool UpdateHouseView(float pitch)
        {
            MapLabStage03Settings s3 = Settings.Stage03;
            int next = houseView;
            if (houseView == 50 && pitch < s3.HouseViewSwitch - s3.HouseViewHysteresis * 0.5f)
                next = 35;
            else if (houseView == 35 && pitch > s3.HouseViewSwitch + s3.HouseViewHysteresis * 0.5f)
                next = 50;
            if (next == houseView)
                return false;
            houseView = next;
            return true;
        }

        private void BuildArtGroup(string name, MapLabArtBuilder.Plan plan, System.Predicate<MapLabArtEntry> filter)
        {
            List<MapLabArtEntry> entries = new List<MapLabArtEntry>(plan.Cards.Keys);
            entries.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            GameObject group = null;
            foreach (MapLabArtEntry entry in entries)
            {
                if (!filter(entry))
                    continue;
                group = group != null ? group : Group(name);
                Material material = ArtMaterial(entry);
                List<Mesh> meshes = MapLabArtBuilder.BuildMeshes(data, entry, plan.Cards[entry], 64f);
                for (int i = 0; i < meshes.Count; i++)
                    AddRenderer(group, meshes[i].name, meshes[i], material);
            }
        }

        private Material ArtMaterial(MapLabArtEntry entry)
        {
            if (artMaterials.TryGetValue(entry, out Material material) && material != null)
                return material;
            material = Own(new Material(ArtCardShader) { name = "MapLab Art " + entry.Id });
            artMaterials[entry] = material;
            ConfigureArtMaterial(material, entry);
            return material;
        }

        // Материал карточки одного рисунка: текстура, порог, оттенок палитры по типу.
        public void ConfigureArtMaterial(Material m, MapLabArtEntry entry)
        {
            if (m == null || entry == null || Settings == null)
                return;
            MapLabPalette palette = Palette;
            MapLabArtSettings art = Settings.Art;
            m.SetTexture("_MainTex", entry.Texture);
            m.SetFloat("_Cutoff", entry.Cutoff);
            m.SetFloat("_CardLean", art.CardLean);
            Color tone;
            switch (entry.Category)
            {
                case MapLabArtCategory.Vegetation:
                    tone = entry.Kind == MapLabObjectKind.Conifer ? palette.TreeSmoky : palette.TreeMoss;
                    break;
                case MapLabArtCategory.Rock:
                case MapLabArtCategory.Cliff:
                    tone = palette.RockLight;
                    break;
                default:
                    tone = Color.white;
                    break;
            }
            m.SetColor("_TintColor", tone);
            m.SetFloat("_TintStrength", entry.Category == MapLabArtCategory.Figure || entry.Category == MapLabArtCategory.FigureCalibration || entry.Category == MapLabArtCategory.Building ? 0f : 1f);
            m.SetColor("_ShadowTint", palette.ShadowTint);
            m.SetFloat("_GroundShade", Settings.Diagnostics.ShowReliefLight ? art.GroundShade : 0f);
            m.SetFloat("_MapSize", data != null ? data.Size : Settings.General.MapSize);
            m.SetTexture("_ShadeTex", shadeTexture != null ? shadeTexture : Texture2D.whiteTexture);
            m.SetFloat("_Saturation", Settings.PainterlyStyle.Saturation);
            m.SetFloat("_AlphaView", (float)art.AlphaView);
            MapLabStage03Settings s3 = Settings.Stage03;
            m.SetColor("_AccentInk", s3.InkStyle.Color);
            m.SetFloat("_AccentOpacity", s3.InkStyle.Opacity);
            m.SetColor("_AccentLight", Color.Lerp(palette.TreeMoss, palette.GrassDry, 0.45f));
            m.SetFloat("_AccentLightStrength", s3.AccentHighlightStrength);
            m.SetFloat("_AccentHighlight", s3.AccentHighlight);
            m.SetFloat("_AccentOn", Settings.IsStage03 && s3.ForestAccents ? 1f : 0f);
            m.SetFloat("_DiagMode", (float)Settings.Diagnostics.Mode);
            MapLabStage05Settings s5 = Settings.Stage05;
            bool stage05 = Settings.IsStage05;
            m.SetFloat("_AlphaToMask", stage05 && s5.AlphaToCoverage ? 1f : 0f);
            // Эксперимент: мазки на спрайтах окружения (галочка Stage05 → Sprite Strokes).
            int strokeMode = 0;
            if (stage05 && s5.SpriteStrokes && !MapLabArtBuilder.IsDetail05(entry))
            {
                switch (entry.Category)
                {
                    case MapLabArtCategory.Vegetation: strokeMode = 1; break;
                    case MapLabArtCategory.Rock:
                    case MapLabArtCategory.CliffModule: strokeMode = 3; break;
                    case MapLabArtCategory.Building:
                    case MapLabArtCategory.Prop: strokeMode = 4; break;
                }
            }
            // Та же кисть, что у мазков луга и полей (BrushScale), и те же оттенки сдвига.
            m.SetVector("_SpriteStrokes", new Vector4(strokeMode > 0 ? s5.SpriteStrokeStrength : 0f, strokeMode, Mathf.Max(0.3f, Settings.PainterlyStyle.BrushScale) * s5.SpriteStrokeLength, 0f));
            m.SetColor("_StrokeDry", palette.GrassDry);
            m.SetColor("_StrokeLush", palette.GrassLush);
            // R03: контурная трава — смешивание мягкой альфы, без записи глубины,
            // после обычных карточек (тонкие штрихи не рвутся порогом).
            bool inkGrass = stage05 && entry.Role == "grass_ink_tuft";
            m.SetFloat("_BlendAlpha", inkGrass ? 1f : 0f);
            m.SetFloat("_SrcBlend", inkGrass ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            m.SetFloat("_DstBlend", inkGrass ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", inkGrass ? 0f : 1f);
            if (inkGrass)
            {
                m.SetFloat("_Cutoff", 0.02f);
                m.SetFloat("_AlphaToMask", 0f);
                m.renderQueue = (int)RenderQueue.AlphaTest + 30;
            }
            m.SetVector("_DetailFade", stage05 && MapLabArtBuilder.IsDetail05(entry) ? new Vector4(s5.DetailFadePixels.x, Mathf.Max(s5.DetailFadePixels.x + 0.5f, s5.DetailFadePixels.y), 0f, 0f) : Vector4.zero);
            if (stage05)
                m.SetColor("_AccentInk", s5.InkColor);
        }

        private Material ModuleMaterial(MapLabArtEntry entry)
        {
            if (moduleMaterials.TryGetValue(entry, out Material material) && material != null)
                return material;
            material = Own(new Material(ModuleShader) { name = "MapLab Module " + entry.Id });
            moduleMaterials[entry] = material;
            ConfigureModuleMaterial(material, entry);
            return material;
        }

        private void ConfigureModuleMaterial(Material m, MapLabArtEntry entry)
        {
            MapLabStage05Settings s5 = Settings.Stage05;
            m.SetTexture("_MainTex", entry.Texture);
            m.SetFloat("_Cutoff", entry.Cutoff);
            m.SetTexture("_ShadeTex", shadeTexture != null ? shadeTexture : Texture2D.whiteTexture);
            m.SetFloat("_MapSize", data != null ? data.Size : Settings.General.MapSize);
            m.SetColor("_ShadowTint", Palette.ShadowTint);
            m.SetFloat("_GroundShade", Settings.Diagnostics.ShowReliefLight ? 0.5f : 0f);
            bool stone = entry.Id == MapLabModules05.Abutment;
            m.SetColor("_Tint", stone ? s5.CliffColor : s5.WoodColor);
            m.SetFloat("_TintStrength", 0.15f);
            m.SetVector("_LightDir", Settings.LightDirection);
            m.SetFloat("_Saturation", Settings.PainterlyStyle.Saturation);
            m.SetFloat("_AlphaToMask", s5.AlphaToCoverage ? 1f : 0f);
        }

        // Пересобрать только нарисованные объекты (после импорта или смены режима).
        public bool RebuildArt()
        {
            if (data == null || !Validate())
                return false;
            DestroyObjectGroups();
            foreach (Material m in artMaterials.Values)
            {
                owned.Remove(m);
                DestroySafe(m);
            }
            artMaterials.Clear();
            BuildObjects();
            ApplyMaterialSettings();
            ApplyVisibility();
            MapLabStats stats = data.Stats;
            CountStats(ref stats);
            data.Stats = stats;
            MapLabMarker marker = Marker;
            if (marker != null)
                marker.OnMapBuilt(this);
            return true;
        }

        // Тестовая полоса перед картой (ближе к камере): на светлой панели —
        // исходный PNG без оттенка и тени рядом с экземпляром «как в сцене»; на
        // тёмной — исходная альфа и результат AlphaClip. Возвращает центр полосы.
        public Vector3 BuildArtStrip(IList<MapLabArtEntry> entries)
        {
            DestroyGroup("ArtTestStrip");
            if (data == null || entries == null || entries.Count == 0)
                return Vector3.zero;
            GameObject group = Group("ArtTestStrip");
            float spacing = 0f;
            foreach (MapLabArtEntry e in entries)
                spacing = Mathf.Max(spacing, e.CardHeight * e.Aspect * e.SolidRect.width * 1.2f + 2f);
            float width = spacing * 2f * entries.Count + 6f;
            float x0 = data.Size * 0.5f - width * 0.5f;
            float y = data.MinHeight - 2f;
            float zLight = -18f, zDark = -48f;
            Material light = Own(new Material(LinesShader) { name = "MapLab Strip Light" });
            light.SetColor("_Color", Palette.Paper);
            Material dark = Own(new Material(LinesShader) { name = "MapLab Strip Dark" });
            dark.SetColor("_Color", Palette.ForestFloor);
            Mesh Panel(float z0, float z1)
            {
                Mesh m = new Mesh { name = "MapLab Strip Panel", hideFlags = HideFlags.DontSave };
                m.vertices = new[] { new Vector3(x0 - 4f, y, z0), new Vector3(x0 + width, y, z0), new Vector3(x0 + width, y, z1), new Vector3(x0 - 4f, y, z1) };
                m.triangles = new[] { 0, 3, 2, 0, 2, 1 };
                m.RecalculateBounds();
                return m;
            }
            AddRenderer(group, "Light Panel", Panel(zLight - 16f, zLight + 14f), light);
            AddRenderer(group, "Dark Panel", Panel(zDark - 16f, zLight - 16f), dark);
            for (int i = 0; i < entries.Count; i++)
            {
                MapLabArtEntry e = entries[i];
                float xa = x0 + spacing * (2 * i + 0.5f), xb = xa + spacing;
                AddStripCard(group, e, new Vector3(xa, y, zLight), 0f, MapLabAlphaView.Off, "Source");
                AddStripCard(group, e, new Vector3(xb, y, zLight), 1f, MapLabAlphaView.Off, "Scene");
                AddStripCard(group, e, new Vector3(xa, y, zDark), 0f, MapLabAlphaView.SourceAlpha, "Alpha");
                AddStripCard(group, e, new Vector3(xb, y, zDark), 0f, MapLabAlphaView.ClipResult, "Clip");
            }
            return new Vector3(data.Size * 0.5f, y, (zLight + zDark) * 0.5f);
        }

        private void AddStripCard(GameObject group, MapLabArtEntry entry, Vector3 basePoint, float sceneLook, MapLabAlphaView alphaView, string label)
        {
            Material m = Own(new Material(ArtCardShader) { name = $"MapLab Strip {entry.Id} {label}" });
            ConfigureArtMaterial(m, entry);
            m.SetFloat("_AlphaView", (float)alphaView);
            if (sceneLook < 0.5f)
            {
                m.SetFloat("_TintStrength", 0f);
                m.SetFloat("_GroundShade", 0f);
            }
            MapLabArtInstance inst = new MapLabArtInstance
            {
                Base = basePoint,
                Height = entry.CardHeight,
                Width = entry.CardHeight * entry.Aspect,
                Pivot = entry.Pivot,
                Brightness = 1f,
                Tint = sceneLook > 0.5f ? Settings.Art.PaletteTint : 0f,
                GroundShade = 0f,
            };
            List<Vector3> positions = new List<Vector3>();
            List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<int> indices = new List<int>();
            MapLabArtBuilder.AppendCard(inst, new Color(0.8f, inst.Tint, 0f, 1f), positions, uv0, uv1, colors, indices);
            Mesh mesh = new Mesh { name = $"MapLab Strip {entry.Id} {label}", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, false);
            mesh.bounds = new Bounds(basePoint, Vector3.one * (inst.Height * 4f + 8f));
            AddRenderer(group, entry.Id + " " + label, mesh, m);
        }

        public void ClearArtStrip()
        {
            DestroyGroup("ArtTestStrip");
        }

        // Камера сменила наклон или приближение: дома-карточки ↔ геометрия, кадр фишки.
        public void OnCameraChanged()
        {
            if (data == null)
                return;
            float pitch = CameraRig != null ? CameraRig.Pitch : 50f;
            if (Settings.IsStage03)
            {
                if (UpdateHouseView(pitch))
                    ApplyVisibility();
            }
            else if (Settings.Art.BuildingSpritesAt(pitch) != buildingSprites)
                ApplyVisibility();
            MapLabMarker marker = Marker;
            if (marker != null)
                marker.Refresh();
        }

        private void BuildWireframe()
        {
            if (linesMaterial == null)
                return;
            AddRenderer(Group("Wireframe"), "Wireframe", MapLabMeshes.BuildWireframe(data), linesMaterial);
        }

        // ---------- Дешёвые изменения ----------

        public void ApplyMaterialSettings()
        {
            if (data == null || Settings == null || terrainMaterial == null)
                return;
            MapLabStyleSettings style = Settings.PainterlyStyle;
            MapLabDiagnosticsSettings diag = Settings.Diagnostics;
            MapLabPalette palette = Palette;
            if (paletteTexture != null)
                MapLabArt.WritePalette(paletteTexture, palette);
            Vector4 light = Settings.LightDirection;
            Vector4 shadowTint = (Vector4)palette.ShadowTint;
            Vector4 lightTint = (Vector4)palette.LightTint;
            float outline = diag.ShowOutline ? style.Outline : 0f;
            float paper = diag.ShowPaper ? style.PaperGrain : 0f;
            float micro = diag.ShowStrokes ? style.MicroTexture : 0f;
            float mode = (float)diag.Mode;

            bool stage03 = Settings.IsStage03;
            MapLabStage03Settings s3 = Settings.Stage03;
            // Вклад мазков — отдельная перепечка краски в режиме разницы.
            if (stage03 && (diag.Mode == MapLabDiagnosticMode.StrokeContribution) != paintContribution)
                Repaint();

            Material t = terrainMaterial;
            t.SetTexture("_PaintTex", paintTexture != null ? paintTexture : (paintTiles.Count > 0 ? paintTiles[0] : Texture2D.grayTexture));
            t.SetVector("_PaintRect", new Vector4(0f, 0f, 1f / data.Size, 1f / data.Size));
            t.SetTexture("_ObjShadowTex", objShadowTexture != null ? objShadowTexture : Texture2D.blackTexture);
            bool shadowsOn = stage03 && s3.ObjectShadows && objShadowTexture != null && diag.ShowReliefLight;
            bool stage04 = Settings.IsStage04;
            MapLabStage04Settings s4 = Settings.Stage04;
            t.SetVector("_ObjShadow", new Vector4(shadowsOn ? s3.ShadowStrength : 0f, shadowsOn ? (stage04 ? s4.ContactDensity : s3.ContactStrength) : 0f, s3.ShadowTones, data.Size));
            t.SetVector("_Stage04", new Vector4(stage04 ? 1f : 0f, stage04 && diag.ShowPaper ? s4.Grain : 0f, 0f, 0f));
            bool stage05 = Settings.IsStage05;
            MapLabStage05Settings s5 = Settings.Stage05;
            t.SetColor("_ObjShadowColor", stage05 ? s5.ShadowColor : s3.ShadowColor);
            t.SetTexture("_ShadeTex", shadeTexture);
            t.SetTexture("_MaskA", maskATexture);
            t.SetTexture("_MaskB", maskBTexture);
            t.SetTexture("_DetailTex", detailTexture);
            t.SetVector("_LightDir", light);
            // Этап 05: тень рельефа на земле — тёплая оливково-серая (без серо-лиловых
            // пятен у подножия уступов), согласованная с тенью объектов.
            t.SetVector("_ShadowTint", stage05 ? (Vector4)Color.Lerp(palette.ShadowTint, new Color(0.6f, 0.6f, 0.5f, 1f), 0.65f) : shadowTint);
            t.SetVector("_LightTint", lightTint);
            t.SetColor("_InkColor", stage05 ? s5.InkColor : stage03 ? s3.InkStyle.Color : palette.Ink);
            t.SetColor("_PaperColor", palette.Paper);
            t.SetFloat("_LightStrength", style.LightStrength);
            t.SetFloat("_ShadowSoftness", style.ShadowSoftness);
            t.SetFloat("_ToneBands", style.ToneBands);
            t.SetFloat("_CastShadow", style.CastShadows);
            t.SetFloat("_AO", style.AmbientOcclusion);
            t.SetFloat("_Outline", outline);
            t.SetFloat("_Micro", micro);
            t.SetFloat("_Paper", paper);
            t.SetFloat("_BrushScale", Mathf.Max(0.2f, style.BrushScale));
            t.SetFloat("_Saturation", style.Saturation);
            t.SetFloat("_DebugMode", mode);
            t.SetFloat("_ShowPaint", diag.ShowPaint ? 1f : 0f);
            t.SetFloat("_ShowLight", diag.ShowReliefLight ? 1f : 0f);
            t.SetVector("_HeightRange", new Vector4(data.MinHeight, data.MaxHeight, 0f, 0f));
            t.SetFloat("_Exaggeration", style.ReliefExaggeration);

            Material w = waterMaterial;
            w.SetTexture("_HeightTex", heightTexture);
            w.SetTexture("_ShadeTex", shadeTexture);
            w.SetTexture("_WaterTex", waterTexture);
            w.SetTexture("_DetailTex", detailTexture);
            w.SetColor("_Deep", palette.WaterDeep);
            w.SetColor("_Shallow", palette.WaterShallow);
            w.SetColor("_Stroke", palette.WaterStroke);
            w.SetColor("_ShoreColor", palette.Shore);
            w.SetColor("_InkColor", stage03 ? s3.InkStyle.Color : palette.Ink);
            w.SetVector("_ShadowTint", shadowTint);
            w.SetFloat("_MapSize", data.Size);
            w.SetFloat("_StrokeAmount", diag.ShowStrokes ? Mathf.Lerp(0.25f, 0.75f, style.BrushStrength) : 0f);
            w.SetFloat("_CastShadow", style.CastShadows);
            w.SetFloat("_Outline", outline);
            w.SetFloat("_Paper", paper);
            w.SetFloat("_Saturation", style.Saturation);
            w.SetFloat("_DebugMode", mode);
            bool water05 = stage05 && shoreTexture != null;
            w.SetFloat("_Stage05", water05 ? 1f : 0f);
            w.SetFloat("_DepthPush", water05 ? 0.3f : 0f);
            w.SetColor("_Base", s5.WaterBase);
            w.SetColor("_WaterInk", s5.WaterInkColor);
            w.SetVector("_Strokes05", diag.ShowStrokes ? new Vector4(s5.RiverStrokes, s5.LakeStrokes, 0f, 0f) : Vector4.zero);
            if (water05)
            {
                float span = MapLabShore.Step * paintFields.Shore.N;
                w.SetTexture("_ShoreTex", shoreTexture);
                w.SetVector("_ShoreRect", new Vector4(-MapLabShore.Step * 0.5f, -MapLabShore.Step * 0.5f, 1f / span, 1f / span));
            }

            Material c = cardMaterial;
            c.SetTexture("_MainTex", atlasTexture);
            c.SetTexture("_PaletteTex", paletteTexture);
            c.SetTexture("_DetailTex", detailTexture);
            c.SetFloat("_CardLean", Settings.Advanced.CardLean);
            c.SetFloat("_LightStrength", diag.ShowReliefLight ? style.LightStrength : 0f);
            c.SetFloat("_Outline", outline);
            c.SetFloat("_Paper", paper);
            c.SetFloat("_Saturation", style.Saturation);

            Material s = solidMaterial;
            s.SetTexture("_PaletteTex", paletteTexture);
            s.SetTexture("_DetailTex", detailTexture);
            // Этап 03: верх уступов берёт обзорную копию тайлов (она покрывает все тайлы).
            s.SetTexture("_PaintTex", stage03 && overviewTexture != null ? overviewTexture : paintTexture);
            s.SetFloat("_MapSize", stage03 && overviewTexture != null ? tilesPerSide * tileWorld : data.Size);
            s.SetVector("_LightDir", light);
            s.SetVector("_ShadowTint", shadowTint);
            s.SetVector("_LightTint", lightTint);
            s.SetColor("_InkColor", stage05 ? s5.InkColor : stage04 ? s3.InkStyle.Color : palette.Ink);
            s.SetFloat("_Stage04", stage04 ? 1f : 0f);
            s.SetFloat("_Stage05", stage05 ? 1f : 0f);
            s.SetColor("_WoodColor", stage05 ? s5.WoodColor : s4.WoodColor);
            s.SetColor("_StoneColor", stage05 ? s5.CliffColor : s4.RockColor);
            s.SetFloat("_LightStrength", diag.ShowReliefLight ? style.LightStrength : 0f);
            s.SetFloat("_ShadowSoftness", style.ShadowSoftness);
            s.SetFloat("_ToneBands", style.ToneBands);
            s.SetFloat("_Outline", outline);
            s.SetFloat("_Paper", paper);
            s.SetFloat("_Saturation", style.Saturation);
            s.SetFloat("_DebugMode", mode);

            if (linesMaterial != null)
                linesMaterial.SetColor("_Color", new Color(0.12f, 0.12f, 0.14f, 1f));
            if (inkMaterial != null)
            {
                MapLabInkStyle ink = MapLabInk.Style(Settings);
                foreach (Material im in new[] { inkMaterial, shoreInkMaterial })
                {
                    if (im == null)
                        continue;
                    im.SetColor("_Color", ink.Color);
                    im.SetFloat("_Width", ink.Width);
                    im.SetFloat("_MinPx", ink.MinPixels);
                    im.SetFloat("_MaxPx", ink.MaxPixels);
                    im.SetFloat("_Opacity", diag.ShowOutline ? ink.Opacity : 0f);
                    im.SetFloat("_Grain", ink.Grain);
                    im.SetVector("_Drift", stage05 ? new Vector4(s5.InkThicknessDrift, 1f / Mathf.Max(1f, s5.InkDriftLength), 0f, 0f) : Vector4.zero);
                    im.SetFloat("_Split", 0f);
                }
                if (shoreInkMaterial != null)
                {
                    // Берег: к воде — синяя тушь, к суше — тёплая; чуть тоньше дороги.
                    shoreInkMaterial.SetFloat("_Split", 1f);
                    shoreInkMaterial.SetColor("_Color2", s5.WaterInkColor);
                    shoreInkMaterial.SetFloat("_Width", ink.Width * 0.85f);
                    shoreInkMaterial.SetFloat("_MinPx", ink.MinPixels * 0.85f);
                }
            }
            foreach (KeyValuePair<MapLabArtEntry, Material> pair in moduleMaterials)
                ConfigureModuleMaterial(pair.Value, pair.Key);
            foreach (KeyValuePair<MapLabArtEntry, Material> pair in cliffMaterials)
                ConfigureCliffMaterial(pair.Value, pair.Key);

            ApplyGroundTextures(t, s);
            foreach (KeyValuePair<MapLabArtEntry, Material> pair in artMaterials)
                ConfigureArtMaterial(pair.Value, pair.Key);
            if (CameraRig != null)
                CameraRig.Bind(this, false);
        }

        // Фактуры этапа 02: трава, земля, камень — если подключены и включены.
        private void ApplyGroundTextures(Material terrain, Material solid)
        {
            MapLabArtSet set = ResolvedArtSet;
            MapLabArtSettings art = Settings.Art;
            MapLabArtEntry grass = set != null ? set.Find("Ground_Grass_Color") : null;
            MapLabArtEntry earth = set != null ? set.Find("Ground_Earth_Color") : null;
            MapLabArtEntry rock = set != null ? set.Find("Rock_Color") : null;
            bool on = art.GroundTextures && Settings.Diagnostics.ShowPaint && grass != null && grass.HasTexture && earth != null && earth.HasTexture && rock != null && rock.HasTexture;
            terrain.SetFloat("_GroundStrength", on ? art.GroundStrength : 0f);
            terrain.SetVector("_GroundScales", new Vector4(art.GrassScale, art.EarthScale, art.RockScale, 0f));
            if (on)
            {
                terrain.SetTexture("_GrassTex", grass.Texture);
                terrain.SetTexture("_EarthTex", earth.Texture);
                terrain.SetTexture("_RockTex", rock.Texture);
                terrain.SetColor("_GrassMean", grass.MeanColor.linear);
                terrain.SetColor("_EarthMean", earth.MeanColor.linear);
                terrain.SetColor("_RockMean", rock.MeanColor.linear);
            }
            bool rockOn = rock != null && rock.HasTexture && Settings.Diagnostics.ShowPaint;
            solid.SetFloat("_RockStrength", rockOn ? art.RockColorStrength : 0f);
            if (rockOn)
            {
                solid.SetTexture("_RockTex", rock.Texture);
                solid.SetColor("_RockMean", rock.MeanColor.linear);
                solid.SetFloat("_RockScale", art.RockScale * 0.5f);
            }
        }

        public void ApplyVisibility()
        {
            if (Settings == null || root == null)
                return;
            MapLabDiagnosticsSettings d = Settings.Diagnostics;
            bool final = d.Mode == MapLabDiagnosticMode.Final || d.Mode == MapLabDiagnosticMode.Wireframe
                         || d.Mode == MapLabDiagnosticMode.ForestGroups || d.Mode == MapLabDiagnosticMode.ForestAccents;
            bool forest = d.ShowForest && (final || d.Mode == MapLabDiagnosticMode.ForestDensity);
            bool stage03 = Settings.IsStage03;
            float pitch = CameraRig != null ? CameraRig.Pitch : Settings.Camera.PitchFor(1f);
            if (stage03)
                UpdateHouseView(pitch);
            buildingSprites = Settings.IsStage05 ? groups.ContainsKey("ArtHouses05") : stage03 ? artHouses03 : Settings.Art.BuildingSpritesAt(pitch) && groups.ContainsKey("ArtHouses");
            SetActive("Water", d.ShowWater);
            SetActive("Vegetation", forest);
            SetActive("ArtVegetation", forest);
            SetActive("Structures", d.ShowBuildings && final);
            SetActive("Houses", d.ShowBuildings && final && !buildingSprites);
            SetActive("ArtHouses", d.ShowBuildings && final && buildingSprites && !stage03);
            SetActive("ArtHouses50", d.ShowBuildings && final && buildingSprites && houseView == 50);
            SetActive("ArtHouses35", d.ShowBuildings && final && buildingSprites && houseView == 35);
            // Этап 05: один ракурс дома при любом наклоне и приближении.
            SetActive("ArtHouses05", d.ShowBuildings && final);
            SetActive("ArtDecor05", final && d.ShowForest);
            SetActive("Modules05", d.ShowBuildings && final);
            SetActive("Ink", stage03 && Settings.Stage03.Ink && (final || d.Mode == MapLabDiagnosticMode.RoadGraph));
            SetActive("Grass", Settings.IsStage04 && !Settings.IsStage05 && final && d.ShowForest);
            SetActive("Boulders", d.ShowCliffs && final);
            SetActive("ArtBoulders", d.ShowCliffs && final);
            SetActive("ArtCliffDecor", d.ShowCliffs && final);
            SetActive("Cliffs", d.ShowCliffs && d.Mode != MapLabDiagnosticMode.Height && d.Mode != MapLabDiagnosticMode.Slope);
            SetActive("Wireframe", d.Mode == MapLabDiagnosticMode.Wireframe);
            MapLabMarker marker = Marker;
            if (marker != null && marker.View == this)
                marker.Refresh();
        }

        // Для замеров batch: временно скрыть или показать группу (например, перо).
        public void SetGroupVisible(string name, bool visible) => SetActive(name, visible);

        private void SetActive(string name, bool active)
        {
            if (groups.TryGetValue(name, out GameObject group) && group != null && group.activeSelf != active)
                group.SetActive(active);
        }

        private void CountStats(ref MapLabStats stats)
        {
            stats.Vertices = 0;
            stats.Triangles = 0;
            stats.Renderers = 0;
            HashSet<Material> materials = new HashSet<Material>();
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                stats.Renderers++;
                materials.Add(renderer.sharedMaterial);
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;
                stats.Vertices += filter.sharedMesh.vertexCount;
                if (filter.sharedMesh.GetTopology(0) == MeshTopology.Triangles)
                    stats.Triangles += (int)(filter.sharedMesh.GetIndexCount(0) / 3);
            }
            stats.Materials = materials.Count;
        }

        // После перезагрузки домена C#-ссылки на временные ресурсы теряются;
        // оставшийся корень с его мешами и материалами удаляем целиком.
        private void DestroyStaleRoot()
        {
            if (root == null)
            {
                Transform found = transform.Find(RootName);
                if (found != null)
                    root = found.gameObject;
            }
            if (root == null)
                return;
            HashSet<Object> garbage = new HashSet<Object>();
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material material = renderer.sharedMaterial;
                if (material != null && (material.hideFlags & HideFlags.DontSave) != 0)
                {
                    garbage.Add(material);
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        Texture texture = material.GetTexture(property);
                        if (texture != null && (texture.hideFlags & HideFlags.DontSave) != 0 && texture.name.StartsWith("MapLab"))
                            garbage.Add(texture);
                    }
                }
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && (filter.sharedMesh.hideFlags & HideFlags.DontSave) != 0)
                    garbage.Add(filter.sharedMesh);
            }
            DestroySafe(root);
            root = null;
            foreach (Object obj in garbage)
                DestroySafe(obj);
        }

        private static void DestroySafe(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }

        // ---------- Диагностика ----------

        // Граф дорог, русла, линии уступов и мосты — в Scene View (в экспорт не попадают).
        private void OnDrawGizmosSelected()
        {
            if (data == null)
                return;
            Gizmos.color = new Color(0.9f, 0.2f, 0.1f);
            foreach (MapLabPolyline road in data.Roads)
                DrawLine(road.Points, 0.4f);
            Gizmos.color = new Color(0.2f, 0.4f, 1f);
            foreach (MapLabRiver river in data.Rivers)
                DrawLine(river.Points, 0.3f);
            Gizmos.color = new Color(0.7f, 0.2f, 0.9f);
            foreach (MapLabCliffLine cliff in data.CliffLines)
                DrawLine(cliff.Points, 0.6f);
            Gizmos.color = Color.yellow;
            foreach (MapLabBridge bridge in data.Bridges)
                Gizmos.DrawLine(new Vector3(bridge.EndA.x, bridge.DeckHeight + 1f, bridge.EndA.y), new Vector3(bridge.EndB.x, bridge.DeckHeight + 1f, bridge.EndB.y));
        }

        private void DrawLine(List<Vector2> points, float lift)
        {
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a = points[i - 1], b = points[i];
                Gizmos.DrawLine(new Vector3(a.x, data.SampleHeight(a.x, a.y) + lift, a.y), new Vector3(b.x, data.SampleHeight(b.x, b.y) + lift, b.y));
            }
        }

        // Пересечение луча с картой высот — для диагностического клика.
        public bool Raycast(Ray ray, out Vector3 point)
        {
            point = default;
            if (data == null)
                return false;
            float top = data.MaxHeight + 2f;
            if (Mathf.Abs(ray.direction.y) < 1e-4f)
                return false;
            float t = (top - ray.origin.y) / ray.direction.y;
            if (t < 0f)
                t = 0f;
            Vector3 p = ray.origin + ray.direction * t;
            float step = data.Cell * 0.5f;
            for (int i = 0; i < 4000; i++)
            {
                if (data.Contains(p.x, p.z) && p.y <= data.SampleHeight(p.x, p.z))
                {
                    // Уточнение пересечения между последним шагом над землёй и первым под ней.
                    Vector3 above = p - ray.direction * step, below = p;
                    for (int k = 0; k < 12; k++)
                    {
                        Vector3 mid = (above + below) * 0.5f;
                        if (data.Contains(mid.x, mid.z) && mid.y <= data.SampleHeight(mid.x, mid.z))
                            below = mid;
                        else
                            above = mid;
                    }
                    point = new Vector3(below.x, data.SampleHeight(below.x, below.z), below.z);
                    return true;
                }
                if (p.y < data.MinHeight - 1f)
                    return false;
                p += ray.direction * step;
            }
            return false;
        }

        public string Describe(Vector3 point)
        {
            if (data == null)
                return "Карта не построена.";
            float x = point.x, z = point.z;
            string surface;
            if (data.IsUnderWater(x, z))
                surface = "вода";
            else if (data.SampleMask(data.Road, x, z) > 0.5f)
                surface = "дорога";
            else if (data.SampleMask(data.Cliff, x, z) > 0.4f)
                surface = "скальная стенка";
            else if (data.SampleMask(data.Rock, x, z) > 0.5f)
                surface = "камень";
            else if (data.SampleMask(data.Field, x, z) > 0.5f)
                surface = "поле";
            else if (data.SampleMask(data.Forest, x, z) > 0.4f)
                surface = "лес";
            else if (data.SampleMask(data.Shore, x, z) > 0.5f)
                surface = "берег";
            else if (data.SampleMask(data.Settlement, x, z) > 0.5f)
                surface = "поселение";
            else
                surface = "луг";
            return $"X {x:0.0}  Z {z:0.0}  высота {data.SampleHeight(x, z):0.00}  уклон {data.SampleSlope(x, z):0.0}°  — {surface}";
        }
    }
}
