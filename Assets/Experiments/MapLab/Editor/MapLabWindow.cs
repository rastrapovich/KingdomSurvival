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
            "General", "Relief", "Water", "RoadsAndFields", "Cliffs", "Vegetation", "Settlement", "PainterlyStyle", "Camera", "Art", "Stage03", "Stage04", "Diagnostics", "Advanced",
        };

        private static readonly string[] GroupTitles =
        {
            "Общее (General)", "Рельеф (Relief)", "Вода (Water)", "Дороги и поля (Roads And Fields)", "Уступы (Cliffs)",
            "Растительность (Vegetation)", "Поселение (Settlement)", "Живописный стиль (Painterly Style)", "Камера (Camera)", "Арт этапа 02 (Art) · применяется пересборкой объектов",
            "Этап 03 (Stage03) · перекраска, перо, тени, акценты", "Этап 04 (Stage04) · тень, луг, узлы, трава, ритм леса, герой", "Диагностика (Diagnostics)", "Дополнительно (Advanced)",
        };

        // Группы, изменение которых не требует перестройки.
        private static readonly bool[] CheapGroups = { false, false, false, false, false, false, false, true, true, true, true, true, true, false };

        private static readonly Vector2Int[] ExportSizes = { new Vector2Int(2048, 1365), new Vector2Int(3072, 2048), new Vector2Int(1536, 1024), new Vector2Int(1920, 1080) };
        private static readonly string[] ExportSizeNames = { "2048 × 1365", "3072 × 2048", "1536 × 1024", "1920 × 1080" };

        private MapLabView view;
        private SerializedObject serialized;
        private Vector2 scroll;
        private readonly bool[] open = { false, false, false, false, false, false, false, false, true, false, false, false, false, false };
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
            DrawStage03();
            DrawStage02();

            if (serialized == null || serialized.targetObject != view.Settings)
                serialized = new SerializedObject(view.Settings);
            serialized.Update();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            bool cheapChanged = false;
            bool artChanged = false;
            bool stage03Changed = false;
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
                    {
                        cheapChanged = true;
                        artChanged |= Groups[g] == "Art";
                        stage03Changed |= Groups[g] == "Stage03" || Groups[g] == "Stage04";
                    }
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }
            EditorGUILayout.EndScrollView();
            if (serialized.ApplyModifiedProperties() && cheapChanged)
            {
                if (stage03Changed && view.Data != null)
                    RunGuarded(() => { view.RebuildArt(); Require(view.RebuildStage03Layers()); });
                ApplyCheap(artChanged);
            }
        }

        // ---------- Этап 03: тушь, мировые мазки, лес, тени, камера 35° ----------

        private bool stage03Open = true;

        private void DrawStage03()
        {
            stage03Open = EditorGUILayout.BeginFoldoutHeaderGroup(stage03Open, "Этап 03 · тушь и спокойные массы, камера до 35°");
            if (stage03Open)
            {
                MapLabSettings s = view.Settings;
                MapLabStage03Settings s3 = s.Stage03;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Подключить пакет этапа 03…", "Отдельная папка и набор (Assets/_Project/Art/MapLabStage03); этап 02 и его калибровка не перезаписываются.")))
                    RunGuarded(() => { MapLabArtImport.ImportStage03Menu(); });
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(MapLabArtImport.LastStage03Folder)))
                {
                    if (GUILayout.Button(new GUIContent("Переподключить", MapLabArtImport.LastStage03Folder)))
                        RunGuarded(() => { MapLabArtImport.ReimportStage03Menu(); });
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Подключить героя (этап 04)…", "Пакет ревизии 4: 12 кадров героя (50° и 35° × шесть направлений) в отдельный набор Assets/_Project/Art/MapLabStage04.")))
                    RunGuarded(() => { MapLabArtImport.ImportStage04Menu(); });
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(MapLabArtImport.LastStage04Folder)))
                {
                    if (GUILayout.Button(new GUIContent("Переподключить героя", MapLabArtImport.LastStage04Folder)))
                        RunGuarded(() => { MapLabArtImport.ReimportStage04Menu(); });
                }
                EditorGUILayout.EndHorizontal();
                MapLabMarkerSet hero = view.MarkerSet04;
                if (hero != null)
                    EditorGUILayout.LabelField($"Герой: наборов {hero.PitchSets.Count} ({string.Join("°, ", hero.PitchSets.ConvertAll(p => p.Pitch.ToString("0")))}°){(hero.Complete ? "" : " · есть пустые кадры")}" +
                                               (view.Marker != null && s.IsStage04 ? $" · сейчас {view.Marker.PitchSetPitch:0}°" : ""), EditorStyles.miniLabel);
                MapLabArtSet set03 = view.ResolvedArtSet03;
                if (set03 == null)
                    EditorGUILayout.HelpBox("Набор этапа 03 не подключён — на этапе 03 деревья и дома процедурные. Нажмите «Подключить пакет этапа 03…».", MessageType.Info);
                else
                    EditorGUILayout.HelpBox($"{set03.PackageId} · подключение №{set03.ImportVersion} · записей {set03.Entries.Count}" + (set03.MissingTextures > 0 ? $" · без картинки {set03.MissingTextures}" : ""), set03.MissingTextures > 0 ? MessageType.Warning : MessageType.None);

                // Язык карты и слои: каждый переключатель — одно понятное действие.
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(new GUIContent("Язык карты", "Stage02 — прежний живописный вид для сравнения; Stage03 — тушь, спокойные массы. Смена — полная перестройка."));
                MapLabLook look = GUILayout.Toggle(s3.Look == MapLabLook.Stage02, "Stage02", EditorStyles.miniButtonLeft) ? MapLabLook.Stage02 : s3.Look;
                look = GUILayout.Toggle(s3.Look == MapLabLook.Stage03, "Stage03", EditorStyles.miniButtonMid) ? MapLabLook.Stage03 : look;
                look = GUILayout.Toggle(s3.Look == MapLabLook.Stage04, "Stage04", EditorStyles.miniButtonRight) ? MapLabLook.Stage04 : look;
                EditorGUILayout.EndHorizontal();
                if (look != s3.Look)
                {
                    Undo.RecordObject(s, "MapLab: язык карты");
                    s3.Look = look;
                    EditorUtility.SetDirty(s);
                    RunGeneration("Generate", p => view.Generate(p));
                }
                EditorGUILayout.BeginHorizontal();
                bool undercoat = GUILayout.Toggle(s.Art.GroundTextures, new GUIContent("Подложка", "Принятые цветовые подложки этапа 02 (трава, земля, камень)."), EditorStyles.miniButtonLeft);
                bool strokes = GUILayout.Toggle(s.Diagnostics.ShowStrokes, new GUIContent("Мазки", "Программные мазки покраски."), EditorStyles.miniButtonMid);
                bool ink = GUILayout.Toggle(s3.Ink, new GUIContent("Перо", "Тушевые кромки дорог и троп."), EditorStyles.miniButtonMid);
                bool accents = GUILayout.Toggle(s3.ForestAccents, new GUIContent("Акценты леса", "Линия и подсветка по верхним кромкам групп крон."), EditorStyles.miniButtonMid);
                bool shadows = GUILayout.Toggle(s3.ObjectShadows, new GUIContent("Тени", "Стилизованные тени объектов (переключение — перестройка: тень рельефа считается заново)."), EditorStyles.miniButtonRight);
                EditorGUILayout.EndHorizontal();
                if (undercoat != s.Art.GroundTextures || strokes != s.Diagnostics.ShowStrokes || ink != s3.Ink || accents != s3.ForestAccents || shadows != s3.ObjectShadows)
                {
                    Undo.RecordObject(s, "MapLab: слои этапа 03");
                    bool regenerate = shadows != s3.ObjectShadows;
                    bool repaint = strokes != s.Diagnostics.ShowStrokes || ink != s3.Ink;
                    s.Art.GroundTextures = undercoat;
                    s.Diagnostics.ShowStrokes = strokes;
                    s3.Ink = ink;
                    s3.ForestAccents = accents;
                    s3.ObjectShadows = shadows;
                    EditorUtility.SetDirty(s);
                    if (regenerate)
                        RunGeneration("Generate", p => view.Generate(p));
                    else if (repaint)
                        RunGuarded(() => Require(view.RebuildStage03Layers()));
                    else
                        ApplyCheap(false);
                }

                MapLabCameraRig rig = view.CameraRig;
                if (rig != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel(new GUIContent("Наклон", "Фокус сохраняется. «От приближения» — 60° на общем виде, 35° на наибольшем фактическом приближении."));
                    if (GUILayout.Button("50°", EditorStyles.miniButtonLeft)) SetMode(MapLabCameraMode.Volume50);
                    if (GUILayout.Button("35°", EditorStyles.miniButtonMid)) SetMode(MapLabCameraMode.Low35);
                    bool byZoom = GUILayout.Toggle(s.Camera.PitchByZoom, "От приближения", EditorStyles.miniButtonRight);
                    EditorGUILayout.EndHorizontal();
                    if (byZoom != s.Camera.PitchByZoom)
                    {
                        Undo.RecordObject(s, "MapLab: наклон от приближения");
                        s.Camera.PitchByZoom = byZoom;
                        EditorUtility.SetDirty(s);
                        WithCamera(() => { rig.RecomputeFitSize(); rig.Apply(); view.OnCameraChanged(); });
                    }
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel("Приближение");
                    foreach (float z in new[] { 1f, 2f, 4f, rig.ZoomLimits.y })
                        if (GUILayout.Button(z >= rig.ZoomLimits.y ? $"Max {z:0.#}" : "Zoom " + z, EditorStyles.miniButton))
                            WithCamera(() => rig.ZoomAround(z, new Vector2(0.5f, 0.5f), false));
                    EditorGUILayout.EndHorizontal();
                    string houses = s.IsStage03 && view.HasArtHouses03 ? $" · кадр домов {view.HouseView}°" : "";
                    EditorGUILayout.LabelField($"Сейчас {rig.Pitch:0.#}°, Zoom {rig.Zoom:0.00}{houses}{(view.ArtSpritesApproximate ? " · ракурс рисунков приближённый" : "")}", EditorStyles.miniLabel);
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Диагностика");
                DiagButton("Итог", MapLabDiagnosticMode.Final);
                DiagButton("Краска", MapLabDiagnosticMode.SourcePaint);
                DiagButton("Мазки", MapLabDiagnosticMode.StrokeContribution);
                DiagButton("Дороги", MapLabDiagnosticMode.RoadGraph);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(" ");
                DiagButton("Группы леса", MapLabDiagnosticMode.ForestGroups);
                DiagButton("Акценты", MapLabDiagnosticMode.ForestAccents);
                DiagButton("Тени", MapLabDiagnosticMode.ShadowMask);
                DiagButton("Контакт", MapLabDiagnosticMode.ContactGradient);
                EditorGUILayout.EndHorizontal();
                MapLabTiledPainter.Result paint = view.LastTiledPaint;
                if (s.IsStage03 && paint != null)
                    EditorGUILayout.LabelField($"Покраска {paint.TexelsPerUnit:0.#} текс./ед., тайлов {paint.Tiles.Count}, мазков {paint.Strokes}, {paint.Milliseconds:0} мс · перо {view.InkStats.Polylines} линий · теней {view.ShadowShapes}" + (s.IsStage04 ? $" · пучков травы {view.GrassTufts}" : ""), EditorStyles.miniLabel);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DiagButton(string label, MapLabDiagnosticMode mode)
        {
            bool on = view.Settings.Diagnostics.Mode == mode;
            if (GUILayout.Toggle(on, label, EditorStyles.miniButton) == on)
                return;
            Undo.RecordObject(view.Settings, "MapLab: диагностика");
            view.Settings.Diagnostics.Mode = on ? MapLabDiagnosticMode.Final : mode;
            EditorUtility.SetDirty(view.Settings);
            ApplyCheap(false);
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

        // ---------- Этап 02: арт, камера, фишка ----------

        private bool stage02Open = true;
        private bool previewing;

        private void DrawStage02()
        {
            stage02Open = EditorGUILayout.BeginFoldoutHeaderGroup(stage02Open, "Этап 02 · арт, камеры 60/50/40, фишка");
            if (stage02Open)
            {
                MapLabArtSet set = view.ResolvedArtSet;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Подключить пакет этапа 02…", "Выбрать распакованную папку ZIP с ASSET_MANIFEST.json: проверка, копирование, импорт, ArtSet.")))
                    RunGuarded(() => { MapLabArtImport.ImportMenu(); });
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(MapLabArtImport.LastPackageFolder)))
                {
                    if (GUILayout.Button(new GUIContent("Переподключить", "Повторный импорт из той же папки: обновление по стабильным ID, без дубликатов.\n" + MapLabArtImport.LastPackageFolder)))
                        RunGuarded(() => { MapLabArtImport.ReimportMenu(); });
                }
                if (GUILayout.Button(new GUIContent("Калибровка арта…", "Опора, порог альфы, рост; проверка регистрации кадров фишки.")))
                    MapLabArtCalibrationWindow.Open();
                EditorGUILayout.EndHorizontal();
                if (set == null)
                    EditorGUILayout.HelpBox("Набор арта не подключён — карта строится процедурно. Нажмите «Подключить пакет этапа 02…».", MessageType.Info);
                else
                {
                    int missing = set.MissingTextures;
                    string cards = view.ArtPlan != null ? $" · карточек на карте {view.ArtPlan.Count}, материалов арта {view.ArtMaterialCount}" : "";
                    EditorGUILayout.HelpBox($"{set.PackageId} · подключение №{set.ImportVersion} ({set.ImportedAt}) · записей {set.Entries.Count}" +
                                            (missing > 0 ? $" · без картинки {missing} (fallback)" : "") + cards, missing > 0 ? MessageType.Warning : MessageType.None);
                }

                MapLabCameraRig rig = view.CameraRig;
                if (rig != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel(new GUIContent("Камера", "Фокус сохраняется при смене наклона и приближения."));
                    if (GUILayout.Button("Атлас 60°", EditorStyles.miniButtonLeft)) SetMode(MapLabCameraMode.Atlas60);
                    if (GUILayout.Button("Объёмная 50°", EditorStyles.miniButtonMid)) SetMode(MapLabCameraMode.Volume50);
                    if (GUILayout.Button("Диорама 40°", EditorStyles.miniButtonRight)) SetMode(MapLabCameraMode.Diorama40);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel("Приближение");
                    foreach (float z in new[] { 1f, 2f, 4f })
                        if (GUILayout.Button("Zoom " + z, EditorStyles.miniButton))
                            WithCamera(() => rig.ZoomAround(z, new Vector2(0.5f, 0.5f), false));
                    if (GUILayout.Button(new GUIContent("Фокус: участок", "Центр кадра — демонстрационный участок (поселение)."), EditorStyles.miniButton))
                        WithCamera(() => { Vector2 f = view.DemoFocus; rig.Target = rig.ToGround(new Vector3(f.x, view.Data != null ? view.Data.SampleHeight(f.x, f.y) : 0f, f.y)); rig.Apply(); });
                    if (GUILayout.Button(new GUIContent("Fit Map", "Вся карта в кадре (переносит фокус в центр)."), EditorStyles.miniButton))
                        WithCamera(() => rig.Fit());
                    EditorGUILayout.EndHorizontal();
                    string approx = view.ArtSpritesApproximate ? " · рисунки нарисованы под 50° — ракурс приближённый" : "";
                    EditorGUILayout.LabelField($"Сейчас: {(rig.PitchByZoom ? "наклон от приближения" : rig.Mode.ToString())}, {rig.Pitch:0.#}°, Zoom {rig.Zoom:0.00}{approx}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField("Edit Mode — кнопки выше; Play Mode — колесо к курсору, 1/2/3, P, WASD, ПКМ.", EditorStyles.miniLabel);
                }

                MapLabMarker marker = view.Marker;
                if (marker != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel(new GUIContent("Фишка", "Направление относительно камеры; кадр выбирается по сектору 60°."));
                    for (int i = 0; i < 6; i++)
                    {
                        int index = i;
                        if (GUILayout.Button(MapLabMarkerSet.DirectionNames[i].Replace("_", " "), EditorStyles.miniButton))
                        {
                            marker.PreviewMove = false;
                            marker.FacingDegrees = (rig != null ? rig.Azimuth : 0f) + 180f - index * 60f;
                            marker.Refresh();
                            SceneView.RepaintAll();
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    bool preview = GUILayout.Toggle(marker.PreviewMove, "Preview движения", EditorStyles.miniButtonLeft);
                    if (preview != marker.PreviewMove)
                    {
                        marker.PreviewMove = preview;
                        SetPreview(preview);
                    }
                    bool arrow = GUILayout.Toggle(marker.ShowArrow, "Стрелка", EditorStyles.miniButtonMid);
                    if (arrow != marker.ShowArrow)
                    {
                        marker.ShowArrow = arrow;
                        marker.Refresh();
                    }
                    bool ghost = GUILayout.Toggle(view.Settings.Art.MarkerGhost, "Контур за препятствием", EditorStyles.miniButtonRight);
                    if (ghost != view.Settings.Art.MarkerGhost)
                    {
                        view.Settings.Art.MarkerGhost = ghost;
                        EditorUtility.SetDirty(view.Settings);
                        marker.Refresh();
                    }
                    EditorGUILayout.EndHorizontal();
                    if (!string.IsNullOrEmpty(marker.Status))
                        EditorGUILayout.LabelField(marker.Status, EditorStyles.miniLabel);
                }

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(set == null || view.Data == null))
                {
                    if (GUILayout.Button(new GUIContent("Тестовая полоса", "Перед картой: исходный PNG и экземпляр сцены (светлая панель), исходная альфа и AlphaClip (тёмная).")))
                        RunGuarded(() => ShowStrip(set));
                }
                if (GUILayout.Button("Убрать полосу"))
                    view.ClearArtStrip();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void SetMode(MapLabCameraMode mode)
        {
            Undo.RecordObject(view.Settings, "MapLab: режим камеры");
            WithCamera(() => view.CameraRig.SetMode(mode));
            EditorUtility.SetDirty(view.Settings);
            serialized = null;
        }

        private void WithCamera(Action action)
        {
            if (view.CameraRig == null || view.Data == null)
            {
                status = "Сначала нажмите Generate.";
                return;
            }
            action();
            view.OnCameraChanged();
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private void ShowStrip(MapLabArtSet set)
        {
            System.Collections.Generic.List<MapLabArtEntry> entries = new System.Collections.Generic.List<MapLabArtEntry>();
            foreach (string id in new[] { "Tree_Oak_01", "House_Thatch_01", "Figure_Front" })
            {
                MapLabArtEntry e = set.Find(id);
                if (e != null && e.HasTexture)
                    entries.Add(e);
            }
            Vector3 center = view.BuildArtStrip(entries);
            if (view.CameraRig != null && entries.Count > 0)
            {
                view.CameraRig.Target = center;
                view.CameraRig.Zoom = 3.2f;
                view.CameraRig.Apply();
            }
            status = "Тестовая полоса: слева в паре — исходный PNG, справа — экземпляр сцены; ниже — альфа и AlphaClip.";
        }

        private void SetPreview(bool on)
        {
            EditorApplication.update -= PreviewTick;
            previewing = on;
            if (on)
                EditorApplication.update += PreviewTick;
        }

        private void PreviewTick()
        {
            MapLabMarker marker = view != null ? view.Marker : null;
            if (!previewing || marker == null || !marker.PreviewMove)
            {
                SetPreview(false);
                return;
            }
            if (!Application.isPlaying)
            {
                marker.Tick();
                SceneView.RepaintAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
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

        private void ApplyCheap(bool art)
        {
            if (art)
                view.RebuildArt();
            view.ApplyMaterialSettings();
            view.ApplyVisibility();
            // Камера: фокус и приближение сохраняются; полный Fit — отдельной кнопкой.
            if (view.CameraRig != null)
            {
                view.CameraRig.RecomputeFitSize();
                view.CameraRig.Apply();
                view.OnCameraChanged();
            }
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
