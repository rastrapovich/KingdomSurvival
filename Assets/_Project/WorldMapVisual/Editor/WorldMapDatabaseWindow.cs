using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // Редактор World Map Database: текстуры, реальные стартовые локации,
    // проверка конфигурации и быстрый preview без Play Mode. Полигональный
    // редактор регионов и Rule Tile по-прежнему не вводятся.
    public sealed partial class WorldMapDatabaseWindow : EditorWindow
    {
        private enum WindowTab
        {
            World,
            Textures,
            Geography,
            Locations,
            Validate,
            Preview,
            Movement
        }

        private WorldMapDatabaseAsset database;
        private WindowTab tab;
        private int previewSeed = 1;
        private Vector2 issuesScroll;
        private Vector2 themeScroll;
        private Vector2 locationsScroll;
        private Vector2 worldScroll;
        private Vector2 geographyScroll;
        private List<string> issues = new List<string>();
        private bool validated;

        // Editor-only переключатели слоёв Preview — не сериализуются в
        // игровой asset (раздел 13 задачи), хранятся в EditorPrefs.
        private const string PrefShowArt = "KingdomSurvival.WorldMapPreview.ShowArt";
        private const string PrefShowLocations = "KingdomSurvival.WorldMapPreview.ShowLocations";
        private const string PrefShowSpawnSlots = "KingdomSurvival.WorldMapPreview.ShowSpawnSlots";

        // Задача "Map Art Layers": отдельный тумблер видимости слоёв и
        // отдельный тумблер debug-рамок их Bounds (раздел 13 задачи) — рамки
        // полезны только при размещении PNG, поэтому не завязаны на основной
        // тумблер видимости самих слоёв.
        private const string PrefShowArtLayers = "KingdomSurvival.WorldMapPreview.ShowArtLayers";
        private const string PrefShowArtLayerBounds = "KingdomSurvival.WorldMapPreview.ShowArtLayerBounds";

        private bool previewShowArt = true;
        private bool previewShowLocations = true;
        private bool previewShowSpawnSlots = true;
        private bool previewShowArtLayers = true;
        private bool previewShowArtLayerBounds;

        // Индекс Art Layer, выбранного на вкладке «Текстуры» или кликом в
        // Preview — Preview выделяет его рамкой + resize handles (раздел
        // WM-T04.7), не сериализуется в игровой asset.
        private int selectedArtLayerIndex = -1;
        private WorldMapVisualTheme lastSeenArtLayerTheme;

        // Задача "Map Art Layers — direct manipulation" (WM-T04.7): drag/
        // resize выбранного Art Layer в Preview. Один drag = одна Undo-
        // операция (Undo.RecordObject на MouseDown, не на каждый MouseDrag).
        // originalArtLayerBounds/artLayerDragStartMapPoint — снимок на
        // момент MouseDown; move/resize считаются от НЕГО + суммарной
        // дельты, а не накопительно кадр за кадром (раздел 7 задачи —
        // исключает плавающий дрифт координат за долгий drag).
        private bool isDraggingArtLayer;
        private bool isResizingArtLayer;
        private ArtLayerCorner resizingArtLayerCorner;
        private MapBounds originalArtLayerBounds;
        private Vector2 artLayerDragStartMapPoint;

        // Минимальный размер Art Layer в map-space при resize (раздел 12
        // задачи) — меньше уже неудобно тянуть handle'ом; для точных мелких
        // фрагментов остаются числовые поля на вкладке «Текстуры», их этот
        // порог не ограничивает.
        private const float MinArtLayerSizePercent = 1f;

        // Единственный режим редактирования Preview активен в любой момент.
        // 12И (канон v1.50 §9.9): режим «Местность» — кисть разметки клеток
        // шестиугольной сетки; прежние Terrain Areas и Roads отменены.
        private enum PreviewEditMode
        {
            View,
            ArtLayers,
            Terrain
        }

        private PreviewEditMode previewEditMode = PreviewEditMode.View;

        // Задача "Global Map Aspect + Preview Canvas Navigation" (WM-T04.9):
        // zoom/pan — editor-only viewport state, НЕ gameplay-данные (раздел
        // 23 задачи). zoom=1, pan=(0,0) — это ровно "Вписать карту" (fitRect
        // без изменений); persisted через EditorPrefs, чтобы не сбрасываться
        // при закрытии/открытии окна, если это просто (раздел 23).
        private const string PrefPreviewZoom = "KingdomSurvival.WorldMapPreview.Zoom";
        private const string PrefPreviewPanX = "KingdomSurvival.WorldMapPreview.PanX";
        private const string PrefPreviewPanY = "KingdomSurvival.WorldMapPreview.PanY";
        private const float MinPreviewZoom = 0.1f;
        private const float MaxPreviewZoom = 30f;
        private const float MinPreviewCanvasHeight = 320f;

        // Единственное место, где считается допустимый диапазон zoom —
        // wheel, "1×"/"Вписать карту" и загрузка из EditorPrefs проходят
        // через эту функцию, а не через отдельные Mathf.Clamp с
        // продублированными границами. Публичный static — используется
        // напрямую из EditMode-тестов.
        public static float ClampPreviewZoom(float zoom) => Mathf.Clamp(zoom, MinPreviewZoom, MaxPreviewZoom);

        private float previewZoom = 1f;
        private Vector2 previewPanFraction = Vector2.zero;
        private bool isPanningPreview;

        // Кэш последнего вычисленного за кадр fitRect/canvasRect/reference-
        // ширины — нужен кнопке "1×" (она рисуется ДО canvas в этом же
        // OnGUI-проходе, поэтому берёт значения предыдущего кадра).
        private Rect lastPreviewFitRect;
        private Rect lastPreviewCanvasRect;
        private float lastPreviewReferenceWidth = WorldMapWorldDefinitionAsset.DefaultMapCanvasWidth;

        [MenuItem("Kingdom Survival/Карта/World Map Database")]
        private static void Open()
        {
            WorldMapDatabaseWindow window = GetWindow<WorldMapDatabaseWindow>();
            window.titleContent = new GUIContent("World Map Database");
            window.minSize = new Vector2(640f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            if (database == null)
                database = Resources.Load<WorldMapDatabaseAsset>(WorldMapDatabaseAsset.ResourcesPath);

            previewShowArt = EditorPrefs.GetBool(PrefShowArt, true);
            previewShowLocations = EditorPrefs.GetBool(PrefShowLocations, true);
            previewShowSpawnSlots = EditorPrefs.GetBool(PrefShowSpawnSlots, true);
            previewShowArtLayers = EditorPrefs.GetBool(PrefShowArtLayers, true);
            previewShowArtLayerBounds = EditorPrefs.GetBool(PrefShowArtLayerBounds, false);
            LoadTerrainPaintPrefs();

            previewZoom = ClampPreviewZoom(EditorPrefs.GetFloat(PrefPreviewZoom, 1f));
            previewPanFraction = new Vector2(
                EditorPrefs.GetFloat(PrefPreviewPanX, 0f),
                EditorPrefs.GetFloat(PrefPreviewPanY, 0f));
        }

        private void SavePreviewViewportPrefs()
        {
            EditorPrefs.SetFloat(PrefPreviewZoom, previewZoom);
            EditorPrefs.SetFloat(PrefPreviewPanX, previewPanFraction.x);
            EditorPrefs.SetFloat(PrefPreviewPanY, previewPanFraction.y);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);
            database = (WorldMapDatabaseAsset)EditorGUILayout.ObjectField(
                "World Map Database", database, typeof(WorldMapDatabaseAsset), false);

            // Раздел 4 задачи "Direct Art Layer Manipulation": при смене
            // базы/темы индекс выбранного Art Layer может указывать на
            // совсем другой слой — сбрасываем, не пересчитываем вслепую.
            WorldMapVisualTheme activeTheme = database != null ? database.ActiveTheme : null;
            if (activeTheme != lastSeenArtLayerTheme)
            {
                selectedArtLayerIndex = -1;
                lastSeenArtLayerTheme = activeTheme;
            }

            WorldMapWorldDefinitionAsset activeWorld = database != null ? database.ActiveWorld : null;
            if (activeWorld != lastSeenTerrainWorld)
            {
                InvalidateTerrainPaintCache();
                lastSeenTerrainWorld = activeWorld;
            }

            EditorGUILayout.Space(6f);
            tab = (WindowTab)GUILayout.Toolbar(
                (int)tab,
                new[] { "Мир", "Текстуры", "География", "Локации", "Проверка", "Предпросмотр", "Перемещение" });
            EditorGUILayout.Space(8f);

            switch (tab)
            {
                case WindowTab.World:
                    DrawWorldSection();
                    break;
                case WindowTab.Textures:
                    DrawTexturesSection();
                    break;
                case WindowTab.Geography:
                    DrawGeographySection();
                    break;
                case WindowTab.Locations:
                    DrawLocationsSection();
                    break;
                case WindowTab.Validate:
                    DrawValidateSection();
                    break;
                case WindowTab.Preview:
                    DrawPreviewSection();
                    break;
                case WindowTab.Movement:
                    DrawMovementSection();
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Мир (AM-01/AM-03) — авторский постоянный мир: id/версия, Дом.
        // Заменяет заполнение WorldMapWorldDefinitionAsset через eval/скрипты.
        // ------------------------------------------------------------------

        private void DrawWorldSection()
        {
            if (database == null)
            {
                EditorGUILayout.HelpBox("Выберите или назначьте World Map Database сверху.", MessageType.Info);
                return;
            }

            SerializedObject databaseSO = new SerializedObject(database);
            databaseSO.Update();
            SerializedProperty activeWorldProp = databaseSO.FindProperty("activeWorld");

            EditorGUILayout.PropertyField(activeWorldProp, new GUIContent("Active World"));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Создать новый World Definition"))
                CreateWorldDefinitionAsset(databaseSO, activeWorldProp);
            EditorGUILayout.EndHorizontal();

            databaseSO.ApplyModifiedProperties();

            WorldMapWorldDefinitionAsset world =
                activeWorldProp.objectReferenceValue as WorldMapWorldDefinitionAsset;

            if (world == null)
            {
                EditorGUILayout.HelpBox(
                    "Без Active World география процедурная (переходное поведение AM-01) — " +
                    "рельеф и река генерируются заново из WorldSeed при каждой новой партии.",
                    MessageType.Info);
                return;
            }

            SerializedObject worldSO = new SerializedObject(world);
            worldSO.Update();

            worldScroll = EditorGUILayout.BeginScrollView(worldScroll);

            EditorGUILayout.LabelField("Идентификация", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(
                worldSO.FindProperty("worldDefinitionId"), new GUIContent("World Definition Id"));
            EditorGUILayout.PropertyField(
                worldSO.FindProperty("geographyVersion"), new GUIContent("Geography Version"));
            EditorGUILayout.HelpBox(
                "Geography Version увеличивать только при изменении рельефа/реки/фиксированных " +
                "объектов — замена спрайта/цвета версию не меняет (версия арта отдельная, хранится в теме).",
                MessageType.None);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Дом", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(
                worldSO.FindProperty("homeLocationId"), new GUIContent("Home Location Id"));
            EditorGUILayout.PropertyField(
                worldSO.FindProperty("homeXPercent"), new GUIContent("X, %"));
            EditorGUILayout.PropertyField(
                worldSO.FindProperty("homeYPercent"), new GUIContent("Y, %"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10f);
            DrawGlobalMapCanvasSection(worldSO, world);

            EditorGUILayout.EndScrollView();
            worldSO.ApplyModifiedProperties();
        }

        // Задача "Global Map Aspect" (WM-T04.9, раздел 7): reference canvas,
        // определяющий ТОЛЬКО геометрические пропорции глобальной карты —
        // не разрешение какой-либо реальной Texture (Base Map/Art Layer
        // остаются любого разрешения). Preview больше не берёт aspect из
        // BaseMapSprite — только отсюда.
        private static void DrawGlobalMapCanvasSection(SerializedObject worldSO, WorldMapWorldDefinitionAsset world)
        {
            EditorGUILayout.LabelField("Глобальная карта", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(
                worldSO.FindProperty("mapCanvasWidth"), new GUIContent("Reference Width"));
            EditorGUILayout.PropertyField(
                worldSO.FindProperty("mapCanvasHeight"), new GUIContent("Reference Height"));

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.FloatField("Aspect", world.GlobalMapAspect);

            EditorGUILayout.HelpBox(
                "Reference размер определяет только пропорции глобального map-space (0..100×0..100 " +
                "gameplay-координат) — это НЕ обязательное разрешение PNG. Base Map и Map Art Layers " +
                "остаются любого разрешения и не влияют на эти пропорции.",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void CreateWorldDefinitionAsset(
            SerializedObject databaseSO,
            SerializedProperty activeWorldProp)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Новый World Definition",
                "KingdomSurvivalWorldDefinition",
                "asset",
                "Выберите, где сохранить новый авторский мир.");

            if (string.IsNullOrEmpty(path))
                return;

            WorldMapWorldDefinitionAsset asset =
                ScriptableObject.CreateInstance<WorldMapWorldDefinitionAsset>();
            asset.EditorSetWorldId("new-world", 1);
            asset.EditorSetHome(
                "home", WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent);

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            activeWorldProp.objectReferenceValue = asset;
            databaseSO.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------
        // География (AM-03) — авторские зоны местности и опорные точки реки
        // активного World Definition. Прямоугольники в процентах карты, без
        // полигонов/маски — по решению инструкции по миграции.
        // ------------------------------------------------------------------

        private void DrawGeographySection()
        {
            if (database == null || database.ActiveWorld == null)
            {
                EditorGUILayout.HelpBox(
                    "Назначьте Active World на вкладке «Мир», чтобы редактировать географию.",
                    MessageType.Info);
                return;
            }

            SerializedObject worldSO = new SerializedObject(database.ActiveWorld);
            worldSO.Update();

            geographyScroll = EditorGUILayout.BeginScrollView(geographyScroll);

            DrawTerrainMarkupSection(database.ActiveWorld);
            EditorGUILayout.Space(14f);
            DrawSpawnSlotsSection(worldSO);

            EditorGUILayout.EndScrollView();
            worldSO.ApplyModifiedProperties();
        }

        private static void DrawSpawnSlotsSection(SerializedObject worldSO)
        {
            EditorGUILayout.LabelField("Слоты появления малых локаций", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Область поверх готовой карты, внутри которой Anchored-локация может появиться. " +
                "Tags описывают, что реально нарисовано в этой области (Forest, Shore, NearRoad...) — " +
                "локация с требованием тегов (RequiredSlotTags в базе локаций) выбирает только слоты, " +
                "содержащие ВСЕ эти теги, и никогда не окажется в реке или на вершине горы, если " +
                "подходящий слот не разрешён здесь. Пустой список слотов — используется старый " +
                "встроенный запасной набор (WorldMapSpawnSlotRegistry).",
                MessageType.None);

            SerializedProperty slotsProp = worldSO.FindProperty("spawnSlots");

            for (int i = 0; i < slotsProp.arraySize; i++)
            {
                SerializedProperty slot = slotsProp.GetArrayElementAtIndex(i);
                SerializedProperty idProp = slot.FindPropertyRelative("Id");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(idProp.stringValue)
                        ? $"Слот {i + 1}"
                        : idProp.stringValue,
                    EditorStyles.boldLabel);
                if (GUILayout.Button("Удалить", GUILayout.Width(80f)))
                {
                    slotsProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(idProp, new GUIContent("ID"));
                DrawTagsField(
                    slot.FindPropertyRelative("Tags"),
                    "Теги (через запятую, напр. Forest, Clearing)");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(
                    slot.FindPropertyRelative("MinXPercent"), new GUIContent("Min X, %"));
                EditorGUILayout.PropertyField(
                    slot.FindPropertyRelative("MaxXPercent"), new GUIContent("Max X, %"));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(
                    slot.FindPropertyRelative("MinYPercent"), new GUIContent("Min Y, %"));
                EditorGUILayout.PropertyField(
                    slot.FindPropertyRelative("MaxYPercent"), new GUIContent("Max Y, %"));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4f);
            }

            if (GUILayout.Button("+ ДОБАВИТЬ СЛОТ", GUILayout.Height(26f)))
            {
                int index = slotsProp.arraySize;
                slotsProp.InsertArrayElementAtIndex(index);
                SerializedProperty slot = slotsProp.GetArrayElementAtIndex(index);
                slot.FindPropertyRelative("Id").stringValue = "slot-" + (index + 1);
                slot.FindPropertyRelative("Tags").ClearArray();
                slot.FindPropertyRelative("MinXPercent").floatValue = 40f;
                slot.FindPropertyRelative("MaxXPercent").floatValue = 60f;
                slot.FindPropertyRelative("MinYPercent").floatValue = 40f;
                slot.FindPropertyRelative("MaxYPercent").floatValue = 60f;
            }
        }

        // Компактный редактор List<string> как одна строка через запятую —
        // проще для тегов, чем разворачиваемый Unity-массив на 1 короткое
        // слово за раз.
        private static void DrawTagsField(SerializedProperty tagsProp, string label)
        {
            string joined = JoinTags(tagsProp);
            string edited = EditorGUILayout.TextField(label, joined);
            if (edited != joined)
                SplitTagsInto(tagsProp, edited);
        }

        private static string JoinTags(SerializedProperty tagsProp)
        {
            List<string> values = new List<string>();
            for (int i = 0; i < tagsProp.arraySize; i++)
                values.Add(tagsProp.GetArrayElementAtIndex(i).stringValue);
            return string.Join(", ", values);
        }

        private static void SplitTagsInto(SerializedProperty tagsProp, string text)
        {
            string[] parts = text.Split(',');
            List<string> cleaned = new List<string>();
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    cleaned.Add(trimmed);
            }

            tagsProp.arraySize = cleaned.Count;
            for (int i = 0; i < cleaned.Count; i++)
                tagsProp.GetArrayElementAtIndex(i).stringValue = cleaned[i];
        }

        // ------------------------------------------------------------------
        // Тема — редактирование спрайтов/цветов прямо в окне, без прыжков
        // в Inspector ассета. Через SerializedObject/SerializedProperty —
        // тот же способ, что и в DialogueDatabaseWindow этого проекта, с
        // поддержкой Undo и корректной пометкой ассета как изменённого.
        // ------------------------------------------------------------------

        private void DrawTexturesSection()
        {
            if (database == null)
            {
                EditorGUILayout.HelpBox("Выберите или назначьте World Map Database сверху.", MessageType.Info);
                return;
            }

            if (database.ActiveTheme == null)
            {
                EditorGUILayout.HelpBox(
                    "У базы не назначена Active Theme — редактировать нечего. " +
                    "Назначьте WorldMapVisualTheme в Inspector ассета World Map Database.",
                    MessageType.Warning);
                return;
            }

            SerializedObject themeSO = new SerializedObject(database.ActiveTheme);
            themeSO.Update();

            themeScroll = EditorGUILayout.BeginScrollView(themeScroll);

            DrawBaseMapSection(themeSO);
            EditorGUILayout.Space(14f);
            DrawMarkerScaleSection(themeSO);
            EditorGUILayout.Space(14f);
            DrawArtLayersSection(themeSO);
            EditorGUILayout.Space(14f);
            DrawDefaultIconSection(themeSO);

            EditorGUILayout.EndScrollView();

            themeSO.ApplyModifiedProperties();
        }

        private static void DrawBaseMapSection(SerializedObject themeSO)
        {
            EditorGUILayout.LabelField("Основа карты", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(
                themeSO.FindProperty("baseMapSprite"),
                new GUIContent("Текстура фона"));
            EditorGUILayout.PropertyField(
                themeSO.FindProperty("baseMapColor"),
                new GUIContent("Цвет без текстуры"));
            EditorGUILayout.PropertyField(
                themeSO.FindProperty("baseMapTint"),
                new GUIContent("Оттенок текстуры"));
            EditorGUILayout.HelpBox(
                "Фоновая текстура заполняет слой под маршрутами и маркерами. Рельеф (холмы, горы, " +
                "лес, поля), река, озёра, берега и прочая постоянная география рисуются прямо в " +
                "этой текстуре художником — код не генерирует и не рисует их поверх карты. " +
                "Вкладка «География» задаёт только невидимую gameplay-разметку поверх этого арта.",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        // Задача "регулируемый визуальный размер героя и Дома": ЧИСТО
        // презентационная настройка — не влияет на скорость, расстояние,
        // координаты, discovery radius, Road Width, Terrain или маршрут.
        // Единица — доли логической клетки карты (1.0 = размер одной
        // клетки), не пиксели, поэтому маркер масштабируется вместе с zoom.
        // Preview использует то же значение (WorldMapVisualTheme —
        // единственный источник истины и для runtime, и для Preview).
        private static void DrawMarkerScaleSection(SerializedObject themeSO)
        {
            EditorGUILayout.LabelField("Визуальный масштаб маркеров", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            SerializedProperty heroProp = themeSO.FindProperty("heroMarkerSizeCells");
            SerializedProperty homeProp = themeSO.FindProperty("homeMarkerSizeCells");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Размер героя", GUILayout.Width(110f));
            heroProp.floatValue = EditorGUILayout.Slider(heroProp.floatValue, 0.2f, 2.0f);
            EditorGUILayout.LabelField("клетки", GUILayout.Width(45f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Размер Дома", GUILayout.Width(110f));
            homeProp.floatValue = EditorGUILayout.Slider(homeProp.floatValue, 0.1f, 4.0f);
            EditorGUILayout.LabelField("клетки", GUILayout.Width(45f));
            EditorGUILayout.EndHorizontal();

            if (heroProp.floatValue <= 0f || float.IsNaN(heroProp.floatValue) || float.IsInfinity(heroProp.floatValue))
            {
                EditorGUILayout.HelpBox(
                    "Размер героя невалиден (0/NaN/Infinity) — в игре будет использован " +
                    $"безопасный fallback {WorldMapVisualTheme.DefaultHeroMarkerSizeCells:0.##} клетки.",
                    MessageType.Warning);
            }

            if (homeProp.floatValue <= 0f || float.IsNaN(homeProp.floatValue) || float.IsInfinity(homeProp.floatValue))
            {
                EditorGUILayout.HelpBox(
                    "Размер Дома невалиден (0/NaN/Infinity) — в игре будет использован " +
                    $"безопасный fallback {WorldMapVisualTheme.DefaultHomeMarkerSizeCells:0.##} клетки.",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "1.0 = визуальный размер одной логической клетки карты (104×64). Маркер — только " +
                "изображение: не меняет скорость движения, расстояние, координаты, discovery radius, " +
                "Road Width, Terrain или маршрут. Масштабируется вместе с zoom/pan карты и в Preview, " +
                "и в игре — фиксированный экранный размер здесь сознательно не используется.",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        // Задача "Map Art Layers": отдельные PNG-фрагменты глобальной карты
        // поверх (необязательного) Base Map — каждый занимает только свой
        // Bounds в координатах карты (0..100%), не растягивается на всю
        // карту. Тот же UI-паттерн, что DrawRoadsSection (список через
        // SerializedProperty, добавление/удаление кнопками).
        private void DrawArtLayersSection(SerializedObject themeSO)
        {
            EditorGUILayout.LabelField("Map Art Layers", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Отдельные PNG-фрагменты глобальной карты (0..100% по обеим осям, та же система " +
                "координат, что у Roads/Locations/Spawn Slots/героя) — позволяют дорисовывать карту " +
                "постепенно, регион за регионом, не растягивая один спрайт на всю площадь. Base Map " +
                "выше (если назначен) — необязательный фон под всеми слоями. Основной способ " +
                "позиционирования — drag/resize на вкладке «Предпросмотр» (кнопка «Показать в " +
                "Preview» ниже); числа под «Точное положение» — точный ручной ввод, не основной способ.",
                MessageType.None);

            SerializedProperty layersProp = themeSO.FindProperty("artLayers");

            for (int i = 0; i < layersProp.arraySize; i++)
            {
                SerializedProperty layer = layersProp.GetArrayElementAtIndex(i);
                SerializedProperty idProp = layer.FindPropertyRelative("Id");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                bool isSelected = selectedArtLayerIndex == i;
                EditorGUILayout.LabelField(
                    (string.IsNullOrWhiteSpace(idProp.stringValue) ? $"Layer {i + 1}" : idProp.stringValue) +
                    (isSelected ? "  [показан в Preview]" : ""),
                    EditorStyles.boldLabel);
                if (GUILayout.Button(isSelected ? "Скрыть в Preview" : "Показать в Preview", GUILayout.Width(150f)))
                {
                    selectedArtLayerIndex = isSelected ? -1 : i;
                    if (!isSelected)
                    {
                        previewEditMode = PreviewEditMode.ArtLayers;
                        tab = WindowTab.Preview; // раздел 17 задачи — сразу переходим на вкладку
                    }
                }
                if (GUILayout.Button("Удалить", GUILayout.Width(80f)))
                {
                    layersProp.DeleteArrayElementAtIndex(i);
                    // Раздел 4 задачи "Direct Art Layer Manipulation":
                    // удаление выбранного слоя сбрасывает selection; удаление
                    // слоя ПЕРЕД выбранным сдвигает индекс, чтобы selection
                    // не "перепрыгнул" на другой слой.
                    if (selectedArtLayerIndex == i)
                        selectedArtLayerIndex = -1;
                    else if (selectedArtLayerIndex > i)
                        selectedArtLayerIndex--;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(idProp, new GUIContent("ID"));
                EditorGUILayout.PropertyField(
                    layer.FindPropertyRelative("DisplayName"), new GUIContent("Название"));
                EditorGUILayout.PropertyField(
                    layer.FindPropertyRelative("Sprite"), new GUIContent("Sprite"));
                EditorGUILayout.PropertyField(
                    layer.FindPropertyRelative("Enabled"), new GUIContent("Активен"));

                // Раздел 1/16 задачи "Direct Art Layer Manipulation": теперь
                // это ТОЧНЫЙ, а не основной способ позиционирования — основной
                // — drag/resize во вкладке «Предпросмотр» (кнопка ниже).
                // Внутренние имена полей (MinXPercent и т.д.) не менялись —
                // переименован только заголовок для художника.
                EditorGUILayout.LabelField("Точное положение (проценты карты, 0..100)");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(
                    layer.FindPropertyRelative("MinXPercent"), new GUIContent("Левая граница X"));
                EditorGUILayout.PropertyField(
                    layer.FindPropertyRelative("MaxXPercent"), new GUIContent("Правая граница X"));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(
                    layer.FindPropertyRelative("MinYPercent"), new GUIContent("Верхняя граница Y"));
                EditorGUILayout.PropertyField(
                    layer.FindPropertyRelative("MaxYPercent"), new GUIContent("Нижняя граница Y"));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(
                    layer.FindPropertyRelative("Order"),
                    new GUIContent("Order", "Меньший Order — ниже. Base Map всегда ниже всех Art Layers."));
                EditorGUILayout.PropertyField(
                    layer.FindPropertyRelative("Opacity"), new GUIContent("Opacity"));
                EditorGUILayout.PropertyField(
                    layer.FindPropertyRelative("FitMode"),
                    new GUIContent(
                        "Fit Mode",
                        "Preserve Aspect — вписать с сохранением пропорций (letterbox внутри Bounds). " +
                        "Stretch — растянуть на весь Bounds, искажая пропорции."));

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4f);
            }

            if (GUILayout.Button("+ ДОБАВИТЬ СЛОЙ", GUILayout.Height(26f)))
            {
                int index = layersProp.arraySize;
                layersProp.InsertArrayElementAtIndex(index);
                SerializedProperty layer = layersProp.GetArrayElementAtIndex(index);
                layer.FindPropertyRelative("Id").stringValue = "art-layer-" + (index + 1);
                layer.FindPropertyRelative("DisplayName").stringValue = "Новый слой";
                layer.FindPropertyRelative("Sprite").objectReferenceValue = null;
                layer.FindPropertyRelative("Enabled").boolValue = true;
                layer.FindPropertyRelative("MinXPercent").floatValue = 0f;
                layer.FindPropertyRelative("MaxXPercent").floatValue = 100f;
                layer.FindPropertyRelative("MinYPercent").floatValue = 0f;
                layer.FindPropertyRelative("MaxYPercent").floatValue = 100f;
                layer.FindPropertyRelative("Order").intValue = 0;
                layer.FindPropertyRelative("Opacity").floatValue = 1f;
                layer.FindPropertyRelative("FitMode").enumValueIndex = 0;
            }
        }

        private static void DrawDefaultIconSection(SerializedObject themeSO)
        {
            EditorGUILayout.LabelField("Резервная иконка", EditorStyles.boldLabel);

            SerializedProperty iconLibraryProp = themeSO.FindProperty("iconLibrary");
            WorldMapIconLibrary iconLibrary = iconLibraryProp.objectReferenceValue as WorldMapIconLibrary;

            EditorGUILayout.PropertyField(iconLibraryProp, new GUIContent("Icon Library"));

            if (iconLibrary == null)
            {
                EditorGUILayout.HelpBox(
                    "У темы не назначена Icon Library — создайте ассет через " +
                    "Assets → Create → Kingdom Survival → Карта → Icon Library и назначьте сюда.",
                    MessageType.Info);
                return;
            }

            SerializedObject iconSO = new SerializedObject(iconLibrary);
            iconSO.Update();
            EditorGUILayout.PropertyField(
                iconSO.FindProperty("defaultLocationIcon"),
                new GUIContent("Иконка по умолчанию"));
            EditorGUILayout.HelpBox(
                "Используется, если у конкретной локации во вкладке «Локации» не назначена своя иконка.",
                MessageType.None);
            iconSO.ApplyModifiedProperties();
        }

        private void DrawLocationsSection()
        {
            if (database == null)
            {
                EditorGUILayout.HelpBox("Выберите World Map Database сверху.", MessageType.Info);
                return;
            }

            SerializedObject databaseSO = new SerializedObject(database);
            databaseSO.Update();
            SerializedProperty locationsProp = databaseSO.FindProperty("locations");

            EditorGUILayout.LabelField(
                $"Локации новой игры ({locationsProp.arraySize})",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Записи отсюда создают реальные LocationData при старте новой партии. " +
                "Пустой слот означает случайную авторскую зону; конкретный слот фиксирует регион появления.",
                MessageType.None);

            locationsScroll = EditorGUILayout.BeginScrollView(locationsScroll);

            for (int i = 0; i < locationsProp.arraySize; i++)
            {
                SerializedProperty location = locationsProp.GetArrayElementAtIndex(i);
                SerializedProperty id = location.FindPropertyRelative("id");
                SerializedProperty displayName = location.FindPropertyRelative("displayName");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(displayName.stringValue)
                        ? $"Локация {i + 1}"
                        : displayName.stringValue,
                    EditorStyles.boldLabel);
                if (GUILayout.Button("Удалить", GUILayout.Width(80f)))
                {
                    locationsProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(id, new GUIContent("ID"));
                EditorGUILayout.PropertyField(displayName, new GUIContent("Название"));
                EditorGUILayout.PropertyField(
                    location.FindPropertyRelative("interactionDescription"),
                    new GUIContent("Описание"));
                EditorGUILayout.PropertyField(
                    location.FindPropertyRelative("threat"),
                    new GUIContent("Угроза"));
                EditorGUILayout.PropertyField(
                    location.FindPropertyRelative("explorationHours"),
                    new GUIContent("Исследование, часов"));

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Награда", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(
                    location.FindPropertyRelative("rewardArmyGold"),
                    new GUIContent("Золото отряда"));
                EditorGUILayout.PropertyField(
                    location.FindPropertyRelative("rewardArmySupply"),
                    new GUIContent("Припасы отряда"));

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Появление", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(
                    location.FindPropertyRelative("initiallyVisibleOnMap"),
                    new GUIContent("Видима на карте"));
                EditorGUILayout.PropertyField(
                    location.FindPropertyRelative("initiallyDiscovered"),
                    new GUIContent("Сразу обнаружена"));

                SerializedProperty modeProp = location.FindPropertyRelative("mode");
                EditorGUILayout.PropertyField(modeProp, new GUIContent("Placement Mode"));

                WorldMapPlacementMode mode = (WorldMapPlacementMode)modeProp.enumValueIndex;
                if (mode == WorldMapPlacementMode.Fixed)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(
                        location.FindPropertyRelative("fixedXPercent"), new GUIContent("X, %"));
                    EditorGUILayout.PropertyField(
                        location.FindPropertyRelative("fixedYPercent"), new GUIContent("Y, %"));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.HelpBox(
                        "Fixed — точные координаты, не зависят от WorldSeed. Использовать для " +
                        "сюжетно важных мест (мельница, брод завязки), которые нельзя случайно " +
                        "унести в другой регион.",
                        MessageType.None);
                }
                else if (mode == WorldMapPlacementMode.Anchored)
                {
                    DrawSpawnSlotField(location.FindPropertyRelative("spawnSlotId"));
                    DrawTagsField(
                        location.FindPropertyRelative("requiredSlotTags"),
                        "Требуемые теги слота (через запятую, напр. Forest, Shore)");
                    EditorGUILayout.HelpBox(
                        "Если указан конкретный слот выше — теги игнорируются. Иначе локация " +
                        "выбирает случайный слот только среди тех, что содержат ВСЕ перечисленные " +
                        "теги (см. вкладку «География» → «Слоты появления»). Пусто — любой слот.",
                        MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Temporary — появляется по условиям мира (зоны Encounter, AM-08, ещё не " +
                        "реализовано). В стартовое наполнение партии эта локация не попадёт.",
                        MessageType.Warning);
                }

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Иконка", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(
                    location.FindPropertyRelative("icon"),
                    new GUIContent("Спрайт"));
                EditorGUILayout.PropertyField(
                    location.FindPropertyRelative("iconTint"),
                    new GUIContent("Оттенок"));
                SerializedProperty iconScale =
                    location.FindPropertyRelative("iconScale");
                iconScale.floatValue = EditorGUILayout.Slider(
                    "Масштаб",
                    Mathf.Clamp(iconScale.floatValue, 0.25f, 3f),
                    0.25f,
                    3f);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(6f);
            }

            if (GUILayout.Button("+ ДОБАВИТЬ ЛОКАЦИЮ", GUILayout.Height(28f)))
                AddLocation(locationsProp);

            EditorGUILayout.EndScrollView();
            databaseSO.ApplyModifiedProperties();
        }

        private static void DrawSpawnSlotField(SerializedProperty slotIdProp)
        {
            List<string> ids = new List<string> { string.Empty };
            List<string> labels = new List<string> { "Случайный слот" };

            foreach (WorldMapSpawnSlotDefinition slot in WorldMapSpawnSlotRegistry.StartingLocationSlots)
            {
                ids.Add(slot.Id);
                labels.Add(slot.Id);
            }

            int selected = Mathf.Max(0, ids.IndexOf(slotIdProp.stringValue));
            int next = EditorGUILayout.Popup("Зона появления", selected, labels.ToArray());
            slotIdProp.stringValue = ids[next];
        }

        private static void AddLocation(SerializedProperty locationsProp)
        {
            int index = locationsProp.arraySize;
            locationsProp.InsertArrayElementAtIndex(index);
            SerializedProperty location = locationsProp.GetArrayElementAtIndex(index);
            location.FindPropertyRelative("id").stringValue = "location-" + (index + 1);
            location.FindPropertyRelative("displayName").stringValue = "Новая локация";
            location.FindPropertyRelative("interactionDescription").stringValue = string.Empty;
            location.FindPropertyRelative("threat").stringValue = "неизвестна";
            location.FindPropertyRelative("explorationHours").doubleValue = 0.0;
            location.FindPropertyRelative("rewardArmyGold").intValue = 0;
            location.FindPropertyRelative("rewardArmySupply").intValue = 0;
            location.FindPropertyRelative("initiallyDiscovered").boolValue = false;
            location.FindPropertyRelative("initiallyVisibleOnMap").boolValue = true;
            location.FindPropertyRelative("spawnSlotId").stringValue = string.Empty;
            location.FindPropertyRelative("requiredSlotTags").ClearArray();
            location.FindPropertyRelative("mode").enumValueIndex = (int)WorldMapPlacementMode.Anchored;
            location.FindPropertyRelative("fixedXPercent").floatValue = 50f;
            location.FindPropertyRelative("fixedYPercent").floatValue = 50f;
            location.FindPropertyRelative("icon").objectReferenceValue = null;
            location.FindPropertyRelative("iconTint").colorValue = Color.white;
            location.FindPropertyRelative("iconScale").floatValue = 1f;
        }

        // ------------------------------------------------------------------
        // Validate
        // ------------------------------------------------------------------

        private void DrawValidateSection()
        {
            EditorGUILayout.LabelField("Validate", EditorStyles.boldLabel);

            if (GUILayout.Button("ПРОВЕРИТЬ БАЗУ"))
            {
                issues = CollectIssues(database);
                validated = true;
            }

            if (!validated)
                return;

            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Проблем не найдено.", MessageType.Info);
                return;
            }

            issuesScroll = EditorGUILayout.BeginScrollView(issuesScroll, GUILayout.Height(120f));
            foreach (string issue in issues)
                EditorGUILayout.HelpBox(issue, MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }

        private static List<string> CollectIssues(WorldMapDatabaseAsset database)
        {
            List<string> result = new List<string>();

            if (database == null)
            {
                result.Add("Не выбран World Map Database.");
                return result;
            }

            WorldMapVisualTheme theme = database.ActiveTheme;
            if (theme == null)
                result.Add("У базы не назначена Active Theme.");
            else if (theme.IconLibrary == null)
                result.Add("У темы не назначена Icon Library.");

            HashSet<string> locationIds = new HashSet<string>();
            foreach (WorldMapLocationDefinition location in database.Locations)
            {
                if (location == null)
                {
                    result.Add("В списке локаций есть пустая запись.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(location.Id))
                    result.Add("У локации не заполнен ID.");
                else if (!locationIds.Add(location.Id))
                    result.Add($"Дублирующийся ID локации: '{location.Id}'.");

                if (string.IsNullOrWhiteSpace(location.DisplayName))
                    result.Add($"У локации '{location.Id}' не заполнено название.");

                if (location.ExplorationHours < 0.0)
                    result.Add($"У локации '{location.Id}' отрицательное время исследования.");

                if (location.RewardArmyGold < 0 || location.RewardArmySupply < 0)
                    result.Add($"У локации '{location.Id}' отрицательная награда.");

                if (!string.IsNullOrWhiteSpace(location.SpawnSlotId) &&
                    WorldMapSpawnSlotRegistry.Find(location.SpawnSlotId) == null)
                {
                    result.Add(
                        $"Локация '{location.Id}' ссылается на неизвестный Spawn Slot " +
                        $"'{location.SpawnSlotId}'.");
                }

                if (location.IconScale < 0.25f || location.IconScale > 3f)
                    result.Add($"Масштаб иконки '{location.Id}' должен быть в диапазоне 0,25–3.");

                if (location.Mode == WorldMapPlacementMode.Fixed &&
                    (location.FixedXPercent < 0f || location.FixedXPercent > 100f ||
                     location.FixedYPercent < 0f || location.FixedYPercent > 100f))
                {
                    result.Add($"У Fixed-локации '{location.Id}' координаты вне диапазона 0..100%.");
                }
            }

            if (database.Locations.Count == 0)
                result.Add("В базе нет ни одной локации — будет использован Core fallback.");

            CollectWorldIssues(database.ActiveWorld, result);

            if (theme == null)
                return result;

            if (theme.IconLibrary != null)
            {
                HashSet<string> seenIds = new HashSet<string>();
                foreach (WorldMapLocationIconEntry entry in theme.IconLibrary.LocationIcons)
                {
                    if (entry == null)
                        continue;

                    if (string.IsNullOrEmpty(entry.LocationId))
                        result.Add("В Icon Library есть запись с пустым LocationId.");
                    else if (!seenIds.Add(entry.LocationId))
                        result.Add($"Дублирующийся LocationId в Icon Library: '{entry.LocationId}'.");

                    if (entry.Icon == null)
                    {
                        result.Add(
                            $"LocationId '{entry.LocationId}' в Icon Library без иконки.");
                    }
                }
            }

            CollectArtLayerIssues(theme?.ArtLayers, result);

            return result;
        }

        // Задача "Map Art Layers", раздел 16. Публичный static, принимает
        // список напрямую (не ScriptableObject) — используется и здесь, и
        // напрямую из EditMode-тестов без создания ассета.
        public static void CollectArtLayerIssues(
            IReadOnlyList<WorldMapArtLayerEntry> layers, List<string> result)
        {
            if (layers == null)
                return;

            HashSet<string> seenIds = new HashSet<string>();
            HashSet<int> seenOrders = new HashSet<int>();
            HashSet<int> ambiguousOrders = new HashSet<int>();

            foreach (WorldMapArtLayerEntry layer in layers)
            {
                if (layer == null)
                {
                    result.Add("В Map Art Layers есть пустая запись.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(layer.Id))
                    result.Add("У Art Layer не заполнен ID.");
                else if (!seenIds.Add(layer.Id))
                    result.Add($"Дублирующийся ID Art Layer: '{layer.Id}'.");

                if (layer.Enabled && layer.Sprite == null)
                    result.Add($"Активный Art Layer '{layer.Id}' без Sprite.");

                if (layer.MinXPercent >= layer.MaxXPercent)
                    result.Add($"У Art Layer '{layer.Id}' Min X >= Max X.");
                if (layer.MinYPercent >= layer.MaxYPercent)
                    result.Add($"У Art Layer '{layer.Id}' Min Y >= Max Y.");

                bool fullyOutside =
                    layer.MaxXPercent <= 0f || layer.MinXPercent >= 100f ||
                    layer.MaxYPercent <= 0f || layer.MinYPercent >= 100f;
                if (fullyOutside)
                    result.Add($"Art Layer '{layer.Id}' полностью вне карты (0..100%).");

                if (layer.Opacity <= 0f)
                    result.Add($"У Art Layer '{layer.Id}' Opacity <= 0 — слой невидим.");

                const float minReasonableSizePercent = 0.5f;
                if (layer.MaxXPercent - layer.MinXPercent < minReasonableSizePercent ||
                    layer.MaxYPercent - layer.MinYPercent < minReasonableSizePercent)
                {
                    result.Add($"Art Layer '{layer.Id}' имеет очень маленький Bounds (< 0.5%).");
                }

                if (layer.Enabled && !seenOrders.Add(layer.Order))
                    ambiguousOrders.Add(layer.Order);
            }

            foreach (int order in ambiguousOrders)
                result.Add($"Несколько активных Art Layers имеют одинаковый Order ({order}) — порядок среди них зависит от позиции в списке.");
        }

        private static void CollectWorldIssues(
            WorldMapWorldDefinitionAsset world,
            List<string> result)
        {
            if (world == null)
                return;

            if (string.IsNullOrWhiteSpace(world.WorldDefinitionId))
                result.Add("У Active World не заполнен World Definition Id.");

            if (world.HomeXPercent < 0f || world.HomeXPercent > 100f ||
                world.HomeYPercent < 0f || world.HomeYPercent > 100f)
            {
                result.Add("Координаты Дома в Active World должны быть в диапазоне 0..100%.");
            }

            // 12И: разметка клеток вместо зон и дорог.
            if (world.HasLegacyMarkup)
            {
                result.Add("В Active World остались старые зоны местности или дороги — перенесите их " +
                           "в разметку клеток (География → Местность → «Перенести старую разметку»).");
            }

            WorldMapHexGrid markupGrid = world.CreateGrid();
            if (markupGrid.Columns * markupGrid.Rows > 400000)
                result.Add("Сетка Active World слишком мелкая (больше 400 000 клеток) — увеличьте размер клетки.");

            HashSet<string> slotIds = new HashSet<string>();
            foreach (WorldMapWorldDefinitionAsset.SpawnSlotEntry slot in world.SpawnSlots)
            {
                if (slot == null)
                {
                    result.Add("В географии Active World есть пустой слот появления.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(slot.Id))
                    result.Add("У слота появления не заполнен ID.");
                else if (!slotIds.Add(slot.Id))
                    result.Add($"Дублирующийся ID слота появления: '{slot.Id}'.");

                if (slot.MinXPercent >= slot.MaxXPercent || slot.MinYPercent >= slot.MaxYPercent)
                {
                    result.Add(
                        $"Слот появления '{slot.Id}': Min должен быть строго меньше Max по обеим осям.");
                }
            }

        }

        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        private void DrawPreviewSection()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            bool hasAuthoredWorld = database != null && database.ActiveWorld != null;

            // AM-07.5: Seed никогда больше не определяет рельеф (процедурной
            // генерации не существует) — только будущее наполнение малых
            // локаций (AM-04/08, зоны). Поле оставлено неактивным, пока
            // наполнение по seed не подключено к превью.
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
                previewSeed = EditorGUILayout.IntField("Seed наполнения (пока не используется)", previewSeed);
            if (GUILayout.Button("REGENERATE", GUILayout.Width(120f)))
            {
                ApplyPreviewGeography();
                InvalidateTerrainPaintCache();
            }
            EditorGUILayout.EndHorizontal();

            if (hasAuthoredWorld)
            {
                EditorGUILayout.HelpBox(
                    $"Активен авторский мир '{database.ActiveWorld.WorldDefinitionId}' — местность постоянная " +
                    "и размечена вручную (режим «Местность»), не зависит от Seed.",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Без Active World вся карта — сплошная открытая местность: процедурной " +
                    "генерации местности нет ни в каком виде.",
                    MessageType.None);
            }

            DrawPreviewLayerToggles();
            DrawPreviewModeToolbar();
            DrawPreviewViewportControls();

            Sprite backgroundSprite = database != null && database.ActiveTheme != null
                ? database.ActiveTheme.BaseMapSprite
                : null;

            if (previewEditMode == PreviewEditMode.Terrain && hasAuthoredWorld)
                DrawTerrainPaintBanner(database.ActiveWorld);

            // Задача "Global Map Aspect + Preview Canvas Navigation"
            // (WM-T04.9, раздел 8/9): canvas занимает всё оставшееся место
            // окна — GUILayout.ExpandHeight(true) вместо жёсткой высоты;
            // минимум подстрахован на случай вырожденного layout-прохода.
            Rect canvasArea = GUILayoutUtility.GetRect(
                0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (canvasArea.height < MinPreviewCanvasHeight)
                canvasArea.height = MinPreviewCanvasHeight;

            // Раздел 1/5 задачи: аспект глобальной карты — ТОЛЬКО из World
            // Definition, никогда из BaseMapSprite/размера окна. Без
            // авторского мира используется тот же default, что и у нового
            // World Definition (4160×2560) — не 0/1, чтобы Preview не
            // деформировался и без Active World.
            float globalAspect = hasAuthoredWorld
                ? database.ActiveWorld.GlobalMapAspect
                : WorldMapWorldDefinitionAsset.DefaultMapCanvasWidth / WorldMapWorldDefinitionAsset.DefaultMapCanvasHeight;

            // Раздел 17/22 задачи: GUI.BeginGroup даёт И clip (карту при
            // zoom/pan не видно поверх тулбара выше), И бесплатный перевод
            // Event.current.mousePosition в локальные координаты канваса —
            // ни один из существующих Draw*/Handle*EditingInput методов не
            // меняется, они как и раньше получают один Rect mapRect.
            GUI.BeginGroup(canvasArea);
            Rect localCanvasRect = new Rect(0f, 0f, canvasArea.width, canvasArea.height);
            EditorGUI.DrawRect(localCanvasRect, new Color(0.03f, 0.03f, 0.03f));

            Rect fitRect = WorldMapPreviewMath.ComputeMapRect(localCanvasRect, globalAspect, true);
            Rect mapRect = WorldMapPreviewMath.ApplyZoomPan(fitRect, previewZoom, previewPanFraction);
            lastPreviewFitRect = fitRect;
            lastPreviewCanvasRect = localCanvasRect;
            lastPreviewReferenceWidth = hasAuthoredWorld
                ? database.ActiveWorld.MapCanvasWidth
                : WorldMapWorldDefinitionAsset.DefaultMapCanvasWidth;

            bool showArtNow = previewShowArt && backgroundSprite != null;
            if (showArtNow)
                DrawPreviewBackgroundSprite(mapRect, backgroundSprite);
            else
                EditorGUI.DrawRect(mapRect, new Color(0.08f, 0.08f, 0.08f));

            if (previewShowArtLayers && database != null && database.ActiveTheme != null)
                DrawPreviewArtLayers(mapRect, database.ActiveTheme.ArtLayers);

            if ((previewShowTerrainMarkup || previewEditMode == PreviewEditMode.Terrain) && hasAuthoredWorld)
                DrawPreviewTerrainMarkup(mapRect, database.ActiveWorld);

            if (previewShowSpawnSlots && hasAuthoredWorld)
                DrawPreviewSpawnSlots(mapRect, database.ActiveWorld);

            if (previewShowLocations)
                DrawPreviewLocations(mapRect);


            // Раздел 17/26 задачи: zoom (wheel) и pan (MMB) работают
            // независимо от PreviewEditMode — button==2/ScrollWheel никогда
            // не пересекаются с button==0, которым пользуются все три
            // режима авторинга ниже, поэтому конфликтов нет структурно, без
            // явного gate.
            HandlePreviewViewportInput(localCanvasRect, fitRect);

            // Задача "Terrain Area Preview Authoring" (WM-T04.8, раздел 2):
            // единственный активный режим редактирования на уровне вызова —
            // ни один из трёх обработчиков не может сработать одновременно
            // с другим, независимо от порядка current.Use().
            switch (previewEditMode)
            {
                case PreviewEditMode.ArtLayers:
                    if (database != null && database.ActiveTheme != null)
                        HandleArtLayerEditingInput(mapRect, database.ActiveTheme.ArtLayers);
                    break;
                case PreviewEditMode.Terrain:
                    if (hasAuthoredWorld)
                        HandleTerrainPaintInput(mapRect, database.ActiveWorld);
                    break;
                case PreviewEditMode.View:
                default:
                    break; // чистый просмотр — никакого взаимодействия мышью
            }

            GUI.EndGroup();
        }

        private void DrawPreviewViewportControls()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Вписать карту", GUILayout.Width(130f)))
            {
                previewZoom = 1f;
                previewPanFraction = Vector2.zero;
                SavePreviewViewportPrefs();
                Repaint();
            }
            if (GUILayout.Button("1×", GUILayout.Width(40f)))
                SetPreviewOneToOneZoom();
            GUILayout.Label("Zoom: " + Mathf.RoundToInt(previewZoom * 100f) + "%", GUILayout.Width(90f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                "Колесо мыши — zoom вокруг курсора. Средняя кнопка мыши — pan.",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        // Раздел 20 задачи: "1×" — один reference-пиксель Map Canvas
        // (World Definition → Reference Width/Height) соответствует одному
        // экранному пикселю. Использует fitRect/canvasRect ПРЕДЫДУЩЕГО
        // кадра (последний известный на момент клика — сам canvas ещё не
        // пересчитан в этом вызове OnGUI) — стандартный IMGUI паттерн,
        // расхождение с текущим кадром практически никогда не заметно и
        // самокорректируется следующим Repaint.
        private void SetPreviewOneToOneZoom()
        {
            if (lastPreviewFitRect.width <= 0f)
                return;

            float referenceWidth = lastPreviewReferenceWidth > 0f
                ? lastPreviewReferenceWidth
                : WorldMapWorldDefinitionAsset.DefaultMapCanvasWidth;

            float newZoom = referenceWidth / lastPreviewFitRect.width;
            ApplyZoomKeepingPointFixed(lastPreviewFitRect, newZoom, lastPreviewCanvasRect.center);
            SavePreviewViewportPrefs();
            Repaint();
        }

        private void ApplyZoomKeepingPointFixed(Rect fitRect, float newZoom, Vector2 anchorLocalPoint)
        {
            if (fitRect.width <= 0f || fitRect.height <= 0f)
                return;

            float clampedNewZoom = ClampPreviewZoom(newZoom);
            Rect currentDisplayRect = WorldMapPreviewMath.ApplyZoomPan(fitRect, previewZoom, previewPanFraction);
            float factor = clampedNewZoom / Mathf.Max(0.0001f, previewZoom);
            Rect zoomedRect = WorldMapPreviewMath.ZoomRectAroundPoint(currentDisplayRect, factor, anchorLocalPoint);
            WorldMapPreviewMath.ExtractZoomPan(fitRect, zoomedRect, out previewZoom, out previewPanFraction);
            previewZoom = ClampPreviewZoom(previewZoom);
        }

        // Раздел 15/17 задачи: zoom только когда курсор над canvas; pan
        // (MMB) не проверяет границы на продолжении drag — как и остальные
        // drag-инструменты в этом окне, "отпустить за пределами" — нормально.
        private void HandlePreviewViewportInput(Rect localCanvasRect, Rect fitRect)
        {
            Event current = Event.current;
            if (current == null)
                return;

            if (current.type == EventType.ScrollWheel && localCanvasRect.Contains(current.mousePosition))
            {
                float zoomFactor = Mathf.Pow(1.1f, -current.delta.y);
                float newZoom = previewZoom * zoomFactor;
                ApplyZoomKeepingPointFixed(fitRect, newZoom, current.mousePosition);
                SavePreviewViewportPrefs();
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 2 &&
                localCanvasRect.Contains(current.mousePosition))
            {
                isPanningPreview = true;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && current.button == 2 && isPanningPreview)
            {
                if (fitRect.width > 0f && fitRect.height > 0f)
                {
                    previewPanFraction += new Vector2(
                        current.delta.x / fitRect.width, current.delta.y / fitRect.height);
                }
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp && current.button == 2 && isPanningPreview)
            {
                isPanningPreview = false;
                SavePreviewViewportPrefs();
                current.Use();
            }
        }

        private void DrawPreviewModeToolbar()
        {
            EditorGUILayout.LabelField("Режим редактирования", EditorStyles.miniBoldLabel);
            PreviewEditMode newMode = (PreviewEditMode)GUILayout.Toolbar(
                (int)previewEditMode,
                new[] { "Просмотр", "Art Layers", "Местность" });

            if (newMode != previewEditMode)
            {
                // Раздел 2/3/4 задачи: смена режима обрывает любой активный
                // drag/resize/armed-create предыдущего режима — иначе
                // MouseUp, пришедший уже в другом режиме, применил бы
                // фантомное изменение к чужому объекту.
                isDraggingArtLayer = false;
                isResizingArtLayer = false;
                EndTerrainPaintStroke();
                previewEditMode = newMode;
            }
        }

        private void DrawPreviewLayerToggles()
        {
            EditorGUILayout.LabelField("Отображение", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            DrawLayerToggle(ref previewShowArt, "Base Map", PrefShowArt);
            DrawLayerToggle(ref previewShowArtLayers, "Map Art Layers", PrefShowArtLayers);
            DrawLayerToggle(ref previewShowTerrainMarkup, "Местность", PrefShowTerrainMarkup);
            DrawLayerToggle(ref previewShowLocations, "Locations", PrefShowLocations);
            DrawLayerToggle(ref previewShowSpawnSlots, "Spawn Slots", PrefShowSpawnSlots);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawLayerToggle(ref previewShowArtLayerBounds, "Art Layer Bounds", PrefShowArtLayerBounds);
            EditorGUILayout.EndHorizontal();

            DrawTerrainMarkupOpacitySlider();
        }

        private static void DrawLayerToggle(ref bool value, string label, string prefKey)
        {
            bool newValue = EditorGUILayout.ToggleLeft(label, value, GUILayout.Width(120f));
            if (newValue != value)
                EditorPrefs.SetBool(prefKey, newValue);
            value = newValue;
        }

        private void ApplyPreviewGeography()
        {
            if (database != null && database.ActiveWorld != null)
                WorldMapNavigation.ConfigureFromDefinition(database.ActiveWorld.ToData());
            else
                WorldMapNavigation.ConfigureDefaultTerrain();
        }

        // Раздел 3/20/21 задачи: источник — уже существующая тема
        // (WorldMapVisualTheme.BaseMapSprite), не отдельное поле. Sprite.rect
        // используется явно (не вся Texture) — корректно работает с
        // атласами/паддингом любого разрешения PNG.
        private static void DrawPreviewBackgroundSprite(Rect mapRect, Sprite sprite)
        {
            Texture2D texture = sprite.texture;
            if (texture == null)
                return;

            Rect spriteRect = sprite.rect;
            Rect uv = new Rect(
                spriteRect.x / texture.width,
                spriteRect.y / texture.height,
                spriteRect.width / texture.width,
                spriteRect.height / texture.height);

            GUI.DrawTextureWithTexCoords(mapRect, texture, uv, true);
        }

        // Задача "Map Art Layers": каждый слой рисуется ТОЛЬКО в своём
        // Bounds (через WorldMapPreviewMath.MapBoundsToRect — та же точка
        // перевода координат, что и everything else в Preview), не на всю
        // карту. Порядок и фильтрация Enabled/Sprite — через
        // WorldMapArtLayerUtility, единственный источник правды, общий с
        // runtime-рендером (PrototypeUIController.ApplyWorldMapArtLayers).
        private void DrawPreviewArtLayers(Rect mapRect, IReadOnlyList<WorldMapArtLayerEntry> layers)
        {
            List<WorldMapArtLayerEntry> ordered = WorldMapArtLayerUtility.GetOrderedEnabledLayers(layers);

            foreach (WorldMapArtLayerEntry layer in ordered)
            {
                Rect layerRect = WorldMapPreviewMath.MapBoundsToRect(
                    mapRect, layer.MinXPercent, layer.MinYPercent, layer.MaxXPercent, layer.MaxYPercent);

                DrawArtLayerSprite(layerRect, layer);
            }

            if (previewShowArtLayerBounds)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    if (i == selectedArtLayerIndex)
                        continue; // рамка выбранного слоя рисуется отдельно ниже, всегда.

                    WorldMapArtLayerEntry layer = layers[i];
                    if (layer == null)
                        continue;

                    Rect layerRect = WorldMapPreviewMath.MapBoundsToRect(
                        mapRect, layer.MinXPercent, layer.MinYPercent, layer.MaxXPercent, layer.MaxYPercent);
                    DrawRectOutline(layerRect, new Color(0.5f, 0.85f, 0.95f, 0.7f));
                }
            }

            // Раздел 5 задачи "Direct Art Layer Manipulation": рамка +
            // resize handles выбранного слоя видны ВСЕГДА (не только при
            // включённом debug-тумблере "Art Layer Bounds") — иначе нечем
            // управлять drag/resize. Не рисуем и не даём тянуть Disabled/
            // без-Sprite слой — раздел 23 задачи.
            if (selectedArtLayerIndex >= 0 && selectedArtLayerIndex < layers.Count)
            {
                WorldMapArtLayerEntry selected = layers[selectedArtLayerIndex];
                if (selected != null && selected.Enabled && selected.Sprite != null)
                {
                    Rect layerRect = WorldMapPreviewMath.MapBoundsToRect(
                        mapRect, selected.MinXPercent, selected.MinYPercent,
                        selected.MaxXPercent, selected.MaxYPercent);
                    DrawRectOutline(layerRect, Color.white);
                    GUI.Label(
                        new Rect(layerRect.x + 2f, layerRect.y + 2f, 200f, 16f),
                        string.IsNullOrWhiteSpace(selected.Id) ? "(без ID)" : selected.Id,
                        EditorStyles.whiteMiniLabel);
                    DrawArtLayerResizeHandles(layerRect);
                }
            }
        }

        // Раздел 5 задачи: маленькие квадраты по углам — фиксированный
        // экранный размер (не зависит от масштаба PNG/Bounds).
        private const float ArtLayerHandleScreenSize = 10f;

        private static void DrawArtLayerResizeHandles(Rect layerRect)
        {
            Color handleColor = Color.white;
            float half = ArtLayerHandleScreenSize * 0.5f;

            Vector2[] corners =
            {
                new Vector2(layerRect.xMin, layerRect.yMin),
                new Vector2(layerRect.xMax, layerRect.yMin),
                new Vector2(layerRect.xMin, layerRect.yMax),
                new Vector2(layerRect.xMax, layerRect.yMax)
            };

            foreach (Vector2 corner in corners)
            {
                Rect handleRect = new Rect(corner.x - half, corner.y - half, ArtLayerHandleScreenSize, ArtLayerHandleScreenSize);
                EditorGUI.DrawRect(handleRect, handleColor);
                DrawRectOutline(handleRect, Color.black);

                if (Event.current != null && Event.current.type == EventType.Repaint)
                {
                    EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ScaleArrow);
                }
            }
        }

        private static void DrawArtLayerSprite(Rect layerRect, WorldMapArtLayerEntry layer)
        {
            if (layer.Sprite == null || layer.Sprite.texture == null || layer.Opacity <= 0f)
                return;

            Texture2D texture = layer.Sprite.texture;
            Rect spriteRect = layer.Sprite.rect;
            Rect uv = new Rect(
                spriteRect.x / texture.width,
                spriteRect.y / texture.height,
                spriteRect.width / texture.width,
                spriteRect.height / texture.height);

            Rect drawRect = layerRect;
            if (layer.FitMode == WorldMapArtLayerFitMode.PreserveAspect &&
                spriteRect.height > 0f && layerRect.height > 0f)
            {
                float spriteAspect = spriteRect.width / spriteRect.height;
                drawRect = WorldMapPreviewMath.ComputeMapRect(layerRect, spriteAspect, true);
            }

            Color previous = GUI.color;
            Color tint = GUI.color;
            tint.a *= Mathf.Clamp01(layer.Opacity);
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(drawRect, texture, uv, true);
            GUI.color = previous;
        }

        // Задача "Map Art Layers — Direct Manipulation" (WM-T04.7), разделы
        // 3/6/9/13/19: единственный обработчик мыши для выбора/drag/resize
        // Art Layer в Preview. Вызывается ТОЛЬКО когда Road edit mode
        // выключен (см. DrawPreviewSection) — конфликт с добавлением Road
        // Point исключён структурно, не порядком current.Use().
        private void HandleArtLayerEditingInput(Rect mapRect, IReadOnlyList<WorldMapArtLayerEntry> layers)
        {
            Event current = Event.current;
            if (current == null || database == null || database.ActiveTheme == null)
                return;

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                if (!mapRect.Contains(current.mousePosition))
                    return;

                // 1) Ручки resize — только у уже выбранного слоя.
                if (selectedArtLayerIndex >= 0 && selectedArtLayerIndex < layers.Count)
                {
                    WorldMapArtLayerEntry selected = layers[selectedArtLayerIndex];
                    if (selected != null && selected.Enabled && selected.Sprite != null)
                    {
                        Rect layerRect = WorldMapPreviewMath.MapBoundsToRect(
                            mapRect, selected.MinXPercent, selected.MinYPercent,
                            selected.MaxXPercent, selected.MaxYPercent);

                        if (TryFindArtLayerCorner(layerRect, current.mousePosition, out ArtLayerCorner corner))
                        {
                            Undo.RecordObject(database.ActiveTheme, "Resize Art Layer");
                            originalArtLayerBounds = new MapBounds(
                                selected.MinXPercent, selected.MinYPercent,
                                selected.MaxXPercent, selected.MaxYPercent);
                            resizingArtLayerCorner = corner;
                            isResizingArtLayer = true;
                            isDraggingArtLayer = false;
                            current.Use();
                            Repaint();
                            return;
                        }
                    }
                }

                // 2) Тело слоя — среди пересекающихся выбираем самый верхний
                // по Order (раздел 3/L задачи): GetOrderedEnabledLayers уже
                // отсортирован по возрастанию Order, значит верхний — в
                // конце списка, перебираем в обратном порядке.
                int hitIndex = FindArtLayerIndexAtPoint(mapRect, layers, current.mousePosition);
                if (hitIndex >= 0)
                {
                    selectedArtLayerIndex = hitIndex;
                    WorldMapArtLayerEntry layer = layers[hitIndex];

                    Undo.RecordObject(database.ActiveTheme, "Move Art Layer");
                    originalArtLayerBounds = new MapBounds(
                        layer.MinXPercent, layer.MinYPercent, layer.MaxXPercent, layer.MaxYPercent);
                    artLayerDragStartMapPoint = WorldMapPreviewMath.PreviewToMap(mapRect, current.mousePosition);
                    isDraggingArtLayer = true;
                    isResizingArtLayer = false;
                    current.Use();
                    Repaint();
                    return;
                }

                // 3) Пустое место — снять выделение, ничего не создавать.
                selectedArtLayerIndex = -1;
                Repaint();
            }
            else if (current.type == EventType.MouseDrag && (isDraggingArtLayer || isResizingArtLayer))
            {
                if (selectedArtLayerIndex < 0 || selectedArtLayerIndex >= layers.Count)
                {
                    isDraggingArtLayer = false;
                    isResizingArtLayer = false;
                    return;
                }

                WorldMapArtLayerEntry layer = layers[selectedArtLayerIndex];
                Vector2 currentMapPoint = WorldMapPreviewMath.PreviewToMap(mapRect, current.mousePosition);

                MapBounds newBounds;
                if (isResizingArtLayer)
                {
                    float aspectRatio = layer.Sprite != null && layer.Sprite.rect.height > 0f
                        ? layer.Sprite.rect.width / layer.Sprite.rect.height
                        : 0f;
                    bool preserveAspect = layer.FitMode == WorldMapArtLayerFitMode.PreserveAspect;

                    // artLayerDragStartMapPoint не используется здесь (он
                    // нужен только для move) — resize считает абсолютную
                    // новую позицию угла напрямую от текущей мыши;
                    // ResizeBoundsFromCorner сам работает от НЕИЗМЕННОГО
                    // originalArtLayerBounds (раздел 7/9 задачи), поэтому
                    // накопления дрифта за долгий drag не возникает.
                    newBounds = WorldMapArtLayerBoundsMath.ResizeBoundsFromCorner(
                        originalArtLayerBounds, resizingArtLayerCorner, currentMapPoint,
                        preserveAspect, aspectRatio, MinArtLayerSizePercent);
                }
                else
                {
                    Vector2 totalDelta = currentMapPoint - artLayerDragStartMapPoint;
                    newBounds = WorldMapArtLayerBoundsMath.MoveBounds(
                        originalArtLayerBounds, totalDelta.x, totalDelta.y);
                }

                layer.MinXPercent = newBounds.MinX;
                layer.MinYPercent = newBounds.MinY;
                layer.MaxXPercent = newBounds.MaxX;
                layer.MaxYPercent = newBounds.MaxY;

                EditorUtility.SetDirty(database.ActiveTheme);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp && current.button == 0 &&
                     (isDraggingArtLayer || isResizingArtLayer))
            {
                isDraggingArtLayer = false;
                isResizingArtLayer = false;
                current.Use();
            }
        }

        private static bool TryFindArtLayerCorner(Rect layerRect, Vector2 screenPoint, out ArtLayerCorner corner)
        {
            float hitRadius = ArtLayerHandleScreenSize;
            (ArtLayerCorner Corner, Vector2 Point)[] corners =
            {
                (ArtLayerCorner.TopLeft, new Vector2(layerRect.xMin, layerRect.yMin)),
                (ArtLayerCorner.TopRight, new Vector2(layerRect.xMax, layerRect.yMin)),
                (ArtLayerCorner.BottomLeft, new Vector2(layerRect.xMin, layerRect.yMax)),
                (ArtLayerCorner.BottomRight, new Vector2(layerRect.xMax, layerRect.yMax))
            };

            float bestDistance = hitRadius;
            corner = ArtLayerCorner.BottomRight;
            bool found = false;

            foreach ((ArtLayerCorner Corner, Vector2 Point) candidate in corners)
            {
                float distance = Vector2.Distance(candidate.Point, screenPoint);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    corner = candidate.Corner;
                    found = true;
                }
            }

            return found;
        }

        // Раздел 3/L задачи: при перекрытии слоёв выбирается верхний по
        // Order (при равном Order — стабильный порядок из
        // GetOrderedEnabledLayers, тот же, что рисует runtime). Возвращает
        // индекс в ИСХОДНОМ (несортированном) списке — том же, которым
        // адресуется selectedArtLayerIndex и вкладка «Текстуры».
        // Публичный static — используется напрямую из EditMode-тестов
        // (раздел 27, пункты L/M), как и CollectArtLayerIssues.
        public static int FindArtLayerIndexAtPoint(
            Rect mapRect, IReadOnlyList<WorldMapArtLayerEntry> layers, Vector2 screenPoint)
        {
            List<WorldMapArtLayerEntry> ordered = WorldMapArtLayerUtility.GetOrderedEnabledLayers(layers);

            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                WorldMapArtLayerEntry layer = ordered[i];
                Rect layerRect = WorldMapPreviewMath.MapBoundsToRect(
                    mapRect, layer.MinXPercent, layer.MinYPercent, layer.MaxXPercent, layer.MaxYPercent);

                if (layerRect.Contains(screenPoint))
                {
                    for (int j = 0; j < layers.Count; j++)
                    {
                        if (ReferenceEquals(layers[j], layer))
                            return j;
                    }
                }
            }

            return -1;
        }

        private static void DrawPreviewSpawnSlots(Rect mapRect, WorldMapWorldDefinitionAsset world)
        {
            Color fill = new Color(0.32f, 0.55f, 0.92f, 0.16f);
            Color outline = new Color(0.45f, 0.68f, 1f, 0.85f);

            foreach (WorldMapWorldDefinitionAsset.SpawnSlotEntry slot in world.SpawnSlots)
            {
                if (slot == null)
                    continue;

                Vector2 min = WorldMapPreviewMath.MapToPreview(mapRect, slot.MinXPercent, slot.MinYPercent);
                Vector2 max = WorldMapPreviewMath.MapToPreview(mapRect, slot.MaxXPercent, slot.MaxYPercent);
                Rect rect = Rect.MinMaxRect(
                    Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                    Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));

                EditorGUI.DrawRect(rect, fill);
                DrawRectOutline(rect, outline);
            }
        }

        private static void DrawRectOutline(Rect rect, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawLine(new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin));
            Handles.DrawLine(new Vector3(rect.xMax, rect.yMin), new Vector3(rect.xMax, rect.yMax));
            Handles.DrawLine(new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMin, rect.yMax));
            Handles.DrawLine(new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMin, rect.yMin));
            Handles.EndGUI();
        }

        private void DrawPreviewLocations(Rect mapRect)
        {
            if (database == null)
                return;

            GameState previewState = new GameState();
            WorldMapDefinitionData previewWorldData =
                database.ActiveWorld != null ? database.ActiveWorld.ToData() : null;
            previewState.CreateNewGame(
                previewSeed,
                database.BuildRuntimeLocationTemplates(),
                previewWorldData);

            float homeXPercent = previewWorldData != null
                ? previewWorldData.HomeXPercent
                : WorldMapNavigation.CapitalXPercent;
            float homeYPercent = previewWorldData != null
                ? previewWorldData.HomeYPercent
                : WorldMapNavigation.CapitalYPercent;

            // Задача "регулируемый визуальный размер героя и Дома": тот же
            // WorldMapVisualTheme.HomeMarkerSizeCells, что и runtime — единый
            // источник истины, не отдельный фиксированный 6px квадрат.
            float homeMarkerSizeCells = database.ActiveTheme != null
                ? database.ActiveTheme.HomeMarkerSizeCells
                : WorldMapVisualTheme.DefaultHomeMarkerSizeCells;
            // 12И: доля клетки — ширина шестиугольника активного мира.
            WorldMapHexGrid homeGrid = WorldMapNavigation.Grid;
            float homeWidth = mapRect.width * (float)homeGrid.HexWidth / homeGrid.CanvasWidth * homeMarkerSizeCells;
            float homeHeight = mapRect.height * (float)homeGrid.HexWidth / homeGrid.CanvasHeight * homeMarkerSizeCells;

            Vector2 homePoint = WorldMapPreviewMath.MapToPreview(mapRect, homeXPercent, homeYPercent);
            Rect capitalRect = new Rect(
                homePoint.x - homeWidth * 0.5f, homePoint.y - homeHeight * 0.5f, homeWidth, homeHeight);
            EditorGUI.DrawRect(capitalRect, new Color(0.95f, 0.72f, 0.24f, 1f));

            foreach (LocationData location in previewState.Locations)
            {
                if (location == null)
                    continue;

                WorldMapLocationDefinition definition =
                    database.FindLocation(location.Id);
                float scale = definition != null
                    ? Mathf.Clamp(definition.IconScale, 0.25f, 3f)
                    : 1f;
                float size = 8f * scale;
                Vector2 point = WorldMapPreviewMath.MapToPreview(
                    mapRect, location.MapXPercent, location.MapYPercent);
                Rect markerRect = new Rect(
                    point.x - size * 0.5f, point.y - size * 0.5f, size, size);

                if (definition != null && definition.Icon != null)
                {
                    Texture preview = AssetPreview.GetAssetPreview(definition.Icon);
                    if (preview == null)
                        preview = AssetPreview.GetMiniThumbnail(definition.Icon);

                    Color previous = GUI.color;
                    Color tint = definition.IconTint;
                    if (!location.IsVisibleOnMap)
                        tint.a *= 0.35f;
                    GUI.color = tint;

                    if (preview != null)
                        GUI.DrawTexture(markerRect, preview, ScaleMode.ScaleToFit, true);
                    else
                        EditorGUI.DrawRect(markerRect, tint);

                    GUI.color = previous;
                }
                else
                {
                    Color fallback = new Color(0.90f, 0.82f, 0.62f, 1f);
                    if (!location.IsVisibleOnMap)
                        fallback.a = 0.35f;
                    EditorGUI.DrawRect(markerRect, fallback);
                }
            }
        }
    }
}
