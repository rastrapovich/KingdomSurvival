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
            Textures,
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
                new[] { "Текстуры", "Локации", "Проверка", "Предпросмотр" });
            EditorGUILayout.Space(8f);

            switch (tab)
            {
                case WindowTab.Textures:
                    DrawTexturesSection();
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
            DrawWaterSection(themeSO);
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
                "Фоновая текстура заполняет слой под местностью, рекой, маршрутами и маркерами.",
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

        private static void DrawWaterSection(SerializedObject themeSO)
        {
            EditorGUILayout.LabelField("Вода (река)", EditorStyles.boldLabel);

            SerializedProperty waterProp = themeSO.FindProperty("water");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(
                waterProp.FindPropertyRelative("segmentSprite"), new GUIContent("Segment Sprite"));
            EditorGUILayout.PropertyField(
                waterProp.FindPropertyRelative("fallbackColor"), new GUIContent("Fallback Color"));
            EditorGUILayout.PropertyField(
                waterProp.FindPropertyRelative("widthPixels"), new GUIContent("Width (px)"));

            EditorGUILayout.HelpBox(
                "Лента сегментов вдоль реки (WM-09). Без Segment Sprite — заливка Fallback Color.",
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
                DrawSpawnSlotField(location.FindPropertyRelative("spawnSlotId"));

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
            }

            if (database.Locations.Count == 0)
                result.Add("В базе нет ни одной локации — будет использован Core fallback.");

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

            if (theme.Water == null)
            {
                result.Add("У темы не задан профиль воды.");
            }
            else if (theme.Water.SegmentSprite == null &&
                     theme.Water.FallbackColor.a <= 0f)
            {
                result.Add("У воды нет ни SegmentSprite, ни видимого FallbackColor — река не отобразится.");
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

        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        private void DrawPreviewSection()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            previewSeed = EditorGUILayout.IntField("Seed", previewSeed);
            if (GUILayout.Button("REGENERATE", GUILayout.Width(120f)))
                WorldMapNavigation.ConfigureTerrain(previewSeed);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                $"Схематичный превью-грид ({WorldMapNavigation.GridWidth}×{WorldMapNavigation.GridHeight}), не финальный визуал. " +
                "Показывает местность и реку по текущему Seed без захода в Play Mode.",
                MessageType.None);

            WorldMapNavigation.ConfigureTerrain(previewSeed);

            Rect area = GUILayoutUtility.GetRect(
                position.width - 24f, 220f, GUILayout.ExpandWidth(false));

            DrawPreviewGrid(area);
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

            Color riverColor = GetPreviewWaterColor();
            foreach ((int X, int Y) cell in WorldMapNavigation.GetRiverPath())
            {
                Rect cellRect = new Rect(
                    area.x + cell.X * cellWidth,
                    area.y + cell.Y * cellHeight,
                    cellWidth + 1f,
                    cellHeight + 1f);

                EditorGUI.DrawRect(cellRect, riverColor);
            }

            DrawPreviewLocations(area);
        }

        private void DrawPreviewLocations(Rect area)
        {
            if (database == null)
                return;

            GameState previewState = new GameState();
            previewState.CreateNewGame(
                previewSeed,
                database.BuildRuntimeLocationTemplates());

            Rect capitalRect = new Rect(
                area.x + area.width * WorldMapNavigation.CapitalXPercent / 100f - 3f,
                area.y + area.height * WorldMapNavigation.CapitalYPercent / 100f - 3f,
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

        private Color GetPreviewWaterColor()
        {
            if (database != null &&
                database.ActiveTheme != null &&
                database.ActiveTheme.Water != null &&
                database.ActiveTheme.Water.FallbackColor.a > 0f)
            {
                return database.ActiveTheme.Water.FallbackColor;
            }

            return new Color(0.30f, 0.42f, 0.52f, 0.9f);
        }
    }
}
