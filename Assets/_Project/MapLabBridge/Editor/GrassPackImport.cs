using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.ArtAssets.Editor;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Editor
{
    // Пакет луговой травы «15 вариантов» (папка с CATALOG_RU.csv и classic/):
    //   • classic → записи Базы ассетов через ArtAssetPackImport (группа и высота
    //     из CSV, назначение «Карта и арена», теги grass-pack-15 и Grass_<ID>);
    // Повторный запуск обновляет те же записи (по ключу пакета).
    //   -executeMethod KingdomSurvival.MapLabBridge.Editor.GrassPackImport.ImportBatch -grassFolder <папка>
    public static class GrassPackImport
    {
        private const string Staging = "Temp/GrassPackStaging";

        public static void ImportBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-grassFolder");
            string folder = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            Debug.Log("GRASS_PACK: " + Import(folder));
        }

        [MenuItem("Kingdom Survival/Арена/Импорт луговой травы…")]
        private static void ImportMenu()
        {
            string folder = EditorUtility.OpenFolderPanel("Папка пакета травы (CATALOG_RU.csv, classic)", "", "");
            if (!string.IsNullOrEmpty(folder))
                EditorUtility.DisplayDialog("Луговая трава", Import(folder), "OK");
        }

        public static string Import(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, "CATALOG_RU.csv")) || !Directory.Exists(Path.Combine(folder, "classic")))
                return "Нет CATALOG_RU.csv или папки classic в «" + folder + "».";
            // ArtAssetPackImport ищет PNG рядом с CSV — собираем classic в рабочую папку.
            string staging = Path.GetFullPath(Staging);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            File.Copy(Path.Combine(folder, "CATALOG_RU.csv"), Path.Combine(staging, "CATALOG_RU.csv"), true);
            foreach (string png in Directory.GetFiles(Path.Combine(folder, "classic"), "*.png"))
                File.Copy(png, Path.Combine(staging, Path.GetFileName(png)), true);

            string imported = ArtAssetPackImport.Import(staging);
            ArtAssetDatabaseAsset catalog = AssetDatabase.LoadAssetAtPath<ArtAssetDatabaseAsset>(ArtAssetDatabaseAsset.AssetPath);
            List<string> report = new List<string> { imported };
            foreach (ArtAssetPackImport.Entry entry in ArtAssetPackImport.ReadCatalog(staging))
            {
                string key = ArtAssetPackImport.PackKey(entry);
                ArtAssetDefinition asset = catalog.assets.FirstOrDefault(a => a != null && string.Equals(a.ImportKey, key, StringComparison.OrdinalIgnoreCase));
                if (asset == null)
                {
                    report.Add(entry.Id + ": нет записи");
                    continue;
                }
                asset.Purpose = ArtAssetPurpose.ArenaOnly;
                EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return string.Join("; ", report);
        }

        // Трава глобальной карты (набор этапа 05, роли grass_tuft и grass_ink_tuft),
        // которую арена брала для ковра прямо из набора: переносится в Базу ассетов
        // записями «MapGrass_*» («Только арена»), чтобы управлять ею как остальными
        // (размер, приоритет).
        //   -executeMethod KingdomSurvival.MapLabBridge.Editor.GrassPackImport.ImportMapGrassBatch
        private const float MapGrassHeight = 0.3f;

        public static void ImportMapGrassBatch() => Debug.Log("MAP_GRASS: " + ImportMapGrass());

        [MenuItem("Kingdom Survival/Арена/Перенести траву карты в Базу ассетов")]
        private static void ImportMapGrassMenu() => EditorUtility.DisplayDialog("Трава карты", ImportMapGrass(), "OK");

        public static string ImportMapGrass()
        {
            string[] folders = { "Assets/_Project/Art/MapLabStage05/Grass", "Assets/_Project/Art/MapLabStage05/GrassInk" };
            string staging = Path.GetFullPath(Staging + "Map");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            List<string> csv = new List<string> { "ID;Название;Категория;Файл;Группа;Высота_м" };
            List<(string Id, string Stem)> items = new List<(string, string)>();
            foreach (string dir in folders)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string png in Directory.GetFiles(dir, "*.png").OrderBy(p => p, StringComparer.Ordinal))
                {
                    string stem = Path.GetFileNameWithoutExtension(png);
                    string id = "MapGrass_" + (stem.StartsWith("GrassInk_") ? "Ink_" + stem.Substring("GrassInk_".Length) : stem.StartsWith("Grass_") ? stem.Substring("Grass_".Length) : stem);
                    string name = stem.StartsWith("GrassInk_") ? "Трава карты: контурный пучок, " + stem.Substring("GrassInk_".Length).Replace('_', ' ') : "Трава карты: пучок, " + stem.Substring("Grass_".Length).Replace('_', ' ');
                    File.Copy(png, Path.Combine(staging, id + ".png"), true);
                    csv.Add(string.Join(";", id, name, "Vegetation", id + ".png", "низкая", MapGrassHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    items.Add((id, stem));
                }
            }
            if (items.Count == 0) return "Нет рисунков травы карты в Art/MapLabStage05.";
            File.WriteAllLines(Path.Combine(staging, "CATALOG_RU.csv"), csv, new System.Text.UTF8Encoding(false));
            string imported = ArtAssetPackImport.Import(staging);
            ArtAssetDatabaseAsset catalog = AssetDatabase.LoadAssetAtPath<ArtAssetDatabaseAsset>(ArtAssetDatabaseAsset.AssetPath);
            foreach ((string id, string stem) in items)
            {
                string key = ArtAssetPackImport.PackKey(new ArtAssetPackImport.Entry { Id = id });
                ArtAssetDefinition asset = catalog.assets.FirstOrDefault(a => a != null && string.Equals(a.ImportKey, key, StringComparison.OrdinalIgnoreCase));
                if (asset == null) continue;
                asset.Purpose = ArtAssetPurpose.ArenaOnly;
                EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return imported;
        }
    }
}
