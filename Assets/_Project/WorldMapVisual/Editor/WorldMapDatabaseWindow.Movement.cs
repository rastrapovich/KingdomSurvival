using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // 12И (канон v1.50 §9, §9.9): инструменты прямого управления героем.
    // «География → Местность» — размер клетки, сводка и перенос старой
    // разметки; «Предпросмотр → Местность» — кисть и заливка по клеткам
    // шестиугольной сетки поверх арта; вкладка «Перемещение» — темп,
    // таблица местности, герой, управление и камера.
    public sealed partial class WorldMapDatabaseWindow
    {
        private const string PrefShowTerrainMarkup = "KingdomSurvival.WorldMapPreview.ShowTerrainMarkup";
        private const string PrefTerrainMarkupOpacity = "KingdomSurvival.WorldMapPreview.TerrainMarkupOpacity";
        private const string PrefTerrainBrushRadius = "KingdomSurvival.WorldMapPreview.TerrainBrushRadius";
        private const string PrefTerrainBrushType = "KingdomSurvival.WorldMapPreview.TerrainBrushType";
        // Размер клетки в текстуре предпросмотра разметки (пиксели радиуса).
        private const float TerrainTextureHexRadius = 6f;
        private const int TerrainTextureMaxSide = 2048;

        private enum TerrainPaintTool
        {
            Brush,
            Fill
        }

        private bool previewShowTerrainMarkup = true;
        private float previewTerrainMarkupOpacity = 0.45f;
        private WorldMapGameplayTerrainType terrainBrushType = WorldMapGameplayTerrainType.Water;
        private int terrainBrushRadius = 1;
        private TerrainPaintTool terrainPaintTool = TerrainPaintTool.Brush;

        private WorldMapWorldDefinitionAsset lastSeenTerrainWorld;
        private WorldMapWorldDefinitionAsset terrainCacheWorld;
        private WorldMapTerrainLayer terrainCacheLayer;
        private Texture2D terrainCacheTexture;
        private bool isPaintingTerrain;
        private WorldMapHexCell lastPaintedCell = new WorldMapHexCell(-1, -1);

        private Vector2 movementScroll;
        private UnityEditor.Editor movementSettingsEditor;

        private void LoadTerrainPaintPrefs()
        {
            previewShowTerrainMarkup = EditorPrefs.GetBool(PrefShowTerrainMarkup, true);
            previewTerrainMarkupOpacity = EditorPrefs.GetFloat(PrefTerrainMarkupOpacity, 0.45f);
            terrainBrushRadius = Mathf.Clamp(EditorPrefs.GetInt(PrefTerrainBrushRadius, 1), 0, 12);
            terrainBrushType = (WorldMapGameplayTerrainType)EditorPrefs.GetInt(
                PrefTerrainBrushType, (int)WorldMapGameplayTerrainType.Water);
        }

        private void InvalidateTerrainPaintCache()
        {
            terrainCacheWorld = null;
            terrainCacheLayer = null;
            EndTerrainPaintStroke();
        }

        private void EndTerrainPaintStroke()
        {
            if (isPaintingTerrain && terrainCacheWorld != null && terrainCacheLayer != null)
            {
                terrainCacheWorld.EditorSetTerrainLayer(terrainCacheLayer);
                EditorUtility.SetDirty(terrainCacheWorld);
            }
            isPaintingTerrain = false;
            lastPaintedCell = new WorldMapHexCell(-1, -1);
        }

        private WorldMapTerrainLayer GetTerrainPaintLayer(WorldMapWorldDefinitionAsset world)
        {
            if (terrainCacheLayer == null || terrainCacheWorld != world ||
                terrainCacheLayer.Grid.HexesAcross != world.HexesAcross ||
                !Mathf.Approximately(terrainCacheLayer.Grid.CanvasWidth, world.MapCanvasWidth) ||
                !Mathf.Approximately(terrainCacheLayer.Grid.CanvasHeight, world.MapCanvasHeight))
            {
                terrainCacheWorld = world;
                terrainCacheLayer = world.BuildTerrainLayer();
                RebuildTerrainTexture(null);
            }
            return terrainCacheLayer;
        }

        // ------------------------------------------------------------------
        // География → Местность
        // ------------------------------------------------------------------

        private void DrawTerrainMarkupSection(WorldMapWorldDefinitionAsset world)
        {
            EditorGUILayout.LabelField("Местность (шестиугольная сетка)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox(
                "Невидимая разметка поверх арта: каждая клетка — тип местности. По ней герой ищет " +
                "путь, обходит воду и скалы, а местность задаёт темп. Рисуется кистью на вкладке " +
                "«Предпросмотр» в режиме «Местность». Числа типов — на вкладке «Перемещение».",
                MessageType.None);

            WorldMapTerrainLayer layer = GetTerrainPaintLayer(world);
            WorldMapHexGrid grid = layer.Grid;

            EditorGUI.BeginChangeCheck();
            int hexesAcross = EditorGUILayout.DelayedIntField(
                new GUIContent("Клеток по ширине карты",
                    "Размер сетки: " + WorldMapHexGrid.MinHexesAcross + "…" + WorldMapHexGrid.MaxHexesAcross +
                    ". Больше — мельче клетка. Разметка пересчитывается под новую сетку."),
                world.HexesAcross);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(world, "Размер сетки карты");
                world.EditorSetHexesAcross(hexesAcross);
                EditorUtility.SetDirty(world);
                InvalidateTerrainPaintCache();
                layer = GetTerrainPaintLayer(world);
                grid = layer.Grid;
            }

            EditorGUILayout.LabelField(
                "Сетка",
                grid.Columns + " × " + grid.Rows + " = " + grid.CellCount + " клеток · шаг " +
                grid.HexWidth.ToString("0.#") + " px полотна");

            Dictionary<WorldMapGameplayTerrainType, int> counts = CountTerrain(layer);
            foreach (WorldMapGameplayTerrainType terrain in WorldMapTerrainLabels.All)
            {
                if (terrain == WorldMapGameplayTerrainType.OpenGround)
                    continue;
                counts.TryGetValue(terrain, out int count);
                if (count > 0)
                    EditorGUILayout.LabelField("  " + WorldMapTerrainLabels.Name(terrain), count + " клеток");
            }

            if (world.HasLegacyMarkup)
            {
                EditorGUILayout.HelpBox(
                    "В мире остались старые прямоугольные зоны или ломаные дороги (до канона v1.50). " +
                    "Игра уже читает их как разметку; перенос запишет их в клетки и удалит.",
                    MessageType.Warning);
                if (GUILayout.Button("Перенести старую разметку в клетки"))
                {
                    Undo.RecordObject(world, "Перенос старой разметки");
                    world.MigrateLegacyMarkup();
                    EditorUtility.SetDirty(world);
                    InvalidateTerrainPaintCache();
                }
            }

            using (new EditorGUI.DisabledScope(layer.IsEmpty && !world.HasLegacyMarkup))
            {
                if (GUILayout.Button("Очистить всю разметку") &&
                    EditorUtility.DisplayDialog(
                        "Очистить разметку",
                        "Вся карта станет открытой местностью. Отменить можно через Undo.",
                        "Очистить",
                        "Отмена"))
                {
                    Undo.RecordObject(world, "Очистка разметки");
                    world.EditorSetTerrainLayer(new WorldMapTerrainLayer(world.CreateGrid()));
                    world.MigrateLegacyMarkup();
                    world.EditorSetTerrainLayer(new WorldMapTerrainLayer(world.CreateGrid()));
                    EditorUtility.SetDirty(world);
                    InvalidateTerrainPaintCache();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private static Dictionary<WorldMapGameplayTerrainType, int> CountTerrain(WorldMapTerrainLayer layer)
        {
            Dictionary<WorldMapGameplayTerrainType, int> counts = new Dictionary<WorldMapGameplayTerrainType, int>();
            for (int i = 0; i < layer.Grid.CellCount; i++)
            {
                WorldMapGameplayTerrainType terrain = layer.GetAtIndex(i);
                counts.TryGetValue(terrain, out int count);
                counts[terrain] = count + 1;
            }
            return counts;
        }

        // ------------------------------------------------------------------
        // Предпросмотр → Местность: кисть
        // ------------------------------------------------------------------

        private void DrawTerrainPaintBanner(WorldMapWorldDefinitionAsset world)
        {
            WorldMapMovementSettingsAsset settings = LoadMovementSettingsForEditor();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            terrainPaintTool = (TerrainPaintTool)GUILayout.Toolbar(
                (int)terrainPaintTool, new[] { "Кисть", "Заливка" }, GUILayout.Width(180f));
            GUILayout.Space(12f);
            using (new EditorGUI.DisabledScope(terrainPaintTool != TerrainPaintTool.Brush))
            {
                EditorGUILayout.LabelField("Размер кисти", GUILayout.Width(90f));
                int newRadius = EditorGUILayout.IntSlider(terrainBrushRadius, 0, 12, GUILayout.Width(200f));
                if (newRadius != terrainBrushRadius)
                {
                    terrainBrushRadius = newRadius;
                    EditorPrefs.SetInt(PrefTerrainBrushRadius, newRadius);
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            foreach (WorldMapGameplayTerrainType terrain in WorldMapTerrainLabels.All)
            {
                Color color = settings != null
                    ? settings.GetTerrainColor(terrain)
                    : WorldMapMovementSettingsAsset.DefaultColor(terrain);
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = terrain == terrainBrushType ? color : Color.Lerp(color, Color.gray, 0.55f);
                GUIStyle style = terrain == terrainBrushType ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
                if (GUILayout.Button(WorldMapTerrainLabels.Name(terrain), style))
                {
                    terrainBrushType = terrain;
                    EditorPrefs.SetInt(PrefTerrainBrushType, (int)terrain);
                }
                GUI.backgroundColor = previous;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "ЛКМ — рисовать выбранным типом. Shift+ЛКМ — стереть (открытая местность). " +
                "ПКМ — взять тип из клетки. Колесо — zoom, средняя кнопка — pan.",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawTerrainMarkupOpacitySlider()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Прозрачность местности", GUILayout.Width(180f));
            float newOpacity = GUILayout.HorizontalSlider(previewTerrainMarkupOpacity, 0f, 1f, GUILayout.Width(120f));
            GUILayout.Label(Mathf.RoundToInt(newOpacity * 100f) + "%", GUILayout.Width(40f));
            if (!Mathf.Approximately(newOpacity, previewTerrainMarkupOpacity))
            {
                previewTerrainMarkupOpacity = newOpacity;
                EditorPrefs.SetFloat(PrefTerrainMarkupOpacity, newOpacity);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreviewTerrainMarkup(Rect mapRect, WorldMapWorldDefinitionAsset world)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            WorldMapTerrainLayer layer = GetTerrainPaintLayer(world);
            if (terrainCacheTexture == null)
                RebuildTerrainTexture(null);

            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(previewTerrainMarkupOpacity));
            GUI.DrawTexture(mapRect, terrainCacheTexture, ScaleMode.StretchToFill, true);
            GUI.color = previous;

            // Контуры клеток — только когда клетка на экране крупнее нескольких пикселей.
            WorldMapHexGrid grid = layer.Grid;
            float scale = mapRect.width / grid.CanvasWidth;
            float radiusScreen = grid.HexRadius * scale;
            if (radiusScreen < 7f && previewEditMode != PreviewEditMode.Terrain)
                return;
            if (radiusScreen < 4f)
                return;

            Rect visible = new Rect(0f, 0f, position.width, position.height);
            double minX = (visible.xMin - mapRect.x) / scale - grid.HexWidth;
            double maxX = (visible.xMax - mapRect.x) / scale + grid.HexWidth;
            double minY = (visible.yMin - mapRect.y) / scale - grid.RowStep;
            double maxY = (visible.yMax - mapRect.y) / scale + grid.RowStep;
            int firstRow = Mathf.Max(0, (int)System.Math.Floor(minY / grid.RowStep));
            int lastRow = Mathf.Min(grid.Rows - 1, (int)System.Math.Ceiling(maxY / grid.RowStep));
            int firstColumn = Mathf.Max(0, (int)System.Math.Floor(minX / grid.HexWidth) - 1);
            int lastColumn = Mathf.Min(grid.Columns - 1, (int)System.Math.Ceiling(maxX / grid.HexWidth));
            if ((lastRow - firstRow + 1) * (lastColumn - firstColumn + 1) > 12000)
                return;

            Handles.color = new Color(1f, 1f, 1f, 0.18f);
            Vector3[] points = new Vector3[7];
            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    grid.CellCenter(new WorldMapHexCell(column, row), out double cx, out double cy);
                    Vector2 center = new Vector2(mapRect.x + (float)cx * scale, mapRect.y + (float)cy * scale);
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = Mathf.Deg2Rad * (60f * i - 90f);
                        points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radiusScreen;
                    }
                    points[6] = points[0];
                    Handles.DrawPolyLine(points);
                }
            }
        }

        private void HandleTerrainPaintInput(Rect mapRect, WorldMapWorldDefinitionAsset world)
        {
            Event current = Event.current;
            if (current == null)
                return;

            WorldMapTerrainLayer layer = GetTerrainPaintLayer(world);
            WorldMapHexGrid grid = layer.Grid;
            bool inside = mapRect.Contains(current.mousePosition);

            if (current.type == EventType.MouseDown && current.button == 1 && inside)
            {
                terrainBrushType = layer.Get(CellUnderMouse(mapRect, grid, current.mousePosition));
                EditorPrefs.SetInt(PrefTerrainBrushType, (int)terrainBrushType);
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0 && inside)
            {
                Undo.RecordObject(world, "Разметка местности");
                isPaintingTerrain = true;
                lastPaintedCell = new WorldMapHexCell(-1, -1);
                ApplyTerrainPaint(layer, CellUnderMouse(mapRect, grid, current.mousePosition), current.shift);
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseDrag && current.button == 0 && isPaintingTerrain)
            {
                if (terrainPaintTool == TerrainPaintTool.Brush)
                    ApplyTerrainPaint(layer, CellUnderMouse(mapRect, grid, current.mousePosition), current.shift);
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseUp && current.button == 0 && isPaintingTerrain)
            {
                EndTerrainPaintStroke();
                current.Use();
                Repaint();
            }
        }

        private static WorldMapHexCell CellUnderMouse(Rect mapRect, WorldMapHexGrid grid, Vector2 mouse)
        {
            Vector2 percent = WorldMapPreviewMath.PreviewToMap(mapRect, mouse);
            return grid.CellAtPercent(percent.x, percent.y);
        }

        private void ApplyTerrainPaint(WorldMapTerrainLayer layer, WorldMapHexCell center, bool erase)
        {
            if (center.Equals(lastPaintedCell))
                return;
            lastPaintedCell = center;

            WorldMapGameplayTerrainType terrain = erase ? WorldMapGameplayTerrainType.OpenGround : terrainBrushType;
            List<WorldMapHexCell> changed = terrainPaintTool == TerrainPaintTool.Fill && !erase
                ? FloodFill(layer, center, terrain)
                : StampBrush(layer, center, terrain);
            if (changed.Count > 0)
                RebuildTerrainTexture(changed);
        }

        private List<WorldMapHexCell> StampBrush(WorldMapTerrainLayer layer, WorldMapHexCell center, WorldMapGameplayTerrainType terrain)
        {
            List<WorldMapHexCell> changed = new List<WorldMapHexCell>();
            WorldMapHexGrid grid = layer.Grid;
            int radius = terrainBrushRadius;
            for (int row = center.Row - radius; row <= center.Row + radius; row++)
            {
                for (int column = center.Column - radius - 1; column <= center.Column + radius + 1; column++)
                {
                    WorldMapHexCell cell = new WorldMapHexCell(column, row);
                    if (!grid.IsInside(cell) || cell.DistanceTo(center) > radius || layer.Get(cell) == terrain)
                        continue;
                    layer.Set(cell, terrain);
                    changed.Add(cell);
                }
            }
            return changed;
        }

        private static List<WorldMapHexCell> FloodFill(WorldMapTerrainLayer layer, WorldMapHexCell start, WorldMapGameplayTerrainType terrain)
        {
            List<WorldMapHexCell> changed = new List<WorldMapHexCell>();
            WorldMapGameplayTerrainType source = layer.Get(start);
            if (source == terrain)
                return changed;

            Queue<WorldMapHexCell> queue = new Queue<WorldMapHexCell>();
            queue.Enqueue(start);
            layer.Set(start, terrain);
            changed.Add(start);
            while (queue.Count > 0)
            {
                WorldMapHexCell cell = queue.Dequeue();
                for (int direction = 0; direction < 6; direction++)
                {
                    WorldMapHexCell next = cell.Neighbor(direction);
                    if (!layer.Grid.IsInside(next) || layer.Get(next) != source)
                        continue;
                    layer.Set(next, terrain);
                    changed.Add(next);
                    queue.Enqueue(next);
                }
            }
            return changed;
        }

        // Текстура разметки: пиксель → клетка под ним. changed == null —
        // перерисовать всё, иначе только прямоугольник вокруг изменённых клеток.
        private void RebuildTerrainTexture(List<WorldMapHexCell> changed)
        {
            if (terrainCacheLayer == null)
                return;

            WorldMapHexGrid grid = terrainCacheLayer.Grid;
            float scale = TerrainTextureHexRadius / grid.HexRadius;
            int width = Mathf.Clamp(Mathf.CeilToInt(grid.CanvasWidth * scale), 16, TerrainTextureMaxSide);
            int height = Mathf.Clamp(Mathf.CeilToInt(grid.CanvasHeight * scale), 16, TerrainTextureMaxSide);
            scale = Mathf.Min(width / grid.CanvasWidth, height / grid.CanvasHeight);

            if (terrainCacheTexture == null || terrainCacheTexture.width != width || terrainCacheTexture.height != height)
            {
                if (terrainCacheTexture != null)
                    DestroyImmediate(terrainCacheTexture);
                terrainCacheTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                changed = null;
            }

            int xMin = 0;
            int xMax = width - 1;
            int yMin = 0;
            int yMax = height - 1;
            if (changed != null && changed.Count > 0)
            {
                double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
                foreach (WorldMapHexCell cell in changed)
                {
                    grid.CellCenter(cell, out double cx, out double cy);
                    minX = System.Math.Min(minX, cx);
                    maxX = System.Math.Max(maxX, cx);
                    minY = System.Math.Min(minY, cy);
                    maxY = System.Math.Max(maxY, cy);
                }
                xMin = Mathf.Clamp(Mathf.FloorToInt((float)(minX - grid.HexRadius) * scale) - 1, 0, width - 1);
                xMax = Mathf.Clamp(Mathf.CeilToInt((float)(maxX + grid.HexRadius) * scale) + 1, 0, width - 1);
                yMin = Mathf.Clamp(Mathf.FloorToInt((float)(minY - grid.HexRadius) * scale) - 1, 0, height - 1);
                yMax = Mathf.Clamp(Mathf.CeilToInt((float)(maxY + grid.HexRadius) * scale) + 1, 0, height - 1);
            }

            WorldMapMovementSettingsAsset settings = LoadMovementSettingsForEditor();
            Color32[] palette = new Color32[16];
            foreach (WorldMapGameplayTerrainType terrain in WorldMapTerrainLabels.All)
            {
                Color color = settings != null
                    ? settings.GetTerrainColor(terrain)
                    : WorldMapMovementSettingsAsset.DefaultColor(terrain);
                color.a = terrain == WorldMapGameplayTerrainType.OpenGround ? 0f : 1f;
                palette[(int)terrain] = color;
            }

            int blockWidth = xMax - xMin + 1;
            int blockHeight = yMax - yMin + 1;
            Color32[] pixels = new Color32[blockWidth * blockHeight];
            for (int y = yMin; y <= yMax; y++)
            {
                // Текстура хранится снизу вверх, полотно — сверху вниз.
                double canvasY = (height - 1 - y + 0.5) / scale;
                for (int x = xMin; x <= xMax; x++)
                {
                    double canvasX = (x + 0.5) / scale;
                    int value = (int)terrainCacheLayer.GetAtPixel(canvasX, canvasY);
                    pixels[(y - yMin) * blockWidth + (x - xMin)] = value >= 0 && value < palette.Length
                        ? palette[value]
                        : new Color32(255, 0, 255, 255);
                }
            }
            terrainCacheTexture.SetPixels32(xMin, yMin, blockWidth, blockHeight, pixels);
            terrainCacheTexture.Apply(false);
        }

        // ------------------------------------------------------------------
        // Вкладка «Перемещение»
        // ------------------------------------------------------------------

        private static WorldMapMovementSettingsAsset LoadMovementSettingsForEditor()
        {
            WorldMapMovementSettingsAsset settings =
                AssetDatabase.LoadAssetAtPath<WorldMapMovementSettingsAsset>(WorldMapMovementSettingsAsset.AssetPath);
            return settings != null ? settings : WorldMapVisualRuntime.LoadMovementSettings();
        }

        private void DrawMovementSection()
        {
            WorldMapMovementSettingsAsset settings = LoadMovementSettingsForEditor();
            if (settings == null)
            {
                EditorGUILayout.HelpBox(
                    "Нет ассета настроек перемещения (" + WorldMapMovementSettingsAsset.AssetPath + "). " +
                    "Без него игра использует значения ядра по умолчанию.",
                    MessageType.Warning);
                if (GUILayout.Button("Создать настройки перемещения"))
                {
                    WorldMapMovementSettingsAsset created = CreateInstance<WorldMapMovementSettingsAsset>();
                    created.EnsureTerrainEntries();
                    AssetDatabase.CreateAsset(created, WorldMapMovementSettingsAsset.AssetPath);
                    AssetDatabase.SaveAssets();
                    WorldMapVisualRuntime.ClearCache();
                }
                return;
            }

            movementScroll = EditorGUILayout.BeginScrollView(movementScroll);

            EditorGUILayout.HelpBox(
                "Прямое управление героем (канон v1.50): клик — бег к точке, зажатая кнопка — бег за " +
                "курсором. В походе время идёт только пока отряд бежит или занят делом. Все числа здесь — " +
                "рабочие настройки, не канон. В Play Mode изменения применяются сразу.",
                MessageType.None);

            DrawMovementSummary(settings);

            EditorGUI.BeginChangeCheck();
            UnityEditor.Editor.CreateCachedEditor(settings, null, ref movementSettingsEditor);
            movementSettingsEditor.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(settings);
                if (Application.isPlaying)
                    settings.ApplyToCore();
                if (terrainCacheLayer != null)
                    RebuildTerrainTexture(null);
            }

            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Применить в запущенной игре"))
                    settings.ApplyToCore();
            }

            EditorGUILayout.EndScrollView();
        }

        // Подсказка для настройки: во что выливаются числа в игре.
        private void DrawMovementSummary(WorldMapMovementSettingsAsset settings)
        {
            WorldMapMovementRules rules = settings.ToRules();
            WorldMapWorldDefinitionAsset world = database != null ? database.ActiveWorld : null;
            WorldMapHexGrid grid = world != null ? world.CreateGrid() : WorldMapHexGrid.CreateDefault();

            double hoursPerSecond = rules.RunningGameHoursPerRealSecond(WorldMapGameplayTerrainType.OpenGround);
            double crossHexes = grid.CanvasWidth / grid.HexWidth;
            double crossHours = crossHexes * rules.HoursPerHex(WorldMapGameplayTerrainType.OpenGround);
            double crossSeconds = crossHexes / rules.SafeRunSpeedHexesPerSecond;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Что это значит в игре", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Бег по открытой местности",
                hoursPerSecond.ToString("0.##") + " игровых ч за реальную секунду");
            EditorGUILayout.LabelField("Игровые сутки бега",
                (hoursPerSecond > 0.0001 ? 24.0 / hoursPerSecond : 0.0).ToString("0.#") + " с реального времени");
            EditorGUILayout.LabelField("Карта по ширине",
                crossHexes.ToString("0") + " клеток · " + (crossHours / 24.0).ToString("0.#") + " игровых сут · " +
                crossSeconds.ToString("0") + " с бега");
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
        }
    }

    // 12И: разовый перенос старых зон и дорог активного мира в разметку
    // клеток при загрузке редактора (идемпотентно — после переноса старых
    // данных нет).
    [InitializeOnLoad]
    internal static class WorldMapLegacyMarkupMigration
    {
        static WorldMapLegacyMarkupMigration()
        {
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            WorldMapDatabaseAsset database = AssetDatabase.LoadAssetAtPath<WorldMapDatabaseAsset>(
                "Assets/_Project/WorldMapVisual/Resources/" + WorldMapDatabaseAsset.ResourcesPath + ".asset");
            WorldMapWorldDefinitionAsset world = database != null ? database.ActiveWorld : null;
            if (world == null || !world.HasLegacyMarkup)
                return;

            world.MigrateLegacyMarkup();
            EditorUtility.SetDirty(world);
            AssetDatabase.SaveAssets();
        }
    }
}
