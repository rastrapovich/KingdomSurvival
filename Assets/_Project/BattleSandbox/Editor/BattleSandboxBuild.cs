using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KingdomSurvival.BattleSandbox.Editor
{
    // Отдельная Windows-сборка только со сценой BattleSandbox — чтобы
    // проверять бой вне Editor, не собирая всю игру. Основная сборка
    // (Build Profiles → Windows, обе сцены) не меняется. Результат — папка
    // Build/BattleSandbox (в .gitignore); передавать её целиком.
    public static class BattleSandboxBuild
    {
        private const string ScenePath = "Assets/_Project/Scenes/BattleSandbox.unity";
        private const string OutputFolder = "Build/BattleSandbox";
        private const string ExeName = "BattleSandbox.exe";

        [MenuItem("Kingdom Survival/Сборка/BattleSandbox (Windows)")]
        private static void BuildFromMenu()
        {
            BuildReport report = Build();
            if (report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(Path.Combine(OutputFolder, ExeName));
            else
                EditorUtility.DisplayDialog("Сборка BattleSandbox", "Сборка не удалась: " + report.summary.result + ". Подробности — в Console.", "OK");
        }

        // Командная строка: Unity.exe -batchmode -buildTarget Win64 -projectPath <путь>
        //   -executeMethod KingdomSurvival.BattleSandbox.Editor.BattleSandboxBuild.BuildBatch
        public static void BuildBatch()
        {
            BuildReport report = Build();
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static BuildReport Build()
        {
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(OutputFolder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log("Сборка BattleSandbox: " + summary.result + ", " + summary.totalErrors + " ошибок, " +
                      (summary.totalSize / (1024 * 1024)) + " МБ, " + summary.totalTime.TotalSeconds.ToString("0") + " с → " +
                      Path.GetFullPath(options.locationPathName));
            return report;
        }
    }
}
