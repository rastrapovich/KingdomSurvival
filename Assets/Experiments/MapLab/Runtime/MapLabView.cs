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

        [Header("Художественный набор этапа 02 (локальные PNG; без них — процедурный вид)")]
        public MapLabArtSet ArtSet;
        public MapLabMarkerSet MarkerSet;

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

        public MapLabData Data => data;
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
                return Mathf.Abs(CameraRig.Pitch - 50f) > 4f;
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
            artPlan = null;
            MapLabMarker marker = Marker;
            if (marker != null)
                marker.Clear();
            terrainMaterial = waterMaterial = cardMaterial = solidMaterial = linesMaterial = null;
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

        private void AddRenderer(GameObject parent, string name, Mesh mesh, Material material)
        {
            if (mesh == null)
                return;
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

        private void Repaint()
        {
            if (paletteTexture != null)
                MapLabArt.WritePalette(paletteTexture, Palette);
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
        }

        private void BuildTerrain()
        {
            GameObject group = Group("Terrain");
            List<Mesh> chunks = MapLabMeshes.BuildTerrainChunks(data, Settings.Advanced.ChunkVertices);
            for (int i = 0; i < chunks.Count; i++)
                AddRenderer(group, chunks[i].name, chunks[i], terrainMaterial);
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
            AddRenderer(group, "Cliff Walls", MapLabMeshes.BuildCliffs(data, Settings), solidMaterial);
        }

        private static readonly string[] ObjectGroups = { "Vegetation", "Structures", "Houses", "Boulders", "ArtVegetation", "ArtHouses", "ArtBoulders", "ArtCliffDecor" };

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
            MapLabArtSet set = ResolvedArtSet;
            if (set != null)
            {
                MapLabArtBuilder.PlanVegetation(data, Settings, set, artPlan);
                MapLabArtBuilder.PlanBoulders(data, Settings, set, artPlan);
                MapLabArtBuilder.PlanCliffDecor(data, Settings, set, Settings.Camera.AzimuthDegrees, artPlan);
                if (Settings.Art.Buildings != MapLabBuildingDisplay.Geometry)
                    MapLabArtBuilder.PlanBuildings(data, Settings, set, housePlan);
            }
            GameObject vegetation = Group("Vegetation");
            List<Mesh> cards = MapLabMeshes.BuildVegetationCards(data, 64f, artPlan.Replaced);
            for (int i = 0; i < cards.Count; i++)
                AddRenderer(vegetation, cards[i].name, cards[i], cardMaterial);
            HashSet<MapLabObjectKind> noHouses = new HashSet<MapLabObjectKind> { MapLabObjectKind.House, MapLabObjectKind.Barn };
            AddRenderer(Group("Structures"), "Structures", MapLabMeshes.BuildStructures(data, Settings, noHouses), solidMaterial);
            AddRenderer(Group("Houses"), "Houses", MapLabMeshes.BuildHouses(data), solidMaterial);
            AddRenderer(Group("Boulders"), "Boulders", MapLabMeshes.BuildBoulders(data, artPlan.Replaced), solidMaterial);
            BuildArtGroup("ArtVegetation", artPlan, e => e.Category == MapLabArtCategory.Vegetation);
            BuildArtGroup("ArtBoulders", artPlan, e => e.Category == MapLabArtCategory.Rock);
            BuildArtGroup("ArtCliffDecor", artPlan, e => e.Category == MapLabArtCategory.Cliff);
            BuildArtGroup("ArtHouses", housePlan, e => e.Category == MapLabArtCategory.Building);
            foreach (KeyValuePair<MapLabArtEntry, List<MapLabArtInstance>> pair in housePlan.Cards)
                foreach (MapLabArtInstance inst in pair.Value)
                    artPlan.Add(pair.Key, inst);
            foreach (MapLabObjectKind kind in housePlan.Replaced)
                artPlan.Replaced.Add(kind);
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
            bool sprites = Settings.Art.BuildingSpritesAt(CameraRig != null ? CameraRig.Pitch : 50f);
            if (sprites != buildingSprites)
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

            Material t = terrainMaterial;
            t.SetTexture("_PaintTex", paintTexture);
            t.SetTexture("_ShadeTex", shadeTexture);
            t.SetTexture("_MaskA", maskATexture);
            t.SetTexture("_MaskB", maskBTexture);
            t.SetTexture("_DetailTex", detailTexture);
            t.SetVector("_LightDir", light);
            t.SetVector("_ShadowTint", shadowTint);
            t.SetVector("_LightTint", lightTint);
            t.SetColor("_InkColor", palette.Ink);
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
            w.SetColor("_InkColor", palette.Ink);
            w.SetVector("_ShadowTint", shadowTint);
            w.SetFloat("_MapSize", data.Size);
            w.SetFloat("_StrokeAmount", diag.ShowStrokes ? Mathf.Lerp(0.25f, 0.75f, style.BrushStrength) : 0f);
            w.SetFloat("_CastShadow", style.CastShadows);
            w.SetFloat("_Outline", outline);
            w.SetFloat("_Paper", paper);
            w.SetFloat("_Saturation", style.Saturation);
            w.SetFloat("_DebugMode", mode);

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
            s.SetTexture("_PaintTex", paintTexture);
            s.SetFloat("_MapSize", data.Size);
            s.SetVector("_LightDir", light);
            s.SetVector("_ShadowTint", shadowTint);
            s.SetVector("_LightTint", lightTint);
            s.SetColor("_InkColor", palette.Ink);
            s.SetFloat("_LightStrength", diag.ShowReliefLight ? style.LightStrength : 0f);
            s.SetFloat("_ShadowSoftness", style.ShadowSoftness);
            s.SetFloat("_ToneBands", style.ToneBands);
            s.SetFloat("_Outline", outline);
            s.SetFloat("_Paper", paper);
            s.SetFloat("_Saturation", style.Saturation);
            s.SetFloat("_DebugMode", mode);

            if (linesMaterial != null)
                linesMaterial.SetColor("_Color", new Color(0.12f, 0.12f, 0.14f, 1f));

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
            bool final = d.Mode == MapLabDiagnosticMode.Final || d.Mode == MapLabDiagnosticMode.Wireframe;
            bool forest = d.ShowForest && (final || d.Mode == MapLabDiagnosticMode.ForestDensity);
            buildingSprites = Settings.Art.BuildingSpritesAt(CameraRig != null ? CameraRig.Pitch : Settings.Camera.PitchFor(1f)) && groups.ContainsKey("ArtHouses");
            SetActive("Water", d.ShowWater);
            SetActive("Vegetation", forest);
            SetActive("ArtVegetation", forest);
            SetActive("Structures", d.ShowBuildings && final);
            SetActive("Houses", d.ShowBuildings && final && !buildingSprites);
            SetActive("ArtHouses", d.ShowBuildings && final && buildingSprites);
            SetActive("Boulders", d.ShowCliffs && final);
            SetActive("ArtBoulders", d.ShowCliffs && final);
            SetActive("ArtCliffDecor", d.ShowCliffs && final);
            SetActive("Cliffs", d.ShowCliffs && d.Mode != MapLabDiagnosticMode.Height && d.Mode != MapLabDiagnosticMode.Slope);
            SetActive("Wireframe", d.Mode == MapLabDiagnosticMode.Wireframe);
            MapLabMarker marker = Marker;
            if (marker != null && marker.View == this)
                marker.Refresh();
        }

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
