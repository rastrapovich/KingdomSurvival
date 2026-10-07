using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Загрузка: файлы и папки из Проводника, Sprite и Texture из Project.
    // На пустое место — новые ассеты; на объект — ракурсы в эту запись; в
    // ячейку ракурса — замена одного рисунка или нормали.
    public sealed partial class ArtAssetDatabaseWindow
    {
        private enum DropKind { None, Reorder, Create, AddToAsset, Slot }

        private struct DropTarget
        {
            public DropKind Kind;
            public ArtAssetDefinition Asset;
            public ArtAssetPart Part;
            public ArtAssetView View;
            public bool Normal;
            public bool Single;
            public ArtAssetView? DefaultView;
            public Rect Rect;
            public string Hint;
        }

        private static bool HasExternalDrag() =>
            (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0) || (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0);

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

        private static bool LooksLikeNormal(string path)
        {
            List<string> tokens = ArtAssetImportParser.Tokens(Path.GetFileNameWithoutExtension(path));
            return tokens.Any(token => token == "normal" || token == "normals" || token == "nrm" || token == "нормаль" || token == "нормали") ||
                   (tokens.Count > 1 && tokens[tokens.Count - 1] == "n");
        }

        // Приём перетаскивания — событиями UI Toolkit (как в Базе анимаций):
        // так файлы из Проводника и Project доходят до окна надёжно. Цель —
        // по точке над центральной областью: пустое место, объект, ячейка.
        private void RegisterDrop()
        {
            center.RegisterCallback<DragUpdatedEvent>(evt => OnCenterDrag(evt, evt.mousePosition, false));
            center.RegisterCallback<DragPerformEvent>(evt => OnCenterDrag(evt, evt.mousePosition, true));
            center.RegisterCallback<DragLeaveEvent>(_ => ClearDropHint());
            center.RegisterCallback<DragExitedEvent>(_ => ClearDropHint());
            // Остальное окно (категории, свойства, панели) — новые ассеты.
            rootVisualElement.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!HasExternalDrag()) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                status.text = "Отпустите — создать ассет(ы): файлов и папок " + DraggedPaths().Count + ".";
            });
            rootVisualElement.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (!HasExternalDrag()) return;
                DragAndDrop.AcceptDrag();
                List<string> paths = DraggedPaths();
                ArtAssetPicker.EndDrag();
                EditorApplication.delayCall += () => ImportPaths(paths, null, ArtAssetView.Front);
            });
        }

        private void ClearDropHint()
        {
            if (dropHint == null) return;
            dropHint = null;
            center?.MarkDirtyRepaint();
        }

        private void OnCenterDrag(EventBase evt, Vector2 worldMouse, bool perform)
        {
            Vector2 mouse = center.WorldToLocal(worldMouse);
            DropTarget target = ResolveDrop(mouse);
            evt.StopPropagation();
            if (target.Kind == DropKind.None)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                ClearDropHint();
                return;
            }
            DragAndDrop.visualMode = target.Kind == DropKind.Reorder ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Copy;
            if (!perform)
            {
                dropHint = target.Hint;
                dropRect = target.Rect;
                center.MarkDirtyRepaint();
                return;
            }
            DragAndDrop.AcceptDrag();
            string reorderId = ArtAssetPicker.DraggedAssetId();
            List<string> paths = DraggedPaths();
            UnityEngine.Object projectObject = DragAndDrop.objectReferences?.FirstOrDefault();
            ArtAssetPicker.EndDrag();
            ClearDropHint();
            status.text = target.Kind == DropKind.Reorder ? status.text : "Получено файлов и папок: " + paths.Count + "…";
            // Модальная сводка — уже после завершения перетаскивания.
            EditorApplication.delayCall += () => PerformDrop(target, reorderId, paths, projectObject);
        }

        private void PerformDrop(DropTarget target, string reorderId, List<string> paths, UnityEngine.Object projectObject)
        {
            switch (target.Kind)
            {
                case DropKind.Reorder:
                    ReorderBefore(reorderId, target.Asset);
                    break;
                case DropKind.Slot when target.Single:
                    AssignSingle(target.Asset, target.Part, target.View, paths[0], projectObject, target.Normal ? ArtAssetFileKind.Normal : ArtAssetFileKind.Color);
                    break;
                case DropKind.Slot:
                case DropKind.AddToAsset:
                    ImportPaths(paths, target.Asset, target.DefaultView);
                    break;
                case DropKind.Create:
                    ImportPaths(paths, null, ArtAssetView.Front);
                    break;
            }
            center?.MarkDirtyRepaint();
        }

        // Куда упадёт перетаскиваемое (mouse — координаты центральной области).
        private DropTarget ResolveDrop(Vector2 mouse)
        {
            Rect area = new Rect(0, 0, center.contentRect.width, center.contentRect.height);
            DropTarget result = new DropTarget { Kind = DropKind.None, Rect = area };
            string reorderId = ArtAssetPicker.DraggedAssetId();
            if (!string.IsNullOrEmpty(reorderId))
            {
                if (state.Mode == ArtAssetCenterMode.Card) return result;
                result.Kind = DropKind.Reorder;
                result.Asset = state.Mode == ArtAssetCenterMode.Gallery ? GalleryHit(area, mouse) : CanvasHit(area, mouse)?.Asset;
                result.Hint = "Поставить на холсте " + (result.Asset != null ? "перед «" + result.Asset.Name + "»" : "в конец");
                return result;
            }
            if (!HasExternalDrag()) return result;
            List<string> paths = DraggedPaths();
            bool single = paths.Count == 1 && !Directory.Exists(paths[0]);
            ArtAssetDefinition selected = Selected;
            if (state.Mode == ArtAssetCenterMode.Card && selected != null)
            {
                CardLayout(area, out Rect preview, out Rect slots);
                cardPart = Mathf.Clamp(cardPart, 0, selected.Parts.Count - 1);
                ArtAssetPart part = selected.Parts[cardPart];
                for (int i = 0; i < ArtAssetLabels.ViewCount; i++)
                {
                    Rect cell = SlotRect(slots, i);
                    if (!cell.Contains(mouse)) continue;
                    SlotHalves(cell, out Rect colorRect, out Rect normalRect);
                    ArtAssetView view = ArtAssetLabels.Views[i];
                    bool normal = mouse.x >= normalRect.x - 2 || (single && LooksLikeNormal(paths[0]));
                    string partNote = selected.Parts.Count > 1 ? " · " + part.Name : "";
                    return new DropTarget
                    {
                        Kind = DropKind.Slot, Asset = selected, Part = part, View = view, Normal = normal, Single = single, DefaultView = view,
                        Rect = single ? (normal ? normalRect : colorRect) : cell,
                        Hint = single ? (normal ? "Заменить нормаль: " : "Заменить рисунок: ") + ArtAssetLabels.ViewTitle(view) + partNote
                            : "Добавить файлы в «" + selected.Name + "» (без ракурса в имени — в «" + ArtAssetLabels.ViewTitle(view) + "»)"
                    };
                }
                return new DropTarget
                {
                    Kind = DropKind.AddToAsset, Asset = selected, DefaultView = cardView, Rect = preview,
                    Hint = "Добавить в «" + selected.Name + "» (без ракурса в имени — в «" + ArtAssetLabels.ViewTitle(cardView) + "»)"
                };
            }
            ArtAssetDefinition hit = state.Mode == ArtAssetCenterMode.Gallery ? GalleryHit(area, mouse)
                : state.Mode == ArtAssetCenterMode.Canvas ? CanvasHit(area, mouse)?.Asset : null;
            if (hit != null)
                return new DropTarget { Kind = DropKind.AddToAsset, Asset = hit, Rect = area, Hint = "Добавить ракурсы в «" + hit.Name + "»" };
            return new DropTarget
            {
                Kind = DropKind.Create, Rect = area,
                Hint = single ? "Создать ассет (без ракурса в имени — «Спереди»)" : "Создать ассеты из пакета (" + paths.Count + ")"
            };
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
            ImportPaths(new List<string> { path }, null, ArtAssetView.Front);
        }

        private void LoadFolder()
        {
            string path = EditorUtility.OpenFolderPanel("Загрузить папку с рисунками", "", "");
            if (string.IsNullOrEmpty(path)) return;
            ImportPaths(new List<string> { path }, null, ArtAssetView.Front);
        }

        // Пакет: разбор → сводка → импорт. target — все файлы в одну запись.
        private void ImportPaths(List<string> paths, ArtAssetDefinition target, ArtAssetView? defaultView = null)
        {
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(paths, target?.Name, defaultView);
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

        // Тесты: сводка подтверждается без окна.
        public static bool AutoConfirm;

        public static bool Confirm(ArtAssetImportPlan plan, Dictionary<ArtAssetImportGroup, ArtAssetDefinition> matches,
            Dictionary<ArtAssetImportGroup, bool> update, ArtAssetDefinition target)
        {
            // Тесты и пакетный режим: модальное окно не открывается.
            if (AutoConfirm || Application.isBatchMode) return true;
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
                        return ArtAssetLabels.ViewTitle(view) + (slot?.ColorPath != null ? " ✓" + (slot.NormalPath != null ? "N" : "") +
                            (slot.FrameCount > 1 ? " ×" + slot.FrameCount : "") : " —");
                    }).ToList();
                    EditorGUILayout.LabelField("Основа: " + string.Join(" · ", views), EditorStyles.wordWrappedMiniLabel);
                    if (group.MaxFrameCount > 1)
                        EditorGUILayout.LabelField("Анимация: до " + group.MaxFrameCount + " кадров в ракурсе (номер в конце имени файла).", EditorStyles.wordWrappedMiniLabel);
                    ArtAssetImportSlot assumed = group.Slots.FirstOrDefault(slot => slot.ViewAssumed);
                    if (assumed != null)
                        EditorGUILayout.HelpBox("Ракурса в имени файла нет — рисунок поставлен в «" + ArtAssetLabels.ViewTitle(assumed.View) +
                                                "». Остальные ракурсы можно добавить позже в карточке.", MessageType.Info);
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
