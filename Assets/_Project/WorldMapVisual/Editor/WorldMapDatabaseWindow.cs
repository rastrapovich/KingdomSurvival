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

            EditorGUILayout.EndScrollView();
            worldSO.ApplyModifiedProperties();
        }

        private static void DrawTerrainAreasSection(SerializedObject worldSO)
        {
            EditorGUILayout.LabelField("Авторские зоны местности", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Прямоугольник в процентах карты (0..100) с типом местности и приоритетом. " +
                "При равном Priority побеждает последняя область в списке — вкладка «Проверка» " +
                "предупредит о таком конфликте.",
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
                    area.FindPropertyRelative("Terrain"), new GUIContent("Местность"));
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
                area.FindPropertyRelative("MinXPercent").floatValue = 40f;
                area.FindPropertyRelative("MaxXPercent").floatValue = 60f;
                area.FindPropertyRelative("MinYPercent").floatValue = 40f;
                area.FindPropertyRelative("MaxYPercent").floatValue = 60f;
                area.FindPropertyRelative("Priority").intValue = 0;
            }
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
            DrawTerrainProfilesSection(themeSO);
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
                "Фоновая текстура заполняет слой под местностью, маршрутами и маркерами. Река, " +
                "озёра, берега и прочая постоянная география рисуются прямо в этой текстуре " +
                "художником — код больше не генерирует и не рисует реку поверх карты.",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private static void DrawTerrainProfilesSection(SerializedObject themeSO)
        {
            EditorGUILayout.LabelField("Местность", EditorStyles.boldLabel);

            SerializedProperty profilesProp = themeSO.FindProperty("terrainProfiles");

            for (int i = 0; i < profilesProp.arraySize; i++)
            {
                SerializedProperty profileProp = profilesProp.GetArrayElementAtIndex(i);
                SerializedProperty terrainProp = profileProp.FindPropertyRelative("terrain");
                SerializedProperty colorProp = profileProp.FindPropertyRelative("cellColor");
                SerializedProperty massVariantsProp = profileProp.FindPropertyRelative("massVariants");

                WorldMapTerrainType terrainValue = (WorldMapTerrainType)terrainProp.enumValueIndex;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(terrainValue.ToString(), EditorStyles.boldLabel);
                if (GUILayout.Button("Удалить профиль", GUILayout.Width(140f)))
                {
                    profilesProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(colorProp, new GUIContent("Cell Color"));
                DrawSpriteList(massVariantsProp, "Mass Variants");

                EditorGUILayout.HelpBox(
                    "Cell Color — плоская заливка клетки, если Mass Variants пуст (WM-01). " +
                    "Если добавить спрайты — местность рисуется органичными массами по кластерам клеток, " +
                    "а не клетками (WM-04).",
                    MessageType.None);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4f);
            }

            DrawAddMissingTerrainProfileButton(profilesProp);
        }

        private static void DrawAddMissingTerrainProfileButton(SerializedProperty profilesProp)
        {
            HashSet<WorldMapTerrainType> present = new HashSet<WorldMapTerrainType>();
            for (int i = 0; i < profilesProp.arraySize; i++)
            {
                SerializedProperty terrainProp =
                    profilesProp.GetArrayElementAtIndex(i).FindPropertyRelative("terrain");
                present.Add((WorldMapTerrainType)terrainProp.enumValueIndex);
            }

            List<WorldMapTerrainType> missing = new List<WorldMapTerrainType>();
            foreach (WorldMapTerrainType terrain in System.Enum.GetValues(typeof(WorldMapTerrainType)))
            {
                if (!present.Contains(terrain))
                    missing.Add(terrain);
            }

            if (missing.Count == 0)
                return;

            EditorGUILayout.BeginHorizontal();
            foreach (WorldMapTerrainType terrain in missing)
            {
                if (!GUILayout.Button($"+ Добавить профиль {terrain}"))
                    continue;

                int newIndex = profilesProp.arraySize;
                profilesProp.InsertArrayElementAtIndex(newIndex);
                SerializedProperty newProfile = profilesProp.GetArrayElementAtIndex(newIndex);
                newProfile.FindPropertyRelative("terrain").enumValueIndex = (int)terrain;
                newProfile.FindPropertyRelative("cellColor").colorValue = new Color(0f, 0f, 0f, 0f);
                newProfile.FindPropertyRelative("massVariants").ClearArray();
            }
            EditorGUILayout.EndHorizontal();
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
            location.FindPropertyRelative("mode").enumValueIndex = (int)WorldMapPlacementMode.Anchored;
            location.FindPropertyRelative("fixedXPercent").floatValue = 50f;
            location.FindPropertyRelative("fixedYPercent").floatValue = 50f;
            location.FindPropertyRelative("icon").objectReferenceValue = null;
            location.FindPropertyRelative("iconTint").colorValue = Color.white;
            location.FindPropertyRelative("iconScale").floatValue = 1f;
        }

        private static void DrawSpriteList(SerializedProperty listProp, string label)
        {
            EditorGUILayout.LabelField($"{label} ({listProp.arraySize})");

            for (int i = 0; i < listProp.arraySize; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(listProp.GetArrayElementAtIndex(i), GUIContent.none);
                if (GUILayout.Button("✕", GUILayout.Width(22f)))
                {
                    listProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button($"+ Добавить {label}"))
            {
                int newIndex = listProp.arraySize;
                listProp.InsertArrayElementAtIndex(newIndex);
                listProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = null;
            }
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

            HashSet<WorldMapTerrainType> seenTerrains = new HashSet<WorldMapTerrainType>();
            foreach (WorldMapTerrainVisualProfile profile in theme.TerrainProfiles)
            {
                if (profile == null)
                    continue;

                if (!seenTerrains.Add(profile.Terrain))
                    result.Add($"Дублирующийся профиль местности: {profile.Terrain}.");
            }

            foreach (WorldMapTerrainType terrain in new[]
                     {
                         WorldMapTerrainType.Hills,
                         WorldMapTerrainType.Mountains
                     })
            {
                WorldMapTerrainVisualProfile profile = theme.FindTerrainProfile(terrain);

                if (profile == null)
                {
                    result.Add($"Нет профиля местности для {terrain} — местность останется невидимой.");
                }
                else if (profile.MassVariants.Count == 0 && profile.CellColor.a <= 0f)
                {
                    result.Add(
                        $"{terrain}: нет ни массы (MassVariants), ни видимого CellColor — местность не отобразится.");
                }
            }

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
        }

        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        private void DrawPreviewSection()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            bool hasAuthoredWorld = database != null && database.ActiveWorld != null;

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(hasAuthoredWorld))
                previewSeed = EditorGUILayout.IntField("Seed наполнения", previewSeed);
            if (GUILayout.Button("REGENERATE", GUILayout.Width(120f)))
                ApplyPreviewGeography();
            EditorGUILayout.EndHorizontal();

            if (hasAuthoredWorld)
            {
                EditorGUILayout.HelpBox(
                    $"Активен авторский мир '{database.ActiveWorld.WorldDefinitionId}' — рельеф и река " +
                    "постоянны и не зависят от Seed; Seed здесь бы влиял только на будущее наполнение " +
                    "малых локаций (AM-04, ещё не подключено).",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Схематичный превью-грид ({WorldMapNavigation.GridWidth}×{WorldMapNavigation.GridHeight}), не финальный визуал. " +
                    "Без Active World местность процедурная и меняется по Seed (переходное поведение AM-01).",
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
        }

        private void ApplyPreviewGeography()
        {
            if (database != null && database.ActiveWorld != null)
                WorldMapNavigation.ConfigureFromDefinition(database.ActiveWorld.ToData());
            else
                WorldMapNavigation.ConfigureTerrain(previewSeed);
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

        private Color GetPreviewTerrainColor(WorldMapTerrainType terrain)
        {
            WorldMapTerrainVisualProfile profile =
                database != null && database.ActiveTheme != null
                    ? database.ActiveTheme.FindTerrainProfile(terrain)
                    : null;

            if (profile != null && profile.CellColor.a > 0f)
                return profile.CellColor;

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
