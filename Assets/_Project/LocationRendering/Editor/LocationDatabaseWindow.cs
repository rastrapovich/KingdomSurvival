using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.LocationRendering.Editor
{
    public sealed class LocationDatabaseWindow : EditorWindow
    {
        private LocalLocationDatabaseAsset database;
        private BattlefieldDatabaseAsset fields;
        private string selectedId = LocationLightingTestBootstrap.CampId, selectedObjectId;
        private ListView list;
        private ScrollView settings;
        private Label clock, status;
        private Slider hourSlider;
        private IMGUIContainer canvas;
        private PreviewRenderUtility preview;
        private LocationWorldRenderer renderer;
        private LocalPartyMover mover;
        private float hour = 13, zoom = 1;
        private Vector2 pan;
        private bool cycle, showGrid, showBlocked, setStart, setPivot, dragging;
        private Vector2 lastPointer;
        private double lastUpdate;
        private List<LocalLocationDefinition> visible = new List<LocalLocationDefinition>();
        private string query = "";
        private LocalLocationDefinition Location => database?.locations.Find(item => item.Id == selectedId);
        private LocationVisualDefinition Visual => database?.FindVisual(selectedId);
        private BattlefieldDefinitionData Field => Location != null ? fields?.FindById(Location.BattlefieldId) : null;
        private LocationVisualObject Object => Visual?.Objects.Find(item => item.Id == selectedObjectId);

        [MenuItem("Kingdom Survival/База локаций")]
        public static void OpenWindow()
        {
            LocationDatabaseWindow window = GetWindow<LocationDatabaseWindow>();
            window.titleContent = new GUIContent("База локаций");
            window.minSize = new Vector2(1080, 650);
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
        private void UndoChanged() { RefreshList(); BuildSettings(); RebuildPreview(); }
        private void PlayChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) ReleasePreview();
            if (state == PlayModeStateChange.EnteredEditMode) { LocationLightingTestBootstrap.RestorePlayScene(); RebuildPreview(); }
        }
        public void CreateGUI()
        {
            LocationLightingTestBootstrap.EnsureCamp();
            database = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            fields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
            rootVisualElement.Clear();
            rootVisualElement.style.backgroundColor = new Color(.12f, .145f, .135f);
            Toolbar toolbar = new Toolbar();
            AddButton(toolbar, "+ Локация", AddLocation);
            AddButton(toolbar, "Дублировать", DuplicateLocation);
            AddButton(toolbar, "Удалить", DeleteLocation);
            AddButton(toolbar, "Проверить", Validate);
            AddButton(toolbar, "Сохранить", () => { AssetDatabase.SaveAssetIfDirty(database); status.text = "Сохранено."; });
            AddButton(toolbar, "▶ Запустить локацию", Launch);
            rootVisualElement.Add(toolbar);
            TwoPaneSplitView outer = new TwoPaneSplitView(0, 195, TwoPaneSplitViewOrientation.Horizontal);
            outer.style.flexGrow = 1;
            VisualElement left = new VisualElement();
            ToolbarSearchField search = new ToolbarSearchField();
            search.RegisterValueChangedCallback(evt => { query = evt.newValue; RefreshList(); });
            left.Add(search);
            list = new ListView { fixedItemHeight = 48, selectionType = SelectionType.Single };
            list.style.flexGrow = 1;
            list.makeItem = () => { Label label = new Label(); label.style.whiteSpace = WhiteSpace.Normal; label.style.paddingLeft = 8; return label; };
            list.bindItem = (row, index) => ((Label)row).text = visible[index].DisplayName + "\n" +
                (database.FindVisual(visible[index].Id)?.TechnicalTest == true ? "Технический тест" : "Исследуемое место");
            list.selectionChanged += values =>
            {
                LocalLocationDefinition selected = values.OfType<LocalLocationDefinition>().FirstOrDefault();
                if (selected == null) return;
                if (database != null) AssetDatabase.SaveAssetIfDirty(database);
                selectedId = selected.Id; selectedObjectId = null;
                pan = Vector2.zero; zoom = 1; setStart = setPivot = false;
                BuildSettings(); RebuildPreview();
            };
            left.Add(list);
            outer.Add(left);
            TwoPaneSplitView inner = new TwoPaneSplitView(1, 315, TwoPaneSplitViewOrientation.Horizontal);
            VisualElement center = new VisualElement(); center.style.flexGrow = 1;
            VisualElement time = new VisualElement();
            time.style.flexDirection = FlexDirection.Row; time.style.alignItems = Align.Center;
            time.style.height = 38;
            clock = new Label(); clock.style.width = 55; clock.style.marginLeft = 8; time.Add(clock);
            hourSlider = new Slider(0, 24) { value = hour }; hourSlider.style.flexGrow = 1;
            hourSlider.tooltip = "Меняет только свет предпросмотра. Кампания и её события не затрагиваются.";
            hourSlider.RegisterValueChangedCallback(evt => { hour = Mathf.Repeat(evt.newValue, 24); cycle = false; });
            time.Add(hourSlider);
            AddButton(time, "▶ Сутки", () => cycle = !cycle);
            center.Add(time);
            VisualElement presets = new VisualElement(); presets.style.flexDirection = FlexDirection.Row;
            foreach ((string title, float h) in new[] { ("Рассвет", 6f), ("Утро", 8f), ("День", 13f), ("Вечер", 19f), ("Ночь", 1f) })
                AddButton(presets, title, () => { hour = h; cycle = false; });
            AddButton(presets, "Весь кадр", () => { pan = Vector2.zero; zoom = 1; });
            center.Add(presets);
            canvas = new IMGUIContainer(DrawPreview);
            canvas.style.flexGrow = 1;
            center.Add(canvas);
            Label hints = new Label("Перетащите PNG или Sprite сюда · ЛКМ: выбор и перемещение · ПКМ: панорама · Колесо: масштаб");
            hints.style.whiteSpace = WhiteSpace.Normal; hints.style.color = new Color(.65f, .71f, .65f);
            hints.style.paddingLeft = 8; hints.style.paddingBottom = 5; center.Add(hints);
            inner.Add(center);
            settings = new ScrollView(); settings.style.paddingLeft = settings.style.paddingRight = 10;
            inner.Add(settings); outer.Add(inner); rootVisualElement.Add(outer);
            status = new Label("Первый этап · сборка лагеря и свет. Тест использует отдельное состояние.");
            status.style.paddingLeft = 8; status.style.height = 26; rootVisualElement.Add(status);
            RefreshList();
            list.SetSelection(visible.FindIndex(item => item.Id == selectedId));
            BuildSettings(); RebuildPreview();
        }
        private static void AddButton(VisualElement parent, string title, Action action) => parent.Add(new Button(action) { text = title });
        private void RefreshList()
        {
            if (list == null || database == null) return;
            visible = database.locations.Where(item => item.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            list.itemsSource = visible; list.Rebuild();
        }
        private void Change(Action action, bool refreshSettings = false)
        {
            Undo.RecordObject(database, "Изменить локацию"); action();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
            RebuildPreview();
            if (refreshSettings) BuildSettings();
        }
        private void Heading(string text)
        {
            Label label = new Label(text); label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 14; label.style.color = new Color(.88f, .78f, .53f);
            label.style.marginTop = 14; label.style.marginBottom = 7; settings.Add(label);
        }
        private void Text(string label, string value, Action<string> set)
        {
            TextField field = new TextField(label) { value = value, isDelayed = true };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue))); settings.Add(field);
        }
        private void Toggle(string label, bool value, Action<bool> set)
        {
            UnityEngine.UIElements.Toggle field = new UnityEngine.UIElements.Toggle(label) { value = value };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue))); settings.Add(field);
        }
        private void Number(string label, float value, float min, float max, Action<float> set, string help = null)
        {
            Slider field = new Slider(label, min, max) { value = value, showInputField = true, tooltip = help };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue))); settings.Add(field);
        }
        private void ColorField(string label, Color value, Action<Color> set)
        {
            UnityEditor.UIElements.ColorField field = new UnityEditor.UIElements.ColorField(label) { value = value };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue))); settings.Add(field);
        }
        private void SpriteField(string label, Sprite value, Action<Sprite> set)
        {
            ObjectField field = new ObjectField(label) { objectType = typeof(Sprite), allowSceneObjects = false, value = value };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue as Sprite))); settings.Add(field);
        }
        private void BuildSettings()
        {
            if (settings == null) return;
            settings.Clear();
            if (Location == null) return;
            Heading(Location.DisplayName);
            Text("Название", Location.DisplayName, value => { Location.DisplayName = value; RefreshList(); });
            if (Visual == null)
            {
                settings.Add(new HelpBox("Для этого места ещё нет художественной сборки.", HelpBoxMessageType.Info));
                AddButton(settings, "Добавить сборку и свет", () => Change(() => database.visuals.Add(new LocationVisualDefinition { LocationId = selectedId }), true));
                return;
            }
            AddButton(settings, "Открыть поле в базе", () => EditorApplication.ExecuteMenuItem("Kingdom Survival/База полей боя"));
            List<BattlefieldDefinitionData> fieldOptions = fields.Battlefields.Where(item => item != null).ToList();
            PopupField<string> fieldChoice = new PopupField<string>("Поле / геометрия", fieldOptions.Select(item => item.DisplayLabel).ToList(),
                Mathf.Max(0, fieldOptions.FindIndex(item => item.Id == Location.BattlefieldId)));
            fieldChoice.RegisterValueChangedCallback(evt => Change(() => Location.BattlefieldId = fieldOptions[fieldChoice.index].Id, true));
            settings.Add(fieldChoice);
            SpriteField("Фон земли", Field?.Background, SetGround);
            Heading("Общий свет");
            Number("Общая яркость", Visual.Daylight.Intensity, 0, 2, value => Visual.Daylight.Intensity = value);
            CurveField curve = new CurveField("Яркость за сутки") { value = Visual.Daylight.Brightness };
            curve.RegisterValueChangedCallback(evt => Change(() => Visual.Daylight.Brightness = evt.newValue)); settings.Add(curve);
            GradientField gradient = new GradientField("Цвет за сутки") { value = Visual.Daylight.Color };
            gradient.RegisterValueChangedCallback(evt => Change(() => Visual.Daylight.Color = evt.newValue)); settings.Add(gradient);
            Heading("Служебные слои");
            UnityEngine.UIElements.Toggle grid = new UnityEngine.UIElements.Toggle("Сетка") { value = showGrid };
            grid.RegisterValueChangedCallback(evt => showGrid = evt.newValue); settings.Add(grid);
            UnityEngine.UIElements.Toggle blocked = new UnityEngine.UIElements.Toggle("Проходимость и основания") { value = showBlocked };
            blocked.RegisterValueChangedCallback(evt => showBlocked = evt.newValue); settings.Add(blocked);
            AddButton(settings, "Поставить точку старта мышью", () => { setStart = true; setPivot = false; status.text = "Кликните по проходимой клетке."; });
            UnitDatabaseAsset units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
            if (units != null)
            {
                List<UnitDefinitionData> options = units.Units.Where(item => item != null).ToList();
                PopupField<string> unit = new PopupField<string>("Персонаж теста", options.Select(item => item.DisplayLabel).ToList(),
                    Mathf.Max(0, options.FindIndex(item => item.Id == Visual.TestUnitId)));
                unit.RegisterValueChangedCallback(evt => Change(() => Visual.TestUnitId = options[unit.index].Id)); settings.Add(unit);
            }
            SliderInt followers = new SliderInt("Спутников в тесте", 0, 4) { value = Visual.TestFollowers, showInputField = true };
            followers.RegisterValueChangedCallback(evt => Change(() => Visual.TestFollowers = evt.newValue)); settings.Add(followers);
            Heading("Объекты");
            VisualElement actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; settings.Add(actions);
            AddButton(actions, "PNG…", ImportPng);
            AddButton(actions, "+ Костёр", () => AddObject(null, LocationPlaceholder.Fire));
            AddButton(actions, "+ Палатка", () => AddObject(null, LocationPlaceholder.Tent));
            foreach (LocationVisualObject item in Visual.Objects)
                AddButton(settings, (item.Id == selectedObjectId ? "● " : "") + item.Name + (item.Hidden ? " · скрыт" : ""), () =>
                { selectedObjectId = item.Id; BuildSettings(); });
            LocationVisualObject selected = Object;
            if (selected == null) return;
            Heading("Выбранный объект");
            Text("Название", selected.Name, value => selected.Name = value);
            SpriteField("Рисунок", selected.Sprite, value => selected.Sprite = value);
            Text("Группа частей", selected.GroupId, value => selected.GroupId = value);
            Number("Высота рисунка", selected.Height, .1f, 6, value => selected.Height = value, "Герой показан рядом в том же масштабе.");
            Toggle("Отразить по X", selected.FlipX, value => selected.FlipX = value);
            Toggle("Заблокировать", selected.Locked, value => selected.Locked = value);
            Toggle("Скрыть", selected.Hidden, value => selected.Hidden = value);
            PopupField<string> band = new PopupField<string>("Слой", new List<string> { "Земля", "Детали земли", "Объекты и персонажи", "Кроны / крыши" }, (int)selected.Band);
            band.RegisterValueChangedCallback(evt => Change(() => selected.Band = (LocationVisualBand)band.index)); settings.Add(band);
            Number("Порядок внутри слоя", selected.OrderOffset, -1000, 1000, value => selected.OrderOffset = Mathf.RoundToInt(value));
            AddButton(settings, "Поставить точку опоры мышью", () => { setPivot = true; setStart = false; status.text = "Кликните по точке опоры на рисунке."; });
            Toggle("Блокирует проход", selected.BlocksMovement, value => selected.BlocksMovement = value);
            Vector2Field footprint = new Vector2Field("Основание на земле") { value = selected.Footprint };
            footprint.RegisterValueChangedCallback(evt => Change(() => selected.Footprint = Vector2.Max(Vector2.zero, evt.newValue))); settings.Add(footprint);
            Toggle("Тень от локального света", selected.CastsShadow, value => selected.CastsShadow = value);
            settings.Add(new HelpBox("Тень строится от основания. Солнечные тени и произвольный контур — следующий этап.", HelpBoxMessageType.Info));
            Heading("Свет этого объекта");
            Toggle("Источник включён", selected.Light.Enabled, value => selected.Light.Enabled = value);
            ColorField("Цвет", selected.Light.Color, value => selected.Light.Color = value);
            Number("Яркость", selected.Light.Intensity, 0, 5, value => selected.Light.Intensity = value);
            Number("Радиус", selected.Light.Radius, .1f, 10, value => selected.Light.Radius = value);
            Number("Мягкость границы", selected.Light.Softness, 0, 1, value => selected.Light.Softness = value);
            Toggle("Отбрасываемые тени", selected.Light.Shadows, value => selected.Light.Shadows = value);
            Number("Темнота тени", selected.Light.ShadowStrength, 0, 1, value => selected.Light.ShadowStrength = value);
            Number("Мягкость тени", selected.Light.ShadowSoftness, 0, 1, value => selected.Light.ShadowSoftness = value);
            Number("Мерцание", selected.Light.Flicker, 0, .3f, value => selected.Light.Flicker = value);
            Toggle("По расписанию", selected.Light.NightOnly, value => selected.Light.NightOnly = value);
            Number("Включить в", selected.Light.StartsAt, 0, 24, value => selected.Light.StartsAt = value);
            Number("Выключить в", selected.Light.EndsAt, 0, 24, value => selected.Light.EndsAt = value);
            Vector2Field offset = new Vector2Field("Смещение источника") { value = selected.Light.Offset };
            offset.RegisterValueChangedCallback(evt => Change(() => selected.Light.Offset = evt.newValue)); settings.Add(offset);
            Heading("Состояния рисунка");
            foreach (LocationVisualVariant variant in selected.Variants)
            {
                Text("Название состояния", variant.Name, value => variant.Name = value);
                SpriteField(variant.Name, variant.Sprite, value => variant.Sprite = value);
                AddButton(settings, "Показать: " + variant.Name, () => Change(() => selected.DefaultVariantId = variant.Id));
            }
            AddButton(settings, "+ Состояние", () => Change(() => selected.Variants.Add(new LocationVisualVariant { Id = Guid.NewGuid().ToString("N"), Name = "Новое состояние" }), true));
            AddButton(settings, "Основной рисунок", () => Change(() => selected.DefaultVariantId = ""));
            AddButton(settings, "Дублировать объект", () => Change(() =>
            {
                LocationVisualObject copy = JsonUtility.FromJson<LocationVisualObject>(JsonUtility.ToJson(selected));
                copy.Id = Guid.NewGuid().ToString("N"); copy.GroupId = ""; copy.Position += new Vector2(.03f, .03f);
                Visual.Objects.Add(copy); selectedObjectId = copy.Id;
            }, true));
            AddButton(settings, "Удалить объект", () => Change(() => { Visual.Objects.Remove(selected); selectedObjectId = null; }, true));
        }

        private void RebuildPreview()
        {
            ReleasePreview();
            if (Visual == null || Field == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            preview = new PreviewRenderUtility(true);
            renderer = new LocationWorldRenderer(Location, Visual, Field);
            renderer.Camera.enabled = false;
            preview.AddSingleGO(renderer.Root);
            preview.camera.orthographic = true;
            preview.camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            preview.camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.035f, .045f, .04f);
            List<HexCoord> cells = new List<HexCoord>(renderer.Geometry.Region(new HexCoord(Visual.TestStart.x, Visual.TestStart.y)));
            HexCoord start = new HexCoord(Visual.TestStart.x, Visual.TestStart.y);
            cells.Sort((a, b) => a.DistanceTo(start).CompareTo(b.DistanceTo(start)));
            List<KeyValuePair<string, HexCoord>> members = new List<KeyValuePair<string, HexCoord>>();
            int count = Mathf.Min(cells.Count, Visual.TestFollowers + 1);
            for (int i = 0; i < count; i++) members.Add(new KeyValuePair<string, HexCoord>("preview_" + i, cells[i]));
            if (count > 0) { mover = new LocalPartyMover(renderer.Geometry.IsPassable, renderer.Geometry.StepCost, members); renderer.AddTestActors(count); }
        }
        private void ReleasePreview()
        {
            renderer?.Dispose(); renderer = null;
            preview?.Cleanup(); preview = null; mover = null;
        }
        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = Mathf.Clamp((float)(now - lastUpdate), 0, .1f); lastUpdate = now;
            if (cycle) hour = Mathf.Repeat(hour + delta * 24 / 30, 24);
            if (clock != null) clock.text = LocationLightingTest.FormatHour(hour);
            hourSlider?.SetValueWithoutNotify(hour);
            canvas?.MarkDirtyRepaint();
        }
        private Rect FrameRect(Rect area)
        {
            float height = Mathf.Min(area.height, area.width / BattlefieldFrame.Aspect);
            return new Rect(area.center.x - height * BattlefieldFrame.Aspect / 2, area.center.y - height / 2,
                height * BattlefieldFrame.Aspect, height);
        }
        private Vector2 MouseWorld(Rect frame, Vector2 mouse) => pan + new Vector2(
            (mouse.x - frame.center.x) / frame.height, -(mouse.y - frame.center.y) / frame.height) * LocationVisualDefinition.WorldHeight / zoom;
        private Vector2 WorldMouse(Rect frame, Vector2 world) => frame.center + new Vector2(world.x - pan.x, -(world.y - pan.y)) *
            frame.height * zoom / LocationVisualDefinition.WorldHeight;

        private void DrawPreview()
        {
            Rect area = new Rect(0, 0, canvas.contentRect.width, canvas.contentRect.height);
            if (area.width < 10 || area.height < 10) return;
            EditorGUI.DrawRect(area, new Color(.045f, .055f, .05f));
            Rect frame = FrameRect(area);
            if (renderer == null || preview == null)
            { GUI.Label(area, EditorApplication.isPlaying ? "Локация запущена во вкладке Game." : "Выберите локацию с художественной сборкой."); return; }
            Event evt = Event.current;
            Vector2 world = MouseWorld(frame, evt.mousePosition);
            if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && frame.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    Sprite[] sprites = DragAndDrop.objectReferences.OfType<Sprite>().ToArray();
                    foreach (Sprite sprite in sprites) AddObject(sprite, LocationPlaceholder.None, world);
                    foreach (string path in DragAndDrop.paths)
                    {
                        if (sprites.Any(sprite => AssetDatabase.GetAssetPath(sprite) == path)) continue;
                        Sprite sprite = ImportSprite(path);
                        if (sprite != null) AddObject(sprite, LocationPlaceholder.None, world);
                    }
                }
                evt.Use(); return;
            }
            if (evt.type == EventType.ScrollWheel && frame.Contains(evt.mousePosition))
            { zoom = Mathf.Clamp(zoom * Mathf.Pow(1.1f, -evt.delta.y), .6f, 4); evt.Use(); }
            if (evt.type == EventType.MouseDown && frame.Contains(evt.mousePosition))
            {
                if (evt.button == 1) { lastPointer = evt.mousePosition; evt.Use(); }
                else if (evt.button == 0)
                {
                    if (setStart)
                    {
                        if (LocationVisualGeometry.TryCell(Field, world, out HexCoord cell) && renderer.Geometry.IsPassable(cell))
                        { Change(() => Visual.TestStart = new Vector2Int(cell.Q, cell.R)); setStart = false; status.text = "Точка старта поставлена."; }
                        else status.text = "Точка должна быть на проходимой клетке.";
                    }
                    else if (setPivot && Object != null)
                    {
                        SpriteRenderer image = renderer.FindObject(Object.Id);
                        if (image != null)
                        {
                            Bounds bounds = image.bounds;
                            Vector2 pivot = new Vector2(Mathf.Clamp01((world.x - bounds.min.x) / bounds.size.x),
                                Mathf.Clamp01((world.y - bounds.min.y) / bounds.size.y));
                            LocationVisualObject selected = Object;
                            Change(() => { selected.Pivot = pivot; selected.Position = LocationVisualGeometry.ToNormalized(world); }, true);
                            setPivot = false; status.text = "Точка опоры поставлена.";
                        }
                    }
                    else
                    {
                        selectedObjectId = Visual.Objects.Where(item => !item.Hidden && renderer.FindObject(item.Id) != null &&
                            renderer.FindObject(item.Id).bounds.Contains(new Vector3(world.x, world.y, 0)))
                            .OrderByDescending(item => renderer.FindObject(item.Id).sortingOrder).FirstOrDefault()?.Id;
                        BuildSettings();
                        dragging = Object != null && !Object.Locked;
                        if (dragging) { Undo.RecordObject(database, "Переместить объект или группу"); lastPointer = world; }
                    }
                    evt.Use();
                }
            }
            if (evt.type == EventType.MouseDrag)
            {
                if (evt.button == 1)
                {
                    Vector2 delta = evt.mousePosition - lastPointer;
                    pan -= new Vector2(delta.x, -delta.y) * LocationVisualDefinition.WorldHeight / frame.height / zoom;
                    lastPointer = evt.mousePosition; evt.Use();
                }
                else if (dragging && Object != null)
                {
                    Vector2 delta = world - lastPointer;
                    LocationVisualObject selected = Object;
                    foreach (LocationVisualObject item in Visual.Objects)
                        if (!item.Locked && (item == selected || (!string.IsNullOrEmpty(selected.GroupId) && item.GroupId == selected.GroupId)))
                            item.Position += new Vector2(delta.x / LocationVisualDefinition.WorldWidth, -delta.y / LocationVisualDefinition.WorldHeight);
                    lastPointer = world; EditorUtility.SetDirty(database); RebuildPreview(); evt.Use();
                }
            }
            if (evt.type == EventType.MouseUp && dragging)
            { dragging = false; AssetDatabase.SaveAssetIfDirty(database); evt.Use(); }
            if (evt.type != EventType.Repaint || preview == null) return;
            renderer.SetTime(hour, (float)EditorApplication.timeSinceStartup);
            if (mover != null) renderer.RenderActors(mover.Members, (float)EditorApplication.timeSinceStartup);
            preview.camera.transform.position = new Vector3(pan.x, pan.y, -10);
            preview.camera.transform.rotation = Quaternion.identity;
            preview.camera.orthographicSize = LocationVisualDefinition.WorldHeight / zoom / 2;
            preview.BeginPreview(frame, GUIStyle.none);
            preview.Render(true);
            Texture texture = preview.EndPreview();
            GUI.DrawTexture(frame, texture, ScaleMode.StretchToFill);
            if (showGrid || showBlocked)
            {
                Handles.BeginGUI();
                foreach (HexCoord cell in SandboxArenaShape.Cells())
                {
                    Vector2 p = WorldMouse(frame, LocationVisualGeometry.CellPosition(Field, cell));
                    bool blocked = !renderer.Geometry.IsPassable(cell);
                    if (showBlocked && blocked) EditorGUI.DrawRect(new Rect(p - new Vector2(9, 6), new Vector2(18, 12)), new Color(1, .2f, .12f, .7f));
                    else if (showGrid) { Handles.color = new Color(.8f, .8f, .65f, .4f); Handles.DrawWireDisc(p, Vector3.forward, 5); }
                }
                foreach (LocationVisualObject item in Visual.Objects)
                {
                    if (!showBlocked || item.Hidden || !item.BlocksMovement) continue;
                    Vector2 a = WorldMouse(frame, LocationVisualGeometry.ToWorld(item.Position) - item.Footprint / 2);
                    Vector2 b = WorldMouse(frame, LocationVisualGeometry.ToWorld(item.Position) + item.Footprint / 2);
                    Handles.DrawSolidRectangleWithOutline(new Rect(Vector2.Min(a, b), new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y))),
                        new Color(1, .35f, .15f, .1f), new Color(1, .4f, .2f, .8f));
                }
                Handles.EndGUI();
            }
            if (Object != null)
            {
                Vector2 p = WorldMouse(frame, LocationVisualGeometry.ToWorld(Object.Position));
                EditorGUI.DrawRect(new Rect(p.x - 5, p.y - 1, 10, 2), Color.yellow);
                EditorGUI.DrawRect(new Rect(p.x - 1, p.y - 5, 2, 10), Color.yellow);
            }
        }
        private void AddObject(Sprite sprite, LocationPlaceholder placeholder, Vector2? world = null)
        {
            if (Visual == null) return;
            Change(() =>
            {
                LocationVisualObject item = new LocationVisualObject { Sprite = sprite, Placeholder = placeholder,
                    Name = sprite != null ? sprite.name : placeholder == LocationPlaceholder.Fire ? "Костёр" : "Палатка",
                    Position = LocationVisualGeometry.ToNormalized(world ?? Vector2.zero),
                    Height = placeholder == LocationPlaceholder.Fire ? .8f : 1.8f,
                    BlocksMovement = placeholder == LocationPlaceholder.Tent,
                    CastsShadow = placeholder == LocationPlaceholder.Tent };
                if (placeholder == LocationPlaceholder.Fire) item.Light.Enabled = true;
                Visual.Objects.Add(item); selectedObjectId = item.Id;
            }, true);
        }
        private void ImportPng()
        {
            string path = EditorUtility.OpenFilePanel("Загрузить PNG локации", "", "png");
            if (string.IsNullOrEmpty(path)) return;
            Sprite sprite = ImportSprite(path);
            if (sprite != null) AddObject(sprite, LocationPlaceholder.None);
        }
        internal static Sprite ImportSprite(string path)
        {
            if (!File.Exists(path)) return null;
            if (!path.Replace("\\", "/").StartsWith("Assets/", StringComparison.Ordinal))
            {
                string folder = "Assets/_Project/Art/Locations";
                Directory.CreateDirectory(folder);
                string destination = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileName(path));
                File.Copy(path, destination); path = destination;
                AssetDatabase.ImportAsset(path);
            }
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            // Не разрушать существующие атласы; для внешнего PNG режим Single.
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        private void Validate()
        {
            List<string> errors = LocationVisualGeometry.Validate(Location, Visual, Field);
            status.text = errors.Count == 0 ? "Проверка пройдена." : string.Join(" · ", errors);
            if (errors.Count > 0) EditorUtility.DisplayDialog("Проверка локации", string.Join("\n", errors), "Понятно");
        }
        private void Launch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            List<string> errors = LocationVisualGeometry.Validate(Location, Visual, Field);
            if (errors.Count > 0) { Validate(); return; }
            AssetDatabase.SaveAssetIfDirty(database);
            ReleasePreview();
            LocationLightingTestBootstrap.Launch(selectedId, hour);
        }
        private void AddLocation()
        {
            string sourceFieldId = Field?.Id ?? LocationLightingTestBootstrap.FieldId;
            Change(() =>
            {
                selectedId = "location_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                database.locations.Add(new LocalLocationDefinition { Id = selectedId, DisplayName = "Новая локация",
                    WorldLocationId = "__technical", BattlefieldId = sourceFieldId,
                    Entrances = new List<LocalEntranceDefinition> { new LocalEntranceDefinition { Id = "entry", Label = "Вход", Cell = new LocalCellData(4, 4) } } });
                database.visuals.Add(new LocationVisualDefinition { LocationId = selectedId, TechnicalTest = true });
                RefreshList();
            }, true);
        }
        private void DuplicateLocation()
        {
            if (Location == null || Visual == null) return;
            Change(() =>
            {
                LocalLocationDefinition location = JsonUtility.FromJson<LocalLocationDefinition>(JsonUtility.ToJson(Location));
                LocationVisualDefinition visual = JsonUtility.FromJson<LocationVisualDefinition>(JsonUtility.ToJson(Visual));
                selectedId = "location_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                location.Id = selectedId; location.DisplayName += " · копия"; location.WorldLocationId = "__technical";
                visual.LocationId = selectedId; visual.TechnicalTest = true;
                database.locations.Add(location); database.visuals.Add(visual); RefreshList();
            }, true);
        }
        private void DeleteLocation()
        {
            if (Location == null || selectedId == LocationLightingTestBootstrap.CampId) return;
            if (Visual?.TechnicalTest != true)
            {
                EditorUtility.DisplayDialog("Игровое место", "Удаление игрового места требует проверки ссылок карты и истории. Здесь можно удалять только технические локации.", "Понятно");
                return;
            }
            if (!EditorUtility.DisplayDialog("Удалить локацию?", Location.DisplayName + "\nСвязанные рисунки, поле и диалоги сохранятся.", "Удалить", "Отмена")) return;
            LocalLocationDefinition location = Location; LocationVisualDefinition visual = Visual;
            Change(() =>
            {
                database.locations.Remove(location); database.visuals.Remove(visual);
                selectedId = LocationLightingTestBootstrap.CampId; selectedObjectId = null; RefreshList();
            }, true);
        }
        private void SetGround(Sprite sprite)
        {
            if (Field == null) return;
            Undo.RecordObject(fields, "Изменить фон поля");
            SerializedObject serialized = new SerializedObject(fields);
            SerializedProperty array = serialized.FindProperty("battlefields");
            int index = fields.Battlefields.ToList().FindIndex(item => item.Id == Location.BattlefieldId);
            SerializedProperty field = array.GetArrayElementAtIndex(index);
            field.FindPropertyRelative("background").objectReferenceValue = sprite;
            field.FindPropertyRelative("awaitingArt").boolValue = sprite == null;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(fields);
            status.text = "Обновлён фон связанного поля. Его используют все места с этим полем.";
        }
    }
}
