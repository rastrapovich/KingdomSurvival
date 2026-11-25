using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual.Editor
{
    // 12Ф-1 «Редактор карты» (бриф ProjectDocs/WORLD_MAP_EDITOR_BRIEF_RU.md):
    // автор сам собирает карту свободной игры. Холст-схема перерисовывается
    // на каждый мазок; рельеф, местность, деревни, лагерь и размер карты.
    // Данные — AuthoredWorldMapAsset; новая партия собирает карту из него.
    public sealed class AuthoredWorldMapWindow : EditorWindow
    {
        private enum Tool
        {
            Relief,
            Terrain,
            Villages,
            Start,
            Size
        }

        private static readonly string[] ToolTitles = { "⛰ Рельеф", "🌲 Местность", "🏠 Деревни", "⚑ Лагерь", "⬚ Размер" };
        private static readonly string[] ToolHints =
        {
            "ЛКМ — поднять, Ctrl+ЛКМ — опустить (в режиме «Поднять»). Alt+клик — взять высоту для «Выровнять». [ ] — размер кисти.",
            "ЛКМ — рисовать тип клетки или залить область. Alt+клик — взять тип с карты. [ ] — размер кисти.",
            "Клик по пустому месту — новая деревня. Клик по деревне — выбрать, тянуть — двигать, Del — удалить.",
            "Клик — поставить лагерь отряда: здесь начинается новая партия.",
            "Карта растёт и уменьшается с любого края, нарисованное остаётся на месте."
        };
        private static readonly string[] HeightBrushTitles = { "Поднять", "Опустить", "Выровнять", "Сгладить", "Шум" };
        private const string PrototypeScenePath = "Assets/_Project/Scenes/Prototype_Main.unity";

        private AuthoredWorldMapAsset asset;
        private Tool tool;
        private AuthoredWorldMapEditing.HeightBrush heightBrush;
        private float brushRadius = 3f;
        private float brushStrength = 0.5f;
        private float brushSoftness = 0.5f;
        private float flattenTarget = 0.3f;
        private bool terraces;
        private float terraceStep = 0.1f;
        private WorldMapGameplayTerrainType paintType = WorldMapGameplayTerrainType.Forest;
        private bool fillMode;
        private bool keepWaterAndRoads = true;
        private bool showGrid;
        private int resizeStep = 10;
        private int newColumns = 60, newRows = 44;

        private Texture2D texture;
        private int[] pixelCell;
        private Color32[] pixels;
        private bool textureDirty = true;
        private float zoom = 1f;
        private Vector2 pan;
        private bool fitPending = true;
        private Rect canvasRect;
        private int selectedVillage = -1;
        private bool dragging;
        private bool panning;
        private int strokeGroup;
        private int hoverCell = -1;
        private List<AuthoredWorldMapValidation.Problem> problems = new List<AuthoredWorldMapValidation.Problem>();
        private Vector2 problemsScroll, inspectorScroll;

        [MenuItem("Kingdom Survival/Редактор карты", priority = 40)]
        public static void Open()
        {
            AuthoredWorldMapWindow window = GetWindow<AuthoredWorldMapWindow>();
            window.titleContent = new GUIContent("Редактор карты");
            window.minSize = new Vector2(900f, 560f);
            window.Show();
        }

        // Пакетный запуск: создать ассет карты, если его нет.
        public static void EnsureAssetBatch() => LoadOrCreateAsset();

        public static AuthoredWorldMapAsset LoadOrCreateAsset()
        {
            AuthoredWorldMapAsset existing = AssetDatabase.LoadAssetAtPath<AuthoredWorldMapAsset>(AuthoredWorldMapAsset.AssetPath);
            if (existing != null)
                return existing;
            AuthoredWorldMapAsset created = CreateInstance<AuthoredWorldMapAsset>();
            AssetDatabase.CreateAsset(created, AuthoredWorldMapAsset.AssetPath);
            AssetDatabase.SaveAssets();
            return created;
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            wantsMouseMove = true;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (texture != null)
                DestroyImmediate(texture);
        }

        private void OnUndoRedo()
        {
            if (asset == null)
                return;
            asset.Data.Invalidate();
            MarkChanged(false);
            Repaint();
        }

        private AuthoredWorldMapData Data => asset.Data;

        private void OnGUI()
        {
            if (asset == null)
                asset = LoadOrCreateAsset();
            if (asset == null)
            {
                EditorGUILayout.HelpBox("Не удалось создать ассет карты " + AuthoredWorldMapAsset.AssetPath, MessageType.Error);
                return;
            }

            HandleShortcuts();
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawToolColumn();
            canvasRect = GUILayoutUtility.GetRect(10f, 10000f, 10f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawCanvas();
            DrawInspector();
            EditorGUILayout.EndHorizontal();
            DrawStatusBar();
        }

        // ---------------- Панели ----------------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField(Data.Name, EditorStyles.toolbarTextField, GUILayout.Width(220f));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(asset, "Редактор карты: название");
                Data.Name = name;
                EditorUtility.SetDirty(asset);
            }
            GUILayout.Label($"{Data.Columns} × {Data.Rows} клеток", EditorStyles.miniLabel, GUILayout.Width(110f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Вписать (F)", "Вся карта в окне"), EditorStyles.toolbarButton))
                fitPending = true;
            showGrid = GUILayout.Toggle(showGrid, new GUIContent("Сетка", "Границы клеток"), EditorStyles.toolbarButton);
            if (GUILayout.Button(new GUIContent("Сохранить", "Записать карту на диск (Ctrl+S)"), EditorStyles.toolbarButton))
                Save();
            GUI.enabled = !EditorApplication.isPlayingOrWillChangePlaymode;
            if (GUILayout.Button(new GUIContent("▶ Играть на этой карте", "Сохранить и начать новую партию на этой карте"), EditorStyles.toolbarButton))
                PlayOnThisMap();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolColumn()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(120f));
            for (int i = 0; i < ToolTitles.Length; i++)
            {
                bool on = (int)tool == i;
                if (GUILayout.Toggle(on, new GUIContent(ToolTitles[i], ToolHints[i] + "  Клавиша " + (i + 1) + "."), "Button", GUILayout.Height(30f)) && !on)
                {
                    tool = (Tool)i;
                    selectedVillage = -1;
                }
            }
            GUILayout.Space(8f);
            EditorGUILayout.LabelField(ToolHints[(int)tool], EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawInspector()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(270f));
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            switch (tool)
            {
                case Tool.Relief: DrawReliefInspector(); break;
                case Tool.Terrain: DrawTerrainInspector(); break;
                case Tool.Villages: DrawVillagesInspector(); break;
                case Tool.Start: DrawStartInspector(); break;
                case Tool.Size: DrawSizeInspector(); break;
            }
            EditorGUILayout.EndScrollView();
            DrawProblems();
            EditorGUILayout.EndVertical();
        }

        private void DrawBrushSliders(bool withStrength)
        {
            brushRadius = EditorGUILayout.Slider(new GUIContent("Размер, клеток", "[ и ] — меньше/больше"), brushRadius, 0.5f, 20f);
            if (!withStrength)
                return;
            brushStrength = EditorGUILayout.Slider(new GUIContent("Сила"), brushStrength, 0.02f, 1f);
            brushSoftness = EditorGUILayout.Slider(new GUIContent("Мягкость края"), brushSoftness, 0f, 1f);
        }

        private void DrawReliefInspector()
        {
            Header("Кисть рельефа");
            heightBrush = (AuthoredWorldMapEditing.HeightBrush)GUILayout.SelectionGrid((int)heightBrush, HeightBrushTitles, 3);
            DrawBrushSliders(true);
            if (heightBrush == AuthoredWorldMapEditing.HeightBrush.Flatten)
                flattenTarget = EditorGUILayout.Slider(new GUIContent("Высота площадки", "Alt+клик по карте — взять высоту"), flattenTarget, 0f, 1f);
            terraces = EditorGUILayout.Toggle(new GUIContent("Ступени", "Высоты липнут к уровням — плато и террасы"), terraces);
            if (terraces)
                terraceStep = EditorGUILayout.Slider("Шаг ступени", terraceStep, 0.03f, 0.3f);

            Header("Холмы и горы по высоте");
            EditorGUI.BeginChangeCheck();
            bool auto = EditorGUILayout.Toggle(new GUIContent("Ставить сами", "На открытой местности клетка становится холмами или горами по высоте"), Data.AutoReliefTerrain);
            float hills = Data.HillsFrom, mountains = Data.MountainsFrom;
            if (auto)
            {
                hills = EditorGUILayout.Slider("Холмы от", hills, 0.05f, 0.95f);
                mountains = EditorGUILayout.Slider("Горы от", mountains, 0.1f, 1f);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(asset, "Редактор карты: холмы и горы");
                Data.AutoReliefTerrain = auto;
                Data.HillsFrom = hills;
                Data.MountainsFrom = Mathf.Max(hills + 0.01f, mountains);
                MarkChanged(true);
            }
            EditorGUILayout.HelpBox("Горы непроходимы, холмы — медленнее. Цвет схемы показывает, что получится в игре.", MessageType.None);
        }

        private void DrawTerrainInspector()
        {
            Header("Тип клетки");
            WorldMapGameplayTerrainType[] all = WorldMapTerrainLabels.All;
            for (int i = 0; i < all.Length; i += 2)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(all.Length, i + 2); j++)
                {
                    WorldMapGameplayTerrainType type = all[j];
                    Color previous = GUI.backgroundColor;
                    GUI.backgroundColor = TerrainColor(type);
                    if (GUILayout.Toggle(paintType == type, WorldMapTerrainLabels.Name(type), "Button", GUILayout.Height(24f)))
                        paintType = type;
                    GUI.backgroundColor = previous;
                }
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Space(4f);
            fillMode = GUILayout.Toolbar(fillMode ? 1 : 0, new[] { "Кисть", "Заливка" }) == 1;
            if (!fillMode)
                DrawBrushSliders(false);
            keepWaterAndRoads = EditorGUILayout.Toggle(new GUIContent("Не трогать воду и дороги", "Кисть обходит воду, дороги и тропы"), keepWaterAndRoads);
            EditorGUILayout.HelpBox("Холмы и горы удобнее поднимать рельефом — тип поставится сам. Вода в игре непроходима.", MessageType.None);
        }

        private void DrawVillagesInspector()
        {
            Header("Деревни: " + Data.Villages.Count);
            for (int i = 0; i < Data.Villages.Count; i++)
            {
                AuthoredVillage v = Data.Villages[i];
                string title = string.IsNullOrWhiteSpace(v.Name) ? "Деревня " + (i + 1) : v.Name;
                if (GUILayout.Toggle(selectedVillage == i, title, "Button"))
                {
                    if (selectedVillage != i)
                    {
                        selectedVillage = i;
                        CenterOn(v.Column, v.Row);
                    }
                }
            }
            if (selectedVillage < 0 || selectedVillage >= Data.Villages.Count)
            {
                EditorGUILayout.HelpBox("Клик по карте — поставить деревню.", MessageType.None);
                return;
            }
            AuthoredVillage selected = Data.Villages[selectedVillage];
            Header("Выбранная деревня");
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("Название", selected.Name);
            int size = EditorGUILayout.IntSlider(new GUIContent("Размер", "1 — хутор, 2 — деревня, 3 — большое село"), selected.Size, 1, 3);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(asset, "Редактор карты: деревня");
                selected.Name = name;
                selected.Size = size;
                MarkChanged(true);
            }
            EditorGUILayout.HelpBox("Услуги (корчма, торговец, доска) назначаются из Базы поселений, как раньше. Название — ваше; новые факты мира — сначала в LORE.", MessageType.None);
            if (GUILayout.Button("Удалить деревню (Del)"))
                DeleteSelectedVillage();
        }

        private void DrawStartInspector()
        {
            Header("Лагерь отряда");
            EditorGUILayout.LabelField($"Клетка {Data.StartColumn}:{Data.StartRow}");
            if (GUILayout.Button("Показать"))
                CenterOn(Data.StartColumn, Data.StartRow);
            EditorGUILayout.HelpBox("Новая партия начинается здесь, в лагере у дороги.", MessageType.None);
        }

        private void DrawSizeInspector()
        {
            Header("Добавить или убрать клетки");
            resizeStep = EditorGUILayout.IntSlider("Шаг", resizeStep, 1, 50);
            ResizeRow("Слева", (n) => Resize(n, 0, 0, 0));
            ResizeRow("Справа", (n) => Resize(0, n, 0, 0));
            ResizeRow("Сверху", (n) => Resize(0, 0, n, 0));
            ResizeRow("Снизу", (n) => Resize(0, 0, 0, n));
            EditorGUILayout.HelpBox("Сверху ряды добавляются парами — так не сбивается сетка. Убранные края теряют нарисованное (Ctrl+Z вернёт).", MessageType.None);

            Header("Новая карта");
            newColumns = EditorGUILayout.IntSlider("По ширине", newColumns, AuthoredWorldMapData.MinColumns, AuthoredWorldMapData.MaxColumns);
            newRows = EditorGUILayout.IntSlider("По высоте", newRows, AuthoredWorldMapData.MinRows, AuthoredWorldMapData.MaxRows);
            if (GUILayout.Button("Начать новую карту…") &&
                EditorUtility.DisplayDialog("Новая карта", "Текущая карта будет заменена ровной равниной. Отменить можно Ctrl+Z.", "Заменить", "Отмена"))
            {
                Undo.RecordObject(asset, "Редактор карты: новая карта");
                AuthoredWorldMapData fresh = AuthoredWorldMapData.CreateDefault(newColumns, newRows);
                fresh.Name = Data.Name;
                asset.Replace(fresh);
                selectedVillage = -1;
                fitPending = true;
                MarkChanged(true);
            }
        }

        private void ResizeRow(string title, System.Action<int> apply)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, GUILayout.Width(60f));
            if (GUILayout.Button("− " + resizeStep))
                apply(-resizeStep);
            if (GUILayout.Button("+ " + resizeStep))
                apply(resizeStep);
            EditorGUILayout.EndHorizontal();
        }

        private void Resize(int left, int right, int top, int bottom)
        {
            Undo.RecordObject(asset, "Редактор карты: размер");
            AuthoredWorldMapEditing.Resize(Data, left, right, top, bottom);
            selectedVillage = -1;
            // Нарисованное остаётся под курсором: сдвиг на добавленные клетки.
            WorldMapHexGrid g = Data.Grid;
            float scale = canvasRect.width > 0f && texture != null ? zoom * texture.width / g.CanvasWidth : 0f;
            pan -= new Vector2((float)(left * g.HexWidth), (float)(top * g.RowStep)) * scale;
            MarkChanged(true);
        }

        private void DrawProblems()
        {
            Header(problems.Count == 0 ? "Проверка: всё в порядке" : "Проверка: " + problems.Count);
            if (problems.Count == 0)
                return;
            problemsScroll = EditorGUILayout.BeginScrollView(problemsScroll, GUILayout.MaxHeight(140f));
            foreach (AuthoredWorldMapValidation.Problem p in problems)
            {
                GUIContent content = new GUIContent((p.IsError ? "✖ " : "⚠ ") + p.Text, "Клик — показать на карте");
                if (GUILayout.Button(content, EditorStyles.wordWrappedMiniLabel))
                    CenterOn(p.Column, p.Row);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (hoverCell >= 0 && hoverCell < Data.CellCount)
            {
                WorldMapHexCell c = Data.Grid.CellAt(hoverCell);
                WorldMapGameplayTerrainType effective = Data.EffectiveTerrain(hoverCell);
                GUILayout.Label($"Клетка {c.Column}:{c.Row}   высота {Data.HeightAt(hoverCell) * 100f:0}   {WorldMapTerrainLabels.Name(effective)}", EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("Колесо — масштаб, средняя кнопка или Пробел+ЛКМ — сдвиг, Ctrl+Z — отмена", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private static void Header(string text)
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        // ---------------- Холст ----------------

        private void DrawCanvas()
        {
            if (Event.current.type == EventType.Layout)
                return;
            EnsureTexture();
            if (fitPending && canvasRect.width > 10f)
                Fit();

            EditorGUI.DrawRect(canvasRect, new Color(0.16f, 0.16f, 0.16f));
            Rect image = ImageRect();
            GUI.BeginClip(canvasRect);
            Rect local = new Rect(image.x - canvasRect.x, image.y - canvasRect.y, image.width, image.height);
            texture.filterMode = zoom * texture.width / Data.Columns > 14f ? FilterMode.Point : FilterMode.Bilinear;
            GUI.DrawTexture(local, texture, ScaleMode.StretchToFill, false);
            DrawMarkers(local);
            DrawBrushCursor();
            GUI.EndClip();
            HandleCanvasInput();
        }

        private Rect ImageRect()
        {
            Vector2 size = new Vector2(texture.width, texture.height) * zoom;
            return new Rect(canvasRect.x + pan.x, canvasRect.y + pan.y, size.x, size.y);
        }

        private void Fit()
        {
            fitPending = false;
            zoom = Mathf.Min(canvasRect.width / texture.width, canvasRect.height / texture.height) * 0.95f;
            Vector2 size = new Vector2(texture.width, texture.height) * zoom;
            pan = new Vector2((canvasRect.width - size.x) * 0.5f, (canvasRect.height - size.y) * 0.5f);
        }

        private void CenterOn(int column, int row)
        {
            if (texture == null)
                return;
            WorldMapHexGrid g = Data.Grid;
            g.CellCenter(new WorldMapHexCell(column, row), out double x, out double y);
            float k = zoom * texture.width / g.CanvasWidth;
            pan = new Vector2(canvasRect.width * 0.5f - (float)x * k, canvasRect.height * 0.5f - (float)y * k);
            Repaint();
        }

        // Точка окна → пиксель полотна карты.
        private bool ScreenToCanvas(Vector2 mouse, out double px, out double py)
        {
            Rect image = ImageRect();
            WorldMapHexGrid g = Data.Grid;
            px = (mouse.x - image.x) / image.width * g.CanvasWidth;
            py = (mouse.y - image.y) / image.height * g.CanvasHeight;
            return mouse.x >= image.x && mouse.x <= image.xMax && mouse.y >= image.y && mouse.y <= image.yMax;
        }

        private Vector2 CellToLocal(Rect local, int column, int row)
        {
            WorldMapHexGrid g = Data.Grid;
            g.CellCenter(new WorldMapHexCell(column, row), out double x, out double y);
            return new Vector2(local.x + (float)(x / g.CanvasWidth) * local.width, local.y + (float)(y / g.CanvasHeight) * local.height);
        }

        private static Texture2D discTexture, ringTexture;

        private static Texture2D Circle(bool ring)
        {
            const int size = 64;
            Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            Color32[] c = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
                    float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.94f) * 25f) : Mathf.Clamp01((1f - d) * 20f);
                    c[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(c);
            t.Apply(false);
            return t;
        }

        private static void Disc(Vector2 center, float radius, Color color) => DrawCircle(discTexture ??= Circle(false), center, radius, color);

        private static void Ring(Vector2 center, float radius, Color color) => DrawCircle(ringTexture ??= Circle(true), center, radius, color);

        private static void DrawCircle(Texture2D t, Vector2 center, float radius, Color color)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), t);
            GUI.color = previous;
        }

        private void DrawMarkers(Rect local)
        {
            float hexPx = local.width / Mathf.Max(1, Data.Columns);
            GUIStyle label = new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            for (int i = 0; i < Data.Villages.Count; i++)
            {
                AuthoredVillage v = Data.Villages[i];
                Vector2 p = CellToLocal(local, v.Column, v.Row);
                float r = Mathf.Max(5f, hexPx * (0.45f + 0.2f * v.Size));
                Disc(p, r, new Color(0.15f, 0.1f, 0.05f, 0.9f));
                Disc(p, r * 0.7f, selectedVillage == i ? new Color(1f, 0.85f, 0.3f) : new Color(0.95f, 0.8f, 0.55f));
                string title = string.IsNullOrWhiteSpace(v.Name) ? "Деревня " + (i + 1) : v.Name;
                GUI.Label(new Rect(p.x - 80f, p.y + r + 1f, 160f, 16f), title, label);
            }
            Vector2 s = CellToLocal(local, Data.StartColumn, Data.StartRow);
            float sr = Mathf.Max(6f, hexPx * 0.6f);
            Disc(s, sr, new Color(0.1f, 0.1f, 0.1f, 0.9f));
            Disc(s, sr * 0.7f, new Color(0.85f, 0.25f, 0.2f));
            GUI.Label(new Rect(s.x - 80f, s.y + sr + 1f, 160f, 16f), "Лагерь", label);
        }

        private void DrawBrushCursor()
        {
            if (tool != Tool.Relief && !(tool == Tool.Terrain && !fillMode))
                return;
            Vector2 mouse = Event.current.mousePosition;
            if (!canvasRect.Contains(mouse) || texture == null)
                return;
            float k = zoom * texture.width / Data.Grid.CanvasWidth;
            float r = brushRadius * (float)Data.Grid.HexWidth * k;
            Vector2 at = mouse - canvasRect.position;
            Ring(at, r, new Color(1f, 1f, 1f, 0.85f));
            if (tool == Tool.Relief && brushSoftness > 0.01f)
                Ring(at, r * (1f - brushSoftness), new Color(1f, 1f, 1f, 0.35f));
        }

        private void HandleCanvasInput()
        {
            Event e = Event.current;
            Vector2 mouse = e.mousePosition;
            bool inside = canvasRect.Contains(mouse);
            int control = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                int cell = -1;
                if (inside && ScreenToCanvas(mouse, out double hx, out double hy))
                    cell = Data.Grid.IndexOf(Data.Grid.CellAtPixel(hx, hy));
                hoverCell = cell;
                if (inside)
                    Repaint();
            }

            if (e.type == EventType.ScrollWheel && inside)
            {
                float factor = e.delta.y > 0f ? 1f / 1.15f : 1.15f;
                float newZoom = Mathf.Clamp(zoom * factor, 0.05f, 40f);
                Vector2 local = mouse - canvasRect.position;
                pan = local - (local - pan) * (newZoom / zoom);
                zoom = newZoom;
                e.Use();
                Repaint();
                return;
            }

            bool panButton = e.button == 2 || (e.button == 0 && spaceHeld);
            if (e.type == EventType.MouseDown && inside && panButton)
            {
                panning = true;
                GUIUtility.hotControl = control;
                e.Use();
                return;
            }
            if (panning)
            {
                if (e.type == EventType.MouseDrag)
                {
                    pan += e.delta;
                    e.Use();
                    Repaint();
                }
                else if (e.rawType == EventType.MouseUp)
                {
                    panning = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && inside && ScreenToCanvas(mouse, out double px, out double py))
            {
                GUIUtility.hotControl = control;
                Undo.IncrementCurrentGroup();
                strokeGroup = Undo.GetCurrentGroup();
                dragging = true;
                OnPointerDown(px, py, e);
                e.Use();
                return;
            }
            if (dragging && e.type == EventType.MouseDrag && e.button == 0)
            {
                ScreenToCanvas(mouse, out double dx, out double dy);
                OnPointerDrag(dx, dy, e);
                e.Use();
                return;
            }
            if (dragging && e.rawType == EventType.MouseUp)
            {
                dragging = false;
                GUIUtility.hotControl = 0;
                Undo.CollapseUndoOperations(strokeGroup);
                e.Use();
            }
        }

        private bool spaceHeld;

        private void HandleShortcuts()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space)
                spaceHeld = true;
            if (e.type == EventType.KeyUp && e.keyCode == KeyCode.Space)
                spaceHeld = false;
            if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0)
                return;
            switch (e.keyCode)
            {
                case KeyCode.Alpha1: tool = Tool.Relief; break;
                case KeyCode.Alpha2: tool = Tool.Terrain; break;
                case KeyCode.Alpha3: tool = Tool.Villages; break;
                case KeyCode.Alpha4: tool = Tool.Start; break;
                case KeyCode.Alpha5: tool = Tool.Size; break;
                case KeyCode.LeftBracket: brushRadius = Mathf.Max(0.5f, brushRadius / 1.2f); break;
                case KeyCode.RightBracket: brushRadius = Mathf.Min(20f, brushRadius * 1.2f); break;
                case KeyCode.F: fitPending = true; break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (tool == Tool.Villages && selectedVillage >= 0)
                        DeleteSelectedVillage();
                    else
                        return;
                    break;
                case KeyCode.S when e.control || e.command: Save(); break;
                default: return;
            }
            e.Use();
            Repaint();
        }

        private void OnPointerDown(double px, double py, Event e)
        {
            WorldMapHexCell cell = Data.Grid.CellAtPixel(px, py);
            int index = Data.Grid.IndexOf(cell);
            switch (tool)
            {
                case Tool.Relief:
                    if (e.alt)
                    {
                        flattenTarget = Data.HeightAt(index);
                        heightBrush = AuthoredWorldMapEditing.HeightBrush.Flatten;
                        dragging = false;
                        return;
                    }
                    if (heightBrush == AuthoredWorldMapEditing.HeightBrush.Flatten && e.shift)
                        flattenTarget = Data.HeightAt(index);
                    StrokeHeight(px, py, e);
                    break;
                case Tool.Terrain:
                    if (e.alt)
                    {
                        paintType = Data.TerrainAt(index);
                        dragging = false;
                        return;
                    }
                    if (fillMode)
                    {
                        Undo.RecordObject(asset, "Редактор карты: заливка");
                        AuthoredWorldMapEditing.Fill(Data, index, paintType);
                        MarkChanged(true);
                        dragging = false;
                    }
                    else
                        StrokeTerrain(px, py);
                    break;
                case Tool.Villages:
                {
                    int hit = VillageAt(cell);
                    if (hit >= 0)
                    {
                        selectedVillage = hit;
                        break;
                    }
                    Undo.RecordObject(asset, "Редактор карты: новая деревня");
                    Data.Villages.Add(new AuthoredVillage { Column = cell.Column, Row = cell.Row, Size = 1 });
                    selectedVillage = Data.Villages.Count - 1;
                    MarkChanged(true);
                    break;
                }
                case Tool.Start:
                    MoveStart(cell);
                    break;
                default:
                    dragging = false;
                    break;
            }
        }

        private void OnPointerDrag(double px, double py, Event e)
        {
            WorldMapHexCell cell = Data.Grid.CellAtPixel(px, py);
            switch (tool)
            {
                case Tool.Relief:
                    StrokeHeight(px, py, e);
                    break;
                case Tool.Terrain:
                    if (!fillMode)
                        StrokeTerrain(px, py);
                    break;
                case Tool.Villages:
                    if (selectedVillage >= 0 && selectedVillage < Data.Villages.Count)
                    {
                        AuthoredVillage v = Data.Villages[selectedVillage];
                        if (v.Column != cell.Column || v.Row != cell.Row)
                        {
                            Undo.RecordObject(asset, "Редактор карты: деревня");
                            v.Column = cell.Column;
                            v.Row = cell.Row;
                            MarkChanged(true);
                        }
                    }
                    break;
                case Tool.Start:
                    MoveStart(cell);
                    break;
            }
        }

        private void StrokeHeight(double px, double py, Event e)
        {
            AuthoredWorldMapEditing.HeightBrush brush = heightBrush;
            if (brush == AuthoredWorldMapEditing.HeightBrush.Raise && (e.control || e.command))
                brush = AuthoredWorldMapEditing.HeightBrush.Lower;
            Undo.RecordObject(asset, "Редактор карты: рельеф");
            AuthoredWorldMapEditing.ApplyHeight(Data, brush, px, py, brushRadius, brushStrength, brushSoftness,
                flattenTarget, terraces, terraceStep, Random.Range(0, int.MaxValue));
            MarkChanged(true);
        }

        private void StrokeTerrain(double px, double py)
        {
            Undo.RecordObject(asset, "Редактор карты: местность");
            AuthoredWorldMapEditing.PaintTerrain(Data, paintType, px, py, brushRadius, keepWaterAndRoads);
            MarkChanged(true);
        }

        private void MoveStart(WorldMapHexCell cell)
        {
            if (Data.StartColumn == cell.Column && Data.StartRow == cell.Row)
                return;
            Undo.RecordObject(asset, "Редактор карты: лагерь");
            Data.StartColumn = cell.Column;
            Data.StartRow = cell.Row;
            MarkChanged(true);
        }

        private int VillageAt(WorldMapHexCell cell)
        {
            for (int i = Data.Villages.Count - 1; i >= 0; i--)
                if (cell.DistanceTo(new WorldMapHexCell(Data.Villages[i].Column, Data.Villages[i].Row)) <= 1)
                    return i;
            return -1;
        }

        private void DeleteSelectedVillage()
        {
            if (selectedVillage < 0 || selectedVillage >= Data.Villages.Count)
                return;
            Undo.RecordObject(asset, "Редактор карты: удалить деревню");
            Data.Villages.RemoveAt(selectedVillage);
            selectedVillage = -1;
            MarkChanged(true);
        }

        // ---------------- Данные, сохранение, игра ----------------

        private void MarkChanged(bool commit)
        {
            if (commit)
            {
                Data.Commit();
                EditorUtility.SetDirty(asset);
            }
            textureDirty = true;
            problems = AuthoredWorldMapValidation.Check(Data);
            Repaint();
        }

        private void Save()
        {
            Data.Commit();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            ShowNotification(new GUIContent("Карта сохранена"), 1.0);
        }

        private void PlayOnThisMap()
        {
            Save();
            if (problems.Exists(p => p.IsError) &&
                !EditorUtility.DisplayDialog("На карте есть ошибки", "Лагерь или деревни недоступны. Всё равно запустить?", "Запустить", "Отмена"))
                return;
            if (EditorSceneManager.GetActiveScene().path != PrototypeScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                EditorSceneManager.OpenScene(PrototypeScenePath);
            }
            SessionState.SetBool(WorldMapVisualRuntime.EditorQuickStartKey, true);
            EditorApplication.EnterPlaymode();
        }

        // ---------------- Рисунок схемы ----------------

        private void EnsureTexture()
        {
            WorldMapHexGrid g = Data.Grid;
            int pxPerHex = Mathf.Clamp(Mathf.RoundToInt(2400f / Mathf.Max(1, Data.Columns)), 3, 14);
            int width = Mathf.Max(8, Data.Columns * pxPerHex);
            int height = Mathf.Max(8, Mathf.RoundToInt(width * g.CanvasHeight / g.CanvasWidth));
            if (texture == null || texture.width != width || texture.height != height || pixelCell == null || pixelCell.Length != width * height)
            {
                if (texture != null)
                    DestroyImmediate(texture);
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                pixels = new Color32[width * height];
                pixelCell = new int[width * height];
                for (int y = 0; y < height; y++)
                {
                    double cy = (y + 0.5) / height * g.CanvasHeight;
                    for (int x = 0; x < width; x++)
                    {
                        double cx = (x + 0.5) / width * g.CanvasWidth;
                        // Texture2D — снизу вверх: строка y рисунка = height-1-y.
                        pixelCell[(height - 1 - y) * width + x] = g.IndexOf(g.CellAtPixel(cx, cy));
                    }
                }
                textureDirty = true;
                problems = AuthoredWorldMapValidation.Check(Data);
            }
            if (!textureDirty)
                return;
            textureDirty = false;

            int count = g.CellCount;
            Color32[] cellColors = new Color32[count];
            float[] h = Data.Heights;
            for (int i = 0; i < count; i++)
            {
                WorldMapHexCell c = g.CellAt(i);
                float left = HeightOr(g, h, c.Neighbor(3), h[i]);
                float right = HeightOr(g, h, c.Neighbor(0), h[i]);
                float up = (HeightOr(g, h, c.Neighbor(1), h[i]) + HeightOr(g, h, c.Neighbor(2), h[i])) * 0.5f;
                float down = (HeightOr(g, h, c.Neighbor(4), h[i]) + HeightOr(g, h, c.Neighbor(5), h[i])) * 0.5f;
                // Свет слева сверху: склон, поднимающийся вправо и вниз, освещён.
                float shade = Mathf.Clamp(1f + ((right - left) + (down - up)) * 3.5f, 0.55f, 1.35f);
                Color baseColor = TerrainColor(Data.EffectiveTerrain(i));
                Color color = Color.Lerp(baseColor, new Color(0.95f, 0.93f, 0.86f), h[i] * 0.25f) * shade;
                color.a = 1f;
                cellColors[i] = color;
            }
            int w = texture.width;
            for (int p = 0; p < pixels.Length; p++)
            {
                int cell = pixelCell[p];
                Color32 color = cell >= 0 && cell < count ? cellColors[cell] : new Color32(0, 0, 0, 255);
                if (showGrid && ((p % w > 0 && pixelCell[p - 1] != cell) || (p >= w && pixelCell[p - w] != cell)))
                    color = new Color32((byte)(color.r * 0.7f), (byte)(color.g * 0.7f), (byte)(color.b * 0.7f), 255);
                pixels[p] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private static float HeightOr(WorldMapHexGrid g, float[] h, WorldMapHexCell cell, float fallback) =>
            g.IsInside(cell) ? h[g.IndexOf(cell)] : fallback;

        private void OnInspectorUpdate()
        {
            // Сетка — переключатель: перерисовать схему.
            if (showGrid != lastShowGrid)
            {
                lastShowGrid = showGrid;
                textureDirty = true;
                Repaint();
            }
        }

        private bool lastShowGrid;

        public static Color TerrainColor(WorldMapGameplayTerrainType type)
        {
            switch (type)
            {
                case WorldMapGameplayTerrainType.Road: return new Color(0.62f, 0.5f, 0.36f);
                case WorldMapGameplayTerrainType.Trail: return new Color(0.7f, 0.62f, 0.46f);
                case WorldMapGameplayTerrainType.Field: return new Color(0.86f, 0.78f, 0.46f);
                case WorldMapGameplayTerrainType.Forest: return new Color(0.3f, 0.47f, 0.28f);
                case WorldMapGameplayTerrainType.Water: return new Color(0.33f, 0.53f, 0.74f);
                case WorldMapGameplayTerrainType.Swamp: return new Color(0.42f, 0.5f, 0.38f);
                case WorldMapGameplayTerrainType.Hills: return new Color(0.66f, 0.6f, 0.43f);
                case WorldMapGameplayTerrainType.Mountains: return new Color(0.58f, 0.55f, 0.53f);
                case WorldMapGameplayTerrainType.Cliffs: return new Color(0.43f, 0.4f, 0.38f);
                default: return new Color(0.66f, 0.72f, 0.48f);
            }
        }
    }
}
