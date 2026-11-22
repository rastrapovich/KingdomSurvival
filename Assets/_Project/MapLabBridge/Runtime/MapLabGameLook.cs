using System.Collections.Generic;
using KingdomSurvival.MapLab;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // Облик карты для игрового интерфейса (22.11.2026). Интерфейс не ссылается
    // на сборку MapLab напрямую — всё, что ему нужно об облике, здесь: облака
    // тумана войны с цветокоррекцией и их тень. Настройки — MapLabMapLook
    // (Resources). Заменяет прежний выбор вида карты: облик один.
    public static class MapLabGameLook
    {
        // Облака тумана войны облика (рисунки без цветокоррекции). Пусто —
        // облаков нет, маска тумана скрывает местность как раньше.
        public static List<Texture2D> FogClouds()
        {
            List<Texture2D> result = new List<Texture2D>();
            MapLabMapLook look = MapLabMapLook.Load();
            if (look.FogClouds != null)
                foreach (Texture2D t in look.FogClouds)
                    if (t != null)
                        result.Add(t);
            return result;
        }

        // Облака с коррекцией облика (яркость, тон, насыщенность, уровни).
        // Коррекции нет — те же текстуры. Готовые копии хранятся, пока не
        // сменится коррекция; adjustKey — для проверки, что пора обновить.
        private static readonly Dictionary<Texture2D, Texture2D> adjustedClouds = new Dictionary<Texture2D, Texture2D>();
        private static string adjustedCloudsKey;

        public static List<Texture2D> FogCloudsAdjusted(out string adjustKey)
        {
            List<Texture2D> textures = FogClouds();
            MapLabMapLook look = MapLabMapLook.Load();
            adjustKey = look.FogCloudAdjustKey;
            if (look.FogCloudAdjustNeutral)
                return textures;
            if (adjustKey != adjustedCloudsKey)
            {
                ClearAdjustedClouds();
                adjustedCloudsKey = adjustKey;
            }
            for (int i = 0; i < textures.Count; i++)
            {
                Texture2D source = textures[i];
                if (!adjustedClouds.TryGetValue(source, out Texture2D adjusted) || adjusted == null)
                {
                    adjusted = MapLabColorMath.CreateAdjusted(source, look.FogCloudBrightness, look.FogCloudHue, look.FogCloudSaturation, look.FogCloudLevels);
                    adjustedClouds[source] = adjusted;
                }
                if (adjusted != null)
                    textures[i] = adjusted;
            }
            return textures;
        }

        private static void ClearAdjustedClouds()
        {
            foreach (Texture2D t in adjustedClouds.Values)
                if (t != null)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(t);
                    else
                        UnityEngine.Object.DestroyImmediate(t);
                }
            adjustedClouds.Clear();
            adjustedCloudsKey = null;
        }

        // Тень облака на земле: размытый белый силуэт (окраску задаёт интерфейс).
        public static Texture2D CreateFogCloudShadow(Texture2D cloud, float pad, float blur) =>
            MapLabColorMath.CreateShadow(cloud, pad, blur);
    }
}
