using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Виды карты (17.11.2026): один и тот же мир карты — рельеф, высоты,
    // дороги, леса, деревни — под разной «шкурой». Вид меняет только облик:
    //   1) общая цветокоррекция всего рисунка (Grade) — мгновенно, без
    //      пересборки: земля, мазки, тушь, вода, деревья, дома, фигуры;
    //   2) необязательная своя палитра среды — перекраска земли (пересборка);
    //   3) необязательные свои рисунки по ID ассета карты — дом, дерево, мост…
    //      в манере вида (пересборка). Нет своего рисунка — берётся основной
    //      рисунок под цветокоррекцией вида.
    // Видов сколько угодно (заложено расширение до 5 и больше); первый —
    // прежний облик карты. Выбор вида — настройка показа (PlayerPrefs), а не
    // данные партии: сохранения от вида не зависят. Рабочие настройки облика,
    // не канон.
    [CreateAssetMenu(menuName = "Kingdom Survival/MapLab/Виды карты", fileName = "MapLabMapViews")]
    public sealed class MapLabMapViews : ScriptableObject
    {
        public const string ResourcesPath = "MapLabMapViews";
        public const string AssetPath = "Assets/_Project/MapLabBridge/Resources/MapLabMapViews.asset";
        public const string PrefsKey = "KingdomSurvival.MapView";
        // Папка своих рисунков вида: <ArtRoot>/<ID вида>/<ID ассета>.png.
        public const string ArtRoot = "Assets/_Project/Art/MapViews";
        // Облака тумана войны вида: <FogCloudsRoot>/<ID вида>/*.png.
        public const string FogCloudsRoot = "Assets/_Project/Art/MapFogClouds";

        public const string ClassicId = "classic";
        public const string GloomId = "gloom";
        public const string SepiaId = "sepia";

        public List<MapLabMapView> Views = new List<MapLabMapView>();

        // Сменился выбранный вид (игра и окно генератора).
        public static event Action ActiveChanged;

        private static MapLabMapViews loaded;

        // Ассет из Resources; без ассета — встроенные виды в памяти (игра
        // работает и до того, как автор сохранил виды).
        public static MapLabMapViews Load()
        {
            if (loaded == null)
            {
                loaded = Resources.Load<MapLabMapViews>(ResourcesPath);
                if (loaded == null)
                {
                    loaded = CreateInstance<MapLabMapViews>();
                    loaded.name = "MapLabMapViews (встроенные)";
                    loaded.hideFlags = HideFlags.DontSave;
                }
                loaded.EnsureBuiltIn();
            }
            return loaded;
        }

        // Для тестов и окна генератора после создания ассета.
        public static void ResetCache()
        {
            loaded = null;
        }

        // Включённые виды по порядку списка (кнопка переключения идёт по ним).
        public List<MapLabMapView> EnabledViews()
        {
            List<MapLabMapView> result = new List<MapLabMapView>();
            foreach (MapLabMapView v in Views)
                if (v != null && v.Enabled && !string.IsNullOrEmpty(v.Id))
                    result.Add(v);
            return result;
        }

        public MapLabMapView Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            foreach (MapLabMapView v in Views)
                if (v != null && v.Id == id)
                    return v;
            return null;
        }

        // ---------- выбранный вид ----------

        private static string activeId;

        public static string ActiveId
        {
            get
            {
                if (activeId == null)
                {
                    try
                    {
                        activeId = PlayerPrefs.GetString(PrefsKey, ClassicId);
                    }
                    catch (Exception)
                    {
                        activeId = ClassicId;
                    }
                }
                return activeId;
            }
            set
            {
                string next = string.IsNullOrEmpty(value) ? ClassicId : value;
                if (next == ActiveId)
                    return;
                activeId = next;
                try
                {
                    PlayerPrefs.SetString(PrefsKey, next);
                    PlayerPrefs.Save();
                }
                catch (Exception)
                {
                    // Выбор остаётся на сеанс.
                }
                ActiveChanged?.Invoke();
            }
        }

        // Выбранный вид; выключенный или удалённый — первый включённый.
        public static MapLabMapView Active
        {
            get
            {
                MapLabMapViews views = Load();
                MapLabMapView v = views.Find(ActiveId);
                if (v != null && v.Enabled)
                    return v;
                List<MapLabMapView> enabled = views.EnabledViews();
                return enabled.Count > 0 ? enabled[0] : null;
            }
        }

        // Следующий включённый вид по кругу (кнопка «Вид карты» в игре).
        public static MapLabMapView Cycle(int step = 1)
        {
            List<MapLabMapView> enabled = Load().EnabledViews();
            if (enabled.Count == 0)
                return null;
            MapLabMapView current = Active;
            int index = Mathf.Max(0, enabled.IndexOf(current));
            MapLabMapView next = enabled[((index + step) % enabled.Count + enabled.Count) % enabled.Count];
            ActiveId = next.Id;
            return next;
        }

        // Ключ того, что требует пересборки мира карты (палитра и свои
        // рисунки). Цветокоррекция в ключ не входит — она мгновенная.
        public static string BuildKey(MapLabMapView view) => view != null ? view.BuildKey : string.Empty;

        // ---------- встроенные виды ----------

        // Версия набора встроенных видов: ассет, собранный раньше, получает
        // недостающие встроенные виды (правки автора не трогаются).
        // 2 — тёплый свет земли (мрачный) и тушь вида (сепия).
        // 3 — мягкая гамма облаков тумана (меньше контраст и насыщенность).
        public const int CurrentBuiltInVersion = 3;
        [SerializeField] private int builtInVersion;

        public bool EnsureBuiltIn()
        {
            Views ??= new List<MapLabMapView>();
            if (builtInVersion >= CurrentBuiltInVersion && Views.Count > 0)
                return false;
            foreach (MapLabMapView fallback in CreateBuiltIn())
            {
                MapLabMapView existing = Find(fallback.Id);
                if (existing == null)
                {
                    Views.Add(fallback);
                    continue;
                }
                // Версия 2: новые поля встроенных видов (у сохранённых раньше —
                // нули); остальные правки автора не трогаются.
                if (builtInVersion < 2 && existing.Grade != null && fallback.Grade != null)
                {
                    MapLabViewGrade g = existing.Grade, d = fallback.Grade;
                    g.GroundGlow = d.GroundGlow;
                    g.GroundGlowColor = d.GroundGlowColor;
                    g.GroundGlowRelief = d.GroundGlowRelief;
                    g.GroundGlowStrokes = d.GroundGlowStrokes;
                    g.InkStrength = d.InkStrength;
                    g.InkColor = d.InkColor;
                    g.InkDensity = d.InkDensity;
                }
                // Версия 3: облака тумана в гамме карты — только если автор
                // их цвет ещё не трогал.
                if (builtInVersion < 3 && existing.FogCloudAdjustNeutral)
                    existing.CopyFogCloudAdjust(fallback);
            }
            builtInVersion = CurrentBuiltInVersion;
            return true;
        }

        public static List<MapLabMapView> CreateBuiltIn() => new List<MapLabMapView> { Classic(), Gloom(), Sepia() };

        // 1. Прежний облик карты — без коррекции, палитры и подмен.
        public static MapLabMapView Classic() => new MapLabMapView
        {
            Id = ClassicId,
            Name = "Обычный",
            Note = "Прежний облик карты: живописная акварель с тушью, палитра профиля MapLab.",
            Grade = MapLabViewGrade.Neutral(),
            // Облака мягче рисунка карты: светлая тушь и приглушённые света.
            FogCloudBrightness = 0.95f,
            FogCloudSaturation = 0.8f,
            FogCloudLevels = new MapLabLevels { Gamma = 0.95f, OutputBlack = 40f, OutputWhite = 222f },
        };

        // 2. Мрачный: сумеречный лес, приглушённые холодные тона, тёплые окна.
        public static MapLabMapView Gloom() => new MapLabMapView
        {
            Id = GloomId,
            Name = "Мрачный",
            Note = "Сумерки тёмного фэнтези: приглушённые сизо-лиловые тени, оливково-бурая земля, тёплые огни окон и фонарей.",
            Grade = new MapLabViewGrade
            {
                Strength = 1f,
                Brightness = 0.42f,
                Contrast = 1.2f,
                Saturation = 0.7f,
                GradientMix = 0.55f,
                Shadows = MapLabPalette.Hex(0x0C0A12),
                Midtones = MapLabPalette.Hex(0x37302E),
                Highlights = MapLabPalette.Hex(0x8E7864),
                Tint = MapLabPalette.Hex(0xA99BC2),
                TintStrength = 0.5f,
                KeepWarmLights = 0.8f,
                ShadowLift = 0.1f,
                GroundGlow = 0.6f,
                GroundGlowColor = MapLabPalette.Hex(0xD6A55E),
                GroundGlowRelief = 0.35f,
                GroundGlowStrokes = 1.3f,
            },
            OverrideFog = true,
            FogColor = new Color(0.07f, 0.06f, 0.09f, 0.9f),
            // Сумеречные облака: темнее и спокойнее, без светлых бликов.
            FogCloudBrightness = 0.82f,
            FogCloudSaturation = 0.75f,
            FogCloudLevels = new MapLabLevels { Gamma = 0.9f, OutputBlack = 22f, OutputWhite = 196f },
            UiTint = new Color(0.62f, 0.58f, 0.62f, 1f),
            Background = MapLabPalette.Hex(0x1E1B22),
        };

        // 3. Сепия: старая рисованная карта на пергаменте, коричневая тушь.
        public static MapLabMapView Sepia() => new MapLabMapView
        {
            Id = SepiaId,
            Name = "Сепия",
            Note = "Рисованная карта на пергаменте: вся гамма — оттенки сепии, коричневая тушь, светлая бумага.",
            Grade = new MapLabViewGrade
            {
                Strength = 1f,
                Brightness = 1f,
                Contrast = 1.3f,
                Saturation = 0f,
                GradientMix = 1f,
                Shadows = MapLabPalette.Hex(0x2B160A),
                Midtones = MapLabPalette.Hex(0xA27A4E),
                Highlights = MapLabPalette.Hex(0xF0E0BE),
                Tint = Color.white,
                TintStrength = 0f,
                KeepWarmLights = 0f,
                ShadowLift = 0.05f,
                InkStrength = 1f,
                InkColor = MapLabPalette.Hex(0x3A2412),
                InkDensity = 1.8f,
            },
            OverrideFog = true,
            FogColor = new Color(0.36f, 0.27f, 0.18f, 0.88f),
            // Облака пергамента: тушь светлее, кремовый приглушён к бумаге.
            FogCloudBrightness = 0.93f,
            FogCloudSaturation = 0.7f,
            FogCloudLevels = new MapLabLevels { Gamma = 0.95f, OutputBlack = 52f, OutputWhite = 214f },
            UiTint = new Color(0.86f, 0.74f, 0.56f, 1f),
            Background = MapLabPalette.Hex(0xE9D9B8),
        };
    }

    // Один вид карты.
    [Serializable]
    public sealed class MapLabMapView
    {
        [Tooltip("Стабильный ID: по нему игра помнит выбор, по нему — папка своих рисунков вида.")]
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name = "Новый вид";
        [TextArea(1, 4)] public string Note = string.Empty;
        [Tooltip("Вид доступен в игре и генераторе (кнопка переключения идёт по включённым).")]
        public bool Enabled = true;

        [Tooltip("Общая цветокоррекция всего рисунка карты — мгновенно, без пересборки.")]
        public MapLabViewGrade Grade = MapLabViewGrade.Neutral();

        [Tooltip("Своя палитра среды (трава, поля, дороги, вода, тушь, бумага). Пусто — палитра профиля. Смена — пересборка карты.")]
        public MapLabPalette Palette;

        [Tooltip("Свой цвет тумана войны и полей за краем карты в игре.")]
        public bool OverrideFog;
        public Color FogColor = new Color(0.055f, 0.06f, 0.07f, 0.86f);
        [Tooltip("Подкраска картинок, которые игра рисует поверх карты интерфейсом (скрытые места), — чтобы они не выбивались из вида.")]
        public Color UiTint = Color.white;
        [Tooltip("Цвет фона под картой (пока карта рисуется и за краем полотна).")]
        public Color Background = new Color(0.86f, 0.82f, 0.72f, 1f);

        [Tooltip("Облака тумана войны в манере вида: отдельные PNG с прозрачностью, рисуются над маской неисследованного без цветокоррекции. Пусто — облака вида «Обычный».")]
        public List<Texture2D> FogClouds = new List<Texture2D>();
        [Tooltip("Коррекция облаков вида: яркость (1 — без изменений).")]
        [Range(0.3f, 2f)] public float FogCloudBrightness = 1f;
        [Tooltip("Поворот цветового тона облаков, градусов.")]
        [Range(-180f, 180f)] public float FogCloudHue;
        [Tooltip("Насыщенность облаков (1 — без изменений).")]
        [Range(0f, 2f)] public float FogCloudSaturation = 1f;
        [Tooltip("Уровни облаков, как в Photoshop.")]
        public MapLabLevels FogCloudLevels = new MapLabLevels();

        public bool FogCloudAdjustNeutral => MapLabColorMath.IsNeutral(FogCloudBrightness, FogCloudHue, FogCloudSaturation, FogCloudLevels);

        public void CopyFogCloudAdjust(MapLabMapView other)
        {
            FogCloudBrightness = other.FogCloudBrightness;
            FogCloudHue = other.FogCloudHue;
            FogCloudSaturation = other.FogCloudSaturation;
            (FogCloudLevels ??= new MapLabLevels()).CopyFrom(other.FogCloudLevels);
        }

        public string FogCloudAdjustKey => FogCloudAdjustNeutral ? "-" :
            FogCloudBrightness.ToString("0.###") + "/" + FogCloudHue.ToString("0.##") + "/" + FogCloudSaturation.ToString("0.###") + "/" + (FogCloudLevels != null ? FogCloudLevels.Key : "-");

        [Tooltip("Свои рисунки вида нарисованы уже в его гамме: цветокоррекция вида их не трогает (земля, мазки и тушь — под коррекцией).")]
        public bool OwnArtUngraded = true;

        [Tooltip("Свои рисунки вида: ID ассета карты → PNG в манере вида. Собираются из папки Assets/_Project/Art/MapViews/<ID вида>/ кнопкой в генераторе карты.")]
        public List<MapLabViewArtOverride> Art = new List<MapLabViewArtOverride>();

        [NonSerialized] private Dictionary<string, Texture2D> index;
        [NonSerialized] private int indexCount = -1;

        public string ArtFolder => MapLabMapViews.ArtRoot + "/" + Id;

        // Папка облаков тумана вида: <FogCloudsRoot>/<ID вида>/*.png.
        public string FogCloudsFolder => MapLabMapViews.FogCloudsRoot + "/" + Id;

        public int FogCloudCount
        {
            get
            {
                int n = 0;
                if (FogClouds != null)
                    foreach (Texture2D t in FogClouds)
                        if (t != null)
                            n++;
                return n;
            }
        }

        // Имя PNG в папке вида ↔ ID ассета: у ассетов Базы ассетов ID «art:…»,
        // двоеточие в имени файла Windows не допускает — в файле «art_…».
        public static string FileNameOf(string artId) => artId != null && artId.StartsWith("art:") ? "art_" + artId.Substring(4) : artId;

        public static string IdOfFile(string fileName) => fileName != null && fileName.StartsWith("art_") ? "art:" + fileName.Substring(4) : fileName;

        // Свой рисунок вида для ID ассета; null — своего нет.
        public Texture2D FindTexture(string artId)
        {
            if (string.IsNullOrEmpty(artId) || Art == null || Art.Count == 0)
                return null;
            if (index == null || indexCount != Art.Count)
            {
                index = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
                foreach (MapLabViewArtOverride o in Art)
                    if (o != null && o.Enabled && o.Texture != null && !string.IsNullOrEmpty(o.ArtId))
                        index[o.ArtId] = o.Texture;
                indexCount = Art.Count;
            }
            return index.TryGetValue(artId, out Texture2D t) ? t : null;
        }

        // Свой рисунок для записи каталога — только при тех же пропорциях холста
        // (±3 %): у одного ID бывают записи с разными холстами (этапы 03 и 05),
        // и рисунок, нарисованный под другой холст, встал бы искажённым.
        public Texture2D FindTexture(MapLabArtEntry entry, out bool skewed)
        {
            skewed = false;
            Texture2D t = entry != null ? FindTexture(entry.Id) : null;
            // Копия записи с другим размером ("art:…#block") — рисунок основной записи.
            int hash = entry != null && t == null && entry.Id != null ? entry.Id.IndexOf('#') : -1;
            if (hash > 0)
                t = FindTexture(entry.Id.Substring(0, hash));
            if (t == null || entry.Width <= 0 || entry.Height <= 0 || t.height <= 0)
                return t;
            float want = entry.Width / (float)entry.Height, have = t.width / (float)t.height;
            skewed = Mathf.Abs(have - want) > want * 0.03f;
            return skewed ? null : t;
        }

        // Сбросить индекс после правки списка (окно генератора).
        public void InvalidateIndex() => index = null;

        public int ArtCount
        {
            get
            {
                int n = 0;
                if (Art != null)
                    foreach (MapLabViewArtOverride o in Art)
                        if (o != null && o.Enabled && o.Texture != null)
                            n++;
                return n;
            }
        }

        // Что требует пересборки мира: палитра и набор своих рисунков.
        public string BuildKey
        {
            get
            {
                uint h = 2166136261;
                void Mix(string s)
                {
                    foreach (char c in s ?? string.Empty)
                        h = unchecked((h ^ c) * 16777619);
                }
                Mix(Palette != null ? Palette.name : "-");
                Mix(OwnArtUngraded ? "u" : "g");
                // Одинаковые имена PNG у разных видов — разные рисунки: ID вида в ключе.
                if (Palette != null || ArtCount > 0)
                    Mix("#" + Id);
                if (Art != null)
                    foreach (MapLabViewArtOverride o in Art)
                        if (o != null && o.Enabled && o.Texture != null)
                            Mix(o.ArtId + "=" + o.Texture.name + ":" + o.Texture.width + "x" + o.Texture.height + ";");
                return h.ToString("x8");
            }
        }

        public MapLabMapView Clone()
        {
            MapLabMapView copy = (MapLabMapView)MemberwiseClone();
            copy.Grade = Grade != null ? Grade.Clone() : MapLabViewGrade.Neutral();
            copy.Art = new List<MapLabViewArtOverride>();
            if (Art != null)
                foreach (MapLabViewArtOverride o in Art)
                    if (o != null)
                        copy.Art.Add(new MapLabViewArtOverride { ArtId = o.ArtId, Texture = o.Texture, Enabled = o.Enabled });
            copy.FogClouds = FogClouds != null ? new List<Texture2D>(FogClouds) : new List<Texture2D>();
            copy.FogCloudLevels = FogCloudLevels != null ? FogCloudLevels.Clone() : new MapLabLevels();
            copy.index = null;
            return copy;
        }
    }

    // Свой рисунок вида для одного ассета карты.
    [Serializable]
    public sealed class MapLabViewArtOverride
    {
        [Tooltip("ID ассета карты (как в наборах арта MapLab: House_Thatch_A_P35, Map05_Well_Roofed_A…).")]
        public string ArtId;
        public Texture2D Texture;
        public bool Enabled = true;
    }

    // Общая цветокоррекция рисунка карты (шейдерный глобал, см. MapLabGrade.hlsl).
    [Serializable]
    public sealed class MapLabViewGrade
    {
        [Tooltip("Сила вида: 0 — исходный рисунок.")]
        [Range(0f, 1f)] public float Strength;
        [Range(0.2f, 2f)] public float Brightness = 1f;
        [Range(0.3f, 2f)] public float Contrast = 1f;
        [Range(0f, 2f)] public float Saturation = 1f;
        [Tooltip("Градиентная карта: светлота пикселя → тень / середина / свет вида (1 — чистая сепия).")]
        [Range(0f, 1f)] public float GradientMix;
        public Color Shadows = Color.black;
        public Color Midtones = Color.gray;
        public Color Highlights = Color.white;
        [Tooltip("Подкраска умножением.")]
        public Color Tint = Color.white;
        [Range(0f, 1f)] public float TintStrength;
        [Tooltip("Тёплые яркие пятна (окна, фонари, огонь) сохраняют свой цвет.")]
        [Range(0f, 1f)] public float KeepWarmLights;
        [Tooltip("Подъём самых тёмных тонов (рисунок не проваливается в черноту).")]
        [Range(0f, 1f)] public float ShadowLift;

        [Header("Тёплый свет на земле")]
        [Tooltip("Сила тёплых бликов на земле — как подсветка крон мрачного вида. 0 — нет.")]
        [Range(0f, 1f)] public float GroundGlow;
        public Color GroundGlowColor = new Color(0.85f, 0.66f, 0.36f, 1f);
        [Tooltip("Блики на склонах, повёрнутых к свету.")]
        [Range(0f, 2f)] public float GroundGlowRelief = 1f;
        [Tooltip("Блики на светлых мазках покраски.")]
        [Range(0f, 2f)] public float GroundGlowStrokes = 0.7f;

        [Header("Тушь вида")]
        [Tooltip("Насколько контуры (дороги, берега, межи) берут цвет туши вида. 0 — как есть.")]
        [Range(0f, 1f)] public float InkStrength;
        public Color InkColor = new Color(0.23f, 0.14f, 0.07f, 1f);
        [Tooltip("Плотность туши: 1 — как есть, больше — контуры темнее и плотнее.")]
        [Range(1f, 3f)] public float InkDensity = 1f;

        public bool IsNeutral => Strength <= 0f;

        public MapLabViewGrade Clone() => (MapLabViewGrade)MemberwiseClone();

        public static MapLabViewGrade Neutral() => new MapLabViewGrade { Strength = 0f };
    }

    // Глобальные значения шейдеров вида карты. Ставятся только на время
    // рендера карты и снимаются (Neutral) сразу после.
    public static class MapLabViewGradeShader
    {
        private static readonly int GradeId = Shader.PropertyToID("_MapLabGrade");
        private static readonly int MapId = Shader.PropertyToID("_MapLabGradeMap");
        private static readonly int ShadowsId = Shader.PropertyToID("_MapLabGradeShadows");
        private static readonly int MidsId = Shader.PropertyToID("_MapLabGradeMids");
        private static readonly int LightsId = Shader.PropertyToID("_MapLabGradeLights");
        private static readonly int TintId = Shader.PropertyToID("_MapLabGradeTint");
        private static readonly int GlowId = Shader.PropertyToID("_MapLabGroundGlow");
        private static readonly int GlowShapeId = Shader.PropertyToID("_MapLabGroundGlowShape");
        private static readonly int InkId = Shader.PropertyToID("_MapLabViewInk");
        private static readonly int InkShapeId = Shader.PropertyToID("_MapLabViewInkShape");

        public static void Apply(MapLabMapView view) => Apply(view != null ? view.Grade : null);

        public static void Apply(MapLabViewGrade g)
        {
            if (g == null || g.IsNeutral)
            {
                Neutral();
                return;
            }
            Shader.SetGlobalVector(GradeId, new Vector4(Mathf.Clamp01(g.Strength), g.Brightness, g.Contrast, g.Saturation));
            Shader.SetGlobalVector(MapId, new Vector4(g.GradientMix, g.TintStrength, g.KeepWarmLights, g.ShadowLift));
            // SetGlobalColor переводит sRGB-цвет в линейное пространство проекта.
            Shader.SetGlobalColor(ShadowsId, g.Shadows);
            Shader.SetGlobalColor(MidsId, g.Midtones);
            Shader.SetGlobalColor(LightsId, g.Highlights);
            Shader.SetGlobalColor(TintId, g.Tint);
            // Цвет — в линейное пространство проекта, сила — как есть.
            Color glow = g.GroundGlowColor.linear;
            Shader.SetGlobalVector(GlowId, new Vector4(glow.r, glow.g, glow.b, Mathf.Clamp01(g.GroundGlow)));
            Shader.SetGlobalVector(GlowShapeId, new Vector4(g.GroundGlowRelief, g.GroundGlowStrokes, 0f, 0f));
            Color ink = g.InkColor.linear;
            Shader.SetGlobalVector(InkId, new Vector4(ink.r, ink.g, ink.b, Mathf.Clamp01(g.InkStrength)));
            Shader.SetGlobalVector(InkShapeId, new Vector4(Mathf.Max(1f, g.InkDensity), 0f, 0f, 0f));
        }

        public static void Neutral() => Shader.SetGlobalVector(GradeId, Vector4.zero);
    }
}
