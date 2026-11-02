using System;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    public enum MapLabPresetKind
    {
        HillAndLedge = 0,
        ForestLake = 1,
        RiverValley = 2,
    }

    public enum MapLabQuality
    {
        Samples129 = 129,
        Samples257 = 257,
        Samples513 = 513,
    }

    public enum MapLabHeightSource
    {
        Procedural = 0,
        ExternalHeightmap = 1,
        ProceduralPlusHeightmap = 2,
    }

    public enum MapLabDiagnosticMode
    {
        Final = 0,
        Height = 1,
        Slope = 2,
        Normals = 3,
        MaterialMasks = 4,
        ForestDensity = 5,
        WaterAndBanks = 6,
        RoadGraph = 7,
        CliffLines = 8,
        Wireframe = 9,
    }

    // Именованные режимы камеры этапа 02 (наклон вниз от горизонта).
    public enum MapLabCameraMode
    {
        Custom = 0,
        Atlas60 = 1,
        Volume50 = 2,
        Diorama40 = 3,
    }

    public enum MapLabArtMode
    {
        Procedural = 0,
        Art = 1,
    }

    // Постройки: геометрия, нарисованная карточка или карточка только при 50°.
    public enum MapLabBuildingDisplay
    {
        Geometry = 0,
        SpriteDecorative = 1,
        AutoByPitch = 2,
    }

    public enum MapLabAlphaView
    {
        Off = 0,
        SourceAlpha = 1,
        ClipResult = 2,
    }

    [Serializable]
    public sealed class MapLabGeneralSettings
    {
        [Tooltip("Композиционный пресет: задаёт контрольные точки холмов, воды, уступов, дорог и поселения.")]
        public MapLabPresetKind Preset = MapLabPresetKind.HillAndLedge;
        [Tooltip("Seed партии. Меняет формы в пределах пресета, расстановку и варианты деталей.")]
        public int Seed = 104;
        [Tooltip("Сторона участка в условных единицах (XZ). 256 — охват эталонных картинок.")]
        [Range(96f, 512f)] public float MapSize = 256f;
        [Tooltip("Число отсчётов высоты по стороне всей карты. Поверхность делится на чанки до 129 × 129 вершин.")]
        public MapLabQuality Quality = MapLabQuality.Samples257;
        [Tooltip("Разрешение живописной текстуры поверхности, пикселей по стороне.")]
        public int PaintResolution = 2048;
    }

    [Serializable]
    public sealed class MapLabReliefSettings
    {
        [Tooltip("Источник высот: процедурная основа, внешняя grayscale-карта или их сумма.")]
        public MapLabHeightSource Source = MapLabHeightSource.Procedural;
        [Tooltip("Наибольшая высота рельефа, ед. (вершины холмов без уступов).")]
        [Range(2f, 40f)] public float HeightRange = 12f;
        [Tooltip("Ширина крупных холмов, ед. — масштаб основной формы.")]
        [Range(20f, 300f)] public float HillWidth = 110f;
        [Tooltip("Высота холмов, доля от диапазона высот.")]
        [Range(0f, 1.5f)] public float HillHeight = 1f;
        [Tooltip("Доля средних форм (бугры, ложбины).")]
        [Range(0f, 1f)] public float MediumForms = 0.25f;
        [Tooltip("Доля мелкого шума. Держите малой: микрофактура живёт в покраске.")]
        [Range(0f, 0.3f)] public float FineNoise = 0.04f;
        [Tooltip("Сглаживание рельефа, проходов размытия.")]
        [Range(0, 8)] public int Smoothing = 2;

        [Header("Внешняя карта высот")]
        [Tooltip("Grayscale-карта высот (лучше 16 бит). Читается как данные, без sRGB.")]
        public Texture2D ExternalHeightmap;
        [Tooltip("Множитель внешней карты, доля диапазона высот.")]
        public float ExternalScale = 1f;
        [Tooltip("Сдвиг внешней карты, доля диапазона высот.")]
        public float ExternalOffset;
        [Tooltip("Инвертировать внешнюю карту (светлое — низко).")]
        public bool ExternalInvert;
        [Tooltip("Кривая перевода значения карты (0..1) в долю высоты (0..1).")]
        public AnimationCurve ExternalRemap = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    [Serializable]
    public sealed class MapLabWaterSettings
    {
        [Tooltip("Сдвиг уровня воды от уровня пресета, ед. (минус — вода ниже).")]
        [Range(-3f, 3f)] public float LevelOffset;
        [Tooltip("Масштаб размера озера.")]
        [Range(0.5f, 1.6f)] public float LakeScale = 1f;
        [Tooltip("Ширина реки, ед.")]
        [Range(3f, 30f)] public float RiverWidth = 11f;
        [Tooltip("Глубина русла под водой, ед.")]
        [Range(0.3f, 4f)] public float RiverDepth = 1.2f;
        [Tooltip("Ширина берегового откоса, ед.")]
        [Range(1f, 16f)] public float BankWidth = 5f;
    }

    [Serializable]
    public sealed class MapLabRoadSettings
    {
        [Tooltip("Ширина основной дороги, ед.")]
        [Range(1f, 8f)] public float RoadWidth = 3.2f;
        [Tooltip("Ширина тропы, ед.")]
        [Range(0.5f, 5f)] public float PathWidth = 1.6f;
        [Tooltip("Плотность полей вокруг поселения, 0–1.")]
        [Range(0f, 1f)] public float FieldDensity = 0.7f;
        [Tooltip("Ширина полосы поля (надела), ед.")]
        [Range(3f, 24f)] public float FieldStripWidth = 9f;
        [Tooltip("Наибольший уклон поля, градусов.")]
        [Range(3f, 25f)] public float FieldMaxSlope = 11f;
        [Tooltip("Внешний радиус полей от центра поселения, ед.")]
        [Range(20f, 200f)] public float FieldRadius = 95f;
    }

    [Serializable]
    public sealed class MapLabCliffSettings
    {
        [Tooltip("Высота скального уступа, ед.")]
        [Range(0f, 20f)] public float CliffHeight = 6f;
        [Tooltip("Резкость уступа: ширина перепада, ед. Меньше — круче стенка.")]
        [Range(0.6f, 8f)] public float CliffSharpness = 1.6f;
        [Tooltip("Средняя ширина скального столба, ед.")]
        [Range(0.8f, 6f)] public float ColumnWidth = 2.4f;
        [Tooltip("Неровность верхнего края и граней, 0–1.")]
        [Range(0f, 1f)] public float Jaggedness = 0.45f;
    }

    [Serializable]
    public sealed class MapLabVegetationSettings
    {
        [Tooltip("Плотность леса, 0–1 (нормализованная ручка, не число деревьев на м²).")]
        [Range(0f, 1f)] public float ForestDensity = 0.45f;
        [Tooltip("Размер кроны на экране, ед.")]
        [Range(1.5f, 10f)] public float TreeSize = 4.6f;
        [Tooltip("Разброс размера деревьев, доля.")]
        [Range(0f, 0.6f)] public float SizeVariation = 0.22f;
        [Tooltip("Доля хвойных.")]
        [Range(0f, 1f)] public float ConiferShare = 0.18f;
        [Tooltip("Неровность опушки: выступы, заливы, оторванные рощи. 0–1.")]
        [Range(0f, 1f)] public float EdgeRoughness = 0.6f;
        [Tooltip("Плотность одиночных деревьев на лугах, 0–1.")]
        [Range(0f, 1f)] public float SingleTrees = 0.35f;
        [Tooltip("Плотность кустов, 0–1.")]
        [Range(0f, 1f)] public float Bushes = 0.4f;
    }

    [Serializable]
    public sealed class MapLabSettlementSettings
    {
        [Tooltip("Наименьшее число домов. 0 — без поселения.")]
        [Range(0, 40)] public int HousesMin;
        [Tooltip("Наибольшее число домов.")]
        [Range(0, 40)] public int HousesMax;
        [Tooltip("Масштаб построек.")]
        [Range(0.5f, 2f)] public float BuildingScale = 1f;
        [Tooltip("Радиус застройки, ед.")]
        [Range(10f, 80f)] public float Radius = 34f;
        [Tooltip("Ограды дворов.")]
        public bool Fences = true;
        [Tooltip("Этап 02: ставить избы и амбары в ориентацию, в которой нарисованы PNG (рисунок нельзя крутить). Одинаково для геометрии и картинок.")]
        public bool AlignToDrawnView = true;
        [Tooltip("Угол конька на рисунке (от +X мира к +Z): изба с соломой, изба с дранкой, амбар. Оценено по PNG этапа 02.")]
        public Vector3 DrawnRidgeYaw = new Vector3(23f, -27f, 32f);
    }

    [Serializable]
    public sealed class MapLabStyleSettings
    {
        [Tooltip("Палитра карты. Изменение палитры — Repaint Only, без перестройки геометрии.")]
        public MapLabPalette Palette;
        [Tooltip("Масштаб крупных цветовых пятен, ед. (TextureScale).")]
        [Range(10f, 200f)] public float MacroScale = 60f;
        [Tooltip("Длина мазка, ед. (BrushScale).")]
        [Range(0.3f, 6f)] public float BrushScale = 1.6f;
        [Tooltip("Сила мазков в покраске, 0–1.")]
        [Range(0f, 1f)] public float BrushStrength = 0.55f;
        [Tooltip("Плотность мазков, 0–1.")]
        [Range(0f, 1f)] public float StrokeDensity = 0.7f;
        [Tooltip("Сила штриховки теневых склонов, 0–1.")]
        [Range(0f, 1f)] public float Hatching = 0.35f;
        [Tooltip("Сила художественного света по нормалям, 0–1.")]
        [Range(0f, 1f)] public float LightStrength = 0.75f;
        [Tooltip("Преувеличение рельефа для света (не меняет геометрию): объём читается на пологих холмах.")]
        [Range(1f, 5f)] public float ReliefExaggeration = 3f;
        [Tooltip("Мягкость перехода свет/тень, 0–1.")]
        [Range(0.05f, 1f)] public float ShadowSoftness = 0.55f;
        [Tooltip("Число мягких тональных диапазонов. 0 — плавно.")]
        [Range(0, 5)] public int ToneBands = 0;
        [Tooltip("Сила падающих теней от рельефа и крон, 0–1.")]
        [Range(0f, 1f)] public float CastShadows = 0.55f;
        [Tooltip("Сила затенения впадин и опушек, 0–1.")]
        [Range(0f, 1f)] public float AmbientOcclusion = 0.5f;
        [Tooltip("Сила тонкого контура, 0–1.")]
        [Range(0f, 1f)] public float Outline = 0.55f;
        [Tooltip("Сила мелкой фактуры мазка в шейдере (крупный план), 0–1.")]
        [Range(0f, 1f)] public float MicroTexture = 0.35f;
        [Tooltip("Зерно бумаги, 0–1.")]
        [Range(0f, 1f)] public float PaperGrain = 0.25f;
        [Tooltip("Насыщенность итоговой картинки.")]
        [Range(0.5f, 1.5f)] public float Saturation = 1f;
    }

    [Serializable]
    public sealed class MapLabCameraSettings
    {
        [Tooltip("Наклон камеры вниз от горизонта, градусов: 90 — строго сверху, 0 — вдоль земли.")]
        [Range(30f, 90f)] public float CameraPitchDownDegrees = 60f;
        [Tooltip("Приближение: 1 — вся карта в кадре, больше — ближе.")]
        [Range(0.5f, 8f)] public float Zoom = 1f;
        [Tooltip("Азимут камеры, градусов (фиксирован в эксперименте).")]
        public float AzimuthDegrees;
        [Tooltip("Fit: заполнить кадр картой без полей (края обрезаются) или вписать карту целиком.")]
        public bool FillFrame = true;
        [Tooltip("Режим сравнения: Атлас 60°, Объёмная карта 50°, Ближняя диорама 40°; Custom — наклон из поля выше.")]
        public MapLabCameraMode Mode = MapLabCameraMode.Volume50;
        [Tooltip("Наклон зависит от приближения: плавно от общего вида к ближнему (спрайты нарисованы под 50° — ракурс приближённый).")]
        public bool PitchByZoom;
        [Tooltip("Наклон при Zoom 1 и при наибольшем приближении (PitchByZoomFullAt).")]
        public Vector2 PitchByZoomRange = new Vector2(60f, 40f);
        [Tooltip("Приближение, при котором наклон доходит до второго значения.")]
        [Range(1.5f, 8f)] public float PitchByZoomFullAt = 4f;
        [Tooltip("Плавность приближения колесом (больше — быстрее).")]
        [Range(2f, 30f)] public float ZoomSmoothing = 12f;

        // Фактический наклон для данного приближения.
        public float PitchFor(float zoom)
        {
            if (PitchByZoom)
            {
                float t = Mathf.InverseLerp(1f, Mathf.Max(1.01f, PitchByZoomFullAt), zoom);
                t = t * t * (3f - 2f * t);
                return Mathf.Clamp(Mathf.Lerp(PitchByZoomRange.x, PitchByZoomRange.y, t), 30f, 90f);
            }
            switch (Mode)
            {
                case MapLabCameraMode.Atlas60: return 60f;
                case MapLabCameraMode.Volume50: return 50f;
                case MapLabCameraMode.Diorama40: return 40f;
                default: return Mathf.Clamp(CameraPitchDownDegrees, 30f, 90f);
            }
        }

        public static float PitchOf(MapLabCameraMode mode)
        {
            switch (mode)
            {
                case MapLabCameraMode.Atlas60: return 60f;
                case MapLabCameraMode.Diorama40: return 40f;
                default: return 50f;
            }
        }
    }

    // Художественный набор этапа 02. Переключение Procedural/Art меняет только
    // отображение: позиции объектов, дороги и вода от него не зависят.
    [Serializable]
    public sealed class MapLabArtSettings
    {
        [Tooltip("Деревья и кусты: процедурный атлас или нарисованные PNG.")]
        public MapLabArtMode Vegetation = MapLabArtMode.Art;
        [Tooltip("Постройки: геометрия, нарисованная карточка или карточка только при наклоне около 50°.")]
        public MapLabBuildingDisplay Buildings = MapLabBuildingDisplay.AutoByPitch;
        [Tooltip("Валуны: процедурная геометрия или нарисованный Boulder_01.")]
        public MapLabArtMode Boulders = MapLabArtMode.Art;
        [Tooltip("Декоративные карточки Cliff_Short/Cliff_End на обращённой к камере стороне уступов (поверх геометрии).")]
        public bool CliffDecor = true;
        [Tooltip("Фактуры земли (трава, земля, камень) поверх CPU-покраски.")]
        public bool GroundTextures = true;
        [Tooltip("Сила фактур земли.")]
        [Range(0f, 1f)] public float GroundStrength = 0.5f;
        [Tooltip("Размер одного повтора фактуры травы в мире, единиц.")]
        [Range(4f, 80f)] public float GrassScale = 26f;
        [Range(4f, 80f)] public float EarthScale = 18f;
        [Range(4f, 80f)] public float RockScale = 14f;
        [Tooltip("Rock_Color на стенках уступов — слабая вариация цвета, не замена света.")]
        [Range(0f, 1f)] public float RockColorStrength = 0.35f;
        [Tooltip("Мягкий оттенок палитры поверх рисунков (0 — рисунок как есть).")]
        [Range(0f, 1f)] public float PaletteTint = 0.2f;
        [Tooltip("Тень рельефа под опорой рисунка (падающая тень уступа, холма).")]
        [Range(0f, 1f)] public float GroundShade = 0.6f;
        [Tooltip("Наклон нарисованных карточек к камере.")]
        [Range(0f, 1f)] public float CardLean = 0.3f;
        [Tooltip("Диагностика альфы нарисованных карточек.")]
        public MapLabAlphaView AlphaView = MapLabAlphaView.Off;
        [Tooltip("Фишка отряда на дороге.")]
        public bool ShowMarker = true;
        [Tooltip("Призрачный контур фишки, когда её закрывает дом или скала (диагностика).")]
        public bool MarkerGhost;

        public bool BuildingSpritesAt(float pitch)
        {
            return Buildings == MapLabBuildingDisplay.SpriteDecorative || (Buildings == MapLabBuildingDisplay.AutoByPitch && Mathf.Abs(pitch - 50f) <= 4f);
        }
    }

    [Serializable]
    public sealed class MapLabDiagnosticsSettings
    {
        public MapLabDiagnosticMode Mode = MapLabDiagnosticMode.Final;
        public bool ShowWater = true;
        public bool ShowForest = true;
        public bool ShowBuildings = true;
        public bool ShowCliffs = true;
        public bool ShowStrokes = true;
        public bool ShowOutline = true;
        public bool ShowPaper = true;
        [Tooltip("Выключить живописную текстуру (остаётся геометрический свет).")]
        public bool ShowPaint = true;
        [Tooltip("Выключить геометрический свет (остаётся материал).")]
        public bool ShowReliefLight = true;
    }

    [Serializable]
    public sealed class MapLabAdvancedSettings
    {
        [Tooltip("Наклон карточек деревьев к камере: 0 — вертикальные, 1 — лицом к камере.")]
        [Range(0f, 1f)] public float CardLean = 0.25f;
        [Tooltip("Наибольшая сторона чанка в вершинах.")]
        [Range(33, 129)] public int ChunkVertices = 129;
        [Tooltip("Направление к условному источнику света (мир): X — вправо, Z — вглубь кадра.")]
        public Vector3 LightDirection = new Vector3(-0.62f, 0.72f, 0.06f);
        [Tooltip("Направление мазков на ровных местах, градусов.")]
        public float FlatStrokeAngle = 20f;
    }

    // Описание карты: все ручки художника. Сама генерация — MapLabGenerator.
    [CreateAssetMenu(menuName = "Kingdom Survival/MapLab/Настройки карты", fileName = "MapLabSettings")]
    public sealed class MapLabSettings : ScriptableObject
    {
        public MapLabGeneralSettings General = new MapLabGeneralSettings();
        public MapLabReliefSettings Relief = new MapLabReliefSettings();
        public MapLabWaterSettings Water = new MapLabWaterSettings();
        public MapLabRoadSettings RoadsAndFields = new MapLabRoadSettings();
        public MapLabCliffSettings Cliffs = new MapLabCliffSettings();
        public MapLabVegetationSettings Vegetation = new MapLabVegetationSettings();
        public MapLabSettlementSettings Settlement = new MapLabSettlementSettings();
        public MapLabStyleSettings PainterlyStyle = new MapLabStyleSettings();
        public MapLabCameraSettings Camera = new MapLabCameraSettings();
        public MapLabArtSettings Art = new MapLabArtSettings();
        public MapLabDiagnosticsSettings Diagnostics = new MapLabDiagnosticsSettings();
        public MapLabAdvancedSettings Advanced = new MapLabAdvancedSettings();

        public int Resolution => Mathf.Clamp((int)General.Quality, 33, 1025);

        public int PaintResolution => Mathf.ClosestPowerOfTwo(Mathf.Clamp(General.PaintResolution, 512, 4096));

        public Vector3 LightDirection
        {
            get
            {
                Vector3 l = Advanced.LightDirection;
                if (l.sqrMagnitude < 1e-6f || float.IsNaN(l.x))
                    l = new Vector3(-0.62f, 0.72f, 0.06f);
                if (l.y < 0.15f)
                    l.y = 0.15f;
                return l.normalized;
            }
        }

        public static void ApplyPresetDefaults(MapLabSettings settings, MapLabPresetKind kind)
        {
            settings.General.Preset = kind;
            switch (kind)
            {
                case MapLabPresetKind.HillAndLedge:
                    settings.General.Seed = 104;
                    settings.General.Quality = MapLabQuality.Samples129;
                    settings.Relief.HeightRange = 12f;
                    settings.Relief.HillWidth = 120f;
                    settings.Cliffs.CliffHeight = 9f;
                    settings.Vegetation.ForestDensity = 0.2f;
                    settings.Vegetation.SingleTrees = 0.3f;
                    settings.Settlement.HousesMin = 0;
                    settings.Settlement.HousesMax = 0;
                    settings.RoadsAndFields.FieldDensity = 0f;
                    settings.Camera.CameraPitchDownDegrees = 60f;
                    break;
                case MapLabPresetKind.ForestLake:
                    settings.General.Seed = 204;
                    settings.General.Quality = MapLabQuality.Samples257;
                    settings.Relief.HeightRange = 18f;
                    settings.Relief.HillWidth = 100f;
                    settings.Cliffs.CliffHeight = 11f;
                    settings.Water.RiverWidth = 7f;
                    settings.Vegetation.ForestDensity = 0.8f;
                    settings.Vegetation.SingleTrees = 0.35f;
                    settings.Vegetation.ConiferShare = 0.22f;
                    settings.Settlement.HousesMin = 3;
                    settings.Settlement.HousesMax = 4;
                    settings.Settlement.Radius = 16f;
                    settings.Settlement.Fences = false;
                    settings.RoadsAndFields.FieldDensity = 0f;
                    settings.RoadsAndFields.RoadWidth = 2f;
                    settings.Camera.CameraPitchDownDegrees = 65f;
                    break;
                case MapLabPresetKind.RiverValley:
                    settings.General.Seed = 304;
                    settings.General.Quality = MapLabQuality.Samples257;
                    settings.Relief.HeightRange = 13f;
                    settings.Relief.MediumForms = 0.45f;
                    settings.Relief.HillWidth = 110f;
                    settings.Cliffs.CliffHeight = 8f;
                    settings.Water.RiverWidth = 11f;
                    settings.Vegetation.ForestDensity = 0.45f;
                    settings.Vegetation.SingleTrees = 0.4f;
                    settings.Settlement.HousesMin = 12;
                    settings.Settlement.HousesMax = 25;
                    settings.Settlement.Radius = 36f;
                    settings.RoadsAndFields.FieldDensity = 0.75f;
                    settings.Camera.CameraPitchDownDegrees = 60f;
                    break;
            }
        }
    }
}
