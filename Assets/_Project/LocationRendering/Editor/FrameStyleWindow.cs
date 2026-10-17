using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
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
        private enum CompareSource { Raw, Style }

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
            if (renderer != null && renderer.BuiltSpriteStructure != LocationWorldRenderer.SpriteStructureKey(Sky.Post)) RebuildPreview();
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
            PopupField<string> source = new PopupField<string>(new List<string> { "«До» — без обработки", "«До» — выбранный стиль" }, (int)compareSource)
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
                Snapshot = Snapshot
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
            if (renderer != null && renderer.BuiltSpriteStructure != LocationWorldRenderer.SpriteStructureKey(Sky.Post))
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
            if (compare == CompareMode.SideBySide)
            {
                Rect leftFrame = new Rect(area.x, area.y, area.width / 2 - 1, area.height);
                Rect rightFrame = new Rect(area.x + area.width / 2 + 1, area.y, area.width / 2 - 1, area.height);
                Texture before = Render(leftFrame, true, ref beforeTexture);
                Texture after = Render(rightFrame, false, ref afterTexture);
                if (before != null) GUI.DrawTexture(leftFrame, before, ScaleMode.StretchToFill, false);
                if (after != null) GUI.DrawTexture(rightFrame, after, ScaleMode.StretchToFill, false);
                Caption(leftFrame, BeforeName(), true);
                Caption(rightFrame, "после", false);
            }
            else if (compare == CompareMode.Curtain)
            {
                Texture before = Render(area, true, ref beforeTexture);
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

        private string BeforeName()
        {
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
                renderer.SetTime(hour, (float)EditorApplication.timeSinceStartup);
                RenderActors();
                Camera camera = preview.camera;
                Vector2 world = LocationVisualGeometry.PixelToWorld(Location, center);
                camera.transform.position = new Vector3(world.x, world.y, -10);
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
            if (!area.Contains(evt.mousePosition) && !dragging && !draggingSplit) return;
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
                    if (compare == CompareMode.Curtain && Mathf.Abs(evt.mousePosition.x - splitX) < 10) draggingSplit = true;
                    else dragging = true;
                    evt.Use();
                    break;
                case EventType.MouseDrag:
                    if (draggingSplit) split = Mathf.Clamp01((evt.mousePosition.x - area.x) / area.width);
                    else if (dragging) center -= evt.delta * (ViewHeight(area) / area.height);
                    evt.Use();
                    break;
                case EventType.MouseUp:
                    dragging = draggingSplit = false;
                    evt.Use();
                    break;
            }
            if (compare == CompareMode.Curtain)
                EditorGUIUtility.AddCursorRect(new Rect(splitX - 10, area.y, 20, area.height), MouseCursor.ResizeHorizontal);
        }

        private Vector2 ToPixel(Rect frame, Vector2 mouse) => center + (mouse - frame.center) * (ViewHeight(frame) / frame.height);

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
