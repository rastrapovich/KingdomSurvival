using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Порядок отрисовки предметов места для окна Базы локаций: список «сверху
    // вниз» (первый — рисуется поверх всех), перестановка в нём и шаг
    // выше/ниже. Порядок предмета — тот же, что у рендерера
    // (LocationVisualGeometry.SortOrder): слой основы, глубина опоры и
    // «Порядок внутри слоя». Перестановка меняет слой и порядок основы; у
    // экземпляра ассета для этого включается «Слой основы: настроить для
    // этого экземпляра». Источники света без рисунка — в конце, вне порядка.
    public static class LocationObjectOrder
    {
        public static LocationVisualBand BandOf(LocationVisualObject item)
        {
            LocationResolvedPart main = LocationVisualResolver.Resolve(item)?.Main;
            return main != null ? main.Band : item.Band;
        }

        private static int OffsetOf(LocationVisualObject item)
        {
            LocationResolvedPart main = LocationVisualResolver.Resolve(item)?.Main;
            return main != null ? main.OrderOffset : item.OrderOffset;
        }

        private static float DepthOf(LocalLocationDefinition location, LocationVisualObject item) =>
            LocationVisualGeometry.ToWorld(location, item.Position).y;

        // Порядок основы предмета в кадре (больше — поверх).
        public static int DrawOrder(LocalLocationDefinition location, LocationVisualObject item) =>
            LocationVisualGeometry.SortOrder(BandOf(item), DepthOf(location, item), OffsetOf(item));

        // Предметы сверху вниз: первый рисуется поверх всех. Свет без рисунка — в конце.
        public static List<LocationVisualObject> TopFirst(LocalLocationDefinition location, LocationVisualDefinition visual)
        {
            List<LocationVisualObject> drawn = visual.Objects.Where(item => item != null && !item.LightOnly).ToList();
            Dictionary<LocationVisualObject, int> orders = drawn.ToDictionary(item => item, item => DrawOrder(location, item));
            Dictionary<LocationVisualObject, int> index = new Dictionary<LocationVisualObject, int>();
            for (int i = 0; i < visual.Objects.Count; i++)
                if (visual.Objects[i] != null) index[visual.Objects[i]] = i;
            // При равном порядке позже добавленный считается выше.
            List<LocationVisualObject> result = drawn.OrderByDescending(item => orders[item]).ThenByDescending(item => index[item]).ToList();
            result.AddRange(visual.Objects.Where(item => item != null && item.LightOnly));
            return result;
        }

        // Предмет topFirst[index] встаёт в кадре между соседями по списку:
        // ниже topFirst[index − 1] и выше topFirst[index + 1]. Если между ними
        // нет места, предметы выше него сдвигаются вверх. Слой — как у соседа
        // снизу (у самого нижнего — как у соседа сверху).
        public static void PlaceAt(LocalLocationDefinition location, List<LocationVisualObject> topFirst, int index)
        {
            List<LocationVisualObject> drawn = topFirst.Where(item => item != null && !item.LightOnly).ToList();
            LocationVisualObject moved = index >= 0 && index < topFirst.Count ? topFirst[index] : null;
            if (moved == null || moved.LightOnly) return;
            int at = drawn.IndexOf(moved);
            LocationVisualObject above = at > 0 ? drawn[at - 1] : null;
            LocationVisualObject below = at + 1 < drawn.Count ? drawn[at + 1] : null;
            if (above == null && below == null) return;
            LocationVisualBand band = below != null ? BandOf(below) : BandOf(above);
            int target = below != null ? DrawOrder(location, below) + 1 : DrawOrder(location, above) - 1;
            SetOrder(location, moved, band, target);
            // Над ним — строго выше: кто не выше, сдвигается вверх.
            int previous = DrawOrder(location, moved);
            for (int i = at - 1; i >= 0; i--)
            {
                LocationVisualObject item = drawn[i];
                int order = DrawOrder(location, item);
                if (order > previous) break;
                SetOrder(location, item, BandOf(item), previous + 1);
                previous = DrawOrder(location, item);
            }
        }

        // Шаг выше (+1) или ниже (−1) по порядку: меняется местами с соседом.
        // false — уже крайний.
        public static bool Step(LocalLocationDefinition location, LocationVisualDefinition visual, LocationVisualObject item, int direction)
        {
            List<LocationVisualObject> list = TopFirst(location, visual);
            int at = list.IndexOf(item);
            int to = at - System.Math.Sign(direction);
            if (at < 0 || item.LightOnly || to < 0 || to >= list.Count || list[to].LightOnly) return false;
            list.RemoveAt(at);
            list.Insert(to, item);
            PlaceAt(location, list, to);
            return true;
        }

        // Поверх всех предметов своего слоя (top) или под всеми.
        public static bool ToEdgeOfBand(LocalLocationDefinition location, LocationVisualDefinition visual, LocationVisualObject item, bool top)
        {
            List<LocationVisualObject> list = TopFirst(location, visual);
            LocationVisualBand band = BandOf(item);
            List<LocationVisualObject> same = list.Where(other => other != item && !other.LightOnly && BandOf(other) == band).ToList();
            if (same.Count == 0 || item.LightOnly) return false;
            int edge = top ? same.Max(other => DrawOrder(location, other)) + 1 : same.Min(other => DrawOrder(location, other)) - 1;
            if (top ? DrawOrder(location, item) >= edge : DrawOrder(location, item) <= edge) return false;
            SetOrder(location, item, band, edge);
            return true;
        }

        // Слой и «Порядок внутри слоя» основы так, чтобы порядок в кадре стал order.
        public static void SetOrder(LocalLocationDefinition location, LocationVisualObject item, LocationVisualBand band, int order)
        {
            int offset = order - LocationVisualGeometry.SortOrder(band, DepthOf(location, item));
            if (item.UsesAsset) item.Overrides |= LocationAssetOverride.Layer;
            item.Band = band;
            item.OrderOffset = offset;
        }
    }
}
