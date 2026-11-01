using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KingdomSurvival.BattleSandbox.Editor
{
    // Windows-сборка основной игры: Prototype_Main и BattleSandbox (бои
    // кампании грузят её по имени). Профиль Build Profiles → Windows намеренно
    // собирает только песочницу боя и здесь не трогается — сцены заданы явно.
    // Результат — папка Build/KingdomSurvival (в .gitignore); передавать целиком.
    public static class MainGameBuild
    {
        private static readonly string[] Scenes =
        {
            "Assets/_Project/Scenes/Prototype_Main.unity",
            "Assets/_Project/Scenes/BattleSandbox.unity"
        };

        private const string OutputFolder = "Build/KingdomSurvival";
        private const string ExeName = "KingdomSurvival.exe";

        [MenuItem("Kingdom Survival/Сборка/Основная игра (Windows)")]
        private static void BuildFromMenu()
        {
            BuildReport report = Build();
            if (report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(Path.Combine(OutputFolder, ExeName));
            else
                EditorUtility.DisplayDialog("Сборка игры", "Сборка не удалась: " + report.summary.result + ". Подробности — в Console.", "OK");
        }

        // Командная строка: Unity.exe -batchmode -buildTarget Win64 -projectPath <путь>
        //   -executeMethod KingdomSurvival.BattleSandbox.Editor.MainGameBuild.BuildBatch
        public static void BuildBatch()
        {
            BuildReport report = Build();
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static BuildReport Build()
        {
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = Path.Combine(OutputFolder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log("Сборка игры: " + summary.result + ", " + summary.totalErrors + " ошибок, " +
                      (summary.totalSize / (1024 * 1024)) + " МБ, " + summary.totalTime.TotalSeconds.ToString("0") + " с → " +
                      Path.GetFullPath(options.locationPathName));
            return report;
        }
    }
}
