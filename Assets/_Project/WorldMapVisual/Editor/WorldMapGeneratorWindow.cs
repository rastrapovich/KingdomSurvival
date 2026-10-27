using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // Канон v1.58 §9.9: окно «Генератор карты». Устроено как «Обработка
    // кадра»: слева — блоки настроек со «Сбросить» и сохранённые наборы,
    // справа — большой предпросмотр с масштабом, сдвигом, пошаговым показом
    // стадий генерации и наложениями (разметка клеток, слоты появления).
    // Слева сверху — что генерировать (масштаб, рельеф, вода, лес, деревни,
    // дороги, приметные места), ниже — как выглядит (подложка-узор на всю
    // карту, местность, тушь, вода, лес, горы, дороги, деревни, знаки,
    // обработка). Правки идут с Undo; предпросмотр обновляется сам.
    public sealed class WorldMapGeneratorWindow : EditorWindow
    {
        private const string PrefsKey = "KS.WorldMapGenerator.";

        private WorldMapDatabaseAsset database;
        private WorldMapGeneratorAsset generator;

        private ScrollView settingsScroll;
        private VisualElement parent;
        private VisualElement viewport;
        private VisualElement canvas;
        private Image mapImage;
        private Image markupImage;
        private WorldMapLayersView layersView;
        private WorldMapLayers layers;
        private VisualElement overlay;
        private Label statusLabel;
        private Label stageLabel;
        private SliderInt stageSlider;
        private IntegerField seedField;
        private Button playButton;

        private Texture2D mapTexture;
        private Texture2D markupTexture;
        private GeneratedWorldMap currentMap;
        private IVisualElementScheduledItem pendingRefresh;
        private IVisualElementScheduledItem playItem;
        private float zoom = 1f;
        private Vector2 pan;
        private bool panning;
        private Vector2 panStart;
        private Vector2 panOrigin;

        private static int Seed
        {
            get => EditorPrefs.GetInt(PrefsKey + "Seed", 1);
            set => EditorPrefs.SetInt(PrefsKey + "Seed", value);
        }

        private static int Stage
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(PrefsKey + "Stage", (int)WorldMapGenerationStage.Complete), 0, (int)WorldMapGenerationStage.Complete);
            set => EditorPrefs.SetInt(PrefsKey + "Stage", value);
        }

        // 0 — черновик, 1 — обычно, 2 — как в игре.
        private static int Quality
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(PrefsKey + "Quality", 1), 0, 2);
            set => EditorPrefs.SetInt(PrefsKey + "Quality", value);
        }

        private static bool ShowMarkup
        {
            get => EditorPrefs.GetBool(PrefsKey + "Markup", false);
            set => EditorPrefs.SetBool(PrefsKey + "Markup", value);
        }

        private static bool ShowSlots
        {
            get => EditorPrefs.GetBool(PrefsKey + "Slots", false);
            set => EditorPrefs.SetBool(PrefsKey + "Slots", value);
        }

        private static bool ShowHidden
        {
            get => EditorPrefs.GetBool(PrefsKey + "Hidden", true);
            set => EditorPrefs.SetBool(PrefsKey + "Hidden", value);
        }

        private static bool ShowSites
        {
            get => EditorPrefs.GetBool(PrefsKey + "Sites", false);
            set => EditorPrefs.SetBool(PrefsKey + "Sites", value);
        }

        private static bool AutoRefresh
        {
            get => EditorPrefs.GetBool(PrefsKey + "Auto", true);
            set => EditorPrefs.SetBool(PrefsKey + "Auto", value);
        }

        private static string SelectedPreset
        {
            get => EditorPrefs.GetString(PrefsKey + "Preset", "");
            set => EditorPrefs.SetString(PrefsKey + "Preset", value ?? "");
        }

        [MenuItem("Kingdom Survival/Генератор карты")]
        public static void Open()
        {
            WorldMapGeneratorWindow window = GetWindow<WorldMapGeneratorWindow>();
            window.titleContent = new GUIContent("Генератор карты");
            window.minSize = new Vector2(900f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            WorldMapInkEditorWindow.Changed += OnInkChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            WorldMapInkEditorWindow.Changed -= OnInkChanged;
            DestroyTexture(ref mapTexture);
            DestroyTexture(ref markupTexture);
            layers?.Destroy();
            layers = null;
        }

        // Правка в «Редакторе туши» — перерисовать карту (без новой генерации).
        private void OnInkChanged()
        {
            if (generator != null && AutoRefresh)
                ScheduleRefresh(false);
        }

        private void OnUndoRedo()
        {
            if (generator == null)
                return;
            RebuildSettings();
            ScheduleRefresh(true);
        }

        private void CreateGUI()
        {
            database = Resources.Load<WorldMapDatabaseAsset>(WorldMapDatabaseAsset.ResourcesPath);
            generator = database != null ? database.Generator : null;
            OfferPoolRepair(false);

            VisualElement root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Row;

            VisualElement left = new VisualElement();
            left.style.width = 400;
            left.style.minWidth = 320;
            left.style.borderRightWidth = 1;
            left.style.borderRightColor = new Color(0f, 0f, 0f, 0.4f);
            settingsScroll = new ScrollView(ScrollViewMode.Vertical);
            settingsScroll.style.flexGrow = 1;
            left.Add(settingsScroll);
            root.Add(left);

            VisualElement right = new VisualElement();
            right.style.flexGrow = 1;
            right.Add(BuildToolbar());
            right.Add(BuildViewport());
            statusLabel = new Label();
            statusLabel.style.paddingLeft = 6;
            statusLabel.style.paddingTop = 2;
            statusLabel.style.paddingBottom = 4;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            right.Add(statusLabel);
            root.Add(right);

            RebuildSettings();
            ScheduleRefresh(true);
        }

        // ------------------------------------------------------------------
        // Предпросмотр
        // ------------------------------------------------------------------

        private VisualElement BuildToolbar()
        {
            VisualElement bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Column;
            bar.style.paddingLeft = bar.style.paddingRight = 6;
            bar.style.paddingTop = 4;

            VisualElement row = Row(true);
            seedField = new IntegerField("Seed") { value = Seed, tooltip = "Число, из которого строится карта. Одинаковый seed — одинаковая карта." };
            seedField.labelElement.style.minWidth = 40;
            seedField.style.width = 170;
            seedField.RegisterValueChangedCallback(evt => { Seed = evt.newValue; ScheduleRefresh(true); });
            row.Add(seedField);
            row.Add(new Button(() => { seedField.value = UnityEngine.Random.Range(1, 999999); }) { text = "🎲 Другой seed", tooltip = "Новая случайная карта с теми же настройками" });
            row.Add(new Button(() => ScheduleRefresh(true, 0)) { text = "Сгенерировать" });
            PopupField<string> quality = new PopupField<string>(new List<string> { "Черновик (25 %)", "Обычно (60 %)", "Как в игре" }, Quality) { tooltip = "Разрешение предпросмотра. Мелкие знаки видно честно только «Как в игре» — разрешение из блока «Качество»." };
            quality.style.width = 140;
            quality.RegisterValueChangedCallback(evt => { Quality = quality.index; ScheduleRefresh(false); });
            row.Add(quality);
            UnityEngine.UIElements.Toggle auto = new UnityEngine.UIElements.Toggle("Сразу") { value = AutoRefresh, tooltip = "Обновлять предпросмотр после каждой правки" };
            auto.labelElement.style.minWidth = 0;
            auto.RegisterValueChangedCallback(evt => AutoRefresh = evt.newValue);
            row.Add(auto);
            row.Add(new Button(SavePng) { text = "Сохранить PNG…", tooltip = "Рисунок этой карты в разрешении «как в игре»" });
            bar.Add(row);

            VisualElement stageRow = Row(false);
            stageSlider = new SliderInt("Стадия", 0, (int)WorldMapGenerationStage.Complete) { value = Stage, tooltip = "Показать карту, собранную до этой стадии" };
            stageSlider.labelElement.style.minWidth = 50;
            stageSlider.style.flexGrow = 1;
            stageSlider.RegisterValueChangedCallback(evt => { Stage = evt.newValue; UpdateStageLabel(); ScheduleRefresh(true, 10); });
            stageRow.Add(stageSlider);
            stageLabel = new Label { style = { width = 130, unityTextAlign = TextAnchor.MiddleLeft, unityFontStyleAndWeight = FontStyle.Bold } };
            stageRow.Add(stageLabel);
            playButton = new Button(TogglePlay) { text = "▶ По шагам", tooltip = "Показать, как собирается карта: стадия за стадией" };
            stageRow.Add(playButton);
            bar.Add(stageRow);
            UpdateStageLabel();

            VisualElement overlays = Row(true);
            overlays.Add(OverlayToggle("Разметка клеток", ShowMarkup, value => { ShowMarkup = value; RefreshOverlays(); }, "Местность клеток так, как её видит игра (цвета — из настроек перемещения)"));
            overlays.Add(OverlayToggle("Слоты мест", ShowSlots, value => { ShowSlots = value; overlay.MarkDirtyRepaint(); }, "Где могут появиться места из Базы карты"));
            overlays.Add(OverlayToggle("Подписи", ShowSites, value => { ShowSites = value; overlay.MarkDirtyRepaint(); }, "Точки деревень, мест и старта"));
            overlays.Add(OverlayToggle("Скрытые места", ShowHidden, value => { ShowHidden = value; ScheduleRefresh(false, 0); }, "Рисовать и места, которые в игре не видны, пока их не найдут"));
            overlays.Add(new Button(() => { zoom = 1f; pan = Vector2.zero; ApplyView(); }) { text = "Вписать" });
            Label hint = new Label("Колесо — масштаб, перетаскивание — сдвиг") { style = { fontSize = 10, color = new Color(0.7f, 0.7f, 0.65f), unityTextAlign = TextAnchor.MiddleLeft, marginLeft = 6 } };
            overlays.Add(hint);
            bar.Add(overlays);
            return bar;
        }

        private static UnityEngine.UIElements.Toggle OverlayToggle(string label, bool value, Action<bool> set, string tooltip)
        {
            UnityEngine.UIElements.Toggle toggle = new UnityEngine.UIElements.Toggle(label) { value = value, tooltip = tooltip };
            toggle.labelElement.style.minWidth = 0;
            toggle.style.marginRight = 10;
            toggle.RegisterValueChangedCallback(evt => set(evt.newValue));
            return toggle;
        }

        private VisualElement BuildViewport()
        {
            viewport = new VisualElement();
            viewport.style.flexGrow = 1;
            viewport.style.overflow = Overflow.Hidden;
            viewport.style.backgroundColor = new Color(0.05f, 0.05f, 0.05f);
            viewport.style.marginTop = 4;

            canvas = new VisualElement { pickingMode = PickingMode.Ignore };
            canvas.style.position = Position.Absolute;
            mapImage = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            Stretch(mapImage);
            markupImage = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            Stretch(markupImage);
            markupImage.style.opacity = 0.55f;
            overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            Stretch(overlay);
            overlay.generateVisualContent += DrawOverlay;
            canvas.Add(mapImage);
            layersView = new WorldMapLayersView();
            canvas.Add(layersView);
            canvas.Add(markupImage);
            canvas.Add(overlay);
            viewport.Add(canvas);

            viewport.RegisterCallback<GeometryChangedEvent>(_ => ApplyView());
            viewport.RegisterCallback<WheelEvent>(OnWheel);
            viewport.RegisterCallback<PointerDownEvent>(evt =>
            {
                panning = true;
                panStart = evt.localPosition;
                panOrigin = pan;
                viewport.CapturePointer(evt.pointerId);
            });
            viewport.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!panning)
                    return;
                pan = panOrigin + ((Vector2)evt.localPosition - panStart);
                ApplyView();
            });
            viewport.RegisterCallback<PointerUpEvent>(evt =>
            {
                panning = false;
                viewport.ReleasePointer(evt.pointerId);
            });
            return viewport;
        }

        private static void Stretch(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = element.style.top = element.style.right = element.style.bottom = 0;
        }

        private Rect FitRect()
        {
            float aspect = currentMap != null ? currentMap.CanvasWidth / currentMap.CanvasHeight
                : generator != null ? generator.Settings.CanvasWidth / Mathf.Max(1f, generator.Settings.CanvasHeight) : 1.625f;
            Rect area = viewport.contentRect;
            if (area.width <= 1f || area.height <= 1f)
                return new Rect(0, 0, 1, 1);
            float w = area.width, h = w / aspect;
            if (h > area.height)
            {
                h = area.height;
                w = h * aspect;
            }
            return new Rect((area.width - w) * 0.5f, (area.height - h) * 0.5f, w, h);
        }

        private void ApplyView()
        {
            if (viewport == null)
                return;
            Rect fit = FitRect();
            canvas.style.left = fit.x + pan.x + fit.width * (1f - zoom) * 0.5f;
            canvas.style.top = fit.y + pan.y + fit.height * (1f - zoom) * 0.5f;
            canvas.style.width = fit.width * zoom;
            canvas.style.height = fit.height * zoom;
            overlay.MarkDirtyRepaint();
            foreach (VisualElement child in layersView?.Children() ?? Enumerable.Empty<VisualElement>())
                child.MarkDirtyRepaint();
        }

        private void OnWheel(WheelEvent evt)
        {
            float old = zoom;
            zoom = Mathf.Clamp(zoom * Mathf.Pow(1.12f, -evt.delta.y), 0.3f, 12f);
            // Точка под курсором остаётся на месте.
            Rect fit = FitRect();
            Vector2 center = new Vector2(fit.x + fit.width * 0.5f, fit.y + fit.height * 0.5f) + pan;
            Vector2 mouse = evt.localMousePosition;
            pan += (mouse - center) * (1f - zoom / old);
            ApplyView();
            evt.StopPropagation();
        }

        private void DrawOverlay(MeshGenerationContext context)
        {
            if (currentMap == null)
                return;
            Rect rect = overlay.contentRect;
            Painter2D painter = context.painter2D;
            Vector2 ToLocal(float x, float y) => new Vector2(x / 100f * rect.width, y / 100f * rect.height);

            if (ShowSlots && currentMap.SpawnSlots != null)
            {
                foreach (GeneratedSpawnSlot slot in currentMap.SpawnSlots)
                {
                    Vector2 min = ToLocal(slot.MinXPercent, slot.MinYPercent);
                    Vector2 max = ToLocal(slot.MaxXPercent, slot.MaxYPercent);
                    bool place = slot.Tags.Any(tag => tag.StartsWith(WorldMapLandmarkRule.PlaceTagPrefix));
                    painter.fillColor = place ? new Color(1f, 0.5f, 0.2f, 0.35f) : new Color(0.3f, 0.6f, 1f, 0.22f);
                    painter.strokeColor = place ? new Color(1f, 0.6f, 0.3f, 0.95f) : new Color(0.45f, 0.7f, 1f, 0.9f);
                    painter.lineWidth = 1f;
                    painter.BeginPath();
                    painter.MoveTo(min);
                    painter.LineTo(new Vector2(max.x, min.y));
                    painter.LineTo(max);
                    painter.LineTo(new Vector2(min.x, max.y));
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                }
            }

            if (ShowSites)
            {
                void Dot(float x, float y, Color color, float radius)
                {
                    painter.fillColor = color;
                    painter.strokeColor = Color.black;
                    painter.lineWidth = 1.5f;
                    painter.BeginPath();
                    painter.Arc(ToLocal(x, y), radius, 0f, 360f);
                    painter.Fill();
                    painter.Stroke();
                }
                foreach (GeneratedMapSite village in currentMap.Villages)
                    Dot(village.XPercent, village.YPercent, village.IsStart ? new Color(1f, 0.85f, 0.2f) : new Color(0.95f, 0.95f, 0.9f), village.IsStart ? 7f : 5f);
                foreach (GeneratedMapSite landmark in currentMap.Landmarks)
                    Dot(landmark.XPercent, landmark.YPercent, new Color(1f, 0.45f, 0.25f), 5f);
            }
        }

        private void UpdateStageLabel()
        {
            if (stageLabel != null)
                stageLabel.text = (Stage + 1) + ". " + WorldMapGenerationStages.Title((WorldMapGenerationStage)Stage);
        }

        private void TogglePlay()
        {
            if (playItem != null)
            {
                StopPlay();
                return;
            }
            stageSlider.value = 0;
            playButton.text = "■ Стоп";
            playItem = rootVisualElement.schedule.Execute(() =>
            {
                if (stageSlider.value >= (int)WorldMapGenerationStage.Complete)
                {
                    StopPlay();
                    return;
                }
                stageSlider.value++;
            }).Every(900).StartingIn(900);
        }

        private void StopPlay()
        {
            playItem?.Pause();
            playItem = null;
            if (playButton != null)
                playButton.text = "▶ По шагам";
        }

        private void ScheduleRefresh(bool regenerate, long delayMs = 250)
        {
            if (generator == null || rootVisualElement == null)
            {
                ShowStatus();
                return;
            }
            pendingRefresh?.Pause();
            pendingRefresh = rootVisualElement.schedule.Execute(() => Refresh(regenerate)).StartingIn(delayMs);
        }

        private void Refresh(bool regenerate)
        {
            if (generator == null)
                return;
            Stopwatch watch = Stopwatch.StartNew();
            if (regenerate || currentMap == null)
                currentMap = generator.Generate(Seed, (WorldMapGenerationStage)Stage);
            double generateMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            float resolution = Quality == 0 ? Mathf.Min(0.25f, generator.Art.Resolution)
                : Quality == 1 ? Mathf.Min(0.6f, generator.Art.Resolution)
                : generator.Art.Resolution;
            DestroyTexture(ref mapTexture);
            layers?.Destroy();
            mapTexture = WorldMapPainter.Paint(currentMap, generator.Art, resolution, ShowHidden, out layers);
            layersView.Layers = layers;
            double paintMs = watch.Elapsed.TotalMilliseconds;
            mapImage.image = mapTexture;
            RefreshOverlays();
            ApplyView();
            ShowStatus(generateMs, paintMs);
        }

        private void RefreshOverlays()
        {
            DestroyTexture(ref markupTexture);
            if (ShowMarkup && currentMap != null)
                markupTexture = BuildMarkupTexture(currentMap);
            markupImage.image = markupTexture;
            overlay.MarkDirtyRepaint();
        }

        private static Texture2D BuildMarkupTexture(GeneratedWorldMap map)
        {
            WorldMapMovementSettingsAsset movement = WorldMapVisualRuntime.LoadMovementSettings();
            WorldMapTerrainLayer layer = map.CreateTerrainLayer();
            WorldMapHexGrid grid = layer.Grid;
            int width = 640;
            int height = Mathf.Max(16, Mathf.RoundToInt(width * map.CanvasHeight / map.CanvasWidth));
            Color32[] colors = new Color32[16];
            foreach (WorldMapGameplayTerrainType type in WorldMapTerrainLabels.All)
                colors[(int)type] = movement != null ? movement.GetTerrainColor(type) : WorldMapMovementSettingsAsset.DefaultColor(type);
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double cx = (x + 0.5) / width * map.CanvasWidth;
                    double cy = (y + 0.5) / height * map.CanvasHeight;
                    pixels[(height - 1 - y) * width + x] = colors[(int)layer.GetAtPixel(cx, cy)];
                }
            }
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private void ShowStatus(double generateMs = 0, double paintMs = 0)
        {
            if (statusLabel == null)
                return;
            if (generator == null)
            {
                statusLabel.text = "Генератор не подключён к базе карты — нажмите «Создать генератор» слева.";
                return;
            }
            if (currentMap == null)
            {
                statusLabel.text = string.Empty;
                return;
            }
            WorldMapHexGrid grid = currentMap.CreateGrid();
            statusLabel.text =
                $"{grid.Columns}×{grid.Rows} клеток · рек {currentMap.Rivers.Count} · дорог и троп {currentMap.Roads.Count} · " +
                $"мостов и бродов {currentMap.Bridges.Count} · деревень {currentMap.Villages.Count} · мест {currentMap.Landmarks.Count} · " +
                $"слотов {currentMap.SpawnSlots.Count} · рисунок {(mapTexture != null ? mapTexture.width + "×" + mapTexture.height + " (" + Mathf.RoundToInt(100f * mapTexture.width / currentMap.CanvasWidth) + " % полотна)" : "—")} · " +
                $"генерация {generateMs:0} мс, рисунок {paintMs:0} мс";
        }

        private void SavePng()
        {
            if (generator == null)
                return;
            string path = EditorUtility.SaveFilePanel("Сохранить рисунок карты", "", "generated-map-" + Seed + ".png", "png");
            if (string.IsNullOrEmpty(path))
                return;
            GeneratedWorldMap map = generator.Generate(Seed, (WorldMapGenerationStage)Stage);
            Texture2D texture = WorldMapPainter.Paint(map, generator.Art);
            WorldMapGeneratorTools.SavePng(texture, path);
            DestroyImmediate(texture);
            EditorUtility.RevealInFinder(path);
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null)
                DestroyImmediate(texture);
            texture = null;
        }

        // ------------------------------------------------------------------
        // Настройки
        // ------------------------------------------------------------------

        private WorldMapGenerationSettings S => generator.Settings;
        private WorldMapArtStyle A => generator.Art;

        // Правка: Undo, запись, обновление предпросмотра (и, если нужно,
        // перестройка панели — когда меняется состав списков).
        private void Change(Action action, bool regenerate, bool rebuild = false)
        {
            if (generator == null)
                return;
            Undo.RecordObject(generator, "Генератор карты");
            action();
            EditorUtility.SetDirty(generator);
            if (rebuild)
                RebuildSettings();
            if (AutoRefresh)
                ScheduleRefresh(regenerate);
        }

        private void RebuildSettings()
        {
            if (settingsScroll == null)
                return;
            float scroll = settingsScroll.scrollOffset.y;
            settingsScroll.Clear();
            parent = settingsScroll.contentContainer;
            parent.style.paddingLeft = parent.style.paddingRight = 6;
            parent.style.paddingBottom = 12;

            Label title = new Label("Генератор карты «Свободной игры»") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13, marginTop = 6, marginBottom = 2 } };
            parent.Add(title);

            if (database == null)
            {
                Help("База карты не найдена (Resources/" + WorldMapDatabaseAsset.ResourcesPath + ").");
                return;
            }
            if (generator == null)
            {
                Help("У базы карты ещё нет генератора. Создайте его — настройки по умолчанию дадут рабочую карту, которую можно сразу посмотреть.");
                Buttons(("Создать генератор", () =>
                {
                    generator = WorldMapGeneratorTools.EnsureGeneratorAsset(database);
                    RebuildSettings();
                    ScheduleRefresh(true, 0);
                }, null));
                return;
            }

            Toggle("Карта свободной игры — генерируется", generator.UseForFreePlay, value => generator.UseForFreePlay = value, false,
                "Каждая новая партия «Свободной игры» строит свою карту по этим настройкам и своему seed. Выключено — авторский мир, как в кампании.");
            Help("Каждая новая партия свободной игры получает свою карту: seed партии + эти настройки. Карта сохраняется вместе с партией. " +
                 "Кампании кризисов идут по авторскому миру (окно «Карта»).");

            BuildPresets();

            Section("Что генерировать");
            BuildScale();
            BuildRelief();
            BuildWater();
            BuildVegetation();
            BuildVillages();
            BuildRoads();
            BuildLandmarks();

            Section("Как выглядит");
            BuildLayers();
            BuildQuality();
            BuildIcons();
            BuildBase();
            BuildTerrainArt();
            BuildInk();
            BuildWaterArt();
            BuildForestArt();
            BuildReliefArt();
            BuildRoadArt();
            BuildVillageArt();
            BuildLandmarkArt();
            BuildPost();

            settingsScroll.schedule.Execute(() => settingsScroll.scrollOffset = new Vector2(0f, scroll));
        }

        private void BuildPresets()
        {
            List<WorldMapGeneratorPreset> presets = generator.Presets;
            Foldout block = Block("Сохранённые наборы (" + presets.Count + ")", "Presets", true);
            VisualElement keep = parent;
            parent = block;
            WorldMapGeneratorPreset selected = generator.FindPreset(SelectedPreset);
            if (presets.Count == 0)
                Help("Наборов пока нет. Настройте карту и нажмите «Запомнить как новый» — потом его можно применить целиком, только генерацию или только облик.");
            else
            {
                List<string> names = presets.Select(preset => preset.Name + (string.IsNullOrEmpty(preset.Note) ? "" : " — " + preset.Note)).ToList();
                int index = Mathf.Max(0, presets.IndexOf(selected));
                PopupField<string> popup = new PopupField<string>("Набор", names, index);
                popup.RegisterValueChangedCallback(_ => { SelectedPreset = presets[popup.index].Id; RebuildSettings(); });
                parent.Add(popup);
                if (selected == null)
                {
                    selected = presets[index];
                    SelectedPreset = selected.Id;
                }
            }
            TextField name = new TextField("Имя") { value = selected != null ? selected.Name : "Новый набор" };
            TextField note = new TextField("Заметка") { value = selected != null ? selected.Note : "", tooltip = "«много воды», «северная тайга», «степь у гор»…" };
            parent.Add(name);
            parent.Add(note);
            Buttons(
                ("Запомнить как новый", () => Change(() =>
                {
                    WorldMapGeneratorPreset preset = new WorldMapGeneratorPreset { Name = Unique(name.value), Note = note.value ?? "", Settings = S.Clone(), Art = A.Clone() };
                    presets.Add(preset);
                    SelectedPreset = preset.Id;
                }, false, true), "Сохранить текущие настройки генерации и облика"),
                ("Перезаписать", selected == null ? null : (Action)(() => Change(() =>
                {
                    selected.Settings = S.Clone();
                    selected.Art = A.Clone();
                    selected.Name = string.IsNullOrWhiteSpace(name.value) ? selected.Name : name.value.Trim();
                    selected.Note = note.value ?? "";
                }, false, true)), null));
            Buttons(
                ("Применить всё", selected == null ? null : (Action)(() => Change(() => { generator.Settings = selected.Settings.Clone(); generator.Art = selected.Art.Clone(); }, true, true)), null),
                ("Только генерацию", selected == null ? null : (Action)(() => Change(() => generator.Settings = selected.Settings.Clone(), true, true)), "Что и где — без облика"),
                ("Только облик", selected == null ? null : (Action)(() => Change(() => generator.Art = selected.Art.Clone(), false, true)), "Как выглядит — без изменения карты"),
                ("Удалить", selected == null ? null : (Action)(() =>
                {
                    if (!EditorUtility.DisplayDialog("Удалить набор", "Удалить набор «" + selected.Name + "»?", "Удалить", "Отмена"))
                        return;
                    Change(() => { presets.Remove(selected); SelectedPreset = ""; }, false, true);
                }), null));
            Buttons(("Всё по умолчанию", () =>
            {
                if (EditorUtility.DisplayDialog("Сбросить генератор", "Вернуть все настройки генерации и облика к значениям по умолчанию? Сохранённые наборы останутся.", "Сбросить", "Отмена"))
                    Change(() => { generator.Settings = WorldMapGenerationSettings.CreateDefault(); generator.Art = WorldMapArtStyle.CreateDefault(); }, true, true);
            }, null));
            parent = keep;
        }

        private string Unique(string name)
        {
            string baseName = string.IsNullOrWhiteSpace(name) ? "Набор" : name.Trim();
            string result = baseName;
            int n = 2;
            while (generator.Presets.Any(preset => preset.Name == result))
                result = baseName + " " + n++;
            return result;
        }

        // ---- что генерировать ----

        private void BuildScale()
        {
            Group("Масштаб карты", "Scale", () =>
            {
                Help("Размер мира — числом клеток пути по ширине: больше клеток — больше мира и дольше дорога через него. Полотно задаёт пропорции и единицу координат.");
                IntSlider("Клеток по ширине", S.HexesAcross, 20, 300, v => S.HexesAcross = v, true);
                FloatField("Ширина полотна, px", S.CanvasWidth, v => S.CanvasWidth = v, true);
                FloatField("Высота полотна, px", S.CanvasHeight, v => S.CanvasHeight = v, true);
                Buttons(("16:10", () => Change(() => S.CanvasHeight = S.CanvasWidth * 10f / 16f, true, true), null),
                    ("4:3", () => Change(() => S.CanvasHeight = S.CanvasWidth * 3f / 4f, true, true), null),
                    ("1:1", () => Change(() => S.CanvasHeight = S.CanvasWidth, true, true), null),
                    ("Как в авторском мире", database.ActiveWorld == null ? null : (Action)(() => Change(() =>
                    {
                        S.CanvasWidth = database.ActiveWorld.MapCanvasWidth;
                        S.CanvasHeight = database.ActiveWorld.MapCanvasHeight;
                        S.HexesAcross = database.ActiveWorld.HexesAcross;
                    }, true, true)), null));
                Slider("Поляна у старта, клеток", S.StartClearRadiusHexes, 0, 6, v => S.StartClearRadiusHexes = Mathf.RoundToInt(v), true,
                    "Вокруг точки старта нет воды, гор и леса");
            }, () =>
            {
                WorldMapGenerationSettings d = WorldMapGenerationSettings.CreateDefault();
                S.HexesAcross = d.HexesAcross; S.CanvasWidth = d.CanvasWidth; S.CanvasHeight = d.CanvasHeight; S.StartClearRadiusHexes = d.StartClearRadiusHexes;
            });
        }

        private void BuildRelief()
        {
            Group("Рельеф", "Relief", () =>
            {
                Slider("Размер форм, клеток", S.ReliefScaleHexes, 4, 120, v => S.ReliefScaleHexes = v, true, "Крупные плавные формы или мелкая пестрота");
                IntSlider("Детали", S.ReliefDetail, 1, 7, v => S.ReliefDetail = v, true);
                Slider("Изрезанность", S.ReliefRoughness, 0.1f, 0.9f, v => S.ReliefRoughness = v, true);
                Slider("Хребты", S.MountainRidges, 0, 1, v => S.MountainRidges = v, true, "0 — округлые возвышенности, 1 — вытянутые гряды");
                Slider("Доля холмов", S.HillsShare, 0, 0.6f, v => S.HillsShare = v, true);
                Slider("Доля гор", S.MountainsShare, 0, 0.3f, v => S.MountainsShare = v, true);
                Slider("Скалы среди гор", S.CliffsShare, 0, 1, v => S.CliffsShare = v, true, "Самые крутые горы становятся непроходимыми скалами");
            }, () =>
            {
                WorldMapGenerationSettings d = WorldMapGenerationSettings.CreateDefault();
                S.ReliefScaleHexes = d.ReliefScaleHexes; S.ReliefDetail = d.ReliefDetail; S.ReliefRoughness = d.ReliefRoughness;
                S.MountainRidges = d.MountainRidges; S.HillsShare = d.HillsShare; S.MountainsShare = d.MountainsShare; S.CliffsShare = d.CliffsShare;
            });
        }

        private void BuildWater()
        {
            Group("Озёра и реки", "Water", () =>
            {
                Help("Реки начинаются на возвышенностях и стекают по рельефу к краю карты или в озеро, сливаясь друг с другом. Вода непроходима: переправа — только мостом или бродом там, где реку пересекает дорога или тропа.");
                Slider("Доля озёр", S.LakesShare, 0, 0.2f, v => S.LakesShare = v, true);
                IntSlider("Рек", S.RiverCount, 0, 16, v => S.RiverCount = v, true);
                IntSlider("Наименьшая длина, клеток", S.RiverMinLengthHexes, 2, 80, v => S.RiverMinLengthHexes = v, true);
                Slider("Ширина в устье, клеток", S.RiverWidthHexes, 0.1f, 2f, v => S.RiverWidthHexes = v, true);
                Slider("Извилистость", S.RiverMeander, 0, 1, v => S.RiverMeander = v, true);
            }, () =>
            {
                WorldMapGenerationSettings d = WorldMapGenerationSettings.CreateDefault();
                S.LakesShare = d.LakesShare; S.RiverCount = d.RiverCount; S.RiverMinLengthHexes = d.RiverMinLengthHexes;
                S.RiverWidthHexes = d.RiverWidthHexes; S.RiverMeander = d.RiverMeander;
            });
        }

        private void BuildVegetation()
        {
            Group("Лес и болота", "Vegetation", () =>
            {
                Slider("Доля леса", S.ForestShare, 0, 0.9f, v => S.ForestShare = v, true);
                Slider("Размер массивов, клеток", S.ForestScaleHexes, 3, 80, v => S.ForestScaleHexes = v, true);
                Slider("Лес на холмах", S.ForestOnHills, 0, 1, v => S.ForestOnHills = v, true);
                Slider("Доля болот", S.SwampShare, 0, 0.3f, v => S.SwampShare = v, true, "Сырые низины, чаще у воды");
                Slider("Размер пятен влажности", S.MoistureScaleHexes, 3, 80, v => S.MoistureScaleHexes = v, true);
            }, () =>
            {
                WorldMapGenerationSettings d = WorldMapGenerationSettings.CreateDefault();
                S.ForestShare = d.ForestShare; S.ForestScaleHexes = d.ForestScaleHexes; S.ForestOnHills = d.ForestOnHills;
                S.SwampShare = d.SwampShare; S.MoistureScaleHexes = d.MoistureScaleHexes;
            });
        }

        private void BuildVillages()
        {
            Group("Деревни и поля", "Villages", () =>
            {
                Help("Первая деревня — точка старта отряда. Остальные ставятся на ровной земле, охотнее у воды, не ближе заданного расстояния.");
                IntSlider("Деревень (со стартовой)", S.VillageCount, 1, 40, v => S.VillageCount = v, true);
                IntSlider("Наименьшее расстояние, клеток", S.VillageMinDistanceHexes, 2, 40, v => S.VillageMinDistanceHexes = v, true);
                Slider("Тяга к воде", S.VillageNearWater, 0, 2, v => S.VillageNearWater = v, true);
                Slider("Поля вокруг, клеток", S.FieldRadiusHexes, 0, 8, v => S.FieldRadiusHexes = v, true);
                Slider("Густота полей", S.FieldDensity, 0, 1, v => S.FieldDensity = v, true);
            }, () =>
            {
                WorldMapGenerationSettings d = WorldMapGenerationSettings.CreateDefault();
                S.VillageCount = d.VillageCount; S.VillageMinDistanceHexes = d.VillageMinDistanceHexes; S.VillageNearWater = d.VillageNearWater;
                S.FieldRadiusHexes = d.FieldRadiusHexes; S.FieldDensity = d.FieldDensity;
            });
        }

        private void BuildRoads()
        {
            Group("Дороги и тропы", "Roads", () =>
            {
                Help("Дороги связывают все деревни кратчайшей сетью, затем добавляются петли и выходы к краю карты. Путь выбирается по цене местности: холмы, лес и болото дороже, река — мост или брод. К приметным местам ведут тропы.");
                Slider("Петли", S.ExtraRoadLoops, 0, 1.5f, v => S.ExtraRoadLoops = v, true, "Лишние дороги между соседними деревнями — несколько путей вместо одного");
                IntSlider("Выходов к краю", S.RoadsToEdge, 0, 8, v => S.RoadsToEdge = v, true);
                Toggle("Тропы к приметным местам", S.TrailsToLandmarks, v => S.TrailsToLandmarks = v, true);
                Slider("Петляние", S.RoadWander, 0, 4, v => S.RoadWander = v, true, "0 — кратчайший путь по сетке");
                Slider("Цена холмов", S.RoadAvoidHills, 1, 10, v => S.RoadAvoidHills = v, true);
                Slider("Цена леса", S.RoadAvoidForest, 1, 10, v => S.RoadAvoidForest = v, true);
                Slider("Цена болота", S.RoadAvoidSwamp, 1, 20, v => S.RoadAvoidSwamp = v, true);
                Slider("Цена переправы", S.RiverCrossingCost, 1, 40, v => S.RiverCrossingCost = v, true, "Больше — меньше мостов, дороги идут вдоль рек");
            }, () =>
            {
                WorldMapGenerationSettings d = WorldMapGenerationSettings.CreateDefault();
                S.ExtraRoadLoops = d.ExtraRoadLoops; S.RoadsToEdge = d.RoadsToEdge; S.TrailsToLandmarks = d.TrailsToLandmarks; S.RoadWander = d.RoadWander;
                S.RoadAvoidHills = d.RoadAvoidHills; S.RoadAvoidForest = d.RoadAvoidForest; S.RoadAvoidSwamp = d.RoadAvoidSwamp; S.RiverCrossingCost = d.RiverCrossingCost;
            });
        }

        private void BuildLandmarks()
        {
            Group("Приметные места (" + S.Landmarks.Count + ")", "Landmarks", () =>
            {
                Help("Правило ставит знак на карту и слот появления. «Место из базы» — локация Базы карты, которая встанет именно сюда (её слот получает тег place:<id>). Примеры по умолчанию — рабочие, не факты мира.");
                List<string> locationIds = new List<string> { "" };
                List<string> locationNames = new List<string> { "— нет —" };
                foreach (WorldMapLocationDefinition location in database.Locations)
                {
                    if (location == null || string.IsNullOrWhiteSpace(location.Id))
                        continue;
                    locationIds.Add(location.Id);
                    locationNames.Add(location.DisplayName + " (" + location.Id + ")");
                }

                for (int i = 0; i < S.Landmarks.Count; i++)
                {
                    WorldMapLandmarkRule rule = S.Landmarks[i];
                    if (rule == null)
                        continue;
                    int index = i;
                    Foldout item = Block((rule.Enabled ? "● " : "○ ") + rule.Name + " ×" + rule.Count, "Landmark." + rule.Id, false);
                    VisualElement keep = parent;
                    parent = item;
                    Toggle("Включено", rule.Enabled, v => rule.Enabled = v, true);
                    Text("Название", rule.Name, v => rule.Name = v);
                    Text("ID", rule.Id, v => rule.Id = v, "По ID знак берёт свой рисунок в «Как выглядит → Приметные места»");
                    IntSlider("Сколько", rule.Count, 0, 20, v => rule.Count = v, true);
                    TerrainMask(rule.Terrains);
                    Toggle("У воды", rule.NearWater, v => rule.NearWater = v, true);
                    Toggle("Рядом с деревней", rule.NearVillage, v => rule.NearVillage = v, true);
                    Slider("Не ближе, клеток", rule.MinDistanceHexes, 0, 40, v => rule.MinDistanceHexes = v, true, "От деревень (если не «рядом с деревней») и от других мест");
                    int selected = Mathf.Max(0, locationIds.IndexOf(rule.LocationId ?? ""));
                    PopupField<string> location = new PopupField<string>("Место из базы", locationNames, selected);
                    location.RegisterValueChangedCallback(_ => Change(() => rule.LocationId = locationIds[location.index], true));
                    parent.Add(location);
                    Text("Теги слота", rule.SlotTags, v => rule.SlotTags = v, "Через запятую: Ruins, Hills…");
                    Toggle("Знак на карте", rule.DrawMark, v => rule.DrawMark = v, true);
                    Toggle("На перекрёстке дорог", rule.Placement == WorldMapLandmarkPlacement.Crossroads,
                        v => rule.Placement = v ? WorldMapLandmarkPlacement.Crossroads : WorldMapLandmarkPlacement.Terrain, true,
                        "Ставится после дорог на клетку, где сходятся три дороги и больше; «Где» тогда не важно");
                    Label placeTitle = new Label("Место в игре") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } };
                    parent.Add(placeTitle);
                    Toggle("Скрыто, пока не найдут", rule.Hidden, v => rule.Hidden = v, false,
                        "Скрытое место не рисуется на карте и не видно меткой, пока отряд его не обнаружит");
                    TextArea("Описание у входа", rule.Description, v => rule.Description = v);
                    Text("Угроза", rule.Threat, v => rule.Threat = v);
                    Slider("Исследование, часов", (float)rule.ExplorationHours, 0, 24, v => rule.ExplorationHours = Math.Round(v * 2.0) / 2.0, false,
                        "0 — исследовать нечего, только описание");
                    IntSlider("Припасы за исследование", rule.RewardArmySupply, 0, 20, v => rule.RewardArmySupply = v);
                    IntSlider("Деньги за исследование", rule.RewardArmyGold, 0, 100, v => rule.RewardArmyGold = v);
                    Help("Вход внутрь (локальная карта) — через «Место из базы»: у записи Базы карты своя локальная карта в Базе локаций (WorldLocationId). Остальные экземпляры правила — обычные места: описание, исследование, итог.");
                    if (!string.IsNullOrEmpty(rule.Source))
                        parent.Add(new Label("Источник типа места: " + rule.Source) { style = { fontSize = 10, color = new Color(.7f, .72f, .66f), whiteSpace = WhiteSpace.Normal } });
                    Buttons(
                        ("↑", index == 0 ? null : (Action)(() => Change(() => { S.Landmarks.RemoveAt(index); S.Landmarks.Insert(index - 1, rule); }, true, true)), null),
                        ("↓", index >= S.Landmarks.Count - 1 ? null : (Action)(() => Change(() => { S.Landmarks.RemoveAt(index); S.Landmarks.Insert(index + 1, rule); }, true, true)), null),
                        ("Дублировать", () => Change(() =>
                        {
                            WorldMapLandmarkRule copy = rule.Clone();
                            copy.Id = rule.Id + "-copy";
                            copy.Name = rule.Name + " (копия)";
                            S.Landmarks.Insert(index + 1, copy);
                        }, true, true), null),
                        ("Удалить", () => Change(() => S.Landmarks.RemoveAt(index), true, true), null));
                    parent = keep;
                }
                Buttons(("+ Добавить место", () => Change(() => S.Landmarks.Add(new WorldMapLandmarkRule { Id = "landmark-" + (S.Landmarks.Count + 1) }), true, true), null));
            }, () => S.Landmarks = WorldMapGenerationSettings.DefaultLandmarks());
        }

        private void TerrainMask(List<WorldMapGameplayTerrainType> terrains)
        {
            Label label = new Label("Где (пусто — любая суша):") { style = { marginTop = 2 } };
            parent.Add(label);
            VisualElement row = Row(true);
            foreach (WorldMapGameplayTerrainType type in WorldMapTerrainLabels.All)
            {
                if (type == WorldMapGameplayTerrainType.Water || type == WorldMapGameplayTerrainType.Cliffs)
                    continue;
                WorldMapGameplayTerrainType captured = type;
                UnityEngine.UIElements.Toggle toggle = new UnityEngine.UIElements.Toggle(WorldMapTerrainLabels.Name(type)) { value = terrains.Contains(type) };
                toggle.labelElement.style.minWidth = 0;
                toggle.style.marginRight = 8;
                toggle.RegisterValueChangedCallback(evt => Change(() =>
                {
                    terrains.Remove(captured);
                    if (evt.newValue)
                        terrains.Add(captured);
                }, true));
                row.Add(toggle);
            }
            parent.Add(row);
        }

        // ---- как выглядит ----

        // Все слои облика на одном экране: включить или выключить каждый,
        // либо всё разом — чтобы посмотреть слой отдельно. Настройки слоёв
        // при выключении сохраняются.
        private (string Group, string Label, Func<bool> Get, Action<bool> Set)[] LayerSwitches() => new (string, string, Func<bool>, Action<bool>)[]
        {
            ("Подложка", "Подложка", () => A.DrawBase, v => A.DrawBase = v),
            ("Подложка", "Узор", () => A.UseBasePattern, v => A.UseBasePattern = v),
            ("Подложка", "Пятна оттенка", () => A.UseTintVariation, v => A.UseTintVariation = v),
            ("Подложка", "Мелкий узор", () => A.UseDetailPattern, v => A.UseDetailPattern = v),
            ("Местность", "Местность", () => A.DrawTerrain, v => A.DrawTerrain = v),
            ("Местность", "Узоры местности", () => A.UseTerrainPatterns, v => A.UseTerrainPatterns = v),
            ("Вода", "Вода", () => A.DrawWater, v => A.DrawWater = v),
            ("Вода", "Озёра", () => A.DrawLakes, v => A.DrawLakes = v),
            ("Вода", "Реки", () => A.DrawRivers, v => A.DrawRivers = v),
            ("Вода", "Отмель", () => A.DrawShoreBand, v => A.DrawShoreBand = v),
            ("Вода", "Рябь", () => A.UseWaterPattern, v => A.UseWaterPattern = v),
            ("Тушь", "Тушь", () => A.DrawInk, v => A.DrawInk = v),
            ("Тушь", "Тушь берега", () => A.DrawShoreInk, v => A.DrawShoreInk = v),
            ("Тушь", "Кромка леса", () => A.DrawForestOutline, v => A.DrawForestOutline = v),
            ("Лес", "Деревья", () => A.DrawTrees, v => A.DrawTrees = v),
            ("Лес", "Редкие деревья", () => A.DrawSparseTrees, v => A.DrawSparseTrees = v),
            ("Лес", "Камыш", () => A.DrawReeds, v => A.DrawReeds = v),
            ("Рельеф", "Холмы", () => A.DrawHills, v => A.DrawHills = v),
            ("Рельеф", "Горы", () => A.DrawMountains, v => A.DrawMountains = v),
            ("Рельеф", "Скалы", () => A.DrawCliffs, v => A.DrawCliffs = v),
            ("Дороги", "Дороги", () => A.DrawRoads, v => A.DrawRoads = v),
            ("Дороги", "Тропы", () => A.DrawTrails, v => A.DrawTrails = v),
            ("Дороги", "Мосты", () => A.DrawBridges, v => A.DrawBridges = v),
            ("Поселения и места", "Деревни", () => A.DrawVillages, v => A.DrawVillages = v),
            ("Поселения и места", "Стоянка", () => A.MarkStart, v => A.MarkStart = v),
            ("Поселения и места", "Приметные места", () => A.DrawLandmarks, v => A.DrawLandmarks = v),
            ("Рисунки", "Свои рисунки и иконки", () => A.UseCustomArt, v => A.UseCustomArt = v),
            ("Обработка", "Обработка", () => A.DrawPost, v => A.DrawPost = v),
            ("Обработка", "Цвет", () => A.UseColorAdjust, v => A.UseColorAdjust = v),
            ("Обработка", "Бумага", () => A.UsePaper, v => A.UsePaper = v),
            ("Обработка", "Зерно", () => A.UseGrain, v => A.UseGrain = v),
            ("Обработка", "Виньетка", () => A.UseVignette, v => A.UseVignette = v)
        };

        private void BuildLayers()
        {
            var switches = LayerSwitches();
            int on = switches.Count(item => item.Get());
            Group("Слои (" + on + " из " + switches.Length + ")", "Layers", () =>
            {
                Help("Каждый слой рисунка можно выключить — настройки слоя сохраняются. «Всё выключить» и включение одного слоя показывают его отдельно. Типы местности и знаки мест выключаются по одному в своих блоках; «Всё» трогает и их.");
                Buttons(
                    ("Всё включить", () => Change(() => SetAllLayers(true), false, true), "Включить все слои, типы местности и знаки мест"),
                    ("Всё выключить", () => Change(() => SetAllLayers(false), false, true), "Выключить все слои — останется ровный цвет основы"));
                foreach (IGrouping<string, (string Group, string Label, Func<bool> Get, Action<bool> Set)> group in switches.GroupBy(item => item.Group))
                {
                    SubTitle(group.Key);
                    VisualElement row = Row(true);
                    foreach (var (_, label, get, set) in group)
                    {
                        UnityEngine.UIElements.Toggle toggle = new UnityEngine.UIElements.Toggle(label) { value = get() };
                        toggle.labelElement.style.minWidth = 0;
                        toggle.style.marginRight = 10;
                        toggle.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), false, true));
                        row.Add(toggle);
                    }
                    parent.Add(row);
                }
            }, null);
        }

        private void SetAllLayers(bool value)
        {
            foreach (var item in LayerSwitches())
                item.Set(value);
            foreach (WorldMapTerrainArt terrain in A.Terrain)
            {
                if (terrain != null)
                    terrain.Enabled = value;
            }
            foreach (WorldMapLandmarkArt landmark in A.Landmarks)
            {
                if (landmark != null)
                    landmark.Enabled = value;
            }
        }

        private void BuildQuality()
        {
            Group("Качество", "Quality", () =>
            {
                Switches(
                    ("Текстуры видеокартой — в своём разрешении", A.TexturesOverMap, v => A.TexturesOverMap = v),
                    ("Рисунки поверх карты — как загружены", A.SignsOverMap, v => A.SignsOverMap = v));
                Help("Включено: подложка, узоры местности, рябь и бумага рисуются видеокартой плитками в разрешении самих текстур, а свои рисунки — отдельным слоем; при любом приближении они чёткие, в окне и в игре. В общий рисунок карты остаются только плавные цвета (подкраска местности, вода, тушь, дороги, знаки кодом), поэтому «Разрешение рисунка» влияет лишь на их края. Выключено — всё впекается в один рисунок и мельчает вместе с ним (так же сохраняется «Сохранить PNG»).");
                Slider("Разрешение рисунка", A.Resolution, 0.1f, 1f, v => A.Resolution = v, false,
                    "Пикселей рисунка на пиксель полотна. Столько рисует игра; предпросмотр в режимах «Черновик/Обычно» — меньше.");
                if (generator != null)
                {
                    Vector2Int size = new Vector2Int(Mathf.RoundToInt(S.CanvasWidth * A.Resolution), Mathf.RoundToInt(S.CanvasHeight * A.Resolution));
                    Help("В игре: " + size.x + "×" + size.y + " пикселей.");
                }
            }, () => A.Resolution = 0.5f);
        }

        private void BuildBase()
        {
            Group("Подложка", "Base", () =>
            {
                Help("Общая подложка: бесшовный узор, который размножается на всю карту. Крупные пятна оттенка и второй мелкий узор прячут повтор.");
                Help("Выключенная подложка — ровный «Цвет основы» без узоров и пятен.");
                Switches(("Узор подложки", A.UseBasePattern, v => A.UseBasePattern = v), ("Пятна оттенка", A.UseTintVariation, v => A.UseTintVariation = v), ("Мелкий узор", A.UseDetailPattern, v => A.UseDetailPattern = v));
                ColorField("Цвет основы", A.BaseColor, v => A.BaseColor = v);
                TextureField("Узор подложки", A.BasePattern, v => A.BasePattern = v);
                Slider("Плитка, клеток", A.BasePatternTileHexes, 0.5f, 60, v => A.BasePatternTileHexes = v);
                Slider("Сила узора", A.BasePatternStrength, 0, 1, v => A.BasePatternStrength = v);
                ColorField("Оттенок узора", A.BasePatternTint, v => A.BasePatternTint = v);
                Slider("Пятна оттенка", A.TintVariation, 0, 0.6f, v => A.TintVariation = v);
                Slider("Размер пятен, клеток", A.TintVariationScaleHexes, 2, 120, v => A.TintVariationScaleHexes = v);
                TextureField("Мелкий узор", A.DetailPattern, v => A.DetailPattern = v);
                Slider("Плитка мелкого, клеток", A.DetailPatternTileHexes, 0.2f, 30, v => A.DetailPatternTileHexes = v);
                Slider("Сила мелкого", A.DetailPatternStrength, 0, 1, v => A.DetailPatternStrength = v);
            }, () =>
            {
                WorldMapArtStyle d = WorldMapArtStyle.CreateDefault();
                A.BaseColor = d.BaseColor; A.BasePatternTileHexes = d.BasePatternTileHexes; A.BasePatternStrength = d.BasePatternStrength; A.BasePatternTint = d.BasePatternTint;
                A.TintVariation = d.TintVariation; A.TintVariationScaleHexes = d.TintVariationScaleHexes; A.DetailPatternTileHexes = d.DetailPatternTileHexes; A.DetailPatternStrength = d.DetailPatternStrength;
            }, A.DrawBase, v => A.DrawBase = v);
        }

        private void BuildTerrainArt()
        {
            Group("Местность", "TerrainArt", () =>
            {
                Help("Каждый тип местности — слой поверх подложки: подкраска и свой узор (например, рисунок лесной подстилки или пашни).");
                Switches(("Узоры местности", A.UseTerrainPatterns, v => A.UseTerrainPatterns = v));
                Slider("Неровность краёв, клеток", A.EdgeRoughness, 0, 1.5f, v => A.EdgeRoughness = v, false, "0 — видны шестиугольники клеток");
                Slider("Рваность краёв", A.EdgeRaggedness, 0, 1, v => A.EdgeRaggedness = v, false, "Края местности, воды и узоров — зубцами и выступами, а не растворением (слоями видеокартой)");
                Slider("Размер зубцов, клеток", A.RaggedScaleHexes, 0.05f, 1.5f, v => A.RaggedScaleHexes = v);
                int blend = Array.IndexOf(WorldMapEdgeBlendLabels.All, A.EdgeBlend);
                PopupField<string> blendField = new PopupField<string>("Переход между заливками", WorldMapEdgeBlendLabels.All.Select(WorldMapEdgeBlendLabels.Name).ToList(), Mathf.Max(0, blend))
                {
                    tooltip = "Как встречаются заливки соседних местностей (слоями видеокартой). Где есть тушь, заливка всегда обрывается по линии туши."
                };
                blendField.RegisterValueChangedCallback(evt => Change(() => A.EdgeBlend = WorldMapEdgeBlendLabels.All[Mathf.Max(0, blendField.index)], false, true));
                parent.Add(blendField);
                Help(WorldMapEdgeBlendLabels.Hint(A.EdgeBlend));
                if (A.EdgeBlend != WorldMapEdgeBlend.Torn)
                {
                    Slider("Ширина перехода, клеток", A.EdgeBlendWidthHexes, 0, 2, v => A.EdgeBlendWidthHexes = v, false, "Насколько заливка заходит на соседнюю");
                    Slider("Размер языков и пятен, клеток", A.EdgeBlendScaleHexes, 0.05f, 3, v => A.EdgeBlendScaleHexes = v);
                    Slider("Густота", A.EdgeBlendDensity, 0, 1, v => A.EdgeBlendDensity = v, false, "Как часто заливка заходит на соседнюю");
                    if (A.EdgeBlend != WorldMapEdgeBlend.Wash)
                        Slider("Мягкость", A.EdgeBlendSoftness, 0, 1, v => A.EdgeBlendSoftness = v, false, "0 — заход обрывается чётко, 1 — тает к соседней заливке");
                    if (A.EdgeBlend == WorldMapEdgeBlend.Streaks)
                        Slider("Направление штрихов, °", A.EdgeBlendAngle, 0, 180, v => A.EdgeBlendAngle = v);
                }
                Slider("Мягкость переходов (в PNG), клеток", A.EdgeSoftness, 0, 1, v => A.EdgeSoftness = v);
                foreach (WorldMapTerrainArt art in A.Terrain)
                {
                    if (art == null || art.Terrain == WorldMapGameplayTerrainType.Road || art.Terrain == WorldMapGameplayTerrainType.Trail)
                        continue;
                    WorldMapTerrainArt entry = art;
                    Foldout item = Block((entry.Enabled ? "● " : "○ ") + WorldMapTerrainLabels.Name(entry.Terrain), "TerrainArt." + entry.Terrain, false);
                    VisualElement keep = parent;
                    parent = item;
                    Toggle("Включено", entry.Enabled, v => entry.Enabled = v);
                    ColorField("Цвет", entry.Color, v => entry.Color = v);
                    Slider("Непрозрачность", entry.Opacity, 0, 1, v => entry.Opacity = v);
                    TextureField("Узор", entry.Pattern, v => entry.Pattern = v);
                    Slider("Плитка, клеток", entry.PatternTileHexes, 0.2f, 60, v => entry.PatternTileHexes = v);
                    Slider("Сила узора", entry.PatternStrength, 0, 1, v => entry.PatternStrength = v);
                    parent = keep;
                }
            }, () =>
            {
                A.Terrain.Clear();
                A.EnsureTerrainEntries();
                A.EdgeRoughness = 0.55f;
                A.EdgeSoftness = 0.18f;
                WorldMapArtStyle d = WorldMapArtStyle.CreateDefault();
                A.EdgeRaggedness = d.EdgeRaggedness; A.RaggedScaleHexes = d.RaggedScaleHexes; A.EdgeBlend = d.EdgeBlend; A.EdgeBlendWidthHexes = d.EdgeBlendWidthHexes;
                A.EdgeBlendScaleHexes = d.EdgeBlendScaleHexes; A.EdgeBlendDensity = d.EdgeBlendDensity; A.EdgeBlendSoftness = d.EdgeBlendSoftness; A.EdgeBlendAngle = d.EdgeBlendAngle;
            }, A.DrawTerrain, v => A.DrawTerrain = v);
        }

        private void BuildInk()
        {
            Group("Тушь", "Ink", () =>
            {
                Help("Чёрный контур — стиль канона v1.56: берега, кромка леса, дороги, знаки. Облик каждой линии (нажим, перо, обрывы, штрихи, сухая кисть) — в «Редакторе туши».");
                Switches(("Кромка леса", A.DrawForestOutline, v => A.DrawForestOutline = v), ("Тушь берега", A.DrawShoreInk, v => A.DrawShoreInk = v));
                ColorField("Цвет туши", A.InkColor, v => A.InkColor = v);
                Slider("Толщина, px полотна", A.InkWidth, 0, 12, v => A.InkWidth = v);
                Buttons(("Редактор туши…", WorldMapInkEditorWindow.Open, "Настройки каждого вида линий туши с предпросмотром"));
            }, () => { WorldMapArtStyle d = WorldMapArtStyle.CreateDefault(); A.InkColor = d.InkColor; A.InkWidth = d.InkWidth; }, A.DrawInk, v => A.DrawInk = v);
        }

        private void BuildWaterArt()
        {
            Group("Вода", "WaterArt", () =>
            {
                Help("Озёра рисуются по клеткам, реки — гладкой линией, расширяющейся к устью. У берега — светлая отмель, по кромке — тушь.");
                Switches(("Озёра", A.DrawLakes, v => A.DrawLakes = v), ("Реки", A.DrawRivers, v => A.DrawRivers = v), ("Отмель", A.DrawShoreBand, v => A.DrawShoreBand = v), ("Тушь берега", A.DrawShoreInk, v => A.DrawShoreInk = v), ("Рябь", A.UseWaterPattern, v => A.UseWaterPattern = v));
                ColorField("Мелко", A.WaterShallowColor, v => A.WaterShallowColor = v);
                ColorField("Глубоко", A.WaterDeepColor, v => A.WaterDeepColor = v);
                Slider("Отмель, клеток", A.ShoreBand, 0, 1, v => A.ShoreBand = v);
                Slider("Ширина рек ×", A.RiverWidthScale, 0.2f, 4, v => A.RiverWidthScale = v);
                TextureField("Рябь (узор)", A.WaterPattern, v => A.WaterPattern = v);
                Slider("Плитка ряби, клеток", A.WaterPatternTileHexes, 0.5f, 30, v => A.WaterPatternTileHexes = v);
                Slider("Сила ряби", A.WaterPatternStrength, 0, 1, v => A.WaterPatternStrength = v);
            }, () =>
            {
                WorldMapArtStyle d = WorldMapArtStyle.CreateDefault();
                A.WaterShallowColor = d.WaterShallowColor; A.WaterDeepColor = d.WaterDeepColor; A.ShoreBand = d.ShoreBand; A.RiverWidthScale = d.RiverWidthScale;
                A.WaterPatternTileHexes = d.WaterPatternTileHexes; A.WaterPatternStrength = d.WaterPatternStrength;
            }, A.DrawWater, v => A.DrawWater = v);
        }

        private void BuildForestArt()
        {
            Group("Лес", "ForestArt", () =>
            {
                Help("Деревья расставляются внутри леса, редкие — на болоте и в поле. Без рисунков — ели и лиственные деревья кодом.");
                Switches(("Редкие деревья", A.DrawSparseTrees, v => A.DrawSparseTrees = v), ("Камыш на болоте", A.DrawReeds, v => A.DrawReeds = v));
                Slider("Деревьев на клетку", A.TreesPerHex, 0, 12, v => A.TreesPerHex = v);
                Slider("Размер, клеток", A.TreeSizeHexes, 0.1f, 3, v => A.TreeSizeHexes = v);
                Slider("Разброс размера", A.TreeSizeJitter, 0, 1, v => A.TreeSizeJitter = v);
                Slider("Доля елей", A.ConiferShare, 0, 1, v => A.ConiferShare = v);
                ColorField("Ели", A.ConiferColor, v => A.ConiferColor = v);
                ColorField("Лиственные", A.LeafColor, v => A.LeafColor = v);
                Slider("Редкие деревья", A.SparseTreesPerHex, 0, 2, v => A.SparseTreesPerHex = v);
                Slider("Камыша на клетку болота", A.ReedsPerHex, 0, 6, v => A.ReedsPerHex = v);
                Slider("Высота камыша, клеток", A.ReedSizeHexes, 0.1f, 2, v => A.ReedSizeHexes = v);
            }, () =>
            {
                WorldMapArtStyle d = WorldMapArtStyle.CreateDefault();
                A.TreesPerHex = d.TreesPerHex; A.TreeSizeHexes = d.TreeSizeHexes; A.TreeSizeJitter = d.TreeSizeJitter;
                A.ConiferShare = d.ConiferShare; A.ConiferColor = d.ConiferColor; A.LeafColor = d.LeafColor; A.SparseTreesPerHex = d.SparseTreesPerHex;
                A.ReedsPerHex = d.ReedsPerHex; A.ReedSizeHexes = d.ReedSizeHexes;
            }, A.DrawTrees, v => A.DrawTrees = v);
        }

        private void BuildReliefArt()
        {
            Group("Холмы и горы", "ReliefArt", () =>
            {
                Switches(("Холмы", A.DrawHills, v => A.DrawHills = v), ("Горы", A.DrawMountains, v => A.DrawMountains = v), ("Скалы", A.DrawCliffs, v => A.DrawCliffs = v));
                Slider("Холмов на клетку", A.HillsPerHex, 0, 4, v => A.HillsPerHex = v);
                Slider("Размер холма, клеток", A.HillSizeHexes, 0.2f, 4, v => A.HillSizeHexes = v);
                ColorField("Цвет холмов", A.HillColor, v => A.HillColor = v);
                Slider("Гор на клетку", A.MountainsPerHex, 0, 4, v => A.MountainsPerHex = v);
                Slider("Размер горы, клеток", A.MountainSizeHexes, 0.2f, 5, v => A.MountainSizeHexes = v);
                ColorField("Свет", A.MountainLightColor, v => A.MountainLightColor = v);
                ColorField("Тень", A.MountainShadowColor, v => A.MountainShadowColor = v);
            }, () =>
            {
                WorldMapArtStyle d = WorldMapArtStyle.CreateDefault();
                A.DrawHills = d.DrawHills; A.HillsPerHex = d.HillsPerHex; A.HillSizeHexes = d.HillSizeHexes; A.HillColor = d.HillColor;
                A.DrawMountains = d.DrawMountains; A.MountainsPerHex = d.MountainsPerHex; A.MountainSizeHexes = d.MountainSizeHexes;
                A.MountainLightColor = d.MountainLightColor; A.MountainShadowColor = d.MountainShadowColor;
            });
        }

        private void BuildRoadArt()
        {
            Group("Дороги", "RoadArt", () =>
            {
                Switches(("Дороги", A.DrawRoads, v => A.DrawRoads = v), ("Тропы", A.DrawTrails, v => A.DrawTrails = v), ("Мосты", A.DrawBridges, v => A.DrawBridges = v));
                ColorField("Дорога", A.RoadColor, v => A.RoadColor = v);
                Slider("Ширина дороги, клеток", A.RoadWidthHexes, 0.02f, 1, v => A.RoadWidthHexes = v);
                Toggle("Дорога пунктиром", A.RoadDashed, v => A.RoadDashed = v);
                ColorField("Тропа", A.TrailColor, v => A.TrailColor = v);
                Slider("Ширина тропы, клеток", A.TrailWidthHexes, 0.02f, 1, v => A.TrailWidthHexes = v);
                Toggle("Тропа пунктиром", A.TrailDashed, v => A.TrailDashed = v);
                ColorField("Мосты", A.BridgeColor, v => A.BridgeColor = v);
            }, () =>
            {
                WorldMapArtStyle d = WorldMapArtStyle.CreateDefault();
                A.RoadColor = d.RoadColor; A.RoadWidthHexes = d.RoadWidthHexes; A.RoadDashed = d.RoadDashed;
                A.TrailColor = d.TrailColor; A.TrailWidthHexes = d.TrailWidthHexes; A.TrailDashed = d.TrailDashed; A.BridgeColor = d.BridgeColor;
            });
        }

        private void BuildVillageArt()
        {
            Group("Деревни", "VillageArt", () =>
            {
                Help("Рисунок деревни (один на деревню) или домики кодом — чем больше деревня, тем больше домов. Старт отряда отмечен частоколом.");
                Slider("Размер, клеток", A.VillageSizeHexes, 0.2f, 4, v => A.VillageSizeHexes = v);
                ColorField("Стены", A.WallColor, v => A.WallColor = v);
                ColorField("Крыши", A.RoofColor, v => A.RoofColor = v);
                Switches(("Отмечать стоянку", A.MarkStart, v => A.MarkStart = v));
            }, () =>
            {
                WorldMapArtStyle d = WorldMapArtStyle.CreateDefault();
                A.VillageSizeHexes = d.VillageSizeHexes; A.WallColor = d.WallColor; A.RoofColor = d.RoofColor; A.MarkStart = d.MarkStart;
            }, A.DrawVillages, v => A.DrawVillages = v);
        }

        private void BuildLandmarkArt()
        {
            Group("Приметные места", "LandmarkArt", () =>
            {
                Help("Знаки мест кодом, если рисунок не назначен (рисунки и иконки — в блоке «Иконки и рисунки»).");
                Slider("Размер знаков кодом, клеток", A.LandmarkSizeHexes, 0.2f, 4, v => A.LandmarkSizeHexes = v);
                ColorField("Цвет камня", A.LandmarkColor, v => A.LandmarkColor = v);
            }, () => { A.LandmarkSizeHexes = 1.4f; A.LandmarkColor = WorldMapArtStyle.CreateDefault().LandmarkColor; }, A.DrawLandmarks, v => A.DrawLandmarks = v);
        }

        // Все рисунки и иконки карты в одном месте: деревья, болото, холмы,
        // горы, скалы, мосты, стоянка, деревни и каждое приметное место.
        // Пустой список — рисует код. Из нескольких рисунков у каждого
        // дерева или места выбирается свой.
        private void BuildIcons()
        {
            Group("Иконки и рисунки", "Icons", () =>
            {
                Help("У каждого объекта — пул до " + WorldMapArtStyle.MaxPoolSize + " рисунков: каждому дереву, горе, мосту, деревне или месту достаётся случайный, и он не меняется после загрузки. «Знаки» — рисунки на карте; «Иконки метки» — кнопка места на карте игры, по которой к нему идут. Пусто — рисует код. Выделите спрайты в Project и нажмите «+ выделенные» — добавятся все сразу.");
                Switches(("Обрезать пустые поля рисунков", A.TrimSpritePadding, v => A.TrimSpritePadding = v));
                Buttons(("Починить нарезку", () => OfferPoolRepair(true), "Картинки, которые Unity нарезала на куски, — одним спрайтом; обрывки и повторы — из пулов"));
                IntSlider("Разрешение рисунков, px", A.SignTextureSize, 64, 512, v => A.SignTextureSize = v);
                Help("«Рисунки поверх карты»: загруженные картинки не впекаются в рисунок карты, а показываются отдельным слоем — видеокартой, в разрешении самих рисунков (до «Разрешения рисунков»), чётко при любом приближении, в окне и в игре. Выключено — впекаются в рисунок карты и мельчают вместе с ним. Размер знака на карте — в клетках, в блоках «Лес», «Холмы и горы», «Деревни», «Приметные места».");
                SubTitle("Лес и болото");
                SpriteList("Ели и сосны", A.ConiferSprites, A.UseConiferSprites, v => A.UseConiferSprites = v);
                SpriteList("Лиственные", A.LeafSprites, A.UseLeafSprites, v => A.UseLeafSprites = v);
                SpriteList("Деревья — общие (если для ели или лиственного своих нет)", A.TreeSprites, A.UseTreeSprites, v => A.UseTreeSprites = v);
                SpriteList("Камыш и кочки", A.ReedSprites, A.UseReedSprites, v => A.UseReedSprites = v);
                SubTitle("Рельеф");
                SpriteList("Холмы", A.HillSprites, A.UseHillSprites, v => A.UseHillSprites = v);
                SpriteList("Горы", A.MountainSprites, A.UseMountainSprites, v => A.UseMountainSprites = v);
                SpriteList("Скалы (пусто — как горы)", A.CliffSprites, A.UseCliffSprites, v => A.UseCliffSprites = v);
                SubTitle("Дороги, деревни, стоянка");
                SpriteList("Мосты", A.BridgeSprites, A.UseBridgeSprites, v => A.UseBridgeSprites = v);
                SpriteList("Деревни", A.VillageSprites, A.UseVillageSprites, v => A.UseVillageSprites = v);
                SpriteList("Иконки метки деревни", A.VillageIcons, A.UseVillageIcons, v => A.UseVillageIcons = v, true);
                SpriteList("Стоянка отряда", A.StartSprites, A.UseStartSprites, v => A.UseStartSprites = v);
                SubTitle("Приметные места");
                foreach (WorldMapLandmarkRule rule in S.Landmarks)
                {
                    if (rule == null || string.IsNullOrEmpty(rule.Id))
                        continue;
                    WorldMapLandmarkArt art = LandmarkArt(rule.Id);
                    Foldout item = Block((art.Enabled ? "● " : "○ ") + rule.Name + "  (знаков " + art.Variants.Count + ", иконок " + art.Icons.Count + ")", "IconRule." + rule.Id, false);
                    VisualElement keep = parent;
                    parent = item;
                    Switches(("Знак на карте", art.Enabled, v => art.Enabled = v));
                    SpriteList("Знаки на карте", art.Variants, art.UseSprites, v => art.UseSprites = v);
                    SpriteList("Иконки метки", art.Icons, art.UseIcons, v => art.UseIcons = v, true);
                    Slider("Размер знака, клеток", art.SizeHexes, 0.2f, 4, v => art.SizeHexes = v);
                    ColorField("Оттенок знака", art.Tint, v => art.Tint = v);
                    parent = keep;
                }
            }, () =>
            {
                A.ConiferSprites.Clear(); A.LeafSprites.Clear(); A.TreeSprites.Clear(); A.ReedSprites.Clear();
                A.HillSprites.Clear(); A.MountainSprites.Clear(); A.CliffSprites.Clear(); A.VillageSprites.Clear();
                A.BridgeSprites.Clear(); A.VillageIcons.Clear(); A.StartSprites.Clear();
                A.Landmarks.Clear();
            }, A.UseCustomArt, v => A.UseCustomArt = v);
        }

        // Все пулы рисунков облика (и иконок меток — с mip-картами).
        private List<(List<Sprite> Pool, bool Icons)> AllPools()
        {
            List<(List<Sprite>, bool)> pools = new List<(List<Sprite>, bool)>
            {
                (A.ConiferSprites, false), (A.LeafSprites, false), (A.TreeSprites, false), (A.ReedSprites, false),
                (A.HillSprites, false), (A.MountainSprites, false), (A.CliffSprites, false), (A.BridgeSprites, false),
                (A.VillageSprites, false), (A.StartSprites, false), (A.VillageIcons, true)
            };
            foreach (WorldMapLandmarkArt landmark in A.Landmarks)
            {
                if (landmark == null)
                    continue;
                pools.Add((landmark.Variants, false));
                pools.Add((landmark.Icons, true));
            }
            return pools;
        }

        // Unity иногда режет одну картинку на кусок и обрывки; обрывки в
        // пуле рисуются мутными прямоугольниками. Окно находит такие
        // картинки и после вопроса делает каждую одним спрайтом.
        private void OfferPoolRepair(bool reportClean)
        {
            if (generator == null)
                return;
            List<(List<Sprite> Pool, bool Icons)> pools = AllPools();
            int broken = WorldMapSpriteImport.CountBrokenPictures(pools.Select(item => item.Pool));
            if (broken == 0)
            {
                if (reportClean)
                    EditorUtility.DisplayDialog("Рисунки", "Нарезанных по ошибке картинок и обрывков в пулах нет.", "Понятно");
                return;
            }
            if (!WorldMapSpriteImport.ConfirmConversion(
                    "В пулах рисунков картинок, которые Unity нарезала на куски: " + broken + ".\n" +
                    "Мелкие обрывки рисуются на карте мутными прямоугольниками, а счётчик пула показывает лишние рисунки.\n\n" +
                    "Сделать каждую такую картинку одним спрайтом и убрать обрывки и повторы из пулов?"))
                return;
            Undo.RecordObject(generator, "Починить нарезку рисунков");
            foreach ((List<Sprite> pool, bool icons) in pools)
                WorldMapSpriteImport.RepairPool(pool, icons);
            EditorUtility.SetDirty(generator);
            AssetDatabase.SaveAssetIfDirty(generator);
            if (settingsScroll != null)
            {
                RebuildSettings();
                ScheduleRefresh(false, 0);
            }
        }

        // Запись облика места создаётся сразу (без Undo-шага), чтобы поля
        // блока всегда было куда писать.
        private WorldMapLandmarkArt LandmarkArt(string ruleId)
        {
            WorldMapLandmarkArt art = A.FindLandmark(ruleId);
            if (art != null)
                return art;
            art = new WorldMapLandmarkArt { RuleId = ruleId, SizeHexes = A.LandmarkSizeHexes };
            A.Landmarks.Add(art);
            EditorUtility.SetDirty(generator);
            return art;
        }

        private void SubTitle(string text)
        {
            parent.Add(new Label(text) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6, color = new Color(.8f, .82f, .75f) } });
        }

        private void TextArea(string label, string value, Action<string> set)
        {
            TextField field = new TextField(label) { value = value ?? "", multiline = true, isDelayed = true };
            field.style.whiteSpace = WhiteSpace.Normal;
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), false));
            parent.Add(field);
        }

        private void BuildPost()
        {
            Group("Обработка", "Post", () =>
            {
                Switches(("Цвет", A.UseColorAdjust, v => A.UseColorAdjust = v), ("Бумага", A.UsePaper, v => A.UsePaper = v), ("Зерно", A.UseGrain, v => A.UseGrain = v), ("Виньетка", A.UseVignette, v => A.UseVignette = v));
                Slider("Яркость", A.Brightness, -0.5f, 0.5f, v => A.Brightness = v);
                Slider("Контраст", A.Contrast, 0.3f, 2f, v => A.Contrast = v);
                Slider("Насыщенность", A.Saturation, 0, 2, v => A.Saturation = v);
                Slider("Тепло", A.Warmth, -1, 1, v => A.Warmth = v, false, "Теплее (+) или холоднее (−)");
                Slider("Виньетка", A.Vignette, 0, 1, v => A.Vignette = v);
                Slider("Зерно", A.Grain, 0, 0.3f, v => A.Grain = v);
                TextureField("Бумага / холст", A.PaperTexture, v => A.PaperTexture = v);
                Slider("Плитка бумаги, клеток", A.PaperTileHexes, 1, 120, v => A.PaperTileHexes = v);
                Slider("Сила бумаги", A.PaperStrength, 0, 1, v => A.PaperStrength = v);
            }, () =>
            {
                WorldMapArtStyle d = WorldMapArtStyle.CreateDefault();
                A.Brightness = d.Brightness; A.Contrast = d.Contrast; A.Saturation = d.Saturation; A.Warmth = d.Warmth;
                A.Vignette = d.Vignette; A.Grain = d.Grain; A.PaperTileHexes = d.PaperTileHexes; A.PaperStrength = d.PaperStrength;
            }, A.DrawPost, v => A.DrawPost = v);
        }

        // ------------------------------------------------------------------
        // Поля (по образцу панели «Обработка кадра»)
        // ------------------------------------------------------------------

        private void Section(string title)
        {
            Label label = new Label(title.ToUpperInvariant());
            label.style.marginTop = 12;
            label.style.marginBottom = 2;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new Color(0.62f, 0.75f, 0.85f);
            parent.Add(label);
        }

        private Foldout Block(string title, string key, bool open)
        {
            string prefs = PrefsKey + "Block." + key;
            Foldout block = new Foldout { text = title, value = EditorPrefs.GetBool(prefs, open) };
            block.RegisterValueChangedCallback(evt => { if (evt.target == block) EditorPrefs.SetBool(prefs, evt.newValue); });
            block.style.marginTop = 6;
            block.style.paddingLeft = block.style.paddingRight = 6;
            block.style.paddingTop = 2;
            block.style.paddingBottom = 4;
            block.style.backgroundColor = new Color(1, 1, 1, .035f);
            block.style.borderTopWidth = block.style.borderBottomWidth = block.style.borderLeftWidth = block.style.borderRightWidth = 1;
            block.style.borderTopColor = block.style.borderBottomColor = block.style.borderLeftColor = block.style.borderRightColor = new Color(1, 1, 1, .09f);
            block.style.borderTopLeftRadius = block.style.borderTopRightRadius = block.style.borderBottomLeftRadius = block.style.borderBottomRightRadius = 4;
            Label label = block.Q<UnityEngine.UIElements.Toggle>()?.Q<Label>();
            if (label != null)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = new Color(.88f, .78f, .53f);
            }
            block.contentContainer.style.marginLeft = 2;
            parent.Add(block);
            return block;
        }

        // Блок настроек: выключатель слоя (если есть, как в «Обработке
        // кадра»: ● — включён, ○ — выключен, поля блока гаснут, но
        // сохраняются), «Сбросить» и поля.
        private void Group(string title, string key, Action content, Action reset, bool? enabled = null, Action<bool> setEnabled = null)
        {
            string Title(bool? on) => (on == null ? "" : on.Value ? "● " : "○ ") + title;
            Foldout block = Block(Title(enabled), key, false);
            VisualElement keep = parent;
            parent = block;
            VisualElement body = new VisualElement();
            if (enabled != null || reset != null)
            {
                VisualElement header = Row(false);
                if (enabled != null && setEnabled != null)
                {
                    UnityEngine.UIElements.Toggle toggle = new UnityEngine.UIElements.Toggle("Слой включён") { value = enabled.Value };
                    toggle.style.flexGrow = 1;
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        Change(() => setEnabled(evt.newValue), false);
                        body.SetEnabled(evt.newValue);
                        block.text = Title(evt.newValue);
                    });
                    header.Add(toggle);
                    body.SetEnabled(enabled.Value);
                }
                else
                    header.Add(new VisualElement { style = { flexGrow = 1 } });
                if (reset != null)
                    header.Add(new Button(() => Change(reset, true, true)) { text = "Сбросить", tooltip = "Значения блока — по умолчанию (выключатель не трогает)" });
                block.Add(header);
            }
            block.Add(body);
            parent = body;
            content();
            parent = keep;
        }

        // Строка выключателей подслоёв в одну линию.
        private void Switches(params (string label, bool value, Action<bool> set)[] switches)
        {
            VisualElement row = Row(true);
            foreach ((string label, bool value, Action<bool> set) in switches)
            {
                UnityEngine.UIElements.Toggle toggle = new UnityEngine.UIElements.Toggle(label) { value = value };
                toggle.labelElement.style.minWidth = 0;
                toggle.style.marginRight = 10;
                toggle.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), false));
                row.Add(toggle);
            }
            parent.Add(row);
        }

        private static VisualElement Row(bool wrap)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            if (wrap)
                row.style.flexWrap = Wrap.Wrap;
            return row;
        }

        private void Help(string text) => parent.Add(new HelpBox(text, HelpBoxMessageType.Info));

        private void Buttons(params (string text, Action action, string tooltip)[] buttons)
        {
            VisualElement row = Row(true);
            foreach ((string text, Action action, string tooltip) in buttons)
            {
                Button button = new Button(action) { text = text, tooltip = tooltip };
                button.SetEnabled(action != null);
                row.Add(button);
            }
            parent.Add(row);
        }

        private void Toggle(string label, bool value, Action<bool> set, bool regenerate = false, string tooltip = null)
        {
            UnityEngine.UIElements.Toggle field = new UnityEngine.UIElements.Toggle(label) { value = value, tooltip = tooltip };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), regenerate));
            parent.Add(field);
        }

        private void Slider(string label, float value, float min, float max, Action<float> set, bool regenerate = false, string tooltip = null)
        {
            UnityEngine.UIElements.Slider field = new UnityEngine.UIElements.Slider(label, min, max) { value = value, showInputField = true, tooltip = tooltip };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), regenerate));
            parent.Add(field);
        }

        private void IntSlider(string label, int value, int min, int max, Action<int> set, bool regenerate = false)
        {
            SliderInt field = new SliderInt(label, min, max) { value = value, showInputField = true };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), regenerate));
            parent.Add(field);
        }

        private void FloatField(string label, float value, Action<float> set, bool regenerate = false)
        {
            UnityEngine.UIElements.FloatField field = new UnityEngine.UIElements.FloatField(label) { value = value, isDelayed = true };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), regenerate));
            parent.Add(field);
        }

        private void Text(string label, string value, Action<string> set, string tooltip = null)
        {
            TextField field = new TextField(label) { value = value ?? "", isDelayed = true, tooltip = tooltip };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), true, true));
            parent.Add(field);
        }

        private void ColorField(string label, Color value, Action<Color> set)
        {
            UnityEditor.UIElements.ColorField field = new UnityEditor.UIElements.ColorField(label) { value = value };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), false));
            parent.Add(field);
        }

        // Поле текстуры: кружок выбора, перетаскивание картинки или спрайта
        // из Project и файла из проводника (копируется в проект), «Снять».
        private void TextureField(string label, Texture2D value, Action<Texture2D> set)
        {
            VisualElement box = new VisualElement();
            box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = 1;
            box.style.borderTopLeftRadius = box.style.borderTopRightRadius = box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 3;
            Color idleBorder = new Color(1, 1, 1, 0f);
            SetBorder(box, idleBorder);
            VisualElement row = Row(false);
            ObjectField field = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false, value = value, tooltip = "Перетащите сюда картинку или спрайт из Project или файл из проводника" };
            field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue as Texture2D), false, true));
            row.Add(field);
            if (value != null)
                row.Add(new Button(() => Change(() => set(null), false, true)) { text = "Снять" });
            box.Add(row);
            Label hint = new Label(value != null
                ? value.width + "×" + value.height + (value.wrapMode == TextureWrapMode.Repeat ? " · повтор включён" : " · повтор выключен — плитка будет со швами")
                : "Перетащите картинку: из Project или PNG / JPG из проводника")
            {
                style = { fontSize = 10, color = new Color(.7f, .72f, .66f), marginLeft = 4, whiteSpace = WhiteSpace.Normal }
            };
            box.Add(hint);

            box.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!WorldMapSpriteImport.HasCandidate(DragAndDrop.objectReferences, DragAndDrop.paths))
                    return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                SetBorder(box, new Color(0.55f, 0.85f, 0.45f, 0.9f));
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            box.RegisterCallback<DragLeaveEvent>(_ => SetBorder(box, idleBorder));
            box.RegisterCallback<DragExitedEvent>(_ => SetBorder(box, idleBorder));
            box.RegisterCallback<DragPerformEvent>(evt =>
            {
                SetBorder(box, idleBorder);
                if (!WorldMapSpriteImport.HasCandidate(DragAndDrop.objectReferences, DragAndDrop.paths))
                    return;
                DragAndDrop.AcceptDrag();
                UnityEngine.Object[] objects = DragAndDrop.objectReferences;
                string[] paths = DragAndDrop.paths;
                evt.StopPropagation();
                EditorApplication.delayCall += () =>
                {
                    Texture2D texture = WorldMapSpriteImport.CollectTexture(objects, paths, label);
                    if (texture == null)
                        EditorUtility.DisplayDialog("Картинка", "Это не картинка. Годятся PNG, JPG, TGA, PSD.", "Понятно");
                    else
                        Change(() => set(texture), false, true);
                };
            }, TrickleDown.TrickleDown);
            parent.Add(box);
        }

        // Пул рисунков объекта (до MaxPoolSize). Рисунки принимаются из
        // чего угодно: перетаскиванием на весь список (спрайт, картинка
        // любого импорта, папка из Project, файлы и папки из проводника),
        // кнопкой «Папка…» и «+ выделенные в Project». Картинки не-Sprite
        // переводятся в Sprite после вопроса. icons — пул иконок меток.
        private void SpriteList(string label, List<Sprite> sprites, bool? use = null, Action<bool> setUse = null, bool icons = false)
        {
            int max = WorldMapArtStyle.MaxPoolSize;
            VisualElement box = new VisualElement();
            box.style.marginTop = 3;
            box.style.paddingLeft = box.style.paddingRight = 3;
            box.style.paddingTop = box.style.paddingBottom = 2;
            box.style.borderTopLeftRadius = box.style.borderTopRightRadius = box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 3;
            box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = 1;
            Color idleBorder = new Color(1, 1, 1, 0.06f);
            Color hotBorder = new Color(0.55f, 0.85f, 0.45f, 0.9f);
            SetBorder(box, idleBorder);

            VisualElement titleRow = Row(false);
            Label title = new Label(label + (sprites.Count == 0 ? " — нет, рисует код" : " (" + sprites.Count + " из " + max + ")")) { style = { flexGrow = 1, whiteSpace = WhiteSpace.Normal } };
            titleRow.Add(title);
            if (use != null && setUse != null)
            {
                UnityEngine.UIElements.Toggle toggle = new UnityEngine.UIElements.Toggle("свои") { value = use.Value, tooltip = "Брать рисунки из этого пула; выключено — рисует код, рисунки остаются" };
                toggle.labelElement.style.minWidth = 0;
                toggle.RegisterValueChangedCallback(evt => Change(() => setUse(evt.newValue), false));
                titleRow.Add(toggle);
            }
            box.Add(titleRow);

            for (int i = 0; i < sprites.Count; i++)
            {
                int index = i;
                VisualElement row = Row(false);
                ObjectField field = new ObjectField { objectType = typeof(Sprite), allowSceneObjects = false, value = sprites[i] };
                field.style.flexGrow = 1;
                field.RegisterValueChangedCallback(evt => Change(() => sprites[index] = evt.newValue as Sprite, false));
                row.Add(field);
                row.Add(new Button(() => Change(() => sprites.RemoveAt(index), false, true)) { text = "−" });
                box.Add(row);
            }

            Label hint = new Label(sprites.Count >= max
                ? "Пул полон (" + max + "). Уберите лишние, чтобы добавить новые."
                : "Перетащите сюда спрайты, картинки, папку из Project или файлы и папки из проводника")
            {
                style = { fontSize = 10, color = new Color(.7f, .72f, .66f), whiteSpace = WhiteSpace.Normal, marginLeft = 2 }
            };
            box.Add(hint);

            bool full = sprites.Count >= max;
            VisualElement buttons = Row(true);
            void AddButton(string text, Action action, string tooltip)
            {
                Button button = new Button(action) { text = text, tooltip = tooltip };
                button.SetEnabled(action != null);
                buttons.Add(button);
            }
            AddButton("+ рисунок", full ? null : (Action)(() => Change(() => sprites.Add(null), false, true)), "Пустая строка: выбрать рисунок кружком справа");
            AddButton("Папка…", full ? null : (Action)(() => AddToPool(sprites, WorldMapSpriteImport.CollectFromFolderPanel(label, icons))),
                "Взять все картинки папки (в проекте или на диске — с диска копируются в " + WorldMapSpriteImport.DiskFolder + ")");
            AddButton("+ выделенные в Project", full ? null : (Action)(() => AddToPool(sprites,
                WorldMapSpriteImport.Collect(Selection.objects, null, label, icons))), "Спрайты, картинки и папки, выделенные в окне Project");
            AddButton("Очистить", sprites.Count == 0 ? null : (Action)(() => Change(sprites.Clear, false, true)), null);
            box.Add(buttons);

            // Приём перетаскивания на весь список. TrickleDown — раньше полей
            // строк, которые сами принимают только готовый спрайт.
            box.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!WorldMapSpriteImport.HasCandidate(DragAndDrop.objectReferences, DragAndDrop.paths))
                    return;
                DragAndDrop.visualMode = full ? DragAndDropVisualMode.Rejected : DragAndDropVisualMode.Copy;
                SetBorder(box, hotBorder);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            box.RegisterCallback<DragLeaveEvent>(_ => SetBorder(box, idleBorder));
            box.RegisterCallback<DragExitedEvent>(_ => SetBorder(box, idleBorder));
            box.RegisterCallback<DragPerformEvent>(evt =>
            {
                SetBorder(box, idleBorder);
                if (full || !WorldMapSpriteImport.HasCandidate(DragAndDrop.objectReferences, DragAndDrop.paths))
                    return;
                DragAndDrop.AcceptDrag();
                UnityEngine.Object[] objects = DragAndDrop.objectReferences;
                string[] paths = DragAndDrop.paths;
                evt.StopPropagation();
                // После события: импорт и диалог нельзя делать внутри обработки drag.
                EditorApplication.delayCall += () => AddToPool(sprites, WorldMapSpriteImport.Collect(objects, paths, label, icons));
            }, TrickleDown.TrickleDown);

            parent.Add(box);
        }

        private void AddToPool(List<Sprite> pool, List<Sprite> found)
        {
            int max = WorldMapArtStyle.MaxPoolSize;
            if (found == null || found.Count == 0)
            {
                EditorUtility.DisplayDialog("Рисунки", "Не нашлось ни одной картинки-спрайта. Годятся PNG, JPG, TGA, PSD — по одному, папкой или выделением в Project.", "Понятно");
                return;
            }
            int added = 0, skipped = 0;
            Change(() =>
            {
                pool.RemoveAll(sprite => sprite == null);
                foreach (Sprite sprite in found)
                {
                    if (pool.Contains(sprite))
                        continue;
                    if (pool.Count >= max)
                    {
                        skipped++;
                        continue;
                    }
                    pool.Add(sprite);
                    added++;
                }
            }, false, true);
            if (skipped > 0)
                EditorUtility.DisplayDialog("Рисунки", "Добавлено " + added + ". Пул вмещает " + max + " рисунков — ещё " + skipped + " не поместились.", "Понятно");
        }

        private static void SetBorder(VisualElement element, Color color)
        {
            element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
        }
    }
}
