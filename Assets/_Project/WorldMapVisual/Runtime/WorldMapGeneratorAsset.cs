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
            if (art != null)
                art.EnsureTerrainEntries();
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
