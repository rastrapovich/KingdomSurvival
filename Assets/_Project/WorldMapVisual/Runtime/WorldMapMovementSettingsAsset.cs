using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // 12И (канон v1.50 §9, §9.9): все настраиваемые числа прямого управления
    // героем на карте. Игровая часть (скорость, время на клетку, местность)
    // передаётся в ядро через WorldMapMovementRules.Current; остальное —
    // показ героя, ввод и камера — читает только интерфейс карты.
    // Все значения — рабочие настройки базы, не канон.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalWorldMapMovement",
        menuName = "Kingdom Survival/Карта/Movement Settings")]
    public sealed class WorldMapMovementSettingsAsset : ScriptableObject
    {
        public const string ResourcesPath = "WorldMapVisual/KingdomSurvivalWorldMapMovement";
        public const string AssetPath = "Assets/_Project/WorldMapVisual/Resources/WorldMapVisual/KingdomSurvivalWorldMapMovement.asset";

        [Header("Темп")]
        [Tooltip("Общая скорость передвижения по карте: сколько клеток в секунду реального времени проходит герой по открытой местности. Группы мира (обоз, дозор, мастер, ватага) идут с той же общей скоростью, каждая со своей долей.")]
        [SerializeField, Range(0.1f, 12f)] private float heroRunSpeedHexesPerSecond = WorldMapMovementRules.DefaultHeroRunSpeedHexesPerSecond;
        [Tooltip("Время идёт само, ровным темпом — независимо от того, идёт ли герой; группы движутся по часам мира. Выключено — прежняя модель: часы идут, только пока отряд в пути или занят делом.")]
        [SerializeField] private bool independentTime = true;
        [Tooltip("Скорость времени: игровых часов за секунду реального времени (0,3 — сутки за 80 секунд). Часы на клетку пути = скорость времени / скорость передвижения.")]
        [SerializeField, Range(0.02f, 4f)] private float worldGameHoursPerRealSecond = WorldMapMovementRules.DefaultWorldGameHoursPerRealSecond;
        [Tooltip("Сколько игровых часов занимает одна клетка открытой местности. Пока отряд бежит, часы идут со скоростью бега.")]
        [SerializeField, Range(0.05f, 24f)] private float travelHoursPerHex = 1.5f;
        [Tooltip("На каком расстоянии (в клетках) от фактического пути замечается скрытое место.")]
        [SerializeField, Range(0.25f, 6f)] private float discoveryRadiusHexes = WorldMapMovementRules.DefaultDiscoveryRadiusHexes;

        [Header("Туман войны (свободная игра)")]
        [Tooltip("Радиус (в клетках) вокруг пути героя, в котором местность открывается навсегда.")]
        [SerializeField, Range(0.5f, 10f)] private float fogRevealRadiusHexes = WorldMapMovementRules.DefaultFogRevealRadiusHexes;
        [Tooltip("Цвет и непрозрачность неисследованной местности.")]
        [SerializeField] private Color fogColor = new Color(0.055f, 0.06f, 0.07f, 0.86f);
        [Tooltip("Насколько светлее туман у самой границы исследованного (0 — резкая граница).")]
        [SerializeField, Range(0f, 1f)] private float fogEdgeSoftness = 0.45f;

        // Облака тумана (19.11.2026): декоративный объём над маской тумана.
        // Рисунки — у каждого вида карты (окно «Генератор карты» → «Виды
        // карты» → «Облака тумана войны»), числа — здесь. Рабочие значения.
        [Header("Облака тумана войны (декор)")]
        [Tooltip("Показывать облака над неисследованной местностью. Выключено — остаётся обычная маска тумана.")]
        [SerializeField] private bool fogCloudsEnabled = true;
        [Tooltip("Прежнее затемнение неисследованного сплошной маской под облаками. Выключено (по умолчанию) — неизвестное закрывают только облака. Без облаков (выключены или нет рисунков) маска включается сама, чтобы местность не открылась.")]
        [SerializeField] private bool fogMaskVisible;
        [Tooltip("Облако рассеивается, когда путь героя проходит ближе этого расстояния (клетки) от его центра. Не меньше радиуса открытия тумана.")]
        [SerializeField, Range(0.5f, 12f)] private float fogCloudTriggerRadiusHexes = 3f;
        [Tooltip("Шаг между облаками в ряду (клетки). Меньше — гуще.")]
        [SerializeField, Range(1f, 12f)] private float fogCloudSpacingHexes = 4.5f;
        [Tooltip("Шаг между рядами облаков (клетки). Облака низкие, поэтому ряды чаще: меньше — плотнее, без просветов.")]
        [SerializeField, Range(0.5f, 8f)] private float fogCloudRowSpacingHexes = 1.2f;
        [Tooltip("Разброс положения облаков (доля шага): убирает ровную сетку.")]
        [SerializeField, Range(0f, 0.9f)] private float fogCloudJitter = 0.45f;
        [Tooltip("Ширина облака (клетки).")]
        [SerializeField, Range(1f, 30f)] private float fogCloudWidthHexes = 8f;
        [Tooltip("Разброс размера облаков (±доля).")]
        [SerializeField, Range(0f, 0.6f)] private float fogCloudSizeVariation = 0.35f;
        [Tooltip("Непрозрачность облаков.")]
        [SerializeField, Range(0.05f, 1f)] private float fogCloudOpacity = 1f;
        [Tooltip("На сколько клеток облако отплывает от героя, пока тает.")]
        [SerializeField, Range(0f, 12f)] private float fogCloudDriftHexes = 3f;
        [Tooltip("Длительность рассеивания (секунды реального времени).")]
        [SerializeField, Range(0.1f, 6f)] private float fogCloudDissolveSeconds = 1.6f;
        [Tooltip("Плавность рассеивания: 0→1 по времени — доля пути и исчезновения.")]
        [SerializeField] private AnimationCurve fogCloudEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Сколько облаков может таять одновременно; лишние завершаются сразу.")]
        [SerializeField, Range(4, 200)] private int fogCloudMaxDissolving = 48;

        [Header("Местность")]
        [SerializeField] private List<TerrainEntry> terrain = new List<TerrainEntry>();

        [Header("Герой на карте")]
        [Tooltip("ID набора из Базы анимаций.")]
        [SerializeField] private string heroAnimationSetId = "militia";
        [Tooltip("Высота фигуры в радиусах клетки (умножается на масштаб набора).")]
        [SerializeField, Range(0.5f, 8f)] private float heroHeightInHexRadii = 1.4f;
        [Tooltip("Подгонять темп шага под скорость бега, чтобы ноги не скользили.")]
        [SerializeField] private bool syncWalkCadence = true;

        [Header("Управление")]
        [Tooltip("Сколько секунд держать кнопку, чтобы герой побежал за курсором. Короче — обычный клик: бег к точке клика.")]
        [SerializeField, Range(0.05f, 1f)] private float holdStartDelaySeconds = 0.25f;
        [Tooltip("Как часто пересчитывается путь, пока кнопка мыши зажата (секунды).")]
        [SerializeField, Range(0.03f, 1f)] private float holdRepathIntervalSeconds = 0.1f;
        [Tooltip("Насколько должна сместиться точка под курсором (в клетках), чтобы путь пересчитался.")]
        [SerializeField, Range(0.05f, 3f)] private float holdRepathMinShiftHexes = 0.3f;
        [Tooltip("Короткая отметка места клика. Траектория не рисуется никогда.")]
        [SerializeField] private bool showClickMarker = true;
        [SerializeField, Range(0.1f, 2f)] private float clickMarkerSeconds = 0.45f;
        [SerializeField] private Color clickMarkerColor = new Color(0.96f, 0.88f, 0.62f, 0.9f);

        [Header("Камера")]
        [Tooltip("Камера плавно держит героя в центре, пока он в походе. Значение по умолчанию: игрок меняет его переключателем «За героем» на карте.")]
        [SerializeField] private bool cameraFollowsHero;
        [Tooltip("Чем больше, тем быстрее камера догоняет героя.")]
        [SerializeField, Range(0.5f, 30f)] private float cameraFollowSharpness = 6f;

        public float HeroRunSpeedHexesPerSecond => heroRunSpeedHexesPerSecond;
        public bool IndependentTime => independentTime;
        public float WorldGameHoursPerRealSecond => worldGameHoursPerRealSecond;

        // Общие настройки (окно «Генератор карты» → «Время и движение»).
        public void SetPace(float mapSpeedHexesPerSecond, bool independent, float gameHoursPerRealSecond)
        {
            heroRunSpeedHexesPerSecond = Mathf.Clamp(mapSpeedHexesPerSecond, 0.1f, 12f);
            independentTime = independent;
            worldGameHoursPerRealSecond = Mathf.Clamp(gameHoursPerRealSecond, 0.02f, 4f);
            ApplyToCore();
        }
        public float TravelHoursPerHex => travelHoursPerHex;
        public float DiscoveryRadiusHexes => discoveryRadiusHexes;
        public float FogRevealRadiusHexes => fogRevealRadiusHexes;
        public Color FogColor => fogColor;
        public float FogEdgeSoftness => Mathf.Clamp01(fogEdgeSoftness);
        public bool FogCloudsEnabled => fogCloudsEnabled;
        public bool FogMaskVisible => fogMaskVisible;
        public float FogCloudRowSpacingHexes => Mathf.Max(0.5f, fogCloudRowSpacingHexes);
        public float FogCloudTriggerRadiusHexes => Mathf.Max(fogRevealRadiusHexes, fogCloudTriggerRadiusHexes);
        public float FogCloudSpacingHexes => Mathf.Max(1f, fogCloudSpacingHexes);
        public float FogCloudJitter => Mathf.Clamp(fogCloudJitter, 0f, 0.9f);
        public float FogCloudWidthHexes => Mathf.Max(0.5f, fogCloudWidthHexes);
        public float FogCloudSizeVariation => Mathf.Clamp(fogCloudSizeVariation, 0f, 0.6f);
        public float FogCloudOpacity => Mathf.Clamp01(fogCloudOpacity);
        public float FogCloudDriftHexes => Mathf.Max(0f, fogCloudDriftHexes);
        public float FogCloudDissolveSeconds => Mathf.Max(0.05f, fogCloudDissolveSeconds);
        public int FogCloudMaxDissolving => Mathf.Max(1, fogCloudMaxDissolving);
        public float EvaluateFogCloudEase(float t)
        {
            t = Mathf.Clamp01(t);
            return fogCloudEase != null && fogCloudEase.length >= 2 ? Mathf.Clamp01(fogCloudEase.Evaluate(t)) : t * t * (3f - 2f * t);
        }
        public IReadOnlyList<TerrainEntry> Terrain => terrain;
        public string HeroAnimationSetId => heroAnimationSetId ?? string.Empty;
        public float HeroHeightInHexRadii => Mathf.Max(0.1f, heroHeightInHexRadii);
        public bool SyncWalkCadence => syncWalkCadence;
        public float HoldStartDelaySeconds => Mathf.Max(0f, holdStartDelaySeconds);
        public float HoldRepathIntervalSeconds => Mathf.Max(0.01f, holdRepathIntervalSeconds);
        public float HoldRepathMinShiftHexes => Mathf.Max(0.01f, holdRepathMinShiftHexes);
        public bool ShowClickMarker => showClickMarker;
        public float ClickMarkerSeconds => Mathf.Max(0.05f, clickMarkerSeconds);
        public Color ClickMarkerColor => clickMarkerColor;
        public bool CameraFollowsHero => cameraFollowsHero;
        public float CameraFollowSharpness => Mathf.Max(0.1f, cameraFollowSharpness);

        public WorldMapMovementRules ToRules()
        {
            WorldMapMovementRules rules = new WorldMapMovementRules
            {
                HeroRunSpeedHexesPerSecond = heroRunSpeedHexesPerSecond,
                TravelHoursPerHex = travelHoursPerHex,
                DiscoveryRadiusHexes = discoveryRadiusHexes,
                FogRevealRadiusHexes = fogRevealRadiusHexes,
                IndependentTime = independentTime,
                WorldGameHoursPerRealSecond = worldGameHoursPerRealSecond
            };
            foreach (WorldMapTerrainRule fallback in WorldMapMovementRules.DefaultTerrainRules())
            {
                TerrainEntry entry = FindTerrain(fallback.Terrain);
                rules.Terrain.Add(entry != null
                    ? new WorldMapTerrainRule
                    {
                        Terrain = entry.Terrain,
                        Traversable = entry.Traversable,
                        TravelSpeedMultiplier = entry.TravelSpeedMultiplier,
                        RunSpeedMultiplier = entry.RunSpeedMultiplier
                    }
                    : fallback);
            }
            return rules;
        }

        public void ApplyToCore() => WorldMapMovementRules.Current = ToRules();

        public TerrainEntry FindTerrain(WorldMapGameplayTerrainType type)
        {
            if (terrain == null)
                return null;
            foreach (TerrainEntry entry in terrain)
            {
                if (entry != null && entry.Terrain == type)
                    return entry;
            }
            return null;
        }

        public Color GetTerrainColor(WorldMapGameplayTerrainType type)
        {
            TerrainEntry entry = FindTerrain(type);
            return entry != null ? entry.PreviewColor : DefaultColor(type);
        }

        // Идемпотентно: недостающие типы местности получают значения ядра.
        public bool EnsureTerrainEntries()
        {
            if (terrain == null)
                terrain = new List<TerrainEntry>();
            bool changed = false;
            foreach (WorldMapTerrainRule rule in WorldMapMovementRules.DefaultTerrainRules())
            {
                if (FindTerrain(rule.Terrain) != null)
                    continue;
                terrain.Add(new TerrainEntry
                {
                    Terrain = rule.Terrain,
                    Traversable = rule.Traversable,
                    TravelSpeedMultiplier = rule.TravelSpeedMultiplier,
                    RunSpeedMultiplier = rule.RunSpeedMultiplier,
                    PreviewColor = DefaultColor(rule.Terrain)
                });
                changed = true;
            }
            terrain.Sort((a, b) => System.Array.IndexOf(WorldMapTerrainLabels.All, a.Terrain)
                .CompareTo(System.Array.IndexOf(WorldMapTerrainLabels.All, b.Terrain)));
            return changed;
        }

        private void OnEnable()
        {
            EnsureTerrainEntries();
        }

        public static Color DefaultColor(WorldMapGameplayTerrainType type)
        {
            switch (type)
            {
                case WorldMapGameplayTerrainType.Road: return new Color(0.86f, 0.70f, 0.42f, 1f);
                case WorldMapGameplayTerrainType.Trail: return new Color(0.80f, 0.74f, 0.52f, 1f);
                case WorldMapGameplayTerrainType.Field: return new Color(0.86f, 0.84f, 0.38f, 1f);
                case WorldMapGameplayTerrainType.Forest: return new Color(0.20f, 0.52f, 0.25f, 1f);
                case WorldMapGameplayTerrainType.Swamp: return new Color(0.38f, 0.48f, 0.30f, 1f);
                case WorldMapGameplayTerrainType.Hills: return new Color(0.66f, 0.52f, 0.34f, 1f);
                case WorldMapGameplayTerrainType.Mountains: return new Color(0.56f, 0.54f, 0.56f, 1f);
                case WorldMapGameplayTerrainType.Water: return new Color(0.22f, 0.46f, 0.86f, 1f);
                case WorldMapGameplayTerrainType.Cliffs: return new Color(0.32f, 0.28f, 0.30f, 1f);
                default: return new Color(0.70f, 0.80f, 0.62f, 1f);
            }
        }

        [System.Serializable]
        public sealed class TerrainEntry
        {
            public WorldMapGameplayTerrainType Terrain;
            public bool Traversable = true;
            [Tooltip("Скорость по игровому времени: часы на клетку = время на клетку / множитель.")]
            [Range(0.05f, 4f)] public float TravelSpeedMultiplier = 1f;
            [Tooltip("Видимая скорость бега фигуры.")]
            [Range(0.05f, 4f)] public float RunSpeedMultiplier = 1f;
            [Tooltip("Цвет разметки в редакторе и в режиме сетки.")]
            public Color PreviewColor = Color.white;
        }
    }
}
