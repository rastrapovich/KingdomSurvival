using System;
using System.Collections.Generic;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Переключатель вида карты в Базе ассетов. Сами виды — в сборке MapLab;
    // мост (KingdomSurvival.MapLabBridge.Editor) подключает эти функции при
    // загрузке редактора. Без моста переключателя нет.
    public static class ArtAssetViewSwitch
    {
        public static Func<IReadOnlyList<(string Id, string Name)>> ListViews;
        public static Func<string> ActiveViewId;
        public static Func<string> ActiveViewName;
        public static Action<string> SelectView;

        public static bool Available => ListViews != null && SelectView != null;

        public static IReadOnlyList<(string Id, string Name)> List() => ListViews != null ? ListViews() : Array.Empty<(string, string)>();

        public static string ActiveId() => ActiveViewId != null ? ActiveViewId() : string.Empty;

        public static string ActiveName() => ActiveViewName != null ? ActiveViewName() : string.Empty;

        public static void Select(string id) => SelectView?.Invoke(id);
    }
}
