using System.Collections.Generic;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.MapLab;
using KingdomSurvival.MapLab.Editor;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Editor
{
    // Виды карты в окнах редактора, которые сборку MapLab не видят:
    //  • База ассетов — переключатель вида и рисунки ассетов в выбранном виде
    //    (своя версия — PNG «art_<ID ассета>.png» в папке вида);
    //  • Генератор карты — ассеты Базы (арена, лес карты) среди известных ID
    //    для «Собрать из папки» и «Список ID».
    [InitializeOnLoad]
    internal static class MapLabViewsEditorHooks
    {
        static MapLabViewsEditorHooks()
        {
            ArtAssetViewSwitch.ListViews = () =>
            {
                List<(string, string)> list = new List<(string, string)>();
                foreach (MapLabMapView v in MapLabMapViews.Load().EnabledViews())
                    list.Add((v.Id, v.Name));
                return list;
            };
            ArtAssetViewSwitch.ActiveViewId = () => MapLabMapViews.Active?.Id ?? string.Empty;
            ArtAssetViewSwitch.ActiveViewName = () => MapLabMapViews.Active?.Name ?? string.Empty;
            ArtAssetViewSwitch.SelectView = id => MapLabMapViews.ActiveId = id;
            ArtAssetDrawing.ViewTexture = ViewTexture;
            MapLabGeneratorWindow.ExtraKnownArt = ExtraKnownArt;
            MapLabMapViews.ActiveChanged -= RepaintWindows;
            MapLabMapViews.ActiveChanged += RepaintWindows;
        }

        // Рисунок ассета Базы в выбранном виде; null — своего нет или холст другой.
        private static Texture2D ViewTexture(ArtAssetDefinition asset, Sprite sprite)
        {
            MapLabMapView look = MapLabMapViews.Active;
            if (look == null || asset == null || look.ArtCount == 0)
                return null;
            Texture2D own = look.FindTexture(MapLabArtAssets.IdPrefix + asset.Id);
            if (own == null || sprite == null || sprite.rect.height < 1f || own.height < 1)
                return own;
            float want = sprite.rect.width / sprite.rect.height, have = own.width / (float)own.height;
            return Mathf.Abs(have - want) > want * 0.03f ? null : own;
        }

        // Записи ассетов Базы, которые берут арена и лес карты (ID «art:…»).
        private static IEnumerable<MapLabArtEntry> ExtraKnownArt()
        {
            List<MapLabArtEntry> result = new List<MapLabArtEntry>();
            foreach (bool arena in new[] { true, false })
            {
                MapLabArtSet set = MapLabArtAssets.BuildSet(arena, 1f, null);
                if (set == null)
                    continue;
                result.AddRange(set.Entries);
                Object.DestroyImmediate(set);
            }
            return result;
        }

        // Смена вида (в игре, генераторе или Базе) — перерисовать открытые окна.
        private static void RepaintWindows()
        {
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                string name = window.GetType().Name;
                if (name == "ArtAssetDatabaseWindow" || name == "MapLabGeneratorWindow")
                    window.Repaint();
            }
        }
    }
}
