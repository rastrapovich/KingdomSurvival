using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Облик карты (22.11.2026): настройки показа карты свободной игры, которые
    // не относятся к самому миру. Сейчас это облака тумана войны: набор PNG
    // с прозрачностью и их цветокоррекция (яркость, тон, насыщенность, уровни).
    // Заменяет прежние «Виды карты»: остался единственный облик — прежний
    // «Обычный», механизма нескольких видов нет. Рабочие настройки облика,
    // не канон. Значения по умолчанию в коде — те же, что в ассете, поэтому без
    // ассета облик карты тот же.
    [CreateAssetMenu(menuName = "Kingdom Survival/MapLab/Облик карты", fileName = "MapLabMapLook")]
    public sealed class MapLabMapLook : ScriptableObject
    {
        public const string ResourcesPath = "MapLabMapLook";
        public const string AssetPath = "Assets/_Project/MapLabBridge/Resources/MapLabMapLook.asset";
        // Папка облаков тумана войны: <FogCloudsFolder>/*.png.
        public const string FogCloudsFolder = "Assets/_Project/Art/MapFogClouds/classic";

        [Tooltip("Облака тумана войны: отдельные PNG с прозрачностью, рисуются над маской неисследованного. Пусто — облаков нет, маска тумана скрывает местность как раньше.")]
        public List<Texture2D> FogClouds = new List<Texture2D>();
        [Tooltip("Коррекция облаков: яркость (1 — без изменений).")]
        [Range(0.3f, 2f)] public float FogCloudBrightness = 0.95f;
        [Tooltip("Поворот цветового тона облаков, градусов.")]
        [Range(-180f, 180f)] public float FogCloudHue;
        [Tooltip("Насыщенность облаков (1 — без изменений).")]
        [Range(0f, 2f)] public float FogCloudSaturation = 0.8f;
        [Tooltip("Уровни облаков, как в Photoshop.")]
        public MapLabLevels FogCloudLevels = new MapLabLevels { Gamma = 0.95f, OutputBlack = 40f, OutputWhite = 222f };

        private static MapLabMapLook loaded;

        // Ассет из Resources; без ассета — настройки по умолчанию в памяти
        // (облаков нет, коррекция та же), игра работает и без него.
        public static MapLabMapLook Load()
        {
            if (loaded == null)
            {
                loaded = Resources.Load<MapLabMapLook>(ResourcesPath);
                if (loaded == null)
                {
                    loaded = CreateInstance<MapLabMapLook>();
                    loaded.name = "MapLabMapLook (по умолчанию)";
                    loaded.hideFlags = HideFlags.DontSave;
                }
            }
            return loaded;
        }

        // Для тестов и окна генератора после создания ассета.
        public static void ResetCache()
        {
            loaded = null;
        }

        public bool FogCloudAdjustNeutral => MapLabColorMath.IsNeutral(FogCloudBrightness, FogCloudHue, FogCloudSaturation, FogCloudLevels);

        // Ключ коррекции облаков: меняется вместе с любым из чисел.
        public string FogCloudAdjustKey => FogCloudAdjustNeutral ? "-" :
            FogCloudBrightness.ToString("0.###") + "/" + FogCloudHue.ToString("0.##") + "/" + FogCloudSaturation.ToString("0.###") + "/" + (FogCloudLevels != null ? FogCloudLevels.Key : "-");

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
    }
}
