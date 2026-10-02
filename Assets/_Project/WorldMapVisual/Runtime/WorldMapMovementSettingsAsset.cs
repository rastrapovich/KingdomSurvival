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
        [Tooltip("Сколько клеток в секунду реального времени пробегает герой по открытой местности.")]
        [SerializeField, Range(0.25f, 12f)] private float heroRunSpeedHexesPerSecond = WorldMapMovementRules.DefaultHeroRunSpeedHexesPerSecond;
        [Tooltip("Сколько игровых часов занимает одна клетка открытой местности. Пока отряд бежит, часы идут со скоростью бега.")]
        [SerializeField, Range(0.05f, 24f)] private float travelHoursPerHex = 1.5f;
        [Tooltip("На каком расстоянии (в клетках) от фактического пути замечается скрытое место.")]
        [SerializeField, Range(0.25f, 6f)] private float discoveryRadiusHexes = WorldMapMovementRules.DefaultDiscoveryRadiusHexes;

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
        public float TravelHoursPerHex => travelHoursPerHex;
        public float DiscoveryRadiusHexes => discoveryRadiusHexes;
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
                DiscoveryRadiusHexes = discoveryRadiusHexes
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
