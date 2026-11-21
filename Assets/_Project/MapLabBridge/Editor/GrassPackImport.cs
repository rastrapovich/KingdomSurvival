using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.MapLab.Editor;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Editor
{
    // Пакет луговой травы «15 вариантов × 3 вида» (папка с CATALOG_RU.csv,
    // classic/, gloom/, sepia/):
    //   • classic → записи Базы ассетов через ArtAssetPackImport (группа и высота
    //     из CSV, назначение «Карта и арена», теги grass-pack-15 и Grass_<ID>);
    //   • gloom/sepia → Art/MapViews/<вид>/art_<ID записи>.png (имя Grass_* —
    //     ID поставки, не ID записи Базы) и сбор рисунков видов.
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
            string folder = EditorUtility.OpenFolderPanel("Папка пакета травы (CATALOG_RU.csv, classic, gloom, sepia)", "", "");
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
            int copied = 0;
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
                foreach (string view in new[] { "gloom", "sepia" })
                {
                    string source = Path.Combine(folder, view, entry.Id + "_" + view + ".png");
                    if (!File.Exists(source))
                    {
                        report.Add(entry.Id + ": нет " + view);
                        continue;
                    }
                    string targetFolder = "Assets/_Project/Art/MapViews/" + view;
                    Directory.CreateDirectory(targetFolder);
                    // Кайма вида очищается так же, как у classic; альфа и холст прежние.
                    ArtAssetPackImport.Defringe(source, Path.Combine(targetFolder, "art_" + asset.Id + ".png"));
                    copied++;
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            MapLabGeneratorWindow.CollectViewArtBatch();
            report.Add("рисунков видов скопировано: " + copied);
            return string.Join("; ", report);
        }
    }
}
