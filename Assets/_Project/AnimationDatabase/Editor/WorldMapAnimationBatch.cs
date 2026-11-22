using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    // Пакетный импорт фигуры глобальной карты (Unity -batchmode):
    //   Unity.exe -batchmode -nographics -quit -projectPath <проект>
    //     -executeMethod KingdomSurvival.AnimationDatabase.Editor.WorldMapAnimationBatch.ImportFolderBatch
    //     -wmaFolder <папка пакета или персонажа: внутри Walk/<ракурс>/*.png>
    //     [-wmaSet hero] [-wmaName Герой] [-wmaFps 12] [-wmaPivotX 0.5] [-wmaPivotY 0.12109375]
    //     [-wmaHeight 0.4] [-wmaStride 0.5] [-wmaHero 1]
    // Код возврата 0 — набор импортирован и сохранён, 1 — ошибка (причина в логе).
    public static class WorldMapAnimationBatch
    {
        public const string LogPrefix = "[WorldMapAnimationBatch]";

        public sealed class Options
        {
            public string Folder = string.Empty;
            public string SetId = WorldMapAnimationDatabaseAsset.DefaultHeroSetId;
            public string DisplayName;
            public float? FramesPerSecond;
            public float? PivotX;
            public float? PivotY;
            public float? HeightInHexes;
            public float? StrideHexesPerCycle;
            public bool MakeHero;
        }

        public static void ImportFolderBatch()
        {
            int exitCode = 0;
            try
            {
                Options options = ParseArguments(Environment.GetCommandLineArgs(), out string argumentError);
                if (argumentError != null)
                {
                    Debug.LogError(LogPrefix + " " + argumentError);
                    exitCode = 1;
                }
                else
                {
                    WorldMapAnimationDatabaseAsset database = WorldMapAnimationEditorData.LoadOrCreate();
                    if (Import(database, options, out string report))
                    {
                        AssetDatabase.SaveAssets();
                        Debug.Log(LogPrefix + " Готово.\n" + report);
                    }
                    else
                    {
                        Debug.LogError(LogPrefix + " Импорт не выполнен.\n" + report);
                        exitCode = 1;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(LogPrefix + " Исключение: " + exception);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        public static Options ParseArguments(string[] args, out string error)
        {
            error = null;
            Options options = new Options();
            string folder = ArgumentValue(args, "-wmaFolder");
            if (string.IsNullOrWhiteSpace(folder))
            {
                error = "Не задан -wmaFolder <папка пакета>.";
                return options;
            }
            options.Folder = folder;
            string setId = ArgumentValue(args, "-wmaSet");
            if (!string.IsNullOrWhiteSpace(setId))
                options.SetId = setId.Trim();
            options.DisplayName = ArgumentValue(args, "-wmaName");
            options.FramesPerSecond = ParseFloat(args, "-wmaFps", ref error);
            options.PivotX = ParseFloat(args, "-wmaPivotX", ref error);
            options.PivotY = ParseFloat(args, "-wmaPivotY", ref error);
            options.HeightInHexes = ParseFloat(args, "-wmaHeight", ref error);
            options.StrideHexesPerCycle = ParseFloat(args, "-wmaStride", ref error);
            string hero = ArgumentValue(args, "-wmaHero");
            options.MakeHero = hero == "1" || string.Equals(hero, "true", StringComparison.OrdinalIgnoreCase);
            return options;
        }

        private static string ArgumentValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        private static float? ParseFloat(string[] args, string name, ref string error)
        {
            string text = ArgumentValue(args, name);
            if (text == null)
                return null;
            if (float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return value;
            error = "Не число в " + name + ": " + text;
            return null;
        }

        // Ядро: создаёт набор при необходимости, заменяет его кадры из папки,
        // применяет числа. Файл базы сохраняет вызывающий (в тестах база
        // существует только в памяти).
        public static bool Import(WorldMapAnimationDatabaseAsset database, Options options, out string report)
        {
            report = string.Empty;
            if (database == null)
            {
                report = "Нет базы анимаций карты.";
                return false;
            }
            if (!Directory.Exists(options.Folder))
            {
                report = "Папка не найдена: " + options.Folder;
                return false;
            }

            string[] files = Directory.GetFiles(options.Folder, "*.*", SearchOption.AllDirectories);
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(options.Folder, files);
            WorldMapAnimationSection.PreparePackage(package);
            if (!package.ChosenGroups.Any(group => group.ChosenAction == CreatureAnimationAction.Walk))
                package.AddError("В папке не найдено действие «Ходьба» (Walk/<ракурс>/*.png).");
            if (package.HasErrors)
            {
                report = string.Join("\n", package.Issues.Select(issue => issue.ToString()));
                return false;
            }

            WorldMapAnimationSetData set = database.FindSet(options.SetId) ?? database.AddSet(options.SetId, options.DisplayName ?? "Герой");
            if (!string.IsNullOrWhiteSpace(options.DisplayName))
                set.Animation.SetDisplayName(options.DisplayName);

            if (!CreatureAnimationImporter.ApplyToWorldMap(database, set, package, CreatureAnimationImportMode.Replace, out string importReport))
            {
                report = string.Join("\n", package.Issues.Select(issue => issue.ToString()));
                return false;
            }

            CreatureAnimationClipData walk = set.Animation.FindClip(CreatureAnimationAction.Walk);
            if (walk != null)
            {
                if (options.FramesPerSecond.HasValue)
                    walk.SetFramesPerSecond(Mathf.Clamp(options.FramesPerSecond.Value, CreatureAnimationClipData.MinFramesPerSecond, CreatureAnimationClipData.MaxFramesPerSecond));
                walk.SetPlayback(CreatureAnimationPlayback.Loop);
            }
            if (options.PivotX.HasValue || options.PivotY.HasValue)
            {
                Vector2 pivot = set.Animation.Pivot;
                set.Animation.SetPivot(new Vector2(
                    Mathf.Clamp01(options.PivotX ?? pivot.x),
                    Mathf.Clamp01(options.PivotY ?? pivot.y)));
            }
            if (options.HeightInHexes.HasValue)
                set.SetHeightInHexes(options.HeightInHexes.Value);
            if (options.StrideHexesPerCycle.HasValue)
                set.SetStrideHexesPerCycle(options.StrideHexesPerCycle.Value);
            if (options.MakeHero)
                database.SetHeroSetId(set.Id);
            EditorUtility.SetDirty(database);

            List<string> lines = new List<string>
            {
                importReport,
                "Набор: " + set.Id + " «" + set.DisplayName + "» · готово (Ходьба во всех ракурсах): " + set.HasCompleteWalk,
                "Холст: " + set.Animation.CanvasSize.x + "×" + set.Animation.CanvasSize.y +
                " · опора " + set.Animation.Pivot.x.ToString("0.#####", CultureInfo.InvariantCulture) + "; " +
                set.Animation.Pivot.y.ToString("0.#####", CultureInfo.InvariantCulture),
                "Ходьба: " + (walk != null ? walk.FramesPerSecond.ToString("0.##", CultureInfo.InvariantCulture) + " к/с, " + CreatureAnimationLabels.PlaybackTitle(walk.Playback) +
                    ", пропуск последних: " + walk.SkipLastFrames : "нет"),
                "Рост на карте: " + set.HeightInHexes.ToString("0.###", CultureInfo.InvariantCulture) + " клеток · шаг: " +
                set.StrideHexesPerCycle.ToString("0.###", CultureInfo.InvariantCulture) + " клеток/цикл",
                "Герой на карте: " + database.HeroSetId
            };
            if (package.Issues.Count > 0)
                lines.AddRange(package.Issues.Select(issue => issue.ToString()));
            report = string.Join("\n", lines);
            return true;
        }
    }
}
