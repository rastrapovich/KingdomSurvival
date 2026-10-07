using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Одно использование записи каталога: где и как к нему перейти.
    public sealed class ArtAssetUsage
    {
        public string AssetId;
        public string Database;
        public string Owner;
        public string OwnerId;
        public string Element;
        public string ElementId;
        public Action Open;

        public string Title => Database + " · " + Owner + " · " + Element;
    }

    // ПР-12Н: «Где используется». Ссылки считаются по всем сохранённым данным
    // (а не по открытым окнам). Другие базы, которым понадобятся объекты
    // окружения, добавляют сюда своего поставщика.
    public static class ArtAssetUsages
    {
        public static readonly List<Func<IEnumerable<ArtAssetUsage>>> Providers = new List<Func<IEnumerable<ArtAssetUsage>>> { LocationUsages };

        // Переход к месту в Базе локаций; окно подписывается само (без
        // зависимости модуля ассетов от модуля окна мест).
        public static event Action<string, string> LocationRequested;
        public static string PendingLocationId, PendingObjectId;

        public static List<ArtAssetUsage> All()
        {
            List<ArtAssetUsage> result = new List<ArtAssetUsage>();
            foreach (Func<IEnumerable<ArtAssetUsage>> provider in Providers)
                result.AddRange(provider() ?? Enumerable.Empty<ArtAssetUsage>());
            return result;
        }

        public static List<ArtAssetUsage> Find(string assetId) => All().Where(item => item.AssetId == assetId).ToList();

        public static Dictionary<string, int> Counts()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ArtAssetUsage usage in All())
                counts[usage.AssetId] = counts.TryGetValue(usage.AssetId, out int count) ? count + 1 : 1;
            return counts;
        }

        public static IEnumerable<LocalLocationDatabaseAsset> LocationDatabases() =>
            AssetDatabase.FindAssets("t:" + nameof(LocalLocationDatabaseAsset))
                .Select(guid => AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(item => item != null);

        // Удаление записи: только без ссылок. Файлы рисунков не удаляются.
        public static bool TryDelete(ArtAssetDatabaseAsset catalog, ArtAssetDefinition asset, out List<ArtAssetUsage> usages)
        {
            usages = asset != null ? Find(asset.Id) : new List<ArtAssetUsage>();
            if (catalog == null || asset == null || usages.Count > 0) return false;
            Undo.RecordObject(catalog, "Удалить ассет");
            catalog.assets.Remove(asset);
            EditorUtility.SetDirty(catalog);
            catalog.MarkChanged();
            return true;
        }

        private static IEnumerable<ArtAssetUsage> LocationUsages() => LocationUsages(LocationDatabases());

        public static IEnumerable<ArtAssetUsage> LocationUsages(IEnumerable<LocalLocationDatabaseAsset> databases)
        {
            foreach (LocalLocationDatabaseAsset database in databases)
            {
                foreach (LocationVisualDefinition visual in database.visuals.Where(item => item != null))
                {
                    string name = database.locations.Find(item => item.Id == visual.LocationId)?.DisplayName ?? visual.LocationId;
                    foreach (LocationVisualObject item in visual.Objects.Where(item => item != null && item.UsesAsset))
                    {
                        string locationId = visual.LocationId, objectId = item.Id;
                        yield return new ArtAssetUsage
                        {
                            AssetId = item.AssetId, Database = "База локаций", Owner = name, OwnerId = locationId,
                            Element = item.Name + " · " + ArtAssetLabels.ViewTitle(item.View), ElementId = objectId,
                            Open = () => OpenLocation(locationId, objectId)
                        };
                    }
                    // ПР-12Р: раскидка — одна строка на ассет слоя (кисть или экземпляры).
                    foreach (LocationScatterLayer layer in (visual.ScatterLayers ?? new List<LocationScatterLayer>()).Where(item => item != null))
                    {
                        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (LocationScatterEntry entry in layer.Assets.Where(item => item != null && !string.IsNullOrEmpty(item.AssetId)))
                            if (!counts.ContainsKey(entry.AssetId)) counts[entry.AssetId] = 0;
                        foreach (LocationScatterInstance instance in layer.Instances.Where(item => item != null && !string.IsNullOrEmpty(item.AssetId)))
                            counts[instance.AssetId] = counts.TryGetValue(instance.AssetId, out int count) ? count + 1 : 1;
                        string locationId = visual.LocationId, layerName = layer.Name;
                        foreach (KeyValuePair<string, int> pair in counts)
                            yield return new ArtAssetUsage
                            {
                                AssetId = pair.Key, Database = "База локаций", Owner = name, OwnerId = locationId,
                                Element = "раскидка «" + layerName + "» · " + pair.Value + " шт.", ElementId = null,
                                Open = () => OpenLocation(locationId, null)
                            };
                    }
                }
            }
        }

        public static void OpenLocation(string locationId, string objectId)
        {
            PendingLocationId = locationId;
            PendingObjectId = objectId;
            EditorApplication.ExecuteMenuItem("Kingdom Survival/База локаций");
            LocationRequested?.Invoke(locationId, objectId);
        }

        // Перевести все экземпляры с одного ассета на другой: ID экземпляров,
        // опоры, ракурсы и локальные переопределения сохраняются.
        public static int ReplaceEverywhere(string fromId, string toId)
        {
            int count = 0;
            foreach (LocalLocationDatabaseAsset database in LocationDatabases())
            {
                bool touched = false;
                foreach (LocationVisualObject item in database.visuals.Where(item => item != null).SelectMany(visual => visual.Objects))
                {
                    if (item == null || item.AssetId != fromId) continue;
                    if (!touched) { Undo.RecordObject(database, "Заменить ассет во всех местах"); touched = true; }
                    item.AssetId = toId;
                    count++;
                }
                foreach (LocationScatterLayer layer in database.visuals.Where(item => item?.ScatterLayers != null).SelectMany(visual => visual.ScatterLayers))
                {
                    if (layer == null) continue;
                    foreach (LocationScatterEntry entry in layer.Assets.Where(item => item != null && item.AssetId == fromId))
                    {
                        if (!touched) { Undo.RecordObject(database, "Заменить ассет во всех местах"); touched = true; }
                        entry.AssetId = toId;
                    }
                    foreach (LocationScatterInstance instance in layer.Instances.Where(item => item != null && item.AssetId == fromId))
                    {
                        if (!touched) { Undo.RecordObject(database, "Заменить ассет во всех местах"); touched = true; }
                        instance.AssetId = toId;
                        count++;
                    }
                }
                if (touched) { EditorUtility.SetDirty(database); AssetDatabase.SaveAssetIfDirty(database); }
            }
            return count;
        }
    }
}
