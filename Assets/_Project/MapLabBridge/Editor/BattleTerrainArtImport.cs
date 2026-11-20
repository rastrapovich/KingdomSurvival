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
    // Бриф «Поверхности арены»: импорт набора объектов местности арены
    // (BATTLE_TERRAIN_ART_REQUEST.md) — папка с classic/, gloom/, sepia/.
    //   • classic → запись Базы ассетов: «Только арена», PPU 108, опора из
    //     набора (центр, 12 % высоты), тег слота arena-object:<вид>;
    //   • gloom/sepia → Art/MapViews/<вид>/art_<ID записи>.png и сбор рисунков
    //     видов. Повторный запуск обновляет те же записи (по имени).
    //   -executeMethod KingdomSurvival.MapLabBridge.Editor.BattleTerrainArtImport.ImportBatch -terrainArtFolder <папка>
    public static class BattleTerrainArtImport
    {
        private const float PivotY = 0.119140625f;

        private static readonly (string File, string Kind, ArtAssetCategory Category)[] Set =
        {
            ("Arena_StoneWall_A", "stone_wall", ArtAssetCategory.Rocks),
            ("Arena_StoneWall_B", "stone_wall", ArtAssetCategory.Rocks),
            ("Arena_FallenLog_A", "fallen_log", ArtAssetCategory.Items),
            ("Arena_FallenLog_B", "fallen_log", ArtAssetCategory.Items),
            ("Arena_Deadfall_A", "deadfall", ArtAssetCategory.Items),
            ("Arena_Snag_A", "trunk", ArtAssetCategory.Items),
            ("Arena_Snag_B", "trunk", ArtAssetCategory.Items),
            ("Arena_BridgeDeck_A", "bridge", ArtAssetCategory.Items),
            ("Arena_Wattle_A", "wattle", ArtAssetCategory.Items),
            ("Arena_Wattle_Broken_A", "wattle_broken", ArtAssetCategory.Items),
        };

        public static void ImportBatch()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int index = System.Array.IndexOf(args, "-terrainArtFolder");
            string folder = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            Debug.Log("TERRAIN_ART: " + Import(folder));
        }

        [MenuItem("Kingdom Survival/Арена/Импорт объектов местности…")]
        private static void ImportMenu()
        {
            string folder = EditorUtility.OpenFolderPanel("Папка набора (classic, gloom, sepia)", "", "");
            if (!string.IsNullOrEmpty(folder))
                EditorUtility.DisplayDialog("Объекты местности арены", Import(folder), "OK");
        }

        public static string Import(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(Path.Combine(folder, "classic")))
                return "Нет папки classic в «" + folder + "».";
            ArtAssetDatabaseAsset catalog = ArtAssetImporter.LoadOrCreateCatalog();
            List<string> report = new List<string>();
            foreach ((string file, string kind, ArtAssetCategory category) in Set)
            {
                string classic = Path.Combine(folder, "classic", file + ".png");
                if (!File.Exists(classic))
                {
                    report.Add(file + ": нет classic");
                    continue;
                }
                ArtAssetDefinition existing = catalog.assets.FirstOrDefault(a => a != null && a.Name == file);
                ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { classic }, file, ArtAssetView.Front);
                ArtAssetImportResult result = ArtAssetImporter.Import(catalog, plan, _ => existing, false);
                string id = result.Created.Concat(result.Updated).FirstOrDefault();
                ArtAssetDefinition asset = id != null ? catalog.Find(id) : null;
                if (asset == null)
                {
                    report.Add(file + ": не импортирован (" + string.Join("; ", result.Errors) + ")");
                    continue;
                }
                asset.Category = category;
                asset.Purpose = ArtAssetPurpose.ArenaOnly;
                asset.PixelsPerUnit = ArtAssetDefinition.DefaultPixelsPerUnit;
                asset.Settings(asset.PrimaryView).Pivot = new Vector2(0.5f, PivotY);
                asset.Tags ??= new List<string>();
                asset.Tags.RemoveAll(tag => tag != null && tag.StartsWith(MapLabArtAssets.ArenaObjectTagPrefix));
                asset.Tags.Add(MapLabArtAssets.ArenaObjectTagPrefix + kind);
                foreach (string view in new[] { "gloom", "sepia" })
                {
                    string source = Path.Combine(folder, view, file + "_" + view + ".png");
                    if (!File.Exists(source))
                        continue;
                    string targetFolder = "Assets/_Project/Art/MapViews/" + view;
                    Directory.CreateDirectory(targetFolder);
                    File.Copy(source, Path.Combine(targetFolder, "art_" + asset.Id + ".png"), true);
                }
                report.Add(file + " → " + asset.Id + " (" + kind + ")");
            }
            catalog.MarkChanged();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            MapLabGeneratorWindow.CollectViewArtBatch();
            return string.Join("; ", report);
        }
    }
}
