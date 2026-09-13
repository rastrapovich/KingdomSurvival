using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // Редактор World Map Database: текстуры, реальные стартовые локации,
    // проверка конфигурации и быстрый preview без Play Mode. Полигональный
    // редактор регионов и Rule Tile по-прежнему не вводятся.
    public sealed class WorldMapDatabaseWindow : EditorWindow
    {
        private enum WindowTab
        {
            World,
            Textures,
            Geography,
            Locations,
            Validate,
            Preview
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

        // WM-T02: индекс дороги, которую сейчас редактируют кликом по карте
        // на вкладке «Предпросмотр» (-1 — ни одна не выбрана).
        private int editingRoadIndex = -1;

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
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);
            database = (WorldMapDatabaseAsset)EditorGUILayout.ObjectField(
                "World Map Database", database, typeof(WorldMapDatabaseAsset), false);

            EditorGUILayout.Space(6f);
            tab = (WindowTab)GUILayout.Toolbar(
                (int)tab,
                new[] { "Мир", "Текстуры", "География", "Локации", "Проверка", "Предпросмотр" });
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

            EditorGUILayout.EndScrollView();
            worldSO.ApplyModifiedProperties();
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

            DrawTerrainAreasSection(worldSO);
            EditorGUILayout.Space(14f);
            DrawSpawnSlotsSection(worldSO);
            EditorGUILayout.Space(14f);
            DrawGameplayTerrainSettingsSection(worldSO);
            EditorGUILayout.Space(14f);
            DrawRoadsSection(worldSO);

            EditorGUILayout.EndScrollView();
            worldSO.ApplyModifiedProperties();
        }

        // --------------------------------------------------------------
        // Типы местности (WM-T01) — задача "gameplay-география дорог".
        // --------------------------------------------------------------

        private static void DrawGameplayTerrainSettingsSection(SerializedObject worldSO)
        {
            EditorGUILayout.LabelField("Типы местности", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Проходимость и множитель скорости движения — независимо от Terrain выше " +
                "(тот считает только стоимость пути при построении маршрута). Здесь — живой " +
                "запрос текущей позиции героя (WorldMapGameplayTerrainQuery), проверяется на " +
                "каждом шаге движения.",
                MessageType.None);

            SerializedProperty settingsProp = worldSO.FindProperty("gameplayTerrainSettings");

            if (settingsProp.arraySize == 0 && GUILayout.Button("+ Заполнить значениями по умолчанию"))
            {
                WorldMapWorldDefinitionAsset world = (WorldMapWorldDefinitionAsset)worldSO.targetObject;
                Undo.RecordObject(world, "Fill Default Gameplay Terrain Settings");
                world.EditorEnsureDefaultGameplayTerrainSettings();
                worldSO.Update();
                settingsProp = worldSO.FindProperty("gameplayTerrainSettings");
            }

            for (int i = 0; i < settingsProp.arraySize; i++)
            {
                SerializedProperty entry = settingsProp.GetArrayElementAtIndex(i);
                SerializedProperty terrainProp = entry.FindPropertyRelative("Terrain");

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    ((WorldMapGameplayTerrainType)terrainProp.enumValueIndex).ToString(),
                    EditorStyles.boldLabel,
                    GUILayout.Width(110f));
                EditorGUILayout.PropertyField(
                    entry.FindPropertyRelative("Traversable"),
                    new GUIContent("Проходимость"),
                    GUILayout.Width(140f));
                EditorGUILayout.PropertyField(
                    entry.FindPropertyRelative("MovementMultiplier"),
                    new GUIContent("Скорость"));
                EditorGUILayout.EndHorizontal();
            }
        }

        // --------------------------------------------------------------
        // Дороги (WM-T02) — задача "gameplay-география дорог".
        // --------------------------------------------------------------

        private void DrawRoadsSection(SerializedObject worldSO)
        {
            EditorGUILayout.LabelField("Дороги", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Путь — центр уже нарисованной художником дороги; ширина — gameplay-зона в тех " +
                "же координатах карты (0..100%), не пиксели фона. Точки редактируются числами " +
                "здесь или добавляются кликом по карте на вкладке «Предпросмотр», когда дорога " +
                "выбрана для редактирования.",
                MessageType.None);

            SerializedProperty roadsProp = worldSO.FindProperty("roads");

            for (int i = 0; i < roadsProp.arraySize; i++)
            {
                SerializedProperty road = roadsProp.GetArrayElementAtIndex(i);
                SerializedProperty idProp = road.FindPropertyRelative("Id");
                SerializedProperty pointsProp = road.FindPropertyRelative("Points");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                bool isEditingThis = editingRoadIndex == i;
                EditorGUILayout.LabelField(
                    (string.IsNullOrWhiteSpace(idProp.stringValue) ? $"Дорога {i + 1}" : idProp.stringValue) +
                    (isEditingThis ? "  [редактируется]" : ""),
                    EditorStyles.boldLabel);
                if (GUILayout.Button("Удалить", GUILayout.Width(80f)))
                {
                    roadsProp.DeleteArrayElementAtIndex(i);
                    if (editingRoadIndex == i)
                        editingRoadIndex = -1;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(idProp, new GUIContent("ID"));
                EditorGUILayout.PropertyField(
                    road.FindPropertyRelative("DisplayName"), new GUIContent("Название"));
                EditorGUILayout.PropertyField(
                    road.FindPropertyRelative("Enabled"), new GUIContent("Активна"));
                EditorGUILayout.PropertyField(
                    road.FindPropertyRelative("Width"),
                    new GUIContent(
                        "Ширина",
                        "Ширина gameplay-зоны дороги в координатах карты (0..100% по обеим осям), " +
                        "не разрешение фоновой текстуры."));

                EditorGUILayout.LabelField($"Точек: {pointsProp.arraySize}");
                for (int p = 0; p < pointsProp.arraySize; p++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"#{p}", GUILayout.Width(28f));
                    EditorGUILayout.PropertyField(pointsProp.GetArrayElementAtIndex(p), GUIContent.none);
                    if (GUILayout.Button("✕", GUILayout.Width(22f)))
                    {
                        pointsProp.DeleteArrayElementAtIndex(p);
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("+ Добавить точку (числом)"))
                {
                    int index = pointsProp.arraySize;
                    pointsProp.InsertArrayElementAtIndex(index);
                    pointsProp.GetArrayElementAtIndex(index).vector2Value =
                        new Vector2(50f, 50f);
                }

                EditorGUILayout.BeginHorizontal();
                if (!isEditingThis)
                {
                    if (GUILayout.Button("Редактировать путь"))
                    {
                        editingRoadIndex = i;
                        tab = WindowTab.Preview;
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Клик по карте на вкладке «Предпросмотр» добавляет точку в конец пути.",
                        MessageType.Info);
                    if (GUILayout.Button("Готово"))
                        editingRoadIndex = -1;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4f);
            }

            if (GUILayout.Button("+ ДОБАВИТЬ ДОРОГУ", GUILayout.Height(26f)))
            {
                int index = roadsProp.arraySize;
                roadsProp.InsertArrayElementAtIndex(index);
                SerializedProperty road = roadsProp.GetArrayElementAtIndex(index);
                road.FindPropertyRelative("Id").stringValue = "road-" + (index + 1);
                road.FindPropertyRelative("DisplayName").stringValue = "Новая дорога";
                road.FindPropertyRelative("Enabled").boolValue = true;
                road.FindPropertyRelative("Width").floatValue = 1f;
                road.FindPropertyRelative("Points").ClearArray();
            }
        }

        private static void DrawTerrainAreasSection(SerializedObject worldSO)
        {
            EditorGUILayout.LabelField("Авторские зоны местности", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Прямоугольник в процентах карты (0..100) над уже нарисованной художником картой — " +
                "невидимая gameplay-разметка, не рисует местность. Terrain определяет стоимость пути " +
                "(Plains/Hills/Mountains); Tags — чисто описательные, для подбора совместимых слотов " +
                "(Forest, Shore, Road и т.п.), не влияют на скорость сами по себе. При равном Priority " +
                "побеждает последняя область в списке — вкладка «Проверка» предупредит о конфликте.",
                MessageType.None);

            SerializedProperty areasProp = worldSO.FindProperty("terrainAreas");

            for (int i = 0; i < areasProp.arraySize; i++)
            {
                SerializedProperty area = areasProp.GetArrayElementAtIndex(i);
                SerializedProperty idProp = area.FindPropertyRelative("Id");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(idProp.stringValue)
                        ? $"Зона {i + 1}"
                        : idProp.stringValue,
                    EditorStyles.boldLabel);
                if (GUILayout.Button("Удалить", GUILayout.Width(80f)))
                {
                    areasProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(idProp, new GUIContent("ID"));
                EditorGUILayout.PropertyField(
                    area.FindPropertyRelative("Terrain"), new GUIContent("Местность (стоимость пути)"));
                DrawTagsField(
                    area.FindPropertyRelative("Tags"),
                    "Теги (через запятую, напр. Forest, NearRoad)");
                EditorGUILayout.PropertyField(
                    area.FindPropertyRelative("Priority"), new GUIContent("Priority"));

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(
                    area.FindPropertyRelative("MinXPercent"), new GUIContent("Min X, %"));
                EditorGUILayout.PropertyField(
                    area.FindPropertyRelative("MaxXPercent"), new GUIContent("Max X, %"));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(
                    area.FindPropertyRelative("MinYPercent"), new GUIContent("Min Y, %"));
                EditorGUILayout.PropertyField(
                    area.FindPropertyRelative("MaxYPercent"), new GUIContent("Max Y, %"));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4f);
            }

            if (GUILayout.Button("+ ДОБАВИТЬ ЗОНУ МЕСТНОСТИ", GUILayout.Height(26f)))
            {
                int index = areasProp.arraySize;
                areasProp.InsertArrayElementAtIndex(index);
                SerializedProperty area = areasProp.GetArrayElementAtIndex(index);
                area.FindPropertyRelative("Id").stringValue = "area-" + (index + 1);
                area.FindPropertyRelative("Terrain").enumValueIndex = (int)WorldMapTerrainType.Hills;
                area.FindPropertyRelative("Tags").ClearArray();
                area.FindPropertyRelative("MinXPercent").floatValue = 40f;
                area.FindPropertyRelative("MaxXPercent").floatValue = 60f;
                area.FindPropertyRelative("MinYPercent").floatValue = 40f;
                area.FindPropertyRelative("MaxYPercent").floatValue = 60f;
                area.FindPropertyRelative("Priority").intValue = 0;
            }
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
                    new GUIContent("Снабжение отряда"));

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

            return result;
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

            HashSet<string> areaIds = new HashSet<string>();
            List<WorldMapWorldDefinitionAsset.TerrainAreaEntry> areas =
                new List<WorldMapWorldDefinitionAsset.TerrainAreaEntry>(world.TerrainAreas);

            foreach (WorldMapWorldDefinitionAsset.TerrainAreaEntry area in areas)
            {
                if (area == null)
                {
                    result.Add("В географии Active World есть пустая зона местности.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(area.Id))
                    result.Add("У зоны местности не заполнен ID.");
                else if (!areaIds.Add(area.Id))
                    result.Add($"Дублирующийся ID зоны местности: '{area.Id}'.");

                if (area.MinXPercent >= area.MaxXPercent || area.MinYPercent >= area.MaxYPercent)
                {
                    result.Add(
                        $"Зона местности '{area.Id}': Min должен быть строго меньше Max по обеим осям.");
                }
            }

            // Конфликт равных приоритетов — не зависит от порядка объектов в
            // списке, поэтому выявляется явной проверкой пересечений здесь,
            // а не оставляется на "последняя побеждает" в рантайме.
            for (int i = 0; i < areas.Count; i++)
            {
                for (int j = i + 1; j < areas.Count; j++)
                {
                    WorldMapWorldDefinitionAsset.TerrainAreaEntry a = areas[i];
                    WorldMapWorldDefinitionAsset.TerrainAreaEntry b = areas[j];
                    if (a == null || b == null || a.Priority != b.Priority)
                        continue;

                    bool overlaps =
                        a.MinXPercent < b.MaxXPercent && b.MinXPercent < a.MaxXPercent &&
                        a.MinYPercent < b.MaxYPercent && b.MinYPercent < a.MaxYPercent;

                    if (overlaps)
                    {
                        result.Add(
                            $"Зоны местности '{a.Id}' и '{b.Id}' пересекаются при одинаковом " +
                            $"Priority ({a.Priority}) — результат недетерминирован, задайте разный приоритет.");
                    }
                }
            }

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

            CollectRoadIssues(world, result);
        }

        // WM-T02 (раздел 17 задачи): проверки дорог — расширение
        // существующей Validate, не отдельная система.
        private static void CollectRoadIssues(
            WorldMapWorldDefinitionAsset world,
            List<string> result)
        {
            HashSet<string> roadIds = new HashSet<string>();
            bool hasRoadTerrainRule = false;

            foreach (WorldMapWorldDefinitionAsset.GameplayTerrainSettingsEntry entry in
                     world.GameplayTerrainSettings)
            {
                if (entry != null && entry.Terrain == WorldMapGameplayTerrainType.Road)
                    hasRoadTerrainRule = true;
            }

            foreach (WorldMapWorldDefinitionAsset.RoadEntry road in world.Roads)
            {
                if (road == null)
                {
                    result.Add("В географии Active World есть пустая дорога.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(road.Id))
                    result.Add("У дороги не заполнен ID.");
                else if (!roadIds.Add(road.Id))
                    result.Add($"Дублирующийся ID дороги: '{road.Id}'.");

                if (!road.Enabled)
                    continue; // Раздел 17: выключенная дорога — не ошибка.

                if (road.Width <= 0f)
                    result.Add($"Дорога '{road.Id}': Width должен быть больше нуля.");

                if (road.Points == null || road.Points.Count < 2)
                {
                    result.Add(
                        $"Дорога '{road.Id}': путь содержит меньше 2 точек — не участвует в gameplay.");
                }
                else
                {
                    foreach (Vector2 point in road.Points)
                    {
                        if (point.x < 0f || point.x > 100f || point.y < 0f || point.y > 100f)
                        {
                            result.Add(
                                $"Дорога '{road.Id}': точка ({point.x:0.##}, {point.y:0.##}) вне " +
                                "допустимого диапазона карты 0..100%.");
                            break;
                        }
                    }
                }

                if (!hasRoadTerrainRule)
                {
                    result.Add(
                        $"Дорога '{road.Id}' задана, но в «Типы местности» нет правила для Road — " +
                        "будет использован запасной множитель ×1.30 по умолчанию.");
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
                ApplyPreviewGeography();
            EditorGUILayout.EndHorizontal();

            if (hasAuthoredWorld)
            {
                EditorGUILayout.HelpBox(
                    $"Активен авторский мир '{database.ActiveWorld.WorldDefinitionId}' — рельеф постоянный " +
                    "и задан вручную (Terrain Areas на вкладке «География»), не зависит от Seed.",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Схематичный превью-грид ({WorldMapNavigation.GridWidth}×{WorldMapNavigation.GridHeight}), не финальный визуал. " +
                    "Без Active World вся карта — сплошная Plains: процедурной генерации местности " +
                    "больше нет ни в каком виде.",
                    MessageType.None);
            }

            EditorGUILayout.HelpBox(
                "Предпросмотр использует общий статический WorldMapNavigation — держите это окно " +
                "закрытым или на другой вкладке во время Play Mode с работающей партией, иначе Preview " +
                "временно подменит географию живой игры (известное ограничение, снимается в AM-10).",
                MessageType.Warning);

            ApplyPreviewGeography();

            Rect area = GUILayoutUtility.GetRect(
                position.width - 24f, 220f, GUILayout.ExpandWidth(false));

            DrawPreviewGrid(area);
            DrawPreviewRoads(area);
            HandlePreviewRoadEditingClick(area);
        }

        private void ApplyPreviewGeography()
        {
            if (database != null && database.ActiveWorld != null)
                WorldMapNavigation.ConfigureFromDefinition(database.ActiveWorld.ToData());
            else
                WorldMapNavigation.ConfigureDefaultTerrain();
        }

        private void DrawPreviewGrid(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.08f, 0.08f, 0.08f));

            float cellWidth = area.width / WorldMapNavigation.GridWidth;
            float cellHeight = area.height / WorldMapNavigation.GridHeight;

            for (int y = 0; y < WorldMapNavigation.GridHeight; y++)
            {
                for (int x = 0; x < WorldMapNavigation.GridWidth; x++)
                {
                    WorldMapTerrainType terrain = WorldMapNavigation.GetTerrainAtGridCell(x, y);
                    Color color = GetPreviewTerrainColor(terrain);

                    Rect cellRect = new Rect(
                        area.x + x * cellWidth,
                        area.y + y * cellHeight,
                        cellWidth + 1f,
                        cellHeight + 1f);

                    EditorGUI.DrawRect(cellRect, color);
                }
            }

            DrawPreviewLocations(area);
        }

        private void DrawPreviewLocations(Rect area)
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

            Rect capitalRect = new Rect(
                area.x + area.width * homeXPercent / 100f - 3f,
                area.y + area.height * homeYPercent / 100f - 3f,
                6f,
                6f);
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
                Rect markerRect = new Rect(
                    area.x + area.width * location.MapXPercent / 100f - size * 0.5f,
                    area.y + area.height * location.MapYPercent / 100f - size * 0.5f,
                    size,
                    size);

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

        // WM-T02/T03 (задача "gameplay-география дорог"): debug-визуализация
        // дорог — только в редакторе (раздел 9 задачи), не попадает в
        // обычный игровой экран карты. Показывает саму gameplay-ширину
        // (полупрозрачная полоса), не только центральную линию, чтобы было
        // видно, где именно герой физически считается "на дороге".
        private void DrawPreviewRoads(Rect area)
        {
            if (database == null || database.ActiveWorld == null)
                return;

            int index = 0;
            foreach (WorldMapWorldDefinitionAsset.RoadEntry road in database.ActiveWorld.Roads)
            {
                if (road?.Points != null && road.Points.Count >= 2)
                    DrawPreviewRoad(area, road, index == editingRoadIndex);
                index++;
            }
        }

        private static void DrawPreviewRoad(
            Rect area,
            WorldMapWorldDefinitionAsset.RoadEntry road,
            bool isBeingEdited)
        {
            Color lineColor = road.Enabled
                ? new Color(0.85f, 0.72f, 0.35f, 0.95f)
                : new Color(0.5f, 0.5f, 0.5f, 0.6f);
            Color zoneColor = road.Enabled
                ? new Color(0.85f, 0.72f, 0.35f, 0.18f)
                : new Color(0.5f, 0.5f, 0.5f, 0.10f);

            float halfWidthX = road.Width * 0.5f / 100f * area.width;
            float halfWidthY = road.Width * 0.5f / 100f * area.height;

            for (int i = 1; i < road.Points.Count; i++)
            {
                Vector2 a = PercentToPreviewPoint(area, road.Points[i - 1]);
                Vector2 b = PercentToPreviewPoint(area, road.Points[i]);

                DrawThickSegment(a, b, (halfWidthX + halfWidthY), zoneColor);
                Handles.BeginGUI();
                Handles.color = lineColor;
                Handles.DrawLine(a, b);
                Handles.EndGUI();
            }

            for (int i = 0; i < road.Points.Count; i++)
            {
                Vector2 p = PercentToPreviewPoint(area, road.Points[i]);
                Rect pointRect = new Rect(p.x - 3f, p.y - 3f, 6f, 6f);
                EditorGUI.DrawRect(pointRect, isBeingEdited ? Color.white : lineColor);
                GUI.Label(new Rect(p.x + 5f, p.y - 7f, 24f, 14f), i.ToString(), EditorStyles.miniLabel);
            }
        }

        private static Vector2 PercentToPreviewPoint(Rect area, Vector2 percentPoint)
        {
            return new Vector2(
                area.x + area.width * percentPoint.x / 100f,
                area.y + area.height * percentPoint.y / 100f);
        }

        private static Vector2 PercentToPreviewPoint(Rect area, MapPointData point)
        {
            return PercentToPreviewPoint(area, new Vector2(point.XPercent, point.YPercent));
        }

        private static void DrawThickSegment(Vector2 a, Vector2 b, float thickness, Color color)
        {
            if (thickness <= 0.01f)
                return;

            Vector2 direction = (b - a).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * (thickness * 0.5f);

            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawAAConvexPolygon(
                new Vector3(a.x + normal.x, a.y + normal.y),
                new Vector3(b.x + normal.x, b.y + normal.y),
                new Vector3(b.x - normal.x, b.y - normal.y),
                new Vector3(a.x - normal.x, a.y - normal.y));
            Handles.EndGUI();
        }

        // WM-T02 (раздел 8 задачи): дешёвая первая версия редактирования
        // пути — клик по Preview добавляет точку в конец пути редактируемой
        // дороги. Полноценный drag существующих точек мышью сознательно не
        // реализован в этом проходе (см. отчёт перед реализацией) — числовые
        // поля на вкладке «География» уже позволяют скорректировать любую
        // точку точно.
        private void HandlePreviewRoadEditingClick(Rect area)
        {
            if (editingRoadIndex < 0 ||
                database == null ||
                database.ActiveWorld == null ||
                editingRoadIndex >= database.ActiveWorld.Roads.Count)
            {
                return;
            }

            Event current = Event.current;
            if (current == null ||
                current.type != EventType.MouseDown ||
                current.button != 0 ||
                !area.Contains(current.mousePosition))
            {
                return;
            }

            float xPercent = Mathf.Clamp01((current.mousePosition.x - area.x) / area.width) * 100f;
            float yPercent = Mathf.Clamp01((current.mousePosition.y - area.y) / area.height) * 100f;

            SerializedObject worldSO = new SerializedObject(database.ActiveWorld);
            worldSO.Update();
            SerializedProperty road = worldSO.FindProperty("roads")
                .GetArrayElementAtIndex(editingRoadIndex);
            SerializedProperty points = road.FindPropertyRelative("Points");
            int index = points.arraySize;
            points.InsertArrayElementAtIndex(index);
            points.GetArrayElementAtIndex(index).vector2Value = new Vector2(xPercent, yPercent);
            worldSO.ApplyModifiedProperties();

            current.Use();
            Repaint();
        }

        // AM-07.5: это диагностическая раскраска невидимой gameplay-разметки
        // в редакторе (проверить, что авторские Terrain Areas легли туда,
        // куда задумано), а не превью финального визуала — финальный визуал
        // целиком в baseMapSprite темы.
        private static Color GetPreviewTerrainColor(WorldMapTerrainType terrain)
        {
            switch (terrain)
            {
                case WorldMapTerrainType.Hills:
                    return new Color(0.34f, 0.40f, 0.30f, 1f);
                case WorldMapTerrainType.Mountains:
                    return new Color(0.32f, 0.30f, 0.29f, 1f);
                default:
                    return new Color(0.16f, 0.17f, 0.15f, 1f);
            }
        }

    }
}
