using System;
using UnityEditor;

namespace KingdomSurvival.DevelopmentTracker.Editor
{
    public readonly struct AutoCheckResult
    {
        public readonly bool HasResult;
        public readonly bool Passed;
        public readonly string Message;

        public AutoCheckResult(bool hasResult, bool passed, string message)
        {
            HasResult = hasResult;
            Passed = passed;
            Message = message ?? string.Empty;
        }

        public static AutoCheckResult None
        {
            get { return new AutoCheckResult(false, false, string.Empty); }
        }
    }

    // Автопроверка может подтвердить только объективный факт — существование
    // файла/asset (раздел 4.9). Она не заменяет ручную галочку и не
    // оценивает качество текста, тон или завершённость сцены.
    public static class DevelopmentPlanAutoChecks
    {
        private const string FilePrefix = "file:";
        private const string AssetPrefix = "asset:";

        public static AutoCheckResult Run(string autoCheckId)
        {
            if (string.IsNullOrWhiteSpace(autoCheckId))
                return AutoCheckResult.None;

            string id = autoCheckId.Trim();

            if (id.StartsWith(FilePrefix, StringComparison.Ordinal))
            {
                string path = id.Substring(FilePrefix.Length).Trim();
                bool exists = System.IO.File.Exists(path) || System.IO.Directory.Exists(path);
                return new AutoCheckResult(
                    true,
                    exists,
                    exists ? "Файл найден: " + path : "Файл не найден: " + path);
            }

            if (id.StartsWith(AssetPrefix, StringComparison.Ordinal))
            {
                string path = id.Substring(AssetPrefix.Length).Trim();
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                bool exists = asset != null;
                return new AutoCheckResult(
                    true,
                    exists,
                    exists ? "Asset найден: " + path : "Asset не найден: " + path);
            }

            return new AutoCheckResult(true, false, "Неизвестный тип автопроверки: " + id);
        }
    }
}
