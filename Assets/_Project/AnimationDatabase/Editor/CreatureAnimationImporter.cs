using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    public enum CreatureAnimationImportMode
    {
        // Кадры ячейки заменяются целиком.
        Replace,
        // Кадры добавляются в конец ячейки.
        Append
    }

    // Что изменит пакет в наборе: для сводки перед применением.
    public sealed class CreatureAnimationImportChange
    {
        public CreatureAnimationAction Action { get; }
        public string ClipKey { get; }
        public CreatureAnimationDirection Direction { get; }
        public int NewFrameCount { get; }
        public int ExistingFrameCount { get; }
        public bool Replaces => ExistingFrameCount > 0;

        public CreatureAnimationImportChange(CreatureAnimationAction action, string clipKey, CreatureAnimationDirection direction, int newFrameCount, int existingFrameCount)
        {
            Action = action;
            ClipKey = clipKey ?? string.Empty;
            Direction = direction;
            NewFrameCount = newFrameCount;
            ExistingFrameCount = existingFrameCount;
        }

        public string Describe(CreatureAnimationImportMode mode)
        {
            string where = CreatureAnimationLabels.ActionTitle(Action) +
                           (string.IsNullOrEmpty(ClipKey) ? string.Empty : " [" + ClipKey + "]") +
                           " → " + CreatureAnimationLabels.DirectionTitle(Direction);
            if (!Replaces)
                return where + ": " + NewFrameCount + " кадр(ов), новое";
            return mode == CreatureAnimationImportMode.Append
                ? where + ": +" + NewFrameCount + " к " + ExistingFrameCount + " кадрам"
                : where + ": " + NewFrameCount + " кадр(ов), заменит " + ExistingFrameCount;
        }
    }

    public static class CreatureAnimationImporter
    {
        public static List<CreatureAnimationImportChange> DescribeChanges(
            CreatureAnimationImportPackage package,
            CreatureAnimationSetData set)
        {
            List<CreatureAnimationImportChange> changes = new List<CreatureAnimationImportChange>();
            foreach (CreatureAnimationImportGroup group in package.ChosenGroups)
            {
                foreach (CreatureAnimationImportCell cell in group.Cells.Values)
                {
                    CreatureAnimationFrames existing = set?.FindFrames(group.ChosenAction.Value, cell.Direction, group.ClipKey);
                    changes.Add(new CreatureAnimationImportChange(
                        group.ChosenAction.Value,
                        group.ClipKey,
                        cell.Direction,
                        cell.Frames.Count,
                        existing != null ? existing.FrameCount : 0));
                }
            }
            return changes;
        }

        // Первый проход: все файлы читаются и сверяются с холстом набора.
        // Ничего в проекте не меняется. False — пакет применять нельзя.
        public static bool Validate(
            CreatureAnimationImportPackage package,
            CreatureAnimationSetData set,
            out Vector2Int canvas)
        {
            canvas = set != null && set.HasAnyFrames ? set.CanvasSize : Vector2Int.zero;
            CreatureAnimationImportParser.ValidateDuplicateTargets(package);
            if (package.HasErrors)
                return false;

            List<(CreatureAnimationImportGroup group, CreatureAnimationImportCell cell)> cells = package.ChosenGroups
                .SelectMany(group => group.Cells.Values.Select(cell => (group, cell)))
                .ToList();
            if (cells.Count == 0)
            {
                package.AddError("Нечего загружать: ни одна папка не сопоставлена с действием.");
                return false;
            }

            int total = cells.Sum(entry => entry.cell.Frames.Count);
            int checkedCount = 0;
            Vector2Int packageCanvas = canvas;
            try
            {
                foreach ((CreatureAnimationImportGroup group, CreatureAnimationImportCell cell) in cells)
                {
                    string where = CreatureAnimationLabels.ActionTitle(group.ChosenAction.Value) + " → " +
                                   CreatureAnimationLabels.DirectionTitle(cell.Direction);
                    foreach (CreatureAnimationSourceFrame frame in cell.Frames)
                    {
                        checkedCount++;
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Проверка кадров",
                                where + ": " + Path.GetFileName(frame.Path),
                                checkedCount / (float)Math.Max(1, total)))
                        {
                            package.AddError("Проверка отменена. Набор не изменён.");
                            return false;
                        }

                        if (!CreatureAnimationAtlasBuilder.TryReadImageSize(frame.Path, out Vector2Int size, out string error))
                        {
                            package.AddError(where + ": " + Path.GetFileName(frame.Path) + " — " + error + ".");
                            continue;
                        }
                        if (packageCanvas == Vector2Int.zero)
                        {
                            packageCanvas = size;
                        }
                        else if (size != packageCanvas)
                        {
                            package.AddError(where + ": " + Path.GetFileName(frame.Path) + " — размер " + size.x + "×" + size.y +
                                             " не совпадает с общим холстом " + packageCanvas.x + "×" + packageCanvas.y +
                                             ". Кадры набора должны рендериться одним холстом, иначе ноги «поплывут».");
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (packageCanvas.x > CreatureAnimationAtlasBuilder.MaxAtlasSize - CreatureAnimationAtlasBuilder.Padding * 2 ||
                packageCanvas.y > CreatureAnimationAtlasBuilder.MaxAtlasSize - CreatureAnimationAtlasBuilder.Padding * 2)
            {
                package.AddError("Кадр " + packageCanvas.x + "×" + packageCanvas.y + " больше допустимого для атласа.");
            }
            canvas = packageCanvas;
            return !package.HasErrors;
        }

        // Второй проход: атласы в управляемой папке, затем одна запись в базу
        // (одно действие Undo). Ошибка до записи не трогает действующий набор.
        public static bool Apply(
            CreatureAnimationDatabaseAsset database,
            CreatureAnimationSetData set,
            CreatureAnimationImportPackage package,
            CreatureAnimationImportMode mode,
            out string report)
        {
            report = string.Empty;
            if (database == null || set == null)
            {
                package.AddError("Не выбран набор анимаций.");
                return false;
            }
            if (!Validate(package, set, out Vector2Int canvas))
                return false;

            List<(CreatureAnimationImportGroup group, CreatureAnimationImportCell cell)> cells = package.ChosenGroups
                .SelectMany(group => group.Cells.Values.Select(cell => (group, cell)))
                .ToList();
            Dictionary<CreatureAnimationImportCell, List<Sprite>> built = new Dictionary<CreatureAnimationImportCell, List<Sprite>>();
            string folder = CreatureAnimationEditorData.ManagedFolder(set.Id);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            try
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    (CreatureAnimationImportGroup group, CreatureAnimationImportCell cell) = cells[i];
                    CreatureAnimationAction action = group.ChosenAction.Value;
                    EditorUtility.DisplayProgressBar(
                        "Сборка атласов",
                        CreatureAnimationLabels.ActionTitle(action) + " → " + CreatureAnimationLabels.DirectionTitle(cell.Direction),
                        i / (float)cells.Count);
                    string actionPart = action + (string.IsNullOrEmpty(group.ClipKey) ? string.Empty : "-" + CreatureAnimationEditorData.SanitizeFileName(group.ClipKey));
                    string spritePrefix = actionPart + "_" + CreatureAnimationLabels.DirectionFolder(cell.Direction);
                    string baseName = CreatureAnimationEditorData.SanitizeFileName(set.Id) + "__" + spritePrefix + "__" + stamp;
                    built[cell] = CreatureAnimationAtlasBuilder.BuildAtlas(
                        folder,
                        baseName,
                        spritePrefix,
                        cell.Frames.Select(frame => frame.Path).ToList(),
                        canvas);
                }
            }
            catch (Exception exception)
            {
                package.AddError("Сборка атласа прервана: " + exception.Message + " Набор не изменён; неиспользуемые атласы можно убрать командой «Очистить неиспользуемые атласы».");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            List<CreatureAnimationImportChange> changes = DescribeChanges(package, set);
            Undo.RecordObject(database, "Загрузка анимаций");
            foreach ((CreatureAnimationImportGroup group, CreatureAnimationImportCell cell) in cells)
            {
                CreatureAnimationClipData clip = GetOrAddClipFromTemplate(database, set, group.ChosenAction.Value, group.ClipKey);
                CreatureAnimationFrames target = clip.GetOrAddDirection(cell.Direction);
                if (mode == CreatureAnimationImportMode.Append)
                    target.AppendFrames(built[cell], cell.Numbers);
                else
                    target.SetFrames(built[cell], cell.Numbers);
            }
            if (canvas != Vector2Int.zero)
                set.SetCanvasSize(canvas);
            EditorUtility.SetDirty(database);
            if (EditorUtility.IsPersistent(database))
                AssetDatabase.SaveAssetIfDirty(database);
            CreatureAnimationEditorData.NotifyChanged();

            report = "Загружено в «" + set.DisplayName + "»: " + package.ChosenGroups.Sum(group => group.FileCount) + " файл(ов).\n" +
                     string.Join("\n", changes.Select(change => change.Describe(mode)));
            return true;
        }

        // Уже импортированные Sprite из окна Project — прямо в ячейку, без
        // нового атласа. Номер кадра берётся из имени, если он там есть.
        public static bool ApplySprites(
            CreatureAnimationDatabaseAsset database,
            CreatureAnimationSetData set,
            CreatureAnimationAction action,
            string clipKey,
            CreatureAnimationDirection direction,
            IReadOnlyList<Sprite> sprites,
            CreatureAnimationImportMode mode,
            out string message)
        {
            message = string.Empty;
            List<Sprite> ordered = (sprites ?? Array.Empty<Sprite>()).Where(sprite => sprite != null).ToList();
            if (database == null || set == null || ordered.Count == 0)
            {
                message = "Нет спрайтов для загрузки.";
                return false;
            }

            Vector2Int canvas = set.HasAnyFrames ? set.CanvasSize : Vector2Int.zero;
            foreach (Sprite sprite in ordered)
            {
                Vector2Int size = new Vector2Int(Mathf.RoundToInt(sprite.rect.width), Mathf.RoundToInt(sprite.rect.height));
                if (canvas == Vector2Int.zero)
                    canvas = size;
                else if (size != canvas)
                {
                    message = "Спрайт «" + sprite.name + "» " + size.x + "×" + size.y + " не совпадает с холстом набора " +
                              canvas.x + "×" + canvas.y + ".";
                    return false;
                }
            }

            List<int> numbers = new List<int>();
            bool numbered = ordered.All(sprite =>
            {
                bool parsed = CreatureAnimationImportParser.TryParseFrameNumber(sprite.name, out int number);
                numbers.Add(number);
                return parsed;
            });
            if (numbered && numbers.Distinct().Count() == numbers.Count)
            {
                List<(Sprite sprite, int number)> pairs = ordered.Zip(numbers, (sprite, number) => (sprite, number))
                    .OrderBy(pair => pair.number)
                    .ToList();
                ordered = pairs.Select(pair => pair.sprite).ToList();
                numbers = pairs.Select(pair => pair.number).ToList();
            }
            else
            {
                numbers.Clear();
            }

            Undo.RecordObject(database, "Загрузка кадров");
            CreatureAnimationFrames target = GetOrAddClipFromTemplate(database, set, action, clipKey).GetOrAddDirection(direction);
            if (mode == CreatureAnimationImportMode.Append)
                target.AppendFrames(ordered, numbers);
            else
                target.SetFrames(ordered, numbers);
            set.SetCanvasSize(canvas);
            EditorUtility.SetDirty(database);
            if (EditorUtility.IsPersistent(database))
                AssetDatabase.SaveAssetIfDirty(database);
            CreatureAnimationEditorData.NotifyChanged();
            message = CreatureAnimationLabels.ActionTitle(action) + " → " + CreatureAnimationLabels.DirectionTitle(direction) +
                      ": " + ordered.Count + " кадр(ов) из проекта.";
            return true;
        }

        // Новое действие получает настройки того же действия образца.
        private static CreatureAnimationClipData GetOrAddClipFromTemplate(
            CreatureAnimationDatabaseAsset database,
            CreatureAnimationSetData set,
            CreatureAnimationAction action,
            string clipKey)
        {
            bool existed = set.FindClip(action, clipKey) != null;
            CreatureAnimationClipData clip = set.GetOrAddClip(action, clipKey);
            if (!existed)
                database.ApplyTemplateClipSettings(clip);
            return clip;
        }

        // Атласы управляемой папки, на которые не ссылается ни один набор.
        public static List<string> FindUnusedAtlases(CreatureAnimationDatabaseAsset database)
        {
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (database != null)
            {
                foreach (CreatureAnimationSetData set in database.Sets)
                {
                    if (set == null)
                        continue;
                    foreach (CreatureAnimationClipData clip in set.Clips)
                    {
                        if (clip == null)
                            continue;
                        foreach (CreatureAnimationFrames cell in clip.Directions)
                        {
                            if (cell == null)
                                continue;
                            foreach (Sprite sprite in cell.Frames)
                            {
                                if (sprite != null)
                                    used.Add(AssetDatabase.GetAssetPath(sprite));
                            }
                        }
                    }
                }
            }

            List<string> unused = new List<string>();
            if (!AssetDatabase.IsValidFolder(CreatureAnimationEditorData.ArtRoot))
                return unused;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { CreatureAnimationEditorData.ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Только атласы, собранные импортом (имя «набор__действие__время»).
                if (Path.GetFileName(path).Contains("__") && !used.Contains(path))
                    unused.Add(path);
            }
            return unused;
        }
    }
}
