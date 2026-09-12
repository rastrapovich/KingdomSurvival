using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // WM-10/WM-11: редактор поверх World Map Database — Тема (правка спрайтов
    // прямо в окне), Validate (список проблем) и Preview (без захода в Play
    // Mode). Сознательно не редактор полигонов/слотов и не интеграция с Rule
    // Tile — ни то ни другое не доказано нужным на этом этапе.
    public sealed class WorldMapDatabaseWindow : EditorWindow
    {
        private enum WindowTab
        {
            Theme,
            Validate,
            Preview
        }

        // Известные сейчас в игре id локаций — GameState.CreateNewGame создаёт
        // ровно эти три ("ruins"/"mine"/"forest"). Используется только для
        // кнопки-подсказки "добавить недостающие id" в Icon Library, не как
        // источник истины — если состав локаций изменится, список тут
        // устареет и его надо будет поправить вручную.
        private static readonly string[] KnownLocationIds = { "ruins", "mine", "forest" };

        private WorldMapDatabaseAsset database;
        private WindowTab tab;
        private int previewSeed = 1;
        private Vector2 issuesScroll;
        private Vector2 themeScroll;
        private List<string> issues = new List<string>();
        private bool validated;

        [MenuItem("Kingdom Survival/Карта/World Map Database")]
        private static void Open()
        {
            WorldMapDatabaseWindow window = GetWindow<WorldMapDatabaseWindow>();
            window.titleContent = new GUIContent("World Map Database");
            window.minSize = new Vector2(420f, 420f);
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
            tab = (WindowTab)GUILayout.Toolbar((int)tab, new[] { "Тема", "Validate", "Preview" });
            EditorGUILayout.Space(8f);

            switch (tab)
            {
                case WindowTab.Theme:
                    DrawThemeSection();
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

        private void DrawThemeSection()
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

            DrawTerrainProfilesSection(themeSO);
            EditorGUILayout.Space(14f);
            DrawWaterSection(themeSO);
            EditorGUILayout.Space(14f);
            DrawIconLibrarySection(themeSO);

            EditorGUILayout.EndScrollView();

            themeSO.ApplyModifiedProperties();
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

        private static void DrawIconLibrarySection(SerializedObject themeSO)
        {
            EditorGUILayout.LabelField("Иконки локаций", EditorStyles.boldLabel);

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

            SerializedProperty defaultIconProp = iconSO.FindProperty("defaultLocationIcon");
            SerializedProperty entriesProp = iconSO.FindProperty("locationIcons");

            EditorGUILayout.PropertyField(defaultIconProp, new GUIContent("Default Location Icon"));

            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                SerializedProperty entryProp = entriesProp.GetArrayElementAtIndex(i);
                SerializedProperty idProp = entryProp.FindPropertyRelative("locationId");
                SerializedProperty iconProp = entryProp.FindPropertyRelative("icon");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(idProp, GUIContent.none, GUILayout.Width(120f));
                EditorGUILayout.PropertyField(iconProp, GUIContent.none);
                if (GUILayout.Button("✕", GUILayout.Width(22f)))
                {
                    entriesProp.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Добавить запись"))
            {
                int newIndex = entriesProp.arraySize;
                entriesProp.InsertArrayElementAtIndex(newIndex);
                SerializedProperty newEntry = entriesProp.GetArrayElementAtIndex(newIndex);
                newEntry.FindPropertyRelative("locationId").stringValue = string.Empty;
                newEntry.FindPropertyRelative("icon").objectReferenceValue = null;
            }

            DrawAddMissingLocationIdsButton(entriesProp);

            EditorGUILayout.HelpBox(
                "Иконка ищется по LocationData.Id. Известные сейчас в игре id: " +
                string.Join(", ", KnownLocationIds) + " (см. GameState.CreateNewGame).",
                MessageType.None);

            iconSO.ApplyModifiedProperties();
        }

        private static void DrawAddMissingLocationIdsButton(SerializedProperty entriesProp)
        {
            HashSet<string> present = new HashSet<string>();
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                string id = entriesProp.GetArrayElementAtIndex(i).FindPropertyRelative("locationId").stringValue;
                if (!string.IsNullOrEmpty(id))
                    present.Add(id);
            }

            List<string> missing = new List<string>();
            foreach (string id in KnownLocationIds)
            {
                if (!present.Contains(id))
                    missing.Add(id);
            }

            if (missing.Count == 0)
                return;

            if (!GUILayout.Button($"+ Добавить недостающие id ({string.Join(", ", missing)})"))
                return;

            foreach (string id in missing)
            {
                int newIndex = entriesProp.arraySize;
                entriesProp.InsertArrayElementAtIndex(newIndex);
                SerializedProperty newEntry = entriesProp.GetArrayElementAtIndex(newIndex);
                newEntry.FindPropertyRelative("locationId").stringValue = id;
                newEntry.FindPropertyRelative("icon").objectReferenceValue = null;
            }
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

            if (GUILayout.Button("VALIDATE DATABASE"))
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
            {
                result.Add("У базы не назначена Active Theme.");
                return result;
            }

            if (theme.IconLibrary == null)
                result.Add("У темы не назначена Icon Library.");

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

            if (theme.Water != null &&
                theme.Water.SegmentSprite == null &&
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
