using System;
using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12Р: слои раскидки места. Объекты — каждый экземпляр как предмет
    // (тот же путь, что у поставленных вручную: части, кадры, нормали,
    // сортировка с людьми, тени слоя); ковёр — одной сеткой на группу
    // (LocationScatterCarpet). Кисть в окне Базы локаций дополняет слой
    // по мазку без пересборки места.
    public sealed partial class LocationWorldRenderer
    {
        private sealed class ScatterView
        {
            public LocationScatterLayer Layer;
            public Transform Root;
            public LocationScatterMode Mode;
            public bool Hidden;
            public readonly Dictionary<int, Placed> Objects = new Dictionary<int, Placed>();
            public LocationScatterCarpet Carpet;
        }

        private readonly Dictionary<string, ScatterView> scatter = new Dictionary<string, ScatterView>(StringComparer.Ordinal);
        private Material carpetMaterial;

        private void BuildScatter()
        {
            if (Definition.ScatterLayers == null) return;
            foreach (LocationScatterLayer layer in Definition.ScatterLayers)
                if (layer != null) BuildScatterLayer(layer);
        }

        private Material CarpetMaterial()
        {
            if (carpetMaterial != null) return carpetMaterial;
            Shader shader = Resources.Load<Shader>(AdjustShaderPath);
            if (shader == null) return null;
            carpetMaterial = Own(new Material(shader) { name = "Ковёр раскидки" });
            carpetMaterial.EnableKeyword("_KS_PARTICLE");
            return carpetMaterial;
        }

        private void BuildScatterLayer(LocationScatterLayer layer)
        {
            ScatterView view = new ScatterView { Layer = layer, Mode = layer.Mode, Hidden = layer.Hidden, Root = Child("Раскидка · " + layer.Name).transform };
            scatter[layer.Id] = view;
            if (layer.Hidden) return;
            if (layer.Mode == LocationScatterMode.Carpet)
            {
                view.Carpet = new LocationScatterCarpet(layer, Location, view.Root, CarpetMaterial());
                return;
            }
            foreach (LocationScatterInstance instance in layer.Instances)
                if (instance != null) AddScatterObject(view, instance);
        }

        private void AddScatterObject(ScatterView view, LocationScatterInstance instance)
        {
            LocationVisualObject item = view.Layer.ToObject(instance);
            Transform anchor = new GameObject(item.Name).transform;
            anchor.SetParent(view.Root, false);
            anchor.localPosition = LocationVisualGeometry.ToWorld(Location, item.Position);
            Placed entry = new Placed { Data = item, Anchor = anchor };
            placed.Add(entry);
            BuildObjectArt(entry, null);
            view.Objects[instance.Key] = entry;
        }

        private void RemovePlaced(Placed entry)
        {
            ClearObjectArt(entry);
            placed.Remove(entry);
            if (entry.Anchor != null) Destroy(entry.Anchor.gameObject);
        }

        private void RemoveScatterView(ScatterView view)
        {
            foreach (Placed entry in view.Objects.Values) RemovePlaced(entry);
            view.Objects.Clear();
            view.Carpet?.Clear();
            view.Carpet = null;
            if (view.Root != null) Destroy(view.Root.gameObject);
            scatter.Remove(view.Layer.Id);
        }

        // После мазка, ластика или перекраски: объекты — добавить новые,
        // убрать стёртые, пересобрать изменённые (changed); ковёр — собрать
        // сетку заново. Смена режима или видимости — слой целиком.
        public void RefreshScatter(LocationScatterLayer layer, ICollection<int> changed = null)
        {
            if (layer == null) return;
            if (!scatter.TryGetValue(layer.Id, out ScatterView view) || view.Mode != layer.Mode || view.Hidden != layer.Hidden)
            {
                RebuildScatterLayer(layer);
                return;
            }
            if (layer.Hidden) return;
            if (layer.Mode == LocationScatterMode.Carpet)
            {
                view.Carpet?.Rebuild();
                return;
            }
            HashSet<int> keys = new HashSet<int>();
            foreach (LocationScatterInstance instance in layer.Instances)
                if (instance != null) keys.Add(instance.Key);
            List<int> gone = new List<int>();
            foreach (KeyValuePair<int, Placed> pair in view.Objects)
                if (!keys.Contains(pair.Key) || (changed != null && changed.Contains(pair.Key))) gone.Add(pair.Key);
            foreach (int key in gone)
            {
                RemovePlaced(view.Objects[key]);
                view.Objects.Remove(key);
            }
            foreach (LocationScatterInstance instance in layer.Instances)
                if (instance != null && !view.Objects.ContainsKey(instance.Key)) AddScatterObject(view, instance);
            UpdateShadows();
        }

        // Слой целиком заново (смена режима, слоя рисунка, теней, видимости).
        public void RebuildScatterLayer(LocationScatterLayer layer)
        {
            if (layer == null) return;
            if (scatter.TryGetValue(layer.Id, out ScatterView view)) RemoveScatterView(view);
            BuildScatterLayer(layer);
            UpdateShadows();
        }

        private void AnimateScatter()
        {
            foreach (ScatterView view in scatter.Values) view.Carpet?.Animate(Seconds);
        }

        // Для окна и проверок.
        public int ScatterObjectCount(string layerId) => scatter.TryGetValue(layerId, out ScatterView view) ? view.Objects.Count : 0;
        public int ScatterCarpetCount(string layerId) => scatter.TryGetValue(layerId, out ScatterView view) ? view.Carpet?.Count ?? 0 : 0;
        public LocationScatterCarpet ScatterCarpet(string layerId) => scatter.TryGetValue(layerId, out ScatterView view) ? view.Carpet : null;

        public IReadOnlyList<SpriteRenderer> ScatterImages(string layerId, int key) =>
            scatter.TryGetValue(layerId, out ScatterView view) && view.Objects.TryGetValue(key, out Placed entry)
                ? entry.Images : (IReadOnlyList<SpriteRenderer>)Array.Empty<SpriteRenderer>();
    }
}
