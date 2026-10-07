using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12К (канон v1.54 §28.3): окно «База локаций». Место — рисунок своего
    // размера; разметка местности (кисть, как у глобальной карты) задаёт, где
    // ходят; входы, объекты, противники, зоны угрозы, точки отхода и кадры
    // боя ставятся мышью. Сетка боя — ровно настройки поля Базы полей боя:
    // кадр поля показывается на рисунке там, где начнётся бой.
    public sealed partial class LocationDatabaseWindow : EditorWindow
    {
        private enum Tool { Select, Terrain, Entrance, ObjectPoint, Enemy, TriggerArea, ArenaFrame, RetreatPoint, TestStart, Pivot, LightShape, PlaceAsset }
        private enum Kind { None, Art, Entrance, GameObject, Enemy, Encounter }
        private enum Tab { Place, Game, Light, Art, Ground }
        private static readonly string[] TabNames = { "Место", "Игровое", "Свет", "Предметы", "Земля" };

        private static readonly string[] ToolNames =
        {
            "Выбор", "Местность", "Вход", "Объект места", "Противник", "Зона угрозы", "Кадр боя", "Точка отхода", "Старт теста", "Опора рисунка", "Форма света", "Ставить ассет"
        };

        private LocalLocationDatabaseAsset database;
        private BattlefieldDatabaseAsset fields;
        private string selectedId = LocationLightingTestBootstrap.CampId;
        private Kind selectedKind;
        private string selectedElementId;
        private ListView list;
        private ScrollView settings;
        private Label clock, status;
        private Slider hourSlider;
        private IMGUIContainer canvas;
        private PreviewRenderUtility preview;
        private LocationWorldRenderer renderer;
        private LocalLocationGeometry geometry;
        private Texture2D terrainOverlay;
        private float hour = 13, zoom = 1;
        private Vector2 viewCenter;
        private bool cycle, showTerrain = true, showArena = true, showMarkers = true, dragging;
        private Tool tool;
        private Tab tab = Tab.Place;
        private bool showLights = true, compareDay;
        private int dragVertex = -1;
        private WorldMapGameplayTerrainType brushTerrain = WorldMapGameplayTerrainType.Cliffs;
        private float brushRadius = 40;
        private WorldMapTerrainLayer paintLayer;
        private Vector2 lastPointer, dragStart;
        private double lastUpdate;
        private List<LocalLocationDefinition> visible = new List<LocalLocationDefinition>();
        private string query = "";

        private LocalLocationDefinition Location => database?.locations.Find(item => item.Id == selectedId);
        private LocationVisualDefinition Visual => database?.FindVisual(selectedId);
        private BattlefieldDefinitionData Field => Location != null ? fields?.FindById(Location.BattlefieldId) : null;
        private LocationVisualObject ArtObject => selectedKind == Kind.Art ? Visual?.Objects.Find(item => item.Id == selectedElementId) : null;
        private LocalEntranceDefinition SelectedEntrance => selectedKind == Kind.Entrance ? Location?.Entrances.Find(item => item.Id == selectedElementId) : null;
        private LocalObjectDefinition SelectedGameObject => selectedKind == Kind.GameObject ? Location?.FindObject(selectedElementId) : null;
        private LocalEnemyDefinition SelectedEnemy => selectedKind == Kind.Enemy ? Location?.FindEnemy(selectedElementId) : null;
        private LocalEncounterDefinition SelectedEncounter =>
            selectedKind == Kind.Encounter ? Location?.FindEncounter(selectedElementId)
            : selectedKind == Kind.Enemy ? Location?.FindEncounter(SelectedEnemy?.EncounterId)
            : Location?.Encounters.FirstOrDefault();
        private Vector2 CanvasSize => LocationVisualGeometry.CanvasSize(Location);
        private float ViewHeight => CanvasSize.y / zoom;

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
            ArtAssetDatabaseAsset.Changed += CatalogChanged;
            ArtAssetUsages.LocationRequested += OpenRequested;
        }

        // Каталог изменился: экземпляры пересобираются с новыми рисунками,
        // их положение, ракурс и переопределения остаются.
        private void CatalogChanged() => rebuildRequested = true;

        private void OpenRequested(string locationId, string objectId)
        {
            ArtAssetUsages.PendingLocationId = ArtAssetUsages.PendingObjectId = null;
            if (database == null || database.locations.All(item => item.Id != locationId)) return;
            if (database != null) AssetDatabase.SaveAssetIfDirty(database);
            selectedId = locationId;
            tab = Tab.Art;
            selectedKind = objectId != null ? Kind.Art : Kind.None;
            selectedElementId = objectId;
            zoom = 1; viewCenter = CanvasSize / 2;
            list?.SetSelectionWithoutNotify(new[] { visible.FindIndex(item => item.Id == selectedId) });
            BuildSettings(); RebuildPreview();
            Focus();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= UndoChanged;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayChanged;
            ArtAssetDatabaseAsset.Changed -= CatalogChanged;
            ArtAssetUsages.LocationRequested -= OpenRequested;
            ReleasePreview();
            ClearGroundPreview();
            if (database != null) AssetDatabase.SaveAssetIfDirty(database);
            if (fields != null) AssetDatabase.SaveAssetIfDirty(fields);
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
                ClearGroundPreview(); pendingPackage = null; pendingPlan = null; groundCheck.Clear();
                selectedId = selected.Id; selectedKind = Kind.None; selectedElementId = null;
                zoom = 1; viewCenter = CanvasSize / 2; tool = Tool.Select;
                BuildSettings(); RebuildPreview();
            };
            left.Add(list);
            outer.Add(left);
            TwoPaneSplitView inner = new TwoPaneSplitView(1, 340, TwoPaneSplitViewOrientation.Horizontal);
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
            VisualElement presets = new VisualElement(); presets.style.flexDirection = FlexDirection.Row; presets.style.flexWrap = Wrap.Wrap;
            foreach ((string title, float h) in new[] { ("Рассвет", 6f), ("Утро", 8f), ("День", 13f), ("Вечер", 19f), ("Ночь", 1f) })
                AddButton(presets, title, () => { hour = h; cycle = false; });
            AddButton(presets, "Весь рисунок", () => { viewCenter = CanvasSize / 2; zoom = 1; });
            center.Add(presets);
            VisualElement tools = new VisualElement(); tools.style.flexDirection = FlexDirection.Row; tools.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i <= (int)Tool.TestStart; i++)
            {
                Tool value = (Tool)i;
                AddButton(tools, ToolNames[i], () => SetTool(value));
            }
            center.Add(tools);
            canvas = new IMGUIContainer(DrawPreview) { focusable = true };
            RegisterCanvasDrop();
            canvas.style.flexGrow = 1;
            center.Add(canvas);
            Label hints = new Label("Перетащите PNG или Sprite сюда · ЛКМ: инструмент · ПКМ: панорама · Колесо: масштаб");
            hints.style.whiteSpace = WhiteSpace.Normal; hints.style.color = new Color(.65f, .71f, .65f);
            hints.style.paddingLeft = 8; hints.style.paddingBottom = 5; center.Add(hints);
            inner.Add(center);
            settings = new ScrollView(); settings.style.paddingLeft = settings.style.paddingRight = 10;
            inner.Add(settings); outer.Add(inner); rootVisualElement.Add(outer);
            status = new Label("Движение по месту — как на глобальной карте; клетки — только в бою.");
            status.style.paddingLeft = 8; status.style.height = 26; rootVisualElement.Add(status);
            RefreshList();
            viewCenter = CanvasSize / 2;
            list.SetSelection(visible.FindIndex(item => item.Id == selectedId));
            BuildSettings(); RebuildPreview();
            // Переход «Где используется» из Базы ассетов, пока окно открывалось.
            if (!string.IsNullOrEmpty(ArtAssetUsages.PendingLocationId))
                OpenRequested(ArtAssetUsages.PendingLocationId, ArtAssetUsages.PendingObjectId);
        }

        private void SetTool(Tool value)
        {
            tool = value;
            switch (value)
            {
                case Tool.Terrain: status.text = "Местность: ЛКМ — красить выбранным типом (тип и размер кисти — справа)."; break;
                case Tool.Entrance: status.text = "Вход: клик — поставить выбранный вход (или новый)."; break;
                case Tool.ObjectPoint: status.text = "Объект места: клик — поставить выбранный объект (или новый)."; break;
                case Tool.Enemy: status.text = "Противник: клик — поставить выбранного противника (или нового)."; break;
                case Tool.TriggerArea: status.text = "Зона угрозы: протяните прямоугольник для выбранного столкновения."; break;
                case Tool.ArenaFrame: status.text = "Кадр боя: клик — центр кадра поля для выбранного столкновения."; break;
                case Tool.RetreatPoint: status.text = "Точка отхода: клик — безопасная точка выбранного столкновения."; break;
                case Tool.TestStart: status.text = "Старт теста: клик по проходимой точке."; break;
                case Tool.Pivot: status.text = "Кликните по точке опоры на рисунке выбранного объекта."; break;
                case Tool.LightShape: status.text = "Форма света: тяните точку, клик у края — новая точка, Shift+клик — удалить."; break;
                case Tool.PlaceAsset: status.text = "Ставить ассет «" + (ArtAssetDatabaseAsset.FindCurrent(placingAssetId)?.Name ?? "?") + "»: клик — новый экземпляр; Esc или «Выбор» — закончить."; break;
                default: status.text = "Выбор: клик по метке или предмету, перетаскивание — переместить."; break;
            }
            if (value == Tool.Terrain) tab = Tab.Place;
            else if (value == Tool.LightShape) tab = Tab.Light;
            else if (value == Tool.TestStart || value == Tool.Pivot || value == Tool.PlaceAsset) tab = tab == Tab.Light ? Tab.Light : Tab.Art;
            else if (value != Tool.Select) tab = Tab.Game;
            BuildSettings();
        }

        private static void AddButton(VisualElement parent, string title, Action action) => parent.Add(new Button(action) { text = title });

        private void RefreshList()
        {
            if (list == null || database == null) return;
            visible = database.locations.Where(item => item.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            list.itemsSource = visible; list.Rebuild();
        }

        // Пересборка предпросмотра и запись ассета — не на каждый шаг ползунка:
        // один раз за кадр и через полсекунды после последней правки.
        private bool rebuildRequested;
        private double saveAt = -1;
        private double fieldsSaveAt = -1;

        private void Change(Action action, bool refreshSettings = false)
        {
            Undo.RecordObject(database, "Изменить локацию"); action();
            EditorUtility.SetDirty(database);
            saveAt = EditorApplication.timeSinceStartup + .5;
            rebuildRequested = true;
            if (refreshSettings) BuildSettings();
        }

        private void FlushPending()
        {
            if (rebuildRequested && !dragging) { rebuildRequested = false; RebuildPreview(); }
            if (saveAt > 0 && EditorApplication.timeSinceStartup >= saveAt && !dragging)
            {
                saveAt = -1;
                if (database != null) AssetDatabase.SaveAssetIfDirty(database);
            }
            if (fieldsSaveAt > 0 && EditorApplication.timeSinceStartup >= fieldsSaveAt)
            {
                fieldsSaveAt = -1;
                if (fields != null) AssetDatabase.SaveAssetIfDirty(fields);
            }
        }

        // ------------------------------------------------------------------
        // Панель настроек
        // ------------------------------------------------------------------

        private void Heading(string text)
        {
            Label label = new Label(text); label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 14; label.style.color = new Color(.88f, .78f, .53f);
            label.style.marginTop = 14; label.style.marginBottom = 7; settings.Add(label);
        }

        private void Help(string text) => settings.Add(new HelpBox(text, HelpBoxMessageType.Info));

        private void Text(string label, string value, Action<string> set)
        {
            TextField field = new TextField(label) { value = value ?? string.Empty, isDelayed = true };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue))); settings.Add(field);
        }

        private void LongText(string label, string value, Action<string> set)
        {
            TextField field = new TextField(label) { value = value ?? string.Empty, isDelayed = true, multiline = true };
            field.style.whiteSpace = WhiteSpace.Normal;
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue))); settings.Add(field);
        }

        private void Toggle(string label, bool value, Action<bool> set, bool refresh = false)
        {
            UnityEngine.UIElements.Toggle field = new UnityEngine.UIElements.Toggle(label) { value = value };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), refresh)); settings.Add(field);
        }

        private void Number(string label, float value, float min, float max, Action<float> set, string help = null)
        {
            Slider field = new Slider(label, min, max) { value = value, showInputField = true, tooltip = help };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue))); settings.Add(field);
        }

        private void Integer(string label, int value, Action<int> set, string help = null)
        {
            IntegerField field = new IntegerField(label) { value = value, isDelayed = true, tooltip = help };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), true)); settings.Add(field);
        }

        private void Point(string label, LocalPointData point)
        {
            Vector2Field field = new Vector2Field(label) { value = new Vector2(point.X, point.Y) };
            field.RegisterValueChangedCallback(evt => Change(() => { point.X = evt.newValue.x; point.Y = evt.newValue.y; }));
            settings.Add(field);
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

        private void Choice<T>(string label, List<T> options, Func<T, string> name, T current, Action<T> set)
        {
            List<string> names = options.Select(name).ToList();
            PopupField<string> field = new PopupField<string>(label, names, Mathf.Max(0, options.IndexOf(current)));
            field.RegisterValueChangedCallback(evt => Change(() => set(options[field.index]), true));
            settings.Add(field);
        }

        private void Select(Kind kind, string id)
        {
            selectedKind = kind;
            selectedElementId = id;
            if (kind == Kind.Art) { if (tab != Tab.Light) tab = Tab.Art; }
            else if (kind != Kind.None) tab = Tab.Game;
            BuildSettings();
        }

        private void SelectButton(Kind kind, string id, string title)
        {
            bool active = selectedKind == kind && selectedElementId == id;
            AddButton(settings, (active ? "● " : "") + title, () => Select(kind, id));
        }

        private void BuildSettings()
        {
            if (settings == null) return;
            settings.Clear();
            LocalLocationDefinition location = Location;
            if (location == null) return;
            Heading(location.DisplayName);
            VisualElement tabs = new VisualElement(); tabs.style.flexDirection = FlexDirection.Row; tabs.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < TabNames.Length; i++)
            {
                Tab value = (Tab)i;
                Button button = new Button(() => { tab = value; BuildSettings(); }) { text = TabNames[i] };
                if (value == tab) { button.style.unityFontStyleAndWeight = FontStyle.Bold; button.style.color = new Color(.95f, .82f, .5f); }
                tabs.Add(button);
            }
            settings.Add(tabs);
            switch (tab)
            {
                case Tab.Game: BuildGameSettings(location); return;
                case Tab.Light: BuildLightSettings(location); return;
                case Tab.Art: BuildVisualSettings(location); return;
                case Tab.Ground: BuildGroundSettings(location); return;
            }
            Text("Название", location.DisplayName, value => { location.DisplayName = value; RefreshList(); });
            Text("ID места карты", location.WorldLocationId, value => location.WorldLocationId = value);

            if (tool == Tool.Terrain)
            {
                Heading("Кисть местности");
                Help("Как «Местность» глобальной карты: скалы и вода непроходимы, холмы, лес и болото замедляют. " +
                     "В бою клетка, чей центр на непроходимом, — стена; на замедляющем — трудная.");
                List<WorldMapGameplayTerrainType> types = WorldMapTerrainLabels.All.ToList();
                PopupField<string> type = new PopupField<string>("Тип", types.Select(WorldMapTerrainLabels.Name).ToList(), Mathf.Max(0, types.IndexOf(brushTerrain)));
                type.RegisterValueChangedCallback(evt => brushTerrain = types[type.index]); settings.Add(type);
                Slider radius = new Slider("Радиус кисти", 8, 300) { value = brushRadius, showInputField = true };
                radius.RegisterValueChangedCallback(evt => brushRadius = evt.newValue); settings.Add(radius);
                AddButton(settings, "Залить всё выбранным", () => Change(() =>
                {
                    WorldMapTerrainLayer layer = new WorldMapTerrainLayer(location.CreateGrid());
                    for (int i = 0; i < layer.Grid.CellCount; i++) layer.Set(layer.Grid.CellAt(i), brushTerrain);
                    location.TerrainCells = layer.Encode();
                }));
            }

            Heading("Рисунок и размер");
            Help("Размер рисунка места — в пикселях, как у глобальной карты. «Клеток проходимости по ширине» — " +
                 "точность разметки. При смене размера разметка пересчитывается.");
            Integer("Ширина рисунка", Mathf.RoundToInt(location.CanvasWidth), value => ResizeCanvas(location, value, location.CanvasHeight, location.HexesAcross));
            Integer("Высота рисунка", Mathf.RoundToInt(location.CanvasHeight), value => ResizeCanvas(location, location.CanvasWidth, value, location.HexesAcross));
            Integer("Клеток проходимости по ширине", location.HexesAcross, value => ResizeCanvas(location, location.CanvasWidth, location.CanvasHeight, value));
            if (Visual?.Ground != null && Visual.Ground.IsTiled)
                Help("Земля места — из участков экспорта Blender (вкладка «Земля»): «Рисунок места» ниже сейчас не показывается.");
            if (Visual != null)
            {
                SpriteField("Рисунок места", Visual.Background, value => Visual.Background = value);
                Label drop = new Label("Перетащите сюда рисунок места (PNG из Проводника или Sprite; рядом «имя_normal.png» — его нормали)");
                drop.style.whiteSpace = WhiteSpace.Normal;
                drop.style.unityTextAlign = TextAnchor.MiddleCenter;
                drop.style.paddingTop = drop.style.paddingBottom = 12;
                drop.style.marginTop = drop.style.marginBottom = 4;
                drop.style.color = new Color(.7f, .76f, .7f);
                drop.style.borderTopWidth = drop.style.borderBottomWidth = drop.style.borderLeftWidth = drop.style.borderRightWidth = 1;
                drop.style.borderTopColor = drop.style.borderBottomColor = drop.style.borderLeftColor = drop.style.borderRightColor = new Color(.45f, .5f, .45f);
                drop.RegisterCallback<DragUpdatedEvent>(evt =>
                {
                    bool content = (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0) || DragAndDrop.objectReferences.OfType<Sprite>().Any() ||
                                   DragAndDrop.objectReferences.OfType<Texture2D>().Any();
                    DragAndDrop.visualMode = content ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                    drop.style.backgroundColor = new Color(.95f, .75f, .3f, .2f);
                    evt.StopPropagation();
                });
                drop.RegisterCallback<DragLeaveEvent>(_ => drop.style.backgroundColor = StyleKeyword.Null);
                drop.RegisterCallback<DragPerformEvent>(evt =>
                {
                    DragAndDrop.AcceptDrag();
                    evt.StopPropagation();
                    Sprite[] sprites = DragAndDrop.objectReferences.OfType<Sprite>()
                        .Concat(DragAndDrop.objectReferences.OfType<Texture2D>().Select(texture => AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(texture))))
                        .Where(sprite => sprite != null).ToArray();
                    string[] paths = DragAndDrop.paths ?? Array.Empty<string>();
                    EditorApplication.delayCall += () => SetBackgroundFrom(sprites, paths);
                });
                settings.Add(drop);
                AddButton(settings, "Загрузить рисунок места…", () =>
                {
                    string path = EditorUtility.OpenFilePanel("Рисунок места (PNG)", "", "png");
                    if (!string.IsNullOrEmpty(path)) SetBackgroundFrom(Array.Empty<Sprite>(), new[] { path });
                });
                NormalMapField("Нормали рисунка места", Visual.Background, Visual.BackgroundNormalMap, value => Visual.BackgroundNormalMap = value);
                if (Visual.BackgroundNormalMap != null)
                    AddButton(settings, "Выпрямить нормали земли (снято наклонной камерой)", () =>
                    {
                        Texture2D level = GroundNormals.LevelCopy(Visual.BackgroundNormalMap, out string message);
                        if (level != null)
                        {
                            SpriteNormalMaps.Assign(Visual.Background, level, out _);
                            Change(() => Visual.BackgroundNormalMap = level, true);
                        }
                        status.text = message;
                    });
            }
            Toggle("Рисунок — временная заглушка", location.PlaceholderArt, value => location.PlaceholderArt = value);

            Heading("Перемещение");
            Help("Те же поля, что «Перемещение» глобальной карты, но свои числа для этого места.");
            WorldMapMovementRules rules = location.MovementRules;
            Number("Скорость бега (клеток/с)", rules.HeroRunSpeedHexesPerSecond, .5f, 30, value => location.Movement.HeroRunSpeedHexesPerSecond = value);
            Number("Часов на клетку", rules.TravelHoursPerHex, .001f, .2f, value => location.Movement.TravelHoursPerHex = value);
            Number("Часов на действие", (float)location.HoursPerInteraction, 0, 2, value => location.HoursPerInteraction = value);

            Heading("Бой на месте");
            Help("Сетка боя — ровно настройки поля Базы полей боя (масштаб и сдвиг сетки). Кадр поля (16:9) ложится на " +
                 "рисунок там, где задан кадр столкновения; «Ширина кадра» — сколько пикселей рисунка он закрывает. " +
                 "Отключённые гексы поля в бою на месте не действуют: стены задаёт разметка.");
            List<BattlefieldDefinitionData> fieldOptions = fields.Battlefields.Where(item => item != null).ToList();
            Choice("Поле боя", fieldOptions, item => item.DisplayLabel, Field, item => location.BattlefieldId = item.Id);
            AddButton(settings, "Открыть поле в базе", () => EditorApplication.ExecuteMenuItem("Kingdom Survival/База полей боя"));
            FieldNumber("Масштаб сетки (поле)", "gridScale", Field?.GridScale ?? 1, .5f, 1.5f);
            FieldVector("Сдвиг сетки (поле)", "gridOffset", Field?.GridOffset ?? Vector2.zero);
            Number("Ширина кадра боя (пиксели)", location.BattleFrameWidth, 320, Mathf.Max(640, location.CanvasWidth * 2), value => location.BattleFrameWidth = value);

            Heading("Служебные слои");
            UnityEngine.UIElements.Toggle terrain = new UnityEngine.UIElements.Toggle("Местность") { value = showTerrain };
            terrain.RegisterValueChangedCallback(evt => showTerrain = evt.newValue); settings.Add(terrain);
            UnityEngine.UIElements.Toggle markers = new UnityEngine.UIElements.Toggle("Входы, объекты, противники") { value = showMarkers };
            markers.RegisterValueChangedCallback(evt => showMarkers = evt.newValue); settings.Add(markers);
            UnityEngine.UIElements.Toggle arena = new UnityEngine.UIElements.Toggle("Зоны угрозы и кадры боя") { value = showArena };
            arena.RegisterValueChangedCallback(evt => showArena = evt.newValue); settings.Add(arena);
            UnityEngine.UIElements.Toggle lights = new UnityEngine.UIElements.Toggle("Источники света и солнце") { value = showLights };
            lights.RegisterValueChangedCallback(evt => showLights = evt.newValue); settings.Add(lights);
        }

        private void BuildGameSettings(LocalLocationDefinition location)
        {
            Heading("Входы");
            foreach (LocalEntranceDefinition entrance in location.Entrances)
                SelectButton(Kind.Entrance, entrance.Id, entrance.Label + " · " + entrance.Point);
            AddButton(settings, "+ Вход", () => Change(() =>
            {
                LocalEntranceDefinition entrance = new LocalEntranceDefinition { Id = UniqueId(location, "entry"), Label = "Вход", Point = new LocalPointData(viewCenter.x, viewCenter.y) };
                location.Entrances.Add(entrance); selectedKind = Kind.Entrance; selectedElementId = entrance.Id;
            }, true));
            LocalEntranceDefinition selectedEntrance = SelectedEntrance;
            if (selectedEntrance != null)
            {
                Text("ID", selectedEntrance.Id, value => { selectedEntrance.Id = value; selectedElementId = value; });
                Text("Подпись", selectedEntrance.Label, value => selectedEntrance.Label = value);
                Point("Точка", selectedEntrance.Point);
                AddButton(settings, "Удалить вход", () => Change(() => { location.Entrances.Remove(selectedEntrance); selectedKind = Kind.None; }, true));
            }

            Heading("Объекты места");
            foreach (LocalObjectDefinition item in location.Objects)
                SelectButton(Kind.GameObject, item.Id, item.Label + " · " + item.Point);
            AddButton(settings, "+ Объект", () => Change(() =>
            {
                LocalObjectDefinition item = new LocalObjectDefinition { Id = UniqueId(location, "object"), Label = "Объект", ActionLabel = "Осмотреть",
                    Kind = LocalObjectKind.Inspect, Text = "Описание.", Point = new LocalPointData(viewCenter.x, viewCenter.y) };
                location.Objects.Add(item); selectedKind = Kind.GameObject; selectedElementId = item.Id;
            }, true));
            LocalObjectDefinition selectedObject = SelectedGameObject;
            if (selectedObject != null)
            {
                Text("ID", selectedObject.Id, value => { selectedObject.Id = value; selectedElementId = value; });
                Text("Подпись", selectedObject.Label, value => selectedObject.Label = value);
                Text("Действие", selectedObject.ActionLabel, value => selectedObject.ActionLabel = value);
                Choice("Вид", new List<LocalObjectKind> { LocalObjectKind.Dialogue, LocalObjectKind.Inspect },
                    kind => kind == LocalObjectKind.Dialogue ? "Диалог" : "Осмотр (текст)", selectedObject.Kind, kind => selectedObject.Kind = kind);
                if (selectedObject.Kind == LocalObjectKind.Dialogue)
                    Text("ID диалога", selectedObject.DialogueId, value => selectedObject.DialogueId = value);
                LongText("Текст", selectedObject.Text, value => selectedObject.Text = value);
                Point("Точка", selectedObject.Point);
                Number("Радиус действия", selectedObject.InteractRadius, 20, 300, value => selectedObject.InteractRadius = value,
                    "Командир действует, подойдя на это расстояние (пиксели рисунка).");
                Toggle("Однократно", selectedObject.OnceOnly, value => selectedObject.OnceOnly = value);
                Text("Виден, если флаг", selectedObject.RequiresFlag, value => selectedObject.RequiresFlag = value);
                Text("Скрыт, если флаг", selectedObject.HiddenWhenFlag, value => selectedObject.HiddenWhenFlag = value);
                AddButton(settings, "Удалить объект места", () => Change(() => { location.Objects.Remove(selectedObject); selectedKind = Kind.None; }, true));
            }

            Heading("Столкновения");
            foreach (LocalEncounterDefinition encounter in location.Encounters)
                SelectButton(Kind.Encounter, encounter.Id, encounter.Id);
            AddButton(settings, "+ Столкновение", () => Change(() =>
            {
                string id = UniqueId(location, "encounter");
                LocalEncounterDefinition encounter = new LocalEncounterDefinition
                {
                    Id = id, BattleIdPrefix = "local.battle." + id + ".",
                    TriggerArea = new LocalAreaData(viewCenter.x - 60, viewCenter.y - 120, 120, 240),
                    RetreatPoint = new LocalPointData(viewCenter.x - 300, viewCenter.y)
                };
                location.Encounters.Add(encounter); selectedKind = Kind.Encounter; selectedElementId = id;
            }, true));
            LocalEncounterDefinition selectedEncounter = selectedKind == Kind.Encounter ? SelectedEncounter : null;
            if (selectedEncounter != null)
            {
                Text("ID", selectedEncounter.Id, value =>
                {
                    foreach (LocalEnemyDefinition enemy in location.Enemies.Where(enemy => enemy.EncounterId == selectedEncounter.Id))
                        enemy.EncounterId = value;
                    selectedEncounter.Id = value; selectedElementId = value;
                });
                Text("Префикс ID боя", selectedEncounter.BattleIdPrefix, value => selectedEncounter.BattleIdPrefix = value);
                Text("Диалог перед боем", selectedEncounter.IntroDialogueId, value => selectedEncounter.IntroDialogueId = value);
                Toggle("Отход разрешён", selectedEncounter.AllowRetreat, value => selectedEncounter.AllowRetreat = value);
                Point("Точка отхода", selectedEncounter.RetreatPoint);
                Toggle("Кадр боя задан", selectedEncounter.HasArenaCenter, value => selectedEncounter.HasArenaCenter = value, true);
                if (selectedEncounter.HasArenaCenter)
                    Point("Центр кадра боя", selectedEncounter.ArenaCenter);
                else
                    Help("Кадр не задан — в бою он встанет посередине между отрядом и противниками.");
                Text("Подготовленное начало (спутник)", selectedEncounter.PreparedStartCompanionId, value => selectedEncounter.PreparedStartCompanionId = value);
                Text("Флаг исчерпания", selectedEncounter.ResolvedFlag, value => selectedEncounter.ResolvedFlag = value);
                AddButton(settings, "Удалить столкновение", () => Change(() => { location.Encounters.Remove(selectedEncounter); selectedKind = Kind.None; }, true));
            }

            Heading("Противники");
            foreach (LocalEnemyDefinition enemy in location.Enemies)
                SelectButton(Kind.Enemy, enemy.InstanceId, enemy.InstanceId + " · " + enemy.UnitTypeId);
            AddButton(settings, "+ Противник", () => Change(() =>
            {
                LocalEnemyDefinition enemy = new LocalEnemyDefinition
                {
                    InstanceId = UniqueId(location, "enemy"), UnitTypeId = "forest_beast", Level = 1,
                    Point = new LocalPointData(viewCenter.x, viewCenter.y),
                    EncounterId = (SelectedEncounter ?? location.Encounters.FirstOrDefault())?.Id ?? string.Empty
                };
                location.Enemies.Add(enemy); selectedKind = Kind.Enemy; selectedElementId = enemy.InstanceId;
            }, true));
            LocalEnemyDefinition selectedEnemy = SelectedEnemy;
            if (selectedEnemy != null)
            {
                Text("ID экземпляра", selectedEnemy.InstanceId, value => { selectedEnemy.InstanceId = value; selectedElementId = value; });
                UnitDatabaseAsset units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
                List<UnitDefinitionData> options = units != null ? units.Units.Where(item => item != null).ToList() : new List<UnitDefinitionData>();
                if (options.Count > 0)
                    Choice("Существо", options, item => item.DisplayLabel + " (" + item.Id + ")", options.Find(item => item.Id == selectedEnemy.UnitTypeId),
                        item => selectedEnemy.UnitTypeId = item.Id);
                Integer("Уровень", selectedEnemy.Level, value => selectedEnemy.Level = Mathf.Max(1, value));
                if (location.Encounters.Count > 0)
                    Choice("Столкновение", location.Encounters, item => item.Id, location.FindEncounter(selectedEnemy.EncounterId), item => selectedEnemy.EncounterId = item.Id);
                Point("Логово (точка)", selectedEnemy.Point);
                AddButton(settings, "Удалить противника", () => Change(() => { location.Enemies.Remove(selectedEnemy); selectedKind = Kind.None; }, true));
            }
        }

        private void BuildVisualSettings(LocalLocationDefinition location)
        {
            LocationVisualDefinition visual = Visual;
            if (visual == null)
            {
                Heading("Художественная сборка");
                Help("Для этого места ещё нет художественной сборки (рисунок, предметы, свет).");
                AddButton(settings, "Добавить сборку и свет", () => Change(() => database.visuals.Add(new LocationVisualDefinition { LocationId = selectedId }), true));
                return;
            }
            Heading("Тест");
            UnitDatabaseAsset units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
            if (units != null)
            {
                List<UnitDefinitionData> options = units.Units.Where(item => item != null).ToList();
                Choice("Персонаж теста", options, item => item.DisplayLabel, options.Find(item => item.Id == visual.TestUnitId), item => visual.TestUnitId = item.Id);
            }
            SliderInt followers = new SliderInt("Спутников в тесте", 0, 4) { value = visual.TestFollowers, showInputField = true };
            followers.RegisterValueChangedCallback(evt => Change(() => visual.TestFollowers = evt.newValue)); settings.Add(followers);

            Heading("Предметы рисунка");
            VisualElement actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; actions.style.flexWrap = Wrap.Wrap; settings.Add(actions);
            AddButton(actions, "+ Из Базы ассетов", PickAssetToAdd);
            AddButton(actions, "PNG…", ImportPng);
            AddButton(actions, "+ Костёр", () => AddArtObject(null, LocationPlaceholder.Fire));
            AddButton(actions, "+ Палатка", () => AddArtObject(null, LocationPlaceholder.Tent));
            BuildAssetPalette();
            foreach (LocationVisualObject item in visual.Objects)
                SelectButton(Kind.Art, item.Id, item.Name + (item.UsesAsset ? " · ассет" : "") + (item.Hidden ? " · скрыт" : ""));
            AddButton(settings, "Перевести предметы места в Базу ассетов…", MigrateLocationObjects);
            LocationVisualObject selected = ArtObject;
            if (selected == null) return;
            Heading("Выбранный предмет");
            if (selected.UsesAsset)
            {
                BuildAssetInstanceSettings(visual, selected);
                return;
            }
            Text("Название", selected.Name, value => selected.Name = value);
            if (!selected.LightOnly && selected.Sprite != null)
                AddButton(settings, "Сохранить этот предмет в Базу ассетов", () => MigrateObjects(new List<LocationVisualObject> { selected }, false));
            SpriteField("Рисунок", selected.Sprite, value => selected.Sprite = value);
            Text("Группа частей", selected.GroupId, value => selected.GroupId = value);
            Number("Высота рисунка", selected.Height, .1f, 6, value => selected.Height = value, "Единицы мира: 108 пикселей рисунка = 1.");
            Toggle("Отразить по X", selected.FlipX, value => selected.FlipX = value);
            Toggle("Заблокировать", selected.Locked, value => selected.Locked = value);
            Toggle("Скрыть", selected.Hidden, value => selected.Hidden = value);
            PopupField<string> band = new PopupField<string>("Слой", new List<string> { "Земля", "Детали земли", "Объекты и персонажи", "Кроны / крыши" }, (int)selected.Band);
            band.RegisterValueChangedCallback(evt => Change(() => selected.Band = (LocationVisualBand)band.index)); settings.Add(band);
            Number("Порядок внутри слоя", selected.OrderOffset, -1000, 1000, value => selected.OrderOffset = Mathf.RoundToInt(value));
            AddButton(settings, "Поставить точку опоры мышью", () => SetTool(Tool.Pivot));
            Toggle("Блокирует проход", selected.BlocksMovement, value => selected.BlocksMovement = value);
            Vector2Field footprint = new Vector2Field("Основание на земле") { value = selected.Footprint };
            footprint.RegisterValueChangedCallback(evt => Change(() => selected.Footprint = Vector2.Max(Vector2.zero, evt.newValue))); settings.Add(footprint);
            Toggle("Только источник света (без рисунка)", selected.LightOnly, value => selected.LightOnly = value, true);
            Heading("Тени предмета");
            Toggle("Отбрасывает тень-силуэт (солнце и огонь)", selected.ProjectsShadow, value => selected.ProjectsShadow = value);
            Number("Длина тени (множитель)", selected.ShadowLength, 0, 3, value => selected.ShadowLength = value);
            SpriteField("Свой силуэт тени", selected.ShadowSprite, value => selected.ShadowSprite = value);
            Toggle("Перекрывает свет местных источников (по основанию)", selected.CastsShadow, value => selected.CastsShadow = value);
            NormalMapField("Карта нормалей", selected.ResolveSprite(), selected.NormalMap, value => selected.NormalMap = value);
            Heading("Свет этого предмета");
            LightEditor(selected);
            Heading("Состояния рисунка");
            foreach (LocationVisualVariant variant in selected.Variants)
            {
                Text("Название состояния", variant.Name, value => variant.Name = value);
                SpriteField(variant.Name, variant.Sprite, value => variant.Sprite = value);
                NormalMapField("Нормаль: " + variant.Name, variant.Sprite, variant.NormalMap, value => variant.NormalMap = value);
                AddButton(settings, "Показать: " + variant.Name, () => Change(() => selected.DefaultVariantId = variant.Id));
            }
            AddButton(settings, "+ Состояние", () => Change(() => selected.Variants.Add(new LocationVisualVariant { Id = Guid.NewGuid().ToString("N"), Name = "Новое состояние" }), true));
            AddButton(settings, "Основной рисунок", () => Change(() => selected.DefaultVariantId = ""));
            AddButton(settings, "Дублировать предмет", () => Change(() =>
            {
                LocationVisualObject copy = JsonUtility.FromJson<LocationVisualObject>(JsonUtility.ToJson(selected));
                copy.Id = Guid.NewGuid().ToString("N"); copy.GroupId = ""; copy.Position += new Vector2(.03f, .03f);
                visual.Objects.Add(copy); selectedElementId = copy.Id;
            }, true));
            AddButton(settings, "Удалить предмет", () => Change(() => { visual.Objects.Remove(selected); selectedKind = Kind.None; }, true));
        }

        private static string UniqueId(LocalLocationDefinition location, string prefix)
        {
            HashSet<string> ids = new HashSet<string>(location.Entrances.Select(item => item.Id)
                .Concat(location.Objects.Select(item => item.Id))
                .Concat(location.Enemies.Select(item => item.InstanceId))
                .Concat(location.Encounters.Select(item => item.Id)));
            for (int i = 1; ; i++)
            {
                string id = location.Id + "." + prefix + "." + i;
                if (!ids.Contains(id)) return id;
            }
        }

        // Новый размер рисунка: разметка пересчитывается, точки — в той же доле.
        // exact — размер земли из участков (1 пиксель земли = 1 пиксель места),
        // без ограничений поля ввода.
        private void ResizeCanvas(LocalLocationDefinition location, float width, float height, int hexesAcross, bool exact = false)
        {
            ResizeLocation(location, width, height, hexesAcross, exact);
            viewCenter = new Vector2(location.CanvasWidth, location.CanvasHeight) / 2;
            zoom = 1;
        }

        public static void ResizeLocation(LocalLocationDefinition location, float width, float height, int hexesAcross, bool exact = false)
        {
            width = exact ? Mathf.Clamp(width, 16, 65536) : Mathf.Clamp(width, 256, 8192);
            height = exact ? Mathf.Clamp(height, 16, 65536) : Mathf.Clamp(height, 256, 8192);
            WorldMapTerrainLayer old = location.CreateTerrainLayer();
            float sx = width / Mathf.Max(1, location.CanvasWidth), sy = height / Mathf.Max(1, location.CanvasHeight);
            void Scale(LocalPointData point) { if (point == null) return; point.X *= sx; point.Y *= sy; }
            location.CanvasWidth = width;
            location.CanvasHeight = height;
            location.HexesAcross = WorldMapHexGrid.SanitizeHexesAcross(hexesAcross);
            location.TerrainCells = old.IsEmpty ? string.Empty : old.ResampleTo(location.CreateGrid()).Encode();
            location.Entrances.ForEach(item => Scale(item.Point));
            location.Objects.ForEach(item => Scale(item.Point));
            location.Enemies.ForEach(item => Scale(item.Point));
            foreach (LocalEncounterDefinition encounter in location.Encounters)
            {
                Scale(encounter.RetreatPoint); Scale(encounter.ArenaCenter);
                encounter.TriggerArea.X *= sx; encounter.TriggerArea.Width *= sx;
                encounter.TriggerArea.Y *= sy; encounter.TriggerArea.Height *= sy;
            }
        }

        // ------------------------------------------------------------------
        // Предпросмотр
        // ------------------------------------------------------------------

        private void RebuildPreview()
        {
            ReleasePreview();
            if (Location == null || Field == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            preview = new PreviewRenderUtility(true);
            renderer = new LocationWorldRenderer(Location, Visual, Field, null, database.worldLighting);
            renderer.Camera.enabled = false;
            geometry = renderer.Geometry;
            preview.AddSingleGO(renderer.Root);
            preview.camera.orthographic = true;
            preview.camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            preview.camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            preview.camera.GetUniversalAdditionalCameraData().volumeLayerMask = 1 << LocationWorldRenderer.PostVolumeLayer;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.035f, .045f, .04f);
            RebuildTerrainOverlay();
            ApplyGroundView();
        }

        private void RebuildTerrainOverlay()
        {
            if (terrainOverlay != null) DestroyImmediate(terrainOverlay);
            terrainOverlay = null;
            LocalLocationDefinition location = Location;
            if (location == null) return;
            WorldMapTerrainLayer layer = paintLayer ?? location.CreateTerrainLayer();
            int width = Mathf.Clamp(Mathf.RoundToInt(location.CanvasWidth / 8), 8, 512);
            int height = Mathf.Clamp(Mathf.RoundToInt(location.CanvasHeight / 8), 8, 512);
            terrainOverlay = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            Color[] colors = new Color[width * height];
            WorldMapMovementRules rules = location.MovementRules;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    WorldMapGameplayTerrainType type = layer.GetAtPixel((x + .5) / width * location.CanvasWidth, (1 - (y + .5) / height) * location.CanvasHeight);
                    Color color = !rules.IsTraversable(type) ? new Color(1, .15f, .1f, .38f)
                        : rules.RunSpeedMultiplier(type) < LocalLocationGeometry.DifficultRunMultiplier ? new Color(1, .75f, .2f, .28f)
                        : type == WorldMapGameplayTerrainType.OpenGround ? Color.clear : new Color(.4f, .8f, 1, .18f);
                    colors[y * width + x] = color;
                }
            }
            terrainOverlay.SetPixels(colors);
            terrainOverlay.Apply();
        }

        private void ReleasePreview()
        {
            renderer?.Dispose(); renderer = null; geometry = null;
            preview?.Cleanup(); preview = null;
            if (terrainOverlay != null) DestroyImmediate(terrainOverlay);
            terrainOverlay = null;
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = Mathf.Clamp((float)(now - lastUpdate), 0, .1f); lastUpdate = now;
            FlushPending();
            if (cycle) hour = Mathf.Repeat(hour + delta * 24 / 30, 24);
            if (clock != null) clock.text = LocationLightingTest.FormatHour(hour);
            hourSlider?.SetValueWithoutNotify(hour);
            canvas?.MarkDirtyRepaint();
        }

        private Rect CanvasFrame(Rect area)
        {
            float aspect = CanvasSize.x / CanvasSize.y;
            float height = Mathf.Min(area.height, area.width / aspect);
            return new Rect(area.center.x - height * aspect / 2, area.center.y - height / 2, height * aspect, height);
        }

        private Vector2 ToPixel(Rect frame, Vector2 mouse) => viewCenter + (mouse - frame.center) * (ViewHeight / frame.height);
        private Vector2 ToGui(Rect frame, Vector2 pixel) => frame.center + (pixel - viewCenter) * (frame.height / ViewHeight);

        private void DrawPreview()
        {
            Rect area = new Rect(0, 0, canvas.contentRect.width, canvas.contentRect.height);
            if (area.width < 10 || area.height < 10) return;
            EditorGUI.DrawRect(area, new Color(.045f, .055f, .05f));
            if (renderer == null || preview == null)
            {
                GUI.Label(area, EditorApplication.isPlaying ? "Локация запущена во вкладке Game." : "Выберите место с полем боя.");
                return;
            }
            if (compareDay)
            {
                if (Event.current.type == EventType.Repaint) DrawDayComparison(area);
                return;
            }
            Rect frame = CanvasFrame(area);
            Event evt = Event.current;
            Vector2 pixel = ToPixel(frame, evt.mousePosition);
            if (HandleInput(evt, frame, pixel)) return;
            if (evt.type != EventType.Repaint) return;

            renderer.SetTime(hour, (float)EditorApplication.timeSinceStartup);
            RenderPreviewActors();
            Vector2 world = LocationVisualGeometry.PixelToWorld(Location, viewCenter);
            preview.camera.transform.position = new Vector3(world.x, world.y, -10);
            preview.camera.transform.rotation = Quaternion.identity;
            preview.camera.orthographicSize = ViewHeight / LocationVisualGeometry.PixelsPerUnit / 2;
            preview.camera.GetUniversalAdditionalCameraData().renderPostProcessing = Visual != null && Visual.Post.Enabled;
            preview.BeginPreview(frame, GUIStyle.none);
            preview.Render(true);
            Texture texture = preview.EndPreview();
            GUI.DrawTexture(frame, texture, ScaleMode.StretchToFill);
            Vector2 mouse = evt.mousePosition;
            GUI.BeginClip(frame);
            Vector2 shift = -frame.position;
            if (showTerrain && terrainOverlay != null)
            {
                Vector2 a = ToGui(frame, Vector2.zero) + shift, b = ToGui(frame, CanvasSize) + shift;
                GUI.DrawTexture(Rect.MinMaxRect(a.x, a.y, b.x, b.y), terrainOverlay, ScaleMode.StretchToFill, true);
            }
            DrawOverlays(frame, shift);
            Handles.BeginGUI();
            DrawLightOverlays(frame, shift);
            Handles.EndGUI();
            DrawGroundOverlay(frame, shift, mouse);
            if (tool == Tool.Terrain && frame.Contains(mouse))
            {
                Handles.color = new Color(1, 1, 1, .7f);
                Handles.DrawWireDisc(mouse + shift, Vector3.forward, brushRadius * frame.height / ViewHeight);
            }
            GUI.EndClip();
        }

        private void RenderPreviewActors()
        {
            List<LocationWorldRenderer.ActorFrame> frames = new List<LocationWorldRenderer.ActorFrame>();
            if (Visual != null)
            {
                Vector2 start = LocationVisualGeometry.ToPixel(Location, Visual.TestStartPoint);
                frames.Add(new LocationWorldRenderer.ActorFrame { Id = "preview_hero", UnitTypeId = Visual.TestUnitId, Pixel = start });
            }
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

        private void DrawOverlays(Rect frame, Vector2 shift)
        {
            LocalLocationDefinition location = Location;
            Vector2 Gui(float x, float y) => ToGui(frame, new Vector2(x, y)) + shift;
            float scale = frame.height / ViewHeight;
            Handles.BeginGUI();
            if (showArena && geometry != null && Field != null)
            {
                foreach (LocalEncounterDefinition encounter in location.Encounters)
                {
                    bool active = encounter == SelectedEncounter;
                    LocalAreaData area = encounter.TriggerArea;
                    Vector2 a = Gui(area.X, area.Y), b = Gui(area.X + area.Width, area.Y + area.Height);
                    Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(a.x, a.y, b.x, b.y), new Color(1, .45f, .1f, active ? .16f : .07f), new Color(1, .5f, .15f, .9f));
                    Vector2 retreat = Gui(encounter.RetreatPoint.X, encounter.RetreatPoint.Y);
                    Handles.color = new Color(.4f, .7f, 1f, .95f);
                    Handles.DrawWireDisc(retreat, Vector3.forward, 7);
                    GUI.Label(new Rect(retreat.x + 8, retreat.y - 9, 120, 18), "отход");
                    if (!active) continue;
                    List<LocalPointData> anchor = location.Enemies.Where(enemy => enemy.EncounterId == encounter.Id).Select(enemy => enemy.Point).ToList();
                    anchor.Add(new LocalPointData(area.X + area.Width / 2, area.Y + area.Height / 2));
                    Vector2 arenaCenter = geometry.ArenaCenterFor(encounter, anchor);
                    Rect rect = geometry.FrameRect(arenaCenter);
                    Vector2 fa = Gui(rect.xMin, rect.yMin), fb = Gui(rect.xMax, rect.yMax);
                    Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(fa.x, fa.y, fb.x, fb.y), Color.clear, new Color(.95f, .9f, .6f, .9f));
                    HashSet<HexCoord> blocked = geometry.ArenaBlockedCells(arenaCenter);
                    HashSet<HexCoord> difficult = geometry.ArenaDifficultCells(arenaCenter);
                    BattlefieldGridLayout layout = geometry.ArenaLayout(arenaCenter);
                    foreach (HexCoord cell in SandboxArenaShape.Cells())
                    {
                        Vector2 center = layout.GetCenter(cell.Q, cell.R);
                        Vector2 p = Gui(center.x, center.y);
                        Handles.color = blocked.Contains(cell) ? new Color(1, .25f, .2f, .9f)
                            : difficult.Contains(cell) ? new Color(1, .8f, .3f, .9f) : new Color(.85f, .85f, .7f, .55f);
                        Handles.DrawWireDisc(p, Vector3.forward, Mathf.Max(3, layout.Size * scale * .8f));
                    }
                }
            }
            if (showMarkers)
            {
                foreach (LocalEntranceDefinition entrance in location.Entrances)
                    Marker(Gui(entrance.Point.X, entrance.Point.Y), new Color(.35f, .9f, .45f), entrance.Label, selectedKind == Kind.Entrance && selectedElementId == entrance.Id);
                foreach (LocalObjectDefinition item in location.Objects)
                {
                    Vector2 p = Gui(item.Point.X, item.Point.Y);
                    Handles.color = new Color(.95f, .8f, .4f, .35f);
                    Handles.DrawWireDisc(p, Vector3.forward, item.InteractRadius * scale);
                    Marker(p, new Color(.95f, .8f, .4f), item.Label, selectedKind == Kind.GameObject && selectedElementId == item.Id);
                }
                foreach (LocalEnemyDefinition enemy in location.Enemies)
                    Marker(Gui(enemy.Point.X, enemy.Point.Y), new Color(.95f, .35f, .3f), enemy.InstanceId, selectedKind == Kind.Enemy && selectedElementId == enemy.InstanceId);
                if (Visual != null)
                {
                    Vector2 start = LocationVisualGeometry.ToPixel(location, Visual.TestStartPoint);
                    Marker(Gui(start.x, start.y), new Color(.6f, .8f, 1f), "старт теста", false);
                }
            }
            LocationVisualObject art = ArtObject;
            if (art != null)
            {
                Vector2 point = LocationVisualGeometry.ToPixel(location, art.Position);
                Vector2 p = Gui(point.x, point.y);
                EditorGUI.DrawRect(new Rect(p.x - 5, p.y - 1, 10, 2), Color.yellow);
                EditorGUI.DrawRect(new Rect(p.x - 1, p.y - 5, 2, 10), Color.yellow);
                LocationResolvedVisual resolved = LocationVisualResolver.Resolve(art);
                if (resolved.BlocksMovement || art.UsesAsset)
                {
                    Rect footprint = LocationVisualGeometry.FootprintRect(location, art, resolved);
                    Vector2 a = Gui(footprint.xMin, footprint.yMin), b = Gui(footprint.xMax, footprint.yMax);
                    Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(a.x, a.y, b.x, b.y),
                        new Color(1, .35f, .15f, resolved.BlocksMovement ? .1f : .03f), new Color(1, .4f, .2f, resolved.BlocksMovement ? .8f : .3f));
                }
            }
            Handles.EndGUI();
        }

        private static void Marker(Vector2 p, Color color, string label, bool selected)
        {
            Handles.color = color;
            Handles.DrawSolidDisc(p, Vector3.forward, selected ? 7 : 5);
            if (selected) { Handles.color = Color.white; Handles.DrawWireDisc(p, Vector3.forward, 10); }
            GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = color } };
            GUI.Label(new Rect(p.x + 9, p.y - 9, 220, 18), label, style);
        }

        // Приём перетаскивания — событиями UI Toolkit: файлы из Проводника и
        // Project, Sprite и ассеты из окна «База ассетов» доходят надёжно.
        private void RegisterCanvasDrop()
        {
            canvas.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DropPixel(evt.mousePosition, out _) ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                evt.StopPropagation();
            });
            canvas.RegisterCallback<DragPerformEvent>(evt =>
            {
                evt.StopPropagation();
                if (!DropPixel(evt.mousePosition, out Vector2 pixel)) return;
                DragAndDrop.AcceptDrag();
                string assetId = ArtAssetPicker.DraggedAssetId();
                Sprite[] sprites = (DragAndDrop.objectReferences ?? Array.Empty<UnityEngine.Object>()).OfType<Sprite>().ToArray();
                string[] paths = DragAndDrop.paths ?? Array.Empty<string>();
                ArtAssetPicker.EndDrag();
                EditorApplication.delayCall += () => PerformCanvasDrop(assetId, sprites, paths, pixel);
            });
        }

        private bool DropPixel(Vector2 worldMouse, out Vector2 pixel)
        {
            pixel = default;
            if (renderer == null || compareDay || Visual == null) return false;
            bool content = !string.IsNullOrEmpty(ArtAssetPicker.DraggedAssetId()) ||
                           (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0) ||
                           (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0);
            Vector2 mouse = canvas.WorldToLocal(worldMouse);
            Rect frame = CanvasFrame(new Rect(0, 0, canvas.contentRect.width, canvas.contentRect.height));
            if (!content || !frame.Contains(mouse)) return false;
            pixel = ToPixel(frame, mouse);
            return true;
        }

        private void PerformCanvasDrop(string assetId, Sprite[] sprites, string[] paths, Vector2 pixel)
        {
            // Ассет, перетащенный из окна «База ассетов»: экземпляр по ссылке.
            if (!string.IsNullOrEmpty(assetId))
            {
                AddAssetInstance(assetId, pixel);
                return;
            }
            // Папка экспорта Blender или его manifest — импорт земли.
            if (paths.Length == 1 && (Directory.Exists(paths[0]) || paths[0].EndsWith("_manifest.json", StringComparison.OrdinalIgnoreCase)))
            {
                LoadPackage(paths[0]);
                return;
            }
            List<string> files = paths.Where(path => sprites.All(sprite => AssetDatabase.GetAssetPath(sprite) != path))
                .Where(path => File.Exists(path) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
            List<string> normals = files.Where(IsNormalFileName).ToList();
            // Один большой рисунок — скорее всего фон места: спросить, куда его.
            List<string> colors = files.Except(normals).ToList();
            if (sprites.Length + colors.Count == 1)
            {
                Vector2Int size = sprites.Length == 1 ? new Vector2Int((int)sprites[0].rect.width, (int)sprites[0].rect.height) : PngSize(colors[0]);
                if (size.x >= CanvasSize.x * .5f || size.y >= CanvasSize.y * .5f)
                {
                    int choice = EditorUtility.DisplayDialogComplex("Куда поставить рисунок?",
                        "Рисунок " + size.x + "×" + size.y + " — размером почти с место (" + Mathf.RoundToInt(CanvasSize.x) + "×" + Mathf.RoundToInt(CanvasSize.y) + ").",
                        "Рисунок места (фон)", "Отмена", "Предмет");
                    if (choice == 1) return;
                    if (choice == 0) { SetBackgroundFrom(sprites, paths); return; }
                }
            }
            foreach (Sprite sprite in sprites) AddArtObject(sprite, LocationPlaceholder.None, pixel);
            // PNG с парой «*_normal.png» / «*_n.png»: нормаль подключается к рисунку.
            int added = 0, withNormals = 0;
            foreach (string path in files.Except(normals))
            {
                Sprite sprite = ImportSprite(path);
                if (sprite == null) continue;
                string stem = Path.GetFileNameWithoutExtension(path);
                string normalPath = normals.FirstOrDefault(normal => string.Equals(NormalBaseName(normal), stem, StringComparison.OrdinalIgnoreCase));
                Texture2D normal = normalPath != null ? ImportNormalTexture(normalPath) : null;
                if (normal != null && SpriteNormalMaps.Assign(sprite, normal, out _)) withNormals++;
                AddArtObject(sprite, LocationPlaceholder.None, pixel);
                if (normal != null && ArtObject != null) Change(() => ArtObject.NormalMap = normal);
                added++;
            }
            if (added > 0) status.text = "Добавлено рисунков: " + added + (withNormals > 0 ? ", с нормалями: " + withNormals : "") + ".";
            else if (normals.Count > 0) status.text = "Нормаль без рисунка: перетащите её в поле «Карта нормалей» предмета (вкладка «Предметы»).";
        }

        // Размер PNG по заголовку (без импорта).
        public static Vector2Int PngSize(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    byte[] header = new byte[24];
                    if (stream.Read(header, 0, 24) < 24) return Vector2Int.zero;
                    int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                    int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                    return new Vector2Int(width, height);
                }
            }
            catch (IOException)
            {
                return Vector2Int.zero;
            }
        }

        // Рисунок места из перетаскивания (PNG из Проводника или Sprite из
        // Project); пара «*_normal» / «*_n» — нормали рисунка места. Если
        // размер рисунка другой — предложить подогнать размер места.
        private void SetBackgroundFrom(Sprite[] sprites, string[] paths)
        {
            if (Visual == null || Location == null) { status.text = "Для рисунка места нужна художественная сборка (вкладка «Предметы»)."; return; }
            List<string> files = paths.Where(path => File.Exists(path) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .Where(path => sprites.All(sprite => AssetDatabase.GetAssetPath(sprite) != path)).ToList();
            List<string> normals = files.Where(IsNormalFileName).ToList();
            Sprite background = sprites.FirstOrDefault() ?? files.Except(normals).Select(ImportSprite).FirstOrDefault(sprite => sprite != null);
            if (background == null)
            {
                status.text = normals.Count > 0 ? "Это карта нормалей — перетащите вместе с ней сам рисунок места." : "Нужен PNG или Sprite.";
                return;
            }
            string stem = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(background));
            string normalPath = normals.FirstOrDefault(normal => string.Equals(NormalBaseName(normal), stem, StringComparison.OrdinalIgnoreCase)) ??
                                (normals.Count == 1 ? normals[0] : null);
            Texture2D normal = normalPath != null ? ImportNormalTexture(normalPath) : null;
            if (normal != null) SpriteNormalMaps.Assign(background, normal, out _);
            LocalLocationDefinition location = Location;
            Vector2 size = background.rect.size;
            bool resize = Mathf.Abs(size.x - location.CanvasWidth) > .5f || Mathf.Abs(size.y - location.CanvasHeight) > .5f;
            if (resize)
                resize = EditorUtility.DisplayDialog("Размер места",
                    "Рисунок " + size.x + "×" + size.y + ", место " + Mathf.RoundToInt(location.CanvasWidth) + "×" + Mathf.RoundToInt(location.CanvasHeight) +
                    ".\nПодогнать размер места под рисунок? Входы, объекты и противники сохранят свою долю рисунка, разметка пересчитается.",
                    "Подогнать", "Растянуть рисунок на место");
            Change(() =>
            {
                Visual.Background = background;
                if (normal != null) Visual.BackgroundNormalMap = normal;
                if (resize) ResizeCanvas(location, size.x, size.y, location.HexesAcross);
            }, true);
            status.text = "Рисунок места: " + background.name + (normal != null ? " (с нормалями)" : "") + (resize ? ", размер места подогнан." : ".");
        }

        internal static bool IsNormalFileName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.EndsWith("_n", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_normal", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("_normals", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_нормаль", StringComparison.OrdinalIgnoreCase);
        }

        // «tent_normal» → «tent».
        internal static string NormalBaseName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            foreach (string suffix in new[] { "_normals", "_normal", "_нормаль", "_n" })
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - suffix.Length);
            return name;
        }

        // Внешняя карта нормалей — копия в Art/Locations, импорт как Normal map.
        internal static Texture2D ImportNormalTexture(string path)
        {
            if (!File.Exists(path)) return null;
            if (!path.Replace("\\", "/").StartsWith("Assets/", StringComparison.Ordinal))
            {
                string folder = "Assets/_Project/Art/Locations";
                Directory.CreateDirectory(folder);
                string destination = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileName(path));
                File.Copy(path, destination);
                path = destination;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null) SpriteNormalMaps.PrepareNormalTexture(texture);
            return texture;
        }

        private bool HandleInput(Event evt, Rect frame, Vector2 pixel)
        {
            LocalLocationDefinition location = Location;
            if (evt.type == EventType.MouseDown && frame.Contains(evt.mousePosition)) canvas.Focus();
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && tool == Tool.PlaceAsset)
            {
                SetTool(Tool.Select); evt.Use(); return true;
            }
            if (evt.type == EventType.ScrollWheel && frame.Contains(evt.mousePosition))
            {
                Vector2 before = pixel;
                zoom = Mathf.Clamp(zoom * Mathf.Pow(1.1f, -evt.delta.y), .5f, 8);
                viewCenter += before - ToPixel(frame, evt.mousePosition);
                evt.Use(); return true;
            }
            if (evt.type == EventType.MouseDown && evt.button == 1 && frame.Contains(evt.mousePosition))
            {
                lastPointer = evt.mousePosition; evt.Use(); return true;
            }
            if (evt.type == EventType.MouseDrag && evt.button == 1)
            {
                viewCenter -= (evt.mousePosition - lastPointer) * (ViewHeight / frame.height);
                lastPointer = evt.mousePosition; evt.Use(); return true;
            }
            if (tool == Tool.LightShape && evt.type == EventType.MouseDown && evt.button == 0 && frame.Contains(evt.mousePosition))
            {
                LightShapeMouseDown(location, frame, evt.mousePosition, evt.shift);
                evt.Use(); return true;
            }
            if (tool == Tool.LightShape && evt.type == EventType.MouseDrag && evt.button == 0 && dragging)
            {
                LightShapeMouseDrag(location, frame, evt.mousePosition);
                evt.Use(); return true;
            }
            if (evt.type == EventType.MouseDown && evt.button == 0 && frame.Contains(evt.mousePosition))
            {
                MouseDown(location, frame, pixel);
                evt.Use(); return true;
            }
            if (evt.type == EventType.MouseDrag && evt.button == 0 && dragging)
            {
                MouseDrag(location, pixel);
                evt.Use(); return true;
            }
            if (evt.type == EventType.MouseUp && evt.button == 0 && dragging)
            {
                MouseUp(location, pixel);
                evt.Use(); return true;
            }
            return false;
        }

        private void MouseDown(LocalLocationDefinition location, Rect frame, Vector2 pixel)
        {
            LocalPointData point = new LocalPointData(pixel.x, pixel.y);
            switch (tool)
            {
                case Tool.Terrain:
                    Undo.RecordObject(database, "Местность места");
                    paintLayer = location.CreateTerrainLayer();
                    Paint(pixel);
                    dragging = true;
                    return;
                case Tool.Entrance:
                    Change(() =>
                    {
                        LocalEntranceDefinition entrance = SelectedEntrance;
                        if (entrance == null) { entrance = new LocalEntranceDefinition { Id = UniqueId(location, "entry"), Label = "Вход" }; location.Entrances.Add(entrance); }
                        entrance.Point = point; selectedKind = Kind.Entrance; selectedElementId = entrance.Id;
                    }, true);
                    return;
                case Tool.ObjectPoint:
                    Change(() =>
                    {
                        LocalObjectDefinition item = SelectedGameObject;
                        if (item == null)
                        {
                            item = new LocalObjectDefinition { Id = UniqueId(location, "object"), Label = "Объект", ActionLabel = "Осмотреть", Kind = LocalObjectKind.Inspect, Text = "Описание." };
                            location.Objects.Add(item);
                        }
                        item.Point = point; selectedKind = Kind.GameObject; selectedElementId = item.Id;
                    }, true);
                    return;
                case Tool.Enemy:
                    Change(() =>
                    {
                        LocalEnemyDefinition enemy = SelectedEnemy;
                        if (enemy == null)
                        {
                            enemy = new LocalEnemyDefinition { InstanceId = UniqueId(location, "enemy"), UnitTypeId = "forest_beast", Level = 1,
                                EncounterId = location.Encounters.FirstOrDefault()?.Id ?? string.Empty };
                            location.Enemies.Add(enemy);
                        }
                        enemy.Point = point; selectedKind = Kind.Enemy; selectedElementId = enemy.InstanceId;
                    }, true);
                    return;
                case Tool.TriggerArea:
                    if (SelectedEncounter == null) { status.text = "Сначала выберите или добавьте столкновение."; return; }
                    Undo.RecordObject(database, "Зона угрозы");
                    dragStart = pixel; dragging = true;
                    return;
                case Tool.ArenaFrame:
                    LocalEncounterDefinition framed = SelectedEncounter;
                    if (framed == null) { status.text = "Сначала выберите или добавьте столкновение."; return; }
                    Change(() => { framed.HasArenaCenter = true; framed.ArenaCenter = point; }, true);
                    return;
                case Tool.RetreatPoint:
                    LocalEncounterDefinition retreating = SelectedEncounter;
                    if (retreating == null) { status.text = "Сначала выберите или добавьте столкновение."; return; }
                    Change(() => retreating.RetreatPoint = point, true);
                    return;
                case Tool.TestStart:
                    if (Visual == null) return;
                    if (geometry != null && !geometry.IsPassable(pixel.x, pixel.y)) { status.text = "Точка должна быть проходимой."; return; }
                    Change(() => Visual.TestStartPoint = LocationVisualGeometry.ToNormalized(location, pixel));
                    status.text = "Точка старта поставлена.";
                    return;
                case Tool.PlaceAsset:
                    if (string.IsNullOrEmpty(placingAssetId)) { SetTool(Tool.Select); return; }
                    AddAssetInstance(placingAssetId, pixel);
                    return;
                case Tool.Pivot:
                    LocationVisualObject art = ArtObject;
                    if (art != null && art.UsesAsset)
                    {
                        status.text = "Опора ассета задаётся в Базе ассетов (у каждого ракурса своя).";
                        tool = Tool.Select;
                        return;
                    }
                    SpriteRenderer image = art != null ? renderer.FindObject(art.Id) : null;
                    if (image == null) return;
                    Vector2 world = LocationVisualGeometry.PixelToWorld(location, pixel);
                    Bounds bounds = image.bounds;
                    Vector2 pivot = new Vector2(Mathf.Clamp01((world.x - bounds.min.x) / bounds.size.x), Mathf.Clamp01((world.y - bounds.min.y) / bounds.size.y));
                    Change(() => { art.Pivot = pivot; art.Position = LocationVisualGeometry.ToNormalized(location, pixel); }, true);
                    tool = Tool.Select; status.text = "Точка опоры поставлена.";
                    return;
            }

            // Выбор: метки места ближе всего, затем предметы рисунка.
            float pick = 12 * ViewHeight / frame.height;
            (Kind kind, string id) hit = PickMarker(location, pixel, pick);
            if (hit.kind == Kind.None && Visual != null)
            {
                Vector2 world = LocationVisualGeometry.PixelToWorld(location, pixel);
                // Составной ассет выбирается целиком: по любой своей части.
                string artId = Visual.Objects.Where(item => !item.Hidden && renderer.ObjectImages(item.Id)
                        .Any(part => part != null && part.bounds.Contains(new Vector3(world.x, world.y, 0))))
                    .OrderByDescending(item => renderer.ObjectSortingOrder(item.Id)).FirstOrDefault()?.Id;
                if (artId != null) hit = (Kind.Art, artId);
            }
            if (hit.kind == Kind.None && showArena)
            {
                LocalEncounterDefinition encounter = location.Encounters.FirstOrDefault(item => item.TriggerArea.Contains(pixel.x, pixel.y));
                if (encounter != null) hit = (Kind.Encounter, encounter.Id);
            }
            Select(hit.kind, hit.id);
            bool locked = hit.kind == Kind.Art && ArtObject != null && ArtObject.Locked;
            dragging = hit.kind != Kind.None && !locked;
            if (dragging) { Undo.RecordObject(database, "Переместить"); lastPointer = pixel; }
        }

        private (Kind, string) PickMarker(LocalLocationDefinition location, Vector2 pixel, float radius)
        {
            (Kind, string) best = (Kind.None, null);
            float bestDistance = radius;
            void Try(Kind kind, string id, LocalPointData point)
            {
                if (point == null) return;
                float distance = (float)point.DistanceTo(pixel.x, pixel.y);
                if (distance <= bestDistance) { bestDistance = distance; best = (kind, id); }
            }
            if (!showMarkers) return best;
            location.Entrances.ForEach(item => Try(Kind.Entrance, item.Id, item.Point));
            location.Objects.ForEach(item => Try(Kind.GameObject, item.Id, item.Point));
            location.Enemies.ForEach(item => Try(Kind.Enemy, item.InstanceId, item.Point));
            if (Visual != null)
                foreach (LocationVisualObject item in Visual.Objects.Where(item => item.LightOnly && !item.Hidden))
                {
                    Vector2 point = LocationVisualGeometry.ToPixel(location, item.Position);
                    Try(Kind.Art, item.Id, new LocalPointData(point.x, point.y));
                }
            return best;
        }

        private void MouseDrag(LocalLocationDefinition location, Vector2 pixel)
        {
            if (tool == Tool.Terrain) { Paint(pixel); return; }
            if (tool == Tool.TriggerArea)
            {
                LocalEncounterDefinition encounter = SelectedEncounter;
                if (encounter == null) return;
                Rect rect = Rect.MinMaxRect(Mathf.Min(dragStart.x, pixel.x), Mathf.Min(dragStart.y, pixel.y), Mathf.Max(dragStart.x, pixel.x), Mathf.Max(dragStart.y, pixel.y));
                encounter.TriggerArea = new LocalAreaData(rect.x, rect.y, rect.width, rect.height);
                EditorUtility.SetDirty(database);
                return;
            }
            Vector2 delta = pixel - lastPointer;
            lastPointer = pixel;
            void Move(LocalPointData point) { if (point != null) { point.X += delta.x; point.Y += delta.y; } }
            switch (selectedKind)
            {
                case Kind.Entrance: Move(SelectedEntrance?.Point); break;
                case Kind.GameObject: Move(SelectedGameObject?.Point); break;
                case Kind.Enemy: Move(SelectedEnemy?.Point); break;
                case Kind.Encounter:
                    LocalEncounterDefinition encounter = SelectedEncounter;
                    if (encounter != null) { encounter.TriggerArea.X += delta.x; encounter.TriggerArea.Y += delta.y; }
                    break;
                case Kind.Art:
                    LocationVisualObject selected = ArtObject;
                    if (selected == null) break;
                    Vector2 normalized = new Vector2(delta.x / CanvasSize.x, delta.y / CanvasSize.y);
                    foreach (LocationVisualObject item in Visual.Objects)
                    {
                        if (!item.Locked && (item == selected || (!string.IsNullOrEmpty(selected.GroupId) && item.GroupId == selected.GroupId)))
                        {
                            item.Position += normalized;
                            renderer.MoveObject(item.Id, item.Position);
                        }
                    }
                    break;
            }
            EditorUtility.SetDirty(database);
        }

        private void MouseUp(LocalLocationDefinition location, Vector2 pixel)
        {
            dragging = false;
            if (tool == Tool.Terrain && paintLayer != null)
            {
                location.TerrainCells = paintLayer.Encode();
                paintLayer = null;
            }
            EditorUtility.SetDirty(database);
            // Отпустили — пересчитать проходимость и записать.
            RebuildPreview();
            BuildSettings();
            AssetDatabase.SaveAssetIfDirty(database);
        }

        private void Paint(Vector2 pixel)
        {
            if (paintLayer == null) return;
            WorldMapHexGrid grid = paintLayer.Grid;
            for (int index = 0; index < grid.CellCount; index++)
            {
                WorldMapHexCell cell = grid.CellAt(index);
                grid.CellCenter(cell, out double x, out double y);
                if ((x - pixel.x) * (x - pixel.x) + (y - pixel.y) * (y - pixel.y) <= brushRadius * brushRadius)
                    paintLayer.Set(cell, brushTerrain);
            }
            RebuildTerrainOverlay();
        }

        // ------------------------------------------------------------------
        // Предметы рисунка, проверка, запуск, места
        // ------------------------------------------------------------------

        private void AddArtObject(Sprite sprite, LocationPlaceholder placeholder, Vector2? pixel = null)
        {
            if (Visual == null) return;
            LocalLocationDefinition location = Location;
            Change(() =>
            {
                LocationVisualObject item = new LocationVisualObject { Sprite = sprite, Placeholder = placeholder,
                    Name = sprite != null ? sprite.name : placeholder == LocationPlaceholder.Fire ? "Костёр" : "Палатка",
                    Position = LocationVisualGeometry.ToNormalized(location, pixel ?? viewCenter),
                    Height = placeholder == LocationPlaceholder.Fire ? .8f : 1.8f,
                    // В костёр и в палатку не встают.
                    BlocksMovement = placeholder == LocationPlaceholder.Tent || placeholder == LocationPlaceholder.Fire,
                    CastsShadow = placeholder == LocationPlaceholder.Tent };
                if (placeholder == LocationPlaceholder.Fire) { item.Light.Enabled = true; item.Light.Wander = .05f; }
                Visual.Objects.Add(item); selectedKind = Kind.Art; selectedElementId = item.Id;
            }, true);
        }

        private void ImportPng()
        {
            string path = EditorUtility.OpenFilePanel("Загрузить PNG локации", "", "png");
            if (string.IsNullOrEmpty(path)) return;
            Sprite sprite = ImportSprite(path);
            if (sprite != null) AddArtObject(sprite, LocationPlaceholder.None);
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

        private List<string> CollectErrors()
        {
            DialogueDatabaseAsset dialogues = DialogueDatabaseRuntime.LoadDefaultDatabase();
            UnitDatabaseAsset units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
            List<string> errors = LocalLocationValidator.Validate(Location, fields,
                id => dialogues != null && dialogues.FindDialogue(id) != null,
                id => units != null && units.FindById(id) != null,
                LocationVisualGeometry.BlockedAreas(Visual, Location));
            if (Visual != null) errors.AddRange(LocationVisualGeometry.Validate(Location, Visual, Field));
            return errors;
        }

        private void Validate()
        {
            List<string> errors = CollectErrors();
            status.text = errors.Count == 0 ? "Проверка пройдена." : string.Join(" · ", errors);
            if (errors.Count > 0) EditorUtility.DisplayDialog("Проверка локации", string.Join("\n", errors), "Понятно");
        }

        private void Launch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (Visual == null) { status.text = "Для запуска нужна художественная сборка (кнопка справа)."; return; }
            List<string> errors = LocationVisualGeometry.Validate(Location, Visual, Field);
            if (errors.Count > 0) { Validate(); return; }
            AssetDatabase.SaveAssetIfDirty(database);
            if (fields != null) AssetDatabase.SaveAssetIfDirty(fields);
            ReleasePreview();
            LocationLightingTestBootstrap.Launch(selectedId, hour);
        }

        private void AddLocation()
        {
            string sourceFieldId = Field?.Id ?? LocationLightingTestBootstrap.FieldId;
            Change(() =>
            {
                selectedId = "location_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                database.locations.Add(new LocalLocationDefinition
                {
                    Id = selectedId, DisplayName = "Новая локация", WorldLocationId = "__technical", BattlefieldId = sourceFieldId,
                    Entrances = new List<LocalEntranceDefinition> { new LocalEntranceDefinition { Id = "entry", Label = "Вход", Point = new LocalPointData(960, 540) } }
                });
                database.visuals.Add(new LocationVisualDefinition { LocationId = selectedId, TechnicalTest = true });
                RefreshList();
            }, true);
        }

        private void DuplicateLocation()
        {
            if (Location == null) return;
            Change(() =>
            {
                LocalLocationDefinition location = JsonUtility.FromJson<LocalLocationDefinition>(JsonUtility.ToJson(Location));
                LocationVisualDefinition visual = Visual != null ? JsonUtility.FromJson<LocationVisualDefinition>(JsonUtility.ToJson(Visual)) : null;
                selectedId = "location_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                location.Id = selectedId; location.DisplayName += " · копия"; location.WorldLocationId = "__technical";
                database.locations.Add(location);
                if (visual != null) { visual.LocationId = selectedId; visual.TechnicalTest = true; database.visuals.Add(visual); }
                RefreshList();
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
                selectedId = LocationLightingTestBootstrap.CampId; selectedKind = Kind.None; RefreshList();
            }, true);
        }

        // Свойство связанного поля Базы полей боя (общее для боя и всех мест с ним).
        private SerializedProperty FieldProperty(SerializedObject serialized, string name)
        {
            int index = fields.Battlefields.ToList().FindIndex(item => item != null && item.Id == Location?.BattlefieldId);
            if (index < 0) return null;
            return serialized.FindProperty("battlefields").GetArrayElementAtIndex(index).FindPropertyRelative(name);
        }

        private void ApplyField(string undoName, Action<SerializedProperty> apply, string name)
        {
            if (Field == null) return;
            Undo.RecordObject(fields, undoName);
            SerializedObject serialized = new SerializedObject(fields);
            SerializedProperty property = FieldProperty(serialized, name);
            if (property == null) return;
            apply(property);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(fields);
            fieldsSaveAt = EditorApplication.timeSinceStartup + .5;
            rebuildRequested = true;
        }

        private void FieldNumber(string label, string name, float value, float min, float max)
        {
            Slider field = new Slider(label, min, max) { value = value, showInputField = true,
                tooltip = "Настройка поля в Базе полей боя: меняет и бой, и все места с этим полем." };
            field.RegisterValueChangedCallback(evt => ApplyField("Изменить поле", property => property.floatValue = evt.newValue, name));
            settings.Add(field);
        }

        private void FieldVector(string label, string name, Vector2 value)
        {
            Vector2Field field = new Vector2Field(label) { value = value,
                tooltip = "Настройка поля в Базе полей боя: меняет и бой, и все места с этим полем." };
            field.RegisterValueChangedCallback(evt => ApplyField("Изменить поле", property => property.vector2Value = evt.newValue, name));
            settings.Add(field);
        }
    }
}
