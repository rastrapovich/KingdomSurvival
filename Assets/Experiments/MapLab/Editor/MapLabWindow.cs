using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // Окно MapLab: команды генерации и все ручки художника по группам.
    // Тяжёлое — по кнопкам; свет, тени, контур, бумага, палитра объектов,
    // диагностика и камера применяются сразу без перестройки.
    public sealed class MapLabWindow : EditorWindow
    {
        private static readonly string[] Groups =
        {
            "General", "Relief", "Water", "RoadsAndFields", "Cliffs", "Vegetation", "Settlement", "PainterlyStyle", "Camera", "Diagnostics", "Advanced",
        };

        private static readonly string[] GroupTitles =
        {
            "Общее (General)", "Рельеф (Relief)", "Вода (Water)", "Дороги и поля (Roads And Fields)", "Уступы (Cliffs)",
            "Растительность (Vegetation)", "Поселение (Settlement)", "Живописный стиль (Painterly Style)", "Камера (Camera)",
            "Диагностика (Diagnostics)", "Дополнительно (Advanced)",
        };

        // Группы, изменение которых не требует перестройки.
        private static readonly bool[] CheapGroups = { false, false, false, false, false, false, false, true, true, true, false };

        private static readonly Vector2Int[] ExportSizes = { new Vector2Int(2048, 1365), new Vector2Int(3072, 2048), new Vector2Int(1536, 1024), new Vector2Int(1920, 1080) };
        private static readonly string[] ExportSizeNames = { "2048 × 1365", "3072 × 2048", "1536 × 1024", "1920 × 1080" };

        private MapLabView view;
        private SerializedObject serialized;
        private Vector2 scroll;
        private readonly bool[] open = { true, true, false, false, true, true, false, true, true, true, false };
        private int exportSize;
        private int supersample = 2;
        private string status = "";
        private bool sceneClick;

        [MenuItem("Kingdom Survival/MapLab (эксперимент)/Окно MapLab", priority = 400)]
        public static void Open()
        {
            MapLabWindow window = GetWindow<MapLabWindow>("MapLab");
            window.minSize = new Vector2(380f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EditorUtility.ClearProgressBar();
        }

        private void OnHierarchyChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            if (view == null)
                view = MapLabAssets.FindView();
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Тестовый генератор живописной карты", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Эксперимент: в игру, сохранения и общий рендер не подключён.", MessageType.None);

            if (view == null)
            {
                EditorGUILayout.HelpBox("В открытой сцене нет MapLab. Создайте тестовую сцену.", MessageType.Info);
                if (GUILayout.Button("Create Test Scene — создать тестовую сцену", GUILayout.Height(28)))
                    RunGuarded(() => { MapLabAssets.CreateTestSceneMenu(); view = MapLabAssets.FindView(); });
                return;
            }

            EditorGUI.BeginChangeCheck();
            MapLabSettings settings = (MapLabSettings)EditorGUILayout.ObjectField(new GUIContent("Настройки (preset)", "Ассет MapLabSettings, который строит карта."), view.Settings, typeof(MapLabSettings), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(view, "MapLab: настройки");
                view.Settings = settings;
                serialized = null;
            }
            DrawPresetSwitch();
            if (view.Settings == null)
            {
                EditorGUILayout.HelpBox("Назначьте MapLabSettings.", MessageType.Warning);
                return;
            }

            DrawCommands();
            DrawStatus();

            if (serialized == null || serialized.targetObject != view.Settings)
                serialized = new SerializedObject(view.Settings);
            serialized.Update();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            bool cheapChanged = false;
            for (int g = 0; g < Groups.Length; g++)
            {
                SerializedProperty group = serialized.FindProperty(Groups[g]);
                if (group == null)
                    continue;
                open[g] = EditorGUILayout.BeginFoldoutHeaderGroup(open[g], GroupTitles[g] + (CheapGroups[g] ? "" : "  ·  применяется перестройкой"));
                if (open[g])
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.indentLevel++;
                    SerializedProperty end = group.GetEndProperty();
                    SerializedProperty child = group.Copy();
                    bool enter = true;
                    while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                    {
                        enter = false;
                        EditorGUILayout.PropertyField(child, true);
                    }
                    EditorGUI.indentLevel--;
                    if (EditorGUI.EndChangeCheck() && CheapGroups[g])
                        cheapChanged = true;
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }
            EditorGUILayout.EndScrollView();
            if (serialized.ApplyModifiedProperties() && cheapChanged)
                ApplyCheap();
        }

        private void DrawPresetSwitch()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Проверочный пресет", "Переключить на один из трёх ассетов в Presets."));
            string[] names = { "А · Холм и уступ", "Б · Лесное озеро", "В · Речная долина" };
            for (int i = 0; i < names.Length; i++)
            {
                MapLabPresetKind kind = (MapLabPresetKind)i;
                if (GUILayout.Button(names[i], EditorStyles.miniButton))
                {
                    Undo.RecordObject(view, "MapLab: пресет");
                    view.Settings = MapLabAssets.EnsurePreset(kind);
                    serialized = null;
                    RunGeneration("Generate", p => view.Generate(p));
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCommands()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Generate", "Полная генерация: данные, покраска, меши, камера."), GUILayout.Height(26)))
                RunGeneration("Generate", p => view.Generate(p));
            if (GUILayout.Button(new GUIContent("Regenerate Same Seed", "Повторить с тем же seed — результат совпадает."), GUILayout.Height(26)))
                RunGeneration("Regenerate", p => view.Generate(p));
            if (GUILayout.Button(new GUIContent("Randomize Seed", "Новый seed и генерация."), GUILayout.Height(26)))
            {
                Undo.RecordObject(view.Settings, "MapLab: seed");
                view.Settings.General.Seed = UnityEngine.Random.Range(1, 999999);
                EditorUtility.SetDirty(view.Settings);
                serialized = null;
                RunGeneration("Randomize", p => view.Generate(p));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Rebuild Terrain", "Рельеф и всё зависимое: берега, стены, мосты, высоты объектов.")))
                RunGeneration("Rebuild Terrain", p => view.RebuildTerrain(p));
            if (GUILayout.Button(new GUIContent("Repaint Only", "Перекраска поверхности (палитра, мазки, пятна) без перестройки геометрии.")))
                RunGuarded(() => Require(view.RepaintOnly()));
            if (GUILayout.Button(new GUIContent("Rebuild Objects", "Лес, кусты, валуны, постройки — без изменения рельефа и дорог.")))
                RunGeneration("Rebuild Objects", p => view.RebuildObjects(p));
            if (GUILayout.Button(new GUIContent("Clear Generated", "Удалить только сгенерированное этим MapLab.")))
                view.Clear();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Fit Camera", "Вписать карту в кадр.")) && view.CameraRig != null)
                view.CameraRig.ResetView();
            if (GUILayout.Button(new GUIContent("Save Preset", "Сохранить текущие настройки отдельным ассетом.")))
                SavePreset();
            if (GUILayout.Button(new GUIContent("Export PNG", "Чистое изображение камеры MapLab и JSON с параметрами воспроизведения.")))
                ExportPng();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            exportSize = EditorGUILayout.Popup("Размер PNG", exportSize, ExportSizeNames);
            supersample = EditorGUILayout.IntSlider(new GUIContent("Сглаживание", "Суперсэмплинг при экспорте."), supersample, 1, 3);
            EditorGUILayout.EndHorizontal();
            sceneClick = EditorGUILayout.ToggleLeft(new GUIContent("Диагностический клик в Scene View (Shift + ЛКМ)", "Координата, высота, уклон и тип поверхности."), sceneClick);
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(view.LastError))
                EditorGUILayout.HelpBox(view.LastError, MessageType.Error);
            MapLabData data = view.Data;
            if (data != null)
            {
                MapLabStats s = data.Stats;
                int trees = data.CountObjects(MapLabObjectKind.Deciduous) + data.CountObjects(MapLabObjectKind.Conifer) + data.CountObjects(MapLabObjectKind.DeciduousGroup);
                EditorGUILayout.HelpBox(
                    $"{data.Preset} · seed {data.Seed} · {data.Resolution}² отсчётов · высоты {data.MinHeight:0.0}–{data.MaxHeight:0.0}\n" +
                    $"Генерация {s.GenerateMs:0} мс (рельеф {s.TerrainMs:0}, покраска {s.PaintMs:0}) · вершин {s.Vertices:N0} · треугольников {s.Triangles:N0}\n" +
                    $"Рендереров {s.Renderers} · материалов {s.Materials} · деревьев {trees} · домов {data.CountObjects(MapLabObjectKind.House)} · мостов {data.Bridges.Count}", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("Карта не построена — нажмите Generate.", MessageType.Info);
            }
            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
        }

        private void ApplyCheap()
        {
            view.ApplyMaterialSettings();
            view.ApplyVisibility();
            if (view.CameraRig != null)
                view.CameraRig.Bind(view, true);
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private void RunGeneration(string title, Func<MapLabGenerator.Progress, bool> action)
        {
            RunGuarded(() =>
            {
                DateTime start = DateTime.Now;
                bool ok = action((stage, fraction) => EditorUtility.DisplayCancelableProgressBar("MapLab — " + title, stage, fraction));
                Require(ok);
                status = $"{title}: {(DateTime.Now - start).TotalSeconds:0.0} с";
            });
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private void RunGuarded(Action action)
        {
            try
            {
                action();
            }
            catch (MapLabGenerator.CancelledException)
            {
                status = "Отменено; прежняя карта сохранена.";
            }
            catch (Exception exception)
            {
                status = "Ошибка: " + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void Require(bool ok)
        {
            if (!ok)
                status = string.IsNullOrEmpty(view.LastError) ? "Сначала нажмите Generate." : view.LastError;
        }

        private void SavePreset()
        {
            string path = EditorUtility.SaveFilePanelInProject("Сохранить пресет MapLab", view.Settings.name + "_copy", "asset", "Новый ассет настроек", MapLabAssets.PresetFolder);
            if (string.IsNullOrEmpty(path))
                return;
            MapLabSettings copy = Instantiate(view.Settings);
            AssetDatabase.CreateAsset(copy, path);
            Undo.RegisterCreatedObjectUndo(copy, "MapLab: сохранить пресет");
            AssetDatabase.SaveAssets();
            Undo.RecordObject(view, "MapLab: настройки");
            view.Settings = copy;
            serialized = null;
            status = "Пресет сохранён: " + path;
        }

        private void ExportPng()
        {
            if (view.Data == null || view.CameraRig == null)
            {
                status = "Сначала нажмите Generate.";
                return;
            }
            Vector2Int size = ExportSizes[exportSize];
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "MapLabExports"));
            Directory.CreateDirectory(folder);
            string name = $"MapLab_{view.Settings.General.Preset}_{view.Settings.General.Seed}";
            // Панель сохранения сама спрашивает подтверждение перезаписи.
            string path = EditorUtility.SaveFilePanel("Экспорт PNG", folder, name, "png");
            if (string.IsNullOrEmpty(path))
                return;
            RunGuarded(() =>
            {
                byte[] png = MapLabExport.RenderPng(view.CameraRig, size.x, size.y, supersample);
                File.WriteAllBytes(path, png);
                MapLabExportInfo.Write(view, path, size.x, size.y);
                status = "Сохранено: " + path;
            });
        }

        private void OnSceneGui(SceneView sceneView)
        {
            if (!sceneClick || view == null || view.Data == null)
                return;
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                status = view.Raycast(ray, out Vector3 point) ? view.Describe(point) : "Вне карты.";
                e.Use();
                Repaint();
            }
        }
    }
}
