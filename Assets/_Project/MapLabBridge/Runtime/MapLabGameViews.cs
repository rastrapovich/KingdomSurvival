using System;
using System.Collections.Generic;
using KingdomSurvival.MapLab;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // Виды карты для игрового интерфейса (17.11.2026). Интерфейс не ссылается
    // на сборку MapLab напрямую — всё, что ему нужно о видах, здесь: имя
    // выбранного вида, переключение по кругу, цвета тумана и фона, нужна ли
    // пересборка живой карты. Сами виды — MapLabMapViews (Resources).
    public static class MapLabGameViews
    {
        public static event Action Changed
        {
            add => MapLabMapViews.ActiveChanged += value;
            remove => MapLabMapViews.ActiveChanged -= value;
        }

        public static string ActiveId => MapLabMapViews.Active?.Id ?? MapLabMapViews.ClassicId;

        public static string ActiveName => MapLabMapViews.Active?.Name ?? "Обычный";

        public static string ActiveNote => MapLabMapViews.Active?.Note ?? string.Empty;

        // Включённые виды: ID и название, по порядку.
        public static List<(string Id, string Name)> All()
        {
            List<(string, string)> result = new List<(string, string)>();
            foreach (MapLabMapView v in MapLabMapViews.Load().EnabledViews())
                result.Add((v.Id, v.Name));
            return result;
        }

        public static int Count => MapLabMapViews.Load().EnabledViews().Count;

        // Следующий вид по кругу; возвращает его название.
        public static string Cycle(int step = 1) => MapLabMapViews.Cycle(step)?.Name ?? ActiveName;

        public static void Select(string id) => MapLabMapViews.ActiveId = id;

        // Цвет тумана войны выбранного вида (если вид его задаёт).
        public static bool TryFogColor(out Color color)
        {
            MapLabMapView v = MapLabMapViews.Active;
            color = v != null && v.OverrideFog ? v.FogColor : default;
            return v != null && v.OverrideFog;
        }

        // Облака тумана войны выбранного вида (рисунки без цветокоррекции —
        // они уже в гамме вида). Нет своих — явный запасной набор вида
        // «Обычный» (fallbackViewName). Пусто совсем — облаков нет, маска
        // тумана скрывает местность как раньше.
        public static List<Texture2D> FogClouds(out string fallbackViewName)
        {
            fallbackViewName = null;
            List<Texture2D> result = new List<Texture2D>();
            MapLabMapView v = MapLabMapViews.Active;
            Collect(v, result);
            if (result.Count > 0)
                return result;
            MapLabMapView classic = MapLabMapViews.Load().Find(MapLabMapViews.ClassicId);
            if (classic != null && !ReferenceEquals(classic, v))
            {
                Collect(classic, result);
                if (result.Count > 0)
                    fallbackViewName = classic.Name;
            }
            return result;

            static void Collect(MapLabMapView view, List<Texture2D> into)
            {
                if (view?.FogClouds == null)
                    return;
                foreach (Texture2D t in view.FogClouds)
                    if (t != null)
                        into.Add(t);
            }
        }

        // Облака с коррекцией вида (яркость, тон, насыщенность, уровни).
        // Коррекции нет — те же текстуры. Готовые копии хранятся, пока не
        // сменится коррекция; adjustKey — для проверки, что пора обновить.
        private static readonly Dictionary<Texture2D, Texture2D> adjustedClouds = new Dictionary<Texture2D, Texture2D>();
        private static string adjustedCloudsKey;

        public static List<Texture2D> FogCloudsAdjusted(out string fallbackViewName, out string adjustKey)
        {
            List<Texture2D> textures = FogClouds(out fallbackViewName);
            MapLabMapView v = MapLabMapViews.Active;
            adjustKey = v != null ? v.Id + ":" + v.FogCloudAdjustKey : "-";
            if (v == null || v.FogCloudAdjustNeutral)
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
                    adjusted = MapLabColorMath.CreateAdjusted(source, v.FogCloudBrightness, v.FogCloudHue, v.FogCloudSaturation, v.FogCloudLevels);
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

        // Подкраска картинок интерфейса поверх карты.
        public static Color UiTint
        {
            get
            {
                MapLabMapView v = MapLabMapViews.Active;
                return v != null && v.Grade != null && !v.Grade.IsNeutral ? v.UiTint : Color.white;
            }
        }

        // Фон под картой (пока рисуется и за краем полотна).
        public static Color Background(Color fallback)
        {
            MapLabMapView v = MapLabMapViews.Active;
            return v != null && v.Grade != null && !v.Grade.IsNeutral ? v.Background : fallback;
        }

        // Живая карта собрана с другой палитрой — нужна пересборка. Другие свои
        // рисунки при той же палитре подменяются сразу (TryApplyActiveView).
        public static bool LiveNeedsRebuild
        {
            get
            {
                MapLabGameLive.World w = MapLabGameLive.Current;
                return w != null && w.ViewKey != MapLabMapViews.BuildKey(MapLabMapViews.Active) && !MapLabGameLive.TryApplyActiveView();
            }
        }
    }
}
