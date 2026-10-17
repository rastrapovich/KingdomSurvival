using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12М: окно «Обработка кадра» — всё о виде готового кадра места в
    // одном месте: большой предпросмотр выбранного места с часом суток,
    // сравнение «до / после» шторкой или рядом (до — без обработки или
    // выбранный сохранённый стиль), справа — панель FrameEffectsEditor
    // (стили и разделы). Правки — те же данные, что у Базы локаций
    // (небо места или общий свет мира), с Undo и записью ассета.
    public sealed class FrameStyleWindow : EditorWindow
    {
        private const string PrefsKey = "KS.FrameStyleWindow.";

        private enum CompareMode { None, Curtain, SideBySide }
        private enum CompareSource { Raw, Style, Reference }

        // Набор материалов рисунков сменился — окна с предпросмотром места пересобирают его.
        public static event Action StructureChanged;

        private LocalLocationDatabaseAsset database;
        private BattlefieldDatabaseAsset fields;
        private string selectedId;
        private PreviewRenderUtility preview;
        private LocationWorldRenderer renderer;
        private IMGUIContainer canvas;
        private ScrollView panel;
        private Label status;
        private Slider hourSlider;
        private float hour = 13, zoom = 1, split = .5f;
        private Vector2 center;
        private bool cycle, dragging, draggingSplit;
        private CompareMode compare = CompareMode.None;
        private CompareSource compareSource = CompareSource.Raw;
        private RenderTexture afterTexture, beforeTexture;
        private double lastTick, nextRepaint, saveAt = -1, rebuildAt = -1;
        private double dissolveStart = -1;
        private double transitionBackAt = -1;
        private float transitionSeconds;
        private bool draggingZone;
        private float dissolveDuration = 1;

        private LocalLocationDefinition Location => database?.locations.Find(item => item.Id == selectedId);
        private LocationVisualDefinition Visual => database?.FindVisual(selectedId);
        private BattlefieldDefinitionData Field => Location != null ? fields?.FindById(Location.BattlefieldId) : null;
        private LocationSky Sky => new LocationSky(Visual, database.worldLighting);

        [MenuItem("Kingdom Survival/Обработка кадра")]
        public static void OpenWindow() => Open(null);

        public static void Open(string locationId)
        {
            FrameStyleWindow window = GetWindow<FrameStyleWindow>();
            window.titleContent = new GUIContent("Обработка кадра");
            window.minSize = new Vector2(980, 560);
            if (!string.IsNullOrEmpty(locationId) && window.selectedId != locationId)
            {
                window.selectedId = locationId;
                EditorPrefs.SetString(PrefsKey + "Location", locationId);
                window.center = Vector2.zero;
                if (window.panel != null) { window.BuildPanel(); window.RebuildPreview(); }
            }
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += UndoChanged;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= UndoChanged;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayChanged;
            ReleasePreview();
            if (database != null) AssetDatabase.SaveAssetIfDirty(database);
        }

        private void UndoChanged()
        {
            if (renderer != null && renderer.BuiltSpriteStructure != renderer.CurrentSpriteStructure) RebuildPreview();
            BuildPanel();
        }

        private void PlayChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) ReleasePreview();
            if (state == PlayModeStateChange.EnteredEditMode) RebuildPreview();
        }

        public void CreateGUI()
        {
            database = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            fields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
            if (string.IsNullOrEmpty(selectedId)) selectedId = EditorPrefs.GetString(PrefsKey + "Location", "");
            if (database != null && Location == null)
                selectedId = database.locations.FirstOrDefault(item => database.FindVisual(item.Id) != null)?.Id ?? database.locations.FirstOrDefault()?.Id;
            compare = (CompareMode)EditorPrefs.GetInt(PrefsKey + "Compare", 0);
            compareSource = (CompareSource)EditorPrefs.GetInt(PrefsKey + "CompareSource", 0);

            rootVisualElement.Clear();
            rootVisualElement.style.backgroundColor = new Color(.12f, .145f, .135f);
            TwoPaneSplitView split = new TwoPaneSplitView(1, 440, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;

            VisualElement left = new VisualElement { style = { flexGrow = 1 } };
            left.Add(BuildToolbar());
            left.Add(BuildToolsBar());
            canvas = new IMGUIContainer(DrawPreview) { style = { flexGrow = 1 } };
            canvas.focusable = true;
            left.Add(canvas);
            status = new Label { style = { marginLeft = 6, marginBottom = 3, color = new Color(.75f, .78f, .72f), whiteSpace = WhiteSpace.Normal } };
            left.Add(status);
            split.Add(left);

            VisualElement right = new VisualElement { style = { flexGrow = 1, paddingLeft = 6, paddingRight = 4, paddingTop = 4 } };
            panel = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            right.Add(panel);
            split.Add(right);
            rootVisualElement.Add(split);

            if (database == null) { status.text = "База мест не найдена: " + LocalLocationDatabaseAsset.AssetPath; return; }
            BuildPanel();
            RebuildPreview();
        }

        private VisualElement BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();
            List<LocalLocationDefinition> places = database != null
                ? database.locations.Where(item => database.FindVisual(item.Id) != null).OrderBy(item => item.IsBattlefield).ThenBy(item => item.DisplayName).ToList()
                : new List<LocalLocationDefinition>();
            if (places.Count > 0)
            {
                List<string> names = places.Select(item => (item.IsBattlefield ? "Поле боя · " : "") + item.DisplayName).ToList();
                int index = Mathf.Max(0, places.FindIndex(item => item.Id == selectedId));
                PopupField<string> place = new PopupField<string>(names, index) { tooltip = "Какое место показывать и править" };
                place.style.minWidth = 200;
                place.RegisterValueChangedCallback(evt =>
                {
                    selectedId = places[place.index].Id;
                    EditorPrefs.SetString(PrefsKey + "Location", selectedId);
                    center = Vector2.zero;
                    zoom = 1;
                    BuildPanel();
                    RebuildPreview();
                });
                toolbar.Add(place);
            }
            hourSlider = new Slider("Час", 0, 24) { value = hour, showInputField = true };
            hourSlider.style.width = 230;
            hourSlider.labelElement.style.minWidth = 30;
            hourSlider.RegisterValueChangedCallback(evt => hour = evt.newValue);
            toolbar.Add(hourSlider);
            ToolbarToggle play = new ToolbarToggle { text = "▶ Сутки", value = cycle, tooltip = "Прокручивать сутки (30 секунд на сутки)" };
            play.RegisterValueChangedCallback(evt => cycle = evt.newValue);
            toolbar.Add(play);
            PopupField<string> mode = new PopupField<string>(new List<string> { "Без сравнения", "Сравнение: шторка", "Сравнение: рядом" }, (int)compare)
            { tooltip = "Слева — «до», справа — «после». Шторку двигайте мышью." };
            mode.RegisterValueChangedCallback(evt => { compare = (CompareMode)mode.index; EditorPrefs.SetInt(PrefsKey + "Compare", mode.index); });
            toolbar.Add(mode);
            PopupField<string> source = new PopupField<string>(new List<string> { "«До» — без обработки", "«До» — выбранный стиль", "«До» — эталонная картинка" }, (int)compareSource)
            { tooltip = "С чем сравнивать: кадр без обработки или выбранный в списке сохранённый стиль" };
            source.RegisterValueChangedCallback(evt => { compareSource = (CompareSource)source.index; EditorPrefs.SetInt(PrefsKey + "CompareSource", source.index); });
            toolbar.Add(source);
            toolbar.Add(new ToolbarButton(() => { zoom = 1; center = Vector2.zero; }) { text = "Вписать", tooltip = "Всё место целиком (колесо — масштаб, перетаскивание — сдвиг)" });
            toolbar.Add(new ToolbarButton(OpenDatabase) { text = "База локаций", tooltip = "Открыть это место в Базе локаций" });
            return toolbar;
        }

        private void OpenDatabase() => KingdomSurvival.ArtAssets.Editor.ArtAssetUsages.OpenLocation(selectedId, null);

        private void BuildPanel()
        {
            if (panel == null || database == null) return;
            panel.Clear();
            LocalLocationDefinition location = Location;
            if (location == null) { panel.Add(new HelpBox("Нет мест с художественной сборкой.", HelpBoxMessageType.Info)); return; }
            Label title = new Label(location.DisplayName) { style = { fontSize = 15, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(.88f, .78f, .53f), marginBottom = 4 } };
            panel.Add(title);
            FrameEffectsEditor editor = new FrameEffectsEditor
            {
                Database = database,
                Visual = Visual,
                PlaceName = location.DisplayName,
                Change = Change,
                Rebuild = BuildPanel,
                Pulse = strength => renderer?.Pulse(strength),
                TestDissolve = StartDissolveTest,
                Snapshot = Snapshot,
                ShowsCreatures = true,
                CurrentHour = () => hour,
                SetHour = value => { hour = value; cycle = false; hourSlider?.SetValueWithoutNotify(hour); },
                ViewCenter = () => center,
                SetViewCenter = value => center = value,
                TestTransition = StartTransitionTest,
                Hit = strength => renderer?.HitEffects(strength),
                TestState = name => FrameStateTest.Run(renderer, name)
            };
            panel.Add(editor.Build());
        }

        // Правка данных: Undo, запись через полсекунды; набор материалов
        // рисунков сменился — пересборка предпросмотров (здесь и в Базе локаций).
        private void Change(Action action, bool rebuildPanel)
        {
            Undo.RecordObject(database, "Обработка кадра");
            action();
            EditorUtility.SetDirty(database);
            saveAt = EditorApplication.timeSinceStartup + .5;
            if (renderer != null && renderer.BuiltSpriteStructure != renderer.CurrentSpriteStructure)
                rebuildAt = EditorApplication.timeSinceStartup + .2;
            if (rebuildPanel) BuildPanel();
            canvas?.MarkDirtyRepaint();
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = Mathf.Clamp((float)(now - lastTick), 0, .1f);
            lastTick = now;
            if (cycle)
            {
                hour = Mathf.Repeat(hour + delta * 24 / 30, 24);
                hourSlider?.SetValueWithoutNotify(hour);
            }
            if (transitionBackAt > 0 && now >= transitionBackAt)
            {
                transitionBackAt = -1;
                renderer?.EndTransition(transitionSeconds);
            }
            if (saveAt > 0 && now >= saveAt && !dragging)
            {
                saveAt = -1;
                if (database != null) AssetDatabase.SaveAssetIfDirty(database);
            }
            if (rebuildAt > 0 && now >= rebuildAt)
            {
                rebuildAt = -1;
                RebuildPreview();
                StructureChanged?.Invoke();
            }
            bool active = focusedWindow == this || mouseOverWindow == this || dragging;
            if (now >= nextRepaint)
            {
                nextRepaint = now + (active ? 1 / 30.0 : 1 / 6.0);
                canvas?.MarkDirtyRepaint();
            }
        }

        // ------------------------------------------------------------------
        // Предпросмотр
        // ------------------------------------------------------------------

        private void RebuildPreview()
        {
            ReleasePreview();
            if (database == null || Location == null || Field == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            preview = new PreviewRenderUtility(true);
            renderer = new LocationWorldRenderer(Location, Visual, Field, null, database.worldLighting);
            renderer.Camera.enabled = false;
            preview.AddSingleGO(renderer.Root);
            Camera camera = preview.camera;
            camera.orthographic = true;
            camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .045f, .04f);
            camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            renderer.BindCamera(camera);
            if (status != null) status.text = "";
        }

        private void ReleasePreview()
        {
            renderer?.Dispose();
            renderer = null;
            preview?.Cleanup();
            preview = null;
            Release(ref afterTexture);
            Release(ref beforeTexture);
            Release(ref referenceFitted);
        }

        private static void Release(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            DestroyImmediate(texture);
            texture = null;
        }

        private Vector2 CanvasSize => LocationVisualGeometry.CanvasSize(Location);

        // Высота видимого в пикселях рисунка: при масштабе 1 место целиком.
        private float ViewHeight(Rect frame)
        {
            Vector2 size = CanvasSize;
            float aspect = frame.width / Mathf.Max(1, frame.height);
            return Mathf.Max(size.y, size.x / Mathf.Max(.01f, aspect)) / zoom;
        }

        private void DrawPreview()
        {
            Rect area = new Rect(0, 0, canvas.contentRect.width, canvas.contentRect.height);
            if (float.IsNaN(area.width) || area.width < 10 || area.height < 10) return;
            EditorGUI.DrawRect(area, new Color(.045f, .055f, .05f));
            if (renderer == null || preview == null)
            {
                GUI.Label(new Rect(10, 10, area.width - 20, 40), EditorApplication.isPlaying ? "Идёт игра — предпросмотр вернётся после выхода из Play Mode." : "Выберите место с художественной сборкой и полем боя.");
                return;
            }
            if (center == Vector2.zero) center = CanvasSize / 2;
            HandleDrop(area);
            HandleInput(area);
            if (Event.current.type != EventType.Repaint) return;

            UpdateDissolveTest();
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            if (compare == CompareMode.SideBySide)
            {
                Rect leftFrame = new Rect(area.x, area.y, area.width / 2 - 1, area.height);
                Rect rightFrame = new Rect(area.x + area.width / 2 + 1, area.y, area.width / 2 - 1, area.height);
                Texture before = Before(leftFrame);
                Texture after = Render(rightFrame, false, ref afterTexture);
                if (before != null) GUI.DrawTexture(leftFrame, before, ScaleMode.StretchToFill, false);
                if (after != null) GUI.DrawTexture(rightFrame, after, ScaleMode.StretchToFill, false);
                Caption(leftFrame, BeforeName(), true);
                Caption(rightFrame, "после", false);
            }
            else if (compare == CompareMode.Curtain)
            {
                Texture before = Before(area);
                Texture after = Render(area, false, ref afterTexture);
                if (after != null) GUI.DrawTexture(area, after, ScaleMode.StretchToFill, false);
                float x = area.x + area.width * split;
                if (before != null)
                    GUI.DrawTextureWithTexCoords(new Rect(area.x, area.y, x - area.x, area.height), before, new Rect(0, 0, split, 1), false);
                EditorGUI.DrawRect(new Rect(x - 1, area.y, 2, area.height), new Color(1, .9f, .6f, .9f));
                EditorGUI.DrawRect(new Rect(x - 7, area.center.y - 18, 14, 36), new Color(1, .9f, .6f, .9f));
                Caption(new Rect(area.x, area.y, x - area.x, area.height), BeforeName(), true);
                Caption(new Rect(x, area.y, area.xMax - x, area.height), "после", false);
            }
            else
            {
                Texture after = Render(area, false, ref afterTexture);
                if (after != null) GUI.DrawTexture(area, after, ScaleMode.StretchToFill, false);
            }
            if (compare != CompareMode.SideBySide) DrawZones(area);
            frameMs = Mathf.Lerp(frameMs, (float)watch.Elapsed.TotalMilliseconds, .2f);
            Readback(afterTexture);
            DrawTools(compare == CompareMode.SideBySide ? new Rect(area.x + area.width / 2 + 1, area.y, area.width / 2 - 1, area.height) : area);
            if (frameLabel != null) frameLabel.text = "кадр " + frameMs.ToString("0.0") + " мс" + (LocationWorldRenderer.QualityLevel != LocationFrameQualityLevel.High ? " · качество: " + (LocationWorldRenderer.QualityLevel == LocationFrameQualityLevel.Medium ? "среднее" : "низкое") : "");
            LocationWorldRenderer.ClearFrameGlobals();
            if (status != null)
            {
                LocationPostEffects post = Sky.Post;
                status.text = (post != null && post.Enabled ? "Обработка кадра включена" : "Обработка кадра ВЫКЛЮЧЕНА") +
                              " · " + LocationLightingTest.FormatHour(hour) + " · частиц: " + renderer.AtmosphereParticleCount +
                              " · колесо — масштаб, перетаскивание — сдвиг" + (compare == CompareMode.Curtain ? ", шторка — мышью у линии" : "") +
                              " · картинку (PNG, спрайт) можно бросить на кадр: LUT, палитра, бумага, частицы";
            }
        }

        // «До»: эталонная картинка или кадр без обработки / со стилем.
        private Texture Before(Rect frame) =>
            compareSource == CompareSource.Reference && reference != null ? ReferenceFor(frame) : Render(frame, true, ref beforeTexture);

        private string BeforeName()
        {
            if (compareSource == CompareSource.Reference)
                return reference != null ? "до · эталон «" + reference.name + "»" : "до · эталон не выбран";
            if (compareSource == CompareSource.Style)
            {
                LocationFrameStyle style = database.FindFrameStyle(EditorPrefs.GetString("KS.FrameEffects.Style", ""));
                return style != null ? "до · стиль «" + style.Name + "»" : "до · стиль не выбран";
            }
            return "до · без обработки";
        }

        private static void Caption(Rect frame, string text, bool left)
        {
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1, .93f, .75f) }, alignment = left ? TextAnchor.UpperLeft : TextAnchor.UpperRight };
            Rect rect = new Rect(frame.x + 8, frame.y + 6, Mathf.Max(0, frame.width - 16), 20);
            EditorGUI.DrawRect(left ? new Rect(rect.x - 4, rect.y - 2, Mathf.Min(rect.width + 8, style.CalcSize(new GUIContent(text)).x + 8), 22)
                : new Rect(rect.xMax - style.CalcSize(new GUIContent(text)).x - 4, rect.y - 2, style.CalcSize(new GUIContent(text)).x + 8, 22), new Color(0, 0, 0, .45f));
            GUI.Label(rect, text, style);
        }

        // Кадр предпросмотра в свою текстуру (обработка кадра — этого места
        // и только его). before — «до»: без обработки или с выбранным стилем.
        private Texture Render(Rect frame, bool before, ref RenderTexture target)
        {
            LocationSky sky = Sky;
            LocationPostEffects own = sky.Post;
            LocationFrameStyle style = before && compareSource == CompareSource.Style ? database.FindFrameStyle(EditorPrefs.GetString("KS.FrameEffects.Style", "")) : null;
            try
            {
                if (style != null) sky.Post = style.Post;
                renderer.ShowFrameEffects(!before || style != null);
                renderer.SetFocus(center);
                renderer.SetTime(hour, (float)EditorApplication.timeSinceStartup);
                RenderActors();
                Camera camera = preview.camera;
                Vector2 world = LocationVisualGeometry.PixelToWorld(Location, center);
                Vector2 shake = renderer.ShakeOffset;
                camera.transform.position = new Vector3(world.x + shake.x, world.y + shake.y, -10);
                camera.transform.rotation = Quaternion.identity;
                camera.orthographicSize = ViewHeight(frame) / LocationVisualGeometry.PixelsPerUnit / 2;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = renderer.PostActive;
                renderer.IsolatePost();
                preview.BeginPreview(frame, GUIStyle.none);
                preview.Render(true);
                Texture texture = preview.EndPreview();
                target = Match(target, texture);
                // Blit оставляет свою цель активной: без возврата окно рисовало
                // бы дальше в эту текстуру, а не на экран (чёрный предпросмотр).
                RenderTexture active = RenderTexture.active;
                Graphics.Blit(texture, target);
                RenderTexture.active = active;
                return target;
            }
            finally
            {
                if (style != null) sky.Post = own;
                renderer.ShowFrameEffects(true);
            }
        }

        private static RenderTexture Match(RenderTexture texture, Texture source)
        {
            if (texture != null && texture.width == source.width && texture.height == source.height) return texture;
            Release(ref texture);
            RenderTextureDescriptor descriptor = source is RenderTexture rt ? rt.descriptor : new RenderTextureDescriptor(source.width, source.height, RenderTextureFormat.ARGBHalf, 0);
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            texture = new RenderTexture(descriptor) { hideFlags = HideFlags.HideAndDontSave };
            texture.Create();
            return texture;
        }

        // Фигуры: герой у старта теста и противники места (обводка, пар, растворение видны на них).
        private void RenderActors()
        {
            List<LocationWorldRenderer.ActorFrame> frames = new List<LocationWorldRenderer.ActorFrame>();
            LocationVisualDefinition visual = Visual;
            if (visual != null)
                frames.Add(new LocationWorldRenderer.ActorFrame
                {
                    Id = "preview_hero", UnitTypeId = visual.TestUnitId, Pixel = LocationVisualGeometry.ToPixel(Location, visual.TestStartPoint)
                });
            foreach (LocalEnemyDefinition enemy in Location.Enemies)
            {
                if (enemy == null || enemy.Point == null) continue;
                frames.Add(new LocationWorldRenderer.ActorFrame
                {
                    Id = "preview_" + enemy.InstanceId, UnitTypeId = enemy.UnitTypeId,
                    Kind = LocationWorldRenderer.ActorKind.Enemy, Pixel = new Vector2(enemy.Point.X, enemy.Point.Y)
                });
            }
            // Вкладка «Персонажи»: выбранное существо или все существа в ряд —
            // в центре кадра, чтобы подгонять цвет прямо на поле.
            int mode = FrameEffectsEditor.IsCharactersTab ? EditorPrefs.GetInt(FrameEffectsEditor.CreaturePreviewPrefs, 1) : 0;
            UnitDatabaseAsset units = mode > 0 ? Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath) : null;
            if (units != null)
            {
                string chosen = EditorPrefs.GetString(FrameEffectsEditor.CreaturePrefs, "");
                List<UnitDefinitionData> lineup = mode == 1
                    ? units.Units.Where(unit => unit != null && unit.Id == chosen).ToList()
                    : units.Units.Where(unit => unit != null).ToList();
                float step = renderer.HexSizePixels * 2.4f;
                int perRow = Mathf.Min(7, Mathf.Max(1, lineup.Count));
                for (int i = 0; i < lineup.Count; i++)
                {
                    int row = i / perRow, column = i % perRow;
                    int inRow = Mathf.Min(perRow, lineup.Count - row * perRow);
                    Vector2 pixel = center + new Vector2((column - (inRow - 1) / 2f) * step, (row - (lineup.Count - 1) / perRow / 2f) * step * 1.1f);
                    frames.Add(new LocationWorldRenderer.ActorFrame { Id = "preview_creature_" + lineup[i].Id, UnitTypeId = lineup[i].Id, Pixel = pixel });
                }
            }
            renderer.SetActors(frames, (float)EditorApplication.timeSinceStartup);
        }

        // Картинка, брошенная на предпросмотр: меню «Использовать как…» —
        // LUT, палитра, бумага или картинка слоя частиц. Эффект включается.
        private void HandleDrop(Rect area)
        {
            Event evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
            if (!area.Contains(evt.mousePosition) || !FrameTextureDrop.HasCandidate() || Visual == null) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                UnityEngine.Object dropped = DragAndDrop.objectReferences.FirstOrDefault(item => item is Texture2D || item is Sprite);
                string path = dropped == null ? (DragAndDrop.paths ?? Array.Empty<string>()).FirstOrDefault(FrameTextureDrop.IsDroppableFile) : null;
                // .cube — только LUT: без меню.
                if (LutTools.IsCube(path))
                {
                    Texture2D cube = LutTools.FromCubeFile(path, out string cubeError);
                    if (cube == null) EditorUtility.DisplayDialog("LUT", cubeError, "Понятно");
                    else Change(() =>
                    {
                        LocationPostEffects post = Sky.Post.Repair();
                        post.Enabled = true;
                        post.Tone.Lut = cube;
                        post.Tone.LutContribution = Mathf.Max(post.Tone.LutContribution, 1);
                    }, true);
                    evt.Use();
                    return;
                }
                GenericMenu menu = new GenericMenu();
                void Item(string title, FrameTextureKind kind, Action<LocationPostEffects, Texture2D> apply) =>
                    menu.AddItem(new GUIContent(title), false, () =>
                    {
                        Texture2D texture = dropped != null
                            ? FrameTextureDrop.FromObject(dropped, kind, out string error)
                            : FrameTextureDrop.FromFile(path, kind, out error);
                        if (texture == null) { EditorUtility.DisplayDialog("Картинка", error, "Понятно"); return; }
                        Change(() =>
                        {
                            LocationPostEffects post = Sky.Post.Repair();
                            post.Enabled = true;
                            apply(post, texture);
                        }, true);
                    });
                Item("LUT (полоска или покрашенный снимок)", FrameTextureKind.Lut, (post, texture) => { post.Tone.Lut = texture; post.Tone.LutContribution = Mathf.Max(post.Tone.LutContribution, 1); });
                Item("Рисунок-палитра", FrameTextureKind.Palette, (post, texture) => { post.Canvas.PaletteTexture = texture; post.Canvas.Palette = true; });
                Item("Бумага / холст", FrameTextureKind.Paper, (post, texture) => { post.Canvas.PaperTexture = texture; post.Canvas.Paper = true; });
                menu.AddSeparator("");
                Item("Частицы/Пыль в лучах", FrameTextureKind.Particle, (post, texture) => { post.Atmosphere.Dust.Texture = texture; post.Atmosphere.Dust.Enabled = true; });
                Item("Частицы/Мошкара", FrameTextureKind.Particle, (post, texture) => { post.Atmosphere.Midges.Texture = texture; post.Atmosphere.Midges.Enabled = true; });
                Item("Частицы/Дождь", FrameTextureKind.Particle, (post, texture) => { post.Atmosphere.Rain.Texture = texture; post.Atmosphere.Rain.Enabled = true; });
                Item("Частицы/Падающие листья", FrameTextureKind.Particle, (post, texture) => { post.Atmosphere.Leaves.Texture = texture; post.Atmosphere.Leaves.Enabled = true; });
                Item("Частицы/Искры от огня", FrameTextureKind.Particle, (post, texture) => { post.Atmosphere.Sparks.Texture = texture; post.Atmosphere.Sparks.Enabled = true; });
                Item("Частицы/Пар изо рта", FrameTextureKind.Particle, (post, texture) => { post.Atmosphere.Breath.Texture = texture; post.Atmosphere.Breath.Enabled = true; });
                menu.ShowAsContext();
            }
            evt.Use();
        }

        private void HandleInput(Rect area)
        {
            Event evt = Event.current;
            if (!area.Contains(evt.mousePosition) && !dragging && !draggingSplit && !draggingZone) return;
            float splitX = area.x + area.width * split;
            switch (evt.type)
            {
                case EventType.ScrollWheel:
                {
                    Vector2 before = ToPixel(area, evt.mousePosition);
                    zoom = Mathf.Clamp(zoom * (evt.delta.y > 0 ? 1 / 1.15f : 1.15f), .3f, 12);
                    Vector2 after = ToPixel(area, evt.mousePosition);
                    center += before - after;
                    evt.Use();
                    break;
                }
                case EventType.MouseDown:
                    if (pipette && evt.button == 0) { PickWhiteBalance(area, evt.mousePosition, afterTexture); evt.Use(); break; }
                    if (evt.control && EditedZone() != null) draggingZone = true;
                    else if (compare == CompareMode.Curtain && Mathf.Abs(evt.mousePosition.x - splitX) < 10) draggingSplit = true;
                    else dragging = true;
                    evt.Use();
                    break;
                case EventType.MouseDrag:
                    if (draggingZone && EditedZone() is LocationFrameZone moved) { Undo.RecordObject(database, "Сдвинуть зону"); moved.Center += evt.delta * (ViewHeight(area) / area.height); EditorUtility.SetDirty(database); saveAt = EditorApplication.timeSinceStartup + .5; }
                    else if (draggingSplit) split = Mathf.Clamp01((evt.mousePosition.x - area.x) / area.width);
                    else if (dragging) center -= evt.delta * (ViewHeight(area) / area.height);
                    evt.Use();
                    break;
                case EventType.MouseUp:
                    if (draggingZone) BuildPanel();
                    dragging = draggingSplit = draggingZone = false;
                    evt.Use();
                    break;
            }
            if (compare == CompareMode.Curtain)
                EditorGUIUtility.AddCursorRect(new Rect(splitX - 10, area.y, 20, area.height), MouseCursor.ResizeHorizontal);
        }

        private Vector2 ToPixel(Rect frame, Vector2 mouse) => center + (mouse - frame.center) * (ViewHeight(frame) / frame.height);

        // ------------------------------------------------------------------
        // Зоны и переходы
        // ------------------------------------------------------------------

        private LocationFrameZone EditedZone()
        {
            string target = FrameEffectsEditor.EditTarget;
            return target.StartsWith("zone:") ? Visual?.FrameZones?.Find(zone => zone != null && zone.Id == target.Substring(5)) : null;
        }

        // Зоны места поверх кадра (вкладка «Время и зоны» или правится зона).
        private void DrawZones(Rect frame)
        {
            List<LocationFrameZone> zones = Visual?.FrameZones;
            if (zones == null || zones.Count == 0 || (!FrameEffectsEditor.IsTimeTab && EditedZone() == null)) return;
            LocationFrameZone edited = EditedZone();
            float scale = frame.height / ViewHeight(frame);
            Vector2 Gui(Vector2 pixel) => frame.center + (pixel - center) * scale;
            Handles.BeginGUI();
            foreach (LocationFrameZone zone in zones)
            {
                if (zone == null) continue;
                bool active = zone == edited;
                Color color = !zone.Enabled ? new Color(.6f, .6f, .6f, .6f) : active ? new Color(.5f, .85f, 1f, 1f) : new Color(1f, .85f, .5f, .8f);
                Vector2 c = Gui(zone.Center);
                Vector2 half = zone.Size * scale / 2;
                Vector2 outer = half + Vector2.one * zone.Feather * scale;
                Shape(c, half, zone.Ellipse, color, 2);
                Shape(c, outer, zone.Ellipse, new Color(color.r, color.g, color.b, color.a * .35f), 1);
                GUI.Label(new Rect(c.x - 80, c.y - half.y - 18, 160, 18), zone.Name + (active ? " (правится)" : ""),
                    new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = color } });
            }
            Handles.EndGUI();
        }

        private static void Shape(Vector2 center, Vector2 half, bool ellipse, Color color, float width)
        {
            Handles.color = color;
            Vector3[] points;
            if (ellipse)
            {
                const int segments = 48;
                points = new Vector3[segments + 1];
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2;
                    points[i] = center + new Vector2(Mathf.Cos(a) * half.x, Mathf.Sin(a) * half.y);
                }
            }
            else
                points = new Vector3[]
                {
                    center + new Vector2(-half.x, -half.y), center + new Vector2(half.x, -half.y), center + new Vector2(half.x, half.y),
                    center + new Vector2(-half.x, half.y), center + new Vector2(-half.x, -half.y)
                };
            Handles.DrawAAPolyLine(width, points);
        }

        // Проверка перехода: к стилю, пауза, назад.
        private void StartTransitionTest(LocationPostEffects target, float seconds)
        {
            if (renderer == null || target == null) return;
            renderer.TransitionTo(target, seconds);
            transitionSeconds = seconds;
            transitionBackAt = EditorApplication.timeSinceStartup + seconds + 1.2;
        }

        // ------------------------------------------------------------------
        // Инструменты: эталон, гистограмма, пересвет, пипетка, снимок, время кадра
        // ------------------------------------------------------------------

        private Texture2D reference;
        private RenderTexture referenceFitted;
        private bool showHistogram, showClipping, pipette;
        private readonly int[] histogram = new int[64];
        private readonly int[,] histogramRgb = new int[3, 64];
        private Texture2D clipOverlay;
        private float clipLow, clipHigh;
        private double nextReadback;
        private float frameMs;
        private Label frameLabel;
        private const int ReadbackWidth = 192, ReadbackHeight = 108;

        private VisualElement BuildToolsBar()
        {
            Toolbar tools = new Toolbar();
            string referencePath = EditorPrefs.GetString(PrefsKey + "Reference", "");
            reference = string.IsNullOrEmpty(referencePath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(referencePath);
            ObjectField field = new ObjectField("Эталон") { objectType = typeof(Texture2D), allowSceneObjects = false, value = reference };
            field.tooltip = "Картинка-образец (кадр игры, картина): сравнение шторкой или рядом, «До» — эталонная картинка. Можно перетащить PNG из проводника.";
            field.labelElement.style.minWidth = 48;
            field.style.width = 260;
            void SetReference(Texture2D texture)
            {
                reference = texture;
                field.SetValueWithoutNotify(texture);
                EditorPrefs.SetString(PrefsKey + "Reference", texture != null ? AssetDatabase.GetAssetPath(texture) : "");
                if (texture != null && compareSource != CompareSource.Reference)
                {
                    compareSource = CompareSource.Reference;
                    EditorPrefs.SetInt(PrefsKey + "CompareSource", (int)compareSource);
                    if (compare == CompareMode.None) { compare = CompareMode.Curtain; EditorPrefs.SetInt(PrefsKey + "Compare", (int)compare); }
                    CreateGUI();
                }
            }
            field.RegisterValueChangedCallback(evt => SetReference(evt.newValue as Texture2D));
            FrameTextureDrop.MakeDropTarget(field, FrameTextureKind.Particle, texture => EditorApplication.delayCall += () => SetReference(texture),
                error => EditorUtility.DisplayDialog("Эталон", error, "Понятно"));
            tools.Add(field);
            ToolbarToggle histogramToggle = new ToolbarToggle { text = "Гистограмма", value = showHistogram, tooltip = "Распределение яркости и каналов кадра «после»" };
            histogramToggle.RegisterValueChangedCallback(evt => showHistogram = evt.newValue);
            tools.Add(histogramToggle);
            ToolbarToggle clipToggle = new ToolbarToggle { text = "Пересвет", value = showClipping, tooltip = "Красным — ушедшее в белое, синим — в чёрное" };
            clipToggle.RegisterValueChangedCallback(evt => showClipping = evt.newValue);
            tools.Add(clipToggle);
            ToolbarToggle pipetteToggle = new ToolbarToggle { text = "Пипетка баланса", value = pipette, tooltip = "Щёлкните по кадру: этот цвет станет нейтральным (цветной фильтр дня или ночи того, что правится)" };
            pipetteToggle.RegisterValueChangedCallback(evt => pipette = evt.newValue);
            tools.Add(pipetteToggle);
            tools.Add(new ToolbarButton(ShowSnapshotMenu) { text = "Снимок…", tooltip = "Сохранить кадр в высоком разрешении (PNG)" });
            frameLabel = new Label { style = { unityTextAlign = TextAnchor.MiddleLeft, marginLeft = 8, color = new Color(.75f, .78f, .72f) } };
            tools.Add(frameLabel);
            return tools;
        }

        // Эталон по размеру кадра: заполнить с обрезкой, пропорции сохранить.
        private Texture ReferenceFor(Rect frame)
        {
            if (reference == null) return null;
            float scale = EditorGUIUtility.pixelsPerPoint;
            int width = Mathf.Max(8, Mathf.RoundToInt(frame.width * scale)), height = Mathf.Max(8, Mathf.RoundToInt(frame.height * scale));
            if (referenceFitted == null || referenceFitted.width != width || referenceFitted.height != height)
            {
                Release(ref referenceFitted);
                referenceFitted = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.HideAndDontSave };
                referenceFitted.Create();
            }
            float frameAspect = width / (float)height, imageAspect = reference.width / (float)Mathf.Max(1, reference.height);
            Vector2 crop = frameAspect > imageAspect ? new Vector2(1, imageAspect / frameAspect) : new Vector2(frameAspect / imageAspect, 1);
            RenderTexture active = RenderTexture.active;
            Graphics.Blit(reference, referenceFitted, crop, (Vector2.one - crop) / 2);
            RenderTexture.active = active;
            return referenceFitted;
        }

        // Кадр «после» в маленькой копии (гамма экрана): гистограмма и пересвет.
        private void Readback(Texture source)
        {
            if (source == null || (!showHistogram && !showClipping)) return;
            double now = EditorApplication.timeSinceStartup;
            if (now < nextReadback) return;
            nextReadback = now + .25;
            RenderTexture small = RenderTexture.GetTemporary(ReadbackWidth, ReadbackHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture active = RenderTexture.active;
            Graphics.Blit(source, small);
            RenderTexture.active = small;
            Texture2D read = new Texture2D(ReadbackWidth, ReadbackHeight, TextureFormat.RGBA32, false, false);
            read.ReadPixels(new Rect(0, 0, ReadbackWidth, ReadbackHeight), 0, 0);
            read.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(small);
            Color32[] pixels = read.GetPixels32();
            DestroyImmediate(read);
            System.Array.Clear(histogram, 0, histogram.Length);
            System.Array.Clear(histogramRgb, 0, histogramRgb.Length);
            if (clipOverlay == null)
                clipOverlay = new Texture2D(ReadbackWidth, ReadbackHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            Color32[] overlay = new Color32[pixels.Length];
            int low = 0, high = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];
                int luma = (c.r * 54 + c.g * 183 + c.b * 19) >> 8;
                histogram[Mathf.Min(63, luma >> 2)]++;
                histogramRgb[0, c.r >> 2]++;
                histogramRgb[1, c.g >> 2]++;
                histogramRgb[2, c.b >> 2]++;
                bool black = c.r <= 4 && c.g <= 4 && c.b <= 4, white = c.r >= 251 || c.g >= 251 || c.b >= 251;
                if (black) low++;
                if (white) high++;
                overlay[i] = white ? new Color32(255, 40, 40, 200) : black ? new Color32(40, 90, 255, 200) : new Color32(0, 0, 0, 0);
            }
            clipOverlay.SetPixels32(overlay);
            clipOverlay.Apply();
            clipLow = low / (float)pixels.Length;
            clipHigh = high / (float)pixels.Length;
        }

        private void DrawTools(Rect area)
        {
            if (showClipping && clipOverlay != null) GUI.DrawTexture(area, clipOverlay, ScaleMode.StretchToFill, true);
            if (!showHistogram) return;
            Rect box = new Rect(area.xMax - 276, area.yMax - 128, 268, 120);
            EditorGUI.DrawRect(box, new Color(0, 0, 0, .65f));
            Rect plot = new Rect(box.x + 6, box.y + 18, box.width - 12, box.height - 24);
            int peak = 1;
            for (int i = 0; i < 64; i++) peak = Mathf.Max(peak, histogram[i], histogramRgb[0, i], histogramRgb[1, i], histogramRgb[2, i]);
            float barWidth = plot.width / 64;
            for (int i = 0; i < 64; i++)
            {
                float h = plot.height * Mathf.Sqrt(histogram[i] / (float)peak);
                EditorGUI.DrawRect(new Rect(plot.x + i * barWidth, plot.yMax - h, Mathf.Max(1, barWidth - .5f), h), new Color(.85f, .85f, .82f, .55f));
            }
            Color[] colors = { new Color(1, .3f, .3f, .9f), new Color(.3f, 1, .3f, .9f), new Color(.4f, .6f, 1, .9f) };
            Handles.BeginGUI();
            for (int channel = 0; channel < 3; channel++)
            {
                Vector3[] line = new Vector3[64];
                for (int i = 0; i < 64; i++)
                    line[i] = new Vector3(plot.x + (i + .5f) * barWidth, plot.yMax - plot.height * Mathf.Sqrt(histogramRgb[channel, i] / (float)peak), 0);
                Handles.color = colors[channel];
                Handles.DrawAAPolyLine(1.5f, line);
            }
            Handles.EndGUI();
            GUI.Label(new Rect(box.x + 6, box.y + 1, box.width - 12, 16),
                "провал " + (clipLow * 100).ToString("0.0") + "% · пересвет " + (clipHigh * 100).ToString("0.0") + "%", EditorStyles.miniBoldLabel);
        }

        // Пипетка: цвет кадра в точке → нейтральный (цветной фильтр дня или ночи).
        private void PickWhiteBalance(Rect area, Vector2 mouse, Texture source)
        {
            if (source == null) return;
            RenderTexture small = RenderTexture.GetTemporary(ReadbackWidth, ReadbackHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture active = RenderTexture.active;
            Graphics.Blit(source, small);
            RenderTexture.active = small;
            int x = Mathf.Clamp(Mathf.RoundToInt((mouse.x - area.x) / area.width * ReadbackWidth), 0, ReadbackWidth - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt((1 - (mouse.y - area.y) / area.height) * ReadbackHeight), 0, ReadbackHeight - 1);
            Texture2D read = new Texture2D(3, 3, TextureFormat.RGBA32, false, false);
            read.ReadPixels(new Rect(Mathf.Clamp(x - 1, 0, ReadbackWidth - 3), Mathf.Clamp(y - 1, 0, ReadbackHeight - 3), 3, 3), 0, 0);
            read.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(small);
            Color picked = Color.black;
            foreach (Color c in read.GetPixels()) picked += c;
            DestroyImmediate(read);
            picked /= 9;
            Vector3 linear = new Vector3(Mathf.GammaToLinearSpace(picked.r), Mathf.GammaToLinearSpace(picked.g), Mathf.GammaToLinearSpace(picked.b));
            float mean = (linear.x + linear.y + linear.z) / 3;
            if (mean < .002f || Mathf.Min(linear.x, Mathf.Min(linear.y, linear.z)) < .0005f)
            {
                EditorUtility.DisplayDialog("Пипетка баланса", "Слишком тёмная или чистая по цвету точка — выберите что-то сероватое (камень, ткань, дорогу).", "Понятно");
                return;
            }
            LocationPostEffects post = EditedPost();
            LocationColorGrade grade = renderer != null && renderer.DayFactor < .5f && !renderer.Indoor ? post.Night : post.Day;
            Change(() =>
            {
                Color filter = grade.Filter;
                filter = new Color(filter.r * mean / linear.x, filter.g * mean / linear.y, filter.b * mean / linear.z, 1);
                float max = Mathf.Max(filter.r, Mathf.Max(filter.g, filter.b));
                grade.Filter = new Color(filter.r / max, filter.g / max, filter.b / max, 1);
                post.Enabled = true;
            }, true);
            if (status != null) status.text = "Пипетка: цвет " + ColorUtility.ToHtmlStringRGB(picked) + " стал нейтральным (фильтр " + (grade == post.Night ? "ночи" : "дня") + ").";
        }

        // Что правит панель: основные, ключ суток или зона.
        private LocationPostEffects EditedPost()
        {
            string target = FrameEffectsEditor.EditTarget;
            LocationSky sky = Sky;
            if (target.StartsWith("key:") && sky.FrameKeys.Find(target.Substring(4)) is LocationFrameKey key && key.Post != null) return key.Post.Repair();
            if (EditedZone() is LocationFrameZone zone && zone.Post != null) return zone.Post.Repair();
            return sky.Post.Repair();
        }

        private void ShowSnapshotMenu()
        {
            if (renderer == null) return;
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Как видно — 1920×1080"), false, () => SaveShot(1920, 1080, false));
            menu.AddItem(new GUIContent("Как видно — 3840×2160"), false, () => SaveShot(3840, 2160, false));
            menu.AddItem(new GUIContent("Всё место — 3840 по ширине"), false, () => SaveShot(3840, 0, true));
            menu.ShowAsContext();
        }

        private void SaveShot(int width, int height, bool whole)
        {
            float keepZoom = zoom;
            Vector2 keepCenter = center;
            RenderTexture shot = null;
            try
            {
                if (whole)
                {
                    zoom = 1;
                    center = CanvasSize / 2;
                    height = Mathf.RoundToInt(width * CanvasSize.y / Mathf.Max(1, CanvasSize.x));
                }
                float scale = EditorGUIUtility.pixelsPerPoint;
                Rect frame = new Rect(0, 0, width / scale, height / scale);
                Texture rendered = Render(frame, false, ref shot);
                RenderTexture srgb = RenderTexture.GetTemporary(rendered.width, rendered.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                RenderTexture active = RenderTexture.active;
                Graphics.Blit(rendered, srgb);
                RenderTexture.active = srgb;
                Texture2D image = new Texture2D(srgb.width, srgb.height, TextureFormat.RGBA32, false, false);
                image.ReadPixels(new Rect(0, 0, srgb.width, srgb.height), 0, 0);
                image.Apply();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(srgb);
                string path = EditorUtility.SaveFilePanel("Снимок кадра (PNG)", "", Location.DisplayName + "_" + LocationLightingTest.FormatHour(hour).Replace(":", "-") + ".png", "png");
                if (!string.IsNullOrEmpty(path))
                {
                    System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
                    EditorUtility.RevealInFinder(path);
                }
                DestroyImmediate(image);
            }
            finally
            {
                Release(ref shot);
                zoom = keepZoom;
                center = keepCenter;
                LocationWorldRenderer.ClearFrameGlobals();
            }
        }

        // ------------------------------------------------------------------
        // Проверки и снимок
        // ------------------------------------------------------------------

        private void StartDissolveTest()
        {
            LocationSpriteEffects s = Sky.Post?.Sprites;
            dissolveDuration = s != null ? Mathf.Max(.1f, s.DissolveDuration) : 1;
            dissolveStart = EditorApplication.timeSinceStartup;
        }

        // Пробное растворение: за длительность — до заданной доли, полсекунды держит, затем фигуры целы.
        private void UpdateDissolveTest()
        {
            if (dissolveStart < 0 || renderer == null) return;
            float t = (float)(EditorApplication.timeSinceStartup - dissolveStart);
            LocationSpriteEffects s = Sky.Post?.Sprites;
            float amount = s != null ? Mathf.Clamp01(s.DissolveAmount) : 1;
            if (t > dissolveDuration + .8f)
            {
                dissolveStart = -1;
                renderer.PreviewDissolve(0);
                return;
            }
            renderer.PreviewDissolve(Mathf.Clamp01(t / dissolveDuration) * amount);
        }

        // Кадр без LUT, 1600 по ширине (полоска LUT займёт верхний левый угол).
        private Texture2D Snapshot()
        {
            if (renderer == null || preview == null) return null;
            LocationToneEffects tone = Sky.Post?.Tone;
            Texture2D lut = tone?.Lut;
            int width = 1600, height = Mathf.RoundToInt(width * CanvasSize.y / Mathf.Max(1, CanvasSize.x));
            height = Mathf.Clamp(height, 600, 1600);
            float keepZoom = zoom;
            Vector2 keepCenter = center;
            try
            {
                if (tone != null) tone.Lut = null;
                zoom = 1;
                center = CanvasSize / 2;
                RenderTexture shot = null;
                Rect frame = new Rect(0, 0, width, height);
                Texture rendered = Render(frame, false, ref shot);
                RenderTexture srgb = RenderTexture.GetTemporary(rendered.width, rendered.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(rendered, srgb);
                RenderTexture active = RenderTexture.active;
                RenderTexture.active = srgb;
                Texture2D result = new Texture2D(srgb.width, srgb.height, TextureFormat.RGBA32, false, false);
                result.ReadPixels(new Rect(0, 0, srgb.width, srgb.height), 0, 0);
                result.Apply();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(srgb);
                Release(ref shot);
                return result;
            }
            finally
            {
                if (tone != null) tone.Lut = lut;
                zoom = keepZoom;
                center = keepCenter;
                LocationWorldRenderer.ClearFrameGlobals();
            }
        }
    }
}
