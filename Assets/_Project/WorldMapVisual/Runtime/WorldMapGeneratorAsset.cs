using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Канон v1.58 §9.9: генератор карты «Свободной игры» — настройки
    // генерации (что и где), облик (как выглядит) и сохранённые наборы.
    // Подключается к World Map Database; каждая новая партия свободной игры
    // строит по нему свою карту из WorldSeed. Кампании кризисов по-прежнему
    // идут по авторскому World Definition.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalWorldMapGenerator",
        menuName = "Kingdom Survival/Карта/Map Generator")]
    public sealed class WorldMapGeneratorAsset : ScriptableObject
    {
        public const string AssetPath = "Assets/_Project/WorldMapVisual/Resources/WorldMapVisual/KingdomSurvivalWorldMapGenerator.asset";

        [Tooltip("Новая партия «Свободной игры» получает сгенерированную карту. Выключено — авторский мир, как раньше.")]
        [SerializeField] private bool useForFreePlay = true;
        [SerializeField] private WorldMapGenerationSettings settings = WorldMapGenerationSettings.CreateDefault();
        [SerializeField] private WorldMapArtStyle art = WorldMapArtStyle.CreateDefault();
        [SerializeField] private List<WorldMapGeneratorPreset> presets = new List<WorldMapGeneratorPreset>();

        public bool UseForFreePlay
        {
            get => useForFreePlay;
            set => useForFreePlay = value;
        }

        public WorldMapGenerationSettings Settings
        {
            get
            {
                if (settings == null)
                    settings = WorldMapGenerationSettings.CreateDefault();
                return settings;
            }
            set => settings = value ?? WorldMapGenerationSettings.CreateDefault();
        }

        public WorldMapArtStyle Art
        {
            get
            {
                if (art == null)
                    art = WorldMapArtStyle.CreateDefault();
                art.EnsureTerrainEntries();
                art.NormalizePools();
                return art;
            }
            set => art = value ?? WorldMapArtStyle.CreateDefault();
        }

        public List<WorldMapGeneratorPreset> Presets => presets ??= new List<WorldMapGeneratorPreset>();

        public GeneratedWorldMap Generate(int seed, WorldMapGenerationStage upTo = WorldMapGenerationStage.Complete) =>
            WorldMapGenerator.Generate(Settings, seed, upTo);

        public WorldMapGeneratorPreset FindPreset(string id) =>
            string.IsNullOrEmpty(id) ? null : Presets.Find(preset => preset != null && preset.Id == id);

        private void OnEnable()
        {
            bool poolsChanged = art != null && (art.EnsureTerrainEntries() | art.NormalizePools() | art.UpgradeInk());
            if (UpgradeLandmarkCatalog() | poolsChanged)
            {
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
        }

        // 12С-4: каталог мест по лору. Ассет, собранный раньше, получает
        // недостающие правила по умолчанию и описания у прежних правил;
        // правки автора (числа, выключенные правила) не трогаются.
        public const int CurrentLandmarkCatalogVersion = 2;
        [SerializeField] private int landmarkCatalogVersion;

        public bool UpgradeLandmarkCatalog()
        {
            if (landmarkCatalogVersion >= CurrentLandmarkCatalogVersion || settings == null)
                return false;
            settings.Landmarks ??= new List<WorldMapLandmarkRule>();
            foreach (WorldMapLandmarkRule fallback in WorldMapGenerationSettings.DefaultLandmarks())
            {
                WorldMapLandmarkRule existing = settings.Landmarks.Find(rule => rule != null && rule.Id == fallback.Id);
                if (existing == null)
                {
                    settings.Landmarks.Add(fallback);
                    continue;
                }
                if (!string.IsNullOrEmpty(existing.Description))
                    continue;
                existing.Description = fallback.Description;
                existing.Source = fallback.Source;
                existing.Hidden = fallback.Hidden;
                existing.Threat = fallback.Threat;
                existing.ExplorationHours = fallback.ExplorationHours;
                existing.RewardArmySupply = fallback.RewardArmySupply;
                existing.RewardArmyGold = fallback.RewardArmyGold;
                existing.Placement = fallback.Placement;
            }
            landmarkCatalogVersion = CurrentLandmarkCatalogVersion;
            return true;
        }
    }

    // Сохранённый набор: «Запомнить» в окне генератора, «Применить» —
    // целиком или только генерацию / только облик.
    [System.Serializable]
    public sealed class WorldMapGeneratorPreset
    {
        public string Id = System.Guid.NewGuid().ToString("N");
        public string Name = "Набор";
        public string Note = string.Empty;
        public WorldMapGenerationSettings Settings = WorldMapGenerationSettings.CreateDefault();
        public WorldMapArtStyle Art = WorldMapArtStyle.CreateDefault();
    }
}
