using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // Канон v1.58 §9.9: служебное для генератора карты — создать ассет
    // генератора и подключить его к базе карты, сохранить рисунок в PNG и
    // выгрузить образцы из командной строки (проверка без открытого окна).
    public static class WorldMapGeneratorTools
    {
        public static WorldMapGeneratorAsset EnsureGeneratorAsset(WorldMapDatabaseAsset database)
        {
            if (database != null && database.Generator != null)
                return database.Generator;

            WorldMapGeneratorAsset asset = AssetDatabase.LoadAssetAtPath<WorldMapGeneratorAsset>(WorldMapGeneratorAsset.AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<WorldMapGeneratorAsset>();
                AssetDatabase.CreateAsset(asset, WorldMapGeneratorAsset.AssetPath);
            }

            if (database != null)
            {
                Undo.RecordObject(database, "Подключить генератор карты");
                database.Generator = asset;
                EditorUtility.SetDirty(database);
            }
            AssetDatabase.SaveAssets();
            return asset;
        }

        public static void SavePng(Texture2D texture, string path)
        {
            if (texture == null || string.IsNullOrEmpty(path))
                return;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }

        // Unity.exe -batchmode -executeMethod
        //   KingdomSurvival.WorldMapVisual.Editor.WorldMapGeneratorTools.ExportSamplesBatch
        //   -ksOut <папка> [-ksSeeds 1,2,3] [-ksStages]
        // Пишет PNG готовых карт (и, с -ksStages, каждой стадии первой карты).
        public static void ExportSamplesBatch()
        {
            string output = Argument("-ksOut") ?? Path.Combine(Application.temporaryCachePath, "WorldMapSamples");
            string seedsText = Argument("-ksSeeds") ?? "1,2,3";
            bool stages = Array.IndexOf(Environment.GetCommandLineArgs(), "-ksStages") >= 0;

            WorldMapDatabaseAsset database = Resources.Load<WorldMapDatabaseAsset>(WorldMapDatabaseAsset.ResourcesPath);
            WorldMapGeneratorAsset generator = EnsureGeneratorAsset(database);
            int index = 0;
            foreach (string part in seedsText.Split(','))
            {
                if (!int.TryParse(part.Trim(), out int seed))
                    continue;
                DateTime started = DateTime.Now;
                GeneratedWorldMap map = generator.Generate(seed);
                double generateSeconds = (DateTime.Now - started).TotalSeconds;
                started = DateTime.Now;
                Texture2D texture = WorldMapPainter.Paint(map, generator.Art, 0f, Array.IndexOf(Environment.GetCommandLineArgs(), "-ksHidden") >= 0);
                double paintSeconds = (DateTime.Now - started).TotalSeconds;
                SavePng(texture, Path.Combine(output, "map-" + seed + ".png"));
                Debug.Log($"[WorldMapGenerator] seed {seed}: {texture.width}×{texture.height}, генерация {generateSeconds:0.00} с, рисунок {paintSeconds:0.00} с, " +
                          $"рек {map.Rivers.Count}, дорог {map.Roads.Count}, деревень {map.Villages.Count}, мест {map.Landmarks.Count}, слотов {map.SpawnSlots.Count}");
                UnityEngine.Object.DestroyImmediate(texture);

                if (stages && index == 0)
                {
                    foreach (WorldMapGenerationStage stage in WorldMapGenerationStages.All)
                    {
                        GeneratedWorldMap partial = generator.Generate(seed, stage);
                        Texture2D stageTexture = WorldMapPainter.Paint(partial, generator.Art, 0.25f);
                        SavePng(stageTexture, Path.Combine(output, "map-" + seed + "-stage-" + (int)stage + ".png"));
                        UnityEngine.Object.DestroyImmediate(stageTexture);
                    }
                }
                index++;
            }
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }
    }

    // Картинка переимпортирована (перерисована, другие настройки импорта) —
    // память рисунков карты сбрасывается, следующий рисунок читает её заново.
    internal sealed class WorldMapPainterCacheReset : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".psd" || extension == ".tga")
                {
                    WorldMapPainter.ClearCaches();
                    return;
                }
            }
        }
    }
}
