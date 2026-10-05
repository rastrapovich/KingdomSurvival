using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Загрузка: файлы и папки из Проводника, Sprite и Texture из Project.
    // На пустое место — новые ассеты; на объект — ракурсы в эту запись; в
    // ячейку ракурса — замена одного рисунка или нормали.
    public sealed partial class ArtAssetDatabaseWindow
    {
        private static bool HasExternalDrag() =>
            string.IsNullOrEmpty(ArtAssetPicker.DraggedAssetId()) &&
            ((DragAndDrop.paths != null && DragAndDrop.paths.Length > 0) || (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0));

        private static List<string> DraggedPaths()
        {
            List<string> paths = new List<string>();
            foreach (string path in DragAndDrop.paths ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(path) && !paths.Contains(path)) paths.Add(path);
            foreach (UnityEngine.Object item in DragAndDrop.objectReferences ?? Array.Empty<UnityEngine.Object>())
            {
                string path = AssetDatabase.GetAssetPath(item);
                if (!string.IsNullOrEmpty(path) && !paths.Contains(path)) paths.Add(path);
            }
            return paths;
        }

        // Перетаскивание на холст, галерею или общий предпросмотр карточки.
        private bool HandleFileDrop(Event evt, Rect area, ArtAssetDefinition target)
        {
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return false;
            if (!HasExternalDrag() || !area.Contains(evt.mousePosition)) return false;
            List<string> paths = DraggedPaths();
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            dropRect = area;
            int count = paths.Count;
            dropHint = target != null ? "Добавить ракурсы в «" + target.Name + "»"
                : count == 1 && !Directory.Exists(paths[0]) ? "Создать ассет" : "Создать ассеты из пакета (" + count + ")";
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                dropHint = null;
                ImportPaths(paths, target);
            }
            evt.Use();
            center.MarkDirtyRepaint();
            return true;
        }

        // Ячейка ракурса: левая половина — рисунок, правая — нормаль.
        private bool HandleSlotDrop(Event evt, Rect cell, Rect colorRect, Rect normalRect, ArtAssetDefinition asset, ArtAssetPart part, ArtAssetView view)
        {
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return false;
            if (!HasExternalDrag() || !cell.Contains(evt.mousePosition)) return false;
            List<string> paths = DraggedPaths();
            bool normal = evt.mousePosition.x >= normalRect.x - 2;
            bool single = paths.Count == 1 && !Directory.Exists(paths[0]);
            if (single)
            {
                // Слово «normal» в имени тоже делает файл нормалью.
                List<string> tokens = ArtAssetImportParser.Tokens(Path.GetFileNameWithoutExtension(paths[0]));
                if (tokens.Any(token => token == "normal" || token == "normals" || token == "nrm" || token == "нормаль" || token == "нормали")) normal = true;
            }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            dropRect = single ? (normal ? normalRect : colorRect) : cell;
            string partNote = asset.Parts.Count > 1 ? " · " + part.Name : "";
            dropHint = single ? (normal ? "Заменить нормаль: " : "Заменить рисунок: ") + ArtAssetLabels.ViewTitle(view) + partNote
                : "Добавить файлы в «" + asset.Name + "»";
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                dropHint = null;
                if (single) AssignSingle(asset, part, view, paths[0], DragAndDrop.objectReferences.FirstOrDefault(), normal ? ArtAssetFileKind.Normal : ArtAssetFileKind.Color);
                else ImportPaths(paths, asset);
            }
            evt.Use();
            center.MarkDirtyRepaint();
            return true;
        }

        private void AssignSingle(ArtAssetDefinition asset, ArtAssetPart part, ArtAssetView view, string path, UnityEngine.Object projectObject, ArtAssetFileKind kind)
        {
            try
            {
                string warning = projectObject is Sprite || (projectObject is Texture2D && ArtAssetImporter.IsProjectPath(path))
                    ? ArtAssetImporter.AssignProjectObject(catalog, asset, part, view, projectObject, kind)
                    : ArtAssetImporter.AssignFile(catalog, asset, part, view, path, kind);
                status.text = (kind == ArtAssetFileKind.Color ? "Рисунок" : "Нормаль") + " «" + ArtAssetLabels.ViewTitle(view) + "» назначен" +
                              (kind == ArtAssetFileKind.Color ? "." : "а.") + (warning != null ? " ⚠ " + warning : "");
                cardView = view;
                litDirty = true;
                saveAt = EditorApplication.timeSinceStartup + .3;
            }
            catch (Exception exception)
            {
                status.text = "Не назначено: " + exception.Message;
            }
            visibleDirty = true;
            BuildProperties();
        }

        private void LoadFiles()
        {
            string path = EditorUtility.OpenFilePanel("Загрузить PNG", "", "png");
            if (string.IsNullOrEmpty(path)) return;
            ArtAssetDefinition asset = state.Mode == ArtAssetCenterMode.Card ? Selected : null;
            if (asset != null)
            {
                // Без ракурса в имени — в выбранную ячейку карточки.
                bool known = ArtAssetImportParser.Classify(new List<string> { Path.GetFileName(path) }, asset.Name, out _, out ArtAssetView view, out _,
                    out ArtAssetFileKind kind, out _);
                List<string> tokens = ArtAssetImportParser.Tokens(Path.GetFileNameWithoutExtension(path));
                bool normal = known ? kind == ArtAssetFileKind.Normal : tokens.Contains("normal") || tokens.Contains("нормаль");
                AssignSingle(asset, asset.Parts[Mathf.Clamp(cardPart, 0, asset.Parts.Count - 1)], known ? view : cardView, path, null,
                    normal ? ArtAssetFileKind.Normal : ArtAssetFileKind.Color);
                return;
            }
            ImportPaths(new List<string> { path }, null);
        }

        private void LoadFolder()
        {
            string path = EditorUtility.OpenFolderPanel("Загрузить папку с рисунками", "", "");
            if (string.IsNullOrEmpty(path)) return;
            ImportPaths(new List<string> { path }, null);
        }

        // Пакет: разбор → сводка → импорт. target — все файлы в одну запись.
        private void ImportPaths(List<string> paths, ArtAssetDefinition target)
        {
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(paths, target?.Name);
            if (plan.IsEmpty)
            {
                status.text = "Ничего не распознано: " + string.Join("; ", plan.Unresolved.Take(4).Select(item => Path.GetFileName(item.Path) + " — " + item.Reason)) +
                              (target != null ? " Перетащите файл прямо в ячейку ракурса в карточке." : "");
                if (plan.Unresolved.Count > 0) ArtAssetImportSummaryWindow.ShowResult(plan, null);
                return;
            }
            Dictionary<ArtAssetImportGroup, ArtAssetDefinition> matches = new Dictionary<ArtAssetImportGroup, ArtAssetDefinition>();
            foreach (ArtAssetImportGroup group in plan.Groups)
            {
                if (target != null) { matches[group] = target; continue; }
                ArtAssetDefinition match = catalog.assets.FirstOrDefault(item => item != null &&
                    (string.Equals(item.ImportKey, group.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(ArtAssetImportParser.NormalizeKey(item.Name), group.Key, StringComparison.OrdinalIgnoreCase)));
                if (match != null) matches[group] = match;
            }
            // Однозначный пакет в выбранную запись — без лишних вопросов.
            Dictionary<ArtAssetImportGroup, bool> update = plan.Groups.ToDictionary(group => group, group => target != null);
            if (!ArtAssetImportSummaryWindow.Confirm(plan, matches, update, target)) { status.text = "Импорт отменён."; return; }
            ArtAssetImportResult result = ArtAssetImporter.Import(catalog, plan,
                group => update.TryGetValue(group, out bool yes) && yes && matches.TryGetValue(group, out ArtAssetDefinition existing) ? existing : null);
            visibleDirty = true;
            layoutDirty = true;
            BuildCategories();
            string first = result.Created.Concat(result.Updated).FirstOrDefault();
            if (first != null) SelectAsset(first, plan.Groups.Count == 1);
            status.text = result.Summary + (result.Warnings.Count > 0 ? " ⚠ " + string.Join(" ", result.Warnings.Take(3)) : "") +
                          (result.Errors.Count > 0 ? " Ошибки: " + string.Join("; ", result.Errors.Take(3)) : "") +
                          (plan.Unresolved.Count > 0 ? " Не назначено файлов: " + plan.Unresolved.Count + " — перетащите их в ячейки вручную." : "");
            if (result.Errors.Count > 0 || result.Warnings.Count > 0) Debug.Log("База ассетов: " + status.text);
        }
    }

    // Сводка перед пакетным импортом: объекты, ракурсы и нормали, совпадения
    // по имени (обновлять — только явной галочкой) и нераспознанные файлы.
    public sealed class ArtAssetImportSummaryWindow : EditorWindow
    {
        private ArtAssetImportPlan plan;
        private Dictionary<ArtAssetImportGroup, ArtAssetDefinition> matches;
        private Dictionary<ArtAssetImportGroup, bool> update;
        private ArtAssetDefinition target;
        private bool confirmed, resultOnly;
        private Vector2 scroll;

        public static bool Confirm(ArtAssetImportPlan plan, Dictionary<ArtAssetImportGroup, ArtAssetDefinition> matches,
            Dictionary<ArtAssetImportGroup, bool> update, ArtAssetDefinition target)
        {
            ArtAssetImportSummaryWindow window = CreateInstance<ArtAssetImportSummaryWindow>();
            window.titleContent = new GUIContent("Импорт в Базу ассетов");
            window.plan = plan;
            window.matches = matches;
            window.update = update;
            window.target = target;
            window.minSize = new Vector2(560, 380);
            window.ShowModalUtility();
            return window.confirmed;
        }

        public static void ShowResult(ArtAssetImportPlan plan, ArtAssetDefinition target)
        {
            ArtAssetImportSummaryWindow window = CreateInstance<ArtAssetImportSummaryWindow>();
            window.titleContent = new GUIContent("Импорт: не распознано");
            window.plan = plan;
            window.matches = new Dictionary<ArtAssetImportGroup, ArtAssetDefinition>();
            window.update = new Dictionary<ArtAssetImportGroup, bool>();
            window.target = target;
            window.resultOnly = true;
            window.minSize = new Vector2(520, 300);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (plan == null) { Close(); return; }
            EditorGUILayout.LabelField(target != null ? "Файлы для «" + target.Name + "»" : "Распознано объектов: " + plan.Groups.Count, EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (ArtAssetImportGroup group in plan.Groups)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string parts = string.Join(", ", group.Parts.Select(part => part.Length == 0 ? "основа" : part));
                    EditorGUILayout.LabelField(group.Name, "рисунков: " + group.ColorCount + " · нормалей: " + group.NormalCount + " · части: " + parts);
                    List<string> views = ArtAssetLabels.Views.Select(view =>
                    {
                        ArtAssetImportSlot slot = group.Slots.FirstOrDefault(item => item.View == view && item.Part.Length == 0);
                        return ArtAssetLabels.ViewTitle(view) + (slot?.ColorPath != null ? " ✓" + (slot.NormalPath != null ? "N" : "") : " —");
                    }).ToList();
                    EditorGUILayout.LabelField("Основа: " + string.Join(" · ", views), EditorStyles.wordWrappedMiniLabel);
                    if (group.MainViewCount == 0)
                        EditorGUILayout.HelpBox("Нет рисунков основы: будут загружены только части.", MessageType.Warning);
                    if (target == null && matches.TryGetValue(group, out ArtAssetDefinition match))
                    {
                        bool value = update.TryGetValue(group, out bool current) && current;
                        update[group] = EditorGUILayout.ToggleLeft("Обновить существующий «" + match.Name + "» (ID и размещения сохранятся); иначе — новая запись",
                            value);
                    }
                    else if (target == null) EditorGUILayout.LabelField("Новая запись", EditorStyles.miniLabel);
                }
            }
            if (plan.Unresolved.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Не назначено автоматически (" + plan.Unresolved.Count + ") — назначьте вручную перетаскиванием в ячейку ракурса:", EditorStyles.boldLabel);
                foreach (ArtAssetImportIssue issue in plan.Unresolved.Take(60))
                    EditorGUILayout.LabelField(Path.GetFileName(issue.Path), issue.Reason, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (resultOnly)
                {
                    if (GUILayout.Button("Понятно", GUILayout.Width(120))) Close();
                    return;
                }
                if (GUILayout.Button("Отмена", GUILayout.Width(100))) { confirmed = false; Close(); }
                GUI.enabled = plan.Groups.Count > 0;
                if (GUILayout.Button("Импортировать", GUILayout.Width(140))) { confirmed = true; Close(); }
                GUI.enabled = true;
            }
        }
    }
}
