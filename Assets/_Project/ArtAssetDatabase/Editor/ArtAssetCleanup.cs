using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    // 20.11.2026, просьба пользователя: убрать старые неиспользуемые записи
    // окружения (трава, деревья, пни, камни). Старая и неиспользуемая запись:
    //   - категория «Деревья», «Растительность» или «Камни»;
    //   - назначение «Только места» (генераторы карты и арены её не берут —
    //     прежние записи до одного вида);
    //   - ни одного размещения в местах (объекты и раскидка Базы локаций);
    //   - не слот объекта арены (тег arena-object:…).
    // Удаление: запись уходит из каталога, её файлы (рисунок, нормали, кадры) — в резервную папку Logs/ArtCleanupBackup/<дата>
    // (вне Assets, в Git не идёт); файл, на который ссылается оставшаяся
    // запись, не трогается.
    public static class ArtAssetCleanup
    {
        private static readonly ArtAssetCategory[] Categories = { ArtAssetCategory.Trees, ArtAssetCategory.Vegetation, ArtAssetCategory.Rocks };

        public static List<ArtAssetDefinition> Candidates(ArtAssetDatabaseAsset catalog)
        {
            if (catalog?.assets == null) return new List<ArtAssetDefinition>();
            Dictionary<string, int> usages = ArtAssetUsages.Counts();
            return catalog.assets.Where(asset => asset != null && Categories.Contains(asset.Category) && asset.Purpose == ArtAssetPurpose.LocationsOnly &&
                                                 !usages.ContainsKey(asset.Id) &&
                                                 (asset.Tags == null || !asset.Tags.Any(tag => tag != null && tag.StartsWith("arena-object:"))))
                .ToList();
        }

        // Пути файлов записи (рисунки, нормали, кадры, листы).
        public static HashSet<string> FilesOf(ArtAssetDefinition asset)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(UnityEngine.Object item)
            {
                string path = item != null ? AssetDatabase.GetAssetPath(item) : null;
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/")) paths.Add(path);
            }
            foreach (ArtAssetPart part in asset.Parts ?? new List<ArtAssetPart>())
            foreach (ArtAssetPartView view in part?.Views ?? new List<ArtAssetPartView>())
            {
                if (view == null) continue;
                Add(view.Sprite);
                Add(view.NormalMap);
                Add(view.ShadowSprite);
                foreach (ArtAssetFrame frame in view.Frames ?? new List<ArtAssetFrame>()) { Add(frame?.Sprite); Add(frame?.NormalMap); }
                foreach (Sprite sprite in view.SheetFrames ?? new List<Sprite>()) Add(sprite);
                foreach (Texture2D normal in view.SheetNormals ?? new List<Texture2D>()) Add(normal);
            }
            return paths;
        }

        public static string Report(ArtAssetDatabaseAsset catalog, out List<ArtAssetDefinition> candidates)
        {
            candidates = Candidates(catalog);
            StringBuilder text = new StringBuilder();
            text.AppendLine("Старые неиспользуемые записи окружения: " + candidates.Count + " из " + (catalog?.assets?.Count ?? 0));
            foreach (ArtAssetDefinition asset in candidates.OrderBy(item => item.Category).ThenBy(item => item.Name))
                text.AppendLine(" - [" + asset.Category + "] " + asset.Name + " (" + asset.Id + "), файлов: " + FilesOf(asset).Count);
            Dictionary<string, int> usages = ArtAssetUsages.Counts();
            List<ArtAssetDefinition> removing = candidates;
            List<ArtAssetDefinition> kept = (catalog?.assets ?? new List<ArtAssetDefinition>()).Where(item => item != null && item.Purpose == ArtAssetPurpose.LocationsOnly && !removing.Contains(item)).ToList();
            text.AppendLine("Остаются (\"Только места\", стоят в местах): " + kept.Count);
            foreach (ArtAssetDefinition asset in kept.OrderBy(item => item.Category).ThenBy(item => item.Name))
                text.AppendLine(" - [" + asset.Category + "] " + asset.Name + " — размещений: " + (usages.TryGetValue(asset.Id, out int count) ? count : 0));
            return text.ToString();
        }

        [MenuItem("Kingdom Survival/Служебное/База ассетов: убрать старые неиспользуемые записи окружения…")]
        public static void RemoveWithDialog()
        {
            ArtAssetDatabaseAsset catalog = ArtAssetDatabaseAsset.Current;
            string report = Report(catalog, out List<ArtAssetDefinition> candidates);
            if (candidates.Count == 0) { EditorUtility.DisplayDialog("Уборка Базы ассетов", "Старых неиспользуемых записей нет.", "Понятно"); return; }
            if (!EditorUtility.DisplayDialog("Уборка Базы ассетов", report.Length > 1800 ? report.Substring(0, 1800) + "…" : report, "Удалить (файлы — в резерв)", "Отмена")) return;
            Debug.Log(Remove(catalog, candidates));
        }

        public static string Remove(ArtAssetDatabaseAsset catalog, List<ArtAssetDefinition> candidates)
        {
            if (catalog == null || candidates == null || candidates.Count == 0) return "Нечего удалять.";
            HashSet<string> removedIds = new HashSet<string>(candidates.Select(item => item.Id));
            HashSet<string> keptFiles = new HashSet<string>(catalog.assets.Where(item => item != null && !removedIds.Contains(item.Id)).SelectMany(FilesOf),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> files = new HashSet<string>(candidates.SelectMany(FilesOf).Where(path => !keptFiles.Contains(path)), StringComparer.OrdinalIgnoreCase);
            string backup = Path.Combine("Logs", "ArtCleanupBackup", DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
            int moved = 0;
            foreach (string path in files)
            {
                string target = Path.Combine(backup, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? backup);
                File.Copy(path, target, true);
                if (File.Exists(path + ".meta")) File.Copy(path + ".meta", target + ".meta", true);
                moved++;
            }
            File.WriteAllText(Path.Combine(backup, "removed_assets.json"),
                "{\"assets\":[" + string.Join(",", candidates.Select(item => JsonUtility.ToJson(item))) + "]}");
            catalog.assets.RemoveAll(item => item != null && removedIds.Contains(item.Id));
            EditorUtility.SetDirty(catalog);
            catalog.MarkChanged();
            AssetDatabase.SaveAssetIfDirty(catalog);
            HashSet<string> folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                AssetDatabase.DeleteAsset(path);
                folders.Add(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            }
            foreach (string folder in folders.Where(item => !string.IsNullOrEmpty(item) && item.StartsWith("Assets/_Project/Art/Assets/")))
                if (AssetDatabase.IsValidFolder(folder) && Directory.GetFiles(folder).All(file => file.EndsWith(".meta")) && Directory.GetDirectories(folder).Length == 0)
                    AssetDatabase.DeleteAsset(folder);
            AssetDatabase.Refresh();
            return "База ассетов: удалено записей " + candidates.Count + ", файлов " + moved + " (копии — " + backup.Replace('\\', '/') + ").";
        }

        // batchmode: отчёт в журнал и Logs/art-cleanup-report.txt.
        public static void ReportBatch()
        {
            string report = Report(ArtAssetDatabaseAsset.Current, out _);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Path.Combine("Logs", "art-cleanup-report.txt"), report);
            Debug.Log(report);
        }

        // batchmode: удалить без вопросов (для подтверждённой уборки).
        public static void RemoveBatch()
        {
            ArtAssetDatabaseAsset catalog = ArtAssetDatabaseAsset.Current;
            Debug.Log(Remove(catalog, Candidates(catalog)));
        }
    }
}
